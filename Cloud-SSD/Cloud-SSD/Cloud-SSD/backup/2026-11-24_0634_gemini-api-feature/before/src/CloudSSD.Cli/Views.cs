using System.Diagnostics;
using CloudSSD.App;
using CloudSSD.Core;
using CloudSSD.Diagnostics;
using CloudSSD.Games;
using CloudSSD.Mount;
using Microsoft.Extensions.Logging.Abstractions;

namespace CloudSSD.Cli;

/// <summary>
/// What each command prints. Everything is written to a <see cref="TextWriter"/> (not straight to the console), so the
/// shell can hold the answer while its spinner runs and then show it under a "● Heading" with the ⎿ line.
/// </summary>
public static class Views
{
    private const int LabelWidth = 11;

    // ---- small building blocks ---------------------------------------------------------------------------------

    /// <summary>"Label      value" with the label in gray; an empty label lines the value up under the previous one.</summary>
    internal static void Row(TextWriter o, string label, string value) =>
        o.WriteLine(Ansi.Gray(label.PadRight(Math.Max(LabelWidth, label.Length + 2))) + value);

    internal static string Ago(DateTimeOffset when, DateTimeOffset now)
    {
        if (when == default) return "never";
        var d = now - when;
        if (d < TimeSpan.Zero) d = TimeSpan.Zero;
        if (d.TotalSeconds < 60) return "just now";
        if (d.TotalMinutes < 60) return $"{(int)d.TotalMinutes} min ago";
        if (d.TotalHours < 24) return $"{(int)d.TotalHours} h ago";
        if (d.TotalDays < 2) return "yesterday";
        if (d.TotalDays < 30) return $"{(int)d.TotalDays} days ago";
        return when.ToLocalTime().ToString("d MMM yyyy");
    }

    internal static Process? FindApp() =>
        Process.GetProcessesByName("Cloud-SSD").FirstOrDefault() ?? Process.GetProcessesByName("CloudPlayX").FirstOrDefault();

    internal static AppSettings Settings(AppPaths paths) => new JsonSettingsStore(paths.SettingsFile).Load();

    internal static string Letter(string? driveLetter) =>
        string.IsNullOrWhiteSpace(driveLetter) ? "" : driveLetter.Trim().TrimEnd(':', '\\').ToUpperInvariant();

    internal sealed record CloudDrive(string Letter, string Name, long Free, long Total);

