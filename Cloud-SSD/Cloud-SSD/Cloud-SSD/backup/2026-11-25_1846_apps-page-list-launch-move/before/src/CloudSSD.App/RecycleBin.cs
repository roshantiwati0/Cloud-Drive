using System.Runtime.InteropServices;

namespace CloudSSD.App;

/// <summary>Something in a Recycle Bin: where it was, when it was deleted, and the "$R…" content and "$I…" record.</summary>
public sealed record BinItem(string OriginalPath, DateTime DeletedUtc, long Size, string Content, string Record);

/// <summary>
/// The Windows Recycle Bin: put a file or folder in it (it can be restored from there), see how much it holds, and
/// empty it. Uses the same shell calls as Explorer's Delete key, without its dialogs.
/// </summary>
public static class RecycleBin
{
    /// <summary>Moves a file or folder to the Recycle Bin. False when Windows could not (in use, no bin on that disk…).</summary>
    public static bool Send(string path)
    {
        bool ok = false;
        // The shell wants a single-threaded apartment; a short thread of its own keeps callers free of that.
        var t = new Thread(() =>
        {
            var op = new SHFILEOPSTRUCT
            {
                wFunc = FO_DELETE,
                pFrom = Path.GetFullPath(path).TrimEnd('\\') + "\0\0",
                fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_SILENT
            };
            ok = SHFileOperation(ref op) == 0 && !op.fAnyOperationsAborted;
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        return ok && !File.Exists(path) && !Directory.Exists(path);
    }

    /// <summary>What the Recycle Bins of all disks hold.</summary>
    public static (long Bytes, long Items) Size()
    {
        var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
        return SHQueryRecycleBin(null, ref info) == 0 ? (info.i64Size, info.i64NumItems) : (0, 0);
    }

    /// <summary>Empties the Recycle Bins of all disks (no dialog, no sound).</summary>
    public static bool Empty() => SHEmptyRecycleBin(IntPtr.Zero, null, SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND) is 0 or unchecked((int)0x8000FFFF);

    /// <summary>This user's Recycle Bin folder on the disk of <paramref name="path"/> (each disk has its own).</summary>
    public static string? BinDirectoryFor(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            var sid = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value;
            return string.IsNullOrEmpty(root) || sid is null ? null : Path.Combine(root, "$Recycle.Bin", sid);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads a "$I…" record of the bin: where the thing was, when it was deleted and how big it is. Windows Vista to 8
    /// write format 1 (a 260-character path), Windows 10 and later format 2 (the path's length, then the path).
    /// </summary>
    public static BinItem? ReadRecord(string recordPath)
    {
        try
        {
            var b = File.ReadAllBytes(recordPath);
            if (b.Length < 26) return null;
            long version = BitConverter.ToInt64(b, 0);
            long size = BitConverter.ToInt64(b, 8);
            var deleted = DateTime.FromFileTimeUtc(BitConverter.ToInt64(b, 16));
            string path;
            if (version == 2)
            {
                if (b.Length < 28) return null;
                int chars = BitConverter.ToInt32(b, 24);
                if (chars <= 0 || 28L + chars * 2L > b.Length) return null;
                path = System.Text.Encoding.Unicode.GetString(b, 28, chars * 2);
            }
            else if (version == 1)
            {
                path = System.Text.Encoding.Unicode.GetString(b, 24, Math.Min(520, b.Length - 24));
            }
            else return null;
            int end = path.IndexOf('\0');
            if (end >= 0) path = path[..end];
            if (path.Length == 0) return null;
            var name = Path.GetFileName(recordPath);
            var content = Path.Combine(Path.GetDirectoryName(recordPath)!, "$R" + name[2..]);
            return new BinItem(path, deleted, size, content, recordPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Everything a bin folder holds that can still be restored (its record and its content are there).</summary>
    public static IReadOnlyList<BinItem> Items(string binDirectory)
    {
        if (!Directory.Exists(binDirectory)) return [];
        var found = new List<BinItem>();
        foreach (var record in Directory.EnumerateFiles(binDirectory, "$I*", new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0 }))
        {
            if (ReadRecord(record) is { } item && (File.Exists(item.Content) || Directory.Exists(item.Content))) found.Add(item);
        }
        return found;
    }

    /// <summary>What the bin still holds from <paramref name="originalPath"/>, newest first (deleted more than once: each).</summary>
    public static IReadOnlyList<BinItem> Find(string originalPath, string? binDirectory = null)
    {
        var dir = binDirectory ?? BinDirectoryFor(originalPath);
        if (dir is null) return [];
        var want = Path.GetFullPath(originalPath).TrimEnd('\\');
        return Items(dir).Where(i => string.Equals(i.OriginalPath.TrimEnd('\\'), want, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(i => i.DeletedUtc).ToList();
    }

    /// <summary>
    /// Puts it back where it was, like Explorer's Restore. When something new has that name now, it comes back as
    /// "name (restored).ext" next to it. Returns the path it is at again.
    /// </summary>
    public static string Restore(BinItem item)
    {
        bool folder = Directory.Exists(item.Content);
        if (!folder && !File.Exists(item.Content)) throw new FileNotFoundException("It is no longer in the Recycle Bin.", item.OriginalPath);
        var target = item.OriginalPath;
        if (File.Exists(target) || Directory.Exists(target))
        {
            var dir = Path.GetDirectoryName(target)!;
            var stem = folder ? Path.GetFileName(target) : Path.GetFileNameWithoutExtension(target);
            var ext = folder ? "" : Path.GetExtension(target);
            for (int n = 1; ; n++)
            {
                target = Path.Combine(dir, $"{stem} (restored{(n > 1 ? " " + n : "")}){ext}");
                if (!File.Exists(target) && !Directory.Exists(target)) break;
            }
        }
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if (folder) Directory.Move(item.Content, target);
        else File.Move(item.Content, target);
        try
        {
            File.Delete(item.Record);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The record without its content is ignored by Windows; it goes when the bin is emptied.
        }
        return target;
    }

    private const uint FO_DELETE = 3;
    private const ushort FOF_SILENT = 0x0004, FOF_NOCONFIRMATION = 0x0010, FOF_ALLOWUNDO = 0x0040, FOF_NOERRORUI = 0x0400;
    private const uint SHERB_NOCONFIRMATION = 1, SHERB_NOPROGRESSUI = 2, SHERB_NOSOUND = 4;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    // 64-bit layout (natural alignment); the app is built for x64 only.
    [StructLayout(LayoutKind.Sequential)]
    private struct SHQUERYRBINFO
    {
        public int cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT op);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBin(string? rootPath, ref SHQUERYRBINFO info);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? rootPath, uint flags);
}
