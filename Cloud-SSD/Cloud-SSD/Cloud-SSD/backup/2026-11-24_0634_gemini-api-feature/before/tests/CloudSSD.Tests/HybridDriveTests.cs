using CloudSSD.App.Hybrid;
using CloudSSD.Core;

namespace CloudSSD.Tests;

/// <summary>
/// The "fast drive": the drive letter is a folder on this PC; cloud folders show up in it as links, and what is made
/// on the PC is a real folder there. These tests use two plain folders, one playing the cloud drive.
/// Links need Windows' "Developer Mode" (or admin); without it the link tests do nothing, as the feature itself would.
/// </summary>
public class HybridDriveTests
{
    private sealed class Box : IDisposable
    {
        public string Dir { get; } = Path.Combine(Path.GetTempPath(), "cssd-hybrid-" + Guid.NewGuid().ToString("N"));
        public string Fast => Path.Combine(Dir, "fast");
        public string Cloud => Path.Combine(Dir, "cloud");
        public Box() { Directory.CreateDirectory(Fast); Directory.CreateDirectory(Cloud); }

        public void Dispose()
        {
            // Links first (only the link goes), then the rest.
            foreach (var i in new DirectoryInfo(Fast).EnumerateFileSystemInfos().Where(HybridLayout.IsLink).ToList())
            {
                if (i is DirectoryInfo) Directory.Delete(i.FullName); else File.Delete(i.FullName);
            }
            Directory.Delete(Dir, recursive: true);
        }
    }

