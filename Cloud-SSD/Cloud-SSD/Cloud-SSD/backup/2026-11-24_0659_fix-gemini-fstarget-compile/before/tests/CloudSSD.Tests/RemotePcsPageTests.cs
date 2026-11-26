using CloudSSD.App;
using CloudSSD.App.Remote;
using CloudSSD.Core;
using CloudSSD.Desktop.ViewModels;
using Xunit;

namespace CloudSSD.Tests;

/// <summary>The "Other PCs" page against a real bridge (no window: page updates run inline, as they do on the UI thread).</summary>
[Collection("Language")]
public class RemotePcsPageTests
{
    private static async Task Until(Func<bool> done, int ms = 10000)
    {
        // No UI thread here: the page updates its lists from wherever an answer arrives, so a look can collide.
        bool Look() { try { return done(); } catch (Exception e) when (e is InvalidOperationException or NullReferenceException or ArgumentException) { return false; } }
        for (int i = 0; i < ms / 50 && !Look(); i++) await Task.Delay(50);
        Assert.True(Look(), "timed out");
    }

    private static async Task WithPage(Func<BridgeEnv, AppHost, RemotePcSession, RemotePcsViewModel, Task> body) => await BridgeHarness.With(async env =>
    {
        await File.WriteAllTextAsync(Path.Combine(env.Cloud, "notes.txt"), "hello");
        var dir = Path.Combine(Path.GetTempPath(), "cssd_laptop_page_" + Guid.NewGuid().ToString("N"));
        var host = AppHost.Create(new AppPaths(dir));
        try
        {
            var s = await host.RemotePcs.AddPcAsync(env.Base.Replace("http://", ""), env.Bridge.PairingCode);
            var vm = new RemotePcsViewModel(host);
            vm.OnShown();
            await Until(() => vm.Entries.Count > 0 && !vm.Busy);
            await body(env, host, s, vm);
        }
        finally
        {
            await host.DisposeAsync();
            try { Directory.Delete(dir, true); } catch { }
        }
    });

    [Fact]
    public Task Opening_a_folder_asks_that_PC_once_not_in_a_loop() => WithPage(async (env, _, s, vm) =>
    {
        int lists = 0;
        s.ListChanged += _ => Interlocked.Increment(ref lists);
        vm.OpenEntryCommand.Execute(vm.Entries.First(e => e.Id == "cloudssd:"));
        await Until(() => vm.Entries.Any(e => e.Name == "notes.txt") && !vm.Busy);
        await Task.Delay(1500);
        Assert.InRange(lists, 1, 3);                          // once, plus at most a background refresh, never a stream
        Assert.Null(vm.Message);
        Assert.Equal(new[] { "Drives", "All Accounts" }.Select(Desktop.LocalizationManager.T), vm.Crumbs.Select(c => c.Title));
    });

    [Fact]
    public Task A_file_sent_to_the_open_folder_shows_up_there() => WithPage(async (env, _, s, vm) =>
    {
        vm.OpenEntryCommand.Execute(vm.Entries.First(e => e.Id == "cloudssd:"));
        await Until(() => vm.Entries.Any(e => e.Name == "notes.txt") && !vm.Busy);
        var file = Path.Combine(env.Temp, "from-laptop.txt");
        await File.WriteAllTextAsync(file, "sent from the laptop");
        vm.EnqueueDrop(new[] { file });
        await Until(() => vm.Entries.Any(e => e.Name == "from-laptop.txt"));
        Assert.True(File.Exists(Path.Combine(env.Cloud, "from-laptop.txt")));
        Assert.True(vm.Entries.Single(e => e.Name == "from-laptop.txt").IsNew);
    });

    [Fact]
    public Task Keep_offline_marks_the_folder_and_its_header_says_so() => WithPage(async (env, _, s, vm) =>
    {
        var docs = Directory.CreateDirectory(Path.Combine(env.Cloud, "Docs")).FullName;
        await File.WriteAllTextAsync(Path.Combine(docs, "a.txt"), "aaa");
        vm.OpenEntryCommand.Execute(vm.Entries.First(e => e.Id == "cloudssd:"));
        await Until(() => vm.Entries.Any(e => e.Name == "Docs") && !vm.Busy);
        var item = vm.Entries.Single(e => e.Name == "Docs");
        Assert.Equal(Desktop.LocalizationManager.T("Keep offline"), vm.KeepOfflineText(item));
        vm.KeepOfflineCommand.Execute(item);
        await Until(() => vm.Entries.Single(e => e.Name == "Docs").KeptOffline);
        Assert.Equal(Desktop.LocalizationManager.T("Remove offline copy"), vm.KeepOfflineText(vm.Entries.Single(e => e.Name == "Docs")));
        await s.SyncPinsAsync();
        vm.OpenEntryCommand.Execute(vm.Entries.Single(e => e.Name == "Docs"));
        await Until(() => vm.Entries.Any(e => e.Name == "a.txt") && !vm.Busy);
        Assert.True(vm.Entries.Single(e => e.Name == "a.txt").KeptOffline);
        var updated = Desktop.LocalizationManager.T("Kept offline · updated {0}").Split("{0}")[0];
        Assert.Contains(updated, vm.HeaderSub);
        vm.UpCommand.Execute(null);
        await Until(() => vm.Entries.Any(e => e.Name == "Docs") && !vm.Busy);
        vm.KeepOfflineCommand.Execute(vm.Entries.Single(e => e.Name == "Docs"));
        Assert.Empty(s.Pins);
        Assert.False(vm.Entries.Single(e => e.Name == "Docs").KeptOffline);
    });
}
