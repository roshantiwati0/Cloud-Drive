using System.Net;
using System.Text;
using CloudSSD.App;
using Xunit;

namespace CloudSSD.Tests;

/// <summary>This PC asking a phone (list a folder, send a file): the phone's long request, its answers, the limits.</summary>
public class PeerTests
{
    private static string IdOf(BridgeEnv env, string name) => env.Host.Settings.MobileBridge.PairedDevices.Single(d => d.Name == name).Id;

    private static async Task<(string Job, string Kind)> NextJob(BridgeEnv env, string token)
    {
        var r = await env.Json(await env.Get("/api/peer/inbox?wait=10", token));
        var job = r.GetProperty("job");
        return (job.GetProperty("id").GetString()!, job.GetProperty("kind").GetString()!);
    }

    [Fact]
    public Task A_question_reaches_the_waiting_phone_and_its_answer_comes_back() => BridgeHarness.With(async env =>
    {
        var token = await env.PairNew("Pixel", "inst-peer-01", "&kind=phone");
        var id = IdOf(env, "Pixel");
        Assert.False(env.Bridge.Peer.IsListening(id));
        Assert.False(env.Bridge.GetDevice(id)!.Listening);           // the card hides Browse / Ring / Send clipboard

        var waiting = NextJob(env, token);                         // the phone waits first
        await Task.Delay(200);
        Assert.True(env.Bridge.Peer.IsListening(id));
        Assert.True(env.Bridge.GetDevice(id)!.Listening);            // ... and shows them now
        var ask = env.Bridge.AskDeviceAsync(id, "fs.list", new Dictionary<string, string> { ["path"] = "DCIM" }, TimeSpan.FromSeconds(10));
        var (job, kind) = await waiting;
        Assert.Equal("fs.list", kind);

        var body = new StringContent("{\"folders\":[{\"name\":\"Camera\"}],\"files\":[]}", Encoding.UTF8, "application/json");
        var answered = await env.Http.PostAsync($"{env.Base}/api/peer/result?job={job}&token={token}", body);
        Assert.Equal(HttpStatusCode.OK, answered.StatusCode);
        var result = await ask;
        Assert.True(result.Ok);
        Assert.Equal("Camera", result.Json!.Value.GetProperty("folders")[0].GetProperty("name").GetString());
    });

