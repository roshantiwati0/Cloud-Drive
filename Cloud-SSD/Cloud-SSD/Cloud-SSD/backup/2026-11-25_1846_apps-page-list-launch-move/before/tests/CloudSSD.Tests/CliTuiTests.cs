using CloudSSD.Cli;
using CloudSSD.Core;

namespace CloudSSD.Tests;

/// <summary>The terminal shell (cloudssd / cpx): command list, "/" menu, drawing widths and plain output for pipes.</summary>
public sealed class CliTuiTests : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), "cloudssd-cli-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly bool _wasEnabled = Ansi.Enabled;
    private readonly bool _wasUnicode = Ansi.Unicode;

    public CliTuiTests() => Directory.CreateDirectory(_home);

    public void Dispose()
    {
        Ansi.Enabled = _wasEnabled;
        Ansi.Unicode = _wasUnicode;
        try { Directory.Delete(_home, true); } catch { /* temp folder */ }
    }

    private static void Fancy(bool on)
    {
        Ansi.Enabled = on;
        Ansi.Unicode = on;
    }

    [Fact]
    public void No_color_keeps_the_frame_but_drops_the_colours()
    {
        Ansi.Enabled = false;
        Ansi.Unicode = true;
        var row = Shell.Box(["hi"], 10).First();
        Assert.Equal("╭────────╮", row);
        // Char search: a culture string search treats the ESC control character as ignorable and "finds" it anywhere.
        Assert.False(Shell.FormatAnswer(Ansi.Accent, "Status", "ok").Contains('\u001b'));
    }

    [Theory]
    [InlineData("/status", "status")]
    [InlineData("status", "status")]
    [InlineData("STATUS", "status")]
    [InlineData("s", "status")]
    [InlineData("mounts", "drives")]
    [InlineData("remotes", "accounts")]
    [InlineData("devices", "phone")]
    [InlineData("\uFEFFhelp", "help")]
    [InlineData("/q", "exit")]
    public void Old_and_new_words_find_the_same_command(string typed, string expected) =>
        Assert.Equal(expected, CommandCatalog.Find(typed)?.Name);

    [Fact]
    public void A_slash_opens_the_whole_list_but_never_the_hidden_secret_command()
    {
        var all = CommandCatalog.Suggest("/");
        Assert.Contains(all, c => c.Name == "status");
        Assert.DoesNotContain(all, c => c.Name == "secret");
        Assert.Equal(CommandCatalog.All.Count(c => !c.Hidden), all.Count);
    }

    [Fact]
    public void Typing_after_the_slash_filters_best_matches_first()
    {
        var st = CommandCatalog.Suggest("/st").Select(c => c.Name).ToList();
        Assert.Equal(["status", "start", "stop"], st.Take(3));

        Assert.Contains(CommandCatalog.Suggest("/mou"), c => c.Name == "drives"); // by its old name "mounts"
        Assert.Contains(CommandCatalog.Suggest("/google"), c => c.Name == "accounts"); // help text
        Assert.Empty(CommandCatalog.Suggest("status"));     // no slash: no list
        Assert.Empty(CommandCatalog.Suggest("/open z"));    // a second word closes the list
    }

    [Theory]
    [InlineData("stauts", "status")]
    [InlineData("/phnoe", "phone")]
    [InlineData("drievs", "drives")]
    public void A_mistyped_word_gets_a_did_you_mean(string typed, string expected) =>
        Assert.Equal(expected, CommandCatalog.Closest(typed));

    [Fact]
    public void Nonsense_gets_no_guess() => Assert.Null(CommandCatalog.Closest("xyzzyq"));

    [Fact]
    public void Quotes_keep_a_folder_with_spaces_together_and_the_bom_is_dropped()
    {
        Assert.Equal(["scan", @"D:\My Games\Game One", "--mbps", "50"],
            CommandCatalog.Split("\uFEFFscan \"D:\\My Games\\Game One\" --mbps 50"));
    }

    [Fact]
    public void Widths_ignore_colour_codes_and_count_wide_letters_twice()
    {
        Fancy(true);
        var coloured = Ansi.Accent("Z:") + " " + Ansi.Bold("drive");
        Assert.Equal(8, Ansi.Width(coloured));
        Assert.Equal("Z: drive", Ansi.Strip(coloured));
        Assert.Equal(4, Ansi.Width("中文"));
        Assert.Equal(10, Ansi.Width(Ansi.Pad(coloured, 10)));
    }

    [Fact]
    public void Long_text_is_cut_with_dots_and_sliced_for_the_input_box()
    {
        Fancy(true);
        Assert.Equal("abcd…", Ansi.Fit("abcdefgh", 5));
        Assert.Equal("abc", Ansi.Fit("abc", 5));
        Assert.Equal("cdef", Ansi.Slice("abcdefgh", 2, 4));

        Fancy(false);
        Assert.Equal("ab...", Ansi.Fit("abcdefgh", 5));
    }

    [Fact]
    public void Without_colour_nothing_but_plain_ascii_is_written()
    {
        Fancy(false);
        var text = Ansi.Accent("a") + Ansi.Dot + Ansi.Tick + Ansi.Sep + Views.Bar(0.5, 4);
        Assert.Equal("a*OK | [##..]", text);
        Assert.All(text, ch => Assert.True(ch < 128));
    }

    [Fact]
    public void Answers_are_shown_under_a_heading_with_the_hook_line()
    {
        Fancy(true);
        var s = Ansi.Strip(Shell.FormatAnswer(Ansi.Accent, "Status", "\nPC app  running\nDrives  Z:\n\n"));
        Assert.Equal("● Status\r\n  └  PC app  running\r\n     Drives  Z:\r\n\r\n", s);

        Assert.Contains("Done.", Ansi.Strip(Shell.FormatAnswer(Ansi.Accent, "Clean", "")));
    }

    [Fact]
    public void Every_row_of_a_box_is_exactly_as_wide_as_asked()
    {
        Fancy(true);
        var rows = Shell.Box([Ansi.Bold("Cloud-SSD"), "", "a very long line that does not fit in the box at all"], 30).ToList();
        Assert.Equal(5, rows.Count);
        Assert.All(rows, r => Assert.Equal(30, Ansi.Width(r)));
        Assert.EndsWith("… │", Ansi.Strip(rows[3]));
    }

    [Fact]
    public void The_input_area_is_a_frame_with_a_hint_line_and_the_menu_opens_on_a_slash()
    {
        Fancy(true);
        var shell = new Shell(new AppPaths(_home));

        var empty = shell.BuildArea(60, out var row, out var col);
        Assert.Equal(4, empty.Count);                       // top, "> ", bottom, hint line
        Assert.Equal(1, row);
        Assert.Equal(4, col);                               // just after "│ > "
        Assert.All(empty.Take(3), l => Assert.Equal(60, Ansi.Width(l)));
        Assert.Contains("z drive kholo", Ansi.Strip(empty[1])); // the placeholder

        shell.TypeForTest("/st");
        var menu = shell.BuildArea(60, out _, out col);
        Assert.Equal(7, col);
        var items = menu.Skip(3).Select(Ansi.Strip).ToList();
        Assert.StartsWith("› /status", items[0]);           // first match chosen
        Assert.Contains(items, l => l.Contains("/start"));
        Assert.Contains("esc close", items[^1]);

        shell.TypeForTest("/");
        var full = shell.BuildArea(60, out _, out _);
        Assert.Equal(3 + 8 + 1, full.Count);                // never more than 8 commands at once
    }

    [Fact]
    public void A_line_longer_than_the_box_scrolls_so_the_cursor_stays_visible()
    {
        Fancy(true);
        var shell = new Shell(new AppPaths(_home));
        shell.TypeForTest("/scan " + new string('x', 100));
        var area = shell.BuildArea(40, out _, out var col);
        Assert.Equal(40, Ansi.Width(area[1]));
        Assert.True(col < 40);
    }

    [Fact]
    public async Task One_shot_commands_write_plain_text_and_say_when_something_is_missing()
    {
        Fancy(false);
        var paths = new AppPaths(_home);

        var o = new StringWriter();
        var err = new StringWriter();
        Assert.Equal(0, await Program.DispatchAsync(["mounts"], paths, o, err));
        Assert.Contains("No drives set up yet", o.ToString());

        o = new StringWriter();
        Assert.Equal(64, await Program.DispatchAsync(["scan"], paths, o, err));
        Assert.Contains("scan needs <folder>", err.ToString());

        err = new StringWriter();
        Assert.Equal(64, await Program.DispatchAsync(["stauts"], paths, o, err));
        Assert.Contains("did you mean \"status\"", err.ToString());
    }

    [Theory]
    [InlineData("status")]
    [InlineData("drives")]
    [InlineData("phone")]
    [InlineData("mirror")]
    [InlineData("help")]
    public async Task Output_into_a_pipe_is_plain_ascii(string command)
    {
        Fancy(false);
        var o = new StringWriter();
        await Program.DispatchAsync([command], new AppPaths(_home), o, o);
        Assert.NotEmpty(o.ToString());
        Assert.All(o.ToString(), ch => Assert.True(ch < 128, $"'{ch}' (U+{(int)ch:X4}) in: {o}"));
    }

    [Fact]
    public async Task Deps_always_exits_0_because_the_release_build_uses_it_as_a_smoke_test()
    {
        Fancy(false);
        var o = new StringWriter();
        Assert.Equal(0, await Program.DispatchAsync(["deps"], new AppPaths(_home), o, o));
        Assert.Contains("WinFsp", o.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_secret_for_rclone_prints_nothing_but_the_key()
    {
        var paths = new AppPaths(_home);
        var o = new StringWriter();
        var err = new StringWriter();
        Assert.Equal(1, await Program.DispatchAsync(["secret", "emit-config-pass"], paths, o, err));
        Assert.Equal("", o.ToString());
        Assert.Contains("No configuration key", err.ToString());
    }

    [Fact]
    public void Home_option_is_taken_out_before_the_command_runs()
    {
        Assert.Equal(["secret", "emit-config-pass"],
            Program.WithoutOption(["secret", "emit-config-pass", "--home", @"C:\data"], "--home"));
        Assert.Equal(["status"], Program.WithoutOption(["--home", @"C:\data", "status"], "--home"));
    }

    [Fact]
    public void Help_lists_every_visible_command()
    {
        Fancy(false);
        var o = new StringWriter();
        Views.Help(o, shell: true);
        foreach (var c in CommandCatalog.All.Where(c => !c.Hidden)) Assert.Contains("/" + c.Name, o.ToString());
        Assert.DoesNotContain("secret", o.ToString());
    }

    [Theory]
    [InlineData("z drive kholo", "/open Z:")]
    [InlineData("Z: kholo", "/open Z:")]
    [InlineData("y drive open karo", "/open Y:")]
    [InlineData("drive kholo", "/open")]
    [InlineData("phone dikhao", "/phone")]
    [InlineData("फोन दिखाओ", "/phone")]
    [InlineData("kitni jagah bachi hai?", "/cache")]
    [InlineData("google account dikhao", "/accounts")]
    [InlineData("app band karo", "/stop")]
    [InlineData("app chalu karo", "/start")]
    [InlineData("safai karo", "/clean")]
    [InlineData("kya haal hai", "/status")]
    [InlineData("drive band karo", "/drives")]   // not "close the app": the sentence is about a drive
    [InlineData("phone kaisa hai", "/phone")]    // the thing named wins over "kaisa"
    public void Hinglish_sentences_become_commands(string said, string expected) =>
        Assert.Equal(expected, CommandCatalog.Understand(said)?.Shown);

    [Theory]
    [InlineData("hello")]
    [InlineData("i am a user")]
    [InlineData("")]
    public void Sentences_about_nothing_known_are_not_guessed(string said) => Assert.Null(CommandCatalog.Understand(said));

    [Fact]
    public void After_open_the_list_shows_the_drives_and_runs_the_chosen_one()
    {
        Fancy(true);
        var shell = new Shell(new AppPaths(_home));
        shell.SetDrivesForTest([new("Y:", "Each Account", true), new("Z:", "All Accounts", false)]);

        shell.TypeForTest("/op");
        var open = Assert.Single(shell.Menu());
        Assert.Equal("/open ", open.Fill);
        Assert.False(open.RunsNow);                         // Enter fills "/open " and shows the drives

        shell.TypeForTest("/open ");
        var drives = shell.Menu();
        Assert.Equal(["/open Y:", "/open Z:"], drives.Select(d => d.Fill));
        Assert.All(drives, d => Assert.True(d.RunsNow));
        Assert.Contains("not connected", Ansi.Strip(drives[1].Help));

        shell.TypeForTest("/open z");
        Assert.Equal("/open Z:", Assert.Single(shell.Menu()).Fill);

        shell.TypeForTest("/st");
        Assert.True(shell.Menu().First().RunsNow);          // /status runs straight away
    }

    [Fact]
    public void Drive_choices_list_set_up_drives_and_say_which_are_connected()
    {
        var s = new AppSettings();
        s.Mounts.Add(new MountDefinition { DisplayName = "All Accounts", DriveLetter = "Z" });
        s.Mounts.Add(new MountDefinition { DisplayName = "Each Account", DriveLetter = "Y:" });
        var choices = Shell.Choices(s, [new Views.CloudDrive("Z:", "All Accounts", 1, 2)]);
        Assert.Equal(["Y:", "Z:"], choices.Select(c => c.Letter));
        Assert.False(choices[0].On);
        Assert.True(choices[1].On);
    }

    [Fact]
    public void A_yes_no_question_is_a_box_with_numbered_answers()
    {
        Fancy(true);
        var rows = Shell.BuildChoice("Close the Cloud-SSD app?", "The drives keep working.", ["Yes, close it", "No"], 1, 100);
        var plain = rows.Select(Ansi.Strip).ToList();
        Assert.Contains(plain, r => r.Contains("Close the Cloud-SSD app?"));
        Assert.Contains(plain, r => r.Contains("  1. Yes, close it"));
        Assert.Contains(plain, r => r.Contains("› 2. No"));
        Assert.All(rows.Take(rows.Count - 1), r => Assert.Equal(78, Ansi.Width(r)));
        Assert.Contains("esc cancel", plain[^1]);
    }

    [Fact]
    public void Only_closing_a_running_app_and_cleaning_are_asked_first()
    {
        Assert.NotNull(Shell.ConfirmFor("stop", appRunning: true));
        Assert.Null(Shell.ConfirmFor("stop", appRunning: false));   // nothing to close
        Assert.NotNull(Shell.ConfirmFor("clean", appRunning: false));
        Assert.Null(Shell.ConfirmFor("status", appRunning: true));
        Assert.Equal(["clean", "disconnect", "stop"], CommandCatalog.All.Where(c => c.Confirm).Select(c => c.Name).Order());
        var ask = Shell.ConfirmFor("disconnect", appRunning: true, "z");
        Assert.Equal("Disconnect Z:?", ask!.Value.Question);
    }

    [Fact]
    public void The_logo_is_five_even_rows_and_only_where_it_fits()
    {
        Fancy(true);
        var rows = Logo.Lines(80);
        Assert.Equal(5, rows.Count);
        Assert.All(rows, r => Assert.Equal(Logo.Width, Ansi.Width(r)));
        Assert.Empty(Logo.Lines(Logo.Width - 1));                // too narrow
        Fancy(false);
        Assert.Empty(Logo.Lines(80));                            // no block letters in a plain console
    }

    [Fact]
    public void Slow_answers_say_how_long_they_took()
    {
        Fancy(true);
        Assert.Contains("2.5s", Ansi.Strip(Shell.FormatAnswer(Ansi.Accent, "Google accounts", "x", TimeSpan.FromSeconds(2.5))));
        Assert.DoesNotContain("0.3s", Ansi.Strip(Shell.FormatAnswer(Ansi.Accent, "Status", "x", TimeSpan.FromSeconds(0.3))));
    }

    // ---- /problem /live /key /find /copy (TASK-CLAUDE-CLI-TUI-3) -------------------------------------------------

    [Theory]
    [InlineData("kya dikkat hai", "/problem")]
    [InlineData("drive slow hai", "/problem")]
    [InlineData("speed dikhao", "/live")]
    [InlineData("google key lagao", "/key")]
    [InlineData("photo.jpg dhundho", "/find photo.jpg")]
    [InlineData("meri report file dhundho", "/find report")]
    [InlineData("z drive mein report dhundho", "/find report in Z:")]
    [InlineData("dhundho", "/find")]
    public void New_sentences_reach_the_new_commands(string said, string expected) =>
        Assert.Equal(expected, CommandCatalog.Understand(said)?.Shown);

    [Theory]
    [InlineData("z drive jodo", "/connect Z:")]
    [InlineData("z drive chalu karo", "/connect Z:")]
    [InlineData("y drive hatao", "/disconnect Y:")]
    [InlineData("z drive band karo", "/disconnect Z:")]
    [InlineData("drive band karo", "/drives")]              // no letter: only shows them, closes nothing
    [InlineData("phone connect karo", "/phone")]            // not about a drive
    [InlineData("app band karo", "/stop")]                  // still the app
    [InlineData("photo.jpg phone ko bhejo", "/send photo.jpg")]
    [InlineData("send report.pdf", "/send report.pdf")]
    public void Sentences_about_one_drive_or_sending_reach_the_right_command(string said, string expected) =>
        Assert.Equal(expected, CommandCatalog.Understand(said)?.Shown);

    [Fact]
    public void After_connect_and_disconnect_the_list_shows_the_drives_too()
    {
        Fancy(true);
        var shell = new Shell(new AppPaths(_home));
        shell.SetDrivesForTest([new("Y:", "Each Account", true), new("Z:", "All Accounts", false)]);
        shell.TypeForTest("/connect ");
        Assert.Equal(["/connect Y:", "/connect Z:"], shell.Menu().Select(d => d.Fill));
        shell.TypeForTest("/disconnect y");
        Assert.Equal("/disconnect Y:", Assert.Single(shell.Menu()).Fill);
        shell.TypeForTest("/eject ");
        Assert.All(shell.Menu(), d => Assert.StartsWith("/disconnect ", d.Fill));
    }

    [Fact]
    public async Task Connect_disconnect_and_send_say_what_they_need()
    {
        var paths = new AppPaths(_home);
        foreach (var (cmd, needs) in new[] { ("connect", "connect needs <letter>"), ("disconnect", "disconnect needs <letter>"), ("send", "send needs <file>") })
        {
            var err = new StringWriter();
            Assert.Equal(64, await Program.DispatchAsync([cmd], paths, new StringWriter(), err));
            Assert.Contains(needs, err.ToString());
        }
        var o = new StringWriter();
        Assert.Equal(1, await Program.DispatchAsync(["send", Path.Combine(_home, "nope.jpg")], paths, o, o));
        Assert.Contains("Nothing at", o.ToString());
        Assert.Equal(("a.jpg", "vivo"), Files.SplitAt(["send", "a.jpg", "to", "vivo"], "to"));
    }

    [Fact]
    public void Long_rows_break_at_spaces_and_keep_their_indent()
    {
        Fancy(true);
        // 10 wide with a 2-space indent: every piece gets 8 (the caller puts the indent before the later ones too).
        Assert.Equal(["  one two", "three", "four"], Ansi.Wrap("  one two three four", 10));
        Assert.Equal(["short"], Ansi.Wrap("short", 10));
        Assert.Equal(["a  b"], Ansi.Wrap("a  b", 10));                   // spaces inside a row (table columns) stay

        var text = Ansi.Strip(Shell.FormatAnswer(Ansi.Accent, "Problems", "  Aapke accounts rclone ki shared key se jude hain", null, 30));
        var rows = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.True(rows.Length > 2);
        Assert.All(rows.Skip(1), r => Assert.True(Ansi.Width(r) <= 30, r));
        Assert.All(rows.Skip(2), r => Assert.StartsWith("       ", r));   // under its own indent, not at the left edge

        var box = Shell.BuildChoice("Q?", "a very long explanation that would never fit on one line of the box at all", ["Yes"], 0, 40);
        Assert.All(box.Take(box.Count - 1), r => Assert.Equal(40, Ansi.Width(r)));
        Assert.DoesNotContain(box, r => Ansi.Strip(r).Contains('…'));     // wrapped, not cut
    }

    [Fact]
    public void Places_are_read_the_way_people_type_them()
    {
        Assert.Equal(@"Z:\", Files.Place("z"));
        Assert.Equal(@"Y:\", Files.Place("Y:"));
        Assert.EndsWith(@"\Downloads", Files.Place("downloads"));
        Assert.Equal(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + @"\x", Files.Place("%USERPROFILE%\\x"));
        Assert.Equal(@"D:\My Games", Files.Place("\"D:\\My Games\""));
    }

    [Theory]
    [InlineData("*.zip", "Game.ZIP", true)]
    [InlineData("photo", "My Photo 1.jpg", true)]
    [InlineData("photo?.jpg", "photo7.jpg", true)]
    [InlineData("report", "notes.txt", false)]
    public void Find_matches_parts_of_names_and_wildcards(string query, string name, bool expected) =>
        Assert.Equal(expected, Files.Matches(query, name));

    [Fact]
    public void Find_reads_where_to_look_after_in()
    {
        Assert.Equal(("report", "Z:"), Files.ParseFind(["find", "report", "in", "Z:"]));
        Assert.Equal(("my file", (string?)null), Files.ParseFind(["find", "my", "file"]));
        Assert.Equal(("in", (string?)null), Files.ParseFind(["find", "in"]));
    }

    [Fact]
    public void Find_lists_matches_in_a_folder()
    {
        Fancy(false);
        var root = Directory.CreateDirectory(Path.Combine(_home, "find")).FullName;
        Directory.CreateDirectory(Path.Combine(root, "a", "b"));
        File.WriteAllText(Path.Combine(root, "a", "b", "Report 2026.pdf"), "x");
        File.WriteAllText(Path.Combine(root, "notes.txt"), "x");
        var o = new StringWriter();
        Assert.Equal(0, Files.Find(o, new AppPaths(_home), "report", root, CancellationToken.None, null));
        Assert.Contains("Report 2026.pdf", o.ToString());
        Assert.DoesNotContain("notes.txt", o.ToString());
        Assert.Contains("1 found", o.ToString());
    }

    [Fact]
    public async Task Copy_copies_a_folder_with_its_name_and_never_overwrites()
    {
        Fancy(false);
        var src = Directory.CreateDirectory(Path.Combine(_home, "src", "Game")).FullName;
        File.WriteAllText(Path.Combine(src, "a.txt"), "new a");
        Directory.CreateDirectory(Path.Combine(src, "sub"));
        File.WriteAllText(Path.Combine(src, "sub", "b.txt"), "new b");
        var dst = Directory.CreateDirectory(Path.Combine(_home, "dst")).FullName;
        Directory.CreateDirectory(Path.Combine(dst, "Game"));
        File.WriteAllText(Path.Combine(dst, "Game", "a.txt"), "MINE");     // already there: must stay

        var o = new StringWriter();
        Assert.Equal(0, await Files.CopyAsync(o, src, dst, CancellationToken.None, null));
        Assert.Equal("MINE", File.ReadAllText(Path.Combine(dst, "Game", "a.txt")));
        Assert.Equal("new b", File.ReadAllText(Path.Combine(dst, "Game", "sub", "b.txt")));
        Assert.Contains("Copied 1 file", o.ToString());
        Assert.Contains("1 already there", o.ToString());
        Assert.Empty(Directory.GetFiles(dst, "*.cloudssd-part", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task A_stopped_copy_leaves_no_half_file()
    {
        Fancy(false);
        var src = Path.Combine(_home, "big.bin");
        File.WriteAllBytes(src, new byte[3 << 20]);
        var dst = Directory.CreateDirectory(Path.Combine(_home, "out")).FullName;
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var o = new StringWriter();
        Assert.Equal(1, await Files.CopyAsync(o, src, dst, cts.Token, null));
        Assert.Contains("Stopped", o.ToString());
        Assert.Empty(Directory.GetFiles(dst));
    }

    [Fact]
    public void Copy_refuses_what_makes_no_sense()
    {
        var src = Directory.CreateDirectory(Path.Combine(_home, "self")).FullName;
        Assert.Contains("into itself", Files.PlanCopy(src, Path.Combine(src, "inside")).Error);
        Assert.StartsWith("Nothing at", Files.PlanCopy(Path.Combine(_home, "missing.txt"), _home).Error);
        File.WriteAllText(Path.Combine(_home, "f.txt"), "x");
        var plan = Files.PlanCopy(Path.Combine(_home, "f.txt"), src);
        Assert.Equal(Path.Combine(src, "f.txt"), Assert.Single(plan.Items!).To);   // into a folder: keeps its name
    }

    [Fact]
    public void Copy_progress_says_how_far_how_fast_and_how_long()
    {
        Fancy(false);
        var line = Files.ProgressLine(50 << 20, 100 << 20, TimeSpan.FromSeconds(10), "game.zip");
        Assert.Contains("50%", line);
        Assert.Contains("/s", line);
        Assert.Contains("10 s left", line);
        Assert.Contains("game.zip", line);
    }

    [Fact]
    public void Live_speed_comes_from_the_byte_counter_and_a_restart_is_not_a_negative_speed()
    {
        Live.DriveSample S(string l, long bytes, bool answered = true) => new(l, "x", answered, bytes, 0, 0, 0, 0, 0, false);
        var speed = Live.Speeds([S("Z:", 1000), S("Y:", 5000), S("X:", 0, false)], [S("Z:", 3000), S("Y:", 100), S("X:", 9)], TimeSpan.FromSeconds(2));
        Assert.Equal(1000, speed["Z:"]);
        Assert.Equal(0, speed["Y:"]);     // counter started again
        Assert.Equal(0, speed["X:"]);     // did not answer before
    }

    [Fact]
    public void Live_shows_a_sparkline_and_warns_about_the_rate_limit()
    {
        Fancy(true);
        Assert.Equal(" ▒█", Live.Sparkline([0, 5, 10]));
        var rows = Live.Rows([new("Z:", "All Accounts", true, 0, 2, 1, 3, 1 << 30, 0, true)],
            new Dictionary<string, double> { ["Z:"] = 2 << 20 }, new Dictionary<string, List<double>>(), true, null).Select(Ansi.Strip).ToList();
        Assert.Contains(rows, r => r.Contains("Z:") && r.Contains("2.0 MiB/s") && r.Contains("1 going, 3 waiting"));
        Assert.Contains(rows, r => r.Contains("rate limit") && r.Contains("/key"));
        Assert.True(Live.IsRateLimit("googleapi: Error 403: Quota exceeded, rateLimitExceeded"));
        Assert.False(Live.IsRateLimit("file not found"));
    }

    [Fact]
    public void Live_skips_mounts_whose_rclone_is_gone()
    {
        File.WriteAllText(Path.Combine(_home, "mounts.state.json"),
            """[{"Id":"cloudssd","Pid":999999,"ProcessStartTicks":0,"Port":1,"User":"u","PassProtected":"x","Letter":"Z","LogFile":"l","RequestJson":"{}"}]""");
        Assert.Empty(Live.ReadMounts(new AppPaths(_home)));
    }

    [Fact]
    public void Problem_reads_the_log_counts_kinds_and_forgets_old_lines()
    {
        var now = new DateTime(2026, 11, 17, 12, 0, 0);
        string L(int minutesAgo, string level, string msg) => $"{now.AddMinutes(-minutesAgo):yyyy-MM-dd HH:mm:ss}.123 {level} {msg}";
        var lines = new List<string>();
        for (var i = 0; i < 6; i++) lines.Add(L(10 + i, "WRN", "RcloneMountService: rclone [cloudssd] Error: HTTP token bucket error: context deadline exceeded"));
        lines.Add(L(5, "ERR", "LocalMobileBridgeService: Failed to start Cloud-SSD Mobile Bridge on port 8090"));
        lines.Add(L(3, "ERR", "Something nobody has seen before"));
        lines.Add(L(3, "WRN", "A warning nobody has seen before"));       // unknown warnings are not counted
        lines.Add(L(2, "INF", "Mounted at Z:"));                           // info is not a problem
        lines.Add(L(60 * 30, "ERR", "LocalMobileBridgeService: Failed to start Cloud-SSD Mobile Bridge on port 8090")); // 30 h old
        lines.Add("garbage line");

        var found = Problems.FromLog(lines, now, TimeSpan.FromHours(24));
        var rate = Assert.Single(found, f => f.Title.Contains("rate limit"));
        Assert.Equal(6, rate.Count);
        Assert.Equal(Problems.Level.Bad, rate.Level);                      // 5 or more: not a passing thing
        Assert.Equal(now.AddMinutes(-10), rate.Last);
        Assert.Equal(1, Assert.Single(found, f => f.Title.Contains("port 8090")).Count);
        Assert.Equal(1, Assert.Single(found, f => f.Title.Contains("aur galti")).Count);
        Assert.Equal(3, found.Count);
    }

    [Fact]
    public void Problem_says_so_when_all_is_well_and_never_prints_a_log_line()
    {
        Fancy(false);
        var o = new StringWriter();
        Problems.Write(o, [], DateTime.Now);
        Assert.Contains("Koi dikkat nahi", o.ToString());

        var now = DateTime.Now;
        var secretLine = $"{now:yyyy-MM-dd HH:mm:ss}.000 ERR AppHost: Could not restore mount All Accounts: Drive Z: did not appear within 120 s. DEBUG : Private Folder/secret.docx";
        o = new StringWriter();
        Problems.Write(o, Problems.FromLog([secretLine], now, TimeSpan.FromHours(1)), now);
        Assert.DoesNotContain("secret.docx", o.ToString());
        Assert.Contains("Kya karein", o.ToString());
        Assert.All(o.ToString(), ch => Assert.True(ch < 128, $"'{ch}' in: {o}"));
    }

    [Fact]
    public void Key_page_says_what_to_do_in_each_situation()
    {
        Fancy(false);
        var o = new StringWriter();
        Key.Write(o, new Key.State([], new Dictionary<string, string>(), null, true));
        Assert.Contains("apni key par hain", o.ToString());

        o = new StringWriter();
        Key.Write(o, new Key.State(["gdrive"], new Dictionary<string, string> { ["gdrive"] = "me@example.com" }, null, true));
        var text = o.ToString();
        Assert.Contains("1 account abhi shared key par", text);
        Assert.Contains("me@example.com", text);
        Assert.Contains("Download JSON", text);                             // the steps, since there is no key file yet
        Assert.Contains("Switch to my own Google key", text);
        Assert.All(text, ch => Assert.True(ch < 128, $"'{ch}' in: {text}"));

        o = new StringWriter();
        Key.Write(o, new Key.State(["gdrive"], new Dictionary<string, string>(), null, false));
        Assert.Contains("rclone nahi mila", o.ToString());
    }

    [Fact]
    public async Task New_commands_say_what_they_need()
    {
        var paths = new AppPaths(_home);
        var err = new StringWriter();
        Assert.Equal(64, await Program.DispatchAsync(["copy", "only-one"], paths, new StringWriter(), err));
        Assert.Contains("copy needs <from> <to>", err.ToString());
        err = new StringWriter();
        Assert.Equal(64, await Program.DispatchAsync(["find"], paths, new StringWriter(), err));
        Assert.Contains("find needs <name>", err.ToString());
        var o = new StringWriter();
        Assert.Equal(1, await Program.DispatchAsync(["ls", Path.Combine(_home, "nope")], paths, o, o));
        Assert.Contains("No folder at", o.ToString());
    }

    [Theory]
    [InlineData(30 * 86400.0, "30 days")]
    [InlineData(86400.0, "1 day")]
    [InlineData(7200.0, "2 h")]
    [InlineData(90.0, "1.5 min")]
    [InlineData(5.0, "5 s")]
    [InlineData(0.0, "0 s")]
    public void Time_spans_read_like_words(double seconds, string expected) =>
        Assert.Equal(expected, Views.Span(TimeSpan.FromSeconds(seconds)));

    [Theory]
    [InlineData(0, "just now")]
    [InlineData(5 * 60, "5 min ago")]
    [InlineData(3 * 3600, "3 h ago")]
    [InlineData(30 * 3600, "yesterday")]
    [InlineData(3 * 86400, "3 days ago")]
    public void Last_seen_reads_like_a_person_would_say_it(int secondsAgo, string expected)
    {
        var now = new DateTimeOffset(2026, 11, 17, 12, 0, 0, TimeSpan.FromHours(5.5));
        Assert.Equal(expected, Views.Ago(now.AddSeconds(-secondsAgo), now));
    }
}
