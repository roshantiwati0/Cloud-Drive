using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CloudSSD.App;
using CloudSSD.Core;

namespace CloudSSD.Desktop.Views;

/// <summary>
/// The cloud drives of this PC (the Cloud SSD), beside a phone's storage on the "Files" page: on the left each
/// connected drive and the kinds of files (photos, videos, audio, documents, apps, archives) of the drive in use;
/// on the right the open folder. Pick several files and folders: "Send to phone" (folders whole, into the phone's
/// Download folder, as one run shown below and on the Transfers screen), open a file, show it in File Explorer.
/// </summary>
public sealed class SsdFilesView : UserControl
{
    private sealed record Entry(string Name, string Path, bool IsDir, long Size, DateTime Modified);

    private static readonly (string Key, string Name, string Icon, string[] Ext)[] Kinds =
    [
        ("photos", "Photos", "\uEB9F", [".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp", ".heic", ".svg"]),
        ("videos", "Videos", "\uE714", [".mp4", ".mkv", ".webm", ".avi", ".mov", ".m4v", ".3gp"]),
        ("audio", "Audio", "\uEC4F", [".mp3", ".wav", ".flac", ".aac", ".ogg", ".m4a", ".opus"]),
        ("docs", "Documents", "\uE8A5", [".pdf", ".doc", ".docx", ".txt", ".xls", ".xlsx", ".ppt", ".pptx", ".csv", ".md"]),
        // Apps and archives together, as the phone shows them.
        ("apps", "Apps and archives", "", [".apk", ".exe", ".msi", ".zip", ".rar", ".7z", ".tar", ".gz", ".iso"]),
    ];

    private const int MaxRows = 3000, MaxKindRows = 1000, KindDepth = 6;
    private static readonly TimeSpan KindTime = TimeSpan.FromSeconds(20);

    private readonly AppHost _host;
    private readonly TextBlock _status;
    private readonly WrapPanel _crumbs;
    private readonly ListBox _list, _side;
    private readonly Button _up, _send, _open, _show;
    private readonly Border _taskBox;
    private readonly TextBlock _taskTitle, _taskInfo;
    private readonly ProgressBar _taskBar;
    private readonly Button _taskCancel;
    private TransferTask? _task;
    private string? _root;          // the drive in use ("Z:\")
    private string? _path;          // the open folder
    private string? _kind;          // a kind of file is shown instead of a folder
    private int _seq;
    private bool _started, _syncingSide;

    private static string T(string s) => LocalizationManager.T(s);
    private static string L(string f, params object[] a) => string.Format(T(f), a);

