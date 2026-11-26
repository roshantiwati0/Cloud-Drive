using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using CloudSSD.Core;
using Microsoft.Extensions.Logging;

namespace CloudSSD.App;

/// <summary>
/// The devices paired with this PC as the Devices page shows them: kind, online now or when last seen, how they reach
/// the PC, what they are doing, and rename / block / remove. Presence lives in memory; the last-seen time is saved at
/// most once a minute, never on the request path.
/// </summary>
public sealed partial class LocalMobileBridgeService
{
    /// <summary>The clock for presence (tests move it forward).</summary>
    internal Func<DateTimeOffset> Now { get; set; } = () => DateTimeOffset.Now;

    internal static readonly TimeSpan LastSeenSaveInterval = TimeSpan.FromSeconds(60);
    internal static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(5);

    private DevicePresenceTracker? _presence;
    private DevicePresenceTracker Presence => LazyInitializer.EnsureInitialized(ref _presence, () => new DevicePresenceTracker(() => Now()));

    /// <summary>The transfer the current request is running, so <see cref="TrackBytes"/> can count its bytes.</summary>
    private static readonly AsyncLocal<DevicePresenceTracker.Transfer?> CurrentTransfer = new();

    private Timer? _sweepTimer;
    private DateTimeOffset _lastSeenSavedAt;
    private bool _lastSeenDirty;
    private int _devicesChangedPending;

    /// <summary>The device list changed (paired, removed, renamed, blocked, online/offline, activity). At most twice a second.</summary>
    public event Action? DevicesChanged;

    /// <summary>A device paired just now, or came back after at least two minutes away (at most once per ten minutes).</summary>
    public event Action<DeviceConnectedEvent>? DeviceConnected;

    // ---- the list ----------------------------------------------------------------------------------------------------

    /// <summary>Every paired device with its presence, for the PC's own screens (includes addresses).</summary>
    public IReadOnlyList<DeviceSnapshot> GetDevices()
    {
        lock (_devicesLock) return _host.Settings.MobileBridge.PairedDevices.Select(Snapshot).ToList();
    }

    public DeviceSnapshot? GetDevice(string id)
    {
        lock (_devicesLock) return _host.Settings.MobileBridge.PairedDevices.FirstOrDefault(d => d.Id == id) is { } d ? Snapshot(d) : null;
    }

    private DeviceSnapshot Snapshot(PairedDevice d)
    {
        var p = Presence;
        return new DeviceSnapshot(
            d.Id, d.Name, DeviceKinds.Of(d.Kind), d.Platform, d.AppVersion,
            Online: !d.IsBlocked && p.IsOnline(d.Id, d.LastSeen),
            LastSeen: p.LastSeen(d.Id, d.LastSeen),
            d.PairedAt,
            Link: p.LinkOf(d.Id) ?? d.LastLink ?? "lan",
            Ip: d.IpAddress,
            Activity: d.IsBlocked ? null : p.ActivityOf(d.Id),
            Blocked: d.IsBlocked,
            Legacy: d.TokenHash == null,
            d.PeerPcId,
            VitalsOf(d.Id),
            Listening: !d.IsBlocked && Peer.IsListening(d.Id),
            GuestUntil: d.GuestUntil);
    }

    // ---- changes from the PC -----------------------------------------------------------------------------------------

