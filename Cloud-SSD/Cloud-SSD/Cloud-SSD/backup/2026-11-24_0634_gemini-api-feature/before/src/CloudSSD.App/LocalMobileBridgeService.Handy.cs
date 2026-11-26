using System.Collections.Concurrent;
using System.Net.Sockets;
using CloudSSD.Core;
using Microsoft.Extensions.Logging;

namespace CloudSSD.App;

/// <summary>A phone's battery and free space as it last reported them (with its long request for questions).</summary>
public sealed record DeviceVitals(int? Battery, bool Charging, long? FreeBytes, long? TotalBytes, DateTimeOffset At);

/// <summary>
/// Small things between the PC and its phones: the phone's battery and storage on its card, ringing a phone that is
/// lost in the house, and passing the clipboard both ways.
/// </summary>
public sealed partial class LocalMobileBridgeService
{
    /// <summary>Longest clipboard text passed in one go.</summary>
    internal const int MaxClipboard = 4000;

    private readonly ConcurrentDictionary<string, DeviceVitals> _vitals = new();

    /// <summary>A phone sent its clipboard to this PC.</summary>
    public event Action<PairedDevice, string>? ClipboardReceived;

    /// <summary>The phone's battery and storage, when it reported them in the last ten minutes.</summary>
    public DeviceVitals? VitalsOf(string deviceId) =>
        _vitals.TryGetValue(deviceId, out var v) && Now() - v.At < TimeSpan.FromMinutes(10) ? v : null;

    /// <summary>Read from the phone's /api/peer/inbox request: battery=0..100, charging=0|1, free and total in bytes.</summary>
    private void NoteVitals(string deviceId, Dictionary<string, string> q)
    {
        if (!q.ContainsKey("battery") && !q.ContainsKey("free")) return;
        int? battery = int.TryParse(q.GetValueOrDefault("battery"), out var b) && b is >= 0 and <= 100 ? b : null;
        long? free = long.TryParse(q.GetValueOrDefault("free"), out var f) && f >= 0 ? f : null;
        long? total = long.TryParse(q.GetValueOrDefault("total"), out var t) && t > 0 ? t : null;
        var before = _vitals.GetValueOrDefault(deviceId);
        var now = new DeviceVitals(battery, q.GetValueOrDefault("charging") == "1", free, total, Now());
        _vitals[deviceId] = now;
        if (before is null || before.Battery != now.Battery || before.Charging != now.Charging) RaiseDevicesChanged();
    }

    /// <summary>Makes the phone ring loudly for a while (even on silent), to find it.</summary>
    public Task<PeerResult> RingDeviceAsync(string deviceId, CancellationToken ct = default) =>
        AskDeviceAsync(deviceId, "phone.ring", new Dictionary<string, string> { ["from"] = Environment.MachineName }, TimeSpan.FromSeconds(30), ct);

    /// <summary>Puts text on the phone's clipboard.</summary>
    public Task<PeerResult> SendClipboardAsync(string deviceId, string text, CancellationToken ct = default) =>
        AskDeviceAsync(deviceId, "clip.set", new Dictionary<string, string>
        {
            ["text"] = text.Length > MaxClipboard ? text[..MaxClipboard] : text,
            ["from"] = Environment.MachineName
        }, TimeSpan.FromSeconds(30), ct);

    /// <summary>POST /api/clip?text=…: the phone's clipboard for this PC.</summary>
    private async Task<bool> TryHandleClipRouteAsync(string method, string path, Dictionary<string, string> q, NetworkStream stream, Caller caller, CancellationToken ct)
    {
        if (!path.Equals("/api/clip", StringComparison.OrdinalIgnoreCase) || method != "POST") return false;
        var text = q.GetValueOrDefault("text") ?? "";
        if (caller.Device is not { } me || text.Length == 0 || text.Length > MaxClipboard)
        {
            await SendJsonAsync(stream, 400, "Bad Request", new { error = $"Send 1 to {MaxClipboard} characters." }, ct);
            return true;
        }
        try { ClipboardReceived?.Invoke(me, text); } catch (Exception e) { _log.LogDebug(e, "ClipboardReceived handler failed"); }
        await SendJsonAsync(stream, 200, "OK", new { ok = true }, ct);
        return true;
    }
}
