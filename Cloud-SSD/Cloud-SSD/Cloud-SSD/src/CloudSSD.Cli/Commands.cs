using System.Text;

namespace CloudSSD.Cli;

/// <summary>One command of the shell / command line.</summary>
/// <param name="Name">The main word ("status"). In the shell it is typed as "/status" or just "status".</param>
/// <param name="Aliases">Other words that run the same command (old names such as "mounts" keep working).</param>
/// <param name="Args">What may follow the word, for the menu and help ("[letter]", "&lt;folder&gt;").</param>
/// <param name="Help">One short line for the menu.</param>
/// <param name="Title">Heading above the answer ("● Google accounts").</param>
/// <param name="Doing">Spinner text while it runs ("Asking Google").</param>
/// <param name="Live">Runs straight in the window until Ctrl+C (mount, test server); no spinner, no capture.</param>
/// <param name="Hidden">Not shown in menus or help (used by the app itself).</param>
/// <param name="Confirm">The shell asks "Yes / No" first (the command closes or deletes something).</param>
/// <param name="ArgMenu">After the word the shell offers a list of choices (the drive letters for /open).</param>
public sealed record CliCommand(
    string Name,
    string[] Aliases,
    string Args,
    string Help,
    string Title,
    string Doing,
    bool Live = false,
    bool Hidden = false,
    bool Confirm = false,
    bool ArgMenu = false)
{
    /// <summary>True when the command cannot run without something after the word.</summary>
    public bool NeedsArgs => Args.Length > 0 && !Args.StartsWith('[');

    /// <summary>"/scan &lt;folder&gt;" as shown in the menu.</summary>
    public string Usage => "/" + Name + (Args.Length > 0 ? " " + Args : "");
}

public static class CommandCatalog
{
    public static readonly IReadOnlyList<CliCommand> All =
    [
        new("status", ["s"], "", "Everything at a glance: app, drives, cache, accounts, phone", "Status", "Checking everything"),
        new("open", ["o"], "[letter]", "Open a cloud drive in File Explorer", "Open", "Opening", ArgMenu: true),
        new("live", ["watch", "speed"], "", "Live speed, uploads and cache of the drives (Esc stops)", "Live", "Asking the drives"),
        new("problem", ["problems", "issue"], "", "What is wrong right now, and what to do", "Problems", "Reading the app log"),
        new("key", ["googlekey"], "", "Use your own Google key: drives stop being slow (step by step)", "Google key", "Checking the accounts"),
        new("faststart", ["fast-start", "boot"], "", "Drives appear right after the laptop starts (set up once)", "Fast start", "Asking Google once for each account's top folder"),
        new("drives", ["mounts", "m"], "", "Your drive letters and what each one shows", "Drives", "Looking at the drives"),
        new("connect", [], "[letter]", "Connect a cloud drive (bring Z: or Y: back)", "Connect", "Connecting (Google can take a minute)", ArgMenu: true),
        new("disconnect", ["eject"], "[letter]", "Disconnect a cloud drive (asks first)", "Disconnect", "Disconnecting (waiting for uploads)", Confirm: true, ArgMenu: true),
        new("accounts", ["remotes", "r"], "", "Google accounts connected to Cloud-SSD", "Google accounts", "Reading the accounts"),
        new("phone", ["devices", "p"], "", "Phones paired with this PC", "Phones", "Looking for phones"),
        new("ls", ["dir", "list"], "<folder>", "What is inside a folder (Z:\\Games, D:\\, downloads)", "Folder", "Reading the folder"),
        new("find", ["search"], "<name> [in <folder>]", "Find files by name on the cloud drives", "Find", "Searching"),
        new("gemini", ["ai"], "<summary|ask|tags|test> [file] [q]", "Gemini AI assistant: file summaries, Q&A, and tags", "Gemini AI", "Analyzing with Gemini"),
        new("copy", ["cp"], "<from> <to>", "Copy a file or folder with progress (never overwrites)", "Copy", "Copying"),
        new("send", [], "<file> [to <phone>]", "Send a file or folder from this PC to your phone", "Send", "Offering it to the phone"),
        new("cache", ["c"], "", "Space on this PC that makes cloud files open fast", "Cache", "Measuring the cache"),
        new("clean", [], "", "Delete old temporary files (safe, never your files)", "Clean up", "Cleaning", Confirm: true),
        new("mirror", ["sync"], "", "Cloud copy of this PC (files while the PC is off)", "Cloud copy", "Reading the cloud copy"),
        new("start", [], "", "Start the Cloud-SSD app", "Start", "Starting the app"),
        new("stop", [], "", "Close the Cloud-SSD app", "Stop", "Closing the app", Confirm: true),
        new("deps", ["doctor"], "", "Check WinFsp, rclone and free space", "Check-up", "Checking WinFsp and rclone"),
        new("scan", [], "<folder>", "Will this game folder run from the cloud?", "Game scan", "Scanning the game"),
        new("pe", [], "<file.exe>", "What a program needs to start (DLLs)", "Program check", "Reading the program"),
        new("profile", [], "<game-id>", "What a game reads when it starts", "Game profile", "Reading the profile"),
        new("mount", [], "<id> [--letter X]", "Mount one drive in this window (Ctrl+C stops it)", "Mount", "Mounting", Live: true),
        new("simcloud", [], "--root <dir>", "Test cloud server in this window (Ctrl+C stops it)", "Test cloud", "Starting", Live: true),
        new("clear", ["cls"], "", "Clean the screen", "Clear", ""),
        new("help", ["?"], "", "All commands and keys", "Help", ""),
        new("exit", ["quit", "q"], "", "Leave the Cloud-SSD shell", "Exit", ""),
        // rclone runs "cpx secret emit-config-pass" to unlock the saved login. Never in menus.
        new("secret", [], "emit-config-pass", "", "Secret", "", Hidden: true),
    ];

