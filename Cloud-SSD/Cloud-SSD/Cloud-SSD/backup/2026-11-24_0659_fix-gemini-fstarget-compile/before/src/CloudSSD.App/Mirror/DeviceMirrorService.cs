using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using CloudSSD.Core;
using CloudSSD.Mount;
using Microsoft.Extensions.Logging;

namespace CloudSSD.App.Mirror;

/// <summary>
/// What the mirror is doing, for its page. State: off (no device and this PC both said yes), paused (why is in
/// <see cref="Reason"/>: person, game, battery, no-rclone, no-account, upload-limit, rate-limit, error), scanning, uploading, idle.
/// </summary>
public sealed record MirrorStatus(
    string State, string? Reason, string? Account, DateTimeOffset? LastSync, IReadOnlyList<MirrorTierTotal> Tiers,
    IReadOnlyDictionary<string, long> Failed, int FoldersNotRead, int GoneHeld, string? Error);

/// <summary>
/// "Files when it is off", this PC's side: keeps a copy of the PC's own files in the cloud while a paired device and
/// this PC both said yes (docs/WHEN_OFF_PLAN.md). One pass at a time: look at the disk (the person's folders every
/// hour, everything once a day), copy what is new or changed (the person's folders first, small files first), move
/// the copies of deleted files to a trash folder, write the state for the other side to read.
/// It stops for a game, on battery, and when the person pauses it; nothing of the app's own data is ever copied.
/// </summary>
public sealed class DeviceMirrorService : IDisposable
{
    private static readonly TimeSpan QuickEvery = TimeSpan.FromHours(1), FullEvery = TimeSpan.FromHours(24),
        RetryFailedAfter = TimeSpan.FromHours(6), AfterError = TimeSpan.FromMinutes(5), AfterUploadLimit = TimeSpan.FromHours(6),
        AfterRateLimit = TimeSpan.FromMinutes(15);
    private const int BatchFiles = 400;
    private const long BatchBytes = 1L << 30;

    private readonly AppHost _host;
    private readonly ILogger _log;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _lock = new();
    private MirrorState? _state;
    private CancellationTokenSource? _running;
    private volatile string _phase = "";
    private volatile string? _error;
    private volatile bool _syncNow;
    private volatile int _foldersNotRead;
    private IReadOnlyList<MirrorFile> _goneHeld = [];
    private DateTimeOffset _notBefore = DateTimeOffset.MinValue;
    private string? _pauseAfterPass;
    private (DateTime At, IReadOnlyList<MirrorTierTotal> Tiers, IReadOnlyDictionary<string, long> Failed) _totals = (DateTime.MinValue, [], new Dictionary<string, long>());

    /// <summary>Tests: runs "rclone" (no process), the account to use, the places and drives of a made-up PC, and the clock.</summary>
    internal RcloneRun? RunOverride;
    internal string? AccountOverride;
    internal MirrorPlaces? PlacesOverride;
    internal IReadOnlyList<string>? DrivesOverride;
    internal Func<bool>? OnBatteryOverride, PlayingOverride;
    internal Func<DateTimeOffset> Now = () => DateTimeOffset.Now;

    public DeviceMirrorService(AppHost host, ILogger<DeviceMirrorService> log)
    {
        _host = host;
        _log = log;
    }

    /// <summary>Something on the page changed (state, numbers).</summary>
    public event Action? Changed;

    private MirrorSettings Settings => _host.Settings.Mirror;
    private MirrorState State => LazyInitializer.EnsureInitialized(ref _state, () => new MirrorState(Path.Combine(_host.Paths.DataRoot, "mirror", "pc.db")))!;
    private MirrorPlaces Places => PlacesOverride ?? MirrorPlaces.ForThisPc(_host.Paths.DataRoot);

    /// <summary>A paired device and this PC both said yes to "Files when it is off".</summary>
    public bool Active => _host.Settings.MobileBridge.PairedDevices.Any(LocalMobileBridgeService.WhenOffActive);

    /// <summary>The folder of this PC inside "Cloud-SSD Devices": its id and its name.</summary>
    public string DeviceFolder
    {
        get
        {
            var name = new string(Environment.MachineName.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray());
            return (_host.Settings.MobileBridge.PcId + " " + (name.Length > 0 ? name : "PC")).Trim();
        }
    }

