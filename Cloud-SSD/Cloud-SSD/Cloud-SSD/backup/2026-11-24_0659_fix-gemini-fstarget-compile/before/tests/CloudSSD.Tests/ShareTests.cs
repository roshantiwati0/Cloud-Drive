using System.Net;
using System.Net.Http.Headers;
using CloudSSD.App;
using CloudSSD.Core;
using Xunit;

namespace CloudSSD.Tests;

/// <summary>Sending files: this PC → a device, a device → another device through the PC, and links for a while.</summary>
public class ShareTests
{
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

    /// <summary>The PC marks a file delivered right after it went out, which can be a moment after the last byte arrived here.</summary>
    private static async Task Until(Func<bool> done)
    {
        for (int i = 0; i < 100 && !done(); i++) await Task.Delay(20);
        Assert.True(done());
    }

    private static PairedDevice Dev(BridgeEnv env, string name) => env.Host.Settings.MobileBridge.PairedDevices.Single(d => d.Name == name);

    private static async Task<string> File(BridgeEnv env, string name, int size)
    {
        var p = Path.Combine(env.Temp, name);
        var bytes = new byte[size];
        new Random(size).NextBytes(bytes);
        await System.IO.File.WriteAllBytesAsync(p, bytes);
        return p;
    }

    [Fact]
    public Task The_PC_offers_files_the_phone_sees_them_downloads_with_resume_and_they_are_delivered() => With(async (env, _) =>
    {
        var token = await env.PairNew("Pixel", "inst-share-01", "&kind=phone");
        var other = await env.PairNew("Tab", "inst-share-02", "&kind=tablet");
        var a = await File(env, "holiday.jpg", 300_000);
        var b = await File(env, "notes.txt", 50);
        ShareOffer? changed = null;
        env.Bridge.Shares.Changed += o => changed = o;
        var offer = env.Bridge.SendToDevice(Dev(env, "Pixel"), [a, b, Path.Combine(env.Temp, "missing.bin")])!;
        Assert.Equal(2, offer.Items.Count);                                       // what is not there is left out
        Assert.Same(offer, changed);

        Assert.Equal(1, (await env.Json(await env.Get("/api/status", token))).GetProperty("inbox").GetInt32());
        var inbox = await env.Json(await env.Get("/api/share/inbox", token));
        var o = inbox.GetProperty("offers").EnumerateArray().Single();
        Assert.True(o.GetProperty("fromPc").GetBoolean());
        Assert.DoesNotContain("path", o.GetRawText(), StringComparison.OrdinalIgnoreCase);   // where the files are on the PC stays on the PC
        Assert.Empty((await env.Json(await env.Get("/api/share/inbox", other))).GetProperty("offers").EnumerateArray());

        // Only the device it was offered to can take it.
        var itemA = offer.Items[0].Id;
        Assert.Equal(HttpStatusCode.NotFound, (await env.Get($"/api/share/file?id={offer.Id}&item={itemA}", other)).StatusCode);

        // A broken download continues from where it stopped.
        var req = new HttpRequestMessage(HttpMethod.Get, $"{env.Base}/api/share/file?id={offer.Id}&item={itemA}&token={token}");
        req.Headers.Range = new RangeHeaderValue(0, 99_999);
        var first = await env.Http.SendAsync(req);
        Assert.Equal(HttpStatusCode.PartialContent, first.StatusCode);
        Assert.False(offer.Items[0].Delivered);                                   // a part is not the whole file
        req = new HttpRequestMessage(HttpMethod.Get, $"{env.Base}/api/share/file?id={offer.Id}&item={itemA}&token={token}");
        req.Headers.Range = new RangeHeaderValue(100_000, null);
        var rest = await env.Http.SendAsync(req);
        var all = (await first.Content.ReadAsByteArrayAsync()).Concat(await rest.Content.ReadAsByteArrayAsync()).ToArray();
        Assert.Equal(await System.IO.File.ReadAllBytesAsync(a), all);
        await Until(() => offer.Items[0].Delivered);
        Assert.Equal("accepted", offer.State);                                    // downloading it is accepting it

        Assert.Equal(HttpStatusCode.OK, (await env.Get($"/api/share/file?id={offer.Id}&item={offer.Items[1].Id}", token)).StatusCode);
        await Until(() => offer.State == "done");
        Assert.Empty((await env.Json(await env.Get("/api/share/inbox", token))).GetProperty("offers").EnumerateArray());
    });