    /// <summary>The command for a typed word: "/status", "status", "STATUS", "s" and "mounts" style old names all work.</summary>
    public static CliCommand? Find(string word)
    {
        var w = Clean(word).TrimStart('/').ToLowerInvariant();
        if (w.Length == 0) return null;
        return All.FirstOrDefault(c => c.Name == w) ?? All.FirstOrDefault(c => c.Aliases.Contains(w));
    }

    /// <summary>
    /// The menu under the input box. Opens when the line starts with "/" and is still one word; best matches first:
    /// name starts with it, then an old name starts with it, then (3+ letters) the name or help text contains it.
    /// </summary>
    public static IReadOnlyList<CliCommand> Suggest(string input)
    {
        if (!input.StartsWith('/') || input.Contains(' ')) return [];
        var q = input[1..].ToLowerInvariant();
        var shown = All.Where(c => !c.Hidden).ToList();
        if (q.Length == 0) return shown;

        var result = shown.Where(c => c.Name.StartsWith(q, StringComparison.Ordinal)).ToList();
        result.AddRange(shown.Where(c => !result.Contains(c) && c.Aliases.Any(a => a.StartsWith(q, StringComparison.Ordinal))));
        if (q.Length >= 3)
        {
            result.AddRange(shown.Where(c => !result.Contains(c) &&
                (c.Name.Contains(q, StringComparison.Ordinal) || c.Help.Contains(q, StringComparison.OrdinalIgnoreCase))));
        }
        return result;
    }

    /// <summary>For a mistyped word, the nearest command name ("stauts" → "status"), or null.</summary>
    public static string? Closest(string word)
    {
        var w = Clean(word).TrimStart('/').ToLowerInvariant();
        if (w.Length < 2) return null;
        string? best = null;
        var bestDistance = int.MaxValue;
        foreach (var c in All.Where(c => !c.Hidden))
        {
            foreach (var candidate in c.Aliases.Prepend(c.Name))
            {
                var d = Distance(w, candidate);
                if (d < bestDistance) { bestDistance = d; best = c.Name; }
            }
        }
        return bestDistance <= Math.Min(2, w.Length - 1) ? best : null;
    }

    /// <summary>A sentence understood as a command: <see cref="Words"/> run like typed words, <see cref="Shown"/> is "/open Z:".</summary>
    public sealed record Understood(string[] Words)
    {
        public string Shown => "/" + string.Join(" ", Words);
    }