    public MirrorStatus Status
    {
        get
        {
            var (state, reason) = StateNow();
            bool open = _state != null || Active;   // the list of files is opened only once the mirror is wanted
            DateTimeOffset? last = open ? Time("lastSync") : null;
            if (open && DateTime.UtcNow - _totals.At > TimeSpan.FromSeconds(5))
                _totals = (DateTime.UtcNow, State.Totals(), State.FailedByReason());
            return new MirrorStatus(state, reason, Settings.Account, last, _totals.Tiers, _totals.Failed, _foldersNotRead, _goneHeld.Count, _error);
        }
    }

    private (string State, string? Reason) StateNow()
    {
        if (!Active) return ("off", null);
        if (PauseReason() is { } why) return ("paused", why);
        if (_phase.Length > 0) return (_phase, null);
        if (_pauseAfterPass != null && Now() < _notBefore) return ("paused", _pauseAfterPass);
        return ("idle", null);
    }

    /// <summary>Why the mirror must not work right now, or null.</summary>
    private string? PauseReason()
    {
        if (Settings.Paused) return "person";
        if (PlayingOverride?.Invoke() ?? GameActivity.IsPlaying(_host)) return "game";
        if (Settings.PauseOnBattery && (OnBatteryOverride?.Invoke() ?? OnBattery())) return "battery";
        return null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus { public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag; public int BatteryLifeTime, BatteryFullLifeTime; }
    [DllImport("kernel32.dll")] private static extern bool GetSystemPowerStatus(out SystemPowerStatus s);
    private static bool OnBattery() => GetSystemPowerStatus(out var s) && s.ACLineStatus == 0;

    /// <summary>The person pauses the mirror, or lets it go on.</summary>
    public void SetPaused(bool paused)
    {
        Settings.Paused = paused;
        _host.SaveSettings();
        if (paused) Stop();
        Changed?.Invoke();
    }

    /// <summary>"Sync now": looks at the whole disk and copies, whatever the schedule says (the task ends with the pass).</summary>
    public Task SyncNow()
    {
        _syncNow = true;
        _notBefore = DateTimeOffset.MinValue;
        _pauseAfterPass = null;
        _error = null;
        return TickAsync();
    }

    /// <summary>
    /// Many files were gone at once and the mirror asked: yes moves their copies to the cloud's trash (as for any
    /// deleted file), no keeps the copies where they are.
    /// </summary>
    public void AnswerGone(bool theyAreReallyGone)
    {
        IReadOnlyList<MirrorFile> held;
        lock (_lock) { held = _goneHeld; _goneHeld = []; }
        if (held.Count == 0) return;
        if (theyAreReallyGone) lock (_lock) _goneAnswered = held;
        else State.Forget(held.Select(g => g.Path).ToList());   // their copies stay in files/, nobody lists them any more
        Changed?.Invoke();
        _ = TickAsync();
    }
    private IReadOnlyList<MirrorFile> _goneAnswered = [];

    private void Stop()
    {
        CancellationTokenSource? cts;
        lock (_lock) { cts = _running; }
        try { cts?.Cancel(); } catch (ObjectDisposedException) { }
    }

    /// <summary>Called every few seconds: starts a pass when one is due and nothing forbids it; stops a running one when something does.</summary>
    public async Task TickAsync()
    {
        try
        {
            if (!Active || PauseReason() != null) { Stop(); return; }
            if (Now() < _notBefore && !_syncNow) return;
            if (!await _gate.WaitAsync(0)) return;
            try
            {
                if (!_syncNow && !Due() && State.Next(MaxTier, 1, long.MaxValue, RetryBefore()).Count == 0 && _goneAnswered.Count == 0) return;
                var cts = new CancellationTokenSource();
                lock (_lock) _running = cts;
                try { await Task.Run(() => RunPassAsync(cts.Token)); }
                catch (OperationCanceledException) { }
                finally
                {
                    lock (_lock) _running = null;
                    cts.Dispose();
                    _phase = "";
                    _totals = (DateTime.MinValue, _totals.Tiers, _totals.Failed);   // the numbers are read anew
                    Changed?.Invoke();
                }
            }
            finally { _gate.Release(); }
        }
        catch (Exception e)
        {
            _error = e.Message;
            _notBefore = Now() + AfterError;
            _log.LogWarning(e, "Mirror pass failed");
        }
    }

    private int MaxTier => Settings.IncludeSystem ? MirrorScope.TierSystem : MirrorScope.TierOther;
    private long RetryBefore() => (Now() - RetryFailedAfter).UtcTicks;

    private DateTimeOffset? Time(string key) =>
        DateTimeOffset.TryParse(State.Meta(key), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var t) ? t : null;
    private void SetTime(string key) => State.SetMeta(key, Now().ToString("o", CultureInfo.InvariantCulture));

    private bool FullDue() => Time("lastFull") is not { } t || Now() - t > FullEvery;
    private bool QuickDue() => Time("lastQuick") is not { } t || Now() - t > QuickEvery;
    private bool Due() => FullDue() || QuickDue();

    /// <summary>One pass: look, copy, tidy, write the state. Internal for the tests.</summary>
    internal async Task RunPassAsync(CancellationToken ct)
    {
        _pauseAfterPass = null;
        var run = RunOverride;
        if (run == null)
        {
            if (_host.Rclone is not { } rclone) { Hold("no-rclone", AfterError); return; }
            var runner = _host.RcloneConfig.CreateRunner(rclone);
            run = (args, onLine, c) => runner.RunAsync(args, TimeSpan.FromHours(12), c, onStdErrLine: onLine);
        }
        var account = await AccountAsync(ct);
        if (account == null) { Hold("no-account", AfterError); return; }
        var uploader = new MirrorUploader(run, $"{account}:{MirrorScope.CloudFolder}/{DeviceFolder}", Path.Combine(_host.Paths.DataRoot, "mirror"), Settings, Now);
        var places = Places;
        var drives = DrivesOverride ?? MirrorScope.Drives(_host.Mounts.Sessions.Select(s => s.Root));
        bool changed = false;

        // 1. Look at the disk: everything once a day (or on "Sync now"), the person's folders every hour.
        bool full = _syncNow || FullDue();
        _syncNow = false;
        if (full || QuickDue())
        {
            _phase = "scanning";
            Changed?.Invoke();
            // Only some folders are wanted (settings.json: Mirror.OnlyFolders): those are looked at whole, every time.
            var only = Settings.OnlyFolders.Where(Directory.Exists).ToList();
            if (Settings.OnlyFolders.Count > 0) full = true;
            IReadOnlyList<string> roots = Settings.OnlyFolders.Count > 0 ? only : full ? drives : [places.Profile];
            var result = MirrorScanner.Scan(State, places, Settings, roots, full ? MaxTier : MirrorScope.TierProfile, ct);
            SetTime("lastQuick");
            if (full) { SetTime("lastFull"); _foldersNotRead = result.FoldersNotRead; }
            _log.LogInformation("Mirror looked at the disk ({Kind}): {Files} files, {Bytes}, {NotRead} folders not readable, {Gone} gone",
                full ? "all" : "own folders", result.Files, ByteSize.Format(result.Bytes), result.FoldersNotRead, result.Gone.Count);
            if (result.GoneHeld) lock (_lock) _goneHeld = result.Gone;
            else lock (_lock) _goneAnswered = _goneAnswered.Concat(result.Gone).ToList();
        }

        // 2. Copies of files that are gone from the PC go to the cloud's trash folder.
        IReadOnlyList<MirrorFile> gone;
        lock (_lock) { gone = _goneAnswered; _goneAnswered = []; }
        foreach (var group in gone.GroupBy(g => Path.GetPathRoot(g.Path) ?? "", StringComparer.OrdinalIgnoreCase))
        {
            var paths = group.Select(g => g.Path).ToList();
            if (await uploader.TrashAsync(group.Key, paths, ct)) { State.Forget(paths); changed = true; }
            else lock (_lock) _goneAnswered = _goneAnswered.Concat(group).ToList();   // tried again in the next pass
        }

        // 3. Copy what is new or changed: the lowest tier first, small files first, a batch at a time.
        long copiedFiles = 0, copiedBytes = 0;
        _phase = "uploading";
        Changed?.Invoke();
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var batch = State.Next(MaxTier, BatchFiles, BatchBytes, RetryBefore());
            if (batch.Count == 0) break;
            bool progressed = false;
            foreach (var group in batch.GroupBy(f => Path.GetPathRoot(f.Path) ?? "", StringComparer.OrdinalIgnoreCase))
            {
                var r = await uploader.CopyAsync(group.Key, group.ToList(), ct);
                State.Uploaded(r.Done);
                copiedFiles += r.Done.Count;
                copiedBytes += r.Done.Sum(f => f.Size);
                long at = Now().UtcTicks;
                foreach (var (file, reason) in r.Failed) State.Failed(file.Path, reason, at);
                progressed |= r.Done.Count > 0 || r.Failed.Count > 0;
                changed |= r.Done.Count > 0;
                _totals = (DateTime.MinValue, _totals.Tiers, _totals.Failed);
                Changed?.Invoke();
                if (r.UploadLimit) { await FinishAsync(uploader, changed, ct); Hold("upload-limit", AfterUploadLimit); return; }
                if (r.RateLimit) { _error = null; await FinishAsync(uploader, changed, ct); Hold("rate-limit", AfterRateLimit); return; }
                if (r.Error != null)
                {
                    _error = Short(r.Error);
                    _log.LogWarning("Mirror could not copy: {Error}", _error);
                    await FinishAsync(uploader, changed, ct);
                    Hold("error", AfterError);
                    return;
                }
            }
            // rclone said nothing about any file of the batch (it should not happen): do not spin on it.
            if (!progressed) { Hold("error", AfterError); _error ??= "rclone copied nothing"; break; }
        }
        _error = null;
        if (copiedFiles > 0 || gone.Count > 0)
            _log.LogInformation("Mirror pass done: {Files} files ({Bytes}) are in the cloud copy now, {Gone} copies moved to its trash",
                copiedFiles, ByteSize.Format(copiedBytes), gone.Count);
        await FinishAsync(uploader, changed, ct);
    }

