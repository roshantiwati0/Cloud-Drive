using System.Text;
using CloudSSD.App;
using CloudSSD.Core;
using CloudSSD.Diagnostics;
using CloudSSD.SimCloud;

namespace CloudSSD.Cli;

/// <summary>
/// cloudssd / cpx. With no command it opens the interactive shell (<see cref="Shell"/>, laid out like Claude Code and
/// Codex); with a command it runs that one command and returns, for scripts and for the app itself.
/// </summary>
public static class Program
{
    public static string Version
    {
        get
        {
            var v = typeof(Program).Assembly.GetName().Version;
            return v is null ? "?" : $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }

    public static async Task<int> Main(string[] args)
    {
        var home = Opt(args, "--home");
        args = WithoutOption(args, "--home");
        var paths = new AppPaths(home);

        // rclone runs "cpx secret emit-config-pass" to unlock the saved Google login and reads exactly what is
        // printed: no colours, no encoding change, nothing else on the way.
        if (args.Length > 0 && args[0] == "secret")
            return await DispatchAsync(args, paths, Console.Out, Console.Error);

        var vt = Ansi.TryEnableVirtualTerminal();
        Ansi.Enabled = vt && !Ansi.NoColorRequested;
        Ansi.Unicode = vt;
        if (vt) Console.OutputEncoding = new UTF8Encoding(false);

        if (args.Length == 0)
        {
            if (vt && !Console.IsInputRedirected) return await new Shell(paths).RunAsync();
            return await Shell.RunPlainAsync(paths);
        }

        if (args[0] is "-h" or "--help" or "help" or "/help" or "/?" or "?")
        {
            Views.Help(Console.Out, shell: false);
            return 0;
        }

        // "cloudssd drive kholo" works like "cloudssd open". Never for something that closes or deletes: a script line
        // has nobody to answer "Yes / No".
        if (CommandCatalog.Find(args[0]) is null &&
            CommandCatalog.Understand(string.Join(" ", args)) is { } understood &&
            CommandCatalog.Find(understood.Words[0]) is { Hidden: false } meant &&
            meant.Name is not ("exit" or "clear"))
        {
            if (meant.Confirm)
            {
                Console.Error.WriteLine($"\"{string.Join(" ", args)}\" means {understood.Shown}. It closes or deletes something, so from the " +
                                        $"command line it runs only when typed exactly: cloudssd {meant.Name}");
                return 64;
            }
            Console.Error.WriteLine($"({Ansi.Arrow} {understood.Shown})");
            args = understood.Words;
        }

        try
        {
            return await DispatchAsync(args, paths, Console.Out, Console.Error);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Error: " + Redactor.Redact(ex.Message));
            return 1;
        }
    }

    /// <summary>
    /// Runs one command. <paramref name="o"/> gets the answer, <paramref name="err"/> the "cannot do it" lines.
    /// <paramref name="ct"/> is cancelled when the person stops waiting (Esc in the shell); <paramref name="progress"/>
    /// receives "how far" lines for the spinner (/find, /copy).
    /// </summary>
    public static async Task<int> DispatchAsync(string[] args, AppPaths paths, TextWriter o, TextWriter err,
        CancellationToken ct = default, IProgress<string>? progress = null)
    {
        var word = CommandCatalog.Clean(args[0]);
        var name = CommandCatalog.Find(word)?.Name ?? word.ToLowerInvariant();
        switch (name)
        {
            case "status":
                Views.Status(o, paths);
                return 0;

            case "live":
                return await Live.PrintOnceAsync(o, paths, ct);

            case "problem":
                return await Problems.ShowAsync(o, paths, ct);

            case "key":
                Key.Write(o, await Key.ReadAsync(paths, ct));
                return 0;

            case "faststart":
                try
                {
                    return await Views.FastStart(o, paths, ct);
                }
                catch (MissingMethodException)
                {
                    // This cpx next to an older installed app: its CloudSSD.Mount.dll does not have the new code yet.
                    err.WriteLine(Ansi.Yellow("The installed PC app is an older version without this. Update the app once, then run it again."));
                    return 1;
                }

            case "ls":
                return Views.Wrap(() => Files.List(o, args.Length > 1 ? string.Join(" ", args[1..]) : ".", ct));

            case "find":
            {
                var (query, where) = Files.ParseFind(args);
                if (query.Length == 0) return NeedsArgument(err, "find", "<name>", "Give a name, e.g. /find photo  or  /find *.zip in Z:");
                return Files.Find(o, paths, query, where, ct, progress);
            }

            case "copy":
                if (args.Length < 3) return NeedsArgument(err, "copy", "<from> <to>", "e.g. /copy \"Z:\\Games\\game.zip\" downloads");
                return await Files.CopyAsync(o, args[1], args[2], ct, progress);

            case "connect":
                if (args.Length < 2) return NeedsArgument(err, "connect", "<letter>", "e.g. /connect Z  (see /drives)");
                return await AppLink.ConnectAsync(o, paths, args[1], ct);

            case "disconnect":
                if (args.Length < 2) return NeedsArgument(err, "disconnect", "<letter>", "e.g. /disconnect Z  (see /drives)");
                return await AppLink.DisconnectAsync(o, paths, args[1], ct);

            case "send":
            {
                var (what, device) = Files.SplitAt(args, "to");
                if (what.Length == 0) return NeedsArgument(err, "send", "<file>", "e.g. /send \"D:\\Photos\\trip.jpg\"  or  /send downloads\\a.pdf to vivo");
                return await AppLink.SendAsync(o, paths, what, device, ct);
            }

            case "open":
                return Views.Open(o, args.Length > 1 ? args[1] : null);

            case "drives":
                Views.Drives(o, paths);
                return 0;

            case "accounts":
                await Views.Accounts(o, paths, ct);
                return 0;

            case "phone":
                Views.Phones(o, paths);
                return 0;

            case "cache":
                Views.Cache(o, paths);
                return 0;

            case "clean":
                return Views.Clean(o);

            case "mirror":
                Views.Mirror(o, paths);
                return 0;

            case "start":
                return Views.Start(o);

            case "stop":
                return Views.Stop(o);

            case "deps":
                return await Views.Deps(o, paths);

            case "help":
                Views.Help(o, shell: false);
                return 0;

            case "secret" when args.Length > 1 && args[1] == "emit-config-pass":
            {
                var pass = SecretStore.Load(paths.ConfigPasswordFile);
                if (pass is null)
                {
                    err.WriteLine("No configuration key found for this Windows user.");
                    return 1;
                }
                o.Write(pass);
                return 0;
            }

            case "scan":
                if (args.Length < 2) return NeedsArgument(err, "scan", "<folder>", "Give the game folder, e.g. /scan \"D:\\Games\\My Game\"");
                return Views.Scan(o, args[1], double.TryParse(Opt(args, "--mbps"), out var mbps) ? mbps : 70);

            case "pe":
                if (args.Length < 2) return NeedsArgument(err, "pe", "<file.exe>", "Give the program file, e.g. /pe \"D:\\Games\\game.exe\"");
                return Views.Pe(o, args[1]);

            case "profile":
                if (args.Length < 2) return NeedsArgument(err, "profile", "<game-id>", "Give the game id from the PC app's Games page.");
                return Views.Profile(o, paths, args[1]);

            case "mount":
                if (args.Length < 2) return NeedsArgument(err, "mount", "<id> [--letter X]", "Give a drive id from /drives.");
                return await MountAsync(args, paths, o);

            case "simcloud":
                return await SimCloudAsync(args, o);

            default:
                var guess = CommandCatalog.Closest(word);
                err.WriteLine($"Unknown command: {word}{(guess != null ? $" (did you mean \"{guess}\"?)" : "")}");
                err.WriteLine("Type 'cloudssd help' for the list.");
                return 64;
        }
    }

    private static int NeedsArgument(TextWriter err, string name, string args, string hint)
    {
        err.WriteLine(Ansi.Yellow($"{name} needs {args}.") + " " + hint);
        return 64;
    }

    /// <summary>Mounts one drive in this window until Ctrl+C.</summary>
    private static async Task<int> MountAsync(string[] args, AppPaths paths, TextWriter o)
    {
        await using var host = AppHost.Create(paths);
        if (!host.TryAcquireInstanceLock())
        {
            o.WriteLine(Ansi.Yellow("The Cloud-SSD app is running, and it already looks after the drives."));
            o.WriteLine("See them with /status, or open one with /open.");
            return 0;
        }
        await host.InitializeAsync(restoreMounts: false);
        var def = host.Settings.Mounts.FirstOrDefault(m => m.Id == args[1] || m.DisplayName == args[1])
                  ?? throw new ArgumentException($"No drive '{args[1]}'. See /drives for the ids.");
        if (Opt(args, "--letter") is { } l) def.DriveLetter = l;
        var s = await host.MountAsync(def);
        o.WriteLine($"Mounted {def.RemoteSpec} at {s.DriveLetter}:. Press Ctrl+C to unmount.");
        await WaitForCtrlC();
        await host.ShutdownAsync(keepMounts: false);
        o.WriteLine("Unmounted.");
        return 0;
    }

    private static async Task<int> SimCloudAsync(string[] args, TextWriter o)
    {
        var root = Opt(args, "--root") ?? throw new ArgumentException("--root <dir> is needed: the folder to serve.");
        var port = int.TryParse(Opt(args, "--port"), out var p) ? p : 8080;
        await using var server = new SimCloudServer(root, port)
        {
            BandwidthMbps = double.TryParse(Opt(args, "--mbps"), out var mb) ? mb : 0,
            Latency = TimeSpan.FromMilliseconds(double.TryParse(Opt(args, "--latency-ms"), out var lat) ? lat : 0)
        };
        o.WriteLine($"Serving {server.Root} at {server.Url} ({server.BandwidthMbps:0} Mbps, {server.Latency.TotalMilliseconds:0} ms). Ctrl+C to stop.");
        await WaitForCtrlC();
        o.WriteLine($"Requests {server.Requests}, bytes sent {ByteSize.Format(server.BytesSent)}");
        return 0;
    }

    /// <summary>Waits for one Ctrl+C. The handler is removed afterwards, so running mount twice in the shell works.</summary>
    private static async Task WaitForCtrlC()
    {
        var done = new TaskCompletionSource();
        ConsoleCancelEventHandler handler = (_, e) => { e.Cancel = true; done.TrySetResult(); };
        Console.CancelKeyPress += handler;
        try
        {
            await done.Task;
        }
        finally
        {
            Console.CancelKeyPress -= handler;
        }
    }

    internal static string? Opt(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    /// <summary>The arguments without "name value" (so "cpx --home D:\x status" runs "status").</summary>
    internal static string[] WithoutOption(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        if (i < 0) return args;
        var list = args.ToList();
        list.RemoveRange(i, Math.Min(2, list.Count - i));
        return list.ToArray();
    }
}
