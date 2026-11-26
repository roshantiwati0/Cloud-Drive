using System.Net;
using System.Text.Json;
using CloudSSD.App.Remote;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CloudSSD.Tests;

/// <summary>The laptop's "Other PCs" client against a real bridge: pairing, lists kept for when it is away, NEW badges, queued uploads.</summary>
public class RemotePcClientTests
{
    /// <summary>Lets a test switch the network off, or cut uploads after some pieces.</summary>
    private sealed class ToggleHandler : DelegatingHandler
    {
        public volatile bool Offline;
        public int WritesAllowed = int.MaxValue;
        private int _writes;
        public ToggleHandler() : base(new SocketsHttpHandler { UseProxy = false }) { }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (Offline) throw new HttpRequestException("offline (test)");
            if (request.RequestUri!.AbsolutePath == "/api/fs/write" && Interlocked.Increment(ref _writes) > WritesAllowed) throw new HttpRequestException("cut off (test)");
            return base.SendAsync(request, ct);
        }
    }

    private const string OwnPcId = "ffffffffffffffff";

    private static async Task WithClient(Func<BridgeEnv, RemotePcManager, ToggleHandler, string, Task> body)
    {
        var dir = Path.Combine(Path.GetTempPath(), "cssd_laptop_" + Guid.NewGuid().ToString("N"));
        var toggle = new ToggleHandler();
        try
        {
            await BridgeHarness.With(async env =>
            {
                await using var mgr = new RemotePcManager(dir, () => OwnPcId, () => null, NullLogger.Instance, () => toggle);
                await body(env, mgr, toggle, dir);
            });
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    /// <summary>Coming back starts sending in the background: wait until nothing is being sent.</summary>
    private static async Task Settle(RemotePcSession s)
    {
        for (int i = 0; i < 100 && s.Uploads.Any(u => u.State == "running"); i++) await Task.Delay(50);
    }

    private static async Task<RemotePcSession> Pair(BridgeEnv env, RemotePcManager mgr) =>
        await mgr.AddPcAsync(env.Base.Replace("http://", ""), env.Bridge.PairingCode);

    [Fact]
    public Task Pairs_as_a_laptop_lists_a_folder_and_keeps_the_key_protected() => WithClient(async (env, mgr, _, dir) =>
    {
        await File.WriteAllTextAsync(Path.Combine(env.Cloud, "notes.txt"), "hello");
        var s = await Pair(env, mgr);
        Assert.True(s.IsOnline);

        var dev = env.Host.Settings.MobileBridge.PairedDevices.Single();
        Assert.Contains(dev.Kind, new[] { "laptop", "desktop" });
        Assert.Equal(OwnPcId, dev.PeerPcId);

        var json = await File.ReadAllTextAsync(Path.Combine(dir, "pcs.json"));
        var token = mgr.Store.Token(s.Pc)!;
        Assert.DoesNotContain(token, json);                       // only the DPAPI blob is on disk

        var roots = await s.GetRootsAsync();
        Assert.False(roots.FromCache);
        Assert.Contains(roots.Value, r => r.Id == "cloudssd");
        Assert.Contains(roots.Value, r => r.Id == "received");      // the folder files sent to that PC land in

        var list = await s.ListAsync("cloudssd:");
        Assert.False(list.FromCache);
        Assert.Contains(list.Value.Entries, e => e.Name == "notes.txt");
    });

    [Fact]
    public Task Refuses_to_pair_with_itself() => WithClient(async (env, _, _, _) =>
    {
        await using var self = new RemotePcManager(Path.Combine(Path.GetTempPath(), "cssd_self_" + Guid.NewGuid().ToString("N")),
            () => env.Host.Settings.MobileBridge.PcId, () => null, NullLogger.Instance);
        var e = await Assert.ThrowsAsync<RemotePcException>(() => self.AddPcAsync(env.Base, env.Bridge.PairingCode));
        Assert.Equal(RemoteErrorKind.Self, e.Kind);
    });

    [Fact]
    public Task A_wrong_code_is_refused_clearly() => WithClient(async (env, mgr, _, _) =>
    {
        var code = env.Bridge.PairingCode == "000000" ? "111111" : "000000";
        var e = await Assert.ThrowsAsync<RemotePcException>(() => mgr.AddPcAsync(env.Base, code));
        Assert.Equal(RemoteErrorKind.WrongCode, e.Kind);
    });

    [Fact]
    public Task Away_shows_the_saved_list_with_its_time_then_marks_new_files_on_return() => WithClient(async (env, mgr, toggle, _) =>
    {
        await File.WriteAllTextAsync(Path.Combine(env.Cloud, "a.txt"), "a");
        var s = await Pair(env, mgr);
        var first = await s.ListAsync("cloudssd:");
        Assert.Empty(first.Value.NewIds);                          // nothing is NEW on the first visit

        toggle.Offline = true;
        Assert.False(await s.TickAsync());
        Assert.Equal(RemoteLinkState.Offline, s.State);
        var saved = await s.ListAsync("cloudssd:");
        Assert.True(saved.FromCache);
        Assert.Equal(first.SavedAt, saved.SavedAt);
        await Assert.ThrowsAsync<RemotePcException>(() => s.ListAsync("cloudssd:never-opened"));
        await Assert.ThrowsAsync<RemotePcException>(() => s.CreateFolderAsync("cloudssd:", "x"));

        await File.WriteAllTextAsync(Path.Combine(env.Cloud, "new.jpg"), "n");
        toggle.Offline = false;
        Assert.True(await s.TickAsync());
        var fresh = await s.ListAsync("cloudssd:");
        Assert.False(fresh.FromCache);
        Assert.Equal(new[] { "cloudssd:new.jpg" }, fresh.Value.NewIds);
        Assert.True(ListDiff.IsNew(fresh.Value, "cloudssd:new.jpg", DateTimeOffset.Now));

        s.MarkSeen("cloudssd:");
        Assert.Empty(s.Saved("cloudssd:")!.NewIds);
    });

    [Fact]
    public Task Files_sent_while_away_wait_survive_a_restart_and_resume_from_where_the_PC_stopped() => WithClient(async (env, mgr, toggle, dir) =>
    {
        var s = await Pair(env, mgr);
        var local = Path.Combine(dir, "big.bin");
        var bytes = Enumerable.Range(0, 300_000).Select(i => (byte)(i * 31)).ToArray();
        await File.WriteAllBytesAsync(local, bytes);
        var pcId = s.Pc.PcId;

        toggle.Offline = true;
        await s.TickAsync();
        Assert.Equal(1, s.EnqueueFiles(new[] { local }, "cloudssd:Inbox"));
        Assert.Equal("queued", s.Uploads.Single().State);

        // A new manager over the same folder (the app was restarted): the job is still there.
        await using var again = new RemotePcManager(dir, () => OwnPcId, () => null, NullLogger.Instance, () => toggle);
        var s2 = again.Session(pcId)!;
        Assert.Equal("queued", s2.Uploads.Single().State);

        // Back, but the connection breaks after the first piece: it continues from the PC's offset next time.
        toggle.Offline = false;
        toggle.WritesAllowed = 1;
        s2.ChunkSize = 64 * 1024;
        await s2.TickAsync();
        await Settle(s2);
        await s2.FlushUploadsAsync(CancellationToken.None);
        await Settle(s2);
        Assert.Equal("queued", s2.Uploads.Single().State);
        var status = await env.Json(await env.Get("/api/upload/status?uploadId=" + s2.Uploads.Single().Id));
        Assert.True(status.GetProperty("offset").GetInt64() > 0);

        toggle.WritesAllowed = int.MaxValue;
        await s2.TickAsync();
        await Settle(s2);
        await s2.FlushUploadsAsync(CancellationToken.None);
        await Settle(s2);
        Assert.Equal("done", s2.Uploads.Single().State);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(env.Cloud, "Inbox", "big.bin")));
    });

    [Fact]
    public Task Downloads_and_opening_a_file() => WithClient(async (env, mgr, toggle, dir) =>
    {
        var content = Enumerable.Range(0, 200_000).Select(i => (byte)(i * 7)).ToArray();
        await File.WriteAllBytesAsync(Path.Combine(env.Cloud, "movie.mp4"), content);
        var s = await Pair(env, mgr);
        var e = (await s.ListAsync("cloudssd:")).Value.Entries.Single(x => x.Name == "movie.mp4");
        var to = Path.Combine(dir, "downloads");
        var path = await s.DownloadAsync(e, to);
        Assert.Equal(content, await File.ReadAllBytesAsync(path));
        var second = await s.DownloadAsync(e, to);                 // never overwrites
        Assert.NotEqual(path, second);

        var opened = await s.OpenAsync(e);
        Assert.Equal(content, await File.ReadAllBytesAsync(opened));
        toggle.Offline = true;
        await s.TickAsync();
        await Assert.ThrowsAsync<RemotePcException>(() => s.OpenAsync(e));   // not kept offline: needs the PC
    });

    [Theory]
    [InlineData("cloudssd:..\\x")]
    [InlineData("cloudssd:a/../../x")]
    [InlineData("..:x")]
    [InlineData("c\\d:x")]
    public void Paths_from_another_PC_cannot_leave_their_folder(string id) =>
        Assert.Throws<RemotePcException>(() => RemotePcSession.SafeLocal(Path.Combine(Path.GetTempPath(), "cssd_safe"), id));

    [Fact]
    public Task A_PC_that_forgot_this_one_asks_for_pairing_again() => WithClient(async (env, mgr, _, _) =>
    {
        var s = await Pair(env, mgr);
        env.Bridge.RemoveDevice(env.Host.Settings.MobileBridge.PairedDevices.Single().Id);
        Assert.False(await s.TickAsync());
        Assert.Equal(RemoteLinkState.NeedsRepair, s.State);
        Assert.True(mgr.Store.Get(s.Pc.PcId)!.NeedsRepair);
    });

    [Fact]
    public Task Forgetting_removes_the_key_and_tells_that_PC() => WithClient(async (env, mgr, _, dir) =>
    {
        await File.WriteAllTextAsync(Path.Combine(env.Cloud, "kept.txt"), "kept");
        var s = await Pair(env, mgr);
        var id = s.Pc.PcId;
        await s.ListAsync("cloudssd:");
        s.Pin("cloudssd:kept.txt", isFile: true);
        await s.SyncPinsAsync();
        Assert.NotNull(s.PinnedCopy(new RemoteEntry("cloudssd:kept.txt", "kept.txt", false, 4, 0, 0, true, false)));   // read-only on disk
        Assert.True(Directory.Exists(Path.Combine(dir, id)));
        await mgr.ForgetAsync(id);
        Assert.Null(mgr.Store.Get(id));
        Assert.False(Directory.Exists(Path.Combine(dir, id)));
        Assert.Empty(env.Host.Settings.MobileBridge.PairedDevices);
    });

    [Fact]
    public void Subnet_hosts_skip_ourselves_and_stay_small()
    {
        var hosts = RemotePcManager.SubnetHosts(IPAddress.Parse("192.168.1.5"), IPAddress.Parse("255.255.255.0")).Select(h => h.ToString()).ToList();
        Assert.Equal(253, hosts.Count);
        Assert.DoesNotContain("192.168.1.5", hosts);
        Assert.DoesNotContain("192.168.1.0", hosts);
        Assert.DoesNotContain("192.168.1.255", hosts);
        Assert.Equal(253, RemotePcManager.SubnetHosts(IPAddress.Parse("10.1.2.3"), IPAddress.Parse("255.0.0.0")).Count());   // a /8 is cut to our /24
        Assert.Equal("http://192.168.1.5:8090", RemotePcClient.NormalizeUrl("192.168.1.5"));
        Assert.Equal("http://pc:9000", RemotePcClient.NormalizeUrl("http://pc:9000/"));
    }

    [Fact]
    public void List_diff_rules()
    {
        RemoteEntry E(string n) => new("d:" + n, n, false, 1, 0, 0, true, false);
        var now = DateTimeOffset.Now;
        Assert.Empty(ListDiff.NewIds(null, new[] { E("a") }, now).Ids);
        var old = new CachedList { Entries = new() { E("a") } };
        var (ids, at) = ListDiff.NewIds(old, new[] { E("a"), E("b") }, now);
        Assert.Equal(new[] { "d:b" }, ids);
        var carried = ListDiff.NewIds(new CachedList { Entries = new() { E("a"), E("b") }, NewIds = ids, NewAt = at }, new[] { E("a"), E("b") }, now.AddMinutes(1));
        Assert.Equal(new[] { "d:b" }, carried.Ids);
        var later = ListDiff.NewIds(new CachedList { Entries = new() { E("a"), E("b") }, NewIds = ids, NewAt = at }, new[] { E("a"), E("b") }, now.AddDays(2));
        Assert.Empty(later.Ids);
    }
    [Fact]
    public Task Pinned_folder_mirrors_and_opens_offline() => WithClient(async (env, mgr, toggle, dir) =>
    {
        var docs = Directory.CreateDirectory(Path.Combine(env.Cloud, "Docs")).FullName;
        Directory.CreateDirectory(Path.Combine(docs, "sub"));
        await File.WriteAllTextAsync(Path.Combine(docs, "a.txt"), "aaa");
        await File.WriteAllTextAsync(Path.Combine(docs, "sub", "b.txt"), "bbbb");
        var s = await Pair(env, mgr);

        s.Pin("cloudssd:Docs", isFile: false);
        await s.SyncPinsAsync();                                  // waits for a round already started by Pin
        await s.SyncPinsAsync();
        var pin = Assert.Single(s.Pins);
        Assert.Null(pin.Error);
        Assert.Equal(2, pin.Files);
        Assert.Equal(7, pin.Bytes);
        var a = new RemoteEntry("cloudssd:Docs/a.txt", "a.txt", false, 3, 0, 0, true, false);
        var copy = s.PinnedCopy(a)!;
        Assert.Equal("aaa", await File.ReadAllTextAsync(copy));
        Assert.True(File.GetAttributes(copy).HasFlag(FileAttributes.ReadOnly));   // a copy, not a place to edit
        Assert.Same(pin, s.PinOf("cloudssd:Docs/sub/b.txt"));

        // Changes there are mirrored: gone goes, new comes.
        File.Delete(Path.Combine(docs, "sub", "b.txt"));
        await File.WriteAllTextAsync(Path.Combine(docs, "c.txt"), "cc");
        await s.SyncPinsAsync();
        var pinned = Path.Combine(dir, s.Pc.PcId, "pinned", "cloudssd", "Docs");
        Assert.False(File.Exists(Path.Combine(pinned, "sub", "b.txt")));
        Assert.True(File.Exists(Path.Combine(pinned, "c.txt")));

        // Away: the kept copy opens, and a sub-folder never opened by hand can still be browsed.
        toggle.Offline = true;
        await s.TickAsync();
        Assert.False(s.IsOnline);
        Assert.Equal(copy, await s.OpenAsync(a));
        var sub = await s.ListAsync("cloudssd:Docs/sub");
        Assert.True(sub.FromCache);
        Assert.Empty(sub.Value.Entries);

        // Not kept any more: the copies go.
        s.Unpin("cloudssd:Docs");
        Assert.Empty(s.Pins);
        Assert.False(Directory.Exists(pinned));
    });

    [Fact]
    public Task A_big_folder_is_only_kept_after_an_ok() => WithClient(async (env, mgr, _, _) =>
    {
        var big = Directory.CreateDirectory(Path.Combine(env.Cloud, "Big")).FullName;
        await File.WriteAllBytesAsync(Path.Combine(big, "x.bin"), new byte[3000]);
        var s = await Pair(env, mgr);
        var limit = RemotePcSession.ConfirmAboveBytes;
        RemotePcSession.ConfirmAboveBytes = 1000;
        try
        {
            s.Pin("cloudssd:Big", isFile: false);
            await s.SyncPinsAsync();
            await s.SyncPinsAsync();
            var pin = Assert.Single(s.Pins);
            Assert.Equal(3000, pin.NeedsOkBytes);
            Assert.Null(s.PinnedCopy(new RemoteEntry("cloudssd:Big/x.bin", "x.bin", false, 3000, 0, 0, true, false)));

            s.ApprovePin("cloudssd:Big");
            await s.SyncPinsAsync();
            await s.SyncPinsAsync();
            Assert.Null(pin.NeedsOkBytes);
            Assert.NotNull(s.PinnedCopy(new RemoteEntry("cloudssd:Big/x.bin", "x.bin", false, 3000, 0, 0, true, false)));
        }
        finally { RemotePcSession.ConfirmAboveBytes = limit; }
    });

    [Fact]
    public Task A_single_file_can_be_kept_and_a_folder_kept_inside_survives_its_parent() => WithClient(async (env, mgr, _, _) =>
    {
        var docs = Directory.CreateDirectory(Path.Combine(env.Cloud, "Docs")).FullName;
        Directory.CreateDirectory(Path.Combine(docs, "sub"));
        await File.WriteAllTextAsync(Path.Combine(docs, "one.txt"), "1");
        await File.WriteAllTextAsync(Path.Combine(docs, "sub", "two.txt"), "22");
        var s = await Pair(env, mgr);
        s.Pin("cloudssd:Docs/one.txt", isFile: true);
        s.Pin("cloudssd:Docs", isFile: false);
        s.Pin("cloudssd:Docs/sub", isFile: false);
        await s.SyncPinsAsync();
        await s.SyncPinsAsync();
        var two = new RemoteEntry("cloudssd:Docs/sub/two.txt", "two.txt", false, 2, 0, 0, true, false);
        var one = new RemoteEntry("cloudssd:Docs/one.txt", "one.txt", false, 1, 0, 0, true, false);
        Assert.NotNull(s.PinnedCopy(two));
        s.Unpin("cloudssd:Docs");
        Assert.NotNull(s.PinnedCopy(two));                        // still kept by its own pin
        Assert.NotNull(s.PinnedCopy(one));                        // kept by itself as well
        s.Unpin("cloudssd:Docs/one.txt");
        Assert.Null(s.PinnedCopy(one));
    });

    [Fact]
    public Task A_laptop_pairs_back_so_the_PC_lists_it_with_nothing_allowed_and_forgetting_cleans_both_ends() => WithClient(async (env, _, toggle, dir) =>
    {
        // The laptop gives the PC a key to itself; here a stand-in for the laptop's own bridge records what it gave.
        string? revoked = null;
        await using var laptop = new RemotePcManager(Path.Combine(dir, "pairback"), () => OwnPcId, () => 8099, NullLogger.Instance, () => toggle,
            issueBack: (pcId, name) => ("backdev01", "0123456789abcdef0123456789abcdef"), revokeBack: id => revoked = id);
        var s = await laptop.AddPcAsync(env.Base.Replace("http://", ""), env.Bridge.PairingCode);
        Assert.Equal("backdev01", s.Pc.BackDeviceId);

        // The PC now lists the laptop on its own Other PCs page, at the address it came from, with the key it was given.
        var back = env.Host.RemotePcs.Store.Get(OwnPcId);
        Assert.NotNull(back);
        Assert.True(back!.PairedBack);
        Assert.Equal("http://127.0.0.1:8099", back.LanUrl);
        Assert.Equal("backdev01", back.DeviceId);
        Assert.Equal("0123456789abcdef0123456789abcdef", env.Host.RemotePcs.Store.Token(back));

        // A phone cannot pair back, and a laptop cannot claim to be another PC.
        var phone = await env.PairNew("Pixel", "inst-pb-phone", "&kind=phone");
        var bad = new StringContent("{\"pcId\":\"" + OwnPcId + "\",\"port\":8099,\"deviceId\":\"x1234\",\"token\":\"0123456789abcdef0123456789abcdef\"}", System.Text.Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.Forbidden, (await env.Http.SendAsync(new HttpRequestMessage(HttpMethod.Post, env.Base + "/api/peer/pairback") { Content = bad, Headers = { { "X-Pairing-Token", phone } } })).StatusCode);

        // The laptop forgets the PC: its own key there goes (the PC drops the paired-back entry), and the key it gave is taken back.
        await laptop.ForgetAsync(s.Pc.PcId);
        Assert.Equal("backdev01", revoked);
        // The PC answers first and removes the device right after (its own request would be cut otherwise).
        for (int i = 0; i < 40 && env.Host.RemotePcs.Store.Get(OwnPcId) != null; i++) await Task.Delay(50);
        Assert.Null(env.Host.RemotePcs.Store.Get(OwnPcId));
    });

    [Fact]
    public Task A_laptop_takes_files_sent_to_it_marked_as_downloaded() => WithClient(async (env, mgr, _, dir) =>
    {
        // Laptops could not receive files before: only phones and tablets could.
        var s = await Pair(env, mgr);
        s.ReceivedFolder = Path.Combine(dir, "received-here");
        var src = Path.Combine(env.Temp, "plan.pdf");
        var bytes = new byte[70_000];
        new Random(3).NextBytes(bytes);
        await File.WriteAllBytesAsync(src, bytes);
        var offer = env.Bridge.SendToDevice(env.Host.Settings.MobileBridge.PairedDevices.Single(), [src]);
        Assert.NotNull(offer);

        await s.CheckInboxAsync();
        var waiting = Assert.Single(s.Inbox);
        Assert.Equal("offered", waiting.State);
        var file = Assert.Single(await s.AcceptAsync(waiting));
        Assert.StartsWith(s.ReceivedFolder, file);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(file));
        Assert.Contains("ZoneId=3", await File.ReadAllTextAsync(file + ":Zone.Identifier"));   // Windows treats it like a download
        Assert.Empty(s.Inbox);                                     // nothing left to answer
        for (int i = 0; i < 50 && offer!.State != "done"; i++) await Task.Delay(50);
        Assert.Equal("done", offer!.State);                        // the PC sees it delivered
    });

    [Fact]
    public Task A_laptop_takes_a_folder_with_its_tree_and_a_second_one_gets_a_number() => WithClient(async (env, mgr, _, dir) =>
    {
        var s = await Pair(env, mgr);
        s.ReceivedFolder = Path.Combine(dir, "received-here");
        var root = Path.Combine(env.Temp, "MyApp");
        Directory.CreateDirectory(Path.Combine(root, "src", "lib"));
        Directory.CreateDirectory(Path.Combine(root, "node_modules", "x"));
        await File.WriteAllTextAsync(Path.Combine(root, "src", "index.js"), "a");
        await File.WriteAllTextAsync(Path.Combine(root, "src", "lib", "index.js"), "bb");
        await File.WriteAllTextAsync(Path.Combine(root, "node_modules", "x", "big.js"), "ccc");
        var dev = env.Host.Settings.MobileBridge.PairedDevices.Single();

        for (int round = 1; round <= 2; round++)
        {
            var sent = env.Bridge.SendToDevice(dev, [root]);
            Assert.NotNull(sent);
            await s.CheckInboxAsync();
            var files = await s.AcceptAsync(Assert.Single(s.Inbox));
            Assert.Equal(2, files.Count);
            // The PC marks a file delivered a moment after its last byte went out: the next send waits for that, or the
            // first offer would still be in the inbox.
            for (int i = 0; i < 100 && sent!.State != "done"; i++) await Task.Delay(20);
            Assert.Equal("done", sent!.State);
            var top = round == 1 ? "MyApp" : "MyApp (2)";            // not mixed into the first copy
            var day = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(files.Single(f => f.EndsWith(Path.Combine("src", "index.js")) && !f.Contains("lib"))))!)!;
            Assert.Equal("a", await File.ReadAllTextAsync(Path.Combine(day, top, "src", "index.js")));
            Assert.Equal("bb", await File.ReadAllTextAsync(Path.Combine(day, top, "src", "lib", "index.js")));
            Assert.False(Directory.Exists(Path.Combine(day, top, "node_modules")));
        }
    });

    [Fact]
    public Task A_laptop_learns_what_needs_allowing_and_asks_for_it() => WithClient(async (env, mgr, _, _) =>
    {
        // A refused folder used to look like a "wrong code" error; now it says what to allow and the laptop can ask.
        var s = await Pair(env, mgr);
        var dev = env.Host.Settings.MobileBridge.PairedDevices.Single();
        // Seeing a folder is browsing (it used to ask for "change files"); changing files is still asked for separately.
        env.Bridge.Access.Revoke(dev, CloudSSD.App.AccessCategories.Browse);
        var e = await Assert.ThrowsAsync<RemotePcException>(() => s.ListAsync("cloudssd:"));
        Assert.Equal(RemoteErrorKind.NeedsPermission, e.Kind);
        Assert.Equal([CloudSSD.App.AccessCategories.Browse], e.Categories);

        var asked = await s.RequestAccessAsync(e.Categories);
        Assert.Equal("pending", asked.State);
        Assert.True(env.Bridge.Access.Decide(asked.Id, CloudSSD.App.AccessMode.Always));   // someone at that PC says yes
        Assert.Equal("allowed", (await s.AccessRequestAsync())!.State);
        Assert.False((await s.ListAsync("cloudssd:")).FromCache);
    });
}
