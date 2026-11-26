using System.Text.Json;
using System.Text.Json.Serialization;

namespace CloudSSD.Core;

public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;
    public CacheSettings Cache { get; set; } = new();
    public NetworkSettings Network { get; set; } = new();
    public StreamingSettings Streaming { get; set; } = new();
    public List<MountDefinition> Mounts { get; set; } = new();
    /// <summary>Last known sign-in address of each account (remote name → e-mail), so names stay stable while offline.</summary>
    public Dictionary<string, string> AccountEmails { get; set; } = new();
    public string? RclonePathOverride { get; set; }
    public bool RestoreMountsOnStartup { get; set; } = true;
    // True: closing the window leaves the drives connected ("Yes" is the answer already chosen in the question the
    // app asks). A drive the user connected stays until the user disconnects it; with false, pressing Enter on that
    // question took Z: and Y: away until the app was started again.
    public bool KeepMountsAfterExit { get; set; } = true;
    public bool EncryptRcloneConfig { get; set; } = true;
    public bool FirstRunCompleted { get; set; }
    /// <summary>The person said "not now" when the app offered to install WinFsp at start: it is not offered at start again (connecting a drive still asks).</summary>
    public bool WinFspOfferDeclined { get; set; }
    public string? LocalGamesRoot { get; set; }
    public MobileBridgeSettings MobileBridge { get; set; } = new();
    public SmartOffloadSettings SmartOffload { get; set; } = new();
    public MemorySettings Memory { get; set; } = new();
    public MirrorSettings Mirror { get; set; } = new();
    public HybridDriveSettings Hybrid { get; set; } = new();
}

/// <summary>
/// "Fast drive" (hybrid): the drive letter people use is a folder on this PC. New files and folders are written there
/// at the disk's own speed and uploaded behind; the folders that are already in the cloud show up in it as links to
/// the cloud drive, which moves to another letter. Measured on 17 Nov 2026 with an 853-file install: 4 s this way,
/// 13 min written straight to the cloud drive (every new folder there is a request to Google), 71 s through an
/// rclone mount of a plain local folder.
/// </summary>
public sealed class HybridDriveSettings
{
    public bool Enabled { get; set; }
    /// <summary>The letter the fast drive shows up as.</summary>
    public string Letter { get; set; } = "Z";
    /// <summary>The letter the cloud drive behind it uses while the fast drive is on.</summary>
    public string BackingLetter { get; set; } = "X";
    /// <summary>The folder on this PC behind the letter. Empty = "Cloud-SSD\All Accounts" in the user's folder.</summary>
    public string? Root { get; set; }
    /// <summary>The cloud drive the fast drive sits in front of.</summary>
    public string MountId { get; set; } = "cloudssd";
    /// <summary>A new or changed folder is uploaded once nothing in it changed for this long.</summary>
    public int QuietSeconds { get; set; } = 20;

    [System.Text.Json.Serialization.JsonIgnore]   // worked out, not a setting: it must not be written into settings.json
    public string RootOrDefault => string.IsNullOrWhiteSpace(Root)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Cloud-SSD", "All Accounts")
        : Root;
}

/// <summary>
/// "Files when it is off": this PC keeps a copy of its own files in the cloud, so a paired device can open them while
/// the PC is switched off (docs/WHEN_OFF_PLAN.md). It runs only while a device and this PC both said yes.
/// </summary>
public sealed class MirrorSettings
{
    /// <summary>The account (an rclone remote) the copies go to. Chosen once, the one with the most free space, and kept.</summary>
    public string? Account { get; set; }
    /// <summary>
    /// Also Windows, the programs and their data. They go last, after the person's own files, and are a copy of files,
    /// not a system image: Windows cannot be started again from them.
    /// </summary>
    public bool IncludeSystem { get; set; } = true;
    /// <summary>Folders the person left out, on top of what is never copied.</summary>
    public List<string> Excluded { get; set; } = new();
    /// <summary>
    /// When not empty, only these folders are copied instead of the whole PC (a small first try, or a test). There is
    /// no switch for it on the page; it is set in settings.json.
    /// </summary>
    public List<string> OnlyFolders { get; set; } = new();
    /// <summary>Upload speed limit in megabits a second; 0 = no limit.</summary>
    public double UploadLimitMbps { get; set; }
    public bool PauseOnBattery { get; set; } = true;
    /// <summary>Paused by the person ("Pause" on the page) until they continue.</summary>
    public bool Paused { get; set; }
    /// <summary>How long a deleted or replaced file's copy stays in the cloud's trash folder.</summary>
    public int KeepTrashDays { get; set; } = 30;
}

