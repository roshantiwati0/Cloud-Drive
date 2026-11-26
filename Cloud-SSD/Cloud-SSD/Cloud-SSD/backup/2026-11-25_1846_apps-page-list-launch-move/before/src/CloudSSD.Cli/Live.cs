using System.Diagnostics;
using System.Text.Json.Nodes;
using CloudSSD.Core;
using CloudSSD.Diagnostics;
using CloudSSD.Mount;

namespace CloudSSD.Cli;

/// <summary>
/// /live: how busy every cloud drive is right now. It asks each running rclone mount itself, read-only, through the
/// remote-control port the app gave it (the port and its password are in mounts.state.json; the password is protected
/// for this Windows user). The app's own speed graph reads the same numbers (MetricsCollector), so they agree.
/// Nothing here changes a drive.
/// </summary>
public static class Live
{
    /// <summary>A running mount as the app wrote it down: its letter, rc port and login.</summary>
    internal sealed record RcMount(string Id, string Letter, int Port, string User, string Pass, int Pid);

    /// <summary>One look at one drive.</summary>
    public sealed record DriveSample(
        string Letter, string Name, bool Answered, long Bytes, int Transferring, int UploadsInProgress, int UploadsQueued,
        long CacheBytes, long Errors, bool RateLimited);

    /// <summary>The mounts in mounts.state.json whose rclone is still running.</summary>
    internal static List<RcMount> ReadMounts(AppPaths paths)
    {
        var list = new List<RcMount>();
        try
        {
            if (!File.Exists(paths.MountStateFile)) return list;
            if (JsonNode.Parse(File.ReadAllText(paths.MountStateFile)) is not JsonArray arr) return list;
            foreach (var n in arr)
            {
                if (n is null) continue;
                var pass = SecretStore.Unprotect(n["PassProtected"]?.GetValue<string>());
                var pid = n["Pid"]?.GetValue<int>() ?? 0;
                if (pass is null || !IsRclone(pid)) continue;
                // PersistedMount.Letter is a char; System.Text.Json writes it as a one-letter string.
                list.Add(new RcMount(n["Id"]?.GetValue<string>() ?? "", (n["Letter"]?.GetValue<string>() ?? "?") + ":",
                    n["Port"]?.GetValue<int>() ?? 0, n["User"]?.GetValue<string>() ?? "", pass, pid));
            }
        }
        catch
        {
            // No readable state: no live numbers, the rest of the shell keeps working.
        }
        return list.OrderBy(m => m.Letter).ToList();
    }

