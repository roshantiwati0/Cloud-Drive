using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using CloudSSD.Core;
using Microsoft.Extensions.Logging;

namespace CloudSSD.App.Gemini;

/// <summary>
/// Service providing Google Gemini API integration for multimodal file analysis,
/// summarization, interactive Q&amp;A, and semantic auto-tagging.
/// </summary>
public sealed class GeminiService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };
    private readonly AppHost _host;
    private readonly ILogger<GeminiService> _log;

    public GeminiService(AppHost host, ILogger<GeminiService> log)
    {
        _host = host;
        _log = log;
    }

    /// <summary>
    /// Gets the effective API key from settings or GEMINI_API_KEY environment variable.
    /// </summary>
    public string? GetEffectiveApiKey()
    {
        var key = _host.Settings.Gemini.ApiKey;
        if (!string.IsNullOrWhiteSpace(key)) return key.Trim();
        var envKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        return string.IsNullOrWhiteSpace(envKey) ? null : envKey.Trim();
    }

    /// <summary>Whether a valid Gemini API key is configured.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(GetEffectiveApiKey());

    /// <summary>Active model identifier (default: gemini-4-hydrogen).</summary>
    public string Model => string.IsNullOrWhiteSpace(_host.Settings.Gemini.Model)
        ? "gemini-4-hydrogen"
        : _host.Settings.Gemini.Model.Trim();

    /// <summary>
    /// Generates a structured executive summary of the given file.
    /// </summary>
    public async Task<string> SummarizeFileAsync(string filePath, CancellationToken ct = default)
    {
        var prompt = "You are a Cloud Drive AI assistant. Analyze this file thoroughly and provide a structured summary:\n" +
                     "1. **Headline**: Single-sentence high-level summary.\n" +
                     "2. **Key Takeaways**: 3 to 5 clear bullet points highlighting essential information, numbers, dates, or concepts.\n" +
                     "3. **Context & Type**: Brief evaluation of what kind of document/file this is and its primary purpose.\n" +
                     "Keep the tone professional, concise, and easy to skim.";

        return await GenerateContentForFileAsync(filePath, prompt, ct);
    }

    /// <summary>
    /// Asks a specific question about the content of the given file.
    /// </summary>
    public async Task<string> AskFileAsync(string filePath, string question, CancellationToken ct = default)
    {
        var prompt = $"You are a Cloud Drive AI assistant analyzing this file.\n" +
                     $"User question: \"{question}\"\n\n" +
                     $"Answer accurately based on the contents of the file. If the answer cannot be determined from the file, state that clearly.";

        return await GenerateContentForFileAsync(filePath, prompt, ct);
    }

    /// <summary>
    /// Suggests 3 to 7 semantic tags for the given file for classification and search.
    /// </summary>
    public async Task<IReadOnlyList<string>> SuggestTagsAsync(string filePath, CancellationToken ct = default)
    {
        var prompt = "Suggest 3 to 7 concise semantic tags (1-2 words each) for categorizing this file in cloud storage. " +
                     "Return ONLY a valid JSON array of strings, for example: [\"Invoice\", \"Finance\", \"2026\", \"Tax\"]. " +
                     "Do not include any explanation or markdown formatting around the JSON.";

        var raw = await GenerateContentForFileAsync(filePath, prompt, ct);
        try
        {
            var cleaned = raw.Trim();
            if (cleaned.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
                cleaned = cleaned[7..];
            if (cleaned.StartsWith("```", StringComparison.OrdinalIgnoreCase))
                cleaned = cleaned[3..];
            if (cleaned.EndsWith("```", StringComparison.OrdinalIgnoreCase))
                cleaned = cleaned[..^3];
            cleaned = cleaned.Trim();

            var tags = JsonSerializer.Deserialize<List<string>>(cleaned);
            if (tags != null && tags.Count > 0)
                return tags.Select(t => t.Trim().Trim('"', '\'')).Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Failed to parse JSON tags from Gemini response: {Raw}", raw);
        }

        // Fallback: split by commas or newlines if model did not return pure JSON
        return raw.Split(new[] { ',', '\n', '\r', '-' }, StringSplitOptions.RemoveEmptyEntries)
                  .Select(s => s.Trim().Trim('"', '\'', '[', ']'))
                  .Where(s => !string.IsNullOrWhiteSpace(s) && s.Length < 30)
                  .Take(7)
                  .ToList();
    }

    /// <summary>
    /// Validates an API key and model connectivity with a lightweight ping.
    /// </summary>
    public async Task<bool> TestConnectionAsync(string? overrideKey = null, string? overrideModel = null, CancellationToken ct = default)
    {
        var key = overrideKey ?? GetEffectiveApiKey();
        if (string.IsNullOrWhiteSpace(key)) return false;

        var model = string.IsNullOrWhiteSpace(overrideModel) ? Model : overrideModel.Trim();
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={key}";

        var requestBody = new
        {
            contents = new[]
            {
                new { parts = new[] { new { text = "Respond with 'OK'." } } }
            },
            generationConfig = new
            {
                temperature = 0.0,
                maxOutputTokens = 10
            }
        };

        try
        {
            using var resp = await Http.PostAsJsonAsync(url, requestBody, ct);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Gemini API test connection failed");
            return false;
        }
    }

    /// <summary>
    /// Low-level method that inspects the file, prepares multimodal parts, and calls Gemini GenerateContent.
    /// </summary>
    public async Task<string> GenerateContentForFileAsync(string filePath, string userPrompt, CancellationToken ct = default)
    {
        var key = GetEffectiveApiKey();
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("Gemini API key is not configured. Please set your API key in Settings or via GEMINI_API_KEY environment variable.");

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"File not found: {filePath}");

        var fi = new FileInfo(filePath);
        var mime = GetMimeType(filePath);
        var isText = IsTextMimeType(mime);

        var partsList = new List<object>();

        if (isText)
        {
            // For text/code/data files, read up to 150 KB
            const int maxTextBytes = 150 * 1024;
            string textContent;
            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream))
            {
                var buffer = new char[maxTextBytes];
                int read = await reader.ReadBlockAsync(buffer, 0, buffer.Length);
                textContent = new string(buffer, 0, read);
            }

            var fileContext = $"[File: {fi.Name} ({FormatSize(fi.Length)})]\nContent:\n{textContent}\n\n";
            partsList.Add(new { text = fileContext + userPrompt });
        }
        else
        {
            // For binary files (PDF, images), send as inline_data base64 if <= 12 MB
            const long maxBinaryBytes = 12 * 1024 * 1024;
            if (fi.Length > maxBinaryBytes)
                throw new InvalidOperationException($"File size ({FormatSize(fi.Length)}) exceeds 12 MB limit for inline AI analysis.");

            byte[] bytes = await File.ReadAllBytesAsync(filePath, ct);
            string base64 = Convert.ToBase64String(bytes);

            partsList.Add(new
            {
                inline_data = new
                {
                    mime_type = mime,
                    data = base64
                }
            });
            partsList.Add(new { text = $"[File: {fi.Name} ({FormatSize(fi.Length)})]\n{userPrompt}" });
        }

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{Model}:generateContent?key={key}";
        var requestObj = new
        {
            contents = new[]
            {
                new { parts = partsList }
            },
            generationConfig = new
            {
                temperature = _host.Settings.Gemini.Temperature,
                maxOutputTokens = _host.Settings.Gemini.MaxOutputTokens
            }
        };

        using var response = await Http.PostAsJsonAsync(url, requestObj, ct);
        var responseString = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _log.LogError("Gemini API call failed with HTTP {Status}: {Response}", response.StatusCode, responseString);
            throw new HttpRequestException($"Gemini API error ({response.StatusCode}): {ExtractErrorMessage(responseString)}");
        }

        return ExtractTextFromResponse(responseString);
    }

    public static string GetMimeType(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".txt" => "text/plain",
            ".md" => "text/markdown",
            ".csv" => "text/csv",
            ".json" => "application/json",
            ".xml" => "text/xml",
            ".html" or ".htm" => "text/html",
            ".css" => "text/css",
            ".js" => "text/javascript",
            ".cs" => "text/plain",
            ".py" => "text/plain",
            ".log" => "text/plain",
            ".doc" or ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xls" or ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".ppt" or ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            _ => "application/octet-stream"
        };
    }

    public static bool IsTextMimeType(string mime) =>
        mime.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ||
        mime == "application/json" ||
        mime == "application/xml" ||
        mime == "application/javascript";

    private static string ExtractTextFromResponse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("candidates", out var candidates) &&
                candidates.GetArrayLength() > 0)
            {
                var first = candidates[0];
                if (first.TryGetProperty("content", out var content) &&
                    content.TryGetProperty("parts", out var parts) &&
                    parts.GetArrayLength() > 0)
                {
                    var textSb = new System.Text.StringBuilder();
                    foreach (var part in parts.EnumerateArray())
                    {
                        if (part.TryGetProperty("text", out var t))
                            textSb.Append(t.GetString());
                    }
                    var result = textSb.ToString().Trim();
                    if (!string.IsNullOrEmpty(result)) return result;
                }
            }
        }
        catch (Exception)
        {
            // Ignore parse failures and fall through
        }
        return json;
    }

    private static string ExtractErrorMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out var err) &&
                err.TryGetProperty("message", out var msg))
            {
                return msg.GetString() ?? "Unknown error";
            }
        }
        catch { }
        return json;
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024.0):F1} MB";
    }
}