public sealed class CacheSettings
{
    /// <summary>Root directory for rclone VFS caches. Each mount gets its own subdirectory.</summary>
    public string? CacheRoot { get; set; }

    /// <summary>Total physical disk budget for all mount caches.</summary>
    public long BudgetBytes { get; set; } = 20 * ByteSize.GiB;

    /// <summary>Free space that must always remain on the cache volume for Windows.</summary>
    public long MinFreeSpaceBytes { get; set; } = 8 * ByteSize.GiB;

    /// <summary>
    /// rclone --vfs-cache-mode. "full" downloads a file completely as soon as it is opened: good for games (the
    /// file then sits on the local disk and reads are instant), but opening a 460 MB archive meant downloading
    /// 460 MB first - measured: a 1 MiB read pulled the whole file and took 39 s, so a zip simply did not open.
    /// "writes" keeps only what is written on the disk and serves reads straight from the cloud, so an archive
    /// opens with just its file list (one request, measured 5 s for the same file and 0 MB extra).
    /// </summary>
    public string VfsCacheMode { get; set; } = "full";

    /// <summary>How long unused cached data may stay before rclone evicts it.</summary>
    public TimeSpan MaxAge { get; set; } = TimeSpan.FromDays(30);

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// rclone --buffer-size. In full cache mode rclone reads buffer-size + read-ahead past every read position.
    /// Measured: default 16M causes large speculative downloads for random game reads, so the default here is 0 and
    /// the prediction engine owns read-ahead.
    /// </summary>
    public long BufferSizeBytes { get; set; } = 0;

    public long ReadAheadBytes { get; set; } = 0;

    /// <summary>
    /// rclone --vfs-read-chunk-size: one request per chunk, and every request can hit Google Drive's quota.
    /// A small chunk keeps a small read small: Windows reads an archive's file list from the END of the file, and
    /// with a 32 MiB chunk that single probe pulled 32 MiB (measured: 21.5 s instead of 5.3 s for the same read).
    /// 4 MiB is the balanced value: a probe stays cheap and a 63 MB file still needs only 16 requests.
    /// </summary>
    public long ReadChunkSizeBytes { get; set; } = 4 * ByteSize.MiB;

    public int ReadChunkStreams { get; set; } = 0;

    public TimeSpan WriteBack { get; set; } = TimeSpan.FromSeconds(5);

    public List<string> PinnedPaths { get; set; } = new();
}

public sealed class NetworkSettings
{
    /// <summary>User-declared internet speed in megabits per second (used until a measurement exists).</summary>
    public double DeclaredDownloadMbps { get; set; } = 70;

    /// <summary>Last measured throughput (Mbps), null if never measured.</summary>
    public double? MeasuredDownloadMbps { get; set; }
    public double? MeasuredLatencyMs { get; set; }
    public DateTimeOffset? MeasuredAt { get; set; }

    /// <summary>Maximum share of bandwidth background (P2/P3) prefetch may use.</summary>
    public double BackgroundShare { get; set; } = 0.6;

    /// <summary>Optional hard rclone --bwlimit in Mbps (0 = unlimited).</summary>
    public double HardLimitMbps { get; set; } = 0;

    /// <summary>
    /// While a game or a phone stream is in front, cloud uploads are slowed to this many Mbps so they do not take
    /// the internet the game needs (0 = never slow them). Downloads keep their speed: a game may be loading from the cloud.
    /// </summary>
    public double GameUploadLimitMbps { get; set; } = 1;

    public int MaxPrefetchConcurrency { get; set; } = 4;

    /// <summary>rclone --tpslimit for provider API calls (0 = unlimited). Protects Google Drive quotas.</summary>
    public double TpsLimit { get; set; } = 10;

    public double EffectiveMbps => MeasuredDownloadMbps ?? DeclaredDownloadMbps;
}

public sealed class StreamingSettings
{
    public bool PredictionEnabled { get; set; } = true;

    /// <summary>Record per-read telemetry from rclone debug logs. Costs some CPU while active.</summary>
    public bool ReadProfilingEnabled { get; set; } = true;

    public long BlockSizeBytes { get; set; } = ByteSize.MiB;

    public double MinConfidence { get; set; } = 0.4;

    /// <summary>Upper bound of bytes the engine may prefetch speculatively in one game session.</summary>
    public long SessionPrefetchBudgetBytes { get; set; } = 4 * ByteSize.GiB;

