using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using CloudSSD.Core;
using CloudSSD.Diagnostics;

namespace CloudSSD.Cli;

/// <summary>
/// The interactive Cloud-SSD shell, laid out like Claude Code / Codex in the terminal:
/// a welcome box with the logo, a framed input line at the bottom, a command list that opens on "/", answers shown as
/// "● Heading" with "└" under it, a spinner while a command works, Yes/No questions before closing or deleting
/// anything, and earlier commands on ↑ ↓. Plain sentences work too ("z drive kholo").
/// </summary>
/// <remarks>
/// Drawing works the way Ink (Claude Code's renderer) does: the area at the bottom is redrawn in place. The shell remembers
/// on which row of that area the cursor stands, moves up that many rows, clears to the end of the screen and draws it
/// again. Only relative cursor moves are used, so it keeps working when the window scrolls.
/// </remarks>
internal sealed partial class Shell
{
    private const string Esc = "\u001b[";
    private const int MenuRows = 8;
    private const int HistoryKeep = 200;

    private static readonly string[] Frames = ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"];

    private readonly AppPaths _paths;
    private readonly string _historyFile;
    private readonly List<string> _history = [];
    private readonly StringBuilder _buf = new();
    private int _pos;
    private int _histIndex = -1;
    private string _draft = "";
    private int _menuSel;
    private int _menuTop;
    private bool _menuClosed;
    private bool _drawn;
    private int _cursorRow;
    private int _cursorCol;
    private int _lastWidth;
    private DateTime _ctrlCAt = DateTime.MinValue;
    private string _note = "";
    private volatile string _vitals = "";
    private volatile IReadOnlyList<DriveChoice> _drives = [];
    private int _vitalsBusy;

    /// <summary>A drive the person can open: from the drives set up in Cloud-SSD, and whether Windows shows it now.</summary>
    internal sealed record DriveChoice(string Letter, string Name, bool On);

    /// <summary>One row of the list under the input box: a command, or a choice after the command (a drive letter).</summary>
    internal sealed record MenuItem(string Label, string Help, string Fill, bool RunsNow);

    public Shell(AppPaths paths)
    {
        _paths = paths;
        _historyFile = Path.Combine(paths.DataRoot, "cli-history.txt");
    }

    // ---- the main loop ------------------------------------------------------------------------------------------

