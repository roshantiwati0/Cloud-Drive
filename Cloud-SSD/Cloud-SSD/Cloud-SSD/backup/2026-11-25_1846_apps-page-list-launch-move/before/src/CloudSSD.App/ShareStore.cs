using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using CloudSSD.Core;

namespace CloudSSD.App;

/// <summary>One file in a share: on this PC (sent from here) or in the relay folder (sent by another device).</summary>
public sealed class ShareItem
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Its place inside a folder that was sent ("MyApp/src"), "" for a loose file: the receiver rebuilds the tree.</summary>
    public string Folder { get; set; } = "";
    public long Size { get; set; }
    /// <summary>Last write time of the file when it was offered (ms since 1970): a changed file is not sent.</summary>
    public long Modified { get; set; }
    [JsonIgnore] public string? Path { get; set; }
    /// <summary>Where the file is on this PC (kept in the saved list, never sent to devices).</summary>
    public string? Source { get => Path; set => Path = value; }
    /// <summary>The file is here (a relayed file has finished uploading).</summary>
    public bool Ready { get; set; }
    /// <summary>The receiver has all of it.</summary>
    public bool Delivered { get; set; }
}

/// <summary>
/// Files offered to a device: from this PC, or from another device through this PC (relay). The receiver accepts or
/// declines; an offer nobody answers ends after a day, and relayed files are deleted three days after at the latest.
/// </summary>
public sealed class ShareOffer
{
    public string Id { get; set; } = "";
    /// <summary>"pc" (this PC) or the sending device's id.</summary>
    public string FromId { get; set; } = "";
    public string FromName { get; set; } = "";
    public string ToId { get; set; } = "";
    public string ToName { get; set; } = "";
    public bool Relay { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    /// <summary>uploading (the sender is still sending) | offered | accepted | declined | done | expired | cancelled</summary>
    public string State { get; set; } = "offered";
    public List<ShareItem> Items { get; set; } = new();
    /// <summary>Left out of the folders that were sent (build output, .git…): how many files, bytes, and which folders.</summary>
    public int SkippedFiles { get; set; }
    public long SkippedBytes { get; set; }
    public List<string> SkippedFolders { get; set; } = new();

    [JsonIgnore] public long TotalBytes => Items.Sum(i => i.Size);
    /// <summary>The folders at the top of what was sent ("MyApp"), in order.</summary>
    [JsonIgnore] public IReadOnlyList<string> TopFolders => Items.Where(i => i.Folder.Length > 0).Select(i => i.Folder.Split('/')[0]).Distinct().ToList();
    [JsonIgnore] public bool IsOpen => State is "uploading" or "offered" or "accepted";
}

/// <summary>A link that lets anyone on the home network (or the tailnet) download one file for a while.</summary>
public sealed class ShareLink
{
    public string Id { get; set; } = "";
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public int? MaxDownloads { get; set; }
    public int Downloads { get; set; }
    public bool Revoked { get; set; }
}

/// <summary>
/// Offers and links, kept in shares\offers.json so they survive a restart. Relayed files live in shares\relay\&lt;offer&gt;\
/// and are removed when the offer ends (or after three days). One lock guards everything; the lists are small.
/// </summary>
public sealed class ShareStore
{
    public static readonly TimeSpan OfferLifetime = TimeSpan.FromHours(24);
    public static readonly TimeSpan RelayKeep = TimeSpan.FromHours(72);
    public const int MaxItems = 200;
    /// <summary>Most files in one offer when folders are sent (a project easily has thousands).</summary>
    public const int MaxFolderItems = 5000;
    /// <summary>Most a relayed offer may hold, and all relayed files together.</summary>
    public static long RelayOfferQuota = 4L << 30;
    public static long RelayTotalQuota = 20L << 30;

    private readonly string _dir;
    private readonly Func<DateTimeOffset> _now;
    private readonly object _lock = new();
    private State _state = new();

