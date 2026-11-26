using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using CloudSSD.Core;
using Microsoft.Extensions.Logging;

namespace CloudSSD.App;

/// <summary>
/// Makes the files of a folder open at once. The first time a file on a cloud drive is opened, somebody has to wait
/// for the provider: measured on 17 Nov 2026 (own Google key, 9.9 MB/s line), the open alone took 1.7 s for a 0.5 MB
/// file and 3–15 s for 5–14 MB ones, because Windows' antivirus reads the file the moment it is opened and Google
/// needs 1.5 s before the first byte arrives; the second open of the same file took 5 ms. So when a folder is opened
/// (listed), this fetches its files ahead of time into rclone's cache: small files completely, bigger ones their
/// start and their end (what programs, the antivirus and File Explorer read first), archives their file list.
/// A click a few seconds later then needs no request at all.
/// (It began as the archive warmer: Windows opens a .zip by reading its very end in small steps, each a request.)
/// </summary>
public sealed class ArchiveWarmer : IDisposable
{
    public static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase) { ".zip", ".7z", ".rar" };

    /// <summary>Archives up to this size are fetched completely (then extracting is instant too).</summary>
    public const long WholeFileLimit = 8 * ByteSize.MiB;
    /// <summary>End of the archive: a zip's file list ("central directory") lives here — about 1 MiB covers ~10,000 files.</summary>
    public const int TailBytes = (int)ByteSize.MiB;
    /// <summary>Start of the archive: read by Windows when it identifies the file.</summary>
    public const int HeadBytes = 128 * 1024;
    /// <summary>Any other file up to this size is fetched completely: documents, pictures, small programs.</summary>
    public const long SmallFileLimit = 4 * ByteSize.MiB;
    /// <summary>Start of a bigger file: its type, icon, the first screen of a video or a document.</summary>
    public const int FileHeadBytes = 512 * 1024;
    /// <summary>End of a bigger file: a PDF's page index and many videos' index live there.</summary>
    public const int FileTailBytes = 128 * 1024;
    /// <summary>
    /// After the provider refused a request ("slow down"), nothing is fetched ahead for this long. Fetching ahead
    /// only saves time later; while the provider is counting requests it takes them away from what the user is
    /// doing right now.
    /// </summary>
    public static readonly TimeSpan QuietAfterRateLimit = TimeSpan.FromMinutes(2);
    /// <summary>At most this many files of one folder, smallest first, and at most this much data for one folder.</summary>
    public const int MaxPerDirectory = 60;
    public const long MaxBytesPerDirectory = 128 * ByteSize.MiB;
    /// <summary>Files fetched at the same time (each is one request; the person's own clicks still get through).</summary>
    // 6: with 3 a folder of 19 small files took 14.5 s to get ready on the real drive (each file waits ~2 s for Google's first byte).
    public const int Parallel = 6;
    /// <summary>At most this many folders inside the opened folder are listed ahead.</summary>
    public const int MaxSubfolders = 40;
    private static readonly TimeSpan Rescan = TimeSpan.FromMinutes(5);

    private readonly Func<string, string?> _mountRoot;
    private readonly Func<string, ReadAttributionRegistry?> _attribution;
    private readonly Func<bool> _paused;
    private readonly ILogger _log;
    private readonly Channel<(string MountId, string Dir)> _queue =
        Channel.CreateBounded<(string, string)>(new BoundedChannelOptions(64) { FullMode = BoundedChannelFullMode.DropOldest });
    private readonly ConcurrentDictionary<string, DateTime> _listed = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _warmed = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _cts = new();

    public ArchiveWarmer(Func<string, string?> mountRoot, Func<string, ReadAttributionRegistry?> attribution, Func<bool> paused, ILogger log)
    {
        _mountRoot = mountRoot;
        _attribution = attribution;
        _paused = paused;
        _log = log;
        _ = Task.Run(WorkAsync);
    }

    /// <summary>The last folder that was made ready: when, how many files, how much data. Null until the first one.</summary>
    public (DateTime At, int Files, long Bytes)? LastReady { get; private set; }
    /// <summary>True while nothing is fetched ahead (a game runs, the disk is short of space, the provider asked to slow down).</summary>
    public bool IsPaused => _paused();

    /// <summary>(path relative to the drive root, bytes fetched) after a file was made ready.</summary>
    public event Action<string, long>? Warmed;

    /// <summary>A folder of a drive was opened. Cheap: the same folder is looked at again at most every few minutes.</summary>
    public void OnDirectoryListed(string mountId, string dir)
    {
        if (_paused()) return;
        var key = mountId + "|" + dir;
        var now = DateTime.UtcNow;
        if (_listed.TryGetValue(key, out var last) && now - last < Rescan) return;
        _listed[key] = now;
        _queue.Writer.TryWrite((mountId, dir));
    }

    /// <summary>What to fetch of an archive of this size: everything when small, otherwise its end and its start.</summary>
    public static IReadOnlyList<(long Offset, int Length)> Ranges(long size) =>
        size <= 0 ? []
        : size <= WholeFileLimit ? [(0, (int)size)]
        : [(size - TailBytes, TailBytes), (0, HeadBytes)];

    /// <summary>What to fetch of any file: archives as above; other files whole when small, otherwise their start and their end.</summary>
    public static IReadOnlyList<(long Offset, int Length)> RangesFor(string name, long size) =>
        Extensions.Contains(Path.GetExtension(name)) ? Ranges(size)
        : size <= 0 ? []
        : size <= SmallFileLimit ? [(0, (int)size)]
        : [(0, FileHeadBytes), (size - FileTailBytes, FileTailBytes)];

    /// <summary>Files Windows and File Explorer put into folders themselves: nobody opens them.</summary>
    private static bool Skip(string name) =>
        name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase) || name.Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase);

    /// <summary>The files of a folder that get fetched ahead: the smallest first, within the folder's limits.</summary>
    public static IReadOnlyList<FileInfo> Choose(IEnumerable<FileInfo> files)
    {
        var chosen = new List<FileInfo>();
        long budget = MaxBytesPerDirectory;
        foreach (var f in files.Where(f => f.Length > 0 && !Skip(f.Name)).OrderBy(f => f.Length))
        {
            if (chosen.Count >= MaxPerDirectory) break;
            long cost = RangesFor(f.Name, f.Length).Sum(r => (long)r.Length);
            if (cost > budget) continue;
            budget -= cost;
            chosen.Add(f);
        }
        return chosen;
    }

    // One folder at a time, a few of its files at once.
    private async Task WorkAsync()
    {
        try
        {
            await foreach (var (mountId, dir) in _queue.Reader.ReadAllAsync(_cts.Token))
            {
                try { await WarmDirectoryAsync(mountId, dir, _cts.Token); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    _log.LogDebug("Warm-up of a folder skipped: {Error}", e.Message);
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task WarmDirectoryAsync(string mountId, string dir, CancellationToken ct)
    {
        if (_mountRoot(mountId) is not { } root) return;
        var full = Path.Combine(root, dir.Replace('/', '\\'));
        if (!Directory.Exists(full)) return;
        // Listing the folder here shows up as one more "folder opened" event; the rescan interval absorbs it.
        var files = Choose(new DirectoryInfo(full).EnumerateFiles())
            .Where(f => _warmed.TryAdd($"{mountId}|{f.FullName}|{f.Length}|{f.LastWriteTimeUtc.Ticks}", 0)).ToList();
        // The folders inside it are listed too (one request each), so stepping into one shows its content at once.
        // They are marked as just seen, so this listing does not make their own files get fetched as well.
        var subfolders = new DirectoryInfo(full).EnumerateDirectories().Take(MaxSubfolders).ToList();
        foreach (var sub in subfolders)
            _listed[mountId + "|" + Path.GetRelativePath(root, sub.FullName).Replace('\\', '/')] = DateTime.UtcNow;
        await System.Threading.Tasks.Parallel.ForEachAsync(subfolders, new ParallelOptions { MaxDegreeOfParallelism = Parallel, CancellationToken = ct }, (sub, token) =>
        {
            try { if (!_paused() && !token.IsCancellationRequested) sub.EnumerateFileSystemInfos().Take(1).Count(); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* it is listed when the person opens it */ }
            return ValueTask.CompletedTask;
        });
        if (files.Count == 0) return;
        var clock = Stopwatch.StartNew();
        long total = 0;
        int ready = 0;
        await System.Threading.Tasks.Parallel.ForEachAsync(files, new ParallelOptions { MaxDegreeOfParallelism = Parallel, CancellationToken = ct }, async (f, token) =>
        {
            if (_paused()) { _warmed.TryRemove($"{mountId}|{f.FullName}|{f.Length}|{f.LastWriteTimeUtc.Ticks}", out _); return; }
            var rel = Path.GetRelativePath(root, f.FullName).Replace('\\', '/');
            try
            {
                var bytes = await WarmFileAsync(mountId, f, rel, token);
                Interlocked.Add(ref total, bytes);
                Interlocked.Increment(ref ready);
                Warmed?.Invoke(rel, bytes);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                _warmed.TryRemove($"{mountId}|{f.FullName}|{f.Length}|{f.LastWriteTimeUtc.Ticks}", out _); // try again the next time the folder is opened
            }
        });
        // One line for the folder, without names: the log is not a list of what people keep in their drive.
        if (ready > 0) LastReady = (DateTime.UtcNow, ready, total);
        if (ready > 0)
            _log.LogInformation("Folder ready to open: {Files} file(s), {Bytes} fetched ahead in {Seconds:0.0} s", ready, ByteSize.Format(total), clock.Elapsed.TotalSeconds);
    }

    private async Task<long> WarmFileAsync(string mountId, FileInfo file, string rel, CancellationToken ct)
    {
        var buffer = new byte[256 * 1024];
        long total = 0;
        await using var fs = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, useAsync: true);
        foreach (var (offset, length) in RangesFor(file.Name, fs.Length))
        {
            // Marked as read-ahead, so it is not counted as a program's own read (cache hit rate, game learning).
            using var reg = _attribution(mountId)?.Register(rel, offset, length);
            fs.Seek(offset, SeekOrigin.Begin);
            for (long left = length; left > 0;)
            {
                var n = await fs.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, left)), ct);
                if (n == 0) break;
                left -= n;
                total += n;
            }
        }
        return total;
    }

    public void Dispose()
    {
        _cts.Cancel();
        _queue.Writer.TryComplete();
    }
}