    public SsdFilesView(AppHost host)
    {
        _host = host;
        var icons = Application.Current?.TryFindResource("IconFont") as FontFamily;
        var root = new DockPanel();

        _status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
        if (Application.Current?.TryFindResource("Caption") is Style cap) _status.Style = cap;
        DockPanel.SetDock(_status, Dock.Top);
        root.Children.Add(_status);

        // The run in progress (or the last one).
        _taskTitle = new TextBlock { FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        _taskTitle.SetResourceReference(TextBlock.ForegroundProperty, "Text");
        _taskInfo = new TextBlock { FontSize = 12, Margin = new Thickness(0, 4, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        _taskInfo.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        _taskBar = new ProgressBar { Height = 6, Maximum = 1, Margin = new Thickness(0, 8, 0, 0) };
        _taskCancel = Tool(T("Cancel"));
        _taskCancel.Margin = new Thickness(8, 0, 0, 0);
        _taskCancel.VerticalAlignment = VerticalAlignment.Center;
        _taskCancel.Click += (_, _) => { if (_task != null) _host.MobileBridge.Tasks.Cancel(_task.Id); };
        var taskRow = new DockPanel();
        DockPanel.SetDock(_taskCancel, Dock.Right);
        taskRow.Children.Add(_taskCancel);
        var taskText = new StackPanel();
        taskText.Children.Add(_taskTitle);
        taskText.Children.Add(_taskInfo);
        taskText.Children.Add(_taskBar);
        taskRow.Children.Add(taskText);
        _taskBox = new Border { Child = taskRow, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 12, 0, 0) };
        if (Application.Current?.TryFindResource("Card") is Style card) _taskBox.Style = card;
        _taskBox.Margin = new Thickness(0, 12, 0, 0);
        DockPanel.SetDock(_taskBox, Dock.Bottom);
        root.Children.Add(_taskBox);

        // Left: the drives and the kinds of files.
        _side = new ListBox { Width = 210, Margin = new Thickness(0, 0, 14, 0), BorderThickness = new Thickness(0), Background = Brushes.Transparent };
        if (Application.Current?.TryFindResource("NavItem") is Style nav) _side.ItemContainerStyle = nav;
        ScrollViewer.SetHorizontalScrollBarVisibility(_side, ScrollBarVisibility.Disabled);
        _side.SelectionChanged += (_, _) =>
        {
            if (_syncingSide || _side.SelectedItem is not ListBoxItem item) return;
            if (item.Tag is string tag && tag.StartsWith("kind:", StringComparison.Ordinal)) LoadKind(tag[5..]);
            else if (item.Tag is string drive) { _root = drive; Load(drive); }
        };
        DockPanel.SetDock(_side, Dock.Left);
        root.Children.Add(_side);

        // Up + where we are.
        var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        _up = new Button { Content = "\uE74A", Padding = new Thickness(10, 4, 10, 4), ToolTip = T("Up") };
        if (icons != null) _up.FontFamily = icons;
        System.Windows.Automation.AutomationProperties.SetName(_up, T("Up"));
        _up.Click += (_, _) => GoUp();
        DockPanel.SetDock(_up, Dock.Left);
        bar.Children.Add(_up);
        _crumbs = new WrapPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        bar.Children.Add(_crumbs);
        DockPanel.SetDock(bar, Dock.Top);
        root.Children.Add(bar);

        // What can be done here.
        var tools = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        _send = Tool(T("Send to phone"), primary: true);
        _send.Click += (_, _) => SendSelected();
        _open = Tool(T("Open"));
        _open.Click += (_, _) => OpenSelected();
        var selectAll = Tool(T("Select all"));
        selectAll.Click += (_, _) => { _list!.SelectAll(); _list.Focus(); };
        _show = Tool(T("Show in File Explorer"));
        _show.Click += (_, _) => ShowInExplorer();
        var refresh = Tool(T("Refresh"));
        refresh.Click += (_, _) => { if (_kind != null) LoadKind(_kind); else if (_path != null) Load(_path); else FillSide(); };
        foreach (var b in new[] { _send, _open, selectAll, _show, refresh }) tools.Children.Add(b);
        DockPanel.SetDock(tools, Dock.Top);
        root.Children.Add(tools);

        _list = new ListBox { SelectionMode = SelectionMode.Extended, MinHeight = 220 };
        _list.SetResourceReference(BackgroundProperty, "Surface");
        _list.SetResourceReference(ForegroundProperty, "Text");
        _list.SetResourceReference(BorderBrushProperty, "BorderBrush");
        _list.MouseDoubleClick += (_, _) => OpenSelected();
        _list.KeyDown += (_, e) => { if (e.Key == Key.Enter) OpenSelected(); else if (e.Key == Key.Back) GoUp(); };
        root.Children.Add(_list);
        Content = root;

        Loaded += (_, _) =>
        {
            _host.StateChanged -= OnHostChanged;
            _host.StateChanged += OnHostChanged;
            if (_started) { FillSide(); return; }
            _started = true;
            FillSide();
            if (_root != null) Load(_root);
        };
        Unloaded += (_, _) => _host.StateChanged -= OnHostChanged;
        ShowCrumbs();
    }

    /// <summary>A drive was connected or disconnected: the side list follows.</summary>
    private void OnHostChanged() => Dispatcher.BeginInvoke(() =>
    {
        var before = _root;
        FillSide();
        if (_root != before && _root != null) Load(_root);
    });

    private List<(string Name, string Root)> Drives() =>
        _host.Mounts.Sessions.Where(s => s.State == MountState.Mounted)
            .OrderBy(s => s.Id == AppHost.CombinedMountId ? 0 : 1).ThenBy(s => s.DriveLetter)
            .Select(s => (s.Definition.DisplayName + " (" + s.DriveLetter + ":)", s.Root)).ToList();

    /// <summary>The side list: every connected drive, then the kinds of files.</summary>
    private void FillSide()
    {
        var icons = Application.Current?.TryFindResource("IconFont") as FontFamily;
        var drives = Drives();
        if (_root != null && drives.All(d => d.Root != _root)) { _root = null; _path = null; _kind = null; }
        _root ??= drives.Count > 0 ? drives[0].Root : null;
        _syncingSide = true;
        try
        {
            _side.Items.Clear();
            foreach (var d in drives) _side.Items.Add(SideItem("\uE753", d.Name, d.Root, icons, translate: false));
            if (drives.Count > 0)
            {
                var label = new TextBlock { Text = T("Kinds of files"), FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(14, 12, 0, 4) };
                label.SetResourceReference(TextBlock.ForegroundProperty, "Dim");
                var plain = new ControlTemplate(typeof(ListBoxItem)) { VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)) };
                _side.Items.Add(new ListBoxItem { Content = label, IsEnabled = false, Focusable = false, Template = plain });
                foreach (var k in Kinds) _side.Items.Add(SideItem(k.Icon, T(k.Name), "kind:" + k.Key, icons, translate: true));
            }
            SelectSide();
        }
        finally { _syncingSide = false; }
        if (drives.Count == 0)
        {
            _list.Items.Clear();
            _status.Text = T("No cloud drive is connected. Open Cloud Drives and connect one: its files show here.");
            ShowCrumbs();
        }
        UpdateButtons();
    }

