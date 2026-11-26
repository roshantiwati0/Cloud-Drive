namespace CloudSSD.App;

/// <summary>What kind of device is paired with this PC.</summary>
public enum DeviceKind { Phone, Tablet, Laptop, Desktop, Browser }

/// <summary>What a device is doing on this PC right now, as far as the PC can see it.</summary>
public enum DeviceActivityKind { Browsing, Photos, Upload, Download, Stream, Backup, Game, Space, Hud }

/// <summary>A device's current activity: the file and a percentage for transfers, nothing made up.</summary>
public sealed record DeviceActivity(DeviceActivityKind Kind, string? File, int? Percent, DateTimeOffset Since);

/// <summary>One paired device as the Devices page shows it.</summary>
public sealed record DeviceSnapshot(
    string Id, string Name, DeviceKind Kind, string? Platform, string? AppVersion, bool Online, DateTimeOffset LastSeen,
    DateTimeOffset PairedAt, string Link, string Ip, DeviceActivity? Activity, bool Blocked, bool Legacy, string? PeerPcId,
    DeviceVitals? Vitals = null,
    // The device's app is waiting for questions from this PC right now (browse its files, ring it, send it text).
    bool Listening = false,
    // A guest, removed at this time (it paired with a one-time guest code).
    DateTimeOffset? GuestUntil = null);

/// <summary>A device paired just now, or came back after being away.</summary>
public sealed record DeviceConnectedEvent(DeviceSnapshot Device, bool JustPaired);

public static class DeviceKinds
{
    /// <summary>The kind as stored ("phone", "laptop"…), or null when <paramref name="kind"/> is not one we know.</summary>
    public static string? Normalize(string? kind) => kind?.Trim().ToLowerInvariant() switch
    {
        "phone" => "phone",
        "tablet" => "tablet",
        "laptop" => "laptop",
        "desktop" or "pc" => "desktop",
        "browser" => "browser",
        _ => null
    };

    /// <summary>
    /// The kind a device sent wins; otherwise it is guessed so today's phone app and web page keep working:
    /// the web page calls itself "Phone browser", an iPad/tablet user agent is a tablet, anything else a phone.
    /// </summary>
    public static string Infer(string? kind, string? name, string? userAgent)
    {
        if (Normalize(kind) is { } k) return k;
        if (string.Equals(name?.Trim(), "Phone browser", StringComparison.OrdinalIgnoreCase)) return "browser";
        if (userAgent is { } ua && (ua.Contains("iPad", StringComparison.OrdinalIgnoreCase) || ua.Contains("Tablet", StringComparison.OrdinalIgnoreCase))) return "tablet";
        return "phone";
    }

    public static DeviceKind Of(string? kind) => Normalize(kind) switch
    {
        "tablet" => DeviceKind.Tablet,
        "laptop" => DeviceKind.Laptop,
        "desktop" => DeviceKind.Desktop,
        "browser" => DeviceKind.Browser,
        _ => DeviceKind.Phone
    };

    public static string Name(DeviceKind k) => k.ToString().ToLowerInvariant();
}

/// <summary>
/// Who is online and what they are doing. Kept in memory only (no I/O), so it is cheap on every request and easy to test.
/// A device is online while it was seen within <see cref="OnlineWindow"/> or has a transfer running.
/// </summary>
internal sealed class DevicePresenceTracker
{
    public static readonly TimeSpan OnlineWindow = TimeSpan.FromSeconds(40);
    /// <summary>Away at least this long before coming back counts as "connected" (a note on the PC).</summary>
    public static readonly TimeSpan ComebackAfter = TimeSpan.FromMinutes(2);
    /// <summary>At most one "connected" note per device in this time.</summary>
    public static readonly TimeSpan NotifyCooldown = TimeSpan.FromMinutes(10);
    /// <summary>The last kind of request counts as the activity this long.</summary>
    public static readonly TimeSpan ActivityTtl = TimeSpan.FromSeconds(20);

    private readonly Func<DateTimeOffset> _now;
    private readonly object _lock = new();
    private readonly Dictionary<string, Entry> _entries = new();

