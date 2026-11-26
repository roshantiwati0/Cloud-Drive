using System.IO.Compression;
using CloudSSD.Desktop;
using CloudSSD.Games;
using CloudSSD.Mount;
using CloudSSD.Providers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CloudSSD.Tests;

[Collection("Language")]   // switches the app's language; never at the same time as other language tests
public class NewFeaturesTests
{
    [Fact]
    public void ProviderRegistry_Includes_OneDrive_And_WebDav_As_Supported()
    {
        var oneDrive = ProviderRegistry.All.FirstOrDefault(p => p.Id == "onedrive");
        Assert.NotNull(oneDrive);
        Assert.Equal(SupportLevel.Supported, oneDrive.Support);
        Assert.Equal("onedrive", oneDrive.RcloneBackend);

        var webDav = ProviderRegistry.All.FirstOrDefault(p => p.Id == "webdav");
        Assert.NotNull(webDav);
        Assert.Equal(SupportLevel.Supported, webDav.Support);
        Assert.Equal("webdav", webDav.RcloneBackend);
    }

    [Fact]
    public async Task WebDav_Request_Validates_Url_And_User()
    {
        var connector = new WebDavConnector(null!, NullLogger<WebDavConnector>.Instance);
        var badUrlReq = new WebDavConnector.Request("test", "not-a-url", "nextcloud", "user", "pass");
        var badUserReq = new WebDavConnector.Request("test", "https://cloud.com/dav", "nextcloud", "", "pass");

        await Assert.ThrowsAsync<ArgumentException>(() => connector.ConnectAsync(null!, badUrlReq, null, default));
        await Assert.ThrowsAsync<ArgumentException>(() => connector.ConnectAsync(null!, badUserReq, null, default));
    }

    [Fact]
    public async Task SaveBackupService_Creates_Zip_And_Restores_With_Safety_Snapshot()
    {
        var tempAppDir = Path.Combine(Path.GetTempPath(), "cpx_test_backup_" + Guid.NewGuid().ToString("N"));
        var tempSaveDir = Path.Combine(Path.GetTempPath(), "cpx_test_save_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempSaveDir);
            File.WriteAllText(Path.Combine(tempSaveDir, "progress.sav"), "GAME_SAVE_DATA_LEVEL_10");

            var service = new SaveBackupService(tempAppDir, NullLogger<SaveBackupService>.Instance);
            var game = new GameEntry
            {
                Id = "testgame",
                Name = "Test Game",
                CustomSavePath = tempSaveDir,
                CloudSaveSyncEnabled = true
            };

            // Test Backup
            var backupRes = await service.BackupAsync(game, null, null, null);
            Assert.True(backupRes.Success);
            Assert.NotNull(backupRes.BackupFile);
            Assert.True(File.Exists(backupRes.BackupFile));
            Assert.Equal(1, backupRes.FileCount);

            // Change local save to simulate newer/corrupted state
            File.WriteAllText(Path.Combine(tempSaveDir, "progress.sav"), "CORRUPTED_OR_NEW_DATA");

            // Test Restore
            var restoreRes = await service.RestoreAsync(game, null, null, null, backupRes.BackupFile);
            Assert.True(restoreRes.Success);
            Assert.NotNull(restoreRes.SafetyBackup);
            Assert.True(File.Exists(restoreRes.SafetyBackup));

            // Verify content restored to original backup state
            var restoredContent = File.ReadAllText(Path.Combine(tempSaveDir, "progress.sav"));
            Assert.Equal("GAME_SAVE_DATA_LEVEL_10", restoredContent);
        }
        finally
        {
            try { Directory.Delete(tempAppDir, true); } catch { }
            try { Directory.Delete(tempSaveDir, true); } catch { }
        }
    }

    [Fact]
    public void LocalizationManager_Translates_Hinglish_And_Hindi()
    {
        // TranslateTo, not SetLanguage: SetLanguage saves the choice in the user's own language.txt, and a test run
        // must not change the language of the app installed on this PC.
        Assert.Equal("Dashboard (Home)", LocalizationManager.TranslateTo("Hinglish", "Dashboard"));
        Assert.Equal("Cloud Drives (Storage)", LocalizationManager.TranslateTo("Hinglish", "Cloud Drives"));
        Assert.Equal("Cloud Account Jodein", LocalizationManager.TranslateTo("Hinglish", "Connect Cloud Account"));

        Assert.Equal("डैशबोर्ड", LocalizationManager.TranslateTo("हिन्दी (Hindi)", "Dashboard"));
        Assert.Equal("क्लाउड ड्राइव्स", LocalizationManager.TranslateTo("हिन्दी (Hindi)", "Cloud Drives"));
        Assert.Equal("क्लाउड खाता जोड़ें", LocalizationManager.TranslateTo("हिन्दी (Hindi)", "Connect Cloud Account"));
    }
}
