namespace CloudSSD.Core;

/// <summary>
/// File-system locations used by Cloud-SSD. The data root defaults to %LOCALAPPDATA%\Cloud-SSD (or %LOCALAPPDATA%\CloudPlayX if it exists)
/// and can be redirected with the CLOUD_SSD_HOME or CLOUDPLAYX_HOME environment variable (portable mode, tests).
/// </summary>
public sealed class AppPaths
{
    public AppPaths(string? dataRoot = null)
    {
        DataRoot = dataRoot
                   ?? Environment.GetEnvironmentVariable("CLOUD_SSD_HOME")
                   ?? Environment.GetEnvironmentVariable("CLOUDPLAYX_HOME")
                   ?? ResolveDefaultDataRoot();
        Directory.CreateDirectory(DataRoot);
        Directory.CreateDirectory(LogDirectory);
    }

    private static string ResolveDefaultDataRoot()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var ssdPath = Path.Combine(localAppData, "Cloud-SSD");
        var legacyPath = Path.Combine(localAppData, "CloudPlayX");
        if (Directory.Exists(legacyPath) && !Directory.Exists(ssdPath))
        {
            return legacyPath;
        }
        return ssdPath;
    }

    public string DataRoot { get; }
    public string SettingsFile => Path.Combine(DataRoot, "settings.json");
    public string DatabaseFile => File.Exists(Path.Combine(DataRoot, "cloudssd.db"))
        ? Path.Combine(DataRoot, "cloudssd.db")
        : (File.Exists(Path.Combine(DataRoot, "cloudplayx.db")) ? Path.Combine(DataRoot, "cloudplayx.db") : Path.Combine(DataRoot, "cloudssd.db"));
    public string LogDirectory => Path.Combine(DataRoot, "logs");
    public string RcloneConfigFile => Path.Combine(DataRoot, "rclone.conf");
    public string ConfigPasswordFile => Path.Combine(DataRoot, "rclone.conf.key");
    public string MountStateFile => Path.Combine(DataRoot, "mounts.state.json");
    public string DefaultCacheRoot => Path.Combine(DataRoot, "cache");
    public string DefaultLocalGamesRoot => Path.Combine(DataRoot, "local-games");
    public string DiagnosticsDirectory => Path.Combine(DataRoot, "diagnostics");
    /// <summary>Other PCs this one connects to as a laptop: their keys (DPAPI), saved lists, thumbnails and "keep offline" copies.</summary>
    public string RemoteDirectory => Path.Combine(DataRoot, "remote");
    /// <summary>Who opened what on this PC, per device (kept 30 days).</summary>
    public string AccessLogDirectory => Path.Combine(DataRoot, "access-log");
    /// <summary>What devices deleted on this PC, so it can be found and restored (kept 60 days).</summary>
    public string DeleteLogFile => Path.Combine(DataRoot, "device-deletes.json");
    /// <summary>Files offered between devices, share links, and files passing through for another device.</summary>
    public string SharesDirectory => Path.Combine(DataRoot, "shares");

    public string CacheRootFor(AppSettings s) => string.IsNullOrWhiteSpace(s.Cache.CacheRoot) ? DefaultCacheRoot : s.Cache.CacheRoot!;

    public string MountCacheDir(AppSettings s, string mountId) => Path.Combine(CacheRootFor(s), "m-" + mountId);

    public string LocalGamesRootFor(AppSettings s) => string.IsNullOrWhiteSpace(s.LocalGamesRoot) ? DefaultLocalGamesRoot : s.LocalGamesRoot!;

    /// <summary>Directory containing the running executables (bundled rclone lives under tools\rclone).</summary>
    public static string AppDirectory => AppContext.BaseDirectory;
}
