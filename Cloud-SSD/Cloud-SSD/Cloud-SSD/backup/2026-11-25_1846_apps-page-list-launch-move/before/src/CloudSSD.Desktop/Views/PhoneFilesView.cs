using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CloudSSD.App;
using CloudSSD.Core;

namespace CloudSSD.Desktop.Views;

/// <summary>
/// A phone's storage on this PC, like a phone in File Explorer over USB, as far as the phone allows (it asks its owner
/// the first time, and again before this PC may add files). On the left: "Phone storage" (its folders) and the kinds of
/// files (photos, videos, audio, documents, apps, archives). Pick several files and folders: "Save to PC" (folders whole,
/// with their tree), "Add files" / "Add folder" / drop from Explorer into the open phone folder, "New folder". Each run
/// shows below with its progress and is listed on the Transfers screen. Needs the Cloud-SSD app on the phone.
/// Through another PC (a laptop's view): looking and saving to this PC only.
/// </summary>
public sealed class PhoneFilesView : UserControl
{
    private sealed record Entry(string Name, string Path, bool IsDir, long Size, int Count, long Modified);

    /// <summary>
    /// The phone's own categories, in its order and with its names (its Phone tab): Downloads is its Download folder;
    /// "APK and archives" is two of the phone's kinds together ("+").
    /// </summary>
    private static readonly (string Key, string Name, string Icon)[] Kinds =
    [
        ("downloads", "Downloads", ""), ("photos", "Images", ""), ("videos", "Videos", ""),
        ("audio", "Audio", ""), ("docs", "Documents", ""), ("apks+archives", "APK and archives", ""),
    ];
    private const string DownloadsKind = "downloads";

    /// <summary>Asks the phone something (fs.roots, fs.list, fs.tree, fs.read, fs.cats, fs.cat): here, or through another PC.</summary>
    private readonly Func<string, Dictionary<string, string>, TimeSpan, Task<PeerResult>> _ask;
    private readonly AppHost? _host;
    private readonly PairedDevice? _device;
    private readonly string _name;
    private readonly TextBlock _status;
    private readonly WrapPanel _crumbs;
    private readonly ListBox _list, _side;
    private readonly Dictionary<string, TextBlock> _kindCounts = new();
    private readonly Button _up, _save, _addFiles, _addFolder, _newFolder;
    private readonly Border _taskBox;
    private readonly TextBlock _taskTitle, _taskInfo, _dropHint;
    private readonly ProgressBar _taskBar;
    private readonly Button _taskCancel, _taskOpen;
    private readonly DispatcherTimer _retry = new() { Interval = TimeSpan.FromSeconds(3) };
    private TransferTask? _task;
    private string? _path;          // null = the places the phone allows
    private string? _kind;          // a kind of file is shown instead of a folder
    private int _retries, _askedAll;
    private readonly Button _wholePhone;
    private string _scope = "";
    private bool _busy, _started, _syncingSide;

    private static string T(string s) => LocalizationManager.T(s);
    private static string L(string f, params object[] a) => string.Format(T(f), a);

    /// <summary>A phone paired with this PC: both ways, with the kinds of files.</summary>
    public PhoneFilesView(AppHost host, PairedDevice device)
        : this(device.Name, (kind, args, timeout) => host.MobileBridge.AskDeviceAsync(device.Id, kind, args, timeout), host, device) { }

    /// <summary>Any phone, asked through <paramref name="ask"/> (another PC's phone, through that PC): look and save.</summary>
    public PhoneFilesView(string name, Func<string, Dictionary<string, string>, TimeSpan, Task<PeerResult>> ask)
        : this(name, ask, null, null) { }

