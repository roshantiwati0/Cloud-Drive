using System.Diagnostics;
using System.Text.Json;
using CloudSSD.Core;
using CloudSSD.Diagnostics;
using CloudSSD.Mount;
using Microsoft.Extensions.Logging;

namespace CloudSSD.App;

/// <summary>A program whose folder was sent to the cloud drive: where it was, where it is now, and how far the move got.</summary>
public sealed record MovedApp(string Name, string LocalDir, string CloudDir, long Bytes, DateTime At, string Stage);

/// <summary>How far a move is: a line for the person, and the bytes done of the total (0 of 0 while that is not known).</summary>
public sealed record MoveProgress(string Step, long DoneBytes, long TotalBytes, double BytesPerSecond = 0);

public sealed record MoveResult(bool Success, string Message);

/// <summary>
/// Sends a program's folder from this PC to the cloud drive and brings it back (Apps page).
/// <para>To the cloud: the folder is <b>copied</b> first and counted on the other side; only then the folder on the PC
/// is renamed aside, a link to the cloud copy takes its place (so the Start menu, the registry and the program's own
/// updater still find it where it always was), the program file is read through the link, and the folder set aside
/// is deleted. Whatever goes wrong before that last step leaves the program on the PC exactly as it was.</para>
/// <para>Back to the PC: copied next to the link, counted, then the link is replaced by the real folder.</para>
/// Every step is written to apps-moved.json first, so a move cut off by a crash or a power cut is undone at the next start.
/// </summary>
public sealed class AppMover
{
    public const string OldSuffix = ".cloudssd-old", NewSuffix = ".cloudssd-new";
    private const string Linking = "linking", Done = "done";

    private readonly string _stateFile;
    private readonly ILogger _log;
    private readonly Func<IReadOnlyList<MountSession>> _mounts;
    private readonly Func<string, string, bool> _makeLink;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _lock = new();
    private List<MovedApp> _moved;

    public AppMover(AppHost host, ILogger<AppMover> log)
        : this(Path.Combine(host.Paths.DataRoot, "apps-moved.json"), log,
            () => host.Mounts.Sessions.Where(s => s.State == MountState.Mounted && !s.Definition.ReadOnly).ToList(),
            MakeLink) { }

    internal AppMover(string stateFile, ILogger log, Func<IReadOnlyList<MountSession>> mounts, Func<string, string, bool> makeLink)
    {
        _stateFile = stateFile;
        _log = log;
        _mounts = mounts;
        _makeLink = makeLink;
        _moved = Load();
        Recover();
    }

    /// <summary>True when Windows will show its own "allow?" box for the link (no Developer Mode, not an administrator).</summary>
    public static bool LinkNeedsPermission => !DependencyChecker.CanCreateSymlinks();