    public async Task<int> RunAsync()
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        Console.TreatControlCAsInput = true;
        LoadHistory();
        try
        {
            await PrintWelcomeAsync();
            while (true)
            {
                var line = ReadLine();
                if (line is null) break;
                if (!await ExecuteAsync(line)) break;
            }
        }
        finally
        {
            Console.TreatControlCAsInput = false;
            Console.Write(Esc + "0m" + Esc + "?25h");
        }
        return 0;
    }

    /// <summary>For a pipe or a file as input ("status" | cpx), or a console without colour codes: plain lines, no frames.</summary>
    public static async Task<int> RunPlainAsync(AppPaths paths)
    {
        var interactive = !Console.IsInputRedirected;
        if (interactive)
        {
            Console.WriteLine($"Cloud-SSD {Program.Version} - type help for the commands, exit to leave.");
        }
        while (true)
        {
            if (interactive) Console.Write("cloudssd > ");
            var line = Console.ReadLine();
            if (line is null) break;
            var words = CommandCatalog.Split(line.Trim());
            if (words.Length == 0) continue;
            var cmd = CommandCatalog.Find(words[0]);
            if (cmd is null && CommandCatalog.Understand(line) is { } u && CommandCatalog.Find(u.Words[0]) is { } meant)
            {
                // Without a real keyboard there is nobody to answer "Yes / No", so sentences never close or delete.
                if (meant.Confirm)
                {
                    Console.WriteLine($"\"{line.Trim()}\" means {u.Shown}. Type it exactly to do it: {meant.Name}");
                    continue;
                }
                Console.WriteLine($"({Ansi.Arrow} {u.Shown})");
                words = u.Words;
                cmd = meant;
            }
            if (cmd?.Name == "exit") break;
            if (cmd?.Name == "clear") { if (interactive) Console.Clear(); continue; }
            if (cmd?.Name == "help") { Views.Help(Console.Out, shell: true); continue; }
            if (cmd is null || cmd.Hidden)
            {
                var guess = CommandCatalog.Closest(words[0]);
                Console.WriteLine($"Unknown command: {words[0]}{(guess != null ? $" (did you mean {guess}?)" : "")}. Type help for the list.");
                continue;
            }
            words[0] = cmd.Name;
            try
            {
                await Program.DispatchAsync(words, paths, Console.Out, Console.Out);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: " + Redactor.Redact(ex.Message));
            }
            Console.WriteLine();
        }
        return 0;
    }

    // ---- welcome box --------------------------------------------------------------------------------------------

    private sealed record Snapshot(bool AppOn, int? Pid, string Drives, string? Phone, string? PhoneSeen, IReadOnlyList<DriveChoice> Choices);

    private Snapshot ReadSnapshot()
    {
        var s = Views.Settings(_paths);
        var app = Views.FindApp();
        var mounted = Views.MountedCloudDrives(s);
        var phone = s.MobileBridge.PairedDevices.Where(p => !p.IsBlocked).OrderByDescending(p => p.LastSeen).FirstOrDefault();
        return new Snapshot(app != null, app?.Id, string.Join("  ", mounted.Select(d => d.Letter)), phone?.Name,
            phone != null ? Views.Ago(phone.LastSeen, DateTimeOffset.Now) : null, Choices(s, mounted));
    }

    /// <summary>The drives set up in Cloud-SSD (on or off), plus any other cloud drive Windows shows, by letter.</summary>
    internal static IReadOnlyList<DriveChoice> Choices(AppSettings s, IReadOnlyList<Views.CloudDrive> mounted)
    {
        var on = mounted.Select(d => d.Letter).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var list = s.Mounts
            .Where(m => Views.Letter(m.DriveLetter).Length == 1)
            .Select(m => new DriveChoice(Views.Letter(m.DriveLetter) + ":", m.DisplayName, on.Contains(Views.Letter(m.DriveLetter) + ":")))
            .ToList();
        list.AddRange(mounted.Where(d => list.All(c => !c.Letter.Equals(d.Letter, StringComparison.OrdinalIgnoreCase)))
            .Select(d => new DriveChoice(d.Letter, d.Name, true)));
        return list.GroupBy(c => c.Letter.ToUpperInvariant()).Select(g => g.First()).OrderBy(c => c.Letter).ToList();
    }

    private async Task PrintWelcomeAsync()
    {
        // Looking at a cloud drive can hang while Google is slow; the welcome never waits more than 2.5 s for it.
        var read = Task.Run(ReadSnapshot);
        var done = await Task.WhenAny(read, Task.Delay(2500));
        Snapshot? snap = done == read && read.IsCompletedSuccessfully ? read.Result : null;
        SetVitals(snap);

        var width = Math.Min(ScreenWidth(), 78);
        var inner = width - 4;
        var lines = new List<string>();
        var logo = Logo.Lines(inner - 2);
        if (logo.Count > 0)
        {
            lines.Add("");
            lines.AddRange(logo.Select(l => "  " + l));
            lines.Add("");
        }
        lines.Add(Ansi.Accent(Ansi.Bold(">_")) + " " + Ansi.Bold("Cloud-SSD") + Ansi.Gray($"  v{Program.Version}"));
        lines.Add(Ansi.Gray(Ansi.Fit("Your Google Drive as a real drive letter on this PC.", inner)));
        lines.Add("");
        if (snap is null)
        {
            lines.Add(Label("app") + Ansi.Gray("still checking" + Ansi.Ellipsis + " (try /status in a moment)"));
        }
        else
        {
            lines.Add(Label("app") + (snap.AppOn
                ? Ansi.Green(Ansi.Dot + " running") + Ansi.Gray($"{Ansi.Sep}PID {snap.Pid}")
                : Ansi.Yellow(Ansi.Ring + " stopped") + Ansi.Gray($"{Ansi.Sep}/start to run it")));
            lines.Add(Label("drives") + (snap.Drives.Length > 0 ? Ansi.Accent(snap.Drives) : Ansi.Gray("none connected")));
            lines.Add(Label("phone") + (snap.Phone != null
                ? Ansi.Fit(snap.Phone, inner - 22) + Ansi.Gray($"{Ansi.Sep}seen {snap.PhoneSeen}")
                : Ansi.Gray("none paired")));
        }
        lines.Add(Label("data") + Ansi.Gray(Ansi.Fit(_paths.DataRoot, inner - 10)));

        var sb = new StringBuilder();
        sb.Append("\r\n");
        foreach (var l in Box(lines, width)) sb.Append(l).Append("\r\n");
        sb.Append("\r\n");
        sb.Append(' ').Append(Ansi.Bold("Tips for getting started:")).Append("\r\n");
        sb.Append(Ansi.Gray(" 1. ")).Append("Type ").Append(Ansi.Accent("/")).Append(" to see every command, ")
          .Append(Ansi.G("↑ ↓", "Up/Down")).Append(" to pick, Enter to run").Append("\r\n");
        sb.Append(Ansi.Gray(" 2. ")).Append(Ansi.Accent("/status")).Append(" shows everything at once").Append(Ansi.Gray(Ansi.Sep))
          .Append(Ansi.Accent("/open")).Append(" opens a drive").Append("\r\n");
        sb.Append(Ansi.Gray(" 3. ")).Append("Or just say it: ").Append(Ansi.Accent("z drive kholo")).Append(", ")
          .Append(Ansi.Accent("phone dikhao")).Append(", ").Append(Ansi.Accent("kitni jagah bachi")).Append("\r\n");
        sb.Append("\r\n");
        Console.Out.Write(sb.ToString());
        Console.Out.Flush();
    }

    private static string Label(string text) => Ansi.Gray(text.PadRight(8));

    /// <summary>A box with round corners around the lines (each line is cut or padded to fit).</summary>
    internal static IEnumerable<string> Box(IReadOnlyList<string> lines, int width, Func<string, string>? border = null)
    {
        border ??= Ansi.Faint;
        var h = Ansi.G("─", "-");
        yield return border(Ansi.G("╭", "+") + string.Concat(Enumerable.Repeat(h, width - 2)) + Ansi.G("╮", "+"));
        foreach (var l in lines)
        {
            // A line that does not fit loses its colours and gets "…"; the frame always stays straight.
            var fitted = Ansi.Width(l) > width - 4 ? Ansi.Fit(Ansi.Strip(l), width - 4) : l;
            yield return border(Ansi.G("│", "|")) + " " + Ansi.Pad(fitted, width - 4) + " " + border(Ansi.G("│", "|"));
        }
        yield return border(Ansi.G("╰", "+") + string.Concat(Enumerable.Repeat(h, width - 2)) + Ansi.G("╯", "+"));
    }

    // ---- reading a line -----------------------------------------------------------------------------------------

    private string? ReadLine()
    {
        _buf.Clear();
        _pos = 0;
        _histIndex = -1;
        _menuSel = 0;
        _menuTop = 0;
        _menuClosed = false;
        _drawn = false;
        Render();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            var action = HandleKey(key);
            if (action == KeyResult.Quit)
            {
                Erase();
                return null;
            }
            if (action == KeyResult.Submit)
            {
                var text = _buf.ToString();
                Erase();
                return text;
            }
            // A paste arrives as many keys at once: draw once when they are all in.
            if (!Console.KeyAvailable) Render();
        }
    }

    private enum KeyResult { Continue, Submit, Quit }

    private KeyResult HandleKey(ConsoleKeyInfo k)
    {
        var ctrl = (k.Modifiers & ConsoleModifiers.Control) != 0;
        var menu = Menu();
        if (!(ctrl && k.Key == ConsoleKey.C)) _note = "";

        switch (k.Key)
        {
            case ConsoleKey.Enter:
                if (menu.Count > 0)
                {
                    var item = menu[_menuSel];
                    if (!item.RunsNow)
                    {
                        SetText(item.Fill);
                        return KeyResult.Continue;
                    }
                    SetText(item.Fill.TrimEnd());
                }
                return _buf.ToString().Trim().Length == 0 ? KeyResult.Continue : KeyResult.Submit;

            case ConsoleKey.Tab:
                if (menu.Count > 0) SetText(menu[_menuSel].Fill);
                return KeyResult.Continue;

            case ConsoleKey.UpArrow:
                if (menu.Count > 0) _menuSel = (_menuSel - 1 + menu.Count) % menu.Count;
                else HistoryMove(+1);
                return KeyResult.Continue;

            case ConsoleKey.DownArrow:
                if (menu.Count > 0) _menuSel = (_menuSel + 1) % menu.Count;
                else HistoryMove(-1);
                return KeyResult.Continue;

            case ConsoleKey.LeftArrow:
                _pos = ctrl ? WordLeft() : Math.Max(0, _pos - 1);
                return KeyResult.Continue;

            case ConsoleKey.RightArrow:
                _pos = ctrl ? WordRight() : Math.Min(_buf.Length, _pos + 1);
                return KeyResult.Continue;

            case ConsoleKey.Home:
                _pos = 0;
                return KeyResult.Continue;

            case ConsoleKey.End:
                _pos = _buf.Length;
                return KeyResult.Continue;

            case ConsoleKey.Backspace:
                if (ctrl) DeleteRange(WordLeft(), _pos);
                else if (_pos > 0) DeleteRange(_pos - 1, _pos);
                return KeyResult.Continue;

            case ConsoleKey.Delete:
                if (_pos < _buf.Length) DeleteRange(_pos, _pos + 1);
                return KeyResult.Continue;

            case ConsoleKey.Escape:
                if (menu.Count > 0) _menuClosed = true;
                else if (_buf.Length > 0) SetText("");
                return KeyResult.Continue;
        }

        if (ctrl)
        {
            switch (k.Key)
            {
                case ConsoleKey.C:
                    if (_buf.Length > 0)
                    {
                        SetText("");
                        _note = "";
                        return KeyResult.Continue;
                    }
                    if ((DateTime.UtcNow - _ctrlCAt).TotalSeconds < 2) return KeyResult.Quit;
                    _ctrlCAt = DateTime.UtcNow;
                    _note = "Press Ctrl+C again to leave";
                    return KeyResult.Continue;
                case ConsoleKey.D:
                    return _buf.Length == 0 ? KeyResult.Quit : KeyResult.Continue;
                case ConsoleKey.L:
                    Console.Out.Write(Esc + "2J" + Esc + "3J" + Esc + "H");
                    _drawn = false;
                    return KeyResult.Continue;
                case ConsoleKey.A:
                    _pos = 0;
                    return KeyResult.Continue;
                case ConsoleKey.E:
                    _pos = _buf.Length;
                    return KeyResult.Continue;
                case ConsoleKey.U:
                    DeleteRange(0, _pos);
                    return KeyResult.Continue;
                case ConsoleKey.K:
                    DeleteRange(_pos, _buf.Length);
                    return KeyResult.Continue;
                case ConsoleKey.W:
                    DeleteRange(WordLeft(), _pos);
                    return KeyResult.Continue;
            }
        }

        if (k.KeyChar != '\0' && !char.IsControl(k.KeyChar) && k.KeyChar != '﻿')
        {
            _buf.Insert(_pos, k.KeyChar);
            _pos++;
            TextChanged();
        }
        return KeyResult.Continue;
    }

    [GeneratedRegex(@"^/?(open|o|connect|disconnect|eject)\s+(\S*)$", RegexOptions.IgnoreCase)]
    private static partial Regex DriveArgument();

    /// <summary>
    /// What the list under the input box shows now: after "/open " the drives, otherwise (line starts with "/") the
    /// commands that match.
    /// </summary>
    internal IReadOnlyList<MenuItem> Menu()
    {
        if (_menuClosed) return [];
        var text = _buf.ToString();

        var drive = DriveArgument().Match(text);
        if (drive.Success)
        {
            var command = CommandCatalog.Find(drive.Groups[1].Value)!.Name;
            var typed = drive.Groups[2].Value.TrimEnd(':').ToUpperInvariant();
            return _drives
                .Where(d => d.Letter.StartsWith(typed, StringComparison.OrdinalIgnoreCase))
                .Select(d => new MenuItem(d.Letter, d.Name + (d.On ? $"{Ansi.Sep}connected" : $"{Ansi.Sep}not connected now"), $"/{command} {d.Letter}", RunsNow: true))
                .ToList();
        }

        return CommandCatalog.Suggest(text)
            .Select(c => new MenuItem(c.Usage, c.Help, "/" + c.Name + (c.Args.Length > 0 ? " " : ""), RunsNow: !c.NeedsArgs && !c.ArgMenu))
            .ToList();
    }

    /// <summary>For tests: as if the person had typed this line.</summary>
    internal void TypeForTest(string text) => SetText(text);

    /// <summary>For tests: the drives the shell knows about.</summary>
    internal void SetDrivesForTest(IReadOnlyList<DriveChoice> drives) => _drives = drives;

    private void SetText(string text)
    {
        _buf.Clear().Append(text);
        _pos = _buf.Length;
        TextChanged();
    }

    private void TextChanged()
    {
        _menuSel = 0;
        _menuTop = 0;
        _menuClosed = false;
    }

    private void DeleteRange(int from, int to)
    {
        if (to <= from) return;
        _buf.Remove(from, to - from);
        _pos = from;
        TextChanged();
    }

    private int WordLeft()
    {
        var i = _pos;
        while (i > 0 && _buf[i - 1] == ' ') i--;
        while (i > 0 && _buf[i - 1] != ' ') i--;
        return i;
    }

    private int WordRight()
    {
        var i = _pos;
        while (i < _buf.Length && _buf[i] == ' ') i++;
        while (i < _buf.Length && _buf[i] != ' ') i++;
        return i;
    }

    /// <summary>↑ (+1) goes to older commands, ↓ (-1) back towards what was being typed.</summary>
    private void HistoryMove(int step)
    {
        if (_history.Count == 0) return;
        if (_histIndex == -1) _draft = _buf.ToString();
        var next = Math.Clamp(_histIndex + step, -1, _history.Count - 1);
        if (next == _histIndex) return;
        _histIndex = next;
        var text = next == -1 ? _draft : _history[_history.Count - 1 - next];
        _buf.Clear().Append(text);
        _pos = _buf.Length;
        _menuClosed = true; // a line from history should not pop the list open
    }

    // ---- drawing the area at the bottom -------------------------------------------------------------------------

    private static int ScreenWidth()
    {
        try
        {
            return Math.Max(30, Console.WindowWidth - 1);
        }
        catch
        {
            return 79;
        }
    }

    private void Render()
    {
        var lines = BuildArea(ScreenWidth(), out var inputRow, out var inputCol);
        Draw(lines, inputRow, inputCol, showCursor: true);
    }

    /// <summary>Draws the rows in place of what was drawn last time and puts the cursor at (row, col) of them.</summary>
    private void Draw(List<string> lines, int row, int col, bool showCursor)
    {
        var width = ScreenWidth();
        var sb = new StringBuilder();
        sb.Append(Esc + "?25l");
        if (_drawn) sb.Append(MoveToTop(width));
        sb.Append('\r').Append(Esc + "J");
        _lastWidth = width;

        sb.Append(string.Join("\r\n", lines));
        var up = lines.Count - 1 - row;
        if (up > 0) sb.Append(Esc).Append(up).Append('A');
        sb.Append('\r');
        if (col > 0) sb.Append(Esc).Append(col).Append('C');
        if (showCursor) sb.Append(Esc + "?25h");
        Console.Out.Write(sb.ToString());
        Console.Out.Flush();
        _cursorRow = row;
        _cursorCol = col;
        _drawn = true;
    }

    /// <summary>
    /// Cursor moves that bring it back to the first row of what was drawn last time. When the window got narrower the
    /// terminal has wrapped every old row into several, so more rows are climbed.
    /// </summary>
    private string MoveToTop(int width)
    {
        var rows = _cursorRow;
        if (width < _lastWidth && width > 0)
        {
            var perRow = (int)Math.Ceiling(_lastWidth / (double)width);
            rows = _cursorRow * perRow + _cursorCol / width;
        }
        return rows > 0 ? $"{Esc}{rows}A" : "";
    }

    private void Erase()
    {
        if (!_drawn) return;
        Console.Out.Write(MoveToTop(ScreenWidth()) + "\r" + Esc + "J");
        Console.Out.Flush();
        _drawn = false;
    }

    /// <summary>The rows of the input area: the framed input line, then the list or the hint line.</summary>
    internal List<string> BuildArea(int width, out int inputRow, out int inputCol)
    {
        var h = Ansi.G("─", "-");
        var v = Ansi.Faint(Ansi.G("│", "|"));
        var lines = new List<string>
        {
            Ansi.Faint(Ansi.G("╭", "+") + string.Concat(Enumerable.Repeat(h, width - 2)) + Ansi.G("╮", "+")),
        };

        var room = width - 6; // "│ > " ... " │"
        var text = _buf.ToString();
        var cursorCol = Ansi.Width(text[.._pos]);
        var scroll = Math.Max(0, cursorCol - (room - 1));
        var shown = text.Length == 0
            ? Ansi.Gray(Ansi.Fit("Type / for commands, or just say it: z drive kholo", room))
            : Ansi.Slice(text, scroll, room);
        lines.Add(v + " " + Ansi.Accent(Ansi.Bold(">")) + " " + Ansi.Pad(shown, room) + " " + v);
        inputRow = 1;
        inputCol = 4 + (cursorCol - scroll);
        lines.Add(Ansi.Faint(Ansi.G("╰", "+") + string.Concat(Enumerable.Repeat(h, width - 2)) + Ansi.G("╯", "+")));

        var menu = Menu();
        if (menu.Count > 0)
        {
            _menuSel = Math.Clamp(_menuSel, 0, menu.Count - 1);
            if (_menuSel < _menuTop) _menuTop = _menuSel;
            if (_menuSel >= _menuTop + MenuRows) _menuTop = _menuSel - MenuRows + 1;
            _menuTop = Math.Clamp(_menuTop, 0, Math.Max(0, menu.Count - MenuRows));
            var nameWidth = Math.Min(26, Math.Max(12, width / 3));
            for (var i = _menuTop; i < Math.Min(menu.Count, _menuTop + MenuRows); i++)
            {
                var item = menu[i];
                var label = Ansi.Fit(item.Label, nameWidth);
                var help = Ansi.Fit(Ansi.Strip(item.Help), width - nameWidth - 5);
                lines.Add(i == _menuSel
                    ? Ansi.Accent(Ansi.G("› ", "> ") + Ansi.Pad(label, nameWidth) + " " + help)
                    : "  " + Ansi.Pad(label, nameWidth) + " " + Ansi.Gray(help));
            }
            var more = menu.Count > MenuRows ? $"{menu.Count} commands{Ansi.Sep}" : "";
            lines.Add(Ansi.Faint("  " + more + $"{Ansi.G("↑↓", "Up/Down")} choose{Ansi.Sep}tab fill in{Ansi.Sep}enter run{Ansi.Sep}esc close"));
        }
        else
        {
            var left = _note.Length > 0
                ? Ansi.Yellow("  " + _note)
                : Ansi.Faint($"  / commands{Ansi.Sep}{Ansi.G("↑↓", "Up/Down")} history{Ansi.Sep}esc clear{Ansi.Sep}ctrl+c leave");
            var right = _vitals;
            var gap = width - Ansi.Width(left) - Ansi.Width(right) - 1;
            lines.Add(gap >= 2 ? left + new string(' ', gap) + right : left);
        }
        return lines;
    }

    // ---- Yes / No and other choices (like Claude Code's permission question) ------------------------------------------

    /// <summary>The rows of a question with numbered answers; <paramref name="selected"/> is marked with "›".</summary>
    internal static List<string> BuildChoice(string question, string? detail, IReadOnlyList<string> options, int selected, int width)
    {
        var boxWidth = Math.Min(width, 78);
        var inner = new List<string> { Ansi.Bold(question) };
        if (!string.IsNullOrEmpty(detail)) inner.AddRange(Ansi.Wrap(detail, boxWidth - 4).Select(Ansi.Gray));
        inner.Add("");
        for (var i = 0; i < options.Count; i++)
        {
            inner.Add(i == selected
                ? Ansi.Accent(Ansi.G("› ", "> ") + $"{i + 1}. {options[i]}")
                : $"  {Ansi.Gray($"{i + 1}.")} {options[i]}");
        }
        var lines = Box(inner, boxWidth, Ansi.Accent).ToList();
        lines.Add(Ansi.Faint($"  {Ansi.G("↑↓", "Up/Down")} or 1-{options.Count} choose{Ansi.Sep}enter ok{Ansi.Sep}esc cancel"));
        return lines;
    }

    /// <summary>Asks and waits for an answer: the index of the chosen option, or null for Esc / Ctrl+C.</summary>
    private int? Choose(string question, string? detail, IReadOnlyList<string> options, int selected = 0)
    {
        _drawn = false;
        try
        {
            while (true)
            {
                var lines = BuildChoice(question, detail, options, selected, ScreenWidth());
                Draw(lines, lines.Count - 1, 0, showCursor: false);
                var k = Console.ReadKey(intercept: true);
                var ctrl = (k.Modifiers & ConsoleModifiers.Control) != 0;
                if (k.Key == ConsoleKey.UpArrow) selected = (selected - 1 + options.Count) % options.Count;
                else if (k.Key == ConsoleKey.DownArrow || k.Key == ConsoleKey.Tab) selected = (selected + 1) % options.Count;
                else if (k.Key == ConsoleKey.Enter) { Erase(); return selected; }
                else if (k.Key == ConsoleKey.Escape || (ctrl && k.Key == ConsoleKey.C)) { Erase(); return null; }
                else if (k.KeyChar >= '1' && k.KeyChar - '1' < options.Count) { Erase(); return k.KeyChar - '1'; }
            }
        }
        finally
        {
            Console.Out.Write(Esc + "?25h");
            Console.Out.Flush();
        }
    }

    /// <summary>The question asked before a command that closes or deletes something; null = nothing to ask now.</summary>
    internal static (string Question, string Detail, string Yes, string No)? ConfirmFor(string command, bool appRunning, string? what = null) => command switch
    {
        "disconnect" => ($"Disconnect {(what != null ? AppLink.DriveId(what) + (AppLink.DriveId(what).Length == 1 ? ":" : "") : "this drive")}?",
            "Programs using the drive lose it. Files still uploading get up to a minute to finish. /connect brings it back.",
            "Yes, disconnect", "No, keep it"),
        "stop" when appRunning => ("Close the Cloud-SSD app?",
            "The drives keep working. The phone cannot reach this PC until /start.", "Yes, close it", "No, keep it running"),
        "clean" => ("Delete old temporary files?",
            "Only files in your Temp folder that nobody changed for 7 days. Never your own files.", "Yes, clean up", "No"),
        _ => null,
    };

    // ---- running a command --------------------------------------------------------------------------------------

    private async Task<bool> ExecuteAsync(string line)
    {
        line = CommandCatalog.Clean(line).Trim();
        if (line.Length == 0) return true;
        AddHistory(line);

        var words = CommandCatalog.Split(line);
        var echo = new StringBuilder(Ansi.Gray("> " + line)).Append("\r\n");

        var cmd = CommandCatalog.Find(words[0]);
        if ((cmd is null || cmd.Hidden) && CommandCatalog.Understand(line) is { } understood)
        {
            // "z drive kholo" → /open Z:, shown under the line so the person learns the short form too.
            words = understood.Words;
            cmd = CommandCatalog.Find(words[0]);
            echo.Append(Ansi.Faint($"  {Ansi.Arrow} {understood.Shown}")).Append("\r\n");
        }
        Console.Out.Write(echo.Append("\r\n").ToString());

        if (cmd is null || cmd.Hidden)
        {
            var guess = CommandCatalog.Closest(words[0]);
            Answer(Ansi.Yellow, "I don't know \"" + words[0] + "\"",
                (guess != null ? $"Did you mean {Ansi.Accent("/" + guess)}? " : "") + "Type / to see every command, or say it in other words.");
            return true;
        }
        words[0] = cmd.Name;

        switch (cmd.Name)
        {
            case "exit":
                Console.Out.Write(Ansi.Gray("  Bye!") + "\r\n");
                return false;
            case "clear":
                Console.Out.Write(Esc + "2J" + Esc + "3J" + Esc + "H");
                await PrintWelcomeAsync();
                return true;
            case "help":
                var help = new StringWriter();
                Views.Help(help, shell: true);
                Answer(Ansi.Accent, "Help", help.ToString());
                return true;
        }

        // "/open" without a letter: with more than one drive, ask which one (with one drive, that one).
        if (cmd.Name == "open" && words.Length == 1)
        {
            var drives = _drives;
            if (drives.Count == 1) words = ["open", drives[0].Letter];
            else if (drives.Count > 1)
            {
                var pick = Choose("Which drive should open?", null,
                    drives.Select(d => $"{d.Letter}  {d.Name}{(d.On ? "" : Ansi.Sep + "not connected now")}").ToList(),
                    Math.Max(0, drives.ToList().FindIndex(d => d.On)));
                if (pick is null)
                {
                    Answer(Ansi.Gray, "Open", "Nothing opened.");
                    return true;
                }
                words = ["open", drives[pick.Value].Letter];
            }
        }

        // "/connect" or "/disconnect" without a letter: the drives it makes sense for (off ones to connect, on ones to
        // disconnect); one such drive is taken as meant, several are asked about.
        if (cmd.Name is "connect" or "disconnect" && words.Length == 1)
        {
            var wantOn = cmd.Name == "disconnect";
            var candidates = _drives.Where(d => d.On == wantOn).ToList();
            if (candidates.Count == 0)
            {
                Answer(Ansi.Gray, cmd.Title, wantOn ? "No cloud drive is connected right now." : "Every drive is already connected. See /drives.");
                return true;
            }
            var chosen = candidates[0];
            if (candidates.Count > 1)
            {
                var pick = Choose($"Which drive should {(wantOn ? "disconnect" : "connect")}?", null, candidates.Select(d => $"{d.Letter}  {d.Name}").ToList());
                if (pick is null)
                {
                    Answer(Ansi.Gray, cmd.Title, "Nothing was changed.");
                    return true;
                }
                chosen = candidates[pick.Value];
            }
            words = [cmd.Name, chosen.Letter];
        }

        if (cmd.Confirm && ConfirmFor(cmd.Name, Views.FindApp() != null, words.Length > 1 ? words[1] : null) is { } ask)
        {
            // "No" is chosen to start with: an Enter pressed by mistake changes nothing.
            var answer = Choose(ask.Question, ask.Detail, [ask.Yes, ask.No], selected: 1);
            if (answer != 0)
            {
                Answer(Ansi.Gray, cmd.Title, "Nothing was changed.");
                return true;
            }
        }

        if (cmd.Live)
        {
            Console.Out.Write(Ansi.Accent(Ansi.Dot) + " " + Ansi.Bold(cmd.Title) + Ansi.Gray("  (Ctrl+C stops it)") + "\r\n");
            Console.TreatControlCAsInput = false;
            try
            {
                await Program.DispatchAsync(words, _paths, Console.Out, Console.Out);
            }
            catch (Exception ex)
            {
                Console.Out.WriteLine(Ansi.Red("Error: " + Redactor.Redact(ex.Message)));
            }
            finally
            {
                Console.TreatControlCAsInput = true;
            }
            Console.Out.Write("\r\n");
            RefreshVitals();
            return true;
        }

        if (cmd.Name == "live")
        {
            await RunLiveAsync();
            return true;
        }
        if (cmd.Name == "key")
        {
            await RunKeyAsync();
            return true;
        }

        // The answer is collected in its own writer while the spinner runs, then shown in one piece. Esc cancels the
        // command (a copy removes its half file, a search shows what it found so far); one that does not stop in time
        // is left behind and its late answer goes into that writer and is dropped.
        var output = new StringWriter();
        var clock = Stopwatch.StartNew();
        using var cts = new CancellationTokenSource();
        var latest = new LatestText();
        var run = Task.Run(() => Program.DispatchAsync(words, _paths, output, output, cts.Token, latest));
        var stopped = await SpinAsync(run, cmd.Doing, latest, cts);
        if (stopped)
        {
            Answer(Ansi.Yellow, cmd.Title, "Stopped waiting. It may still finish in the background.");
            RefreshVitals();
            return true;
        }

        int code;
        var body = "";
        try
        {
            code = await run;
            body = output.ToString();
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            code = 1;
            body = output.ToString() + Ansi.Yellow("Stopped.");
        }
        catch (Exception ex)
        {
            code = 1;
            body = output.ToString() + Ansi.Red("Error: " + Redactor.Redact(ex.Message));
        }
        // "Open Z:" / "Game scan D:\Game", but "Phones" for "phone dikhao": only commands that take something show it.
        var title = cmd.Title + (cmd.Args.Length > 0 && words.Length > 1 ? " " + string.Join(" ", words[1..]) : "");
        if (Ansi.Width(title) > 50) title = cmd.Title;   // "/copy <long path> <long path>": the answer says where
        Answer(code == 0 ? Ansi.Accent : cts.IsCancellationRequested ? Ansi.Yellow : Ansi.Red, title, body, clock.Elapsed);
        RefreshVitals();
        return true;
    }

    /// <summary>The newest "how far" line a command reported (/copy, /find); read by the spinner.</summary>
    internal sealed class LatestText : IProgress<string>
    {
        private volatile string? _text;
        public string? Text => _text;
        public void Report(string value) => _text = value;
    }

    /// <summary>
    /// Spinner with seconds (or the command's own progress line) while it works. Esc or Ctrl+C cancels it through
    /// <paramref name="cancel"/> and waits up to 3 s for it to wrap up (then its answer says what was done); returns true
    /// only when it was left running.
    /// </summary>
    private static async Task<bool> SpinAsync(Task work, string doing, LatestText? latest = null, CancellationTokenSource? cancel = null)
    {
        // Quick commands finish before the first frame, so nothing flickers.
        await Task.WhenAny(work, Task.Delay(150));
        if (work.IsCompleted) return false;

        var clock = Stopwatch.StartNew();
        var frame = 0;
        TimeSpan? stopAsked = null;
        Console.Out.Write(Esc + "?25l");
        try
        {
            while (!work.IsCompleted)
            {
                if (stopAsked is { } at && clock.Elapsed - at > TimeSpan.FromSeconds(3)) return true;
                var secs = (int)clock.Elapsed.TotalSeconds;
                var label = stopAsked != null ? "Stopping" + Ansi.Ellipsis : latest?.Text ?? doing + Ansi.Ellipsis;
                var line = Ansi.Accent(Frames[frame++ % Frames.Length]) + " " + label + Ansi.Gray($"  ({secs}s{Ansi.Sep}esc to stop)");
                var width = ScreenWidth();
                if (Ansi.Width(line) > width) line = Ansi.Fit(Ansi.Strip(line), width);   // one row only, or \r cannot redraw it
                Console.Out.Write("\r" + Esc + "K" + line);
                Console.Out.Flush();
                await Task.WhenAny(work, Task.Delay(80));
                while (Console.KeyAvailable)
                {
                    var k = Console.ReadKey(intercept: true);
                    if (k.Key == ConsoleKey.Escape || (k.Key == ConsoleKey.C && (k.Modifiers & ConsoleModifiers.Control) != 0))
                    {
                        if (cancel is null) return true;
                        cancel.Cancel();
                        stopAsked ??= clock.Elapsed;
                    }
                }
            }
            return false;
        }
        finally
        {
            Console.Out.Write("\r" + Esc + "K" + Esc + "?25h");
            Console.Out.Flush();
        }
    }

    /// <summary>Shows an answer the Claude Code way: "● Heading", then the text indented under "└".</summary>
    internal static void Answer(Func<string, string> colour, string title, string body, TimeSpan? took = null)
    {
        Console.Out.Write(FormatAnswer(colour, title, body, took, ScreenWidth()));
        Console.Out.Flush();
    }

    /// <summary>
    /// The answer as text. With a <paramref name="width"/> long rows are broken at spaces and go on under their own
    /// indent, instead of the terminal wrapping them to the left edge.
    /// </summary>
    internal static string FormatAnswer(Func<string, string> colour, string title, string body, TimeSpan? took = null, int width = 0)
    {
        var sb = new StringBuilder();
        sb.Append(colour(Ansi.Dot)).Append(' ').Append(Ansi.Bold(title));
        // Only a slow answer says how long it took (Google can be slow; quick ones would just be noise).
        if (took is { TotalSeconds: >= 1 } t) sb.Append(Ansi.Gray($"  {t.TotalSeconds:0.0}s"));
        sb.Append("\r\n");
        var rows = body.Replace("\r\n", "\n").Split('\n').ToList();
        while (rows.Count > 0 && rows[0].Trim().Length == 0) rows.RemoveAt(0);
        while (rows.Count > 0 && rows[^1].Trim().Length == 0) rows.RemoveAt(rows.Count - 1);
        if (rows.Count == 0) rows.Add(Ansi.Gray("Done."));
        for (var i = 0; i < rows.Count; i++)
        {
            var lead = rows[i].Length - rows[i].TrimStart(' ').Length;
            var pieces = width > 10 ? Ansi.Wrap(rows[i], Math.Max(10, width - 5)) : [rows[i]];
            for (var j = 0; j < pieces.Count; j++)
            {
                // Claude Code draws "⎿" here; Consolas (the classic cmd.exe font) has no such letter and shows an empty
                // box, so the box-drawing "└" that every console font has is used instead.
                sb.Append(i == 0 && j == 0 ? Ansi.Gray(Ansi.G("  └  ", "  |  ")) : "     ");
                sb.Append(j == 0 ? pieces[j] : new string(' ', lead) + pieces[j]).Append("\r\n");
            }
        }
        sb.Append("\r\n");
        return sb.ToString();
    }

    // ---- /live: a screen that updates every second until Esc ------------------------------------------------------

    private async Task RunLiveAsync()
    {
        var history = new Dictionary<string, List<double>>();
        var before = await Live.SampleAllAsync(_paths, CancellationToken.None);
        var clock = Stopwatch.StartNew();
        List<string> rows = Live.Rows(before, new Dictionary<string, double>(), history, Views.FindApp() != null, Live.PhoneText(_paths));
        _drawn = false;
        try
        {
            while (true)
            {
                var width = ScreenWidth();
                var lines = new List<string> { Ansi.Accent(Ansi.Dot) + " " + Ansi.Bold("Live") + Ansi.Gray($"  updates every second{Ansi.Sep}esc stops") };
                lines.AddRange(rows.Select(r => FitRow("  " + r, width)));
                Draw(lines, lines.Count - 1, 0, showCursor: false);

                // About a second, watching the keyboard.
                var until = DateTime.UtcNow.AddSeconds(1);
                var stop = false;
                while (DateTime.UtcNow < until && !stop)
                {
                    while (Console.KeyAvailable)
                    {
                        var k = Console.ReadKey(intercept: true);
                        if (k.Key is ConsoleKey.Escape or ConsoleKey.Q || (k.Key == ConsoleKey.C && (k.Modifiers & ConsoleModifiers.Control) != 0)) stop = true;
                    }
                    await Task.Delay(50);
                }
                if (stop) break;

                var now = await Live.SampleAllAsync(_paths, CancellationToken.None);
                var speeds = Live.Speeds(before, now, clock.Elapsed);
                clock.Restart();
                before = now;
                foreach (var (letter, speed) in speeds)
                {
                    var h = history.TryGetValue(letter, out var l) ? l : history[letter] = [];
                    h.Add(speed);
                    if (h.Count > 10) h.RemoveAt(0);
                }
                rows = Live.Rows(now, speeds, history, Views.FindApp() != null, Live.PhoneText(_paths));
            }
        }
        finally
        {
            Erase();
            Console.Out.Write(Esc + "?25h");
        }
        // The last picture stays on the screen as the answer.
        Answer(Ansi.Accent, "Live" + Ansi.Gray("  (last look)"), string.Join("\n", rows));
        RefreshVitals();
    }

    /// <summary>A row cut to the window, so the screen never wraps (a wrapped row would break the redraw).</summary>
    private static string FitRow(string row, int width) => Ansi.Width(row) <= width ? row : Ansi.Fit(Ansi.Strip(row), width);

    // ---- /key: own Google key, step by step ---------------------------------------------------------------------

    private async Task RunKeyAsync()
    {
        using var cts = new CancellationTokenSource();
        var read = Task.Run(() => Key.ReadAsync(_paths, cts.Token));
        if (await SpinAsync(read, "Checking which accounts use the shared key", null, cts))
        {
            Answer(Ansi.Yellow, "Google key", "Stopped waiting.");
            return;
        }
        Key.State state;
        try
        {
            state = await read;
        }
        catch (Exception ex)
        {
            Answer(Ansi.Red, "Google key", "Error: " + Redactor.Redact(ex.Message));
            return;
        }
        var text = new StringWriter();
        Key.Write(text, state);
        Answer(state.Shared.Count == 0 && state.RcloneFound ? Ansi.Green : Ansi.Yellow, "Google key", text.ToString());
        if (!state.RcloneFound || state.Shared.Count == 0) return;

        var file = state.File;
        if (file is null)
        {
            var pick = Choose("Apni Google key banayein?",
                "Upar ke 5 kadam Google Cloud Console mein karo. Main Downloads mein key file aane ka intezaar karunga.",
                ["Haan: browser kholo, main intezaar karta hoon", "File pehle se hai (Downloads ya Desktop mein rakh di)", "Baad mein"]);
            if (pick is null or 2)
            {
                Answer(Ansi.Gray, "Google key", "Theek hai. Jab chahein /key likhna.");
                return;
            }
            if (pick == 0) OpenInBrowser(Key.ConsoleUrl);
            file = await WaitForKeyFileAsync(pick == 0 ? TimeSpan.FromMinutes(30) : TimeSpan.FromSeconds(4));
            if (file is null)
            {
                Answer(Ansi.Yellow, "Google key", pick == 0
                    ? "File abhi nahi aayi. Download ho jaye to /key dobara likhna."
                    : "Downloads ya Desktop mein client_secret_….json nahi mili. File wahan rakh ke /key dobara likhna.");
                return;
            }
            Answer(Ansi.Green, "Google key", Ansi.Tick + $" Key file mil gayi: {Path.GetFileName(file.Path)}" +
                                             (file.ProjectId != null ? Ansi.Gray($"  (project {file.ProjectId})") : ""));
        }

        var go = Choose("Aakhri kadam PC app mein: \"Switch to my own Google key\"",
            "App har account ke liye Google sign-in kholega: wahi Gmail chuno jo pehle tha. Files ko kuch nahi hota; drives kuch minute hatengi aur khud wapas aayengi.",
            ["PC app kholo", "Baad mein"]);
        Answer(Ansi.Accent, "Google key", go == 0
            ? Views.ShowApp() + "\nApp mein: Cloud Drives → \"Switch to my own Google key\"."
            : "Jab chahein: PC app → Cloud Drives → \"Switch to my own Google key\".");
    }

    /// <summary>Waits (with a spinner, Esc stops) until a Google key file shows up in Downloads or on the Desktop.</summary>
    private static async Task<CloudSSD.Mount.GoogleClientFile?> WaitForKeyFileAsync(TimeSpan max)
    {
        using var cts = new CancellationTokenSource(max);
        var wait = Task.Run(async () =>
        {
            while (true)
            {
                if (CloudSSD.Mount.GoogleClientFile.FindDownloaded() is { } f) return f;
                await Task.Delay(2000, cts.Token);
            }
        });
        await SpinAsync(wait, "Downloads mein key file (client_secret_….json) ka intezaar", null, cts);
        try
        {
            return await wait;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    private static void OpenInBrowser(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* the URL is also printed in the steps */ }
    }

    // ---- the line under the input box: is the app on, which drives -----------------------------------------------

    private void RefreshVitals()
    {
        if (Interlocked.Exchange(ref _vitalsBusy, 1) == 1) return;
        _ = Task.Run(() =>
        {
            try
            {
                SetVitals(ReadSnapshot());
            }
            catch
            {
                // Keep the old line.
            }
            finally
            {
                Interlocked.Exchange(ref _vitalsBusy, 0);
            }
        });
    }

    private void SetVitals(Snapshot? s)
    {
        if (s is null) return;
        var app = s.AppOn ? Ansi.Green(Ansi.Dot) + Ansi.Gray(" app on") : Ansi.Faint(Ansi.Ring) + Ansi.Gray(" app off");
        _vitals = app + (s.Drives.Length > 0 ? Ansi.Gray(Ansi.Sep) + Ansi.Accent(s.Drives) : "");
        _drives = s.Choices;
    }

    // ---- history (kept between runs, like Claude Code) ------------------------------------------------------------

    private void LoadHistory()
    {
        try
        {
            if (File.Exists(_historyFile))
                _history.AddRange(File.ReadAllLines(_historyFile).Where(l => l.Trim().Length > 0).TakeLast(HistoryKeep));
        }
        catch
        {
            // No history is fine.
        }
    }

    private void AddHistory(string line)
    {
        if (_history.Count > 0 && _history[^1] == line) return;
        _history.Add(line);
        if (_history.Count > HistoryKeep) _history.RemoveRange(0, _history.Count - HistoryKeep);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_historyFile)!);
            File.WriteAllLines(_historyFile, _history);
        }
        catch
        {
            // History is a convenience; never fail a command because of it.
        }
    }
}