    /// <summary>The cloud drives Windows shows right now: the letters set in Cloud-SSD plus any other rclone drive.</summary>
    internal static List<CloudDrive> MountedCloudDrives(AppSettings settings)
    {
        var configured = settings.Mounts
            .Where(m => Letter(m.DriveLetter).Length == 1)
            .GroupBy(m => Letter(m.DriveLetter))
            .ToDictionary(g => g.Key, g => g.First().DisplayName);
        var list = new List<CloudDrive>();
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                var letter = d.Name[..1].ToUpperInvariant();
                var ours = configured.ContainsKey(letter);
                if (!ours && d.DriveType is DriveType.Fixed or DriveType.Removable or DriveType.CDRom) continue;
                if (!d.IsReady) continue;
                if (!ours && !d.DriveFormat.Contains("rclone", StringComparison.OrdinalIgnoreCase)) continue;
                var name = configured.TryGetValue(letter, out var n) ? n : d.VolumeLabel;
                list.Add(new CloudDrive(letter + ":", string.IsNullOrWhiteSpace(name) ? "Cloud drive" : name, d.TotalFreeSpace, d.TotalSize));
            }
            catch
            {
                // A drive that vanishes while we look is simply skipped.
            }
        }
        return list.OrderBy(d => d.Letter).ToList();
    }

    // ---- the commands ------------------------------------------------------------------------------------------

    public static void Status(TextWriter o, AppPaths paths)
    {
        var s = Settings(paths);
        var now = DateTimeOffset.Now;
        var app = FindApp();
        Row(o, "PC app", app != null
            ? Ansi.Green(Ansi.Dot + " running") + Ansi.Gray($"{Ansi.Sep}PID {app.Id}{Ansi.Sep}{ByteSize.Format(app.WorkingSet64)} memory")
            : Ansi.Yellow(Ansi.Ring + " stopped") + Ansi.Gray($"{Ansi.Sep}start it with /start"));

        var drives = MountedCloudDrives(s);
        if (drives.Count == 0) Row(o, "Drives", Ansi.Gray("none connected right now (open the app, or /start)"));
        for (var i = 0; i < drives.Count; i++)
        {
            var d = drives[i];
            Row(o, i == 0 ? "Drives" : "", $"{Ansi.Accent(d.Letter)}  {Ansi.Pad(d.Name, 16)} {ByteSize.Format(d.Free)} free of {ByteSize.Format(d.Total)}");
        }

        try
        {
            var cacheRoot = paths.CacheRootFor(s);
            var disk = new DriveInfo(Path.GetPathRoot(cacheRoot) ?? "C:");
            Row(o, "Cache", $"{ByteSize.Format(s.Cache.BudgetBytes)} budget" +
                Ansi.Gray($"{Ansi.Sep}{disk.Name.TrimEnd('\\')} has {ByteSize.Format(disk.TotalFreeSpace)} free"));
        }
        catch
        {
            Row(o, "Cache", $"{ByteSize.Format(s.Cache.BudgetBytes)} budget");
        }

        if (s.AccountEmails.Count == 0) Row(o, "Accounts", Ansi.Gray("see /accounts"));
        var k = 0;
        foreach (var (remote, email) in s.AccountEmails.OrderBy(p => p.Key))
            Row(o, k++ == 0 ? "Accounts" : "", $"{email}" + Ansi.Gray($"  ({remote})"));

        var phones = s.MobileBridge.PairedDevices;
        if (phones.Count == 0) Row(o, "Phone", Ansi.Gray($"none paired (PC app {Ansi.Arrow} Mobile Hub {Ansi.Arrow} QR code)"));
        for (var i = 0; i < phones.Count; i++)
        {
            var p = phones[i];
            var state = p.IsBlocked ? Ansi.Red("blocked") : Ansi.Gray("seen " + Ago(p.LastSeen, now));
            Row(o, i == 0 ? "Phone" : "", $"{p.Name}{Ansi.Gray(Ansi.Sep)}{state}");
        }
    }

    public static void Drives(TextWriter o, AppPaths paths)
    {
        var s = Settings(paths);
        if (s.Mounts.Count == 0)
        {
            o.WriteLine($"No drives set up yet. Add one in the PC app: Cloud Drives {Ansi.Arrow} Connect.");
            return;
        }
        var mounted = MountedCloudDrives(s).Select(d => d.Letter).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var m in s.Mounts)
        {
            var letter = Letter(m.DriveLetter);
            var shown = letter.Length == 1 ? letter + ":" : "auto";
            var on = letter.Length == 1 && mounted.Contains(letter + ":");
            var state = on ? Ansi.Green(Ansi.Dot + " on") : Ansi.Gray(Ansi.Ring + " off");
            o.WriteLine($"{Ansi.Accent(Ansi.Pad(shown, 5))} {Ansi.Pad(m.DisplayName, 18)} {Ansi.Pad(state, 6)} " +
                        Ansi.Gray($"{m.RemoteSpec}{Ansi.Sep}id {m.Id}{(m.AutoMount ? Ansi.Sep + "starts by itself" : "")}{(m.ReadOnly ? Ansi.Sep + "read only" : "")}"));
        }
    }

    /// <summary>
    /// Asks rclone for the list of remotes, and nothing else. It used to start a whole AppHost for this, and that also
    /// started the phone bridge (port 8090 is the running app's: "Failed to start Cloud-SSD Mobile Bridge" in the app
    /// log), re-attached Z:/Y: and could hang on the way out.
    /// </summary>
    public static async Task Accounts(TextWriter o, AppPaths paths, CancellationToken ct = default)
    {
        var settings = Settings(paths);
        var rclone = await RcloneLocator.LocateAsync(settings.RclonePathOverride, paths.DataRoot, ct);
        if (rclone is null)
        {
            o.WriteLine(Ansi.Red("rclone was not found.") + " Run /deps to see what is missing.");
            return;
        }
        var config = new RcloneConfigService(paths, NullLogger<RcloneConfigService>.Instance);
        var remotes = await config.ListRemotesAsync(rclone, ct);
        if (remotes.Count == 0)
        {
            o.WriteLine($"No Google account connected yet. Connect one in the PC app: Cloud Drives {Ansi.Arrow} Connect.");
            return;
        }
        // Google accounts first; the "combine" / "union" remotes are not accounts but the joined views behind Y: and Z:.
        foreach (var r in remotes.OrderBy(r => r.Type == "drive" ? 0 : 1))
        {
            var email = settings.AccountEmails.TryGetValue(r.Name, out var e) ? e : "";
            var what = RemoteKind(r.Type);
            o.WriteLine(r.Type == "drive"
                ? $"{Ansi.Accent(Ansi.Pad(r.Name, 14))} {Ansi.Pad(email.Length > 0 ? email : what, 34)} {Ansi.Gray(email.Length > 0 ? what : "")}"
                : Ansi.Gray($"{Ansi.Pad(r.Name, 14)} {what}"));
        }
    }

    /// <summary>
    /// /faststart: saves each Google account's top-folder id once, so the drives mount right after the PC starts without
    /// asking Google first (RcloneConfigService.SaveRootFolderIdsAsync). Also says whether the app starts with Windows.
    /// </summary>
    public static async Task<int> FastStart(TextWriter o, AppPaths paths, CancellationToken ct = default)
    {
        var settings = Settings(paths);
        var rclone = await RcloneLocator.LocateAsync(settings.RclonePathOverride, paths.DataRoot, ct);
        if (rclone is null)
        {
            o.WriteLine(Ansi.Red("rclone was not found.") + " Run /deps to see what is missing.");
            return 1;
        }
        var config = new RcloneConfigService(paths, NullLogger<RcloneConfigService>.Instance);
        var before = RcloneConfigService.DriveRemotesWithoutRootId(await config.DumpAsync(rclone, ct));
        var saved = before.Count > 0 ? await config.SaveRootFolderIdsAsync(rclone, ct) : [];
        var left = RcloneConfigService.DriveRemotesWithoutRootId(await config.DumpAsync(rclone, ct));

        string Who(string remote) => settings.AccountEmails.TryGetValue(remote, out var e) ? $"{remote} ({e})" : remote;
        foreach (var r in saved) o.WriteLine(Ansi.Green(Ansi.Tick) + $" {Who(r)}: saved. Its drive no longer waits for Google at start.");
        foreach (var r in left) o.WriteLine(Ansi.Yellow("!") + $" {Who(r)}: Google did not answer in time. Run /faststart again in a few minutes.");
        if (before.Count == 0) o.WriteLine(Ansi.Green(Ansi.Tick) + " Already set up for every Google account.");

        var auto = StartsWithWindows();
        o.WriteLine(auto
            ? Ansi.Green(Ansi.Tick) + " The app starts by itself when you sign in to Windows (minimized), and connects the drives."
            : Ansi.Yellow("!") + $" The app does not start with Windows. Turn it on: PC app {Ansi.Arrow} Settings {Ansi.Arrow} Start with Windows.");
        if (left.Count == 0 && auto)
            o.WriteLine(Ansi.Gray("After the next restart Z: and Y: are in Files a few seconds after the app starts (before: 1-2 minutes)."));
        return left.Count == 0 ? 0 : 1;
    }

    /// <summary>True when Windows starts the app at sign-in (the Run entry the installer and the app's setting write).</summary>
    internal static bool StartsWithWindows()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            return key?.GetValueNames().Any(n => (key.GetValue(n) as string ?? "").Contains("Cloud-SSD.exe", StringComparison.OrdinalIgnoreCase)) == true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>What an rclone remote type means for the person.</summary>
    internal static string RemoteKind(string type) => type switch
    {
        "drive" => "Google Drive",
        "combine" => "all accounts side by side (one folder each)",
        "union" => "all accounts joined as one big drive",
        "alias" => "another name for one account (the joined drive while there is only one)",
        "crypt" => "encrypted folder",
        "local" => "folder on this PC",
        _ => type,
    };

    public static void Phones(TextWriter o, AppPaths paths)
    {
        var s = Settings(paths);
        var now = DateTimeOffset.Now;
        var phones = s.MobileBridge.PairedDevices;
        if (phones.Count == 0)
        {
            o.WriteLine($"No phone paired yet. PC app {Ansi.Arrow} Mobile Hub {Ansi.Arrow} scan the QR code with the phone.");
            return;
        }
        for (var i = 0; i < phones.Count; i++)
        {
            var p = phones[i];
            if (i > 0) o.WriteLine();
            var state = p.IsBlocked ? Ansi.Red(Ansi.Cross + " blocked")
                : p.GuestUntil is { } g ? Ansi.Yellow($"guest until {g.ToLocalTime():d MMM HH:mm}")
                : Ansi.Green(Ansi.Dot + " paired");
            o.WriteLine($"{Ansi.Bold(p.Name)}  {state}");
            var about = new[] { p.Platform, p.AppVersion is { Length: > 0 } v ? "app " + v : null, p.LastLink }
                .Where(x => !string.IsNullOrWhiteSpace(x));
            Row(o, "  Device", string.Join(Ansi.Sep, about.DefaultIfEmpty(p.Kind ?? "phone")!));
            Row(o, "  Last seen", Ago(p.LastSeen, now) + Ansi.Gray($"{Ansi.Sep}paired {p.PairedAt.ToLocalTime():d MMM yyyy}"));
            if (!string.IsNullOrWhiteSpace(p.IpAddress)) Row(o, "  Address", p.IpAddress);
            Row(o, "  When off", (p.WhenOffPc && p.WhenOffDevice) ? Ansi.Green("both said yes")
                : $"this PC {(p.WhenOffPc ? "yes" : "no")}{Ansi.Sep}phone {(p.WhenOffDevice ? "yes" : "no")}");
            Row(o, "  Moved", $"{ByteSize.Format(p.BytesToDevice)} to phone{Ansi.Sep}{ByteSize.Format(p.BytesFromDevice)} from phone");
        }
    }

    public static void Cache(TextWriter o, AppPaths paths)
    {
        var s = Settings(paths);
        var cacheRoot = paths.CacheRootFor(s);
        Row(o, "Folder", cacheRoot);
        Row(o, "Budget", ByteSize.Format(s.Cache.BudgetBytes) + Ansi.Gray("  (most it may use)"));
        Row(o, "Keep free", ByteSize.Format(s.Cache.MinFreeSpaceBytes) + Ansi.Gray("  (always left for Windows)"));
        Row(o, "Max age", Span(s.Cache.MaxAge) + Ansi.Gray("  (unused files leave the cache after this)"));
        Row(o, "Write-back", Span(s.Cache.WriteBack) + Ansi.Gray("  (wait before an upload starts)"));
        try
        {
            var disk = new DriveInfo(Path.GetPathRoot(cacheRoot) ?? "C:");
            var pct = disk.TotalSize > 0 ? (double)disk.TotalFreeSpace / disk.TotalSize : 0;
            var free = $"{ByteSize.Format(disk.TotalFreeSpace)} free of {ByteSize.Format(disk.TotalSize)}";
            Row(o, "Disk " + disk.Name.TrimEnd('\\'), (pct < 0.1 ? Ansi.Yellow(free) : free) + "  " + Bar(1 - pct, 20));
        }
        catch
        {
            // No disk info: the rest is still useful.
        }
    }

    /// <summary>Runs a command that may be stopped (Esc) halfway; a stop is an answer, not an error.</summary>
    internal static int Wrap(Func<int> run)
    {
        try { return run(); }
        catch (OperationCanceledException) { return 1; }
    }

    /// <summary>A time span as a person says it: "30 days", "2 h", "5 s" (not "30.00:00:00").</summary>
    internal static string Span(TimeSpan t)
    {
        if (t <= TimeSpan.Zero) return "0 s";
        if (t.TotalDays >= 1) return Num(t.TotalDays) + (t.TotalDays == 1 ? " day" : " days");
        if (t.TotalHours >= 1) return Num(t.TotalHours) + " h";
        if (t.TotalMinutes >= 1) return Num(t.TotalMinutes) + " min";
        return Num(t.TotalSeconds) + " s";

        static string Num(double v) => v % 1 == 0 ? ((long)v).ToString() : v.ToString("0.#");
    }

    /// <summary>A small fill bar: ███████░░░ (used part in accent).</summary>
    internal static string Bar(double used, int width)
    {
        used = Math.Clamp(used, 0, 1);
        var full = (int)Math.Round(used * width);
        return Ansi.Unicode
            ? Ansi.Accent(new string('█', full)) + Ansi.Faint(new string('░', width - full))
            : "[" + new string('#', full) + new string('.', width - full) + "]";
    }

    public static int Clean(TextWriter o)
    {
        var res = TempCleanup.Clean(TempCleanup.UserTempDirectory, DateTime.UtcNow);
        o.WriteLine(res.Files == 0
            ? "Nothing to clean: no old temporary files."
            : Ansi.Green(Ansi.Tick) + $" Removed {res.Files:N0} old temporary files, {ByteSize.Format(res.Bytes)} free again.");
        return 0;
    }

    public static void Mirror(TextWriter o, AppPaths paths)
    {
        var s = Settings(paths);
        var m = s.Mirror;
        Row(o, "State", m.Paused ? Ansi.Yellow("paused") + Ansi.Gray($"  (Continue in the PC app: Devices {Ansi.Arrow} When off)") : Ansi.Green(Ansi.Dot + " copying"));
        Row(o, "Account", string.IsNullOrWhiteSpace(m.Account) ? Ansi.Gray("not chosen yet") : m.Account);
        Row(o, "What", m.OnlyFolders.Count > 0 ? "only " + string.Join(", ", m.OnlyFolders) : m.IncludeSystem ? "the whole PC" : "your own files (not Windows)");
        if (m.Excluded.Count > 0) Row(o, "Left out", string.Join(", ", m.Excluded));
        Row(o, "Trash", $"kept {m.KeepTrashDays} days");
        Row(o, "Offload", (s.SmartOffload.Enabled ? "on" : "off") +
            Ansi.Gray($"{Ansi.Sep}last {(s.SmartOffload.LastOffloadTime is { } t ? Ago(t, DateTimeOffset.Now) : "never")}{Ansi.Sep}{ByteSize.Format(s.SmartOffload.TotalBytesOffloaded)} moved so far"));
    }

    public static async Task<int> Deps(TextWriter o, AppPaths paths)
    {
        var settings = Settings(paths);
        var rclone = await RcloneLocator.LocateAsync(settings.RclonePathOverride, paths.DataRoot);
        foreach (var d in DependencyChecker.CheckAll(rclone?.Path, rclone?.Version, paths.CacheRootFor(settings), settings.Cache.MinFreeSpaceBytes))
        {
            var mark = d.State switch
            {
                DependencyState.Ok => Ansi.Green(Ansi.Tick),
                DependencyState.Warning => Ansi.Yellow("!"),
                _ => Ansi.Red(Ansi.Cross),
            };
            o.WriteLine($"{Ansi.Pad(mark, 2)} {Ansi.Pad(d.Name, 16)} {d.Detail}");
            if (d.Remedy != null) o.WriteLine($"   {new string(' ', 16)} {Ansi.Gray(Ansi.Arrow + " " + d.Remedy)}");
        }
        // Always 0, as before: installer\build-release.ps1 runs "cpx deps" as a smoke test of the published folder and
        // stops on any other exit code. The check results are in the text.
        return 0;
    }

    public static int Open(TextWriter o, string? letter)
    {
        var target = Letter(letter);
        if (target.Length == 0) target = "Z";
        var root = target + ":\\";
        if (!Directory.Exists(root))
        {
            o.WriteLine(Ansi.Yellow($"Drive {target}: is not there right now.") + Ansi.Gray(" See /drives, or start the app with /start."));
            return 1;
        }
        Process.Start(new ProcessStartInfo("explorer.exe", root) { UseShellExecute = true });
        o.WriteLine(Ansi.Green(Ansi.Tick) + $" Opened {target}: in File Explorer.");
        return 0;
    }

    public static int Start(TextWriter o)
    {
        if (FindApp() is { } running)
        {
            o.WriteLine($"Cloud-SSD is already running (PID {running.Id}).");
            return 0;
        }
        var appPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Cloud-SSD", "Cloud-SSD.exe");
        if (!File.Exists(appPath))
        {
            o.WriteLine(Ansi.Red($"The app is not installed at {appPath}."));
            return 1;
        }
        Process.Start(new ProcessStartInfo(appPath) { UseShellExecute = true });
        o.WriteLine(Ansi.Green(Ansi.Tick) + " Cloud-SSD is starting. The drives come back by themselves in a few seconds.");
        return 0;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    /// <summary>Brings the PC app's window to the front, or starts the app. Returns what happened, for the person.</summary>
    internal static string ShowApp()
    {
        if (FindApp() is { } app)
        {
            var h = app.MainWindowHandle;
            if (h == IntPtr.Zero) return "PC app chal raha hai par uski window chhupi hai: taskbar ya system tray mein Cloud-SSD par click karo.";
            ShowWindow(h, 9); // SW_RESTORE: also brings it back when minimized
            SetForegroundWindow(h);
            return Ansi.Tick + " PC app saamne aa gaya.";
        }
        var o = new StringWriter();
        return Start(o) == 0 ? Ansi.Tick + " PC app chalu ho raha hai." : Ansi.Strip(o.ToString()).Trim();
    }

    public static int Stop(TextWriter o)
    {
        var list = Process.GetProcessesByName("Cloud-SSD").Concat(Process.GetProcessesByName("CloudPlayX")).ToList();
        if (list.Count == 0)
        {
            o.WriteLine("Cloud-SSD is not running.");
            return 0;
        }
        var failed = 0;
        foreach (var p in list)
        {
            try
            {
                p.CloseMainWindow();
                if (!p.WaitForExit(3000)) p.Kill();
            }
            catch (Exception ex)
            {
                failed++;
                o.WriteLine(Ansi.Red($"Could not close PID {p.Id}: {Redactor.Redact(ex.Message)}"));
            }
        }
        if (failed == 0) o.WriteLine(Ansi.Green(Ansi.Tick) + " Cloud-SSD is closed. Start it again with /start.");
        return failed == 0 ? 0 : 1;
    }

    public static int Scan(TextWriter o, string folder, double mbps)
    {
        var scan = GameScanner.Scan(folder);
        var r = CompatibilityAnalyzer.Analyze(scan, null, mbps, null, 0);
        if (r.Executable != null && PeInspector.TryRead(Path.Combine(folder, r.Executable)) is { } pe)
            CompatibilityAnalyzer.AddDependencyIssues(r, DependencyResolver.Check(pe, Path.GetDirectoryName(Path.Combine(folder, r.Executable))!, folder));
        Row(o, "Size", $"{scan.FileCount:N0} files{Ansi.Sep}{ByteSize.Format(scan.TotalBytes)}{Ansi.Sep}engine {scan.Engine ?? "unknown"}" +
            Ansi.Gray($"  ({scan.Elapsed.TotalSeconds:0.00} s)"));
        Row(o, "Result", $"{r.Summary}{Ansi.Sep}best mode: {Ansi.Accent(r.RecommendedMode.ToString())}");
        if (scan.Markers.Count() > 0) Row(o, "Markers", string.Join(", ", scan.Markers));
        var first = true;
        foreach (var e in scan.Executables)
        {
            Row(o, first ? "Programs" : "", $"{Ansi.Gray($"[{e.Score,3}]")} {e.RelativePath}" + Ansi.Gray($"  {e.Reason}"));
            first = false;
        }
        foreach (var i in r.Issues)
        {
            o.WriteLine($"{Ansi.Yellow(Ansi.Pad(i.Severity.ToString(), 8))} {i.Message}");
            if (i.Suggestion != null) o.WriteLine($"         {Ansi.Gray(Ansi.Arrow + " " + i.Suggestion)}");
        }
        return 0;
    }

    public static int Pe(TextWriter o, string file)
    {
        var pe = PeInspector.TryRead(file);
        if (pe is null)
        {
            o.WriteLine(Ansi.Red("Not a program file Windows can run (.exe / .dll)."));
            return 1;
        }
        Row(o, "Type", $"{pe.Machine}{Ansi.Sep}{pe.Subsystem}{(pe.IsDotNet ? Ansi.Sep + ".NET" : "")}");
        var first = true;
        foreach (var i in pe.AllImports)
        {
            Row(o, first ? "Needs" : "", i);
            first = false;
        }
        var dir = Path.GetDirectoryName(Path.GetFullPath(file))!;
        foreach (var d in DependencyResolver.Check(pe, dir, dir))
            o.WriteLine($"{Ansi.Red(Ansi.Cross + " missing")} {d.Dll}: {d.Problem}{(d.Remedy != null ? Ansi.Gray(" " + Ansi.Arrow + " " + d.Remedy) : "")}");
        return 0;
    }

    public static int Profile(TextWriter o, AppPaths paths, string gameId)
    {
        var store = new CloudSSD.Streaming.ProfileStore(paths.DatabaseFile);
        var s = store.Summary(gameId, TimeSpan.FromMinutes(2));
        Row(o, "Sessions", $"{s.Sessions}{Ansi.Sep}last {s.LastSession}");
        Row(o, "Start", $"{s.DistinctBlocks:N0} blocks{Ansi.Sep}about {ByteSize.Format(s.StartupBytesEstimate)} read at start");
        var first = true;
        foreach (var p in store.LoadPredictorStats(gameId))
        {
            Row(o, first ? "Guessers" : "", $"{Ansi.Pad(p.Predictor, 12)} issued {p.Issued} useful {p.Useful} wasted {p.Wasted} " +
                $"accuracy {(p.Accuracy is { } a ? a.ToString("P0") : "n/a")}{(p.Disabled ? Ansi.Yellow(" off") : "")}");
            first = false;
        }
        return 0;
    }

    /// <summary>The help page. In the shell commands are shown as "/status", on the command line as "cloudssd status".</summary>
    public static void Help(TextWriter o, bool shell)
    {
        if (!shell)
        {
            o.WriteLine(Ansi.Bold("cloudssd") + " (or cpx): Cloud-SSD in the terminal");
            o.WriteLine();
            o.WriteLine("  " + Ansi.Accent("cloudssd") + "                    open the Cloud-SSD shell (type / in it for every command)");
            o.WriteLine("  " + Ansi.Accent("cloudssd <command>") + "          run one command and come back, e.g. cloudssd status");
            o.WriteLine();
        }
        o.WriteLine(Ansi.Bold("Commands"));
        foreach (var c in CommandCatalog.All.Where(c => !c.Hidden))
        {
            if (!shell && c.Name is "clear" or "exit") continue;
            var usage = shell ? c.Usage : c.Usage.TrimStart('/');
            var also = c.Aliases.Length > 0 ? Ansi.Gray("  also: " + string.Join(", ", c.Aliases)) : "";
            o.WriteLine("  " + Ansi.Accent(Ansi.Pad(usage, 26)) + " " + c.Help + also);
        }
        if (shell)
        {
            o.WriteLine();
            o.WriteLine(Ansi.Bold("Keys"));
            Key(o, "/", "show the command list; keep typing to filter it");
            Key(o, "↑ ↓", "move in the list, or bring back earlier commands");
            Key(o, "Tab", "fill in the chosen command");
            Key(o, "Enter", "run it");
            Key(o, "Esc", "close the list, or empty the line; while a command runs: stop waiting");
            Key(o, "1 2", "answer a Yes / No question (asked before /stop and /clean)");
            Key(o, "Ctrl+L", "clean the screen");
            Key(o, "Ctrl+C", "empty the line; twice on an empty line: leave");
            o.WriteLine();
            o.WriteLine(Ansi.Bold("Or just say it") + Ansi.Gray("  (English or Hinglish)"));
            foreach (var (say, runs) in new[]
                     {
                         ("z drive kholo", "/open Z:"), ("phone dikhao", "/phone"), ("kitni jagah bachi hai", "/cache"),
                         ("kya dikkat hai", "/problem"), ("speed dikhao", "/live"), ("google key lagao", "/key"),
                         ("photo.jpg dhundho", "/find photo.jpg"), ("photo.jpg phone ko bhejo", "/send photo.jpg"),
                         ("z drive hatao", "/disconnect Z:"), ("z drive jodo", "/connect Z:"), ("google account dikhao", "/accounts"),
                         ("app band karo", "/stop"), ("safai karo", "/clean"),
                     })
                o.WriteLine("  " + Ansi.Pad(say, 24) + Ansi.Gray(Ansi.Arrow + " " + runs));
        }
        else
        {
            o.WriteLine();
            o.WriteLine(Ansi.Gray("Options: --home <dir>  use another data folder (default %LOCALAPPDATA%\\Cloud-SSD)"));
        }
    }

    private static void Key(TextWriter o, string key, string what) =>
        o.WriteLine("  " + Ansi.Accent(Ansi.Pad(Ansi.G(key, key.Replace("↑ ↓", "Up/Down")), 10)) + " " + what);
}
