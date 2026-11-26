using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CloudSSD.App;
using CloudSSD.Core;

namespace CloudSSD.Desktop.Views;

/// <summary>
/// "What may Redmi 12 do?": one tick per category (a switch that is off for every device says so), and what the device
/// opened lately. Shown from the device's card, and right after a new device pairs.
/// </summary>
public sealed class DevicePermissionsWindow : Window
{
    private readonly AppHost _host;
    private readonly PairedDevice _device;
    private readonly Dictionary<string, CheckBox> _boxes = new();

    private static string T(string s) => LocalizationManager.T(s);
    private static string L(string f, params object[] a) => string.Format(T(f), a);

    public DevicePermissionsWindow(AppHost host, PairedDevice device, bool justPaired, Action? openSwitches = null)
    {
        _host = host;
        _device = device;
        Title = T("Permissions");
        Width = 560;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        if (Application.Current?.TryFindResource("DialogWindow") is Style st) Style = st;

        var bridge = host.MobileBridge;
        var settings = host.Settings.MobileBridge;
        var grants = bridge.Access.Current(device);

        var panel = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
        var head = new TextBlock { Text = L("What may {0} do?", device.Name), TextWrapping = TextWrapping.Wrap };
        if (Application.Current?.TryFindResource("H1") is Style h1) head.Style = h1;
        panel.Children.Add(head);
        panel.Children.Add(Caption(justPaired
            ? T("It just paired with this PC. Untick what it should not do; it can ask later, and you answer here.")
            : T("Ticked = allowed. A switch that is off in Devices → Permissions stops it for every device."), new Thickness(0, 0, 0, 12)));

        foreach (var c in AccessCategories.All)
        {
            var g = grants.FirstOrDefault(x => x.Category == c);
            var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
            var state = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            state.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            if (!SwitchOn(settings, c)) { state.Text = T("off for every device"); state.SetResourceReference(TextBlock.ForegroundProperty, "Warning"); }
            else if (g?.Mode == "timed" && g.ExpiresAt is { } at) state.Text = L("for 1 hour · until {0}", at.ToLocalTime().ToString("HH:mm"));
            else if (g?.Mode == "session") state.Text = T("until Cloud-SSD restarts");
            DockPanel.SetDock(state, Dock.Right);
            row.Children.Add(state);
            var box = new CheckBox { Content = T(AccessCategories.Describe(c)), IsChecked = g != null, VerticalAlignment = VerticalAlignment.Center };
            _boxes[c] = box;
            row.Children.Add(box);
            panel.Children.Add(row);
        }

        // "Files when it is off": its own yes/no, apart from the list above (never switched on by "everything"), and it
        // needs a yes on the device too. A guest cannot have it.
        if (device.GuestUntil is null)
        {
            var woHead = new TextBlock { Text = T("Files when it is off"), Margin = new Thickness(0, 16, 0, 4) };
            if (Application.Current?.TryFindResource("Label") is Style woLabel) woHead.Style = woLabel;
            panel.Children.Add(woHead);
            var woRow = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
            var woState = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            DockPanel.SetDock(woState, Dock.Right);
            woRow.Children.Add(woState);
            _whenOff = new CheckBox
            {
                Content = T("Keep copies in the cloud, so each can open the other's files while it is off"),
                IsChecked = device.WhenOffPc, VerticalAlignment = VerticalAlignment.Center
            };
            void ShowWhenOff()
            {
                bool pc = _whenOff.IsChecked == true;
                woState.Text = !pc ? "" : device.WhenOffDevice ? T("On: both said yes") : T("Waiting for a yes on the device");
                woState.SetResourceReference(TextBlock.ForegroundProperty, pc && device.WhenOffDevice ? "Success" : "Warning");
            }
            _whenOff.Checked += (_, _) => ShowWhenOff();
            _whenOff.Unchecked += (_, _) => ShowWhenOff();
            ShowWhenOff();
            woRow.Children.Add(_whenOff);
            panel.Children.Add(woRow);
            panel.Children.Add(Caption(T("Off until this PC and the device both say yes. The copies then start by themselves; Devices → When off shows how far they are."), new Thickness(0, 2, 0, 0)));
        }

        // What it did lately.
        var log = bridge.AccessLog.Read(device.Id, 40);
        var logHead = new TextBlock { Text = T("Recent access"), Margin = new Thickness(0, 16, 0, 6) };
        if (Application.Current?.TryFindResource("Label") is Style label) logHead.Style = label;
        panel.Children.Add(logHead);
        if (log.Count == 0) panel.Children.Add(Caption(T("Nothing opened yet."), new Thickness(0)));
        else
        {
            var list = new StackPanel();
            foreach (var e in log)
            {
                var line = new TextBlock
                {
                    Text = $"{e.At.ToLocalTime():dd MMM HH:mm}   {(e.Allowed ? "✓" : "✗ " + T("Refused") + " ·")}  {T(AccessCategories.Describe(e.Category))}{(string.IsNullOrEmpty(e.Target) ? "" : "  —  " + e.Target)}",
                    FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 1)
                };
                line.SetResourceReference(TextBlock.ForegroundProperty, e.Allowed ? "Muted" : "Danger");
                list.Children.Add(line);
            }
            panel.Children.Add(new ScrollViewer { Content = list, MaxHeight = 170, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        }

        var buttons = new DockPanel { Margin = new Thickness(0, 18, 0, 0) };
        if (openSwitches != null)
        {
            var sw = new Button { Content = T("Switches for every device"), Padding = new Thickness(10, 5, 10, 5) };
            if (Application.Current?.TryFindResource("Ghost") is Style ghost) sw.Style = ghost;
            sw.Click += (_, _) => { Close(); openSwitches(); };
            DockPanel.SetDock(sw, Dock.Left);
            buttons.Children.Add(sw);
        }
        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var save = new Button { Content = T("Save"), IsDefault = true, Padding = new Thickness(16, 6, 16, 6) };
        if (Application.Current?.TryFindResource("Primary") is Style p) save.Style = p;
        save.Click += (_, _) => { Save(); DialogResult = true; };
        var cancel = new Button { Content = T("Cancel"), IsCancel = true, Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(0) };
        right.Children.Add(save);
        right.Children.Add(cancel);
        buttons.Children.Add(right);
        panel.Children.Add(buttons);
        Content = panel;
    }

    private static bool SwitchOn(MobileBridgeSettings s, string c) => c switch
    {
        AccessCategories.Browse => s.AllowDriveBrowsing,
        AccessCategories.Stream => s.AllowStreaming,
        AccessCategories.Upload => s.AllowUploads,
        AccessCategories.Manage => s.AllowFileManagement,
        AccessCategories.Hud => s.AllowGamingHud,
        AccessCategories.Control => s.AllowPcControl,
        AccessCategories.Space => s.AllowSpace,
        AccessCategories.Game => s.AllowGameStream,
        _ => true
    };

    private static TextBlock Caption(string text, Thickness margin)
    {
        var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = margin };
        if (Application.Current?.TryFindResource("Caption") is Style c) t.Style = c;
        return t;
    }

    /// <summary>The ticks become the device's permissions (one taken away stops at once).</summary>
    public void Save()
    {
        _host.MobileBridge.Access.Set(_device, _boxes.Where(kv => kv.Value.IsChecked == true).Select(kv => kv.Key));
        if (_whenOff != null) _host.MobileBridge.SetWhenOffPc(_device.Id, _whenOff.IsChecked == true);
    }

    /// <summary>This PC's yes/no for "Files when it is off" (null for a guest, who cannot have it).</summary>
    private readonly CheckBox? _whenOff;

    /// <summary>For tests and the render tool: tick or untick "Files when it is off".</summary>
    public void SetWhenOff(bool on) { if (_whenOff != null) _whenOff.IsChecked = on; }

    /// <summary>For tests and the render tool: tick or untick one category.</summary>
    internal void Tick(string category, bool on) => _boxes[category].IsChecked = on;
}
