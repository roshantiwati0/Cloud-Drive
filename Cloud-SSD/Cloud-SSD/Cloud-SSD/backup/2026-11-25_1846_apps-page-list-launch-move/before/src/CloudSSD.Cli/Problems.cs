using System.Globalization;
using System.Text.RegularExpressions;
using CloudSSD.Core;
using CloudSSD.Mount;
using Microsoft.Extensions.Logging.Abstractions;

namespace CloudSSD.Cli;

/// <summary>
/// /problem: what is wrong right now and what to do, in simple Hinglish. It reads the app's own log (never rclone's
/// logs, and never prints a log line: they can hold file names) and looks at the PC as it is now.
/// </summary>
public static partial class Problems
{
    public enum Level { Info, Warn, Bad }

    /// <summary>One thing found: how bad, what it is, why it happens, what to do, how often and when last.</summary>
    public sealed record Finding(Level Level, string Title, string Why, string? Do, int Count = 1, DateTime? Last = null);

    private sealed record Rule(string Key, Regex Pattern, Level Level, string Title, string Why, string? Do);

    // The kinds of log lines that mean something to the person. Matched on the message, never shown.
    private static readonly Rule[] Rules =
    [
        new("rate", new Regex(@"rate.?limit|RATE_LIMIT|\b403\b|quota|token bucket", RegexOptions.IgnoreCase), Level.Warn,
            "Google drive ko dheema kar raha hai (rate limit)",
            "Aapke accounts rclone ki shared Google key se jude hain. Use bahut log use karte hain, isliye Google baar-baar rokta hai, aur file khulne mein der lagti hai.",
            "/key  (apni Google key lagao, free, ek baar ka 5 minute ka kaam)"),
        new("restart", new Regex(@"Automatic restart of mount .* failed|exited unexpectedly", RegexOptions.IgnoreCase), Level.Warn,
            "Drive beech mein ruk gayi thi",
            "Drive chalane wala rclone band ho gaya tha. App use khud dobara chalata hai.",
            "/live se dekho ki drive abhi chal rahi hai. Baar-baar ho to wajah aksar Google ka rate limit hai: /key"),
        new("late", new Regex(@"did not appear within|didn't became available", RegexOptions.IgnoreCase), Level.Info,
            "Drive letter (Z:/Y:) der se aaya",
            "Google dheema tha, isliye Windows mein drive dikhne mein der lagi. Aksar apne aap aa jaata hai.",
            "/drives dekho. Na aaye to /stop, phir /start."),
        new("bridge", new Regex(@"Failed to start Cloud-SSD Mobile Bridge on port", RegexOptions.IgnoreCase), Level.Info,
            "Phone wala darwaza (port 8090) pehle se khula tha",
            "Kisi doosre program ne wahi darwaza kholne ki koshish ki (jaise purana cpx). App chal raha hai aur phone judta hai to chinta ki baat nahi.",
            "Phone na jude to app band karke chalu karo: /stop, phir /start."),
        new("config", new Regex(@"accounts file was missing or empty", RegexOptions.IgnoreCase), Level.Info,
            "Accounts ki file khaali mili thi, purani sahi copy wapas lagayi gayi",
            "Saare account jude rahe. Aisa tab hota hai jab PC achanak band ho jaye.",
            null),
        new("statfs", new Regex(@"Statfs failed", RegexOptions.IgnoreCase), Level.Info,
            "Z: ka size kabhi galat dikh sakta hai",
            "Sirf dikhne ki baat hai. Aapka data safe hai.",
            null),
        new("about", new Regex(@"could not be reached", RegexOptions.IgnoreCase), Level.Info,
            "Ek account ki jagah (storage) padhi nahi ja saki",
            "Google us waqt dheema tha.",
            "Thodi der baad /status"),
    ];

    [GeneratedRegex(@"^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2})\.\d+ (ERR|WRN|INF) (.*)$")]
    private static partial Regex LogLine();