    [Fact]
    public Task A_file_comes_back_byte_for_byte_and_only_the_phone_asked_may_answer() => BridgeHarness.With(async env =>
    {
        var token = await env.PairNew("Pixel", "inst-peer-02", "&kind=phone");
        var other = await env.PairNew("Tab", "inst-peer-03", "&kind=tablet");
        var ask = env.Bridge.AskDeviceAsync(IdOf(env, "Pixel"), "fs.read", new Dictionary<string, string> { ["path"] = "DCIM/a.jpg" }, TimeSpan.FromSeconds(10));
        var (job, _) = await NextJob(env, token);                   // queued before the phone asked: handed at once

        var bytes = new byte[300_000];
        new Random(3).NextBytes(bytes);
        Assert.Equal(HttpStatusCode.NotFound, (await env.PostBytes($"/api/peer/result?job={job}", bytes, other)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await env.PostBytes($"/api/peer/result?job={job}", bytes, token)).StatusCode);
        var result = await ask;
        Assert.True(result.Ok);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(result.FilePath!));
        File.Delete(result.FilePath!);
    });

    [Fact]
    public Task The_phone_saying_ask_me_first_reaches_the_PC() => BridgeHarness.With(async env =>
    {
        var token = await env.PairNew("Pixel", "inst-peer-04", "&kind=phone");
        var ask = env.Bridge.AskDeviceAsync(IdOf(env, "Pixel"), "fs.roots", new Dictionary<string, string>(), TimeSpan.FromSeconds(10));
        var (job, _) = await NextJob(env, token);
        await env.Post($"/api/peer/result?job={job}&status=needs-permission", token);
        Assert.Equal("needs-permission", (await ask).Status);
    });

    [Fact]
    public Task Nobody_listening_is_a_timeout_and_a_second_wait_ends_the_first() => BridgeHarness.With(async env =>
    {
        var token = await env.PairNew("Pixel", "inst-peer-05", "&kind=phone");
        var r = await env.Bridge.AskDeviceAsync(IdOf(env, "Pixel"), "fs.roots", new Dictionary<string, string>(), TimeSpan.FromMilliseconds(300));
        Assert.Equal("timeout", r.Status);

        var first = env.Get("/api/peer/inbox?wait=20", token);
        await Task.Delay(300);
        var second = env.Get("/api/peer/inbox?wait=1", token);
        var firstAnswer = await env.Json(await first.WaitAsync(TimeSpan.FromSeconds(5)));   // ended by the second, long before 20 s
        Assert.False(firstAnswer.TryGetProperty("job", out var j) && j.ValueKind != System.Text.Json.JsonValueKind.Null);   // no question
        await second;
    });

    [Fact]
    public Task An_answer_bigger_than_the_limit_is_refused() => BridgeHarness.With(async env =>
    {
        var token = await env.PairNew("Pixel", "inst-peer-06", "&kind=phone");
        var ask = env.Bridge.AskDeviceAsync(IdOf(env, "Pixel"), "fs.list", new Dictionary<string, string>(), TimeSpan.FromSeconds(10));
        var (job, _) = await NextJob(env, token);
        var big = new byte[LocalMobileBridgeService.MaxPeerJson + 1];
        // Refused before it is read: the PC answers 413 and closes, which the sender may see as a reset.
        try { Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await env.PostBytes($"/api/peer/result?job={job}", big, token)).StatusCode); }
        catch (HttpRequestException) { }
        Assert.Equal("error", (await ask).Status);
    });

    [Fact]
    public Task Battery_and_storage_ride_on_the_wait_and_ring_and_clipboard_reach_the_phone() => BridgeHarness.With(async env =>
    {
        var token = await env.PairNew("Pixel", "inst-peer-07", "&kind=phone");
        var id = IdOf(env, "Pixel");
        await env.Get("/api/peer/inbox?wait=0&battery=83&charging=1&free=12000000000&total=128000000000", token);
        var v = env.Bridge.GetDevice(id)!.Vitals!;
        Assert.Equal(83, v.Battery);
        Assert.True(v.Charging);
        Assert.Equal(12000000000, v.FreeBytes);

        var ring = env.Bridge.RingDeviceAsync(id);
        var (job, kind) = await NextJob(env, token);
        Assert.Equal("phone.ring", kind);
        await env.Post($"/api/peer/result?job={job}&status=ok", token);
        Assert.Equal("error", (await ring).Status);              // an empty answer is not JSON: the PC says so

        (string Name, string Text)? got = null;
        env.Bridge.ClipboardReceived += (d, text) => got = (d.Name, text);
        Assert.Equal(HttpStatusCode.OK, (await env.Post("/api/clip?text=" + Uri.EscapeDataString("hello from the phone"), token)).StatusCode);
        Assert.Equal(("Pixel", "hello from the phone"), got);
        Assert.Equal(HttpStatusCode.BadRequest, (await env.Post("/api/clip?text=" + new string('x', 4001), token)).StatusCode);
    });

    [Fact]
    public Task A_laptop_sees_a_phones_files_through_the_PC_as_far_as_the_phone_allows() => BridgeHarness.With(async env =>
    {
        var phone = await env.PairNew("Pixel", "inst-relay-01", "&kind=phone");
        var laptop = await env.PairNew("Dell", "inst-relay-02", "&kind=laptop");
        var phoneId = IdOf(env, "Pixel");

        // The PC's phones, for the laptop to pick.
        var devs = await env.Json(await env.Get("/api/peer/devices", laptop));
        Assert.Contains(devs.GetProperty("devices").EnumerateArray(), d => d.GetProperty("id").GetString() == phoneId);
        Assert.DoesNotContain(devs.GetProperty("devices").EnumerateArray(), d => d.GetProperty("name").GetString() == "Dell");

        // A list: the phone is asked (it is told who asks) and its answer comes back to the laptop.
        var waiting = env.Get("/api/peer/inbox?wait=10", phone);              // the phone waits first
        await Task.Delay(200);
        var list = env.Get($"/api/peer/fs/list?device={phoneId}&path=DCIM", laptop);
        var job = (await env.Json(await waiting)).GetProperty("job");
        Assert.Equal("fs.list", job.GetProperty("kind").GetString());
        Assert.Equal("Dell", job.GetProperty("args").GetProperty("asker").GetString());
        var body = new StringContent("{\"folders\":[{\"name\":\"Camera\"}],\"files\":[]}", Encoding.UTF8, "application/json");
        await env.Http.PostAsync($"{env.Base}/api/peer/result?job={job.GetProperty("id").GetString()}&token={phone}", body);
        var answer = await env.Json(await list);
        Assert.Equal("ok", answer.GetProperty("status").GetString());
        Assert.Equal("Camera", answer.GetProperty("data").GetProperty("folders")[0].GetProperty("name").GetString());

        // A file: its bytes, as the phone sent them.
        var bytes = new byte[30_000];
        new Random(7).NextBytes(bytes);
        waiting = env.Get("/api/peer/inbox?wait=10", phone);
        await Task.Delay(200);
        var read = env.Get($"/api/peer/fs/read?device={phoneId}&path=DCIM/a.jpg", laptop);
        job = (await env.Json(await waiting)).GetProperty("job");
        await env.PostBytes($"/api/peer/result?job={job.GetProperty("id").GetString()}&status=ok", bytes, phone);
        var file = await read;
        Assert.Equal(HttpStatusCode.OK, file.StatusCode);
        Assert.Equal(bytes, await file.Content.ReadAsByteArrayAsync());

        // Not a phone, or not allowed by this PC: refused.
        Assert.Equal(HttpStatusCode.NotFound, (await env.Get($"/api/peer/fs/roots?device={IdOf(env, "Dell")}", phone)).StatusCode);
        env.Bridge.Access.Revoke(env.Host.Settings.MobileBridge.PairedDevices.Single(d => d.Name == "Dell"), AccessCategories.Relay);
        var refused = await env.Get($"/api/peer/fs/roots?device={phoneId}", laptop);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Contains("relay", await refused.Content.ReadAsStringAsync());
    });
}
