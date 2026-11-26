using System.Net;
using CloudSSD.App;
using CloudSSD.Cli;
using CloudSSD.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace CloudSSD.Tests;

/// <summary>
/// The terminal key (cloudssd / cpx talking to the running app): who it lets in, from where, and how little it opens.
/// </summary>
public class CliBridgeTests
{
    private static Task With(Func<BridgeEnv, Task> body) => BridgeHarness.With(body);

    private static string KeyOf(BridgeEnv env) =>
        SecretStore.Unprotect(File.ReadAllText(Path.Combine(env.Host.Paths.DataRoot, LocalMobileBridgeService.CliKeyFileName)))!;

    private static Task<HttpResponseMessage> Send(BridgeEnv env, HttpMethod method, string url, string? key)
    {
        var req = new HttpRequestMessage(method, env.Base + url);
        if (key != null) req.Headers.Add("X-Pairing-Token", key);
        return env.Http.SendAsync(req);
    }

    [Fact]
    public Task The_key_is_written_protected_when_the_bridge_starts_and_opens_the_drive_routes() => With(async env =>
    {
        var file = Path.Combine(env.Host.Paths.DataRoot, LocalMobileBridgeService.CliKeyFileName);
        var key = KeyOf(env);
        Assert.Equal(32, key.Length);
        Assert.DoesNotContain(key, await File.ReadAllTextAsync(file));          // only the DPAPI-protected form is on disk

        Assert.Equal(HttpStatusCode.OK, (await Send(env, HttpMethod.Get, "/api/drives", key)).StatusCode);
        // A drive that is not connected: "already disconnected" (the route itself is open to the terminal).
        var r = await Send(env, HttpMethod.Post, "/api/drives/unmount?id=Q", key);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains("already disconnected", await r.Content.ReadAsStringAsync());
    });