    /// <summary>
    /// What the app's log says (only ERR / WRN lines of the last <paramref name="within"/>). Pure, so it can be tested
    /// with made-up lines. Lines that match no rule are counted as "other".
    /// </summary>
    public static List<Finding> FromLog(IEnumerable<string> lines, DateTime now, TimeSpan within)
    {
        var hits = new Dictionary<string, (int Count, DateTime Last)>();
        var other = 0;
        DateTime otherLast = default;
        foreach (var line in lines)
        {
            var m = LogLine().Match(line);
            if (!m.Success || m.Groups[2].Value == "INF") continue;
            if (!DateTime.TryParseExact(m.Groups[1].Value, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var at)) continue;
            if (now - at > within) continue;
            var rule = Rules.FirstOrDefault(r => r.Pattern.IsMatch(m.Groups[3].Value));
            if (rule is null)
            {
                if (m.Groups[2].Value == "ERR") { other++; if (at > otherLast) otherLast = at; }
                continue;
            }
            var h = hits.GetValueOrDefault(rule.Key);
            hits[rule.Key] = (h.Count + 1, at > h.Last ? at : h.Last);
        }

        var list = new List<Finding>();
        foreach (var r in Rules)
        {
            if (!hits.TryGetValue(r.Key, out var h)) continue;
            // Many rate limits (or a drive stopping again and again) are not a passing thing any more, unless the last one
            // was hours ago: then it is history, worth knowing but not a problem now.
            var recent = now - h.Last < TimeSpan.FromHours(2);
            var level = r.Key is "rate" or "restart" && h.Count >= 5 && recent ? Level.Bad : r.Level;
            list.Add(new Finding(level, r.Title, r.Why, r.Do, h.Count, h.Last));
        }
        if (other > 0)
            list.Add(new Finding(Level.Info, $"Log mein {other} aur galti", "Inka matlab yahan pehchaana nahi gaya.",
                $"Poora log: %LOCALAPPDATA%\\Cloud-SSD\\logs (PC app {Ansi.Arrow} Diagnostics bhi)", other, otherLast));
        return list;
    }

    /// <summary>The lines of today's and yesterday's app log (read while the app keeps writing to it).</summary>
    internal static List<string> ReadAppLog(AppPaths paths, DateTime now)
    {
        var lines = new List<string>();
        foreach (var day in new[] { now.AddDays(-1), now })
        {
            var file = Path.Combine(paths.DataRoot, "logs", $"cloudssd-{day:yyyyMMdd}.log");
            if (!File.Exists(file)) continue;
            try
            {
                using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(fs);
                while (reader.ReadLine() is { } l) lines.Add(l);
            }
            catch (IOException)
            {
                // A log that cannot be read is skipped.
            }
        }
        return lines;
    }

    /// <summary>How the PC is right now: app, drives, shared Google key, free space.</summary>
    internal static async Task<List<Finding>> CheckNowAsync(AppPaths paths, CancellationToken ct)
    {
        var list = new List<Finding>();
        var s = Views.Settings(paths);
        var appOn = Views.FindApp() != null;
        if (!appOn)
            list.Add(new Finding(Level.Bad, "Cloud-SSD app band hai", "App band ho to drives apne aap nahi judti aur phone PC tak nahi pahunchta.", "/start"));

        var mounted = Views.MountedCloudDrives(s).Select(d => d.Letter).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var m in s.Mounts.Where(m => m.AutoMount && Views.Letter(m.DriveLetter).Length == 1))
        {
            var letter = Views.Letter(m.DriveLetter) + ":";
            if (mounted.Contains(letter)) continue;
            list.Add(new Finding(appOn ? Level.Warn : Level.Info, $"{letter} ({m.DisplayName}) abhi judi nahi hai",
                appOn ? "App use jodne ki koshish karta rehta hai. Google dheema ho to kuch minute lagte hain." : "App chalu hote hi judegi.",
                appOn ? "Thodi der baad /live. Na jude to /stop, phir /start." : "/start"));
        }