    // Word beginnings (English, Hinglish, Hindi) that point at a command. "khol" matches khol, kholo, kholna, kholdo.
    private static readonly (string Command, string[] Stems)[] Meanings =
    [
        ("open", ["khol", "open", "explorer", "खोल"]),
        ("stop", ["stop", "band", "close", "बंद"]),
        ("start", ["start", "chalu", "shuru", "चालू", "शुरू"]),
        ("clean", ["clean", "saaf", "safai", "kachra", "temp", "साफ", "सफाई"]),
        ("phone", ["phone", "mobile", "fone", "फोन", "फ़ोन", "मोबाइल"]),
        ("key", ["key", "chabi", "oauth", "चाबी"]),   // before accounts: "google key lagao" is about the key
        ("accounts", ["account", "gmail", "google", "email", "khata", "अकाउंट", "खाता"]),
        ("cache", ["cache", "jagah", "jagh", "space", "storage", "kitni", "kitna", "bachi", "जगह"]),
        ("problem", ["problem", "dikkat", "error", "galti", "issue", "slow", "dheem", "kharab", "दिक्कत", "समस्या"]),
        ("live", ["live", "speed", "raftar", "raftaar", "upload", "download"]),
        ("find", ["dhundh", "dhoondh", "dhund", "find", "search", "khoj", "ढूंढ", "खोज"]),
        ("send", ["bhej", "send", "भेज"]),
        ("connect", ["jod", "connect", "जोड़", "जोड"]),
        ("disconnect", ["hata", "disconnect", "eject", "alag", "हटा"]),
        ("mirror", ["mirror", "backup", "sync"]),
        ("deps", ["deps", "doctor", "check", "jaanch", "janch", "winfsp", "rclone", "जांच"]),
        ("drives", ["drive", "mount", "letter", "ड्राइव"]),
        ("status", ["status", "haal", "sab", "kaisa", "summary", "overview", "हाल"]),
        ("help", ["help", "madad", "commands", "मदद"]),
        ("clear", ["clear", "screen"]),
        ("exit", ["exit", "bye", "bahar", "nikal", "quit"]),
    ];

    // Little words around a name in a sentence ("meri photo.jpg file dhundho do").
    private static readonly HashSet<string> Filler =
    [
        "karo", "kar", "do", "de", "dena", "ko", "hai", "mera", "meri", "mere", "wala", "wali", "wale", "file", "files",
        "ki", "ka", "ke", "me", "mein", "main", "please", "plz", "naam", "ek", "koi", "kaha", "kahan", "kidhar", "the", "a",
        "my", "for", "please", "को", "की", "का", "मेरी", "मेरा", "फाइल", "करो", "दो",
    ];

