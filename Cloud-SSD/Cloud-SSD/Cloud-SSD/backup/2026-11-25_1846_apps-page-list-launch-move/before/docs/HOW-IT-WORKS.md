# Cloud-SSD — How It Works (real code, real numbers)

Yeh document **sirf wahi batata hai jo code me hai**. Har flow ke saath:
- kaunsa file/line se pata chalta hai (`path:line`)
- real command, real HTTP route, ya real measured number
- aur ek honest line — kya *nahi* hota, ya kahan exception hai

Agar koi claim is document me nahi hai, to wo README me bhi nahi hona chahiye.

Codebase root: `Cloud-SSD/Cloud-SSD/Cloud-SSD` (relative to repo root).

---

## Flow 1 — App khola, ek email login kiya: pura system kaise uthta hai

**Ek line me:** Cloud-SSD khud OAuth nahi karta. Wo ek private `rclone rcd` daemon loopback par chalata hai, uske rc API se Google Drive remote banata hai, aur **rclone** browser me Google ka consent page kholta hai. App password kabhi nahi dekhta.

### 1.1 Button se daemon tak

```
[Cloud Drives page → "Connect Cloud Account"]
   src/CloudSSD.Desktop/Views/CloudDrivesView.xaml:7        (ConnectAccountCommand)
   src/CloudSSD.Desktop/ViewModels/CloudDrivesViewModel.cs:128-135
        ↓
[ConnectDriveWindow → "Quick connect" / "Connect"]
   src/CloudSSD.Desktop/Views/ConnectDriveWindow.xaml.cs:116-124
        ↓
[GoogleDriveConnector.ConnectAsync]
   src/CloudSSD.Mount/RcloneConfigService.cs:314
        ↓
[rclone rcd --rc-addr 127.0.0.1:<random port>]
   src/CloudSSD.Mount/RcloneConfigService.cs:541-548
        ↓
[HTTP POST config/create  {name:"gdrive", type:"drive", parameters:{scope:"drive"}}]
   src/CloudSSD.Mount/RcloneConfigService.cs:332-338
        ↓
[AnswerQuestionsAsync → config_is_local = "true"]
   src/CloudSSD.Mount/RcloneConfigService.cs:508-537
        ↓
[rclone khud browser kholta hai → Google consent → rclone ke redirect listener par token]
        ↓
[operations/about → quota verify]
   src/CloudSSD.Mount/RcloneConfigService.cs:347-350
```

### 1.2 Point by point

**Daemon sirf loopback par.** `src/CloudSSD.Mount/RcloneConfigService.cs:541-548`:
```csharp
var port = RcClient.FindFreeLoopbackPort();
var user = SecretStore.RandomToken(12);
var pass = SecretStore.RandomToken(24);
var env = new Dictionary<string, string?>(_config.BaseEnvironment()) { ["RCLONE_RC_USER"] = user, ["RCLONE_RC_PASS"] = pass };
var psi = new RcloneRunner(rclone.Path, env).CreateStartInfo(["rcd", "--rc-addr", $"127.0.0.1:{port}"]);
```
Random port + random user/pass, sirf `127.0.0.1`. Koi TCP daemon Internet par nahi khulta.

**Remote ka naam.** Email naam nahi hota. Ek plain counter hota hai — `src/CloudSSD.Desktop/Views/ConnectDriveWindow.xaml.cs:34-39`:
```csharp
private string NextRemoteName(string prefix)
{
    var name = prefix;
    for (int i = 2; _host.Remotes.Any(r => r.Name == name); i++) name = prefix + i;
    return name;
}
```
Isliye accounts `gdrive`, `gdrive2`, `gdrive3` hote hain. Email baad me pata chalta hai.

**Scope.** Read-only sign-in par `drive.readonly`, warna `drive` — `RcloneConfigService.cs:323`.

**Aadha ban hua account kabhi nahi chhoda jaata.** Cancel/timeout/refusal par remote delete — `RcloneConfigService.cs:352-359`:
```csharp
catch (Exception e)
{
    // Cancelled, timed out or refused: never leave an account behind that has no working login. Such an
    // entry would be picked up as an account and break the drives that join all accounts.
    try { await rc.CallAsync("config/delete", new { name = req.RemoteName }, CancellationToken.None, TimeSpan.FromSeconds(10)); } catch (Exception) { }
```

**Token kahan gaya.** App token khud nahi likhta. `RCLONE_CONFIG` env var se rclone ko apna config file dikhaya jaata hai — `src/CloudSSD.Core/AppPaths.cs:37`:
```
%LOCALAPPDATA%\Cloud-SSD\rclone.conf
```

**Email kaise pata chalta hai.** Sign-in ke baad app rclone ke config se access token uthata hai aur Google ko khud puchta hai — `RcloneConfigService.cs:458-470`:
```csharp
var cfg = await rc.CallAsync("config/get", new { name = remote }, ct);
var token = JsonNode.Parse(cfg["token"]?.GetValue<string>() ?? "")?["access_token"]?.GetValue<string>();
...
using var req = new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/drive/v3/about?fields=user(displayName,emailAddress)");
req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
```
Result `settings.json` me `AccountEmails` me cache hota hai (`AppHost.cs:252-258`) taaki offline me bhi drive ka naam wahi dikhe.

### 1.3 Honest notes

- **PKCE nahi hai, `rclone config authorize` nahi hai.** `src/` me authorize URL banane wala koi code nahi hai. Poora OAuth rclone ka apna flow hai; app sirf uska *driver* hai.
- **rclone ka shared OAuth client 2026 me retire ho raha hai.** Isi liye app apna key use karne ko kehta hai — `RcloneConfigService.cs:97-101`, aur UI me "How do I get a key?" → "Switch to my own Google key" (`CloudDrivesViewModel.cs:273-277`).
- `127.0.0.1:53682` redirect port code me sirf ek **comment** me likha hai (`RcloneConfigService.cs:291`) — wo rclone ka internal behaviour hai, is repo se verify nahi hota.

---

## Flow 2 — Doosra email login kiya: kya hota hai

**Ek line me:** Naya remote banta hai, dono generated drives dobara likhi jaati hain, aur **Z: aur Y: dono disconnect hokar wapas mount hote hain** — kyunki running mount ko nayi account list nahi milti.

### 2.1 Step by step

**(1) Naya remote banega** — `gdrive2`. Purana mount abhi chalta rahega, kuch nahi hota.

**(2) `SyncStandardDrivesAsync()` chalta hai** — `src/CloudSSD.Desktop/ViewModels/CloudDrivesViewModel.cs:137-151`:
```csharp
// No questions after sign-in: the account simply joins the two standard drives ("Each Account" shows every
// account on its own, "All Accounts" joins them). Earlier accounts are always kept.
Say("Signed in. Setting up your drives…", "accent");
await RunAsync(async () =>
{
    await Host.SyncStandardDrivesAsync();
    if (Host.Remotes.All(r => r.Name != w.RemoteName))
    {
        Say("This account was already connected, so nothing was added. To add another one, choose “Use another account” on Google's page.", "warn");
        return;
    }
```