    private static ListBoxItem SideItem(string glyph, string name, string tag, FontFamily? icons, bool translate)
    {
        var row = new DockPanel();
        var icon = new TextBlock { Text = glyph, FontSize = 15, Width = 26, VerticalAlignment = VerticalAlignment.Center };
        if (icons != null) icon.FontFamily = icons;
        DockPanel.SetDock(icon, Dock.Left);
        row.Children.Add(icon);
        var text = new TextBlock { Text = name, FontSize = 13.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        row.Children.Add(text);
        var item = new ListBoxItem { Content = row, Tag = tag, ToolTip = translate ? null : name };
        System.Windows.Automation.AutomationProperties.SetName(item, name);
        return item;
    }

    private void SelectSide()
    {
        var want = _kind != null ? "kind:" + _kind : _root;
        bool was = _syncingSide;
        _syncingSide = true;
        try
        {
            foreach (var item in _side.Items.OfType<ListBoxItem>())
                if (item.IsEnabled && (item.Tag as string) == want) { _side.SelectedItem = item; break; }
        }
        finally { _syncingSide = was; }
    }

    private static Button Tool(string text, bool primary = false)
    {
        var b = new Button { Content = text, Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 8, 6) };
        if (primary && Application.Current?.TryFindResource("Primary") is Style p) b.Style = p;
        return b;
    }

