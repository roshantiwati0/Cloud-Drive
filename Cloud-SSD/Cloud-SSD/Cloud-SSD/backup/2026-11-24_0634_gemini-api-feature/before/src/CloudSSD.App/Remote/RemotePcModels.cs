namespace CloudSSD.App.Remote;

/// <summary>Another PC (or laptop) this one is paired with as a device, as saved in remote\pcs.json.</summary>
public sealed class RemotePc
{
    public string PcId { get; set; } = "";
    /// <summary>The PC's own name (its Windows machine name).</summary>
    public string Name { get; set; } = "";
    /// <summary>A name given on this PC (shown instead of <see cref="Name"/>).</summary>
    public string? Label { get; set; }
    /// <summary>The address it was paired on (home network).</summary>
    public string LanUrl { get; set; } = "";
    /// <summary>Tailscale addresses it reported, for when it is away from home.</summary>
    public List<string> AwayUrls { get; set; } = new();
    /// <summary>The last address that answered.</summary>
    public string? LastUrl { get; set; }
    /// <summary>This PC's key on that PC, protected with Windows DPAPI (this Windows user only).</summary>
    public string TokenProtected { get; set; } = "";
    /// <summary>This PC's id on that PC (for its /api/hello proof).</summary>
    public string DeviceId { get; set; } = "";
    public DateTimeOffset PairedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? LastOnlineAt { get; set; }
    /// <summary>That PC no longer accepts this one's key: it has to be paired again.</summary>
    public bool NeedsRepair { get; set; }
    /// <summary>Added by that PC itself right after it paired with this one (it gave this PC a key to it).</summary>
    public bool PairedBack { get; set; }
    /// <summary>The key this PC gave that PC in return: its device id on this PC (removed when this PC forgets it).</summary>
    public string? BackDeviceId { get; set; }
    public List<string> Features { get; set; } = new();
    /// <summary>Folders kept on this PC ("Offline rakho").</summary>
    public List<PinnedFolder> Pins { get; set; } = new();
    /// <summary>Pairing back (that PC browsing this one) — none | offered | approved.</summary>
    public string ReverseLink { get; set; } = "none";

    public string DisplayName => string.IsNullOrWhiteSpace(Label) ? (string.IsNullOrWhiteSpace(Name) ? "PC" : Name) : Label!;
}

/// <summary>A folder on another PC kept on this one, so its files open without that PC.</summary>
public sealed class PinnedFolder
{
    public string Path { get; set; } = "";
    /// <summary>One file kept, rather than a whole folder.</summary>
    public bool IsFile { get; set; }
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? LastSyncAt { get; set; }
    public int Files { get; set; }
    public long Bytes { get; set; }
    public string? Error { get; set; }
    /// <summary>The folder is bigger than this PC keeps without asking: the size found, waiting for an OK.</summary>
    public long? NeedsOkBytes { get; set; }
    /// <summary>The size the user said yes to (kept until the folder grows past it).</summary>
    public long ApprovedBytes { get; set; }
}

/// <summary>A file or folder on another PC ("&lt;drive&gt;:&lt;path&gt;").</summary>
public sealed record RemoteEntry(string Id, string Name, bool IsDirectory, long? Size, long Modified, long Created, bool CanEdit, bool CanCreate);

/// <summary>A drive on another PC.</summary>
public sealed record RemoteRoot(string Id, string Title, bool ReadOnly, bool FixedTopLevel, bool CanCreate, long? TotalBytes, long? FreeBytes);

/// <summary>A folder list kept on this PC: shown while the other PC is away, compared on return for NEW badges.</summary>
public sealed class CachedList
{
    public string Path { get; set; } = "";
    public DateTimeOffset SavedAt { get; set; }
    public List<RemoteEntry> Entries { get; set; } = new();
    /// <summary>Ids that appeared since the list before (NEW badges, for a day).</summary>
    public List<string> NewIds { get; set; } = new();
    public DateTimeOffset? NewAt { get; set; }
}

/// <summary>Data from another PC: live (<see cref="FromCache"/> false), or kept from when it was last reachable.</summary>
public sealed record Fetched<T>(T Value, bool FromCache, DateTimeOffset SavedAt);

public enum RemoteLinkState { Unknown, Online, Offline, NeedsRepair }

public enum RemoteErrorKind { Offline, Unauthorized, SwitchedOff, NotFound, DriveMissing, NoSpace, WrongCode, Locked, Self, OldPc, Server, NeedsPermission }

/// <summary>What went wrong talking to another PC, in words for the screen.</summary>
public sealed class RemotePcException(RemoteErrorKind kind, string message, int status = 0, int retryAfterSeconds = 0, IReadOnlyList<string>? categories = null) : Exception(message)
{
    public RemoteErrorKind Kind { get; } = kind;
    public int Status { get; } = status;
    public int RetryAfterSeconds { get; } = retryAfterSeconds;
    /// <summary>NeedsPermission: what that PC wants allowed first (browse, stream…), to ask it for.</summary>
    public IReadOnlyList<string> Categories { get; } = categories ?? [];
}

/// <summary>Files offered to this laptop through that PC: by the PC itself, or by a phone through it.</summary>
public sealed record RemoteShareOffer(string Id, string From, bool FromPc, string State, long Total, IReadOnlyList<RemoteShareItem> Items);

public sealed record RemoteShareItem(string Id, string Name, long Size, bool Ready, bool Delivered)
{
    /// <summary>Its place in a folder that was sent ("MyApp/src"); "" for a loose file.</summary>
    public string Folder { get; init; } = "";
}

/// <summary>A phone or tablet paired with that PC (to look at through it); listening = its app answers now.</summary>
public sealed record RemotePhone(string Id, string Name, string Kind, bool Listening, bool Online);

/// <summary>This laptop's question to that PC for more access: pending, allowed or refused.</summary>
public sealed record RemoteAccessRequest(string Id, string State, IReadOnlyList<string> Categories, int ExpiresIn);

/// <summary>A file waiting to be sent to another PC (kept across restarts; continues from where the PC stopped).</summary>
public sealed class UploadJob
{
    public string Id { get; set; } = "";               // 32 lowercase hex: the upload id the PC knows it by
    public string LocalPath { get; set; } = "";
    public string RemoteFolder { get; set; } = "";     // "<drive>:<folder>"
    public string Name { get; set; } = "";
    public long Size { get; set; }
    public DateTime LocalModifiedUtc { get; set; }
    public string State { get; set; } = "queued";      // queued | running | done | failed
    public long SentBytes { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
}

/// <summary>A Cloud-SSD PC found on this network (its public answer, no key involved).</summary>
public sealed record FoundPc(string Url, string PcName, string PcId, string Version);

/// <summary>What this PC tells another one when it pairs with it.</summary>
public sealed record PairInfo(string Name, string InstallId, string Kind, string Platform, string AppVersion, string? HubPcId, string? HubName, int? HubPort);

public sealed record PairResult(string Token, string DeviceId, string PcName, string PcId);
