using CloudSSD.App;
using Xunit;

namespace CloudSSD.Tests;

/// <summary>The WinFsp installer that comes with the app: found in the app's folder, and run only when it is the genuine one.</summary>
public class WinFspSetupTests
{
    /// <summary>The repo's tools\winfsp folder (the installer the release carries), or null on a machine without it.</summary>
    private static string? RepoInstaller()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var dir = Path.Combine(d.FullName, "tools", "winfsp");
            if (Directory.Exists(dir) && Directory.EnumerateFiles(dir, "winfsp*.msi").FirstOrDefault() is { } msi) return msi;
        }
        return null;
    }

    [Fact]
    public void The_installer_is_found_in_the_apps_folder_newest_first()
    {
        var app = Path.Combine(Path.GetTempPath(), "cssd_winfsp_" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Assert.Null(WinFspSetup.BundledInstaller(app));                              // an app folder without it
            var dir = Directory.CreateDirectory(Path.Combine(app, "tools", "winfsp")).FullName;
            File.WriteAllText(Path.Combine(dir, "winfsp-2.0.1.msi"), "x");
            File.WriteAllText(Path.Combine(dir, "winfsp-2.1.25156.msi"), "x");
            File.WriteAllText(Path.Combine(dir, "readme.txt"), "x");
            Assert.Equal("winfsp-2.1.25156.msi", Path.GetFileName(WinFspSetup.BundledInstaller(app)));
        }
        finally { try { Directory.Delete(app, true); } catch (IOException) { } }
    }

    [Fact]
    public void Only_the_genuine_installer_passes_the_signature_check()
    {
        var fake = Path.Combine(Path.GetTempPath(), "cssd_fake_" + Guid.NewGuid().ToString("N")[..8] + ".msi");
        File.WriteAllText(fake, "not an installer");
        try { Assert.False(WinFspSetup.IsGenuine(fake)); }
        finally { File.Delete(fake); }
        Assert.False(WinFspSetup.IsGenuine(Path.Combine(Path.GetTempPath(), "no-such-file.msi")));

        if (RepoInstaller() is not { } real) return;                                     // nothing more to check without it
        Assert.True(WinFspSetup.IsGenuine(real));
        // The same installer with one byte changed is not genuine any more.
        var bytes = File.ReadAllBytes(real);
        bytes[bytes.Length / 2] ^= 0x5A;
        var tampered = Path.Combine(Path.GetTempPath(), "cssd_tampered_" + Guid.NewGuid().ToString("N")[..8] + ".msi");
        File.WriteAllBytes(tampered, bytes);
        try { Assert.False(WinFspSetup.IsGenuine(tampered)); }
        finally { File.Delete(tampered); }
    }
}