    public DevicePresenceTracker(Func<DateTimeOffset> now) => _now = now;

    private sealed class Entry
    {
        public DateTimeOffset LastSeen;
        public string Link = "lan";
        public string Ip = "";
        public DeviceActivityKind? RouteActivity;
        public DateTimeOffset RouteActivityAt;
        public readonly List<Transfer> Transfers = new();
        public DateTimeOffset? LastNotified;
        public bool WasOnline;
        public CancellationTokenSource Kick = new();
    }

    /// <summary>A transfer in progress for one device; dispose it when done.</summary>
    public sealed class Transfer : IDisposable
    {
        private readonly DevicePresenceTracker _owner;
        private readonly string _id;
        private long _done;
        public DeviceActivityKind Kind { get; }
        public string File { get; }
        public long Total { get; }
        public long Already { get; }
        public DateTimeOffset Since { get; }
        public event Action? Ended;

        internal Transfer(DevicePresenceTracker owner, string id, DeviceActivityKind kind, string file, long total, long already, DateTimeOffset since)
        {
            _owner = owner; _id = id; Kind = kind; File = file; Total = total; Already = already; Since = since;
        }

        public void Add(long bytes) => Interlocked.Add(ref _done, bytes);
        public long Done => Interlocked.Read(ref _done);

        /// <summary>0–100 when the size is known, else null.</summary>
        public int? Percent => Total > 0 ? (int)Math.Clamp((Already + Done) * 100 / Total, 0, 100) : null;

        public void Dispose()
        {
            if (_owner.End(_id, this)) Ended?.Invoke();
        }
    }

    private Entry Get(string id, DateTimeOffset persistedLastSeen)
    {
        if (!_entries.TryGetValue(id, out var e))
        {
            // Seeded from the saved time, so restarting the PC app does not make every device "come back".
            e = new Entry { LastSeen = persistedLastSeen };
            e.WasOnline = _now() - persistedLastSeen <= OnlineWindow;
            _entries[id] = e;
        }
        return e;
    }

    /// <summary>
    /// A request from the device. Returns whether it just came online (the list changed) and whether that is a
    /// comeback worth a note (away at least <see cref="ComebackAfter"/>, not noted in the last <see cref="NotifyCooldown"/>).
    /// </summary>
    public (bool CameOnline, bool Notify) Seen(string id, DateTimeOffset persistedLastSeen, string link, string ip, string path, string method)
    {
        var now = _now();
        lock (_lock)
        {
            var e = Get(id, persistedLastSeen);
            bool away = now - e.LastSeen >= ComebackAfter;
            bool wasOnline = IsOnlineLocked(e, now);
            e.LastSeen = now;
            e.Link = link;
            e.Ip = ip;
            if (FromRoute(path, method) is { } act) { e.RouteActivity = act; e.RouteActivityAt = now; }
            bool notify = away && (e.LastNotified is not { } n || now - n >= NotifyCooldown);
            if (notify) e.LastNotified = now;
            e.WasOnline = true;
            return (!wasOnline, notify);
        }
    }

    /// <summary>Marks a device as just paired (online now, and no "came back" note right after the "paired" one).</summary>
    public void Paired(string id, string link, string ip)
    {
        var now = _now();
        lock (_lock)
        {
            var e = Get(id, now);
            e.LastSeen = now; e.Link = link; e.Ip = ip; e.LastNotified = now; e.WasOnline = true;
        }
    }

    public Transfer BeginTransfer(string id, DateTimeOffset persistedLastSeen, DeviceActivityKind kind, string file, long total, long already = 0)
    {
        var now = _now();
        lock (_lock)
        {
            var e = Get(id, persistedLastSeen);
            var t = new Transfer(this, id, kind, file, total, already, now);
            e.Transfers.Add(t);
            e.WasOnline = true;
            return t;
        }
    }

