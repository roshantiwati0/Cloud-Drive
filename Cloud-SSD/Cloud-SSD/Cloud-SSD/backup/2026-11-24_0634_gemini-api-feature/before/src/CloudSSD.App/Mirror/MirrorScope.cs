using System.Runtime.InteropServices;
using CloudSSD.Core;

namespace CloudSSD.App.Mirror;

/// <summary>
/// The places on this PC the mirror tells apart: the person's own folders go first, Windows and the programs last.
/// Made once for the PC (<see cref="ForThisPc"/>); tests make their own.
/// </summary>
public sealed record MirrorPlaces(
    string Profile,
    IReadOnlyList<string> KnownFolders,
    string Windows,
    IReadOnlyList<string> ProgramFolders,
    string DataRoot)
{
    public static MirrorPlaces ForThisPc(string dataRoot)
    {
        string F(Environment.SpecialFolder f) => Environment.GetFolderPath(f);
        var profile = F(Environment.SpecialFolder.UserProfile);
        var known = new[]
        {
            F(Environment.SpecialFolder.DesktopDirectory), F(Environment.SpecialFolder.MyDocuments), F(Environment.SpecialFolder.MyPictures),
            F(Environment.SpecialFolder.MyVideos), F(Environment.SpecialFolder.MyMusic), DownloadsFolder() ?? Path.Combine(profile, "Downloads")
        }.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var programs = new[]
        {
            F(Environment.SpecialFolder.ProgramFiles), F(Environment.SpecialFolder.ProgramFilesX86), F(Environment.SpecialFolder.CommonApplicationData)
        }.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return new MirrorPlaces(profile, known, F(Environment.SpecialFolder.Windows), programs, dataRoot);
    }

    /// <summary>The Downloads folder as Windows knows it (it has no SpecialFolder; it may have been moved).</summary>
    private static string? DownloadsFolder()
    {
        try
        {
            var id = new Guid("374DE290-123F-4565-9164-39C4925E467B");
            if (SHGetKnownFolderPath(ref id, 0, IntPtr.Zero, out var p) != 0) return null;
            try { return Marshal.PtrToStringUni(p); } finally { Marshal.FreeCoTaskMem(p); }
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException) { return null; }
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath(ref Guid id, uint flags, IntPtr token, out IntPtr path);
}

/// <summary>What the mirror copies, what it never does, and in which order (the tiers).</summary>
public static class MirrorScope
{
    /// <summary>The folder in the cloud that holds every device's copy. Hidden from the Z: and Y: drives and from the phone's Cloud SSD tab.</summary>
    public const string CloudFolder = "Cloud-SSD Devices";

    /// <summary>The person's folders (Desktop, Documents, Pictures, Videos, Music, Downloads).</summary>
    public const int TierOwn = 1;
    /// <summary>The rest of the person's profile, without AppData.</summary>
    public const int TierProfile = 2;
    /// <summary>Other drives and other folders on the system drive.</summary>
    public const int TierOther = 3;
    /// <summary>Program Files and ProgramData.</summary>
    public const int TierPrograms = 4;
    /// <summary>AppData and Windows.</summary>
    public const int TierSystem = 5;

    private static readonly HashSet<string> RootFilesNever = new(StringComparer.OrdinalIgnoreCase)
    {
        "pagefile.sys", "hiberfil.sys", "swapfile.sys", "DumpStack.log.tmp"
    };
    private static readonly HashSet<string> RootFoldersNever = new(StringComparer.OrdinalIgnoreCase)
    {
        "System Volume Information", "$Recycle.Bin", "$WinREAgent", "Recovery", "Config.Msi"
    };

    // Files that are only a pointer to the cloud (OneDrive "online only"…): reading one downloads it whole.
    private const FileAttributes RecallOnOpen = (FileAttributes)0x00040000, RecallOnDataAccess = (FileAttributes)0x00400000;

    public static bool IsUnder(string path, string folder) =>
        folder.Length > 0 && path.Length >= folder.Length && path.StartsWith(folder, StringComparison.OrdinalIgnoreCase)
        && (path.Length == folder.Length || folder[^1] is '\\' or '/' || path[folder.Length] is '\\' or '/');

    /// <summary>The tier of a file or folder: lower goes first.</summary>
    public static int TierOf(string path, MirrorPlaces p)
    {
        if (p.KnownFolders.Any(k => IsUnder(path, k))) return TierOwn;
        if (IsUnder(path, Path.Combine(p.Profile, "AppData"))) return TierSystem;
        if (IsUnder(path, p.Profile)) return TierProfile;
        if (IsUnder(path, p.Windows)) return TierSystem;
        if (p.ProgramFolders.Any(f => IsUnder(path, f))) return TierPrograms;
        return TierOther;
    }

    /// <summary>
    /// A folder the mirror does not go into: links to other places, Windows' own stores, this app's data (it holds the
    /// keys to the cloud accounts and to the paired devices), what the person left out, and the system tiers when they
    /// are switched off.
    /// </summary>
    public static bool SkipFolder(string path, FileAttributes attributes, MirrorPlaces p, MirrorSettings s)
    {
        if ((attributes & FileAttributes.ReparsePoint) != 0) return true;
        var parent = Path.GetDirectoryName(path);
        bool atRoot = parent != null && Path.GetPathRoot(path) is { } root && parent.Length == root.Length;
        if (atRoot && RootFoldersNever.Contains(Path.GetFileName(path))) return true;
        if (IsUnder(path, Path.Combine(p.Windows, "WinSxS"))) return true;
        if (IsUnder(path, p.DataRoot)) return true;
        if (s.Excluded.Any(x => IsUnder(path, x.TrimEnd('\\', '/')))) return true;
        return !s.IncludeSystem && TierOf(path, p) >= TierPrograms;
    }

    /// <summary>A file the mirror leaves: Windows' swap files, links, and files that are only a pointer to another cloud.</summary>
    public static bool SkipFile(string path, FileAttributes attributes)
    {
        if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Offline | RecallOnOpen | RecallOnDataAccess)) != 0) return true;
        var parent = Path.GetDirectoryName(path);
        bool atRoot = parent != null && Path.GetPathRoot(path) is { } root && parent.Length == root.Length;
        return atRoot && RootFilesNever.Contains(Path.GetFileName(path));
    }

    /// <summary>
    /// Where a file lies in the cloud copy, under "files/": the drive's letter as a folder, then its path
    /// ("C:\Users\me\a.txt" → "C/Users/me/a.txt").
    /// </summary>
    public static string CloudPath(string path)
    {
        var root = Path.GetPathRoot(path) ?? "";
        var rest = path[root.Length..].Replace('\\', '/').TrimStart('/');
        return DriveName(root) + (rest.Length > 0 ? "/" + rest : "");
    }

    /// <summary>"C:\" → "C". A root that is not a drive letter (a test folder, a share) gets a name made of its letters and digits.</summary>
    public static string DriveName(string root)
    {
        if (root.Length >= 2 && root[1] == ':' && char.IsAsciiLetter(root[0])) return char.ToUpperInvariant(root[0]).ToString();
        var name = new string(root.Where(char.IsLetterOrDigit).ToArray());
        return name.Length > 0 ? name : "disk";
    }

    /// <summary>The drives whose files are copied: this PC's own disks (NTFS or ReFS), never a cloud drive this app or another mounts.</summary>
    public static IReadOnlyList<string> Drives(IEnumerable<string> mountedByUs)
    {
        var ours = new HashSet<string>(mountedByUs.Select(m => m.TrimEnd('\\', ':').ToUpperInvariant()));
        var list = new List<string>();
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (d.DriveType != DriveType.Fixed || !d.IsReady) continue;
                if (d.DriveFormat is not ("NTFS" or "ReFS")) continue;
                if (ours.Contains(d.Name.TrimEnd('\\', ':').ToUpperInvariant())) continue;
                list.Add(d.RootDirectory.FullName);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return list;
    }
}
