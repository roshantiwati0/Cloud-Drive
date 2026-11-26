using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace CloudSSD.App;

/// <summary>
/// What a paired device needs to wake this PC later (Wake-on-LAN): the network card's MAC address and the broadcast
/// address of its network. Wired cards usually can wake a sleeping or switched-off PC (when it is on in the BIOS and
/// in the card's settings); Wi-Fi cards seldom can, so the device is told which kind it is.
/// </summary>
public sealed record WakeInfo(string Mac, string Broadcast, string Ip, bool Wired);

public static class WakeOnLan
{
    private static readonly object Lock = new();
    private static (DateTime At, string Preferred, WakeInfo? Info)? _cache;

    /// <summary>The card to wake this PC through: a wired one on a network with a router first, else the card of the
    /// PC's home address. Read again at most every 30 seconds (devices ask for it on every status call).</summary>
    public static WakeInfo? Current(string preferredIp)
    {
        lock (Lock)
        {
            if (_cache is { } c && c.Preferred == preferredIp && DateTime.UtcNow - c.At < TimeSpan.FromSeconds(30)) return c.Info;
        }
        WakeInfo? info = null;
        try
        {
            info = Pick(Cards(), preferredIp);
        }
        catch
        {
            // No card list (rare): nothing to offer.
        }
        lock (Lock) _cache = (DateTime.UtcNow, preferredIp, info);
        return info;
    }

    /// <summary>A network card as far as waking goes.</summary>
    public sealed record Card(string Mac, IPAddress Ip, int PrefixLength, bool Wired, bool HasGateway);

    /// <summary>Wired with a router beats the card that has the PC's home address; tunnels and virtual cards never count.</summary>
    public static WakeInfo? Pick(IReadOnlyList<Card> cards, string preferredIp)
    {
        var card = cards.FirstOrDefault(c => c.Wired && c.HasGateway)
                   ?? cards.FirstOrDefault(c => c.Ip.ToString() == preferredIp)
                   ?? cards.FirstOrDefault(c => c.HasGateway);
        return card is null ? null : new WakeInfo(card.Mac, Broadcast(card.Ip, card.PrefixLength).ToString(), card.Ip.ToString(), card.Wired);
    }

    public static IReadOnlyList<Card> Cards()
    {
        var list = new List<Card>();
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up) continue;
            var type = ni.NetworkInterfaceType;
            if (type is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel or NetworkInterfaceType.Ppp) continue;
            var desc = ni.Description ?? "";
            // Tailscale, VPNs and Hyper-V switches cannot carry a wake packet to the real card.
            if (desc.Contains("Tailscale", StringComparison.OrdinalIgnoreCase) || desc.Contains("Virtual", StringComparison.OrdinalIgnoreCase)
                || desc.Contains("VPN", StringComparison.OrdinalIgnoreCase) || desc.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase)) continue;
            var mac = ni.GetPhysicalAddress().GetAddressBytes();
            if (mac.Length != 6 || mac.All(b => b == 0)) continue;
            var props = ni.GetIPProperties();
            bool gateway = props.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));
            foreach (var ua in props.UnicastAddresses)
            {
                if (ua.Address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(ua.Address)) continue;
                if (LinkClassifier.IsLinkLocal(ua.Address)) continue;
                list.Add(new Card(FormatMac(mac), ua.Address, ua.PrefixLength, type != NetworkInterfaceType.Wireless80211, gateway));
            }
        }
        return list;
    }

    /// <summary>192.168.1.20/24 → 192.168.1.255.</summary>
    public static IPAddress Broadcast(IPAddress ip, int prefixLength)
    {
        var bytes = ip.GetAddressBytes();
        uint addr = (uint)(bytes[0] << 24 | bytes[1] << 16 | bytes[2] << 8 | bytes[3]);
        int bits = Math.Clamp(prefixLength, 0, 32);
        uint mask = bits == 0 ? 0 : uint.MaxValue << (32 - bits);
        uint b = addr | ~mask;
        return new IPAddress(new[] { (byte)(b >> 24), (byte)(b >> 16), (byte)(b >> 8), (byte)b });
    }

    public static string FormatMac(byte[] mac) => string.Join(":", mac.Select(b => b.ToString("X2")));

    /// <summary>The "magic packet": six 0xFF bytes, then the MAC 16 times.</summary>
    public static byte[] MagicPacket(string mac)
    {
        var bytes = ParseMac(mac) ?? throw new ArgumentException("Not a MAC address: " + mac, nameof(mac));
        var packet = new byte[6 + 16 * 6];
        for (int i = 0; i < 6; i++) packet[i] = 0xFF;
        for (int i = 0; i < 16; i++) Array.Copy(bytes, 0, packet, 6 + i * 6, 6);
        return packet;
    }

    /// <summary>"AA:BB:CC:DD:EE:FF", "aa-bb-…" or "aabbccddeeff"; null when it is not six bytes.</summary>
    public static byte[]? ParseMac(string? mac)
    {
        var hex = new string((mac ?? "").Where(Uri.IsHexDigit).ToArray());
        if (hex.Length != 12 || (mac ?? "").Count(char.IsLetterOrDigit) != 12) return null;
        return Enumerable.Range(0, 6).Select(i => Convert.ToByte(hex.Substring(i * 2, 2), 16)).ToArray();
    }

    /// <summary>The MAC inside a magic packet, or null when the bytes are not one.</summary>
    public static string? MacOf(byte[] packet)
    {
        if (packet.Length < 102 || packet.Take(6).Any(b => b != 0xFF)) return null;
        var mac = packet.Skip(6).Take(6).ToArray();
        for (int i = 1; i < 16; i++)
            if (!packet.Skip(6 + i * 6).Take(6).SequenceEqual(mac)) return null;
        return FormatMac(mac);
    }
}
