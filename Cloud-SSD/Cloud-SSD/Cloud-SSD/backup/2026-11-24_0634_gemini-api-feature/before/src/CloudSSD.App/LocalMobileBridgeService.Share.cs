using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using CloudSSD.Core;
using Microsoft.Extensions.Logging;

namespace CloudSSD.App;

/// <summary>
/// Sending files between devices. This PC offers files to a device (it accepts or declines, then downloads them with
/// resume); a device sends files to another device through this PC (relay: uploaded here, then offered); and a file can
/// be handed out as a link that works for a while on the home network or the tailnet only.
/// </summary>
public sealed partial class LocalMobileBridgeService
{
    private ShareStore? _shares;

    public ShareStore Shares => LazyInitializer.EnsureInitialized(ref _shares, () => new ShareStore(_host.Paths.SharesDirectory, () => Now()))!;

    private TransferTasks? _tasks;
    /// <summary>All transfers of this PC as tasks (offers, uploads, passed-on files, phone folders); screens bind to them.</summary>
    public TransferTasks Tasks => LazyInitializer.EnsureInitialized(ref _tasks, () =>
    {
        var t = new TransferTasks(() => Now());
        // Files a device sends to this PC ("up-"): the OK or No given here decides what its next requests get.
        t.HandleAsking("up-", accept: (task, _) => { lock (_uploadAnswers) _uploadAnswers[task.Id] = true; return true; },
            decline: task => { lock (_uploadAnswers) _uploadAnswers[task.Id] = false; return true; });
        t.Handle("up-", cancel: task => { lock (_uploadAnswers) _uploadAnswers[task.Id] = false; return true; });
        return t;
    })!;

    /// <summary>A device sent a file to this PC ("Send to PC"); the PC shows a note.</summary>
    public event Action<PairedDevice, string>? FileReceived;

    private void RaiseFileReceived(string path)
    {
        if (CurrentCaller.Value?.Device is not { } d) return;
        try { FileReceived?.Invoke(d, path); } catch (Exception e) { _log.LogDebug(e, "FileReceived handler failed"); }
    }

    /// <summary>
    /// Phones and tablets with the app take files, and so do other PCs and laptops through their "Other PCs" page; a
    /// browser cannot (nothing there looks at its offers).
    /// </summary>
    public static bool CanReceive(PairedDevice d) => DeviceKinds.Of(d.Kind) is DeviceKind.Phone or DeviceKind.Tablet or DeviceKind.Laptop or DeviceKind.Desktop;

    /// <summary>Offers files of this PC to a device (it sees them the next time it looks: within seconds when open).</summary>
    public ShareOffer? SendToDevice(PairedDevice to, IEnumerable<string> paths, bool everything = false) =>
        Shares.OfferFromPc(Environment.MachineName, to, paths, everything);

    /// <summary>A link to one file for anyone on the home network or tailnet, for a while (and at most so many downloads).</summary>
    public string? CreateShareLink(string path, TimeSpan lifetime, int? maxDownloads) =>
        Shares.CreateLink(path, lifetime, maxDownloads) is { } l ? $"{LocalUrl}s/{l.Id}" : null;

    private object OfferDto(ShareOffer o) => new
    {
        id = o.Id, from = o.FromName, fromPc = o.FromId == "pc", to = o.ToName, relay = o.Relay, state = o.State,
        expiresIn = Math.Max(0, (int)(o.ExpiresAt - Now()).TotalSeconds), total = o.TotalBytes,
        // Folders sent: their names, and what was left out of them (build output, .git…).
        folders = o.TopFolders, skipped = o.SkippedFiles > 0 ? new { files = o.SkippedFiles, bytes = o.SkippedBytes, folders = o.SkippedFolders } : null,
        items = o.Items.Select(i => new { id = i.Id, name = i.Name, folder = i.Folder, size = i.Size, ready = i.Ready, delivered = i.Delivered })
    };

    private PairedDevice? OtherDevice(string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        lock (_devicesLock) return _host.Settings.MobileBridge.PairedDevices.FirstOrDefault(d => d.Id == id && !d.IsBlocked);
    }

