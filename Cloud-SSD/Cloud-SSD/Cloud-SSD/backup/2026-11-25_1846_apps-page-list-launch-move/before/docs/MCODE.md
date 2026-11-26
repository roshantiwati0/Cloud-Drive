# MCODE.md — MiniMax Code Work Log & Action Registry

> **Purpose:** MiniMax Code (MCode) agent dwara Cloud-SSD project mein kiye gaye sabhi kaam (audit, bug fixes, code edits, maintenance) ka central work history log.
> **App Name:** Cloud-SSD (Virtual Drive: Cloud Drive)
> **Agent:** MiniMax Code (MCode)
> **Central Rules:** See `RULE.md`
> **Note:** Yeh file sirf MCode ka apna log hai — Antigravity / Codex / Claude / Gemini ke logs bilkul alag hain (RULE.md rule 7).

---

## 🛠️ Summary of Work Done by MiniMax Code

---

### [TASK-MCODE-001] Project Health Audit — Build/Test + Runtime Logs + Broken README Links

- **Date & Time:** 2026-11-16 15:05 – 15:20
- **Agent & Model:** MiniMax Code (MCode, MiniMax-M3.1-Flash)
- **Prompt:** "ha tum bhi aapane name ka file banakr usme kam karna or abhi isme kya kya chije tik se work nahi kar raha hai kisme chijo me error hai ya work nahi kar raha hai wo deko or batao"
- **Files Inspected:**
  - `RULE.md` (root + codebase — dono byte-identical), `docs/CHALU_KAAM.md`, `docs/CURRENT_TASK.json`, `docs/antigravity.md`, `docs/CLAUDE.md`, `.work/haal.txt`
  - `README.md` (workspace root + codebase dono), `src/CloudSSD.App/LocalMobileBridgeService.Files.cs`, `src/CloudSSD.App/LocalMobileBridgeService.Security.cs`, `src/CloudSSD.Mount/RcloneConfigService.cs`
  - Runtime: `%LOCALAPPDATA%\Cloud-SSD\settings.json`, `mounts.state.json`, `startup_debug.txt`, `logs\cloudplayx-20261116.log`, `logs\rclone-cloudssd.log`
  - Windows Event Log (Application + System, last 24h), `Get-PSDrive`, `Get-Service`, `tasklist`
- **Files Created / Modified:**
  - `docs/MCODE.md` (nayi — yeh file)
  - `docs/CHALU_KAAM.md` (sirf haal ka naya entry — koi code nahi)
  - `docs/CURRENT_TASK.json` (backup script ne lock kiya)
  - `backup/2026-11-16_1518_mcode-health-audit-readme-links/` (before/ + complete/)
