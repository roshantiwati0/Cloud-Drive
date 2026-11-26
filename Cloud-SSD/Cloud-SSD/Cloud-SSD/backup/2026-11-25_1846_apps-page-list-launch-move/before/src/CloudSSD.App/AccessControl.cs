using CloudSSD.Core;

namespace CloudSSD.App;

/// <summary>What a paired device may do on this PC, one category per PC-wide switch (Phone → Permissions).</summary>
public static class AccessCategories
{
    public const string Browse = "browse", Stream = "stream", Upload = "upload", Manage = "manage",
        Hud = "hud", Control = "control", Space = "space", Game = "game", Relay = "relay";

    public static readonly IReadOnlyList<string> All = [Browse, Stream, Upload, Manage, Hud, Control, Space, Game, Relay];

    public static bool IsKnown(string? c) => c != null && All.Contains(c);

    /// <summary>What the category lets a device do, in words for the screens.</summary>
    public static string Describe(string c) => c switch
    {
        Browse => "Browse the drives and folders",
        Stream => "Open, play and download files",
        Upload => "Send files and photo backups to this PC",
        Manage => "Change files: new folders, rename, move, delete",
        Hud => "Gaming HUD and the PC remote",
        Control => "Control this PC: drives, clean-up, game saves",
        Space => "See Space & RAM",
        Game => "Play PC games on the device",
        Relay => "See phones' files through this PC",
        _ => c
    };
}

public enum AccessMode { Always, Hour, Session }

/// <summary>A device asking the PC for something it may not do yet. Answered at the PC within two minutes.</summary>
public sealed class AccessRequest
{
    public required string Id { get; init; }
    public required string DeviceId { get; init; }
    public required string DeviceName { get; init; }
    public string? DeviceKind { get; init; }
    public required IReadOnlyList<string> Categories { get; init; }
    public string? Reason { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    /// <summary>pending | allowed | denied | expired | cancelled</summary>
    public string State { get; internal set; } = "pending";
    /// <summary>How it was allowed: always | hour | session.</summary>
    public string? Mode { get; internal set; }
}

/// <summary>
/// Per-device permissions on top of the PC-wide switches. A device paired before them (no grants saved) may do what the
/// switches allow, as it always could; a new device gets what the PC chose for new devices. Anything else it asks for,
/// and someone at the PC answers: always, for an hour, until the app restarts, or no. Asking is limited (one question at a
/// time, six per ten minutes, a half-hour pause after three refusals) so a device cannot pester the PC.
/// </summary>
public sealed class AccessControl
{
    public static readonly TimeSpan RequestLifetime = TimeSpan.FromMinutes(2);
    public const int MaxRequestsPerWindow = 6;
    public static readonly TimeSpan RequestWindow = TimeSpan.FromMinutes(10);
    public const int RefusalsBeforePause = 3;
    public static readonly TimeSpan PauseAfterRefusals = TimeSpan.FromMinutes(30);

    private readonly Func<DateTimeOffset> _now;
    private readonly object _devicesLock;
    private readonly Func<string, PairedDevice?> _find;
    private readonly Action _save;
    private readonly Action<string> _kick;
    private readonly object _lock = new();
    private readonly Dictionary<string, AccessRequest> _requests = new();
    private readonly Dictionary<string, List<DateTimeOffset>> _asked = new();
    private readonly Dictionary<string, (int Count, DateTimeOffset Last)> _refusals = new();

    /// <summary>This run of the app: "until the app restarts" grants belong to it.</summary>
    public string SessionId { get; } = Guid.NewGuid().ToString("N");

    /// <summary>A device asked; someone at the PC should answer (raised on the request's thread).</summary>
    public event Action<AccessRequest>? RequestCreated;
    /// <summary>A request was answered, ran out of time or was taken back.</summary>
    public event Action<AccessRequest>? RequestEnded;
    /// <summary>A device's permissions changed.</summary>
    public event Action<string>? GrantsChanged;

    public AccessControl(Func<DateTimeOffset> now, object devicesLock, Func<string, PairedDevice?> find, Action save, Action<string> kick)
    {
        _now = now;
        _devicesLock = devicesLock;
        _find = find;
        _save = save;
        _kick = kick;
    }

    /// <summary>What a newly paired device gets: the PC's choice for new devices, else every category, always.</summary>
    public static List<AccessGrant> Defaults(MobileBridgeSettings s, DateTimeOffset now) =>
        (s.NewDeviceDefaults ?? AccessCategories.All.ToList()).Where(AccessCategories.IsKnown).Distinct()
            .Select(c => new AccessGrant { Category = c, Mode = "always", GrantedAt = now }).ToList();

