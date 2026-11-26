using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace CloudSSD.App;

/// <summary>Why one file of a task did not arrive: network, space, missing, cancelled, denied or other.</summary>
public sealed record TransferFailure(string Name, string Reason);

/// <summary>
/// One transfer as people think of it: an offer, a folder, or one "Send to PC" with many files, not each file alone.
/// The same fields as the phone's tasks, so both sides draw it the same way. Its properties tell when they change
/// (a window can bind to them); they change on the thread that moved the bytes.
/// </summary>
public sealed class TransferTask : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private string _state = "waiting", _current = "", _device = "", _link = "wifi", _saveTo = "", _openPath = "";
    private int _files, _doneFiles;
    private long _bytes, _doneBytes, _speed;
    private IReadOnlyList<TransferFailure> _failed = [];
    private DateTimeOffset? _ended;
    private bool _canRetry, _canCancel = true;

    public string Id { get; init; } = "";
    /// <summary>toPc, toPhone (this PC to a phone or tablet), toDevice (to a laptop or another PC, or passed on), fromDevice.</summary>
    public string Dir { get; init; } = "toPc";
    /// <summary>files or folder.</summary>
    public string Kind { get; init; } = "files";
    /// <summary>The folder sent, at its top ("MyApp"), when Kind is folder.</summary>
    public string Folder { get; init; } = "";
    public DateTimeOffset Started { get; init; }

    /// <summary>The other side's name.</summary>
    public string Device { get => _device; set => Set(ref _device, value); }
    /// <summary>wifi, usb, hotspot, tailscale or relay.</summary>
    public string Link { get => _link; set => Set(ref _link, value); }
    /// <summary>waiting, running, paused, done, partial, failed or cancelled.</summary>
    public string State { get => _state; set => Set(ref _state, value); }
    public int Files { get => _files; set => Set(ref _files, value); }
    public int DoneFiles { get => _doneFiles; set => Set(ref _doneFiles, value); }
    public long Bytes { get => _bytes; set => Set(ref _bytes, value); }
    public long DoneBytes { get => _doneBytes; set => Set(ref _doneBytes, value); }
    /// <summary>Bytes per second now (0 while nothing moves).</summary>
    public long Speed { get => _speed; set => Set(ref _speed, value); }
    /// <summary>The file moving now.</summary>
    public string Current { get => _current; set => Set(ref _current, value); }
    private string _name = "";
    /// <summary>The first file's name (what a one-file task is called).</summary>
    public string Name { get => _name; set => Set(ref _name, value); }
    public IReadOnlyList<TransferFailure> Failed { get => _failed; set => Set(ref _failed, value); }
    /// <summary>Where it is saved, for people ("This PC › Downloads › Cloud-SSD › Received").</summary>
    public string SaveTo { get => _saveTo; set => Set(ref _saveTo, value); }
    /// <summary>A real folder to open ("" when it is on another device).</summary>
    public string OpenPath { get => _openPath; set => Set(ref _openPath, value); }
    public DateTimeOffset? Ended { get => _ended; set => Set(ref _ended, value); }
    public bool CanRetry { get => _canRetry; set => Set(ref _canRetry, value); }
    public bool CanCancel { get => _canCancel; set => Set(ref _canCancel, value); }

    private string _onClash = "keep";
    /// <summary>What to do when a name is taken where it is saved: keep (both), replace or skip. Chosen when it was accepted.</summary>
    public string OnClash { get => _onClash; set => Set(ref _onClash, value); }

    /// <summary>Not finished: asking (waits for an OK here), waiting, running or paused.</summary>
    [JsonIgnore] public bool IsOpen => State is "asking" or "waiting" or "running" or "paused";
    /// <summary>The device on the other side (or the sender of a passed-on offer); not shown.</summary>
    [JsonIgnore] public string DeviceId { get; init; } = "";
    /// <summary>The offer behind it, when there is one.</summary>
    [JsonIgnore] public string OfferId { get; init; } = "";

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

/// <summary>
/// The transfers of this PC (or laptop) as tasks: started by the code that moves the files, fed as bytes and files go,
/// finished when every file has arrived or failed. The newest 60 are kept (open ones always).
/// </summary>
public sealed class TransferTasks
{
    private const int Keep = 60;
    private readonly object _lock = new();
    private readonly List<TransferTask> _tasks = new();
    private readonly Dictionary<string, Dictionary<string, long>> _fileBytes = new();
    private readonly Dictionary<string, HashSet<string>> _doneFiles = new();
    private readonly Dictionary<string, (DateTimeOffset At, long Bytes)> _sample = new();
    private readonly Dictionary<string, DateTimeOffset> _lastRaise = new();
    private readonly Func<DateTimeOffset> _now;

