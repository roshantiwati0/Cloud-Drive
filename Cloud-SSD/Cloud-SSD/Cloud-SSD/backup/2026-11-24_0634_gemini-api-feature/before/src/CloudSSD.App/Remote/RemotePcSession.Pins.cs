using Microsoft.Extensions.Logging;

namespace CloudSSD.App.Remote;

/// <summary>
/// "Offline rakho": folders (or single files) of that PC kept on this one, so they open while it is away. Each is a
/// read-only mirror under remote\&lt;pcId&gt;\pinned, refreshed when that PC is reachable: new and changed files are
/// fetched (resuming), files gone there go here too, and every folder's listing is saved so the whole tree can be
/// browsed offline, not just the folders that were opened.
/// </summary>
public sealed partial class RemotePcSession
{
    /// <summary>Space always left free on this PC's disk.</summary>
    internal static long PinReserveBytes = 5L << 30;
    /// <summary>A folder bigger than this is only kept after the user says yes.</summary>
    internal static long ConfirmAboveBytes = 2L << 30;
    /// <summary>Kept folders are checked again this often while that PC is reachable.</summary>
    internal static TimeSpan PinSyncEvery = TimeSpan.FromMinutes(5);
    private const int MaxPinnedFiles = 20000;
    private const int MaxDepth = 32;

    private readonly SemaphoreSlim _pinGate = new(1, 1);
    private DateTimeOffset _lastPinSync = DateTimeOffset.MinValue;
    private volatile bool _pinsDirty;

    /// <summary>A pin was added, removed, or finished a round (sizes, errors, "needs OK").</summary>
    public event Action? PinsChanged;

    private string PinnedDir => Path.Combine(Dir, "pinned");

    public IReadOnlyList<PinnedFolder> Pins => Pc.Pins;

    private static bool Covers(string pin, string id) =>
        string.Equals(pin, id, StringComparison.OrdinalIgnoreCase)
        || id.StartsWith(pin.EndsWith(':') ? pin : pin + "/", StringComparison.OrdinalIgnoreCase);

    /// <summary>The pin that keeps this item (itself, or a folder above it), if any.</summary>
    public PinnedFolder? PinOf(string id) => Pc.Pins.FirstOrDefault(p => Covers(p.Path, id));

    /// <summary>Keep this folder or file on this PC. It is fetched now when that PC is reachable, else when it is back.</summary>
    public void Pin(RemoteEntry e) => Pin(e.Id, !e.IsDirectory);

    public void Pin(string id, bool isFile)
    {
        SafeLocal(PinnedDir, id);                         // a name that cannot be kept fails here, before it is saved
        if (Pc.Pins.Any(p => string.Equals(p.Path, id, StringComparison.OrdinalIgnoreCase))) return;
        // Copy-on-write: the list may be saved to disk on another thread at the same moment.
        Pc.Pins = Pc.Pins.Append(new PinnedFolder { Path = id, IsFile = isFile, AddedAt = _now() }).ToList();
        SaveMeta();
        RaisePinsChanged();
        SyncPinsSoon(force: true);
    }

    /// <summary>Stop keeping it: the copies go (unless a folder above it is still kept).</summary>
    public void Unpin(string id)
    {
        var pin = Pc.Pins.FirstOrDefault(p => string.Equals(p.Path, id, StringComparison.OrdinalIgnoreCase));
        if (pin is null) return;
        Pc.Pins = Pc.Pins.Where(p => p != pin).ToList();
        SaveMeta();
        if (PinOf(id) is null)
        {
            // A folder inside it that is kept by itself stays.
            var keep = Pc.Pins.Where(p => Covers(id, p.Path)).Select(p => SafeLocal(PinnedDir, p.Path)).ToList();
            DeleteLocal(SafeLocal(PinnedDir, id), keep);
        }
        RaisePinsChanged();
    }

