using System.Windows;
using System.Windows.Controls;
using CloudSSD.App;
using CloudSSD.Desktop.ViewModels;

namespace CloudSSD.Desktop.Views;

/// <summary>
/// The "Files" page: at the top "Phone storage" (one chip per paired phone or tablet) and "Cloud SSD"; below, the one
/// chosen — the phone's storage (<see cref="PhoneFilesView"/>) or this PC's cloud drives (<see cref="SsdFilesView"/>).
/// Each keeps its place while the page is open.
/// </summary>
public sealed class PhoneFilesPageView : UserControl
{
    private readonly WrapPanel _chips = new() { Margin = new Thickness(0, 0, 0, 12) };
    private readonly ContentControl _body = new();
    private readonly TextBlock _empty;
    private readonly Dictionary<string, PhoneFilesView> _views = new();
    private SsdFilesView? _ssd;
    private FrameworkElement? _xfers;
    private const string Ssd = "ssd", NoPhone = "none", Xfers = "transfers";
    private AppHost? _host;
    private string? _shown;

    private static string T(string s) => LocalizationManager.T(s);

    public PhoneFilesPageView()
    {
        var root = new DockPanel { Margin = new Thickness(0, 0, 14, 14) };
        DockPanel.SetDock(_chips, Dock.Top);
        root.Children.Add(_chips);
        _empty = new TextBlock
        {
            Text = T("No phone is paired yet. Open Devices → Add or connect, then come back: its storage shows here, like a phone on a USB cable."),
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0),
        };
        _empty.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        root.Children.Add(_body);
        Content = root;
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
    }

    private static MainViewModel? Main => Application.Current?.MainWindow?.DataContext as MainViewModel;

    private void Attach()
    {
        _host ??= (DataContext as PageViewModel)?.Host ?? Main?.Host;
        if (_host is null) return;
        _host.MobileBridge.DevicesChanged -= OnDevicesChanged;
        _host.MobileBridge.DevicesChanged += OnDevicesChanged;
        if (Main is { } main)
        {
            main.PhoneFilesRequested -= Rebuild;
            main.PhoneFilesRequested += Rebuild;
        }
        Rebuild();
    }

    private void Detach()
    {
        if (_host != null) _host.MobileBridge.DevicesChanged -= OnDevicesChanged;
        if (Main is { } main) main.PhoneFilesRequested -= Rebuild;
    }

    private void OnDevicesChanged() => Dispatcher.BeginInvoke(Rebuild);

    /// <summary>The chips: each paired phone or tablet, then the Cloud SSD. The one asked for, else the one shown before, else the first.</summary>
    private void Rebuild()
    {
        if (_host is null) return;
        var phones = _host.MobileBridge.GetDevices()
            .Where(d => !d.Blocked && d.Kind is DeviceKind.Phone or DeviceKind.Tablet)
            .OrderByDescending(d => d.Listening).ThenBy(d => d.Name).ToList();
        var wanted = (DataContext as PhoneFilesPageViewModel)?.WantedDeviceId;
        if (DataContext is PhoneFilesPageViewModel vm) vm.WantedDeviceId = null;
        var pick = (wanted is Ssd or Xfers ? wanted : null)
            ?? phones.FirstOrDefault(d => d.Id == wanted)?.Id
            ?? (_shown is Ssd or Xfers ? _shown : null)
            ?? phones.FirstOrDefault(d => d.Id == _shown)?.Id
            ?? phones.FirstOrDefault()?.Id
            ?? (_shown ?? NoPhone);

        _chips.Children.Clear();
        void Chip(string text, string id)
        {
            var chip = new RadioButton
            {
                Content = text, GroupName = "cssd-phone-files", IsChecked = id == pick, Padding = new Thickness(16, 0, 16, 0),
                Height = 34, Margin = new Thickness(0, 0, 8, 0), HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
            };
            if (Application.Current?.TryFindResource("PillRadio") is Style ps) chip.Style = ps;
            System.Windows.Automation.AutomationProperties.SetName(chip, text);
            chip.Checked += (_, _) => Show(id);
            _chips.Children.Add(chip);
        }
        // One phone: "Phone storage". Several: "Phone storage · <name>" each.
        foreach (var d in phones)
            Chip(T("Phone storage") + (phones.Count > 1 ? " · " + d.Name : "") + (d.Listening ? "" : " · " + T("app not open")), d.Id);
        if (phones.Count == 0) Chip(T("Phone storage"), NoPhone);
        Chip(T("Cloud SSD"), Ssd);
        // What is moving between them right now, and what finished today.
        Chip(T("Transfers"), Xfers);
        Show(pick);
    }

    /// <summary>
    /// The Transfers part: what is moving now and what finished today; "History" shows every transfer recorded
    /// instead ("← Transfers" returns); "Send files to a device…" (or files dropped here) offers them to a phone or
    /// another PC, which accepts them on its own screen.
    /// </summary>
    private FrameworkElement BuildTransfers(AppHost host)
    {
        var now = new TransfersView { Host = host };
        var history = new TransferHistoryView { Visibility = Visibility.Collapsed, DataContext = Main?.Hub };
        now.ShowHistory += () =>
        {
            // The history is the phone section's list: read as when that section opens.
            Main?.Hub.OnShown();
            history.DataContext ??= Main?.Hub;
            now.Visibility = Visibility.Collapsed;
            history.Visibility = Visibility.Visible;
        };
        history.Back += () =>
        {
            history.Visibility = Visibility.Collapsed;
            now.Visibility = Visibility.Visible;
        };

        void Send(string[] files)
        {
            if (files.Length == 0) return;
            new SendToDevicesWindow(host, files) { Owner = Window.GetWindow(this) }.ShowDialog();
        }
        var send = new Button { Content = T("Send files to a device…"), Padding = new Thickness(14, 6, 14, 6), HorizontalAlignment = HorizontalAlignment.Left };
        send.Click += (_, _) =>
        {
            var d = new Microsoft.Win32.OpenFileDialog { Title = T("Send files to a device…"), Multiselect = true };
            if (d.ShowDialog(Window.GetWindow(this)) == true) Send(d.FileNames);
        };
        var hint = new TextBlock { Text = T("or drop files here"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), FontSize = 12 };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        top.Children.Add(send);
        top.Children.Add(hint);

        var both = new Grid();
        both.Children.Add(now);
        both.Children.Add(history);
        var root = new DockPanel { AllowDrop = true, Background = System.Windows.Media.Brushes.Transparent };
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);
        root.Children.Add(both);
        root.DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
        root.Drop += (_, e) => { if (e.Data.GetData(DataFormats.FileDrop) is string[] files) Send(files); };
        return root;
    }

    private void Show(string deviceId)
    {
        if (_host is null) return;
        if (deviceId == Ssd)
        {
            _shown = Ssd;
            _body.Content = _ssd ??= new SsdFilesView(_host);
            return;
        }
        if (deviceId == Xfers)
        {
            _shown = Xfers;
            _body.Content = _xfers ??= BuildTransfers(_host);
            return;
        }
        if (deviceId == NoPhone) { _shown = NoPhone; _body.Content = _empty; return; }
        if (_shown == deviceId && _body.Content is PhoneFilesView) return;
        var device = _host.Settings.MobileBridge.PairedDevices.FirstOrDefault(d => d.Id == deviceId);
        if (device is null) return;
        if (!_views.TryGetValue(deviceId, out var view)) _views[deviceId] = view = new PhoneFilesView(_host, device);
        _shown = deviceId;
        _body.Content = view;
    }
}
