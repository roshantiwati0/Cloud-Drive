using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace CloudSSD.App;

/// <summary>Where a program's files are: on this PC's own disk, or on a cloud drive.</summary>
public enum AppPlace { Laptop, Cloud }

/// <summary>One Start menu shortcut as Windows resolves it.</summary>
public sealed record ShortcutInfo(string LinkPath, string Target, string Arguments, string WorkDir);

/// <summary>
/// One program the Apps page can start. <see cref="InstallDir"/> is the folder that would move when the program is
/// sent to the cloud; <see cref="WhyNot"/> says in plain words why it cannot move (null when it can).
/// </summary>
public sealed record AppItem(string Id, string Name, string ExePath, string Arguments, string WorkDir, string InstallDir,
    long Bytes, AppPlace Where, string? WhyNot)
{
    public bool CanMove => WhyNot == null;
}

/// <summary>
/// The programs on this PC and on the cloud drives, found without the person typing a path: the Start menu's
/// shortcuts (what Windows itself lists under "All apps"), the size and folder Windows knows from "Installed apps",
/// and the folders under "Apps" on each cloud drive.
/// </summary>
public static class AppCatalog
{
    /// <summary>The folder on a cloud drive that holds programs sent there from the Apps page.</summary>
    public const string CloudFolder = "Apps";

    /// <summary>Shortcut names that are not the program itself.</summary>
    private static readonly string[] NotAProgram =
    [
        "uninstall", "readme", "read me", "license", "licence", "documentation", "manual", "website", "web site",
        "release notes", "what's new", "changelog", "user guide", "check for updates", "safe mode",
    ];

