using System.Windows;
using CloudSSD.App;
using CloudSSD.Desktop.ViewModels;
using CloudSSD.Desktop.Views;

namespace CloudSSD.Desktop;

/// <summary>
/// "Redmi 12 connected · Wi-Fi" in the corner of the screen when a device pairs or comes back. Several devices within
/// two seconds make one note (the notifier shows one at a time), and nothing pops up over a running game.
/// </summary>
public static class DeviceNotifications
{
    private static readonly TimeSpan Gather = TimeSpan.FromSeconds(2);
    private static readonly object Lock = new();
    private static readonly List<DeviceConnectedEvent> Pending = new();
    private static bool _attached;
    private static AppHost? _host;

    public static void Attach(AppHost host)
    {
        lock (Lock)
        {
            if (_attached) return;
            _attached = true;
            _host = host;
        }
        host.MobileBridge.DeviceConnected += OnConnected;
        host.MobileBridge.Access.RequestCreated += OnAccessRequest;
        host.MobileBridge.Shares.Changed += OnShareChanged;
        host.MobileBridge.FileReceived += OnFileReceived;
        host.MobileBridge.ClipboardReceived += OnClipboard;
        host.MobileBridge.Tasks.Changed += OnTaskChanged;
        lock (WhenOffSeen)
            foreach (var d in host.Settings.MobileBridge.PairedDevices) WhenOffSeen[d.Id] = d.WhenOffDevice;
        host.MobileBridge.WhenOffChanged += OnWhenOffChanged;
        // A request answered (here, in its window, or it ran out): its line in the list is done.
        host.MobileBridge.Access.RequestEnded += r => NotificationCenter.MarkRead(AccessTarget(r));
    }

    /// <summary>Where a click on a request's line goes: its window while it waits, else the device's permissions.</summary>
    public static string AccessTarget(AccessRequest r) => $"access:{r.Id}:{r.DeviceId}";