    /// <summary>The user said yes to a big folder: it is fetched now.</summary>
    public void ApprovePin(string id)
    {
        var pin = Pc.Pins.FirstOrDefault(p => string.Equals(p.Path, id, StringComparison.OrdinalIgnoreCase));
        if (pin?.NeedsOkBytes is not { } bytes) return;
        pin.ApprovedBytes = bytes;
        pin.NeedsOkBytes = null;
        SaveMeta();
        RaisePinsChanged();
        SyncPinsSoon(force: true);
    }

    private void RaisePinsChanged()
    {
        try { PinsChanged?.Invoke(); } catch (Exception e) { _log.LogDebug(e, "PinsChanged handler failed"); }
    }

    /// <summary>Starts a round in the background when one is due (or <paramref name="force"/>), and none is running.</summary>
    private void SyncPinsSoon(bool force = false)
    {
        if (force) _pinsDirty = true;
        if (!IsOnline || Pc.Pins.Count == 0) return;
        if (!_pinsDirty && _now() - _lastPinSync < PinSyncEvery) return;
        if (_pinGate.CurrentCount == 0 || _life.IsCancellationRequested) return;   // a round is running; it goes round again for what changed
        _ = Task.Run(async () =>
        {
            try { await SyncPinsAsync(_life.Token); }
            catch (Exception e) when (e is not OperationCanceledException) { _log.LogDebug(e, "Keeping folders offline failed"); }
        });
    }

    /// <summary>Brings every pin up to date (after a round already running, if any). Pins added meanwhile are included.</summary>
    public async Task SyncPinsAsync(CancellationToken ct = default)
    {
        using var both = CancellationTokenSource.CreateLinkedTokenSource(ct, _life.Token);
        ct = both.Token;
        await _pinGate.WaitAsync(ct);
        try
        {
            do
            {
                _pinsDirty = false;
                _lastPinSync = _now();
                await PinRoundAsync(ct);
            }
            while (_pinsDirty && IsOnline);
        }
        finally { _pinGate.Release(); }
    }

    private async Task PinRoundAsync(CancellationToken ct)
    {
        foreach (var pin in Pc.Pins.ToList())
        {
            if (!IsOnline || _url is null) break;
            if (!Pc.Pins.Contains(pin)) continue;      // removed meanwhile
            try
            {
                await SyncPinAsync(pin, ct);
                pin.Error = null;
            }
            catch (RemotePcException e) when (e.Kind == RemoteErrorKind.Offline) { break; }   // tried again when it is back
            catch (RemotePcException e) { pin.Error = e.Message; }
            catch (IOException e) { pin.Error = e.Message; }
            catch (UnauthorizedAccessException e) { pin.Error = e.Message; }
            SaveMeta();
            RaisePinsChanged();
        }
    }

