using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CloudSSD.App;

namespace CloudSSD.Desktop.Views;

/// <summary>
/// "Redmi 12 wants to: change files". Deny is the default (Enter and Esc deny); the Allow buttons wake up after a
/// second, so a key pressed while typing elsewhere cannot allow anything. Closes by itself when the question runs out.
/// </summary>
public sealed class AccessRequestWindow : Window
{
    private readonly AppHost _host;
    private readonly AccessRequest _request;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly TextBlock _countdown;
    private readonly Button[] _allow;
    private readonly DateTime _shownAt = DateTime.UtcNow;
    private bool _answered;

    private static string T(string s) => LocalizationManager.T(s);
    private static string L(string f, params object[] a) => string.Format(T(f), a);

    public AccessRequestWindow(AppHost host, AccessRequest request)
    {
        _host = host;
        _request = request;
        Title = T("Access request");
        Width = 480;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;
        ShowActivated = true;
        if (Application.Current?.TryFindResource("DialogWindow") is Style st) Style = st;

        var panel = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
        var head = new TextBlock { Text = L("{0} wants to:", request.DeviceName), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
        if (Application.Current?.TryFindResource("H2") is Style h2) head.Style = h2;
        panel.Children.Add(head);
        foreach (var c in request.Categories)
            panel.Children.Add(new TextBlock { Text = "•  " + T(AccessCategories.Describe(c)), FontSize = 14, Margin = new Thickness(4, 2, 0, 2), TextWrapping = TextWrapping.Wrap });
        if (!string.IsNullOrWhiteSpace(request.Reason))
            panel.Children.Add(Caption(L("It says: “{0}”", request.Reason), new Thickness(0, 10, 0, 0)));
        panel.Children.Add(Caption(T("Allow only a device you know. You can take it back any time in Devices → Permissions."), new Thickness(0, 10, 0, 0)));
        _countdown = Caption("", new Thickness(0, 6, 0, 0));
        panel.Children.Add(_countdown);

        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var deny = new Button { Content = T("Deny"), IsDefault = true, IsCancel = true, Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 8, 0) };
        deny.Click += (_, _) => Answer(null);
        var session = Allow(T("Until Cloud-SSD restarts"), AccessMode.Session);
        var hour = Allow(T("For 1 hour"), AccessMode.Hour);
        var always = Allow(T("Always"), AccessMode.Always);
        if (Application.Current?.TryFindResource("Primary") is Style primary) always.Style = primary;
        _allow = new[] { session, hour, always };
        buttons.Children.Add(deny);
        foreach (var b in _allow) buttons.Children.Add(b);
        panel.Children.Add(buttons);
        Content = panel;

        _timer.Tick += (_, _) => Tick();
        _timer.Start();
        host.MobileBridge.Access.RequestEnded += OnEnded;
        Closed += (_, _) =>
        {
            _timer.Stop();
            host.MobileBridge.Access.RequestEnded -= OnEnded;
            if (!_answered && _request.State == "pending") host.MobileBridge.Access.Decide(_request.Id, null);   // closed = no
        };
        Loaded += (_, _) => { deny.Focus(); Tick(); };
    }

    private static TextBlock Caption(string text, Thickness margin)
    {
        var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = margin };
        if (Application.Current?.TryFindResource("Caption") is Style c) t.Style = c;
        return t;
    }

    private Button Allow(string text, AccessMode mode)
    {
        var b = new Button { Content = text, IsEnabled = false, Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 8, 0) };
        b.Click += (_, _) => Answer(mode);
        return b;
    }

    private void Tick()
    {
        if (DateTime.UtcNow - _shownAt >= TimeSpan.FromSeconds(1))
            foreach (var b in _allow) b.IsEnabled = true;
        var left = _request.ExpiresAt - DateTimeOffset.Now;
        if (left <= TimeSpan.Zero || _request.State != "pending") { _answered = true; Close(); return; }
        _countdown.Text = L("Answer within {0}", $"{(int)left.TotalMinutes}:{left.Seconds:00}");
    }

    private void OnEnded(AccessRequest r)
    {
        if (r.Id == _request.Id) Dispatcher.BeginInvoke(() => { _answered = true; if (IsLoaded) Close(); });
    }

    private void Answer(AccessMode? mode)
    {
        _answered = true;
        _host.MobileBridge.Access.Decide(_request.Id, mode);
        Close();
    }
}
