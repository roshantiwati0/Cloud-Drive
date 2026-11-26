using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace CloudSSD.App.Remote;

/// <summary>
/// Talks to another PC's Cloud-SSD bridge as a device (the same routes a phone uses). The key goes in a header, never in
/// an address, and only to an address that proved it is that PC (see <see cref="ProveAsync"/>).
/// </summary>
public sealed class RemotePcClient : IDisposable
{
    private readonly HttpClient _http;

    public RemotePcClient(HttpMessageHandler? handler = null)
    {
        _http = new HttpClient(handler ?? new SocketsHttpHandler { UseProxy = false, ConnectTimeout = TimeSpan.FromSeconds(3) }, disposeHandler: true)
        { Timeout = Timeout.InfiniteTimeSpan };
    }

    public void Dispose() => _http.Dispose();

    /// <summary>"192.168.1.5" → "http://192.168.1.5:8090".</summary>
    public static string NormalizeUrl(string input, int defaultPort = 8090)
    {
        var v = (input ?? "").Trim().TrimEnd('/');
        if (v.Length == 0) return "";
        if (!v.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !v.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) v = "http://" + v;
        var hostPart = v[(v.IndexOf("://", StringComparison.Ordinal) + 3)..];
        if (!hostPart.Contains(':') || hostPart.StartsWith('[') && !hostPart.Contains("]:")) v += ":" + defaultPort.ToString(CultureInfo.InvariantCulture);
        return v;
    }

    private static string Q(string s) => Uri.EscapeDataString(s);

