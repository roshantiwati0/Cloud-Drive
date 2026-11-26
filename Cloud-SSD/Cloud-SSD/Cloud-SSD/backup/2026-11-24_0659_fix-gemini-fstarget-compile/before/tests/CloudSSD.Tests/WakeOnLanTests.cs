using System.Net;
using System.Text.Json;
using CloudSSD.App;
using Xunit;

namespace CloudSSD.Tests;

/// <summary>Waking a sleeping PC from a paired device (Wake-on-LAN).</summary>
public class WakeOnLanTests
{
    [Theory]
    [InlineData("192.168.1.20", 24, "192.168.1.255")]
    [InlineData("10.0.0.5", 8, "10.255.255.255")]
    [InlineData("172.16.5.4", 20, "172.16.15.255")]
    [InlineData("192.168.137.1", 32, "192.168.137.1")]
    public void The_broadcast_address_covers_the_network(string ip, int prefix, string expected) =>
        Assert.Equal(expected, WakeOnLan.Broadcast(IPAddress.Parse(ip), prefix).ToString());

    [Fact]
    public void The_magic_packet_is_six_FF_then_the_MAC_16_times()
    {
        var packet = WakeOnLan.MagicPacket("a4-bb-6d-01-02-0f");
        Assert.Equal(102, packet.Length);
        Assert.All(packet.Take(6), b => Assert.Equal(0xFF, b));
        Assert.Equal(new byte[] { 0xA4, 0xBB, 0x6D, 0x01, 0x02, 0x0F }, packet.Skip(96).ToArray());
        Assert.Equal("A4:BB:6D:01:02:0F", WakeOnLan.MacOf(packet));
        // Not a magic packet: anything else on that port.
        Assert.Null(WakeOnLan.MacOf(new byte[102]));
        packet[50] ^= 1;
        Assert.Null(WakeOnLan.MacOf(packet));
    }

    [Theory]
    [InlineData("A4:BB:6D:01:02:0F", true)]
    [InlineData("a4bb6d01020f", true)]
    [InlineData("A4-BB-6D-01-02", false)]
    [InlineData("ZZ:BB:6D:01:02:0F", false)]
    [InlineData("", false)]
    public void Only_six_byte_MACs_are_read(string mac, bool ok) => Assert.Equal(ok, WakeOnLan.ParseMac(mac) is not null);

    [Fact]
    public void A_wired_card_wins_over_the_Wi_Fi_one_that_has_the_home_address()
    {
        var wifi = new WakeOnLan.Card("11:11:11:11:11:11", IPAddress.Parse("192.168.1.20"), 24, Wired: false, HasGateway: true);
        var lan = new WakeOnLan.Card("22:22:22:22:22:22", IPAddress.Parse("192.168.1.9"), 24, Wired: true, HasGateway: true);
        var island = new WakeOnLan.Card("33:33:33:33:33:33", IPAddress.Parse("192.168.56.1"), 24, Wired: true, HasGateway: false);

        Assert.Equal(new WakeInfo("22:22:22:22:22:22", "192.168.1.255", "192.168.1.9", true), WakeOnLan.Pick([wifi, lan, island], "192.168.1.20"));
        // Wi-Fi only: that card, and the device is told it is Wi-Fi.
        Assert.Equal(new WakeInfo("11:11:11:11:11:11", "192.168.1.255", "192.168.1.20", false), WakeOnLan.Pick([island, wifi], "192.168.1.20"));
        Assert.Null(WakeOnLan.Pick([], "192.168.1.20"));
    }

    [Fact]
    public Task Only_paired_devices_learn_how_to_wake_the_PC() => BridgeHarness.With(async env =>
    {
        var token = await env.PairNew("Pixel", "inst-wol-01", "&kind=phone");
        var mine = await env.Json(await env.Get("/api/status", token));
        Assert.Contains("wol", mine.GetProperty("features").EnumerateArray().Select(f => f.GetString()));
        var expected = WakeOnLan.Current(LocalMobileBridgeService.GetPreferredLocalIp());
        var wol = mine.GetProperty("wol");
        if (expected is null)
        {
            Assert.Equal(JsonValueKind.Null, wol.ValueKind);           // a machine without a usable card
        }
        else
        {
            Assert.Equal(expected.Mac, wol.GetProperty("mac").GetString());
            Assert.Equal(expected.Broadcast, wol.GetProperty("broadcast").GetString());
            Assert.Equal(expected.Wired, wol.GetProperty("wired").GetBoolean());
        }

        var stranger = await env.Json(await env.Get("/api/status", null));
        Assert.False(stranger.TryGetProperty("wol", out _));
    });
}
