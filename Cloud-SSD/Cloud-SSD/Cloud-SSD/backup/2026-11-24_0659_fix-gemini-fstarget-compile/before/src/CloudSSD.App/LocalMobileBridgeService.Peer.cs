using System.Net.Sockets;
using System.Text.Json;
using CloudSSD.Core;

namespace CloudSSD.App;

/// <summary>
/// The other direction: this PC looking at a phone's files, only as far as the phone allows it (the phone decides and
/// enforces that itself). The phone app waits on /api/peer/inbox for questions and answers on /api/peer/result.
/// </summary>
public sealed partial class LocalMobileBridgeService
{
    /// <summary>Largest JSON answer, and largest file a device may send in one answer.</summary>
    internal const int MaxPeerJson = 8 * 1024 * 1024;
    internal static long MaxPeerFile = 4L << 30;

    private PeerBroker? _peer;

    public PeerBroker Peer => LazyInitializer.EnsureInitialized(ref _peer, () => new PeerBroker(() => Now()))!;

    /// <summary>A name for a file from a device in <paramref name="folder"/> that is safe and not taken yet ("a (1).jpg").</summary>
    public static string FreeLocalName(string folder, string name) => FreeName(folder, SafeUploadName(name));

    private string PeerTempDir => Path.Combine(_host.Paths.DataRoot, "peer", "tmp");

    /// <summary>
    /// Asks a device something (fs.roots, fs.list {path}, fs.read {path}) and waits for its answer. A read's file is in
    /// <see cref="PeerResult.FilePath"/> (a temporary file the caller moves or deletes).
    /// </summary>
    public Task<PeerResult> AskDeviceAsync(string deviceId, string kind, IReadOnlyDictionary<string, string> args, TimeSpan timeout, CancellationToken ct = default)
    {
        CleanPeerTemp();
        return Peer.AskAsync(deviceId, kind, args, timeout, ct);
    }

