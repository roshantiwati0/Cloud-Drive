using System.Text;

namespace CloudSSD.Cli;

/// <summary>
/// The big "CLOUD-SSD" in block letters at the top of the welcome box, coloured from the app's teal (#5EEAD4) to blue
/// (#3B82F6) from left to right, like the gradient logos of Gemini CLI and Codex.
/// </summary>
public static class Logo
{
    private const int Rows = 5;

    // 5 rows per letter, every row 5 columns wide.
    private static readonly Dictionary<char, string[]> Font = new()
    {
        ['C'] = [" ████", "██   ", "██   ", "██   ", " ████"],
        ['L'] = ["██   ", "██   ", "██   ", "██   ", "█████"],
        ['O'] = [" ███ ", "██ ██", "██ ██", "██ ██", " ███ "],
        ['U'] = ["██ ██", "██ ██", "██ ██", "██ ██", " ███ "],
        ['D'] = ["████ ", "██ ██", "██ ██", "██ ██", "████ "],
        ['S'] = [" ████", "██   ", " ███ ", "   ██", "████ "],
        ['-'] = ["     ", "     ", " ███ ", "     ", "     "],
    };

    private const string Text = "CLOUD-SSD";

    /// <summary>Columns the logo needs.</summary>
    public static int Width => Text.Length * 6 - 1;

    /// <summary>The logo rows, or nothing when the box is too narrow or the console cannot show block letters.</summary>
    public static IReadOnlyList<string> Lines(int room)
    {
        if (!Ansi.Unicode || room < Width) return [];
        var rows = new List<string>(Rows);
        for (var r = 0; r < Rows; r++)
        {
            var plain = new StringBuilder();
            foreach (var ch in Text) plain.Append(Font[ch][r]).Append(' ');
            var row = plain.ToString(0, Width);
            rows.Add(Colour(row));
        }
        return rows;
    }

    /// <summary>Each column gets its own colour between teal and blue; spaces stay uncoloured.</summary>
    private static string Colour(string row)
    {
        if (!Ansi.Enabled) return row;
        var sb = new StringBuilder();
        for (var i = 0; i < row.Length; i++)
        {
            if (row[i] == ' ') { sb.Append(' '); continue; }
            var t = Width <= 1 ? 0 : i / (double)(Width - 1);
            sb.Append(Ansi.Rgb(Mix(0x5E, 0x3B, t), Mix(0xEA, 0x82, t), Mix(0xD4, 0xF6, t), row[i].ToString()));
        }
        return sb.ToString();
    }

    private static int Mix(int a, int b, double t) => (int)Math.Round(a + (b - a) * t);
}
