using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CloudSSD.Desktop;

/// <summary>
/// One line in the notification list (the bell at the top right). <see cref="Target"/> says where a click goes:
/// "page:Devices", "access:&lt;request id&gt;:&lt;device id&gt;", "perm:&lt;device id&gt;", "folder:&lt;path&gt;", or nothing.
/// </summary>
public sealed partial class NotificationItem : ObservableObject
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..12];
    public DateTimeOffset At { get; init; } = DateTimeOffset.Now;
    public string Title { get; init; } = "";
    public string Text { get; init; } = "";
    /// <summary>access | device | files | clip | auto | info: picks the icon.</summary>
    public string Kind { get; init; } = "info";
    public string? Target { get; init; }

    [ObservableProperty] private bool _read;
    /// <summary>"5 min ago" — set when the list is opened.</summary>
    [ObservableProperty] private string _when = "";

    public bool CanOpen => !string.IsNullOrEmpty(Target);

    /// <summary>Segoe MDL2 glyph for the kind.</summary>
    public string Icon => Kind switch
    {
        "access" => "",   // lock
        "device" => "",   // cell phone
        "files" => "",    // download
        "clip" => "",     // paste
        "auto" => "",     // lightning
        _ => ""           // info
    };
}

/// <summary>
/// Everything the app told the person (the corner notes, and requests that open their own window), kept in a list
/// behind the bell at the top right, newest first, so nothing is missed: a note goes after 8 seconds, a new person
/// does not know where to answer a phone's request. A click opens the place it is about. Kept across restarts (the
/// last 100, at most 30 days).
/// </summary>
public static class NotificationCenter
{
    public const int MaxItems = 100;
    public static readonly TimeSpan KeepFor = TimeSpan.FromDays(30);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static string? _file;

    /// <summary>Newest first. Changed on the UI thread only.</summary>
    public static ObservableCollection<NotificationItem> Items { get; } = new();

    public static int Unread => Items.Count(i => !i.Read);

    /// <summary>The list or a line's read state changed (the badge redraws).</summary>
    public static event Action? Changed;

    /// <summary>Opens a target ("page:Devices"…); set by the main window's view model. True when it went somewhere.</summary>
    public static Func<string, bool>? Opener { get; set; }

    /// <summary>Reads the saved list (once, at start).</summary>
    public static void Load(string file)
    {
        _file = file;
        try
        {
            if (!File.Exists(file)) return;
            var saved = JsonSerializer.Deserialize<List<Saved>>(File.ReadAllText(file), Json) ?? [];
            var cutoff = DateTimeOffset.Now - KeepFor;
            Items.Clear();
            foreach (var s in saved.Where(s => s.At > cutoff).OrderByDescending(s => s.At).Take(MaxItems))
                Items.Add(new NotificationItem { Id = s.Id, At = s.At, Title = s.Title, Text = s.Text, Kind = s.Kind, Target = s.Target, Read = s.Read });
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            // A broken list starts empty.
        }
        Changed?.Invoke();
    }

    /// <summary>Adds a line (from any thread).</summary>
    public static void Add(string title, string text, string? target = null, string kind = "info")
    {
        var item = new NotificationItem { Title = title, Text = text, Target = target, Kind = kind };
        OnUi(() =>
        {
            Items.Insert(0, item);
            while (Items.Count > MaxItems) Items.RemoveAt(Items.Count - 1);
            Save();
            Changed?.Invoke();
        });
    }

    public static void MarkAllRead()
    {
        foreach (var i in Items) i.Read = true;
        Save();
        Changed?.Invoke();
    }

    public static void Clear()
    {
        Items.Clear();
        Save();
        Changed?.Invoke();
    }

    /// <summary>A click on a line (or on its corner note): read, and off to its place.</summary>
    public static bool Open(NotificationItem item)
    {
        item.Read = true;
        Save();
        Changed?.Invoke();
        return item.Target is { Length: > 0 } t && Opener?.Invoke(t) == true;
    }

    /// <summary>Opens a target without a line (a corner note clicked before its line exists).</summary>
    public static bool OpenTarget(string? target) => !string.IsNullOrEmpty(target) && Opener?.Invoke(target) == true;

    /// <summary>The lines about one target are read now (a request answered in its own window, a device page opened).</summary>
    public static void MarkRead(string target)
    {
        OnUi(() =>
        {
            bool any = false;
            foreach (var i in Items.Where(i => i.Target == target && !i.Read)) { i.Read = true; any = true; }
            if (!any) return;
            Save();
            Changed?.Invoke();
        });
    }

    /// <summary>"5 min ago" on every line (when the list opens).</summary>
    public static void RefreshTimes()
    {
        var now = DateTimeOffset.Now;
        foreach (var i in Items) i.When = ViewModels.Fmt.Ago(i.At, now);
    }

    private sealed record Saved(string Id, DateTimeOffset At, string Title, string Text, string Kind, string? Target, bool Read);

    private static void Save()
    {
        if (_file is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            var tmp = _file + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Items.Select(i => new Saved(i.Id, i.At, i.Title, i.Text, i.Kind, i.Target, i.Read)).ToList(), Json));
            File.Move(tmp, _file, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Kept in memory; written with the next change.
        }
    }

    private static void OnUi(Action a)
    {
        var d = Application.Current?.Dispatcher;
        if (d is null || d.CheckAccess()) a();
        else d.BeginInvoke(a);
    }
}
