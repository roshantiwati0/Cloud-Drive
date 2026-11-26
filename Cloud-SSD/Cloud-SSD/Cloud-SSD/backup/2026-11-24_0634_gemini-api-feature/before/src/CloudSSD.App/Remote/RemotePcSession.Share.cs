using Microsoft.Extensions.Logging;

namespace CloudSSD.App.Remote;

/// <summary>
/// Files sent to this laptop through that PC (by the PC itself, or by a phone through it). Looked at on each check;
/// Accept saves them in Downloads\Cloud-SSD\Received\&lt;sender&gt;\&lt;date&gt;, marked as from another computer.
/// Files a phone is still sending to the PC come by themselves once they are there.
/// </summary>
public sealed partial class RemotePcSession
{
    private readonly SemaphoreSlim _inboxGate = new(1, 1);
    /// <summary>Offers accepted here whose files were not all here yet (a phone still sending them to the PC).</summary>
    private readonly HashSet<string> _accepted = new();
    /// <summary>Per offer, the name each sent folder got here ("MyApp", or "MyApp (2)" when one was there already).</summary>
    private readonly Dictionary<string, Dictionary<string, string>> _folderNames = new();

    /// <summary>What waits for an answer (or is still coming), as last seen.</summary>
    public IReadOnlyList<RemoteShareOffer> Inbox { get; private set; } = [];

    /// <summary>The offers changed (new ones, answered, files arrived).</summary>
    public event Action? InboxChanged;

    /// <summary>A file arrived on this laptop (its full path).</summary>
    public event Action<string>? FileReceived;

