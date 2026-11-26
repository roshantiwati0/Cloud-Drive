using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CloudSSD.App;
using CloudSSD.Core;
using CloudSSD.Desktop.ViewModels;

namespace CloudSSD.Desktop.Views;

/// <summary>
/// Mobile Hub → Transfers, like a phone maker's PC suite: what is moving now (one card per send: a folder or many files,
/// with how it goes — Wi-Fi, USB, hotspot —, speed, time left, the file on the way, where it is saved), what finished
/// today (Open folder, Retry what failed), and where files from phones are saved. The full history is its own view
/// (<see cref="ShowHistory"/>). Reads <c>host.MobileBridge.Tasks</c>; the page's DataContext gives the host.
/// </summary>
public sealed class TransfersView : UserControl
{
    private readonly StackPanel _now = new();
    private readonly StackPanel _done = new();
    private readonly StackPanel _asking = new();
    private readonly Border _askingCard;
    private readonly CheckBox _askAlways;
    private readonly TextBlock _askNote;
    private bool _syncingAsk;
    private readonly TextBlock _nowEmpty, _doneEmpty, _savedIn;
    private readonly Dictionary<string, RadioButton> _filters = new();
    private readonly DispatcherTimer _later = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private AppHost? _host;
    private string _filter = "all";

    /// <summary>The app; when left out, the page's DataContext (or the main window's) gives it.</summary>
    public AppHost? Host { get => _host; set => _host = value; }

    /// <summary>The History button: the page shows the full history (its own view).</summary>
    public event Action? ShowHistory;

    private static string T(string s) => LocalizationManager.T(s);
    private static string L(string f, params object[] a) => string.Format(T(f), a);

