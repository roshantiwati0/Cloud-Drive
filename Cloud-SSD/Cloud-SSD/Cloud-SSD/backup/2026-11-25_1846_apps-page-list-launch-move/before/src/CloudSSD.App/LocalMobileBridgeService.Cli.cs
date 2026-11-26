using System.Net;
using System.Net.Sockets;
using CloudSSD.Diagnostics;
using Microsoft.Extensions.Logging;

namespace CloudSSD.App;

/// <summary>
/// The terminal (cloudssd / cpx) on this same PC asks the running app to connect or disconnect a drive, or to offer files
/// to a phone, through the bridge, like the phone does. It proves itself with a key of its own:
/// <list type="bullet">
/// <item>a fresh random key every time the bridge starts, kept in <c>cli.key</c> in the data folder, protected with
/// DPAPI so only this Windows user can read it (the same protection as the rclone password);</item>
/// <item>accepted only from 127.0.0.1 / ::1: from the network it is never enough, whatever the token;</item>
/// <item>the PC-wide switches (Phone → Permissions, e.g. "PC control") still apply, as for every caller.</item>
/// </list>
/// Written only after the bridge really listens, so a second process that fails to get the port never replaces the
/// key of the app that has it.
/// </summary>
public sealed partial class LocalMobileBridgeService
{
    public const string CliKeyFileName = "cli.key";

    private volatile string? _cliKeyHash;

    /// <summary>True for this request when it came from the terminal on this PC with the right key.</summary>
    private static readonly AsyncLocal<bool> CliCall = new();

    private string CliKeyFile => Path.Combine(_host.Paths.DataRoot, CliKeyFileName);

    /// <summary>Makes a new terminal key (after the listener started) and writes it for the terminal to read.</summary>
    private void IssueCliKey()
    {
        try
        {
            var key = Core.MobileBridgeSettings.NewPairingToken();
            var tmp = CliKeyFile + ".tmp";
            File.WriteAllText(tmp, SecretStore.Protect(key));
            File.Move(tmp, CliKeyFile, true);
            _cliKeyHash = HashToken(key);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _cliKeyHash = null;
            _log.LogWarning("Could not write the terminal key; cloudssd cannot connect or disconnect drives: {Error}", e.Message);
        }
    }

    /// <summary>The token is this run's terminal key and the request comes from this PC itself.</summary>
    internal bool IsCliCall(string? token, string clientIp)
    {
        if (string.IsNullOrEmpty(token) || _cliKeyHash is not { } expected) return false;
        if (!IPAddress.TryParse(clientIp, out var ip)) return false;
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (!IPAddress.IsLoopback(ip)) return false;
        return SameText(expected, HashToken(token));
    }

    /// <summary>What the terminal key opens: seeing the drives, connecting / disconnecting one, and its own routes. Nothing else.</summary>
    internal static bool CliRouteAllowed(string path) =>
        path.Equals("/api/status", StringComparison.OrdinalIgnoreCase)
        || path.Equals("/api/drives", StringComparison.OrdinalIgnoreCase)
        || path.Equals("/api/drives/mount", StringComparison.OrdinalIgnoreCase)
        || path.Equals("/api/drives/unmount", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/api/cli/", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/api/gemini/", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Routes only the terminal may use. /api/cli/send?device=&lt;id or name&gt;&amp;path=&lt;path&gt;[|&lt;path&gt;…] offers files on this
    /// PC to a paired device, exactly like "Send to devices" in the desktop app (the device accepts it on its side).
    /// </summary>
    private async Task<bool> TryHandleCliRouteAsync(string method, string path, Dictionary<string, string> q, NetworkStream stream, CancellationToken ct)
    {
        if (!path.StartsWith("/api/cli/", StringComparison.OrdinalIgnoreCase)) return false;
        if (!CliCall.Value)
        {
            await SendJsonAsync(stream, 403, "Forbidden", new { error = "Only the terminal on this PC may use this." }, ct);
            return true;
        }
        if (path.Equals("/api/cli/send", StringComparison.OrdinalIgnoreCase))
        {
            if (method != "POST")
            {
                await SendJsonAsync(stream, 405, "Method Not Allowed", new { error = "Use POST." }, ct);
                return true;
            }
            var who = q.GetValueOrDefault("device") ?? "";
            var paths = (q.GetValueOrDefault("path") ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            Core.PairedDevice? device;
            lock (_devicesLock)
            {
                var list = _host.Settings.MobileBridge.PairedDevices.Where(d => !d.IsBlocked).ToList();
                device = who.Length == 0 && list.Count == 1 ? list[0]
                    : list.FirstOrDefault(d => d.Id.Equals(who, StringComparison.OrdinalIgnoreCase))
                      ?? list.FirstOrDefault(d => d.Name.Equals(who, StringComparison.OrdinalIgnoreCase))
                      ?? list.FirstOrDefault(d => who.Length > 0 && d.Name.Contains(who, StringComparison.OrdinalIgnoreCase));
            }
            if (device is null)
            {
                await SendJsonAsync(stream, 404, "Not Found", new { success = false, error = who.Length == 0 ? "Say which device: more than one is paired." : $"No paired device called '{who}'." }, ct);
                return true;
            }
            if (paths.Length == 0)
            {
                await SendJsonAsync(stream, 400, "Bad Request", new { success = false, error = "Give the file or folder to send." }, ct);
                return true;
            }
            var offer = SendToDevice(device, paths);
            if (offer is null)
            {
                await SendJsonAsync(stream, 404, "Not Found", new { success = false, error = "Nothing to send: the file or folder is not there." }, ct);
                return true;
            }
            await SendJsonAsync(stream, 200, "OK", new
            {
                success = true,
                device = device.Name,
                files = offer.Items.Count,
                bytes = offer.Items.Sum(i => i.Size),
                skipped = offer.SkippedFiles,
            }, ct);
            return true;
        }
        await SendJsonAsync(stream, 404, "Not Found", new { error = "Unknown terminal route." }, ct);
        return true;
    }
}
