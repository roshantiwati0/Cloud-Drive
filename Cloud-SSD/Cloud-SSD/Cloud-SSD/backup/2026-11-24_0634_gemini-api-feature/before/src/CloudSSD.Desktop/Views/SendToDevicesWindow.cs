using System.IO;
using System.Windows;
using System.Windows.Controls;
using CloudSSD.App;
using CloudSSD.Core;

namespace CloudSSD.Desktop.Views;

/// <summary>
/// "Send 3 files · 12 MB to: [x] Redmi 12 [ ] Pixel 7". Each chosen device gets an offer it accepts on its screen;
/// or a link anyone on this Wi-Fi (or the tailnet) can open for an hour, a day or a week.
/// </summary>
public sealed class SendToDevicesWindow : Window
{
    private readonly AppHost _host;
    private readonly IReadOnlyList<string> _files;
    /// <summary>Folders dropped (a project…): sent whole, keeping their tree.</summary>
    private readonly IReadOnlyList<string> _folders;
    private readonly TextBlock _head;
    private readonly TextBlock _left;
    private readonly CheckBox _everything;
    private readonly Button _send;
    private int _counting;
    private readonly Dictionary<string, CheckBox> _boxes = new();
    private readonly TextBlock _status;
    private readonly ComboBox _lifetime;

    private static string T(string s) => LocalizationManager.T(s);
    private static string L(string f, params object[] a) => string.Format(T(f), a);

    /// <summary>What was sent, for the caller's list: "Redmi 12: holiday.jpg, notes.txt".</summary>
    public List<string> Sent { get; } = new();

    public SendToDevicesWindow(AppHost host, IReadOnlyList<string> paths, string? deviceId = null)
    {
        _host = host;
        _files = paths.Where(File.Exists).ToList();
        _folders = paths.Where(Directory.Exists).ToList();
        Title = T("Send files");
        Width = 520;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        if (Application.Current?.TryFindResource("DialogWindow") is Style st) Style = st;

        var panel = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
        _head = new TextBlock { TextWrapping = TextWrapping.Wrap };
        if (Application.Current?.TryFindResource("H1") is Style h1) _head.Style = h1;
        panel.Children.Add(_head);
        var all = _folders.Select(f => Path.GetFileName(f.TrimEnd('\\', '/')) + "\\").Concat(_files.Select(Path.GetFileName)).ToList();
        var names = Caption(string.Join(", ", all.Take(4)) + (all.Count > 4 ? " " + L("and {0} more", all.Count - 4) : ""), new Thickness(0, 0, 0, 8));
        panel.Children.Add(names);
        // A folder keeps its tree on the other side. What a build or package manager makes again is left out by default
        // (as a project is usually shared: .git, node_modules, bin, obj…), and can be put back in.
        _left = Caption("", new Thickness(0, 0, 0, 4));
        _everything = new CheckBox { Content = T("Include build output and hidden tool folders"), Margin = new Thickness(0, 0, 0, 12) };
        _everything.Click += (_, _) => Count();
        if (_folders.Count > 0) { panel.Children.Add(_left); panel.Children.Add(_everything); }

        panel.Children.Add(Label(T("TO")));
        var bridge = host.MobileBridge;
        var devices = bridge.GetDevices().OrderByDescending(d => d.Online).ThenBy(d => d.Name).ToList();
        if (devices.Count == 0) panel.Children.Add(Caption(T("No device is paired yet. Add one in Devices first."), new Thickness(0)));
        bool anyChecked = false;
        foreach (var d in devices)
        {
            var paired = host.Settings.MobileBridge.PairedDevices.FirstOrDefault(p => p.Id == d.Id);
            if (paired is null) continue;
            bool can = LocalMobileBridgeService.CanReceive(paired) && !paired.IsBlocked;
            var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
            var state = new TextBlock
            {
                FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0),
                Text = !can ? T("cannot take files yet") : d.Online ? T("● Online now") : L("○ Last seen {0}", CloudSSD.Desktop.ViewModels.Fmt.Ago(d.LastSeen, DateTimeOffset.Now))
            };
            state.SetResourceReference(TextBlock.ForegroundProperty, d.Online && can ? "Success" : "Muted");
            DockPanel.SetDock(state, Dock.Right);
            row.Children.Add(state);
            // The device dropped on, else the one phone that is online.
            bool check = can && (deviceId != null ? d.Id == deviceId : !anyChecked && d.Online);
            anyChecked |= check;
            var box = new CheckBox { Content = d.Name, IsEnabled = can, IsChecked = check, VerticalAlignment = VerticalAlignment.Center };
            _boxes[d.Id] = box;
            row.Children.Add(box);
            panel.Children.Add(row);
        }
        panel.Children.Add(Caption(T("It shows up on the device to accept. A device that is off gets it when it is back (within a day)."), new Thickness(0, 8, 0, 0)));

