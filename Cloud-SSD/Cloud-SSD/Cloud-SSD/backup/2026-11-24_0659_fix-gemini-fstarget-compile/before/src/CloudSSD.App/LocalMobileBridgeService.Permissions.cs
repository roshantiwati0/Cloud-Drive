using System.Net.Sockets;
using CloudSSD.Core;

namespace CloudSSD.App;

/// <summary>
/// Per-device permissions on the bridge: every route that needs a PC-wide switch also needs the device to have that
/// category (a switch that is off still wins). A device without it gets 403 "needs-permission" and may ask the PC
/// (/api/access/request); someone at the PC answers. What devices open is written to the access log.
/// </summary>
public sealed partial class LocalMobileBridgeService
{
    private AccessControl? _access;
    private AccessLog? _accessLog;

    public AccessControl Access => LazyInitializer.EnsureInitialized(ref _access,
        () => new AccessControl(() => Now(), _devicesLock, FindDevice, () => _host.SaveSettings(), id => Presence.Kick(id)))!;

    public AccessLog AccessLog => LazyInitializer.EnsureInitialized(ref _accessLog, () => new AccessLog(_host.Paths.AccessLogDirectory, () => Now()))!;

    private DeleteLog? _deleteLog;

    /// <summary>What devices deleted on this PC (the card's "Deleted by this device", with Restore).</summary>
    public DeleteLog DeleteLog => LazyInitializer.EnsureInitialized(ref _deleteLog, () => new DeleteLog(_host.Paths.DeleteLogFile, () => Now()))!;