    public TimeSpan StartupWindow { get; set; } = TimeSpan.FromSeconds(120);

    /// <summary>How far ahead (in game-progress time) learned startup data is released for prefetch during a launch.</summary>
    public TimeSpan StartupHorizon { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Predictors with accuracy below this after enough samples are disabled automatically.</summary>
    public double MinPredictorAccuracy { get; set; } = 0.25;

    public int PredictorEvaluationSamples { get; set; } = 64;
}

public sealed class MountDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string DisplayName { get; set; } = "Cloud Drive";

    /// <summary>rclone remote name without the trailing colon, e.g. "gdrive".</summary>
    public string RemoteName { get; set; } = "";

    /// <summary>Optional path inside the remote, e.g. "Games".</summary>
    public string RemotePath { get; set; } = "";

    /// <summary>Preferred drive letter, e.g. "X". Null = pick automatically.</summary>
    public string? DriveLetter { get; set; }

    public bool ReadOnly { get; set; }
    public bool AutoMount { get; set; } = true;

    public Dictionary<string, string> ExtraFlags { get; set; } = new();

    public string RemoteSpec => string.IsNullOrEmpty(RemotePath) ? $"{RemoteName}:" : $"{RemoteName}:{RemotePath}";
}

public sealed class MobileBridgeSettings
{
    public bool Enabled { get; set; } = true;
    public int Port { get; set; } = 8090;
    public string PairingToken { get; set; } = NewPairingToken();

    /// <summary>Tokens shorter than this (the 12-character ones from v0.7.0) are replaced on start; phones re-pair by QR.</summary>
    public const int MinPairingTokenLength = 32;

    /// <summary>128 random bits as 32 lowercase hex characters.</summary>
    public static string NewPairingToken() =>
        Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
    public string? UploadDirectory { get; set; }
    /// <summary>
    /// "never-mine" (default): files from my own paired devices are saved without asking, a guest's wait for an OK.
    /// "always": every device's files wait for an OK on this PC.
    /// </summary>
    public string AskBeforeReceiving { get; set; } = "never-mine";
    public string? PhotoBackupDirectory { get; set; }
    public bool AllowDriveBrowsing { get; set; } = true;
    public bool AllowStreaming { get; set; } = true;
    public bool AllowUploads { get; set; } = true;
    public bool AllowGamingHud { get; set; } = true;
    /// <summary>Lets a paired phone create, change and delete files on the cloud drives (and back up its camera folder).</summary>
    public bool AllowFileManagement { get; set; } = true;
    /// <summary>Lets a paired phone act on the PC: connect/disconnect drives, free up space, open files on its screen, restore game saves.</summary>
    public bool AllowPcControl { get; set; } = true;
    /// <summary>Lets a paired phone browse and download from this PC's own disks (C:, D:…), not only the cloud drives. Off by default.</summary>
    public bool AllowPcDisks { get; set; }
    /// <summary>Lets a paired phone reach the PC through Tailscale (away from home). Pairing itself still needs the home network.</summary>
    public bool AllowTailscale { get; set; }
    /// <summary>Refuses connections that come neither from the local network nor through Tailscale.</summary>
    public bool LocalAndTailnetOnly { get; set; } = true;
    /// <summary>When Tailscale is signed in on this PC, turn <see cref="AllowTailscale"/> on by itself (the user asked for it to be automatic).</summary>
    public bool TailscaleAuto { get; set; } = true;
    /// <summary>The firewall rule for Tailscale was asked for once (Windows shows a Yes/No prompt); never asked again by itself.</summary>
    public bool TailscaleFirewallAsked { get; set; }
    /// <summary>Lets a paired phone see where the PC's space and RAM go (Space &amp; RAM). Changing anything there also needs <see cref="AllowPcControl"/>.</summary>
    public bool AllowSpace { get; set; } = true;

    /// <summary>
    /// Whether the shared <see cref="PairingToken"/> still opens the bridge. Phones paired before device tokens keep
    /// working with it until it is turned off in Mobile Hub; null = not decided yet (decided once on start).
    /// </summary>
    public bool? LegacyTokenAccepted { get; set; }

    /// <summary>Key for signed, short-lived file links that can be handed to other apps without the token.</summary>
    public string MediaKey { get; set; } = "";

    /// <summary>A public random id for this PC, so a phone can tell it is talking to its own PC before it sends its token.</summary>
    public string PcId { get; set; } = "";

    public List<PairedDevice> PairedDevices { get; set; } = new();