    public TransferTasks(Func<DateTimeOffset>? now = null) => _now = now ?? (() => DateTimeOffset.Now);

    /// <summary>A task changed (new, progress at most 4 times a second, a file done or failed, finished).</summary>
    public event Action<TransferTask>? Changed;

    /// <summary>Asked to try the failed files of a task again; returns the new task (or null when it cannot).</summary>
    public Func<TransferTask, TransferTask?>? RetryHandler { get; set; }
    /// <summary>Asked to stop a task; true when it was stopped.</summary>
    public Func<TransferTask, bool>? CancelHandler { get; set; }

    /// <summary>Newest first.</summary>
    public IReadOnlyList<TransferTask> All { get { lock (_lock) return _tasks.ToList(); } }

    public TransferTask? Get(string id) { lock (_lock) return _tasks.FirstOrDefault(t => t.Id == id); }

    /// <summary>The task with this id, made when it is new (later files of the same task join it).</summary>
    public TransferTask Begin(string id, string dir, string device, string link, int files, long bytes, string saveTo = "", string openPath = "",
        string kind = "files", string folder = "", string deviceId = "", string offerId = "")
    {
        TransferTask t;
        bool isNew = false;
        lock (_lock)
        {
            t = _tasks.FirstOrDefault(x => x.Id == id)!;
            if (t is null)
            {
                t = new TransferTask
                {
                    Id = id, Dir = dir, Kind = kind, Folder = folder, Started = _now(), DeviceId = deviceId, OfferId = offerId,
                    Device = device, Link = link, Files = files, Bytes = bytes, SaveTo = saveTo, OpenPath = openPath
                };
                _tasks.Insert(0, t);
                _fileBytes[id] = new();
                _doneFiles[id] = new();
                isNew = true;
                Trim();
            }
            else
            {
                // Told more about it (a later file says how many there are).
                if (files > t.Files) t.Files = files;
                if (bytes > t.Bytes) t.Bytes = bytes;
                if (t.State is "done" or "partial" or "failed" or "cancelled")
                {
                    // It goes on (a cut-off send that came back): what was only counted as "never came" is open again.
                    t.State = "running"; t.Ended = null; t.CanCancel = true; t.CanRetry = false;
                    t.Failed = t.Failed.Where(f => f.Name.Length > 0).ToList();
                }
            }
        }
        if (isNew) Raise(t, force: true);
        return t;
    }

    /// <summary>One file of the task has <paramref name="have"/> bytes now (counted once, however often it is told).</summary>
    public void Progress(string id, string file, string name, long have)
    {
        TransferTask? t;
        bool force = false;
        lock (_lock)
        {
            t = _tasks.FirstOrDefault(x => x.Id == id);
            if (t is null || !_fileBytes.TryGetValue(id, out var files) || _doneFiles[id].Contains(file)) return;
            files[file] = Math.Max(0, have);
            if (t.State is "waiting" or "paused") { t.State = "running"; force = true; }
            t.Current = name;
            if (t.Name.Length == 0) t.Name = name;
            t.DoneBytes = files.Values.Sum();
            Sample(t);
        }
        Raise(t, force);
    }

    /// <summary>One file arrived whole.</summary>
    public void FileDone(string id, string file, string name, long size)
    {
        TransferTask? t;
        lock (_lock)
        {
            t = _tasks.FirstOrDefault(x => x.Id == id);
            if (t is null || !_doneFiles[id].Add(file)) return;
            _fileBytes[id][file] = size;
            t.DoneBytes = _fileBytes[id].Values.Sum();
            t.DoneFiles = _doneFiles[id].Count(f => !f.StartsWith('\u0001'));
            if (t.State is "waiting" or "paused") t.State = "running";
            t.Current = name;
            if (t.Name.Length == 0) t.Name = name;
            if (t.DoneFiles > t.Files) t.Files = t.DoneFiles;
            if (t.DoneFiles + t.Failed.Count >= t.Files && t.Files > 0) FinishLocked(t);
        }
        Raise(t, force: true);
    }

    /// <summary>One file did not arrive (network, space, missing, cancelled, denied, other).</summary>
    public void FileFailed(string id, string file, string name, string reason)
    {
        TransferTask? t;
        lock (_lock)
        {
            t = _tasks.FirstOrDefault(x => x.Id == id);
            if (t is null || !_doneFiles[id].Add("\u0001" + file)) return;
            _fileBytes[id].Remove(file);
            t.DoneBytes = _fileBytes[id].Values.Sum();
            if (t.Name.Length == 0) t.Name = name;
            t.Failed = t.Failed.Append(new TransferFailure(name, NormalizeReason(reason))).ToList();
            if (t.DoneFiles + t.Failed.Count >= t.Files && t.Files > 0) FinishLocked(t);
        }
        Raise(t, force: true);
    }

