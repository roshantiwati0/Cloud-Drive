using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace CloudSSD.App.Hybrid;

/// <summary>
/// A drive letter that points at a folder of this PC (what the "subst" command does). The letter lasts until the
/// person signs out, so the app puts it back at every start. Only the name is made or removed; no file is touched.
/// </summary>
public static class DosDevice
{
    private const uint RawTargetPath = 1, RemoveDefinition = 2, ExactMatchOnRemove = 4;
    private const int DriveAdd = 0x100, DriveRemoved = 0x80;
    private const uint PathW = 0x0005;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool DefineDosDeviceW(uint flags, string name, string? target);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint QueryDosDeviceW(string name, [Out] char[] target, int max);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(int eventId, uint flags, string? item1, IntPtr item2);

    private static string Name(char letter) => char.ToUpperInvariant(letter) + ":";
    private static string TargetFor(string folder) => @"\??\" + Path.GetFullPath(folder).TrimEnd('\\');

    /// <summary>What the letter points at right now (for a folder: "\??\C:\..."), or null when the letter is free.</summary>
    public static string? TargetOf(char letter)
    {
        var buffer = new char[4096];
        var n = QueryDosDeviceW(Name(letter), buffer, buffer.Length);
        if (n == 0) return null;
        var text = new string(buffer, 0, (int)n);
        var end = text.IndexOf('\0');
        return end >= 0 ? text[..end] : text;
    }

    public static bool PointsAt(char letter, string folder) =>
        string.Equals(TargetOf(letter), TargetFor(folder), StringComparison.OrdinalIgnoreCase);

    /// <summary>Puts the letter on the folder. False when the letter is taken by something else (a real drive, a dead name).</summary>
    public static bool Define(char letter, string folder)
    {
        if (PointsAt(letter, folder)) return true;
        if (TargetOf(letter) != null) return false;
        Directory.CreateDirectory(folder);
        if (!DefineDosDeviceW(RawTargetPath, Name(letter), TargetFor(folder))) return false;
        SHChangeNotify(DriveAdd, PathW, Name(letter) + "\\", IntPtr.Zero);
        return true;
    }

    /// <summary>Takes the letter off the folder again; a letter that points somewhere else is left alone.</summary>
    public static bool Remove(char letter, string folder)
    {
        if (!PointsAt(letter, folder)) return false;
        var ok = DefineDosDeviceW(RawTargetPath | RemoveDefinition | ExactMatchOnRemove, Name(letter), TargetFor(folder));
        if (ok) SHChangeNotify(DriveRemoved, PathW, Name(letter) + "\\", IntPtr.Zero);
        return ok;
    }

    /// <summary>The name File Explorer shows for the letter ("Cloud SSD (Z:)" instead of "Local Disk (Z:)"); null removes it.</summary>
    public static void SetLabel(char letter, string? label)
    {
        var key = $@"Software\Microsoft\Windows\CurrentVersion\Explorer\DriveIcons\{char.ToUpperInvariant(letter)}\DefaultLabel";
        try
        {
            if (label is null) Registry.CurrentUser.DeleteSubKeyTree(Path.GetDirectoryName(key)!, throwOnMissingSubKey: false);
            else
            {
                using var k = Registry.CurrentUser.CreateSubKey(key);
                k.SetValue("", label);
            }
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException or System.Security.SecurityException) { /* a nicer name only */ }
    }
}