    /// <summary>/api/share/*. True when the route was one of these (the answer has been sent).</summary>
    private async Task<bool> TryHandleShareRouteAsync(string method, string path, Dictionary<string, string> q, Dictionary<string, string> headers,
        NetworkStream stream, string clientIp, Caller caller, CancellationToken ct)
    {
        if (!path.StartsWith("/api/share/", StringComparison.OrdinalIgnoreCase)) return false;
        if (caller.Device is not { } me)
        {
            await SendJsonAsync(stream, 403, "Forbidden", new { error = "Pair this phone again to send and receive files." }, ct);
            return true;
        }
        bool post = method == "POST";
        var id = q.GetValueOrDefault("id") ?? "";
        switch (path.ToLowerInvariant())
        {
            case "/api/share/inbox":
                await SendJsonAsync(stream, 200, "OK", new { offers = Shares.InboxOf(me.Id).Select(OfferDto) }, ct);
                return true;
            case "/api/share/sent":
                await SendJsonAsync(stream, 200, "OK", new { offers = Shares.SentBy(me.Id).Select(OfferDto) }, ct);
                return true;
            case "/api/share/accept" when post:
                await SendJsonAsync(stream, 200, "OK", new { ok = Shares.Accept(me.Id, id) }, ct);
                return true;
            case "/api/share/decline" when post:
                await SendJsonAsync(stream, 200, "OK", new { ok = Shares.Decline(me.Id, id) }, ct);
                return true;
            case "/api/share/cancel" when post:
                await SendJsonAsync(stream, 200, "OK", new { ok = Shares.Cancel(me.Id, id) }, ct);
                return true;
            case "/api/share/file":
                await HandleShareFileAsync(me, id, q.GetValueOrDefault("item") ?? "", headers, stream, clientIp, ct);
                return true;
            case "/api/share/devices":
            {
                var bridge = _host.Settings.MobileBridge;
                List<PairedDevice> others;
                lock (_devicesLock) others = bridge.PairedDevices.Where(d => d.Id != me.Id && !d.IsBlocked).ToList();
                await SendJsonAsync(stream, 200, "OK", new
                {
                    allowed = bridge.AllowDeviceToDevice,
                    devices = bridge.AllowDeviceToDevice
                        ? others.Select(d => (object)new { id = d.Id, name = d.Name, kind = DeviceKinds.Name(DeviceKinds.Of(d.Kind)), online = Presence.IsOnline(d.Id, d.LastSeen), canReceive = CanReceive(d) })
                        : Array.Empty<object>()
                }, ct);
                return true;
            }
            case "/api/share/offer" when post:
            {
                if (!_host.Settings.MobileBridge.AllowDeviceToDevice)
                {
                    await SendJsonAsync(stream, 403, "Forbidden", new { error = "Sending to other devices is switched off on the PC.", code = "switched-off" }, ct);
                    return true;
                }
                var to = OtherDevice(q.GetValueOrDefault("to"));
                if (to is null || to.Id == me.Id || !CanReceive(to))
                {
                    await SendJsonAsync(stream, 400, "Bad Request", new { error = "That device cannot take files." }, ct);
                    return true;
                }
                List<(string, long, string)> files;
                try
                {
                    // {name, size, folder?}: folder is the file's place in a folder being sent ("MyApp/src").
                    var body = q.GetValueOrDefault("items") ?? await ReadSmallBodyAsync(stream, headers, 2 * 1024 * 1024, ct) ?? "[]";
                    using var doc = JsonDocument.Parse(body);
                    files = doc.RootElement.EnumerateArray().Select(e => (e.GetProperty("name").GetString() ?? "", e.GetProperty("size").GetInt64(),
                        e.TryGetProperty("folder", out var f) ? f.GetString() ?? "" : "")).ToList();
                }
                catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
                {
                    await SendJsonAsync(stream, 400, "Bad Request", new { error = "items must be a list of {name, size}." }, ct);
                    return true;
                }
                var (offer, error) = Shares.OfferFromDevice(me, to, files);
                if (offer is null) await SendJsonAsync(stream, 400, "Bad Request", new { error }, ct);
                else await SendJsonAsync(stream, 200, "OK", new { offer = OfferDto(offer) }, ct);
                return true;
            }
            case "/api/share/upload" when post:
                await HandleShareUploadAsync(me, id, q, headers, stream, clientIp, ct);
                return true;
            default:
                await SendJsonAsync(stream, 404, "Not Found", new { error = "Not found." }, ct);
                return true;
        }
    }

