using System.Text.Json;

namespace CloudSSD.App;

/// <summary>A question this PC asks a device (list a folder, read a file…), picked up by the device's long-poll.</summary>
public sealed class PeerJob
{
    public required string Id { get; init; }
    public required string DeviceId { get; init; }
    /// <summary>fs.roots | fs.list | fs.read</summary>
    public required string Kind { get; init; }
    public required IReadOnlyDictionary<string, string> Args { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    internal TaskCompletionSource<PeerResult> Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

/// <summary>
/// The device's answer: ok (with JSON, or a file it sent), needs-permission (someone must allow it on the device),
/// denied, error, or timeout (the device did not pick the question up, or did not answer in time).
/// </summary>
public sealed record PeerResult(string Status, JsonElement? Json = null, string? FilePath = null, long Bytes = 0, string? Error = null)
{
    public bool Ok => Status == "ok";
}

/// <summary>
/// Two-way access without opening a port on the device: the device keeps one long request open (/api/peer/inbox,
/// up to 25 s, then asks again) and this PC hands it questions there; the device answers on /api/peer/result. A second
/// long request from the same device ends the first. At most <see cref="MaxWaiting"/> devices wait at once.
/// </summary>
public sealed class PeerBroker
{
    public const int MaxWaiting = 24;
    public static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(25);
    /// <summary>A device that asked within this time is listening.</summary>
    public static readonly TimeSpan ListeningWindow = TimeSpan.FromSeconds(40);

    private readonly Func<DateTimeOffset> _now;
    private readonly object _lock = new();
    private readonly Dictionary<string, Queue<PeerJob>> _queues = new();
    private readonly Dictionary<string, TaskCompletionSource<PeerJob?>> _waiters = new();
    private readonly Dictionary<string, PeerJob> _taken = new();
    private readonly Dictionary<string, DateTimeOffset> _lastPoll = new();

    public PeerBroker(Func<DateTimeOffset> now) => _now = now;

    /// <summary>True when the device's app is waiting for questions (or asked a moment ago).</summary>
    public bool IsListening(string deviceId)
    {
        lock (_lock)
            return _waiters.ContainsKey(deviceId) || (_lastPoll.TryGetValue(deviceId, out var at) && _now() - at < ListeningWindow);
    }

    /// <summary>Asks the device and waits for its answer (a "timeout" result when it does not come in time).</summary>
    public async Task<PeerResult> AskAsync(string deviceId, string kind, IReadOnlyDictionary<string, string> args, TimeSpan timeout, CancellationToken ct = default)
    {
        var job = new PeerJob { Id = Guid.NewGuid().ToString("N")[..16], DeviceId = deviceId, Kind = kind, Args = args, CreatedAt = _now() };
        lock (_lock)
        {
            if (_waiters.Remove(deviceId, out var w) && w.TrySetResult(job)) _taken[job.Id] = job;
            else (_queues.TryGetValue(deviceId, out var q) ? q : _queues[deviceId] = new Queue<PeerJob>()).Enqueue(job);
        }
        try
        {
            return await job.Done.Task.WaitAsync(timeout, ct);
        }
        catch (TimeoutException)
        {
            return new PeerResult("timeout", Error: "The device did not answer. Is the Cloud-SSD app open on it?");
        }
        finally
        {
            lock (_lock)
            {
                _taken.Remove(job.Id);
                if (_queues.TryGetValue(deviceId, out var q) && q.Contains(job))
                    _queues[deviceId] = new Queue<PeerJob>(q.Where(j => j != job));
            }
        }
    }

    /// <summary>
    /// The device's long request: a question as soon as there is one, or null after <paramref name="wait"/>. Null at
    /// once with busy = true when too many devices are waiting.
    /// </summary>
    public async Task<(PeerJob? Job, bool Busy)> WaitForJobAsync(string deviceId, TimeSpan wait, CancellationToken ct)
    {
        TaskCompletionSource<PeerJob?> me;
        lock (_lock)
        {
            _lastPoll[deviceId] = _now();
            if (_queues.TryGetValue(deviceId, out var q) && q.Count > 0)
            {
                var job = q.Dequeue();
                _taken[job.Id] = job;
                return (job, false);
            }
            // The same device asking again ends its earlier request (it reconnected, or a second copy of the app).
            if (_waiters.Remove(deviceId, out var old)) old.TrySetResult(null);
            if (_waiters.Count >= MaxWaiting) return (null, true);
            me = new TaskCompletionSource<PeerJob?>(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters[deviceId] = me;
        }
        try
        {
            return (await me.Task.WaitAsync(wait < MaxWait ? wait : MaxWait, ct), false);
        }
        catch (Exception e) when (e is TimeoutException or OperationCanceledException)
        {
            lock (_lock)
            {
                if (_waiters.TryGetValue(deviceId, out var w) && w == me) _waiters.Remove(deviceId);
                _lastPoll[deviceId] = _now();
            }
            // A question handed over at the very moment the wait ended is not lost: it goes back in the queue.
            if (me.Task.IsCompletedSuccessfully && me.Task.Result is { } late)
                lock (_lock) (_queues.TryGetValue(deviceId, out var q) ? q : _queues[deviceId] = new Queue<PeerJob>()).Enqueue(late);
            return (null, false);
        }
    }

    /// <summary>The question with this id, when this device picked it up and has not answered yet.</summary>
    public PeerJob? Taken(string jobId, string deviceId)
    {
        lock (_lock) return _taken.TryGetValue(jobId, out var j) && j.DeviceId == deviceId ? j : null;
    }

    public void Complete(PeerJob job, PeerResult result)
    {
        lock (_lock) _taken.Remove(job.Id);
        job.Done.TrySetResult(result);
    }
}
