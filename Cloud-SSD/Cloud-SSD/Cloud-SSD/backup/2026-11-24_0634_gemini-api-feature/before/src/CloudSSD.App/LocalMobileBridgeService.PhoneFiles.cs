using System.Net.Sockets;
using System.Text.Json;
using CloudSSD.Core;
using Microsoft.Extensions.Logging;

namespace CloudSSD.App;

/// <summary>An entry in a phone's folder, as the Phone files page lists it (paths are relative to the phone storage).</summary>
public sealed record PhoneItem(string Path, string Name, bool IsFolder, long Size);

/// <summary>Asks a phone something: this PC's own phone, or one reached through another PC (a laptop's view).</summary>
public delegate Task<PeerResult> PhoneAsk(string kind, Dictionary<string, string> args, TimeSpan timeout, CancellationToken ct);

/// <summary>
/// This PC working in a phone's folders both ways, like a phone maker's PC suite: whole folders from the phone to the PC,
/// and files or folders from the PC into a phone folder. The phone does the work (fs.tree, fs.read, fs.mkdir, fs.pull)
/// as far as it allows this PC; every run is a transfer task ("pf-…") on the Transfers screen.
/// </summary>
public sealed partial class LocalMobileBridgeService
{
    /// <summary>Most files one run handles (a big project fits).</summary>
    public const int MaxPhoneFilesRun = 20000;

    private readonly object _blobsLock = new();
    private readonly Dictionary<string, (string DeviceId, string Path, DateTimeOffset Until)> _blobs = new();
    private readonly Dictionary<string, CancellationTokenSource> _phoneRuns = new();
    private readonly Dictionary<string, Func<TransferTask?>> _phoneRetries = new();
    private bool _phoneTasksHooked;

    /// <summary>A file on this PC that one device may fetch once, within 30 minutes (fs.pull): the url to give it.</summary>
    internal string StageBlob(string deviceId, string path)
    {
        var id = Guid.NewGuid().ToString("N");
        lock (_blobsLock)
        {
            foreach (var old in _blobs.Where(b => b.Value.Until < Now()).Select(b => b.Key).ToList()) _blobs.Remove(old);
            _blobs[id] = (deviceId, path, Now().AddMinutes(30));
        }
        return "/api/peer/blob?id=" + id;
    }

    private void Unstage(string url)
    {
        var id = url[(url.IndexOf("id=", StringComparison.Ordinal) + 3)..];
        lock (_blobsLock) _blobs.Remove(id);
    }

    /// <summary>GET /api/peer/blob?id=: the device fetches a file this PC staged for it.</summary>
    private async Task HandleBlobAsync(PairedDevice me, Dictionary<string, string> q, Dictionary<string, string> headers,
        NetworkStream stream, string clientIp, CancellationToken ct)
    {
        (string DeviceId, string Path, DateTimeOffset Until) b;
        bool found;
        lock (_blobsLock) found = _blobs.TryGetValue(q.GetValueOrDefault("id") ?? "", out b);
        if (!found || b.DeviceId != me.Id || b.Until < Now() || !File.Exists(b.Path))
        {
            await SendJsonAsync(stream, 404, "Not Found", new { error = "No such file for this device (it may have run out of time)." }, ct);
            return;
        }
        await SendFileAsync(new FileInfo(b.Path), headers, stream, clientIp, "application/octet-stream", "attachment", "download", ct);
    }

    /// <summary>Retry and Cancel on the Transfers screen reach the runs started here.</summary>
    private void HookPhoneTasks()
    {
        if (_phoneTasksHooked) return;
        _phoneTasksHooked = true;
        Tasks.Handle("pf-",
            retry: t => { Func<TransferTask?>? again; lock (_phoneRuns) _phoneRetries.TryGetValue(t.Id, out again); return again?.Invoke(); },
            cancel: t =>
            {
                CancellationTokenSource? cts;
                lock (_phoneRuns) _phoneRuns.TryGetValue(t.Id, out cts);
                if (cts is null) return false;
                cts.Cancel();
                return true;
            });
    }