    private static bool IsValid(AccessGrant g, DateTimeOffset now, string session) => g.Mode switch
    {
        "always" => true,
        "timed" => g.ExpiresAt is { } at && at > now,
        "session" => g.Session == session,
        _ => false
    };

    /// <summary>True when the device may do this (the PC-wide switch is checked separately and wins).</summary>
    public bool Has(PairedDevice d, string category)
    {
        var grants = d.Grants;
        if (grants is null) return true;   // paired before per-device permissions: as before
        var now = _now();
        return grants.Any(g => g.Category == category && IsValid(g, now, SessionId));
    }

    /// <summary>The device's grants that count now (all categories, "always", for a device from before).</summary>
    public IReadOnlyList<AccessGrant> Current(PairedDevice d)
    {
        var grants = d.Grants;
        if (grants is null) return AccessCategories.All.Select(c => new AccessGrant { Category = c, Mode = "always" }).ToList();
        var now = _now();
        return grants.Where(g => IsValid(g, now, SessionId)).ToList();
    }

    // ---- asking -------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The device asks for categories it lacks. Returns the request (the one already waiting, if any), or why not:
    /// a number of seconds to wait (too many questions, or refused three times), or an error.
    /// </summary>
    public (AccessRequest? Request, int RetryAfterSeconds, string? Error) Ask(PairedDevice d, IEnumerable<string> categories, string? reason)
    {
        var wanted = categories.Where(AccessCategories.IsKnown).Distinct().Where(c => !Has(d, c)).ToList();
        if (wanted.Count == 0) return (null, 0, "It may already do that.");
        var now = _now();
        AccessRequest req;
        lock (_lock)
        {
            ExpireLocked(now);
            var waiting = _requests.Values.FirstOrDefault(r => r.DeviceId == d.Id && r.State == "pending");
            if (waiting != null) return (waiting, 0, null);
            if (_refusals.TryGetValue(d.Id, out var refused) && refused.Count >= RefusalsBeforePause && now - refused.Last < PauseAfterRefusals)
                return (null, (int)Math.Ceiling((PauseAfterRefusals - (now - refused.Last)).TotalSeconds), "The PC said no three times. Ask again later.");
            var asked = _asked.TryGetValue(d.Id, out var a) ? a : _asked[d.Id] = new List<DateTimeOffset>();
            asked.RemoveAll(t => now - t > RequestWindow);
            if (asked.Count >= MaxRequestsPerWindow)
                return (null, (int)Math.Ceiling((RequestWindow - (now - asked[0])).TotalSeconds), "Too many questions. Ask again in a few minutes.");
            asked.Add(now);
            req = new AccessRequest
            {
                Id = Guid.NewGuid().ToString("N")[..12], DeviceId = d.Id, DeviceName = d.Name, DeviceKind = d.Kind,
                Categories = wanted, Reason = Clean(reason), CreatedAt = now, ExpiresAt = now + RequestLifetime
            };
            _requests[req.Id] = req;
            foreach (var old in _requests.Values.Where(r => r.State != "pending" && now - r.ExpiresAt > TimeSpan.FromMinutes(10)).Select(r => r.Id).ToList())
                _requests.Remove(old);
        }
        RequestCreated?.Invoke(req);
        return (req, 0, null);
    }

    private static string? Clean(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var c = new string(s.Where(ch => !char.IsControl(ch)).ToArray()).Trim();
        return c.Length == 0 ? null : c[..Math.Min(120, c.Length)];
    }

    /// <summary>The device's request that is waiting (or the last one, while it is fresh), for the device to look at.</summary>
    public AccessRequest? Latest(string deviceId)
    {
        lock (_lock)
        {
            ExpireLocked(_now());
            return _requests.Values.Where(r => r.DeviceId == deviceId).OrderByDescending(r => r.CreatedAt).FirstOrDefault();
        }
    }

    public AccessRequest? Get(string id)
    {
        lock (_lock)
        {
            ExpireLocked(_now());
            return _requests.GetValueOrDefault(id);
        }
    }

    /// <summary>The requests waiting for an answer, oldest first.</summary>
    public IReadOnlyList<AccessRequest> Pending()
    {
        lock (_lock)
        {
            ExpireLocked(_now());
            return _requests.Values.Where(r => r.State == "pending").OrderBy(r => r.CreatedAt).ToList();
        }
    }