    /// <summary>An error as one short line (rclone's can run over a page).</summary>
    private static string Short(string error)
    {
        var line = error.Split('\n')[0].Trim();
        return line.Length > 220 ? line[..220] + "…" : line;
    }

    private void Hold(string reason, TimeSpan forHowLong)
    {
        _pauseAfterPass = reason;
        _notBefore = Now() + forHowLong;
        _log.LogInformation("Mirror waits ({Reason}) until {Until}", reason, _notBefore);
    }

    /// <summary>After a pass: the state file for the other side (when something changed, and once a day anyway), and old trash goes.</summary>
    private async Task FinishAsync(MirrorUploader uploader, bool changed, CancellationToken ct)
    {
        bool daily = Time("lastStateFile") is not { } t || Now() - t > TimeSpan.FromHours(24);
        if (!changed && !daily) return;
        SetTime("lastSync");
        var tiers = State.Totals();
        var json = JsonSerializer.Serialize(new
        {
            id = _host.Settings.MobileBridge.PcId,
            name = Environment.MachineName,
            kind = "pc",
            lastSync = Now().ToString("o", CultureInfo.InvariantCulture),
            files = tiers.Sum(x => x.Files),
            bytes = tiers.Sum(x => x.Bytes),
            uploadedFiles = tiers.Sum(x => x.UploadedFiles),
            uploadedBytes = tiers.Sum(x => x.UploadedBytes),
            failed = State.FailedByReason(),
            foldersNotRead = _foldersNotRead,
            tiers = tiers.Select(x => new { tier = x.Tier, files = x.Files, bytes = x.Bytes, uploadedFiles = x.UploadedFiles, uploadedBytes = x.UploadedBytes })
        }, new JsonSerializerOptions { WriteIndented = true });
        if (await uploader.WriteTextAsync("device.json", json, ct)) SetTime("lastStateFile");
        if (daily && RunOverride == null && _host.Rclone is { } rclone)
        {
            try
            {
                var days = await _host.RcloneConfig.ListDirectoryAsync(rclone, Settings.Account!, $"{MirrorScope.CloudFolder}/{DeviceFolder}/trash", ct);
                await uploader.CleanTrashAsync(days.Where(d => d.IsDir).Select(d => d.Name).ToList(), ct);
            }
            catch (InvalidOperationException) { }   // no trash folder yet
        }
    }

    /// <summary>
    /// The account the copies go to: the one chosen before while it is still there, else the Google account with the
    /// most free space that this app may write to (then remembered).
    /// </summary>
    private async Task<string?> AccountAsync(CancellationToken ct)
    {
        if (AccountOverride != null) return Settings.Account = AccountOverride;
        if (Settings.Account is { Length: > 0 } a && _host.Remotes.Any(r => r.Name == a)) return a;
        if (_host.Rclone is not { } rclone) return null;
        var writable = RcloneConfigService.WritableDriveRemotes(await _host.RcloneConfig.DumpAsync(rclone, ct));
        if (writable.Count == 0) return null;
        await _host.RefreshAccountsAsync(ct, TimeSpan.FromMinutes(30));
        var best = writable.OrderByDescending(w => _host.Accounts.TryGetValue(w, out var info) ? info.FreeBytes ?? 0 : 0).First();
        Settings.Account = best;
        _host.SaveSettings();
        return best;
    }

    public void Dispose()
    {
        Stop();
        _state?.Dispose();
        _gate.Dispose();
    }
}
