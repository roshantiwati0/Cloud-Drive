using System.Diagnostics;
using System.Text.Json;
using CloudSSD.Core;
using CloudSSD.Mount;
using Microsoft.Extensions.Logging;

namespace CloudSSD.App;

/// <summary>One line of the speed check. State: "ok", "warn" (works, but something is not as it should be) or "problem" (slow).</summary>
public sealed record SpeedCheck(string Name, string State, string Text);

/// <summary>What the speed check found.</summary>
public sealed record SpeedReport(DateTimeOffset At, IReadOnlyList<SpeedCheck> Checks)
{
    public bool HasProblem => Checks.Any(c => c.State == "problem");
    public IReadOnlyList<string> Problems => Checks.Where(c => c.State == "problem").Select(c => c.Name).ToList();
    /// <summary>One line for a note: what is wrong, or that all is well.</summary>
    public string Summary => HasProblem
        ? string.Join(" ", Checks.Where(c => c.State == "problem").Select(c => c.Text))
        : "Everything that keeps the drive fast is in place.";
}

/// <summary>
/// Keeps the drive fast once it is fast. The things that made it fast (the person's own Google key, the fast drive,
/// preparing the files of opened folders) can each go away without anyone noticing: a login falls back to the
/// shared key, a setting is switched off, a drive letter is lost, an agent changes a flag. The person then only
/// sees "it is slow again". So the app measures for itself: at start, every few hours, and when asked on the
/// Cloud Drives page. It times the real things (writing new files, opening a prepared file) and checks the settings
/// behind them, says in plain words what it found, and raises a note when something that was fine is not any more.
/// It changes nothing by itself and deletes only its own test folder.
/// </summary>
public sealed class SpeedGuard : IDisposable
{
    /// <summary>Writing the test files to the fast drive may take this long (measured: 0.1–0.5 s; the cloud way took minutes).</summary>
    public const double SlowWriteSeconds = 5;
    /// <summary>Opening a file that is already on this PC may take this long (measured: 0.02–0.2 s; a first open took 2–15 s).</summary>
    public const double SlowOpenSeconds = 1;
    public const int ProbeFolders = 20, ProbeFilesPerFolder = 10, ProbeFileBytes = 4096;
    /// <summary>The test folder's name; the fast drive never links, uploads or shows it as the person's item.</summary>
    public const string ProbeName = ".cloudssd-speed-check";
    public static readonly TimeSpan Every = TimeSpan.FromHours(3);

    private readonly AppHost _host;
    private readonly ILogger _log;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Timer? _timer;
    private HashSet<string> _notified = new();

    public SpeedGuard(AppHost host, ILogger log)
    {
        _host = host;
        _log = log;
    }

    public SpeedReport? Last { get; private set; }
    /// <summary>A new report is there (asked for, or one of the regular ones).</summary>
    public event Action? Changed;
    /// <summary>A regular check found a problem that the one before did not have.</summary>
    public event Action<SpeedReport>? ProblemFound;

    private string ReportFile => Path.Combine(_host.Paths.DataRoot, "speed-check.json");

    /// <summary>Starts the regular checks: the first one shortly after the app started (the drives are up by then).</summary>
    public void Start()
    {
        if (_timer != null) return;
        _timer = new Timer(_ => _ = RunAsync(regular: true), null, TimeSpan.FromSeconds(90), Every);
    }