    /// <summary>The receiver downloads one file (with Range, so it resumes). A file changed since it was offered is refused.</summary>
    private async Task HandleShareFileAsync(PairedDevice me, string offerId, string itemId, Dictionary<string, string> headers, NetworkStream stream,
        string clientIp, CancellationToken ct)
    {
        var o = Shares.Get(offerId);
        var item = o?.Items.FirstOrDefault(i => i.Id == itemId);
        if (o is null || item is null || o.ToId != me.Id || o.State is not ("offered" or "accepted"))
        {
            await SendJsonAsync(stream, 404, "Not Found", new { error = "Not offered to this device (or no longer)." }, ct);
            return;
        }
        var fi = item.Path is null ? null : new FileInfo(item.Path);
        if (fi is null || !fi.Exists || !item.Ready)
        {
            await SendJsonAsync(stream, 410, "Gone", new { error = item.Name + " is no longer on the PC." }, ct);
            return;
        }
        if (fi.Length != item.Size || Math.Abs(new DateTimeOffset(fi.LastWriteTimeUtc).ToUnixTimeMilliseconds() - item.Modified) > 2000)
        {
            await SendJsonAsync(stream, 409, "Conflict", new { error = item.Name + " changed on the PC after it was sent. Ask for it again." }, ct);
            return;
        }
        if (o.State == "offered") Shares.Accept(me.Id, o.Id);    // downloading it is accepting it
        // Whole file, or the rest of it from an offset: when that much went out, the device has all of it.
        var range = headers.GetValueOrDefault("Range");
        bool toTheEnd = range is null || System.Text.RegularExpressions.Regex.IsMatch(range, @"^bytes=\d+-$");
        var disposition = "attachment; filename*=UTF-8''" + Uri.EscapeDataString(item.Name);
        await SendFileAsync(fi, headers, stream, clientIp, null, disposition, "download", ct);
        if (toTheEnd && !ct.IsCancellationRequested) Shares.ItemDelivered(o, item);
    }

    /// <summary>The sender of a relayed offer uploads one of its files (resumable, like "Send to PC").</summary>
    private async Task HandleShareUploadAsync(PairedDevice me, string offerId, Dictionary<string, string> q, Dictionary<string, string> headers,
        NetworkStream stream, string clientIp, CancellationToken ct)
    {
        try
        {
            var o = Shares.Get(offerId);
            var item = o?.Items.FirstOrDefault(i => i.Id == q.GetValueOrDefault("item"));
            if (o is null || item is null || o.FromId != me.Id || o.State != "uploading") throw new FsException(404, "This offer does not take files any more.");
            var uploadId = q.GetValueOrDefault("uploadId");
            if (!IsUploadId(uploadId)) throw new FsException(400, "uploadId must be 32 lowercase hex characters.");
            if (QueryLong(q, "total") is not { } total || total != item.Size) throw new FsException(400, "total must be the file's size as offered.");
            var relay = Directory.CreateDirectory(Shares.RelayFolder(o)).FullName;
            // Each file in its own place: two "index.js" in different folders of a project must not meet.
            var folder = Directory.CreateDirectory(item.Folder.Length > 0 ? Path.Combine(relay, item.Folder.Replace('/', Path.DirectorySeparatorChar)) : relay).FullName;
            var r = await ReceiveResumableAsync(uploadId!, "share", folder, item.Name, true, null, QueryLong(q, "offset") ?? 0, total, relay, headers, stream, clientIp, ct);
            if (!r.Complete)
            {
                await SendJsonAsync(stream, 200, "OK", new { success = true, complete = false, offset = r.Offset }, ct);
                return;
            }
            Shares.ItemUploaded(o, item, r.FinalPath!);
            await SendJsonAsync(stream, 200, "OK", new { success = true, complete = true, filename = item.Name, size = r.Size }, ct);
        }
        catch (Exception e) when (e is FsException or IOException or UnauthorizedAccessException)
        {
            await DrainBrieflyAsync(stream, ct);
            await SendFsErrorAsync(stream, e, ct);
        }
    }

    /// <summary>/s/&lt;id&gt;: a shared link (no key). Home network and tailnet only; counted; refused once used up or expired.</summary>
    private async Task HandleShareLinkAsync(string path, NetRoute route, Dictionary<string, string> headers, NetworkStream stream, string clientIp, CancellationToken ct)
    {
        var id = path.Length > 3 ? path[3..].Trim('/') : "";
        var link = route == NetRoute.Other ? null : Shares.UsableLink(id);
        var fi = link is null ? null : new FileInfo(link.Path);
        if (link is null || fi is null || !fi.Exists)
        {
            await SendResponseAsync(stream, 404, "Not Found", "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("This link has expired or was taken back."), ct);
            return;
        }
        // A resumed download (Range from an offset) is the same download, not a new one.
        var range = headers.GetValueOrDefault("Range");
        if (range is null || range.StartsWith("bytes=0-", StringComparison.Ordinal)) Shares.CountDownload(link);
        await SendFileAsync(fi, headers, stream, clientIp, null, "attachment; filename*=UTF-8''" + Uri.EscapeDataString(link.Name), "download", ct);
    }
}
