using CloudSSD.App;
using CloudSSD.App.Hybrid;

namespace CloudSSD.Tests;

/// <summary>
/// The speed check ("is the drive still as fast as it was made?"). The parts that measure are tried here on plain
/// folders; the whole check needs the running app and is tried on the real PC.
/// </summary>
public class SpeedGuardTests
{
    private static string NewDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cssd-speed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void The_write_test_makes_its_files_measures_and_leaves_nothing_behind()
    {
        var dir = NewDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "mine.txt"), "the person's own file");
            var seconds = SpeedGuard.WriteProbe(dir);

            Assert.InRange(seconds, 0, 30);
            Assert.False(Directory.Exists(Path.Combine(dir, SpeedGuard.ProbeName)));
            Assert.Equal(["mine.txt"], Directory.EnumerateFileSystemEntries(dir).Select(Path.GetFileName));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void A_leftover_test_folder_from_a_check_that_was_cut_short_is_replaced()
    {
        var dir = NewDir();
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, SpeedGuard.ProbeName, "old"));
            SpeedGuard.WriteProbe(dir);
            Assert.False(Directory.Exists(Path.Combine(dir, SpeedGuard.ProbeName)));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void The_fast_drive_never_treats_the_test_folder_as_the_persons_item()
    {
        Assert.True(HybridLayout.IsSystemName(SpeedGuard.ProbeName));
        var dir = NewDir();
        try
        {
            var fast = Directory.CreateDirectory(Path.Combine(dir, "fast")).FullName;
            Directory.CreateDirectory(Path.Combine(fast, SpeedGuard.ProbeName));
            Directory.CreateDirectory(Path.Combine(fast, "Real"));
            var r = HybridLayout.Reconcile(fast, Path.Combine(dir, "no-cloud"), new HybridState());
            Assert.Equal(["Real"], r.LocalItems);   // so it is never uploaded
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Times_within_the_limit_are_fine_and_above_it_a_problem()
    {
        Assert.Equal("ok", SpeedGuard.Judge(0.03, SpeedGuard.SlowOpenSeconds));
        Assert.Equal("ok", SpeedGuard.Judge(SpeedGuard.SlowWriteSeconds, SpeedGuard.SlowWriteSeconds));
        Assert.Equal("problem", SpeedGuard.Judge(3.2, SpeedGuard.SlowOpenSeconds));     // the first-open times measured before the fix
        Assert.Equal("problem", SpeedGuard.Judge(780, SpeedGuard.SlowWriteSeconds));    // 13 minutes straight to the cloud drive
    }

    [Fact]
    public void Opening_a_file_is_timed_and_a_prepared_file_is_found_in_the_drives_cache()
    {
        var dir = NewDir();
        try
        {
            // rclone's cache: <cache>\vfs\<remote>\<path in the drive>; the drive shows the same path under its root.
            var cache = Path.Combine(dir, "cache");
            var drive = Path.Combine(dir, "drive");
            Directory.CreateDirectory(Path.Combine(cache, "vfs", "CloudSSD", "Docs"));
            Directory.CreateDirectory(Path.Combine(drive, "Docs"));
            var data = new byte[200_000];
            File.WriteAllBytes(Path.Combine(cache, "vfs", "CloudSSD", "Docs", "a.pdf"), data);
            File.WriteAllBytes(Path.Combine(drive, "Docs", "a.pdf"), data);
            File.WriteAllBytes(Path.Combine(cache, "vfs", "CloudSSD", "Docs", "tiny.txt"), new byte[10]);          // too small to tell anything
            File.WriteAllBytes(Path.Combine(cache, "vfs", "CloudSSD", "Docs", "gone.pdf"), data);                  // no longer on the drive

            var file = SpeedGuard.PreparedFile(cache, drive);

            Assert.Equal(Path.Combine(drive, "Docs", "a.pdf"), file);
            Assert.InRange(SpeedGuard.OpenProbe(file!), 0, 5);
            Assert.Null(SpeedGuard.PreparedFile(Path.Combine(dir, "no-cache"), drive));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void A_report_says_in_one_line_what_is_wrong_or_that_all_is_well()
    {
        var good = new SpeedReport(DateTimeOffset.Now, [new("Google key", "ok", "own key"), new("Dead drive name", "warn", "Z: is dead")]);
        Assert.False(good.HasProblem);
        Assert.Equal("Everything that keeps the drive fast is in place.", good.Summary);

        var bad = new SpeedReport(DateTimeOffset.Now, [new("Google key", "problem", "2 accounts are on the shared key."), new("Cloud drive", "ok", "connected")]);
        Assert.True(bad.HasProblem);
        Assert.Equal(["Google key"], bad.Problems);
        Assert.Equal("2 accounts are on the shared key.", bad.Summary);
    }
}