    private static bool CanLink()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cssd-linkprobe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "t"));
        try { Directory.CreateSymbolicLink(Path.Combine(dir, "l"), Path.Combine(dir, "t")); Directory.Delete(Path.Combine(dir, "l")); return true; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Every_cloud_folder_and_file_gets_a_link_and_what_is_made_on_the_pc_stays_a_real_folder()
    {
        if (!CanLink()) return;
        using var box = new Box();
        Directory.CreateDirectory(Path.Combine(box.Cloud, "Photos"));
        File.WriteAllText(Path.Combine(box.Cloud, "Photos", "a.txt"), "from the cloud");
        File.WriteAllText(Path.Combine(box.Cloud, "notes.txt"), "a file at the top");
        Directory.CreateDirectory(Path.Combine(box.Fast, "New App"));
        var state = new HybridState();

        var r = HybridLayout.Reconcile(box.Fast, box.Cloud, state);

        Assert.True(r.CloudSeen);
        Assert.Equal(2, r.LinksMade);
        Assert.Equal(["New App"], r.LocalItems);
        Assert.True(HybridLayout.IsLink(new DirectoryInfo(Path.Combine(box.Fast, "Photos"))));
        Assert.Equal("from the cloud", File.ReadAllText(Path.Combine(box.Fast, "Photos", "a.txt")));   // opens through the link
        Assert.Equal("a file at the top", File.ReadAllText(Path.Combine(box.Fast, "notes.txt")));
        Assert.False(HybridLayout.IsLink(new DirectoryInfo(Path.Combine(box.Fast, "New App"))));

        // A second pass changes nothing.
        var again = HybridLayout.Reconcile(box.Fast, box.Cloud, state);
        Assert.False(again.Changed);
    }

    [Fact]
    public void A_link_the_person_deleted_is_not_made_again_and_the_cloud_folder_is_never_deleted()
    {
        if (!CanLink()) return;
        using var box = new Box();
        Directory.CreateDirectory(Path.Combine(box.Cloud, "Photos"));
        File.WriteAllText(Path.Combine(box.Cloud, "Photos", "a.txt"), "keep me");
        var state = new HybridState();
        HybridLayout.Reconcile(box.Fast, box.Cloud, state);

        Directory.Delete(Path.Combine(box.Fast, "Photos"));          // what File Explorer does to a link: only the link goes
        var r = HybridLayout.Reconcile(box.Fast, box.Cloud, state);

        Assert.Equal(["Photos"], r.NewlyHidden);
        Assert.False(Directory.Exists(Path.Combine(box.Fast, "Photos")));
        Assert.Equal("keep me", File.ReadAllText(Path.Combine(box.Cloud, "Photos", "a.txt")));
        Assert.Equal(0, HybridLayout.Reconcile(box.Fast, box.Cloud, state).LinksMade);

        Assert.True(HybridLayout.Unhide(state, "Photos"));           // "show it again"
        Assert.Equal(1, HybridLayout.Reconcile(box.Fast, box.Cloud, state).LinksMade);
    }

    [Fact]
    public void A_folder_deleted_in_the_cloud_loses_its_link_but_a_real_folder_of_the_pc_is_never_removed()
    {
        if (!CanLink()) return;
        using var box = new Box();
        Directory.CreateDirectory(Path.Combine(box.Cloud, "Old"));
        Directory.CreateDirectory(Path.Combine(box.Cloud, "Stays"));
        Directory.CreateDirectory(Path.Combine(box.Fast, "Mine"));
        File.WriteAllText(Path.Combine(box.Fast, "Mine", "work.txt"), "local work");
        var state = new HybridState();
        HybridLayout.Reconcile(box.Fast, box.Cloud, state);

        Directory.Delete(Path.Combine(box.Cloud, "Old"));            // deleted on the phone
        var r = HybridLayout.Reconcile(box.Fast, box.Cloud, state);

        Assert.Equal(1, r.LinksRemoved);
        Assert.False(Directory.Exists(Path.Combine(box.Fast, "Old")));
        Assert.True(Directory.Exists(Path.Combine(box.Fast, "Stays")));
        Assert.Equal("local work", File.ReadAllText(Path.Combine(box.Fast, "Mine", "work.txt")));
    }

    [Fact]
    public void A_cloud_drive_that_is_away_or_suddenly_looks_empty_takes_no_link_away()
    {
        if (!CanLink()) return;
        using var box = new Box();
        Directory.CreateDirectory(Path.Combine(box.Cloud, "Photos"));
        Directory.CreateDirectory(Path.Combine(box.Cloud, "Docs"));
        var state = new HybridState();
        HybridLayout.Reconcile(box.Fast, box.Cloud, state);

        // Away: the drive letter is gone.
        var away = HybridLayout.Reconcile(box.Fast, Path.Combine(box.Dir, "not-there"), state);
        Assert.False(away.CloudSeen);
        Assert.Equal(2, state.Links.Count);

        // Looks empty: a listing that failed was once cached as "empty" on the real drive. Not believed.
        var parked = Path.Combine(box.Dir, "parked");
        Directory.Move(box.Cloud, parked);
        Directory.CreateDirectory(box.Cloud);
        var empty = HybridLayout.Reconcile(box.Fast, box.Cloud, state);
        Assert.Equal(0, empty.LinksRemoved);
        Assert.True(HybridLayout.IsLink(new DirectoryInfo(Path.Combine(box.Fast, "Photos"))));
        Directory.Delete(box.Cloud);
        Directory.Move(parked, box.Cloud);
    }

    [Fact]
    public void When_the_cloud_drive_moves_to_another_letter_the_links_follow_it()
    {
        if (!CanLink()) return;
        using var box = new Box();
        Directory.CreateDirectory(Path.Combine(box.Cloud, "Photos"));
        File.WriteAllText(Path.Combine(box.Cloud, "Photos", "a.txt"), "v1");
        var state = new HybridState();
        HybridLayout.Reconcile(box.Fast, box.Cloud, state);

        var moved = Path.Combine(box.Dir, "cloud-on-another-letter");
        Directory.Move(box.Cloud, moved);
        var r = HybridLayout.Reconcile(box.Fast, moved, state);

        Assert.Equal(1, r.LinksFixed);
        Assert.Equal("v1", File.ReadAllText(Path.Combine(box.Fast, "Photos", "a.txt")));
        Directory.Move(moved, box.Cloud);                            // so the box can clean up
        HybridLayout.Reconcile(box.Fast, box.Cloud, state);
    }

    [Fact]
    public void When_the_pc_and_the_cloud_both_have_a_folder_of_one_name_the_pcs_own_folder_is_shown()
    {
        if (!CanLink()) return;
        using var box = new Box();
        Directory.CreateDirectory(Path.Combine(box.Fast, "App"));
        File.WriteAllText(Path.Combine(box.Fast, "App", "new.txt"), "written here");
        Directory.CreateDirectory(Path.Combine(box.Cloud, "App"));   // its upload has arrived
        var state = new HybridState();

        var r = HybridLayout.Reconcile(box.Fast, box.Cloud, state);

        Assert.Equal(0, r.LinksMade);
        Assert.Equal(["App"], r.LocalItems);
        Assert.False(HybridLayout.IsLink(new DirectoryInfo(Path.Combine(box.Fast, "App"))));
    }

    [Fact]
    public void What_the_fast_drive_remembers_survives_a_restart_and_a_broken_file_starts_empty()
    {
        using var box = new Box();
        var file = Path.Combine(box.Dir, "hybrid-state.json");
        var state = new HybridState();
        state.Links["Photos"] = @"X:\Photos";
        state.Hidden.Add("Old");
        state.Uploaded["App"] = new DateTimeOffset(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);
        state.Save(file);

        var back = HybridState.Load(file);
        Assert.Equal(@"X:\Photos", back.Links["photos"]);            // names compare without case, as Windows does
        Assert.Contains("OLD", back.Hidden);
        Assert.True(back.Uploaded.ContainsKey("app"));

        File.WriteAllText(file, "{ not json");
        Assert.Empty(HybridState.Load(file).Links);
    }

    [Fact]
    public void The_fast_drive_is_off_until_someone_switches_it_on_and_its_folder_is_in_the_users_own_folder()
    {
        var s = new AppSettings().Hybrid;
        Assert.False(s.Enabled);
        Assert.Equal("Z", s.Letter);
        Assert.Equal("X", s.BackingLetter);
        Assert.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), s.RootOrDefault);
        Assert.Equal(@"D:\Fast", new HybridDriveSettings { Root = @"D:\Fast" }.RootOrDefault);
    }

    [Fact]
    public void Windows_own_files_at_the_top_of_a_drive_are_not_treated_as_the_persons_items()
    {
        Assert.True(HybridLayout.IsSystemName("desktop.ini"));
        Assert.True(HybridLayout.IsSystemName("$RECYCLE.BIN"));
        Assert.True(HybridLayout.IsSystemName("System Volume Information"));
        Assert.False(HybridLayout.IsSystemName("MiniMax Code"));
    }
}
