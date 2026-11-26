using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using CloudSSD.App;
using CloudSSD.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CloudSSD.Desktop.ViewModels;

/// <summary>
/// Every phone and laptop paired with this PC as a card: online now or when last seen, how it connects, what it is
/// doing, and rename / block / remove. Updates live from the bridge.
/// </summary>
public sealed partial class DevicesViewModel : PageViewModel
{
    private readonly Action<string>? _navigate;
    private DateTimeOffset _lastTimesRefresh;

    public DevicesViewModel(AppHost host, Action<string>? navigate = null) : base(host, "Devices", "")
    {
        _navigate = navigate;
        _notifyOnConnect = host.Settings.MobileBridge.NotifyDeviceConnections;
        host.MobileBridge.DevicesChanged += () => Ui(Rebuild);
        host.MobileBridge.StatusChanged += () => Ui(Rebuild);
        LocalizationManager.LanguageChanged += () => Ui(Rebuild);
        DeviceNotifications.Attach(host);
        Rebuild();
    }

    public ObservableCollection<DeviceCardViewModel> Cards { get; } = new();

    [ObservableProperty] private string _summaryText = "";
    [ObservableProperty] private bool _notifyOnConnect;
    [ObservableProperty] private bool _bridgeOff;

    partial void OnNotifyOnConnectChanged(bool value)
    {
        Host.Settings.MobileBridge.NotifyDeviceConnections = value;
        Host.SaveSettings();
    }

    public override void OnShown()
    {
        base.OnShown();
        Rebuild();
    }

    public override void OnMetrics(EngineMetrics m)
    {
        base.OnMetrics(m);
        // "5 min ago" moves on by itself; a cheap redraw of the times every ten seconds.
        var now = DateTimeOffset.Now;
        if (now - _lastTimesRefresh < TimeSpan.FromSeconds(10)) return;
        _lastTimesRefresh = now;
        Rebuild();
    }

    /// <summary>Updates the cards in place (no flicker, a rename being typed is kept): online first, blocked last.</summary>
    internal void Rebuild()
    {
        var devices = Host.MobileBridge.GetDevices()
            .OrderBy(d => d.Blocked)
            .ThenByDescending(d => d.Online)
            .ThenByDescending(d => d.LastSeen)
            .ToList();
        var now = DateTimeOffset.Now;
        for (int i = 0; i < devices.Count; i++)
        {
            var d = devices[i];
            int at = IndexOf(d.Id);
            if (at < 0) Cards.Insert(i, new DeviceCardViewModel(this, d, now));
            else
            {
                if (at != i) Cards.Move(at, i);
                Cards[i].Update(d, now);
            }
        }
        while (Cards.Count > devices.Count) Cards.RemoveAt(Cards.Count - 1);
        SummaryText = L("{0} online · {1} paired", devices.Count(d => d.Online), devices.Count);
        BridgeOff = !Host.MobileBridge.IsRunning;
    }

    private int IndexOf(string id)
    {
        for (int i = 0; i < Cards.Count; i++) if (Cards[i].Id == id) return i;
        return -1;
    }

    [RelayCommand]
    private void AddDevice()
    {
        var w = new Views.AddDeviceWindow(Host) { Owner = Application.Current?.MainWindow };
        w.ShowDialog();
        Rebuild();
    }

    /// <summary>A one-time code for someone else's phone, with only what is chosen, for a while.</summary>
    [RelayCommand]
    private void AddGuest()
    {
        var w = new Views.AddGuestWindow(Host) { Owner = Application.Current?.MainWindow };
        w.ShowDialog();
        Rebuild();
    }

    [RelayCommand]
    private void OpenMobileHub() => _navigate?.Invoke("Add or connect");

    internal void ShowMessage(string text, string tone) => Say(text, tone);
    internal void OpenPermissions() => _navigate?.Invoke("Permissions");

    /// <summary>A line in the page's status bar, for what a card did.</summary>
    internal void Tell(string text, string tone) => Say(text, tone);

    internal static string L(string format, params object[] args) => string.Format(LocalizationManager.T(format), args);
}

/// <summary>One device card.</summary>
public sealed partial class DeviceCardViewModel : ObservableObject
{
    private readonly DevicesViewModel _page;

    public DeviceCardViewModel(DevicesViewModel page, DeviceSnapshot d, DateTimeOffset now)
    {
        _page = page;
        Id = d.Id;
        Update(d, now);
    }

    public string Id { get; }

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _kindIcon = "";
    [ObservableProperty] private string _kindText = "";
    [ObservableProperty] private bool _isOnline;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _statusTone = "muted";
    [ObservableProperty] private string _linkText = "";
    [ObservableProperty] private string _activityText = "";
    [ObservableProperty] private double? _activityPercent;
    [ObservableProperty] private string _detailText = "";
    [ObservableProperty] private bool _isBlocked;
    [ObservableProperty] private bool _isLegacy;
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private string _editName = "";

    public bool HasActivity => ActivityText.Length > 0;
    public bool HasPercent => ActivityPercent is not null;
    public string BlockText => LocalizationManager.T(IsBlocked ? "Unblock" : "Block");

