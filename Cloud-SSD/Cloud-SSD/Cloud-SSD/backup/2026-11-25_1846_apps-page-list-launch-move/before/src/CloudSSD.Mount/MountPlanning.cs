using System.Globalization;
using CloudSSD.Core;

namespace CloudSSD.Mount;

public static class DriveLetterAllocator
{
    private static readonly char[] Preference = "GXYZWVUTSRQPONMLKJIH".ToCharArray();

    public static IReadOnlySet<char> UsedLetters()
    {
        var used = new HashSet<char>();
        foreach (var d in DriveInfo.GetDrives()) used.Add(char.ToUpperInvariant(d.Name[0]));
        return used;
    }

    public static IReadOnlyList<char> FreeLetters(IReadOnlySet<char>? used = null)
    {
        used ??= UsedLetters();
        return "DEFGHIJKLMNOPQRSTUVWXYZ".Where(c => !used.Contains(c)).ToList();
    }

    /// <summary>Returns the requested letter if free, otherwise the first free letter by preference (G first, then X..H).</summary>
    public static char? Choose(string? requested, IReadOnlySet<char>? used = null)
    {
        used ??= UsedLetters();
        if (!string.IsNullOrWhiteSpace(requested))
        {
            var c = char.ToUpperInvariant(requested.Trim()[0]);
            if (c is >= 'D' and <= 'Z' && !used.Contains(c)) return c;
        }
        foreach (var c in Preference) if (!used.Contains(c)) return c;
        return null;
    }
}

public sealed record MountPlan(IReadOnlyList<string> Arguments, IReadOnlyDictionary<string, string?> Environment, string CacheDir, string LogFile,
    long EffectiveCacheMaxBytes, IReadOnlyList<string> Warnings);

public sealed record MountPlanInput(
    MountDefinition Mount,
    char DriveLetter,
    CacheSettings Cache,
    NetworkSettings Network,
    string CacheDir,
    string LogFile,
    long EffectiveCacheMaxBytes,
    bool Telemetry,
    int RcPort,
    string? RemoteType);

