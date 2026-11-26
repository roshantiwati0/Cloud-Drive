using System.Collections.Concurrent;
using System.Diagnostics;
using CloudSSD.Core;
using CloudSSD.Mount;
using Microsoft.Extensions.Logging;

namespace CloudSSD.App.Hybrid;

/// <summary>One item that lives on this PC in the fast drive, and where its cloud copy stands.</summary>
public sealed record HybridItem(string Name, string State, DateTimeOffset? UploadedAt, string? Error);

/// <summary>What the fast drive is doing right now, for the page and the terminal.</summary>
public sealed record HybridStatus(bool Enabled, string Letter, string Root, bool LetterReady, string? CloudRoot, int Links,
    IReadOnlyList<HybridItem> Items, string Message)
{
    public int WaitingForUpload => Items.Count(i => i.State != "uploaded");
}

/// <summary>
/// The "fast drive": the drive letter is a folder on this PC, so making files and folders is as fast as the disk.
/// The service keeps three things going while it is switched on:
///   1. the letter points at the folder (again after every sign-in),
///   2. every item at the top of the cloud drive has a link in the folder (<see cref="HybridLayout"/>),
///   3. what was made on this PC is copied to the cloud once it has been quiet for a moment, by the cloud drive's
///      own rclone (so one login, one request limit), and copied again when it changes.
/// It never deletes anything on this PC or in the cloud. Making an uploaded item "cloud only" is not part of it yet.
/// </summary>
public sealed class HybridDriveService : IDisposable
{
    private readonly AppHost _host;
    private readonly ILogger _log;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ConcurrentDictionary<string, Track> _tracks = new(StringComparer.OrdinalIgnoreCase);
    private HybridState _state = new();
    private Timer? _timer;
    private FileSystemWatcher? _watcher;
    private Task? _upload;
    private bool _loaded;

    private sealed class Track
    {
        public DateTime LastChange = DateTime.UtcNow;
        public volatile bool Dirty = true;
        public volatile bool Uploading;
        public DateTime NotBefore = DateTime.MinValue;
        public string? Error;
    }

    public HybridDriveService(AppHost host, ILogger log)
    {
        _host = host;
        _log = log;
        Upload = RcUploadAsync;
    }

    private HybridDriveSettings Settings => _host.Settings.Hybrid;
    private string StateFile => Path.Combine(_host.Paths.DataRoot, "hybrid-state.json");

    /// <summary>Copies one local item (path, name, is it a folder) to the cloud; true when all of it arrived. Replaced in tests.</summary>
    internal Func<string, string, bool, CancellationToken, Task<bool>> Upload { get; set; }

    public HybridStatus Status { get; private set; } = new(false, "Z:", "", false, null, 0, [], "Off");
    public event Action? Changed;

    private static char LetterOf(string? text, char fallback) =>
        !string.IsNullOrWhiteSpace(text) && char.IsLetter(text.Trim()[0]) ? char.ToUpperInvariant(text.Trim()[0]) : fallback;

    /// <summary>
    /// Takes the letter for the fast drive before the cloud drives connect, so a cloud drive looking for a free letter
    /// does not sit on it. Does nothing while the fast drive is switched off.
    /// </summary>
    public void ClaimLetter()
    {
        if (!Settings.Enabled) return;
        try { TakeLetter(Settings.RootOrDefault); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _log.LogWarning("Fast drive: could not take its letter: {Error}", e.Message); }
    }

    private char? _fallback;