    private void UpdateButtons()
    {
        _up.IsEnabled = _kind is null && _path != null && _root != null && !SamePath(_path, _root);
        _show.IsEnabled = _root != null;
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(a.TrimEnd('\\'), b.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    private void GoUp()
    {
        if (_kind != null || _path is null || _root is null || SamePath(_path, _root)) return;
        Load(Path.GetDirectoryName(_path.TrimEnd('\\')) ?? _root);
    }

    /// <summary>One folder of a drive. The listing runs off the screen's thread: a cloud folder can take a moment.</summary>
    private async void Load(string path)
    {
        int seq = ++_seq;
        _status.Text = T("Opening…");
        List<Entry>? entries = null;
        string? error = null;
        bool cut = false;
        await Task.Run(() =>
        {
            try
            {
                var opt = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System };
                var list = new List<Entry>();
                foreach (var d in new DirectoryInfo(path).EnumerateFileSystemInfos("*", opt))
                {
                    if (list.Count >= MaxRows) { cut = true; break; }
                    bool dir = (d.Attributes & FileAttributes.Directory) != 0;
                    list.Add(new Entry(d.Name, d.FullName, dir, dir ? 0 : ((FileInfo)d).Length, d.LastWriteTime));
                }
                entries = list.OrderByDescending(e => e.IsDir).ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { error = ex.Message; }
        });
        if (seq != _seq) return;
        if (entries is null) { _status.Text = error ?? T("Something went wrong."); return; }
        _path = path;
        _kind = null;
        SelectSide();
        _status.Text = L("{0} folders · {1} files", entries.Count(e => e.IsDir), entries.Count(e => !e.IsDir))
            + (cut ? " · " + L("only the first {0} are shown", MaxRows.ToString("N0")) : "");
        ShowCrumbs();
        Fill(entries, showFolder: false);
        UpdateButtons();
    }

    /// <summary>Every file of one kind on the drive in use (a few levels deep), newest first.</summary>
    private async void LoadKind(string kind)
    {
        if (_root is not { } drive) return;
        int seq = ++_seq;
        var k = Kinds.First(x => x.Key == kind);
        _kind = kind;
        ShowCrumbs();
        UpdateButtons();
        _list.Items.Clear();
        _status.Text = L("Looking for {0} on {1}…", T(k.Name), drive.TrimEnd('\\'));
        var found = new List<Entry>();
        bool cut = false;
        await Task.Run(() =>
        {
            var ext = new HashSet<string>(k.Ext, StringComparer.OrdinalIgnoreCase);
            var opt = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System };
            var clock = Stopwatch.StartNew();
            var queue = new Queue<(string Dir, int Depth)>();
            queue.Enqueue((drive, 0));
            while (queue.Count > 0 && !cut)
            {
                var (dir, depth) = queue.Dequeue();
                try
                {
                    foreach (var f in new DirectoryInfo(dir).EnumerateFileSystemInfos("*", opt))
                    {
                        if (seq != _seq) return;
                        if (found.Count >= MaxKindRows || clock.Elapsed > KindTime) { cut = true; break; }
                        if ((f.Attributes & FileAttributes.Directory) != 0) { if (depth < KindDepth) queue.Enqueue((f.FullName, depth + 1)); }
                        else if (ext.Contains(f.Extension)) found.Add(new Entry(f.Name, f.FullName, false, ((FileInfo)f).Length, f.LastWriteTime));
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* a folder that cannot be read is left out */ }
            }
        });
        if (seq != _seq) return;
        found = found.OrderByDescending(e => e.Modified).ToList();
        _status.Text = L("{0} files", found.Count.ToString("N0")) + " · " + ByteSize.Format(found.Sum(e => e.Size))
            + (cut ? " · " + T("there are more: open a folder to see all of it") : "");
        Fill(found, showFolder: true);
    }

    private void Fill(List<Entry> entries, bool showFolder)
    {
        _list.Items.Clear();
        var icons = Application.Current?.TryFindResource("IconFont") as FontFamily;
        foreach (var e in entries)
        {
            var row = new DockPanel();
            var detail = (e.IsDir ? "" : ByteSize.Format(e.Size) + "   ") + e.Modified.ToString("dd MMM yyyy HH:mm");
            var info = new TextBlock { Text = detail, FontSize = 11.5, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            info.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            DockPanel.SetDock(info, Dock.Right);
            row.Children.Add(info);
            var icon = new TextBlock { Text = e.IsDir ? "\uE8B7" : "\uE8A5", Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center, FontSize = 16 };
            if (icons != null) icon.FontFamily = icons;
            icon.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
            DockPanel.SetDock(icon, Dock.Left);
            row.Children.Add(icon);
            var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            names.Children.Add(new TextBlock { Text = e.Name, TextTrimming = TextTrimming.CharacterEllipsis });
            if (showFolder)
            {
                var where = new TextBlock { Text = Path.GetDirectoryName(e.Path) ?? "", FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis };
                where.SetResourceReference(TextBlock.ForegroundProperty, "Dim");
                names.Children.Add(where);
            }
            row.Children.Add(names);
            var item = new ListBoxItem { Content = row, Tag = e, Padding = new Thickness(8, 6, 8, 6), HorizontalContentAlignment = HorizontalAlignment.Stretch };
            System.Windows.Automation.AutomationProperties.SetName(item, e.Name);
            _list.Items.Add(item);
        }
    }

    /// <summary>"Z: › Photos › 2024" (each part opens that folder), or "Kinds of files › Photos".</summary>
    private void ShowCrumbs()
    {
        _crumbs.Children.Clear();
        void Crumb(string text, Action? open, bool keep)
        {
            var b = new Button { Content = text, Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(0, 0, 2, 0), IsEnabled = open != null };
            if (Application.Current?.TryFindResource("Ghost") is Style g) b.Style = g;
            if (open != null) b.Click += (_, _) => open();
            _crumbs.Children.Add(b);
        }
        void Sep()
        {
            var sep = new TextBlock { Text = "›", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 2, 0) };
            sep.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            _crumbs.Children.Add(sep);
        }
        if (_root is null) { Crumb(T("Cloud SSD"), null, false); return; }
        if (_kind != null)
        {
            Crumb(_root.TrimEnd('\\'), () => Load(_root), true);
            Sep();
            Crumb(T(Kinds.First(k => k.Key == _kind).Name), null, false);
            return;
        }
        var rest = _path is null || _path.Length <= _root.Length ? Array.Empty<string>() : _path[_root.Length..].Split('\\', StringSplitOptions.RemoveEmptyEntries);
        Crumb(_root.TrimEnd('\\'), rest.Length == 0 ? null : () => Load(_root), true);
        for (int i = 0; i < rest.Length; i++)
        {
            Sep();
            var target = Path.Combine(_root, Path.Combine(rest[..(i + 1)]));
            Crumb(rest[i], i == rest.Length - 1 ? null : () => Load(target), true);
        }
    }

    private List<Entry> Selected() => _list.SelectedItems.OfType<ListBoxItem>().Select(i => i.Tag).OfType<Entry>().ToList();

    /// <summary>A folder opens here; a file opens in its own app (the cloud drive brings it down as it is read).</summary>
    private void OpenSelected()
    {
        if (_list.SelectedItem is not ListBoxItem { Tag: Entry e }) { _status.Text = T("Pick a file or a folder first."); return; }
        if (e.IsDir) { Load(e.Path); return; }
        try { Process.Start(new ProcessStartInfo(e.Path) { UseShellExecute = true }); }
        catch (Win32Exception ex) { _status.Text = ex.Message; }
    }

    private void ShowInExplorer()
    {
        var picked = Selected();
        try
        {
            if (picked.Count == 1) Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{picked[0].Path}\"") { UseShellExecute = true });
            else if ((_path ?? _root) is { } folder) Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
        }
        catch (Win32Exception) { }
    }

