using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace CloudSSD.App;

public sealed partial class LocalMobileBridgeService
{
    /// <summary>
    /// Handles /api/gemini/* routes for AI file summarization, Q&amp;A, and semantic auto-tagging.
    /// </summary>
    private async Task<bool> TryHandleGeminiRouteAsync(
        string method,
        string path,
        Dictionary<string, string> q,
        Dictionary<string, string> headers,
        NetworkStream stream,
        CancellationToken ct)
    {
        if (!path.StartsWith("/api/gemini/", StringComparison.OrdinalIgnoreCase)) return false;

        var route = path.ToLowerInvariant();
        try
        {
            if (route == "/api/gemini/status")
            {
                var configured = _host.Gemini.IsConfigured;
                var model = _host.Gemini.Model;
                var enabled = _host.Settings.Gemini.Enabled;
                await SendJsonAsync(stream, 200, "OK", new { enabled, configured, model }, ct);
                return true;
            }

            if (route == "/api/gemini/test" && method == "POST")
            {
                var key = q.GetValueOrDefault("key");
                var model = q.GetValueOrDefault("model");
                bool ok = await _host.Gemini.TestConnectionAsync(key, model, ct);
                await SendJsonAsync(stream, 200, "OK", new { success = ok }, ct);
                return true;
            }

            if (route == "/api/gemini/config" && method == "POST")
            {
                var key = q.GetValueOrDefault("key");
                var model = q.GetValueOrDefault("model");
                if (key != null) _host.Settings.Gemini.ApiKey = key.Trim();
                if (!string.IsNullOrWhiteSpace(model)) _host.Settings.Gemini.Model = model.Trim();
                _host.SettingsStore.Save(_host.Settings);
                await SendJsonAsync(stream, 200, "OK", new { success = true, model = _host.Gemini.Model, configured = _host.Gemini.IsConfigured }, ct);
                return true;
            }

            // Remaining routes require a target file path
            var targetPath = ResolveFilePath(q.GetValueOrDefault("path"));
            if (string.IsNullOrEmpty(targetPath) || !File.Exists(targetPath))
            {
                await SendJsonAsync(stream, 404, "Not Found", new { error = "File not found or invalid path." }, ct);
                return true;
            }

            if (route == "/api/gemini/summarize" && method == "POST")
            {
                var summary = await _host.Gemini.SummarizeFileAsync(targetPath, ct);
                await SendJsonAsync(stream, 200, "OK", new { summary }, ct);
                return true;
            }

            if (route == "/api/gemini/tags" && method == "POST")
            {
                var tags = await _host.Gemini.SuggestTagsAsync(targetPath, ct);
                await SendJsonAsync(stream, 200, "OK", new { tags }, ct);
                return true;
            }

            if (route == "/api/gemini/ask" && method == "POST")
            {
                var question = q.GetValueOrDefault("q") ?? q.GetValueOrDefault("question");
                if (string.IsNullOrWhiteSpace(question))
                {
                    var body = await ReadSmallBodyAsync(stream, headers, 8192, ct);
                    if (!string.IsNullOrWhiteSpace(body))
                    {
                        try
                        {
                            using var doc = JsonDocument.Parse(body);
                            if (doc.RootElement.TryGetProperty("question", out var qProp))
                                question = qProp.GetString();
                            else if (doc.RootElement.TryGetProperty("prompt", out var pProp))
                                question = pProp.GetString();
                        }
                        catch { }
                    }
                }

                if (string.IsNullOrWhiteSpace(question))
                {
                    await SendJsonAsync(stream, 400, "Bad Request", new { error = "Missing question parameter or body." }, ct);
                    return true;
                }

                var answer = await _host.Gemini.AskFileAsync(targetPath, question, ct);
                await SendJsonAsync(stream, 200, "OK", new { answer }, ct);
                return true;
            }

            await SendJsonAsync(stream, 404, "Not Found", new { error = "Unknown Gemini endpoint." }, ct);
            return true;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Gemini route failed for {Path}", path);
            await SendJsonAsync(stream, 500, "Internal Server Error", new { error = ex.Message }, ct);
            return true;
        }
    }

    private string? ResolveFilePath(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        raw = raw.Trim();
        if (File.Exists(raw)) return raw;
        try
        {
            var target = ResolveCloudPath(BrowseRoots(), raw);
            if (File.Exists(target.Path)) return target.Path;
        }
        catch { }
        return null;
    }
}
