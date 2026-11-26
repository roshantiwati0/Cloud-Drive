using System.Net;
using System.Text.Json;
using CloudSSD.App;
using CloudSSD.Core;
using CloudSSD.Diagnostics;

namespace CloudSSD.Cli;

/// <summary>
/// /connect, /disconnect and /send: things only the running PC app can do (it owns the drives and the phone link), so the
/// terminal asks it. The app listens on 127.0.0.1 for this and wants the terminal key: a random key it writes at every
/// start into cli.key, protected for this Windows user (LocalMobileBridgeService.Cli.cs). The key travels in a header,
/// never in the address, and only ever to 127.0.0.1.
/// </summary>
public static class AppLink
{
    public sealed record Reply(bool Ok, string Message, JsonElement? Json = null);

    /// <summary>The terminal key the running app wrote, or null (no app yet, an older app, or another Windows user's file).</summary>
    internal static string? ReadKey(AppPaths paths)
    {
        try
        {
            var file = Path.Combine(paths.DataRoot, LocalMobileBridgeService.CliKeyFileName);
            return File.Exists(file) ? SecretStore.Unprotect(File.ReadAllText(file).Trim()) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static Task<Reply> PostAsync(AppPaths paths, string pathAndQuery, TimeSpan timeout, CancellationToken ct) =>
        PostAsync(paths, Views.Settings(paths).MobileBridge.Port, pathAndQuery, timeout, ct);

    internal static async Task<Reply> PostAsync(AppPaths paths, int port, string pathAndQuery, TimeSpan timeout, CancellationToken ct)
    {
        var key = ReadKey(paths);
        if (key is null)
        {
            return new Reply(false, Views.FindApp() is null
                ? "The Cloud-SSD app is not running. Start it with /start, then try again."
                : "The running PC app is an older version without this. Update the app once (close it, run the installer, start it); " +
                  "then /connect, /disconnect and /send work.");
        }
        using var http = new HttpClient(new SocketsHttpHandler { UseProxy = false }) { Timeout = timeout };
        using var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{port}{pathAndQuery}");
        request.Headers.Add("X-Pairing-Token", key);
        try
        {
            using var response = await http.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            JsonElement? json = null;
            try { json = JsonDocument.Parse(body).RootElement.Clone(); } catch (JsonException) { /* not JSON: no details */ }
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                return new Reply(false, "The app did not accept the terminal key (it has just restarted, or is an older version). " +
                                        "Try again; if it stays: /stop, then /start.");
            var text = Text(json, "message") ?? Text(json, "error") ?? (response.IsSuccessStatusCode ? "Done." : $"The app answered {(int)response.StatusCode}.");
            return new Reply(response.IsSuccessStatusCode, Redactor.Redact(text), json);
        }
        catch (HttpRequestException)
        {
            return new Reply(false, Views.FindApp() is null
                ? "The Cloud-SSD app is not running. Start it with /start, then try again."
                : $"The app is not answering on port {port}. Try again in a moment.");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return new Reply(false, $"The app did not finish within {Views.Span(timeout)}. See /live or /problem.");
        }
    }

    private static string? Text(JsonElement? json, string name) =>
        json is { ValueKind: JsonValueKind.Object } j && j.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>What the app is told for a drive: "Z" for "z", "Z:" or "z:\"; an id ("cloudssd") as it is.</summary>
    internal static string DriveId(string typed)
    {
        var letter = Views.Letter(typed);
        return letter.Length == 1 ? letter : typed.Trim();
    }

    public static async Task<int> ConnectAsync(TextWriter o, AppPaths paths, string drive, CancellationToken ct)
    {
        // The app waits for the drive letter to appear; with Google holding back the shared key that can take minutes.
        var r = await PostAsync(paths, "/api/drives/mount?id=" + Uri.EscapeDataString(DriveId(drive)), TimeSpan.FromMinutes(6), ct);
        o.WriteLine(r.Ok ? Ansi.Green(Ansi.Tick) + " " + r.Message : Ansi.Yellow(r.Message));
        return r.Ok ? 0 : 1;
    }

    public static async Task<int> DisconnectAsync(TextWriter o, AppPaths paths, string drive, CancellationToken ct)
    {
        // The app gives files that are still uploading up to a minute, like Disconnect in its own window.
        var r = await PostAsync(paths, "/api/drives/unmount?id=" + Uri.EscapeDataString(DriveId(drive)), TimeSpan.FromMinutes(3), ct);
        o.WriteLine(r.Ok ? Ansi.Green(Ansi.Tick) + " " + r.Message + Ansi.Gray("  (/connect brings it back)") : Ansi.Yellow(r.Message));
        return r.Ok ? 0 : 1;
    }

    /// <summary>Offers a file or folder on this PC to a paired phone, like "Send to devices" in the app. The phone accepts it.</summary>
    public static async Task<int> SendAsync(TextWriter o, AppPaths paths, string what, string? device, CancellationToken ct)
    {
        var path = Path.GetFullPath(Files.Place(what));
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            o.WriteLine(Ansi.Yellow($"Nothing at {path}."));
            return 1;
        }
        var r = await PostAsync(paths, $"/api/cli/send?device={Uri.EscapeDataString(device ?? "")}&path={Uri.EscapeDataString(path)}",
            TimeSpan.FromMinutes(2), ct);
        if (!r.Ok || r.Json is not { } j)
        {
            o.WriteLine(Ansi.Yellow(r.Message));
            return 1;
        }
        var files = j.TryGetProperty("files", out var f) ? f.GetInt32() : 0;
        var bytes = j.TryGetProperty("bytes", out var b) ? b.GetInt64() : 0;
        var skipped = j.TryGetProperty("skipped", out var s) ? s.GetInt32() : 0;
        var name = Text(j, "device") ?? "the phone";
        o.WriteLine(Ansi.Green(Ansi.Tick) + $" Offered {files} file(s), {ByteSize.Format(bytes)} to {name}.");
        o.WriteLine(Ansi.Gray("Open Cloud-SSD on the phone and accept it there; the download starts then."));
        if (skipped > 0) o.WriteLine(Ansi.Gray($"{skipped} file(s) that a build makes again (bin, node_modules{Ansi.Ellipsis}) were left out."));
        return 0;
    }
}
