using CloudSSD.App;
using CloudSSD.App.Mirror;
using CloudSSD.Core;
using CloudSSD.Mount;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CloudSSD.Tests;

/// <summary>"Files when it is off", the PC's side: what is copied and in which order, what counts as gone, and a whole pass with a made-up rclone.</summary>
public class MirrorTests
{
    /// <summary>A made-up PC in a temp folder: a profile with Documents and AppData, Windows, Program Files, the app's data.</summary>
    private sealed class Pc : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "cssd_mirror_" + Guid.NewGuid().ToString("N")[..8]);
        public MirrorPlaces Places { get; }
        public MirrorSettings Settings { get; } = new();

        public Pc()
        {
            Places = new MirrorPlaces(P("Users", "me"), [P("Users", "me", "Documents")], P("Windows"), [P("Program Files")], P("data"));
            Directory.CreateDirectory(Root);
        }

        public string P(params string[] parts) => Path.Combine([Root, .. parts]);

        public string Write(string text, params string[] parts)
        {
            var path = P(parts);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
            return path;
        }

        public void Dispose() { try { Directory.Delete(Root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }

    [Fact]
    public void The_persons_folders_go_first_and_Windows_last()
    {
        using var pc = new Pc();
        var p = pc.Places;
        Assert.Equal(MirrorScope.TierOwn, MirrorScope.TierOf(pc.P("Users", "me", "Documents", "a.txt"), p));
        Assert.Equal(MirrorScope.TierProfile, MirrorScope.TierOf(pc.P("Users", "me", "notes.txt"), p));
        Assert.Equal(MirrorScope.TierSystem, MirrorScope.TierOf(pc.P("Users", "me", "AppData", "Local", "x.db"), p));
        Assert.Equal(MirrorScope.TierOther, MirrorScope.TierOf(pc.P("Games", "g.bin"), p));
        Assert.Equal(MirrorScope.TierPrograms, MirrorScope.TierOf(pc.P("Program Files", "App", "a.exe"), p));
        Assert.Equal(MirrorScope.TierSystem, MirrorScope.TierOf(pc.P("Windows", "System32", "k.dll"), p));
        Assert.Equal(MirrorScope.TierOther, MirrorScope.TierOf(pc.P("Users", "meToo", "a.txt"), p));     // a name that only starts the same
    }

    [Fact]
    public void The_apps_own_data_links_and_swap_files_are_never_copied()
    {
        using var pc = new Pc();
        var (p, s) = (pc.Places, pc.Settings);
        Assert.True(MirrorScope.SkipFolder(pc.P("data"), FileAttributes.Directory, p, s));                 // the keys to the accounts live here
        Assert.True(MirrorScope.SkipFolder(pc.P("data", "mirror"), FileAttributes.Directory, p, s));
        Assert.True(MirrorScope.SkipFolder(pc.P("Windows", "WinSxS"), FileAttributes.Directory, p, s));
        Assert.True(MirrorScope.SkipFolder(pc.P("Users", "me", "Link"), FileAttributes.Directory | FileAttributes.ReparsePoint, p, s));
        Assert.True(MirrorScope.SkipFolder(@"C:\$Recycle.Bin", FileAttributes.Directory, p, s));
        Assert.False(MirrorScope.SkipFolder(pc.P("Users", "me", "Documents"), FileAttributes.Directory, p, s));
        Assert.False(MirrorScope.SkipFolder(pc.P("Program Files"), FileAttributes.Directory, p, s));

        Assert.True(MirrorScope.SkipFile(@"C:\pagefile.sys", FileAttributes.Hidden | FileAttributes.System));
        Assert.False(MirrorScope.SkipFile(@"C:\Users\me\pagefile.sys", FileAttributes.Normal));           // only the one Windows keeps at the top
        Assert.True(MirrorScope.SkipFile(pc.P("Users", "me", "online-only.docx"), (FileAttributes)0x00400000));
        Assert.Equal("C/Users/me/a b.txt", MirrorScope.CloudPath(@"C:\Users\me\a b.txt"));

        // "Windows and programs too" switched off; a folder the person left out.
        s.IncludeSystem = false;
        Assert.True(MirrorScope.SkipFolder(pc.P("Program Files"), FileAttributes.Directory, p, s));
        Assert.True(MirrorScope.SkipFolder(pc.P("Users", "me", "AppData"), FileAttributes.Directory, p, s));
        s.Excluded.Add(pc.P("Games") + "\\");
        Assert.True(MirrorScope.SkipFolder(pc.P("Games", "Big"), FileAttributes.Directory, p, s));
    }

    [Fact]
    public void A_look_at_the_disk_lists_what_must_go_in_order_and_notices_changes()
    {
        using var pc = new Pc();
        var doc = pc.Write("12345", "Users", "me", "Documents", "big.txt");
        pc.Write("1", "Users", "me", "Documents", "small.txt");
        pc.Write("123", "Users", "me", "notes.txt");
        pc.Write("12", "Program Files", "App", "a.exe");
        pc.Write("secret", "data", "rclone.conf");
        using var state = new MirrorState(pc.P("data", "mirror", "pc.db"));

        var first = MirrorScanner.Scan(state, pc.Places, pc.Settings, [pc.Root], MirrorScope.TierSystem);
        Assert.Equal(4, first.Files);                                             // not the app's own data
        var next = state.Next(MirrorScope.TierSystem, 10, long.MaxValue, 0);
        Assert.Equal(["small.txt", "big.txt", "notes.txt", "a.exe"], next.Select(f => Path.GetFileName(f.Path)));   // tier, then size
        Assert.Equal(2, state.Next(MirrorScope.TierOwn, 10, long.MaxValue, 0).Count);

        state.Uploaded(next);
        Assert.Empty(state.Next(MirrorScope.TierSystem, 10, long.MaxValue, 0));
        Assert.All(state.Totals(), t => Assert.Equal(t.Files, t.UploadedFiles));

        // A changed file must go again; an unchanged one not.
        File.WriteAllText(doc, "1234567");
        MirrorScanner.Scan(state, pc.Places, pc.Settings, [pc.Root], MirrorScope.TierSystem);
        Assert.Equal(["big.txt"], state.Next(MirrorScope.TierSystem, 10, long.MaxValue, 0).Select(f => Path.GetFileName(f.Path)));

        // A file that failed waits until its time to be tried again.
        state.Failed(doc, "in-use", at: 1000);
        Assert.Empty(state.Next(MirrorScope.TierSystem, 10, long.MaxValue, retryFailedBefore: 500));
        Assert.Single(state.Next(MirrorScope.TierSystem, 10, long.MaxValue, retryFailedBefore: 2000));
        Assert.Equal(1, state.FailedByReason()["in-use"]);
    }

    [Fact]
    public void A_deleted_file_is_reported_gone_only_when_its_folder_was_looked_at()
    {
        using var pc = new Pc();
        var gone = pc.Write("1", "Users", "me", "Documents", "gone.txt");
        var never = pc.Write("1", "Users", "me", "Documents", "never-copied.txt");
        var program = pc.Write("12", "Program Files", "App", "a.exe");
        using var state = new MirrorState(pc.P("data", "mirror", "pc.db"));
        MirrorScanner.Scan(state, pc.Places, pc.Settings, [pc.Root], MirrorScope.TierSystem);
        state.Uploaded(state.Next(MirrorScope.TierSystem, 10, long.MaxValue, 0).Where(f => f.Path != never).ToList());

        File.Delete(gone);
        File.Delete(never);
        File.Delete(program);
        // The hourly look covers only the person's folders: the program's file is not called gone by it.
        var quick = MirrorScanner.Scan(state, pc.Places, pc.Settings, [pc.Places.Profile], MirrorScope.TierProfile);
        Assert.Equal([gone], quick.Gone.Select(g => g.Path));                      // had a copy: for the cloud's trash
        Assert.False(quick.GoneHeld);
        Assert.Empty(state.Next(MirrorScope.TierSystem, 10, long.MaxValue, 0));     // the one that never had a copy is forgotten

        var full = MirrorScanner.Scan(state, pc.Places, pc.Settings, [pc.Root], MirrorScope.TierSystem);
        Assert.Contains(program, full.Gone.Select(g => g.Path));
    }

    [Fact]
    public void Very_many_files_gone_at_once_are_held_for_a_question()
    {
        using var pc = new Pc();
        for (int i = 0; i < MirrorScanner.GoneWithoutAsking + 20; i++) pc.Write("x", "Users", "me", "Documents", "set", $"f{i}.txt");
        using var state = new MirrorState(pc.P("data", "mirror", "pc.db"));
        MirrorScanner.Scan(state, pc.Places, pc.Settings, [pc.Root], MirrorScope.TierSystem);
        state.Uploaded(state.Next(MirrorScope.TierSystem, 10_000, long.MaxValue, 0));
        foreach (var f in Directory.GetFiles(pc.P("Users", "me", "Documents", "set"))) File.Delete(f);
        var r = MirrorScanner.Scan(state, pc.Places, pc.Settings, [pc.Root], MirrorScope.TierSystem);
        Assert.True(r.GoneHeld);
        Assert.Equal(MirrorScanner.GoneWithoutAsking + 20, r.Gone.Count);
    }

    /// <summary>A stand-in for rclone: remembers each command and says what the test tells it to.</summary>
    private sealed class FakeRclone
    {
        public List<IReadOnlyList<string>> Calls { get; } = [];
        public List<string> Listed { get; } = [];
        public Func<IReadOnlyList<string>, IReadOnlyList<string>, (int Exit, IEnumerable<string> Lines)>? OnCopy;

        public Task<RcloneResult> Run(IReadOnlyList<string> args, Action<string>? onLine, CancellationToken ct)
        {
            Calls.Add(args.ToList());
            int exit = 0;
            int at = args.ToList().IndexOf("--files-from-raw");
            var names = at >= 0 ? File.ReadAllLines(args[at + 1]) : [];
            if (args[0] == "copy")
            {
                Listed.AddRange(names);
                var (code, lines) = OnCopy?.Invoke(args, names) ?? (0, names.Select(n => Line("info", "Copied (new)", n)));
                exit = code;
                foreach (var l in lines) onLine?.Invoke(l);
            }
            return Task.FromResult(new RcloneResult(exit, "", "", false));
        }

        public static string Line(string level, string msg, string obj) =>
            System.Text.Json.JsonSerializer.Serialize(new { level, msg, @object = obj, time = "2026-11-14T08:00:00+05:30" });
    }

    [Fact]
    public async Task A_batch_goes_to_the_devices_folder_and_failures_keep_their_reason()
    {
        using var pc = new Pc();
        var rclone = new FakeRclone();
        var up = new MirrorUploader(rclone.Run, "gdrive:Cloud-SSD Devices/ab12 MYPC", pc.P("data", "mirror"), new MirrorSettings { UploadLimitMbps = 16 },
            () => new DateTimeOffset(2026, 11, 14, 8, 0, 0, TimeSpan.Zero));
        var root = pc.Root + Path.DirectorySeparatorChar;                               // the made-up PC's "drive"
        var drive = MirrorScope.DriveName(root);
        var (a, b, locked) = (pc.Write("1", "Users", "me", "a.txt"), pc.Write("22", "Users", "me", "sub", "b b.txt"), pc.Write("333", "Users", "me", "locked.pst"));
        var files = new[] { new MirrorFile(a, 1, 1, 1), new MirrorFile(b, 2, 1, 1), new MirrorFile(locked, 3, 1, 1) };

        var ok = await up.CopyAsync(root, files, default);
        Assert.Equal(3, ok.Done.Count);
        var args = rclone.Calls[0];
        Assert.Equal(["copy", root, "gdrive:Cloud-SSD Devices/ab12 MYPC/files/" + drive], args.Take(3));
        Assert.Contains("--no-traverse", args);
        Assert.Equal("gdrive:Cloud-SSD Devices/ab12 MYPC/trash/2026-11-14/" + drive, args[args.ToList().IndexOf("--backup-dir") + 1]);   // a replaced copy is kept
        Assert.Equal("2M", args[args.ToList().IndexOf("--bwlimit") + 1]);                // 16 megabits = 2 megabytes a second
        Assert.Equal(["Users/me/a.txt", "Users/me/sub/b b.txt", "Users/me/locked.pst"], rclone.Listed);

        // One file is open in a program: rclone ends with an error; only what it said it copied counts.
        rclone.OnCopy = (_, names) => (1, new[]
        {
            FakeRclone.Line("info", "Copied (replaced existing)", names[0]),
            FakeRclone.Line("error", "Failed to copy: The process cannot access the file because it is being used by another process.", names[2])
        });
        var part = await up.CopyAsync(root, files, default);
        Assert.Equal([a], part.Done.Select(f => f.Path));                                // b said nothing: it stays to be tried again
        Assert.Equal((locked, "in-use"), (part.Failed.Single().File.Path, part.Failed.Single().Reason));
        Assert.Null(part.Error);                                                         // a file's failure is not the batch's
        Assert.False(part.UploadLimit);

        // Google's daily upload limit: the pass must stop, not hammer on.
        rclone.OnCopy = (_, _) => (1, new[] { FakeRclone.Line("error", "googleapi: Error 403: User rate limit exceeded., userRateLimitExceeded: upload limit", "") });
        var limit = await up.CopyAsync(root, files, default);
        Assert.True(limit.UploadLimit);
        Assert.Empty(limit.Done);

        // Too many requests on the shared key: not an upload limit, and told apart from other errors.
        rclone.OnCopy = (_, _) => (1, new[] { FakeRclone.Line("error", "Failed to create file system: googleapi: Error 403: Quota exceeded for quota metric 'Queries', RATE_LIMIT_EXCEEDED", "") });
        var rate = await up.CopyAsync(root, files, default);
        Assert.True(rate.RateLimit);
        Assert.False(rate.UploadLimit);

        // Deleted on the PC: the copies move to the trash folder inside the cloud.
        Assert.True(await up.TrashAsync(root, [a], default));
        Assert.Equal(["move", "gdrive:Cloud-SSD Devices/ab12 MYPC/files/" + drive, "gdrive:Cloud-SSD Devices/ab12 MYPC/trash/2026-11-14/" + drive], rclone.Calls[^1].Take(3));
    }

    /// <summary>The rclone that ships with the app (tools\rclone), or null on a machine without it.</summary>
    private static string? RealRclone()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var exe = Path.Combine(d.FullName, "tools", "rclone", "rclone.exe");
            if (File.Exists(exe)) return exe;
        }
        return null;
    }

    [Fact]
    public async Task The_real_rclone_understands_the_commands_and_its_log_is_read_right()
    {
        if (RealRclone() is not { } exe) return;                                         // nothing to check without the program
        using var pc = new Pc();
        var a = pc.Write("one", "Users", "me", "Documents", "a.txt");
        var b = pc.Write("two", "Users", "me", "Documents", "sub", "b b.txt");
        var cloud = pc.P("cloud");                                                        // a folder stands in for the account
        var runner = new RcloneRunner(exe, new Dictionary<string, string?> { ["RCLONE_CONFIG"] = pc.P("none.conf") });
        RcloneRun run = (args, onLine, ct) => runner.RunAsync(args, TimeSpan.FromMinutes(2), ct, onStdErrLine: onLine);
        var up = new MirrorUploader(run, cloud, pc.P("work"), new MirrorSettings());
        var root = pc.Root + Path.DirectorySeparatorChar;
        MirrorFile F(string path) => new(path, new FileInfo(path).Length, File.GetLastWriteTimeUtc(path).Ticks, 1);
        var drive = MirrorScope.DriveName(root);

        var first = await up.CopyAsync(root, [F(a), F(b), new MirrorFile(pc.P("Users", "me", "not-there.txt"), 1, 1, 1)], default);
        Assert.Equal([a, b], first.Done.Select(f => f.Path).Order());
        Assert.Equal("missing", Assert.Single(first.Failed).Reason);                    // gone since the look at the disk: no copy, and said so
        Assert.Equal("one", File.ReadAllText(Path.Combine(cloud, "files", drive, "Users", "me", "Documents", "a.txt")));
        Assert.Equal("two", File.ReadAllText(Path.Combine(cloud, "files", drive, "Users", "me", "Documents", "sub", "b b.txt")));

        // Unchanged files cost nothing and count as there; a changed one leaves its old copy in today's trash.
        File.WriteAllText(a, "one, changed");
        var second = await up.CopyAsync(root, [F(a), F(b)], default);
        Assert.Equal(2, second.Done.Count);
        Assert.Null(second.Error);
        Assert.Equal("one, changed", File.ReadAllText(Path.Combine(cloud, "files", drive, "Users", "me", "Documents", "a.txt")));
        var trashed = Directory.GetFiles(Path.Combine(cloud, "trash"), "a.txt", SearchOption.AllDirectories);
        Assert.Equal("one", File.ReadAllText(Assert.Single(trashed)));

        // Deleted on the PC: the copy moves to the trash; the state file is written at the top.
        Assert.True(await up.TrashAsync(root, [b], default));
        Assert.False(File.Exists(Path.Combine(cloud, "files", drive, "Users", "me", "Documents", "sub", "b b.txt")));
        Assert.Single(Directory.GetFiles(Path.Combine(cloud, "trash"), "b b.txt", SearchOption.AllDirectories));
        Assert.True(await up.WriteTextAsync("device.json", "{}", default));
        Assert.True(File.Exists(Path.Combine(cloud, "device.json")));
    }

    private static async Task WithService(Func<AppHost, DeviceMirrorService, Pc, FakeRclone, Task> body)
    {
        using var pc = new Pc();
        await using var host = AppHost.Create(new AppPaths(pc.P("data")));
        host.Settings.MobileBridge.PcId = "ab12cd34ef56ab78";
        host.Settings.MobileBridge.PairedDevices.Add(new PairedDevice { Id = "phone001", Name = "Pixel", WhenOffPc = true, WhenOffDevice = true });
        var rclone = new FakeRclone();
        var svc = host.Mirror;
        svc.RunOverride = rclone.Run;
        svc.AccountOverride = "gdrive";
        svc.PlacesOverride = pc.Places;
        svc.DrivesOverride = [pc.Root];
        svc.OnBatteryOverride = () => false;
        svc.PlayingOverride = () => false;
        await body(host, svc, pc, rclone);
    }

    [Fact]
    public Task A_pass_copies_the_persons_files_and_writes_the_state_file() => WithService(async (host, svc, pc, rclone) =>
    {
        pc.Write("hello", "Users", "me", "Documents", "a.txt");
        pc.Write("x", "Users", "me", "pic.jpg");
        pc.Write("key", "data", "rclone.conf.key");
        Assert.True(svc.Active);

        await svc.TickAsync();

        Assert.Contains(rclone.Listed, n => n.EndsWith("Documents/a.txt"));
        Assert.DoesNotContain(rclone.Listed, n => n.Contains("rclone.conf") || n.Contains("/data/"));   // never the app's own data
        var copy = rclone.Calls.First(c => c[0] == "copy");
        Assert.StartsWith("gdrive:Cloud-SSD Devices/ab12cd34ef56ab78 ", copy[2]);
        Assert.Contains(rclone.Calls, c => c[0] == "copyto" && c[2].EndsWith("/device.json"));
        var st = svc.Status;
        Assert.Equal("idle", st.State);
        Assert.Equal(st.Tiers.Sum(t => t.Files), st.Tiers.Sum(t => t.UploadedFiles));
        Assert.Equal("gdrive", st.Account);
        Assert.NotNull(st.LastSync);

        // Nothing changed: the next tick has nothing to do and runs no rclone.
        int calls = rclone.Calls.Count;
        await svc.TickAsync();
        Assert.Equal(calls, rclone.Calls.Count);
    });

    [Fact]
    public Task With_only_some_folders_chosen_nothing_else_is_copied() => WithService(async (host, svc, pc, rclone) =>
    {
        pc.Write("in", "Users", "me", "Documents", "Try", "a.txt");
        pc.Write("out", "Users", "me", "Documents", "b.txt");
        host.Settings.Mirror.OnlyFolders.Add(pc.P("Users", "me", "Documents", "Try"));
        await svc.TickAsync();
        Assert.EndsWith("Documents/Try/a.txt", Assert.Single(rclone.Listed));
    });

    [Fact]
    public Task It_does_nothing_without_both_yeses_and_stops_for_a_game_battery_or_pause() => WithService(async (host, svc, pc, rclone) =>
    {
        pc.Write("hello", "Users", "me", "Documents", "a.txt");
        var phone = host.Settings.MobileBridge.PairedDevices.Single();

        phone.WhenOffDevice = false;                       // only the PC said yes
        await svc.TickAsync();
        Assert.Equal("off", svc.Status.State);
        Assert.Empty(rclone.Calls);
        phone.WhenOffDevice = true;

        svc.PlayingOverride = () => true;
        await svc.TickAsync();
        Assert.Equal(("paused", "game"), (svc.Status.State, svc.Status.Reason));
        svc.PlayingOverride = () => false;

        svc.OnBatteryOverride = () => true;
        await svc.TickAsync();
        Assert.Equal(("paused", "battery"), (svc.Status.State, svc.Status.Reason));
        svc.OnBatteryOverride = () => false;

        svc.SetPaused(true);
        await svc.TickAsync();
        Assert.Equal(("paused", "person"), (svc.Status.State, svc.Status.Reason));
        Assert.Empty(rclone.Calls);

        svc.SetPaused(false);
        await svc.TickAsync();
        Assert.NotEmpty(rclone.Calls);
    });

    [Fact]
    public Task The_upload_limit_makes_it_wait_and_a_deleted_file_goes_to_the_trash() => WithService(async (host, svc, pc, rclone) =>
    {
        var a = pc.Write("hello", "Users", "me", "Documents", "a.txt");
        rclone.OnCopy = (_, _) => (1, new[] { FakeRclone.Line("error", "upload limit reached", "") });
        await svc.TickAsync();
        Assert.Equal(("paused", "upload-limit"), (svc.Status.State, svc.Status.Reason));
        int calls = rclone.Calls.Count;
        await svc.TickAsync();                              // still waiting: no new try
        Assert.Equal(calls, rclone.Calls.Count);

        rclone.OnCopy = null;
        await svc.SyncNow();                                // the person says "Sync now"
        Assert.Equal("idle", svc.Status.State);

        File.Delete(a);
        await svc.SyncNow();
        Assert.Contains(rclone.Calls, c => c[0] == "move" && c[2].Contains("/trash/"));
        Assert.Equal(0, svc.Status.Tiers.Sum(t => t.Files));
    });
}