/// <summary>
/// Translates Cloud-SSD settings into rclone mount arguments. Every optional flag is checked against the flags the
/// installed rclone reports, so an older rclone gets a warning instead of a failed mount.
/// </summary>
public static class MountFlagBuilder
{
    public static MountPlan Build(MountPlanInput i, RcloneInfo rclone)
    {
        var warnings = new List<string>();
        var a = new List<string> { "mount", i.Mount.RemoteSpec, $"{i.DriveLetter}:" };

        void Flag(string name, string? value = null)
        {
            if (!rclone.Supports(name))
            {
                warnings.Add($"rclone {rclone.Version} does not support --{name}; skipped.");
                return;
            }
            a.Add("--" + name);
            if (value != null) a.Add(value);
        }

        // "full" downloads a whole file when it is opened (great for games, hopeless for archives: a 460 MB zip
        // had to be downloaded completely before Windows could show its file list). "writes" serves reads straight
        // from the cloud, so an archive opens with one request. The user can switch (Cloud Drives -> Fast browsing).
        Flag("vfs-cache-mode", i.Cache.VfsCacheMode.Equals("writes", StringComparison.OrdinalIgnoreCase) ? "writes" : "full");
        Flag("cache-dir", i.CacheDir);
        Flag("vfs-cache-max-size", ByteSize.ToRclone(Math.Max(16 * ByteSize.MiB, i.EffectiveCacheMaxBytes)));
        if (i.Cache.MinFreeSpaceBytes > 0) Flag("vfs-cache-min-free-space", ByteSize.ToRclone(i.Cache.MinFreeSpaceBytes));
        Flag("vfs-cache-max-age", Dur(i.Cache.MaxAge));
        Flag("vfs-cache-poll-interval", Dur(i.Cache.PollInterval));
        // 0 is a deliberate default, not "unset": in full cache mode rclone downloads buffer-size + read-ahead past
        // every read position, so 32Mi + 16Mi turned each small random game read into up to 48 MiB of speculative
        // download (3 x 64 KiB reads fetched a whole 24 MiB file; BENCHMARKS.md: ~3x slower cold launches). The
        // prediction engine owns read-ahead; users on very fast links can still raise both in Settings.
        Flag("buffer-size", ByteSize.ToRclone(i.Cache.BufferSizeBytes));
        Flag("vfs-read-ahead", ByteSize.ToRclone(i.Cache.ReadAheadBytes));
        // One request per chunk, and every request can hit Google Drive's quota. A small chunk keeps a small read
        // small: Windows reads an archive's file list from the END of the file, and with a 32 MiB chunk that single
        // probe pulled 32 MiB (measured: 21.5 s instead of 5.3 s). 4 MiB is the balanced value.
        var chunkSize = i.Cache.ReadChunkSizeBytes > 0 ? i.Cache.ReadChunkSizeBytes : 4 * ByteSize.MiB;
        Flag("vfs-read-chunk-size", ByteSize.ToRclone(chunkSize));
        var chunkStreams = i.Cache.ReadChunkStreams > 0 ? i.Cache.ReadChunkStreams : 1;
        if (chunkStreams > 1) Flag("vfs-read-chunk-streams", chunkStreams.ToString(CultureInfo.InvariantCulture));
        Flag("vfs-write-back", Dur(i.Cache.WriteBack));
        Flag("dir-cache-time", "72h");
        Flag("attr-timeout", "24h");
        Flag("poll-interval", "1m");
        Flag("vfs-fast-fingerprint");
        // Must match RcloneMountService.HandleCachingWindow (6 s), which graceful unmount and Prepare's readiness check
        // wait on. rclone writes a file's range index only when the cached handle really closes, and a cached handle
        // pins the item against eviction ("Grace period item skipped"). With 30s, quitting within 30 s of a read lost
        // the index (re-download on next start), and under cache/disk pressure rclone reset the file being read
        // instead of the older, closed ones.
        Flag("vfs-handle-caching", "5s");
        // No --vfs-disk-space-total-size: a fixed "15T" made every drive claim 15 TB in File Explorer. Without it rclone
        // reports the account's real quota (for a combined drive, the sum of its accounts).
        //
        // --vfs-refresh stays. It walks the whole drive tree at every mount, which costs Drive API requests (the
        // shared quota is tight), but WITHOUT it a directory listing that failed once (for example because the quota
        // was exhausted) stays cached as empty for --dir-cache-time (72 h): the folder then shows up empty although
        // every file is still in the cloud. Seen for real: "Google AI Studio" came up empty after a mount without
        // the refresh, while the same folder listed fine straight from the remote. A wrong listing is much worse
        // than a slow mount, so the refresh stays. The read speed comes from the chunk size instead.
        Flag("vfs-refresh");
        // The chunk GROWS while a file is read from start to end ("off" = no upper limit): 4, 8, 16, 32 MiB …
        // What counts on Google Drive is the NUMBER of requests, not their size: every request can be answered with
        // "403 quota exceeded" (rclone's shared key: about every second one), and after each such answer rclone
        // sleeps up to 16 s before the next request of that account. With the limit equal to the chunk size (the
        // setting before 17 Nov 2026) a file needed one request per 4 MiB - 14 for a 54 MB file, 125 for a 500 MB
        // installer. Measured on the real drive: that 54 MB file took 259 s (0.21 MB/s on a 9.25 MB/s line, 32
        // quota answers). Growing chunks need 4 requests for 54 MB and 7 for 500 MB.
        // A small read stays small either way: a chunk only grows when the SAME stream is read on past its end, so
        // the probe Windows makes at the end of an archive is still one 4 MiB request. (The earlier "limit 256 MiB
        // -> 38 s, limit 4 MiB -> 4 s" comparison had the same amount of data coming down both times: the 34 s
        // were quota sleeps that happened to fall into one run, not the chunk size.)
        Flag("vfs-read-chunk-size-limit", "off");
        Flag("no-checksum");
        // Without --links rclone refuses symlinks: every mount ended with "symlinks not supported without
        // the --links flag: /" and the drive root stayed a symlink, so the mount needed far longer than
        // the 45 s readiness check to show up and Statfs failed ("union root '': Statfs failed: this usage
        // field is not supported") - File Explorer then showed no space for the drive. With --links the
        // root resolves, the drive appears in ~15 s and the real quota (union of all accounts) is reported.
        // rclone stores such symlinks as .rclonelink files, so the cloud data stays untouched.
        Flag("links");
        // The copies devices keep of themselves ("Files when it is off") are not part of the drive: hidden here so a
        // device that may only browse the drives cannot read them, and so the mount does not load their huge file lists.
        Flag("filter", "- Cloud-SSD Devices/**");
        Flag("filter", "- desktop.ini");
        Flag("filter", "- folder.jpg");
        Flag("filter", "- folder.gif");
        Flag("filter", "- Thumbs.db");
        Flag("filter", "- AlbumArt*");
        // Executables need the execute bit in the WinFsp ACL mapping; default 0666 makes CreateProcess fail with
        // "Access is denied" (verified on rclone 1.75.2 / WinFsp 2025).
        Flag("file-perms", "0777");
        Flag("dir-perms", "0777");
        Flag("volname", Sanitize($"Cloud Drive {i.Mount.DisplayName}"));
        if (i.Mount.ReadOnly) Flag("read-only");

        // Google Drive pacer safety and Windows encoding compatibility
        Flag("drive-pacer-min-sleep", "100ms");
        Flag("drive-pacer-burst", "10");
        Flag("drive-acknowledge-abuse");
        Flag("drive-encoding", "Slash,LtGt,DoubleQuote,Colon,Question,Asterisk,Pipe,BackSlash,Del,Ctl,RightSpace,RightPeriod,InvalidUtf8,Dot");

        if (i.Network.HardLimitMbps > 0)
            Flag("bwlimit", ((long)Bandwidth.MbpsToBytesPerSecond(i.Network.HardLimitMbps)).ToString(CultureInfo.InvariantCulture) + "B");
        // --tpslimit is one global limiter shared by every HTTP transaction (listings, per-file HEADs, chunk GETs). It
        // protects Google Drive API quotas, so it applies to Drive only (NetworkSettings default 10) and 0 keeps its
        // documented meaning "unlimited". Forcing 10 on every remote let the startup directory walks (--vfs-refresh and
        // the rc vfs/refresh after mount) starve data reads: cold throughput fell to ~20 % of the link, a walk took 25 s
        // instead of 60 ms, and an in-flight walk delayed rclone's shutdown by ~11 s.
        // Every cloud API remote keeps the limit, including unions/crypts built on Drive accounts (the real Z: drive is a
        // union of three); only quota-free HTTP/local sources (the simulated cloud in tests) skip it.
        if (i.Network.TpsLimit > 0 && i.RemoteType is not ("http" or "local"))
            Flag("tpslimit", i.Network.TpsLimit.ToString(CultureInfo.InvariantCulture));

        Flag("rc");
        Flag("rc-addr", $"127.0.0.1:{i.RcPort}");
        Flag("log-file", i.LogFile);
        Flag("log-format", "date,time,microseconds");
        if (rclone.Supports("log-file-max-size")) { Flag("log-file-max-size", "64Mi"); Flag("log-file-max-backups", "2"); }
        a.Add(i.Telemetry ? "-vv" : "-v");

        foreach (var (k, v) in i.Mount.ExtraFlags)
        {
            var name = k.TrimStart('-');
            if (IsForbiddenExtraFlag(name))
            {
                warnings.Add($"Extra flag --{name} is not allowed and was ignored.");
                continue;
            }
            Flag(name, string.IsNullOrEmpty(v) ? null : v);
        }

        return new MountPlan(a, new Dictionary<string, string?>(), i.CacheDir, i.LogFile, i.EffectiveCacheMaxBytes, warnings);
    }

    /// <summary>Flags that could execute commands, expose the rc server or redirect credentials.</summary>
    public static bool IsForbiddenExtraFlag(string name) =>
        name.Contains("password-command", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("rc", StringComparison.OrdinalIgnoreCase)
        || name.Equals("config", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("cache-dir", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("log-file", StringComparison.OrdinalIgnoreCase)
        || name.Contains("command", StringComparison.OrdinalIgnoreCase);

    public static string Dur(TimeSpan t) => t.TotalHours >= 1 && t.TotalHours == Math.Floor(t.TotalHours)
        ? $"{(long)t.TotalHours}h"
        : t.TotalSeconds >= 1 ? $"{(long)t.TotalSeconds}s" : $"{(long)t.TotalMilliseconds}ms";

    private static string Sanitize(string s) => new(s.Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_').Take(32).ToArray());
}
