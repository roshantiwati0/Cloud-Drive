using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using CloudSSD.App;
using Xunit;

namespace CloudSSD.Tests;

/// <summary>The Devices page's data: kind, online now, how a device connects, what it is doing, rename / block / remove.</summary>
public class MobileBridgeDevicesTests
{
    /// <summary>A clock the test moves by hand.</summary>
    private sealed class Clock
    {
        public DateTimeOffset Now = DateTimeOffset.Now;
        public void Add(TimeSpan t) => Now += t;
    }

    private static Task With(Func<BridgeEnv, Clock, Task> body)
    {
        var clock = new Clock();
        return BridgeHarness.With(env => body(env, clock), b => b.Now = () => clock.Now);
    }

    private static string IdOf(BridgeEnv env, string name) => env.Host.Settings.MobileBridge.PairedDevices.Single(d => d.Name == name).Id;

    private static DeviceSnapshot Dev(BridgeEnv env, string name) => env.Bridge.GetDevices().Single(d => d.Name == name);

    /// <summary>Waits for the (coalesced) DevicesChanged event after <paramref name="act"/>.</summary>
    private static async Task<bool> RaisesDevicesChanged(BridgeEnv env, Action act)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler() => tcs.TrySetResult();
        await Task.Delay(700); // let earlier events drain
        env.Bridge.DevicesChanged += Handler;
        try
        {
            act();
            return await Task.WhenAny(tcs.Task, Task.Delay(3000)) == tcs.Task;
        }
        finally { env.Bridge.DevicesChanged -= Handler; }
    }

    [Fact]
    public Task Kind_and_platform_are_kept_and_old_phones_count_as_phones() => With(async (env, _) =>
    {
        await env.PairNew("Dell", "install-laptop-01", "&kind=laptop&platform=Windows%2011&app=0.8.0");
        await env.PairNew("Pixel", "install-phone-01");
        await env.PairNew("Phone%20browser", "install-web-0001");
        await env.PairNew("Odd", "install-odd-0001", "&kind=toaster");

        var dell = env.Host.Settings.MobileBridge.PairedDevices.Single(d => d.Name == "Dell");
        Assert.Equal("laptop", dell.Kind);
        Assert.Equal("Windows 11", dell.Platform);
        Assert.Equal("0.8.0", dell.AppVersion);
        Assert.Equal(DeviceKind.Laptop, Dev(env, "Dell").Kind);
        Assert.Equal(DeviceKind.Phone, Dev(env, "Pixel").Kind);
        Assert.Equal(DeviceKind.Browser, Dev(env, "Phone browser").Kind);
        Assert.Equal(DeviceKind.Phone, Dev(env, "Odd").Kind);

        // A device paired before kinds existed has none saved: it shows as a phone.
        env.Host.Settings.MobileBridge.PairedDevices.Add(new Core.PairedDevice { Name = "Old", TokenHash = new string('a', 64) });
        Assert.Equal(DeviceKind.Phone, Dev(env, "Old").Kind);
    });

    [Fact]
    public Task A_laptop_tells_its_own_Cloud_SSD_id_for_pairing_back_later() => With(async (env, _) =>
    {
        await env.PairNew("Dell", "install-laptop-02", "&kind=laptop&hubPcId=00112233aabbccdd&hubName=DELL-PC&hubPort=8090");
        var dell = env.Host.Settings.MobileBridge.PairedDevices.Single();
        Assert.Equal("00112233aabbccdd", dell.PeerPcId);
        Assert.Equal("DELL-PC", dell.PeerName);
        Assert.Equal(8090, dell.PeerPort);

        await env.PairNew("Bad", "install-laptop-03", "&kind=laptop&hubPcId=..%5Cx&hubPort=99999");
        var bad = env.Host.Settings.MobileBridge.PairedDevices.Single(d => d.Name == "Bad");
        Assert.Null(bad.PeerPcId);
        Assert.Null(bad.PeerPort);
    });

    [Fact]
    public Task Presence_goes_online_with_requests_and_offline_after_the_window() => With(async (env, clock) =>
    {
        var token = await env.PairNew("Pixel", "install-presence-1");
        Assert.True(Dev(env, "Pixel").Online);

        clock.Add(TimeSpan.FromSeconds(30));
        Assert.Equal(HttpStatusCode.OK, (await env.Get("/api/ping", token)).StatusCode);
        Assert.True(Dev(env, "Pixel").Online);

        clock.Add(DevicePresenceTracker.OnlineWindow + TimeSpan.FromSeconds(1));
        Assert.True(await RaisesDevicesChanged(env, env.Bridge.SweepPresence));
        Assert.False(Dev(env, "Pixel").Online);
        Assert.Equal("usb-adb", Dev(env, "Pixel").Link);   // a phone over loopback, as with adb reverse

        // Its next request brings it back.
        await env.Get("/api/ping", token);
        Assert.True(Dev(env, "Pixel").Online);
    });

    [Fact]
    public Task Last_seen_is_saved_at_most_once_a_minute() => With(async (env, clock) =>
    {
        var token = await env.PairNew("Pixel", "install-saved-01");
        DateTimeOffset OnDisk() => env.Host.SettingsStore.Load().MobileBridge.PairedDevices.Single().LastSeen;

        clock.Add(TimeSpan.FromSeconds(5));
        await env.Get("/api/ping", token);
        env.Bridge.SweepPresence();                         // first save happens at once
        var first = OnDisk();
        Assert.Equal(clock.Now, first);

        clock.Add(TimeSpan.FromSeconds(20));
        await env.Get("/api/ping", token);
        env.Bridge.SweepPresence();
        Assert.Equal(first, OnDisk());                      // not again within a minute

        clock.Add(TimeSpan.FromSeconds(61));
        await env.Get("/api/ping", token);
        env.Bridge.SweepPresence();
        Assert.Equal(clock.Now, OnDisk());

        clock.Add(TimeSpan.FromSeconds(3));
        await env.Get("/api/ping", token);
        env.Bridge.Stop();                                  // stopping saves what is left
        Assert.Equal(clock.Now, OnDisk());
    });

    [Fact]
    public Task Rename_block_and_unblock_touch_only_that_device() => With(async (env, _) =>
    {
        var a = await env.PairNew("PhoneA", "install-rb-00001");
        var b = await env.PairNew("PhoneB", "install-rb-00002");
        var idA = IdOf(env, "PhoneA");

        Assert.True(env.Bridge.SetBlocked(idA, true));
        Assert.Equal(HttpStatusCode.Unauthorized, (await env.Get("/api/hud", a)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await env.Get("/api/hud", b)).StatusCode);
        Assert.False(Dev(env, "PhoneA").Online);
        Assert.True(Dev(env, "PhoneA").Blocked);

        Assert.True(env.Bridge.SetBlocked(idA, false));
        Assert.Equal(HttpStatusCode.OK, (await env.Get("/api/hud", a)).StatusCode);   // same key works again

        Assert.False(env.Bridge.RenameDevice(idA, ""));
        Assert.False(env.Bridge.RenameDevice(idA, new string('x', 61)));
        Assert.False(env.Bridge.RenameDevice("nope", "Name"));
        Assert.True(env.Bridge.RenameDevice(idA, "  Asha's\u0007 phone "));
        Assert.Equal("Asha's phone", env.Host.Settings.MobileBridge.PairedDevices.Single(d => d.Id == idA).Name);

        // Pairing again from the same install keeps the name given on the PC.
        await env.PairNew("PhoneA", "install-rb-00001");
        Assert.Equal("Asha's phone", env.Host.Settings.MobileBridge.PairedDevices.Single(d => d.Id == idA).Name);
        var list = await env.Json(await env.Get("/api/devices", b));
        Assert.Contains(list.EnumerateArray(), d => d.GetProperty("name").GetString() == "Asha's phone");
    });

    [Fact]
    public Task Devices_list_has_kind_online_and_link_but_no_addresses_or_keys() => With(async (env, _) =>
    {
        var a = await env.PairNew("Dell", "install-dto-0001", "&kind=laptop&platform=Windows%2011");
        await env.PairNew("Pixel", "install-dto-0002");
        var text = await (await env.Get("/api/devices", a)).Content.ReadAsStringAsync();
        Assert.DoesNotContain("127.0.0.1", text);
        Assert.DoesNotContain("tokenHash", text, StringComparison.OrdinalIgnoreCase);
        var list = JsonDocument.Parse(text).RootElement.EnumerateArray().ToList();
        var dell = list.Single(d => d.GetProperty("name").GetString() == "Dell");
        Assert.Equal("laptop", dell.GetProperty("kind").GetString());
        Assert.True(dell.GetProperty("online").GetBoolean());
        Assert.True(dell.GetProperty("isThisDevice").GetBoolean());
        Assert.False(list.Single(d => d.GetProperty("name").GetString() == "Pixel").GetProperty("isThisDevice").GetBoolean());

        var status = await env.Json(await env.Get("/api/status", a));
        Assert.Equal("laptop", status.GetProperty("deviceKind").GetString());
    });

    [Fact]
    public Task Connected_note_on_pairing_and_on_return_but_not_on_every_poll() => With(async (env, clock) =>
    {
        var events = new List<DeviceConnectedEvent>();
        env.Bridge.DeviceConnected += e => { lock (events) events.Add(e); };

        var token = await env.PairNew("Pixel", "install-notify-01");
        Assert.Single(events);
        Assert.True(events[0].JustPaired);
        Assert.Equal("Pixel", events[0].Device.Name);

        for (int i = 0; i < 5; i++) { clock.Add(TimeSpan.FromSeconds(3)); await env.Get("/api/status", token); }
        Assert.Single(events);

        clock.Add(TimeSpan.FromMinutes(11));                 // away, and past the cooldown since the pairing note
        await env.Get("/api/status", token);
        Assert.Equal(2, events.Count);
        Assert.False(events[1].JustPaired);

        clock.Add(TimeSpan.FromMinutes(3));                  // away again, but within ten minutes of the last note
        await env.Get("/api/status", token);
        Assert.Equal(2, events.Count);

        // Renewing its own key is not a new pairing.
        await env.Http.PostAsync($"{env.Base}/api/pair?token={token}&name=Pixel&installId=install-notify-01", null);
        Assert.Equal(2, events.Count);

        env.Host.Settings.MobileBridge.NotifyDeviceConnections = false;
        await env.PairNew("Quiet", "install-notify-02");
        Assert.Equal(2, events.Count);
    });

    [Fact]
    public Task An_upload_shows_as_activity_with_a_percentage() => With(async (env, _) =>
    {
        var token = await env.PairNew("Pixel", "install-upload-01");
        int port = int.Parse(env.Base[(env.Base.LastIndexOf(':') + 1)..]);
        const int size = 200_000;
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, port);
        var s = tcp.GetStream();
        var head = $"POST /api/upload?filename=clip.bin&token={token} HTTP/1.1\r\nHost: 127.0.0.1:{port}\r\nContent-Length: {size}\r\n\r\n";
        await s.WriteAsync(Encoding.ASCII.GetBytes(head));
        await s.WriteAsync(new byte[size / 2]);

        DeviceActivity? act = null;
        for (int i = 0; i < 50 && act is not { Percent: >= 40 }; i++) { await Task.Delay(50); act = Dev(env, "Pixel").Activity; }
        Assert.NotNull(act);
        Assert.Equal(DeviceActivityKind.Upload, act!.Kind);
        Assert.Equal("clip.bin", act.File);
        Assert.InRange(act.Percent!.Value, 40, 60);

        await s.WriteAsync(new byte[size / 2]);
        var buf = new byte[4096];
        await s.ReadAtLeastAsync(buf, 1);                    // the PC answered: the upload is finished
        for (int i = 0; i < 50 && Dev(env, "Pixel").Activity?.Kind == DeviceActivityKind.Upload; i++) await Task.Delay(50);
        Assert.NotEqual(DeviceActivityKind.Upload, Dev(env, "Pixel").Activity?.Kind);
    });

    [Fact]
    public Task Blocking_a_device_cuts_its_running_download() => With(async (env, _) =>
    {
        var token = await env.PairNew("Pixel", "install-cut-0001");
        var file = Path.Combine(env.Cloud, "big.bin");
        await File.WriteAllBytesAsync(file, new byte[64 * 1024 * 1024]);
        using var resp = await env.Http.GetAsync($"{env.Base}/api/download?path={Uri.EscapeDataString(file)}&token={token}", HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        await using var body = await resp.Content.ReadAsStreamAsync();
        var buf = new byte[64 * 1024];
        long got = await body.ReadAsync(buf);                // started (the rest waits: we read slowly)
        Assert.Equal(DeviceActivityKind.Download, Dev(env, "Pixel").Activity?.Kind);

        env.Bridge.SetBlocked(IdOf(env, "Pixel"), true);
        try
        {
            int n;
            while ((n = await body.ReadAsync(buf)) > 0) got += n;
        }
        catch (Exception e) when (e is IOException or HttpRequestException) { }
        Assert.True(got < 64L * 1024 * 1024, "the download should have stopped before the end");
    });

    [Theory]
    [InlineData("/api/fs/thumb", "GET", DeviceActivityKind.Photos)]
    [InlineData("/api/fs/list", "GET", DeviceActivityKind.Browsing)]
    [InlineData("/api/files", "GET", DeviceActivityKind.Browsing)]
    [InlineData("/api/backup/upload", "POST", DeviceActivityKind.Backup)]
    [InlineData("/api/space/summary", "GET", DeviceActivityKind.Space)]
    [InlineData("/api/hud", "GET", DeviceActivityKind.Hud)]
    public void Activity_comes_from_the_route(string path, string method, DeviceActivityKind kind) =>
        Assert.Equal(kind, DevicePresenceTracker.FromRoute(path, method));

    [Theory]
    [InlineData("/api/status")]
    [InlineData("/api/ping")]
    [InlineData("/api/space/phone-report")]
    [InlineData("/api/space/live")]                 // the phone's Home tiles refreshing by themselves
    [InlineData("/api/devices")]
    [InlineData("/api/peer/inbox")]                 // the phone waiting for the PC's questions
    public void Polling_does_not_change_the_activity(string path) => Assert.Null(DevicePresenceTracker.FromRoute(path, "GET"));

    [Fact]
    public Task Each_device_keeps_its_own_bytes_to_and_from_the_PC() => BridgeHarness.With(async env =>
    {
        // The phone's Connection sheet showed the whole PC's bytes since it started (0 after a PC restart).
        var token = await env.PairNew("Pixel", "inst-bytes-01", "&kind=phone");
        var other = await env.PairNew("Tab", "inst-bytes-02", "&kind=tablet");
        var body = new byte[50_000];
        Assert.Equal(HttpStatusCode.OK, (await env.PostBytes("/api/fs/write?path=" + Uri.EscapeDataString("cloudssd:b.bin") + "&overwrite=1", body, token)).StatusCode);
        var read = await env.Get("/api/fs/read?path=" + Uri.EscapeDataString("cloudssd:b.bin"), token);
        Assert.Equal(50_000, (await read.Content.ReadAsByteArrayAsync()).Length);

        async Task<(long To, long From)> Mine(string t)
        {
            var m = (await env.Json(await env.Get("/api/status", t))).GetProperty("myBytes");
            return (m.GetProperty("toDevice").GetInt64(), m.GetProperty("fromDevice").GetInt64());
        }
        Assert.Equal((50_000L, 50_000L), await Mine(token));
        Assert.Equal((0L, 0L), await Mine(other));                       // each device its own

        env.Bridge.FlushLastSeen(force: true);                            // saved with the device…
        var saved = env.Host.Settings.MobileBridge.PairedDevices.Single(d => d.Name == "Pixel");
        Assert.Equal(50_000, saved.BytesToDevice);
        Assert.Equal(50_000, saved.BytesFromDevice);
        Assert.Equal((50_000L, 50_000L), await Mine(token));              // …and not counted twice after
    });

    [Theory]
    [InlineData(null, "Pixel 8", null, "phone")]
    [InlineData("laptop", "Dell", null, "laptop")]
    [InlineData("PC", "Desk", null, "desktop")]
    [InlineData(null, "Phone browser", null, "browser")]
    [InlineData(null, "Tab", "Mozilla/5.0 (iPad; CPU OS 17_0)", "tablet")]
    [InlineData("toaster", "X", null, "phone")]
    public void Kinds_are_inferred(string? kind, string name, string? ua, string expected) =>
        Assert.Equal(expected, DeviceKinds.Infer(kind, name, ua));

    [Fact]
    public void The_PCs_Mobile_Hotspot_and_Tailscale_are_recognised()
    {
        var remote = new IPEndPoint(IPAddress.Parse("192.168.137.20"), 5000);
        Assert.Equal("pc-hotspot", LocalMobileBridgeService.ClassifyLink(new IPEndPoint(IPAddress.Parse("192.168.137.1"), 8090), remote, LocalMobileBridgeService.NetRoute.Lan));
        Assert.Equal("internet", LocalMobileBridgeService.ClassifyLink(new IPEndPoint(IPAddress.Parse("100.100.1.2"), 8090), remote, LocalMobileBridgeService.NetRoute.Tailnet));
        Assert.Equal("local", LocalMobileBridgeService.ClassifyLink(new IPEndPoint(IPAddress.Loopback, 8090), new IPEndPoint(IPAddress.Loopback, 5000), LocalMobileBridgeService.NetRoute.Lan));
    }
}
