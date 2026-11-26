using System.Globalization;
using System.Text;
using System.Text.Json;
using CloudSSD.Core;
using CloudSSD.Mount;

namespace CloudSSD.App.Mirror;

/// <summary>Runs rclone with these arguments; each line of its log is handed over as it comes. (Tests give their own.)</summary>
public delegate Task<RcloneResult> RcloneRun(IReadOnlyList<string> args, Action<string>? onLogLine, CancellationToken ct);

/// <summary>
/// What one batch did: the files that are in the cloud now, the ones that failed and why, whether the account's daily
/// upload limit was hit, and whether Google refused for too many requests (the key this app signs in with is shared).
/// </summary>
public sealed record MirrorBatchResult(IReadOnlyList<MirrorFile> Done, IReadOnlyList<(MirrorFile File, string Reason)> Failed, bool UploadLimit, string? Error,
    bool RateLimit = false);

/// <summary>
/// Copies batches of files to the device's folder in the cloud with a process of rclone of its own (not through the
/// mounted drive: its cache and a running game's reads are left alone). Layout under <c>Cloud-SSD Devices/&lt;device&gt;/</c>:
/// <c>files/C/…</c> the copies, <c>trash/&lt;date&gt;/C/…</c> what was replaced or deleted, <c>device.json</c> the state.
/// </summary>
public sealed class MirrorUploader
{
    private readonly RcloneRun _run;
    private readonly string _base;
    private readonly string _workDir;
    private readonly MirrorSettings _settings;
    private readonly Func<DateTimeOffset> _now;

    /// <param name="remoteBase">"gdrive:Cloud-SSD Devices/ab12cd34 MY-PC" (the account's remote and the device's folder).</param>
    public MirrorUploader(RcloneRun run, string remoteBase, string workDir, MirrorSettings settings, Func<DateTimeOffset>? now = null)
    {
        _run = run;
        _base = remoteBase.TrimEnd('/');
        _workDir = workDir;
        _settings = settings;
        _now = now ?? (() => DateTimeOffset.Now);
    }

    private string Today => _now().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>The speed limit as rclone wants it ("--bwlimit 2.5M" is megabytes a second).</summary>
    private IEnumerable<string> Limit() => _settings.UploadLimitMbps > 0
        ? ["--bwlimit", (_settings.UploadLimitMbps / 8).ToString("0.###", CultureInfo.InvariantCulture) + "M"]
        : [];

    private string ListFile(string name, IEnumerable<string> lines)
    {
        Directory.CreateDirectory(_workDir);
        var path = Path.Combine(_workDir, name);
        File.WriteAllLines(path, lines, new UTF8Encoding(false));
        return path;
    }

    /// <summary>
    /// Copies files of one drive (<paramref name="driveRoot"/>, "C:\"). A file that is already there unchanged costs
    /// nothing; one that changed goes to the trash folder of today first.
    /// </summary>
    public async Task<MirrorBatchResult> CopyAsync(string driveRoot, IReadOnlyList<MirrorFile> files, CancellationToken ct)
    {
        if (files.Count == 0) return new MirrorBatchResult([], [], false, null);
        var drive = MirrorScope.DriveName(driveRoot);
        string Rel(string path) => path[driveRoot.Length..].Replace('\\', '/').TrimStart('/');
        var byRel = new Dictionary<string, MirrorFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in files) byRel[Rel(f.Path)] = f;
        var list = ListFile("copy-" + drive + ".txt", byRel.Keys);

        var args = new List<string>
        {
            "copy", driveRoot, $"{_base}/files/{drive}", "--files-from-raw", list, "--no-traverse",
            "--backup-dir", $"{_base}/trash/{Today}/{drive}",
            "--transfers", "4", "--checkers", "8", "--tpslimit", "8", "--retries", "1",
            "--drive-stop-on-upload-limit", "--use-json-log", "-v", "--stats", "30s"
        };
        args.AddRange(Limit());