    [Fact]
    public Task A_file_changed_after_it_was_offered_is_not_sent_and_a_declined_offer_is_gone() => With(async (env, _) =>
    {
        var token = await env.PairNew("Pixel", "inst-share-03", "&kind=phone");
        var a = await File(env, "draft.docx", 1000);
        var offer = env.Bridge.SendToDevice(Dev(env, "Pixel"), [a])!;
        await System.IO.File.WriteAllTextAsync(a, "changed");
        Assert.Equal(HttpStatusCode.Conflict, (await env.Get($"/api/share/file?id={offer.Id}&item={offer.Items[0].Id}", token)).StatusCode);

        var second = env.Bridge.SendToDevice(Dev(env, "Pixel"), [a])!;
        Assert.Equal(HttpStatusCode.OK, (await env.Post($"/api/share/decline?id={second.Id}", token)).StatusCode);
        Assert.Equal("declined", second.State);
        Assert.Equal(HttpStatusCode.NotFound, (await env.Get($"/api/share/file?id={second.Id}&item={second.Items[0].Id}", token)).StatusCode);
    });

    [Fact]
    public Task An_offer_nobody_answers_ends_after_a_day() => With(async (env, clock) =>
    {
        var token = await env.PairNew("Pixel", "inst-share-04", "&kind=phone");
        var offer = env.Bridge.SendToDevice(Dev(env, "Pixel"), [await File(env, "a.bin", 10)])!;
        clock.Add(TimeSpan.FromHours(25));
        Assert.Empty((await env.Json(await env.Get("/api/share/inbox", token))).GetProperty("offers").EnumerateArray());
        Assert.Equal("expired", offer.State);
    });