    private void CleanPeerTemp()
    {
        try
        {
            if (!Directory.Exists(PeerTempDir)) return;
            foreach (var f in new DirectoryInfo(PeerTempDir).EnumerateFiles())
                if (DateTime.UtcNow - f.LastWriteTimeUtc > TimeSpan.FromHours(1)) try { f.Delete(); } catch (IOException) { }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>/api/peer/*: the device's long request for questions, and its answers.</summary>
    private async Task<bool> TryHandlePeerRouteAsync(string method, string path, Dictionary<string, string> q, Dictionary<string, string> headers,
        NetworkStream stream, Caller caller, CancellationToken ct)
    {
        if (!path.StartsWith("/api/peer/", StringComparison.OrdinalIgnoreCase)) return false;
        // A laptop looking at a phone through this PC: after the switch and permission checks (TryHandleRelayRouteAsync).
        if (IsRelayRoute(path)) return false;
        if (caller.Device is not { } me)
        {
            await SendJsonAsync(stream, 403, "Forbidden", new { error = "Pair this device again first." }, ct);
            return true;
        }
        switch (path.ToLowerInvariant())
        {
            case "/api/peer/inbox":
            {
                NoteVitals(me.Id, q);
                var wait = TimeSpan.FromSeconds(Math.Clamp(QueryLong(q, "wait") ?? 25, 0, 25));
                var (job, busy) = await Peer.WaitForJobAsync(me.Id, wait, ct);
                if (busy) await SendJsonAsync(stream, 503, "Service Unavailable", new { error = "Too many devices are waiting.", retryAfter = 10 }, ct);
                else await SendJsonAsync(stream, 200, "OK", new { job = job is null ? null : new { id = job.Id, kind = job.Kind, args = job.Args } }, ct);
                return true;
            }
            case "/api/peer/result" when method == "POST":
            {
                var job = Peer.Taken(q.GetValueOrDefault("job") ?? "", me.Id);
                if (job is null)
                {
                    await DrainBodyAsync(stream, headers, ct);
                    await SendJsonAsync(stream, 404, "Not Found", new { error = "No such question for this device (it may have run out of time)." }, ct);
                    return true;
                }
                var status = q.GetValueOrDefault("status") ?? "ok";
                if (status != "ok")
                {
                    await DrainBodyAsync(stream, headers, ct);
                    Peer.Complete(job, new PeerResult(status is "needs-permission" or "denied" or "error" ? status : "error", Error: q.GetValueOrDefault("error")));
                    await SendJsonAsync(stream, 200, "OK", new { ok = true }, ct);
                    return true;
                }
                if (job.Kind == "fs.read") await ReceivePeerFileAsync(job, headers, stream, ct);
                else
                {
                    long length = headers.TryGetValue("Content-Length", out var cl) && long.TryParse(cl, out var n) ? n : -1;
                    if (length > MaxPeerJson)
                    {
                        await DrainBrieflyAsync(stream, ct);
                        Peer.Complete(job, new PeerResult("error", Error: "The answer was too big."));
                        await SendJsonAsync(stream, 413, "Payload Too Large", new { error = $"Answers are at most {MaxPeerJson / (1024 * 1024)} MB." }, ct);
                        return true;
                    }
                    var text = await ReadSmallBodyAsync(stream, headers, MaxPeerJson, ct);
                    JsonElement? json = null;
                    try { if (text != null) json = JsonDocument.Parse(text).RootElement.Clone(); } catch (JsonException) { }
                    if (json is null)
                    {
                        Peer.Complete(job, new PeerResult("error", Error: "The device sent an answer that could not be read."));
                        await SendJsonAsync(stream, 400, "Bad Request", new { error = "The answer must be JSON." }, ct);
                        return true;
                    }
                    Peer.Complete(job, new PeerResult("ok", json));
                    await SendJsonAsync(stream, 200, "OK", new { ok = true }, ct);
                }
                return true;
            }
            case "/api/peer/blob":
                // A file this PC staged for this device to fetch (fs.pull: adding a file to the phone's folder).
                await HandleBlobAsync(me, q, headers, stream, caller.Ip, ct);
                return true;
            case "/api/peer/pairback" when method == "POST":
                await HandlePairBackAsync(me, headers, stream, caller.Ip, ct);
                return true;
            case "/api/peer/unpair" when method == "POST":
                // The other PC forgot this one: its "paired back" entry here goes too.
                if (me.PeerPcId is { } pcId) _host.RemotePcs.ForgetPairedBack(pcId);
                await SendJsonAsync(stream, 200, "OK", new { ok = true }, ct);
                return true;
            default:
                await SendJsonAsync(stream, 404, "Not Found", new { error = "Not found." }, ct);
                return true;
        }
    }

    private static bool IsRelayRoute(string path)
    {
        var p = path.ToLowerInvariant();
        return p == "/api/peer/devices" || p.StartsWith("/api/peer/fs/");
    }

    /// <summary>The relay routes, once the "Device to device" switch and this device's "relay" permission were checked.</summary>
    private async Task<bool> TryHandleRelayRouteAsync(string path, Dictionary<string, string> q, Dictionary<string, string> headers,
        NetworkStream stream, Caller caller, CancellationToken ct)
    {
        if (!IsRelayRoute(path)) return false;
        if (caller.Device is not { } me)
        {
            await SendJsonAsync(stream, 403, "Forbidden", new { error = "Pair this device again first." }, ct);
            return true;
        }
        var p = path.ToLowerInvariant();
        if (p == "/api/peer/devices")
        {
            // The phones and tablets a laptop can look at through this PC (each still allows it itself).
            List<PairedDevice> phones;
            lock (_devicesLock)
                phones = _host.Settings.MobileBridge.PairedDevices
                    .Where(d => d.Id != me.Id && !d.IsBlocked && DeviceKinds.Of(d.Kind) is DeviceKind.Phone or DeviceKind.Tablet).ToList();
            await SendJsonAsync(stream, 200, "OK", new
            {
                devices = phones.Select(d => new
                {
                    id = d.Id, name = d.Name, kind = DeviceKinds.Name(DeviceKinds.Of(d.Kind)),
                    listening = Peer.IsListening(d.Id), online = Presence.IsOnline(d.Id, d.LastSeen)
                })
            }, ct);
            return true;
        }
        if (p is "/api/peer/fs/roots" or "/api/peer/fs/list" or "/api/peer/fs/read" or "/api/peer/fs/tree")
        {
            await HandleRelayAsync(me, p, q, headers, stream, caller.Ip, ct);
            return true;
        }
        await SendJsonAsync(stream, 404, "Not Found", new { error = "Not found." }, ct);
        return true;
    }

    /// <summary>
    /// /api/peer/fs/{roots,list,tree,read}?device=&lt;phone&gt;: a laptop seeing a phone's files through this PC. The phone is
    /// asked as for this PC, with who is asking; it answers only as far as it allows this PC.
    /// </summary>
    private async Task HandleRelayAsync(PairedDevice me, string route, Dictionary<string, string> q, Dictionary<string, string> headers,
        NetworkStream stream, string clientIp, CancellationToken ct)
    {
        var phone = OtherDevice(q.GetValueOrDefault("device"));
        if (phone is null || phone.Id == me.Id || DeviceKinds.Of(phone.Kind) is not (DeviceKind.Phone or DeviceKind.Tablet))
        {
            await SendJsonAsync(stream, 404, "Not Found", new { error = "No such phone on this PC." }, ct);
            return;
        }
        var kind = route.EndsWith("/roots") ? "fs.roots" : route.EndsWith("/list") ? "fs.list" : route.EndsWith("/tree") ? "fs.tree" : "fs.read";
        var args = new Dictionary<string, string> { ["asker"] = me.Name };
        if (q.GetValueOrDefault("path") is { } p) args["path"] = p;
        var r = await AskDeviceAsync(phone.Id, kind, args, kind == "fs.read" ? TimeSpan.FromMinutes(15) : TimeSpan.FromSeconds(20), ct);
        if (kind != "fs.read")
        {
            await SendJsonAsync(stream, 200, "OK", new { status = r.Status, data = r.Json, error = r.Error }, ct);
            return;
        }
        if (!r.Ok || r.FilePath is not { } file)
        {
            await SendJsonAsync(stream, 409, "Conflict", new { status = r.Status, error = r.Error ?? "The phone did not send the file." }, ct);
            return;
        }
        try { await SendFileAsync(new FileInfo(file), headers, stream, clientIp, "application/octet-stream", "attachment", "download", ct); }
        finally { try { File.Delete(file); } catch (IOException) { } }
    }

    /// <summary>
    /// /api/peer/pairback: a laptop that just paired with this PC gives it a key to itself (with nothing allowed yet), so
    /// this PC lists it on its Other PCs page; it asks the laptop for access like any device.
    /// </summary>
    private async Task HandlePairBackAsync(PairedDevice me, Dictionary<string, string> headers, NetworkStream stream, string clientIp, CancellationToken ct)
    {
        if (DeviceKinds.Of(me.Kind) is not (DeviceKind.Laptop or DeviceKind.Desktop))
        {
            await DrainBodyAsync(stream, headers, ct);   // else Windows resets the connection and the reason is lost
            await SendJsonAsync(stream, 403, "Forbidden", new { error = "Only another PC can pair back." }, ct);
            return;
        }
        string? pcId = null, name = null, deviceId = null, token = null;
        int port = 0;
        try
        {
            var text = await ReadSmallBodyAsync(stream, headers, 16 * 1024, ct);
            using var doc = JsonDocument.Parse(text ?? "{}");
            var o = doc.RootElement;
            pcId = o.GetProperty("pcId").GetString();
            name = o.TryGetProperty("name", out var n) ? n.GetString() : null;
            deviceId = o.GetProperty("deviceId").GetString();
            token = o.GetProperty("token").GetString();
            port = o.GetProperty("port").GetInt32();
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException) { }
        // It must be the PC this device said it is when it paired, and everything must look like what a PC sends.
        bool plain(string? s, int min, int max) => s is { } v && v.Length >= min && v.Length <= max && v.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
        if (!Remote.RemotePcStore.IsValidPcId(pcId) || pcId != me.PeerPcId || port is < 1 or > 65535 || !plain(deviceId, 4, 64) || !plain(token, 16, 128)
            || !System.Net.IPAddress.TryParse(clientIp, out var ip))
        {
            await SendJsonAsync(stream, 400, "Bad Request", new { error = "That does not look like a pairing from the PC this device is." }, ct);
            return;
        }
        var host = ip.AddressFamily == AddressFamily.InterNetworkV6 ? $"[{ip}]" : ip.ToString();
        var cleanName = SafeUploadName(string.IsNullOrWhiteSpace(name) ? me.Name : name);
        _host.RemotePcs.AddPairedBack(pcId!, cleanName, $"http://{host}:{port}", deviceId!, token!);
        await SendJsonAsync(stream, 200, "OK", new { ok = true }, ct);
    }

    /// <summary>
    /// A key on this PC for another PC this one paired with (its "pair back"): nothing allowed until someone here says
    /// so. The same PC again gets its old entry back (with what was allowed).
    /// </summary>
    public (string DeviceId, string Token)? IssueTokenForPc(string pcId, string pcName, string kind)
    {
        if (!Remote.RemotePcStore.IsValidPcId(pcId)) return null;
        bool isNew;
        lock (_devicesLock) isNew = !_host.Settings.MobileBridge.PairedDevices.Any(d => d.InstallId == "pc-" + pcId);
        var (token, device) = IssueDeviceToken(pcName, "", "pc-" + pcId, null,
            new DeviceInfo(kind is "laptop" or "desktop" ? kind : "desktop", "Windows", null, pcId, pcName, null, true));
        if (isNew)
        {
            lock (_devicesLock) device.Grants = new List<AccessGrant>();
            _host.SaveSettings();
        }
        return (device.Id, token);
    }

    /// <summary>A file the device sends as its answer: kept in a temporary file for the asker (with the PC's 10 GB reserve).</summary>
    private async Task ReceivePeerFileAsync(PeerJob job, Dictionary<string, string> headers, NetworkStream stream, CancellationToken ct)
    {
        bool chunked = headers.TryGetValue("Transfer-Encoding", out var te) && te.Contains("chunked", StringComparison.OrdinalIgnoreCase);
        long length = !chunked && headers.TryGetValue("Content-Length", out var cl) && long.TryParse(cl, out var n) ? n : -1;
        if (length < 0 && !chunked)
        {
            Peer.Complete(job, new PeerResult("error", Error: "The device did not say how big the file is."));
            await SendJsonAsync(stream, 411, "Length Required", new { error = "A file answer needs a Content-Length or chunked encoding." }, ct);
            return;
        }
        if (length > MaxPeerFile)
        {
            await DrainBrieflyAsync(stream, ct);
            Peer.Complete(job, new PeerResult("error", Error: "The file is too big to copy in one go."));
            await SendJsonAsync(stream, 413, "Payload Too Large", new { error = "File too big." }, ct);
            return;
        }
        var dir = Directory.CreateDirectory(PeerTempDir).FullName;
        var file = Path.Combine(dir, job.Id + ".bin");
        long got = 0;
        try
        {
            var body = chunked ? (Stream)new ChunkedBodyStream(stream) : stream;
            await using (var fs = new FileStream(file, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024, true))
            {
                var buf = new byte[128 * 1024];
                long left = length < 0 ? long.MaxValue : length;
                while (left > 0)
                {
                    int read = await body.ReadAsync(buf.AsMemory(0, (int)Math.Min(buf.Length, left)), ct);
                    if (read <= 0) break;
                    await fs.WriteAsync(buf.AsMemory(0, read), ct);
                    got += read;
                    left -= read;
                    if (got > MaxPeerFile) throw new IOException("File too big.");
                }
            }
            if (length >= 0 && got != length) throw new IOException("The file was cut off.");
            Peer.Complete(job, new PeerResult("ok", FilePath: file, Bytes: got));
            await SendJsonAsync(stream, 200, "OK", new { ok = true, bytes = got }, ct);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or OperationCanceledException)
        {
            try { File.Delete(file); } catch (IOException) { }
            Peer.Complete(job, new PeerResult("error", Error: e.Message));
            if (e is not OperationCanceledException) await SendJsonAsync(stream, 400, "Bad Request", new { error = e.Message }, ct);
        }
    }
}
