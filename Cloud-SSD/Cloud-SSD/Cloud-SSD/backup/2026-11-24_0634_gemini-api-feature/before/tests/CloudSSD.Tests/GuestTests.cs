using System.Net;
using CloudSSD.App;
using Xunit;

namespace CloudSSD.Tests;

/// <summary>Guests: a one-time code made at the PC, only the chosen categories, removed when the time is up.</summary>
public class GuestTests
{
    private sealed class Clock
    {
        public DateTimeOffset Now = DateTimeOffset.Now;
    }

    private static Task With(Func<BridgeEnv, Clock, Task> body)
    {
        var clock = new Clock();
        return BridgeHarness.With(env => body(env, clock), b => b.Now = () => clock.Now);
    }

    private static Task<HttpResponseMessage> Pair(BridgeEnv env, string code, string name, string installId) =>
        env.Http.PostAsync($"{env.Base}/api/pair?code={code}&name={name}&installId={installId}&kind=phone", null);

    [Fact]
    public Task A_guest_may_do_only_what_its_code_allowed_and_is_removed_when_its_time_is_up() => With(async (env, clock) =>
    {
        var invite = env.Bridge.CreateGuestInvite(TimeSpan.FromHours(1), [AccessCategories.Browse, AccessCategories.Stream, "nonsense"]);
        Assert.NotEqual(env.Bridge.PairingCode, invite.Code);
        Assert.Equal(new[] { AccessCategories.Browse, AccessCategories.Stream }, invite.Categories);
        Assert.Single(env.Bridge.GuestInvites);

        var r = await Pair(env, invite.Code, "Friend", "inst-guest-01");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var token = (await env.Json(r)).GetProperty("token").GetString()!;
        Assert.Empty(env.Bridge.GuestInvites);                                    // used up
        Assert.Equal(HttpStatusCode.Forbidden, (await Pair(env, invite.Code, "Other", "inst-guest-02")).StatusCode);

        var guest = env.Host.Settings.MobileBridge.PairedDevices.Single(d => d.Name == "Friend");
        Assert.Equal(clock.Now.AddHours(1), guest.GuestUntil);
        Assert.Equal("guest-inst-guest-01", guest.InstallId);
        Assert.All(guest.Grants!, g => Assert.Equal(("timed", guest.GuestUntil), (g.Mode, g.ExpiresAt)));
        Assert.Equal(guest.GuestUntil, env.Bridge.GetDevices().Single(d => d.Name == "Friend").GuestUntil);

        var status = await env.Json(await env.Get("/api/status", token));
        Assert.Equal(guest.GuestUntil!.Value.ToUnixTimeMilliseconds(), status.GetProperty("guestUntil").GetInt64());
        Assert.Equal(HttpStatusCode.OK, (await env.Get("/api/fs/roots", token)).StatusCode);
        var write = await env.PostBytes("/api/fs/write?path=" + Uri.EscapeDataString("cloudssd:g.txt"), [1, 2, 3], token);
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);                  // only Browse and Play were allowed

        // Time is up: not let in any more, and the sweep removes it.
        clock.Now = clock.Now.AddHours(1).AddSeconds(1);
        Assert.Equal(HttpStatusCode.Unauthorized, (await env.Get("/api/fs/roots", token)).StatusCode);
        Assert.Equal(1, env.Bridge.EndExpiredGuests());
        Assert.DoesNotContain(env.Host.Settings.MobileBridge.PairedDevices, d => d.Name == "Friend");
    });

    [Fact]
    public Task A_guest_code_never_turns_the_owners_own_phone_into_a_guest() => With(async (env, clock) =>
    {
        // The owner's phone (install "inst-own") is paired normally; someone types a guest code on the same install.
        var own = await env.PairNew("Pixel", "inst-own", "&kind=phone");
        var invite = env.Bridge.CreateGuestInvite(TimeSpan.FromDays(1), [AccessCategories.Browse]);
        Assert.Equal(HttpStatusCode.OK, (await Pair(env, invite.Code, "Pixel", "inst-own")).StatusCode);

        var list = env.Host.Settings.MobileBridge.PairedDevices.Where(d => d.Name == "Pixel").ToList();
        Assert.Equal(2, list.Count);                                               // its own entry and a guest one
        Assert.Null(list.Single(d => d.InstallId == "inst-own").GuestUntil);
        clock.Now = clock.Now.AddDays(2);
        env.Bridge.EndExpiredGuests();
        Assert.Equal(HttpStatusCode.OK, (await env.Get("/api/fs/roots", own)).StatusCode);
    });

    [Fact]
    public Task Guest_codes_run_out_and_need_something_allowed() => With(async (env, clock) =>
    {
        Assert.Throws<ArgumentException>(() => env.Bridge.CreateGuestInvite(TimeSpan.FromHours(1), ["nonsense"]));
        Assert.Throws<ArgumentOutOfRangeException>(() => env.Bridge.CreateGuestInvite(TimeSpan.FromDays(8), [AccessCategories.Browse]));
        var invite = env.Bridge.CreateGuestInvite(TimeSpan.FromHours(1), [AccessCategories.Browse]);
        clock.Now = clock.Now + LocalMobileBridgeService.GuestCodeLifetime + TimeSpan.FromSeconds(1);
        Assert.Empty(env.Bridge.GuestInvites);
        Assert.Equal(HttpStatusCode.Forbidden, (await Pair(env, invite.Code, "Late", "inst-late")).StatusCode);

        var second = env.Bridge.CreateGuestInvite(TimeSpan.FromHours(1), [AccessCategories.Browse]);
        env.Bridge.CancelGuestInvite(second.Code);
        Assert.Equal(HttpStatusCode.Forbidden, (await Pair(env, second.Code, "Late", "inst-late")).StatusCode);
    });
}