    /// <summary>Shows a note on this PC when a device pairs or comes back online.</summary>
    public bool NotifyDeviceConnections { get; set; } = true;
    /// <summary>After a new device pairs, the PC asks what it may do (the defaults below are ticked).</summary>
    public bool AskOnPairing { get; set; } = true;
    /// <summary>
    /// What a newly paired device may do until the PC says otherwise (category names, see AccessCategories).
    /// Null = everything the switches allow, as before per-device permissions existed.
    /// </summary>
    public List<string>? NewDeviceDefaults { get; set; }

    /// <summary>
    /// Lets a paired phone play the game running on this PC: the phone sees the screen and hears the game, and its
    /// touches reach the emulator. Only on the home network, only with a per-phone key, only while an emulator is in front.
    /// </summary>
    public bool AllowGameStream { get; set; } = true;
    /// <summary>Devices may send files to each other through this PC (held here at most three days).</summary>
    public bool AllowDeviceToDevice { get; set; } = true;

    public GameStreamSettings GameStream { get; set; } = new();
}

/// <summary>Picture, sound and game start for "Play on phone".</summary>
public sealed class GameStreamSettings
{
    public int Width { get; set; } = 1280;
    public int Height { get; set; } = 720;
    public int Fps { get; set; } = 60;
    /// <summary>Starting bitrate; lowered by itself while the Wi-Fi cannot keep up and raised again up to this.</summary>
    public int BitrateKbps { get; set; } = 10_000;
    public int MinBitrateKbps { get; set; } = 2_000;
    /// <summary>Game sound goes to the phone. Only the emulator's own sound when Windows allows it, else everything the PC plays.</summary>
    public bool Sound { get; set; } = true;
    /// <summary>What "Play" starts on the PC; the same command Sunshine used for Free Fire.</summary>
    public string GameName { get; set; } = "Free Fire";
    public string LaunchPath { get; set; } = @"C:\Program Files\BlueStacks_nxt\HD-Player.exe";
    public string LaunchArguments { get; set; } = "--instance Nougat32 --cmd launchApp --package \"com.garena.game.ffi\"";
}

public sealed class PairedDevice
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "Android Device";
    /// <summary>The address the phone last used (it changes with the network); shown only, never used to let it in.</summary>
    public string IpAddress { get; set; } = "";
    public DateTimeOffset PairedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset LastSeen { get; set; } = DateTimeOffset.Now;
    public bool IsBlocked { get; set; }
    /// <summary>SHA-256 (hex) of this phone's own token. The token itself is never stored on the PC.</summary>
    public string? TokenHash { get; set; }
    /// <summary>A random id the phone app keeps for its install, so re-pairing updates this entry instead of adding one.</summary>
    public string? InstallId { get; set; }

    /// <summary>phone | tablet | laptop | desktop | browser. Null for devices paired before v0.8 (shown as a phone).</summary>
    public string? Kind { get; set; }
    /// <summary>"Android 14", "Windows 11"… as the device reported it; shown only.</summary>
    public string? Platform { get; set; }
    /// <summary>Version of the Cloud-SSD app on the device, as it reported it.</summary>
    public string? AppVersion { get; set; }
    /// <summary>How it last reached this PC: wifi | lan | hotspot | usb | internet.</summary>
    public string? LastLink { get; set; }

    /// <summary>Bytes this PC sent to the device (downloads, playing) and received from it (uploads), all time.</summary>
    public long BytesToDevice { get; set; }
    public long BytesFromDevice { get; set; }
    /// <summary>The name was changed on this PC: the device pairing again does not overwrite it.</summary>
    public bool NameSetOnPc { get; set; }
    /// <summary>
    /// "Files when it is off": this PC said yes for the device / the device said yes for this PC / since when both do.
    /// Off until both say yes; never part of "allow everything", never for a guest.
    /// </summary>
    public bool WhenOffPc { get; set; }
    public bool WhenOffDevice { get; set; }
    public DateTimeOffset? WhenOffSince { get; set; }
    /// <summary>The device is itself a Cloud-SSD PC (a laptop): its own bridge id, name and port, for pairing back later.</summary>
    public string? PeerPcId { get; set; }
    public string? PeerName { get; set; }
    public int? PeerPort { get; set; }
    /// <summary>
    /// What this device may do, on top of the PC-wide switches (a switch that is off wins). Null for a device paired
    /// before per-device permissions: it may do everything the switches allow, as it always could.
    /// </summary>
    public List<AccessGrant>? Grants { get; set; }
    /// <summary>A guest: paired with a one-time guest code, it may do only what the code allowed and is removed at this time.</summary>
    public DateTimeOffset? GuestUntil { get; set; }
}

