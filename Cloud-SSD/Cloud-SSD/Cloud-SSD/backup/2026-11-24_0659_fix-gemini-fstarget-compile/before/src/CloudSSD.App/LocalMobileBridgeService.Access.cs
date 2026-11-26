using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace CloudSSD.App;

/// <summary>
/// Proving to a phone (or laptop) that it reached its own PC before it sends its key anywhere. Any device can copy the
/// PC's public id, so the id alone proves nothing on a café's Wi-Fi; only the real PC knows the hash of the device's key
/// and can answer a fresh challenge with it.
/// </summary>
public sealed partial class LocalMobileBridgeService
{
    /// <summary>At most this many /api/hello answers per address per minute (it needs no key, so it is rate limited).</summary>
    internal const int HelloPerMinute = 20;

    private readonly ConcurrentDictionary<string, (long Window, int Count)> _helloRate = new();

    /// <summary>
    /// HMAC-SHA256 of <c>nonce + "\n" + pcId</c>, keyed with the 32 bytes of SHA-256(device key), as lowercase hex.
    /// The device computes the same from its own key; the key itself never travels.
    /// </summary>
    internal static string HelloProof(string tokenHashHex, string nonce, string pcId)
    {
        var key = Convert.FromHexString(tokenHashHex);
        var mac = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(nonce + "\n" + pcId));
        return Convert.ToHexString(mac).ToLowerInvariant();
    }

    /// <summary>A nonce from the device: 16–64 hex characters.</summary>
    internal static bool IsValidNonce(string? nonce) =>
        nonce is { Length: >= 16 and <= 64 } && nonce.All(Uri.IsHexDigit);

    private bool HelloAllowed(string ip)
    {
        long window = Now().ToUnixTimeSeconds() / 60;
        var entry = _helloRate.AddOrUpdate(ip, _ => (window, 1), (_, e) => e.Window == window ? (window, e.Count + 1) : (window, 1));
        if (_helloRate.Count > 1024)
            foreach (var kv in _helloRate)
                if (kv.Value.Window != window) _helloRate.TryRemove(kv.Key, out _);
        return entry.Count <= HelloPerMinute;
    }

    /// <summary>
    /// <c>GET /api/hello?nonce=&lt;hex&gt;&amp;d=&lt;deviceId|legacy&gt;</c>, no key needed. Answers the PC's public id and a
    /// proof for that device. An unknown or removed device gets a proof that looks the same but never matches, so the
    /// answer does not tell which device ids exist. A blocked device gets its real proof (it keeps its key).
    /// </summary>
    private async Task HandleHelloAsync(Dictionary<string, string> q, NetworkStream stream, string clientIp, CancellationToken ct)
    {
        if (!HelloAllowed(clientIp))
        {
            await SendJsonAsync(stream, 429, "Too Many Requests", new { error = "Too many requests. Try again in a minute." }, ct);
            return;
        }
        var nonce = q.GetValueOrDefault("nonce");
        if (!IsValidNonce(nonce))
        {
            await SendJsonAsync(stream, 400, "Bad Request", new { error = "nonce must be 16-64 hex characters" }, ct);
            return;
        }
        var bridge = _host.Settings.MobileBridge;
        string pcId = bridge.PcId;
        string device = q.GetValueOrDefault("d") ?? "";
        string? hash = null;
        if (device == "legacy")
        {
            if (bridge.LegacyTokenAccepted == true) hash = HashToken(PairingToken);
        }
        else if (device.Length is > 0 and <= 64)
        {
            lock (_devicesLock)
            {
                // A blocked device still gets its real proof: it is only blocked, not removed, so it must recognise its
                // PC and keep its key for when it is unblocked. Without the key the proof cannot be told from a decoy.
                var d = bridge.PairedDevices.FirstOrDefault(x => x.Id == device);
                if (d is { TokenHash: { Length: 64 } h }) hash = h;
            }
        }
        // Unknown device: a stable, unguessable stand-in key, so the reply has the same shape and timing.
        hash ??= Convert.ToHexString(HMACSHA256.HashData(MediaKey(), Encoding.UTF8.GetBytes("hello-decoy\n" + device))).ToLowerInvariant();
        await SendJsonAsync(stream, 200, "OK", new
        {
            pcId,
            pcName = Environment.MachineName,
            version = AppVersion,
            port = Port,
            features = Features(),
            proof = HelloProof(hash, nonce!, pcId)
        }, ct);
    }
}
