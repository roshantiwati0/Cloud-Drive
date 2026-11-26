using System.Collections.ObjectModel;
using CloudSSD.App;
using CloudSSD.Core;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CloudSSD.Desktop.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    public MainViewModel(AppHost host)
    {
        Host = host;
        // Seven sections, each built like the Files page (chips at the top, one part below), nothing in two places:
        //   Home · Files (Phone storage, Cloud SSD, Transfers) · Devices (Phones, Add or connect, Permissions,
        //   Other PCs) · Cloud Drives · PC care (Storage saver, RAM, Space and clean-up) · Games ·
        //   Settings (General, Look and language, Speed, Performance, Folders, Logs).
        Devices = new DevicesViewModel(host, Navigate);
        Hub = new MobileHubViewModel(host, Navigate);
        OtherPcs = new RemotePcsViewModel(host);
        PhoneFiles = new PhoneFilesPageViewModel(host);
        var care = new TabsViewModel(host, "PC care", "\uE7F8",
            new TabEntry("Storage saver", new SmartCacheViewModel(host)),
            new TabEntry("RAM", new MemoryViewModel(host)),
            new TabEntry("Space and clean-up", new SpaceViewModel(host)));
        var settings = new SettingsViewModel(host) { Folders = new PhoneFoldersViewModel(Hub) };
        Pages = new ObservableCollection<PageViewModel>
        {
            new DashboardViewModel(host, Navigate),
            PhoneFiles,
            Hub,
            new CloudDrivesViewModel(host),
            care,
            new GameLibraryViewModel(host),
            new AppsViewModel(host),
            settings,
        };
        _all = new List<PageViewModel>();
        void Collect(PageViewModel p)
        {
            _all.Add(p);
            if (p is HubViewModel h) foreach (var i in h.Items) Collect(i.Page);
            if (p is TabsViewModel t) foreach (var e in t.Tabs) { _all.Add(e.Page); _tabOf[e.Page] = t; }
        }
        foreach (var p in Pages) Collect(p);
        _selected = Pages[0];
        host.StateChanged += () => Ui(() =>
        {
            Selected.OnStateChanged();
            UpdateStatus(host.Metrics.Latest);
        });
        host.Metrics.Updated += m => Ui(() =>
        {
            Selected.OnMetrics(m);
            if (Selected is MobileHubViewModel) { Devices.OnMetrics(m); OtherPcs.OnMetrics(m); }
            UpdateStatus(m);
        });

        // The bell at the top right: every note the app showed, newest first; a click opens its place.
        NotificationCenter.Load(System.IO.Path.Combine(host.Paths.DataRoot, "notifications.json"));
        NotificationCenter.Opener = OpenTarget;
        NotificationCenter.Changed += () => Ui(() => UnreadCount = NotificationCenter.Unread);
        UnreadCount = NotificationCenter.Unread;
    }

    // ---- notifications (the bell) ---------------------------------------------------------------------------------------

    public ObservableCollection<NotificationItem> Notifications => NotificationCenter.Items;

    [ObservableProperty] private int _unreadCount;
    [ObservableProperty] private bool _notificationsOpen;

    public bool HasUnread => UnreadCount > 0;
    public string UnreadText => UnreadCount > 99 ? "99+" : UnreadCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
    public bool HasNotifications => Notifications.Count > 0;

    partial void OnUnreadCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasUnread));
        OnPropertyChanged(nameof(UnreadText));
        OnPropertyChanged(nameof(HasNotifications));
    }

    partial void OnNotificationsOpenChanged(bool value)
    {
        if (value) NotificationCenter.RefreshTimes();
        OnPropertyChanged(nameof(HasNotifications));
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void ToggleNotifications() => NotificationsOpen = !NotificationsOpen;

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void MarkAllRead() => NotificationCenter.MarkAllRead();

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void ClearNotifications()
    {
        NotificationCenter.Clear();
        OnPropertyChanged(nameof(HasNotifications));
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void OpenNotification(NotificationItem? item)
    {
        if (item is null) return;
        NotificationsOpen = false;
        NotificationCenter.Open(item);
    }

    /// <summary>
    /// Goes to where a notification is about: "page:&lt;title&gt;", "device:&lt;id&gt;" (Devices), "perm:&lt;id&gt;" (its
    /// permissions), "access:&lt;request&gt;:&lt;device&gt;" (the request while it waits, else the device's permissions),
    /// "folder:&lt;path&gt;" (File Explorer). The window comes to the front first.
    /// </summary>
    public bool OpenTarget(string target)
    {
        var w = System.Windows.Application.Current?.MainWindow;
        if (w != null)
        {
            if (!w.IsVisible) w.Show();
            if (w.WindowState == System.Windows.WindowState.Minimized) w.WindowState = System.Windows.WindowState.Normal;
            w.Activate();
        }
        int colon = target.IndexOf(':');
        if (colon < 0) return false;
        string kind = target[..colon], rest = target[(colon + 1)..];
        switch (kind)
        {
            case "page":
                Navigate(rest);
                return true;
            case "device":
                Navigate("Devices");
                return true;
            case "task":
                // A send that waits for an OK (or any transfer): Files → Transfers, where it is listed.
                Navigate("Transfers");
                return true;
            case "perm":
                return OpenPermissions(rest);
            case "access":
            {
                var parts = rest.Split(':');
                var r = Host.MobileBridge.Access.Get(parts[0]);
                if (r is { State: "pending" })
                {
                    DeviceNotifications.ShowRequest(r);
                    return true;
                }
                // Answered or ran out: what that device may do now, where it can be changed.
                return parts.Length > 1 && OpenPermissions(parts[1]);
            }
            case "folder":
                if (!System.IO.Directory.Exists(rest)) { Navigate("Devices"); return true; }
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "\"" + rest + "\"") { UseShellExecute = true });
                return true;
        }
        return false;
    }

    private bool OpenPermissions(string deviceId)
    {
        Navigate("Devices");
        var device = Host.Settings.MobileBridge.PairedDevices.FirstOrDefault(d => d.Id == deviceId);
        if (device is null) return true;   // removed since: the Devices page is the place
        new Views.DevicePermissionsWindow(Host, device, justPaired: false, () => Navigate("Permissions"))
        { Owner = System.Windows.Application.Current?.MainWindow }.ShowDialog();
        return true;
    }

    public AppHost Host { get; }

    /// <summary>The sidebar's pages. Pages inside a hub ("More") are not here; <see cref="_all"/> has every page.</summary>
    public ObservableCollection<PageViewModel> Pages { get; }
    private readonly List<PageViewModel> _all;
    /// <summary>A page that is a tab of a section ("PC Memory (RAM)" in "PC care") and the section it is in.</summary>
    private readonly Dictionary<PageViewModel, TabsViewModel> _tabOf = new();

    /// <summary>The "Devices" section: pairing, permissions; its values also feed Files → History and Settings → Folders.</summary>
    public MobileHubViewModel Hub { get; }
    /// <summary>The list of paired devices: shown as the "Phones" tab of Devices.</summary>
    public DevicesViewModel Devices { get; }
    /// <summary>Other PCs this one uses: shown as a tab of Devices.</summary>
    public RemotePcsViewModel OtherPcs { get; }
    public PhoneFilesPageViewModel PhoneFiles { get; }

    /// <summary>The tab Devices should show next ("Devices", "Connect", "Permissions", "Other PCs"); its view takes it and clears it.</summary>
    public string? HubTab { get; set; }
    public event Action? HubTabRequested;

    private static PageViewModel TopOf(PageViewModel p)
    {
        while (p.Parent is { } up) p = up;
        return p;
    }

    /// <summary>The sidebar's selection: the section the shown page belongs to. Picking one opens that section.</summary>
    public PageViewModel SelectedTop
    {
        get => TopOf(Selected);
        set { if (value != null && value != TopOf(Selected)) Selected = value; }
    }

    /// <summary>The shown page sits inside a section: the title bar shows a back arrow.</summary>
    public bool CanGoBack => Selected.Parent != null;

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void Back()
    {
        if (Selected.Parent is { } up) Selected = up;
    }

    /// <summary>A click on the section already chosen in the sidebar, from a page inside it: back to the section.</summary>
    public void OpenTop(PageViewModel top)
    {
        if (Selected != top) Selected = top;
    }

    /// <summary>A phone's storage, in the app (from "Browse files" on its card).</summary>
    public void OpenPhoneFiles(string deviceId)
    {
        PhoneFiles.WantedDeviceId = deviceId;
        Selected = PhoneFiles;
        PhoneFilesRequested?.Invoke();
    }

    public event Action? PhoneFilesRequested;

    [ObservableProperty] private PageViewModel _selected;
    [ObservableProperty] private string _storageSummary = "";
    [ObservableProperty] private string _drivesStatus = "No drives connected";
    [ObservableProperty] private string _rateStatus = "";
    [ObservableProperty] private string _engineStatus = "";
    [ObservableProperty] private string _gameStatus = "";
    [ObservableProperty] private string _version = "v" + (typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0");

    partial void OnSelectedChanged(PageViewModel value)
    {
        value?.OnShown();
        if (value is MobileHubViewModel) Devices.OnShown();
        OnPropertyChanged(nameof(SelectedTop));
        OnPropertyChanged(nameof(CanGoBack));
        Ui(() => LocalizationManager.TranslateTree(System.Windows.Application.Current.MainWindow));
        // The page's own view is made after this (when it is laid out): its words are translated once it is there.
        LocalizationManager.TranslateSoon(System.Windows.Application.Current?.MainWindow);
    }

    public void Navigate(string title)
    {
        // Parts of the Devices section, by the names they had as pages or tabs.
        string? hubTab = title.ToLowerInvariant() switch
        {
            "devices" or "phones" => "Devices",
            "phone (mobile hub)" or "connect phone" or "add or connect" => "Connect",
            "permissions" => "Permissions",
            "other pcs" => "Other PCs",
            _ => null,
        };
        if (hubTab != null)
        {
            HubTab = hubTab;
            Selected = Hub;
            HubTabRequested?.Invoke();
            return;
        }
        if (title.Equals("Transfers", StringComparison.OrdinalIgnoreCase))
        {
            // What is moving now, and its history: a part of Files.
            PhoneFiles.WantedDeviceId = "transfers";
            Selected = PhoneFiles;
            PhoneFilesRequested?.Invoke();
            return;
        }
        if (title.Equals("Game Library", StringComparison.OrdinalIgnoreCase)) title = "Games";
        if (title.Equals("Dashboard", StringComparison.OrdinalIgnoreCase)) title = "Home";
        if (title.Equals("Customize", StringComparison.OrdinalIgnoreCase))
        {
            // Settings → Customize: language, colours, glass.
            var s = _all.OfType<SettingsViewModel>().FirstOrDefault();
            if (s != null)
            {
                s.SwitchSubTab("Customize");
                Selected = s;
                return;
            }
        }
        if (title.Contains("Cache", StringComparison.OrdinalIgnoreCase) || title.Contains("Storage", StringComparison.OrdinalIgnoreCase))
        {
            // Storage is a page of its own in the sidebar, like PC Memory.
            var s = _all.OfType<SmartCacheViewModel>().FirstOrDefault();
            if (s != null)
            {
                if (_tabOf.TryGetValue(s, out var care)) { care.Select(s); Selected = care; }
                else Selected = s;
                return;
            }
        }
        if (title.Contains("Accelerator", StringComparison.OrdinalIgnoreCase) || title.Contains("Speed", StringComparison.OrdinalIgnoreCase))
        {
            var s = _all.OfType<SettingsViewModel>().FirstOrDefault();
            if (s != null)
            {
                s.SwitchSubTab("Speed");
                Selected = s;
                return;
            }
        }
        if (title.Contains("Performance", StringComparison.OrdinalIgnoreCase))
        {
            var s = _all.OfType<SettingsViewModel>().FirstOrDefault();
            if (s != null)
            {
                s.SwitchSubTab("Performance");
                Selected = s;
                return;
            }
        }
        if (title.Contains("Diagnostics", StringComparison.OrdinalIgnoreCase) || title.Contains("Log", StringComparison.OrdinalIgnoreCase))
        {
            var s = _all.OfType<SettingsViewModel>().FirstOrDefault();
            if (s != null)
            {
                s.SwitchSubTab("Advanced");
                Selected = s;
                return;
            }
        }
        var p = _all.FirstOrDefault(x => x.Title.Equals(title, StringComparison.OrdinalIgnoreCase))
            ?? _all.FirstOrDefault(x => x.Title.Contains(title, StringComparison.OrdinalIgnoreCase));
        if (p is null) return;
        // A page that is a tab of a section: the section opens on that tab.
        if (_tabOf.TryGetValue(p, out var section))
        {
            section.Select(p);
            Selected = section;
        }
        else Selected = p;
    }

    private void UpdateStatus(EngineMetrics m)
    {
        // Real total over the connected accounts (asked from the provider), not a fixed marketing figure.
        StorageSummary = Host.Remotes.Count == 0 ? "No cloud account connected"
            : AccountText.Totals(Host) is { } t ? $"{ByteSize.Format(t.Total)} cloud · {t.Accounts} account(s)"
            : $"{Host.Remotes.Count(r => !AppHost.IsGeneratedType(r.Type))} account(s) connected";
        var sessions = Host.Mounts.Sessions;
        DrivesStatus = sessions.Count == 0 ? "No drives connected"
            : string.Join("  ", sessions.Select(s => $"{s.DriveLetter}: {s.State}"));
        RateStatus = m.DownloadBytesPerSec is { } r ? $"↓ {Bandwidth.BytesPerSecondToMbps(r):0.0} Mbps" : "";
        EngineStatus = Host.DiskPressure ? "Prefetch paused (disk space)" : Host.Engine.Scheduler.Active > 0 ? $"Prefetching ({Host.Engine.Queue.Count} queued)" : "";
        GameStatus = Host.CurrentLaunch is { State: Games.LaunchState.Running } l ? $"▶ {l.Game.Name}" : "";
    }

    private static void Ui(Action a)
    {
        var d = System.Windows.Application.Current?.Dispatcher;
        if (d is null) return;
        if (d.CheckAccess()) a();
        else d.InvokeAsync(a);
    }
}