    /// <summary>The picked files and folders (folders whole) to a paired phone's Download folder, in one run.</summary>
    private void SendSelected()
    {
        var picked = Selected();
        if (picked.Count == 0) { _status.Text = T("Pick the files and folders to send first (Ctrl or Shift picks several)."); return; }
        var phones = _host.MobileBridge.GetDevices()
            .Where(d => !d.Blocked && d.Kind is DeviceKind.Phone or DeviceKind.Tablet)
            .OrderByDescending(d => d.Listening).ThenBy(d => d.Name).ToList();
        if (phones.Count == 0) { _status.Text = T("No phone is paired yet. Open Devices → Add or connect first."); return; }
        if (phones.Count == 1) { SendTo(phones[0].Id, picked); return; }
        // Several phones: which one.
        var menu = new ContextMenu { PlacementTarget = _send, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        foreach (var d in phones)
        {
            var id = d.Id;
            var mi = new MenuItem { Header = d.Name + (d.Listening ? "" : " · " + T("app not open")) };
            mi.Click += (_, _) => SendTo(id, picked);
            menu.Items.Add(mi);
        }
        menu.IsOpen = true;
    }

    private void SendTo(string deviceId, List<Entry> picked)
    {
        var device = _host.Settings.MobileBridge.PairedDevices.FirstOrDefault(d => d.Id == deviceId);
        if (device is null) return;
        _status.Text = L("Sending to {0} › Download. If it asks, allow it on the phone.", device.Name);
        Watch(_host.MobileBridge.SendToPhoneFolder(device, picked.Select(e => e.Path).ToList(), "Download"), device.Name);
    }

    // ---- the run shown at the bottom ------------------------------------------------------------------------------

    private string _taskPhone = "";

    private void Watch(TransferTask? task, string phone)
    {
        if (_task != null) _task.PropertyChanged -= OnTaskChanged;
        _task = task;
        _taskPhone = phone;
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
        _taskTitle.Text = L("{0} → {1}", what, _taskPhone);
        _taskBar.Value = t.Bytes > 0 ? Math.Min(1, (double)t.DoneBytes / t.Bytes) : t.Files > 0 ? (double)t.DoneFiles / t.Files : 0;
        _taskBar.IsIndeterminate = t.State is "waiting" || (t.IsOpen && t.Files == 0);
        var count = L("{0} of {1}", t.DoneFiles, t.Files);
        var reason = (t.Failed.Select(f => f.Reason).FirstOrDefault() ?? "other") switch
        {
            "denied" => L("allow it on {0} (a note is waiting there)", _taskPhone),
            "network" => T("the connection dropped"),
            "space" => T("not enough space"),
            "missing" => T("the file is gone"),
            _ => T("something went wrong"),
        };
        _taskInfo.Text = t.State switch
        {
            "running" or "waiting" => count + (t.Speed > 0 ? " · " + ByteSize.Format(t.Speed) + "/s" : "") + (t.Current.Length > 0 ? " · " + t.Current : ""),
            "done" => L("Done: {0} files", t.DoneFiles) + " · " + t.SaveTo,
            "partial" => L("{0} done, {1} failed", t.DoneFiles, t.Failed.Count) + " · " + reason,
            "failed" => T("Failed") + " · " + reason,
            "cancelled" => T("Cancelled"),
            _ => count,
        };
        _taskCancel.Visibility = t.IsOpen ? Visibility.Visible : Visibility.Collapsed;
    }
}