    /// <summary>
    /// The letter the fast drive has right now: the one from the settings when it is free or already ours. When that
    /// letter is held by something else (seen for real: Windows kept a dead drive name on Z: until the next restart),
    /// another free letter serves until the next start, when the wanted one is tried first again. Null = no letter yet
    /// (the cloud drive still sits on the wanted one and is about to leave it).
    /// </summary>
    private char? TakeLetter(string root)
    {
        var wanted = LetterOf(Settings.Letter, 'Z');
        if (DosDevice.Define(wanted, root))
        {
            if (_fallback is { } old && old != wanted && DosDevice.Remove(old, root)) DosDevice.SetLabel(old, null);
            _fallback = null;
            DosDevice.SetLabel(wanted, "Cloud SSD");
            return wanted;
        }
        if (CloudDriveHoldsLetter) return null;
        if (_fallback is { } f && DosDevice.PointsAt(f, root)) return f;
        var backing = LetterOf(Settings.BackingLetter, 'X');
        const string candidates = "WVUTSRQPONMLKJIHYX";
        // A letter an earlier run of the app already put on the folder is used again (the letter outlives the app);
        // without this every start of the app took one more letter.
        foreach (var c in candidates)
        {
            if (!DosDevice.PointsAt(c, root)) continue;
            _fallback = c;
            return c;
        }
        foreach (var c in candidates)
        {
            if (c == wanted || c == backing || DosDevice.TargetOf(c) != null || !DosDevice.Define(c, root)) continue;
            _fallback = c;
            DosDevice.SetLabel(c, "Cloud SSD");
            _log.LogWarning("Fast drive: {Wanted}: is held by something else, using {Letter}: until the next start", wanted, c);
            return c;
        }
        return null;
    }

    /// <summary>Starts the regular pass (links, uploads). Safe to call again.</summary>
    public void Start()
    {
        if (!Settings.Enabled || _timer != null) return;
        _timer = new Timer(_ => _ = TickAsync(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10));
    }

    /// <summary>
    /// Switches the fast drive on. The cloud drive behind it uses its own letter from its next connect on; when it sits
    /// on the fast drive's letter right now, <see cref="MoveCloudDriveAsync"/> has to free that letter first.
    /// </summary>
    public async Task EnableAsync()
    {
        var s = Settings;
        s.Enabled = true;
        var def = _host.Settings.Mounts.FirstOrDefault(m => m.Id == s.MountId);
        if (def != null && LetterOf(def.DriveLetter, '?') == LetterOf(s.Letter, 'Z')) def.DriveLetter = LetterOf(s.BackingLetter, 'X').ToString();
        _host.SaveSettings();
        _log.LogInformation("Fast drive switched on: {Letter}: is the folder {Root}", LetterOf(s.Letter, 'Z'), s.RootOrDefault);
        Start();
        await TickAsync();
    }

    /// <summary>
    /// Switches the fast drive off: the letter is released and the cloud drive gets it back at its next connect.
    /// Refused while something on this PC has not reached the cloud yet (it would vanish from the drive letter).
    /// The folder on this PC, with everything in it, stays where it is.
    /// </summary>
    public bool Disable(out string message)
    {
        var waiting = Status.Items.Where(i => i.State != "uploaded").Select(i => i.Name).ToList();
        if (Settings.Enabled && waiting.Count > 0)
        {
            message = $"{waiting.Count} item(s) are only on this PC so far ({string.Join(", ", waiting.Take(3))}{(waiting.Count > 3 ? ", …" : "")}). Wait for their upload, then switch off.";
            return false;
        }
        var s = Settings;
        s.Enabled = false;
        _timer?.Dispose(); _timer = null;
        _watcher?.Dispose(); _watcher = null;
        var letter = LetterOf(s.Letter, 'Z');
        foreach (var c in new[] { letter, _fallback ?? letter }.Distinct())
            if (DosDevice.Remove(c, s.RootOrDefault)) DosDevice.SetLabel(c, null);
        _fallback = null;
        var def = _host.Settings.Mounts.FirstOrDefault(m => m.Id == s.MountId);
        if (def != null) def.DriveLetter = letter.ToString();
        _host.SaveSettings();
        Status = Status with { Enabled = false, LetterReady = false, Message = "Off" };
        Changed?.Invoke();
        _log.LogInformation("Fast drive switched off; its folder {Root} is kept", s.RootOrDefault);
        message = "The fast drive is off. The cloud drive gets its letter back when it reconnects.";
        return true;
    }