    private PhoneFilesView(string name, Func<string, Dictionary<string, string>, TimeSpan, Task<PeerResult>> ask, AppHost? host, PairedDevice? device)
    {
        _ask = ask;
        _host = host;
        _device = device;
        _name = name;
        var icons = Application.Current?.TryFindResource("IconFont") as FontFamily;

        var root = new DockPanel();

        // What the phone allows, or what is going on.
        _status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
        if (Application.Current?.TryFindResource("Caption") is Style cap) _status.Style = cap;
        DockPanel.SetDock(_status, Dock.Top);
        root.Children.Add(_status);

        // The run in progress (or the last one): what, how far, Cancel / Open folder.
        _taskTitle = new TextBlock { FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        _taskInfo = new TextBlock { FontSize = 12, Margin = new Thickness(0, 4, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        _taskInfo.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        _taskBar = new ProgressBar { Height = 6, Maximum = 1, Margin = new Thickness(0, 8, 0, 0) };
        _taskCancel = Tool(T("Cancel"));
        _taskCancel.Margin = new Thickness(8, 0, 0, 0);
        _taskCancel.Click += (_, _) => { if (_task != null) Bridge?.Tasks.Cancel(_task.Id); };
        _taskOpen = Tool(T("Open folder"));
        _taskOpen.Margin = new Thickness(8, 0, 0, 0);
        _taskOpen.Click += (_, _) => { if (_task is { OpenPath.Length: > 0 } t) OpenInExplorer(t.OpenPath); };
        var taskButtons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        taskButtons.Children.Add(_taskOpen);
        taskButtons.Children.Add(_taskCancel);
        var taskRow = new DockPanel();
        DockPanel.SetDock(taskButtons, Dock.Right);
        taskRow.Children.Add(taskButtons);
        var taskText = new StackPanel();
        taskText.Children.Add(_taskTitle);
        taskText.Children.Add(_taskInfo);
        taskText.Children.Add(_taskBar);
        taskRow.Children.Add(taskText);
        _taskBox = new Border { Child = taskRow, Visibility = Visibility.Collapsed };
        if (Application.Current?.TryFindResource("Card") is Style card) _taskBox.Style = card;
        _taskBox.Margin = new Thickness(0, 12, 0, 0);
        DockPanel.SetDock(_taskBox, Dock.Bottom);
        root.Children.Add(_taskBox);

        // Left: the phone's storage and the kinds of files, like the side of a file manager.
        _side = new ListBox { Width = 250, Margin = new Thickness(0, 0, 14, 0), BorderThickness = new Thickness(0), Background = Brushes.Transparent };
        if (Application.Current?.TryFindResource("NavItem") is Style nav) _side.ItemContainerStyle = nav;
        ScrollViewer.SetHorizontalScrollBarVisibility(_side, ScrollBarVisibility.Disabled);
        _side.Items.Add(SideItem("", T("Internal storage"), null, icons));
        if (_device != null)
        {
            var label = new TextBlock { Text = T("Categories"), FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(14, 12, 0, 4) };
            label.SetResourceReference(TextBlock.ForegroundProperty, "Dim");
            _side.Items.Add(new ListBoxItem { Content = label, IsEnabled = false, Focusable = false, Template = PlainTemplate() });
            foreach (var k in Kinds) _side.Items.Add(SideItem(k.Icon, T(k.Name), k.Key, icons));
        }
        _side.SelectedIndex = 0;
        _side.SelectionChanged += async (_, _) =>
        {
            if (_syncingSide || _side.SelectedItem is not ListBoxItem item) return;
            if (item.Tag is DownloadsKind) await LoadAsync("Download");
            else if (item.Tag is string kind) await LoadKindAsync(kind);
            else await LoadAsync(null);
        };
        DockPanel.SetDock(_side, Dock.Left);
        root.Children.Add(_side);

        // Up + where we are (each part opens that folder).
        var nav2 = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        _up = new Button { Content = "", Padding = new Thickness(10, 4, 10, 4), ToolTip = T("Up") };
        if (icons != null) _up.FontFamily = icons;
        System.Windows.Automation.AutomationProperties.SetName(_up, T("Up"));
        _up.Click += (_, _) => GoUp();
        DockPanel.SetDock(_up, Dock.Left);
        nav2.Children.Add(_up);
        _crumbs = new WrapPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        nav2.Children.Add(_crumbs);
        DockPanel.SetDock(nav2, Dock.Top);
        root.Children.Add(nav2);

        // What can be done here.
        var tools = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        _save = Tool(T("Save to PC"), primary: true);
        _save.Click += (_, _) => SaveSelected();
        _addFiles = Tool(T("Add files…"));
        _addFiles.Click += (_, _) => PickAndAdd(folder: false);
        _addFolder = Tool(T("Add folder…"));
        _addFolder.Click += (_, _) => PickAndAdd(folder: true);
        _newFolder = Tool(T("New folder"));
        _newFolder.Click += async (_, _) => await NewFolderAsync();
        var selectAll = Tool(T("Select all"));
        selectAll.Click += (_, _) => _list!.SelectAll();
        // The phone lets this PC see only six folders: ask it (its person decides) for the whole storage.
        _wholePhone = Tool(T("Show the whole phone"));
        _wholePhone.Visibility = Visibility.Collapsed;
        _wholePhone.Click += async (_, _) => await AskWholePhoneAsync();
        var openSaved = Tool(T("Open the save folder"));
        openSaved.Click += (_, _) => { Directory.CreateDirectory(SaveFolder()); OpenInExplorer(SaveFolder()); };
        foreach (var b in new[] { _save, selectAll, _addFiles, _addFolder, _newFolder, openSaved, _wholePhone }) tools.Children.Add(b);
        DockPanel.SetDock(tools, Dock.Top);
        root.Children.Add(tools);

        // The folder or the kind: several can be picked (Ctrl / Shift); files from Explorer can be dropped in.
        var listArea = new Grid();
        _list = new ListBox { SelectionMode = SelectionMode.Extended, AllowDrop = _device != null, MinHeight = 220 };
        // Long names are cut with "…", the size and date stay in view (no sideways scrolling).
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
        _list.SetResourceReference(BackgroundProperty, "Surface");
        _list.SetResourceReference(ForegroundProperty, "Text");
        _list.SetResourceReference(BorderBrushProperty, "BorderBrush");
        _list.MouseDoubleClick += async (_, _) => await OpenSelectedAsync();
        _list.KeyDown += async (_, e) => { if (e.Key == Key.Enter) await OpenSelectedAsync(); else if (e.Key == Key.Back) GoUp(); };
        _list.SelectionChanged += (_, _) => UpdateButtons();
        _list.DragOver += (_, e) =>
        {
            e.Effects = CanAddHere && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            _dropHint!.Visibility = e.Effects == DragDropEffects.Copy ? Visibility.Visible : Visibility.Collapsed;
            e.Handled = true;
        };
        _list.DragLeave += (_, _) => _dropHint!.Visibility = Visibility.Collapsed;
        _list.Drop += (_, e) =>
        {
            _dropHint!.Visibility = Visibility.Collapsed;
            if (CanAddHere && e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0) Add(paths);
        };
        listArea.Children.Add(_list);
        _dropHint = new TextBlock
        {
            Text = T("Drop to add to this phone folder"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 16), Padding = new Thickness(14, 8, 14, 8), IsHitTestVisible = false, Visibility = Visibility.Collapsed,
        };
        _dropHint.SetResourceReference(TextBlock.BackgroundProperty, "Accent");
        _dropHint.SetResourceReference(TextBlock.ForegroundProperty, "OnAccent");
        listArea.Children.Add(_dropHint);
        root.Children.Add(listArea);
        Content = root;

        _retry.Tick += async (_, _) => { _retry.Stop(); await LoadAsync(_path); };
        Unloaded += (_, _) => _retry.Stop();
        Loaded += async (_, _) =>
        {
            if (_started) return;
            _started = true;
            await LoadAsync(null);
        };
        UpdateButtons();
        ShowCrumbs();
    }

    private LocalMobileBridgeService? Bridge =>
        _host?.MobileBridge ?? (Application.Current?.MainWindow?.DataContext as ViewModels.MainViewModel)?.Host.MobileBridge;

    /// <summary>A row of the side list: icon, name and (for a kind) how many there are.</summary>
    private ListBoxItem SideItem(string glyph, string name, string? kind, FontFamily? icons)
    {
        var row = new DockPanel();
        if (kind != null)
        {
            var count = new TextBlock { FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
            count.SetResourceReference(TextBlock.ForegroundProperty, "Dim");
            DockPanel.SetDock(count, Dock.Right);
            row.Children.Add(count);
            _kindCounts[kind] = count;
        }
        var icon = new TextBlock { Text = glyph, FontSize = 15, Width = 26, VerticalAlignment = VerticalAlignment.Center };
        if (icons != null) icon.FontFamily = icons;
        DockPanel.SetDock(icon, Dock.Left);
        row.Children.Add(icon);
        row.Children.Add(new TextBlock { Text = name, FontSize = 13.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        var item = new ListBoxItem { Content = row, Tag = kind };
        System.Windows.Automation.AutomationProperties.SetName(item, name);
        return item;
    }

    /// <summary>A list row that is only a label: no hover, no selection look.</summary>
    private static ControlTemplate PlainTemplate()
    {
        var t = new ControlTemplate(typeof(ListBoxItem));
        t.VisualTree = new FrameworkElementFactory(typeof(ContentPresenter));
        return t;
    }

    private static Button Tool(string text, bool primary = false)
    {
        var b = new Button { Content = text, Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 8, 6) };
        if (primary && Application.Current?.TryFindResource("Primary") is Style p) b.Style = p;
        return b;
    }

    /// <summary>Adding works for a phone paired with this PC, in a folder or on the list of places (not in a kind).</summary>
    private bool CanAddHere => _device != null && _kind is null;

    /// <summary>Where "Add" puts things: the open folder; on the list of places, the phone's Download folder.</summary>
    private string AddTarget => _path ?? "Download";

    private void UpdateButtons()
    {
        _up.IsEnabled = _path != null && _kind is null;
        // A kind shows files from many folders: there is no one folder to add to.
        var add = CanAddHere ? Visibility.Visible : Visibility.Collapsed;
        _addFiles.Visibility = _addFolder.Visibility = _newFolder.Visibility = add;
        var tip = _path is null ? T("Goes to Download on the phone. Open a folder first to add there instead.") : null;
        _addFiles.ToolTip = _addFolder.ToolTip = _newFolder.ToolTip = tip;
    }

    /// <summary>Where "Save to PC" puts things: Downloads\Cloud-SSD\Received\&lt;phone&gt;.</summary>
    private string SaveFolder() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "Cloud-SSD", "Received",
            ProjectFilter.SafeFolder(_name) is { Length: > 0 } n ? n : "Phone");

    private async Task OpenSelectedAsync()
    {
        if (_list.SelectedItem is not ListBoxItem { Tag: Entry e }) return;
        if (e.IsDir) await LoadAsync(e.Path);
        else SaveSelected();
    }

    private void GoUp()
    {
        if (_path is null || _kind != null) return;
        int slash = _path.LastIndexOf('/');
        _ = LoadAsync(slash > 0 ? _path[..slash] : null);
    }

    /// <summary>What the phone answered when it was not "ok", in words. True when it will be asked again by itself.</summary>
    private bool Explain(PeerResult r)
    {
        switch (r.Status)
        {
            case "needs-permission" when _retries++ < 40:
                _status.Text = L("Allow it on {0}: a note is waiting there (Allow 1 hour), or open Cloud-SSD on it.", _name);
                return true;
            case "timeout":
                _status.Text = L("{0} is not answering. Open the Cloud-SSD app on it (it must be running), on the same Wi-Fi.", _name);
                return false;
            case "denied":
                _status.Text = L("{0} does not allow that.", _name);
                return false;
            default:
                _status.Text = r.Error ?? T("Something went wrong.");
                return false;
        }
    }

    /// <summary>The places the phone allows (null) or one folder. Asks again every few seconds while the phone's owner decides.</summary>
    private async Task LoadAsync(string? path)
    {
        if (_busy) return;
        _busy = true;
        _status.Text = L("Asking {0}…", _name);
        try
        {
            var args = new Dictionary<string, string>();
            if (path != null) args["path"] = path;
            var r = await _ask(path is null ? "fs.roots" : "fs.list", args, TimeSpan.FromSeconds(20));
            if (!IsLoaded) return;
            if (r.Ok && path is null && r.Json is { } rootsJson)
            {
                _scope = rootsJson.TryGetProperty("scope", out var sc) ? sc.GetString() ?? "" : "";
                if (_scope == "all")
                {
                    r = await _ask("fs.list", new Dictionary<string, string> { ["path"] = "" }, TimeSpan.FromSeconds(20));
                    if (!IsLoaded) return;
                    if (r.Ok) _askedAll = 0;
                }
                else if (_askedAll > 0 && _askedAll++ < 40) _retry.Start();     // still waiting for the phone's Allow
                _wholePhone.Visibility = _device != null && _scope == "shared" ? Visibility.Visible : Visibility.Collapsed;
            }
            if (r.Ok)
            {
                _retries = 0;
                _path = path;
                _kind = null;
                SelectSide(path == "Download" ? DownloadsKind : null);
                Show(r.Json!.Value, path is null && _scope != "all");
                if (path is null && _scope == "all") _status.Text = L("{0}: the whole storage, as the phone shows it.", _name) + (_device != null ? " " + T("Open a folder to add files to it.") : "");
                if (path is null && _askedAll > 1) _status.Text = L("Allow it on {0}: a note is waiting there (Allow 1 hour), or open Cloud-SSD on it.", _name);
                if (path is null) _ = LoadCountsAsync(false);
            }
            else if (Explain(r)) _retry.Start();
        }
        finally
        {
            _busy = false;
            UpdateButtons();
        }
    }

    /// <summary>How many files of each kind the phone has (shown beside the kinds).</summary>
    private async Task LoadCountsAsync(bool refresh)
    {
        if (_device is null) return;
        var r = await _ask("fs.cats", new Dictionary<string, string> { ["refresh"] = refresh ? "1" : "" }, TimeSpan.FromSeconds(40));
        if (!r.Ok || r.Json is not { } json || !json.TryGetProperty("cats", out var cats)) return;
        var sums = new Dictionary<string, (int Count, long Bytes)>();
        foreach (var c in cats.EnumerateArray())
        {
            var key = c.GetProperty("key").GetString() ?? "";
            var mine = Kinds.FirstOrDefault(k => k.Key.Split('+').Contains(key)).Key;
            if (mine is null) continue;
            var had = sums.GetValueOrDefault(mine);
            sums[mine] = (had.Count + c.GetProperty("count").GetInt32(), had.Bytes + (c.TryGetProperty("bytes", out var b) ? b.GetInt64() : 0));
        }
        foreach (var (key, v) in sums)
            if (_kindCounts.TryGetValue(key, out var text)) text.Text = CountText(v.Count, v.Bytes);
        // Downloads: the files in the Download folder itself (not its folders), as the phone counts them.
        var dl = await _ask("fs.list", new Dictionary<string, string> { ["path"] = "Download" }, TimeSpan.FromSeconds(20));
        if (dl.Ok && dl.Json is { } dj && dj.TryGetProperty("files", out var files) && _kindCounts.TryGetValue(DownloadsKind, out var dlText))
        {
            var real = files.EnumerateArray().Where(x => !(x.GetProperty("name").GetString() ?? "").StartsWith('.')).ToList();
            dlText.Text = real.Count == 0 ? T("Empty") : CountText(real.Count, real.Sum(x => x.TryGetProperty("size", out var z) ? z.GetInt64() : 0));
        }
    }

    /// <summary>"14 · 119 MB", or "None", as the phone writes it under a category.</summary>
    private static string CountText(int count, long bytes) => count == 0 ? T("None") : count.ToString("N0") + " · " + Dec(bytes);

    /// <summary>A size as the phone writes it (1000s, three digits): "340 MB", "43.3 MB".</summary>
    internal static string Dec(long b)
    {
        if (b < 1) return "0 B";
        string[] u = ["B", "kB", "MB", "GB", "TB"];
        int i = Math.Min(u.Length - 1, (int)Math.Floor(Math.Log(b) / Math.Log(1000)));
        double v = b / Math.Pow(1000, i);
        var n = v.ToString(i == 0 || v >= 100 ? "0" : v >= 10 ? "0.0" : "0.00", System.Globalization.CultureInfo.InvariantCulture);
        return n + " " + u[i];
    }

    /// <summary>Asks the phone to let this PC see its whole storage (its person answers there); then waits for it.</summary>
    private async Task AskWholePhoneAsync()
    {
        _wholePhone.IsEnabled = false;
        try
        {
            var r = await _ask("fs.askAll", new Dictionary<string, string>(), TimeSpan.FromSeconds(20));
            if (!IsLoaded) return;
            if (r.Ok && r.Json is { } j && j.TryGetProperty("scope", out var sc) && sc.GetString() == "all") { await LoadAsync(null); return; }
            if (!r.Ok) { Explain(r); return; }
            _askedAll = 1;
            _status.Text = L("Allow it on {0}: a note is waiting there (Allow 1 hour), or open Cloud-SSD on it.", _name);
            _retry.Start();
        }
        finally { _wholePhone.IsEnabled = true; }
    }

    /// <summary>Every file of one category on the phone, newest first (two of the phone's kinds for "APK and archives").</summary>
    private async Task LoadKindAsync(string kind)
    {
        if (_busy) return;
        _busy = true;
        _status.Text = L("Asking {0}…", _name);
        try
        {
            var entries = new List<Entry>();
            int total = 0;
            foreach (var part in kind.Split('+'))
            {
                var r = await _ask("fs.cat", new Dictionary<string, string> { ["cat"] = part }, TimeSpan.FromSeconds(40));
                if (!IsLoaded) return;
                if (!r.Ok || r.Json is not { } json) { Explain(r); SelectSide(_kind); return; }
                foreach (var f in json.GetProperty("files").EnumerateArray())
                    entries.Add(new Entry(f.GetProperty("name").GetString() ?? "", f.GetProperty("path").GetString() ?? "", false,
                        f.GetProperty("size").GetInt64(), 0, f.TryGetProperty("modified", out var m) ? m.GetInt64() : 0));
                total += json.TryGetProperty("total", out var t) ? t.GetInt32() : json.GetProperty("files").GetArrayLength();
            }
            _kind = kind;
            entries = entries.OrderByDescending(e => e.Modified).ToList();
            _status.Text = L("{0} files", total.ToString("N0")) + " · " + Dec(entries.Sum(e => e.Size))
                + (total > entries.Count ? " · " + L("the newest {0} are shown", entries.Count.ToString("N0")) : "");
            ShowCrumbs();
            Fill(entries, showFolder: true);
        }
        finally
        {
            _busy = false;
            UpdateButtons();
        }
    }

    private void SelectSide(string? kind)
    {
        _syncingSide = true;
        try
        {
            foreach (var item in _side.Items.OfType<ListBoxItem>())
                if (item.IsEnabled && (item.Tag as string) == kind) { _side.SelectedItem = item; break; }
        }
        finally { _syncingSide = false; }
    }

    private void Show(JsonElement json, bool roots)
    {
        var entries = new List<Entry>();
        if (roots)
        {
            foreach (var r in json.GetProperty("roots").EnumerateArray())
                entries.Add(new Entry(r.GetProperty("name").GetString() ?? "", r.GetProperty("path").GetString() ?? "", true, 0, -1, 0));
            var scope = json.TryGetProperty("scope", out var s) && s.GetString() == "all" ? T("the whole phone storage") : T("photos, downloads, documents and music");
            _status.Text = L("{0} lets this PC see {1}.", _name, scope) + (_device != null ? " " + T("Open a folder to add files to it.") : "");
        }
        else
        {
            foreach (var f in json.GetProperty("folders").EnumerateArray())
                entries.Add(new Entry(f.GetProperty("name").GetString() ?? "", f.GetProperty("path").GetString() ?? "", true, 0,
                    f.TryGetProperty("count", out var c) ? c.GetInt32() : -1, f.TryGetProperty("modified", out var m) ? m.GetInt64() : 0));
            foreach (var f in json.GetProperty("files").EnumerateArray())
                entries.Add(new Entry(f.GetProperty("name").GetString() ?? "", f.GetProperty("path").GetString() ?? "", false,
                    f.TryGetProperty("size", out var z) ? z.GetInt64() : 0, 0, f.TryGetProperty("modified", out var m) ? m.GetInt64() : 0));
            _status.Text = L("{0} folders · {1} files", entries.Count(e => e.IsDir), entries.Count(e => !e.IsDir))
                + (json.TryGetProperty("truncated", out var t) && t.GetBoolean() ? " · " + T("only the first 2000 are shown") : "");
        }
        ShowCrumbs();
        Fill(entries, showFolder: false);
    }

    /// <summary>The rows: icon, name, and size / items / date; for a kind also the folder the file is in.</summary>
    private void Fill(List<Entry> entries, bool showFolder)
    {
        _list.Items.Clear();
        var icons = Application.Current?.TryFindResource("IconFont") as FontFamily;
        foreach (var e in entries)
        {
            var row = new DockPanel();
            var detail = e.IsDir ? (e.Count >= 0 ? L("{0} items", e.Count) : "") : Dec(e.Size);
            if (e.Modified > 0) detail += (detail.Length > 0 ? "   " : "") + DateTimeOffset.FromUnixTimeMilliseconds(e.Modified).LocalDateTime.ToString("dd MMM yyyy HH:mm");
            var info = new TextBlock { Text = detail, FontSize = 11.5, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            info.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            DockPanel.SetDock(info, Dock.Right);
            row.Children.Add(info);
            var icon = new TextBlock { Text = e.IsDir ? "" : "", Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center, FontSize = 16 };
            if (icons != null) icon.FontFamily = icons;
            icon.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
            DockPanel.SetDock(icon, Dock.Left);
            row.Children.Add(icon);
            var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            names.Children.Add(new TextBlock { Text = e.Name, TextTrimming = TextTrimming.CharacterEllipsis });
            if (showFolder)
            {
                int slash = e.Path.LastIndexOf('/');
                var where = new TextBlock { Text = slash > 0 ? e.Path[..slash].Replace("/", " › ") : T("Internal storage"), FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis };
                where.SetResourceReference(TextBlock.ForegroundProperty, "Dim");
                names.Children.Add(where);
            }
            row.Children.Add(names);
            var item = new ListBoxItem { Content = row, Tag = e, Padding = new Thickness(8, 6, 8, 6), HorizontalContentAlignment = HorizontalAlignment.Stretch };
            System.Windows.Automation.AutomationProperties.SetName(item, e.Name);
            _list.Items.Add(item);
        }
    }

    /// <summary>"Internal storage › DCIM › Camera" (each part opens that folder), or "Categories › Images".</summary>
    private void ShowCrumbs()
    {
        _crumbs.Children.Clear();
        void Crumb(string text, Func<Task>? open)
        {
            var b = new Button { Content = text, Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(0, 0, 2, 0), IsEnabled = open != null };
            if (Application.Current?.TryFindResource("Ghost") is Style g) b.Style = g;
            if (open != null) b.Click += async (_, _) => await open();
            _crumbs.Children.Add(b);
        }
        void Sep()
        {
            var sep = new TextBlock { Text = "›", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 2, 0) };
            sep.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            _crumbs.Children.Add(sep);
        }
        if (_kind != null)
        {
            Crumb(T("Categories"), null);
            Sep();
            Crumb(T(Kinds.First(k => k.Key == _kind).Name), null);
            return;
        }
        var parts = _path is null ? Array.Empty<string>() : _path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        Crumb(T("Internal storage"), parts.Length == 0 ? null : () => LoadAsync(null));
        for (int i = 0; i < parts.Length; i++)
        {
            Sep();
            var target = string.Join('/', parts[..(i + 1)]);
            Crumb(parts[i], i == parts.Length - 1 ? null : () => LoadAsync(target));
        }
    }

    private List<Entry> Selected() => _list.SelectedItems.OfType<ListBoxItem>().Select(i => i.Tag).OfType<Entry>().ToList();

    /// <summary>The picked files and folders (folders whole, with their tree) into this PC's save folder, in one run.</summary>
    private void SaveSelected()
    {
        var picked = Selected();
        if (picked.Count == 0) { _status.Text = T("Pick the files and folders to save first (Ctrl or Shift picks several)."); return; }
        if (Bridge is not { } bridge) return;
        var dest = SaveFolder();
        Directory.CreateDirectory(dest);
        var items = picked.Select(e => new PhoneItem(e.Path, e.Name, e.IsDir, e.Size)).ToList();
        Watch(_device != null
            ? bridge.SavePhoneItemsToPc(_device, items, dest)
            // Through another PC: the same run, asking through it.
            : bridge.SavePhoneItemsToPc(_name, "", "relay", (kind, args, timeout, _) => _ask(kind, args, timeout), items, dest));
    }

    private void PickAndAdd(bool folder)
    {
        if (!CanAddHere) return;
        var owner = Window.GetWindow(this);
        if (folder)
        {
            var d = new Microsoft.Win32.OpenFolderDialog { Title = L("Pick folders to add to {0}", AddTarget), Multiselect = true };
            if (d.ShowDialog(owner) == true) Add(d.FolderNames);
        }
        else
        {
            var d = new Microsoft.Win32.OpenFileDialog { Title = L("Pick files to add to {0}", AddTarget), Multiselect = true };
            if (d.ShowDialog(owner) == true) Add(d.FileNames);
        }
    }

    /// <summary>
    /// Files and folders from this PC into the open phone folder, all in one run, each folder with its tree (build
    /// output such as node_modules left out).
    /// </summary>
    private void Add(IReadOnlyList<string> paths)
    {
        if (!CanAddHere || _host is null || _device is null || paths.Count == 0) return;
        Watch(_host.MobileBridge.SendToPhoneFolder(_device, paths, AddTarget));
    }

    private async Task NewFolderAsync()
    {
        if (!CanAddHere || _host is null || _device is null) return;
        var name = PromptWindow.Ask(T("New folder"), L("A new folder in {0} on {1}:", AddTarget, _name), T("New folder"));
        if (string.IsNullOrWhiteSpace(name)) return;
        _status.Text = L("Asking {0}…", _name);
        var target = AddTarget;
        var r = await _host.MobileBridge.MakePhoneFolderAsync(_device, target, name.Trim());
        if (!IsLoaded) return;
        // Made in Download from the list of places: Download opens, so the new folder is seen.
        if (r.Ok) { await LoadAsync(target); return; }
        _status.Text = r.Status == "needs-permission"
            ? L("Allow it on {0}: a note is waiting there (it may add files for 1 hour).", _name)
            : r.Error ?? T("Something went wrong.");
    }

    // ---- the run shown at the bottom ------------------------------------------------------------------------------

    private void Watch(TransferTask? task)
    {
        if (_task != null) _task.PropertyChanged -= OnTaskChanged;
        _task = task;
        if (_task is null) return;
        _task.PropertyChanged += OnTaskChanged;
        ShowTask();
    }

    private void OnTaskChanged(object? sender, PropertyChangedEventArgs e) => Dispatcher.BeginInvoke(ShowTask);

    private void ShowTask()
    {
        if (_task is not { } t) return;
        _taskBox.Visibility = Visibility.Visible;
        var what = t.Kind == "folder" && t.Folder.Length > 0 ? t.Folder : t.Files == 1 ? T("1 file") : L("{0} files", t.Files);
        _taskTitle.Text = t.Dir == "toPhone" ? L("{0} → {1}", what, _name) : L("{0} → this PC", what);
        _taskBar.Value = t.Bytes > 0 ? Math.Min(1, (double)t.DoneBytes / t.Bytes) : t.Files > 0 ? (double)t.DoneFiles / t.Files : 0;
        _taskBar.IsIndeterminate = t.State is "waiting" || (t.IsOpen && t.Files == 0);
        var count = L("{0} of {1}", t.DoneFiles, t.Files);
        _taskInfo.Text = t.State switch
        {
            "running" or "waiting" => count + (t.Speed > 0 ? " · " + Dec(t.Speed) + "/s" : "") + (t.Current.Length > 0 ? " · " + t.Current : ""),
            "done" => L("Done: {0} files", t.DoneFiles) + " · " + (t.Dir == "toPhone" ? t.SaveTo : t.OpenPath),
            "partial" => L("{0} done, {1} failed", t.DoneFiles, t.Failed.Count) + " · " + Reason(t),
            "failed" => T("Failed") + " · " + Reason(t),
            "cancelled" => T("Cancelled"),
            _ => count,
        };
        _taskCancel.Visibility = t.IsOpen ? Visibility.Visible : Visibility.Collapsed;
        _taskOpen.Visibility = !t.IsOpen && t.OpenPath.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        // Files added to the folder on screen: show them, and count the kinds again.
        if (!t.IsOpen && t.Dir == "toPhone" && t.DoneFiles > 0 && _kind is null)
        {
            _ = LoadAsync(_path);
            _ = LoadCountsAsync(true);
        }
    }

    private string Reason(TransferTask t)
    {
        var r = t.Failed.Select(f => f.Reason).FirstOrDefault() ?? "other";
        return r switch
        {
            "denied" => L("allow it on {0} (a note is waiting there)", _name),
            "network" => T("the connection dropped"),
            "space" => T("not enough space"),
            "missing" => T("the file is gone"),
            _ => T("something went wrong"),
        };
    }

    private static void OpenInExplorer(string path)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true }); }
        catch (Win32Exception) { }
    }
}
