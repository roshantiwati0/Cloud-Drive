using CloudSSD.App;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CloudSSD.Tests;

/// <summary>
/// Sending a program's folder to the cloud drive and bringing it back. A plain folder stands in for the cloud drive.
/// Making a folder link needs Windows Developer Mode or administrator rights; where the test PC has neither, the tests
/// check the other half of the promise instead: the program stays on the PC exactly as it was.
/// </summary>
public sealed class AppMoverTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cssd-mover-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _pc, _cloud, _state;

    public AppMoverTests()
    {
        _pc = Path.Combine(_root, "pc");
        _cloud = Path.Combine(_root, "cloud");
        _state = Path.Combine(_root, "data", "apps-moved.json");
        Directory.CreateDirectory(_pc);
        Directory.CreateDirectory(_cloud);
    }

    public void Dispose()
    {
        foreach (var d in Directory.EnumerateDirectories(_pc)) if (AppMover.IsLink(d)) Directory.Delete(d);
        foreach (var f in new DirectoryInfo(_root).EnumerateFiles("*", SearchOption.AllDirectories)) if (f.IsReadOnly) f.IsReadOnly = false;
        Directory.Delete(_root, recursive: true);
    }

    private AppMover Mover(bool canLink = true, bool cloud = true) =>
        new(_state, NullLogger.Instance, () => [], canLink ? Junction : (_, _) => false) { PlainCloudRoot = cloud ? _cloud : Path.Combine(_root, "no-drive") };

    /// <summary>
    /// The link of the tests. The app makes a symbolic link (the only kind Windows allows to a cloud drive), which
    /// needs Developer Mode or an administrator; between two folders of this PC a junction does the same and needs neither.
    /// </summary>
    private static bool Junction(string link, string target)
    {
        using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true })!;
        p.WaitForExit(20_000);
        return AppMover.IsLink(link);
    }

    /// <summary>Progress told at once, on the calling thread (Progress&lt;T&gt; would tell it later, after the test looked).</summary>
    private sealed class Told(Action<MoveProgress> tell) : IProgress<MoveProgress>
    {
        public void Report(MoveProgress value) => tell(value);
    }

    private AppItem Program(string name = "Tool")
    {
        var dir = Path.Combine(_pc, name);
        Directory.CreateDirectory(Path.Combine(dir, "data", "deep"));
        File.WriteAllBytes(Path.Combine(dir, name + ".exe"), new byte[1200]);
        File.WriteAllText(Path.Combine(dir, "data", "a.txt"), "alpha");
        File.WriteAllText(Path.Combine(dir, "data", "deep", "b.txt"), "beta beta");
        var readOnly = Path.Combine(dir, "data", "locked.cfg");
        File.WriteAllText(readOnly, "x");
        File.SetAttributes(readOnly, FileAttributes.ReadOnly);
        var exe = Path.Combine(dir, name + ".exe");
        return new AppItem(AppCatalog.IdOf(exe), name, exe, "", dir, dir, 0, AppPlace.Laptop, null);
    }

    private static void AssertWhole(string dir, string name)
    {
        Assert.False(AppMover.IsLink(dir));
        Assert.Equal(1200, new FileInfo(Path.Combine(dir, name + ".exe")).Length);
        Assert.Equal("alpha", File.ReadAllText(Path.Combine(dir, "data", "a.txt")));
        Assert.Equal("beta beta", File.ReadAllText(Path.Combine(dir, "data", "deep", "b.txt")));
        Assert.Equal((4L, 1200L + 5 + 9 + 1), AppMover.Count(dir));
    }

    [Fact]
    public async Task To_the_cloud_the_folder_becomes_a_link_or_everything_stays_as_it_was()
    {
        var app = Program();
        var mover = Mover();
        var steps = new List<string>();
        var result = await mover.ToCloudAsync(app, new Told(p => { lock (steps) steps.Add(p.Step); }), CancellationToken.None);

        var cloudDir = Path.Combine(_cloud, "Apps", "Tool");
        {
            Assert.True(result.Success, result.Message);
            Assert.True(AppMover.IsLink(app.InstallDir));
            Assert.False(Directory.Exists(app.InstallDir + AppMover.OldSuffix));
            AssertWhole(cloudDir, "Tool");
            // Through the link the program is where it always was.
            Assert.Equal(1200, new FileInfo(app.ExePath).Length);
            var moved = Assert.Single(mover.Moved);
            Assert.Equal(cloudDir, moved.CloudDir);
            Assert.Equal(1215, moved.Bytes);
            Assert.NotNull(mover.Find(app.InstallDir + "\\"));
            Assert.NotNull(Mover().Find(app.InstallDir));     // written down: a fresh start knows it too
            Assert.Equal("It is on the cloud drive already.", mover.WhyNotNow(app));
            lock (steps) Assert.Contains("Copying to the cloud drive…", steps);
        }
    }

    [Fact]
    public async Task And_back_again_the_real_folder_returns_with_every_file()
    {
        var app = Program();
        var mover = Mover();
        Assert.True((await mover.ToCloudAsync(app, null, CancellationToken.None)).Success);
        var moved = Assert.Single(mover.Moved);

        var back = await mover.ToPcAsync(moved, keepCloudCopy: true, null, CancellationToken.None);
        Assert.True(back.Success, back.Message);
        AssertWhole(app.InstallDir, "Tool");
        Assert.False(Directory.Exists(app.InstallDir + AppMover.NewSuffix));
        Assert.Empty(mover.Moved);
        AssertWhole(moved.CloudDir, "Tool");                   // kept, as asked

        // A second round, this time without keeping the cloud copy.
        Assert.True((await mover.ToCloudAsync(app, null, CancellationToken.None)).Success);
        var again = await mover.ToPcAsync(Assert.Single(mover.Moved), keepCloudCopy: false, null, CancellationToken.None);
        Assert.True(again.Success, again.Message);
        AssertWhole(app.InstallDir, "Tool");
        Assert.False(Directory.Exists(moved.CloudDir));
    }

    [Fact]
    public async Task Without_the_right_to_make_links_nothing_is_touched()
    {
        var app = Program();
        var mover = Mover(canLink: false);          // Windows asked, the person said No
        var result = await mover.ToCloudAsync(app, null, CancellationToken.None);
        Assert.False(result.Success);
        Assert.Contains("still on this PC", result.Message);
        AssertWhole(app.InstallDir, "Tool");
        Assert.False(Directory.Exists(app.InstallDir + AppMover.OldSuffix));
        Assert.Empty(mover.Moved);
        // The copy made before the question is kept, so the next try has nothing left to send.
        AssertWhole(Path.Combine(_cloud, "Apps", "Tool"), "Tool");
        Assert.True((await Mover().ToCloudAsync(app, null, CancellationToken.None)).Success);
    }

    [Fact]
    public async Task Two_programs_with_the_same_folder_name_do_not_share_a_cloud_folder()
    {
        var first = Program();
        var mover = Mover();
        Assert.True((await mover.ToCloudAsync(first, null, CancellationToken.None)).Success);

        var otherDir = Path.Combine(_pc, "elsewhere", "Tool");
        Directory.CreateDirectory(otherDir);
        File.WriteAllBytes(Path.Combine(otherDir, "Tool.exe"), new byte[77]);
        var second = new AppItem("x", "Tool", Path.Combine(otherDir, "Tool.exe"), "", otherDir, otherDir, 0, AppPlace.Laptop, null);
        var result = await mover.ToCloudAsync(second, null, CancellationToken.None);
        Assert.True(result.Success, result.Message);
        Assert.Equal(Path.Combine(_cloud, "Apps", "Tool (2)"), mover.Find(otherDir)!.CloudDir);
        Assert.Equal(77, new FileInfo(Path.Combine(_cloud, "Apps", "Tool (2)", "Tool.exe")).Length);
        Assert.Equal(1200, new FileInfo(Path.Combine(_cloud, "Apps", "Tool", "Tool.exe")).Length);
        Directory.Delete(otherDir);     // the link, so the clean-up does not walk into the "cloud"
    }

    [Fact]
    public async Task Without_a_cloud_drive_it_says_so()
    {
        var app = Program();
        var result = await Mover(cloud: false).ToCloudAsync(app, null, CancellationToken.None);
        Assert.False(result.Success);
        Assert.Contains("No cloud drive", result.Message);
        AssertWhole(app.InstallDir, "Tool");
    }

    [Fact]
    public async Task A_program_that_may_not_move_gives_its_own_reason()
    {
        var app = Program() with { WhyNot = "It is a part of Windows." };
        var result = await Mover().ToCloudAsync(app, null, CancellationToken.None);
        Assert.Equal("It is a part of Windows.", result.Message);
        AssertWhole(app.InstallDir, "Tool");
    }

    [Fact]
    public async Task Stopping_leaves_the_program_on_the_PC()
    {
        var app = Program();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var result = await Mover().ToCloudAsync(app, null, cts.Token);
        Assert.False(result.Success);
        AssertWhole(app.InstallDir, "Tool");
    }

    [Fact]
    public void A_move_cut_off_half_way_is_undone_at_the_next_start()
    {
        var app = Program();
        // What a power cut leaves: the folder set aside, nothing (or a dead link) in its place, and the record "linking".
        Directory.Move(app.InstallDir, app.InstallDir + AppMover.OldSuffix);
        Directory.CreateDirectory(Path.GetDirectoryName(_state)!);
        File.WriteAllText(_state, System.Text.Json.JsonSerializer.Serialize(new[]
        {
            new MovedApp("Tool", app.InstallDir, Path.Combine(_cloud, "Apps", "Tool"), 1215, DateTime.Now, "linking"),
        }));

        var mover = Mover();
        AssertWhole(app.InstallDir, "Tool");
        Assert.False(Directory.Exists(app.InstallDir + AppMover.OldSuffix));
        Assert.Empty(mover.Moved);
        Assert.Null(mover.Find(app.InstallDir));
    }

    [Fact]
    public void A_broken_record_file_does_not_stop_the_app()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_state)!);
        File.WriteAllText(_state, "{ not json");
        Assert.Empty(Mover().Moved);
    }

    [Fact]
    public void A_running_program_is_noticed()
    {
        var mine = Path.GetDirectoryName(Environment.ProcessPath!)!;
        Assert.NotNull(AppMover.RunningFrom(mine));
        Assert.Null(AppMover.RunningFrom(Path.Combine(_pc, "nobody-runs-here")));
    }

    [Fact]
    public void A_folder_this_user_may_not_rename_is_noticed()
    {
        var app = Program();
        Assert.True(AppMover.CanRename(app.InstallDir));
        // A place where nothing can be created (below a file) stands in for a folder Windows protects.
        Assert.False(AppMover.CanRename(Path.Combine(app.ExePath, "below-a-file", "x")));
    }
}
