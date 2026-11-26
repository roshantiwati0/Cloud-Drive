using CloudSSD.Core;
using CloudSSD.Mount;
using Microsoft.Extensions.Logging.Abstractions;

namespace CloudSSD.Cli;

/// <summary>
/// /key: moving the Google accounts from rclone's shared Google key (slow: Google holds it back, and it is being retired
/// in 2026) to the person's own key. The CLI checks where things stand, walks through making the key, waits for the
/// downloaded key file, and hands the last step (signing every account in again) to the PC app's own button, which also
/// disconnects and reconnects the drives safely.
/// </summary>
public static class Key
{
    public const string ConsoleUrl = "https://console.cloud.google.com/auth/clients";

    /// <summary>Where things stand: accounts still on the shared key (with their Gmail when known), and the key file.</summary>
    public sealed record State(IReadOnlyList<string> Shared, IReadOnlyDictionary<string, string> Emails, GoogleClientFile? File, bool RcloneFound);

    public static async Task<State> ReadAsync(AppPaths paths, CancellationToken ct)
    {
        var s = Views.Settings(paths);
        var file = GoogleClientFile.FindDownloaded();
        var rclone = await RcloneLocator.LocateAsync(s.RclonePathOverride, paths.DataRoot, ct);
        if (rclone is null) return new State([], s.AccountEmails, file, false);
        var config = new RcloneConfigService(paths, NullLogger<RcloneConfigService>.Instance);
        var shared = RcloneConfigService.SharedClientDriveRemotes(await config.DumpAsync(rclone, ct));
        return new State(shared, s.AccountEmails, file, true);
    }

    /// <summary>The steps in Google Cloud Console, in the words of the PC app's guide (CloudDrivesViewModel.KeyGuide).</summary>
    public static string[] Steps =>
    [
        "Google Cloud Console mein ek project banao (koi bhi naam).",
        $"APIs & Services {A} Library {A} \"Google Drive API\" {A} Enable.",
        $"Google Auth Platform {A} Branding: app ka naam aur apna email. Audience: External.",
        $"Audience {A} \"Publish app\" (In production). Testing mein chhoda to Google har 7 din mein login khatam kar deta hai.",
        $"Clients {A} Create client {A} Application type: Desktop app {A} Create {A} Download JSON.",
    ];

    private static string A => Ansi.Arrow;
    private static string KeyFileName => $"client_secret_{Ansi.Ellipsis}.json";

    public static void Write(TextWriter o, State st)
    {
        if (!st.RcloneFound)
        {
            o.WriteLine(Ansi.Red("rclone nahi mila.") + " /deps chalao.");
            return;
        }
        if (st.Shared.Count == 0)
        {
            o.WriteLine(Ansi.Green(Ansi.Tick) + " Saare Google account apni key par hain. Kuch karne ki zaroorat nahi.");
            return;
        }
        o.WriteLine(Ansi.Yellow($"{st.Shared.Count} account abhi shared key par:"));
        foreach (var r in st.Shared)
            o.WriteLine("  " + Ansi.Accent(Ansi.Pad(r, 10)) + (st.Emails.TryGetValue(r, out var e) ? e : ""));
        o.WriteLine(Ansi.Gray("  Isliye Google drive ko rokta hai (dheemi), aur 2026 mein yeh key band ho rahi hai."));
        o.WriteLine();
        o.WriteLine(st.File is { } f
            ? Ansi.Green(Ansi.Tick) + $" Key file mil gayi: {Path.GetFileName(f.Path)}" + (f.ProjectId != null ? Ansi.Gray($"  (project {f.ProjectId})") : "")
            : Ansi.Gray(Ansi.Ring) + $" Key file abhi nahi mili (Downloads ya Desktop mein {KeyFileName}).");
        o.WriteLine();
        if (st.File is null)
        {
            o.WriteLine(Ansi.Bold("Key banana (free, lagbhag 5 minute):") + Ansi.Gray("  " + ConsoleUrl));
            for (var i = 0; i < Steps.Length; i++) o.WriteLine($"  {Ansi.Accent($"{i + 1}.")} {Steps[i]}");
            o.WriteLine(Ansi.Gray($"  File Downloads mein aa jayegi ({KeyFileName}); Cloud-SSD use wahin dhoondhta hai."));
            o.WriteLine(Ansi.Gray($"  Google \"hasn't verified this app\" kahe to: woh aapka hi app hai {A} Advanced {A} Go to (app ka naam)."));
            o.WriteLine();
        }
        o.WriteLine(Ansi.Bold("Aakhri kadam, PC app mein:") + $" Cloud Drives {A} \"Switch to my own Google key\".");
        o.WriteLine(Ansi.Gray("  Har account ke liye Google ka sign-in khulega: wahi Gmail chuno jo pehle tha. Files ko kuch nahi hota;"));
        o.WriteLine(Ansi.Gray("  drives kuch minute ke liye hatengi aur khud wapas aayengi."));
    }
}
