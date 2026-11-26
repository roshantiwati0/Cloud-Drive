namespace CloudSSD.App;

/// <summary>One file found inside a folder being sent: where it is, and its place in the folder ("MyApp/src").</summary>
public sealed record FolderFile(string FullPath, string Folder, string Name, long Size, DateTime ModifiedUtc);

/// <summary>What was left out of a folder being sent: how many files and bytes, and which folders (by name).</summary>
public sealed record SkippedPart(int Files, long Bytes, IReadOnlyList<string> Folders);

/// <summary>
/// Walks a folder that is being sent (a project, photos, anything) and lists its files with their place in it, so the
/// receiver can rebuild the same tree — as Warpinator sends a relative path with every file. For projects, what is made
/// again by a build or a package manager is left out by default (".git", "node_modules", "bin", "obj"…): it is most
/// of the files and none of the work. Links to elsewhere (junctions, symlinks) are never followed.
/// </summary>
public static class ProjectFilter
{
    /// <summary>Folders left out unless "everything" is asked for.</summary>
    public static readonly IReadOnlySet<string> SkippedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".svn", ".hg", "node_modules", "bower_components", "bin", "obj", "build", "dist", "out", "target",
        ".gradle", ".idea", ".vs", ".vscode-test", "__pycache__", ".venv", "venv", ".tox", ".mypy_cache", ".pytest_cache",
        ".next", ".nuxt", ".cache", ".parcel-cache", ".dart_tool", "Pods", "DerivedData", ".terraform", "packages"
    };

    /// <summary>Files left out unless "everything" is asked for (system clutter, half-written files).</summary>
    public static bool IsJunkFile(string name) =>
        name.Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase) || name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)
        || name.Equals(".DS_Store", StringComparison.Ordinal) || name.EndsWith(".part", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) || name.StartsWith("~$", StringComparison.Ordinal);

    /// <summary>
    /// Every file under <paramref name="root"/> (at most <paramref name="max"/>), each with its folder relative to the
    /// root's parent: sending "C:\Code\MyApp" gives "MyApp", "MyApp/src"… With <paramref name="everything"/> false the
    /// folders above and junk files are skipped and counted.
    /// </summary>
    public static (List<FolderFile> Files, SkippedPart Skipped) Walk(string root, bool everything, int max)
    {
        var dir = new DirectoryInfo(root);
        var files = new List<FolderFile>();
        int skippedFiles = 0;
        long skippedBytes = 0;
        var skippedFolders = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!dir.Exists) return (files, new SkippedPart(0, 0, []));
        var stack = new Stack<(DirectoryInfo Dir, string Rel)>();
        stack.Push((dir, dir.Name));
        var opts = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System };
        while (stack.Count > 0)
        {
            var (d, rel) = stack.Pop();
            foreach (var f in d.EnumerateFiles("*", opts).OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (!everything && IsJunkFile(f.Name)) { skippedFiles++; skippedBytes += f.Length; continue; }
                if (files.Count >= max) { skippedFiles++; skippedBytes += f.Length; continue; }
                files.Add(new FolderFile(f.FullName, rel, f.Name, f.Length, f.LastWriteTimeUtc));
            }
            foreach (var sub in d.EnumerateDirectories("*", opts).OrderByDescending(s => s.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (!everything && SkippedFolders.Contains(sub.Name))
                {
                    skippedFolders.Add(sub.Name);
                    var inside = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
                    foreach (var f in sub.EnumerateFiles("*", inside)) { skippedFiles++; skippedBytes += f.Length; }
                    continue;
                }
                stack.Push((sub, rel + "/" + sub.Name));
            }
        }
        return (files, new SkippedPart(skippedFiles, skippedBytes, skippedFolders.ToList()));
    }

    /// <summary>
    /// A folder path from another device made safe to use under a save folder: "MyApp/src" stays, while "..", "C:\",
    /// leading slashes, empty parts and names Windows cannot hold are cleaned or dropped. "" when nothing is left.
    /// </summary>
    public static string SafeFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return "";
        var parts = folder.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0 && p != "." && p != ".." && !p.EndsWith(':'))
            .Select(LocalMobileBridgeService.SafeUploadName)
            .Where(p => p.Length > 0 && p != "." && p != "..")
            .Take(24)
            .ToList();
        var joined = string.Join("/", parts);
        return joined.Length > 400 ? joined[..400].TrimEnd('/') : joined;
    }

    /// <summary>"MyApp" when nothing has that name yet under <paramref name="parent"/>, else "MyApp (2)", "MyApp (3)"…</summary>
    public static string FreeFolderName(string parent, string name)
    {
        if (!Directory.Exists(Path.Combine(parent, name)) && !File.Exists(Path.Combine(parent, name))) return name;
        for (int i = 2; ; i++)
        {
            var n = $"{name} ({i})";
            if (!Directory.Exists(Path.Combine(parent, n)) && !File.Exists(Path.Combine(parent, n))) return n;
        }
    }
}