- **Backup Folder:** `backup/2026-11-16_1518_mcode-health-audit-readme-links`
- **Validation Result:** APPROVED (build 0 errors / 0 warnings, C# tests 465/465 pass, JS tests 45/45 pass) — **sirf code ke liye; project ki runtime health ke liye FAILING**
- **Summary of Changes:**
  - **Koi source code nahi badla.** Yehm kaam sirf jaanch (audit) tha: build, dono test suites, runtime logs, README links aur settings — sab padha gaya, kuch likha nahi gaya.
  - Naya work-history file `docs/MCODE.md` bana (RULE.md rule 4 ke mutabiq per-tool log).
  - Neeche "Mil ki jaanch" mein har finding evidence ke saath darj hai.

#### Mil ki jaanch — jo SAHI chal raha hai (verified)

| Check | Result |
|---|---|
| `dotnet build CloudSSD.slnx` (.NET SDK 10.0.404) | ✅ Build succeeded — 0 Warning(s), 0 Error(s) |
| `dotnet test tests\CloudSSD.Tests` | ✅ 465 passed, 0 failed, 0 skipped |
| `node --test tests\mobile-js\*.test.js` | ✅ 45 pass, 0 fail |
| Phone page ki dono copies (`mobile-android/src/main/assets` vs `src/CloudSSD.Desktop/wwwroot/mobile`) | ✅ SHA256 same — `app.js`, `data.js`, `i18n-words.js`, `index.html` sab match |
| WinFsp | ✅ `WinFsp.Launcher` service Running |
| App startup flow | ✅ `startup_debug.txt`: "Startup completed successfully!" (16 Nov 14:53:45) |

#### Mil ki jaanch — jo TOOTA hua hai (evidence ke saath)

1. **Z: aur Y: drive abhi mounted NAHI hain.**
   `Test-Path Z:\` = False, `Get-PSDrive FileSystem` mein sirf `C:`. `tasklist` mein `Cloud-SSD.exe` aur `rclone.exe` koi nahi. Aakhri rclone activity 16 Nov **15:01:28** (ab ~15:20) — matlab app ab band/crashed hai.

2. **App ne gracefully exit nahi kiya (crash/kill).**
   Windows Event Log mein WER `AppTermFailureEvent` baar baar: 14:52:53, 14:55:52, 15:02:28, 15:02:32, 15:05:18 + `crashpad_log` (15:03:22). App log mein band hone ka koi entry nahi.

3. **Is ki direct nuksaani: 7.3 GB ka upload adhoora chhoot gaya.**
   `rclone-cloudssd.log` ke `core/stats` mein `errors:89`, `lastError:"couldn't list directory: context canceled"`, `retryError:true`; transfer `Claude-SSD Folder/New folder/Cloud-Drive.zip` (7,335,944,963 bytes) sirf **24%** par ruka. Transfer resume nahi hua.

4. **Mount auto-restore dono drives ke liye fail.**
   `cloudplayx-20261116.log`:
   - `ERR AppHost: Could not restore mount All Accounts: Drive Z: did not appear within N s`
   - `ERR AppHost: Could not restore mount Each Account: Drive Y: did not appear within N s`
   - `WRN rclone: mountpoint "Z:" didn't became available on mount - continuing anyway`
   - `INF AppHost: Mount cloudssd: Failed Timed out waiting for the drive to appear` + `Restarting mount in N s (attempt N)` (retry loop)

5. **Symlinks mount par fail ho rahe hain.**
   `Error: symlinks not supported without the --links flag: /` (7 baar, dono remotes). Code check kiya — `src` mein kahin bhi `--links` flag pass nahi ho raha. Node_modules/junction jaise folders isi wajah se error dete hain.

6. **Union remote ki statfs support nahi hai → Z: ki disk-space info galat.**
   `Error: union root '': Statfs failed: this usage field is not supported` — Explorer mein drive ki Total/Free size sahi nahi dikhegi.

7. **Google API ke saath timeout.**
   `Error: HTTP token bucket error: context deadline exceeded` (upload chalte hue).

8. **Mount poori C: drive ka tree walk kar raha hai (noise + slow).**
   180+ `Failed to copy: couldn't list directory: context canceled` — `MiniMax Code/...`, `node_modules/...` ke files. `settings.json` mein `Mirror.IncludeSystem = true`, `Mirror.OnlyFolders = []` — CHALU_KAAM ke "Mirror apne aap poori disk upload karne lage" warning ki wajah yahi hai. Mirror abhi `Paused = true` hai (sahi setting).

9. **README ke saare relative links toote hain.**
   - Workspace root `README.md`: 40 relative links me se **37 broken**
   - Codebase `README.md`: 40 me se **40 broken**
   - Wajah: folder structure `Cloud-SSD/Cloud-SSD-0.7.0-source/Cloud-SSD` se badal kar `Cloud-SSD/Cloud-SSD/Cloud-SSD` ho gaya tha, README update nahi hua.
   - Toote hue: 12 `src/*` links, "Folder map", "All documents" table ki poori list (`SETUP.md`, `ARCHITECTURE.md`, `BENCHMARKS.md`, `COMPATIBILITY.md`, `KNOWN_LIMITATIONS.md`, `RAM.md`, `SECURITY.md`, `CONTRIBUTING.md`, `THIRD_PARTY_NOTICES.md`, `docs/GOOGLE_DRIVE_SETUP.md`, `docs/TROUBLESHOOTING.md`, `docs/GAME_AWARE_PROTOCOL.md`), `installer/`, `mobile-android/`, `releases/`, `Cloud-SSD-5TB/`, `LICENSE`, aur archify ke teen screenshots (`archify-system-design.svg` / `-dark.png` / `-light.png`) + logo image.
   - Asli docs ab `docs/archive/` mein hain (`KNOWN_LIMITATIONS.md`, `WHEN_OFF_PLAN.md`, `WHEN_OFF_BINA_KEY_PLAN.md`); baaki (`SETUP.md`, `ARCHITECTURE.md`, `SECURITY.md`…) codebase root par **exist hi nahi** karte. Yaani GitHub par repo ka poora "Docs" section dead hai.

10. **`docs/CHALU_KAAM.md` ka plan-file path stale.**
    File `docs\WHEN_OFF_BINA_KEY_PLAN.md` (aur `docs\WHEN_OFF_PLAN.md`) ko point karti hai, lekin ab wo `docs\archive\` mein hain. Naya agent plan padhne ki koshish karega to fail hoga.

11. **Branch mismatch.** `RULE.md` aur `CHALU_KAAM.md` dono kehta hain branch `feature/multi-device`, lekin repo abhi `main` par hai.

12. **Ek backup folder adhoora.** `backup/2026-11-14_2003_before-restore-from-2026-11-14_2000_system-verification` mein sirf `before/` hai, `complete/` nahi (restore safety snapshot — phir bhi RULE.md ke hisaab se `complete` chahiye tha).

13. **Version drift.** README badge: PC **0.7.0**, phone **0.8.0`. CHALU_KAAM ke hisaab se PC build **0.9.0** (`dist\Cloud-SSD-0.9.0-win-x64`). Installed build 14 Nov 2026 16:01 ka hai.

14. **Purana brand naam log file mein.** Log abhi bhi `logs\cloudplayx-20261116.log` naam se banta hai (baaki sab jagah `Cloud-SSD`). Branding drift.

15. **`settings.json` mein pairing token aur media key plain-text mein** hain (`MobileBridge.PairingToken`, `MobileBridge.MediaKey`). `rclone.conf` DPAPI se encrypted hai, settings.json nahi. (File user-scoped hai aur repo ke bahar hai — low risk, par RULE.md rule 14 ke nazariye se note kiya ja raha hai.)

16. **Phone se real verification 2 din se nahi hui.** Paired device "vivo I2221" ka `LastSeen` **2026-11-14T17:11** hai (aaj 16 Nov). Phase 3 ka asli test (3.7 — "PC app band karke phone se folder kholna") aaj tak nahi hua, isliye wo feature abhi bhi "code ready, verified nahi" hai.

17. **`.work` mein purane patch scripts** (`.work\p3-*.js`, `.work\k1-pc.js`) hain jinke upar "dobara mat chalao" likha hai, lekin koi machine-readable marker nahi — naya agent galti se chala sakta hai.

#### Next Steps / Remaining Work

- **Koi code change is task mein nahi kiya gaya** — sirf audit. Fix ka faisla user ka hai.
- Fix karne ka suggested order (agar user kahe):
  1. Mount restore fail + crash → app log + rclone log ke basis par reproduce (app start karke Z:/Y: dekhna).
  2. `--links` flag missing → symlink error ka seedha fix.
  3. README ke 40 broken links → naye folder structure ke hisaab se path badalna + `docs/archive` wale docs ko sahi jagah link karna.
  4. `CHALU_KAAM.md` mein stale plan-file path (`docs\archive\...`).
  5. Phone par asli jaanch (Phase 3, step 3.7) — abhi 2 din se device link nahi hua.
- Har fix ke liye pehle `tools\backup\backup.ps1 start`, phir code → build → test → asli device jaanch → `complete` + is file mein entry.

---

### [TASK-MCODE-002] Sab kuch theek: mount, docs, release build, settings

- **Date & Time:** 16 Nov 2026, 15:30 – 17:00
- **Agent & Model:** MiniMax Code (MCode, MiniMax-M3.1-Flash)
- **Prompt:** "fix karo sari chije" (TASK-MCODE-001 ki audit ke 17 points)
- **Files Inspected:** `src/CloudSSD.Mount/{MountPlanning,MountService,RcloneConfigService}.cs`, `src/CloudSSD.Diagnostics/Logging.cs`, `src/CloudSSD.Core/Settings.cs`, `src/CloudSSD.Desktop/ViewModels/ConfigViewModels.cs`, `src/CloudSSD.Desktop/App.xaml.cs` (sirf padha — RULE.md rule 5 me banned hai), `installer/build-release.ps1`, `tools/backup/backup.ps1`, `tools/live-test/pc-close.ps1`, runtime logs + Windows Event Log + `settings.json`
- **Backup Folder:** `backup/2026-11-16_1532_mcode-fix-batch-1`
- **Validation Result:** APPROVED (build 0 errors / 0 warnings, C# tests **470/470** pass (465 + 5 naye), JS tests 45/45 pass, **asli PC par install + Z:/Y: jaanch**)

#### Asli gadda kya tha (root cause, guess nahi)

| # | Problem | Asli wajah (evidence) |
|---|---|---|
| 1 | `symlinks not supported without the --links flag: /` har mount par | `MountFlagBuilder.Build` me `--links` kabhi add hi nahi hua tha. `--links` ke bina drive ka root symlink rehta hai → rclone har mount par ye error deta tha |
| 2 | Drive letter 45 s me nahi aata → "Could not restore mount" | `--vfs-refresh` poore drive ka tree walk karta hai; Google Drive API us waqt **403 Quota exceeded** de raha tha (ek session me 377 baar) → listing late hoti thi → 45 s ka timeout beech me kat deta tha, app rclone maar kar dobara shuru karta tha |
| 3 | Explorer me drive ki space galat/0 (`union root '': Statfs failed`) | Wahi symlink: root theek se resolve nahi hua → statfs fail. `--links` ke baad statfs chalta hai (16.5 TB total, 15.7 TB free) |
| 4 | Release build toot chuka tha | `installer\build-release.ps1` root par `THIRD_PARTY_NOTICES.md` maangta tha, jo restructure ke baad `docs\archive\` me chala gaya tha → `Copy-Item ... does not exist` → **release banana hi nahi ho raha tha** |
| 5 | README ke 40/40 links toote | Folder structure `Cloud-SSD/Cloud-SSD-0.7.0-source/Cloud-SSD` → `Cloud-SSD/Cloud-SSD/Cloud-SSD` badla tha, README update nahi hua |
| 6 | Log file `cloudplayx-*.log` | Purana brand naam code me bacha tha |
| 7 | `backup.ps1 complete -Files a,b,c` "0 files" batata | `powershell -File` se comma-list ek hi string banta hai → array nahi banti → kuch copy hi nahi hota tha |
| 8 | Mirror poori disk upload karne lagta hai | `settings.json`: `IncludeSystem = true`, `OnlyFolders = []` → poora C: drive (7.3 GB ka zip, node_modules) cloud me chala gaya tha |
| 9 | `settings.json` me pairing token aur media key plain-text | DPAPI use nahi ho raha tha |
| 10 | `pc-close.ps1` app ko band nahi kar pata | WPF ka MessageBox **top-level** window hai, main window ka child nahi → UIA `TreeScope::Children` se main window ke andar dhoondha hi nahi gaya; saath hi `CloseMainWindow()` `False` return karta hai (WM_CLOSE post nahi hota) |
| 11 | WER "AppTermFailureEvent" | Ye Cloud-SSD ka crash **nahi** tha — event ka payload khaali hai, `crashpad_log` wala to `MicrosoftEdgeUpdate.exe` ka hai. Pehle ka "app crash" claim galat tha; sach: app band hua tha aur 7.3 GB ka upload adhoora chhoda |

#### Kya theek kiya

**Code (C#)**
- `src/CloudSSD.Mount/MountPlanning.cs` — `--links` flag add (purane rclone par warning ke saath skip).
- `src/CloudSSD.Mount/MountService.cs` — drive-appear timeout **45 s → 120 s** (`DriveAppearTimeout`), timeout par ab Google-quota wala actionable message.
- `src/CloudSSD.Mount/RcloneConfigService.cs` — `ExplainError`: Google ke 403 quota pe "apni OAuth key banao" wala message (generic rate-limit se pehle check hota hai).
- `src/CloudSSD.Diagnostics/Logging.cs` — log file `cloudssd-<date>.log` (purane `cloudplayx-*.log` bhi prune hote hain).
- `src/CloudSSD.Core/SecretProtector.cs` (nayi) — DPAPI wrapper; `src/CloudSSD.Core/Settings.cs` — `JsonSettingsStore` save par token/media key protect, load par unprotect (purane plain files bhi chalti hain); `src/CloudSSD.Desktop/ViewModels/ConfigViewModels.cs` — diagnostics zip me disk wali (protected) settings.json.
- `src/CloudSSD.Core/CloudSSD.Core.csproj` — `System.Security.Cryptography.ProtectedData` (already cached).

**Scripts**
- `installer/build-release.ps1` — `Find-Doc` helper: doc root ya `docs\archive` dono me dhoondhta hai → **release build phir se chalta hai** (95.3 MB zip, SHA256 `1105a640…`).
- `tools/backup/backup.ps1` — `-Files "a,b,c"` comma-list ab sahi split hota hai.
- `tools/live-test/pc-close.ps1` — MessageBox desktop level par dhoondha jaata hai.

**Docs**
- README ke **teeno copies** (workspace root, git root, codebase) ke links apne base path ke hisaab se theek: **0 broken** (pehle 37/40 aur 40/40). Badge: PC **0.9.0**, phone **0.12.0** (AndroidManifest). Download links naye repo path + naye files par.
- `docs/DEV_TESTING.md`, `docs/PROJECT_POLICIES.md`, `docs/archive/SETUP.md` ke 6 toote links (0 broken ab).
- `docs/CHALU_KAAM.md` — plan paths `docs\archive\...`; `RULE.md` (3 copies) — branch `main` + `feature/multi-device` dono.
- `.work/README-DO-NOT-RERUN.md` (nayi) — kaam se lag chuke patch scripts ka list, "dobara mat chalao".
- `backup/2026-11-14_2003_…/complete/README.md` (nayi) — ye folder restore safety snapshot tha, "adhoora" nahi.

**Runtime config (user ki app data)**
- `settings.json` → `Mirror.OnlyFolders = ["C:\\Users\\Roshan\\Documents"]`, `Mirror.IncludeSystem = false`, `Mirror.Paused = true` (poori disk upload ka risk band).

**Naye tests (5)**
`Symlink_support_is_on_so_the_drive_root_is_not_a_symlink`, `The_drive_may_take_longer_than_45_seconds_to_appear`, `A_google_drive_quota_error_says_what_to_do`, `The_pairing_token_and_the_media_key_are_not_written_in_plain_text`, `An_older_settings_file_with_plain_secrets_still_loads`.

#### Asli PC par jaanch (16 Nov, naya build install karke)

| Check | Nateeja |
|---|---|
| Build + install | ✅ `Cloud-SSD 0.9.0` install hua |
| Naya log file | ✅ `cloudssd-20261116.log` bana (purana naam gaya) |
| `--links` command me | ✅ rclone command line me `--links` dikh raha hai |
| Symlink error | ✅ **0** (pehle har mount par aata tha) |
| Y: drive | ✅ 16:07:32 par "Mounted at Y:" |
| Z: drive | ⚠️ quota 403 storm (52–60 per session) me 120 s me nahi aaya; retry chal raha hai |
| Naya error message | ✅ "Google Drive API quota khatam ho gaya (403). Apni OAuth key use karo: … Switch to my own Google key" |

#### Final device test — dono drive mount hui (16:41–16:44, naya build)

| Check | Nateeja |
|---|---|
| App start | ✅ 16:41:09 (`cloudssd-20261116.log`) |
| 120 s timeout par | ✅ "The drive is still coming up (Google Drive is slow); it will appear on its own." + quota hint — **rclone is baar mara nahi gaya** |
| Y: (Each Account) | ✅ **16:43:38** "Mounted at Y:" |
| Z: (All Accounts) | ✅ **16:43:58** "Mounted at Z: (the drive appeared while the app was waiting)" |
| Asli file read (Z:) | ✅ `Z:\Google AI Studio\Building the Gemini Live Engine` → 142181 bytes in 1729 ms; `Z:\Offloaded_Files\IMG_20261113_120502_1.jpg` → 1028984 bytes in 2137 ms |
| Disk space (statfs) | ✅ Z: **15.0 TB total / 14.9 TB free** (pehle "union root '': Statfs failed") |
| Symlink error | ✅ **0** (log me sirf NOTICE "Symlinks support enabled") |
| Release build | ✅ `dist\Cloud-SSD-0.9.0-win-x64.zip` (95.3 MB), SHA256 `d931ce2a338f247c4b50753afb658251901e42d8470c7f117b25cc36aa0ac355`, app install hua |

**Naya code:** `src/CloudSSD.Mount/MountService.cs` me `WatchForDriveAsync` — timeout par rclone ko maarne ke bajaye
use zinda chhod kar watcher chalata hai (15 min tak); jaise hi drive letter aata hai, session "Mounted" ho jaata hai.
Pehle: timeout → kill → zero se restart → phir bhi letter nahi (240 s + 186 quota errors me drive nahi aaya tha).
Isi change ne Z: ko 120 s ke baad 47 s aur me la diya.

**Backup band:** `backup/2026-11-16_1532_mcode-fix-batch-1` → `complete/` (new 3, changed 19), `VALIDATION.txt` = APPROVED,
`complete/README.md` (Hinglish me), `docs/CURRENT_TASK.json` → `active:false`, `status: COMPLETED`.

#### A/B test: `--vfs-refresh` hi wajah hai? (nahi — test kiya, reject hua)

`.work\mount-flagtest.ps1` se app ke bilkul wahi flags do mounts par (pehle quoting galat thi — `--filter "- …"` aur
`--volname "…"` me space wali value split ho jaati thi, rclone "malformed rule" ke saath turant exit kar deta tha):

| Mount | Drive letter aaya | Quota 403 | Symlink error |
|---|---|---|---|
| `--vfs-refresh` ke saath (app jaisa) | ✅ **6 s** | 0 | 0 |
| `--vfs-refresh` ke bina | ✅ **3.5 s** | 0 | 0 |

Matlab `--vfs-refresh` sasti cheez hai (2.5 s ka fark), asli blocker nahi — **Google Drive API ka shared rclone
client_id** hai. Usi waqt quota khatam hota hai (ek session me 377 baar "403 Quota exceeded"), tab listing late hoti
hai aur drive letter 120 s me bhi nahi aata. Quota khul jane par drive 3.5–6 s me aa jaati hai.
Isliye `--vfs-refresh` **nahi** hataya gaya (galti ka fix nahi kiya) — fix wahi hai jo ab message me bata raha hai:
apni OAuth key. Sirf apni key se ye solve hota hai; code se nahi.

#### Jo ab bhi user ke haath me hai

1. **Apni Google key** (Cloud Drives → "How do I get a key?" → "Switch to my own Google key"). Bina iske rclone ka shared client_id 2026 me retire ho raha hai aur uski per-minute quota khatam rehti hai → drive late aati hai. Ye code se nahi, user ke Google Cloud Console se banegi.
2. **Phone par jaanch** (Phase 3, step 3.7): paired "vivo I2221" ka `LastSeen` 14 Nov 17:11 hai — 2 din se link nahi.

#### Next Steps / Remaining Work

- `pc-close.ps1`: `CloseMainWindow()` `False` deta hai (custom title bar WM_CLOSE nahi leta) → ab asli X click ka fallback hai; MessageBox desktop level par dhoondha jaata hai.
- Push sirf user ke kehne par (RULE.md rule 13).

---

### [TASK-MCODE-003] `backup.ps1` ka task-lock PS 5.1 par nahi chalta tha

- **Date & Time:** 16 Nov 2026, 16:48 – 17:00
- **Agent & Model:** MiniMax Code (MCode, MiniMax-M3.1-Flash)
- **Prompt:** (MCode ka apna finding — TASK-MCODE-002 close karte waqt mila)
- **Backup Folder:** `backup/2026-11-16_1648_mcode-backup-lock-ps51`
- **Files Modified:** `tools/backup/backup.ps1`, `docs/MCODE.md`, `docs/CHALU_KAAM.md`, `docs/CURRENT_TASK.json`

#### Kya bug tha (evidence)

TASK-MCODE-002 ka `complete` step chalne ke baad bhi `docs/CURRENT_TASK.json` me `active: true` aur
`status: IN_PROGRESS` pada raha. Wajah: `backup.ps1` me `ConvertFrom-Json -AsHashtable` — ye option **PowerShell 6+
me hai, Windows PowerShell 5.1 me nahi**. Script `powershell` (5.1) se chalti hai, to `try/catch` ne error chhupa
diya aur lock file kabhi update hi nahi hui:

- `active` kabhi `false` nahi hua → har naye task ko "Another task ... is currently in progress" warning milta.
- `validationStatus` kabhi likha hi nahi gaya → `VALIDATION.txt` ban'ta hai, par lock me nahi.
- `restore`/`complete` ka status update bhi chup-chaap fail hota tha.

#### Kya fix hua

`tools/backup/backup.ps1` me naya helper `Read-TaskLock` — lock file ko plain hashtable me padhta hai
(`$parsed.PSObject.Properties` se), jo 5.1 aur 7+ dono par chalta hai. Chaar jagah replace hua:
`start` (conflict check), `validate` (validationStatus), `restore` (RESTORED), `complete` (COMPLETED).

#### Proof

- `validate` chalane par ab `docs/CURRENT_TASK.json` me `validationStatus: "APPROVED"` likha gaya (pehle nahi hota tha).
- `complete` chalane par lock khud-ba-khud `active: false` + `status: "COMPLETED"` ho gaya (pehle manual karna padta tha).

---

### [TASK-MCODE-004] Zip khulna slow, install slow, restart pe app nahi chalta

- **Date & Time:** 16 Nov 2026, 17:15 – 18:05
- **Agent & Model:** MiniMax Code (MCode, MiniMax-M3.1-Flash)
- **User ka shikayat:** *"zip abhi bhi bahot slow open ho raha hai ya bol do open hi nahi ho raha hai … install kar raha hu … bahot time le raha hai … jabhi desktop ke restart karta hu to cloud ssd baand ho jata hai"*
- **Backup Folder:** `backup/2026-11-16_1731_mcode-mount-speed-autostart`
- **Files Modified:** `src/CloudSSD.Mount/MountService.cs`, `src/CloudSSD.Mount/MountPlanning.cs`, `src/CloudSSD.Mount/DetachedProcess.cs` (nayi), `src/CloudSSD.Core/Settings.cs`, `installer/Install-CloudSSD.ps1`, `installer/Uninstall-CloudSSD.ps1`, `tests/CloudSSD.Tests/CoreTests.cs`, `docs/MCODE.md`, `docs/CHALU_KAAM.md`
- **Validation Result:** APPROVED (build 0 errors / 0 warnings, C# tests **471/471**, release build + install + asli PC par jaanch)

#### Kya mila (har point par proof)

1. **PC restart ke baad app chalta hi nahi tha.** App me "Start Cloud-SSD with Windows" feature maujood hai
   (`src/CloudSSD.App/StartWithWindows.cs`, checkbox "Files when it is off" view me), lekin is PC par
   `HKCU\...\CurrentVersion\Run` me `Cloud-SSD` entry hi nahi thi — is liye restart ke baad app aur drive dono gayab.
   **Fix:** entry bana di (`"…\Cloud-SSD.exe" --minimized`), aur ab `installer/Install-CloudSSD.ps1` bhi ye entry
   install ke time khud likhta hai (uninstaller use hatata hai) — matlab ek baar install = jab tak uninstall na ho,
   app har restart par khud chalega.

2. **Junk upload poora bandwidth kha raha tha.** Pehle ka "accidental mirror dump" (poora local disk Drive pe gaya
   tha) abhi bhi chal raha tha: `Claude-SSD Folder/New folder/Cloud-Drive.zip` (7.3 GB) **11.6 MB/s up** le raha tha,
   jabki user ka download sirf **251 KB/s** tha. Uplink bhar gaya to zip khul hi nahi raha tha.
   **Fix:** pending-upload wale 4 cache copies (≈28 GB) Recycle Bin me bheji gayi (local cache hi thi, cloud data nahi).
   Upload **0 "Sending chunk" lines** — pura band. Baaki cache (jo files turant khulti hain) rakha gaya.

3. **Read chunk bahut chhota tha.** `--vfs-read-chunk-size 4Mi` → 63 MB archive ke liye **16 API requests**,
   aur har request quota 403 pe backoff me chali jaati thi. Ab **32 MiB** (2 requests). Runtime settings:
   `Cache.ReadChunkSizeBytes` 4 MiB → 32 MiB, `Cache.BudgetBytes` 20 GiB → 60 GiB (bada cache = dusri baar file
   turant khulti hai).

4. **App band karte hi drive mar jaati thi (asli wajah).** App rclone ko apne **pipe** se chala raha tha. App
   band hone par pipe toot'ta hai → rclone (Go program) ko EPIPE milta hai → Go usse SIGPIPE bana kar process maarta
   hai. Is liye user ne "Yes: drives stay mounted" chuna, phir bhi 1–2 min me drive gayab ho jaati thi, aur log me
   kuch nahi tha. **Controlled test:** ek `rclone rc` child pipe ke saath start karke parent ko maara → child bhi
   mara (reproduce hua). **Fix:** naya `src/CloudSSD.Mount/DetachedProcess.cs` — `CreateProcessW` +
   `STARTF_USESTDHANDLES` se rclone ke std handles **NUL device** par point hote hain (koi pipe nahi, toot' hi nahi
   sakta). **Proof:** app ko maar kar 60 s baad bhi rclone zinda hai aur `Z:` mounted hai; app band rahne par bhi
   file read hui (1 MB in 1277 ms); app dobara start karne par "Re-attached to running mount at Z:/Y:" aaya.

5. **Ek folder poori tarah khaali dikh raha tha** (files cloud me maujood, direct `rclone lsf` me 15 items) —
   ye stale **dir-cache** tha: quota 403 me fail hua listing "khali" cache ho gaya tha aur 72 h tak wahi rehta.
   Cache clear karke 14 items wapas aaye. Isi liye `--vfs-refresh` **hataya nahi** gaya (try kiya tha — usi se ye
   khaali-folder ka case aaya): galat listing slow mount se kahin zyada buri hai.

6. **Asli blocker quota hi hai (code se nahi hoga).** Log: `403 Quota exceeded for quota metric 'Queries' …
   consumer 'project_number:202264815644'` — ye rclone ka **shared** client_id hai, poori duniya ke saath share.
   Quota khatam hone par download **4 KB/s** tak gir gaya (measured), is liye cold read 181–222 s lag raha hai.
   Cache hit par random read **24 ms** hai — matlab "local file jaisa tez" tabhi hota hai jab file cache me ho.
   **Solution sirf user ke haath me hai: apni OAuth key** (Cloud Drives → "How do I get a key?" → "Switch to my own
   Google key"). Ye ek baar karne ke baad 403 storm khatam aur drive 3–6 s me aane lagti hai.

#### Naya test

`The_mount_reads_in_big_chunks_and_refreshes_the_listing` — 32 MiB chunk aur `--vfs-refresh` dono jaanchte hain.

#### Release build

`dist\Cloud-SSD-0.9.0-win-x64.zip` (95.3 MB), SHA256 `998ee920d57d6323b379ab1778697f2b58b60baa6f20ca26d08ed0529a294084`, install ho chuka hai.

---

### [TASK-MCODE-005] Zip hi nahi khul raha tha — asli wajah: `--vfs-cache-mode full`

- **Date & Time:** 16 Nov 2026, 18:20 – 18:50
- **Agent & Model:** MiniMax Code (MCode, MiniMax-M3.1-Flash)
- **User ka shikayat:** *"zip abhi bhi bahot slow open ho raha hai ya bol do open hi nahi ho raha hai … abhi bhi wahi problem hai kuch bhi fix nahi huaa hai"* (quiz se confirm: **"Zip open hi nahi hota / bahut slow"**)
- **Backup Folder:** `backup/2026-11-16_1834_mcode-fast-browsing-mode`
- **Files Modified:** `src/CloudSSD.Core/Settings.cs`, `src/CloudSSD.Mount/MountPlanning.cs`, `src/CloudSSD.Desktop/ViewModels/CloudDrivesViewModel.cs`, `src/CloudSSD.Desktop/Views/CloudDrivesView.xaml`, `tests/CloudSSD.Tests/CoreTests.cs`, `docs/MCODE.md`, `docs/CHALU_KAAM.md`
- **Validation Result:** APPROVED (build 0 errors / 0 warnings, C# tests **472/472**, release build + install + asli PC par jaanch)

#### Asli gadda (measured, guess nahi)

Windows koi bhi zip kholte waqt uska **end** padhta hai (zip ka "file list" wahan hota hai). Par drive
`--vfs-cache-mode full` par chalti thi, aur us mode me rclone koi bhi file kholte hi **poora file** download
kar leta hai. A/B test (same file, same machine):

| Mount | 1 MiB ka end padha | Actually kitna data utra |
|---|---|---|
| `--vfs-cache-mode full` (pehle) | **38.8 s** | **460 MB** (poora zip!) |
| `--vfs-cache-mode writes` | **5.3 s** | **0 MB** (sirf maanga gaya range) |

rclone log me bhi confirm: `vfs cache: stopping download thread as it timed out` — pura file gir raha tha.

**Isi se do bade nuksan hote the:**
1. Zip/installer kholna = poori file ka download (460 MB zip = 460 MB) → "zip khulta hi nahi".
2. Har folder listing par ArchiveWarmer har archive ka end padhta hai → full mode me usse bhi **poora archive**
   download ho jaata tha → quota jharta tha (log: 305,051 lines, cache 13.7 GB) → phir sab kuch crawl karta tha.

#### Kya fix hua

- `CacheSettings.VfsCacheMode` (naya): `"full"` (default — games ke liye, file disk par rehti hai) ya `"writes"`
  (fast browsing — sirf likhi hui file disk par, reads seedhe cloud se).
- `MountPlanning` usi hisaab se `--vfs-cache-mode` set karta hai (unknown value → "full").
- **UI toggle**: Cloud Drives page par "Drive mode" card → checkbox "Fast browsing" + "Apply and reconnect"
  (mode mount shuru hone par tay hota hai, is liye button drives ko reconnect karta hai).
- Is PC par `VfsCacheMode = "writes"` set kar diya (user ka asli kaam: drive se install karna).

#### Proof (naya build, Z: par)

- `--vfs-cache-mode writes` command me dikh raha hai ✅
- 460 MB zip ka end padhna: **21.5 s** aur **0 MB** extra (pehle 38.8 s + 460 MB) — bacha hua wait Google ke
  quota backoff ka hai (`Rate limited, increasing sleep to 16.4s`), jo apni key se khatam hota hai.
- Dusra read (buffer me): **47 ms** — local file jaisa.
- Z: ready **15 s** me; `--vfs-refresh` abhi bhi hai (khaali folder wala bug na aaye).

#### Naya test

`The_drive_mode_chooses_how_a_file_is_cached` — full/writes/unknown teeno case.

#### Release build

`dist\Cloud-SSD-0.9.0-win-x64.zip` (95.3 MB), SHA256 `cb4960b8fa07791b16e5568d944b56fabfdd1fd5b752c035280e3f626c2f1bd1`, install ho chuka hai.

---

### [TASK-MCODE-006] Read chunk 32 MiB → wapas 4 MiB

- **Date & Time:** 2026-11-16 19:05 – 19:17
- **Agent & Model:** MiniMax Code (MCode, MiniMax-M3.1-Flash)
- **Background:** TASK-MCODE-004 me chunk 4 MiB → 32 MiB kiya gaya tha. Par 32 MiB chunk me har chhoti probe
  bhi 32 MB utha leti thi — "sirf 1 MiB padho" par bhi poora 32 MB network se aata tha.
- **Backup Folder:** `backup/2026-11-16_1905_mcode-chunk-size-revert-4mib` (before + complete; APPROVED: build 0/0, C# **472/472**)
- **Files Modified:**
  - `src/CloudSSD.Core/Settings.cs` — `Cache.ReadChunkSizeBytes` default wapas `4 * ByteSize.MiB`
  - `src/CloudSSD.Mount/MountPlanning.cs` — fallback chunk `4Mi`
  - `tests/CloudSSD.Tests/CoreTests.cs` — assertion 4 MiB ke mutabiq
  - runtime `%LOCALAPPDATA%\Cloud-SSD\settings.json` → `ReadChunkSizeBytes = 4194304`
- **Proof:** 460 MB zip ka end padhna **21.5 s → 5.3 s**, extra utra hua data **0 MB**.
- **Chain safety:** 4 MiB wali asli value `1731` ke `before/` me, 32 MiB wali value `1834` ke `before/` me,
  4 MiB wali reverted value `1905` ke `before/` me — teeno versions mehfooz.
- **Imaandari ka note:** is backup ka `before/` snapshot revert ke **baad** bana tha, is liye diff
  `new 0, changed 0, removed 0` aaya. Ye backup folder me likha bhi hai.

---

### [TASK-MCODE-007] `backup.ps1` galat agent likhta tha — fix

- **Date & Time:** 2026-11-16 19:18 – 19:20
- **Agent & Model:** MiniMax Code (MCode, MiniMax-M3.1-Flash)
- **Asli gadda:** `backup.ps1` me agent/task id sirf `start` step par `TASK_INFO.json` me likhe jaate hain.
  `complete` aur `restore` steps apna **command line** value use karte the — jo default `Antigravity` hai aur
  TaskId khali. Matlab MiniMax Code ke kaam ki README me **"Antigravity"** likha jaata tha → audit trail galat.
- **Backup Folder:** `backup/2026-11-16_1918_mcode-backup-readme-identity-fix` (before + complete; APPROVED: build 0/0, C# **472/472**)
- **Files Modified:** `tools/backup/backup.ps1`
  - naya `Get-BackupMeta(dir)` — `TASK_INFO.json` ko PS 5.1-compatible tareeke se padhta hai
  - naya `Resolve-Identity(dir)` — command line > `TASK_INFO.json` > default
  - `complete` (README.md) aur `restore` (RESTORE_LOG.md) dono ab `Resolve-Identity` call karte hain
  - `-Agent` default `"Antigravity"` → `""` (taaki pata chale user ne diya ya nahi); `start` me default wahi hai
- **Proof (sandbox, end-to-end):** script ka throwaway copy banaya aur poora cycle chala —
  - `complete` → README: `Agent: MiniMax Code | TaskId: TASK-TEST-999` ✅
  - `restore` → `RESTORE_LOG.md`: `| 2026-11-16 19:20 | 2026-11-16_9999_fake-task | MiniMax Code | ... | RESTORED_OK |` ✅
  - sandbox baad me hata diya gaya
- **Live proof:** is backup ka README **khud script ne** banaya aur usme sahi likha hai.

---

### [TASK-MCODE-008] Pura end-to-end test run (verification)

- **Date & Time:** 2026-11-16 18:50 – 19:25
- **Prompt:** *"pura test kud kiya karo meri puri permission hai"*
- **Kya chala:**
  1. **Unit (C#):** 472/472 PASS
  2. **Mobile JS:** 45/45 PASS
  3. **Integration:** pehli baar chala → 28/29. Fail: `Demo_game_launches_from_cloud...` ("run the demo game install step first")
  4. **Demo game install step** (TEST_REPORT.md wala) → `DOTNET_ROOT` zaroori tha (PATH wala dotnet sirf .NET 8 runtime) → dataset 3,626 MB
  5. **Game flow** bahut aage badha: Launch 1 cold 20030 ms / 126.4 MiB, Launch 2 7942 ms, Prepare 146/146 blocks "ReadyToLaunch" 22 s, Launch 3 7109 ms
  6. **Hybrid step fail:** `UnsupportedFilesystemOperation` → `File.CreateSymbolicLink` (`ExecutionModes.cs:121`) → *"Administrator privilege required"* → Windows **Developer Mode band** tha. Elevated process me **"SYMLINK OK"** → **code sahi**, sirf OS privilege state
  7. **Developer Mode ON** (user ki permission): `HKLM\...\AppModelUnlock\AllowDevelopmentWithoutDevLicense = 1` (UAC se). Non-elevated me abhi bhi fail → **reboot/sign-out chahiye**
  8. **Elevated integration suite: 29/29 PASS** (6.08 min) — hybrid step bhi pass
  9. **Release build + install**: `dist\Cloud-SSD-0.9.0-win-x64.zip` (95.3 MB), SHA256 `8432850da5a0138e36f131d6e146dd7188ef96d77a2ecb2aa16cca665b3ae3d0`
  10. **Device test 14 sections — sab PASS** (app restart ke baad dobara chalaya, phir bhi PASS)
  11. **Regression:** app band → rclone zinda, Z:/Y: mounted, listing 122 ms → app restart → log me `Re-attached to running mount at Z:` aur `at Y:` (19:07:06), **purana rclone zinda raha**
- **Backup:** is task me koi naya code change nahi tha, is liye naya backup folder nahi (006 aur 007 me code changes cover hain).
- **Push nahi hua** (user ne nahi kaha).

---

### [TASK-MCODE-009] Zip kholna — asli gadda mila aur fix hua (warm cache + chunk limit)

- **Date & Time:** 2026-11-16 19:44 – 20:10
- **Agent & Model:** MiniMax Code (MCode, MiniMax-M3.1-Flash)
- **Prompt:** *"koi bhi zip file open karke test karo or check karo ki open ho raha hai ki nahi"*
- **Backup Folder:** `backup/2026-11-16_1954_mcode-warm-zip-cache-chunk-limit` (before + complete; APPROVED: build 0/0, C# **473/473**)

#### Do layer ka gadda

1. **`--vfs-cache-mode writes` me read cache hote hi nahi.** rclone ke apne docs (v1.75.2, `rclone mount` →
   *VFS File Caching*): *"In this mode files opened for **read only** are still read directly from the remote"*.
   Isi liye `ArchiveWarmer` ka poora prefetch **bekaar** tha — log me
   `vfs cache RemoveNotInUse … item Google AI Studio/Jarvis.zip not removed, freed 0 bytes`.
   Har naya open = naya download = Google ke shared quota ki line me.
2. **`--vfs-read-chunk-size-limit 256Mi` chunk ko double karta tha.** rclone har read par chunk badhata hai
   (4 → 8 → 16 … → limit). Isi liye `full` mode "poora file utar deta tha" — TASK-MCODE-005 me isi liye
   `writes` par switch kiya gaya tha.

#### A/B (3 alag mount, naya cache, same remote, same quota)

| Mount | 1 MB zip COLD | 460 MB zip COLD | 7 GB zip COLD | WARM (re-open) | Cache me data |
|---|---|---|---|---|---|
| `writes` (purana setting) | 22,387 ms | 4,706 ms | 4,036 ms | 3,279 / 5,307 / 5,027 ms | **0 MB** |
| `full` + limit `256Mi` (purana limit) | 4,596 ms | **38,383 ms** | 3,544 ms | 16 / 23 / 14 ms | 0.7 MB |
| **`full` + limit `4Mi` (fix)** | 13,038 ms | **4,328 ms** | 4,330 ms | **14 / 10 / 11 ms** | 0.8 MB |

Control run ne dikhaya ki **limit hi deciding factor hai**: wahi 460 MB zip ka end padhna 38.4 s vs 4.3 s.
Data dono cases me ~0.8 MB hi aaya (sparse file).

#### Kya badla

- `src/CloudSSD.Mount/MountPlanning.cs` — `--vfs-read-chunk-size-limit` ab chunk size ke barabar (4 MiB);
  chunk aur limit ek hi variable se aate hain.
- `src/CloudSSD.Desktop/ViewModels/CloudDrivesViewModel.cs` — naya static `CacheModeFor(bool)`; toggle ka
  mapping **palat diya** (pehle "Fast browsing" ON = `writes` = kuch nahi rakhna = har open dubara download).
  Ab ON = `full` (jo padha rakhna → dobara kholna instant), OFF = `writes`.
- `src/CloudSSD.Desktop/Views/CloudDrivesView.xaml` — card ka text ab sach bolta hai.
- `tests/CloudSSD.Tests/CoreTests.cs` — naya test + chunk-limit assertion (**473/473**).
- Runtime `settings.json` → `Cache.VfsCacheMode = "full"`.

#### Proof (naya install, asli Z:)

Teen **fresh** zips (cache me pehle se nahi):

| Zip | COLD | WARM | WARM2 |
|---|---|---|---|
| MYRA_Android16_Updated_Project.zip (5 MB) | 3,884 ms | **48 ms** | 19 ms |
| MYRAA MOBLIE (2).zip (25 MB) | 40,140 ms | **20 ms** | 20 ms |
| NOVA AI.zip (60.89 MB) | — | list **127 ms**, 1761 entries | andar ka 11.7 MB APK extract **169 ms** |
| EduPulse 460 MB zip | — | list **19,958 ms**, **17,979 entries** | — |

Device test 14 sections — sab PASS. Zip open ab **46 ms** (NOVA AI) / 1504 ms (460 MB zip).

#### Do side findings

- **`Jarvis.zip` zip hi nahi hai** — uski pehli bytes `<!doctype html>` hain (Google AI Studio ka saved web
  page, 67 KB, galat naam). Cloud me user ki hi file galat hai; code me koi gadda nahi.
- **Z: ka size 1,024 TB dikha** — ye `--vfs-disk-space-total-size` ki wajah se nahi: quota khatam hone par
  rclone ka `about` call fail hota hai (`HTTP token bucket error: context deadline exceeded` →
  `union root '': Statfs failed`) aur woh 1 PiB fallback dikhata hai. Cosmetic, quota-dependent.

#### Release

`dist\Cloud-SSD-0.9.0-win-x64.zip` (95.3 MB), SHA256 `05741b5931b4b826d0f0343cd2abd7b2e7bbdd92794a2124561d148e764d6919` — install ho chuka hai.

- **Push nahi hua** (user ne nahi kaha).

## TASK-MCODE-010 - README + docs/HOW-IT-WORKS.md (how it works, real code ke mutabiq)

**Task:** `mcode-how-it-works-readme` · **Backup:** `2026-11-16_2036_mcode-how-it-works-readme` (new 1, changed 1, removed 0 · Validation **APPROVED**)

#### Kya tha request

Antigravity ne 8-9 ghante pehle README me "How it works" likha tha, wo **fictional** tha (marketing style).
User ne kaha: "mene usko ... pura gand mar diya tha" aur 5 asli examples diye:

1. App khola, email login kiya → pura system kaise chalega
2. 2 login kiya → kaise hoga
3. Phone se connect karke file transfer kiya → kaise kaam karega
4. Phone ↔ laptop
5. QR code kaise aata hai

#### Kya kiya

**Naya file:** `docs/HOW-IT-WORKS.md` (49,999 bytes, 915 lines, UTF-8 BOM + CRLF) — **12 flows**, har ek ke saath:

- exact `file:line` anchor (`src/CloudSSD.Mount/RcloneConfigService.cs:314` aisa)
- real HTTP route (`POST /api/fs/write`, `POST /api/pair?pair=…`) ya real rclone command
- measured numbers jahan number maani ka claim hai
- aur ek honest line — kya *nahi* hota

Flows: 1 login · 2 doosra login · 3 drive letters + rclone command · 4 chunked read + cache ·
5 QR code · 6 phone?file transfer · 7 phone↔laptop · 8 "PC off" mirror · 9 gaming prepare/launch ·
10 game stream (video) · 11 memory/space/offloader · 12 security + storage.

**README (root + codebase, dono):**

- Purana "How it works" section (Archify diagram + 12 fictional Deep Dive flows, ~435 lines) **hata diya**;
  uski jagah **12 honest flows ka condensed version** + link to `docs/HOW-IT-WORKS.md`.
- "Architecture" section ka **fake mermaid** (PKCE, mTLS 1.3, "5-15TB Pool", "Sparse VHDX", "3-5 GB RAM Drop",
  "Markov ML", `psapi.dll!EmptyWorkingSet`, `FSCTL_SET_SPARSE`, `IRP_MJ_READ`) → **asli architecture map**
  (`rclone` + `WinFsp` + app ke projects, koi kernel-driver claim nahi).
- Phone-connect mermaid ab QR + 6-digit dono dikhata hai, aur dono ke rules (one-time / 10 min / LAN-only).
- Security bullets: "stored encrypted (Windows DPAPI)" → **rclone ki config encryption + DPAPI-protected password**;
  "listens on the local Wi-Fi only" → **all interfaces par bind, par home-network/Tailscale ke alawa refuse**;
  naye bullets — PC sirf **token ka SHA-256 hash** rakhta hai, aur bridge **plain HTTP** hai (TLS nahi).
- Features table: Smart Cache (rclone ke sparse cache files), PC Memory (kitna free hua measure hota hai),
  Mobile Hub (6-digit + one-time QR).
- Screenshot caption "100% Offline Save Vault / Secure IndexedDB offline storage" → **"Keep offline" copies
  phone ke app storage me** (code me `OfflinePins.java` — IndexedDB nahi).
- Download badge **69 MB → 94 MB** (asli zip 99,067,034 B = 94.5 MiB; checksum verify kiya —
  `b0c4b10e…` record ke saath match karta hai). APK badge 1.2 ? **1.4 MB** (1,480,213 B).
- Docs table me `docs/HOW-IT-WORKS.md` pehli row.

#### Fake claims jo theek kiye (code se verify karke)

| Purana claim | Reality | Proof |
|---|---|---|
| "Google OAuth2 PKCE" + app ka loopback listener | **PKCE nahi.** rclone ka apna browser flow; app sirf rclone ka rc driver | `src/` me `pkce`/`authorize` = 0 hits |
| "Local LAN mTLS 1.3" | **Koi TLS nahi** — plain `TcpListener` + hand-written HTTP/1.1 | `SslStream`/`Tls12`/`Tls13` = 0 hits in `src/` |
| "3-5 GB Instant RAM Drop" | Koi byte target nahi; freed amount har run me **measure** hota hai | `MemoryRelief.cs:91-95` |
| "psapi.dll!EmptyWorkingSet" | `kernel32.dll!` **`K32EmptyWorkingSet`** | `MemoryHealth.cs:392-393` |
| "Markov ML Access Predictor" | First-order Markov chain, "Deterministic and fully explainable" | `Predictions.cs:26-31` |
| "Sparse VHDX on C:" | Koi VHDX nahi — rclone ke sparse `vfs/` files | `vhdx` = 0 hits in `src/` |
| "IndexedDB Offline Vault" | Phone ke app storage me "Keep offline" copies | `OfflinePins.java` |
| "15GB-15TB Multi-Account Pool" | Real quota = accounts ka **actual** sum (rclone `about`) | `MountPlanning.cs:107-108` |
| "Gen2 GC suppression / ArrayPool recycling" | Memory engine me koi GC-suppression nahi | 0 hits |

#### Verify

- `backup.ps1 validate` → build **0 warning / 0 error**, unit tests **473/473 PASS**, result **APPROVED**.
- Encoding: dono README UTF-8 (no BOM) + pure CRLF, 0 replacement chars; `HOW-IT-WORKS.md` UTF-8 BOM + CRLF
  (repo docs convention). Ek baar `Get-Content` (ANSI) se encoding corrupt hua tha — `git checkout` se restore
  karke ASCII-only patch scripts se dobara apply kiya.
- `docs/HOW-IT-WORKS.md` ke **saare code anchors** agents ke evidence se aaye the; maine 8 anchor spot-check kiye
  (`RcloneConfigService.cs:314/508/541`, `MemoryHealth.cs:392`, `LocalMobileBridgeService.cs:703-730`,
  `DiskSafety.cs:26-45`, `AccessControl.cs:8-11`, `GameLauncher.cs:115-135`, `MountService.cs:541`,
  `Settings.cs:62-70/186-220`) — sab match.
- Ek explore agent (file-transfer wala) **lost** ho gaya, uska kaam maine khud code padh kar kiya
  (`LocalMobileBridgeService.Files.cs`, `.Upload.cs`, `.Share.cs`, `.Peer.cs`, `Remote/*`).

**Push nahi hua** (user ne is task ke liye push nahi kaha).
