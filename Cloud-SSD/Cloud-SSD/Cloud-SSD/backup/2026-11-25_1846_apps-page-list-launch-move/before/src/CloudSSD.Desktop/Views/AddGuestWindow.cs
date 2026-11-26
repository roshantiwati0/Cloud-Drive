using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CloudSSD.App;

namespace CloudSSD.Desktop.Views;

/// <summary>
/// "Add a guest": someone else's phone for a while. Choose how long and what it may do, then a six-digit code shows; the
/// guest types it in the Cloud-SSD app (+ Add device). The code works once, for 15 minutes; the guest is removed when its
/// time is up.
/// </summary>
public sealed class AddGuestWindow : Window
{
    private readonly AppHost _host;
    private readonly Dictionary<string, CheckBox> _boxes = new();
    private readonly RadioButton _hour, _day, _week;
    private readonly StackPanel _codePanel = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 14, 0, 0) };
    private readonly TextBlock _code = new() { FontSize = 34, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _codeNote = new() { TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center };
    private readonly Button _make;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private GuestInvite? _invite;

    private static string T(string s) => LocalizationManager.T(s);
    private static string L(string f, params object[] a) => string.Format(T(f), a);

    public AddGuestWindow(AppHost host)
    {
        _host = host;
        Title = T("Add a guest");
        Width = 520;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        if (Application.Current?.TryFindResource("DialogWindow") is Style st) Style = st;

        var panel = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
        var head = new TextBlock { Text = T("Let a guest in for a while"), TextWrapping = TextWrapping.Wrap };
        if (Application.Current?.TryFindResource("H1") is Style h1) head.Style = h1;
        panel.Children.Add(head);
        panel.Children.Add(Caption(T("The guest pairs in the Cloud-SSD app (+ Add device) with the code shown here. It may do only what you tick, and is removed when its time is up."),
            new Thickness(0, 0, 0, 12)));
        if (!host.MobileBridge.IsRunning)
            panel.Children.Add(Caption(T("The phone bridge is off, so no device can connect. Turn it on in Devices → Add or connect."), new Thickness(0, 0, 0, 10), "Warning"));

        panel.Children.Add(Label(T("For how long")));
        var times = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        _hour = new RadioButton { Content = T("1 hour"), GroupName = "stay", Margin = new Thickness(0, 0, 16, 0) };
        _day = new RadioButton { Content = T("1 day"), GroupName = "stay", IsChecked = true, Margin = new Thickness(0, 0, 16, 0) };
        _week = new RadioButton { Content = T("1 week"), GroupName = "stay" };
        times.Children.Add(_hour);
        times.Children.Add(_day);
        times.Children.Add(_week);
        panel.Children.Add(times);

        panel.Children.Add(Label(T("What it may do")));
        // Looking and playing by default; changing files or the PC only when ticked.
        foreach (var c in AccessCategories.All)
        {
            var box = new CheckBox
            {
                Content = T(AccessCategories.Describe(c)),
                IsChecked = c is AccessCategories.Browse or AccessCategories.Stream,
                Margin = new Thickness(0, 2, 0, 2)
            };
            box.Checked += (_, _) => CanMake();
            box.Unchecked += (_, _) => CanMake();
            _boxes[c] = box;
            panel.Children.Add(box);
        }

        _code.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
        _codeNote.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        _codePanel.Children.Add(_code);
        _codePanel.Children.Add(_codeNote);
        panel.Children.Add(_codePanel);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        _make = new Button { Content = T("Make the code"), IsDefault = true, Padding = new Thickness(16, 6, 16, 6) };
        if (Application.Current?.TryFindResource("Primary") is Style p) _make.Style = p;
        _make.Click += (_, _) => Make();
        var close = new Button { Content = T("Close"), IsCancel = true, Padding = new Thickness(16, 6, 16, 6) };
        close.Click += (_, _) => Close();
        buttons.Children.Add(_make);
        buttons.Children.Add(close);
        panel.Children.Add(buttons);
        Content = panel;

        _timer.Tick += (_, _) => ShowCode();
        host.MobileBridge.GuestInvitesChanged += OnInvitesChanged;
        Closed += (_, _) =>
        {
            _timer.Stop();
            host.MobileBridge.GuestInvitesChanged -= OnInvitesChanged;
            // A code nobody used yet is not left lying around.
            if (_invite is { } i && host.MobileBridge.GuestInvites.Any(x => x.Code == i.Code)) host.MobileBridge.CancelGuestInvite(i.Code);
        };
    }

    private TimeSpan Stay => _hour.IsChecked == true ? TimeSpan.FromHours(1) : _week.IsChecked == true ? TimeSpan.FromDays(7) : TimeSpan.FromDays(1);

    private void CanMake() => _make.IsEnabled = _boxes.Values.Any(b => b.IsChecked == true);

    private void Make()
    {
        if (_invite is { } old) _host.MobileBridge.CancelGuestInvite(old.Code);
        _invite = _host.MobileBridge.CreateGuestInvite(Stay, _boxes.Where(kv => kv.Value.IsChecked == true).Select(kv => kv.Key));
        _make.Content = T("New code");
        _codePanel.Visibility = Visibility.Visible;
        _timer.Start();
        ShowCode();
    }

    private void OnInvitesChanged() => Dispatcher.BeginInvoke(ShowCode);

    private void ShowCode()
    {
        if (_invite is not { } i) return;
        bool waiting = _host.MobileBridge.GuestInvites.Any(x => x.Code == i.Code);
        var left = i.CodeExpiresAt - DateTimeOffset.Now;
        if (waiting && left > TimeSpan.Zero)
        {
            _code.Text = i.Code[..3] + " " + i.Code[3..];
            _codeNote.Text = L("Type it in the guest's Cloud-SSD app within {0}:{1:00}. It works once.", (int)left.TotalMinutes, left.Seconds);
            return;
        }
        _timer.Stop();
        if (left > TimeSpan.Zero)
        {
            // Used: the guest is in the device list now.
            _code.Text = "✓";
            _codeNote.Text = T("The guest is in. It shows in the device list until its time is up.");
        }
        else
        {
            _code.Text = "—";
            _codeNote.Text = T("The code ran out. Make a new one.");
        }
    }

    private static TextBlock Label(string text)
    {
        var t = new TextBlock { Text = text, Margin = new Thickness(0, 4, 0, 6) };
        if (Application.Current?.TryFindResource("Label") is Style l) t.Style = l;
        return t;
    }

    private static TextBlock Caption(string text, Thickness margin, string? brush = null)
    {
        var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = margin };
        if (Application.Current?.TryFindResource("Caption") is Style c) t.Style = c;
        if (brush != null) t.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return t;
    }
}