    private sealed class State
    {
        public List<ShareOffer> Offers { get; set; } = new();
        public List<ShareLink> Links { get; set; } = new();
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>An offer was made, answered, delivered, or ended (raised outside the lock).</summary>
    public event Action<ShareOffer>? Changed;

    public ShareStore(string dir, Func<DateTimeOffset> now)
    {
        _dir = dir;
        _now = now;
        try
        {
            var f = Path.Combine(dir, "offers.json");
            if (File.Exists(f)) _state = JsonSerializer.Deserialize<State>(File.ReadAllText(f), Json) ?? new State();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { _state = new State(); }
    }

    public string RelayDir => Path.Combine(_dir, "relay");
    public string RelayFolder(ShareOffer o) => Path.Combine(RelayDir, o.Id);

    private void SaveLocked()
    {
        try
        {
            Directory.CreateDirectory(_dir);
            var f = Path.Combine(_dir, "offers.json");
            var tmp = f + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_state, Json));
            File.Move(tmp, f, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private static string NewId(int bytes = 9) => Convert.ToHexString(RandomNumberGenerator.GetBytes(bytes)).ToLowerInvariant();

    private void Raise(ShareOffer o)
    {
        try { Changed?.Invoke(o); } catch (Exception) { /* a screen's handler must not break the bridge */ }
    }

    // ---- offers -------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Files and folders on this PC offered to a device. A folder goes with everything in it, each file keeping its
    /// place ("MyApp/src"); what a build or package manager makes again is left out unless <paramref name="everything"/>.
    /// Missing paths are left out; null when nothing is left.
    /// </summary>
    public ShareOffer? OfferFromPc(string pcName, PairedDevice to, IEnumerable<string> paths, bool everything = false)
    {
        var now = _now();
        var items = new List<ShareItem>();
        int skippedFiles = 0;
        long skippedBytes = 0;
        var skippedFolders = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (Directory.Exists(p))
            {
                var (files, skipped) = ProjectFilter.Walk(p, everything, MaxFolderItems - items.Count);
                items.AddRange(files.Select(f => new ShareItem
                {
                    Id = NewId(6), Name = f.Name, Folder = f.Folder, Size = f.Size, Modified = new DateTimeOffset(f.ModifiedUtc).ToUnixTimeMilliseconds(),
                    Path = f.FullPath, Ready = true
                }));
                skippedFiles += skipped.Files;
                skippedBytes += skipped.Bytes;
                foreach (var s in skipped.Folders) skippedFolders.Add(s);
                continue;
            }
            var fi = new FileInfo(p);
            if (!fi.Exists || items.Count >= MaxFolderItems) continue;
            items.Add(new ShareItem
            {
                Id = NewId(6), Name = fi.Name, Size = fi.Length, Modified = new DateTimeOffset(fi.LastWriteTimeUtc).ToUnixTimeMilliseconds(),
                Path = fi.FullName, Ready = true
            });
        }
        if (items.Count == 0) return null;
        var o = new ShareOffer
        {
            Id = NewId(), FromId = "pc", FromName = pcName, ToId = to.Id, ToName = to.Name, CreatedAt = now, ExpiresAt = now + OfferLifetime,
            State = "offered", Items = items, SkippedFiles = skippedFiles, SkippedBytes = skippedBytes, SkippedFolders = skippedFolders.ToList()
        };
        lock (_lock) { _state.Offers.Add(o); SaveLocked(); }
        Raise(o);
        return o;
    }

    /// <summary>
    /// A device sends files to another device through this PC: the offer waits ("uploading") until every file has
    /// arrived here, then the receiver sees it. Null and a reason when it cannot be done.
    /// </summary>
    public (ShareOffer? Offer, string? Error) OfferFromDevice(PairedDevice from, PairedDevice to, IReadOnlyList<(string Name, long Size)> files) =>
        OfferFromDevice(from, to, files.Select(f => (f.Name, f.Size, "")).ToList());

    /// <summary>The same with each file's folder ("MyApp/src"), made safe here: a device never picks where outside it lands.</summary>
    public (ShareOffer? Offer, string? Error) OfferFromDevice(PairedDevice from, PairedDevice to, IReadOnlyList<(string Name, long Size, string Folder)> files)
    {
        if (files.Count == 0) return (null, "Nothing to send.");
        int max = files.Any(f => !string.IsNullOrEmpty(f.Folder)) ? MaxFolderItems : MaxItems;
        if (files.Count > max) return (null, $"At most {max} files at once.");
        if (files.Any(f => f.Size < 0)) return (null, "Every file needs its size.");
        long total = files.Sum(f => f.Size);
        if (total > RelayOfferQuota) return (null, $"Too big to pass through the PC: at most {RelayOfferQuota >> 30} GB at once.");
        var now = _now();
        ShareOffer o;
        lock (_lock)
        {
            long inRelay = _state.Offers.Where(x => x.Relay && x.IsOpen).Sum(x => x.TotalBytes);
            if (inRelay + total > RelayTotalQuota) return (null, "The PC is holding too many files for other devices right now. Try again later.");
            o = new ShareOffer
            {
                Id = NewId(), FromId = from.Id, FromName = from.Name, ToId = to.Id, ToName = to.Name, Relay = true, CreatedAt = now,
                ExpiresAt = now + OfferLifetime, State = "uploading",
                Items = files.Select(f => new ShareItem
                {
                    Id = NewId(6), Name = LocalMobileBridgeService.SafeUploadName(f.Name), Folder = ProjectFilter.SafeFolder(f.Folder), Size = f.Size
                }).ToList()
            };
            _state.Offers.Add(o);
            SaveLocked();
        }
        Raise(o);
        return (o, null);
    }

    public ShareOffer? Get(string id)
    {
        lock (_lock) return _state.Offers.FirstOrDefault(o => o.Id == id);
    }

    /// <summary>Offers this device should see: offered or accepted, not yet run out.</summary>
    public IReadOnlyList<ShareOffer> InboxOf(string deviceId)
    {
        Sweep();
        lock (_lock) return _state.Offers.Where(o => o.ToId == deviceId && o.State is "offered" or "accepted" && o.ToId == deviceId).ToList();
    }

    /// <summary>Offers to or from this device that are still going (for its "sent" list).</summary>
    public IReadOnlyList<ShareOffer> SentBy(string fromId)
    {
        lock (_lock) return _state.Offers.Where(o => o.FromId == fromId).OrderByDescending(o => o.CreatedAt).Take(50).ToList();
    }

    /// <summary>Every recent offer, newest first (for the PC's screens).</summary>
    public IReadOnlyList<ShareOffer> Recent(int max = 50)
    {
        lock (_lock) return _state.Offers.OrderByDescending(o => o.CreatedAt).Take(max).ToList();
    }

    private bool Move(string id, Func<ShareOffer, bool> allowed, string state)
    {
        ShareOffer? o;
        lock (_lock)
        {
            o = _state.Offers.FirstOrDefault(x => x.Id == id);
            if (o is null || !allowed(o)) return false;
            o.State = state;
            SaveLocked();
        }
        if (state is "declined" or "cancelled") DeleteRelay(o);
        Raise(o);
        return true;
    }

    public bool Accept(string deviceId, string id) => Move(id, o => o.ToId == deviceId && o.State == "offered" && o.ExpiresAt > _now(), "accepted");
    public bool Decline(string deviceId, string id) => Move(id, o => o.ToId == deviceId && o.State is "offered" or "accepted", "declined");
    /// <summary>The sender takes it back (this PC: "pc").</summary>
    public bool Cancel(string fromId, string id) => Move(id, o => o.FromId == fromId && o.IsOpen, "cancelled");

    /// <summary>A relayed file has fully arrived here; once all have, the receiver is told.</summary>
    public void ItemUploaded(ShareOffer o, ShareItem item, string path)
    {
        bool ready;
        lock (_lock)
        {
            item.Path = path;
            item.Ready = true;
            item.Size = new FileInfo(path).Length;
            item.Modified = new DateTimeOffset(File.GetLastWriteTimeUtc(path)).ToUnixTimeMilliseconds();
            ready = o.State == "uploading" && o.Items.All(i => i.Ready);
            if (ready) o.State = "offered";
            SaveLocked();
        }
        if (ready) Raise(o);
    }

    /// <summary>The receiver got all of one file; when it has every file, the offer is done.</summary>
    public void ItemDelivered(ShareOffer o, ShareItem item)
    {
        bool done;
        lock (_lock)
        {
            item.Delivered = true;
            done = o.State is "offered" or "accepted" && o.Items.All(i => i.Delivered);
            if (done) o.State = "done";
            SaveLocked();
        }
        if (done) { DeleteRelay(o); Raise(o); }
    }

    private void DeleteRelay(ShareOffer o)
    {
        if (!o.Relay) return;
        try { if (Directory.Exists(RelayFolder(o))) Directory.Delete(RelayFolder(o), true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Ends offers nobody answered in time, removes relayed files that are no longer needed, forgets old ones.</summary>
    public void Sweep()
    {
        var now = _now();
        var ended = new List<ShareOffer>();
        lock (_lock)
        {
            foreach (var o in _state.Offers.Where(o => o.IsOpen && o.ExpiresAt <= now)) { o.State = "expired"; ended.Add(o); }
            int before = _state.Offers.Count;
            _state.Offers.RemoveAll(o => !o.IsOpen && now - o.CreatedAt > TimeSpan.FromDays(7));
            _state.Links.RemoveAll(l => l.ExpiresAt < now - TimeSpan.FromDays(1));
            if (ended.Count > 0 || _state.Offers.Count != before) SaveLocked();
        }
        foreach (var o in ended) { DeleteRelay(o); Raise(o); }
        // Relay folders of offers that are gone or older than three days.
        try
        {
            if (!Directory.Exists(RelayDir)) return;
            foreach (var d in Directory.EnumerateDirectories(RelayDir))
            {
                var o = Get(Path.GetFileName(d));
                if (o is null || !o.IsOpen || now - o.CreatedAt > RelayKeep)
                    try { Directory.Delete(d, true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    // ---- links ---------------------------------------------------------------------------------------------------------

    public ShareLink? CreateLink(string path, TimeSpan lifetime, int? maxDownloads)
    {
        var fi = new FileInfo(path);
        if (!fi.Exists) return null;
        var now = _now();
        var link = new ShareLink { Id = NewId(12), Path = fi.FullName, Name = fi.Name, CreatedAt = now, ExpiresAt = now + lifetime, MaxDownloads = maxDownloads };
        lock (_lock) { _state.Links.Add(link); SaveLocked(); }
        return link;
    }

    /// <summary>The link when it may still be used, else null.</summary>
    public ShareLink? UsableLink(string id)
    {
        var now = _now();
        lock (_lock)
        {
            var l = _state.Links.FirstOrDefault(x => x.Id == id);
            return l is null || l.Revoked || l.ExpiresAt <= now || (l.MaxDownloads is { } m && l.Downloads >= m) ? null : l;
        }
    }

    public void CountDownload(ShareLink l)
    {
        lock (_lock) { l.Downloads++; SaveLocked(); }
    }

    public bool RevokeLink(string id)
    {
        lock (_lock)
        {
            var l = _state.Links.FirstOrDefault(x => x.Id == id);
            if (l is null) return false;
            l.Revoked = true;
            SaveLocked();
            return true;
        }
    }

    public IReadOnlyList<ShareLink> Links()
    {
        lock (_lock) return _state.Links.OrderByDescending(l => l.CreatedAt).ToList();
    }
}