    /// <summary>
    /// Puts a folder link at <paramref name="link"/> that leads to <paramref name="target"/>. A link to a cloud drive
    /// has to be a symbolic link (Windows refuses a junction there: "local volumes are required"), and without
    /// Developer Mode only an administrator may make one: then Windows is asked once, with its own Yes/No box.
    /// </summary>
    internal static bool MakeLink(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        if (link.Contains('"') || target.Contains('"') || link.Contains('%') || target.Contains('%')) return false;
        try
        {
            using var p = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /D \"{link}\" \"{target}\"")
                { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden });
            p?.WaitForExit(30_000);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { return false; }   // "No" on Windows' box
        return IsLink(link);
    }

    /// <summary>For tests: a plain folder stands in for the cloud drive (no rclone behind it).</summary>
    internal string? PlainCloudRoot { get; set; }

    public bool Busy => _gate.CurrentCount == 0;

    public IReadOnlyList<MovedApp> Moved
    {
        get { lock (_lock) return _moved.Where(m => m.Stage == Done).ToList(); }
    }

    /// <summary>The record of a program that lives on the cloud now, by the folder it has on this PC.</summary>
    public MovedApp? Find(string localDir)
    {
        lock (_lock) return _moved.FirstOrDefault(m => m.Stage == Done && Same(m.LocalDir, localDir));
    }

    // ------------------------------------------------------------------ checks

    /// <summary>Why this program cannot go to the cloud right now (plain words), or null when it can.</summary>
    public string? WhyNotNow(AppItem app)
    {
        if (app.WhyNot != null) return app.WhyNot;
        if (app.Where == AppPlace.Cloud || Find(app.InstallDir) != null) return "It is on the cloud drive already.";
        if (!Directory.Exists(app.InstallDir)) return "Its folder is not there any more.";
        if (IsLink(app.InstallDir)) return "Its folder is a link to another place already.";
        if (CloudRoot() is null) return "No cloud drive is connected. Connect one first (Cloud Drives).";
        if (RunningFrom(app.InstallDir) is { } running) return $"{running} is running from it. Close it first.";
        if (!CanRename(app.InstallDir)) return "Windows protects this folder. Start Cloud-SSD as administrator once to move this program.";
        return null;
    }

    private (string Root, MountSession? Mount)? CloudRoot()
    {
        if (PlainCloudRoot is { } plain) return Directory.Exists(plain) ? (plain, null) : null;
        var m = _mounts().FirstOrDefault(s => Directory.Exists(s.Root));
        return m is null ? null : (m.Root, m);
    }

    /// <summary>The name of a program running from this folder, or null. Processes Windows hides from us are not counted.</summary>
    internal static string? RunningFrom(string dir)
    {
        var prefix = dir.TrimEnd('\\') + "\\";
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.MainModule?.FileName is { } file && file.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return p.ProcessName;
            }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException) { }
            finally { p.Dispose(); }
        }
        return null;
    }

    /// <summary>True when this user may rename the folder: that needs the right to write in the folder above it.</summary>
    internal static bool CanRename(string dir)
    {
        try
        {
            var parent = Path.GetDirectoryName(dir.TrimEnd('\\'));
            if (parent is null) return false;
            var probe = Path.Combine(parent, ".cloudssd-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(probe);
            Directory.Delete(probe);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }

    internal static bool IsLink(string dir)
    {
        try { return Directory.Exists(dir) && new DirectoryInfo(dir).Attributes.HasFlag(FileAttributes.ReparsePoint); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }

    // ------------------------------------------------------------------ PC → cloud

    public async Task<MoveResult> ToCloudAsync(AppItem app, IProgress<MoveProgress>? progress, CancellationToken ct)
    {
        if (!await _gate.WaitAsync(0)) return new(false, "Another program is being moved. Try again when it is done.");
        string local = app.InstallDir.TrimEnd('\\'), old = local + OldSuffix;
        try
        {
            if (WhyNotNow(app) is { } why) return new(false, why);
            if (Directory.Exists(old)) return new(false, $"A folder left from an earlier move is in the way: {old}. Look into it, then remove it.");
            var (root, mount) = CloudRoot()!.Value;

            progress?.Report(new("Measuring the folder…", 0, 0));
            var (files, bytes) = await Task.Run(() => Count(local, ct), ct);
            if (files == 0) return new(false, "The folder is empty.");

            string cloud = FreeCloudDir(root, Path.GetFileName(local));
            _log.LogInformation("Moving {App} to the cloud: {Files} files, {Size} → {Target}", app.Name, files, ByteSize.Format(bytes), cloud);
            if (!await CopyAsync(local, cloud, toCloud: true, root, mount, bytes, "Copying to the cloud drive…", progress, ct))
                return new(false, $"{app.Name} could not be copied to the cloud drive. It is still on this PC, nothing changed. Try again: what is there already is not sent twice.");

            progress?.Report(new("Checking the copy…", bytes, bytes));
            var (cloudFiles, cloudBytes) = await CountCloudAsync(cloud, root, mount, ct);
            if (cloudFiles != files || cloudBytes != bytes)
            {
                _log.LogWarning("Move of {App} stopped: the cloud copy has {CloudFiles} files / {CloudBytes} bytes, the PC has {Files} / {Bytes}", app.Name, cloudFiles, cloudBytes, files, bytes);
                return new(false, $"The copy on the cloud drive is not complete ({cloudFiles} of {files} files). {app.Name} is still on this PC, nothing changed.");
            }

            // From here on the folder on the PC changes: write it down first, so a crash is undone at the next start.
            Remember(new MovedApp(app.Name, local, cloud, bytes, DateTime.Now, Linking));
            if (RunningFrom(local) is { } running) { Forget(local); return new(false, $"{running} was started meanwhile. Close it and try again; the copy on the cloud drive is kept."); }
            try { Directory.Move(local, old); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Forget(local);
                return new(false, "Windows did not let the folder be set aside: " + e.Message + " The program is still on this PC.");
            }
            if (!_makeLink(local, cloud))
            {
                Undo(local);
                return new(false, $"Windows did not make the link (it needs your Yes). {app.Name} is still on this PC; the copy on the cloud drive is kept, so the next try is quick.");
            }

            if (!Readable(app.ExePath))
            {
                Undo(local);
                return new(false, $"{app.Name} could not be read from the cloud drive, so it was put back on this PC.");
            }
            Remember(new MovedApp(app.Name, local, cloud, bytes, DateTime.Now, Done));
            progress?.Report(new("Freeing the space on this PC…", bytes, bytes));
            await Task.Run(() => DeleteTree(old), CancellationToken.None);
            _log.LogInformation("{App} is on the cloud drive now ({Size} freed on this PC)", app.Name, ByteSize.Format(bytes));
            return new(true, $"{app.Name} is on the cloud drive now. {ByteSize.Format(bytes)} are free on this PC.");
        }
        catch (OperationCanceledException)
        {
            return new(false, $"Stopped. {app.Name} is still on this PC; what was copied so far stays on the cloud drive and is not sent again next time.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or HttpRequestException or RcException or InvalidOperationException)
        {
            _log.LogWarning(e, "Could not move {App} to the cloud", app.Name);
            return new(false, "Could not move it to the cloud: " + e.Message);
        }
        finally { _gate.Release(); }
    }

    // ------------------------------------------------------------------ cloud → PC

    public async Task<MoveResult> ToPcAsync(MovedApp m, bool keepCloudCopy, IProgress<MoveProgress>? progress, CancellationToken ct)
    {
        if (!await _gate.WaitAsync(0)) return new(false, "Another program is being moved. Try again when it is done.");
        string local = m.LocalDir, fresh = local + NewSuffix;
        try
        {
            if (CloudRoot() is not { } c) return new(false, "The cloud drive is not connected. Connect it first (Cloud Drives).");
            if (!Directory.Exists(m.CloudDir)) return new(false, $"The folder of {m.Name} is not on the cloud drive any more: {m.CloudDir}");
            if (!IsLink(local) && Directory.Exists(local)) return new(false, $"{local} is a real folder again already.");
            if (RunningFrom(local) is { } running) return new(false, $"{running} is running from it. Close it first.");

            progress?.Report(new("Measuring the folder…", 0, 0));
            var (files, bytes) = await CountCloudAsync(m.CloudDir, c.Root, c.Mount, ct);
            if (files == 0) return new(false, $"The folder of {m.Name} on the cloud drive is empty.");
            long free = DiskInfo.FreeSpace(Path.GetPathRoot(local)!).FreeForUser;
            if (free < bytes + ByteSize.GiB)
                return new(false, $"Not enough free space on this PC: {m.Name} needs {ByteSize.Format(bytes)}, {ByteSize.Format(free)} are free.");

            if (Directory.Exists(fresh)) await Task.Run(() => DeleteTree(fresh), ct);
            if (!await CopyAsync(m.CloudDir, fresh, toCloud: false, c.Root, c.Mount, bytes, "Copying back to this PC…", progress, ct))
                return new(false, $"{m.Name} could not be copied back. It still runs from the cloud drive, nothing changed.");
            var (gotFiles, gotBytes) = await Task.Run(() => Count(fresh, ct), ct);
            if (gotFiles != files || gotBytes != bytes)
                return new(false, $"The copy on this PC is not complete ({gotFiles} of {files} files). {m.Name} still runs from the cloud drive.");

            if (RunningFrom(local) is { } late) return new(false, $"{late} was started meanwhile. Close it and try again.");
            if (IsLink(local)) Directory.Delete(local);       // removes the link only, never what it points at
            Directory.Move(fresh, local);
            Forget(local);
            if (!keepCloudCopy)
            {
                progress?.Report(new("Removing the copy on the cloud drive…", bytes, bytes));
                await DeleteCloudAsync(m.CloudDir, c.Root, c.Mount, CancellationToken.None);
            }
            _log.LogInformation("{App} is back on this PC ({Size})", m.Name, ByteSize.Format(bytes));
            return new(true, $"{m.Name} is back on this PC ({ByteSize.Format(bytes)})." + (keepCloudCopy ? " The copy on the cloud drive was kept." : ""));
        }
        catch (OperationCanceledException)
        {
            return new(false, $"Stopped. {m.Name} still runs from the cloud drive, nothing changed.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or HttpRequestException or RcException or InvalidOperationException)
        {
            _log.LogWarning(e, "Could not bring {App} back", m.Name);
            return new(false, "Could not bring it back: " + e.Message);
        }
        finally { _gate.Release(); }
    }

    // ------------------------------------------------------------------ copying and counting

    /// <summary>
    /// Copies a whole folder between the PC and the cloud. With rclone behind the drive the files go straight to the
    /// cloud (not through the drive's cache on this PC, which would need the program's size a second time) as one job
    /// whose progress is read every two seconds; files that are there already are skipped, so a second try continues.
    /// </summary>
    private async Task<bool> CopyAsync(string from, string to, bool toCloud, string cloudRoot, MountSession? mount, long totalBytes, string step,
        IProgress<MoveProgress>? progress, CancellationToken ct)
    {
        progress?.Report(new(step, 0, totalBytes));
        if (mount?.Rc is not { } rc)
        {
            long done = 0;
            await Task.Run(() => CopyTree(from, to, n => progress?.Report(new(step, done += n, totalBytes)), ct), ct);
            return true;
        }
        string remote(string path) => Spec(mount, cloudRoot, path);
        var job = await rc.CallAsync("sync/copy", new
        {
            srcFs = toCloud ? from : remote(from), dstFs = toCloud ? remote(to) : to, createEmptySrcDirs = true, _async = true,
        }, ct);
        long id = job["jobid"]?.GetValue<long>() ?? throw new InvalidOperationException("rclone did not start the copy.");
        try
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
                var status = await rc.CallAsync("job/status", new { jobid = id }, ct);
                if (status["finished"]?.GetValue<bool>() == true)
                {
                    bool ok = status["success"]?.GetValue<bool>() == true;
                    if (!ok) _log.LogWarning("rclone copy job {Job} failed: {Error}", id, Redactor.Redact(status["error"]?.GetValue<string>() ?? ""));
                    // The drive letter must show the new files before anything is started from them.
                    if (toCloud) await RefreshAsync(rc, cloudRoot, to);
                    return ok;
                }
                try
                {
                    var stats = await rc.CallAsync("core/stats", new { group = "job/" + id }, ct);
                    progress?.Report(new(step, stats["bytes"]?.GetValue<long>() ?? 0, totalBytes, stats["speed"]?.GetValue<double>() ?? 0));
                }
                catch (Exception e) when (e is RcException or HttpRequestException or InvalidOperationException or FormatException) { }
            }
        }
        catch (OperationCanceledException)
        {
            try { await rc.CallAsync("job/stop", new { jobid = id }, CancellationToken.None); }
            catch (Exception e) when (e is RcException or HttpRequestException or TaskCanceledException) { }
            throw;
        }
    }

    private async Task<(long Files, long Bytes)> CountCloudAsync(string cloudDir, string cloudRoot, MountSession? mount, CancellationToken ct)
    {
        if (mount?.Rc is not { } rc) return await Task.Run(() => Count(cloudDir, ct), ct);
        // Asked of the cloud itself, not of the drive letter: the drive may still show what it listed before the copy.
        var size = await rc.CallAsync("operations/size", new { fs = Spec(mount, cloudRoot, cloudDir) }, ct, TimeSpan.FromMinutes(10));
        return (size["count"]?.GetValue<long>() ?? 0, size["bytes"]?.GetValue<long>() ?? 0);
    }

    private static async Task DeleteCloudAsync(string cloudDir, string cloudRoot, MountSession? mount, CancellationToken ct)
    {
        if (mount?.Rc is not { } rc) { DeleteTree(cloudDir); return; }
        await rc.CallAsync("operations/purge", new { fs = mount.Definition.RemoteSpec, remote = Path.GetRelativePath(cloudRoot, cloudDir).Replace('\\', '/') }, ct, TimeSpan.FromMinutes(10));
        await RefreshAsync(rc, cloudRoot, cloudDir);
    }

    /// <summary>
    /// What was copied or deleted went straight to the cloud, past the drive letter, and the drive remembers folder
    /// lists for days: it is told to read the folder above again (measured: without this a deleted program's folder
    /// stayed visible on Z: after the cloud no longer had it).
    /// </summary>
    private static async Task RefreshAsync(RcClient rc, string cloudRoot, string cloudDir)
    {
        string parent = Path.GetRelativePath(cloudRoot, Path.GetDirectoryName(cloudDir.TrimEnd('\\')) ?? cloudRoot).Replace('\\', '/');
        if (parent == ".") parent = "";
        try
        {
            await rc.CallAsync("vfs/forget", new { dir = parent }, CancellationToken.None);
            await rc.CallAsync("vfs/refresh", new { dir = parent }, CancellationToken.None, TimeSpan.FromMinutes(2));
        }
        catch (Exception e) when (e is RcException or HttpRequestException or TaskCanceledException) { }
    }

    /// <summary>"Z:\Apps\Game" → "CloudSSD:Apps/Game": the same folder as rclone names it.</summary>
    private static string Spec(MountSession mount, string cloudRoot, string path)
    {
        string spec = mount.Definition.RemoteSpec, rel = Path.GetRelativePath(cloudRoot, path).Replace('\\', '/');
        return spec + (spec.EndsWith(':') ? "" : "/") + rel;
    }

    /// <summary>Files and bytes below a folder. Links inside it are not followed (rclone does not copy them either).</summary>
    internal static (long Files, long Bytes) Count(string dir, CancellationToken ct = default)
    {
        long files = 0, bytes = 0;
        foreach (var f in new DirectoryInfo(dir).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint }))
        {
            ct.ThrowIfCancellationRequested();
            files++;
            bytes += f.Length;
        }
        return (files, bytes);
    }

    private static void CopyTree(string from, string to, Action<long> copied, CancellationToken ct)
    {
        Directory.CreateDirectory(to);
        foreach (var f in new DirectoryInfo(from).EnumerateFiles())
        {
            ct.ThrowIfCancellationRequested();
            if (f.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
            var target = Path.Combine(to, f.Name);
            if (!File.Exists(target) || new FileInfo(target).Length != f.Length) f.CopyTo(target, overwrite: true);
            copied(f.Length);
        }
        foreach (var d in new DirectoryInfo(from).EnumerateDirectories())
            if (!d.Attributes.HasFlag(FileAttributes.ReparsePoint)) CopyTree(d.FullName, Path.Combine(to, d.Name), copied, ct);
    }

    /// <summary>Deletes a real folder with everything in it; read-only files too. Never follows a link out of it.</summary>
    private static void DeleteTree(string dir)
    {
        if (!Directory.Exists(dir)) return;
        if (IsLink(dir)) { Directory.Delete(dir); return; }
        foreach (var f in new DirectoryInfo(dir).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint }))
            if (f.IsReadOnly) f.IsReadOnly = false;
        Directory.Delete(dir, recursive: true);
    }

    private static bool Readable(string file)
    {
        try
        {
            using var s = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return s.Length == 0 || s.ReadByte() >= 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>"Apps\Name" on the cloud drive; "Name (2)" when another program already has that name there.</summary>
    private string FreeCloudDir(string cloudRoot, string name)
    {
        var apps = Path.Combine(cloudRoot, AppCatalog.CloudFolder);
        Directory.CreateDirectory(apps);
        var dir = Path.Combine(apps, name);
        // A folder of that name with no record is what a stopped move of this program left: the copy continues into it.
        bool Taken(string d) { lock (_lock) return _moved.Any(m => Same(m.CloudDir, d)); }
        for (int i = 2; Taken(dir); i++) dir = Path.Combine(apps, $"{name} ({i})");
        return dir;
    }

    // ------------------------------------------------------------------ the record, and undoing a cut-off move

    /// <summary>Puts the folder set aside back where it was (the link goes; the cloud copy stays).</summary>
    private void Undo(string local)
    {
        try
        {
            string old = local + OldSuffix;
            if (Directory.Exists(old))
            {
                if (IsLink(local)) Directory.Delete(local);
                if (!Directory.Exists(local)) Directory.Move(old, local);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _log.LogError(e, "Could not put {Folder} back; its files are in {Old}", local, local + OldSuffix);
        }
        Forget(local);
    }

    /// <summary>At the start: a move that was cut off between "set aside" and "done" is undone.</summary>
    private void Recover()
    {
        List<MovedApp> open;
        lock (_lock) open = _moved.Where(m => m.Stage == Linking).ToList();
        foreach (var m in open)
        {
            _log.LogWarning("The move of {App} to the cloud was cut off; putting it back on this PC", m.Name);
            Undo(m.LocalDir);
        }
        // Cut off after "done" but before the folder set aside was deleted: the link works, so only the space is still taken.
        foreach (var m in Moved)
        {
            string old = m.LocalDir + OldSuffix;
            if (!Directory.Exists(old) || !IsLink(m.LocalDir)) continue;
            _ = Task.Run(() =>
            {
                try { DeleteTree(old); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _log.LogWarning(e, "Could not remove {Old}", old); }
            });
        }
    }

    private void Remember(MovedApp m)
    {
        lock (_lock)
        {
            _moved.RemoveAll(x => Same(x.LocalDir, m.LocalDir));
            _moved.Add(m);
            Save();
        }
    }

    private void Forget(string localDir)
    {
        lock (_lock)
        {
            _moved.RemoveAll(x => Same(x.LocalDir, localDir));
            Save();
        }
    }

    private List<MovedApp> Load()
    {
        try
        {
            return File.Exists(_stateFile) ? JsonSerializer.Deserialize<List<MovedApp>>(File.ReadAllText(_stateFile)) ?? [] : [];
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            _log.LogWarning(e, "apps-moved.json could not be read");
            return [];
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_stateFile)!);
            var tmp = _stateFile + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_moved, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, _stateFile, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _log.LogWarning(e, "apps-moved.json could not be written"); }
    }

    private static bool Same(string a, string b) => string.Equals(a.TrimEnd('\\'), b.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
}
