using System.Diagnostics;

namespace CloudSSD.App;

/// <summary>
/// Phones on this PC's hotspot, on a phone's hotspot or on a USB cable reach the PC directly, without the internet.
/// Windows usually files those networks as "Public", where its firewall may block the phone; one rule (asked for once,
/// with Windows' Yes/No prompt) lets devices on the same local network in on the bridge port, on every network type.
/// </summary>
public static class DirectLinkSetup
{
    public const string FirewallRuleName = "Cloud-SSD direct links";

    public static string FirewallArguments(int port) =>
        $"advfirewall firewall add rule name=\"{FirewallRuleName}\" dir=in action=allow protocol=TCP localport={port} remoteip=LocalSubnet profile=any";

    public static bool FirewallRuleExists()
    {
        var (code, output) = Run("netsh", $"advfirewall firewall show rule name=\"{FirewallRuleName}\"");
        return code == 0 && output.Contains(FirewallRuleName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Adds the rule as administrator (Windows asks). False when the user said No.</summary>
    public static bool AddFirewallRule(int port)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("netsh", FirewallArguments(port)) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden });
            if (p == null) return false;
            p.WaitForExit(30000);
            return p.HasExited && p.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception) { return false; }   // "No" in the prompt
    }

    /// <summary>
    /// True when a network this PC uses right now (with traffic) is filed by Windows as Public. Pairing still works
    /// there; the PC only reminds that anyone on that network can try to reach it, so pair at home, on your own
    /// hotspot or over a cable.
    /// </summary>
    public static bool OnPublicNetwork()
    {
        var (code, output) = Run("powershell",
            "-NoProfile -NonInteractive -Command \"(Get-NetConnectionProfile | Where-Object { $_.IPv4Connectivity -ne 'NoTraffic' -and $_.InterfaceAlias -notlike '*Tailscale*' }).NetworkCategory -join ','\"");
        return code == 0 && output.Contains("Public", StringComparison.OrdinalIgnoreCase);
    }

    private static (int? Code, string Output) Run(string exe, string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
            });
            if (p == null) return (null, "");
            var stdout = p.StandardOutput.ReadToEndAsync();
            var stderr = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(15000))
            {
                try { p.Kill(true); } catch (InvalidOperationException) { }
                return (null, "timed out");
            }
            return (p.ExitCode, stdout.Result + stderr.Result);
        }
        catch (System.ComponentModel.Win32Exception) { return (null, ""); }
    }
}