        try
        {
            var rclone = await RcloneLocator.LocateAsync(s.RclonePathOverride, paths.DataRoot, ct);
            if (rclone != null)
            {
                var config = new RcloneConfigService(paths, NullLogger<RcloneConfigService>.Instance);
                var shared = RcloneConfigService.SharedClientDriveRemotes(await config.DumpAsync(rclone, ct));
                if (shared.Count > 0)
                    list.Add(new Finding(Level.Warn, $"{shared.Count} Google account abhi shared key par hain",
                        "Shared key ki wajah se Google rokta hai (drive dheemi), aur rclone 2026 mein yeh key band kar raha hai. Tab yeh drives chalna band ho jayengi.",
                        "/key  (apni Google key, ek baar)"));
                try
                {
                    if (SlowStart(await config.DumpAsync(rclone, ct)) is { } slow) list.Add(slow);
                }
                catch (MissingMethodException)
                {
                    // This cpx next to an older installed app (no such check in its CloudSSD.Mount.dll yet).
                }
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Could not ask rclone: this check is left out, the others still run.
        }

        try
        {
            var disk = new DriveInfo(Path.GetPathRoot(paths.CacheRootFor(s)) ?? "C:");
            if (disk.TotalSize > 0 && disk.TotalFreeSpace < disk.TotalSize / 10)
                list.Add(new Finding(Level.Warn, $"{disk.Name.TrimEnd('\\')} mein jagah kam hai ({ByteSize.Format(disk.TotalFreeSpace)} bachi)",
                    "Jagah bahut kam ho to cache kaam nahi karta aur Windows bhi dheema hota hai.", "/clean, ya /cache dekho"));
        }
        catch
        {
            // No disk info: skipped.
        }
        return list;
    }

    /// <summary>Accounts that still make the drives wait for Google at every start of the PC (see /faststart).</summary>
    internal static Finding? SlowStart(System.Text.Json.Nodes.JsonObject dump)
    {
        var n = RcloneConfigService.DriveRemotesWithoutRootId(dump).Count;
        return n == 0 ? null : new Finding(Level.Warn, "Laptop chalu hone par drives der se aati hain (1-2 minute)",
            $"{n} account ke liye rclone har baar Google se ek cheez poochta hai, aur Google use rokta hai. Tab tak Z:/Y: Files mein nahi dikhti.",
            "/faststart  (ek baar; phir drive app chalu hote hi aa jaati hai)");
    }

    public static async Task<int> ShowAsync(TextWriter o, AppPaths paths, CancellationToken ct)
    {
        var now = DateTime.Now;
        var findings = await CheckNowAsync(paths, ct);
        var fromLog = FromLog(ReadAppLog(paths, now), now, TimeSpan.FromHours(24));
        // "Shared key" and "rate limit" say the same thing twice: keep the rate limit (with its count) and drop the other.
        if (fromLog.Any(f => f.Title.StartsWith("Google drive ko dheema", StringComparison.Ordinal)))
            findings.RemoveAll(f => f.Title.Contains("shared key par", StringComparison.Ordinal));
        findings.AddRange(fromLog);
        Write(o, findings.OrderByDescending(f => f.Level).ToList(), now);
        return 0;
    }

    internal static void Write(TextWriter o, IReadOnlyList<Finding> findings, DateTime now)
    {
        if (findings.Count == 0)
        {
            o.WriteLine(Ansi.Green(Ansi.Tick) + " Koi dikkat nahi mili. App chal raha hai, drives judi hain, log mein 24 ghante se koi galti nahi.");
            return;
        }
        var bad = findings.Count(f => f.Level != Level.Info);
        o.WriteLine(bad == 0
            ? Ansi.Green(Ansi.Tick) + " Koi badi dikkat nahi. Kuch chhoti baatein:"
            : Ansi.Yellow($"{bad} cheez(ein) dekhni chahiye:"));
        foreach (var f in findings)
        {
            o.WriteLine();
            var mark = f.Level switch { Level.Bad => Ansi.Red(Ansi.Cross), Level.Warn => Ansi.Yellow("!"), _ => Ansi.Gray("i") };
            var when = f.Last is { } last ? Ansi.Gray($"  ({(f.Count > 1 ? $"{f.Count} baar, " : "")}aakhri {Views.Ago(last, now).Replace("ago", "pehle").Replace("just now", "abhi").Replace("yesterday", "kal")})") : "";
            o.WriteLine($"{mark} {Ansi.Bold(f.Title)}{when}");
            o.WriteLine("  " + Ansi.Gray(f.Why));
            if (f.Do != null) o.WriteLine("  " + Ansi.Accent(Ansi.Arrow + " Kya karein: ") + f.Do);
        }
    }
}