    /// <summary>
    /// Understands a plain sentence the way the user writes it: "z drive kholo" → /open Z:, "phone dikhao" → /phone,
    /// "kitni jagah bachi hai" → /cache, "app band karo" → /stop. No AI: word beginnings, so it is quick and predictable.
    /// Null when nothing points at a command.
    /// </summary>
    public static Understood? Understand(string line)
    {
        var tokens = Clean(line).ToLowerInvariant()
            .Split([' ', '\t', ',', '.', '?', '!', ';', '"', '\'', '/', '-', '।'], StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return null;

        var scores = new Dictionary<string, int>();
        foreach (var t in tokens)
            foreach (var (command, stems) in Meanings)
                if (stems.Any(s => t.StartsWith(s, StringComparison.Ordinal)))
                    scores[command] = scores.GetValueOrDefault(command) + 1;

        // A drive letter on its own ("z", "y:"), never "a" / "i" (ordinary words).
        var letter = tokens.Select(t => t.TrimEnd(':'))
            .FirstOrDefault(t => t.Length == 1 && t[0] is >= 'b' and <= 'z' && t[0] != 'i');

        if (scores.ContainsKey("open"))
            return new Understood(letter != null ? ["open", letter.ToUpperInvariant() + ":"] : ["open"]);
        if (scores.ContainsKey("find"))
        {
            // "photo.jpg dhundho" → /find photo.jpg: what is left after the search word and the little words is the name.
            var findStems = Meanings.First(m => m.Command == "find").Stems;
            // "z drive mein photo dhundho" also says where: /find photo in Z:
            var name = Clean(line).Split([' ', '\t', '?', '!', ',', '।'], StringSplitOptions.RemoveEmptyEntries)
                .Where(w => !findStems.Any(s => w.ToLowerInvariant().StartsWith(s, StringComparison.Ordinal)))
                .Where(w => !Filler.Contains(w.ToLowerInvariant()))
                .Where(w => w.ToLowerInvariant() is not ("drive" or "ड्राइव") && (letter is null || w.TrimEnd(':').ToLowerInvariant() != letter))
                .ToList();
            if (name.Count == 0) return new Understood(["find"]);
            string[] words = ["find", string.Join(" ", name)];
            return new Understood(letter != null ? [.. words, "in", letter.ToUpperInvariant() + ":"] : words);
        }
        if (scores.ContainsKey("send"))
        {
            // "photo.jpg phone ko bhejo" → /send photo.jpg: what is left is the file.
            var skip = Meanings.Where(m => m.Command is "send" or "phone").SelectMany(m => m.Stems).ToList();
            var what = Clean(line).Split([' ', '\t', '?', '!', ',', '।'], StringSplitOptions.RemoveEmptyEntries)
                .Where(w => !skip.Any(s => w.ToLowerInvariant().StartsWith(s, StringComparison.Ordinal)))
                .Where(w => !Filler.Contains(w.ToLowerInvariant()) && w.ToLowerInvariant() != "par")
                .ToList();
            return new Understood(what.Count > 0 ? ["send", string.Join(" ", what)] : ["send"]);
        }

        // "z drive jodo" / "z drive hatao", and "z drive chalu karo" / "z drive band karo" (a letter makes it about that
        // drive, not about the app). Without a drive in the sentence these words mean something else ("phone connect").
        var aboutDrive = letter != null || scores.ContainsKey("drives");
        foreach (var (verb, appVerb) in new[] { ("disconnect", "stop"), ("connect", "start") })
        {
            var said = scores.ContainsKey(verb) || (letter != null && scores.ContainsKey(appVerb) && scores.ContainsKey("drives"));
            if (said && aboutDrive)
                return new Understood(letter != null ? [verb, letter.ToUpperInvariant() + ":"] : [verb]);
            scores.Remove(verb);
        }
        if (scores.Count == 0)
            return null;

        // "band" / "chalu" are about the app unless the sentence is about something else ("drive band" is not "close the app").
        var mentionsApp = tokens.Any(t => t is "app" or "ऐप" || t.StartsWith("cloud", StringComparison.Ordinal));
        foreach (var verb in new[] { "stop", "start" })
        {
            if (!scores.ContainsKey(verb)) continue;
            var others = scores.Keys.Count(k => k != verb);
            if (mentionsApp || others == 0) return new Understood([verb]);
            scores.Remove(verb);
        }

        var order = Meanings.Select(m => m.Command).ToList();
        var best = scores.OrderByDescending(p => p.Value).ThenBy(p => order.IndexOf(p.Key)).First().Key;
        return new Understood([best]);
    }

    /// <summary>Splits a typed line into words; "double quotes" keep spaces inside one word (folder names).</summary>
    public static string[] Split(string input)
    {
        var list = new List<string>();
        var inQuotes = false;
        var current = new StringBuilder();
        foreach (var ch in Clean(input))
        {
            if (ch == '"') inQuotes = !inQuotes;
            else if (char.IsWhiteSpace(ch) && !inQuotes)
            {
                if (current.Length > 0) { list.Add(current.ToString()); current.Clear(); }
            }
            else current.Append(ch);
        }
        if (current.Length > 0) list.Add(current.ToString());
        return list.ToArray();
    }

    /// <summary>
    /// Removes the invisible byte-order mark. Windows PowerShell puts one in front of text it pipes into a program, so
    /// '"help" | cpx' used to answer "Unknown command: ﻿help".
    /// </summary>
    public static string Clean(string s) => s.Replace("﻿", "");

    private static int Distance(string a, string b)
    {
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) prev[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }
}