    /// <summary>A name of 1–60 visible characters; kept when the device pairs again.</summary>
    public bool RenameDevice(string id, string name)
    {
        var clean = new string((name ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (clean.Length is 0 or > 60) return false;
        lock (_devicesLock)
        {
            var d = _host.Settings.MobileBridge.PairedDevices.FirstOrDefault(x => x.Id == id);
            if (d is null) return false;
            d.Name = clean;
            d.NameSetOnPc = true;
            _host.SaveSettings();
        }
        StatusChanged?.Invoke();
        RaiseDevicesChanged();
        return true;
    }

    /// <summary>
    /// Blocks (or unblocks) a device: it stays in the list but nothing it sends is accepted, and what it is downloading or
    /// uploading stops at once. Unblocking keeps its key, so it works again without pairing.
    /// </summary>
    public bool SetBlocked(string id, bool blocked)
    {
        lock (_devicesLock)
        {
            var d = _host.Settings.MobileBridge.PairedDevices.FirstOrDefault(x => x.Id == id);
            if (d is null) return false;
            d.IsBlocked = blocked;
            _host.SaveSettings();
        }
        if (blocked) Presence.Kick(id);
        StatusChanged?.Invoke();
        RaiseDevicesChanged();
        return true;
    }

    // ---- presence ------------------------------------------------------------------------------------------------------

    /// <summary>Every request from a device: last seen, how it came in, what it is doing; a note when it came back.</summary>
    private void UpdateDeviceLastSeen(Caller caller, string path, string method, string link)
    {
        if (caller.Device is not { } device) return;
        DateTimeOffset persisted;
        lock (_devicesLock)
        {
            persisted = device.LastSeen;
            device.LastSeen = Now();
            device.IpAddress = caller.Ip;
            device.LastLink = link = LinkOf(link, device);
            _lastSeenDirty = true;
        }
        var (cameOnline, notify) = Presence.Seen(device.Id, persisted, link, caller.Ip, path, method);
        if (notify && _host.Settings.MobileBridge.NotifyDeviceConnections) RaiseDeviceConnected(device, justPaired: false);
        if (cameOnline || DevicePresenceTracker.FromRoute(path, method) != null) RaiseDevicesChanged();
    }

    /// <summary>Run every few seconds: devices that went quiet turn offline, and the last-seen times are saved now and then.</summary>
    internal void SweepPresence()
    {
        try
        {
            if (Presence.Sweep().Count > 0) RaiseDevicesChanged();
            FlushLastSeen(force: false);
            EndExpiredGuests();
            SweepUploadTasks();
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Device presence sweep failed");
        }
    }

    /// <summary>Saves the last-seen times at most once per <see cref="LastSeenSaveInterval"/> (or now, when the bridge stops).</summary>
    /// <summary>Bytes to [0] and from [1] each device that are not in its saved totals yet (added at the next flush).</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, long[]> _deviceBytes = new();

    /// <summary>A device's all-time totals: saved, plus what moved since the last save.</summary>
    internal (long ToDevice, long FromDevice) BytesOf(PairedDevice d)
    {
        long to = d.BytesToDevice, from = d.BytesFromDevice;
        if (_deviceBytes.TryGetValue(d.Id, out var c)) { to += Interlocked.Read(ref c[0]); from += Interlocked.Read(ref c[1]); }
        return (to, from);
    }

    private object MyBytes(PairedDevice d)
    {
        var (to, from) = BytesOf(d);
        return new { toDevice = to, fromDevice = from };
    }

    internal void FlushLastSeen(bool force)
    {
        var now = Now();
        lock (_devicesLock)
        {
            if (!_lastSeenDirty) return;
            if (!force && now - _lastSeenSavedAt < LastSeenSaveInterval) return;
            foreach (var (id, counts) in _deviceBytes)
            {
                long to = Interlocked.Exchange(ref counts[0], 0), from = Interlocked.Exchange(ref counts[1], 0);
                if (_host.Settings.MobileBridge.PairedDevices.FirstOrDefault(x => x.Id == id) is { } d)
                {
                    d.BytesToDevice += to;
                    d.BytesFromDevice += from;
                }
            }
            try
            {
                _host.SaveSettings();
                _lastSeenDirty = false;
                _lastSeenSavedAt = now;
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "Saving device last-seen times failed; trying again at the next sweep");
            }
        }
    }

    private void StartPresence()
    {
        _sweepTimer?.Dispose();
        _sweepTimer = new Timer(_ => SweepPresence(), null, SweepInterval, SweepInterval);
    }

    private void StopPresence()
    {
        _sweepTimer?.Dispose();
        _sweepTimer = null;
        FlushLastSeen(force: true);
    }

    /// <summary>Coalesced: many requests in a burst raise one event (the Devices page redraws at most twice a second).</summary>
    private void RaiseDevicesChanged()
    {
        if (Interlocked.Exchange(ref _devicesChangedPending, 1) == 1) return;
        _ = Task.Run(async () =>
        {
            await Task.Delay(500);
            Interlocked.Exchange(ref _devicesChangedPending, 0);
            try { DevicesChanged?.Invoke(); }
            catch (Exception ex) { _log.LogDebug(ex, "DevicesChanged handler failed"); }
        });
    }

    private void RaiseDeviceConnected(PairedDevice device, bool justPaired)
    {
        DeviceSnapshot snap;
        lock (_devicesLock) snap = Snapshot(device);
        try { DeviceConnected?.Invoke(new DeviceConnectedEvent(snap, justPaired)); }
        catch (Exception ex) { _log.LogDebug(ex, "DeviceConnected handler failed"); }
    }

    /// <summary>
    /// Starts counting a transfer for the device behind the current request: it shows as "Upload 45%" on its card, keeps
    /// the device online while it runs, and stops when the device is blocked. Dispose it when the transfer ends.
    /// </summary>
    private IDisposable? BeginTransfer(DeviceActivityKind kind, string file, long total, long already = 0)
    {
        if (CurrentCaller.Value?.Device is not { } device) return null;
        DateTimeOffset persisted;
        lock (_devicesLock) persisted = device.LastSeen;
        var t = Presence.BeginTransfer(device.Id, persisted, kind, file, total, already);
        CurrentTransfer.Value = t;
        t.Ended += RaiseDevicesChanged;
        RaiseDevicesChanged();
        return new TransferScope(t);
    }

    private sealed class TransferScope(DevicePresenceTracker.Transfer t) : IDisposable
    {
        public void Dispose()
        {
            if (CurrentTransfer.Value == t) CurrentTransfer.Value = null;
            t.Dispose();
        }
    }

    // ---- how a device reaches the PC -------------------------------------------------------------------------------

    /// <summary>
    /// How a request arrived (see <see cref="LinkKinds"/>): from our own end of the connection, the network card it came in
    /// on and whether the device is that card's gateway. A loopback request stays "local" until a phone is known to have sent it.
    /// </summary>
    internal static string ClassifyLink(IPEndPoint? local, IPEndPoint? remote, NetRoute route) =>
        LinkClassifier.Classify(local?.Address, remote?.Address, route != NetRoute.Lan, LinkClassifier.Snapshot());

    /// <summary>[{url, kind}]: this PC's addresses a device can use without the internet, best first.</summary>
    private object[] DirectAddressesDto() =>
        LinkClassifier.DirectAddresses(LinkClassifier.Snapshot())
            .Select(a => (object)new { url = $"http://{a.Ip}:{Port}", kind = a.Kind }).ToArray();

    /// <summary>The card a paired device can wake this PC through (Wake-on-LAN), or null when there is none.</summary>
    private static object? WakeDto() =>
        WakeOnLan.Current(GetPreferredLocalIp()) is { } w ? new { mac = w.Mac, broadcast = w.Broadcast, ip = w.Ip, wired = w.Wired } : null;

    /// <summary>A phone or tablet app on localhost came through "adb reverse" over USB; anything else there is this PC.</summary>
    internal static string LinkOf(string link, PairedDevice? device) =>
        link == LinkKinds.Local && device is not null && DeviceKinds.Of(device.Kind) is DeviceKind.Phone or DeviceKind.Tablet ? LinkKinds.UsbAdb : link;

    // ---- small routes --------------------------------------------------------------------------------------------------

    /// <summary>A cheap "I'm here" for laptops and phones (/api/status lists folders on every call).</summary>
    private Task HandlePingAsync(NetworkStream stream, CancellationToken ct) =>
        SendJsonAsync(stream, 200, "OK", new { ok = true, pcId = _host.Settings.MobileBridge.PcId, time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }, ct);
}