    [Fact]
    public Task A_phone_sends_to_another_phone_through_the_PC() => With(async (env, _) =>
    {
        var from = await env.PairNew("Pixel", "inst-share-05", "&kind=phone");
        var to = await env.PairNew("Tab", "inst-share-06", "&kind=tablet");
        var laptop = await env.PairNew("Dell", "inst-share-07", "&kind=laptop");
        await env.PairNew("Edge", "inst-share-08", "&kind=browser");
        var tabId = Dev(env, "Tab").Id;

        var devices = await env.Json(await env.Get("/api/share/devices", from));
        Assert.Contains(devices.GetProperty("devices").EnumerateArray(), d => d.GetProperty("name").GetString() == "Tab" && d.GetProperty("canReceive").GetBoolean());
        // A laptop takes files through its "Other PCs" page now; a browser still cannot.
        Assert.Contains(devices.GetProperty("devices").EnumerateArray(), d => d.GetProperty("name").GetString() == "Dell" && d.GetProperty("canReceive").GetBoolean());
        Assert.DoesNotContain(devices.GetProperty("devices").EnumerateArray(), d => d.GetProperty("name").GetString() == "Pixel");   // not itself
        Assert.Equal(HttpStatusCode.BadRequest,
            (await env.Post("/api/share/offer?to=" + Dev(env, "Edge").Id + "&items=" + Uri.EscapeDataString("[{\"name\":\"a.jpg\",\"size\":3}]"), from)).StatusCode);

        var bytes = new byte[5000];
        new Random(5).NextBytes(bytes);
        var r = await env.Post("/api/share/offer?to=" + tabId + "&items=" + Uri.EscapeDataString("[{\"name\":\"../../evil.jpg\",\"size\":5000}]"), from);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var offer = (await env.Json(r)).GetProperty("offer");
        var id = offer.GetProperty("id").GetString()!;
        var item = offer.GetProperty("items")[0];
        Assert.DoesNotContain("..", item.GetProperty("name").GetString()!);
        Assert.DoesNotContain("/", item.GetProperty("name").GetString()!);
        // Nothing for the receiver until every file has arrived here.
        Assert.Empty((await env.Json(await env.Get("/api/share/inbox", to))).GetProperty("offers").EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await env.PostBytes($"/api/share/upload?id={id}&item={item.GetProperty("id").GetString()}&uploadId={new string('a', 32)}&offset=0&total=5000", bytes, to)).StatusCode);

        var up = await env.PostBytes($"/api/share/upload?id={id}&item={item.GetProperty("id").GetString()}&uploadId={new string('b', 32)}&offset=0&total=5000", bytes, from);
        Assert.Equal(HttpStatusCode.OK, up.StatusCode);
        Assert.True((await env.Json(up)).GetProperty("complete").GetBoolean());

        var inbox = (await env.Json(await env.Get("/api/share/inbox", to))).GetProperty("offers").EnumerateArray().Single();
        Assert.Equal("Pixel", inbox.GetProperty("from").GetString());
        Assert.False(inbox.GetProperty("fromPc").GetBoolean());
        var got = await env.Get($"/api/share/file?id={id}&item={item.GetProperty("id").GetString()}", to);
        Assert.Equal(bytes, await got.Content.ReadAsByteArrayAsync());
        var o = env.Bridge.Shares.Get(id)!;
        await Until(() => o.State == "done");
        // Passed on: not kept on the PC (the folder goes right after the last byte, so this may take a moment).
        await Until(() => !Directory.Exists(env.Bridge.Shares.RelayFolder(o)));
        Assert.False(Directory.Exists(env.Bridge.Shares.RelayFolder(o)));
    });

    [Fact]
    public Task Sending_between_devices_can_be_switched_off() => With(async (env, _) =>
    {
        var from = await env.PairNew("Pixel", "inst-share-08", "&kind=phone");
        await env.PairNew("Tab", "inst-share-09", "&kind=tablet");
        env.Host.Settings.MobileBridge.AllowDeviceToDevice = false;
        var r = await env.Post("/api/share/offer?to=" + Dev(env, "Tab").Id + "&items=" + Uri.EscapeDataString("[{\"name\":\"a.jpg\",\"size\":3}]"), from);
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.False((await env.Json(await env.Get("/api/share/devices", from))).GetProperty("allowed").GetBoolean());
    });

    [Fact]
    public Task A_link_works_without_a_key_until_used_up_taken_back_or_expired() => With(async (env, clock) =>
    {
        var a = await File(env, "song.mp3", 2000);
        using var anyone = new HttpClient();
        var once = env.Bridge.CreateShareLink(a, TimeSpan.FromHours(1), 1)!;
        var path = new Uri(once).AbsolutePath;
        Assert.StartsWith("/s/", path);
        var r = await anyone.GetAsync(env.Base + path);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(await System.IO.File.ReadAllBytesAsync(a), await r.Content.ReadAsByteArrayAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await anyone.GetAsync(env.Base + path)).StatusCode);     // used up

        var day = new Uri(env.Bridge.CreateShareLink(a, TimeSpan.FromHours(24), null)!).AbsolutePath;
        Assert.Equal(HttpStatusCode.OK, (await anyone.GetAsync(env.Base + day)).StatusCode);
        clock.Add(TimeSpan.FromHours(25));
        Assert.Equal(HttpStatusCode.NotFound, (await anyone.GetAsync(env.Base + day)).StatusCode);    // expired

        var revoked = new Uri(env.Bridge.CreateShareLink(a, TimeSpan.FromHours(1), null)!).AbsolutePath;
        Assert.True(env.Bridge.Shares.RevokeLink(revoked[3..]));
        Assert.Equal(HttpStatusCode.NotFound, (await anyone.GetAsync(env.Base + revoked)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anyone.GetAsync(env.Base + "/s/0123456789abcdef01234567")).StatusCode);
    });

    [Fact]
    public Task A_file_sent_to_the_PC_is_reported_with_its_sender() => With(async (env, _) =>
    {
        var token = await env.PairNew("Pixel", "inst-share-10", "&kind=phone");
        (PairedDevice Device, string Path)? got = null;
        env.Bridge.FileReceived += (d, p) => got = (d, p);
        var r = await env.PostBytes("/api/upload?filename=photo.jpg", new byte[1234], token);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("Pixel", got?.Device.Name);
        Assert.True(System.IO.File.Exists(got!.Value.Path));
    });
}