    private static string NewPhoneTaskId() => "pf-" + Guid.NewGuid().ToString("N")[..10];

    /// <summary>Why one file did not make it, in the transfer-task words.</summary>
    private static string ReasonOf(PeerResult r) => r.Status switch
    {
        "needs-permission" or "denied" => "denied",
        "timeout" or "offline" => "network",
        _ when (r.Error ?? "").Contains("space", StringComparison.OrdinalIgnoreCase) => "space",
        _ when (r.Error ?? "").Contains("not there", StringComparison.OrdinalIgnoreCase) => "missing",
        _ => "other",
    };

    /// <summary>
    /// Copies files and whole folders from the phone into <paramref name="destFolder"/> on this PC, keeping each folder's
    /// tree ("Camera/2026/a.jpg"). A name already there gets a free one ("a (2).jpg"). Returns the task at once.
    /// </summary>
    public TransferTask SavePhoneItemsToPc(PairedDevice device, IReadOnlyList<PhoneItem> items, string destFolder) =>
        SavePhoneItemsToPc(device.Name, device.Id, TransferTasks.LinkOf(device.LastLink),
            (kind, args, timeout, ct) => AskDeviceAsync(device.Id, kind, args, timeout, ct), items, destFolder);

    /// <summary>The same for a phone asked through <paramref name="ask"/> (a laptop looking at a phone through its PC).</summary>
    public TransferTask SavePhoneItemsToPc(string deviceName, string deviceId, string link, PhoneAsk ask, IReadOnlyList<PhoneItem> items, string destFolder)
    {
        HookPhoneTasks();
        var id = NewPhoneTaskId();
        var cts = new CancellationTokenSource();
        var single = items.Count == 1 && items[0].IsFolder ? items[0].Name : "";
        var task = Tasks.Begin(id, "toPc", deviceName, link,
            items.Count(i => !i.IsFolder), items.Where(i => !i.IsFolder).Sum(i => i.Size), destFolder, destFolder,
            kind: single.Length > 0 ? "folder" : "files", folder: single, deviceId: deviceId);
        lock (_phoneRuns) { _phoneRuns[id] = cts; _phoneRetries[id] = () => SavePhoneItemsToPc(deviceName, deviceId, link, ask, items, destFolder); }
        _ = Task.Run(async () =>
        {
            try { await RunSaveToPcAsync(id, deviceName, ask, items, destFolder, cts.Token); }
            catch (OperationCanceledException) { Tasks.Finish(id, "cancelled", "cancelled"); }
            catch (Exception e) { _log.LogWarning("Phone files: saving from {Device} stopped: {Error}", deviceName, e.Message); Tasks.Finish(id, "failed", "other"); }
            finally { lock (_phoneRuns) _phoneRuns.Remove(id); }
        });
        return task;
    }