    private static CancellationTokenSource Limit(CancellationToken ct, TimeSpan? t)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (t is { } time) cts.CancelAfter(time);
        return cts;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct, TimeSpan? timeout, HttpCompletionOption opt = HttpCompletionOption.ResponseContentRead)
    {
        using var cts = Limit(ct, timeout);
        try
        {
            return await _http.SendAsync(req, opt, cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new RemotePcException(RemoteErrorKind.Offline, "The PC did not answer in time.");
        }
        catch (HttpRequestException e)
        {
            throw new RemotePcException(RemoteErrorKind.Offline, "The PC is not reachable (" + e.Message + ").");
        }
    }

    /// <summary>Turns a refusal into a clear error (the body's "error" text when there is one).</summary>
    private static async Task<RemotePcException> ErrorOf(HttpResponseMessage r, CancellationToken ct)
    {
        string? message = null, code = null;
        int retry = 0;
        var categories = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync(ct));
            if (doc.RootElement.TryGetProperty("error", out var e)) message = e.GetString();
            if (doc.RootElement.TryGetProperty("code", out var c)) code = c.GetString();
            if (doc.RootElement.TryGetProperty("retryAfterSeconds", out var ra) && ra.TryGetInt32(out var n)) retry = n;
            if (doc.RootElement.TryGetProperty("driveMissing", out var dm) && dm.ValueKind == JsonValueKind.True) code = "drive-missing";
            if (doc.RootElement.TryGetProperty("categories", out var cats) && cats.ValueKind == JsonValueKind.Array)
                categories.AddRange(cats.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0));
        }
        catch (Exception) { }
        var status = (int)r.StatusCode;
        var kind = status switch
        {
            401 => RemoteErrorKind.Unauthorized,
            // That PC wants this allowed first (per-device permissions): the page offers to ask it.
            403 when code == "needs-permission" => RemoteErrorKind.NeedsPermission,
            403 when code is "switched-off" or "tailnet-off" or "network-refused" => RemoteErrorKind.SwitchedOff,
            403 => RemoteErrorKind.WrongCode,
            404 when code == "drive-missing" => RemoteErrorKind.DriveMissing,
            404 => RemoteErrorKind.NotFound,
            429 => RemoteErrorKind.Locked,
            503 => RemoteErrorKind.DriveMissing,
            507 => RemoteErrorKind.NoSpace,
            _ => RemoteErrorKind.Server
        };
        return new RemotePcException(kind, message ?? $"The PC answered HTTP {status}.", status, retry, categories);
    }

    private static async Task<JsonElement> JsonOf(HttpResponseMessage r, CancellationToken ct)
    {
        if (!r.IsSuccessStatusCode) throw await ErrorOf(r, ct);
        using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync(ct));
        return doc.RootElement.Clone();
    }

    private HttpRequestMessage Req(HttpMethod m, string url, string? token)
    {
        var req = new HttpRequestMessage(m, url);
        if (token != null) req.Headers.Add("X-Pairing-Token", token);
        return req;
    }

    // ---- without a key ---------------------------------------------------------------------------------------------

    /// <summary>The PC's public answer: name, id, version, features. Null when nothing (or not a Cloud-SSD) answers.</summary>
    public async Task<(string PcName, string PcId, string Version, IReadOnlyList<string> Features)?> ProbeAsync(string url, CancellationToken ct, TimeSpan? timeout = null)
    {
        try
        {
            using var r = await SendAsync(Req(HttpMethod.Get, url + "/api/status", null), ct, timeout ?? TimeSpan.FromSeconds(4));
            if (!r.IsSuccessStatusCode) return null;
            var j = await JsonOf(r, ct);
            if (!j.TryGetProperty("pcName", out var n)) return null;
            var feats = j.TryGetProperty("features", out var f) && f.ValueKind == JsonValueKind.Array ? f.EnumerateArray().Select(x => x.GetString() ?? "").ToList() : new List<string>();
            return (n.GetString() ?? "PC", j.TryGetProperty("pcId", out var id) ? id.GetString() ?? "" : "", j.TryGetProperty("version", out var v) ? v.GetString() ?? "" : "", feats);
        }
        catch (RemotePcException) { return null; }
        catch (JsonException) { return null; }
    }

    /// <summary>
    /// Is this address really that PC? A PC with /api/hello answers a fresh challenge with the hash of this PC's key,
    /// which a device that only copied its public id cannot. An older PC is checked by its public id.
    /// </summary>
    public async Task<bool> ProveAsync(string url, string pcId, string deviceId, string token, CancellationToken ct)
    {
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        if (deviceId.Length > 0)
        {
            try
            {
                using var r = await SendAsync(Req(HttpMethod.Get, $"{url}/api/hello?nonce={nonce}&d={Q(deviceId)}", null), ct, TimeSpan.FromSeconds(4));
                if (r.IsSuccessStatusCode)
                {
                    var j = await JsonOf(r, ct);
                    var theirs = j.TryGetProperty("pcId", out var p) ? p.GetString() ?? "" : "";
                    var proof = j.TryGetProperty("proof", out var pr) ? pr.GetString() ?? "" : "";
                    if (theirs != pcId) return false;
                    var expected = LocalMobileBridgeService.HelloProof(LocalMobileBridgeService.HashToken(token), nonce, theirs);
                    return CryptographicOperations.FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes(expected), System.Text.Encoding.ASCII.GetBytes(proof));
                }
            }
            catch (RemotePcException) { return false; }
        }
        var s = await ProbeAsync(url, ct);
        if (s is null) return false;
        if (deviceId.Length > 0 && s.Value.Features.Contains("hello-proof")) return false;   // it can prove itself, so it must
        return s.Value.PcId.Length == 0 || s.Value.PcId == pcId;
    }

    /// <summary>Pairs with the six digits shown on that PC. Only works on the same network as that PC.</summary>
    public async Task<PairResult> PairAsync(string url, string code, PairInfo info, CancellationToken ct)
    {
        var digits = new string((code ?? "").Where(char.IsAsciiDigit).ToArray());
        var q = $"code={digits}&name={Q(info.Name)}&installId={Q(info.InstallId)}&kind={Q(info.Kind)}&platform={Q(info.Platform)}&app={Q(info.AppVersion)}";
        if (info.HubPcId != null) q += $"&hubPcId={Q(info.HubPcId)}&hubName={Q(info.HubName ?? "")}&hubPort={info.HubPort}";
        using var r = await SendAsync(Req(HttpMethod.Post, $"{url}/api/pair?{q}", null), ct, TimeSpan.FromSeconds(10));
        if ((int)r.StatusCode == 404) throw new RemotePcException(RemoteErrorKind.OldPc, "That PC runs an older Cloud-SSD without pairing codes. Update it first.", 404);
        var j = await JsonOf(r, ct);
        if (!j.TryGetProperty("token", out var t) || t.GetString() is not { Length: > 0 } token)
            throw new RemotePcException(RemoteErrorKind.WrongCode, j.TryGetProperty("error", out var e) ? e.GetString() ?? "Pairing failed." : "Pairing failed.");
        return new PairResult(token, j.TryGetProperty("deviceId", out var d) ? d.GetString() ?? "" : "",
            j.TryGetProperty("pcName", out var n) ? n.GetString() ?? "" : "", j.TryGetProperty("pcId", out var p) ? p.GetString() ?? "" : "");
    }

    // ---- with the key ----------------------------------------------------------------------------------------------

    public async Task<JsonElement> GetJsonAsync(string url, string route, string token, CancellationToken ct, TimeSpan? timeout = null)
    {
        using var r = await SendAsync(Req(HttpMethod.Get, url + route, token), ct, timeout ?? TimeSpan.FromSeconds(20));
        return await JsonOf(r, ct);
    }

    public async Task<JsonElement> PostJsonAsync(string url, string route, string token, CancellationToken ct, TimeSpan? timeout = null)
    {
        using var r = await SendAsync(Req(HttpMethod.Post, url + route, token), ct, timeout ?? TimeSpan.FromSeconds(60));
        return await JsonOf(r, ct);
    }

    /// <summary>A cheap "still there?" (falls back to /api/status on a PC without /api/ping).</summary>
    public async Task PingAsync(string url, string token, CancellationToken ct)
    {
        using var r = await SendAsync(Req(HttpMethod.Get, url + "/api/ping", token), ct, TimeSpan.FromSeconds(4));
        if (r.StatusCode == HttpStatusCode.NotFound) { await GetJsonAsync(url, "/api/status", token, ct, TimeSpan.FromSeconds(6)); return; }
        if (!r.IsSuccessStatusCode) throw await ErrorOf(r, ct);
    }

    public async Task<IReadOnlyList<RemoteRoot>> RootsAsync(string url, string token, CancellationToken ct)
    {
        var j = await GetJsonAsync(url, "/api/fs/roots?include=local", token, ct, TimeSpan.FromSeconds(10));
        return j.EnumerateArray().Select(r => new RemoteRoot(
            Str(r, "id"), Str(r, "title"), Bool(r, "readOnly"), Bool(r, "fixedTopLevel"), Bool(r, "canCreate"), Long(r, "totalBytes"), Long(r, "freeBytes"))).ToList();
    }

    public async Task<List<RemoteEntry>> ListAsync(string url, string token, string path, CancellationToken ct)
    {
        var j = await GetJsonAsync(url, "/api/fs/list?path=" + Q(path), token, ct, TimeSpan.FromSeconds(30));
        return j.TryGetProperty("entries", out var e) ? e.EnumerateArray().Select(EntryOf).ToList() : new List<RemoteEntry>();
    }

    internal static RemoteEntry EntryOf(JsonElement e) => new(Str(e, "id"), Str(e, "name"), Bool(e, "isDirectory"), Long(e, "size"),
        Long(e, "modified") ?? 0, Long(e, "created") ?? 0, Bool(e, "canEdit"), Bool(e, "canCreate"));

    private static string Str(JsonElement e, string p) => e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    private static bool Bool(JsonElement e, string p) => e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.True;
    private static long? Long(JsonElement e, string p) => e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : null;

    public Task<JsonElement> CreateFolderAsync(string url, string token, string parent, string name, CancellationToken ct) =>
        PostJsonAsync(url, $"/api/fs/create?parent={Q(parent)}&name={Q(name)}&dir=1", token, ct);

    public Task<JsonElement> RenameAsync(string url, string token, string path, string name, CancellationToken ct) =>
        PostJsonAsync(url, $"/api/fs/rename?path={Q(path)}&name={Q(name)}", token, ct);

    public Task<JsonElement> DeleteAsync(string url, string token, string path, CancellationToken ct) =>
        PostJsonAsync(url, $"/api/fs/delete?path={Q(path)}", token, ct, TimeSpan.FromMinutes(5));

    /// <summary>Tells that PC to forget this one (best effort; the local copy of the key is deleted anyway).</summary>
    public async Task RemoveSelfAsync(string url, string token, CancellationToken ct)
    {
        try { await PostJsonAsync(url, "/api/devices/remove?id=self", token, ct, TimeSpan.FromSeconds(5)); }
        catch (RemotePcException) { }
    }

    /// <summary>
    /// Downloads a file into <paramref name="dest"/>, continuing a ".cssd-part" left by an earlier attempt when the file
    /// did not change (If-Range); a changed file starts over. Returns the size.
    /// </summary>
    public Task<long> DownloadAsync(string url, string token, string path, string dest, IProgress<long>? progress, CancellationToken ct) =>
        DownloadRouteAsync(url, token, "/api/fs/read?purpose=download&path=" + Q(path), dest, progress, ct);

    /// <summary>One file offered to this laptop (see <see cref="InboxAsync"/>), resumed like any download.</summary>
    public Task<long> DownloadShareAsync(string url, string token, string offerId, string itemId, string dest, IProgress<long>? progress, CancellationToken ct) =>
        DownloadRouteAsync(url, token, $"/api/share/file?id={Q(offerId)}&item={Q(itemId)}", dest, progress, ct);

    // ---- files offered to this laptop ------------------------------------------------------------------------------

    /// <summary>What that PC (or a phone through it) offers this laptop.</summary>
    public async Task<IReadOnlyList<RemoteShareOffer>> InboxAsync(string url, string token, CancellationToken ct)
    {
        var o = await GetJsonAsync(url, "/api/share/inbox", token, ct, TimeSpan.FromSeconds(10));
        if (!o.TryGetProperty("offers", out var offers) || offers.ValueKind != JsonValueKind.Array) return [];
        return offers.EnumerateArray().Select(e => new RemoteShareOffer(
            e.GetProperty("id").GetString() ?? "", e.TryGetProperty("from", out var f) ? f.GetString() ?? "" : "",
            e.TryGetProperty("fromPc", out var fp) && fp.ValueKind == JsonValueKind.True, e.TryGetProperty("state", out var s) ? s.GetString() ?? "" : "",
            e.TryGetProperty("total", out var t) && t.TryGetInt64(out var tl) ? tl : 0,
            e.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array
                ? items.EnumerateArray().Select(i => new RemoteShareItem(i.GetProperty("id").GetString() ?? "", i.GetProperty("name").GetString() ?? "",
                    i.TryGetProperty("size", out var sz) && sz.TryGetInt64(out var szl) ? szl : 0,
                    i.TryGetProperty("ready", out var r) && r.ValueKind == JsonValueKind.True, i.TryGetProperty("delivered", out var d) && d.ValueKind == JsonValueKind.True)
                {
                    Folder = i.TryGetProperty("folder", out var fo) && fo.ValueKind == JsonValueKind.String ? fo.GetString() ?? "" : ""
                }).ToList()
                : [])).ToList();
    }

    public Task<JsonElement> AcceptShareAsync(string url, string token, string offerId, CancellationToken ct) =>
        PostJsonAsync(url, "/api/share/accept?id=" + Q(offerId), token, ct, TimeSpan.FromSeconds(15));

    public Task<JsonElement> DeclineShareAsync(string url, string token, string offerId, CancellationToken ct) =>
        PostJsonAsync(url, "/api/share/decline?id=" + Q(offerId), token, ct, TimeSpan.FromSeconds(15));

    // ---- pairing back, and that PC's phones -------------------------------------------------------------------------

    /// <summary>Gives that PC a key to this one (see RemotePcManager.PairBackAsync); the key travels in the body, not the address.</summary>
    public async Task PairBackAsync(string url, string token, string ownPcId, string ownName, int ownPort, string deviceId, string backToken, CancellationToken ct)
    {
        var req = Req(HttpMethod.Post, url + "/api/peer/pairback", token);
        req.Content = new StringContent(JsonSerializer.Serialize(new { pcId = ownPcId, name = ownName, port = ownPort, deviceId, token = backToken }),
            System.Text.Encoding.UTF8, "application/json");
        using var r = await SendAsync(req, ct, TimeSpan.FromSeconds(15));
        if (!r.IsSuccessStatusCode) throw await ErrorOf(r, ct);
    }

    /// <summary>The phones and tablets paired with that PC, to look at through it.</summary>
    public async Task<IReadOnlyList<RemotePhone>> PhonesAsync(string url, string token, CancellationToken ct)
    {
        var o = await GetJsonAsync(url, "/api/peer/devices", token, ct, TimeSpan.FromSeconds(10));
        if (!o.TryGetProperty("devices", out var a) || a.ValueKind != JsonValueKind.Array) return [];
        return a.EnumerateArray().Select(d => new RemotePhone(d.GetProperty("id").GetString() ?? "", d.GetProperty("name").GetString() ?? "",
            d.TryGetProperty("kind", out var k) ? k.GetString() ?? "phone" : "phone",
            d.TryGetProperty("listening", out var l) && l.ValueKind == JsonValueKind.True, d.TryGetProperty("online", out var on) && on.ValueKind == JsonValueKind.True)).ToList();
    }

    /// <summary>A question to one of that PC's phones (fs.roots / fs.list): its status, answer and error.</summary>
    public async Task<(string Status, JsonElement? Data, string? Error)> AskPhoneAsync(string url, string token, string phoneId, string kind, string? path, CancellationToken ct)
    {
        var route = (kind switch { "fs.roots" => "/api/peer/fs/roots", "fs.tree" => "/api/peer/fs/tree", _ => "/api/peer/fs/list" })
            + "?device=" + Q(phoneId) + (path is null ? "" : "&path=" + Q(path));
        var o = await GetJsonAsync(url, route, token, ct, TimeSpan.FromSeconds(30));
        return (o.TryGetProperty("status", out var s) ? s.GetString() ?? "error" : "error",
            o.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Object ? d.Clone() : null,
            o.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null);
    }

    /// <summary>Copies one file from that PC's phone into <paramref name="dest"/>; the phone's refusal comes back as (status, error).</summary>
    public async Task<(string Status, string? Error, long Bytes)> ReadPhoneFileAsync(string url, string token, string phoneId, string path, string dest, CancellationToken ct)
    {
        var req = Req(HttpMethod.Get, url + "/api/peer/fs/read?device=" + Q(phoneId) + "&path=" + Q(path), token);
        using var r = await SendAsync(req, ct, TimeSpan.FromMinutes(20), HttpCompletionOption.ResponseHeadersRead);
        if (r.StatusCode == HttpStatusCode.Conflict)   // the phone did not send it: its reason
        {
            using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync(ct));
            var o = doc.RootElement;
            return (o.TryGetProperty("status", out var s) ? s.GetString() ?? "error" : "error", o.TryGetProperty("error", out var e) ? e.GetString() : null, 0);
        }
        if (!r.IsSuccessStatusCode) throw await ErrorOf(r, ct);
        long got = 0;
        await using (var file = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024, true))
        await using (var body = await r.Content.ReadAsStreamAsync(ct))
        {
            var buf = new byte[128 * 1024];
            int n;
            while ((n = await body.ReadAsync(buf, ct)) > 0) { await file.WriteAsync(buf.AsMemory(0, n), ct); got += n; }
        }
        return ("ok", null, got);
    }

    // ---- asking that PC for more access (per-device permissions) ---------------------------------------------------

    /// <summary>Asks the person at that PC to allow these categories; they answer there (always, for an hour, or no).</summary>
    public async Task<RemoteAccessRequest> RequestAccessAsync(string url, string token, IEnumerable<string> categories, string? reason, CancellationToken ct)
    {
        var route = "/api/access/request?categories=" + Q(string.Join(",", categories)) + (string.IsNullOrEmpty(reason) ? "" : "&reason=" + Q(reason));
        var o = await PostJsonAsync(url, route, token, ct, TimeSpan.FromSeconds(15));
        return AccessOf(o.GetProperty("request"));
    }

    /// <summary>The answer to the last question (null when there is none).</summary>
    public async Task<RemoteAccessRequest?> AccessRequestAsync(string url, string token, CancellationToken ct)
    {
        var o = await GetJsonAsync(url, "/api/access/mine", token, ct, TimeSpan.FromSeconds(10));
        return o.TryGetProperty("request", out var r) && r.ValueKind == JsonValueKind.Object ? AccessOf(r) : null;
    }

    private static RemoteAccessRequest AccessOf(JsonElement r) => new(
        r.GetProperty("id").GetString() ?? "", r.TryGetProperty("state", out var s) ? s.GetString() ?? "" : "",
        r.TryGetProperty("categories", out var c) && c.ValueKind == JsonValueKind.Array ? c.EnumerateArray().Select(x => x.GetString() ?? "").ToList() : [],
        r.TryGetProperty("expiresIn", out var e) && e.TryGetInt32(out var n) ? n : 0);

    /// <summary>
    /// Downloads one route into <paramref name="dest"/>, continuing a ".cssd-part" left by an earlier attempt when the
    /// file did not change (If-Range); a changed file starts over. Returns the size.
    /// </summary>
    private async Task<long> DownloadRouteAsync(string url, string token, string route, string dest, IProgress<long>? progress, CancellationToken ct)
    {
        var part = dest + ".cssd-part";
        var etagFile = part + ".etag";
        long have = File.Exists(part) ? new FileInfo(part).Length : 0;
        var req = Req(HttpMethod.Get, url + route, token);
        if (have > 0 && File.Exists(etagFile))
        {
            req.Headers.Range = new RangeHeaderValue(have, null);
            req.Headers.TryAddWithoutValidation("If-Range", File.ReadAllText(etagFile).Trim());
        }
        using var r = await SendAsync(req, ct, null, HttpCompletionOption.ResponseHeadersRead);
        if (!r.IsSuccessStatusCode) throw await ErrorOf(r, ct);
        bool resumed = r.StatusCode == HttpStatusCode.PartialContent;
        if (!resumed) have = 0;
        if (r.Headers.ETag is { } tag) { Directory.CreateDirectory(Path.GetDirectoryName(part)!); File.WriteAllText(etagFile, tag.ToString()); }
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        await using (var file = new FileStream(part, resumed ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024, true))
        await using (var body = await r.Content.ReadAsStreamAsync(ct))
        {
            var buf = new byte[128 * 1024];
            int n;
            try
            {
                while ((n = await body.ReadAsync(buf, ct)) > 0)
                {
                    await file.WriteAsync(buf.AsMemory(0, n), ct);
                    have += n;
                    progress?.Report(have);
                }
            }
            catch (IOException e) { throw new RemotePcException(RemoteErrorKind.Offline, "The connection to the PC was lost (" + e.Message + ")."); }
        }
        var expected = r.Content.Headers.ContentRange?.Length ?? (resumed ? null : r.Content.Headers.ContentLength);
        if (expected is { } total && have < total) throw new RemotePcException(RemoteErrorKind.Offline, "The download was cut off; it continues next time.");
        File.Move(part, dest, overwrite: true);
        try { File.Delete(etagFile); } catch (IOException) { }
        return have;
    }

    /// <summary>
    /// Sends a file into a folder on that PC in pieces, continuing from what the PC already has (it never overwrites:
    /// an existing name gets " (1)"). Returns true when the file is complete there.
    /// </summary>
    public async Task<bool> UploadAsync(string url, string token, UploadJob job, IProgress<long>? progress, CancellationToken ct, int chunkSize = 8 * 1024 * 1024)
    {
        var fi = new FileInfo(job.LocalPath);
        if (!fi.Exists) throw new RemotePcException(RemoteErrorKind.NotFound, "The file is no longer on this PC.");
        long offset = 0;
        var st = await GetJsonAsync(url, "/api/upload/status?uploadId=" + job.Id, token, ct, TimeSpan.FromSeconds(10));
        if (st.TryGetProperty("offset", out var o) && o.TryGetInt64(out var have) && st.TryGetProperty("total", out var tot) && tot.TryGetInt64(out var t) && t == fi.Length) offset = have;
        var target = job.RemoteFolder.EndsWith(':') ? job.RemoteFolder + job.Name : job.RemoteFolder.TrimEnd('/') + "/" + job.Name;
        long modified = new DateTimeOffset(fi.LastWriteTimeUtc, TimeSpan.Zero).ToUnixTimeMilliseconds();
        await using var src = new FileStream(fi.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 128 * 1024, true);
        while (true)
        {
            long len = Math.Min(chunkSize, fi.Length - offset);
            src.Seek(offset, SeekOrigin.Begin);
            var buf = new byte[len];
            int read = 0;
            while (read < len) { int n = await src.ReadAsync(buf.AsMemory(read, (int)(len - read)), ct); if (n == 0) break; read += n; }
            var req = Req(HttpMethod.Post, $"{url}/api/fs/write?path={Q(target)}&uploadId={job.Id}&offset={offset}&total={fi.Length}&modified={modified}&mkdirs=1&overwrite=0", token);
            req.Content = new ByteArrayContent(buf, 0, read);
            using var r = await SendAsync(req, ct, TimeSpan.FromMinutes(10));
            if ((int)r.StatusCode == 409)
            {
                // The PC has a different amount than we thought: continue from there.
                try
                {
                    using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync(ct));
                    if (doc.RootElement.TryGetProperty("offset", out var off) && off.TryGetInt64(out var real)) { offset = real; continue; }
                }
                catch (JsonException) { }
                throw await ErrorOf(r, ct);
            }
            var j = await JsonOf(r, ct);
            if (j.TryGetProperty("complete", out var c) && c.ValueKind == JsonValueKind.False)
            {
                offset = j.TryGetProperty("offset", out var no) && no.TryGetInt64(out var nn) ? nn : offset + read;
                progress?.Report(offset);
                if (read == 0) return false;
                continue;
            }
            progress?.Report(fi.Length);
            return true;
        }
    }
}