    /// <summary>Where received files go (a test puts them elsewhere).</summary>
    public string ReceivedFolder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "Cloud-SSD", "Received");

    private void CheckInboxSoon()
    {
        if (!IsOnline || !Pc.Features.Contains("share") || _life.IsCancellationRequested || _inboxGate.CurrentCount == 0) return;
        _ = Task.Run(async () =>
        {
            try { await CheckInboxAsync(_life.Token); }
            catch (Exception e) when (e is RemotePcException or IOException or OperationCanceledException or UnauthorizedAccessException) { _log.LogDebug(e, "Inbox check failed"); }
        });
    }

    /// <summary>Looks at the offers now; files of offers accepted earlier that have arrived at the PC are fetched.</summary>
    public async Task CheckInboxAsync(CancellationToken ct = default)
    {
        if (_url is not { } url) return;
        await _inboxGate.WaitAsync(ct);
        try
        {
            var all = await _client.InboxAsync(url, Token, ct);
            var waiting = all.Where(o => o.State == "offered" || (o.State == "accepted" && o.Items.Any(i => !i.Delivered))).ToList();
            // Accepted here before, and more of its files are at the PC now: they come by themselves.
            foreach (var o in waiting.Where(o => o.State == "accepted" && _accepted.Contains(o.Id)).ToList())
                await FetchAsync(url, o, ct);
            SetInbox(waiting);
        }
        finally { _inboxGate.Release(); }
    }

    private void SetInbox(IReadOnlyList<RemoteShareOffer> offers)
    {
        bool same = offers.Count == Inbox.Count && offers.Zip(Inbox).All(p => p.First.Id == p.Second.Id && p.First.State == p.Second.State
            && p.First.Items.Count(i => i.Delivered) == p.Second.Items.Count(i => i.Delivered) && p.First.Items.Count(i => i.Ready) == p.Second.Items.Count(i => i.Ready));
        Inbox = offers;
        if (!same) { try { InboxChanged?.Invoke(); } catch (Exception e) { _log.LogDebug(e, "InboxChanged handler failed"); } }
    }

    /// <summary>Takes an offer: its files that are at the PC now are saved here; the rest follow when they arrive.</summary>
    public async Task<IReadOnlyList<string>> AcceptAsync(RemoteShareOffer offer, CancellationToken ct = default)
    {
        if (_url is not { } url) throw new RemotePcException(RemoteErrorKind.Offline, Pc.DisplayName + " is not reachable.");
        await _inboxGate.WaitAsync(ct);
        try
        {
            await _client.AcceptShareAsync(url, Token, offer.Id, ct);
            _accepted.Add(offer.Id);
            var saved = await FetchAsync(url, offer, ct);
            SetInbox((await _client.InboxAsync(url, Token, ct)).Where(o => o.State == "offered" || (o.State == "accepted" && o.Items.Any(i => !i.Delivered))).ToList());
            return saved;
        }
        finally { _inboxGate.Release(); }
    }

    public async Task DeclineAsync(RemoteShareOffer offer, CancellationToken ct = default)
    {
        if (_url is not { } url) throw new RemotePcException(RemoteErrorKind.Offline, Pc.DisplayName + " is not reachable.");
        await _client.DeclineShareAsync(url, Token, offer.Id, ct);
        SetInbox(Inbox.Where(o => o.Id != offer.Id).ToList());
    }

    /// <summary>Saves the offer's files that are ready and not here yet; returns their paths.</summary>
    private async Task<List<string>> FetchAsync(string url, RemoteShareOffer offer, CancellationToken ct)
    {
        var saved = new List<string>();
        var folder = Path.Combine(ReceivedFolder, LocalMobileBridgeService.SafeUploadName(offer.From.Length > 0 ? offer.From : Pc.DisplayName),
            DateTime.Now.ToString("yyyy-MM-dd"));
        foreach (var item in offer.Items.Where(i => i.Ready && !i.Delivered))
        {
            var dir = Path.Combine(folder, LocalFolder(folder, offer.Id, item.Folder));
            Directory.CreateDirectory(dir);
            // A part left by a cut-off attempt is continued; otherwise a free name ("photo (1).jpg" when taken).
            var name = LocalMobileBridgeService.SafeUploadName(item.Name);
            var dest = File.Exists(Path.Combine(dir, name + ".cssd-part")) ? Path.Combine(dir, name) : Path.Combine(dir, LocalMobileBridgeService.FreeName(dir, name));
            await _client.DownloadShareAsync(url, Token, offer.Id, item.Id, dest, null, ct);
            MarkFromAnotherComputer(dest);
            saved.Add(dest);
            try { FileReceived?.Invoke(dest); } catch (Exception e) { _log.LogDebug(e, "FileReceived handler failed"); }
        }
        // Everything is here now (what was ready has just come): nothing left to wait for.
        if (offer.Items.All(i => i.Delivered || i.Ready)) _accepted.Remove(offer.Id);
        return saved;
    }

    /// <summary>
    /// Where a file of a sent folder goes, under the day's folder: the same tree ("MyAppsrc"), made safe. The folder at
    /// the top gets a free name once per offer, so a second "MyApp" sent later lands in "MyApp (2)", not mixed into the first.
    /// </summary>
    private string LocalFolder(string dayFolder, string offerId, string sentFolder)
    {
        var safe = ProjectFilter.SafeFolder(sentFolder);
        if (safe.Length == 0) return "";
        var parts = safe.Split('/');
        if (!_folderNames.TryGetValue(offerId, out var names)) _folderNames[offerId] = names = new(StringComparer.OrdinalIgnoreCase);
        if (!names.TryGetValue(parts[0], out var top)) names[parts[0]] = top = ProjectFilter.FreeFolderName(dayFolder, parts[0]);
        parts[0] = top;
        return Path.Combine(parts);
    }

    // ---- that PC's phones (looked at through it) --------------------------------------------------------------------

    /// <summary>The phones and tablets paired with that PC (empty while it is away).</summary>
    public Task<IReadOnlyList<RemotePhone>> PhonesAsync(CancellationToken ct = default) =>
        _url is { } url ? _client.PhonesAsync(url, Token, ct) : Task.FromResult<IReadOnlyList<RemotePhone>>([]);

    /// <summary>
    /// Asks one of that PC's phones (fs.roots, fs.list, fs.read) through it, as the browse window expects: a read's file
    /// arrives in a temporary file. "pc-refused": that PC has not allowed this one to look at its phones.
    /// </summary>
    public async Task<PeerResult> AskPhoneAsync(string phoneId, string kind, IReadOnlyDictionary<string, string> args, CancellationToken ct = default)
    {
        if (_url is not { } url) return new PeerResult("error", Error: Pc.DisplayName + " is not reachable.");
        try
        {
            if (kind == "fs.read")
            {
                var tmp = Path.Combine(Path.GetTempPath(), "cssd-relay-" + Guid.NewGuid().ToString("N") + ".bin");
                var (st, err, bytes) = await _client.ReadPhoneFileAsync(url, Token, phoneId, args.GetValueOrDefault("path") ?? "", tmp, ct);
                if (st == "ok") return new PeerResult("ok", FilePath: tmp, Bytes: bytes);
                try { File.Delete(tmp); } catch (IOException) { }
                return new PeerResult(st, Error: err);
            }
            var (status, data, error) = await _client.AskPhoneAsync(url, Token, phoneId, kind, args.GetValueOrDefault("path"), ct);
            return new PeerResult(status, data, Error: error);
        }
        catch (RemotePcException e)
        {
            return new PeerResult(e.Kind == RemoteErrorKind.NeedsPermission ? "pc-refused" : "error", Error: e.Message);
        }
    }

    // ---- asking that PC for more access ----------------------------------------------------------------------------

    /// <summary>Asks the person at that PC to allow these categories for this laptop (they answer there).</summary>
    public Task<RemoteAccessRequest> RequestAccessAsync(IEnumerable<string> categories, CancellationToken ct = default) =>
        _url is { } url ? _client.RequestAccessAsync(url, Token, categories, "From the Other PCs page", ct)
            : throw new RemotePcException(RemoteErrorKind.Offline, Pc.DisplayName + " is not reachable.");

    /// <summary>The answer to the last question (null when there is none).</summary>
    public Task<RemoteAccessRequest?> AccessRequestAsync(CancellationToken ct = default) =>
        _url is { } url ? _client.AccessRequestAsync(url, Token, ct)
            : throw new RemotePcException(RemoteErrorKind.Offline, Pc.DisplayName + " is not reachable.");

    /// <summary>The "Mark of the Web": Windows treats the file like a download (SmartScreen checks it before it runs).</summary>
    internal static void MarkFromAnotherComputer(string path)
    {
        try { File.WriteAllText(path + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n"); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException) { }   // not NTFS: nothing to mark
    }
}
