using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CloudSSD.App;
using CloudSSD.App.Mirror;
using CloudSSD.Core;
using CloudSSD.Desktop.ViewModels;

namespace CloudSSD.Desktop.Views;

/// <summary>
/// Devices → "When off": the copy of this PC's own files in the cloud ("Files when it is off"). What it is doing and
/// how far it is, Sync now and Pause, the few choices (Windows and programs too, battery, speed, start with Windows),
/// and the question when very many files went missing at once. The yes for a device itself is in its permissions.
/// </summary>
public sealed class WhenOffView : UserControl
{
    private readonly TextBlock _state, _account, _own, _all, _failed, _notRead, _goneText;
    private readonly ProgressBar _ownBar, _allBar;
    private readonly Button _sync, _pause;
    private readonly Border _goneCard;
    private readonly CheckBox _system, _battery, _startup;
    private readonly TextBox _limit;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(3) };
    private bool _filling;

    private static string T(string s) => LocalizationManager.T(s);
    private static string L(string f, params object[] a) => string.Format(T(f), a);

    private AppHost? Host => (DataContext as PageViewModel)?.Host ?? (Application.Current?.MainWindow?.DataContext as MainViewModel)?.Hub.Host;

    public WhenOffView()
    {
        var root = new StackPanel();

        // What it is, what it is doing, how far it is.
        var main = Card();
        var box = new StackPanel();
        box.Children.Add(Title(T("Files when it is off")));
        box.Children.Add(Muted(T("This PC keeps a copy of its files in your cloud drive, so a paired device can open them while the PC is switched off. It runs only while a device and this PC both said yes (Permissions).")));
        _state = new TextBlock { FontSize = 14, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 0) };
        _state.SetResourceReference(TextBlock.ForegroundProperty, "Text");
        box.Children.Add(_state);
        _account = Muted("", 4);
        box.Children.Add(_account);
        _own = Muted("", 14);
        box.Children.Add(_own);
        _ownBar = Bar();
        box.Children.Add(_ownBar);
        _all = Muted("", 10);
        box.Children.Add(_all);
        _allBar = Bar();
        box.Children.Add(_allBar);
        _failed = Muted("", 10);
        box.Children.Add(_failed);
        _notRead = Muted("", 4);
        box.Children.Add(_notRead);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
        _sync = Btn(T("Sync now"));
        if (Application.Current?.TryFindResource("Primary") is Style primary) _sync.Style = primary;
        _sync.Click += (_, _) => { if (Host is { } h) _ = h.Mirror.SyncNow(); Refresh(); };
        _pause = Btn(T("Pause"));
        _pause.Margin = new Thickness(8, 0, 0, 0);
        _pause.Click += (_, _) => { if (Host is { } h) h.Mirror.SetPaused(!h.Settings.Mirror.Paused); Refresh(); };
        buttons.Children.Add(_sync);
        buttons.Children.Add(_pause);
        box.Children.Add(buttons);
        box.Children.Add(Muted(T("The first copy takes long: Google lets about 2 files a second be made. Your own files take 1–2 days, the whole disk weeks.")));
        main.Child = box;
        root.Children.Add(main);

        // Very many files gone at once: nothing is done about their copies until the person says.
        _goneCard = Card();
        var gone = new StackPanel();
        _goneText = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        _goneText.SetResourceReference(TextBlock.ForegroundProperty, "Text");
        gone.Children.Add(_goneText);
        var goneButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        var yes = Btn(T("Yes, move their copies to the trash"));
        yes.Click += (_, _) => { Host?.Mirror.AnswerGone(true); Refresh(); };
        var no = Btn(T("No, keep the copies"));
        no.Margin = new Thickness(8, 0, 0, 0);
        no.Click += (_, _) => { Host?.Mirror.AnswerGone(false); Refresh(); };
        goneButtons.Children.Add(yes);
        goneButtons.Children.Add(no);
        gone.Children.Add(goneButtons);
        _goneCard.Child = gone;
        root.Children.Add(_goneCard);

        // The choices.
        var options = Card();
        var o = new StackPanel();
        _system = Check(T("Also Windows and the programs (they go last; a copy of files, not a system image)"));
        _system.Checked += (_, _) => Save(s => s.IncludeSystem = true);
        _system.Unchecked += (_, _) => Save(s => s.IncludeSystem = false);
        _battery = Check(T("Pause on battery"));
        _battery.Checked += (_, _) => Save(s => s.PauseOnBattery = true);
        _battery.Unchecked += (_, _) => Save(s => s.PauseOnBattery = false);
        _startup = Check(T("Start Cloud-SSD with Windows (needed, so the copies keep up)"));
        _startup.Checked += (_, _) => { if (!_filling && !StartWithWindows.Set(true)) Refresh(); };
        _startup.Unchecked += (_, _) => { if (!_filling) StartWithWindows.Set(false); };
        o.Children.Add(_system);
        o.Children.Add(_battery);
        o.Children.Add(_startup);
        var limitRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        var limitLabel = Muted(T("Upload limit (Mbit/s, 0 = none)"), 0);
        limitLabel.VerticalAlignment = VerticalAlignment.Center;
        _limit = new TextBox { Width = 70, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        _limit.LostFocus += (_, _) =>
        {
            if (_filling) return;
            double.TryParse(_limit.Text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var mbps);
            Save(s => s.UploadLimitMbps = Math.Clamp(mbps, 0, 10_000));
            Refresh();
        };
        limitRow.Children.Add(limitLabel);
        limitRow.Children.Add(_limit);
        o.Children.Add(limitRow);
        options.Child = o;
        root.Children.Add(options);

        Content = root;
        _timer.Tick += (_, _) => Refresh();
        Loaded += (_, _) => { if (Host is { } h) { h.Mirror.Changed -= OnChanged; h.Mirror.Changed += OnChanged; } _timer.Start(); Refresh(); };
        Unloaded += (_, _) => { _timer.Stop(); if (Host is { } h) h.Mirror.Changed -= OnChanged; };
        IsVisibleChanged += (_, _) => { if (IsVisible) Refresh(); };
    }

    private void OnChanged() => Dispatcher.BeginInvoke(Refresh);

    private void Save(Action<MirrorSettings> change)
    {
        if (_filling || Host is not { } h) return;
        change(h.Settings.Mirror);
        h.SaveSettings();
    }

    private void Refresh()
    {
        if (Host is not { } h || !IsVisible) return;
        var st = h.Mirror.Status;
        var s = h.Settings.Mirror;
        _state.Text = StateText(st);
        _account.Text = st.Account is { Length: > 0 } a
            ? L("Copies go to: {0}", h.AccountLabel(a)) + (st.LastSync is { } last ? " · " + L("Last copied: {0}", last.ToLocalTime().ToString("g")) : "")
            : "";
        _account.Visibility = _account.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        var own = st.Tiers.Where(t => t.Tier <= MirrorScope.TierProfile).ToList();
        Line(_own, _ownBar, "Your files: {0} of {1} ({2}%)", own.Sum(t => t.UploadedBytes), own.Sum(t => t.Bytes));
        Line(_all, _allBar, "Everything: {0} of {1} ({2}%)", st.Tiers.Sum(t => t.UploadedBytes), st.Tiers.Sum(t => t.Bytes));

        long failed = st.Failed.Values.Sum();
        long inUse = st.Failed.GetValueOrDefault("in-use"), denied = st.Failed.GetValueOrDefault("denied");
        _failed.Text = failed > 0
            ? L("{0} file(s) could not be copied (open in a program: {1}, no access: {2}, other: {3}). They are tried again later.", failed, inUse, denied, failed - inUse - denied)
            : "";
        _failed.Visibility = failed > 0 ? Visibility.Visible : Visibility.Collapsed;
        _notRead.Text = st.FoldersNotRead > 0 ? L("{0} folder(s) Windows does not let this app read.", st.FoldersNotRead) : "";
        _notRead.Visibility = st.FoldersNotRead > 0 ? Visibility.Visible : Visibility.Collapsed;

        _goneCard.Visibility = st.GoneHeld > 0 ? Visibility.Visible : Visibility.Collapsed;
        _goneText.Text = L("{0} files are gone from this PC at once. Are they really deleted?", st.GoneHeld);

        _sync.IsEnabled = st.State is not ("off" or "scanning" or "uploading") && !s.Paused;
        _pause.Content = T(s.Paused ? "Continue" : "Pause");
        _pause.IsEnabled = st.State != "off";

        _filling = true;
        _system.IsChecked = s.IncludeSystem;
        _battery.IsChecked = s.PauseOnBattery;
        _startup.IsChecked = StartWithWindows.IsOn();
        _startup.IsEnabled = StartWithWindows.AppPath != null || StartWithWindows.IsOn();
        if (!_limit.IsKeyboardFocusWithin) _limit.Text = s.UploadLimitMbps.ToString("0.##", CultureInfo.InvariantCulture);
        _filling = false;
    }

    private static void Line(TextBlock text, ProgressBar bar, string format, long done, long total)
    {
        bool any = total > 0;
        text.Visibility = bar.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        if (!any) return;
        double pct = Math.Min(100, 100.0 * done / total);
        text.Text = L(format, ByteSize.Format(done), ByteSize.Format(total), Math.Floor(pct).ToString("0"));
        bar.Value = pct;
    }

    private static string StateText(MirrorStatus st) => st.State switch
    {
        "off" => T("Off: no device and this PC have both said yes yet."),
        "scanning" => T("Looking at the disk…"),
        "uploading" => T("Copying to the cloud…"),
        "paused" => st.Reason switch
        {
            "person" => T("Paused by you."),
            "game" => T("Paused: a game is running."),
            "battery" => T("Paused: the PC is on battery."),
            "no-rclone" => T("Waiting: rclone was not found."),
            "no-account" => T("Waiting: no Google account this app may write to."),
            "upload-limit" => T("Waiting: today's upload limit of the account is reached. It goes on later by itself."),
            "rate-limit" => T("Waiting: Google is holding back the shared key this app signs in with. It tries again in a few minutes. With your own Google key this does not happen (Cloud Drives → How do I get a key?)."),
            _ => L("Waiting after an error: {0}", st.Error ?? "")
        },
        _ => T("Up to date. It looks again every hour.")
    };

    // ---- small parts ----------------------------------------------------------------------------------------------

    private static Border Card()
    {
        var b = new Border();
        if (Application.Current?.TryFindResource("Card") is Style s) b.Style = s;
        b.Margin = new Thickness(0, 0, 0, 14);
        return b;
    }

    private static TextBlock Title(string text)
    {
        var t = new TextBlock { Text = text, FontSize = 16, FontWeight = FontWeights.SemiBold };
        t.SetResourceReference(TextBlock.ForegroundProperty, "Text");
        return t;
    }

    private static TextBlock Muted(string text, double top = 8)
    {
        var t = new TextBlock { Text = text, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, top, 0, 0) };
        t.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        return t;
    }

    private static ProgressBar Bar() => new() { Height = 6, Minimum = 0, Maximum = 100, Margin = new Thickness(0, 6, 0, 0) };

    private static Button Btn(string text) => new() { Content = text, Padding = new Thickness(14, 5, 14, 5), VerticalAlignment = VerticalAlignment.Center };

    private static CheckBox Check(string text)
    {
        var c = new CheckBox { Content = text, FontSize = 12, Margin = new Thickness(0, 6, 0, 0) };
        c.SetResourceReference(Control.ForegroundProperty, "Text");
        return c;
    }
}
