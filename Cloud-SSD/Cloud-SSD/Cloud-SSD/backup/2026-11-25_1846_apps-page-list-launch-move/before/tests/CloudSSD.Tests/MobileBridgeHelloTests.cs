using System.Net;
using System.Security.Cryptography;
using System.Text;
using CloudSSD.App;
using Xunit;

namespace CloudSSD.Tests;

/// <summary>/api/hello: a device proves it reached its own PC before it sends its key there.</summary>
public class MobileBridgeHelloTests
{
    private const string Nonce = "00112233445566778899aabbccddeeff";

    /// <summary>What the phone computes: HMAC-SHA256(SHA-256(key), nonce + "\n" + pcId), lowercase hex.</summary>
    private static string Expected(string token, string nonce, string pcId) =>
        Convert.ToHexString(HMACSHA256.HashData(SHA256.HashData(Encoding.UTF8.GetBytes(token)), Encoding.UTF8.GetBytes(nonce + "\n" + pcId))).ToLowerInvariant();

    [Fact]
    public Task A_paired_device_gets_a_proof_only_its_own_key_can_check() => BridgeHarness.With(async env =>
    {
        var token = await env.PairNew("PhoneA", "install-hello-01");
        var other = await env.PairNew("PhoneB", "install-hello-02");
        var id = env.Host.Settings.MobileBridge.PairedDevices.Single(d => d.Name == "PhoneA").Id;
        var pcId = env.Host.Settings.MobileBridge.PcId;

        var r = await env.Get($"/api/hello?nonce={Nonce}&d={id}", token: null);   // no key sent
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var j = await env.Json(r);
        Assert.Equal(pcId, j.GetProperty("pcId").GetString());
        var proof = j.GetProperty("proof").GetString()!;
        Assert.Equal(Expected(token, Nonce, pcId), proof);
        Assert.NotEqual(Expected(other, Nonce, pcId), proof);
        Assert.Contains("hello-proof", j.GetProperty("features").EnumerateArray().Select(f => f.GetString()));
        Assert.DoesNotContain(token, await r.Content.ReadAsStringAsync());

        // A fresh nonce gives a fresh proof (an old answer cannot be replayed).
        var again = await env.Json(await env.Get($"/api/hello?nonce={new string('a', 32)}&d={id}", token: null));
        Assert.NotEqual(proof, again.GetProperty("proof").GetString());
    });

    [Fact]
    public Task Unknown_and_removed_devices_get_a_proof_that_never_matches() => BridgeHarness.With(async env =>
    {
        var token = await env.PairNew("Phone", "install-hello-03");
        var dev = env.Host.Settings.MobileBridge.PairedDevices.Single();
        var pcId = env.Host.Settings.MobileBridge.PcId;

        var unknown = await env.Json(await env.Get($"/api/hello?nonce={Nonce}&d=deadbeef", token: null));
        Assert.Equal(64, unknown.GetProperty("proof").GetString()!.Length);   // same shape as a real one

        env.Bridge.RemoveDevice(dev.Id);
        var removed = await env.Json(await env.Get($"/api/hello?nonce={Nonce}&d={dev.Id}", token: null));
        Assert.NotEqual(Expected(token, Nonce, pcId), removed.GetProperty("proof").GetString());
        // Removed: "not paired" with nothing more, so the phone forgets its key.
        var status = await env.Json(await env.Get("/api/status", token));
        Assert.False(status.GetProperty("isPaired").GetBoolean());
        Assert.False(status.TryGetProperty("blocked", out _));
    });

