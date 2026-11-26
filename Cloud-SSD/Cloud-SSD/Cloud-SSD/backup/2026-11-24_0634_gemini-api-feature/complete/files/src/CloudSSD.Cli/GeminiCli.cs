using System.IO;
using System.Text.Json;
using CloudSSD.Core;
using CloudSSD.Diagnostics;

namespace CloudSSD.Cli;

/// <summary>
/// CLI handler for 'cloudssd gemini' commands (summary, ask, tags, test).
/// </summary>
public static class GeminiCli
{
    public static async Task<int> RunAsync(string[] args, AppPaths paths, TextWriter o, TextWriter err, CancellationToken ct)
    {
        if (args.Length < 2)
        {
            o.WriteLine(Ansi.Bold("Cloud-SSD Gemini AI Assistant"));
            o.WriteLine("Usage:");
            o.WriteLine("  cloudssd gemini test               Verify API key and model connectivity");
            o.WriteLine("  cloudssd gemini summary <file>     Generate AI executive summary of a file");
            o.WriteLine("  cloudssd gemini ask <file> <q>     Ask a question about a file's content");
            o.WriteLine("  cloudssd gemini tags <file>        Suggest semantic tags for organizing a file");
            return 0;
        }

        var sub = args[1].ToLowerInvariant();
        if (sub is "test" or "ping")
        {
            var reply = await AppLink.PostAsync(paths, "/api/gemini/test", TimeSpan.FromSeconds(20), ct);
            if (reply.Ok)
            {
                o.WriteLine(Ansi.Green("✓ Gemini API connection successful! Model is ready."));
                return 0;
            }
            err.WriteLine(Ansi.Red("✗ Gemini API connection failed: " + reply.Message));
            return 1;
        }

        if (args.Length < 3)
        {
            err.WriteLine($"Error: Missing file path. Example: cloudssd gemini {sub} \"Z:\\Documents\\report.pdf\"");
            return 1;
        }

        var file = args[2];
        var fullPath = Path.GetFullPath(file);
        if (!File.Exists(fullPath))
        {
            err.WriteLine($"Error: File not found: {file}");
            return 1;
        }

        if (sub is "summary" or "summarize")
        {
            var query = $"/api/gemini/summarize?path={Uri.EscapeDataString(fullPath)}";
            var reply = await AppLink.PostAsync(paths, query, TimeSpan.FromSeconds(60), ct);
            if (!reply.Ok)
            {
                err.WriteLine(Ansi.Red("Error: " + reply.Message));
                return 1;
            }
            var summary = reply.Json?.TryGetProperty("summary", out var p) == true ? p.GetString() : reply.Message;
            o.WriteLine(Ansi.Bold($"✨ Summary of {Path.GetFileName(fullPath)}:"));
            o.WriteLine(summary);
            return 0;
        }

        if (sub is "tags" or "tag")
        {
            var query = $"/api/gemini/tags?path={Uri.EscapeDataString(fullPath)}";
            var reply = await AppLink.PostAsync(paths, query, TimeSpan.FromSeconds(60), ct);
            if (!reply.Ok)
            {
                err.WriteLine(Ansi.Red("Error: " + reply.Message));
                return 1;
            }
            o.WriteLine(Ansi.Bold($"✨ Suggested Tags for {Path.GetFileName(fullPath)}:"));
            if (reply.Json?.TryGetProperty("tags", out var tProp) == true && tProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var tag in tProp.EnumerateArray())
                {
                    o.WriteLine($"  • #{tag.GetString()}");
                }
            }
            return 0;
        }

        if (sub is "ask" or "chat")
        {
            if (args.Length < 4)
            {
                err.WriteLine("Error: Missing question. Example: cloudssd gemini ask file.pdf \"What is the total?\"");
                return 1;
            }
            var question = string.Join(" ", args[3..]);
            var query = $"/api/gemini/ask?path={Uri.EscapeDataString(fullPath)}&q={Uri.EscapeDataString(question)}";
            var reply = await AppLink.PostAsync(paths, query, TimeSpan.FromSeconds(60), ct);
            if (!reply.Ok)
            {
                err.WriteLine(Ansi.Red("Error: " + reply.Message));
                return 1;
            }
            var answer = reply.Json?.TryGetProperty("answer", out var aProp) == true ? aProp.GetString() : reply.Message;
            o.WriteLine(Ansi.Bold("Q: ") + question);
            o.WriteLine(Ansi.Bold("A: ") + answer);
            return 0;
        }

        err.WriteLine($"Unknown subcommand: {sub}. Type 'cloudssd gemini' for usage.");
        return 1;
    }
}
