using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace CloudSSD.App;

/// <summary>One network card of this PC with one IPv4 address, as the link rules see it.</summary>
public sealed record NicInfo(string Description, NetworkInterfaceType Type, IPAddress Address, int PrefixLength, IReadOnlyList<IPAddress> Gateways);

/// <summary>How a device reaches this PC. The names are what the API and the saved devices use.</summary>
public static class LinkKinds
{
    public const string Wifi = "wifi";
    public const string Lan = "lan";
    /// <summary>This PC's own hotspot (Windows Mobile hotspot, 192.168.137.x): a phone joined it.</summary>
    public const string PcHotspot = "pc-hotspot";
    /// <summary>The phone's hotspot: this PC joined it, so the phone is this PC's gateway.</summary>
    public const string PhoneHotspot = "phone-hotspot";
    /// <summary>USB tethering: the phone's network over the cable (an RNDIS/NCM card on this PC).</summary>
    public const string Usb = "usb";
    /// <summary>The phone app through "adb reverse" (USB debugging): it arrives as this PC's own localhost.</summary>
    public const string UsbAdb = "usb-adb";
    /// <summary>Through Tailscale, from anywhere.</summary>
    public const string Internet = "internet";
    /// <summary>A browser on this PC itself.</summary>
    public const string Local = "local";

    /// <summary>Links that work without any internet (the PC and the device see each other directly).</summary>
    public static bool IsDirect(string kind) => kind is Wifi or Lan or PcHotspot or PhoneHotspot or Usb or UsbAdb;
}

/// <summary>
/// Works out the kind of link a request came in on, from this PC's end of the connection: which card it arrived on,
/// and whether the device is that card's gateway. Pure, so it can be tested with made-up cards.
/// </summary>
public static class LinkClassifier
{
    private static readonly object Lock = new();
    private static (DateTimeOffset At, IReadOnlyList<NicInfo> Nics)? _cache;

    /// <summary>The cards that are up, with their IPv4 addresses and gateways (kept 30 seconds).</summary>
    public static IReadOnlyList<NicInfo> Snapshot()
    {
        lock (Lock)
        {
            if (_cache is { } c && DateTimeOffset.UtcNow - c.At < TimeSpan.FromSeconds(30)) return c.Nics;
            var list = new List<NicInfo>();
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    var props = ni.GetIPProperties();
                    var gateways = props.GatewayAddresses.Select(g => g.Address).Where(a => a.AddressFamily == AddressFamily.InterNetwork).ToList();
                    foreach (var ua in props.UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        list.Add(new NicInfo(ni.Description ?? "", ni.NetworkInterfaceType, ua.Address, ua.PrefixLength, gateways));
                    }
                }
            }
            catch (NetworkInformationException) { }
            _cache = (DateTimeOffset.UtcNow, list);
            return list;
        }
    }

    /// <summary>Forget the cards seen (after the network changed).</summary>
    public static void Forget() { lock (Lock) _cache = null; }

    public static bool IsUsbTether(string description) =>
        description.Contains("RNDIS", StringComparison.OrdinalIgnoreCase) || description.Contains("Remote NDIS", StringComparison.OrdinalIgnoreCase)
        || description.Contains("UsbNcm", StringComparison.OrdinalIgnoreCase) || description.Contains("NCM", StringComparison.Ordinal)
        || description.Contains("Android USB Ethernet", StringComparison.OrdinalIgnoreCase);

    public static bool IsHostedNetwork(string description) =>
        description.Contains("Wi-Fi Direct", StringComparison.OrdinalIgnoreCase) || description.Contains("Hosted Network", StringComparison.OrdinalIgnoreCase);

    /// <summary>Windows Mobile hotspot hands out 192.168.137.0/24, with this PC as .1.</summary>
    public static bool IsMobileHotspotRange(IPAddress ip)
    {
        var b = Normalize(ip).GetAddressBytes();
        return b.Length == 4 && b[0] == 192 && b[1] == 168 && b[2] == 137;
    }

    private static IPAddress Normalize(IPAddress ip) => ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip;

    /// <summary>
    /// The link of one request. local = this PC's end, remote = the device's end; tailnet = it came through Tailscale.
    /// A loopback request is "local" here; the caller turns it into "usb-adb" once it knows a phone sent it.
    /// </summary>
    public static string Classify(IPAddress? local, IPAddress? remote, bool tailnet, IReadOnlyList<NicInfo> nics)
    {
        if (tailnet) return LinkKinds.Internet;
        if (remote is not null && IPAddress.IsLoopback(Normalize(remote))) return LinkKinds.Local;
        if (local is null) return LinkKinds.Lan;
        local = Normalize(local);
        if (IsMobileHotspotRange(local)) return LinkKinds.PcHotspot;
        var nic = nics.FirstOrDefault(n => n.Address.Equals(local));
        if (nic is null) return LinkKinds.Lan;
        return KindOf(nic, remote is null ? null : Normalize(remote));
    }

    /// <summary>The kind of link a card gives; with the device's address, whether the device is this card's gateway.</summary>
    public static string KindOf(NicInfo nic, IPAddress? remote = null)
    {
        if (IsUsbTether(nic.Description)) return LinkKinds.Usb;
        if (IsHostedNetwork(nic.Description) || IsMobileHotspotRange(nic.Address)) return LinkKinds.PcHotspot;
        // This PC joined the phone's hotspot: the phone hands out the addresses and is the way out.
        if (remote is not null && nic.Gateways.Any(g => g.Equals(remote))) return LinkKinds.PhoneHotspot;
        return nic.Type == NetworkInterfaceType.Wireless80211 ? LinkKinds.Wifi : LinkKinds.Lan;
    }

    /// <summary>
    /// The addresses a device can use to reach this PC without the internet, best first, with their kind: home Wi-Fi
    /// and Ethernet, then this PC's hotspot and USB tethering (only a device on that link can use those).
    /// Tailscale, link-local and loopback addresses are left out.
    /// </summary>
    public static IReadOnlyList<(IPAddress Ip, string Kind)> DirectAddresses(IReadOnlyList<NicInfo> nics)
    {
        return nics
            .Where(n => !IPAddress.IsLoopback(n.Address) && !TailscaleInfo.IsTailnetAddress(n.Address) && !IsLinkLocal(n.Address))
            .Select(n => (n.Address, Kind: KindOf(n)))
            .OrderBy(a => a.Kind switch { LinkKinds.Wifi => 0, LinkKinds.Lan => 1, LinkKinds.PcHotspot => 2, LinkKinds.Usb => 3, _ => 4 })
            .ToList();
    }

    public static bool IsLinkLocal(IPAddress ip)
    {
        var b = Normalize(ip).GetAddressBytes();
        return b.Length == 4 && b[0] == 169 && b[1] == 254;
    }
}