    private static bool IsRclone(int pid)
    {
        try { return Process.GetProcessById(pid).ProcessName.Equals("rclone", StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    /// <summary>Asks one mount for its numbers (at most 3 s). A mount that does not answer is shown as such.</summary>
    internal static async Task<DriveSample> SampleAsync(RcMount m, string name, CancellationToken ct)
    {
        try
        {
            using var rc = new RcClient(m.Port, m.User, m.Pass, TimeSpan.FromSeconds(3));
            var core = await rc.CallAsync("core/stats", null, ct);
            var cs = CoreStats.Parse(core);
            var vs = VfsStats.Parse(await rc.CallAsync("vfs/stats", null, ct));
            return new DriveSample(m.Letter, name, true, cs.Bytes, cs.Transferring, vs.UploadsInProgress, vs.UploadsQueued,
                vs.DiskBytesUsed, cs.Errors, IsRateLimit(cs.LastError));
        }
        catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return new DriveSample(m.Letter, name, false, 0, 0, 0, 0, 0, 0, false);
        }
    }

    /// <summary>rclone's last error says Google is holding back the shared key (only the kind is used, never the text: it can hold file names).</summary>
    internal static bool IsRateLimit(string? error) =>
        error != null && (error.Contains("rateLimit", StringComparison.OrdinalIgnoreCase) || error.Contains("403") ||
                          error.Contains("quota", StringComparison.OrdinalIgnoreCase) || error.Contains("rate limit", StringComparison.OrdinalIgnoreCase));

    /// <summary>All running drives at once, with the names given to them in Cloud-SSD.</summary>
    public static async Task<List<DriveSample>> SampleAllAsync(AppPaths paths, CancellationToken ct)
    {
        var settings = Views.Settings(paths);
        var mounts = ReadMounts(paths);
        var tasks = mounts.Select(m =>
        {
            var name = settings.Mounts.FirstOrDefault(d => d.Id == m.Id)?.DisplayName ?? m.Id;
            return SampleAsync(m, name, ct);
        });
        return (await Task.WhenAll(tasks)).ToList();
    }

    /// <summary>Speed of every drive from two looks one after the other (bytes per second).</summary>
    public static Dictionary<string, double> Speeds(IReadOnlyList<DriveSample> before, IReadOnlyList<DriveSample> now, TimeSpan between)
    {
        var map = new Dictionary<string, double>();
        foreach (var n in now)
        {
            var b = before.FirstOrDefault(x => x.Letter == n.Letter);
            var secs = Math.Max(between.TotalSeconds, 0.001);
            // rclone's counter starts again when a mount restarts: then there is no speed for this round.
            map[n.Letter] = b is { Answered: true } && n.Answered && n.Bytes >= b.Bytes ? (n.Bytes - b.Bytes) / secs : 0;
        }
        return map;
    }

    // Shades, not the "▁▂▃" bars: Consolas (the classic cmd.exe font) has ░▒▓█ but shows the bars as empty boxes.
    private static readonly string[] Spark = [" ", "░", "▒", "▓", "█"];

    /// <summary>" ░▒▓█" from the last speeds (the highest one is a full block, no traffic is a space).</summary>
    public static string Sparkline(IReadOnlyList<double> values)
    {
        if (values.Count == 0) return "";
        var max = values.Max();
        if (!Ansi.Unicode) return "";
        return string.Concat(values.Select(v => max <= 0 ? Spark[0] : Spark[(int)Math.Round(v / max * (Spark.Length - 1))]));
    }

    /// <summary>The rows of the live screen.</summary>
    public static List<string> Rows(IReadOnlyList<DriveSample> now, IReadOnlyDictionary<string, double> speed,
        IReadOnlyDictionary<string, List<double>> history, bool appOn, string? phone)
    {
        var rows = new List<string>
        {
            Ansi.Gray(Ansi.Pad("Drive", 7) + Ansi.Pad("Name", 15) + Ansi.Pad("Download", 22) + Ansi.Pad("Busy", 6) +
                      Ansi.Pad("Uploads", 17) + "Cache"),
        };
        if (now.Count == 0)
            rows.Add(Ansi.Yellow("No cloud drive is running.") + Ansi.Gray(appOn ? "  The app is connecting them, or see /problem." : "  The app is off: /start"));
        foreach (var d in now)
        {
            if (!d.Answered)
            {
                rows.Add(Ansi.Accent(Ansi.Pad(d.Letter, 7)) + Ansi.Pad(Ansi.Fit(d.Name, 14), 15) + Ansi.Yellow("not answering right now"));
                continue;
            }
            var s = speed.GetValueOrDefault(d.Letter);
            var spark = history.TryGetValue(d.Letter, out var h) ? Sparkline(h) : "";
            var down = Ansi.Pad(ByteSize.Format((long)s) + "/s", 11) + Ansi.Accent(Ansi.Pad(spark, 11));
            var up = d.UploadsInProgress + d.UploadsQueued == 0 ? Ansi.Gray("none") :
                $"{d.UploadsInProgress} going{(d.UploadsQueued > 0 ? $", {d.UploadsQueued} waiting" : "")}";
            rows.Add(Ansi.Accent(Ansi.Pad(d.Letter, 7)) + Ansi.Pad(Ansi.Fit(d.Name, 14), 15) + Ansi.Pad(down, 22) +
                     Ansi.Pad(d.Transferring.ToString(), 6) + Ansi.Pad(up, 17) + ByteSize.Format(d.CacheBytes));
        }
        rows.Add("");
        rows.Add(Ansi.Gray("PC app ") + (appOn ? Ansi.Green(Ansi.Dot + " on") : Ansi.Yellow(Ansi.Ring + " off")) +
                 (phone != null ? Ansi.Gray($"{Ansi.Sep}phone ") + phone : ""));
        if (now.Any(d => d.RateLimited))
            rows.Add(Ansi.Yellow("! Google is holding back the shared key right now (rate limit), so files open slowly.") + Ansi.Gray("  Fix: /key"));
        return rows;
    }

    /// <summary>For the command line ("cloudssd live"): two looks one second apart, printed once.</summary>
    public static async Task<int> PrintOnceAsync(TextWriter o, AppPaths paths, CancellationToken ct)
    {
        var first = await SampleAllAsync(paths, ct);
        var clock = Stopwatch.StartNew();
        await Task.Delay(1000, ct);
        var second = await SampleAllAsync(paths, ct);
        var speed = Speeds(first, second, clock.Elapsed);
        foreach (var row in Rows(second, speed, new Dictionary<string, List<double>>(), Views.FindApp() != null, PhoneText(paths)))
            o.WriteLine(row);
        return 0;
    }

    internal static string? PhoneText(AppPaths paths)
    {
        var s = Views.Settings(paths);
        var p = s.MobileBridge.PairedDevices.Where(d => !d.IsBlocked).OrderByDescending(d => d.LastSeen).FirstOrDefault();
        return p is null ? null : $"{p.Name}{Ansi.Sep}seen {Views.Ago(p.LastSeen, DateTimeOffset.Now)}";
    }
}