**(3) Same Gmail dobara? Delete.** `src/CloudSSD.App/AppHost.cs:311-319`:
```csharp
// Every real login once; of two sign-ins of the same Gmail the writable one stays.
var accounts = logins.GroupBy(r => AccountName(r.Name), StringComparer.OrdinalIgnoreCase)
    .Select(g => g.OrderBy(r => readOnly.Contains(r.Name)).First()).ToList();
foreach (var twin in logins.Except(accounts))
{
    _log.LogInformation("Removing second sign-in {Remote} of an account that is already connected", twin.Name);
    await RcloneConfig.DeleteRemoteAsync(rclone, twin.Name, ct);
}
```
Matlab: **doosra account add karne ke liye Google page par "Use another account" dabana zaroori hai.** Warna app naya remote delete kar deta hai aur warning dikhata hai.

**(4) Dono generated remotes rewrite.** `AppHost.cs:345-357`:
```csharp
var combined = CombinedRemoteSpec(accounts.Select(r => r.Name).ToList(), readOnly);
var combineUpstreams = string.Join(' ', accounts.Select(r => $"{FolderName(AccountName(r.Name))}={r.Name}:"));
...
if (unionChanged)
    await RcloneConfig.WriteRemoteAsync(rclone, CombinedRemote, combined.Type,
        [(combined.Key, combined.Value), ("action_policy", "epmfs"), ("create_policy", "mfs"), ("search_policy", "ff")], ct);
if (combineChanged)
    await RcloneConfig.WriteRemoteAsync(rclone, SingleRemote, "combine", [("upstreams", combineUpstreams)], ct);
```
`WriteRemoteAsync` delete-then-create karta hai (`src/CloudSSD.Mount/RcloneConfigService.cs:167-175`), to purana option bacha nahi rehta.

Jo config likhi jaati hai, wo exactly ye hai:

```
[CloudSSD]
type = union
upstreams = gdrive: gdrive2:
action_policy = epmfs     # file edit/delete usi account me jata hai jahan file hai
create_policy = mfs       # nayi file jis account me sabse zyada space ho wahan
search_policy = ff        # read me pehla mila wala

[Accounts]
type = combine
upstreams = user@gmail.com=gdrive: other@gmail.com=gdrive2:
```

**(5) Dono drives remount.** `AppHost.cs:366-382`:
```csharp
foreach (var (def, changed) in new[] { (each, combineChanged), (all, unionChanged) })
{
    // A running mount keeps the account list it started with, so it reconnects when that list changed.
    if (changed && Mounts.Get(def.Id) != null) await UnmountAsync(def.Id);
    if (Mounts.Get(def.Id) == null)
    {
        var s = await MountAsync(def, ct);
        def.DriveLetter = s.DriveLetter.ToString();
    }
}
```
Jab tak drive wapas nahi aata, wait 120 s (`src/CloudSSD.Mount/MountService.cs:118`: `DriveAppearTimeout = TimeSpan.FromSeconds(120)`).

### 2.2 User ko disk par kya dikhta hai

| | Login 1 ke baad | Login 2 ke baad |
|---|---|---|
| **Z: (All Accounts)** | union = sirf `gdrive:` | Z: disconnect → wapas mount → union = `gdrive: gdrive2:`. Root par dono accounts ka mila-jula content (naye account ki files bhi root par, folder me nahi). |
| **Y: (Each Account)** | combine = 1 folder | Y: disconnect → wapas mount → 2 top-level folders, naam **Gmail address** ke hisaab se. |
| **Files** | — | **Kuch move nahi hota.** File wahi rehti hai jis account me thi. Sirf nayi writes sabse khali account me jaati hain (`create_policy = mfs`). |
| **Explorer me us waqt** | — | Drive letter thodi der ke liye gayab hota hai (rclone nikalta hai, WinFsp letter chhodta hai), phir wapas aata hai. |

### 2.3 Ek account ho to kya

`src/CloudSSD.App/AppHost.cs:388-396`:
```csharp
/// How the remote behind "All Accounts" is defined. Several accounts are joined by a union; read-only sign-ins
/// are shown in it but never chosen for new files (":ro"). rclone refuses a union of a single account ("union
/// can't point to a single upstream"), so one account is exposed through an alias of that account instead.
public static (string Type, string Key, string Value) CombinedRemoteSpec(IReadOnlyList<string> accounts, IReadOnlyCollection<string> readOnly) =>
    accounts.Count == 1
        ? ("alias", "remote", accounts[0] + ":")
        : ("union", "upstreams", string.Join(' ', accounts.Select(a => a + (readOnly.Contains(a) ? "::ro" : ":"))));
```
Ek account → `CloudSSD` ek **alias** ban jata hai, union nahi.

---

## Flow 3 — Drive letters aur asli rclone command

### 3.1 Kaunsa letter kaunsa

`src/CloudSSD.App/AppHost.cs:278-279` aur `:360-362`:
```csharp
public const string CombinedRemote = "CloudSSD", SingleRemote = "Accounts";
public const string CombinedMountId = "cloudssd", SingleMountId = "accounts";
...
var all = StandardDrive(CombinedMountId, "All Accounts", CombinedRemote, 'Z');
var each = StandardDrive(SingleMountId, "Each Account", SingleRemote, 'Y');
```

| Mount id | Naam | Remote | Letter | Backend |
|---|---|---|---|---|
| `cloudssd` | All Accounts | `CloudSSD:` | **Z:** | union (ek account par alias) |
| `accounts` | Each Account | `Accounts:` | **Y:** | combine |

**Z/Y preference hai, guarantee nahi.** `AppHost.cs:398-411` me letter khali ho tabhi assign hota hai; warna `DriveLetter` null chhota hai aur `MountPlanning.cs:24-34` se auto allocate hota hai:
```csharp
private static readonly char[] Preference = "GXYZWVUTSRQPONMLKJIH".ToCharArray();
```
Matlab: request kiya hua letter khali hai to wo; warna **G**, phir X, Y, Z, W…

### 3.2 Poora rclone command (default settings, Z:)

`src/CloudSSD.Mount/MountPlanning.cs:58-185` se banta hai:

```
rclone mount CloudSSD: Z:
  --vfs-cache-mode full
  --cache-dir "<DataRoot>\cache\m-cloudssd"
  --vfs-cache-max-size 20Gi
  --vfs-cache-min-free-space 8Gi
  --vfs-cache-max-age 720h
  --vfs-cache-poll-interval 30s
  --buffer-size 0
  --vfs-read-ahead 0
  --vfs-read-chunk-size 4Mi
  --vfs-write-back 5s
  --dir-cache-time 72h
  --attr-timeout 24h
  --poll-interval 1m
  --vfs-fast-fingerprint
  --vfs-handle-caching 5s
  --vfs-refresh
  --vfs-read-chunk-size-limit 4Mi
  --no-checksum
  --links
  --filter "- Cloud-SSD Devices/**" --filter "- desktop.ini" --filter "- folder.jpg"
  --filter "- folder.gif" --filter "- Thumbs.db" --filter "- AlbumArt*"
  --file-perms 0777 --dir-perms 0777
  --volname "Cloud Drive All Accounts"
  --drive-pacer-min-sleep 100ms --drive-pacer-burst 10 --drive-acknowledge-abuse
  --drive-encoding Slash,LtGt,DoubleQuote,Colon,Question,Asterisk,Pipe,BackSlash,Del,Ctl,RightSpace,RightPeriod,InvalidUtf8,Dot
  --tpslimit 10
  --rc --rc-addr 127.0.0.1:<port>
  --log-file "<DataRoot>\logs\rclone-cloudssd.log" --log-format date,time,microseconds
  --log-file-max-size 64Mi --log-file-max-backups 2
  -vv
```