/// <summary>One thing a device may do: always, until a time, or until the Cloud-SSD app on the PC restarts.</summary>
public sealed class AccessGrant
{
    public string Category { get; set; } = "";
    /// <summary>always | timed | session</summary>
    public string Mode { get; set; } = "always";
    public DateTimeOffset? ExpiresAt { get; set; }
    /// <summary>For "session": the run of the app it was given in.</summary>
    public string? Session { get; set; }
    public DateTimeOffset GrantedAt { get; set; } = DateTimeOffset.Now;
}

public sealed class SmartOffloadSettings
{
    public bool Enabled { get; set; } = true;
    public double MinFreeSpaceThresholdGB { get; set; } = 50.0;
    public string TargetMountId { get; set; } = "cloudssd15tb";
    public string TargetSubfolder { get; set; } = "Offloaded_Files";
    public bool OffloadUploadsFolder { get; set; } = true;
    /// <summary>
    /// Also move old big files that lie directly in the Downloads folder (never its subfolders). Off unless the
    /// user turns it on, because it takes the user's own files off the PC.
    /// </summary>
    public bool OffloadDownloadsFolder { get; set; }
    /// <summary>Below the limit, first delete temporary files nobody has touched for a week (what Windows' Disk Cleanup removes).</summary>
    public bool CleanTempFiles { get; set; } = true;
    /// <summary>
    /// Below the limit, also remove Recycle Bin items deleted more than 30 days ago. Off unless the user turns it
    /// on: these were the user's own files.
    /// </summary>
    public bool CleanRecycleBin { get; set; }
    public bool EvictVfsCacheOnLowSpace { get; set; } = true;
    public DateTimeOffset? LastOffloadTime { get; set; }
    public long TotalBytesOffloaded { get; set; }
}

public sealed class MemorySettings
{
    /// <summary>PC Memory auto mode: when RAM runs low, idle background apps are moved out of RAM.</summary>
    public bool AutoRelief { get; set; } = true;

    /// <summary>Game mode: when a game or a phone stream starts, RAM is freed at once and the apps in <see cref="CloseForGame"/> are asked to close.</summary>
    public bool GameMode { get; set; } = true;

    /// <summary>Process names (no ".exe") of the apps the user chose to have closed while a game runs. Empty until the user picks some.</summary>
    public List<string> CloseForGame { get; set; } = new();

    /// <summary>Open the apps game mode closed again when the game is over.</summary>
    public bool ReopenAfterGame { get; set; } = true;
}

public interface ISettingsStore
{
    AppSettings Load();
    void Save(AppSettings settings);
}

public sealed class JsonSettingsStore : ISettingsStore
{
    public static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly object _gate = new();

    public JsonSettingsStore(string path) => Path = path;
    public string Path { get; }

    public AppSettings Load()
    {
        lock (_gate)
        {
            if (!File.Exists(Path)) return new AppSettings();
            try
            {
                var json = File.ReadAllText(Path);
                var s = JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
                // The pairing token and the media key are stored protected (DPAPI); open them here so the
                // rest of the app works with the real values.
                s.MobileBridge.PairingToken = SecretProtector.Unprotect(s.MobileBridge.PairingToken);
                s.MobileBridge.MediaKey = SecretProtector.Unprotect(s.MobileBridge.MediaKey);
                return s;
            }
            catch (JsonException)
            {
                // Keep the damaged file for diagnostics and start from defaults.
                File.Copy(Path, Path + ".corrupt-" + DateTime.Now.ToString("yyyyMMddHHmmss"), true);
                return new AppSettings();
            }
        }
    }

    public void Save(AppSettings settings)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            var tmp = Path + ".tmp";
            // Write the secrets protected, but keep the live settings object untouched (the app needs the
            // plain values).
            var token = settings.MobileBridge.PairingToken;
            var media = settings.MobileBridge.MediaKey;
            try
            {
                settings.MobileBridge.PairingToken = SecretProtector.Protect(token);
                settings.MobileBridge.MediaKey = SecretProtector.Protect(media);
                File.WriteAllText(tmp, JsonSerializer.Serialize(settings, Options));
            }
            finally
            {
                settings.MobileBridge.PairingToken = token;
                settings.MobileBridge.MediaKey = media;
            }
            if (File.Exists(Path)) File.Replace(tmp, Path, null);
            else File.Move(tmp, Path);
        }
    }
}