    private bool End(string id, Transfer t)
    {
        lock (_lock)
        {
            if (!_entries.TryGetValue(id, out var e) || !e.Transfers.Remove(t)) return false;
            e.LastSeen = _now();   // a long download ends: it was online all along
            return true;
        }
    }

    private bool IsOnlineLocked(Entry e, DateTimeOffset now) => e.Transfers.Count > 0 || now - e.LastSeen <= OnlineWindow;

    public bool IsOnline(string id, DateTimeOffset persistedLastSeen)
    {
        var now = _now();
        lock (_lock)
            return _entries.TryGetValue(id, out var e) ? IsOnlineLocked(e, now) : now - persistedLastSeen <= OnlineWindow;
    }

    /// <summary>The last time the device was seen (in memory, newer than the saved time while it is active).</summary>
    public DateTimeOffset LastSeen(string id, DateTimeOffset persistedLastSeen)
    {
        lock (_lock) return _entries.TryGetValue(id, out var e) && e.LastSeen > persistedLastSeen ? e.LastSeen : persistedLastSeen;
    }

    public string? LinkOf(string id)
    {
        lock (_lock) return _entries.TryGetValue(id, out var e) ? e.Link : null;
    }

    /// <summary>A transfer in progress wins; otherwise the kind of its last request, while recent.</summary>
    public DeviceActivity? ActivityOf(string id)
    {
        var now = _now();
        lock (_lock)
        {
            if (!_entries.TryGetValue(id, out var e)) return null;
            if (e.Transfers.Count > 0)
            {
                var t = e.Transfers[^1];
                return new DeviceActivity(t.Kind, t.File, t.Percent, t.Since);
            }
            if (e.RouteActivity is { } k && now - e.RouteActivityAt <= ActivityTtl && now - e.LastSeen <= OnlineWindow)
                return new DeviceActivity(k, null, null, e.RouteActivityAt);
            return null;
        }
    }

    /// <summary>Ids whose online state changed since the last sweep (or since they were last seen).</summary>
    public IReadOnlyList<string> Sweep()
    {
        var now = _now();
        var flipped = new List<string>();
        lock (_lock)
        {
            foreach (var (id, e) in _entries)
            {
                bool online = IsOnlineLocked(e, now);
                if (online != e.WasOnline) { e.WasOnline = online; flipped.Add(id); }
            }
        }
        return flipped;
    }

    /// <summary>Cancelled when the device is blocked or removed: its running downloads, uploads and streams stop.</summary>
    public CancellationToken KickToken(string id)
    {
        lock (_lock) return Get(id, DateTimeOffset.MinValue).Kick.Token;
    }

    public void Kick(string id)
    {
        CancellationTokenSource? old = null;
        lock (_lock)
        {
            if (_entries.TryGetValue(id, out var e)) { old = e.Kick; e.Kick = new CancellationTokenSource(); }
        }
        if (old != null) { try { old.Cancel(); } catch (AggregateException) { } old.Dispose(); }
    }

    public void Forget(string id)
    {
        Kick(id);
        lock (_lock) _entries.Remove(id);
    }

    /// <summary>What a request says about what the device is doing; null keeps the current activity (polling).</summary>
    internal static DeviceActivityKind? FromRoute(string path, string method)
    {
        var p = path.ToLowerInvariant();
        if (p == "/api/fs/thumb") return DeviceActivityKind.Photos;
        if (p is "/api/fs/list" or "/api/files" or "/api/fs/search" or "/api/fs/stat" or "/api/drives" or "/api/fs/roots" or "/api/preview") return DeviceActivityKind.Browsing;
        if (p.StartsWith("/api/backup/")) return DeviceActivityKind.Backup;
        // /api/space/live is the phone's Home tiles (PC RAM and disk) refreshing by themselves, not someone on Space & RAM.
        if (p.StartsWith("/api/space/") && !p.StartsWith("/api/space/phone-") && p != "/api/space/live") return DeviceActivityKind.Space;
        if (p is "/api/hud" or "/api/remote") return DeviceActivityKind.Hud;
        if (p.StartsWith("/api/game/")) return DeviceActivityKind.Game;
        return null;
    }
}