    private async Task RunSaveToPcAsync(string id, string deviceName, PhoneAsk ask, IReadOnlyList<PhoneItem> items, string destFolder, CancellationToken ct)
    {
        // Every file with where it goes: folders are listed by the phone first.
        var todo = new List<(string PhonePath, string RelFolder, string Name, long Size)>();
        foreach (var it in items)
        {
            if (!it.IsFolder) { todo.Add((it.Path, "", it.Name, it.Size)); continue; }
            var r = await ask("fs.tree", new Dictionary<string, string> { ["path"] = it.Path }, TimeSpan.FromMinutes(2), ct);
            if (!r.Ok || r.Json is not { } json)
            {
                Tasks.Begin(id, "toPc", deviceName, "", 0, 0);
                Tasks.FileFailed(id, it.Path, it.Name, ReasonOf(r));
                continue;
            }
            // A folder of that name already here: "Camera (2)", as for files (keep both).
            var top = ProjectFilter.FreeFolderName(destFolder, ProjectFilter.SafeFolder(it.Name) is { Length: > 0 } n ? n : "Phone folder");
            Directory.CreateDirectory(Path.Combine(destFolder, top));
            if (json.TryGetProperty("folders", out var folders))
                foreach (var f in folders.EnumerateArray())
                    Directory.CreateDirectory(Path.Combine(destFolder, top, ProjectFilter.SafeFolder(f.GetString())));
            foreach (var f in json.GetProperty("files").EnumerateArray())
            {
                var rel = f.GetProperty("rel").GetString() ?? "";
                var slash = rel.LastIndexOf('/');
                var relFolder = slash > 0 ? top + "/" + rel[..slash] : top;
                todo.Add((f.GetProperty("path").GetString() ?? "", relFolder, slash >= 0 ? rel[(slash + 1)..] : rel, f.GetProperty("size").GetInt64()));
                if (todo.Count >= MaxPhoneFilesRun) break;
            }
        }
        Tasks.Begin(id, "toPc", deviceName, "", todo.Count, todo.Sum(t => t.Size));
        foreach (var (phonePath, relFolder, name, size) in todo)
        {
            if (ct.IsCancellationRequested) { Tasks.Finish(id, "cancelled", "cancelled"); return; }
            Tasks.Progress(id, phonePath, name, 0);
            var r = await ask("fs.read", new Dictionary<string, string> { ["path"] = phonePath }, TimeSpan.FromMinutes(15), ct);
            if (!r.Ok || r.FilePath is not { } temp)
            {
                Tasks.FileFailed(id, phonePath, name, ReasonOf(r));
                if (r.Status == "needs-permission") { Tasks.Finish(id, null, "denied"); return; }
                continue;
            }
            try
            {
                var folder = relFolder.Length == 0 ? destFolder : Path.Combine(destFolder, ProjectFilter.SafeFolder(relFolder));
                Directory.CreateDirectory(folder);
                File.Move(temp, Path.Combine(folder, FreeLocalName(folder, name)));
                Tasks.FileDone(id, phonePath, name, size);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                try { File.Delete(temp); } catch (IOException) { }
                Tasks.FileFailed(id, phonePath, name, e.Message.Contains("space", StringComparison.OrdinalIgnoreCase) ? "space" : "other");
            }
        }
        Tasks.Finish(id);
    }

    /// <summary>
    /// Puts files and folders from this PC into a folder on the phone (each folder with its tree; build output such as
    /// node_modules is left out unless <paramref name="everything"/>). onClash: keep | replace | skip.
    /// </summary>
    public TransferTask SendToPhoneFolder(PairedDevice device, IReadOnlyList<string> localPaths, string phoneFolder, string onClash = "keep",
        bool everything = false)
    {
        HookPhoneTasks();
        var id = NewPhoneTaskId();
        var cts = new CancellationTokenSource();
        var single = localPaths.Count == 1 && Directory.Exists(localPaths[0]) ? Path.GetFileName(localPaths[0].TrimEnd('\\', '/')) : "";
        var where = string.IsNullOrEmpty(phoneFolder) ? "Phone storage" : "Phone storage › " + phoneFolder.Replace("/", " › ");
        var task = Tasks.Begin(id, "toPhone", device.Name, TransferTasks.LinkOf(device.LastLink), 0, 0, where, "",
            kind: single.Length > 0 ? "folder" : "files", folder: single, deviceId: device.Id);
        lock (_phoneRuns) { _phoneRuns[id] = cts; _phoneRetries[id] = () => SendToPhoneFolder(device, localPaths, phoneFolder, "skip", everything); }
        _ = Task.Run(async () =>
        {
            try { await RunSendToPhoneAsync(id, device, localPaths, phoneFolder, onClash, everything, cts.Token); }
            // Cancelled while a file was on its way (the wait for the phone is cut short): the phone's task ends too.
            catch (OperationCanceledException) { Tasks.Finish(id, "cancelled", "cancelled"); await EndPhoneTaskAsync(device.Id, id, "cancelled"); }
            catch (Exception e)
            {
                _log.LogWarning("Phone files: sending to {Device} stopped: {Error}", device.Name, e.Message);
                Tasks.Finish(id, "failed", "other");
                await EndPhoneTaskAsync(device.Id, id, "network");
            }
            finally { lock (_phoneRuns) _phoneRuns.Remove(id); }
        });
        return task;
    }

