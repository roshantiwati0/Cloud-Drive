using System.Net;
using CloudSSD.App;
using CloudSSD.Core;
using Xunit;

namespace CloudSSD.Tests;

/// <summary>Per-device permissions: grants on top of the switches, asking the PC, answers, limits, revoking, the log.</summary>
public class PeerAccessTests
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

    private static PairedDevice Dev(BridgeEnv env, string name) => env.Host.Settings.MobileBridge.PairedDevices.Single(d => d.Name == name);

    private static string Files(BridgeEnv env) => "/api/files?path=" + Uri.EscapeDataString(env.Cloud);

    private static async Task<string> Ask(BridgeEnv env, string token, string categories)
    {
        var r = await env.Post("/api/access/request?categories=" + categories, token);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return (await env.Json(r)).GetProperty("request").GetProperty("id").GetString()!;
    }

    [Fact]
    public Task A_new_device_gets_the_defaults_and_a_device_from_before_keeps_everything() => With(async (env, _) =>
    {
        var token = await env.PairNew("Pixel", "inst-acc-0001");
        Assert.Equal(AccessCategories.All.OrderBy(c => c), Dev(env, "Pixel").Grants!.Select(g => g.Category).OrderBy(c => c));
        Assert.Equal(HttpStatusCode.OK, (await env.Get(Files(env), token)).StatusCode);

        Dev(env, "Pixel").Grants = null;                          // as saved before per-device permissions
        Assert.Equal(HttpStatusCode.OK, (await env.Get(Files(env), token)).StatusCode);
        Assert.True(env.Bridge.Access.Has(Dev(env, "Pixel"), AccessCategories.Control));
    });

    [Fact]
    public Task New_devices_get_only_what_the_PC_chose_for_them() => With(async (env, _) =>
    {
        env.Host.Settings.MobileBridge.NewDeviceDefaults = [AccessCategories.Browse];
        var token = await env.PairNew("Pixel", "inst-acc-0002");
        Assert.Equal(HttpStatusCode.OK, (await env.Get(Files(env), token)).StatusCode);
        var r = await env.Get("/api/download?path=" + Uri.EscapeDataString(Path.Combine(env.Cloud, "x.txt")), token);
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        var j = await env.Json(r);
        Assert.Equal("needs-permission", j.GetProperty("code").GetString());
        Assert.Equal(new[] { AccessCategories.Stream }, j.GetProperty("categories").EnumerateArray().Select(c => c.GetString()));
    });

    [Fact]
    public Task A_switch_that_is_off_wins_over_a_grant() => With(async (env, _) =>
    {
        var token = await env.PairNew("Pixel", "inst-acc-0003");
        env.Host.Settings.MobileBridge.AllowDriveBrowsing = false;
        var r = await env.Get(Files(env), token);
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Equal("switched-off", (await env.Json(r)).GetProperty("code").GetString());
    });

    [Fact]
    public Task Asking_the_PC_and_being_allowed_lets_it_in_and_the_log_shows_both() => With(async (env, _) =>
    {
        var token = await env.PairNew("Pixel", "inst-acc-0004");
        var d = Dev(env, "Pixel");
        env.Bridge.Access.Set(d, AccessCategories.All.Where(c => c != AccessCategories.Browse));
        Assert.Equal(HttpStatusCode.Forbidden, (await env.Get(Files(env), token)).StatusCode);

        AccessRequest? seen = null;
        env.Bridge.Access.RequestCreated += r => seen = r;
        var id = await Ask(env, token, "browse");
        Assert.Equal(id, seen?.Id);
        Assert.Equal("Pixel", seen!.DeviceName);
        Assert.Equal(new[] { "browse" }, seen.Categories);

        // A device cannot answer its own question.
        Assert.Equal(HttpStatusCode.NotFound, (await env.Post("/api/access/decide?id=" + id + "&mode=always", token)).StatusCode);
        Assert.Equal("pending", env.Bridge.Access.Get(id)!.State);

        Assert.True(env.Bridge.Access.Decide(id, AccessMode.Always));
        Assert.Equal(HttpStatusCode.OK, (await env.Get(Files(env), token)).StatusCode);
        var mine = await env.Json(await env.Get("/api/access/mine", token));
        Assert.Equal("allowed", mine.GetProperty("request").GetProperty("state").GetString());
        var browse = mine.GetProperty("categories").EnumerateArray().Single(c => c.GetProperty("category").GetString() == "browse");
        Assert.True(browse.GetProperty("allowed").GetBoolean());
        Assert.Equal("always", browse.GetProperty("mode").GetString());

        var log = env.Bridge.AccessLog.Read(d.Id);
        Assert.Contains(log, e => e.Category == "browse" && !e.Allowed);
        Assert.Contains(log, e => e.Category == "browse" && e.Allowed && e.Target == env.Cloud);
    });

    [Fact]
    public Task An_hour_runs_out_and_this_session_ends_with_the_app() => With(async (env, clock) =>
    {
        var token = await env.PairNew("Pixel", "inst-acc-0005");
        var d = Dev(env, "Pixel");
        env.Bridge.Access.Set(d, Array.Empty<string>());
        env.Bridge.Access.Grant(d, [AccessCategories.Browse], AccessMode.Hour);
        env.Bridge.Access.Grant(d, [AccessCategories.Stream], AccessMode.Session);
        Assert.Equal(HttpStatusCode.OK, (await env.Get(Files(env), token)).StatusCode);
        clock.Add(TimeSpan.FromMinutes(61));
        Assert.Equal(HttpStatusCode.Forbidden, (await env.Get(Files(env), token)).StatusCode);

        Assert.True(env.Bridge.Access.Has(d, AccessCategories.Stream));
        d.Grants!.Single(g => g.Category == AccessCategories.Stream).Session = "an-earlier-run";   // the app restarted since
        Assert.False(env.Bridge.Access.Has(d, AccessCategories.Stream));
    });

    [Fact]
    public Task Asking_is_limited() => With(async (env, clock) =>
    {
        var token = await env.PairNew("Pixel", "inst-acc-0006");
        var d = Dev(env, "Pixel");
        env.Bridge.Access.Set(d, Array.Empty<string>());

        // One question at a time: asking again gives the same one.
        var first = await Ask(env, token, "browse");
        Assert.Equal(first, await Ask(env, token, "browse,stream"));

        // It runs out after two minutes.
        clock.Add(TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(1));
        Assert.Equal("expired", (await env.Json(await env.Get("/api/access/mine", token))).GetProperty("request").GetProperty("state").GetString());

        // Three refusals in a row: a half-hour pause.
        for (int i = 0; i < 3; i++) Assert.True(env.Bridge.Access.Decide(await Ask(env, token, "browse"), null));
        var paused = await env.Post("/api/access/request?categories=browse", token);
        Assert.Equal((HttpStatusCode)429, paused.StatusCode);
        Assert.InRange((await env.Json(paused)).GetProperty("retryAfter").GetInt32(), 1700, 1800);

        // Six questions in ten minutes at most.
        clock.Add(TimeSpan.FromMinutes(31));
        var token2 = await env.PairNew("Tab", "inst-acc-0007");
        env.Bridge.Access.Set(Dev(env, "Tab"), Array.Empty<string>());
        for (int i = 0; i < AccessControl.MaxRequestsPerWindow; i++)
        {
            var id = await Ask(env, token2, "browse");
            Assert.True(env.Bridge.Access.Cancel(Dev(env, "Tab").Id, id));
        }
        Assert.Equal((HttpStatusCode)429, (await env.Post("/api/access/request?categories=browse", token2)).StatusCode);
    });

    [Fact]
    public Task Taking_a_permission_away_cuts_what_it_is_downloading() => With(async (env, _) =>
    {
        var token = await env.PairNew("Pixel", "inst-acc-0008");
        var file = Path.Combine(env.Cloud, "big.bin");
        await File.WriteAllBytesAsync(file, new byte[64 * 1024 * 1024]);
        using var resp = await env.Http.GetAsync($"{env.Base}/api/download?path={Uri.EscapeDataString(file)}&token={token}", HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        await using var body = await resp.Content.ReadAsStreamAsync();
        var buf = new byte[64 * 1024];
        long got = await body.ReadAsync(buf);

        env.Bridge.Access.Revoke(Dev(env, "Pixel"), AccessCategories.Stream);
        try
        {
            int n;
            while ((n = await body.ReadAsync(buf)) > 0) got += n;
        }
        catch (Exception e) when (e is IOException or HttpRequestException) { }
        Assert.True(got < 64L * 1024 * 1024, "the download should have stopped before the end");
        Assert.Equal(HttpStatusCode.Forbidden, (await env.Get("/api/download?path=" + Uri.EscapeDataString(file), token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await env.Get(Files(env), token)).StatusCode);   // the rest stays
    });

    [Fact]
    public Task A_device_can_give_something_up_itself() => With(async (env, _) =>
    {
        var token = await env.PairNew("Pixel", "inst-acc-0009");
        Assert.Equal(HttpStatusCode.OK, (await env.Post("/api/access/drop?category=browse", token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await env.Get(Files(env), token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await env.Post("/api/access/drop?category=everything", token)).StatusCode);
    });

    [Theory]
    [InlineData("/api/files", "GET")]
    [InlineData("/api/stream", "GET")]
    [InlineData("/api/upload", "POST")]
    [InlineData("/api/hud", "GET")]
    [InlineData("/api/fs/read", "GET")]
    [InlineData("/api/fs/write", "POST")]
    [InlineData("/api/fs/delete", "POST")]
    [InlineData("/api/drives/mount", "POST")]
    [InlineData("/api/space/summary", "GET")]
    [InlineData("/api/space/action", "POST")]
    [InlineData("/api/game/start", "POST")]
    [InlineData("/api/saves/restore", "POST")]
    public void Every_switch_a_route_needs_has_a_category(string path, string method)
    {
        var switches = LocalMobileBridgeService.RequiredSwitches(path, method);
        Assert.NotEmpty(switches);
        Assert.Equal(switches.Count, LocalMobileBridgeService.RequiredCategories(path, method).Count);
        Assert.All(LocalMobileBridgeService.RequiredCategories(path, method), c => Assert.True(AccessCategories.IsKnown(c)));
    }

    [Fact]
    public Task Forgetting_a_device_deletes_its_log() => With(async (env, _) =>
    {
        var token = await env.PairNew("Pixel", "inst-acc-0010");
        await env.Get(Files(env), token);
        var id = Dev(env, "Pixel").Id;
        Assert.NotEmpty(env.Bridge.AccessLog.Read(id));
        env.Bridge.RemoveDevice(id);
        Assert.Empty(env.Bridge.AccessLog.Read(id));
    });
}
