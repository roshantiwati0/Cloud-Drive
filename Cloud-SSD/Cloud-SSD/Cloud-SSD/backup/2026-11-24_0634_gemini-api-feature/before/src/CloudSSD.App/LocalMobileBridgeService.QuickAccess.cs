using System.Reflection;
using Microsoft.Extensions.Logging;

namespace CloudSSD.App;

/// <summary>
/// /api/pc/home: the start of this PC as File Explorer shows it, for a phone that opens the PC: the folders pinned to
/// Quick access (Desktop, Downloads, Documents… and whatever the person pinned), in Explorer's order. The drives come
/// from /api/drives as before. Only folders the phone may read are given (see <see cref="ResolveShared"/>), with a
/// note on how many were left out because the PC's own disks are not shared.
/// </summary>
public sealed partial class LocalMobileBridgeService
{
    /// <summary>A folder of Quick access: its name in Explorer and its full path.</summary>
    internal sealed record QuickAccessFolder(string Name, string Path);

    /// <summary>Tests (and machines without the Windows shell) give the pinned folders here.</summary>
    internal Func<IReadOnlyList<QuickAccessFolder>>? QuickAccessOverride;

    private IReadOnlyList<QuickAccessFolder>? _quickAccess;
    private DateTime _quickAccessAt;
    private readonly object _quickAccessLock = new();

    /// <summary>The pinned folders of Quick access, read again after half a minute (pinning one shows up soon).</summary>
    private IReadOnlyList<QuickAccessFolder> QuickAccessFolders()
    {
        if (QuickAccessOverride is { } o) return o();
        lock (_quickAccessLock)
        {
            if (_quickAccess != null && DateTime.UtcNow - _quickAccessAt < TimeSpan.FromSeconds(30)) return _quickAccess;
            var list = ReadQuickAccess(_log);
            if (list.Count == 0) list = KnownFolders();
            _quickAccess = list;
            _quickAccessAt = DateTime.UtcNow;
            return list;
        }
    }

    /// <summary>
    /// Asks the Windows shell for Quick access ("Home" on Windows 11) on a thread of its own (the shell wants a
    /// single-threaded apartment). Only pinned folders that exist on a disk count; recent files are left out.
    /// </summary>
    private static List<QuickAccessFolder> ReadQuickAccess(ILogger log)
    {
        var found = new List<QuickAccessFolder>();
        var t = new Thread(() =>
        {
            try
            {
                var type = Type.GetTypeFromProgID("Shell.Application");
                if (type is null) return;
                object shell = Activator.CreateInstance(type)!;
                object? ns = Call(shell, "NameSpace", "shell:::{679f85cb-0220-4080-b29b-5540cc05aab6}");
                if (ns is null) return;
                object? items = Call(ns, "Items");
                if (items is null) return;
                int count = Convert.ToInt32(Get(items, "Count") ?? 0);
                var pinned = new List<QuickAccessFolder>();
                var folders = new List<QuickAccessFolder>();
                for (int i = 0; i < count && i < 200; i++)
                {
                    object? item = Call(items, "Item", i);
                    if (item is null) continue;
                    var path = Get(item, "Path") as string;
                    var name = Get(item, "Name") as string;
                    if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(name) || !Directory.Exists(path)) continue;
                    object? isPinned = null;
                    try { isPinned = Call(item, "ExtendedProperty", "System.Home.IsPinned"); } catch (Exception) { }
                    var f = new QuickAccessFolder(name, path);
                    if (isPinned is true) pinned.Add(f);
                    else folders.Add(f);
                }
                // Windows 11 marks what is pinned; Windows 10 lists pinned and frequent folders without saying which.
                found.AddRange(pinned.Count > 0 ? pinned : folders);
            }
            catch (Exception e) { log.LogDebug(e, "Reading Quick access failed"); }
        }) { IsBackground = true };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join(TimeSpan.FromSeconds(5));
        return found;
    }

    private static object? Call(object target, string name, params object[] args) =>
        target.GetType().InvokeMember(name, BindingFlags.InvokeMethod, null, target, args);

    private static object? Get(object target, string name) =>
        target.GetType().InvokeMember(name, BindingFlags.GetProperty, null, target, null);

    /// <summary>When the shell cannot be asked: the folders Windows pins by itself.</summary>
    private static List<QuickAccessFolder> KnownFolders()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var list = new List<QuickAccessFolder>();
        void Add(string name, string? path) { if (!string.IsNullOrEmpty(path) && Directory.Exists(path)) list.Add(new QuickAccessFolder(name, path)); }
        Add("Desktop", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        Add("Downloads", Path.Combine(home, "Downloads"));
        Add("Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        Add("Pictures", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
        Add("Music", Environment.GetFolderPath(Environment.SpecialFolder.MyMusic));
        Add("Videos", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos));
        return list;
    }

    /// <summary>What a Quick access folder is, for its icon on the phone (desktop, downloads, documents, pictures, music, videos or folder).</summary>
    internal static string QuickAccessKind(string path)
    {
        string Same(Environment.SpecialFolder f) => Environment.GetFolderPath(f);
        bool Is(string? other) => !string.IsNullOrEmpty(other)
            && string.Equals(Path.TrimEndingDirectorySeparator(path), Path.TrimEndingDirectorySeparator(other), StringComparison.OrdinalIgnoreCase);
        if (Is(Same(Environment.SpecialFolder.DesktopDirectory))) return "desktop";
        if (Is(Path.Combine(Same(Environment.SpecialFolder.UserProfile), "Downloads"))) return "downloads";
        if (Is(Same(Environment.SpecialFolder.MyDocuments))) return "documents";
        if (Is(Same(Environment.SpecialFolder.MyPictures))) return "pictures";
        if (Is(Same(Environment.SpecialFolder.MyMusic))) return "music";
        if (Is(Same(Environment.SpecialFolder.MyVideos))) return "videos";
        return "folder";
    }

    /// <summary>Tests give the person's main folders (Desktop, Documents…) here.</summary>
    internal Func<IReadOnlyList<QuickAccessFolder>>? MainFoldersOverride;

    /// <summary>
    /// /api/pc/home: {pinned: [{name, path, kind}], hidden, pcDisks, main: [{name, kind, shared, path?}]}.
    /// hidden = pinned folders the phone may not read. main = the person's own folders (Desktop, Downloads, Documents,
    /// Pictures, Music, Videos) whatever is pinned, so a phone can keep them on itself in one go and open them while
    /// this PC is off; the path is told only for a folder the phone may read.
    /// </summary>
    private object PcHome()
    {
        var pinned = new List<object>();
        int hidden = 0;
        foreach (var f in QuickAccessFolders())
        {
            var (path, refusal) = ResolveShared(f.Path);
            if (path is null || refusal != null) { hidden++; continue; }
            pinned.Add(new { name = f.Name, path = CleanPath(path), kind = QuickAccessKind(path) });
        }
        var main = new List<object>();
        foreach (var f in MainFoldersOverride?.Invoke() ?? KnownFolders())
        {
            var (path, refusal) = ResolveShared(f.Path);
            bool shared = path != null && refusal == null;
            main.Add(new { name = f.Name, kind = QuickAccessKind(f.Path), shared, path = shared ? CleanPath(path!) : null });
        }
        return new { pinned, hidden, pcDisks = _host.Settings.MobileBridge.AllowPcDisks, main };
    }
}
