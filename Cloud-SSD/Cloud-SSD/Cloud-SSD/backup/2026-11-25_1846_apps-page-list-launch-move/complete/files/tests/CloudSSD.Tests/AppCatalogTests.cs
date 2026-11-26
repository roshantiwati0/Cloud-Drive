using CloudSSD.App;
using CloudSSD.Desktop;
using Xunit;

namespace CloudSSD.Tests;

/// <summary>The Apps page's list: what the Start menu's shortcuts and "Installed apps" turn into.</summary>
public class AppCatalogTests
{
    private const string Windows = @"C:\Windows";
    private static readonly string[] Cloud = [@"Z:\"];

    private static ShortcutInfo Link(string name, string target, string args = "", string workDir = "") =>
        new($@"C:\ProgramData\Microsoft\Windows\Start Menu\Programs\{name}.lnk", target, args, workDir);

    private static InstalledPrograms.Program Installed(string name, string location, long bytes) =>
        new("lm64|" + name, name, "", "1.0", bytes, null, null, location);

    private static IReadOnlyList<AppItem> Build(IEnumerable<ShortcutInfo> links, params InstalledPrograms.Program[] programs) =>
        AppCatalog.Build(links, programs, Cloud, _ => true, Windows);

    [Fact]
    public void A_shortcut_becomes_a_row_with_the_size_and_folder_Windows_knows()
    {
        var apps = Build([Link("Paint Pro", @"C:\Program Files\PaintPro\bin\paint.exe", "--fast", @"C:\Program Files\PaintPro")],
            Installed("Paint Pro 4", @"C:\Program Files\PaintPro\", 5_000_000));

        var a = Assert.Single(apps);
        Assert.Equal("Paint Pro", a.Name);
        Assert.Equal(@"C:\Program Files\PaintPro\bin\paint.exe", a.ExePath);
        Assert.Equal("--fast", a.Arguments);
        Assert.Equal(@"C:\Program Files\PaintPro", a.InstallDir);
        Assert.Equal(@"C:\Program Files\PaintPro", a.WorkDir);
        Assert.Equal(5_000_000, a.Bytes);
        Assert.Equal(AppPlace.Laptop, a.Where);
        Assert.True(a.CanMove);
    }

    [Fact]
    public void Without_an_installed_entry_the_folder_is_where_the_program_file_is()
    {
        var a = Assert.Single(Build([Link("Tool", @"D:\Portable\Tool\tool.exe")]));
        Assert.Equal(@"D:\Portable\Tool", a.InstallDir);
        Assert.Equal(@"D:\Portable\Tool", a.WorkDir);
        Assert.Equal(0, a.Bytes);
    }

    [Fact]
    public void Uninstallers_documents_and_Windows_tools_are_left_out()
    {
        var apps = Build([
            Link("Uninstall Paint Pro", @"C:\Program Files\PaintPro\unins000.exe"),
            Link("Paint Pro Readme", @"C:\Program Files\PaintPro\readme.exe"),
            Link("Paint Pro website", @"C:\Program Files\PaintPro\web.exe"),
            Link("Character Map", @"C:\Windows\System32\charmap.exe"),
            Link("A document", @"C:\Users\x\Documents\notes.txt"),
            Link("A web link", "https://example.com/app.exe"),
            Link("Paint Pro", @"C:\Program Files\PaintPro\paint.exe"),
        ]);
        Assert.Equal("Paint Pro", Assert.Single(apps).Name);
    }

    [Fact]
    public void A_program_whose_file_is_gone_is_not_listed()
    {
        var apps = AppCatalog.Build([Link("Gone", @"C:\Apps\Gone\gone.exe"), Link("Here", @"C:\Apps\Here\here.exe")], [], Cloud,
            path => path.Contains("Here"), Windows);
        Assert.Equal("Here", Assert.Single(apps).Name);
    }

    [Fact]
    public void Two_shortcuts_to_one_program_give_one_row_the_plain_one()
    {
        var apps = Build([
            Link("Browser (Private window)", @"C:\Program Files\Browser\browser.exe", "--private"),
            Link("Browser", @"C:\Program Files\Browser\browser.exe"),
            Link("Browser Beta Channel", @"C:\Program Files\Browser\browser.exe"),
        ]);
        var a = Assert.Single(apps);
        Assert.Equal("Browser", a.Name);
        Assert.Equal("", a.Arguments);
    }

    [Fact]
    public void A_program_on_the_cloud_drive_says_so()
    {
        var a = Assert.Single(Build([Link("Racer", @"Z:\Apps\Racer\racer.exe")]));
        Assert.Equal(AppPlace.Cloud, a.Where);
        Assert.Equal(@"Z:\Apps\Racer", a.InstallDir);
    }

    [Fact]
    public void The_longest_matching_install_folder_wins_and_a_similar_name_is_not_a_match()
    {
        var apps = Build([Link("Editor", @"C:\Suite\Editor\editor.exe"), Link("Apple", @"C:\Apple\apple.exe")],
            Installed("Suite", @"C:\Suite", 9_000), Installed("Suite Editor", @"C:\Suite\Editor", 4_000), Installed("App", @"C:\App", 1));
        Assert.Equal(4_000, apps.Single(a => a.Name == "Editor").Bytes);
        Assert.Equal(@"C:\Suite\Editor", apps.Single(a => a.Name == "Editor").InstallDir);
        Assert.Equal(0, apps.Single(a => a.Name == "Apple").Bytes);
    }

    [Theory]
    [InlineData(@"C:\tool.exe", @"C:\", "directly on a drive")]
    [InlineData(@"C:\Program Files\WindowsApps\Store.App_1\app.exe", @"C:\Program Files\WindowsApps\Store.App_1", "Microsoft Store")]
    [InlineData(@"C:\Windows\notepad.exe", @"C:\Windows", "part of Windows")]
    public void Programs_that_must_stay_where_they_are_say_why(string exe, string dir, string reason)
    {
        var why = AppCatalog.WhyNotMovable(exe, dir, AppPlace.Laptop, Windows);
        Assert.NotNull(why);
        Assert.Contains(reason, why);
    }

    [Fact]
    public void A_program_in_a_folder_shared_with_others_cannot_move()
    {
        var shared = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        Assert.NotNull(AppCatalog.WhyNotMovable(Path.Combine(shared, "loose.exe"), shared, AppPlace.Laptop, Windows));
        Assert.Null(AppCatalog.WhyNotMovable(Path.Combine(shared, "Own", "own.exe"), Path.Combine(shared, "Own"), AppPlace.Laptop, Windows));
    }

    [Fact]
    public void The_main_program_of_a_cloud_folder_is_the_one_named_like_it_else_the_biggest()
    {
        Assert.Equal(@"Z:\Apps\Space Racer\SpaceRacer.exe", AppCatalog.MainExe("Space Racer",
            [(@"Z:\Apps\Space Racer\launcher.exe", 900), (@"Z:\Apps\Space Racer\SpaceRacer.exe", 100), (@"Z:\Apps\Space Racer\unins000.exe", 5000)]));
        Assert.Equal(@"Z:\Apps\Thing\big.exe", AppCatalog.MainExe("Thing",
            [(@"Z:\Apps\Thing\small.exe", 10), (@"Z:\Apps\Thing\big.exe", 500), (@"Z:\Apps\Thing\CrashReporter.exe", 9000)]));
        Assert.Null(AppCatalog.MainExe("Only helpers", [(@"Z:\Apps\Only helpers\uninstall.exe", 10), (@"Z:\Apps\Only helpers\setup.exe", 20)]));
    }

    [Fact]
    public void A_cloud_drive_folder_named_Apps_is_read_one_row_per_folder()
    {
        var root = Path.Combine(Path.GetTempPath(), "cssd-apps-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "Apps", "Racer", "data"));
            Directory.CreateDirectory(Path.Combine(root, "Apps", "Empty"));
            File.WriteAllText(Path.Combine(root, "Apps", "Racer", "Racer.exe"), "x");
            File.WriteAllText(Path.Combine(root, "Apps", "Racer", "data", "deep.exe"), "xxxxxxxxxx");

            var a = Assert.Single(AppCatalog.ReadCloudFolder(root));
            Assert.Equal("Racer", a.Name);
            Assert.Equal(Path.Combine(root, "Apps", "Racer", "Racer.exe"), a.ExePath);
            Assert.Equal(AppPlace.Cloud, a.Where);
            Assert.Empty(AppCatalog.ReadCloudFolder(Path.Combine(root, "nothing here")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void The_same_program_file_always_gets_the_same_id()
    {
        Assert.Equal(AppCatalog.IdOf(@"C:\Apps\A\a.exe"), AppCatalog.IdOf(@"c:\apps\a\A.EXE"));
        Assert.NotEqual(AppCatalog.IdOf(@"C:\Apps\A\a.exe"), AppCatalog.IdOf(@"C:\Apps\B\a.exe"));
    }

    [Fact]
    public void The_real_Start_menu_can_be_read_without_an_error()
    {
        var links = AppCatalog.ReadShortcuts(AppCatalog.StartMenuFolders());
        Assert.All(links, l => Assert.False(string.IsNullOrWhiteSpace(l.Target)));
    }

    [Fact]
    public void A_folder_size_counts_every_file_below_it()
    {
        var root = Path.Combine(Path.GetTempPath(), "cssd-size-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "sub"));
            File.WriteAllBytes(Path.Combine(root, "a.bin"), new byte[300]);
            File.WriteAllBytes(Path.Combine(root, "sub", "b.bin"), new byte[700]);
            Assert.Equal(1000, AppCatalog.FolderBytes(root));
            Assert.Equal(0, AppCatalog.FolderBytes(Path.Combine(root, "missing")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Play_as_game_makes_one_library_entry_that_points_at_the_programs_own_folder()
    {
        var temp = Path.Combine(Path.GetTempPath(), "cssd-gamefor-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            await using var host = AppHost.Create(new CloudSSD.Core.AppPaths(Path.Combine(temp, "data")));
            var dir = Directory.CreateDirectory(Path.Combine(temp, "Racer", "bin")).Parent!.FullName;
            var exe = Path.Combine(dir, "bin", "racer.exe");
            File.WriteAllText(exe, "x");
            var app = new AppItem(AppCatalog.IdOf(exe), "Racer", exe, "-windowed", dir, dir, 0, AppPlace.Laptop, null);

            var g = host.GameFor(app);
            Assert.Equal("Racer", g.Name);
            Assert.Equal(CloudSSD.Core.ExecutionMode.FullLocal, g.Mode);
            Assert.Equal(dir, g.LocalRoot);
            Assert.Equal("bin/racer.exe", g.Executable);
            Assert.Equal("-windowed", g.Arguments);
            Assert.Equal("", g.MountId);
            Assert.False(g.CloudSaveSyncEnabled);
            // The launcher finds the program file from exactly these fields, with no cloud drive.
            Assert.Equal(exe, Path.Combine(CloudSSD.Games.GameLauncher.ResolveRoot(g, null), g.Executable!.Replace('/', '\\')));

            // A second start reuses the entry (and what the person changed in it), it does not add another.
            g.Arguments = "-fullscreen";
            host.Library.Save(g);
            var again = host.GameFor(app);
            Assert.Equal(g.Id, again.Id);
            Assert.Equal("-fullscreen", again.Arguments);
            Assert.Single(host.Library.All());
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { Directory.Delete(temp, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void Every_text_of_the_Apps_page_has_Hinglish_and_Hindi()
    {
        foreach (var key in LocalizationManager.AppsTextsHinglish.Keys)
        {
            Assert.True(LocalizationManager.HasTranslation("Hinglish", key), "Hinglish missing: " + key);
            Assert.True(LocalizationManager.HasTranslation("हिन्दी (Hindi)", key), "Hindi missing: " + key);
        }
        Assert.Equal(LocalizationManager.AppsTextsHinglish.Keys.OrderBy(k => k), LocalizationManager.AppsTextsHindi.Keys.OrderBy(k => k));
    }
}