    [Fact]
    public Task Without_the_key_or_with_a_wrong_one_nothing_opens() => With(async env =>
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(env, HttpMethod.Get, "/api/drives", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(env, HttpMethod.Get, "/api/drives", new string('a', 32))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(env, HttpMethod.Post, "/api/cli/send?path=x", null)).StatusCode);
    });

    [Fact]
    public Task The_key_counts_only_from_this_pc_itself() => With(env =>
    {
        var key = KeyOf(env);
        Assert.True(env.Bridge.IsCliCall(key, "127.0.0.1"));
        Assert.True(env.Bridge.IsCliCall(key, "::1"));
        Assert.True(env.Bridge.IsCliCall(key, "::ffff:127.0.0.1"));
        Assert.False(env.Bridge.IsCliCall(key, "192.168.1.4"));               // the right key from the Wi-Fi: no
        Assert.False(env.Bridge.IsCliCall(key, "100.64.0.7"));                 // Tailscale: no
        Assert.False(env.Bridge.IsCliCall(key, "unknown"));
        // Loopback alone is nothing (a phone on "adb reverse" also arrives as 127.0.0.1): the key carries the weight.
        Assert.False(env.Bridge.IsCliCall(null, "127.0.0.1"));
        Assert.False(env.Bridge.IsCliCall("", "127.0.0.1"));
        Assert.False(env.Bridge.IsCliCall(BridgeHarness.Shared, "127.0.0.1")); // a phone's key is not the terminal key
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData("/api/files?path=C%3A%5C")]
    [InlineData("/api/fs/roots")]
    [InlineData("/api/fs/list?path=x")]
    [InlineData("/api/settings")]
    [InlineData("/api/devices")]
    [InlineData("/api/download?path=x")]
    [InlineData("/api/whenoff/key")]
    [InlineData("/api/clean")]
    public Task The_key_opens_nothing_but_drives_and_its_own_routes(string url) => With(async env =>
    {
        var r = await Send(env, HttpMethod.Get, url, KeyOf(env));
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Contains("terminal key does not open this", await r.Content.ReadAsStringAsync());
    });

    [Fact]
    public void Only_the_listed_routes_are_allowed()
    {
        Assert.True(LocalMobileBridgeService.CliRouteAllowed("/api/drives/mount"));
        Assert.True(LocalMobileBridgeService.CliRouteAllowed("/api/cli/send"));
        Assert.False(LocalMobileBridgeService.CliRouteAllowed("/api/drives/../files"));
        Assert.False(LocalMobileBridgeService.CliRouteAllowed("/api/drivesX"));
        Assert.False(LocalMobileBridgeService.CliRouteAllowed("/api/clipboard"));
    }

    [Fact]
    public Task A_phone_cannot_use_the_terminal_routes_even_from_loopback() => With(async env =>
    {
        // The harness talks over 127.0.0.1 with a phone's (legacy) key: paired, local, and still not the terminal.
        var r = await env.Post("/api/cli/send?path=x");
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Contains("Only the terminal", await r.Content.ReadAsStringAsync());
    });

    [Fact]
    public Task The_pc_wide_switch_still_applies_to_the_terminal() => With(async env =>
    {
        env.Host.Settings.MobileBridge.AllowPcControl = false;
        var r = await Send(env, HttpMethod.Post, "/api/drives/unmount?id=Q", KeyOf(env));
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Equal("switched-off", (await env.Json(r)).GetProperty("code").GetString());
    });

    [Fact]
    public Task A_second_process_that_cannot_get_the_port_does_not_replace_the_key() => With(async env =>
    {
        var key = KeyOf(env);
        await using (var second = new LocalMobileBridgeService(env.Host, NullLogger<LocalMobileBridgeService>.Instance))
        {
            second.Start();                                                    // the port is taken: it only logs and stops
            Assert.False(second.IsRunning);
        }
        Assert.Equal(key, KeyOf(env));
        Assert.Equal(HttpStatusCode.OK, (await Send(env, HttpMethod.Get, "/api/drives", key)).StatusCode);
    });

    [Fact]
    public Task Send_offers_a_file_to_the_paired_phone_and_says_so_when_there_is_nothing() => With(async env =>
    {
        await env.PairNew("Test Phone", "install-cli-0001");
        var file = Path.Combine(env.Temp, "trip.jpg");
        await File.WriteAllBytesAsync(file, new byte[1234]);
        var port = new Uri(env.Base).Port;

        // The CLI's own client: reads the key file, talks to 127.0.0.1 only.
        var ok = await AppLink.PostAsync(env.Host.Paths, port, $"/api/cli/send?device=&path={Uri.EscapeDataString(file)}", TimeSpan.FromSeconds(20), default);
        Assert.True(ok.Ok, ok.Message);
        Assert.Equal(1, ok.Json!.Value.GetProperty("files").GetInt32());
        Assert.Equal(1234, ok.Json!.Value.GetProperty("bytes").GetInt64());
        Assert.Equal("Test Phone", ok.Json!.Value.GetProperty("device").GetString());

        var missing = await AppLink.PostAsync(env.Host.Paths, port, $"/api/cli/send?device=phone&path={Uri.EscapeDataString(file + ".nope")}", TimeSpan.FromSeconds(20), default);
        Assert.False(missing.Ok);
        Assert.Contains("Nothing to send", missing.Message);

        var nobody = await AppLink.PostAsync(env.Host.Paths, port, $"/api/cli/send?device=iphone&path={Uri.EscapeDataString(file)}", TimeSpan.FromSeconds(20), default);
        Assert.Contains("No paired device called 'iphone'", nobody.Message);
    });

    [Fact]
    public async Task Without_a_key_file_the_terminal_says_what_to_do_instead_of_calling_the_app()
    {
        var home = Path.Combine(Path.GetTempPath(), "cloudssd-cli-nokey-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(home);
        try
        {
            var r = await AppLink.PostAsync(new CloudSSD.Core.AppPaths(home), 9, "/api/drives/mount?id=Z", TimeSpan.FromSeconds(5), default);
            Assert.False(r.Ok);
            Assert.True(r.Message.Contains("/start") || r.Message.Contains("older version"), r.Message);
        }
        finally
        {
            Directory.Delete(home, true);
        }
    }

    [Theory]
    [InlineData("z", "Z")]
    [InlineData("Z:", "Z")]
    [InlineData(@"y:\", "Y")]
    [InlineData("cloudssd", "cloudssd")]
    public void Drive_names_are_sent_the_way_the_app_expects(string typed, string expected) =>
        Assert.Equal(expected, AppLink.DriveId(typed));
}