        // Or a link.
        panel.Children.Add(Label(T("OR A LINK"), new Thickness(0, 16, 0, 4)));
        var linkRow = new DockPanel();
        var copy = new Button { Content = T("Copy link"), Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(8, 0, 0, 0) };
        copy.Click += (_, _) => CopyLinks();
        DockPanel.SetDock(copy, Dock.Right);
        linkRow.Children.Add(copy);
        _lifetime = new ComboBox { Width = 130, Margin = new Thickness(8, 0, 0, 0) };
        foreach (var s in new[] { T("for 1 hour"), T("for 24 hours"), T("for 7 days") }) _lifetime.Items.Add(s);
        _lifetime.SelectedIndex = 0;
        DockPanel.SetDock(_lifetime, Dock.Right);
        linkRow.Children.Add(_lifetime);
        linkRow.Children.Add(new TextBlock { Text = T("Anyone on this Wi-Fi can download it"), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(linkRow);

        _status = Caption("", new Thickness(0, 10, 0, 0));
        panel.Children.Add(_status);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var send = _send = new Button { Content = T("Send"), IsDefault = true, Padding = new Thickness(18, 6, 18, 6), IsEnabled = _files.Count > 0 };
        if (Application.Current?.TryFindResource("Primary") is Style p) send.Style = p;
        send.Click += (_, _) => SendNow();
        var cancel = new Button { Content = T("Close"), IsCancel = true, Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(0) };
        buttons.Children.Add(send);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        Content = panel;
        Count();
    }

    /// <summary>Counts what will go (folders are walked in the background: a big project takes a moment).</summary>
    private void Count()
    {
        long fileBytes = _files.Sum(f => new FileInfo(f).Length);
        if (_folders.Count == 0)
        {
            _head.Text = L("Send {0} file(s) · {1}", _files.Count, ByteSize.Format(fileBytes));
            return;
        }
        int run = ++_counting;
        bool everything = _everything.IsChecked == true;
        _head.Text = T("Counting the files…");
        _send.IsEnabled = false;
        _ = Task.Run(() =>
        {
            int files = 0, skippedFiles = 0;
            long bytes = 0, skippedBytes = 0;
            var skipped = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in _folders)
            {
                var (list, left) = ProjectFilter.Walk(f, everything, ShareStore.MaxFolderItems);
                files += list.Count; bytes += list.Sum(x => x.Size);
                skippedFiles += left.Files; skippedBytes += left.Bytes;
                foreach (var s in left.Folders) skipped.Add(s);
            }
            return (files, bytes, skippedFiles, skippedBytes, skipped);
        }).ContinueWith(t => Dispatcher.BeginInvoke(() =>
        {
            if (run != _counting) return;
            if (t.IsFaulted)
            {
                // Could not look inside (a folder went away, no access): it is tried again when sent.
                _head.Text = L("Send {0} file(s) · {1}", _folders.Count + _files.Count, "…");
                _left.Text = t.Exception?.GetBaseException().Message ?? "";
                _send.IsEnabled = true;
                return;
            }
            var (files, bytes, skippedFiles, skippedBytes, skipped) = t.Result;
            _head.Text = L("Send {0} folder(s) · {1} files · {2}", _folders.Count, files + _files.Count, ByteSize.Format(bytes + fileBytes));
            _left.Text = skippedFiles == 0 ? T("Everything inside goes, in the same folders.")
                : L("Left out: {0} files · {1} ({2}). They can be made again on the other side.", skippedFiles, ByteSize.Format(skippedBytes), string.Join(", ", skipped.Take(5)) + (skipped.Count > 5 ? "…" : ""));
            if (files + _files.Count >= ShareStore.MaxFolderItems) _left.Text += " " + L("At most {0} files at once: the rest is not sent.", ShareStore.MaxFolderItems);
            _send.IsEnabled = files + _files.Count > 0;
        }), TaskScheduler.Default);
    }

    private static TextBlock Caption(string text, Thickness margin)
    {
        var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = margin };
        if (Application.Current?.TryFindResource("Caption") is Style c) t.Style = c;
        return t;
    }

    private static TextBlock Label(string text, Thickness? margin = null)
    {
        var t = new TextBlock { Text = text, Margin = margin ?? new Thickness(0, 0, 0, 4) };
        if (Application.Current?.TryFindResource("Label") is Style l) t.Style = l;
        return t;
    }

    /// <summary>Offers the files to every ticked device.</summary>
    public void SendNow()
    {
        var chosen = _boxes.Where(kv => kv.Value.IsChecked == true).Select(kv => kv.Key).ToList();
        if (chosen.Count == 0) { _status.Text = T("Tick a device first."); return; }
        var names = new List<string>();
        foreach (var id in chosen)
        {
            var d = _host.Settings.MobileBridge.PairedDevices.FirstOrDefault(p => p.Id == id);
            if (d is null) continue;
            if (_host.MobileBridge.SendToDevice(d, _folders.Concat(_files), _everything.IsChecked == true) is { } offer)
            {
                names.Add(d.Name);
                var what = offer.TopFolders.Select(f => f + "\\").Concat(offer.Items.Where(i => i.Folder.Length == 0).Select(i => i.Name));
                Sent.Add($"{d.Name}: {string.Join(", ", what)}");
            }
        }
        if (names.Count == 0) { _status.Text = T("Nothing could be sent."); return; }
        DialogResult = true;
    }

    private void CopyLinks()
    {
        if (_files.Count == 0) return;
        var life = _lifetime.SelectedIndex switch { 1 => TimeSpan.FromHours(24), 2 => TimeSpan.FromDays(7), _ => TimeSpan.FromHours(1) };
        var links = _files.Select(f => _host.MobileBridge.CreateShareLink(f, life, null)).Where(l => l != null).ToList();
        if (links.Count == 0) return;
        try { Clipboard.SetText(string.Join(Environment.NewLine, links)); }
        catch (System.Runtime.InteropServices.COMException) { }
        _status.Text = links.Count == 1 ? L("Link copied: {0}", links[0]!) : L("{0} links copied.", links.Count);
        Sent.Add(T("Link") + ": " + string.Join(", ", _files.Select(Path.GetFileName)));
    }
}