    public TransfersView()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(340) });

        // Left: Now, with the filter and History.
        var left = new DockPanel();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var history = Btn(T("History"));
        history.Click += (_, _) => ShowHistory?.Invoke();
        DockPanel.SetDock(history, Dock.Right);
        head.Children.Add(history);
        var filters = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 8, 0) };
        foreach (var (key, label) in new[] { ("all", "All"), ("in", "From devices"), ("out", "To devices") })
        {
            var r = new RadioButton { Content = T(label), GroupName = "cssd-xfer-filter", IsChecked = key == "all", Height = 30, MinWidth = 52, Margin = new Thickness(0, 0, 6, 0),
                HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
            if (Application.Current?.TryFindResource("PillRadio") is Style ps) r.Style = ps;
            r.Checked += (_, _) => { _filter = key; Refresh(); };
            _filters[key] = r;
            filters.Children.Add(r);
        }
        DockPanel.SetDock(filters, Dock.Right);
        head.Children.Add(filters);
        head.Children.Add(Title(T("Now")));
        DockPanel.SetDock(head, Dock.Top);
        left.Children.Add(head);
        _nowEmpty = Muted(T("Nothing is moving right now. Send from the phone, or open a phone's files (Files → Phone storage)."));
        _now.Children.Add(_nowEmpty);
        left.Children.Add(_now);
        Grid.SetColumn(left, 0);
        grid.Children.Add(left);

        // Right: files waiting for an OK (a guest's, or everyone's when "always ask" is on), done today, where files are saved.
        var right = new StackPanel();
        _askingCard = Card();
        var askBox = new StackPanel();
        askBox.Children.Add(Title(T("Files to receive")));
        askBox.Children.Add(_asking);
        askBox.Children.Add(Muted(T("Nothing is saved until you accept. A name already there is kept too, as \"name (2)\", unless your own device asked to replace it.")));
        _askingCard.Child = askBox;
        _askingCard.SetResourceReference(Border.BorderBrushProperty, "Accent");
        _askingCard.Visibility = Visibility.Collapsed;
        right.Children.Add(_askingCard);
        var doneCard = Card();
        var doneBox = new StackPanel();
        doneBox.Children.Add(Title(T("Done today")));
        _doneEmpty = Muted(T("Nothing yet today."));
        _done.Children.Add(_doneEmpty);
        doneBox.Children.Add(_done);
        doneCard.Child = doneBox;
        right.Children.Add(doneCard);

        var whereCard = Card();
        var where = new StackPanel();
        where.Children.Add(Title(T("Files from phones are saved in")));
        // Two places: what a phone sends lands in the upload folder; what is taken here with "Save to PC" in Received.
        _savedIn = SavedRow(where, T("Sent from a phone"), UploadRoot);
        SavedRow(where, T("Taken here with \"Save to PC\""), ReceivedRoot).Text = Pretty(ReceivedRoot());
        // Off (the default): your own devices save at once and only a guest's files wait. On: every send waits for an OK.
        _askAlways = new CheckBox { Content = T("Ask me before receiving, also from my own devices"), FontSize = 12, Margin = new Thickness(0, 12, 0, 0) };
        _askAlways.Checked += OnAskChanged;
        _askAlways.Unchecked += OnAskChanged;
        where.Children.Add(_askAlways);
        _askNote = Muted("");
        where.Children.Add(_askNote);
        whereCard.Child = where;
        right.Children.Add(whereCard);
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);

        Content = grid;
        _later.Tick += (_, _) => { _later.Stop(); Refresh(); };
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
    }

    // ---- wiring --------------------------------------------------------------------------------------------------

    private void Attach()
    {
        _host ??= (DataContext as PageViewModel)?.Host ?? (Application.Current?.MainWindow?.DataContext as MainViewModel)?.Host;
        if (_host is null) return;
        _host.MobileBridge.Tasks.Changed -= OnChanged;
        _host.MobileBridge.Tasks.Changed += OnChanged;
        _savedIn.Text = Pretty(UploadRoot());
        _syncingAsk = true;
        _askAlways.IsChecked = _host.Settings.MobileBridge.AskBeforeReceiving == "always";
        _syncingAsk = false;
        ShowAskNote();
        Refresh();
    }

    private void Detach()
    {
        if (_host != null) _host.MobileBridge.Tasks.Changed -= OnChanged;
    }

    private void OnAskChanged(object sender, RoutedEventArgs e)
    {
        if (_syncingAsk || _host is null) return;
        _host.Settings.MobileBridge.AskBeforeReceiving = _askAlways.IsChecked == true ? "always" : "never-mine";
        _host.SaveSettings();
        ShowAskNote();
    }

    private void ShowAskNote() => _askNote.Text = _askAlways.IsChecked == true
        ? T("Every send waits here for your OK.")
        : T("Files from your own devices are saved without asking.");

    /// <summary>Many changes a second while files move: drawn at most 4 times a second.</summary>
    private void OnChanged(TransferTask _) => Dispatcher.BeginInvoke(() => { if (!_later.IsEnabled) _later.Start(); });

    /// <summary>One place files land: what it is for, the folder in words, and a button that opens it.</summary>
    private static TextBlock SavedRow(Panel into, string label, Func<string> folder)
    {
        into.Children.Add(Muted(label, 10));
        var row = new DockPanel { Margin = new Thickness(0, 2, 0, 0) };
        var open = Btn(T("Open folder"));
        open.Margin = new Thickness(8, 0, 0, 0);
        open.Click += (_, _) => { var d = folder(); Directory.CreateDirectory(d); Explore(d); };
        DockPanel.SetDock(open, Dock.Right);
        row.Children.Add(open);
        var path = new TextBlock { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(path);
        into.Children.Add(row);
        return path;
    }

    /// <summary>"C:UsersmeDownloadsX" as "Downloads › X" (under the user's folder), else the path as it is.</summary>
    private static string Pretty(string path)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return path.StartsWith(home, StringComparison.OrdinalIgnoreCase) && path.Length > home.Length
            ? path[(home.Length + 1)..].Replace("\\", " › ") : path;
    }

    /// <summary>Where a phone's sends land: the bridge's upload folder (DownloadsCloud-SSD_Uploads unless changed).</summary>
    private string UploadRoot() => _host?.Settings.MobileBridge.UploadDirectory
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "Cloud-SSD_Uploads");

    private static string ReceivedRoot() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "Cloud-SSD", "Received");

    private bool Shows(TransferTask t) => _filter switch
    {
        "in" => t.Dir is "toPc" or "fromDevice",
        "out" => t.Dir is "toPhone" or "toDevice",
        _ => true,
    };

    private void Refresh()
    {
        if (_host is null) return;
        var all = _host.MobileBridge.Tasks.All;
        var now = all.Where(t => t.IsOpen && t.State != "asking" && Shows(t)).ToList();
        // Waiting for an OK: shown whatever the filter says, so none is missed.
        var asking = all.Where(t => t.State == "asking").ToList();
        _asking.Children.Clear();
        foreach (var t in asking) _asking.Children.Add(AskingRow(t));
        _askingCard.Visibility = asking.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        var today = DateTime.Today;
        var done = all.Where(t => !t.IsOpen && Shows(t) && (t.Ended ?? t.Started).LocalDateTime >= today).Take(12).ToList();

        _now.Children.Clear();
        if (now.Count == 0) _now.Children.Add(_nowEmpty);
        foreach (var t in now) _now.Children.Add(NowCard(t));
        _done.Children.Clear();
        if (done.Count == 0) _done.Children.Add(_doneEmpty);
        foreach (var t in done) _done.Children.Add(DoneRow(t));
    }

    // ---- the cards ------------------------------------------------------------------------------------------------

    /// <summary>"1 file", "12 files".</summary>
    private static string Count(int n) => n == 1 ? T("1 file") : L("{0} files", n);

    private static string What(TransferTask t) => t.Kind == "folder" && t.Folder.Length > 0 ? t.Folder
        : t.Files == 1 && (t.Name.Length > 0 || t.Current.Length > 0) ? (t.Name.Length > 0 ? t.Name : t.Current) : Count(t.Files);

    private static bool Incoming(TransferTask t) => t.Dir is "toPc" or "fromDevice";

    private static string Arrow(TransferTask t) => Incoming(t) ? "←" : "→";

    private static string LinkText(string link) => link switch
    {
        "usb" => "USB",
        "hotspot" => T("Hotspot"),
        "tailscale" => T("Internet"),
        "relay" => T("Through the PC"),
        _ => "Wi-Fi",
    };

    private UIElement NowCard(TransferTask t)
    {
        var card = Card();
        var box = new StackPanel();

        var top = new DockPanel();
        var circle = new Border { Width = 38, Height = 38, CornerRadius = new CornerRadius(19), Margin = new Thickness(0, 0, 12, 0) };
        circle.SetResourceReference(Border.BackgroundProperty, "AccentSoft");
        var arrow = new TextBlock { Text = Incoming(t) ? "" : "", FontSize = 16, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        if (Application.Current?.TryFindResource("IconFont") is FontFamily icons) arrow.FontFamily = icons;
        arrow.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
        circle.Child = arrow;
        DockPanel.SetDock(circle, Dock.Left);
        top.Children.Add(circle);
        if (t.CanCancel)
        {
            var cancel = Btn(T("Cancel"));
            cancel.Margin = new Thickness(8, 0, 0, 0);
            cancel.Click += (_, _) => _host?.MobileBridge.Tasks.Cancel(t.Id);
            DockPanel.SetDock(cancel, Dock.Right);
            top.Children.Add(cancel);
        }
        var badge = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 3, 10, 3), VerticalAlignment = VerticalAlignment.Center };
        badge.SetResourceReference(Border.BackgroundProperty, "AccentSoft");
        var badgeText = new TextBlock { Text = LinkText(t.Link) + (t.Link is "wifi" or "usb" or "hotspot" ? " · " + T("fast") : ""), FontSize = 12, FontWeight = FontWeights.SemiBold };
        badgeText.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
        badge.Child = badgeText;
        DockPanel.SetDock(badge, Dock.Right);
        top.Children.Add(badge);
        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(new TextBlock { Text = $"{What(t)} {Arrow(t)} {(Incoming(t) ? t.Device : t.Device)}", FontSize = 15, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        names.Children.Add(Muted(Count(t.Files) + (t.Bytes > 0 ? " · " + ByteSize.Format(t.Bytes) : ""), 0));
        top.Children.Add(names);
        box.Children.Add(top);

        var bar = new ProgressBar { Height = 6, Maximum = 1, Margin = new Thickness(0, 12, 0, 8), IsIndeterminate = t.State == "waiting" || t.Files == 0 };
        bar.Value = t.Bytes > 0 ? Math.Min(1, (double)t.DoneBytes / t.Bytes) : t.Files > 0 ? (double)t.DoneFiles / t.Files : 0;
        box.Children.Add(bar);

        var line = new DockPanel();
        var right = Muted(Speed(t), 0);
        DockPanel.SetDock(right, Dock.Right);
        line.Children.Add(right);
        line.Children.Add(Muted(t.State == "waiting" ? T("Waiting for the one before it")
            : t.State == "paused" ? T("Paused")
            : L("{0} of {1} files", t.DoneFiles, t.Files) + (t.Bytes > 0 ? " · " + L("{0} of {1}", ByteSize.Format(t.DoneBytes), ByteSize.Format(t.Bytes)) : ""), 0));
        box.Children.Add(line);
        if (t.Current.Length > 0) box.Children.Add(Muted(L("Now: {0}", t.Current), 4));
        if (t.SaveTo.Length > 0) box.Children.Add(Muted(L("Saving to {0}", t.SaveTo), 4));
        card.Child = box;
        return card;
    }

    /// <summary>One send waiting for an OK: what, from whom, how big; Accept (a taken name is kept too) or Decline.</summary>
    private UIElement AskingRow(TransferTask t)
    {
        var box = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        box.Children.Add(new TextBlock { Text = What(t), FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        box.Children.Add(Muted(L("From {0}", t.Device) + " · " + Count(t.Files) + (t.Bytes > 0 ? " · " + ByteSize.Format(t.Bytes) : ""), 2));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var accept = Btn(T("Accept"));
        if (Application.Current?.TryFindResource("Primary") is Style p) accept.Style = p;
        accept.Click += (_, _) => { _host?.MobileBridge.Tasks.Accept(t.Id); Refresh(); };
        var decline = Btn(T("Decline"));
        decline.Margin = new Thickness(8, 0, 0, 0);
        decline.Click += (_, _) => { _host?.MobileBridge.Tasks.Decline(t.Id); Refresh(); };
        buttons.Children.Add(accept);
        buttons.Children.Add(decline);
        box.Children.Add(buttons);
        return box;
    }

    private static string Speed(TransferTask t)
    {
        if (t.State != "running" || t.Speed <= 0) return "";
        var left = t.Bytes > t.DoneBytes ? TimeSpan.FromSeconds((t.Bytes - t.DoneBytes) / (double)t.Speed) : TimeSpan.Zero;
        var leftText = left.TotalSeconds < 1 ? "" : left.TotalMinutes < 1 ? L("{0} s left", (int)left.TotalSeconds)
            : left.TotalHours < 1 ? L("{0} min left", (int)Math.Ceiling(left.TotalMinutes)) : L("{0} h left", Math.Round(left.TotalHours, 1));
        return ByteSize.Format(t.Speed) + "/s" + (leftText.Length > 0 ? " · " + leftText : "");
    }

    private UIElement DoneRow(TransferTask t)
    {
        var row = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        var bad = t.State is "partial" or "failed";
        var dot = new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        dot.SetResourceReference(Border.BackgroundProperty, bad ? "Warning" : t.State == "cancelled" ? "Dim" : "Accent");
        DockPanel.SetDock(dot, Dock.Left);
        row.Children.Add(dot);
        Button? action = null;
        if (t.CanRetry && t.Failed.Count > 0)
        {
            action = Btn(L("Retry {0}", t.Failed.Count));
            action.Click += (_, _) => _host?.MobileBridge.Tasks.Retry(t.Id);
        }
        else if (t.OpenPath.Length > 0 && (Directory.Exists(t.OpenPath) || File.Exists(t.OpenPath)))
        {
            action = Btn(T("Open folder"));
            action.Click += (_, _) => Explore(t.OpenPath);
        }
        if (action != null)
        {
            action.Margin = new Thickness(8, 0, 0, 0);
            DockPanel.SetDock(action, Dock.Right);
            row.Children.Add(action);
        }
        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = $"{What(t)} {Arrow(t)} {t.Device}", FontWeight = FontWeights.Medium, TextTrimming = TextTrimming.CharacterEllipsis });
        var when = (t.Ended ?? t.Started).LocalDateTime.ToString("HH:mm");
        var sub = t.State switch
        {
            "done" => L("Done {0}", when) + " · " + LinkText(t.Link),
            "partial" => L("{0} done, {1} failed", t.DoneFiles, t.Failed.Count) + " · " + Reason(t),
            "failed" => T("Failed") + " · " + Reason(t),
            "cancelled" => T("Cancelled") + " · " + when,
            _ => when,
        };
        var s = Muted(sub, 0);
        if (bad) s.SetResourceReference(TextBlock.ForegroundProperty, "Warning");
        text.Children.Add(s);
        row.Children.Add(text);
        return row;
    }

    private static string Reason(TransferTask t) => (t.Failed.Select(f => f.Reason).FirstOrDefault() ?? "other") switch
    {
        "denied" => T("not allowed on the device"),
        "network" => T("the connection dropped"),
        "space" => T("not enough space"),
        "missing" => T("the file is gone"),
        "cancelled" => T("Cancelled"),
        _ => T("something went wrong"),
    };

    // ---- small parts ----------------------------------------------------------------------------------------------

    private static Border Card()
    {
        var b = new Border();
        if (Application.Current?.TryFindResource("Card") is Style s) b.Style = s;
        b.Margin = new Thickness(0, 0, 0, 12);
        return b;
    }

    private static TextBlock Title(string text)
    {
        var t = new TextBlock { Text = text, FontSize = 16, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        t.SetResourceReference(TextBlock.ForegroundProperty, "Text");
        return t;
    }

    private static TextBlock Muted(string text, double top = 8)
    {
        var t = new TextBlock { Text = text, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, top, 0, 0) };
        t.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        return t;
    }

    private static Button Btn(string text) => new() { Content = text, Padding = new Thickness(12, 4, 12, 4), VerticalAlignment = VerticalAlignment.Center };

    private static void Explore(string path)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception) { }
    }
}
