using System.Net;
using System.Text;
using CloudSSD.App;
using Xunit;

namespace CloudSSD.Tests;

/// <summary>What devices deleted on the PC, and bringing it back from the Recycle Bin.</summary>
public class DeleteLogTests
{
    /// <summary>A "$I…" record as Windows 10+ (format 2) or Vista–8 (format 1) writes it.</summary>
    private static byte[] Record(string originalPath, DateTime deletedUtc, long size, int version)
    {
        var b = new List<byte>();
        b.AddRange(BitConverter.GetBytes((long)version));
        b.AddRange(BitConverter.GetBytes(size));
        b.AddRange(BitConverter.GetBytes(deletedUtc.ToFileTimeUtc()));
        if (version == 2)
        {
            b.AddRange(BitConverter.GetBytes(originalPath.Length + 1));
            b.AddRange(Encoding.Unicode.GetBytes(originalPath + "\0"));
        }
        else
        {
            var path = new byte[520];
            Encoding.Unicode.GetBytes(originalPath).CopyTo(path, 0);
            b.AddRange(path);
        }
        return b.ToArray();
    }

    private static string Temp() => Directory.CreateTempSubdirectory("cssd_bin_").FullName;

    [Fact]
    public void Both_record_formats_are_read_and_the_thing_is_put_back()
    {
        var root = Temp();
        try
        {
            var bin = Directory.CreateDirectory(Path.Combine(root, "bin")).FullName;
            var place = Directory.CreateDirectory(Path.Combine(root, "From phone", "Trip")).FullName;
            var photo = Path.Combine(place, "beach.png");
            var folder = Path.Combine(place, "Album");
            var when = new DateTime(2026, 11, 12, 10, 0, 0, DateTimeKind.Utc);
            File.WriteAllBytes(Path.Combine(bin, "$IAB12CD.png"), Record(photo, when, 3, version: 2));
            File.WriteAllText(Path.Combine(bin, "$RAB12CD.png"), "png");
            File.WriteAllBytes(Path.Combine(bin, "$IZZ99YY"), Record(folder, when.AddMinutes(1), 0, version: 1));
            Directory.CreateDirectory(Path.Combine(bin, "$RZZ99YY", "inside"));
            // A record whose content is gone (Windows emptied it, or it was restored by hand) is not offered.
            File.WriteAllBytes(Path.Combine(bin, "$IGONE00.txt"), Record(Path.Combine(place, "gone.txt"), when, 1, version: 2));
            File.WriteAllText(Path.Combine(bin, "junk.txt"), "not a record");

            var items = RecycleBin.Items(bin);
            Assert.Equal(2, items.Count);
            var found = Assert.Single(RecycleBin.Find(photo, bin));
            Assert.Equal(when, found.DeletedUtc);
            Assert.Equal(3, found.Size);
            Assert.Single(RecycleBin.Find(folder.ToUpperInvariant(), bin));       // Windows paths: any case

            Assert.Equal(photo, RecycleBin.Restore(found));
            Assert.Equal("png", File.ReadAllText(photo));
            Assert.False(File.Exists(Path.Combine(bin, "$IAB12CD.png")));
            Assert.Empty(RecycleBin.Find(photo, bin));

            // Something new has the folder's name now: it comes back next to it.
            Directory.CreateDirectory(folder);
            var back = RecycleBin.Restore(Assert.Single(RecycleBin.Find(folder, bin)));
            Assert.Equal(Path.Combine(place, "Album (restored)"), back);
            Assert.True(Directory.Exists(Path.Combine(back, "inside")));
            Assert.Throws<FileNotFoundException>(() => RecycleBin.Restore(found));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void The_log_keeps_each_devices_deletes_newest_first_and_restores_what_is_in_the_bin()
    {
        var root = Temp();
        try
        {
            var now = new DateTimeOffset(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);
            var log = new DeleteLog(Path.Combine(root, "device-deletes.json"), () => now);
            var bin = Directory.CreateDirectory(Path.Combine(root, "bin")).FullName;
            var file = Path.Combine(root, "notes.txt");
            var a = log.Add("dev-a", "Pixel", "notes.txt", "From phone", false, "recycle", file);
            now = now.AddMinutes(1);
            log.Add("dev-a", "Pixel", "Old", "Test Cloud › Docs", true, "cloud", null);
            log.Add("dev-b", "Tab", "x.mp4", @"C:\Videos", false, "gone", null);

            Assert.Equal(new[] { "Old", "notes.txt" }, log.ForDevice("dev-a").Select(i => i.Name));
            Assert.Equal(1, log.CountFor("dev-b"));
            // Saved: a new log over the same file sees the same.
            Assert.Equal(2, new DeleteLog(Path.Combine(root, "device-deletes.json"), () => now).CountFor("dev-a"));

            Assert.False(DeleteLog.InBin(a, bin));
            Assert.Throws<FileNotFoundException>(() => log.Restore(a.Id, bin));
            Assert.Throws<InvalidOperationException>(() => log.Restore(log.ForDevice("dev-a")[0].Id, bin)); // cloud trash

            File.WriteAllBytes(Path.Combine(bin, "$I000001.txt"), Record(file, a.At.UtcDateTime.AddSeconds(1), 5, 2));
            File.WriteAllText(Path.Combine(bin, "$R000001.txt"), "hello");
            Assert.True(DeleteLog.InBin(a, bin));
            Assert.Equal(file, log.Restore(a.Id, bin));
            Assert.Equal("hello", File.ReadAllText(file));
            Assert.NotNull(log.ForDevice("dev-a").Single(i => i.Id == a.Id).RestoredAt);
            Assert.False(DeleteLog.InBin(log.ForDevice("dev-a").Single(i => i.Id == a.Id), bin));

            // At most 500, and nothing older than 60 days.
            for (int i = 0; i < DeleteLog.MaxEntries + 20; i++) log.Add("dev-c", "Lots", "f" + i, "x", false, "cloud", null);
            Assert.Equal(DeleteLog.MaxEntries, log.ForDevice("dev-a").Count + log.ForDevice("dev-b").Count + log.CountFor("dev-c"));
            now = now.AddDays(61);
            log.Add("dev-d", "Late", "y", "x", false, "cloud", null);
            Assert.Single(log.ForDevice("dev-d"));
            Assert.Equal(0, log.CountFor("dev-c"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public Task A_phone_delete_in_From_phone_is_logged_and_comes_back_from_the_Recycle_Bin() => BridgeHarness.With(async env =>
    {
        var token = await env.PairNew("Pixel", "inst-del-01", "&kind=phone");
        var name = "cssd-test-" + Guid.NewGuid().ToString("N")[..8] + ".txt";
        var path = Path.Combine(env.Uploads, "Trip", name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "keep me");

        var r = await env.Post("/api/fs/delete?path=" + Uri.EscapeDataString("received:Trip/" + name), token);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.False(File.Exists(path));

        var id = env.Host.Settings.MobileBridge.PairedDevices.Single(d => d.Name == "Pixel").Id;
        var item = Assert.Single(env.Bridge.DeleteLog.ForDevice(id));
        Assert.Equal((name, "Received files › Trip", "recycle", "Pixel"), (item.Name, item.Where, item.Bin, item.DeviceName));
        Assert.Equal(path, item.OriginalPath);

        // This uses the real Recycle Bin of the test machine, and leaves nothing in it.
        Assert.True(DeleteLog.InBin(item));
        Assert.Equal(path, env.Bridge.DeleteLog.Restore(item.Id));
        Assert.Equal("keep me", File.ReadAllText(path));
    });
}
