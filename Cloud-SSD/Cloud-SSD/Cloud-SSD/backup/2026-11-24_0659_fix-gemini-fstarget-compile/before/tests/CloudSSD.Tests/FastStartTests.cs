using System.Text.Json.Nodes;
using CloudSSD.Cli;
using CloudSSD.Mount;

namespace CloudSSD.Tests;

/// <summary>
/// Drives right after the PC starts: which Google accounts still make rclone ask Google for their top folder at every
/// start, and reading the id out of rclone's own hint.
/// </summary>
public class FastStartTests
{
    private static JsonObject Dump(string json) => JsonNode.Parse(json)!.AsObject();

    [Fact]
    public void Only_signed_in_google_accounts_without_a_saved_id_are_listed()
    {
        var dump = Dump("""
            {
              "gdrive":   { "type": "drive", "token": "{\"access_token\":\"x\"}" },
              "gdrive2":  { "type": "drive", "token": "{\"access_token\":\"x\"}", "root_folder_id": "0AGJz-iZtL0_nUk9PVA" },
              "shared":   { "type": "drive", "token": "{\"access_token\":\"x\"}", "team_drive": "0AEd3EhGff9SaUk9PVA" },
              "halfdone": { "type": "drive" },
              "robot":    { "type": "drive", "service_account_file": "C:\\sa.json" },
              "CloudSSD": { "type": "union", "upstreams": "gdrive: gdrive2:" },
              "Accounts": { "type": "combine", "upstreams": "a=gdrive: b=gdrive2:" },
              "box":      { "type": "onedrive", "token": "x" }
            }
            """);
        Assert.Equal(["gdrive", "robot"], RcloneConfigService.DriveRemotesWithoutRootId(dump).Order());
    }

    [Fact]
    public void Nothing_is_listed_once_every_account_has_its_id()
    {
        var dump = Dump("""{ "gdrive": { "type": "drive", "token": "t", "root_folder_id": "0AGJz" }, "CloudSSD": { "type": "union", "upstreams": "gdrive:" } }""");
        Assert.Empty(RcloneConfigService.DriveRemotesWithoutRootId(dump));
        Assert.Null(Problems.SlowStart(dump));
    }

    [Theory]
    // The line rclone 1.75 really wrote on 17 Nov 2026 (the id is a made-up one of the same shape).
    [InlineData("2026/11/17 09:05:39.663737 DEBUG : Google drive root '': 'root_folder_id = 0AGJz-iZtL0_nUk9PVA' - save this in the config to speed up startup", "0AGJz-iZtL0_nUk9PVA")]
    [InlineData("first line\nDEBUG : Google drive root 'sub': 'root_folder_id = 0ALsS99v2cSZOUk9PVA' - save this\nlast line", "0ALsS99v2cSZOUk9PVA")]
    public void The_id_is_read_from_rclones_own_hint(string output, string expected) =>
        Assert.Equal(expected, RcloneConfigService.ParseRootFolderId(output));

    [Theory]
    [InlineData("")]
    [InlineData("NOTICE: gdrive: This remote uses rclone's shared Google Drive client_id")]
    [InlineData("'root_folder_id = ' - nothing")]
    [InlineData("root_folder_id = 0AGJz-iZtL0_nUk9PVA")]                 // not rclone's quoted hint
    public void Other_output_gives_no_id(string output) => Assert.Null(RcloneConfigService.ParseRootFolderId(output));

    [Fact]
    public void The_problem_page_says_what_makes_the_start_slow_and_what_to_do()
    {
        var slow = Problems.SlowStart(Dump("""{ "a": { "type": "drive", "token": "t" }, "b": { "type": "drive", "token": "t" } }"""));
        Assert.NotNull(slow);
        Assert.Equal(Problems.Level.Warn, slow!.Level);
        Assert.Contains("2 account", slow.Why);
        Assert.Contains("/faststart", slow.Do);
    }

    [Fact]
    public void Faststart_is_a_command_of_the_shell()
    {
        Assert.Equal("faststart", CommandCatalog.Find("/faststart")?.Name);
        Assert.Equal("faststart", CommandCatalog.Find("boot")?.Name);
        Assert.Contains(CommandCatalog.Suggest("/fast"), c => c.Name == "faststart");
    }
}
