using CloudSSD.App;
using CloudSSD.Core;
using CloudSSD.Mount;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CloudSSD.Tests;

public class ArchiveWarmerTests
{
    [Fact]
    public void Small_archives_are_fetched_whole_big_ones_end_first_then_start()
    {
        Assert.Empty(ArchiveWarmer.Ranges(0));
        Assert.Equal([(0L, 5000)], ArchiveWarmer.Ranges(5000));
        long big = 440 * ByteSize.MiB;
        // The end holds a zip's file list, which Windows reads first; the start is read to identify the file.
        Assert.Equal([(big - ArchiveWarmer.TailBytes, ArchiveWarmer.TailBytes), (0L, ArchiveWarmer.HeadBytes)], ArchiveWarmer.Ranges(big));
    }

    [Fact]
    public void Parser_reports_listed_folders_relative_to_the_drive_root()
    {
        var parser = new RcloneLogParser("m");
        var listed = new List<string>();
        parser.DirectoryListed += listed.Add;
        parser.ParseLine("2026/11/10 09:54:32.565110 DEBUG : /: Opendir: ");
        parser.ParseLine("2026/11/10 09:54:32.565110 DEBUG : /: >Opendir: errc=0, fh=0x0");
        parser.ParseLine("2026/11/10 09:54:40.000000 DEBUG : /ZIP File's: Opendir: ");
        parser.ParseLine("2026/11/10 09:54:41.000000 DEBUG : /File/Part 7.zip: Getattr: fh=0xFFFFFFFFFFFFFFFF");
        Assert.Equal(["", "ZIP File's"], listed);
    }

    [Fact]
    public void Quota_exceeded_is_classified_as_rate_limiting()
    {
        const string msg = "pacer: low level retry 1/10 (error googleapi: Error 403: Quota exceeded for quota metric 'Queries' and limit 'Previous quota: Requests per minute' of service 'drive.googleapis.com')";
        Assert.Equal(ProviderEventKind.RateLimited, RcloneLogParser.Classify(msg, ProviderEventKind.Retry));
    }

    [Fact]
    public void Any_file_is_fetched_whole_when_small_and_start_plus_end_when_big()
    {
        Assert.Empty(ArchiveWarmer.RangesFor("notes.txt", 0));
        Assert.Equal([(0L, 50_000)], ArchiveWarmer.RangesFor("photo.jpg", 50_000));
        long big = ArchiveWarmer.SmallFileLimit + 10 * ByteSize.MiB;
        Assert.Equal([(0L, ArchiveWarmer.FileHeadBytes), (big - ArchiveWarmer.FileTailBytes, ArchiveWarmer.FileTailBytes)], ArchiveWarmer.RangesFor("film.mp4", big));
        // An archive keeps its own rule: its file list sits at the end and is bigger.
        Assert.Equal(ArchiveWarmer.Ranges(big), ArchiveWarmer.RangesFor("Game.ZIP", big));
    }

    [Fact]
    public void The_smallest_files_of_a_folder_go_first_and_a_folder_has_a_limit()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cpx_choose_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllBytes(Path.Combine(dir, "b.bin"), new byte[3000]);
            File.WriteAllBytes(Path.Combine(dir, "a.bin"), new byte[1000]);
            File.WriteAllBytes(Path.Combine(dir, "empty.txt"), []);
            File.WriteAllBytes(Path.Combine(dir, "desktop.ini"), new byte[100]);
            for (var i = 0; i < ArchiveWarmer.MaxPerDirectory + 5; i++) File.WriteAllBytes(Path.Combine(dir, $"many{i:000}.dat"), new byte[5000 + i]);

            var chosen = ArchiveWarmer.Choose(new DirectoryInfo(dir).EnumerateFiles());

            Assert.Equal(ArchiveWarmer.MaxPerDirectory, chosen.Count);
            Assert.Equal(["a.bin", "b.bin"], chosen.Take(2).Select(f => f.Name));
            Assert.DoesNotContain(chosen, f => f.Name is "empty.txt" or "desktop.ini");
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void A_File_Explorer_window_address_becomes_a_folder_path_and_special_places_are_ignored()
    {
        Assert.Equal(@"G:\My Photos\Trip", ExplorerFolderWatcher.PathOf("file:///G:/My%20Photos/Trip"));
        Assert.Equal(@"W:\", ExplorerFolderWatcher.PathOf("file:///W:/"));
        Assert.Null(ExplorerFolderWatcher.PathOf(""));                                  // "This PC", Control Panel…
        Assert.Null(ExplorerFolderWatcher.PathOf(null));
        Assert.Null(ExplorerFolderWatcher.PathOf("https://example.com/"));
        Assert.Null(ExplorerFolderWatcher.PathOf("file://server/share/folder"));        // a network share: not one of our drives
    }

    [Fact]
    public async Task Opening_a_folder_fetches_each_file_once_small_ones_whole_big_ones_start_and_end()
    {
        var root = Path.Combine(Path.GetTempPath(), "cpx_warm_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Zips"));
        try
        {
            long bigSize = ArchiveWarmer.WholeFileLimit + 3 * ByteSize.MiB;
            await File.WriteAllBytesAsync(Path.Combine(root, "Zips", "small.zip"), new byte[70_000]);
            await using (var fs = File.Create(Path.Combine(root, "Zips", "big.7z"))) fs.SetLength(bigSize);
            await File.WriteAllBytesAsync(Path.Combine(root, "Zips", "video.mp4"), new byte[50_000]);

            var done = new TaskCompletionSource();
            var warmed = new Dictionary<string, long>();
            using var warmer = new ArchiveWarmer(_ => root, _ => null, () => false, NullLogger.Instance);
            warmer.Warmed += (path, bytes) =>
            {
                lock (warmed) { warmed[path] = warmed.GetValueOrDefault(path) + bytes; if (warmed.Count == 3) done.TrySetResult(); }
            };
            warmer.OnDirectoryListed("m", "Zips");
            warmer.OnDirectoryListed("m", "Zips"); // File Explorer lists a folder many times; only the first counts
            await done.Task.WaitAsync(TimeSpan.FromSeconds(20));
            await Task.Delay(300);

            lock (warmed)
            {
                Assert.Equal(70_000, warmed["Zips/small.zip"]);
                Assert.Equal(ArchiveWarmer.TailBytes + ArchiveWarmer.HeadBytes, warmed["Zips/big.7z"]);
                Assert.Equal(50_000, warmed["Zips/video.mp4"]);   // not an archive, small: fetched whole
                Assert.Equal(3, warmed.Count);
            }
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }
}
