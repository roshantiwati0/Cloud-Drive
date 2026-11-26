using System.Diagnostics;
using System.IO.Enumeration;
using CloudSSD.App;
using CloudSSD.Core;

namespace CloudSSD.Cli;

/// <summary>
/// /ls, /find and /copy: files on the cloud drives (and anywhere else on the PC) without leaving the terminal.
/// They go through Windows like File Explorer does, so they see exactly what Explorer sees on Z: and Y:.
/// </summary>
public static class Files
{
    private const int ListMax = 60;
    private const int FindMax = 50;
    private static readonly TimeSpan FindTimeLimit = TimeSpan.FromMinutes(3);

    /// <summary>
    /// What the person typed as a place, as a real path: "z" / "Z:" → "Z:\", "downloads" → the Downloads folder,
    /// "%USERPROFILE%\x" and "~\x" expanded, quotes removed.
    /// </summary>
    public static string Place(string typed)
    {
        var t = typed.Trim().Trim('"').Trim();
        if (t.Length == 0) return t;
        if (t.Length == 1 && char.IsAsciiLetter(t[0])) return char.ToUpperInvariant(t[0]) + ":\\";
        if (t.Length == 2 && char.IsAsciiLetter(t[0]) && t[1] == ':') return char.ToUpperInvariant(t[0]) + ":\\";
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var known = t.ToLowerInvariant() switch
        {
            "downloads" or "download" => Path.Combine(home, "Downloads"),
            "desktop" => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "documents" or "docs" => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "pictures" or "photos" => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            "videos" => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            "music" => Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            _ => null,
        };
        if (known != null) return known;
        if (t.StartsWith("~\\") || t.StartsWith("~/")) t = Path.Combine(home, t[2..]);
        return Environment.ExpandEnvironmentVariables(t);
    }

    // ---- /ls --------------------------------------------------------------------------------------------------------

    public static int List(TextWriter o, string typed, CancellationToken ct)
    {
        var dir = Place(typed);
        if (!Directory.Exists(dir))
        {
            o.WriteLine(Ansi.Yellow($"No folder at {dir}") + Ansi.Gray(File.Exists(dir) ? "  (that is a file)" : "  (check the name, or /drives)"));
            return 1;
        }
        var all = new List<FileSystemInfo>();
        foreach (var e in new DirectoryInfo(dir).EnumerateFileSystemInfos("*", new EnumerationOptions { IgnoreInaccessible = true }))
        {
            ct.ThrowIfCancellationRequested();
            all.Add(e);
        }
        var folders = all.OfType<DirectoryInfo>().OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        var files = all.OfType<FileInfo>().OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        o.WriteLine(Ansi.Gray($"{dir}{Ansi.Sep}{folders.Count} folders{Ansi.Sep}{files.Count} files{Ansi.Sep}{ByteSize.Format(files.Sum(f => SafeLength(f)))}"));
        if (all.Count == 0)
        {
            o.WriteLine("The folder is empty.");
            return 0;
        }
        var shown = 0;
        foreach (var d in folders)
        {
            if (shown++ >= ListMax) break;
            o.WriteLine(Ansi.Accent(Ansi.G("▸ ", "+ ") + d.Name + "\\"));
        }
        foreach (var f in files)
        {
            if (shown++ >= ListMax) break;
            o.WriteLine("  " + Ansi.Pad(Ansi.Fit(f.Name, 48), 48) + " " + Ansi.Pad(ByteSize.Format(SafeLength(f)), 11) +
                        Ansi.Gray(SafeTime(f)));
        }
        if (all.Count > ListMax) o.WriteLine(Ansi.Gray($"{Ansi.Ellipsis} and {all.Count - ListMax} more"));
        return 0;
    }

    private static long SafeLength(FileInfo f)
    {
        try { return f.Length; } catch { return 0; }
    }

    private static string SafeTime(FileInfo f)
    {
        try { return f.LastWriteTime.ToString("d MMM yyyy HH:mm"); } catch { return ""; }
    }

    // ---- /find ------------------------------------------------------------------------------------------------------

    /// <summary>"photo" matches names that contain it; "*.zip" / "photo?.jpg" are wildcards like in Explorer.</summary>
    public static bool Matches(string query, string name) =>
        query.IndexOfAny(['*', '?']) >= 0
            ? FileSystemName.MatchesSimpleExpression(query, name, ignoreCase: true)
            : name.Contains(query, StringComparison.CurrentCultureIgnoreCase);

    /// <summary>"/find report in Z:" → ("report", "Z:"); "/find my file" → ("my file", null).</summary>
    public static (string Query, string? Where) ParseFind(IReadOnlyList<string> words) => SplitAt(words, "in");

    /// <summary>The words after the command, split at the last <paramref name="keyword"/>: "/send a.jpg to vivo" → ("a.jpg", "vivo").</summary>
    public static (string First, string? Second) SplitAt(IReadOnlyList<string> words, string keyword)
    {
        var rest = words.Skip(1).ToList();
        var at = rest.FindLastIndex(w => w.Equals(keyword, StringComparison.OrdinalIgnoreCase));
        if (at > 0 && at < rest.Count - 1)
            return (string.Join(" ", rest.Take(at)), string.Join(" ", rest.Skip(at + 1)));
        return (string.Join(" ", rest), null);
    }

