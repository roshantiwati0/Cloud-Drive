using CloudSSD.App;
using CloudSSD.App.Gemini;
using CloudSSD.Cli;
using CloudSSD.Core;
using Xunit;

namespace CloudSSD.Tests;

public class GeminiServiceTests
{
    [Fact]
    public void GeminiSettings_defaults_are_correct()
    {
        var settings = new GeminiSettings();
        Assert.True(settings.Enabled);
        Assert.Equal("gemini-4-hydrogen", settings.Model);
        Assert.Equal(0.2, settings.Temperature);
        Assert.Equal(2048, settings.MaxOutputTokens);
        Assert.Null(settings.ApiKey);
    }

    [Theory]
    [InlineData("document.pdf", "application/pdf")]
    [InlineData("photo.png", "image/png")]
    [InlineData("photo.jpg", "image/jpeg")]
    [InlineData("photo.jpeg", "image/jpeg")]
    [InlineData("photo.webp", "image/webp")]
    [InlineData("notes.txt", "text/plain")]
    [InlineData("readme.md", "text/markdown")]
    [InlineData("data.json", "application/json")]
    [InlineData("table.csv", "text/csv")]
    [InlineData("Program.cs", "text/plain")]
    public void GeminiService_GetMimeType_detects_expected_types(string filename, string expectedMime)
    {
        var mime = GeminiService.GetMimeType(filename);
        Assert.Equal(expectedMime, mime);
    }

    [Theory]
    [InlineData("text/plain", true)]
    [InlineData("text/markdown", true)]
    [InlineData("application/json", true)]
    [InlineData("application/xml", true)]
    [InlineData("image/png", false)]
    [InlineData("image/jpeg", false)]
    [InlineData("application/pdf", false)]
    [InlineData("application/octet-stream", false)]
    public void GeminiService_IsTextMimeType_evaluates_correctly(string mime, bool expectedIsText)
    {
        Assert.Equal(expectedIsText, GeminiService.IsTextMimeType(mime));
    }

    [Fact]
    public void Gemini_CLI_CommandCatalog_is_registered()
    {
        var cmd = CommandCatalog.Find("gemini");
        Assert.NotNull(cmd);
        Assert.Equal("gemini", cmd.Name);
        Assert.Contains("ai", cmd.Aliases);
        Assert.Equal("Gemini AI", cmd.Title);

        // Also ensure alias 'ai' finds it
        var aliasCmd = CommandCatalog.Find("ai");
        Assert.NotNull(aliasCmd);
        Assert.Equal("gemini", aliasCmd.Name);
    }

    [Fact]
    public void CliRouteAllowed_permits_gemini_endpoints()
    {
        Assert.True(LocalMobileBridgeService.CliRouteAllowed("/api/gemini/summarize"));
        Assert.True(LocalMobileBridgeService.CliRouteAllowed("/api/gemini/status"));
        Assert.True(LocalMobileBridgeService.CliRouteAllowed("/api/gemini/ask"));
        Assert.True(LocalMobileBridgeService.CliRouteAllowed("/api/gemini/tags"));
        Assert.True(LocalMobileBridgeService.CliRouteAllowed("/api/gemini/test"));
    }
}