    [Fact]
    public Task A_blocked_phone_still_knows_its_PC_keeps_its_key_and_comes_back_when_unblocked() => BridgeHarness.With(async env =>
    {
        // Found live: blocking a phone made it take "not paired" for "removed" and forget its key, so unblocking it
        // did not bring it back (it had to pair again).
        var token = await env.PairNew("Phone", "install-hello-04");
        var dev = env.Host.Settings.MobileBridge.PairedDevices.Single();
        var pcId = env.Host.Settings.MobileBridge.PcId;
        Assert.True(env.Bridge.SetBlocked(dev.Id, true));

        var hello = await env.Json(await env.Get($"/api/hello?nonce={Nonce}&d={dev.Id}", token: null));
        Assert.Equal(Expected(token, Nonce, pcId), hello.GetProperty("proof").GetString());   // still its own PC
        var status = await env.Json(await env.Get("/api/status", token));
        Assert.False(status.GetProperty("isPaired").GetBoolean());
        Assert.True(status.GetProperty("blocked").GetBoolean());
        Assert.False(status.TryGetProperty("drives", out _));                  // nothing more than an unpaired answer
        Assert.Equal(HttpStatusCode.Unauthorized, (await env.Get("/api/files", token)).StatusCode);
        // Anyone without the key learns nothing: no "blocked" for them.
        Assert.False((await env.Json(await env.Get("/api/status", token: null))).TryGetProperty("blocked", out _));
        Assert.False((await env.Json(await env.Get("/api/status", "0123456789abcdef0123456789abcdee"))).TryGetProperty("blocked", out _));

        Assert.True(env.Bridge.SetBlocked(dev.Id, false));
        Assert.True((await env.Json(await env.Get("/api/status", token))).GetProperty("isPaired").GetBoolean());   // same key works again
    });

    [Fact]
    public Task Old_phones_with_the_shared_key_can_check_it_until_it_is_retired() => BridgeHarness.With(async env =>
    {
        var pcId = env.Host.Settings.MobileBridge.PcId;
        var ok = await env.Json(await env.Get($"/api/hello?nonce={Nonce}&d=legacy", token: null));
        Assert.Equal(Expected(BridgeHarness.Shared, Nonce, pcId), ok.GetProperty("proof").GetString());

        env.Bridge.RetireLegacyToken();
        var after = await env.Json(await env.Get($"/api/hello?nonce={Nonce}&d=legacy", token: null));
        Assert.NotEqual(Expected(BridgeHarness.Shared, Nonce, pcId), after.GetProperty("proof").GetString());
    });

    [Theory]
    [InlineData("")]
    [InlineData("abc")]                                       // too short
    [InlineData("zz112233445566778899aabbccddeeff")]          // not hex
    public Task Bad_nonces_are_refused(string nonce) => BridgeHarness.With(async env =>
        Assert.Equal(HttpStatusCode.BadRequest, (await env.Get($"/api/hello?nonce={nonce}&d=legacy", token: null)).StatusCode));

    [Fact]
    public Task Hello_is_rate_limited_per_address()
    {
        // A fixed time: on the real clock the requests could cross into the next minute, which starts a new count.
        var now = new DateTimeOffset(2026, 11, 13, 10, 0, 30, TimeSpan.Zero);
        return BridgeHarness.With(async env =>
        {
            HttpStatusCode last = HttpStatusCode.OK;
            for (int i = 0; i <= LocalMobileBridgeService.HelloPerMinute; i++)
                last = (await env.Get($"/api/hello?nonce={Nonce}&d=legacy", token: null)).StatusCode;
            Assert.Equal((HttpStatusCode)429, last);
            now = now.AddMinutes(1);                            // a minute later it answers again
            Assert.Equal(HttpStatusCode.OK, (await env.Get($"/api/hello?nonce={Nonce}&d=legacy", token: null)).StatusCode);
        }, b => b.Now = () => now);
    }

    [Fact]
    public void The_proof_matches_a_known_vector()
    {
        // Same inputs as tests/mobile-js/data.test.js, so the phone's JavaScript and the PC agree byte for byte.
        var hash = LocalMobileBridgeService.HashToken("test-token-123");
        const string vector = "a2d8b5b9440a419842bd3aa84e7ef7df2021b5c19aaebbb6e41e0cca0f1d7a34";
        Assert.Equal(vector, LocalMobileBridgeService.HelloProof(hash, Nonce, "a1b2c3d4e5f60708"));
        Assert.Equal(vector, Expected("test-token-123", Nonce, "a1b2c3d4e5f60708"));
    }
}
