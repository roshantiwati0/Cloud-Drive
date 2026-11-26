using System.Text.Json;

namespace CloudSSD.App;

/// <summary>One line of the access log: when a device did (or was refused) what, and on which file or folder.</summary>
public sealed record AccessLogEntry(DateTimeOffset At, string Category, string Route, string? Target, bool Allowed);

/// <summary>
/// Who opened what on this PC: one small file per device per day (JSON lines), kept 30 days. The same thing done again
/// within a minute (a video asking for its next piece, a folder refreshed) is written once.
/// </summary>
public sealed class AccessLog
{
    public static readonly TimeSpan KeepFor = TimeSpan.FromDays(30);
    private static readonly TimeSpan Repeat = TimeSpan.FromMinutes(1);

    private readonly string _dir;
    private readonly Func<DateTimeOffset> _now;
    private readonly object _lock = new();
    private readonly Dictionary<string, DateTimeOffset> _recent = new();
    private DateOnly _prunedOn;

    public AccessLog(string dir, Func<DateTimeOffset> now)
    {
        _dir = dir;
        _now = now;
    }

    private static bool IsSafeId(string id) => id.Length is > 0 and <= 64 && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    private string DeviceDir(string deviceId) => Path.Combine(_dir, deviceId);

    public void Write(string deviceId, string category, string route, string? target, bool allowed)
    {
        if (!IsSafeId(deviceId)) return;
        var now = _now();
        if (target is { Length: > 300 }) target = target[..300];
        var key = $"{deviceId}|{route}|{target}|{allowed}";
        lock (_lock)
        {
            if (_recent.TryGetValue(key, out var last) && now - last < Repeat) return;
            _recent[key] = now;
            if (_recent.Count > 2000)
                foreach (var old in _recent.Where(kv => now - kv.Value > Repeat).Select(kv => kv.Key).ToList()) _recent.Remove(old);
            try
            {
                var dir = Directory.CreateDirectory(DeviceDir(deviceId)).FullName;
                var line = JsonSerializer.Serialize(new AccessLogEntry(now, category, route, target, allowed), RemoteJson) + "\n";
                File.AppendAllText(Path.Combine(dir, now.ToLocalTime().ToString("yyyy-MM-dd") + ".jsonl"), line);
                PruneOnceADay(now);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    private static readonly JsonSerializerOptions RemoteJson = new(JsonSerializerDefaults.Web);

    private void PruneOnceADay(DateTimeOffset now)
    {
        var today = DateOnly.FromDateTime(now.LocalDateTime);
        if (_prunedOn == today) return;
        _prunedOn = today;
        var cutoff = today.AddDays(-(int)KeepFor.TotalDays);
        foreach (var f in Directory.EnumerateFiles(_dir, "*.jsonl", SearchOption.AllDirectories))
        {
            if (DateOnly.TryParseExact(Path.GetFileNameWithoutExtension(f), "yyyy-MM-dd", out var day) && day < cutoff)
            {
                try { File.Delete(f); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
        }
    }

    /// <summary>The newest entries of a device first (at most <paramref name="max"/>).</summary>
    public IReadOnlyList<AccessLogEntry> Read(string deviceId, int max = 100)
    {
        var list = new List<AccessLogEntry>();
        if (!IsSafeId(deviceId)) return list;
        lock (_lock)
        {
            var dir = DeviceDir(deviceId);
            if (!Directory.Exists(dir)) return list;
            foreach (var f in Directory.EnumerateFiles(dir, "*.jsonl").OrderByDescending(f => f, StringComparer.Ordinal))
            {
                string[] lines;
                try { lines = File.ReadAllLines(f); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
                for (int i = lines.Length - 1; i >= 0 && list.Count < max; i--)
                {
                    try { if (JsonSerializer.Deserialize<AccessLogEntry>(lines[i], RemoteJson) is { } e) list.Add(e); }
                    catch (JsonException) { }
                }
                if (list.Count >= max) break;
            }
        }
        return list;
    }

    /// <summary>A forgotten device's log goes with it.</summary>
    public void Forget(string deviceId)
    {
        if (!IsSafeId(deviceId)) return;
        lock (_lock)
        {
            try { if (Directory.Exists(DeviceDir(deviceId))) Directory.Delete(DeviceDir(deviceId), true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }
}
