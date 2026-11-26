using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace CloudSSD.Cli;

/// <summary>
/// Colours, symbols and text widths for the terminal. Every coloured piece of text goes through here, so output that
/// goes into a pipe or a file (or a console without colour support) stays plain ASCII text.
/// </summary>
public static partial class Ansi
{
    /// <summary>True when colours may be written (a console that understands colour codes, and no NO_COLOR).</summary>
    public static bool Enabled { get; set; }

    /// <summary>
    /// True when the symbols ● ✓ ╭─╮ ⎿ may be written (a real console in UTF-8). Independent of colour: NO_COLOR asks for
    /// no colours, not for an uglier frame.
    /// </summary>
    public static bool Unicode { get; set; }

    // Colours of the PC app's dark theme (Themes\Dark.xaml: Accent #5EEAD4).
    public static string Accent(string s) => Rgb(0x5E, 0xEA, 0xD4, s);
    public static string Gray(string s) => Rgb(0x9C, 0xA3, 0xAF, s);
    public static string Faint(string s) => Rgb(0x5B, 0x64, 0x6E, s);
    public static string Green(string s) => Rgb(0x4A, 0xDE, 0x80, s);
    public static string Yellow(string s) => Rgb(0xFA, 0xCC, 0x15, s);
    public static string Red(string s) => Rgb(0xF8, 0x71, 0x71, s);
    public static string Bold(string s) => Enabled && s.Length > 0 ? $"\u001b[1m{s}\u001b[22m" : s;

    public static string Rgb(int r, int g, int b, string s) =>
        Enabled && s.Length > 0 ? $"\u001b[38;2;{r};{g};{b}m{s}\u001b[39m" : s;

    /// <summary>A symbol when the terminal can show it, a plain stand-in otherwise.</summary>
    public static string G(string fancy, string plain) => Unicode ? fancy : plain;

    public static string Dot => G("●", "*");
    public static string Ring => G("○", "o");
    public static string Tick => G("✓", "OK");
    public static string Cross => G("✗", "X");
    public static string Arrow => G("→", "->");
    public static string Sep => G(" · ", " | ");
    public static string Ellipsis => G("…", "...");

    [GeneratedRegex("\u001b\\[[0-9;?]*[ -/]*[@-~]")]
    private static partial Regex EscapeCodes();

    /// <summary>The text without colour / cursor codes.</summary>
    public static string Strip(string s) => EscapeCodes().Replace(s, "");

    /// <summary>How many terminal columns the text takes (colour codes take none, Chinese/emoji take two).</summary>
    public static int Width(string s)
    {
        var w = 0;
        foreach (var r in Strip(s).EnumerateRunes()) w += RuneWidth(r);
        return w;
    }

    internal static int RuneWidth(Rune r)
    {
        if (r.Value < 32 || r.Value == 0x7F) return 0;
        var cat = Rune.GetUnicodeCategory(r);
        if (cat is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark or UnicodeCategory.Format) return 0;
        var c = r.Value;
        var wide = c is (>= 0x1100 and <= 0x115F) or (>= 0x2E80 and <= 0xA4CF) or (>= 0xAC00 and <= 0xD7A3)
            or (>= 0xF900 and <= 0xFAFF) or (>= 0xFE30 and <= 0xFE4F) or (>= 0xFF00 and <= 0xFF60)
            or (>= 0xFFE0 and <= 0xFFE6) or (>= 0x1F300 and <= 0x1FAFF) or (>= 0x20000 and <= 0x3FFFD);
        return wide ? 2 : 1;
    }

    /// <summary>Adds spaces on the right until the text is <paramref name="width"/> columns wide.</summary>
    public static string Pad(string s, int width) => s + new string(' ', Math.Max(0, width - Width(s)));

    /// <summary>Cuts plain (uncoloured) text to <paramref name="width"/> columns, ending with "…" when cut.</summary>
    public static string Fit(string plain, int width)
    {
        if (width <= 0) return "";
        if (Width(plain) <= width) return plain;
        var dots = Ellipsis;
        var room = width - Width(dots);
        if (room <= 0) return dots[..Math.Min(dots.Length, width)];
        var sb = new StringBuilder();
        var w = 0;
        foreach (var r in plain.EnumerateRunes())
        {
            var rw = RuneWidth(r);
            if (w + rw > room) break;
            sb.Append(r.ToString());
            w += rw;
        }
        return sb.Append(dots).ToString();
    }

    /// <summary>
    /// The part of plain text that starts at column <paramref name="fromCol"/> and is at most <paramref name="cols"/> wide
    /// (used to scroll a long line inside the input box).
    /// </summary>
    public static string Slice(string plain, int fromCol, int cols)
    {
        var sb = new StringBuilder();
        var col = 0;
        var used = 0;
        foreach (var r in plain.EnumerateRunes())
        {
            var rw = RuneWidth(r);
            if (col >= fromCol)
            {
                if (used + rw > cols) break;
                sb.Append(r.ToString());
                used += rw;
            }
            col += rw;
        }
        return sb.ToString();
    }

    /// <summary>
    /// Breaks a line at spaces so no piece is wider than <paramref name="width"/> (a single longer word is left whole).
    /// Colour codes stay inside their words, so a colour simply carries on into the next piece.
    /// </summary>
    public static List<string> Wrap(string text, int width)
    {
        var lines = new List<string>();
        if (width <= 0 || Width(text) <= width) { lines.Add(text); return lines; }
        // The indent at the start stays on the first piece (the caller indents the others the same way).
        var lead = text.Length - text.TrimStart(' ').Length;
        width = Math.Max(1, width - lead);
        var current = new StringBuilder(new string(' ', lead));
        var used = 0;
        foreach (var word in text[lead..].Split(' '))
        {
            var w = Width(word);
            if (used > 0 && used + 1 + w > width)
            {
                lines.Add(current.ToString());
                current.Clear();
                used = 0;
            }
            if (used > 0) { current.Append(' '); used++; }
            current.Append(word);
            used += w;
        }
        lines.Add(current.ToString());
        return lines;
    }

    // ---- turning on colour support in the Windows console -------------------------------------------------------

    private const int StdOutputHandle = -11;
    private const uint EnableVirtualTerminalProcessing = 0x0004;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

    /// <summary>
    /// Asks the Windows console to understand colour and cursor codes. Windows Terminal does by default; the classic
    /// cmd.exe window only after this call. False when the output is a pipe or a file, or the console says no.
    /// </summary>
    public static bool TryEnableVirtualTerminal()
    {
        if (Console.IsOutputRedirected) return false;
        try
        {
            var h = GetStdHandle(StdOutputHandle);
            if (!GetConsoleMode(h, out var mode)) return false;
            if ((mode & EnableVirtualTerminalProcessing) != 0) return true;
            return SetConsoleMode(h, mode | EnableVirtualTerminalProcessing);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>The NO_COLOR convention (https://no-color.org): any value switches colours off.</summary>
    public static bool NoColorRequested => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"));
}