    /// <summary>Everything, read fresh. Slow parts (COM, registry, cloud folders) run here: call it off the UI thread.</summary>
    public static IReadOnlyList<AppItem> Read(IEnumerable<string> cloudRoots)
    {
        var roots = cloudRoots.ToList();
        var shortcuts = ReadShortcuts(StartMenuFolders());
        var programs = InstalledPrograms.Read();
        var list = Build(shortcuts, programs, roots, File.Exists, Environment.GetFolderPath(Environment.SpecialFolder.Windows)).ToList();
        var known = list.Select(a => a.ExePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
            foreach (var a in ReadCloudFolder(root))
                if (known.Add(a.ExePath)) list.Add(a);
        return list.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static IReadOnlyList<string> StartMenuFolders() =>
    [
        Environment.GetFolderPath(Environment.SpecialFolder.Programs),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
    ];

    /// <summary>
    /// Turns shortcuts and the installed-programs list into the rows of the page: one row per program file, without
    /// uninstallers, help files and Windows' own tools.
    /// </summary>
    internal static IReadOnlyList<AppItem> Build(IEnumerable<ShortcutInfo> shortcuts, IEnumerable<InstalledPrograms.Program> programs,
        IReadOnlyList<string> cloudRoots, Func<string, bool> fileExists, string windowsDir)
    {
        var installed = programs.Where(p => !string.IsNullOrWhiteSpace(p.Location))
            .Select(p => (Program: p, Dir: Dir(p.Location!))).Where(x => x.Dir.Length > 3).OrderByDescending(x => x.Dir.Length).ToList();
        var windows = Dir(windowsDir);
        var byExe = new Dictionary<string, AppItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in shortcuts)
        {
            var target = s.Target.Trim().Trim('"');
            if (!target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !Path.IsPathFullyQualified(target)) continue;
            var name = Path.GetFileNameWithoutExtension(s.LinkPath).Trim();
            if (name.Length == 0 || NotAProgram.Any(w => name.Contains(w, StringComparison.OrdinalIgnoreCase))) continue;
            if (windows.Length > 3 && Under(target, windows)) continue;
            if (!fileExists(target)) continue;

            var exeDir = Dir(Path.GetDirectoryName(target) ?? "");
            var match = installed.FirstOrDefault(x => Under(target, x.Dir));
            var installDir = match.Program != null ? match.Dir : exeDir;
            var place = cloudRoots.Any(r => Under(target, Dir(r))) ? AppPlace.Cloud : AppPlace.Laptop;
            var item = new AppItem(IdOf(target), name, target, s.Arguments.Trim(),
                string.IsNullOrWhiteSpace(s.WorkDir) ? exeDir.TrimEnd('\\') : s.WorkDir.Trim().Trim('"'),
                installDir.TrimEnd('\\'), match.Program?.Bytes ?? 0, place, WhyNotMovable(target, installDir, place, windows));
            // Two shortcuts to the same program file: the plain one (no arguments, shorter name) is the program.
            if (!byExe.TryGetValue(target, out var have) || Better(item, have)) byExe[target] = item;
        }
        return byExe.Values.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool Better(AppItem a, AppItem b) =>
        (a.Arguments.Length == 0) != (b.Arguments.Length == 0) ? a.Arguments.Length == 0 : a.Name.Length < b.Name.Length;

    /// <summary>Why this program's folder must stay where it is, or null when it can go to the cloud and back.</summary>
    internal static string? WhyNotMovable(string exe, string installDir, AppPlace place, string windowsDir)
    {
        var dir = Dir(installDir);
        if (dir.Length <= 3) return "It sits directly on a drive, not in a folder of its own.";
        if (windowsDir.Length > 3 && Under(exe, Dir(windowsDir))) return "It is a part of Windows.";
        if (dir.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase)) return "It is a Microsoft Store app; Windows keeps those in place.";
        foreach (var shared in SharedFolders())
            if (shared.Length > 3 && string.Equals(dir, Dir(shared), StringComparison.OrdinalIgnoreCase))
                return "Its folder is shared with other programs.";
        if (dir.Contains(@"\Cloud-SSD\", StringComparison.OrdinalIgnoreCase)) return "It is Cloud-SSD itself.";
        return null;
    }

    private static IEnumerable<string> SharedFolders() => new[]
    {
        Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.UserProfile,
        Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolder.ApplicationData, Environment.SpecialFolder.CommonApplicationData,
        Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.MyDocuments, Environment.SpecialFolder.CommonProgramFiles,
    }.Select(Environment.GetFolderPath).Concat(new[]
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
    });

    /// <summary>The programs in "Apps" on one cloud drive: one row per folder, its main program file.</summary>
    internal static IReadOnlyList<AppItem> ReadCloudFolder(string cloudRoot)
    {
        var list = new List<AppItem>();
        try
        {
            var apps = Path.Combine(cloudRoot, CloudFolder);
            if (!Directory.Exists(apps)) return list;
            foreach (var folder in Directory.EnumerateDirectories(apps))
            {
                // Only the folder's own level: every level deeper is another question to the cloud.
                var exes = Directory.EnumerateFiles(folder, "*.exe").Select(f => new FileInfo(f)).ToList();
                if (MainExe(Path.GetFileName(folder), exes.Select(e => (e.FullName, e.Length)).ToList()) is not { } exe) continue;
                list.Add(new AppItem(IdOf(exe), Path.GetFileName(folder), exe, "", folder, folder, 0, AppPlace.Cloud, null));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return list;
    }

    /// <summary>The program file of a folder: the one named like the folder, else the biggest that is not a helper.</summary>
    internal static string? MainExe(string folderName, IReadOnlyList<(string Path, long Size)> exes)
    {
        static string Key(string s) => new(s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        var real = exes.Where(e =>
        {
            var n = Path.GetFileNameWithoutExtension(e.Path);
            return !NotAProgram.Any(w => n.Contains(w.Trim(), StringComparison.OrdinalIgnoreCase)) &&
                   !n.StartsWith("unins", StringComparison.OrdinalIgnoreCase) && !n.Contains("crash", StringComparison.OrdinalIgnoreCase) &&
                   !n.Contains("setup", StringComparison.OrdinalIgnoreCase) && !n.Contains("redist", StringComparison.OrdinalIgnoreCase);
        }).ToList();
        if (real.Count == 0) return null;
        var want = Key(folderName);
        var named = real.Where(e => Key(Path.GetFileNameWithoutExtension(e.Path)) == want).ToList();
        return (named.Count > 0 ? named : real).OrderByDescending(e => e.Size).First().Path;
    }

    /// <summary>Every shortcut (.lnk) below the given folders, resolved by Windows' own shell object.</summary>
    public static IReadOnlyList<ShortcutInfo> ReadShortcuts(IEnumerable<string> folders)
    {
        var list = new List<ShortcutInfo>();
        object? shell = null;
        try
        {
            if (Type.GetTypeFromProgID("WScript.Shell") is not { } type || (shell = Activator.CreateInstance(type)) is null) return list;
            foreach (var folder in folders.Where(f => !string.IsNullOrWhiteSpace(f) && Directory.Exists(f)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                IEnumerable<string> links;
                try
                {
                    links = Directory.EnumerateFiles(folder, "*.lnk", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 4, IgnoreInaccessible = true }).ToList();
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
                foreach (var path in links)
                {
                    try
                    {
                        dynamic link = ((dynamic)shell).CreateShortcut(path);
                        try
                        {
                            string target = link.TargetPath ?? "";
                            if (target.Length > 0) list.Add(new ShortcutInfo(path, target, (string)(link.Arguments ?? ""), (string)(link.WorkingDirectory ?? "")));
                        }
                        finally { Marshal.FinalReleaseComObject(link); }
                    }
                    catch (Exception e) when (e is COMException or InvalidCastException or IOException or UnauthorizedAccessException
                                                  or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { }
                }
            }
        }
        catch (Exception e) when (e is COMException or InvalidCastException) { }
        finally
        {
            if (shell != null) Marshal.FinalReleaseComObject(shell);
        }
        return list;
    }

    /// <summary>Starts the program the way a double click in the Start menu does (it may ask for administrator rights itself).</summary>
    public static void Start(AppItem app)
    {
        if (!File.Exists(app.ExePath)) throw new FileNotFoundException($"{app.Name} is not there any more: {app.ExePath}");
        var psi = new ProcessStartInfo(app.ExePath) { UseShellExecute = true, Arguments = app.Arguments };
        if (Directory.Exists(app.WorkDir)) psi.WorkingDirectory = app.WorkDir;
        Process.Start(psi);
    }

    /// <summary>The size of a folder's files, or 0 when it cannot be read. Links to other places are not followed.</summary>
    public static long FolderBytes(string folder, CancellationToken ct = default)
    {
        try
        {
            long sum = 0;
            foreach (var f in new DirectoryInfo(folder).EnumerateFiles("*", new EnumerationOptions
                     { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }))
            {
                ct.ThrowIfCancellationRequested();
                sum += f.Length;
            }
            return sum;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return 0; }
    }

    internal static string IdOf(string exePath) =>
        Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(exePath.ToLowerInvariant())))[..12].ToLowerInvariant();

    /// <summary>A folder path that ends with one backslash, so "C:\App" is not taken for a part of "C:\Apple".</summary>
    private static string Dir(string path) => path.Trim().Trim('"').TrimEnd('\\', '/').Replace('/', '\\') + "\\";

    private static bool Under(string path, string dirWithSlash) => path.StartsWith(dirWithSlash, StringComparison.OrdinalIgnoreCase);
}