    /// <summary>True while the cloud drive itself still sits on the letter the fast drive needs.</summary>
    public bool CloudDriveHoldsLetter =>
        _host.Mounts.Get(Settings.MountId) is { } s && s.DriveLetter == LetterOf(Settings.Letter, 'Z');

    /// <summary>Programs running from a drive (best effort). Disconnecting a drive under a running program left both stuck in Windows (17 Nov 2026).</summary>
    public static IReadOnlyList<string> ProgramsRunningFrom(string root)
    {
        var names = new List<string>();
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.MainModule?.FileName is { } file && file.StartsWith(root, StringComparison.OrdinalIgnoreCase)) names.Add(p.ProcessName);
            }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException) { /* not ours to look at */ }
            finally { p.Dispose(); }
        }
        return names.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Reconnects the cloud drive on its own letter so the fast drive can have the letter people use. Refused while a
    /// program runs from the drive or uploads are unfinished: both made the reconnect go wrong when tried for real.
    /// </summary>
    public async Task<string?> MoveCloudDriveAsync()
    {
        var s = Settings;
        if (_host.Mounts.Get(s.MountId) is not { } session || !CloudDriveHoldsLetter) return null;
        var running = ProgramsRunningFrom(session.Root);
        if (running.Count > 0) return $"Close these first, they run from {session.DriveLetter}: — {string.Join(", ", running)}.";
        if (session.Rc != null)
        {
            try
            {
                var v = VfsStats.Parse(await session.Rc.CallAsync("vfs/stats"));
                if (v.UploadsInProgress + v.UploadsQueued > 0)
                    return $"{v.UploadsInProgress + v.UploadsQueued} file(s) are still uploading from {session.DriveLetter}:. Try again when they are done.";
            }
            catch (Exception e) when (e is HttpRequestException or RcException or TaskCanceledException) { /* cannot tell: go on */ }
        }
        var def = session.Definition;
        def.DriveLetter = LetterOf(s.BackingLetter, 'X').ToString();
        await _host.UnmountAsync(def.Id);
        await _host.MountAsync(def);
        _host.SaveSettings();
        await TickAsync();
        return null;
    }

    /// <summary>Shows a cloud item again whose link was deleted in the fast drive.</summary>
    public void Unhide(string name)
    {
        if (HybridLayout.Unhide(_state, name)) SaveState();
    }

    public IReadOnlyCollection<string> HiddenItems => _state.Hidden;

    /// <summary>One pass: letter, links, uploads. Runs every 10 s while on; public so the page and the tests can ask for one now.</summary>
    public async Task TickAsync()
    {
        if (!Settings.Enabled) return;
        if (!await _gate.WaitAsync(0)) return;
        try
        {
            var s = Settings;
            var root = s.RootOrDefault;
            var wanted = LetterOf(s.Letter, 'Z');
            if (!_loaded) { _state = HybridState.Load(StateFile); _loaded = true; }
            Directory.CreateDirectory(root);
            var taken = TakeLetter(root);
            var letterReady = taken != null;
            var letter = taken ?? wanted;
            EnsureWatcher(root);

            var session = _host.Mounts.Get(s.MountId);
            var cloudRoot = session is { State: MountState.Mounted } && session.DriveLetter != letter ? session.Root : null;
            // Without the cloud drive the pass still lists what is on this PC; the links wait.
            var layout = HybridLayout.Reconcile(root, cloudRoot ?? "", _state);
            if (layout.Changed) SaveState();
            if (layout.LinksMade + layout.LinksFixed + layout.LinksRemoved > 0)
                _log.LogInformation("Fast drive: {Made} link(s) made, {Fixed} re-pointed, {Removed} removed", layout.LinksMade, layout.LinksFixed, layout.LinksRemoved);
            if (layout.NewlyHidden.Count > 0)
                _log.LogInformation("Fast drive: {Count} link(s) were deleted here; those cloud items are hidden from the fast drive and stay in the cloud", layout.NewlyHidden.Count);
            if (layout.Failed.Count > 0) _log.LogWarning("Fast drive: {Count} item(s) could not be linked (first: {First})", layout.Failed.Count, layout.Failed[0]);

            foreach (var gone in _tracks.Keys.Except(layout.LocalItems, StringComparer.OrdinalIgnoreCase).ToList())
                if (_tracks.TryRemove(gone, out _) && _state.Uploaded.Remove(gone)) SaveState();
            foreach (var name in layout.LocalItems) _tracks.TryAdd(name, new Track());

            if (cloudRoot != null && session?.Rc != null) StartNextUpload(root, s.QuietSeconds);

            var items = layout.LocalItems.Select(n =>
            {
                var t = _tracks.GetValueOrDefault(n);
                var state = t is null ? "waiting" : t.Uploading ? "uploading" : t.Dirty ? (t.Error != null ? "failed" : "waiting") : "uploaded";
                return new HybridItem(n, state, _state.Uploaded.TryGetValue(n, out var at) ? at : null, t?.Error);
            }).ToList();
            var ready = letter == wanted ? $"{letter}: is ready." : $"{wanted}: is held by something else until the next restart, so the fast drive is {letter}: for now.";
            var message =
                !letterReady && CloudDriveHoldsLetter ? $"The cloud drive is still on {letter}:. It moves to its own letter when it reconnects; then {letter}: becomes the fast drive." :
                !letterReady ? $"No free drive letter right now. The fast drive's folder is {root}." :
                cloudRoot is null ? $"{ready} The cloud drive is not connected, so the cloud folders and uploads wait." :
                items.Any(i => i.State == "uploading") ? $"{ready} Uploading {items.First(i => i.State == "uploading").Name}…" :
                items.Any(i => i.State != "uploaded") ? $"{ready} {items.Count(i => i.State != "uploaded")} item(s) on this PC wait for their upload." :
                $"{ready} Everything made here is in the cloud too.";
            var status = new HybridStatus(true, $"{letter}:", root, letterReady, cloudRoot, _state.Links.Count, items, message);
            var changed = status.Message != Status.Message || status.Links != Status.Links || !status.Items.SequenceEqual(Status.Items);
            Status = status;
            if (changed) Changed?.Invoke();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _log.LogWarning("Fast drive: this pass failed: {Error}", e.Message);
        }
        finally { _gate.Release(); }
    }

    private void SaveState()
    {
        try { _state.Save(StateFile); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _log.LogWarning("Fast drive: could not save its state: {Error}", e.Message); }
    }

    private void EnsureWatcher(string root)
    {
        if (_watcher != null && string.Equals(_watcher.Path, root, StringComparison.OrdinalIgnoreCase)) return;
        _watcher?.Dispose();
        var w = new FileSystemWatcher(root)
        {
            IncludeSubdirectories = true,
            InternalBufferSize = 64 * 1024,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
        };
        void Touch(string? relative)
        {
            if (string.IsNullOrEmpty(relative)) return;
            var top = relative.Split('\\', 2)[0];
            if (_tracks.TryGetValue(top, out var t)) { t.LastChange = DateTime.UtcNow; t.Dirty = true; }
        }
        w.Created += (_, e) => Touch(e.Name);
        w.Changed += (_, e) => Touch(e.Name);
        w.Deleted += (_, e) => Touch(e.Name);
        w.Renamed += (_, e) => { Touch(e.OldName); Touch(e.Name); };
        // Too many changes at once (an installer): the list of what changed is lost, so everything counts as changed.
        w.Error += (_, _) => { foreach (var t in _tracks.Values) { t.LastChange = DateTime.UtcNow; t.Dirty = true; } };
        w.EnableRaisingEvents = true;
        _watcher = w;
    }

    /// <summary>Starts the upload of one changed item that has been quiet long enough; one upload at a time.</summary>
    private void StartNextUpload(string root, int quietSeconds)
    {
        if (_upload is { IsCompleted: false }) return;
        var now = DateTime.UtcNow;
        var next = _tracks.Where(kv => kv.Value.Dirty && !kv.Value.Uploading && now >= kv.Value.NotBefore &&
                                       now - kv.Value.LastChange >= TimeSpan.FromSeconds(Math.Max(1, quietSeconds)))
            .OrderBy(kv => kv.Value.LastChange).Select(kv => kv.Key).FirstOrDefault();
        if (next is null) return;
        var track = _tracks[next];
        var path = Path.Combine(root, next);
        var isDir = Directory.Exists(path);
        if (!isDir && !File.Exists(path)) return;
        track.Uploading = true;
        track.Error = null;
        var started = DateTime.UtcNow;
        _upload = Task.Run(async () =>
        {
            var ok = false;
            try
            {
                _log.LogInformation("Fast drive: uploading a {Kind} made on this PC", isDir ? "folder" : "file");
                ok = await Upload(path, next, isDir, CancellationToken.None);
            }
            catch (Exception e) when (e is HttpRequestException or RcException or TaskCanceledException or IOException)
            {
                track.Error = e.Message;
            }
            if (ok && track.LastChange <= started)
            {
                track.Dirty = false;
                _state.Uploaded[next] = DateTimeOffset.Now;
                SaveState();
                _log.LogInformation("Fast drive: upload finished in {Seconds:0} s", (DateTime.UtcNow - started).TotalSeconds);
            }
            else if (!ok)
            {
                track.Error ??= "The upload did not finish.";
                track.NotBefore = DateTime.UtcNow.AddMinutes(1); // try again, not in a tight loop
                _log.LogWarning("Fast drive: an upload did not finish; trying again in a minute");
            }
            track.Uploading = false;
            _ = TickAsync();
        });
    }

    /// <summary>
    /// The real upload: the cloud drive's own rclone copies the item ("sync/copy" never deletes on either side; files
    /// that are already the same are skipped, so running it again after a change only sends what changed).
    /// </summary>
    private async Task<bool> RcUploadAsync(string path, string name, bool isDir, CancellationToken ct)
    {
        if (_host.Mounts.Get(Settings.MountId) is not { Rc: { } rc } session) return false;
        var remote = session.Definition.RemoteSpec;
        var job = isDir
            ? await rc.CallAsync("sync/copy", new Dictionary<string, object>
            {
                ["srcFs"] = path, ["dstFs"] = remote + name, ["createEmptySrcDirs"] = true, ["_async"] = true,
            }, ct)
            : await rc.CallAsync("operations/copyfile", new Dictionary<string, object>
            {
                ["srcFs"] = Path.GetDirectoryName(path)!, ["srcRemote"] = name, ["dstFs"] = remote, ["dstRemote"] = name, ["_async"] = true,
            }, ct);
        var id = job["jobid"]!.GetValue<long>();
        while (true)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), ct);
            var st = await rc.CallAsync("job/status", new Dictionary<string, object> { ["jobid"] = id }, ct);
            if (st["finished"]?.GetValue<bool>() != true) continue;
            var ok = st["success"]?.GetValue<bool>() == true;
            if (!ok) _log.LogWarning("Fast drive: rclone reported an upload error ({Kind})", Diagnostics.Redactor.Redact(st["error"]?.GetValue<string>() ?? ""));
            // The cloud drive listed its top folder before this item existed there: let it look again.
            try { await rc.CallAsync("vfs/refresh", null, ct); }
            catch (Exception e) when (e is HttpRequestException or RcException or TaskCanceledException) { /* it notices by itself within a minute */ }
            return ok;
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _watcher?.Dispose();
    }
}