Secrets command line par nahi — env me jaate hain (`src/CloudSSD.Mount/MountService.cs:205-212`):
`RCLONE_CONFIG`, `RCLONE_ASK_PASSWORD=false`, `RCLONE_CONFIG_PASS`, `RCLONE_RC_USER`, `RCLONE_RC_PASS`.

### 3.3 Flags jo jaan-boojh kar nahi diye gaye

- **`--vfs-disk-space-total-size` nahi** — `MountPlanning.cs:107-108`:
  ```csharp
  // No --vfs-disk-space-total-size: a fixed "15T" made every drive claim 15 TB in File Explorer. Without it rclone
  // reports the account's real quota (for a combined drive, the sum of its accounts).
  ```
  Isliye Explorer me jo size dikhta hai wo **rclone ka asli quota** hai, Cloud-SSD ka nahi.
- **`--buffer-size 0` aur `--vfs-read-ahead 0`** — `Settings.cs:85-90` ka comment: speculative download se game ka cold launch **~3x slow** ho jaata tha.
- **`--vfs-refresh` rakha gaya** — `MountPlanning.cs:110-116`:
  ```csharp
  // --vfs-refresh stays. It walks the whole drive tree at every mount, which costs Drive API requests (the
  // shared quota is tight), but WITHOUT it a directory listing that failed once (for example because the quota
  // was exhausted) stays cached as empty for --dir-cache-time (72 h): the folder then shows up empty although
  // every file is still in the cloud. ... A wrong listing is much worse than a slow mount, so the refresh stays.
  ```

### 3.4 App band karne par mount?

Mount process **detached** spawn hota hai (NUL stdio, no pipe) — `src/CloudSSD.Mount/DetachedProcess.cs`, `MountService.cs:212-220`. App ka window band karne par rclone zinda rehta hai; drive chalti rehti hai.

---

## Flow 4 — File kaise padhi jaati hai (chunked read + cache)

### 4.1 Cache mode: `full` vs `writes`

`src/CloudSSD.Core/Settings.cs:69-76`:
```csharp
/// file then sits on the local disk and reads are instant), but opening a 460 MB archive meant downloading
/// 460 MB first - measured: a 1 MiB read pulled the whole file and took 39 s, so a zip simply did not open.
/// "writes" keeps only what is written on the disk and serves reads straight from the cloud, so an archive
/// opens with just its file list (one request, measured 5 s for the same file and 0 MB extra).
```
Default `"full"` (`Settings.cs:76`). UI toggle **"Fast browsing"** (`CloudDrivesViewModel.CacheModeFor`) mapping:
- **ON → `full`** = file locally rakhna, dobara kholna instant.
- **OFF → `writes`** = kuch nahi rakhna, har read seedha cloud se.

`--vfs-read-chunk-size-limit` = chunk size (4 MiB) set kiya gaya — `MountPlanning.cs:118-124`:
```csharp
// with a 256 MiB limit, reading the END of a 460 MB archive ... took 38.4 s, while with the limit at 4 MiB the
// same read took 4.3 s - nine times faster, with the same ~0.8 MB of data coming down either way.
```

### 4.2 Measured A/B (same quota, alag mount, naya cache)

| Mount | 1 MB cold | 460 MB cold | 7 GB cold | WARM (dobara kholna) | Cache |
|---|---|---|---|---|---|
| `writes` | 22,387 ms | 4,706 ms | 4,036 ms | 3,279 / 5,307 / 5,027 ms | **0 MB** |
| `full` + 256 MiB limit | 4,596 ms | **38,383 ms** | 3,544 ms | 16 / 23 / 14 ms | 0.7 MB |
| **`full` + 4 MiB limit** | 13,038 ms | **4,328 ms** | 4,330 ms | **14 / 10 / 11 ms** | 0.8 MB |

Nayi install par asli Z: par: Jarvis.zip 25,421 ms → **116 ms**; MYRA_Android16 3,884 ms → **48 ms**; MYRAA MOBLIE (2) 40,140 ms → **20 ms**. `NOVA AI.zip` list **127 ms** (1761 entries) + 11.7 MB APK extract **169 ms**; 460 MB zip list **19,958 ms** (17,979 entries) + 246 MB exe extract **180,515 ms** (bytes exact 246,202,368); **7 GB zip list 2,955 ms**, 25,183 entries.

### 4.3 Cache budget aur disk safety

`src/CloudSSD.Cache/DiskSafety.cs:21-40`:
```csharp
/// rclone's --vfs-cache-max-size is not an instantaneous quota: open files are never evicted and cleanup runs every
/// poll interval, so the cache can temporarily exceed it. The guard therefore keeps an overrun margin below the free
/// space reserve and pauses Cloud-SSD's own prefetching under disk pressure.
...
public const double OverrunMargin = 0.10;
public static readonly long[] BudgetPresets = [10 * ByteSize.GiB, 20 * ByteSize.GiB, 30 * ByteSize.GiB, 40 * ByteSize.GiB];
var available = Math.Max(0, currentCacheBytes + volumeFree - reserve);
var safeTotal = (long)(available / (1 + OverrunMargin));
var total = Math.Min(requestedBudget, safeTotal);
var perMount = total / mountCount;
```
Default budget **20 GiB**, reserve **8 GiB** (`Settings.cs:64,67`). Do budgets hain — matlab alag:
- `Cache.BudgetBytes` (20 GiB) → saare mount caches ki disk budget.
- `Streaming.SessionPrefetchBudgetBytes` (4 GiB) → ek game session ka speculative prefetch.

**Eviction khud rclone karta hai** (`--vfs-cache-poll-interval 30s`, `--vfs-cache-max-age 720h`). Cloud-SSD ka apna LRU evictor nahi hai. Cloud-SSD ka cache delete karna manual + guarded hai (`DiskSafety.cs:63-97`): folder cache root ke bahar ho to "Refused", marker file na ho to "Refused", aur pending uploads hon to rukega.

### 4.4 Archive Warmer

`src/CloudSSD.App/ArchiveWarmer.cs:9-13`:
```csharp
/// Makes archives on a cloud drive open at once. Windows opens a .zip by reading its very end (the file list) in
/// small steps going backwards, plus the first bytes; on a cloud drive each step is a separate request to the
/// provider, and each request can take seconds (more when the provider rate-limits). So when a folder is listed,
/// this fetches the start and the end of every archive in it ahead of time, one request each, into rclone's cache;
```
Ranges (`ArchiveWarmer.cs:63-66`): 8 MiB tak poora file, usse bada → **aakhri 1 MiB** (zip ka central directory) + **pehle 128 KiB**. Per directory max 40 archives, rescan 5 min.