    private void ExpireLocked(DateTimeOffset now)
    {
        foreach (var r in _requests.Values.Where(r => r.State == "pending" && r.ExpiresAt <= now).ToList())
        {
            r.State = "expired";
            ThreadPool.QueueUserWorkItem(_ => RequestEnded?.Invoke(r));
        }
    }

    /// <summary>The device takes its question back.</summary>
    public bool Cancel(string deviceId, string id)
    {
        AccessRequest? r;
        lock (_lock)
        {
            if (!_requests.TryGetValue(id, out r) || r.DeviceId != deviceId || r.State != "pending") return false;
            r.State = "cancelled";
        }
        RequestEnded?.Invoke(r);
        return true;
    }

    /// <summary>Someone at the PC answered: allowed (always, for an hour, until the app restarts) or not (null).</summary>
    public bool Decide(string id, AccessMode? mode)
    {
        AccessRequest? r;
        var now = _now();
        lock (_lock)
        {
            ExpireLocked(now);
            if (!_requests.TryGetValue(id, out r) || r.State != "pending") return false;
            r.State = mode is null ? "denied" : "allowed";
            r.Mode = mode switch { AccessMode.Always => "always", AccessMode.Hour => "hour", AccessMode.Session => "session", _ => null };
            if (mode is null)
            {
                var prev = _refusals.GetValueOrDefault(r.DeviceId);
                _refusals[r.DeviceId] = (prev.Count + 1, now);
            }
            else _refusals.Remove(r.DeviceId);
        }
        if (mode is { } m && _find(r.DeviceId) is { } d) Grant(d, r.Categories, m);
        RequestEnded?.Invoke(r);
        return true;
    }

    // ---- changing what a device may do -------------------------------------------------------------------------------

    /// <summary>Adds categories (an existing grant of the same category is replaced).</summary>
    public void Grant(PairedDevice d, IEnumerable<string> categories, AccessMode mode)
    {
        var now = _now();
        var cats = categories.Where(AccessCategories.IsKnown).Distinct().ToList();
        lock (_devicesLock)
        {
            var list = (d.Grants ?? Current(d).ToList()).Where(g => !cats.Contains(g.Category)).ToList();
            foreach (var c in cats)
            {
                list.Add(mode switch
                {
                    AccessMode.Hour => new AccessGrant { Category = c, Mode = "timed", ExpiresAt = now.AddHours(1), GrantedAt = now },
                    AccessMode.Session => new AccessGrant { Category = c, Mode = "session", Session = SessionId, GrantedAt = now },
                    _ => new AccessGrant { Category = c, Mode = "always", GrantedAt = now }
                });
            }
            d.Grants = list;   // a new list: whoever reads the old one keeps a consistent copy
            _save();
        }
        GrantsChanged?.Invoke(d.Id);
    }

    /// <summary>
    /// The PC's editor: exactly these categories from now on. A category kept keeps its grant as it is (an hour stays an
    /// hour); a new one is "always"; one taken away stops at once, along with anything the device is doing.
    /// </summary>
    public void Set(PairedDevice d, IEnumerable<string> categories)
    {
        var now = _now();
        var cats = categories.Where(AccessCategories.IsKnown).Distinct().ToList();
        bool removed;
        lock (_devicesLock)
        {
            var before = Current(d).ToList();
            var list = before.Where(g => cats.Contains(g.Category)).ToList();
            foreach (var c in cats.Where(c => list.All(g => g.Category != c)))
                list.Add(new AccessGrant { Category = c, Mode = "always", GrantedAt = now });
            removed = before.Any(g => !cats.Contains(g.Category));
            d.Grants = list;
            _save();
        }
        if (removed) _kick(d.Id);
        GrantsChanged?.Invoke(d.Id);
    }

    /// <summary>Takes one category (or everything) away; what the device is doing stops at once.</summary>
    public void Revoke(PairedDevice d, string? category)
    {
        lock (_devicesLock)
        {
            d.Grants = category is null ? new List<AccessGrant>() : Current(d).Where(g => g.Category != category).ToList();
            _save();
        }
        _kick(d.Id);
        GrantsChanged?.Invoke(d.Id);
    }

    /// <summary>The device itself gives something up (nothing to stop: it asked).</summary>
    public void Drop(PairedDevice d, string category)
    {
        lock (_devicesLock)
        {
            d.Grants = Current(d).Where(g => g.Category != category).ToList();
            _save();
        }
        GrantsChanged?.Invoke(d.Id);
    }
}