        var copied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var failed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        bool limit = false, rate = false;
        string? fatal = null;
        void OnLine(string line)
        {
            if (line.Length == 0 || line[0] != '{') return;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var o = doc.RootElement;
                var level = o.TryGetProperty("level", out var l) ? l.GetString() ?? "" : "";
                var msg = o.TryGetProperty("msg", out var m) ? m.GetString() ?? "" : "";
                var obj = o.TryGetProperty("object", out var ob) && ob.ValueKind == JsonValueKind.String ? ob.GetString() ?? "" : "";
                if (IsUploadLimit(msg)) limit = true;
                else if (IsRateLimit(msg)) rate = true;
                if (level is "error" or "critical" or "fatal")
                {
                    if (obj.Length > 0 && byRel.ContainsKey(obj)) failed[obj] = ReasonOf(msg);
                    else fatal ??= msg;
                }
                else if (obj.Length > 0 && msg.StartsWith("Copied", StringComparison.Ordinal)) copied.Add(obj);
            }
            catch (JsonException) { }
        }

        RcloneResult result;
        try { result = await _run(args, OnLine, ct); }
        finally { try { File.Delete(list); } catch (IOException) { } }

        // A file that went away since the disk was looked at is passed over by rclone without a word: it has no copy.
        foreach (var p in byRel)
            if (!copied.Contains(p.Key) && !failed.ContainsKey(p.Key) && !File.Exists(p.Value.Path)) failed[p.Key] = "missing";

        // Exit code 0: every file is there (the unchanged ones made no line). Otherwise only what rclone said it copied.
        var done = result.Success
            ? byRel.Where(p => !failed.ContainsKey(p.Key)).Select(p => p.Value).ToList()
            : byRel.Where(p => copied.Contains(p.Key) && !failed.ContainsKey(p.Key)).Select(p => p.Value).ToList();
        var bad = failed.Select(p => (byRel[p.Key], p.Value)).ToList();
        if (!result.Success && !limit && IsRateLimit(result.StdErr)) rate = true;
        string? error = result.Success || bad.Count > 0 && fatal == null ? null
            : fatal ?? (result.TimedOut ? "rclone took too long" : RcloneConfigService.ExplainError(result.StdErr));
        return new MirrorBatchResult(done, bad, limit, error, rate && !limit);
    }

    /// <summary>The account may not take more today (Google's 750 GB a day), or it is full.</summary>
    private static bool IsUploadLimit(string text) =>
        text.Contains("upload limit", StringComparison.OrdinalIgnoreCase) || text.Contains("uploadLimitExceeded", StringComparison.OrdinalIgnoreCase)
        || text.Contains("storageQuotaExceeded", StringComparison.OrdinalIgnoreCase);

    /// <summary>Google refused for too many requests in a minute: what happens with rclone's shared key, not with the person's own.</summary>
    private static bool IsRateLimit(string text) =>
        text.Contains("RATE_LIMIT_EXCEEDED", StringComparison.OrdinalIgnoreCase) || text.Contains("Quota exceeded for quota metric", StringComparison.OrdinalIgnoreCase)
        || text.Contains("rateLimitExceeded", StringComparison.OrdinalIgnoreCase);

    /// <summary>Why rclone could not copy a file, in the mirror's words.</summary>
    internal static string ReasonOf(string message)
    {
        if (message.Contains("being used by another process", StringComparison.OrdinalIgnoreCase)
            || message.Contains("sharing violation", StringComparison.OrdinalIgnoreCase)
            || message.Contains("locked a portion", StringComparison.OrdinalIgnoreCase)) return "in-use";
        if (message.Contains("Access is denied", StringComparison.OrdinalIgnoreCase)
            || message.Contains("privilege", StringComparison.OrdinalIgnoreCase)) return "denied";
        if (message.Contains("cannot find", StringComparison.OrdinalIgnoreCase)
            || message.Contains("no such file", StringComparison.OrdinalIgnoreCase)) return "missing";
        return "other";
    }

    /// <summary>
    /// Files that are gone from the PC: their copies move from <c>files/</c> to today's trash folder (inside the
    /// cloud, nothing is downloaded). Returns false when rclone failed; the files then stay listed and are tried again.
    /// </summary>
    public async Task<bool> TrashAsync(string driveRoot, IReadOnlyList<string> paths, CancellationToken ct)
    {
        if (paths.Count == 0) return true;
        var drive = MirrorScope.DriveName(driveRoot);
        var list = ListFile("gone-" + drive + ".txt", paths.Select(p => p[driveRoot.Length..].Replace('\\', '/').TrimStart('/')));
        try
        {
            var r = await _run(["move", $"{_base}/files/{drive}", $"{_base}/trash/{Today}/{drive}", "--files-from-raw", list, "--no-traverse",
                "--tpslimit", "8", "--retries", "1", "--use-json-log", "-v"], null, ct);
            return r.Success;
        }
        finally { try { File.Delete(list); } catch (IOException) { } }
    }

    /// <summary>Removes trash folders older than the days the settings keep them (really removed, not into Google's own trash).</summary>
    public async Task CleanTrashAsync(IReadOnlyList<string> trashDays, CancellationToken ct)
    {
        var oldest = _now().Date.AddDays(-Math.Max(1, _settings.KeepTrashDays));
        foreach (var day in trashDays)
        {
            if (!DateTime.TryParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) || d >= oldest) continue;
            await _run(["purge", $"{_base}/trash/{day}", "--drive-use-trash=false", "--tpslimit", "8"], null, ct);
        }
    }

    /// <summary>Writes a small text file at the top of the device's folder (device.json: the state the other side reads).</summary>
    public async Task<bool> WriteTextAsync(string name, string text, CancellationToken ct)
    {
        var local = ListFile(name, [text]);
        try { return (await _run(["copyto", local, $"{_base}/{name}", "--tpslimit", "8", "--retries", "2"], null, ct)).Success; }
        finally { try { File.Delete(local); } catch (IOException) { } }
    }
}