    partial void OnActivityTextChanged(string value) => OnPropertyChanged(nameof(HasActivity));
    partial void OnActivityPercentChanged(double? value) => OnPropertyChanged(nameof(HasPercent));
    partial void OnIsBlockedChanged(bool value) => OnPropertyChanged(nameof(BlockText));

    /// <summary>
    /// A phone or tablet whose app is waiting for this PC right now: it can be browsed (as far as it allows), rung
    /// and sent text. Hidden otherwise, as asking would only wait for an answer that cannot come.
    /// </summary>
    [ObservableProperty] private bool _canBrowse;

    /// <summary>"85% · charging · 12.3 GB free", as the phone last reported it.</summary>
    [ObservableProperty] private string _vitalsText = "";

    /// <summary>How many things this device deleted on this PC (kept 60 days); the card offers to look and restore.</summary>
    [ObservableProperty] private int _deletedCount;

    public bool HasDeleted => DeletedCount > 0;
    public string DeletedText => DevicesViewModel.L("Deleted ({0})", DeletedCount);

    partial void OnDeletedCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasDeleted));
        OnPropertyChanged(nameof(DeletedText));
    }

    [RelayCommand]
    private void ShowDeleted()
    {
        new Views.DeviceDeletesWindow(_page.Host, Id, Name) { Owner = Application.Current.MainWindow }.ShowDialog();
        _page.Rebuild();
    }

    internal static string Vitals(DeviceVitals? v)
    {
        if (v is null) return "";
        var parts = new List<string>();
        if (v.Battery is { } b) parts.Add(DevicesViewModel.L("Battery {0}%", b) + (v.Charging ? " · " + LocalizationManager.T("charging") : ""));
        if (v.FreeBytes is { } f) parts.Add(DevicesViewModel.L("{0} free", ByteSize.Format(f)));
        return string.Join(" · ", parts);
    }

    private static string ResultText(PeerResult r, string name) => r.Status switch
    {
        "timeout" => DevicesViewModel.L("{0} is not answering. Open the Cloud-SSD app on it.", name),
        "denied" => DevicesViewModel.L("{0} does not allow that.", name),
        _ => r.Error ?? LocalizationManager.T("Something went wrong.")
    };

    [RelayCommand]
    private async Task RingAsync()
    {
        _page.Tell(DevicesViewModel.L("Ringing {0}…", Name), "muted");
        var r = await _page.Host.MobileBridge.RingDeviceAsync(Id);
        _page.Tell(r.Ok ? DevicesViewModel.L("{0} is ringing.", Name) : ResultText(r, Name), r.Ok ? "ok" : "warn");
    }

    [RelayCommand]
    private async Task SendClipboardAsync()
    {
        string text = "";
        try { if (Clipboard.ContainsText()) text = Clipboard.GetText(); } catch (System.Runtime.InteropServices.COMException) { }
        if (string.IsNullOrWhiteSpace(text)) { _page.Tell(LocalizationManager.T("The clipboard has no text to send."), "warn"); return; }
        var r = await _page.Host.MobileBridge.SendClipboardAsync(Id, text);
        _page.Tell(r.Ok ? DevicesViewModel.L("Clipboard sent to {0}.", Name) : ResultText(r, Name), r.Ok ? "ok" : "warn");
    }

    [RelayCommand]
    private void Browse()
    {
        var device = _page.Host.Settings.MobileBridge.PairedDevices.FirstOrDefault(d => d.Id == Id);
        if (device is null) return;
        // In the app: the Phone files page, on this device (a window of its own only without the main window).
        if (Application.Current.MainWindow?.DataContext is MainViewModel main) main.OpenPhoneFiles(Id);
        else new Views.BrowsePhoneWindow(_page.Host, device) { Owner = Application.Current.MainWindow }.Show();
    }

    internal void Update(DeviceSnapshot d, DateTimeOffset now)
    {
        if (!IsEditing) Name = d.Name;
        KindIcon = DeviceText.Icon(d.Kind);
        KindText = LocalizationManager.T(DeviceText.KindName(d.Kind));
        IsOnline = d.Online;
        IsBlocked = d.Blocked;
        IsLegacy = d.Legacy;
        CanBrowse = !d.Blocked && d.Listening && d.Kind is DeviceKind.Phone or DeviceKind.Tablet;
        VitalsText = Vitals(d.Vitals);
        DeletedCount = _page.Host.MobileBridge.DeleteLog.CountFor(d.Id);
        StatusText = d.Blocked ? LocalizationManager.T("Blocked")
            : d.Online ? LocalizationManager.T("● Online now")
            : DevicesViewModel.L("○ Last seen {0}", Fmt.Ago(d.LastSeen, now));
        StatusTone = d.Blocked ? "error" : d.Online ? "ok" : "muted";
        LinkText = DeviceText.Link(d.Link, d.Ip);
        ActivityText = d.Online ? DeviceText.Activity(d.Activity) : "";
        ActivityPercent = d.Online && d.Activity?.Percent is { } p ? p : null;
        var parts = new List<string>();
        if (d.GuestUntil is { } gu)
        {
            // "Guest · ends 18:00" (or "ends 14 Nov 18:00" when that is not today); removed then.
            KindText = LocalizationManager.T("Guest");
            var end = gu.ToLocalTime();
            parts.Add(DevicesViewModel.L("Guest · ends {0}", end.ToString(end.Date == now.ToLocalTime().Date ? "HH:mm" : "d MMM HH:mm", CultureInfo.CurrentCulture)));
        }
        if (!string.IsNullOrEmpty(d.Platform)) parts.Add(d.Platform!);
        parts.Add(DevicesViewModel.L("Paired {0}", d.PairedAt.ToLocalTime().ToString("d MMM yyyy", CultureInfo.CurrentCulture)));
        if (d.Legacy) parts.Add(LocalizationManager.T("old key"));
        DetailText = string.Join(" · ", parts);
    }

    [RelayCommand]
    private void BeginRename()
    {
        EditName = Name;
        IsEditing = true;
    }

    [RelayCommand]
    private void SaveRename()
    {
        if (!_page.Host.MobileBridge.RenameDevice(Id, EditName))
        {
            _page.ShowMessage(LocalizationManager.T("That name cannot be used (1–60 characters)."), "error");
            return;
        }
        IsEditing = false;
        Name = EditName.Trim();
        _page.Rebuild();
    }

    [RelayCommand]
    private void CancelRename() => IsEditing = false;

    [RelayCommand]
    private void ToggleBlock()
    {
        if (!IsBlocked && MessageBox.Show(Application.Current.MainWindow!, DevicesViewModel.L("Block {0}? It stays in the list but cannot connect until you unblock it.", Name),
                "Cloud-SSD", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        _page.Host.MobileBridge.SetBlocked(Id, !IsBlocked);
        _page.Rebuild();
    }

    [RelayCommand]
    private void Remove()
    {
        if (MessageBox.Show(Application.Current.MainWindow!, DevicesViewModel.L("Remove {0}? It has to pair again to connect.", Name),
                "Cloud-SSD", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        _page.Host.MobileBridge.RemoveDevice(Id);
        _page.Rebuild();
    }

    /// <summary>What this device may do (on top of the switches for every device), and what it opened lately.</summary>
    [RelayCommand]
    private void EditPermissions()
    {
        var device = _page.Host.Settings.MobileBridge.PairedDevices.FirstOrDefault(d => d.Id == Id);
        if (device is null) return;
        new Views.DevicePermissionsWindow(_page.Host, device, justPaired: false, _page.OpenPermissions) { Owner = Application.Current.MainWindow }.ShowDialog();
        _page.Rebuild();
    }
}

/// <summary>Texts and icons for devices (pure, so they can be tested).</summary>
public static class DeviceText
{
    public static string KindName(DeviceKind k) => k switch
    {
        DeviceKind.Tablet => "Tablet",
        DeviceKind.Laptop => "Laptop",
        DeviceKind.Desktop => "PC",
        DeviceKind.Browser => "Browser",
        _ => "Phone"
    };

    /// <summary>Segoe Fluent icons: phone, tablet, laptop, desktop, globe.</summary>
    public static string Icon(DeviceKind k) => k switch
    {
        DeviceKind.Tablet => "",
        DeviceKind.Laptop => "",
        DeviceKind.Desktop => "",
        DeviceKind.Browser => "",
        _ => ""
    };

    public static string Link(string link, string ip)
    {
        var name = link switch
        {
            "wifi" => "Wi-Fi",
            "pc-hotspot" or "hotspot" => "This PC's hotspot",
            "phone-hotspot" => "Phone's hotspot",
            "usb" => "USB cable",
            "usb-adb" => "USB cable (adb)",
            "internet" => "Internet (Tailscale)",
            "local" => "This PC",
            _ => "LAN cable"
        };
        var text = LocalizationManager.T(name);
        return string.IsNullOrEmpty(ip) || link == "local" ? text : $"{text} · {ip}";
    }

    /// <summary>What the device is doing, or "" when nothing is known.</summary>
    public static string Activity(DeviceActivity? a)
    {
        if (a is null) return "";
        string T(string s) => LocalizationManager.T(s);
        string withFile(string what) => string.IsNullOrEmpty(a.File) ? what : $"{what} · {a.File}";
        return a.Kind switch
        {
            DeviceActivityKind.Upload => withFile(a.Percent is { } p ? string.Format(T("Upload {0}%"), p) : T("Uploading")),
            DeviceActivityKind.Download => withFile(a.Percent is { } q ? string.Format(T("Download {0}%"), q) : T("Downloading")),
            DeviceActivityKind.Backup => withFile(a.Percent is { } r ? $"{T("Backing up photos")} {r}%" : T("Backing up photos")),
            DeviceActivityKind.Stream => withFile(T("Watching a video")),
            DeviceActivityKind.Photos => T("Viewing photos"),
            DeviceActivityKind.Browsing => T("Browsing files"),
            DeviceActivityKind.Game => T("Playing on the phone"),
            DeviceActivityKind.Space => T("Looking at Space & RAM"),
            DeviceActivityKind.Hud => T("Gaming HUD"),
            _ => ""
        };
    }
}
