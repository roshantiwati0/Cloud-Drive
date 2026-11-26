using System.Windows;
using CloudSSD.App;

namespace CloudSSD.Desktop;

/// <summary>
/// "Cloud drives need WinFsp, and it is not on this PC": the app offers to install it from the installer it carries
/// (<see cref="WinFspSetup"/>), then connects the drives. Asked once when the app starts (not again after a No, until
/// the person connects a drive themselves) and whenever a drive is to be connected while WinFsp is missing.
/// </summary>
public static class WinFspOffer
{
    private static bool _asking;

    private static string T(string s) => LocalizationManager.T(s);

    /// <summary>
    /// True when WinFsp is there (now, or after installing it). <paramref name="evenAfterNo"/>: the person asked for a
    /// drive, so the earlier "not now" does not count.
    /// </summary>
    public static async Task<bool> EnsureAsync(AppHost host, bool evenAfterNo)
    {
        if (WinFspSetup.Installed) return true;
        if (_asking) return false;
        var owner = Application.Current?.MainWindow;
        if (WinFspSetup.BundledInstaller() == null)
        {
            // An older or hand-copied app folder without the installer: say where to get it.
            if (evenAfterNo && owner != null)
                MessageBox.Show(owner, T("Cloud drives need WinFsp, and it is not installed on this PC. Get it from https://winfsp.dev/rel/ and start Cloud-SSD again."),
                    "Cloud-SSD", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        if (!evenAfterNo && host.Settings.WinFspOfferDeclined) return false;
        _asking = true;
        try
        {
            var answer = owner != null
                ? MessageBox.Show(owner, T("Cloud drives (Z:, Y:) need WinFsp, a small Windows driver, and it is not installed on this PC.\n\nInstall it now? It comes with Cloud-SSD; Windows asks for your permission once."),
                    "Cloud-SSD", MessageBoxButton.YesNo, MessageBoxImage.Question)
                : MessageBoxResult.No;
            if (answer != MessageBoxResult.Yes)
            {
                host.Settings.WinFspOfferDeclined = true;
                host.SaveSettings();
                return false;
            }
            var result = await WinFspSetup.InstallAsync();
            if (result.Installed)
            {
                host.Settings.WinFspOfferDeclined = false;
                host.SaveSettings();
                host.RefreshDependencies();
                return true;
            }
            if (owner != null)
                MessageBox.Show(owner, result.Outcome switch
                {
                    "declined" => T("WinFsp was not installed: Windows' question was answered with No. Cloud drives stay off until it is installed."),
                    "not-genuine" => T("The WinFsp installer that came with Cloud-SSD is not the genuine one, so it was not run. Install Cloud-SSD again from its original download."),
                    _ => T("WinFsp could not be installed. Try again, or get it from https://winfsp.dev/rel/")
                }, "Cloud-SSD", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        finally { _asking = false; }
    }

    /// <summary>At the app's start: offer it when it is missing and, once it is there, connect the drives that connect by themselves.</summary>
    public static async Task AtStartAsync(AppHost host)
    {
        if (WinFspSetup.Installed) return;
        if (await EnsureAsync(host, evenAfterNo: false)) await host.RestoreMountsAsync();
    }
}