    /// <summary>A phone sent its clipboard: it is on this PC's clipboard now.</summary>
    private static void OnClipboard(CloudSSD.Core.PairedDevice d, string text)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            try { Clipboard.SetText(text); } catch (System.Runtime.InteropServices.COMException) { return; }
            var preview = text.Length > 80 ? text[..80] + "…" : text;
            Notifier.Show(string.Format(LocalizationManager.T("Clipboard from {0}"), d.Name), preview, "page:Devices", "clip");
        });
    }

    // ---- files between devices --------------------------------------------------------------------------------------

    private static readonly HashSet<string> AskedAbout = new();

    /// <summary>
    /// A device's files wait for an OK here (a guest's, or everyone's when the setting says always ask): one note per
    /// send, shown even while a game is in front (the sender is waiting); a click opens Files → Transfers, where the
    /// "Files to receive" card has Accept and Decline. Answered: its line in the bell is done.
    /// </summary>
    private static readonly Dictionary<string, bool> WhenOffSeen = new();

    /// <summary>
    /// A device said yes to "Files when it is off": a note that leads to its permissions, where this PC's own yes is
    /// (it works only when both said yes). Nothing is shown when the answer was given here, or taken back.
    /// </summary>
    private static void OnWhenOffChanged(CloudSSD.Core.PairedDevice d)
    {
        bool before;
        lock (WhenOffSeen) { WhenOffSeen.TryGetValue(d.Id, out before); WhenOffSeen[d.Id] = d.WhenOffDevice; }
        if (!d.WhenOffDevice || before) return;
        Application.Current?.Dispatcher.BeginInvoke(() =>
            Notifier.Show(string.Format(LocalizationManager.T("{0} said yes to \"Files when it is off\""), d.Name),
                LocalizationManager.T(d.WhenOffPc ? "It is on for both now." : "Say yes for it here too: open its permissions."), "perm:" + d.Id, "access"));
    }

    private static void OnTaskChanged(TransferTask t)
    {
        var target = "task:" + t.Id;
        if (t.State != "asking")
        {
            bool was;
            lock (AskedAbout) was = AskedAbout.Remove(t.Id);
            if (was) Application.Current?.Dispatcher.BeginInvoke(() => NotificationCenter.MarkRead(target));
            return;
        }
        lock (AskedAbout) { if (!AskedAbout.Add(t.Id)) return; }
        Application.Current?.Dispatcher.BeginInvoke(() =>
            Notifier.Show(string.Format(LocalizationManager.T("{0} wants to send {1} file(s)"), t.Device, t.Files),
                LocalizationManager.T("Accept or decline in Files → Transfers."), target, "files"));
    }

    /// <summary>A device took (or turned down) files from this PC.</summary>
    private static void OnShareChanged(ShareOffer o)
    {
        var host = _host;
        if (host is null || o.FromId != "pc" || GameActivity.IsPlaying(host)) return;
        if (o.State == "done")
            Notifier.Show(LocalizationManager.T("Files delivered"), string.Format(LocalizationManager.T("{0} received {1} file(s)."), o.ToName, o.Items.Count), "page:Devices", "files");
        else if (o.State == "declined")
            Notifier.Show(LocalizationManager.T("Files declined"), string.Format(LocalizationManager.T("{0} did not take the files."), o.ToName), "page:Devices", "files");
    }

    private static readonly List<(string Device, string Name, string Dir)> Received = new();

    /// <summary>"Redmi 12 sent you 3 files" — gathered for a few seconds, so a batch makes one note.</summary>
    private static void OnFileReceived(CloudSSD.Core.PairedDevice d, string path)
    {
        bool first;
        lock (Lock)
        {
            first = Received.Count == 0;
            Received.Add((d.Name, System.IO.Path.GetFileName(path), System.IO.Path.GetDirectoryName(path) ?? ""));
        }
        if (!first) return;
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(4));
            List<(string Device, string Name, string Dir)> batch;
            lock (Lock) { batch = Received.ToList(); Received.Clear(); }
            var host = _host;
            if (host is null || batch.Count == 0) return;
            var who = string.Join(", ", batch.Select(b => b.Device).Distinct());
            var title = string.Format(LocalizationManager.T("{0} sent you files"), who);
            var text = batch.Count == 1 ? batch[0].Name : string.Format(LocalizationManager.T("{0} files · in the From phone folder"), batch.Count);
            // A click opens the folder they are in.
            var target = "folder:" + batch[0].Dir;
            if (GameActivity.IsPlaying(host)) NotificationCenter.Add(title, text, target, "files");
            else Notifier.Show(title, text, target, "files");
        });
    }

    // ---- a device asks for access: one window at a time, none over a running game -----------------------------------

    private static AccessRequestWindow? _openRequest;

    private static void OnAccessRequest(AccessRequest r)
    {
        var host = _host;
        if (host is null) return;
        var title = string.Format(LocalizationManager.T("{0} asks for access"), r.DeviceName);
        var what = string.Join(", ", r.Categories.Select(c => LocalizationManager.T(AccessCategories.Describe(c))));
        if (GameActivity.IsPlaying(host))
        {
            Notifier.Show(title, LocalizationManager.T("Open Cloud-SSD to answer within 2 minutes.") + " " + what, AccessTarget(r), "access");
            return;
        }
        // Its window opens now; the list keeps it too, for whoever closes the window and looks for it later.
        NotificationCenter.Add(title, what, AccessTarget(r), "access");
        Application.Current?.Dispatcher.BeginInvoke(ShowNextRequest);
    }

    /// <summary>Shows a waiting request's window again (from the bell's list); the open one comes to the front.</summary>
    public static void ShowRequest(AccessRequest r)
    {
        if (_host is not { } host) return;
        if (_openRequest != null) { _openRequest.Activate(); return; }
        _openRequest = new AccessRequestWindow(host, r);
        _openRequest.Closed += (_, _) => { _openRequest = null; ShowNextRequest(); };
        _openRequest.Show();
        _openRequest.Activate();
    }

    private static void ShowNextRequest()
    {
        if (_openRequest != null || _host is not { } host) return;
        var next = host.MobileBridge.Access.Pending().FirstOrDefault();
        if (next is null) return;
        _openRequest = new AccessRequestWindow(host, next);
        _openRequest.Closed += (_, _) => { _openRequest = null; ShowNextRequest(); };
        _openRequest.Show();
        _openRequest.Activate();
    }

    /// <summary>A new device paired: what may it do? (the defaults are ticked; skipped during a game).</summary>
    private static void AskWhatItMayDo(DeviceConnectedEvent e)
    {
        var host = _host;
        if (host is null || !host.Settings.MobileBridge.AskOnPairing) return;
        NotificationCenter.Add(string.Format(LocalizationManager.T("{0} paired with this PC"), e.Device.Name),
            LocalizationManager.T("Choose what it may do"), "perm:" + e.Device.Id, "access");
        if (GameActivity.IsPlaying(host)) return;
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            var device = host.Settings.MobileBridge.PairedDevices.FirstOrDefault(d => d.Id == e.Device.Id);
            if (device is null) return;
            var w = new DevicePermissionsWindow(host, device, justPaired: true) { Owner = Application.Current.MainWindow?.IsVisible == true ? Application.Current.MainWindow : null };
            if (w.Owner is null) w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            w.Show();
        });
    }

    private static void OnConnected(DeviceConnectedEvent e)
    {
        // A guest's permissions were chosen when its code was made ("Add guest"): nothing to ask.
        if (e.JustPaired && e.Device.GuestUntil is null) AskWhatItMayDo(e);
        bool first;
        lock (Lock)
        {
            first = Pending.Count == 0;
            Pending.Add(e);
        }
        if (first) _ = FlushLaterAsync();
    }

    private static async Task FlushLaterAsync()
    {
        await Task.Delay(Gather);
        List<DeviceConnectedEvent> batch;
        lock (Lock)
        {
            batch = Pending.ToList();
            Pending.Clear();
        }
        if (batch.Count == 0 || _host is null) return;
        var (title, text) = Compose(batch);
        var ids = batch.Select(e => e.Device.Id).Distinct().ToList();
        var target = ids.Count == 1 ? "device:" + ids[0] : "page:Devices";
        // Listed always; the corner note only when wanted and not over a game.
        if (!_host.Settings.MobileBridge.NotifyDeviceConnections || GameActivity.IsPlaying(_host)) NotificationCenter.Add(title, text, target, "device");
        else Notifier.Show(title, text, target, "device");
    }

    /// <summary>The note for a batch of events (pure, for tests).</summary>
    public static (string Title, string Text) Compose(IReadOnlyList<DeviceConnectedEvent> batch)
    {
        // The same device twice in the batch counts once.
        var unique = batch.GroupBy(e => e.Device.Id).Select(g => g.Last()).ToList();
        if (unique.Count == 1)
        {
            var e = unique[0];
            var title = LocalizationManager.T(e.JustPaired ? "New device paired" : "Device connected");
            return (title, $"{e.Device.Name} · {DeviceText.Link(e.Device.Link, "")}");
        }
        var names = string.Join(", ", unique.Select(e => e.Device.Name));
        return (LocalizationManager.T(unique.Any(e => e.JustPaired) ? "New device paired" : "Device connected"),
            string.Format(LocalizationManager.T("{0} devices connected: {1}"), unique.Count, names));
    }
}