    /// <summary>
    /// Writes the test files the way an installer does (folders, then files in them) and removes them again.
    /// Returns how long the writing took. Public and static so it can be tried on any folder.
    /// </summary>
    public static double WriteProbe(string root)
    {
        var dir = Path.Combine(root, ProbeName);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        var data = new byte[ProbeFileBytes];
        var clock = Stopwatch.StartNew();
        try
        {
            for (var d = 0; d < ProbeFolders; d++)
            {
                var sub = Directory.CreateDirectory(Path.Combine(dir, $"folder{d:00}")).FullName;
                for (var f = 0; f < ProbeFilesPerFolder; f++) File.WriteAllBytes(Path.Combine(sub, $"file{f:00}.bin"), data);
            }
            return clock.Elapsed.TotalSeconds;
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* the next check removes it */ }
        }
    }

    /// <summary>Opens a file and reads its first 64 KB; returns how long that took.</summary>
    public static double OpenProbe(string file)
    {
        var buffer = new byte[64 * 1024];
        var clock = Stopwatch.StartNew();
        using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            _ = fs.Read(buffer, 0, buffer.Length);
        return clock.Elapsed.TotalSeconds;
    }

    /// <summary>
    /// A file the drive already holds completely on this PC (small, so it was fetched whole), as its path on the drive.
    /// Looks at the newest ones in rclone's cache folder; null when there is none yet.
    /// </summary>
    public static string? PreparedFile(string cacheDir, string driveRoot)
    {
        var vfs = Path.Combine(cacheDir, "vfs");
        if (!Directory.Exists(vfs)) return null;
        try
        {
            foreach (var remote in Directory.EnumerateDirectories(vfs))
            {
                var best = new DirectoryInfo(remote).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
                    .Where(f => f.Length is >= 64 * 1024 and <= ArchiveWarmer.SmallFileLimit && !ArchiveWarmer.Extensions.Contains(f.Extension))
                    .Take(3000).OrderByDescending(f => f.LastWriteTimeUtc)
                    .Where(f => OnDiskBytes(f.FullName) >= f.Length)   // rclone's cache files are sparse: only one that is all there counts as ready
                    .Select(f => Path.Combine(driveRoot, Path.GetRelativePath(remote, f.FullName)))
                    .FirstOrDefault(File.Exists);
                if (best != null) return best;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return null;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern uint GetCompressedFileSizeW(string name, out uint high);

    /// <summary>How much of a file is really stored on the disk (a sparse file can be mostly holes).</summary>
    public static long OnDiskBytes(string file)
    {
        var low = GetCompressedFileSizeW(file, out var high);
        return low == 0xFFFFFFFF && System.Runtime.InteropServices.Marshal.GetLastWin32Error() != 0 ? 0 : ((long)high << 32) + low;
    }

    /// <summary>The verdict on a measured time: fine up to the limit, a problem above it.</summary>
    public static string Judge(double seconds, double limit) => seconds <= limit ? "ok" : "problem";

    /// <summary>Runs the whole check now. One at a time; a second caller gets the report of the run that is under way.</summary>
    public async Task<SpeedReport> RunAsync(bool regular = false, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var checks = new List<SpeedCheck>();
            var hybrid = _host.Settings.Hybrid;
            var session = _host.Mounts.Get(hybrid.MountId) ?? _host.Mounts.Sessions.FirstOrDefault();

            // 1. The Google key: the shared one is what made everything slow (Google refused most requests).
            try { await _host.RefreshRemotesAsync(ct); } catch (Exception e) when (e is InvalidOperationException or IOException) { }
            var accounts = _host.Remotes.Count(r => !AppHost.IsGeneratedType(r.Type));
            var shared = _host.SharedClientRemotes.Count;
            if (accounts > 0)
                checks.Add(shared == 0
                    ? new("Google key", "ok", $"All {accounts} Google account(s) use your own Google key.")
                    : new("Google key", "problem", $"{shared} Google account(s) are on rclone's shared Google key again: Google holds it back and everything gets slow. Cloud Drives → \"Switch to my own Google key\"."));

            // 2. The cloud drive itself.
            checks.Add(session is { State: MountState.Mounted }
                ? new("Cloud drive", "ok", $"The cloud drive is connected ({session.DriveLetter}:).")
                : new("Cloud drive", "problem", "The cloud drive is not connected. Cloud Drives → Connect."));

            // 3. The fast drive: new files must land on this PC first.
            var st = _host.Hybrid.Status;
            if (!hybrid.Enabled)
                checks.Add(new("Fast drive", "warn", "The fast drive is off: new folders and installs go straight to the cloud (minutes instead of seconds). Cloud Drives → \"Switch the fast drive on\"."));
            else if (!st.LetterReady)
                checks.Add(new("Fast drive", "problem", "The fast drive is on but has no drive letter right now."));
            else
            {
                try
                {
                    var secs = await Task.Run(() => WriteProbe(st.Root), ct);
                    var n = ProbeFolders * ProbeFilesPerFolder;
                    checks.Add(new("Fast drive", Judge(secs, SlowWriteSeconds), Judge(secs, SlowWriteSeconds) == "ok"
                        ? $"New files on the fast drive ({st.Letter}): {n} files in {ProbeFolders} folders written in {secs:0.0} s."
                        : $"Writing {n} small files to the fast drive ({st.Letter}) took {secs:0.0} s; it should take under a second."));
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    checks.Add(new("Fast drive", "problem", "The fast drive's folder could not be written to: " + e.Message));
                }
                var wanted = string.IsNullOrWhiteSpace(hybrid.Letter) ? "Z" : hybrid.Letter.Trim()[..1].ToUpperInvariant();
                if (!st.Letter.StartsWith(wanted, StringComparison.OrdinalIgnoreCase))
                    checks.Add(new("Drive letter", "warn", $"{wanted}: is held by something else until Windows restarts, so the fast drive is {st.Letter} for now."));
            }

            // 4. Opening a file that is already on this PC must not wait for Google.
            if (session is { State: MountState.Mounted })
            {
                var file = await Task.Run(() => PreparedFile(session.CacheDir, session.Root), ct);
                if (file is null)
                    checks.Add(new("Opening files", "warn", "No prepared file to time yet. Open a cloud folder in File Explorer; its files are then fetched ahead."));
                else
                {
                    try
                    {
                        var secs = await Task.Run(() => OpenProbe(file), ct);
                        checks.Add(new("Opening files", Judge(secs, SlowOpenSeconds), Judge(secs, SlowOpenSeconds) == "ok"
                            ? $"Opening a file that is ready on this PC: {secs:0.00} s."
                            : $"Opening a file that should be ready on this PC took {secs:0.0} s; it should take a fraction of a second."));
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                    {
                        checks.Add(new("Opening files", "warn", "The test file could not be opened: " + e.Message));
                    }
                }
            }

            // 5. Preparing the files of opened folders.
            var last = _host.Archives.LastReady;
            checks.Add(_host.Archives.IsPaused
                ? new("Preparing folders", "warn", "Preparing the files of opened folders is paused right now (a game is running, disk space is low, or Google asked to slow down).")
                : new("Preparing folders", "ok", last is { } l
                    ? $"The files of folders you open are fetched ahead (last: {l.At.ToLocalTime():HH:mm}, {l.Files} file(s), {ByteSize.Format(l.Bytes)})."
                    : "The files of folders you open in File Explorer are fetched ahead (none since the app started)."));

            // 6. Google refusing requests.
            if (_host.Mounts.RateLimitedWithin(TimeSpan.FromMinutes(10)))
                checks.Add(new("Google", "warn", "Google refused some requests in the last 10 minutes (\"slow down\")."));

            // 7. Leftovers that make things worse until Windows restarts.
            foreach (var d in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && !d.IsReady))
                checks.Add(new("Dead drive name", "warn", $"{d.Name.TrimEnd('\\')} is the name of a drive that did not close cleanly. It goes away when Windows restarts."));
            foreach (var s in _host.Mounts.Sessions.Where(s => s.State == MountState.Mounted))
            {
                try
                {
                    var f = new FileInfo(s.LogFile);
                    if (f.Exists && f.Length >= 63 * ByteSize.MiB && DateTime.UtcNow - f.LastWriteTimeUtc > TimeSpan.FromMinutes(15))
                        checks.Add(new("Drive log", "warn", $"The activity log of {s.DriveLetter}: stopped (an older drive process holds it). Until Windows restarts, only folders opened in File Explorer are prepared ahead."));
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }

            var report = new SpeedReport(DateTimeOffset.Now, checks);
            Last = report;
            try { File.WriteAllText(ReportFile, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true })); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            if (report.HasProblem) _log.LogWarning("Speed check: {Count} problem(s): {Names}", report.Problems.Count, string.Join(", ", report.Problems));
            else _log.LogInformation("Speed check: OK ({Count} checks, {Warnings} note(s))", checks.Count, checks.Count(c => c.State == "warn"));

            // A note only for a problem the check before did not have: no nagging every three hours about the same thing.
            var now = report.Problems.ToHashSet();
            if (regular && now.Except(_notified).Any()) ProblemFound?.Invoke(report);
            _notified = now;
            Changed?.Invoke();
            return report;
        }
        finally { _gate.Release(); }
    }

    public void Dispose() => _timer?.Dispose();
}
