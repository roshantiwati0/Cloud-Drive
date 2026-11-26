using System.Diagnostics;
using System.Text.Json;
using CloudSSD.Core;
using CloudSSD.Diagnostics;
using Microsoft.Extensions.Logging;

namespace CloudSSD.Mount;

public sealed record MountRequest(
    MountDefinition Definition,
    CacheSettings Cache,
    NetworkSettings Network,
    string CacheDir,
    long EffectiveCacheMaxBytes,
    bool Telemetry,
    string? RemoteType,
    IReadOnlyDictionary<string, string?>? ExtraEnvironment = null);

/// <summary>A running (or recovering) rclone mount.</summary>
public sealed class MountSession
{
    internal MountSession(MountRequest request, char letter)
    {
        Request = request;
        DriveLetter = letter;
    }

    public MountRequest Request { get; internal set; }
    public MountDefinition Definition => Request.Definition;
    public string Id => Request.Definition.Id;
    public char DriveLetter { get; }
    public string Root => $"{DriveLetter}:\\";
    public MountState State { get; internal set; } = MountState.Starting;
    public string? Message { get; internal set; }
    public DateTimeOffset Since { get; internal set; } = DateTimeOffset.Now;
    public int RestartCount { get; internal set; }
    public int? ProcessId => Process is { } p && !SafeHasExited(p) ? p.Id : null;
    public RcClient? Rc { get; internal set; }
    public string LogFile { get; internal set; } = "";
    public string CacheDir => Request.CacheDir;
    public ReadAttributionRegistry Attribution { get; } = new();
    public IReadOnlyList<string> Warnings { get; internal set; } = [];
    public bool Adopted { get; internal set; }

    public long ProviderErrors;
    public long Retries;
    public long RateLimitEvents;
    public long AuthErrors;
    public long NetworkErrors;
    public DateTime? LastNetworkError;
    /// <summary>When the provider last answered "slow down" (Google: 403 quota / rate limit), in local time as rclone logged it.</summary>
    public DateTime? LastRateLimit;
    public DateTime LastActivity = DateTime.Now;

    internal Process? Process;
    /// <summary>
    /// The rclone process while it has not finished starting. If it dies or is killed in that phase, whoever started
    /// it reports the failure and schedules the retry; the process-exit handler must not start a second recovery.
    /// </summary>
    internal Process? StartingProcess;
    internal LogTailer? Tailer;
    internal RcloneLogParser? Parser;
    internal volatile bool StopRequested;
    internal readonly List<DateTime> RestartTimes = new();
    internal int FailedPings;
    internal string? RcUser;
    internal string? RcPass;

    public MountStatus ToStatus() => new(Id, State, DriveLetter + ":", ProcessId, Message, Since, RestartCount);

    internal static bool SafeHasExited(Process p)
    {
        try { return p.HasExited; } catch (InvalidOperationException) { return true; }
    }
}

internal sealed record PersistedMount(string Id, int Pid, long ProcessStartTicks, int Port, string User, string PassProtected,
    char Letter, string LogFile, string RequestJson);

/// <summary>
/// Manages rclone + WinFsp mounts: start, readiness detection, health checks, crash auto-restart with backoff,
/// graceful shutdown that respects pending uploads, and adoption of mounts that survived an application restart.
/// </summary>
public sealed class RcloneMountService : IAsyncDisposable
{
    private readonly AppPaths _paths;
    private readonly RcloneConfigService _config;
    private readonly ILogger _log;
    private readonly Dictionary<string, MountSession> _sessions = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Timer _health;
    private RcloneInfo? _rclone;