**Honest caveat:** ye sirf `--vfs-cache-mode full` me kaam karta hai. `writes` me rclone reads cache hi nahi karta, to warmer ka prefetch bekaar ho jaata hai.

### 4.5 Predictive prefetch (gaming)

`src/CloudSSD.Streaming/Predictions.cs:26-31`:
```csharp
/// Learns "block A is usually followed by block B" (first-order transitions between distinct consecutive blocks within
/// a short time window) and predicts successors whose conditional probability is high enough. Deterministic and
/// fully explainable: confidence = count(A→B) / (count(A) + 1).
```
**Yeh "ML" nahi hai.** Ek first-order Markov chain hai, plus ek sequential run detector, plus ek Laplace-smoothed startup predictor. Block size 1 MiB (`Settings.cs:144`). Predictor ki accuracy 0.25 se neeche girne par auto-disable (`Predictions.cs:340-357`).

---

## Flow 5 — QR code kaise aata hai

**Ek line me:** QR **PC** banata hai (khud ka pure-C# encoder, koi library nahi), usme hota hai `http://<pc-ip>:8090/?pair=<32-hex>`, aur woh secret **ek hi baar, 10 minute, sirf ghar ke LAN par** chalta hai. Phone ka camera use karta hai — Cloud-SSD app me koi QR scanner nahi hai.

### 5.1 QR banaya kahan

`src/CloudSSD.Desktop/ViewModels/MobileHubViewModel.cs:177-196`:
```csharp
// The QR code carries a one-time secret (ten minutes, home network only), never a lasting key.
_shownLinkSecret = Host.MobileBridge.PairingLinkSecret;
PairingUrl = $"{LocalUrl}?pair={_shownLinkSecret}";
...
QrCode = QrCodeGenerator.GenerateQrBitmap(PairingUrl, 8);
```
`src/CloudSSD.Desktop/QrCodeGenerator.cs:7-11`:
```csharp
/// Lightweight, zero-dependency QR Code generator (ISO/IEC 18004) in pure C#.
/// Produces crisp WPF BitmapSources for instant pairing over local Wi-Fi.
```
Versions 1–6, EC level L, mask 0, WPF `BitmapSource` me render. **Koi QR API call nahi, koi external library nahi.**

### 5.2 QR me kya hai

`src/CloudSSD.App/LocalMobileBridgeService.cs:77`:
```csharp
public string LocalUrl => $"http://{LocalIpAddress}:{Port}/";
```
`src/CloudSSD.App/LocalMobileBridgeService.Security.cs:223`:
```csharp
public string PairingLinkUrl => $"{LocalUrl}?pair={PairingLinkSecret}";
```
To QR ka payload literally:
```
http://192.168.1.5:8090/?pair=9f3ac7b14e8d2065f1a4c8b93d7e0512
```
32 lowercase hex = 128 random bits (`src/CloudSSD.Core/Settings.cs:193-195`):
```csharp
/// 128 random bits as 32 lowercase hex characters.
public static string NewPairingToken() =>
    Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
```

### 5.3 Secret ke rules

`src/CloudSSD.App/LocalMobileBridgeService.Security.cs:202-244`:
```csharp
internal static readonly TimeSpan PairLinkLifetime = TimeSpan.FromMinutes(10);
...
/// The secret in the QR code / pairing link: works once, for ten minutes, from the home network only. It replaces
/// the long token that used to be in the QR code, so a photo of the screen is not a permanent key.
...
internal bool TryPairingLink(string? secret, string clientIp)
{
    if (string.IsNullOrEmpty(secret) || !IsLocalNetworkAddress(clientIp)) return false;
    bool ok;
    lock (_linkLock)
    {
        ok = DateTimeOffset.UtcNow - _linkCreated <= PairLinkLifetime && SameText(secret, _linkSecret);
        if (ok) (_linkSecret, _linkCreated) = (MobileBridgeSettings.NewPairingToken(), DateTimeOffset.UtcNow);
    }
```
- **Ek hi baar** (use hote hi rotate).
- **10 minute** ki expiry.
- **Sirf home network** (`IsLocalNetworkAddress`).
- 10 minute baad `PairingLinkSecret` getter khud naya secret bana deta hai (`Security.cs:217`).

### 5.4 QR me kaunsa IP dikhega

`src/CloudSSD.App/LocalMobileBridgeService.Security.cs:288-302`:
```csharp
/// The score that picks the address shown in the QR code. Tailscale and link-local addresses never win.
internal static int ScoreAddress(NetworkInterfaceType type, IPAddress ip, string description = "")
{
    if (TailscaleInfo.IsTailnetAddress(ip)) return -100;
    int score = type switch { NetworkInterfaceType.Wireless80211 => 30, NetworkInterfaceType.Ethernet => 20, _ => 0 };
    if (LinkClassifier.IsUsbTether(description)) score -= 40;
    if (LinkClassifier.IsMobileHotspotRange(ip) || LinkClassifier.IsHostedNetwork(description)) score -= 30;
    if (b[0] == 192 && b[1] == 168) score += 15;
    else if (b[0] == 10) score += 10;
    ...
}
```
Matlab: Wi-Fi + 192.168.x sabse upar. **Tailscale (−100) aur link-local 169.254 (−50) kabhi QR me nahi aate.** Apna hotspot (−30) aur USB tether (−40) ghar ke Wi-Fi se haar jaate hain.

### 5.5 Phone QR kaise kholta hai

QR me plain `http://` URL hai, isliye **phone ka system camera / browser** use karta hai. Woh PC ki apni served web page kholta hai — `src/CloudSSD.Desktop/wwwroot/mobile/app.js:4779-4795`:
```javascript
// Opened from the PC's QR code in a browser: http://<pc>:8090/?pair=… (one-time code) or, from an older PC, ?token=…
const urlPair = params.get('pair');
...
pairing = applyPairLink({ host: location.host, pair: urlPair });
```
`app.js:1264-1279`:
```javascript
const res = await fetch(`${url}/api/pair?pair=${encodeURIComponent(o.pair)}&${pairQuery()}`, { method: 'POST', cache: 'no-store' });
const data = await res.json().catch(() => ({}));
if (!res.ok || !data.token) { showPair(data.error || 'This pairing link could not be used. Enter the 6-digit code instead.'); return false; }
adoptPairing(url, data);
```
Android app me **QR scanner nahi hai** (koi ZXing/ML Kit nahi; APK raw `aapt2`/`javac`/`d8` se banta hai, zero third-party library — `mobile-android/build-apk.ps1:101-122`). Sirf `cloudssd://pair?host=…&pair=…` deep link handle hota hai (`AndroidManifest.xml:74-81`).

### 5.6 6-digit code bhi hai

`src/CloudSSD.App/LocalMobileBridgeService.cs:56-67`:
```csharp
/// Six digits shown in Mobile Hub. A phone on the local network types them once and gets the long token back, so
/// nobody has to type 32 characters. Used once, then replaced; wrong guesses lock code pairing for a minute.
internal static string NewPairingCode() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
internal const int MaxCodeFailures = 5;
internal static readonly TimeSpan CodeLockout = TimeSpan.FromSeconds(60);
```
**Honest:** is code ka koi clock expiry nahi hai — sirf single-use rotation aur 5 galat guess par 60-second lockout.

---

## Flow 6 — Phone connect karke file transfer kiya

Phone se PC do tarah ka transfer hota hai, dono alag routes se.

### 6.1 Phone → cloud drive (seedha)

Route: `POST /api/fs/write` (`src/CloudSSD.App/LocalMobileBridgeService.Files.cs:461-491`).

```
POST /api/fs/write?root=<id>&path=<folder>&name=<file>&overwrite=1
Body: file bytes
        ↓
[parent folder check + mkdirs=1 → missing folders bana deta hai]
        ↓
[ReceiveFileAsync → hidden ".part" file me likhta hai]
        ↓
[File.Move(part, dest) → rclone write-back window (5s) ke andar rename]
        ↓
[rclone cloud ko ek complete file upload karta hai, half-written kabhi nahi]
```
`Files.cs:497-501`:
```csharp
/// Receives a request body into <paramref name="folder"/> as <paramref name="name"/>. The data goes to a hidden
/// part file first and is renamed only when it is complete, in the same request — inside rclone's write-back delay,
/// so the cloud sees one upload of the finished file and never a half-written one.
```
Space check pehle: `Files.cs:510-514`:
```csharp
long free = new DriveInfo(...).AvailableFreeSpace;
long budget = free - CacheReserveBytes;
if (budget <= 0 || contentLength > budget)
    throw new FsException(507, $"Not enough free space on the PC ({ByteSize.Format(Math.Max(0, free))} free, 10 GB is kept in reserve).");
```
Resumable upload: `uploadId` (32 hex) + `offset` + `total` — `Files.cs:477-486`; PC galat offset bheje to `409` + asli offset.

### 6.2 Phone → PC (upload folder)

Route: `POST /api/upload` (`LocalMobileBridgeService.cs:493-496`), resumable staging (`LocalMobileBridgeService.Upload.cs:11-16`):
```csharp
/// Uploads that continue where they stopped. The phone picks an upload id and sends the file with "offset" and
/// "total"; what arrived is kept in a staging folder on the PC (never inside a cloud drive, where rclone would upload
/// a half file). When the last byte is in, the file is put in place under a temporary name and renamed, so the cloud
/// only ever sees the finished file. /api/upload/status tells the phone how much the PC already has.
```
Staging folder: `%LOCALAPPDATA%\Cloud-SSD\incoming` (`Upload.cs:19`).

### 6.3 PC → phone (download / stream)

- `/api/download` — pura file (`LocalMobileBridgeService.cs:489-492`).
- `/api/stream` — range-based streaming (`LocalMobileBridgeService.cs:485-488`).
- `/api/fs/read` — cloud file ka range read, thumb ke liye `/api/fs/thumb`.

**Signed link** (token ke bina, limited time) — `LocalMobileBridgeService.Security.cs:421-447`:
```csharp
internal static readonly TimeSpan StreamLinkLifetime = TimeSpan.FromHours(12), DownloadLinkLifetime = TimeSpan.FromHours(1);
...
var mac = HMACSHA256.HashData(MediaKey(), Encoding.UTF8.GetBytes($"{kind}\n{path}\n{device}\n{exp}"));
```
`MediaKey` PC se kabhi bahar nahi jaata; phone ko sirf `sig` milta hai.

### 6.4 PC se phone ko file bhejna (offer/accept)

`src/CloudSSD.App/LocalMobileBridgeService.Share.cs:9-13`:
```csharp
/// Sending files between devices. This PC offers files to a device (it accepts or declines, then downloads them with
/// resume); a device sends files to another device through this PC (relay: uploaded here, then offered); and a file can
/// be handed out as a link that works for a while on the home network or the tailnet only.
```
Routes: `/api/share/inbox`, `/api/share/accept`, `/api/share/decline`, `/api/share/file`, `/api/share/offer`, `/api/share/upload`, `/api/share/devices`.

### 6.5 Transfer ki progress kaise dikhti hai

`src/CloudSSD.App/TransferTasks.cs:10-14`:
```csharp
/// One transfer as people think of it: an offer, a folder, or one "Send to PC" with many files, not each file alone.
/// The same fields as the phone's tasks, so both sides draw it the same way.
```
State: `waiting, running, paused, done, partial, failed, cancelled` (`TransferTasks.cs:40`). Phone par bhi wahi shape (`/api/transfers`).

### 6.6 File transfer ke 4 raaste — kaunsa kab

| Raasta | Route | Kaunsa device |
|---|---|---|
| Phone → cloud drive | `POST /api/fs/write` | phone app / web |
| Phone → PC | `POST /api/upload` | phone app |
| PC → phone | `/api/download`, `/api/stream`, `/api/share/*` | phone app / browser |
| Phone → doosra device (relay) | `/api/peer/*` + `/api/share/*` | phone app |

---

## Flow 7 — Phone ↔ Laptop

Laptop seedha phone se baat nahi karta. Beech me **wohi PC** hota hai jiske saath phone paired hai — ya jis PC se laptop paired hai.

### 7.1 Laptop → PC → Phone (relay)

`src/CloudSSD.App/LocalMobileBridgeService.Peer.cs:173-176`:
```csharp
/// /api/peer/fs/{roots,list,tree,read}?device=<phone>: a laptop seeing a phone's files through this PC. The phone is
/// asked as for this PC, with who is asking; it answers only as far as it allows this PC.
```
Phone ko poochha jaata hai — uska jawab sirf utna hi hota hai jitna phone allow karta hai.

Relay permission alag hai: `AccessControl.cs` me `Relay = "relay"`, aur PC-wide switch **"Device to device"** (`LocalMobileBridgeService.Security.cs:317`).

### 7.2 Phone ka long-poll (bina port khole)

`src/CloudSSD.App/PeerBroker.cs:26-36`:
```csharp
/// Two-way access without opening a port on the device: the device keeps one long request open (/api/peer/inbox,
/// up to 25 s, then asks again) and this PC hands it questions there; the device answers on /api/peer/result. A second
/// long request from the same device ends the first. At most <see cref="MaxWaiting"/> devices wait at once.
public const int MaxWaiting = 24;
public static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(25);
```
`/api/peer/inbox` → PC ka question; `/api/peer/result` → phone ka jawab; `/api/peer/blob` → file fetch.

### 7.3 Laptop → dusra PC (Remote PCs)

`src/CloudSSD.App/Remote/RemotePcManager.cs:9-11`:
```csharp
/// The "Other PCs" this PC is paired with as a laptop: pairing with a code, the saved list, one session per PC, and
/// finding Cloud-SSD PCs on this network.
```
- `AddPcAsync(address, code)` — dusre PC ka 6-digit code daal ke pair (`RemotePcManager.cs:104-105`).
- `RemotePcClient.cs:129-133` — **proof**: "Is this address really that PC? A PC with /api/hello answers a fresh challenge with the hash of this PC's key, which a device that only copied its public id cannot."
- `RemotePcClient.cs:232-240` — download with **resume** (`.cssd-part` + `If-Range`).

### 7.4 "Ye PC meri files kyun nahi dikha raha"

File access **shared roots** tak scoped hai — `LocalMobileBridgeService.Security.cs:383-395`:
```csharp
/// The folders a paired phone may read: the connected cloud drives, the folders phone files are saved to, and (only when switched on) the PC's own disks.
```
Refusal codes: **400** galat path, **404** drive connected nahi, **403 `not-shared`** root share nahi hai.

---

## Flow 8 — "PC off ho to bhi files"

`src/CloudSSD.App/LocalMobileBridgeService.WhenOff.cs:14-20`:
```csharp
/// "Files when it is off" ("Band hone par bhi files"): each device keeps a copy of its files in the cloud so the other
/// one can open them while it is switched off. It is its own yes/no, per pair, on BOTH sides, and off until both say
/// yes — on purpose not one of the access categories: "allow everything" for a new device must never switch it on.
/// A guest can never have it. This part keeps the two answers and tells the device where this PC's copy is and which
/// Google key to sign in with; the copying itself is Mirror/DeviceMirrorService (docs/WHEN_OFF_PLAN.md).
```
Do taraf ka "haan" chahiye. Sirf ek taraf ka "haan" kaafi nahi.

**Mirror engine** — `src/CloudSSD.App/Mirror/DeviceMirrorService.cs:18-24`:
```csharp
/// "Files when it is off", this PC's side: keeps a copy of the PC's own files in the cloud while a paired device and
/// this PC both said yes (docs/WHEN_OFF_PLAN.md). One pass at a time: look at the disk (the person's folders every
/// hour, everything once a day), copy what is new or changed (the person's folders first, small files first), move
/// the copies of deleted files to a trash folder, write the state for the other side to read.
/// It stops for a game, on battery, and when the person pauses it; nothing of the app's own data is ever copied.
```
State: `off, paused (reason), scanning, uploading, idle`. Pause reasons: `person, game, battery, no-rclone, no-account, upload-limit, rate-limit, error`.

**Device ko sirf Google key milta hai, sign-in nahi** — `WhenOff.cs:134-135`:
```csharp
// The Google key only (what a sign-in is asked with), never a sign-in itself: the device signs in to Google on
// its own, so no account access ever travels over the home network.
```

---

## Flow 9 — Gaming: prepare aur launch

### 9.1 "Optimize & Prepare" ke 8 steps

`src/CloudSSD.Games/GamePreparer.cs:48-55`:
```csharp
new("Inspect game folder", StepState.Pending, null),
new("Check cache capacity", StepState.Pending, null),
new("Measure network speed", StepState.Pending, null),
new("Load access profile", StepState.Pending, null),
new("Analyze compatibility", StepState.Pending, null),
new("Prepare essential files", StepState.Pending, null),
new("Prefetch startup data", StepState.Pending, null),
new("Validate", StepState.Pending, null),
```
- **Network measurement** mount bypass karta hai — `rclone cat --offset --count` se mid-file ka 8 MiB range padha jaata hai, to cache pollute nahi hota (`GameLauncher.cs:232-235`).
- **Profile nahi hai to jhooth nahi bolta** — `GamePreparer.cs:131-133`: *"No launch profile yet — startup data unknown. The first launch will be profiled (exploratory mode)."*
- **Validate** PE header padhta hai aur rclone ke range index se coverage check karta hai (`GamePreparer.cs:224-228`). `ReadyToLaunch` tabhi jab `coverage >= 0.95`.

### 9.2 Launch

`src/CloudSSD.App/AppHost.cs:578-615` → `src/CloudSSD.Games/GameLauncher.cs:115-135`. Process **Windows Job Object** me chalta hai, kill-on-close **nahi** (`GameLauncher.cs:11`):
```csharp
/// Tracks a process tree with a Windows Job Object (no kill-on-close: closing Cloud-SSD never kills the game).
```
Diagnostics JSON me likhte hain (`launch-<id>-<ts>.json`).

**Honest:** launch readiness ko dobara check nahi karta. `GameLibraryViewModel.cs:141-142` sirf warn karta hai: *"Launching. Data that is not cached will be fetched from the cloud on demand — expect loading waits on first launch."*

### 9.3 Hybrid mode kya hai

`src/CloudSSD.Games/ExecutionModes.cs:14-18`:
```csharp
/// Hybrid Optimized mode: builds a real local folder on the SSD that mirrors the game's directory tree. Executables,
/// DLLs, configuration and small files are copied locally (fast, writable, launch from a normal NTFS path); large asset
/// files become symbolic links into the cloud drive and are streamed. New files the game writes land on the SSD.
/// Requires Windows Developer Mode (or admin) for unprivileged symbolic links; Cloud-SSD never enables it silently.
```
Rule (`ExecutionModes.cs:29-42`): **≤ 2 MiB** ya essential extension (`.exe .dll .ini .cfg .config .json .xml .txt .ico .manifest .dat .lua .bat .cmd .pdb .sig .url .info .vdf .yaml .yml .toml .log`) **≤ 256 MiB** → copy; baaki → symlink.

Developer Mode ya admin chahiye — `src/CloudSSD.Diagnostics/Dependencies.cs:49-50`:
```csharp
/// <summary>True when the current process can create unprivileged symbolic links (Developer Mode or admin).</summary>
public static bool CanCreateSymlinks() => IsDeveloperModeEnabled() || IsAdministrator();
```
Nahi hai to mode apne aap **Cloud Direct** me gir jaata hai (`AppHost.cs:559`).

**Honest:** hybrid me "streaming" alag path nahi hai — bade assets ke symlink WinFsp/rclone mount ke andar hi padhe jaate hain.

### 9.4 Game-Aware Mode

`src/CloudSSD.Streaming/GameAware.cs:9-17` — named pipe protocol (`\\.\pipe\cloudplayx-telemetry-<gameId>`), server `PipeOptions.CurrentUserOnly`. **Koi game/mod/plugin is repo me us protocol ko baat nahi karta** — UI khud kehta hai (`GameLibraryViewModel.cs:97`): *"telemetry pipe enabled (needs an authorized game plugin)"*. Feature code me hai, par asli me **inert** hai.

---

## Flow 10 — Phone par game stream (video)

`src/CloudSSD.GameStream/` asli H.264 video + audio + touch injection karta hai.

- **Capture**: DXGI desktop duplication (`ScreenEncoder.cs:319-325`).
- **Encode**: hardware H.264 via Media Foundation async transform (`ScreenEncoder.cs:120-152`), CBR, no B-frames, low-latency. Hardware encoder na ho to `NotSupportedException` (`ScreenEncoder.cs:376`).
- **Audio**: WASAPI loopback, 16-bit stereo 48 kHz (`AudioLoopback.cs:6-13`).
- **Transport**: HTTP `101 Switching Protocols` + custom binary framing (`LocalMobileBridgeService.Game.cs:70-71`).

Gating strict hai (`LocalMobileBridgeService.Game.cs:27-36`): paired device key chahiye (purana shared token **refuse** hota hai: *"the old shared key cannot control games"*), aur **sirf home LAN** — door se 403 `home-only`.

**Honest scope:** ye cloud game library nahi, **emulator ka game area** stream karta hai. Default `GameName = "Free Fire"`, `LaunchPath = BlueStacks` (`Settings.cs:271-273`). Sirf picture aur touch jaata hai — desktop ka baaki hissa kabhi nahi.

---

## Flow 11 — Memory relief, Space, aur offloader

### 11.1 Memory Relief

`src/CloudSSD.App/MemoryRelief.cs:91-95`:
```csharp
var before = MemoryHealth.ReadRam().AvailableBytes;
var apps = MemoryHealth.TrimBackgroundApps(skip);
Thread.Sleep(10000); // Windows takes about this long to hand the moved pages back as free RAM
var (total, after) = MemoryHealth.ReadRam();
```
`src/CloudSSD.App/MemoryHealth.cs:218-222`:
```csharp
internal static bool Trim(int processId)
{
    var handle = OpenProcess(ProcessSetQuota | ProcessQueryLimitedInformation, false, (uint)processId);
    if (handle == IntPtr.Zero) return false;
    try { return K32EmptyWorkingSet(handle); }
```
- Win32 API: **`kernel32.dll!K32EmptyWorkingSet`** (psapi nahi).
- Kuch kill nahi hota — working set trim hoti hai, Windows jab chahiye wapas laata hai.
- Skip: system/Cloud-SSD processes, < 32 MiB working set, last 15 min me use hua app, aur chal raha hua game.
- **"3-5 GB" koi claim nahi hai.** Code me koi byte target nahi. Freed amount = jitna trimmed processes ne pakda tha, aur har run me measure hokar dikhaya jaata hai.
- Page file chhua nahi jaata — `MemoryHealth.cs:41-43`: *"never changes a Windows setting, because a page file adds no speed and 'RAM cleaner' tricks give nothing Windows does not already do"*.

### 11.2 Game Mode

`src/CloudSSD.App/GameMode.cs:62-73` — app ko `WM_CLOSE` bhejta hai (kill kabhi nahi), background trim karta hai, aur game 2 minute foreground me na aaye to app wapas kholta hai.

### 11.3 Smart Storage Offloader

`src/CloudSSD.App/SmartStorageOffloaderService.cs:201-208`:
```csharp
// Straight to the cloud, not through the drive letter: a copy made there first lands in the
// drive's cache on this same disk, so nothing comes free until the cache lets go of it.
bool sent = await RcMoveAsync(rc, file.DirectoryName!, file.Name, mount.Definition.RemoteSpec, ...);
if (!sent || File.Exists(file.FullName))
{
    _log.LogWarning("Could not offload file {File}; it stays on the PC", file.Name);
```
Safety rules (jo code me hain):
- Game chal raha hai to auto pass bilkul nahi chalta (`:184`, `:677`).
- Downloads se sirf **≥ 100 MiB** aur **≥ 7 din purane** files (`:359`, `:361`).
- Adhoore download (`.crdownload .part .partial .download .tmp .opdownload`) skip.
- Abhi kisi app ne khula hua file skip.
- Sirf top-level Downloads files; subfolders kabhi nahi.
- Fail hua to file PC par hi rehti hai.

**Do honest notes:** `TargetMountId` default `"cloudssd15tb"` hai jo kisi generated mount id se match nahi karta (`Settings.cs:340`) — code hamesha "koi bhi writable mounted drive" par girta hai. Aur `EvictVfsCacheOnLowSpace` actually `vfs/forget` call karta hai (`:283`) — ye **metadata** bhoolta hai, data cache free nahi karta. Ise "cache eviction" mat kehna.

### 11.4 Space page

`src/CloudSSD.App/SpaceIndex.cs:7-12` — har folder ka size live map, pehla scan background me, phir `FileSystemWatcher` se sirf badle folders dobara measure. Snapshot `DataRoot\space\pc.json` me, 10 min me save.

Protection rules (`SpaceService.cs:61-82`): Windows, Program Files, ProgramData, profile root, AppData roots, Desktop/Documents/Downloads ke *roots*, `C:\Users\<koi bhi>`, Cloud-SSD ka apna data/cache, aur saare cloud drives — inme se kuch bhi delete ke liye offer nahi hota.

---

## Flow 12 — Security aur storage: kahan kya pada hai

### 12.1 Files on disk

| Cheez | File | Protection |
|---|---|---|
| Google refresh token + client secret | `%LOCALAPPDATA%\Cloud-SSD\rclone.conf` | **rclone ka apna config encryption**; password `rclone.conf.key` me |
| `rclone.conf.key` (encryption password) | `%LOCALAPPDATA%\Cloud-SSD\rclone.conf.key` | **Windows DPAPI** (`SecretStore.Save`) |
| Phone pairing token + media signing key | `settings.json` me | **Windows DPAPI** (`SecretProtector`) |
| Mount ka RC password | `mounts.state.json` | DPAPI (`MountService.cs:541`) |
| Apna Google OAuth client (id+secret) | `google-client.json` (exe ke paas) | **plaintext JSON** |

### 12.2 DPAPI scope

`src/CloudSSD.Diagnostics/SecretStore.cs` aur `src/CloudSSD.Core/SecretProtector.cs`:
```csharp
/// Protects the small secrets that live inside settings.json (the phone pairing token and the media
/// signing key) with Windows DPAPI, so the file on disk does not hand the token to anyone who can read
/// it. DPAPI ties the value to the same Windows user on the same machine, so a copied settings file is
/// useless elsewhere - the phone simply has to pair again.
```
Scope `CurrentUser` + fixed entropy. Matlab: **doosre Windows user par copy karke koi faayda nahi.**

### 12.3 settings.json me token hai hi nahi

`src/CloudSSD.Core/Settings.cs` — `AppSettings` me koi OAuth token / refresh token / client secret field nahi hai. Login sirf `rclone.conf` me hai.

### 12.4 Phone bridge ka transport

- **Plain HTTP.** Koi TLS nahi — `src/` me `SslStream`/`Tls12`/`Tls13` koi use nahi hota.
- `TcpListener(IPAddress.Any, 8090)` + hand-written HTTP/1.1 parser (`LocalMobileBridgeService.cs:166-172`).
- Max 64 simultaneous connections (`:33`).
- DNS-rebinding guard: Host header allow-list na ho to **421 Misdirected Request** (`:306-310`).
- Token **har request** me chahiye. Same Wi-Fi par hona kaafi nahi (`LocalMobileBridgeService.cs:376-386`).
- PC par sirf **hash** rehta hai, token nahi — `LocalMobileBridgeService.Security.cs:43`:
  ```csharp
  internal static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
  ```
- Comparison constant-time (`CryptographicOperations.FixedTimeEquals`).
- Phone ka token Android Keystore me encrypted hota hai (`mobile-android/.../TokenStore.java:17-43`), aur app ka backup disabled hai (`AndroidManifest.xml:55`).

### 12.5 Permissions: do layers

**Layer 1 — PC-wide switches** (`LocalMobileBridgeService.Security.cs:306-317`): Drive browsing, Streaming, Uploads, Gaming HUD and PC remote, File management, PC control, Space & RAM, Play on phone, Device to device.

**Layer 2 — per-device grants** (`src/CloudSSD.App/AccessControl.cs:8-11`): `browse, stream, upload, manage, hud, control, space, game, relay`. Modes: `always | timed | session`.

**Guest** (`LocalMobileBridgeService.Guests.cs`): 6-digit code, 15 min me type karna hai, max 7 din reh sakta hai, sirf wahi categories jo choose ki gayi thi.

### 12.6 Audit log

`src/CloudSSD.App/AccessLog.cs:8-15`:
```csharp
/// Who opened what on this PC: one small file per device per day (JSON lines), kept 30 days. The same thing done again
/// within a minute (a video asking for its next piece, a folder refreshed) is written once.
```

### 12.7 Honest gaps

- `/api/status` **unauthenticated** hai aur `pcName`, `version`, `port`, `pcId`, `features` jaise koi bhi port 8090 tak pahunchne wale ko bata deta hai. Drives/games/storage nahi dikhata — deliberately (`LocalMobileBridgeService.cs:747-750`).
- `obscure = false` pass hota hai (`RcloneConfigService.cs:337`), to `client_secret` `rclone.conf` me clear text me hai; sirf file-level encryption usko cover karti hai.
- Bridge me chunked transfer encoding support nahi hai — body `Content-Length` se hi padha jaata hai.

---

## Feature ka asli status (ek nazar)

| Feature | Status | Note |
|---|---|---|
| Google Drive login + mount | **Poora** | rclone rc state machine |
| OneDrive / WebDAV | **Poora** | `OneDriveConnector`, `WebDavConnector` |
| Multi-account union/combine | **Poora** | 2 generated remotes |
| Chunked cache (4 MiB) | **Poora** | measured A/B upar |
| Archive warmer | **Poora, `full` mode me hi** | `writes` me no-op |
| Predictive prefetch | **Poora** | Markov chain — "ML" nahi |
| Game scan/prepare/launch | **Poora** | prepare launch ko gate nahi karta |
| Hybrid mode | **Poora, environment-gated** | Developer Mode/admin chahiye |
| Game-Aware Mode | **Partially** | protocol hai, koi plugin nahi |
| Phone pairing (6-digit + QR) | **Poora** | one-time, 10 min, LAN-only |
| Phone bridge | **Poora** | plain HTTP + token + LAN/Tailscale rules |
| Phone ↔ laptop relay | **Poora** | PC beech me |
| "When PC off" mirror | **Poora** | dono taraf "haan" chahiye |
| GameStream (video) | **Poora** | hardware H.264 chahiye, LAN-only |
| Memory relief | **Poora** | measured per run, fixed GB claim nahi |
| Smart offloader | **Poora** | real safety rules |
| Space & RAM | **Poora** | live index |
| Provider adapters (Dropbox/S3/NAS) | **Partial by design** | `AvailableUntested` (`Providers.cs:7-8`) |
| TeraBox | **Unsupported** | official API nahi hai (`Providers.cs:58-64`) |
| mTLS 1.3 | **Nahi hai** | plain HTTP |
| "Markov ML" | **Nahi hai** | deterministic Markov chain |
| "3-5 GB RAM drop" | **Koi target nahi** | har run me measure |

---

## Code anchors (quick index)

| Kya | Kahan |
|---|---|
| Google sign-in (rclone rc) | `src/CloudSSD.Mount/RcloneConfigService.cs:314` |
| Remote naam ka counter | `src/CloudSSD.Desktop/Views/ConnectDriveWindow.xaml.cs:34` |
| Union/combine likhna | `src/CloudSSD.App/AppHost.cs:345-357` |
| Drives remount | `src/CloudSSD.App/AppHost.cs:366-382` |
| Drive letter choose | `src/CloudSSD.Mount/MountPlanning.cs:24` |
| Poora rclone flag set | `src/CloudSSD.Mount/MountPlanning.cs:58-185` |
| Detached mount process | `src/CloudSSD.Mount/DetachedProcess.cs` |
| Cache budget + pressure | `src/CloudSSD.Cache/DiskSafety.cs:28-60` |
| Archive warmer ranges | `src/CloudSSD.App/ArchiveWarmer.cs:63-66` |
| Markov / sequential predictors | `src/CloudSSD.Streaming/Predictions.cs:26-58, 144-220` |
| QR generator | `src/CloudSSD.Desktop/QrCodeGenerator.cs` |
| QR payload banana | `src/CloudSSD.Desktop/ViewModels/MobileHubViewModel.cs:177-196` |
| Pairing secret rules | `src/CloudSSD.App/LocalMobileBridgeService.Security.cs:202-244` |
| QR me kaunsa IP | `src/CloudSSD.App/LocalMobileBridgeService.Security.cs:288-302` |
| 6-digit code | `src/CloudSSD.App/LocalMobileBridgeService.cs:56-67, 703-730` |
| Pair endpoint | `src/CloudSSD.App/LocalMobileBridgeService.cs:389-398, 588-697` |
| Token hash + auth | `src/CloudSSD.App/LocalMobileBridgeService.Security.cs:43, 64-85` |
| Phone → cloud write | `src/CloudSSD.App/LocalMobileBridgeService.Files.cs:461-556` |
| Resumable upload | `src/CloudSSD.App/LocalMobileBridgeService.Upload.cs:85-160` |
| Signed media link | `src/CloudSSD.App/LocalMobileBridgeService.Security.cs:421-447` |
| Share offer/accept | `src/CloudSSD.App/LocalMobileBridgeService.Share.cs` |
| Peer relay (phone↔laptop) | `src/CloudSSD.App/LocalMobileBridgeService.Peer.cs:47-176` |
| Remote PC (laptop↔PC) | `src/CloudSSD.App/Remote/RemotePcManager.cs`, `RemotePcClient.cs` |
| When-off mirror | `src/CloudSSD.App/LocalMobileBridgeService.WhenOff.cs`, `src/CloudSSD.App/Mirror/DeviceMirrorService.cs` |
| Game prepare (8 steps) | `src/CloudSSD.Games/GamePreparer.cs:46-251` |
| Hybrid builder | `src/CloudSSD.Games/ExecutionModes.cs:29-133` |
| Symlink capability | `src/CloudSSD.Diagnostics/Dependencies.cs:40-50` |
| Game stream encoder | `src/CloudSSD.GameStream/ScreenEncoder.cs` |
| Memory trim | `src/CloudSSD.App/MemoryHealth.cs:200-224` |
| Offloader safety | `src/CloudSSD.App/SmartStorageOffloaderService.cs:355-415` |
| Space index | `src/CloudSSD.App/SpaceIndex.cs` |
| Provider support matrix | `src/CloudSSD.Providers/Providers.cs:32-84` |
| DPAPI secret storage | `src/CloudSSD.Core/SecretProtector.cs`, `src/CloudSSD.Diagnostics/SecretStore.cs` |