    /// <summary>The task ends now (an offer declined or expired, a batch that will not get more files): what did not arrive counts as failed.</summary>
    public void Finish(string id, string? stateIfNothing = null, string reasonForTheRest = "other")
    {
        TransferTask? t;
        lock (_lock)
        {
            t = _tasks.FirstOrDefault(x => x.Id == id);
            if (t is null || !t.IsOpen) return;
            if (stateIfNothing != null && t.DoneFiles == 0) { t.State = stateIfNothing; t.Ended = _now(); t.Speed = 0; t.CanCancel = false; t.CanRetry = stateIfNothing != "cancelled"; }
            else FinishLocked(t, reasonForTheRest);
        }
        Raise(t, force: true);
    }

    /// <summary>Pauses (for example when the other side went away); a later byte goes on.</summary>
    public void Pause(string id)
    {
        TransferTask? t;
        lock (_lock)
        {
            t = _tasks.FirstOrDefault(x => x.Id == id);
            if (t is null || t.State != "running") return;
            t.State = "paused";
            t.Speed = 0;
        }
        Raise(t, force: true);
    }

    private readonly List<(string Prefix, Func<TransferTask, TransferTask?>? Retry, Func<TransferTask, bool>? Cancel)> _handlers = new();

    /// <summary>
    /// Retry and Cancel for the tasks whose id starts with <paramref name="prefix"/> ("pf-" for phone folders, "up-"
    /// for uploads…), so each part of the app handles its own; the longest matching prefix wins, else the defaults.
    /// </summary>
    public void Handle(string prefix, Func<TransferTask, TransferTask?>? retry = null, Func<TransferTask, bool>? cancel = null)
    {
        lock (_lock)
        {
            _handlers.RemoveAll(h => h.Prefix == prefix);
            _handlers.Add((prefix, retry, cancel));
        }
    }

    private (Func<TransferTask, TransferTask?>? Retry, Func<TransferTask, bool>? Cancel) HandlersFor(string id)
    {
        lock (_lock)
        {
            var h = _handlers.Where(x => id.StartsWith(x.Prefix, StringComparison.Ordinal)).OrderByDescending(x => x.Prefix.Length).FirstOrDefault();
            return (h.Retry ?? RetryHandler, h.Cancel ?? CancelHandler);
        }
    }

    public TransferTask? Retry(string id) => Get(id) is { CanRetry: true } t ? HandlersFor(id).Retry?.Invoke(t) : null;

    // ---- files that wait for an OK ("Files to receive": from a guest, or from anyone when the setting says always ask) ----

    private readonly List<(string Prefix, Func<TransferTask, string, bool>? Accept, Func<TransferTask, bool>? Decline)> _answers = new();

    /// <summary>
    /// What accepting or declining does for the asking tasks whose id starts with <paramref name="prefix"/>: accept gets
    /// the task and the name-clash choice and returns false when it can no longer be taken; decline tells the sender.
    /// </summary>
    public void HandleAsking(string prefix, Func<TransferTask, string, bool>? accept = null, Func<TransferTask, bool>? decline = null)
    {
        lock (_lock)
        {
            _answers.RemoveAll(h => h.Prefix == prefix);
            _answers.Add((prefix, accept, decline));
        }
    }

    private (Func<TransferTask, string, bool>? Accept, Func<TransferTask, bool>? Decline) AnswersFor(string id)
    {
        lock (_lock)
        {
            var h = _answers.Where(x => id.StartsWith(x.Prefix, StringComparison.Ordinal)).OrderByDescending(x => x.Prefix.Length).FirstOrDefault();
            return (h.Accept, h.Decline);
        }
    }

    /// <summary>A task that waits for an OK here before anything moves (same arguments as <see cref="Begin"/>).</summary>
    public TransferTask BeginAsking(string id, string dir, string device, string link, int files, long bytes, string saveTo = "", string openPath = "",
        string kind = "files", string folder = "", string deviceId = "", string offerId = "", string name = "")
    {
        var t = Begin(id, dir, device, link, files, bytes, saveTo, openPath, kind, folder, deviceId, offerId);
        lock (_lock)
        {
            if (t.State != "waiting" || t.DoneFiles > 0) return t;   // already moving (asked twice)
            t.State = "asking";
            t.CanCancel = false;
            if (name.Length > 0) t.Name = name;
        }
        Raise(t, force: true);
        return t;
    }