    public RcloneMountService(AppPaths paths, RcloneConfigService config, ILogger<RcloneMountService> log)
    {
        _paths = paths;
        _config = config;
        _log = log;
        _health = new Timer(_ => _ = HealthCheckAsync(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    public event Action<MountStatus>? StatusChanged;
    public event Action<AccessEvent>? ReadObserved;
    /// <summary>(mountId, path, offset, present, isPrefetch) for each cache lookup of an in-flight read.</summary>
    public event Action<string, string, long, bool, bool>? LookupObserved;
    public event Action<string, ProviderEvent>? ProviderEventObserved;
    /// <summary>(mountId, folder relative to the drive root) each time a program lists a folder of a drive.</summary>
    public event Action<string, string>? DirectoryListed;

    /// <summary>
    /// Restart policy: up to this many quick restarts (2, 4, 8 … 60 s apart) within <see cref="RestartWindow"/>; after
    /// that the drive is still tried again, only slowly (<see cref="SlowRestartDelay"/>). It never gives up.
    /// </summary>
    public int MaxRestarts { get; set; } = 5;
    public TimeSpan RestartWindow { get; set; } = TimeSpan.FromMinutes(10);
    /// <summary>Pause between tries once the quick restarts are used up.</summary>
    public TimeSpan SlowRestartDelay { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// How long to wait before the next automatic restart of a drive. A drive the user connected stays connected
    /// until the user disconnects it: the first tries come quickly, later ones slowly, and there is no last try.
    /// Before, the fifth failure within ten minutes ended it ("Automatic restart disabled … Remount manually"), so a
    /// PC that started without internet (Wi-Fi joins a minute after sign-in) or met a long Google quota block kept
    /// no drive until someone opened the app and connected it by hand.
    /// </summary>
    public static TimeSpan NextRestartDelay(int recentRestarts, int maxRestarts, TimeSpan slowDelay) =>
        recentRestarts >= maxRestarts ? slowDelay : TimeSpan.FromSeconds(Math.Min(60, 2 * Math.Pow(2, Math.Max(0, recentRestarts))));

    /// <summary>
    /// True when the cloud provider told any drive to slow down within the last <paramref name="window"/>. Background
    /// work that only saves time later (fetching archives ahead) must wait then: every extra request makes rclone
    /// sleep longer before the next one, and the request the user is waiting for queues behind it.
    /// </summary>
    public bool RateLimitedWithin(TimeSpan window)
    {
        var since = DateTime.Now - window;
        lock (_sessions) return _sessions.Values.Any(s => s.LastRateLimit is { } t && t >= since);
    }

    /// <summary>
    /// How long the drive letter may take to appear after rclone was started. The Google Drive API can
    /// answer "403 Quota exceeded" for a while when rclone's shared client id is busy, and every retry
    /// delays the first listing, so a healthy mount can need 75-90 s. The old 45 s cut it off, the app
    /// killed rclone and started again, and the user saw a failure although the drive appears seconds later.
    /// </summary>
    public static readonly TimeSpan DriveAppearTimeout = TimeSpan.FromSeconds(120);

    public void SetRclone(RcloneInfo info) => _rclone = info;
    public RcloneInfo? Rclone => _rclone;

    public IReadOnlyList<MountSession> Sessions
    {
        get { lock (_sessions) return _sessions.Values.ToList(); }
    }

    public MountSession? Get(string id)
    {
        lock (_sessions) return _sessions.GetValueOrDefault(id);
    }

    public async Task<MountSession> MountAsync(MountRequest request, CancellationToken ct = default)
    {
        var rclone = _rclone ?? throw new InvalidOperationException("rclone not found. Check Diagnostics.");
        if (!rclone.IsSupportedVersion)
            throw new InvalidOperationException($"rclone {rclone.Version} is too old; {RcloneInfo.MinimumVersion} or newer is required.");
        if (!WinFspDetector.Detect().Installed)
            throw new InvalidOperationException("WinFsp is not installed. Install it from https://winfsp.dev/rel/ and try again.");

        await _gate.WaitAsync(ct);
        try
        {
            var previous = Get(request.Definition.Id);
            if (previous is { State: MountState.Mounted or MountState.Starting }) return previous;
            // A failed session may still have an automatic restart pending. The new session takes over the drive, so
            // the old one must give up; otherwise two rclone processes fight for the same drive letter.
            if (previous != null) previous.StopRequested = true;
            var used = DriveLetterAllocator.UsedLetters();
            var letter = DriveLetterAllocator.Choose(request.Definition.DriveLetter, used)
                         ?? throw new InvalidOperationException("No free drive letter is available.");
            if (request.Definition.DriveLetter is { Length: > 0 } wanted && char.ToUpperInvariant(wanted[0]) != letter)
                _log.LogWarning("Drive letter {Wanted}: is in use; using {Letter}: instead", wanted, letter);

            PrepareCacheDir(request.CacheDir);
            var session = new MountSession(request, letter);
            lock (_sessions) _sessions[request.Definition.Id] = session;
            try
            {
                await StartProcessAsync(session, rclone, ct);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // The caller gets the error; one background retry loop keeps trying (a start often times out only
                // because the provider is rate limiting). A later MountAsync for this drive supersedes that loop.
                _ = AutoRestartAsync(session);
                throw;
            }
            return session;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Creates the cache directory and the ownership marker that cache cleanup requires before deleting anything.</summary>
    public static void PrepareCacheDir(string dir)
    {
        Directory.CreateDirectory(dir);
        var marker = Path.Combine(dir, CacheMarker);
        if (!File.Exists(marker)) File.WriteAllText(marker, "This folder is managed by Cloud-SSD (rclone VFS cache). Safe to delete only when unmounted.\n");
    }

    public const string CacheMarker = ".cloudplayx-cache";

    private async Task StartProcessAsync(MountSession s, RcloneInfo rclone, CancellationToken ct)
    {
        s.State = MountState.Starting;
        s.Since = DateTimeOffset.Now;
        s.StopRequested = false;
        Publish(s, "Starting rclone mount…");

        var port = RcClient.FindFreeLoopbackPort();
        s.RcUser = SecretStore.RandomToken(12);
        s.RcPass = SecretStore.RandomToken(24);
        s.LogFile = Path.Combine(_paths.LogDirectory, $"rclone-{s.Id}.log");
        RotateOldLog(s.LogFile);

        var plan = MountFlagBuilder.Build(new MountPlanInput(s.Definition, s.DriveLetter, s.Request.Cache, s.Request.Network, s.CacheDir,
            s.LogFile, s.Request.EffectiveCacheMaxBytes, s.Request.Telemetry, port, s.Request.RemoteType), rclone);
        s.Warnings = plan.Warnings;
        foreach (var w in plan.Warnings) _log.LogWarning("Mount {Id}: {Warning}", s.Id, w);

        var env = new Dictionary<string, string?>(_config.BaseEnvironment())
        {
            ["RCLONE_RC_USER"] = s.RcUser,
            ["RCLONE_RC_PASS"] = s.RcPass,
        };
        if (s.Request.ExtraEnvironment != null) foreach (var (k, v) in s.Request.ExtraEnvironment) env[k] = v;

        var psi = new RcloneRunner(rclone.Path, env).CreateStartInfo(plan.Arguments);
        // A child that writes into a PIPE of this app dies with this app: the write end gets EPIPE as soon
        // as this app closes the read end, and rclone (a Go program) turns that into SIGPIPE and exits.
        // Measured: a mount disappeared about a minute after the app was closed, even though the user had
        // answered "yes, keep the drives mounted" - and nothing in the logs said why. So the mount is started
        // with its standard handles on the NUL device: no pipe that can break, no console to hold open.
        _log.LogInformation("Mount {Id}: {Command}", s.Id, RcloneRunner.DescribeCommand(psi));

        var proc = DetachedProcess.Start(rclone.Path, plan.Arguments, psi.Environment);
        proc.EnableRaisingEvents = true;
        proc.Exited += (_, _) => OnProcessExited(s, proc);
        s.StartingProcess = proc;
        s.Process = proc;
        s.Rc?.Dispose();
        s.Rc = new RcClient(port, s.RcUser, s.RcPass);
        AttachTelemetry(s, fromEnd: false);

        // How long the drive letter may take to appear. Google Drive's API can answer with
        // "403 Quota exceeded" for a while when rclone's shared client id is busy (or rate limited), and
        // every one of those retries delays the first directory listing - the mount then really needs
        // 75-90 s. The old 45 s cut the mount off while it was still healthy, the app killed rclone and
        // started over, and the user saw "Drive Z: did not appear within 45 s" although the drive does
        // show up a few seconds later.
        var deadline = DateTime.UtcNow + DriveAppearTimeout;
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if (MountSession.SafeHasExited(proc))
            {
                var tail = SafeTail(s.LogFile, 12);
                s.State = MountState.Failed;
                var reason = RcloneConfigService.ExplainError(tail);
                Publish(s, "rclone exited during startup: " + reason);
                throw new InvalidOperationException($"Mount failed: {reason}");
            }
            if (Directory.Exists(s.Root) && await s.Rc.PingAsync(ct))
            {
                s.State = MountState.Mounted;
                s.Since = DateTimeOffset.Now;
                s.FailedPings = 0;
                s.StartingProcess = null; // from here on an exit is a crash, handled by OnProcessExited
                Persist();
                Publish(s, $"Mounted at {s.DriveLetter}:");
                // The directory tree is pre-loaded by the --vfs-refresh mount flag; a second recursive vfs/refresh here
                // walked the whole drive again, competing with game reads for the Drive API rate limit.
                return;
            }
            await Task.Delay(250, ct);
        }
        s.State = MountState.Failed;
        // The most common cause is Google Drive answering "403 Quota exceeded" while the mount waits for
        // the first listing: say that instead of a bare timeout, so the user knows what to do.
        // The quota error is many lines up in the log (the walk continues after it), so read a long tail:
        // with only 40 lines the message came out as a plain DEBUG line and the user learned nothing.
        var timeoutTail = SafeTail(s.LogFile, 400);
        var hint = RcloneConfigService.ExplainError(timeoutTail);
        // rclone is still alive and still working - Google Drive is only slow (or rate limited) right now.
        // Killing it here threw away everything it had done so far, and the automatic restart began the whole
        // walk from zero again, so the drive never came up at all (measured: 240 s and 186 quota errors).
        // Instead the mount keeps running and a watcher waits for the drive letter to appear on its own.
        if (s.Process is { } alive && !MountSession.SafeHasExited(alive))
        {
            s.State = MountState.Starting;
            Publish(s, $"The drive is still coming up (Google Drive is slow); it will appear on its own. {hint}");
            _ = WatchForDriveAsync(s);
            return;
        }
        Publish(s, $"Timed out waiting for the drive to appear. {hint}");
        TryKill(proc);
        throw new TimeoutException($"Drive {s.DriveLetter}: did not appear within {DriveAppearTimeout.TotalSeconds:0} s. {hint} See {s.LogFile}.");
    }

    /// <summary>
    /// How long a mount that was still starting when the app stopped waiting is watched for its drive letter.
    /// Google Drive's shared rclone client id can answer "403 Quota exceeded" for many minutes, and the
    /// mount becomes usable the moment the quota frees up - so the process is kept alive and watched.
    /// </summary>
    public static readonly TimeSpan DriveAppearWatchTimeout = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Waits for a mount whose drive letter did not appear within <see cref="DriveAppearTimeout"/>. The rclone
    /// process is left running (it is often only slow, not broken), and this watcher marks the session as
    /// mounted as soon as the letter shows up. If rclone dies in the meantime, OnProcessExited takes over and
    /// the normal restart logic applies.
    /// </summary>
    private async Task WatchForDriveAsync(MountSession s)
    {
        // From now on an exit of rclone is a crash of a running mount, not a failed start:
        // OnProcessExited must handle it (otherwise the session would stay "Starting" forever).
        s.StartingProcess = null;
        var deadline = DateTime.UtcNow + DriveAppearWatchTimeout;
        while (DateTime.UtcNow < deadline)
        {
            if (s.StopRequested) return;
            var proc = s.Process;
            // rclone died on its own - OnProcessExited already published the reason and starts the restart loop.
            if (proc is null || MountSession.SafeHasExited(proc)) return;
            if (Directory.Exists(s.Root) && s.Rc is { } rc && await rc.PingAsync())
            {
                s.State = MountState.Mounted;
                s.Since = DateTimeOffset.Now;
                s.FailedPings = 0;
                Persist();
                Publish(s, $"Mounted at {s.DriveLetter}: (the drive appeared while the app was waiting)");
                _log.LogInformation("Mount {Id}: the drive appeared at {Letter} while the app was still waiting", s.Id, s.DriveLetter);
                return;
            }
            try { await Task.Delay(1000); }
            catch (OperationCanceledException) { return; }
        }
        _log.LogWarning("Drive {Letter}: still not visible after {Minutes} min; rclone is left running", s.DriveLetter, DriveAppearWatchTimeout.TotalMinutes);
        Publish(s, $"Drive {s.DriveLetter}: is still not visible. rclone keeps running - check Diagnostics or your Google Drive quota.");
    }

    private void AttachTelemetry(MountSession s, bool fromEnd)
    {
        s.Tailer?.Dispose();
        s.Parser = new RcloneLogParser(s.Id, (path, off, len, start) => s.Attribution.IsPrefetch(path, off, len, start));
        s.Parser.Read += e => { s.LastActivity = DateTime.Now; ReadObserved?.Invoke(e); };
        s.Parser.Provider += e => OnProviderEvent(s, e);
        s.Parser.Lookup += (path, off, present, pre) => LookupObserved?.Invoke(s.Id, path, off, present, pre);
        s.Parser.DirectoryListed += dir => DirectoryListed?.Invoke(s.Id, dir);
        var parser = s.Parser;
        s.Tailer = new LogTailer(s.LogFile, parser.ParseLine, fromEnd);
    }

    private void OnProviderEvent(MountSession s, ProviderEvent e)
    {
        switch (e.Kind)
        {
            case ProviderEventKind.Error: Interlocked.Increment(ref s.ProviderErrors); break;
            case ProviderEventKind.Retry: Interlocked.Increment(ref s.Retries); break;
            case ProviderEventKind.RateLimited: Interlocked.Increment(ref s.RateLimitEvents); Interlocked.Increment(ref s.Retries); s.LastRateLimit = e.Timestamp; break;
            case ProviderEventKind.AuthError: Interlocked.Increment(ref s.AuthErrors); Interlocked.Increment(ref s.ProviderErrors); break;
            case ProviderEventKind.NetworkError: Interlocked.Increment(ref s.NetworkErrors); Interlocked.Increment(ref s.ProviderErrors); s.LastNetworkError = e.Timestamp; break;
        }
        if (e.Kind is ProviderEventKind.Error or ProviderEventKind.AuthError or ProviderEventKind.NetworkError or ProviderEventKind.OutOfSpace)
            _log.LogWarning("rclone [{Id}] {Kind}: {Msg}", s.Id, e.Kind, e.Message);
        ProviderEventObserved?.Invoke(s.Id, e);
    }

    private void OnProcessExited(MountSession s, Process proc)
    {
        if (!ReferenceEquals(s.Process, proc)) return;
        // A process that never finished starting: StartProcessAsync reports it to its caller, which owns the retry.
        if (ReferenceEquals(s.StartingProcess, proc)) return;
        // A newer session owns this drive now; a late exit of the old process must not mark the drive as stopped.
        if (Get(s.Id) is { } current && !ReferenceEquals(current, s)) return;
        int code = -1;
        try { code = proc.ExitCode; } catch (InvalidOperationException) { }
        if (s.StopRequested)
        {
            s.State = MountState.Stopped;
            Publish(s, "Unmounted");
            return;
        }
        var tail = SafeTail(s.LogFile, 20);
        _log.LogError("rclone mount {Id} exited unexpectedly (code {Code}). Last log lines:\n{Tail}", s.Id, code, tail);
        s.State = MountState.Failed;
        Publish(s, $"rclone exited unexpectedly (code {code}). {RcloneConfigService.ExplainError(tail)}");
        _ = AutoRestartAsync(s);
    }

    /// <summary>True while this session is still the one in charge of its drive and nobody asked it to stop.</summary>
    private bool MayRestart(MountSession s) => !s.StopRequested && ReferenceEquals(Get(s.Id), s);

    private async Task AutoRestartAsync(MountSession s)
    {
        if (!MayRestart(s)) return;
        var now = DateTime.UtcNow;
        s.RestartTimes.RemoveAll(t => now - t > RestartWindow);
        if (_rclone is null)
        {
            Publish(s, "rclone is missing, so the drive cannot be connected again by itself.");
            return;
        }
        var delay = NextRestartDelay(s.RestartTimes.Count, MaxRestarts, SlowRestartDelay);
        s.RestartTimes.Add(now);
        s.RestartCount++;
        Publish(s, $"Restarting mount in {delay.TotalSeconds:0} s (attempt {s.RestartCount})…");
        await Task.Delay(delay);
        if (!MayRestart(s)) return;
        // WinFsp releases the drive letter asynchronously after the process dies.
        for (int i = 0; i < 40 && Directory.Exists(s.Root); i++) await Task.Delay(250);
        await _gate.WaitAsync();
        try
        {
            // Unmounted or replaced by a manual connect while this was waiting: that session owns the drive now.
            // Already mounted again: another restart of this same session got here first.
            if (!MayRestart(s) || s.State == MountState.Mounted) return;
            await StartProcessAsync(s, _rclone, CancellationToken.None);
            _log.LogInformation("Mount {Id} restarted successfully; cached data was preserved", s.Id);
        }
        catch (Exception e)
        {
            _log.LogError(e, "Automatic restart of mount {Id} failed", s.Id);
            _ = AutoRestartAsync(s);
        }
        finally { _gate.Release(); }
    }

    public async Task UnmountAsync(string id, TimeSpan? waitForUploads = null, CancellationToken ct = default)
    {
        var s = Get(id);
        if (s is null) return;
        s.StopRequested = true;
        s.State = MountState.Stopping;
        Publish(s, "Unmounting…");

        if (s.Rc != null && waitForUploads is { } wait)
        {
            var until = DateTime.UtcNow + wait;
            while (DateTime.UtcNow < until)
            {
                try
                {
                    var v = VfsStats.Parse(await s.Rc.CallAsync("vfs/stats", null, ct));
                    if (v.UploadsInProgress + v.UploadsQueued == 0) break;
                    Publish(s, $"Waiting for {v.UploadsInProgress + v.UploadsQueued} pending upload(s)…");
                }
                catch (Exception e) when (e is HttpRequestException or RcException or TaskCanceledException) { break; }
                await Task.Delay(1000, ct);
            }
        }

        if (s.Rc != null) await WaitForIdleAsync(s, ct);

        if (s.Process is { } p && !MountSession.SafeHasExited(p))
        {
            try { if (s.Rc != null) await s.Rc.CallAsync("core/quit", null, ct, TimeSpan.FromSeconds(5)); }
            catch (Exception e) when (e is HttpRequestException or RcException or TaskCanceledException) { }
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(15));
            try { await p.WaitForExitAsync(cts.Token); }
            catch (OperationCanceledException) { TryKill(p); }
        }
        for (int i = 0; i < 40 && Directory.Exists(s.Root); i++) await Task.Delay(250, CancellationToken.None);
        s.Tailer?.Dispose();
        s.Rc?.Dispose();
        s.State = MountState.Stopped;
        lock (_sessions) _sessions.Remove(id);
        Persist();
        Publish(s, "Unmounted");
    }

    /// <summary>
    /// rclone persists an item's downloaded-range index only when its last handle closes, after the handle-caching
    /// delay (5 s). Quitting earlier keeps the data on disk but loses the index, so the next session re-downloads it.
    /// Measured on rclone 1.75.2; see KNOWN_LIMITATIONS.md. vfs/stats "inUse" stays at 1 on an idle mount, so idleness
    /// is judged from the last read seen in telemetry (or a fixed window when telemetry is off). Bounded to 20 s.
    /// </summary>
    private static async Task WaitForIdleAsync(MountSession s, CancellationToken ct)
    {
        var deadline = DateTime.Now + TimeSpan.FromSeconds(20);
        var unmountRequested = DateTime.Now;
        while (DateTime.Now < deadline)
        {
            var last = s.Request.Telemetry ? s.LastActivity : unmountRequested;
            var remaining = HandleCachingWindow - (DateTime.Now - last);
            if (remaining <= TimeSpan.Zero) return;
            await Task.Delay(remaining < TimeSpan.FromMilliseconds(250) ? TimeSpan.FromMilliseconds(250) : remaining, ct);
        }
    }

    /// <summary>
    /// Drops rclone's cached directory metadata so the next access re-reads size/modtime from the provider. If the
    /// remote file changed, rclone's fingerprint check then discards the stale cached data instead of serving it.
    /// </summary>
    public async Task ForgetAsync(string id, string? relativePath = null, CancellationToken ct = default)
    {
        var s = Get(id) ?? throw new InvalidOperationException("Mount not running");
        if (s.Rc is null) return;
        if (string.IsNullOrEmpty(relativePath)) await s.Rc.CallAsync("vfs/forget", null, ct);
        else await s.Rc.CallAsync("vfs/forget", new Dictionary<string, string> { ["file"] = relativePath.Replace('\\', '/') }, ct);
    }

    /// <summary>
    /// Changes rclone's bandwidth limit at runtime (core/bwlimit). 0 = unlimited. With <paramref name="uploadMbps"/>
    /// uploads get that lower limit of their own while downloads keep <paramref name="mbps"/>.
    /// </summary>
    public async Task SetBandwidthLimitAsync(string id, double mbps, CancellationToken ct = default, double? uploadMbps = null)
    {
        var s = Get(id);
        if (s?.Rc is null) return;
        await s.Rc.CallAsync("core/bwlimit", new Dictionary<string, string> { ["rate"] = BandwidthRate(mbps, uploadMbps) }, ct);
    }

    /// <summary>rclone's rate text: one value for both directions, or "upload:download".</summary>
    public static string BandwidthRate(double mbps, double? uploadMbps = null)
    {
        static string One(double v) => v <= 0 ? "off" : $"{(long)(v * 1_000_000 / 8)}B";
        if (uploadMbps is not { } up || up <= 0) return One(mbps);
        return One(mbps > 0 ? Math.Min(up, mbps) : up) + ":" + One(mbps);
    }

    public static readonly TimeSpan HandleCachingWindow = TimeSpan.FromSeconds(6);

    public async Task UnmountAllAsync(TimeSpan? waitForUploads = null)
    {
        foreach (var s in Sessions) await UnmountAsync(s.Id, waitForUploads);
    }

    /// <summary>Detaches from running mounts without stopping them (used when the user keeps drives mounted after exit).</summary>
    public void DetachAll()
    {
        foreach (var s in Sessions)
        {
            s.StopRequested = true; // suppress auto-restart in this process
            s.Tailer?.Dispose();
        }
        Persist();
    }

    /// <summary>Re-attaches to mounts left running by a previous Cloud-SSD instance, or cleans up stale entries.</summary>
    public async Task<IReadOnlyList<MountSession>> AdoptExistingAsync(CancellationToken ct = default)
    {
        var adopted = new List<MountSession>();
        foreach (var pm in LoadPersisted())
        {
            Process? proc = null;
            try
            {
                proc = Process.GetProcessById(pm.Pid);
                if (!proc.ProcessName.Equals("rclone", StringComparison.OrdinalIgnoreCase) || proc.StartTime.Ticks != pm.ProcessStartTicks) proc = null;
            }
            catch (ArgumentException) { proc = null; }
            catch (InvalidOperationException) { proc = null; }

            var request = JsonSerializer.Deserialize<MountRequest>(pm.RequestJson, JsonSettingsStore.Options);
            var pass = SecretStore.Unprotect(pm.PassProtected);
            if (proc is null || request is null || pass is null)
            {
                _log.LogInformation("Discarding stale mount record {Id}", pm.Id);
                continue;
            }
            var s = new MountSession(request, pm.Letter) { Adopted = true, LogFile = pm.LogFile, RcUser = pm.User, RcPass = pass };
            s.Rc = new RcClient(pm.Port, pm.User, pass);
            if (!await s.Rc.PingAsync(ct))
            {
                _log.LogWarning("rclone mount {Id} (pid {Pid}) is running but not responding; stopping it so it can be remounted cleanly", pm.Id, pm.Pid);
                TryKill(proc);
                s.Rc.Dispose();
                continue;
            }
            s.Process = proc;
            proc.EnableRaisingEvents = true;
            proc.Exited += (_, _) => OnProcessExited(s, proc);
            s.State = MountState.Mounted;
            lock (_sessions) _sessions[s.Id] = s;
            AttachTelemetry(s, fromEnd: true);
            Publish(s, $"Re-attached to running mount at {s.DriveLetter}:");
            adopted.Add(s);
        }
        Persist();
        return adopted;
    }

    private async Task HealthCheckAsync()
    {
        foreach (var s in Sessions)
        {
            if (s.State is not (MountState.Mounted or MountState.Degraded) || s.Rc is null) continue;
            var ok = await s.Rc.PingAsync();
            var visible = Directory.Exists(s.Root);
            if (ok && visible)
            {
                s.FailedPings = 0;
                if (s.State == MountState.Degraded)
                {
                    s.State = MountState.Mounted;
                    Publish(s, "Mount healthy again");
                }
            }
            else if (++s.FailedPings >= 3 && s.State == MountState.Mounted)
            {
                s.State = MountState.Degraded;
                Publish(s, ok ? $"Drive {s.DriveLetter}: is not visible although rclone is running" : "rclone is not responding");
            }
        }
    }

    /// <summary>Called after Windows resumes from sleep: verifies every mount and restarts dead ones.</summary>
    public async Task OnSystemResumeAsync()
    {
        _log.LogInformation("System resumed; verifying mounts");
        await Task.Delay(TimeSpan.FromSeconds(3));
        foreach (var s in Sessions)
        {
            if (s.Process is { } p && MountSession.SafeHasExited(p) && !s.StopRequested) _ = AutoRestartAsync(s);
        }
        await HealthCheckAsync();
    }

    private void Persist()
    {
        try
        {
            var list = Sessions.Where(s => s.State is MountState.Mounted or MountState.Degraded && s.Process is { } p && !MountSession.SafeHasExited(p) && s.Rc != null)
                .Select(s => new PersistedMount(s.Id, s.Process!.Id, s.Process.StartTime.Ticks, s.Rc!.Port, s.RcUser!, SecretStore.Protect(s.RcPass!),
                    s.DriveLetter, s.LogFile, JsonSerializer.Serialize(s.Request, JsonSettingsStore.Options)))
                .ToList();
            var tmp = _paths.MountStateFile + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(list, JsonSettingsStore.Options));
            File.Move(tmp, _paths.MountStateFile, true);
        }
        catch (Exception e)
        {
            _log.LogWarning(e, "Could not persist mount state");
        }
    }

    private List<PersistedMount> LoadPersisted()
    {
        try
        {
            return File.Exists(_paths.MountStateFile)
                ? JsonSerializer.Deserialize<List<PersistedMount>>(File.ReadAllText(_paths.MountStateFile), JsonSettingsStore.Options) ?? []
                : [];
        }
        catch (JsonException) { return []; }
    }

    private void Publish(MountSession s, string message)
    {
        s.Message = message;
        StatusChanged?.Invoke(s.ToStatus());
    }

    private static void RotateOldLog(string path)
    {
        try
        {
            if (File.Exists(path)) File.Move(path, path + ".prev", true);
        }
        catch (IOException) { /* still held by a dying process; rclone will append */ }
    }

    public static string SafeTail(string path, int lines)
    {
        try { return File.Exists(path) ? string.Join(Environment.NewLine, DiagnosticReport.ReadTail(path, lines)) : ""; }
        catch (IOException) { return ""; }
    }

    private static void TryKill(Process p)
    {
        try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch (Exception) { }
    }

    public async ValueTask DisposeAsync()
    {
        await _health.DisposeAsync();
        foreach (var s in Sessions) s.Tailer?.Dispose();
    }
}
