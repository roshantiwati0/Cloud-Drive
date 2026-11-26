using CloudSSD.Core;
using CloudSSD.Mount;

namespace CloudSSD.Tests;

/// <summary>
/// "Connected once, connected for good": the accounts file (rclone.conf) is written by several rclone processes with
/// no lock between them, and was once found empty right after the PC woke up. The app keeps the last good copy and
/// puts it back, so an account ends only when the user disconnects it.
/// </summary>
public class ConfigGuardTests
{
    private static string NewDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cssd-guard-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void A_missing_empty_or_blank_accounts_file_counts_as_lost_and_one_with_logins_does_not()
    {
        var dir = NewDir();
        try
        {
            var conf = Path.Combine(dir, "rclone.conf");
            Assert.True(ConfigGuard.IsLost(conf)); // not there

            File.WriteAllBytes(conf, []);
            Assert.True(ConfigGuard.IsLost(conf)); // what rclone left behind on 13 Nov 2026: 0 bytes

            File.WriteAllText(conf, "\r\n  \r\n");
            Assert.True(ConfigGuard.IsLost(conf));

            File.WriteAllText(conf, "[gdrive]\ntype = drive\n");
            Assert.False(ConfigGuard.IsLost(conf));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void An_empty_accounts_file_gets_the_last_good_copy_back()
    {
        var dir = NewDir();
        try
        {
            var conf = Path.Combine(dir, "rclone.conf");
            var good = conf + ".good";
            File.WriteAllText(good, "[gdrive]\ntype = drive\ntoken = t1\n");
            File.WriteAllBytes(conf, []);

            Assert.True(ConfigGuard.Restore(conf, good, onlyIfLost: true));

            Assert.Equal("[gdrive]\ntype = drive\ntoken = t1\n", File.ReadAllText(conf));
            Assert.True(File.Exists(good)); // the copy stays for the next time
            Assert.Empty(Directory.GetFiles(dir, "rclone.conf.lost-*")); // an empty file is not worth keeping
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void A_healthy_accounts_file_is_never_replaced_by_the_copy()
    {
        var dir = NewDir();
        try
        {
            var conf = Path.Combine(dir, "rclone.conf");
            var good = conf + ".good";
            File.WriteAllText(good, "[gdrive]\ntype = drive\ntoken = old\n");
            File.WriteAllText(conf, "[gdrive]\ntype = drive\ntoken = new\n[gdrive2]\ntype = drive\n");

            Assert.False(ConfigGuard.Restore(conf, good, onlyIfLost: true));

            Assert.Contains("gdrive2", File.ReadAllText(conf)); // the account added after the copy is still there
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void An_unreadable_accounts_file_is_kept_aside_when_the_copy_is_put_back()
    {
        var dir = NewDir();
        try
        {
            var conf = Path.Combine(dir, "rclone.conf");
            var good = conf + ".good";
            File.WriteAllText(good, "[gdrive]\ntype = drive\n");
            File.WriteAllText(conf, "# Encrypted rclone configuration File\n\nRCLONE_ENCRYPT_V0:\nhalf-written");

            Assert.True(ConfigGuard.Restore(conf, good, onlyIfLost: false));

            Assert.Equal("[gdrive]\ntype = drive\n", File.ReadAllText(conf));
            var aside = Assert.Single(Directory.GetFiles(dir, "rclone.conf.lost-*")); // nothing is thrown away
            Assert.Contains("half-written", File.ReadAllText(aside));
            // Same content as the copy: nothing to do the second time.
            Assert.False(ConfigGuard.Restore(conf, good, onlyIfLost: false));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Without_a_usable_copy_nothing_is_touched()
    {
        var dir = NewDir();
        try
        {
            var conf = Path.Combine(dir, "rclone.conf");
            var good = conf + ".good";
            File.WriteAllBytes(conf, []);

            Assert.False(ConfigGuard.Restore(conf, good, onlyIfLost: true)); // no copy at all
            File.WriteAllBytes(good, []);
            Assert.False(ConfigGuard.Restore(conf, good, onlyIfLost: true)); // an empty copy is no copy

            Assert.Equal(0, new FileInfo(conf).Length);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Closing_the_window_keeps_the_drives_unless_the_user_chose_otherwise()
    {
        // The close question's ready-chosen answer follows this setting: "Yes, keep the drives".
        Assert.True(new AppSettings().KeepMountsAfterExit);
        Assert.True(new AppSettings().RestoreMountsOnStartup);
    }
}
