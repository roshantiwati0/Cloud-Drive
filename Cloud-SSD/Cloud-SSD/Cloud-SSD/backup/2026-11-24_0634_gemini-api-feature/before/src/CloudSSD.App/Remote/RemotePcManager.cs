using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace CloudSSD.App.Remote;

/// <summary>
/// The "Other PCs" this PC is paired with as a laptop: pairing with a code, the saved list, one session per PC, and
/// finding Cloud-SSD PCs on this network.
/// </summary>
public sealed class RemotePcManager : IAsyncDisposable
{
    private readonly Func<string?> _ownPcId;
    private readonly Func<int?> _ownPort;
    private readonly Func<HttpMessageHandler?> _handlerFactory;
    private readonly Func<DateTimeOffset> _now;
    private readonly ILogger _log;
    private readonly Dictionary<string, RemotePcSession> _sessions = new();
    private readonly object _lock = new();
    private RemotePcClient? _client;
    private bool _started;

    /// <summary>Gives a PC this one paired with a key to this PC (pcId, name → its device id and key), or null when this PC's bridge is off.</summary>
    private readonly Func<string, string, (string DeviceId, string Token)?>? _issueBack;
    /// <summary>Takes such a key back (by device id) when this PC forgets that PC.</summary>
    private readonly Action<string>? _revokeBack;

    public RemotePcManager(string dir, Func<string?> ownPcId, Func<int?> ownPort, ILogger log,
        Func<HttpMessageHandler?>? handlerFactory = null, Func<DateTimeOffset>? now = null,
        Func<string, string, (string DeviceId, string Token)?>? issueBack = null, Action<string>? revokeBack = null)
    {
        Store = new RemotePcStore(dir);
        _ownPcId = ownPcId;
        _ownPort = ownPort;
        _log = log;
        _handlerFactory = handlerFactory ?? (() => null);
        _now = now ?? (() => DateTimeOffset.Now);
        _issueBack = issueBack;
        _revokeBack = revokeBack;
    }

    public RemotePcStore Store { get; }
    public event Action? Changed;

    private RemotePcClient Client => _client ??= new RemotePcClient(_handlerFactory());

    /// <summary>Opens a session for every saved PC and starts checking them (the page calls this when first shown).</summary>
    public void Start()
    {
        if (_started) return;
        _started = true;
        foreach (var pc in Store.Pcs) Session(pc.PcId)?.Start();
    }

    public IReadOnlyList<RemotePcSession> Sessions
    {
        get
        {
            foreach (var pc in Store.Pcs) Session(pc.PcId);
            lock (_lock) return Store.Pcs.Select(p => _sessions.GetValueOrDefault(p.PcId)).Where(s => s != null).Cast<RemotePcSession>().ToList();
        }
    }

    public RemotePcSession? Session(string pcId)
    {
        lock (_lock)
        {
            if (_sessions.TryGetValue(pcId, out var s)) return s;
            var pc = Store.Get(pcId);
            if (pc is null) return null;
            s = new RemotePcSession(Store, pc, Client, _log, _now);
            _sessions[pcId] = s;
            if (_started) s.Start();
            return s;
        }
    }

    public RemotePcSession? Active => Store.ActivePcId is { } id ? Session(id) : Sessions.FirstOrDefault();

    public void SetActive(string pcId)
    {
        Store.ActivePcId = pcId;
        Changed?.Invoke();
    }

