using System.Net;
using System.Text;
using System.Text.Json;
using CloudSSD.App;
using CloudSSD.Core;
using Xunit;

namespace CloudSSD.Tests;

/// <summary>
/// This PC working in a phone's folders both ways (the Phone files page): files and folders into a phone folder (the
/// phone fetches each staged file), and whole phone folders onto this PC with their tree. A fake phone answers here.
/// </summary>
public class PhoneFilesTests
{
    private static PairedDevice Dev(BridgeEnv env, string name) => env.Host.Settings.MobileBridge.PairedDevices.Single(d => d.Name == name);

    /// <summary>The phone's side: answers each question with <paramref name="answer"/> until <paramref name="stop"/>.</summary>
    private static async Task FakePhone(BridgeEnv env, string token, Func<string, JsonElement, Task<HttpContent?>> answer, CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            JsonElement r;
            try { r = await env.Json(await env.Get("/api/peer/inbox?wait=2", token)); }
            catch (Exception) when (stop.IsCancellationRequested) { return; }
            if (!r.TryGetProperty("job", out var j) || j.ValueKind != JsonValueKind.Object) { await Task.Delay(100); continue; }
            var job = r.GetProperty("job");
            var id = job.GetProperty("id").GetString()!;
            var content = await answer(job.GetProperty("kind").GetString()!, job.GetProperty("args"));
            if (content is null) await env.Post($"/api/peer/result?job={id}&status=needs-permission&error=write", token);
            else await env.Http.PostAsync($"{env.Base}/api/peer/result?job={id}&token={token}", content);
        }
    }

    private static async Task<TransferTask> Ended(BridgeEnv env, string id)
    {
        for (int i = 0; i < 200; i++)
        {
            var t = env.Bridge.Tasks.Get(id)!;
            if (!t.IsOpen) return t;
            await Task.Delay(50);
        }
        throw new TimeoutException("The task did not end.");
    }

    private static string Temp(string name)
    {
        var d = Path.Combine(Path.GetTempPath(), "cssd_pf_" + name + "_" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(d);
        return d;
    }

    [Fact]
    public Task A_PC_folder_lands_in_the_phone_folder_with_its_tree_and_without_build_output() => BridgeHarness.With(async env =>
    {
        var token = await env.PairNew("Pixel", "inst-pf-01", "&kind=phone");
        var other = await env.PairNew("Tab", "inst-pf-02", "&kind=tablet");
        var root = Temp("send");
        var app = Path.Combine(root, "MyApp");
        Directory.CreateDirectory(Path.Combine(app, "src"));
        Directory.CreateDirectory(Path.Combine(app, "node_modules"));
        await File.WriteAllTextAsync(Path.Combine(app, "a.txt"), "alpha");
        await File.WriteAllTextAsync(Path.Combine(app, "src", "b.txt"), "beta");
        await File.WriteAllTextAsync(Path.Combine(app, "node_modules", "x.js"), "skip me");

        var got = new Dictionary<string, string>();
        HttpStatusCode otherTried = 0;
        using var stop = new CancellationTokenSource();
        var phone = FakePhone(env, token, async (kind, args) =>
        {
            Assert.Equal("fs.pull", kind);
            Assert.Equal("Download", args.GetProperty("path").GetString());
            var url = args.GetProperty("url").GetString()!;
            otherTried = (await env.Get(url, other)).StatusCode;           // another device cannot fetch it
            var text = await (await env.Get(url, token)).Content.ReadAsStringAsync();
            got[args.GetProperty("sub").GetString() + "|" + args.GetProperty("name").GetString()] = text;
            return new StringContent("{\"path\":\"x\",\"skipped\":false}", Encoding.UTF8, "application/json");
        }, stop.Token);

        var task = env.Bridge.SendToPhoneFolder(Dev(env, "Pixel"), [app], "Download");
        Assert.Equal("toPhone", task.Dir);
        Assert.Equal("folder", task.Kind);
        var ended = await Ended(env, task.Id);
        stop.Cancel();
        await phone;

        Assert.Equal("done", ended.State);
        Assert.Equal(2, ended.DoneFiles);
        Assert.Equal("alpha", got["MyApp|a.txt"]);
        Assert.Equal("beta", got["MyApp/src|b.txt"]);
        Assert.DoesNotContain(got.Keys, k => k.Contains("node_modules"));
        Assert.Equal(HttpStatusCode.NotFound, otherTried);
        Directory.Delete(root, true);
    });

    [Fact]
    public Task A_phone_folder_comes_to_the_PC_whole_and_a_taken_name_gets_a_new_one() => BridgeHarness.With(async env =>
    {
        var token = await env.PairNew("Pixel", "inst-pf-03", "&kind=phone");
        var dest = Temp("save");
        Directory.CreateDirectory(Path.Combine(dest, "Camera"));      // already there: the copy becomes "Camera (2)"
        var files = new Dictionary<string, string> { ["DCIM/Camera/a.jpg"] = "AAA", ["DCIM/Camera/2026/b.jpg"] = "BBBB" };
        using var stop = new CancellationTokenSource();
        var phone = FakePhone(env, token, (kind, args) =>
        {
            var path = args.GetProperty("path").GetString()!;
            HttpContent c = kind switch
            {
                "fs.tree" => new StringContent(JsonSerializer.Serialize(new
                {
                    path, name = "Camera", folders = new[] { "2026" },
                    files = new[] { new { rel = "a.jpg", path = "DCIM/Camera/a.jpg", size = 3 }, new { rel = "2026/b.jpg", path = "DCIM/Camera/2026/b.jpg", size = 4 } }
                }), Encoding.UTF8, "application/json"),
                "fs.read" => new ByteArrayContent(Encoding.UTF8.GetBytes(files[path])),
                _ => throw new InvalidOperationException(kind),
            };
            return Task.FromResult<HttpContent?>(c);
        }, stop.Token);

        var task = env.Bridge.SavePhoneItemsToPc(Dev(env, "Pixel"), [new PhoneItem("DCIM/Camera", "Camera", true, 0)], dest);
        var ended = await Ended(env, task.Id);
        stop.Cancel();
        await phone;

        Assert.Equal("done", ended.State);
        Assert.Equal(2, ended.Files);
        Assert.Equal("AAA", await File.ReadAllTextAsync(Path.Combine(dest, "Camera (2)", "a.jpg")));
        Assert.Equal("BBBB", await File.ReadAllTextAsync(Path.Combine(dest, "Camera (2)", "2026", "b.jpg")));
        Directory.Delete(dest, true);
    });

    [Fact]
    public Task A_phone_that_has_not_allowed_adding_files_stops_the_run_with_a_reason() => BridgeHarness.With(async env =>
    {
        var token = await env.PairNew("Pixel", "inst-pf-04", "&kind=phone");
        var root = Temp("deny");
        for (int i = 0; i < 3; i++) await File.WriteAllTextAsync(Path.Combine(root, $"f{i}.txt"), "x");
        int asked = 0;
        using var stop = new CancellationTokenSource();
        var phone = FakePhone(env, token, (kind, args) => { asked++; return Task.FromResult<HttpContent?>(null); }, stop.Token);

        var task = env.Bridge.SendToPhoneFolder(Dev(env, "Pixel"), Directory.GetFiles(root), "Download");
        var ended = await Ended(env, task.Id);
        stop.Cancel();
        await phone;

        Assert.Equal(1, asked);                                       // the rest is not tried: it would fail the same way
        Assert.Equal("failed", ended.State);
        Assert.All(ended.Failed, f => Assert.Equal("denied", f.Reason));
        Directory.Delete(root, true);
    });

    [Fact]
    public Task Cancelling_while_a_file_is_on_its_way_ends_the_phone_task_too() => BridgeHarness.With(async env =>
    {
        var token = await env.PairNew("Pixel", "inst-pf-05", "&kind=phone");
        var root = Temp("cancel");
        for (int i = 0; i < 5; i++) await File.WriteAllTextAsync(Path.Combine(root, $"f{i}.txt"), "x");
        var pulls = new SemaphoreSlim(0);
        string? ended = null;
        using var stop = new CancellationTokenSource();
        var phone = FakePhone(env, token, async (kind, args) =>
        {
            if (kind == "fs.pullEnd") { ended = args.GetProperty("task").GetString() + ":" + args.GetProperty("reason").GetString(); return new StringContent("{\"ok\":true}", Encoding.UTF8, "application/json"); }
            pulls.Release();
            await Task.Delay(1500);                                   // the file is still on its way when Cancel comes
            return new StringContent("{\"path\":\"x\",\"skipped\":false}", Encoding.UTF8, "application/json");
        }, stop.Token);

        var task = env.Bridge.SendToPhoneFolder(Dev(env, "Pixel"), Directory.GetFiles(root), "Download");
        Assert.True(await pulls.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.True(env.Bridge.Tasks.Cancel(task.Id));
        for (int i = 0; i < 100 && ended is null; i++) await Task.Delay(100);
        stop.Cancel();
        await phone;

        Assert.Equal(task.Id + ":cancelled", ended);                                 // the phone was told the run is over
        Assert.Equal("cancelled", env.Bridge.Tasks.Get(task.Id)!.State);
        Directory.Delete(root, true);
    });
}