    public static int Find(TextWriter o, AppPaths paths, string query, string? where, CancellationToken ct, IProgress<string>? progress)
    {
        var roots = where != null ? [Place(where)] : DefaultSearchRoots(paths);
        roots = roots.Where(Directory.Exists).ToList();
        if (roots.Count == 0)
        {
            o.WriteLine(Ansi.Yellow(where != null ? $"No folder at {Place(where)}" : "No cloud drive is connected right now.") + Ansi.Gray("  See /drives."));
            return 1;
        }

        var found = new List<FileSystemInfo>();
        var queue = new Queue<string>(roots);
        var folders = 0;
        var clock = Stopwatch.StartNew();
        var stopReason = "";
        var options = new EnumerationOptions { IgnoreInaccessible = true };
        while (queue.Count > 0)
        {
            if (ct.IsCancellationRequested) { stopReason = "stopped"; break; }
            if (clock.Elapsed > FindTimeLimit) { stopReason = "time"; break; }
            var dir = queue.Dequeue();
            folders++;
            try
            {
                foreach (var e in new DirectoryInfo(dir).EnumerateFileSystemInfos("*", options))
                {
                    if (Matches(query, e.Name)) found.Add(e);
                    if (e is DirectoryInfo d && (d.Attributes & FileAttributes.ReparsePoint) == 0) queue.Enqueue(d.FullName);
                    if (found.Count >= FindMax) break;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A folder that cannot be read is skipped, like Explorer's search does.
            }
            if (found.Count >= FindMax) { stopReason = "enough"; break; }
            if (folders % 20 == 0) progress?.Report($"Searching{Ansi.Ellipsis} {folders:N0} folders looked at{Ansi.Sep}{found.Count} found");
        }

        var whereText = string.Join(", ", roots);
        if (found.Count == 0)
        {
            o.WriteLine($"Nothing named \"{query}\" in {whereText}" + Ansi.Gray($"  ({folders:N0} folders looked at)"));
        }
        foreach (var f in found)
        {
            var size = f is FileInfo fi ? Ansi.Gray("  " + ByteSize.Format(SafeLength(fi))) : "";
            o.WriteLine((f is DirectoryInfo ? Ansi.Accent(f.FullName + "\\") : f.FullName) + size);
        }
        var note = stopReason switch
        {
            "enough" => $"Showing the first {FindMax}. Say more of the name to narrow it down.",
            "time" => "Stopped after 3 minutes (Google is slow to list folders). Search inside one folder: /find name in Z:\\Folder",
            "stopped" => "Stopped before the end.",
            _ => "",
        };
        if (found.Count > 0) o.WriteLine(Ansi.Gray($"{found.Count} found in {whereText}{Ansi.Sep}{folders:N0} folders looked at") + (note.Length > 0 ? "\n" + Ansi.Gray(note) : ""));
        else if (note.Length > 0) o.WriteLine(Ansi.Gray(note));
        return 0;
    }

    /// <summary>
    /// Where a search looks when no folder is given: the "All Accounts" drive (every account joined) when it is connected,
    /// so files are not found twice through "Each Account"; otherwise every connected cloud drive.
    /// </summary>
    internal static List<string> DefaultSearchRoots(AppPaths paths)
    {
        var s = Views.Settings(paths);
        var mounted = Views.MountedCloudDrives(s);
        var all = s.Mounts.FirstOrDefault(m => m.RemoteName == AppHost.CombinedRemote && Views.Letter(m.DriveLetter).Length == 1);
        if (all != null && mounted.Any(d => d.Letter.Equals(Views.Letter(all.DriveLetter) + ":", StringComparison.OrdinalIgnoreCase)))
            return [Views.Letter(all.DriveLetter) + ":\\"];
        return mounted.Select(d => d.Letter + "\\").ToList();
    }

    // ---- /copy ------------------------------------------------------------------------------------------------------

    /// <summary>One file to copy: where from, where to, how big.</summary>
    internal sealed record CopyItem(string From, string To, long Size);

    /// <summary>
    /// What "/copy from to" will do: every file with its target. A folder keeps its name inside the target folder;
    /// a file copied to an existing folder keeps its name. Null + reason when it cannot be done.
    /// </summary>
    internal static (List<CopyItem>? Items, string Target, string? Error) PlanCopy(string from, string to)
    {
        var src = Place(from).TrimEnd('\\');
        var dst = Place(to);
        if (src.Length == 2 && src[1] == ':') src += "\\";
        if (File.Exists(src))
        {
            var target = Directory.Exists(dst) ? Path.Combine(dst, Path.GetFileName(src)) : dst;
            var parent = Path.GetDirectoryName(Path.GetFullPath(target));
            if (parent is null || !Directory.Exists(parent)) return (null, target, $"The folder {parent} is not there.");
            return ([new CopyItem(src, target, new FileInfo(src).Length)], target, null);
        }
        if (Directory.Exists(src))
        {
            var name = Path.GetFileName(src.TrimEnd('\\'));
            if (string.IsNullOrEmpty(name)) name = src[..1] + " drive";
            var target = Directory.Exists(dst) ? Path.Combine(dst, name) : dst;
            var full = Path.GetFullPath(target).TrimEnd('\\') + "\\";
            if (full.StartsWith(Path.GetFullPath(src).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
                return (null, target, "A folder cannot be copied into itself.");
            var items = new List<CopyItem>();
            foreach (var f in new DirectoryInfo(src).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }))
                items.Add(new CopyItem(f.FullName, Path.Combine(target, Path.GetRelativePath(src, f.FullName)), SafeLength(f)));
            return (items, target, null);
        }
        return (null, dst, $"Nothing at {src}.");
    }

    public static async Task<int> CopyAsync(TextWriter o, string from, string to, CancellationToken ct, IProgress<string>? progress)
    {
        var (items, target, error) = PlanCopy(from, to);
        if (items is null)
        {
            o.WriteLine(Ansi.Yellow(error ?? "Cannot copy that."));
            return 1;
        }
        var total = items.Sum(i => i.Size);
        long done = 0;
        int copied = 0;
        var skipped = new List<string>();
        var clock = Stopwatch.StartNew();
        var lastReport = TimeSpan.Zero;
        var buffer = new byte[1 << 20];
        string? part = null;
        try
        {
            foreach (var item in items)
            {
                ct.ThrowIfCancellationRequested();
                if (File.Exists(item.To))
                {
                    skipped.Add(item.To);
                    done += item.Size;
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(item.To)!);
                // Written under another name first: a stopped copy never leaves a half file under the real name.
                part = item.To + ".cloudssd-part";
                await using (var input = new FileStream(item.From, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16, useAsync: true))
                await using (var output = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
                {
                    int n;
                    while ((n = await input.ReadAsync(buffer, ct)) > 0)
                    {
                        await output.WriteAsync(buffer.AsMemory(0, n), ct);
                        done += n;
                        if (clock.Elapsed - lastReport > TimeSpan.FromMilliseconds(250))
                        {
                            lastReport = clock.Elapsed;
                            progress?.Report(ProgressLine(done, total, clock.Elapsed, Path.GetFileName(item.From)));
                        }
                    }
                }
                File.Move(part, item.To);
                part = null;
                copied++;
            }
        }
        catch (OperationCanceledException)
        {
            TryDelete(part);
            o.WriteLine(Ansi.Yellow($"Stopped. {copied} file(s) were copied before that; the half-copied one was removed."));
            return 1;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDelete(part);
            o.WriteLine(Ansi.Red($"Copy stopped: {CloudSSD.Diagnostics.Redactor.Redact(ex.Message)}"));
            if (copied > 0) o.WriteLine(Ansi.Gray($"{copied} file(s) were copied before that."));
            return 1;
        }

        var secs = Math.Max(clock.Elapsed.TotalSeconds, 0.001);
        var moved = done - skipped.Sum(s => items.First(i => i.To == s).Size);
        o.WriteLine(Ansi.Green(Ansi.Tick) + $" Copied {copied} file(s), {ByteSize.Format(moved)} to {target}" +
                    Ansi.Gray($"  ({clock.Elapsed.TotalSeconds:0.0} s{Ansi.Sep}{ByteSize.Format((long)(moved / secs))}/s)"));
        if (skipped.Count > 0)
            o.WriteLine(Ansi.Yellow($"{skipped.Count} already there, left as they were") + Ansi.Gray($" (e.g. {skipped[0]})"));
        return 0;
    }

    /// <summary>"▕██████░░░░▏ 45% · 120 MB of 260 MB · 8.2 MB/s · 17 s left · name.zip"</summary>
    internal static string ProgressLine(long done, long total, TimeSpan elapsed, string name)
    {
        var part = total > 0 ? (double)done / total : 1;
        var speed = elapsed.TotalSeconds > 0.2 ? done / elapsed.TotalSeconds : 0;
        var left = speed > 0 && total > done ? TimeSpan.FromSeconds((total - done) / speed) : (TimeSpan?)null;
        return $"{Views.Bar(part, 18)} {Math.Floor(part * 100):0}%{Ansi.Sep}{ByteSize.Format(done)} of {ByteSize.Format(total)}" +
               (speed > 0 ? $"{Ansi.Sep}{ByteSize.Format((long)speed)}/s" : "") +
               (left is { } l ? $"{Ansi.Sep}{Views.Span(TimeSpan.FromSeconds(Math.Ceiling(l.TotalSeconds)))} left" : "") +
               $"{Ansi.Sep}{Ansi.Fit(name, 28)}";
    }

    private static void TryDelete(string? path)
    {
        if (path is null) return;
        try { File.Delete(path); } catch { /* best effort */ }
    }
}