    /// <summary>Says yes to an asking task; onClash: keep (both), replace or skip. False when it is not asking (any more).</summary>
    public bool Accept(string id, string onClash = "keep")
    {
        var t = Get(id);
        if (t is null || t.State != "asking") return false;
        onClash = onClash is "replace" or "skip" ? onClash : "keep";
        if (AnswersFor(id).Accept is { } h && !h(t, onClash)) return false;
        lock (_lock)
        {
            if (t.State != "asking") return false;
            t.OnClash = onClash;
            t.State = "waiting";
            t.CanCancel = true;
        }
        Raise(t, force: true);
        return true;
    }

    /// <summary>Says no to an asking task: nothing is saved and the sender is told. False when it is not asking.</summary>
    public bool Decline(string id)
    {
        var t = Get(id);
        if (t is null || t.State != "asking") return false;
        if (AnswersFor(id).Decline is { } h && !h(t)) return false;
        Finish(id, "cancelled", "cancelled");
        return true;
    }

    public bool Cancel(string id)
    {
        var t = Get(id);
        if (t is null || !t.IsOpen) return false;
        if (HandlersFor(id).Cancel is { } h && !h(t)) return false;
        Finish(id, "cancelled", "cancelled");
        return true;
    }

    /// <summary>Open tasks that have not moved for <paramref name="idle"/> end (a batch without a count, a device that went away).</summary>
    public void Sweep(TimeSpan idle) => Sweep(idle, "");

    /// <summary>The same for the tasks whose id starts with <paramref name="prefix"/> only (each part sweeps its own).</summary>
    public void Sweep(TimeSpan idle, string prefix)
    {
        List<TransferTask> stale;
        lock (_lock)
            stale = _tasks.Where(t => t.Id.StartsWith(prefix, StringComparison.Ordinal) && t.IsOpen && t.State is not ("waiting" or "asking") && _now() - (_sample.TryGetValue(t.Id, out var s) ? s.At : t.Started) > idle).ToList();
        foreach (var t in stale) Finish(t.Id, reasonForTheRest: "network");
    }

    private void FinishLocked(TransferTask t, string reasonForTheRest = "other")
    {
        // What is neither done nor failed yet will not come now.
        int missing = t.Files - t.DoneFiles - t.Failed.Count;
        if (missing > 0 && t.Files > 0)
            t.Failed = t.Failed.Concat(Enumerable.Range(0, missing).Select(_ => new TransferFailure("", NormalizeReason(reasonForTheRest)))).ToList();
        t.State = t.Failed.Count == 0 ? "done" : t.DoneFiles == 0 ? (reasonForTheRest == "cancelled" ? "cancelled" : "failed") : "partial";
        t.Ended = _now();
        t.Speed = 0;
        t.Current = "";
        t.CanCancel = false;
        t.CanRetry = t.State is "partial" or "failed";
    }

    private void Sample(TransferTask t)
    {
        var now = _now();
        if (!_sample.TryGetValue(t.Id, out var s)) { _sample[t.Id] = (now, t.DoneBytes); return; }
        var secs = (now - s.At).TotalSeconds;
        if (secs < 0.5) return;
        var rate = Math.Max(0, (t.DoneBytes - s.Bytes) / secs);
        t.Speed = t.Speed == 0 ? (long)rate : (long)(t.Speed * 0.5 + rate * 0.5);
        _sample[t.Id] = (now, t.DoneBytes);
    }

    private void Raise(TransferTask t, bool force = false)
    {
        var now = _now();
        lock (_lock)
        {
            if (!force && _lastRaise.TryGetValue(t.Id, out var last) && (now - last).TotalMilliseconds < 250) return;
            _lastRaise[t.Id] = now;
        }
        try { Changed?.Invoke(t); } catch { /* a listener's trouble is not the transfer's */ }
    }

    private void Trim()
    {
        while (_tasks.Count > Keep && _tasks.LastOrDefault(t => !t.IsOpen) is { } old)
        {
            _tasks.Remove(old);
            _fileBytes.Remove(old.Id);
            _doneFiles.Remove(old.Id);
            _sample.Remove(old.Id);
            _lastRaise.Remove(old.Id);
        }
    }

    internal static string NormalizeReason(string? reason) => reason?.ToLowerInvariant() switch
    {
        "network" or "space" or "missing" or "cancelled" or "denied" => reason.ToLowerInvariant(),
        _ => "other",
    };

    /// <summary>The model's link from how a device reached this PC (LinkKinds): wifi, usb, hotspot or tailscale.</summary>
    public static string LinkOf(string? lastLink) => lastLink switch
    {
        LinkKinds.Usb or LinkKinds.UsbAdb => "usb",
        LinkKinds.PcHotspot or LinkKinds.PhoneHotspot => "hotspot",
        LinkKinds.Internet => "tailscale",
        _ => "wifi",
    };
}