    private async Task SyncPinAsync(PinnedFolder pin, CancellationToken ct)
    {
        var url = _url ?? throw new RemotePcException(RemoteErrorKind.Offline, "Not reachable.");
        var token = Token;
        var root = SafeLocal(PinnedDir, pin.Path);

        // 1. What is there now (and the listings, so the folders can be browsed offline).
        var files = new List<RemoteEntry>();
        if (pin.IsFile)
        {
            var parent = ParentOf(pin.Path);
            var siblings = await _client.ListAsync(url, token, parent, ct);
            SaveList(parent, Saved(parent), siblings);
            files.AddRange(siblings.Where(e => !e.IsDirectory && string.Equals(e.Id, pin.Path, StringComparison.OrdinalIgnoreCase)));
            if (files.Count == 0) throw new RemotePcException(RemoteErrorKind.NotFound, "It is no longer on that PC.");
        }
        else
        {
            var queue = new Queue<(string Path, int Depth)>();
            queue.Enqueue((pin.Path, 0));
            while (queue.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                var (folder, depth) = queue.Dequeue();
                var entries = await _client.ListAsync(url, token, folder, ct);
                SaveList(folder, Saved(folder), entries);
                foreach (var e in entries)
                {
                    if (e.IsDirectory) { if (depth + 1 < MaxDepth) queue.Enqueue((e.Id, depth + 1)); }
                    else files.Add(e);
                }
                if (files.Count > MaxPinnedFiles) throw new RemotePcException(RemoteErrorKind.Server, $"More than {MaxPinnedFiles} files: keep a smaller folder.");
            }
        }
        long total = files.Sum(f => f.Size ?? 0);

        // 2. Big folders only with an OK; never below the space kept free.
        if (total > ConfirmAboveBytes && total > pin.ApprovedBytes)
        {
            pin.NeedsOkBytes = total;
            return;
        }
        pin.NeedsOkBytes = null;
        var want = files.ToDictionary(f => SafeLocal(PinnedDir, f.Id), StringComparer.OrdinalIgnoreCase);
        long missing = want.Where(kv => !IsCurrent(kv.Key, kv.Value)).Sum(kv => kv.Value.Size ?? 0);
        if (missing > 0 && FreeSpace(PinnedDir) - missing < PinReserveBytes)
            throw new RemotePcException(RemoteErrorKind.NoSpace, $"Not enough space on this PC to keep it ({missing / (1024 * 1024)} MB more needed, {PinReserveBytes >> 30} GB is always left free).");

        // 3. Fetch what is new or changed (resuming), as read-only copies with that PC's dates.
        foreach (var (local, e) in want)
        {
            ct.ThrowIfCancellationRequested();
            if (IsCurrent(local, e)) continue;
            if (File.Exists(local)) File.SetAttributes(local, FileAttributes.Normal);
            await _client.DownloadAsync(url, token, e.Id, local, null, ct);
            if (e.Modified > 0) File.SetLastWriteTimeUtc(local, DateTimeOffset.FromUnixTimeMilliseconds(e.Modified).UtcDateTime);
            File.SetAttributes(local, FileAttributes.ReadOnly);
        }

        // 4. What went there goes here (only inside this pin, and not copies that another pin keeps).
        if (!pin.IsFile && Directory.Exists(root))
        {
            foreach (var f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                if (want.ContainsKey(f) || f.EndsWith(".cssd-part", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".etag", StringComparison.OrdinalIgnoreCase)) continue;
                File.SetAttributes(f, FileAttributes.Normal);
                File.Delete(f);
            }
            foreach (var d in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
                if (!Directory.EnumerateFileSystemEntries(d).Any()) Directory.Delete(d);
        }

        pin.Files = files.Count;
        pin.Bytes = total;
        pin.LastSyncAt = _now();
    }

    private static bool IsCurrent(string local, RemoteEntry e)
    {
        var fi = new FileInfo(local);
        if (!fi.Exists || fi.Length != (e.Size ?? -1)) return false;
        return e.Modified <= 0 || Math.Abs((fi.LastWriteTimeUtc - DateTimeOffset.FromUnixTimeMilliseconds(e.Modified).UtcDateTime).TotalSeconds) < 2;
    }

    private static string ParentOf(string id)
    {
        int slash = id.LastIndexOf('/');
        return slash > 0 ? id[..slash] : id[..(id.IndexOf(':') + 1)];
    }

    private static long FreeSpace(string dir)
    {
        try { return new DriveInfo(Path.GetPathRoot(Path.GetFullPath(dir))!).AvailableFreeSpace; }
        catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException) { return long.MaxValue; }
    }

    private static void DeleteLocal(string path, IReadOnlyList<string> keep)
    {
        bool Kept(string p) => keep.Any(k => string.Equals(p, k, StringComparison.OrdinalIgnoreCase) || p.StartsWith(k + "\\", StringComparison.OrdinalIgnoreCase));
        try
        {
            if (File.Exists(path)) { File.SetAttributes(path, FileAttributes.Normal); File.Delete(path); return; }
            if (!Directory.Exists(path)) return;
            foreach (var f in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                if (Kept(f)) continue;
                File.SetAttributes(f, FileAttributes.Normal);
                File.Delete(f);
            }
            foreach (var d in Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories).Append(path).OrderByDescending(d => d.Length))
                if (!Directory.EnumerateFileSystemEntries(d).Any()) Directory.Delete(d);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