    /// <summary>Notes a delete by the device of this request. bin: "recycle" (this PC's bin), "cloud" (the account's trash), "gone".</summary>
    private void LogDelete(string name, string where, bool folder, string bin, string? originalPath)
    {
        if (CurrentCaller.Value?.Device is not { } d) return;
        try
        {
            DeleteLog.Add(d.Id, d.Name, name, where, folder, bin, originalPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The delete itself happened; only its note is missing.
        }
    }

    private PairedDevice? FindDevice(string id)
    {
        lock (_devicesLock) return _host.Settings.MobileBridge.PairedDevices.FirstOrDefault(d => d.Id == id);
    }

    // A property, not a field: the switches are fields of another part of this class, set up in an order C# does not promise.
    private static (string Category, (string Name, Func<MobileBridgeSettings, bool> Allowed) Switch)[] CategorySwitches =>
    [
        (AccessCategories.Browse, Browsing), (AccessCategories.Stream, Streaming), (AccessCategories.Upload, Uploads),
        (AccessCategories.Manage, FileManagement), (AccessCategories.Hud, Hud), (AccessCategories.Control, PcControl),
        (AccessCategories.Space, Space), (AccessCategories.Game, GameStream), (AccessCategories.Relay, DeviceToDevice)
    ];

    /// <summary>The categories a route needs: one per PC-wide switch it needs.</summary>
    internal static IReadOnlyList<string> RequiredCategories(string path, string method) =>
        RequiredSwitches(path, method).Select(s => CategorySwitches.First(c => c.Switch.Name == s.Name).Category).Distinct().ToList();

    /// <summary>Asked many times a minute (thumbnails, live gauges): allowed ones are not written to the log.</summary>
    private static bool IsChatty(string path) => path.ToLowerInvariant() is "/api/fs/thumb" or "/api/hud" or "/api/upload/status"
        or "/api/drives" or "/api/saves" or "/api/transfers" || path.StartsWith("/api/space/", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the device lacks a category this route needs (the 403 has been sent).</summary>
    private async Task<bool> RefuseWithoutGrantAsync(Caller caller, string path, string method, Dictionary<string, string> q,
        Dictionary<string, string> headers, NetworkStream stream, CancellationToken ct)
    {
        if (caller.Device is not { } d || caller.Legacy) return false;
        var needed = RequiredCategories(path, method);
        if (needed.Count == 0) return false;
        var target = q.GetValueOrDefault("path") ?? q.GetValueOrDefault("id");
        var missing = needed.Where(c => !Access.Has(d, c)).ToList();
        if (missing.Count == 0)
        {
            if (!IsChatty(path)) AccessLog.Write(d.Id, needed[0], path, target, true);
            return false;
        }
        AccessLog.Write(d.Id, missing[0], path, target, false);
        bool hasBody = (headers.TryGetValue("Content-Length", out var cl) && cl != "0") || headers.ContainsKey("Transfer-Encoding");
        if (hasBody) await DrainBodyAsync(stream, headers, ct);
        await SendJsonAsync(stream, 403, "Forbidden", new
        {
            error = $"This device may not do that yet ({AccessCategories.Describe(missing[0]).ToLowerInvariant()}). Ask the PC for access.",
            code = "needs-permission",
            categories = missing
        }, ct);
        return true;
    }

    private object AccessDto(AccessRequest r) => new
    {
        id = r.Id, state = r.State, mode = r.Mode, categories = r.Categories,
        expiresIn = Math.Max(0, (int)(r.ExpiresAt - Now()).TotalSeconds)
    };

    /// <summary>/api/access/*: what this device may do, asking for more, taking a question back, giving something up.</summary>
    private async Task<bool> TryHandleAccessRouteAsync(string method, string path, Dictionary<string, string> q, Caller caller, NetworkStream stream, CancellationToken ct)
    {
        if (!path.StartsWith("/api/access/", StringComparison.OrdinalIgnoreCase)) return false;
        if (caller.Device is not { } d)
        {
            await SendJsonAsync(stream, 403, "Forbidden", new { error = "Pair this device again (with the code shown on the PC) to ask for access." }, ct);
            return true;
        }
        bool post = method == "POST";
        switch (path.ToLowerInvariant())
        {
            case "/api/access/mine":
            {
                var bridge = _host.Settings.MobileBridge;
                var grants = Access.Current(d);
                var latest = Access.Latest(d.Id);
                await SendJsonAsync(stream, 200, "OK", new
                {
                    categories = CategorySwitches.Select(c =>
                    {
                        var g = grants.FirstOrDefault(x => x.Category == c.Category);
                        return new
                        {
                            category = c.Category, name = AccessCategories.Describe(c.Category), switchOn = c.Switch.Allowed(bridge),
                            allowed = g != null, mode = g?.Mode, expiresAt = g?.ExpiresAt
                        };
                    }),
                    request = latest is null ? null : AccessDto(latest)
                }, ct);
                return true;
            }
            case "/api/access/request" when post:
            {
                var cats = (q.GetValueOrDefault("categories") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var (req, retryAfter, error) = Access.Ask(d, cats, q.GetValueOrDefault("reason"));
                if (req != null) await SendJsonAsync(stream, 200, "OK", new { request = AccessDto(req) }, ct);
                else if (retryAfter > 0) await SendJsonAsync(stream, 429, "Too Many Requests", new { error, retryAfter }, ct);
                else await SendJsonAsync(stream, 400, "Bad Request", new { error }, ct);
                return true;
            }
            case "/api/access/cancel" when post:
                await SendJsonAsync(stream, 200, "OK", new { ok = Access.Cancel(d.Id, q.GetValueOrDefault("id") ?? "") }, ct);
                return true;
            case "/api/access/drop" when post:
            {
                var c = q.GetValueOrDefault("category");
                if (!AccessCategories.IsKnown(c)) { await SendJsonAsync(stream, 400, "Bad Request", new { error = "Unknown category." }, ct); return true; }
                Access.Drop(d, c!);
                await SendJsonAsync(stream, 200, "OK", new { ok = true }, ct);
                return true;
            }
            default:
                await SendJsonAsync(stream, 404, "Not Found", new { error = "Not found." }, ct);
                return true;
        }
    }
}