    private async Task RunSendToPhoneAsync(string id, PairedDevice device, IReadOnlyList<string> localPaths, string phoneFolder, string onClash,
        bool everything, CancellationToken ct)
    {
        var todo = new List<FolderFile>();
        foreach (var p in localPaths)
        {
            if (Directory.Exists(p)) todo.AddRange(ProjectFilter.Walk(p, everything, MaxPhoneFilesRun - todo.Count).Files);
            else if (File.Exists(p))
            {
                var fi = new FileInfo(p);
                todo.Add(new FolderFile(fi.FullName, "", fi.Name, fi.Length, fi.LastWriteTimeUtc));
            }
            if (todo.Count >= MaxPhoneFilesRun) break;
        }
        long total = todo.Sum(f => f.Size);
        Tasks.Begin(id, "toPhone", device.Name, "", todo.Count, total);
        var top = localPaths.Count == 1 && Directory.Exists(localPaths[0]) ? Path.GetFileName(localPaths[0].TrimEnd('\\', '/')) : "";
        // The phone shows the run as one task too; told when it ends early.
        Task EndOnPhone() => EndPhoneTaskAsync(device.Id, id, "cancelled");
        for (int n = 0; n < todo.Count; n++)
        {
            var f = todo[n];
            if (ct.IsCancellationRequested) { Tasks.Finish(id, "cancelled", "cancelled"); await EndOnPhone(); return; }
            Tasks.Progress(id, f.FullPath, f.Name, 0);
            var url = StageBlob(device.Id, f.FullPath);
            PeerResult r;
            try
            {
                var args = new Dictionary<string, string>
                {
                    ["path"] = phoneFolder, ["sub"] = f.Folder, ["name"] = f.Name, ["url"] = url, ["onClash"] = onClash,
                    ["modified"] = new DateTimeOffset(f.ModifiedUtc, TimeSpan.Zero).ToUnixTimeMilliseconds().ToString(),
                    ["task"] = id, ["files"] = todo.Count.ToString(), ["bytes"] = total.ToString(), ["folder"] = top,
                    ["size"] = f.Size.ToString(), ["last"] = n == todo.Count - 1 ? "1" : "",
                };
                r = await AskDeviceAsync(device.Id, "fs.pull", args, TimeSpan.FromMinutes(15), ct);
            }
            finally { Unstage(url); }
            if (r.Ok) { Tasks.FileDone(id, f.FullPath, f.Name, f.Size); continue; }
            Tasks.FileFailed(id, f.FullPath, f.Name, ReasonOf(r));
            // Not allowed to add files: the phone shows its note; the rest would fail the same way.
            // (The phone made no task of its own for a run it did not allow, so there is nothing to end there.)
            if (r.Status == "needs-permission") { Tasks.Finish(id, null, "denied"); return; }
        }
        Tasks.Finish(id);
    }

    /// <summary>Tells the phone a run of adds ended early and why ("cancelled" or "network"): its task for the run ends. Never throws.</summary>
    private async Task EndPhoneTaskAsync(string deviceId, string taskId, string reason)
    {
        try
        {
            await AskDeviceAsync(deviceId, "fs.pullEnd", new Dictionary<string, string> { ["task"] = taskId, ["reason"] = reason }, TimeSpan.FromSeconds(20), CancellationToken.None);
        }
        catch (Exception e) { _log.LogDebug("Phone files: could not end the phone's task {Task}: {Error}", taskId, e.Message); }
    }

    /// <summary>A new folder <paramref name="name"/> inside <paramref name="phoneFolder"/> on the phone.</summary>
    public Task<PeerResult> MakePhoneFolderAsync(PairedDevice device, string phoneFolder, string name, CancellationToken ct = default) =>
        AskDeviceAsync(device.Id, "fs.mkdir", new Dictionary<string, string> { ["path"] = phoneFolder, ["name"] = name }, TimeSpan.FromSeconds(20), ct);
}
