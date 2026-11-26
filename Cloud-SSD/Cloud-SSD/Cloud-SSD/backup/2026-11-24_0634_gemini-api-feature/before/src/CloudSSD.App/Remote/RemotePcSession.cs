using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace CloudSSD.App.Remote;

/// <summary>Which files and folders appeared since a folder was last saved (NEW badges for a day).</summary>
public static class ListDiff
{
    public static readonly TimeSpan NewFor = TimeSpan.FromDays(1);

    /// <summary>Nothing is new on the first visit; badges from the last refresh carry over for a day.</summary>
    public static (List<string> Ids, DateTimeOffset? At) NewIds(CachedList? old, IReadOnlyList<RemoteEntry> fresh, DateTimeOffset now)
    {
        if (old is null) return (new List<string>(), null);
        var before = old.Entries.Select(e => e.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added = fresh.Where(e => !before.Contains(e.Id)).Select(e => e.Id).ToList();
        var stillThere = fresh.Select(e => e.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var carried = old.NewAt is { } at && now - at < NewFor ? old.NewIds.Where(stillThere.Contains).ToList() : new List<string>();
        var ids = added.Concat(carried).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return (ids, added.Count > 0 ? now : carried.Count > 0 ? old.NewAt : null);
    }

    public static bool IsNew(CachedList? list, string id, DateTimeOffset now) =>
        list?.NewAt is { } at && now - at < NewFor && list.NewIds.Contains(id, StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// One other PC as this PC sees it, offline first: what it showed last time is always there (lists, drives, when),
/// and is refreshed whenever it answers. Files sent to it wait in a queue (kept across restarts) until it is back.
/// </summary>
public sealed partial class RemotePcSession : IAsyncDisposable
{
    private readonly RemotePcStore _store;
    private readonly RemotePcClient _client;
    private readonly ILogger _log;
    private readonly Func<DateTimeOffset> _now;
    private readonly SemaphoreSlim _uploadGate = new(1, 1);
    private readonly object _jobsLock = new();
    private List<UploadJob> _jobs = new();
    private CancellationTokenSource? _loop;
    /// <summary>Cancelled when the session ends (that PC forgotten, app closing): no background work outlives it.</summary>
    private readonly CancellationTokenSource _life = new();
    private Task? _loopTask;
    private int _disposed;
    private string? _url;

    public RemotePcSession(RemotePcStore store, RemotePc pc, RemotePcClient client, ILogger log, Func<DateTimeOffset>? now = null)
    {
        _store = store;
        Pc = pc;
        _client = client;
        _log = log;
        _now = now ?? (() => DateTimeOffset.Now);
        Directory.CreateDirectory(Dir);
        LoadJobs();
    }

    public RemotePc Pc { get; private set; }
    public RemoteLinkState State { get; private set; } = RemoteLinkState.Unknown;
    public bool IsOnline => State == RemoteLinkState.Online;
    /// <summary>Its page is on screen: checked every few seconds instead of every half minute.</summary>
    public bool Foreground { get; set; }
    public string? CurrentUrl => _url;
    public string? OpenPath { get; set; }

    public event Action? StateChanged;
    public event Action<string>? ListChanged;
    public event Action? UploadsChanged;

    private string Dir => _store.PcDir(Pc.PcId);
    private string ListsDir => Path.Combine(Dir, "lists");
    private string JobsFile => Path.Combine(Dir, "queue.json");
    internal string OpenDir => Path.Combine(Dir, "open");
    private static string Hash(string s) => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(s.ToLowerInvariant()))).ToLowerInvariant();

    private string Token => _store.Token(Pc) ?? "";

    private void SaveMeta() { try { _store.Upsert(Pc); } catch (IOException e) { _log.LogDebug(e, "Saving remote PC failed"); } }

    // ---- staying in touch --------------------------------------------------------------------------------------------

    public void Start()
    {
        if (_loop != null || _life.IsCancellationRequested) return;
        _loop = CancellationTokenSource.CreateLinkedTokenSource(_life.Token);
        var ct = _loop.Token;
        _loopTask = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                try { await TickAsync(ct); } catch (Exception e) when (e is not OperationCanceledException) { _log.LogDebug(e, "Remote PC check failed"); }
                try { await Task.Delay(Foreground ? TimeSpan.FromSeconds(5) : TimeSpan.FromSeconds(25), ct); } catch (OperationCanceledException) { break; }
            }
        }, CancellationToken.None);
    }

    /// <summary>
    /// Stops the checks, the sending and the keeping of folders, and waits until none of them is still running — so
    /// nothing writes into this PC's folder after it is forgotten and deleted.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        _life.Cancel();
        try { if (_loopTask != null) await _loopTask.WaitAsync(TimeSpan.FromSeconds(10)); } catch (Exception e) when (e is OperationCanceledException or TimeoutException) { }
        foreach (var gate in new[] { _pinGate, _uploadGate, _inboxGate })
        {
            try { if (await gate.WaitAsync(TimeSpan.FromSeconds(10))) gate.Release(); } catch (ObjectDisposedException) { }
        }
    }

    /// <summary>The addresses to try: the last that worked, home, then Tailscale.</summary>
    private IEnumerable<string> Candidates() =>
        new[] { Pc.LastUrl, Pc.LanUrl }.Concat(Pc.AwayUrls).Where(u => !string.IsNullOrWhiteSpace(u)).Select(u => u!.TrimEnd('/')).Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// One check: find an address that proves it is that PC, then a cheap ping. Going from away to back refreshes
    /// what is on screen and sends what waited. Returns whether it is online.
    /// </summary>
    public async Task<bool> TickAsync(CancellationToken ct = default)
    {
        if (Pc.NeedsRepair) { SetState(RemoteLinkState.NeedsRepair); return false; }
        var token = Token;
        if (token.Length == 0) { Pc.NeedsRepair = true; SaveMeta(); SetState(RemoteLinkState.NeedsRepair); return false; }
        bool wasOnline = IsOnline;
        try
        {
            if (_url == null)
            {
                foreach (var url in Candidates())
                {
                    if (await _client.ProveAsync(url, Pc.PcId, Pc.DeviceId, token, ct)) { _url = url; break; }
                }
                if (_url == null) throw new RemotePcException(RemoteErrorKind.Offline, "Not reachable.");
            }
            await _client.PingAsync(_url, token, ct);
            Pc.LastUrl = _url;
            Pc.LastOnlineAt = _now();
            SetState(RemoteLinkState.Online);
            if (!wasOnline) { SaveMeta(); await OnReconnectedAsync(ct); }
            SyncPinsSoon();
            CheckInboxSoon();
            return true;
        }
        catch (RemotePcException e) when (e.Kind == RemoteErrorKind.Unauthorized)
        {
            Pc.NeedsRepair = true;
            SaveMeta();
            SetState(RemoteLinkState.NeedsRepair);
            return false;
        }
        catch (RemotePcException)
        {
            _url = null;
            SetState(RemoteLinkState.Offline);
            if (wasOnline) SaveMeta();
            return false;
        }
    }

    private void SetState(RemoteLinkState s)
    {
        if (State == s) return;
        State = s;
        try { StateChanged?.Invoke(); } catch (Exception e) { _log.LogDebug(e, "StateChanged handler failed"); }
    }

    /// <summary>Back after being away: status, drives and the open folder quietly refreshed; waiting files sent.</summary>
    private async Task OnReconnectedAsync(CancellationToken ct)
    {
        try
        {
            var st = await _client.GetJsonAsync(_url!, "/api/status", Token, ct, TimeSpan.FromSeconds(8));
            if (st.TryGetProperty("pcName", out var n) && n.GetString() is { Length: > 0 } name) Pc.Name = name;
            if (st.TryGetProperty("features", out var f) && f.ValueKind == JsonValueKind.Array) Pc.Features = f.EnumerateArray().Select(x => x.GetString() ?? "").ToList();
            if (st.TryGetProperty("away", out var away) && away.TryGetProperty("allowed", out var al) && al.ValueKind == JsonValueKind.True)
            {
                var port = new Uri(Pc.LanUrl).Port;
                var urls = away.TryGetProperty("ips", out var ips) ? ips.EnumerateArray().Select(i => $"http://{i.GetString()}:{port}").ToList() : new List<string>();
                if (away.TryGetProperty("dnsName", out var dn) && dn.GetString() is { Length: > 0 } dns) urls.Add($"http://{dns}:{port}");
                Pc.AwayUrls = urls;
            }
            RemotePcStore.WriteCache(Path.Combine(Dir, "status.json"), st.GetRawText());
            SaveMeta();
        }
        catch (Exception e) when (e is RemotePcException or IOException) { }
        try { await GetRootsAsync(ct); } catch (RemotePcException) { }
        if (OpenPath != null) { try { await ListAsync(OpenPath, ct); } catch (RemotePcException) { } }
        _ = FlushUploadsAsync(_life.Token);
    }

    // ---- drives and folders (live, else kept) -----------------------------------------------------------------------

    public async Task<Fetched<IReadOnlyList<RemoteRoot>>> GetRootsAsync(CancellationToken ct = default)
    {
        var file = Path.Combine(Dir, "roots.json");
        if (IsOnline && _url != null)
        {
            try
            {
                var roots = await _client.RootsAsync(_url, Token, ct);
                var now = _now();
                RemotePcStore.WriteCache(file, JsonSerializer.Serialize(new { savedAt = now, roots }, RemotePcStore.Json));
                return new Fetched<IReadOnlyList<RemoteRoot>>(roots, false, now);
            }
            catch (RemotePcException e) when (e.Kind == RemoteErrorKind.Offline) { await TickAsync(ct); }
        }
        if (File.Exists(file))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                var at = doc.RootElement.GetProperty("savedAt").GetDateTimeOffset();
                var roots = doc.RootElement.GetProperty("roots").Deserialize<List<RemoteRoot>>(RemotePcStore.Json) ?? new();
                return new Fetched<IReadOnlyList<RemoteRoot>>(roots, true, at);
            }
            catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or KeyNotFoundException or InvalidOperationException) { }
        }
        throw new RemotePcException(RemoteErrorKind.Offline, $"{Pc.DisplayName} is not reachable and nothing was saved from it yet.");
    }

    private string ListFile(string path) => Path.Combine(ListsDir, Hash(path) + ".json");

    public CachedList? Saved(string path)
    {
        var f = ListFile(path);
        if (!File.Exists(f)) return null;
        try { return JsonSerializer.Deserialize<CachedList>(File.ReadAllText(f), RemotePcStore.Json); }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>A folder: fresh from the PC (and saved, with NEW badges), or the saved list with when it was saved.</summary>
    public async Task<Fetched<CachedList>> ListAsync(string path, CancellationToken ct = default)
    {
        var old = Saved(path);
        if (IsOnline && _url != null)
        {
            try
            {
                var entries = await _client.ListAsync(_url, Token, path, ct);
                var list = SaveList(path, old, entries);
                return new Fetched<CachedList>(list, false, list.SavedAt);
            }
            catch (RemotePcException e) when (e.Kind == RemoteErrorKind.Offline) { await TickAsync(ct); }
        }
        if (old != null) return new Fetched<CachedList>(old, true, old.SavedAt);
        throw new RemotePcException(RemoteErrorKind.Offline, $"{Pc.DisplayName} is not reachable, and this folder was not opened while it was.");
    }

    /// <summary>Keeps a fresh listing (with its NEW badges) for when that PC is away, and says so.</summary>
    private CachedList SaveList(string path, CachedList? old, List<RemoteEntry> entries)
    {
        foreach (var e in entries) if (!LocalMobileBridgeService.IsPlainName(e.Name)) throw new RemotePcException(RemoteErrorKind.Server, "The PC sent a name that cannot be used.");
        var now = _now();
        var (ids, at) = ListDiff.NewIds(old, entries, now);
        var list = new CachedList { Path = path, SavedAt = now, Entries = entries, NewIds = ids, NewAt = at };
        RemotePcStore.WriteCache(ListFile(path), JsonSerializer.Serialize(list, RemotePcStore.Json));
        ListChanged?.Invoke(path);
        return list;
    }

    /// <summary>The badges have been seen: they go.</summary>
    public void MarkSeen(string path)
    {
        var l = Saved(path);
        if (l is null || l.NewIds.Count == 0) return;
        l.NewIds.Clear();
        l.NewAt = null;
        RemotePcStore.WriteCache(ListFile(path), JsonSerializer.Serialize(l, RemotePcStore.Json));
    }

    private void RequireOnline()
    {
        if (!IsOnline || _url == null) throw new RemotePcException(RemoteErrorKind.Offline, $"{Pc.DisplayName} is offline — this needs it to be on.");
    }

    public async Task CreateFolderAsync(string parent, string name, CancellationToken ct = default) { RequireOnline(); await _client.CreateFolderAsync(_url!, Token, parent, name, ct); }
    public async Task RenameAsync(string path, string name, CancellationToken ct = default) { RequireOnline(); await _client.RenameAsync(_url!, Token, path, name, ct); }
    public async Task DeleteAsync(string path, CancellationToken ct = default) { RequireOnline(); await _client.DeleteAsync(_url!, Token, path, ct); }

    /// <summary>A safe local path for a file from that PC under <paramref name="root"/> (names are checked first).</summary>
    internal static string SafeLocal(string root, string remoteId)
    {
        var colon = remoteId.IndexOf(':');
        var drive = colon > 0 ? remoteId[..colon] : "";
        if (drive.Length is 0 or > 40 || !drive.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-')) throw new RemotePcException(RemoteErrorKind.Server, "Unexpected drive name from the PC.");
        var parts = remoteId[(colon + 1)..].Split('/', StringSplitOptions.RemoveEmptyEntries);
        foreach (var p in parts) if (!LocalMobileBridgeService.IsPlainName(p)) throw new RemotePcException(RemoteErrorKind.Server, "Unexpected file name from the PC.");
        var full = Path.GetFullPath(Path.Combine(new[] { root, drive }.Concat(parts).ToArray()));
        if (!full.StartsWith(Path.GetFullPath(root).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) throw new RemotePcException(RemoteErrorKind.Server, "Unexpected path from the PC.");
        return full;
    }

    /// <summary>Downloads a file to a folder on this PC (a free name is picked when one exists). Returns the path.</summary>
    public async Task<string> DownloadAsync(RemoteEntry e, string folder, IProgress<long>? progress = null, CancellationToken ct = default)
    {
        RequireOnline();
        if (!LocalMobileBridgeService.IsPlainName(e.Name)) throw new RemotePcException(RemoteErrorKind.Server, "Unexpected file name from the PC.");
        Directory.CreateDirectory(folder);
        var dest = Path.Combine(folder, LocalMobileBridgeService.FreeName(folder, e.Name));
        await _client.DownloadAsync(_url!, Token, e.Id, dest, progress, ct);
        return dest;
    }

    /// <summary>A file to open on this PC: a copy kept offline when there is one, else fetched now (online only).</summary>
    public async Task<string> OpenAsync(RemoteEntry e, CancellationToken ct = default)
    {
        var pinned = PinnedCopy(e);
        if (pinned != null && (!IsOnline || new FileInfo(pinned).Length == (e.Size ?? -1))) return pinned;
        RequireOnline();
        var dest = SafeLocal(OpenDir, e.Id);
        if (File.Exists(dest) && new FileInfo(dest).Length == (e.Size ?? -1) && File.GetLastWriteTimeUtc(dest) >= DateTimeOffset.FromUnixTimeMilliseconds(e.Modified).UtcDateTime.AddSeconds(-2)) return dest;
        await _client.DownloadAsync(_url!, Token, e.Id, dest, null, ct);
        File.SetLastWriteTimeUtc(dest, DateTimeOffset.FromUnixTimeMilliseconds(e.Modified).UtcDateTime);
        return dest;
    }

    /// <summary>The "Offline rakho" copy of a file, when its folder is kept on this PC (filled in by the pin sync).</summary>
    public string? PinnedCopy(RemoteEntry e)
    {
        try
        {
            var p = SafeLocal(Path.Combine(Dir, "pinned"), e.Id);
            return File.Exists(p) ? p : null;
        }
        catch (RemotePcException) { return null; }
    }

    // ---- files sent to that PC: a queue that waits for it -------------------------------------------------------------

    public IReadOnlyList<UploadJob> Uploads { get { lock (_jobsLock) return _jobs.Select(Clone).ToList(); } }
    private static UploadJob Clone(UploadJob j) => new()
    {
        Id = j.Id, LocalPath = j.LocalPath, RemoteFolder = j.RemoteFolder, Name = j.Name, Size = j.Size, LocalModifiedUtc = j.LocalModifiedUtc,
        State = j.State, SentBytes = j.SentBytes, Error = j.Error, CreatedAt = j.CreatedAt
    };

    private void LoadJobs()
    {
        try { _jobs = File.Exists(JobsFile) ? JsonSerializer.Deserialize<List<UploadJob>>(File.ReadAllText(JobsFile), RemotePcStore.Json) ?? new() : new(); }
        catch (JsonException) { _jobs = new(); }
        foreach (var j in _jobs) if (j.State == "running") j.State = "queued";   // the app stopped meanwhile
    }

    private void SaveJobs()
    {
        lock (_jobsLock)
        {
            var tmp = JobsFile + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_jobs, RemotePcStore.Json));
            File.Move(tmp, JobsFile, true);
        }
        UploadsChanged?.Invoke();
    }

    /// <summary>Queues files (a folder's files too, keeping its structure) for a folder on that PC; sent now or when it is back.</summary>
    public int EnqueueFiles(IEnumerable<string> localPaths, string remoteFolder)
    {
        int n = 0;
        lock (_jobsLock)
        {
            foreach (var p in localPaths)
            {
                if (File.Exists(p)) { Add(p, remoteFolder); n++; }
                else if (Directory.Exists(p))
                {
                    var baseDir = Path.GetDirectoryName(Path.GetFullPath(p).TrimEnd('\\')) ?? p;
                    foreach (var f in Directory.EnumerateFiles(p, "*", SearchOption.AllDirectories))
                    {
                        var rel = Path.GetRelativePath(baseDir, Path.GetDirectoryName(f)!).Replace('\\', '/');
                        Add(f, remoteFolder.EndsWith(':') ? remoteFolder + rel : remoteFolder.TrimEnd('/') + "/" + rel);
                        n++;
                    }
                }
            }
        }
        SaveJobs();
        if (IsOnline) _ = FlushUploadsAsync(_life.Token);
        return n;

        void Add(string file, string folder)
        {
            var fi = new FileInfo(file);
            _jobs.Add(new UploadJob
            {
                Id = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant(),
                LocalPath = fi.FullName, RemoteFolder = folder, Name = fi.Name, Size = fi.Length, LocalModifiedUtc = fi.LastWriteTimeUtc
            });
        }
    }

    public void ClearFinishedUploads()
    {
        lock (_jobsLock) _jobs.RemoveAll(j => j.State is "done");
        SaveJobs();
    }

    /// <summary>Sends what is queued, one at a time, while that PC is reachable.</summary>
    public async Task FlushUploadsAsync(CancellationToken ct)
    {
        if (!await _uploadGate.WaitAsync(0, ct)) return;
        try
        {
            while (IsOnline && _url != null)
            {
                UploadJob? job;
                lock (_jobsLock) job = _jobs.FirstOrDefault(j => j.State == "queued");
                if (job is null) break;
                var fi = new FileInfo(job.LocalPath);
                if (!fi.Exists) { Finish(job, "failed", "The file is no longer on this PC."); continue; }
                if (fi.Length != job.Size || fi.LastWriteTimeUtc != job.LocalModifiedUtc)
                {
                    // Changed since it was queued: send the new version from the start.
                    job.Id = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
                    job.Size = fi.Length; job.LocalModifiedUtc = fi.LastWriteTimeUtc; job.SentBytes = 0;
                }
                job.State = "running";
                SaveJobs();
                try
                {
                    var progress = new Progress<long>(b => { job.SentBytes = b; UploadsChanged?.Invoke(); });
                    bool done = await _client.UploadAsync(_url, Token, job, progress, ct, ChunkSize);
                    Finish(job, done ? "done" : "queued", null);
                    if (done) ListChanged?.Invoke(job.RemoteFolder);
                }
                catch (RemotePcException e) when (e.Kind == RemoteErrorKind.Offline)
                {
                    Finish(job, "queued", "Waiting for " + Pc.DisplayName);
                    await TickAsync(ct);
                    break;
                }
                catch (RemotePcException e)
                {
                    Finish(job, "failed", e.Message);
                }
            }
        }
        finally { _uploadGate.Release(); }
    }

    /// <summary>Size of each piece sent (tests use small ones).</summary>
    internal int ChunkSize { get; set; } = 8 * 1024 * 1024;

    private void Finish(UploadJob job, string state, string? error)
    {
        lock (_jobsLock)
        {
            var j = _jobs.FirstOrDefault(x => x.Id == job.Id) ?? job;
            j.State = state;
            j.Error = error;
            if (state == "done") j.SentBytes = j.Size;
        }
        SaveJobs();
    }
}