    /// <summary>What this PC tells the other one about itself: a laptop (or desktop), Windows, this app, its own bridge.</summary>
    private PairInfo Info()
    {
        string kind = HasBattery() ? "laptop" : "desktop";
        var v = Environment.OSVersion.Version;
        string platform = v.Build >= 22000 ? "Windows 11" : "Windows 10";
        var own = _ownPcId();
        return new PairInfo(Environment.MachineName, Store.InstallId, kind, platform, LocalMobileBridgeService.AppVersion,
            RemotePcStore.IsValidPcId(own) ? own : null, Environment.MachineName, _ownPort());
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus { public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag; public int BatteryLifeTime, BatteryFullLifeTime; }
    [DllImport("kernel32.dll")] private static extern bool GetSystemPowerStatus(out SystemPowerStatus s);
    private static bool HasBattery() => GetSystemPowerStatus(out var s) && s.BatteryFlag != 128 && s.BatteryFlag != 255;

    /// <summary>Pairs with another PC by its address and the six digits it shows; it becomes the active one.</summary>
    public async Task<RemotePcSession> AddPcAsync(string address, string code, CancellationToken ct = default)
    {
        var url = RemotePcClient.NormalizeUrl(address);
        if (url.Length == 0) throw new RemotePcException(RemoteErrorKind.NotFound, "Type the other PC's address (shown in its Cloud-SSD → Devices → Add a device).");
        var probe = await Client.ProbeAsync(url, ct) ?? throw new RemotePcException(RemoteErrorKind.Offline, "No Cloud-SSD answers at that address. Are both on the same Wi-Fi, with Cloud-SSD open on that PC?");
        if (probe.PcId.Length > 0 && probe.PcId == _ownPcId()) throw new RemotePcException(RemoteErrorKind.Self, "That is this PC itself.");
        var r = await Client.PairAsync(url, code, Info(), ct);
        if (!RemotePcStore.IsValidPcId(r.PcId)) throw new RemotePcException(RemoteErrorKind.OldPc, "That PC's Cloud-SSD is too old to be added here. Update it first.");
        var existing = Store.Get(r.PcId);
        var pc = existing ?? new RemotePc { PcId = r.PcId };
        pc.Name = r.PcName.Length > 0 ? r.PcName : probe.PcName;
        pc.LanUrl = url;
        pc.LastUrl = url;
        pc.DeviceId = r.DeviceId;
        pc.TokenProtected = RemotePcStore.Protect(r.Token);
        pc.NeedsRepair = false;
        pc.Features = probe.Features.ToList();
        pc.LastOnlineAt = _now();
        Store.Upsert(pc);
        Store.ActivePcId = pc.PcId;
        RemotePcSession? s;
        lock (_lock)
        {
            // A fresh session for it (an old one would still hold the previous key's state).
            _sessions.Remove(pc.PcId);
        }
        s = Session(pc.PcId)!;
        await s.TickAsync(ct);
        await PairBackAsync(pc, s, ct);
        Changed?.Invoke();
        return s;
    }

    /// <summary>
    /// Gives the PC just paired with a key to this PC too (nothing allowed until someone here says so), so this PC is on
    /// its Other PCs page. Not when this PC's bridge is off (it could not be reached anyway) or that PC is too old.
    /// </summary>
    private async Task PairBackAsync(RemotePc pc, RemotePcSession s, CancellationToken ct)
    {
        if (_issueBack is null || !s.IsOnline || s.CurrentUrl is not { } url || !pc.Features.Contains("peer")) return;
        var own = _ownPcId();
        if (!RemotePcStore.IsValidPcId(own) || _ownPort() is not { } port || Store.Token(pc) is not { } token) return;
        try
        {
            if (_issueBack(pc.PcId, pc.DisplayName) is not { } back) return;
            pc.BackDeviceId = back.DeviceId;
            Store.Upsert(pc);
            await Client.PairBackAsync(url, token, own!, Environment.MachineName, port, back.DeviceId, back.Token, ct);
        }
        catch (RemotePcException e) { _log.LogDebug(e, "Pairing back with {Pc} failed", pc.DisplayName); }
    }

    /// <summary>Forgets a PC on this one: tells it first when it can, then deletes its key and everything kept from it.</summary>
    public async Task ForgetAsync(string pcId, CancellationToken ct = default)
    {
        var s = Session(pcId);
        var pc = Store.Get(pcId);
        if (s != null && pc != null && s.IsOnline && s.CurrentUrl != null && Store.Token(pc) is { } token)
            await Client.RemoveSelfAsync(s.CurrentUrl, token, ct);
        // The key this PC gave it in return stops working too.
        if (pc?.BackDeviceId is { Length: > 0 } back) _revokeBack?.Invoke(back);
        if (s != null) await s.DisposeAsync();
        lock (_lock) _sessions.Remove(pcId);
        Store.Remove(pcId);
        Changed?.Invoke();
    }

    /// <summary>
    /// Another PC paired with this one and gave it a key to itself: it shows on the Other PCs page (it allows nothing
    /// until someone there says so). A PC this one paired with itself keeps its own key.
    /// </summary>
    public void AddPairedBack(string pcId, string name, string url, string deviceId, string token)
    {
        if (!RemotePcStore.IsValidPcId(pcId)) return;
        var existing = Store.Get(pcId);
        if (existing is { NeedsRepair: false, PairedBack: false }) return;
        var pc = existing ?? new RemotePc { PcId = pcId };
        pc.Name = name;
        pc.LanUrl = url;
        pc.LastUrl = url;
        pc.DeviceId = deviceId;
        pc.TokenProtected = RemotePcStore.Protect(token);
        pc.NeedsRepair = false;
        pc.PairedBack = true;
        Store.Upsert(pc);
        lock (_lock) _sessions.Remove(pcId);   // a fresh session with the new key
        Session(pcId);
        Changed?.Invoke();
    }

    /// <summary>That PC was removed here, or it forgot this one: the entry it added by pairing back goes too.</summary>
    public void ForgetPairedBack(string pcId)
    {
        if (Store.Get(pcId) is not { PairedBack: true }) return;
        RemotePcSession? s;
        lock (_lock) { _sessions.Remove(pcId, out s); }
        Store.Remove(pcId);
        if (s != null) _ = s.DisposeAsync().AsTask();
        Changed?.Invoke();
    }

    public async ValueTask DisposeAsync()
    {
        List<RemotePcSession> all;
        lock (_lock) { all = _sessions.Values.ToList(); _sessions.Clear(); }
        foreach (var s in all) await s.DisposeAsync();
        _client?.Dispose();
    }

    // ---- finding PCs on this network ------------------------------------------------------------------------------

    /// <summary>Every host of a /24 or smaller subnet (capped), for the address search.</summary>
    internal static IEnumerable<IPAddress> SubnetHosts(IPAddress ip, IPAddress mask, int cap = 1024)
    {
        var a = ip.GetAddressBytes();
        var m = mask.GetAddressBytes();
        uint addr = (uint)(a[0] << 24 | a[1] << 16 | a[2] << 8 | a[3]);
        uint msk = (uint)(m[0] << 24 | m[1] << 16 | m[2] << 8 | m[3]);
        if (msk < 0xFFFFFC00u) msk = 0xFFFFFF00u;   // wider than /22: only our own /24
        uint net = addr & msk, bcast = net | ~msk;
        int n = 0;
        for (uint h = net + 1; h < bcast && n < cap; h++, n++)
        {
            if (h == addr) continue;
            yield return new IPAddress(new[] { (byte)(h >> 24), (byte)(h >> 16), (byte)(h >> 8), (byte)h });
        }
    }

    /// <summary>Asks every address on this PC's home networks for a Cloud-SSD (its public answer only; no key is sent).</summary>
    public async Task<IReadOnlyList<FoundPc>> DiscoverAsync(int port = 8090, IEnumerable<string>? extraHosts = null, CancellationToken ct = default)
    {
        var hosts = new List<string>(extraHosts ?? Array.Empty<string>());
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork || ua.IPv4Mask is null || TailscaleInfo.IsTailnetAddress(ua.Address)) continue;
                    var b = ua.Address.GetAddressBytes();
                    if (b[0] == 169 && b[1] == 254) continue;
                    hosts.AddRange(SubnetHosts(ua.Address, ua.IPv4Mask).Select(h => h.ToString()));
                }
            }
        }
        catch (NetworkInformationException) { }
        var found = new System.Collections.Concurrent.ConcurrentBag<FoundPc>();
        var own = _ownPcId();
        await Parallel.ForEachAsync(hosts.Distinct(), new ParallelOptions { MaxDegreeOfParallelism = 48, CancellationToken = ct }, async (h, token) =>
        {
            var url = $"http://{h}:{port}";
            var p = await Client.ProbeAsync(url, token, TimeSpan.FromMilliseconds(800));
            if (p is { } s && (s.PcId.Length == 0 || s.PcId != own)) found.Add(new FoundPc(url, s.PcName, s.PcId, s.Version));
        });
        return found.OrderBy(f => f.PcName).ToList();
    }
}
