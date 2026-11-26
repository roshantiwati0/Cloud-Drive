using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CloudSSD.Desktop;

/// <summary>The icon Windows shows for a program file, for the rows of the Apps page.</summary>
internal static class AppIcons
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileInfo
    {
        public IntPtr Icon;
        public int IconIndex;
        public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string path, uint attributes, ref ShFileInfo info, uint size, uint flags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);

    private const uint GetIcon = 0x100, LargeIcon = 0x0;

    /// <summary>The 32-pixel icon of a file, frozen so any thread may show it; null when Windows has none.</summary>
    public static ImageSource? Of(string path)
    {
        var info = new ShFileInfo();
        try
        {
            if (SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<ShFileInfo>(), GetIcon | LargeIcon) == IntPtr.Zero || info.Icon == IntPtr.Zero) return null;
            var image = Imaging.CreateBitmapSourceFromHIcon(info.Icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            image.Freeze();
            return image;
        }
        catch (Exception e) when (e is COMException or ArgumentException or InvalidOperationException or ExternalException) { return null; }
        finally
        {
            if (info.Icon != IntPtr.Zero) DestroyIcon(info.Icon);
        }
    }

    /// <summary>
    /// Fetches the icons one after another on a thread of its own (the shell wants an STA thread) and hands each to
    /// <paramref name="done"/> as soon as it is there, so a long list shows at once and fills in.
    /// </summary>
    public static void LoadInBackground(IReadOnlyList<string> paths, Action<string, ImageSource> done, CancellationToken ct)
    {
        var thread = new Thread(() =>
        {
            foreach (var path in paths)
            {
                if (ct.IsCancellationRequested) return;
                if (Of(path) is { } icon) done(path, icon);
            }
        }) { IsBackground = true, Name = "App icons" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }
}
