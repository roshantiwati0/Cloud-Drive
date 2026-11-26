using System.Net;
using System.Net.NetworkInformation;
using CloudSSD.App;
using CloudSSD.Core;
using Xunit;

namespace CloudSSD.Tests;

/// <summary>Wi-Fi, hotspots (this PC's and the phone's), USB tethering, adb and Tailscale, told apart from made-up network cards.</summary>
public class LinkClassifierTests
{
    private static IPAddress Ip(string s) => IPAddress.Parse(s);

    private static NicInfo Nic(string description, NetworkInterfaceType type, string ip, string? gateway = null) =>
        new(description, type, Ip(ip), 24, gateway is null ? Array.Empty<IPAddress>() : new[] { Ip(gateway) });

    private static readonly NicInfo HomeWifi = Nic("Intel(R) Wi-Fi 6 AX201", NetworkInterfaceType.Wireless80211, "192.168.1.73", "192.168.1.1");
    private static readonly NicInfo PcHotspot = Nic("Microsoft Wi-Fi Direct Virtual Adapter #2", NetworkInterfaceType.Wireless80211, "192.168.137.1");
    private static readonly NicInfo UsbTether = Nic("Remote NDIS based Internet Sharing Device", NetworkInterfaceType.Ethernet, "192.168.42.100", "192.168.42.129");
    private static readonly NicInfo Tailscale = Nic("Tailscale Tunnel", NetworkInterfaceType.Unknown, "100.101.102.103");
    private static readonly NicInfo LinkLocal = Nic("Bluetooth Device (Personal Area Network)", NetworkInterfaceType.Ethernet, "169.254.83.107");
    private static readonly NicInfo[] Nics = { HomeWifi, PcHotspot, UsbTether, Tailscale, LinkLocal };

    [Fact]
    public void Each_link_is_recognised_from_the_card_it_arrived_on()
    {
        Assert.Equal(LinkKinds.Wifi, LinkClassifier.Classify(Ip("192.168.1.73"), Ip("192.168.1.4"), false, Nics));
        Assert.Equal(LinkKinds.PcHotspot, LinkClassifier.Classify(Ip("192.168.137.1"), Ip("192.168.137.55"), false, Nics));
        Assert.Equal(LinkKinds.Usb, LinkClassifier.Classify(Ip("192.168.42.100"), Ip("192.168.42.129"), false, Nics));
        Assert.Equal(LinkKinds.Internet, LinkClassifier.Classify(Ip("100.101.102.103"), Ip("100.64.0.7"), true, Nics));
        Assert.Equal(LinkKinds.Local, LinkClassifier.Classify(IPAddress.Loopback, IPAddress.Loopback, false, Nics));
        Assert.Equal(LinkKinds.Lan, LinkClassifier.Classify(Ip("10.0.0.9"), Ip("10.0.0.2"), false, Nics));     // a card not seen (yet)
        Assert.Equal(LinkKinds.Wifi, LinkClassifier.Classify(Ip("192.168.1.73").MapToIPv6(), Ip("192.168.1.4").MapToIPv6(), false, Nics));
    }

    [Fact]
    public void A_PC_on_the_phones_hotspot_sees_the_phone_as_its_gateway()
    {
        var onPhoneHotspot = Nic("Intel(R) Wi-Fi 6 AX201", NetworkInterfaceType.Wireless80211, "10.12.5.20", "10.12.5.1");
        var nics = new[] { onPhoneHotspot };
        Assert.Equal(LinkKinds.PhoneHotspot, LinkClassifier.Classify(Ip("10.12.5.20"), Ip("10.12.5.1"), false, nics));
        // Another device on that hotspot is just Wi-Fi.
        Assert.Equal(LinkKinds.Wifi, LinkClassifier.Classify(Ip("10.12.5.20"), Ip("10.12.5.33"), false, nics));
        Assert.True(LinkKinds.IsDirect(LinkKinds.PhoneHotspot));
        Assert.False(LinkKinds.IsDirect(LinkKinds.Internet));
    }

    [Fact]
    public void A_phone_app_on_localhost_came_over_adb_a_browser_there_is_this_PC()
    {
        Assert.Equal(LinkKinds.UsbAdb, LocalMobileBridgeService.LinkOf(LinkKinds.Local, new PairedDevice { Kind = "phone" }));
        Assert.Equal(LinkKinds.UsbAdb, LocalMobileBridgeService.LinkOf(LinkKinds.Local, new PairedDevice()));   // old phones have no kind: a phone
        Assert.Equal(LinkKinds.Local, LocalMobileBridgeService.LinkOf(LinkKinds.Local, new PairedDevice { Kind = "laptop" }));
        Assert.Equal(LinkKinds.Local, LocalMobileBridgeService.LinkOf(LinkKinds.Local, null));
        Assert.Equal(LinkKinds.Wifi, LocalMobileBridgeService.LinkOf(LinkKinds.Wifi, new PairedDevice { Kind = "phone" }));
    }

    [Fact]
    public void Direct_addresses_put_the_home_network_first_and_leave_out_Tailscale_and_link_local()
    {
        var list = LinkClassifier.DirectAddresses(Nics);
        Assert.Equal(new[] { "192.168.1.73", "192.168.137.1", "192.168.42.100" }, list.Select(a => a.Ip.ToString()));
        Assert.Equal(new[] { LinkKinds.Wifi, LinkKinds.PcHotspot, LinkKinds.Usb }, list.Select(a => a.Kind));
    }

    [Fact]
    public void The_QR_code_prefers_the_home_network_over_a_hotspot_or_a_cable()
    {
        int home = LocalMobileBridgeService.ScoreAddress(NetworkInterfaceType.Wireless80211, Ip("192.168.1.73"), HomeWifi.Description);
        int hotspot = LocalMobileBridgeService.ScoreAddress(NetworkInterfaceType.Wireless80211, Ip("192.168.137.1"), PcHotspot.Description);
        int cable = LocalMobileBridgeService.ScoreAddress(NetworkInterfaceType.Ethernet, Ip("192.168.42.100"), UsbTether.Description);
        Assert.True(home > hotspot && home > cable, $"{home} {hotspot} {cable}");
        Assert.True(cable > LocalMobileBridgeService.ScoreAddress(NetworkInterfaceType.Wireless80211, Ip("100.100.1.1")));   // Tailscale never wins
    }

    [Fact]
    public Task Status_tells_the_phone_its_link_and_every_direct_address() => BridgeHarness.With(async env =>
    {
        var token = await env.PairNew("Pixel", "inst-link-1", "&kind=phone");
        var st = await env.Json(await env.Get("/api/status", token));
        Assert.Equal(LinkKinds.UsbAdb, st.GetProperty("link").GetString());    // the test talks over localhost, like adb reverse
        var addresses = st.GetProperty("addresses").EnumerateArray().ToList();
        Assert.All(addresses, a => Assert.StartsWith("http://", a.GetProperty("url").GetString()));
        Assert.All(addresses, a => Assert.DoesNotContain("100.", a.GetProperty("url").GetString()![7..11]));
        Assert.Contains("links", st.GetProperty("features").EnumerateArray().Select(f => f.GetString()));
        var dev = env.Host.Settings.MobileBridge.PairedDevices.Single(d => d.Name == "Pixel");
        Assert.Equal(LinkKinds.UsbAdb, dev.LastLink);
    });
}
