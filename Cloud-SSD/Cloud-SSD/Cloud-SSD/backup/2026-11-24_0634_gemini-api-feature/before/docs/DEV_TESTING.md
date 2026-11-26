# 🛠️ DEVELOPMENT, BUILD & TESTING GUIDE (Cloud-SSD)

> **Overview:** Consolidated developer guide covering local environment setup, build requirements, unit & integration test suites, benchmarks, compatibility, and known limitations.  
> **Source Documents Consolidated:** SETUP.md, BUILD_STATUS.md, TEST_REPORT.md, BENCHMARKS.md, COMPATIBILITY.md, KNOWN_LIMITATIONS.md.

---

# 📑 TABLE OF CONTENTS
- [1. Development Setup Guide](#1-setup)
- [2. Build Status & Verification](#2-build-status)
- [3. Test Suite Reports](#3-test-reports)
- [4. Real-World Benchmarks](#4-benchmarks)
- [5. OS & Hardware Compatibility](#5-compatibility)
- [6. Known Limitations](#6-limitations)

---

# 1. Development Setup Guide
# Cloud-SSD â€” Poora Setup (PC + Phone), Step by Step

Yeh file do logon ke liye hai:

- **Aap (insaan):** agar aapko setup karna nahi aata, to yeh file apne AI agent (Claude Code, Antigravity, Codex ya koi bhi) ko de dijiye. Wo isse padh kar PC se lekar phone tak ka pura kaam khud kar dega.
- **AI agent:** neeche ke step upar se neeche tak chalao. Har step me command, "kaise check karein" aur "fail ho to kya karein" likha hai.

Haath se karna ho to bhi yahi step kaam karte hain â€” bas command khud chalaiye.

---

## 0. AI agent ko kaise bolein (copy-paste)

Is folder me apna AI agent kholiye aur yeh likh dijiye:

```text
SETUP.md padho aur usme likha pura setup kar do â€” PC app, cloud drive aur phone app, sab.
Meri taraf se full permission hai: install, build, adb, sab command chala sakte ho.
Jahan mera click chahiye (Google "Allow", Windows "Yes", phone ka popup) wahan ek line me batao
ki kya dabana hai, aur phir aage badho. Aakhir me checklist ke saath report do.
```

---

## 1. AI agent ke liye niyam

**Permission.** Jisne aapko yeh file di hai usne is setup ke liye full permission di hai. Har command se pehle mat poochho â€” kaam karo, check karo, aage badho.

**Yeh click sirf insaan karega.** Inhe khud dabane ki koshish mat karo. Ek line me batao ki kya dabana hai, ruk kar wait karo, phir check karke aage badho:

| Kahan | Insaan kya dabayega |
|---|---|
| Windows ka "Do you want to allowâ€¦" (UAC) | **Yes** â€” WinFsp install ke liye |
| Windows Firewall ka popup | **Allow** (Private network) |
| Browser me Google ka page | Account chunna, phir **Allow** |
| Phone: "Allow USB debugging?" | **Allow** |
| Phone: "Install unknown apps" | **Allow / Install** |
| Phone app ke permission popup | **Allow** (notification, photos/files) |

**Yeh kabhi mat karo:**

- Google password ya koi bhi password mat maango, mat likho, mat save karo. Sign-in sirf Google ke apne page par hota hai.
- `%LOCALAPPDATA%\Cloud-SSD` (settings, login, cache) delete mat karo.
- Jab drive laga ho to `rclone.exe` ya `Cloud-SSD.exe` ko zabardasti band (kill) mat karo â€” upload beech me toot sakta hai. App ko uski window se band karo aur "drives lage rahein?" par **Yes** chuno.
- Bina pooche git commit / push mat karo.
- App ko project (source) folder ke andar se mat chalao â€” hamesha install folder se chalao (Step 4), warna project folder lock ho jata hai.

**Kaam ka tareeka.** Har step ke baad uska "Check" chalao. Fail ho to Section 6 dekho. Do baar fail ho to ruk kar user ko saaf batao ki kya fail hua aur kya output aaya. Aakhir me Section 5 ki checklist bhar kar do.

---

## 2. Kya chahiye

| Cheez | Zaroorat |
|---|---|
| PC | Windows 10 (1809+) ya Windows 11, 64-bit, NTFS drive |
| Internet | PC par chalu |
| Google account | Kam se kam ek (Google Drive ke liye) |
| Phone | Android 7.0 ya upar |
| Wi-Fi | Phone aur PC **ek hi Wi-Fi** par |
| USB cable | Sirf tab, jab agent phone par app khud install kare (adb se) |

Do raste hain:

- **Rasta A (Section 3):** bana-banaya installer. Sabse aasan, isi se shuru karo.
- **Rasta B (Section 4):** source code se build. Sirf tab, jab code badla ho ya sabse naya build chahiye.

Sab command **PowerShell** ke hain aur is project folder (jahan yeh `SETUP.md` hai) se chalane hain.

---

## 3. Rasta A â€” Installer se setup (recommended)

### Step 1 â€” PC check karo

```powershell
[Environment]::OSVersion.Version                # 10.0.17763 ya upar hona chahiye
[Environment]::Is64BitOperatingSystem           # True
Test-Path "${env:ProgramFiles(x86)}\WinFsp\bin\winfsp-x64.dll"          # WinFsp laga hai?
Test-Path "$env:LOCALAPPDATA\Programs\Cloud-SSD\Cloud-SSD.exe"          # app pehle se laga hai?
Get-Process Cloud-SSD, rclone -ErrorAction SilentlyContinue             # abhi chal raha hai?
```

- App pehle se laga hai aur chal raha hai â†’ Step 2 se pehle user se app ki window band karwao (drives ke sawaal par **Yes**).
- App pehle se laga hai aur aap sirf phone jodna chahte ho â†’ seedha Step 7 par jao.

### Step 2 â€” PC app install karo

Pehle installer ki jaanch (checksum):

```powershell
$exe = "releases\Cloud-SSD-0.7.0-setup.exe"
$want = (Get-Content "$exe.sha256" -Raw).Split(' ')[0]
$got  = (Get-FileHash $exe -Algorithm SHA256).Hash.ToLower()
if ($want -ne $got) { throw "Installer ka checksum galat hai - mat chalao" } else { "checksum OK" }
```

Phir install (bina sawaal ke):

```powershell
Start-Process $exe -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/TASKS="desktopicon"' -Wait
```

- Agar WinFsp nahi laga tha, to installer use khud lagata hai. Tab Windows ka **UAC popup** aayega â†’ user **Yes** dabaye.
- Silent install kaam na kare to installer ko seedha chalao (`Start-Process $exe -Wait`) aur user se Next / Install dabwao.
- WinFsp alag se lagana ho: `winget install WinFsp.WinFsp` (UAC par **Yes**).

**Check:**

```powershell
Test-Path "$env:LOCALAPPDATA\Programs\Cloud-SSD\Cloud-SSD.exe"                 # True
Test-Path "${env:ProgramFiles(x86)}\WinFsp\bin\winfsp-x64.dll"                 # True
```

### Step 3 â€” Zaroori cheezein check karo

```powershell
& "$env:LOCALAPPDATA\Programs\Cloud-SSD\cpx.exe" deps
```

Har line `[Ok     ]` se shuru honi chahiye. Jis line me problem hai uske neeche `â†’` ke baad ilaaj likha aata hai â€” wahi karo. `[Warning] Symbolic links` chalega: wo sirf Hybrid game mode ke liye hai, drive aur phone ke liye nahi.

### Step 4 â€” App chalu karo (install folder se)

```powershell
$app = "$env:LOCALAPPDATA\Programs\Cloud-SSD"
Start-Process "$app\Cloud-SSD.exe" -WorkingDirectory $app
```

Pehli baar Windows Firewall ka popup aa sakta hai â†’ user **Allow** (Private network) dabaye. Yeh phone ke liye zaroori hai.

**Check:** `Get-Process Cloud-SSD` me app dikhna chahiye.

### Step 5 â€” Google account jodo (yahan insaan chahiye)

1. App me **Cloud Drives** page kholo â†’ **Sign in with Google Drive** â†’ **Sign in with Google**.
2. Browser me Google ka page khulega. **User** apna account chune aur **Allow** dabaye. (Page par naam "rclone" dikhega â€” yeh sahi hai.)
3. Aur account jodne hon to yahi dobara karo â€” ek baar me ek account.

Agent: Google ka page khud mat bharo. User ko bolo "browser me apna account chun kar Allow dabaiye" aur Step 6 ke check se dekho ki ho gaya.

Baar-baar login maange ya "rate limit" aaye to apni khud ki Google key banao: [`docs/GOOGLE_DRIVE_SETUP.md`](archive/GOOGLE_DRIVE_SETUP.md).

### Step 6 â€” Drive check karo

```powershell
& "$env:LOCALAPPDATA\Programs\Cloud-SSD\cpx.exe" remotes     # jude hue account
& "$env:LOCALAPPDATA\Programs\Cloud-SSD\cpx.exe" mounts      # bani hui drive
Get-PSDrive -PSProvider FileSystem | Select-Object Name, Description, @{n='FreeGB';e={[math]::Round($_.Free/1GB)}}
```

Sign-in ke baad app drive khud banata hai. Naye build me do drive bante hain:

| Drive | Kya hai |
|---|---|
| **Each Account** (aksar `Y:`) | Har Google account ka alag folder |
| **All Accounts** (aksar `Z:`) | Sab account jud kar ek badi drive |

Letter alag ho sakta hai agar wo pehle se kisi aur ke paas ho. Check: File Explorer me drive khulni chahiye aur usme ek chhoti test file ban kar delete honi chahiye:

```powershell
$d = "Z:\"                                   # jo letter upar mila wo likho
Set-Content "$d\_setup-test.txt" "ok"; Get-Content "$d\_setup-test.txt"; Remove-Item "$d\_setup-test.txt"
```

### Step 7 â€” Phone par app lagao

**PC ka IP nikaalo** (phone ko yahi chahiye):

```powershell
$ip = (Get-NetIPAddress -AddressFamily IPv4 | Where-Object { $_.PrefixOrigin -eq 'Dhcp' } | Select-Object -First 1).IPAddress; $ip
(Invoke-WebRequest "http://127.0.0.1:8090/api/status" -UseBasicParsing).Content     # PC ka phone-bridge chal raha hai?
```

Phone app (APK) PC app ke saath hi aata hai: `%LOCALAPPDATA%\Programs\Cloud-SSD\wwwroot\mobile\cloudssd.apk`. Do tareeke:

**Tareeka 1 â€” USB se, agent khud lagaye (adb):**

1. User phone me: Settings â†’ About phone â†’ **Build number** par 7 baar tap â†’ Developer options â†’ **USB debugging** ON.
2. Phone ko USB se PC se jodo. Phone par "Allow USB debugging?" â†’ user **Allow** dabaye.
3. Agent:

```powershell
$adb = "$env:LOCALAPPDATA\Android\Sdk\platform-tools\adb.exe"       # na ho to: winget install Google.PlatformTools
& $adb devices                                                       # "device" likha aana chahiye, "unauthorized"/"offline" nahi
& $adb install -r "$env:LOCALAPPDATA\Programs\Cloud-SSD\wwwroot\mobile\cloudssd.apk"
& $adb shell pm list packages com.cloudssd.mobile                    # package:com.cloudssd.mobile
& $adb shell monkey -p com.cloudssd.mobile -c android.intent.category.LAUNCHER 1    # app kholo
```

**Tareeka 2 â€” bina cable ke:** phone ke browser me `http://<PC-IP>:8090/cloudssd.apk` kholo (PC-IP upar wala), file download karke tap karo â†’ **Install**. "Unknown apps" ka sawaal aaye to **Allow**.

### Step 8 â€” Phone ko PC se jodo (pairing)

1. PC app me **Mobile Hub** (Phone) page kholo. Wahan **6 ank ka code** aur QR dikhega.
2. Phone aur PC ek hi Wi-Fi par hon. Phone app kholo â€” wo PC ko khud dhoondh leta hai (port 8090). Na mile to PC ka IP haath se daalo: `<PC-IP>:8090`.
3. Phone me 6 ank ka code daalo â†’ jud gaya. Har phone ko **apni alag chaabi (key)** milti hai; PC se ek phone hatane par baaki chalte rehte hain.
4. QR code sirf **ek baar** aur **10 minute** chalta hai (phone ke browser ke liye). Dobara chahiye to Mobile Hub me **New Code** dabao.
5. Phone app jo permission maange (notification, photos/files) â†’ user **Allow** kare.

**Check:** PC app ke Mobile Hub page me phone ka naam "connected devices" me dikhna chahiye, aur phone app me PC ka naam aur drive dikhni chahiye.

**Purane phone (0.8 ya pehle):** naye PC app se judte hi phone khud apni nayi key le leta hai. Sab phone update ho jaayein to Mobile Hub me **Turn off old key** dabao.

Phone kya-kya kar sakta hai, yeh Mobile Hub ke switches tay karte hain: Browse, Stream, Upload, File management, Gaming, **Control the PC** (drive connect/disconnect, Space saver, game save restore), **PC disks** (C: jaisi PC ki apni disk â€” default OFF), **Tailscale** (ghar ke bahar â€” default OFF).

Phone par PC nahi mil raha â†’ Section 6 ki "Phone" wali line dekho.

### Step 9 â€” Phone ki optional cheezein

- **Camera backup:** phone app me Camera backup ON karo. âš™ button se folder chuno (Camera, Screenshots, WhatsAppâ€¦), "sirf charging par", "mobile data par bhi". Photo jaati hain: `Z:\Phone Backup\<phone ka naam>\<folder>\<saal-mahina>`. Tuta upload wahi se aage chalta hai.
- **Phone ke Files app me cloud:** system file picker me "All Accounts" / "Each Account" dikhte hain (thumbnail aur search ke saath).
- **Settings (phone app):** connection sheet â†’ **Settings** â€” theme (phone jaisa / Light / Dark), switches dekhna, paired phones, Space saver, Tailscale, Google account.
- **Space & RAM (dono app me):** PC app ke sidebar me **Space & RAM** page; phone app me Home ka RAM ya Storage tile. Upar device chuno (yeh phone / PC). Overview, Folders, Apps, RAM, Clean up â€” har line ka **â‹®** batata hai ki kitna khaali hoga (Recycle Bin/trash, hamesha ke liye delete, cloud par bhejo, uninstall, app band, startup se hatao). Phone par app sizes ke liye ek baar **Usage access** dena hota hai (screen par button hai). Phone ka data PC tak tab jaata hai jab phone app khula ho; phone Settings â†’ "This phone on the PC" se band ho sakta hai â€” band ya door hone par PC aakhri baar ka data time ke saath dikhata hai.
- **Ghar ke bahar (Tailscale) â€” ab automatic:**
  1. PC app â†’ Mobile Hub â†’ Permissions â†’ **Set up away-from-home** dabao. App khud Tailscale install karega (Windows ek baar poochhega), browser me Tailscale login khulega (ek baar login), phir khud "Allow Tailscale" ON karega aur firewall rule lagayega (Windows ka ek "Yes").
  2. Agar Tailscale pehle se laga aur login hai, to PC app ise khud pakad leta hai aur ON kar deta hai ("Turn it on by itselfâ€¦" checkbox).
  3. Phone par: phone app â†’ Settings â†’ **Tailscale on this phone â†’ Install/Open**, aur PC wale hi account se login.
  4. Firewall rule mana ho gaya ho to Mobile Hub ka **Copy** command ek baar admin terminal me chalao:

     ```powershell
     netsh advfirewall firewall add rule name="Cloud-SSD via Tailscale" dir=in action=allow protocol=TCP localport=8090 remoteip=100.64.0.0/10 profile=any
     ```

  5. Pehli baar phone ko **ghar ke Wi-Fi par** jodo (pairing Tailscale se nahi hota). Uske baad phone khud Tailscale par chala jaata hai jab ghar ka Wi-Fi na ho.
- **Bina PC ke (Google Drive seedha phone se):** neeche Step B5 wali Google key ke saath bana APK chahiye. Phir phone app â†’ Settings â†’ **Sign in to Google** (wahi Gmail jo PC par hain). PC band ho to Cloud SSD tab aur Files app seedha Google Drive se chalte hain; camera backup bhi.

---

## 4. Rasta B â€” Source code se build (developer / naya build)

### Step B1 â€” Tools

```powershell
dotnet --version                                   # 10.x chahiye;  na ho: winget install Microsoft.DotNet.SDK.10
winget install WinFsp.WinFsp                       # agar nahi laga (UAC par Yes)
powershell -ExecutionPolicy Bypass -File installer\fetch-rclone.ps1     # tools\rclone\rclone.exe laata hai, checksum ke saath
```

Setup `.exe` banana ho to: `winget install JRSoftware.InnoSetup`.

### Step B2 â€” Build + test

```powershell
dotnet build CloudSSD.slnx -c Release --nologo
dotnet test tests\CloudSSD.Tests -c Release --no-build --nologo        # sab test pass hone chahiye
dotnet build-server shutdown                                            # build ke worker band karo, warna folder lock rehta hai
```

Kisi aur ka build / test chal raha ho (dusra agent ya terminal) to uske khatm hone tak ruko â€” do build ek saath ek dusre ki files lock kar dete hain.

### Step B3 â€” Release banao

```powershell
powershell -ExecutionPolicy Bypass -File installer\build-release.ps1
```

Isse bante hain:

- `dist\Cloud-SSD-0.7.0-win-x64\` (folder) aur `dist\Cloud-SSD-0.7.0-win-x64.zip`
- `dist\Cloud-SSD-0.7.0-setup.exe` (agar Inno Setup laga hai)

Install: ya to setup `.exe` chalao (Step 2 jaisa), ya folder se:

```powershell
powershell -ExecutionPolicy Bypass -File "dist\Cloud-SSD-0.7.0-win-x64\Install-CloudSSD.ps1" -DesktopShortcut -Quiet
```

Uske baad Section 3 ka Step 3 se aage chalo.

### Step B4 â€” Phone app (APK) build

Chahiye: Android SDK (`build-tools 35.0.0`, `platforms\android-36`) aur JDK 17. In ke raste `mobile-android\build-apk.ps1` ke upar ki lines me **likhe hue** hain (`%LOCALAPPDATA%\Android\Sdk` aur `%LOCALAPPDATA%\Programs\jdk-17\jdk-17.0.20.1+1`). Aapke PC par alag hon to wahi 2â€“3 line badal do.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File mobile-android\build-apk.ps1      # alag process me hi chalao
Copy-Item mobile-android\Cloud-SSD-Mobile.apk src\CloudSSD.Desktop\wwwroot\mobile\cloudssd.apk -Force
Copy-Item mobile-android\Cloud-SSD-Mobile.apk releases\Cloud-SSD-Mobile.apk -Force
```

Phone app ki web files do jagah hain aur dono same rehni chahiye: `mobile-android\src\main\assets\` aur `src\CloudSSD.Desktop\wwwroot\mobile\` (test `Phone_app_assets_match_the_PC_copy` check karta hai).

`-Debug` lagao to page ko USB par Chrome (`chrome://inspect`) se dekh sakte ho; normal build me yeh band rehta hai.

### Step B5 â€” Phone par Google login (bina PC ke Drive) â€” ek baar, insaan chahiye

Yeh key **kabhi GitHub par nahi jaati** (`.gitignore` me hai).

1. [Google Cloud Console](https://console.cloud.google.com/) me **wahi project** kholo jisme PC wali key hai. **OAuth consent screen** â†’ Publishing status **In production** hona chahiye (Testing me 7 din me login toot jaata hai).
2. **APIs & Services â†’ Credentials â†’ Create credentials â†’ OAuth client ID** â†’ Application type **iOS** â†’ Bundle ID: `com.cloudssd.mobile` â†’ Create.
3. Jo **Client ID** mile (`â€¦.apps.googleusercontent.com`) use is file me daalo â€” `mobile-android\google-oauth.json`:

   ```json
   { "iosClientId": "1234567890-abc.apps.googleusercontent.com" }
   ```

   iOS client na chale to Desktop client bhi chalta hai: `{ "desktopClientId": "â€¦", "desktopClientSecret": "â€¦" }`.
4. APK dobara banao (Step B4). Build me likha aayega: `Google sign-in on the phone: ios key`.
5. Login ke waqt Google "unverified app" dikha sakta hai â†’ **Advanced â†’ Go to â€¦ (unsafe)** â†’ Allow. Yeh aapki apni app hai.

---

## 5. Aakhri checklist (agent yeh bhar kar report de)

| # | Cheez | Kaise check | Theek? |
|---|---|---|---|
| 1 | WinFsp laga hai | `Test-Path "${env:ProgramFiles(x86)}\WinFsp\bin\winfsp-x64.dll"` | |
| 2 | PC app laga hai | `Test-Path "$env:LOCALAPPDATA\Programs\Cloud-SSD\Cloud-SSD.exe"` | |
| 3 | Zaroori cheezein OK | `cpx.exe deps` | |
| 4 | App chal raha hai | `Get-Process Cloud-SSD` | |
| 5 | Google account juda | `cpx.exe remotes` | |
| 6 | Drive lagi hai, likh-padh sakte hain | Step 6 ka test | |
| 7 | Phone-bridge chalu | `http://127.0.0.1:8090/api/status` | |
| 8 | Phone par app laga | `adb shell pm list packages com.cloudssd.mobile` ya phone par icon | |
| 9 | Phone PC se juda | Mobile Hub me phone ka naam | |

Report me yeh bhi likho: kaun se step insaan ne kiye, kya skip hua aur kyun, aur kya fail hua (poora error ke saath).

---

## 6. Dikkat aaye to

| Dikkat | Wajah | Kya karein |
|---|---|---|
| "WinFsp is not installed" | WinFsp nahi laga | `winget install WinFsp.WinFsp`, phir app dobara kholo |
| Drive nahi lagi, "No free drive letter" | Saare letter bhare hain | Koi USB / network drive hatao |
| "Cloud authentication required" | Google login purana ho gaya | Cloud Drives me account dobara jodo |
| Bahut "rate limit" / file dheere khulti hai | Sab log ek hi shared Google key use karte hain | Apni key: `docs/GOOGLE_DRIVE_SETUP.md` |
| "Another instance is running" | App ya `cpx` pehle se chal raha hai | Dusra band karo |
| Phone ko PC nahi milta | Alag Wi-Fi, ya Firewall ne roka | Dono ek Wi-Fi par lao. Firewall: admin PowerShell me `netsh advfirewall firewall add rule name="Cloud-SSD Mobile Bridge" dir=in action=allow protocol=TCP localport=8090 profile=private` |
| `adb devices` me "unauthorized" | Phone par popup Allow nahi hua | Phone unlock karo, popup par **Allow** |
| `adb devices` me "offline" / khali | Phone lock ho gaya ya cable | USB debugging OFF-ON karo, cable nikaal kar lagao |
| Project folder rename / delete nahi hota ("in use") | Koi program us folder ke andar se chal raha hai | `dotnet build-server shutdown`, aur app ko install folder se chalao (Step 4) |
| Build me "file is being used" | Do build ek saath | Dusre ke khatm hone tak ruko |

Aur madad: [`docs/TROUBLESHOOTING.md`](archive/TROUBLESHOOTING.md). App ke **Diagnostics** page se "Export sanitized report" bhi nikal sakte ho (usme login / token nahi hote).

---

## 7. Kaun si cheez kahan hai

| Cheez | Jagah |
|---|---|
| PC app (program) | `%LOCALAPPDATA%\Programs\Cloud-SSD` |
| Settings, login, logs | `%LOCALAPPDATA%\Cloud-SSD` (`settings.json`, `rclone.conf`, `logs\`) |
| Command-line tool | `%LOCALAPPDATA%\Programs\Cloud-SSD\cpx.exe` (`cpx help`) |
| Phone-bridge | `http://<PC-IP>:8090` |
| Phone app ka package | `com.cloudssd.mobile` |
| Solution / projects | `CloudSSD.slnx`, `src\CloudSSD.*`, `tests\CloudSSD.Tests` |
| Installer script | `installer\build-release.ps1`, `installer\CloudSSD.iss` |
| Phone app ka code | `mobile-android\` |

Purane naam jo code me **jaan-bujh kar** rakhe hain (inhe mat badlo, app inhi se apna purana data pehchanta hai): `.cloudplayx-cache`, `cloudplayx.db`, `cloudplayx-*.log`, `CLOUDPLAYX_HOME`, `%LOCALAPPDATA%\CloudPlayX`.

---

## 8. Hatana ho (uninstall)

Windows Settings â†’ Apps â†’ **Cloud-SSD** â†’ Uninstall. Settings, login aur cache jaan-bujh kar nahi hatte; unhe hatana ho to app band karke `%LOCALAPPDATA%\Cloud-SSD` folder khud delete karo. Phone: app ko normal tareeke se uninstall karo.


---

# 2. Build Status & Verification
# Build status — Cloud-SSD 0.7.0

Legend: ✅ implemented and tested on this machine · 🟡 implemented, partially tested / tested only in simulation ·
⚪ implemented, not exercised by automated tests · ⛔ blocked (external dependency) · ❌ not implemented

Environment: Windows 11 Pro 25H2, non-admin user, Developer Mode on, i3-10105F, RX 580, 16 GB, WinFsp 2025,
rclone 1.75.2, .NET 10.0.3. Test results: [TEST_REPORT.md](archive/TEST_REPORT.md).

## M1 — Functional foundation

| Feature | Status | Notes |
|---|---|---|
| Repository, solution, 12 projects, DI-friendly services | ✅ | `CloudSSD.slnx` |
| Desktop application (WPF, MVVM, 8 pages, dialogs) | ✅ | Launched on this PC, auto-mounted a drive, live metrics rendered (screenshot verified). Pages driven by UI Automation for screenshots; most dialogs not click-tested |
| rclone integration (discovery, version, flag capability detection) | ✅ | |
| WinFsp detection | ✅ | |
| Google Drive connection (own OAuth client, rc state machine, encryption) | 🟡 | State machine verified up to the OAuth browser step with a dummy client. ⛔ End-to-end needs the user's Google OAuth client/account |
| Real drive letter mount, file reads, cache dir, integrity | ✅ | Integration tests with SHA-256 comparison |
| Settings persistence | ✅ | Atomic JSON; corrupt file recovery tested |

## M2 — Reliable cache and monitoring

| Feature | Status | Notes |
|---|---|---|
| Cache budget, presets, reserve, overrun margin | ✅ | Eviction and low-disk eviction verified with real rclone |
| Physical vs logical usage of sparse files | ✅ | `GetCompressedFileSizeW` + rclone range index |
| Cache reuse after restart | ✅ | 0 bytes re-downloaded |
| Live transfer statistics (rclone rc) | ✅ | Upload speed shown as Unavailable (not separable in rclone stats) |
| Disk safety rules (pause prefetch, warnings) | ✅ | |
| Restart recovery: crash auto-restart, adoption of running mounts, resume from sleep | ✅ / ⚪ / ⚪ | Crash restart tested; adoption and sleep/resume implemented, not automated |
| Network failure handling (offline, 503/429, dropped connections) | ✅ | Errors surfaced, no fabricated data, recovery verified |
| Remote file change detection | ✅ | Stale data not served |
| Write-back and pending-upload recovery | ✅ | |
| Logging, redaction, diagnostics export | ✅ / ⚪ | Redaction unit-tested; export implemented, not automated |

## M3 — Game compatibility layer

| Feature | Status | Notes |
|---|---|---|
| Game library (SQLite) | ✅ | |
| Executable detection + PE import inspection | ✅ | |
| Missing dependency detection (VC++/DirectX/etc.) | ✅ | Unit-level via `cpx pe`; demo game had none missing |
| Compatibility analyzer (anti-cheat, launchers, installers, engines) | 🟡 | Rules implemented; exercised on the demo game only |
| Launch + process-tree monitoring + failure diagnostics | ✅ | Job objects, NTSTATUS decoding, event log |
| Cloud Direct mode | ✅ | Demo game |
| Hybrid Optimized mode (copy essentials, symlink assets) | ✅ | Demo game |
| Full Local Fallback (rclone copy, approval, resumable) | ⚪ | Implemented in UI and engine; not in the automated suite |
| Optimize & Prepare with verified readiness | ✅ | Coverage verified against rclone's range index |
| Real commercial DRM-free game | ⛔ | No licensed game files available here |

## M4 — Intelligent streaming

| Feature | Status | Notes |
|---|---|---|
| Per-read telemetry (offset, size, hit/miss, wait) from rclone | ✅ | Parser unit-tested on captured lines; live in integration tests |
| Prefetch attribution (separating own reads) | ✅ | |
| Persistent access profiles | ✅ | |
| Startup, Markov, Sequential predictors; confidence; auto-disable | ✅ / 🟡 | Unit-tested; effect measured in simulation and with the demo game |
| P1–P3 queue: dedupe, reprioritize, cancel, TTL, budget, drop notifications | ✅ | |
| Foreground protection (P0), bandwidth-delay concurrency, background rate | ✅ / 🟡 | Policy unit-tested; real effect measured end-to-end |
| Just-in-time startup release during launch | 🟡 | Simulation-validated |
| Cold/warm benchmarks with measurable improvement | ✅ | Real stack over SimCloud + simulation matrix |

## M5 — Advanced game optimization

| Feature | Status | Notes |
|---|---|---|
| Game-Aware interface (pipe protocol, region learning, prediction, cancellation) | 🟡 | Unit-tested with synthetic telemetry incl. real named pipe |
| Game-specific plugins | ⛔ | No authorized integration for any title exists |
| Larger games (Cyberpunk 2077, GTA V, Black Myth: Wukong) | ⛔ | Not owned/available; research targets only |
| Fallback modes | ✅ | Hybrid + Full Local |

## M6 — Providers and release

| Feature | Status | Notes |
|---|---|---|
| TeraBox | ❌ (by decision) | No official API; see docs/TERABOX_RESEARCH.md |
| OneDrive / Dropbox / S3 / NAS via rclone | ⚪ | Mountable through existing rclone remotes; untested |
| Release packaging (self-contained ZIP, install/uninstall scripts, uninstall entry) | ✅ | See TEST_REPORT.md for the install verification on this PC |
| Inno Setup `.exe` installer | ⚪ | Script provided; Inno Setup not installed here |
| Clean-machine installer verification | ⛔ | No clean Windows VM available |
| Code signing | ❌ | |
| Documentation set | ✅ | |


---

# 3. Test Suite Reports
# Test report — Cloud-SSD 0.7.0

All tests below were **actually executed** on the development PC on 2026-10-10/11:
Windows 11 Pro 25H2 (10.0.26200), non-admin user, Developer Mode on, Intel i3-10105F, RX 580, 16 GB RAM,
WinFsp 2.1.25156, rclone v1.75.2, .NET 10.0.3. Nothing was run on a clean machine or in CI.

## Summary

| Suite | Result |
|---|---|
| Unit tests (`tests/CloudSSD.Tests`) | **54 / 54 passed** (0.7.0 release build, 2026-10-12) |
| Integration tests (`tests/CloudSSD.IntegrationTests`, real rclone + WinFsp mounts) | **27 / 27** — final full run 26 passed, 1 failed (test-design flaw, see below), fixed and re-run: passed |
| End-to-end game flow (in the integration suite) | passed |
| Release build + `cpx deps` smoke test in published folder | passed |
| Install / uninstall of the release ZIP (custom folder) | passed (see notes) |
| Desktop UI: launch, auto-mount, all 8 pages rendered via UI Automation | passed after fixing one bug (below) |
| Google Drive end-to-end | **not run** — needs the user's own OAuth client and Google account |
| Commercial games | **not run** — no licensed game files available |

## How to reproduce

```bash
dotnet test tests/CloudSSD.Tests
```

The integration suite creates a ~0.9 GB dataset under `.work/it/` on the first run. The end-to-end game test needs
the demo game copied into that dataset:

```bash
dotnet build tests/CpxDemoGame -c Release
```

```bash
powershell -Command "$g='.work/it/dataset/Games/CPX Demo Game'; New-Item -ItemType Directory -Force $g; Copy-Item tests/CpxDemoGame/bin/Release/net10.0-windows/* $g -Exclude *.pdb; & \"$g/CpxDemoGame.exe\" --generate $g"
```

```bash
dotnet test tests/CloudSSD.IntegrationTests
```

## Unit tests (52)

Units (Mbps vs MB/s, size parsing, block math), settings round trip and corrupt-file recovery, redaction of tokens /
client secrets / rc passwords / query secrets, DPAPI round trip and fail-closed, rclone log parser on **lines captured
from rclone 1.75.2** (offsets, hit/miss, wait time, EOF, errors, rate-limit and auth classification, attribution of
prefetch reads), mount flag builder (safe defaults, unsupported flags → warnings, blocked dangerous extra flags),
drive-letter allocation, cache inspector on rclone metadata, disk-safety decisions, marker-guarded cache clearing,
prefetch queue (priority order, dedupe, in-place upgrade, cancellation, TTL, budget, drop notifications, explicit
requests), scheduling policy (P0 protection), network governor (bandwidth-delay concurrency), Markov / sequential /
startup predictors, accounting and auto-disable, SQLite profile store, simulator determinism and SSD/cold sanity,
warm-profile improvement in simulation, Game-Aware region validation, neighbour scheduling, fast-travel cancellation,
heading weighting, and a real named-pipe telemetry round trip.

## Integration tests (27) — real mounts

| Test | Result | Measured detail |
|---|---|---|
| Drive visible, directory listing, Unicode/spaces | ✅ | `X:` fixed drive, WinFsp |
| Whole-file SHA-256 matches source | ✅ | |
| 40 random range reads match source bytes | ✅ | |
| Offsets beyond 2 GiB in a 3 GiB sparse file | ✅ | |
| Partial reads create a sparse cache; warm reads use no network | ✅ | 3 × 64 KiB cold reads fetched ~0.9 MiB; warm reads: 0 bytes, telemetry reported hits |
| Multiple concurrent handles | ✅ | |
| Memory-mapped read | ✅ | |
| File locking characterized | ✅ | behaviour recorded in `.work/it/locking-result.txt` |
| Executables launch from the drive | ✅ | `where.exe`, `hostname.exe` |
| Metadata (size, mtime) | ✅ | |
| Cache reused after unmount/remount | ✅ | **0 bytes** re-downloaded |
| rclone crash → automatic restart, data correct | ✅ | recovered in ~3 s |
| LRU eviction at `--vfs-cache-max-size` | ✅ | oldest file evicted, newest kept, cloud original untouched |
| Low disk space → eviction via min-free-space | ✅ | range index emptied |
| Remote file change → stale data not served | ✅ | |
| Offline: uncached read fails, cached read works, recovery | ✅ | error after 0.1 s; correct data after reconnect |
| Transient HTTP 503/429 and dropped connections | ✅ | all bytes correct with injected faults |
| Cold throughput at 10/30/70/100/500 Mbps | ✅ | 9.2 / 29.3 / 69.8 / 100.2 / 493 Mbps achieved |
| Read-only mount rejects writes | ✅ | creates fail with EROFS; DeleteFile reports success but file survives |
| Writes, rename, mkdir, delete reach the remote | ✅ | |
| Pending uploads survive an rclone crash | ✅ | uploaded after restart |
| End-to-end demo game flow | ✅ | see below |

### End-to-end game flow (AppHost + demo game, SimCloud 70 Mbps / 80 ms)

| Step | Result |
|---|---|
| Add game from the drive | exe detected, "No blockers found", Hybrid recommended |
| Launch 1, cold, Cloud Direct | load 20.3 s, 115 MiB from cloud, profile saved |
| Launch 2, new level | load 8.2 s (startup + menu cached) |
| Cache cleared → Optimize & Prepare | measured 73.7 Mbps; 145 planned blocks, 140 fetched (139 MiB) in 21 s; **145/145 verified in rclone's cache index** → "Ready to launch" |
| Launch 3, unseen level | load 9.2 s; startup 0.29 s + menu 1.2 s (cold: ~1.1 s + ~10 s); level data streamed |
| Hybrid build + launch 4 | exe/DLLs copied, archives symlinked; load 5.6 s from the local folder |

## Bugs found by testing and fixed

1. **False readiness** — Prepare reported "Ready, 100 % cached" after fetching 0 bytes because in-memory
   "known cached" entries survived a cache clear. Fixed: entries expire, are dropped on unmount, and readiness is
   verified against rclone's on-disk range index.
2. **Prepare hang** — queued items that expired/were cancelled never reported back. Fixed: every unfetched exit
   raises a drop notification; explicit Prepare items do not expire; stuck detection added.
3. **Orphaned rclone pinned the parent's stdout** (test host hang). Fixed: rclone std streams are redirected and drained.
4. **Range index lost on quick quit** — rclone persists it only after handles close + 5 s. Fixed: graceful unmount
   waits for activity to settle.
5. **Startup window measured in wall time** — slow links pushed startup data out of the window. Fixed: game-progress
   time (wall time minus network waits).
6. **Launch-time prefetch of uncertain data hurt slow links** (simulation). Fixed: only consistently seen data
   (confidence ≥ 0.7) is released during a launch, just in time.
7. **Network governor under-estimated link rate** on high-latency single streams. Fixed.
8. **UI**: duplicate resource key crashed startup; Smart Cache preset buttons threw a type error. Both fixed and
   re-verified by UI Automation screenshots.
9. **Installer**: uninstaller could close any CloudPlayX process; message overstated removal. Fixed.

## Notes and failures

- The one failure in the final full run was `Transient…(dropRate: 0.3)`: the random fault injector happened to drop
  no connection in that run (all data was still verified correct), so the "a fault was injected" assertion failed.
  The test now keeps reading until a fault really occurs; re-run passed (2/2).
- Install test used a custom folder; the uninstaller's guard deliberately leaves folders whose name lacks
  "CloudPlayX" (it removed shortcuts and the uninstall entry, and now says so). Default installs are removed fully.
- During development an external, transient system-wide commit-memory exhaustion killed three rclone processes at
  once; this motivated the auto-restart feature (now tested).

## 0.7.0 setup.exe (2026-10-12)

Executed on the same PC (WinFsp already installed) with `CloudPlayX-0.7.0-setup.exe`, silent mode, custom folder:

| Check | Result |
|---|---|
| Install: files, rclone, uninstaller present; product version 0.7.0 | passed |
| Installed app starts (window "Cloud-SSD", separate test data folder) | passed |
| Setup while the app is running | refused (exit 1, AppMutex) |
| Setup while an rclone process runs from the install folder (mounted drive) | refused with the "drive still mounted" message (exit 7) |
| Upgrade over an existing install | passed |
| Uninstall removes program files and the *Installed apps* entry, keeps user data | passed |
| WinFsp auto-install from the bundled signed MSI | **not tested**: WinFsp was already installed here; needs a clean PC or VM |

---

# 4. Real-World Benchmarks
# Benchmarks

Two kinds of benchmark, both reproducible with `benchmarks/CloudSSD.Benchmarks`. Raw outputs (Markdown + JSON) are
in `benchmarks/results/`. Nothing here is estimated or hand-edited.

```bash
dotnet run -c Release --project benchmarks/CloudSSD.Benchmarks -- real --mbps 70 --latency 80
```

```bash
dotnet run -c Release --project benchmarks/CloudSSD.Benchmarks -- sim --scale 0.25
```

## 1. Real end-to-end (measured)

**Setup:** the real Cloud-SSD stack (AppHost, rclone 1.75.2, WinFsp, prediction engine) on the i3-10105F test PC.
The "cloud" is SimCloud, a local HTTP server throttled to **70 Mbps with 80 ms per-request latency** (Google Drive was
not available for automated runs). Workload: the DRM-free **CPX demo game** loading level 2 — 409 reads, 54.5 MiB,
from 4 × 96 MiB archives. Load time is measured by the demo game itself. Each cloud scenario starts with an empty
cache unless stated. Two complete runs:

| Scenario | Run 1 load | Run 2 load | Cloud bytes (run 2) | HTTP requests | Reads ≥ 50 ms | rclone peak RAM |
|---|---|---|---|---|---|---|
| **A** Physical SSD | 0.09 s | 0.10–0.12 s | — | — | 0 | — |
| **B** Standard rclone mount (rclone defaults: buffer 16M, chunk 128M), cold | 56.5 s | 65.2 s | 551 MiB | 65 | 89 | 142 MiB |
| **C** Cloud-SSD cold (no profile) | 21.0 s | 20.3 s | 117 MiB | 164 | 168 | 57 MiB |
| **D** Cloud-SSD warm: 2 profiled launches, cache cleared, Optimize & Prepare, *unseen* level | 8.2 s | 7.7 s | 39 MiB during play (+179 MiB in a 31.9 s Prepare) | 43 | 39 | 58 MiB |
| **D′** Cloud-SSD fully warm cache | 0.15 s | 0.27 s | ~0 | 0 | 0–1 | 58 MiB |

Phases (run 2): B startup/menu/level 3.6 / 44.7 / 15.7 s; C 1.1 / 9.7 / 8.6 s; D 0.29 / 1.13 / 6.2 s.

**Verdicts vs. baselines**

- vs. **A (SSD)**: cold and prepared cloud launches are **much worse** (20 s and 8 s vs 0.1 s). Fully cached is
  close but still slower (0.15–0.27 s vs 0.10 s — VFS overhead).
- vs. **B (standard rclone)**: C is **better** (≈3× faster, 4.7× fewer bytes). This gain comes mostly from the
  tuned VFS flags (`--buffer-size 0`, 4 MiB chunks), not from prediction — the stock 16 MiB read-ahead downloads
  ~10× the data a random-access loader needs. D is **better** again (≈8× faster than B) because Prepare moved
  ~179 MiB of learned startup data before launch.
- CPU: rclone used 1–3 s CPU per cold launch; with `-vv` telemetry over a whole session ~7 s.
- Frame rate was not measured and is not a storage metric.

## 2. Simulation matrix (model, not a measurement)

**Setup:** deterministic discrete-event simulator running the **production** queue, scheduling policy, governor,
predictors, accounting and SQLite profile store against a virtual clock. Network: shared bandwidth at 90 % efficiency
plus fixed per-request latency; SSD 500 MB/s; 0.15 ms VFS overhead per cache hit. Workload: synthetic game trace
(seed 11, scale 0.25): 1,584 reads touching 904 MiB; profile trained on 3 earlier sessions with different levels; the
test session's level differs. Times are total load time.

| Link | Latency | A SSD | B rclone default | B0 buffer 0, no engine | C CPX cold | D CPX profile, no Prepare | D+ profile + Prepare (Prepare time) | D+ with 256 MiB cache |
|---|---|---|---|---|---|---|---|---|
| 10 Mbps | 150 ms | 1.7 s | 4860 s | 959 s | 960 s | 959 s | **300 s** (891 s) | 994 s |
| 30 Mbps | 150 ms | 1.7 s | 1620 s | 397 s | 397 s | 394 s | **126 s** (341 s) | 419 s |
| 70 Mbps | 150 ms | 1.7 s | 698 s | 236 s | 237 s | 233 s | **76 s** (184 s) | 243 s |
| 70 Mbps | 40 ms | 1.7 s | 694 s | 153 s | 154 s | 155 s | **49 s** (136 s) | — |
| 100 Mbps | 150 ms | 1.7 s | 489 s | 200 s | 200 s | 196 s | **65 s** (148 s) | 203 s |
| 500 Mbps | 150 ms | 1.7 s | **110 s** | 133 s | 130 s | 123 s | **43 s** (81 s) | 129 s |

Hit rate: B0 52 %, D 55 %, D+ ≈ 85 % at every link speed. Prediction accuracy 88–94 % (D), ~72 % (D+).

**What the simulation says (and its limits)**

1. **Prediction does not create bandwidth.** Without Prepare, a learned profile is neutral (−8 % … +2 %) because the
   game's own demand already saturates the link.
2. **Prepare moves downloading before PLAY** — ~3× shorter loads, at the cost of a preparation step that itself takes
   roughly the time of the data it fetches.
3. **The cache must hold the working set**; with 256 MiB the prepared data was evicted before use.
4. **rclone's default read-ahead is very wasteful at ≤ 100 Mbps** for random-access loads (5 GiB fetched for 904 MiB
   needed) **but wins at 500 Mbps**, where latency dominates. Users on very fast links may set `--buffer-size`
   4–16 MiB in Settings → Advanced. (Adaptive tuning is future work.)
5. Model assumptions (equal bandwidth sharing, fixed latency, synthetic trace) are simplifications; the real benchmark
   above is the ground truth for this project.

## Methodology rules

No numbers are invented or adjusted. Each run writes a timestamped file in `benchmarks/results/`. Variance between
runs (e.g. B: 56.5 s vs 65.2 s) is reported rather than averaged away.


---

# 5. OS & Hardware Compatibility
# Compatibility

Only behaviour that was actually executed is listed as verified. Everything else is labelled honestly.

## Test environment

| Item | Value |
|---|---|
| OS | Windows 11 Pro 25H2 (10.0.26200), user account without administrator rights, Developer Mode on |
| CPU / GPU / RAM | Intel Core i3-10105F / AMD Radeon RX 580 2048SP / 16 GB |
| WinFsp | 2.1.25156 (2025) |
| rclone | v1.75.2 (bundled, checksum-verified) |
| .NET | 10.0.3 |
| "Cloud" used in tests | SimCloud (local HTTP server, 127.0.0.1) mounted via rclone's `http` backend; rclone `local` backend for write tests |

**Google Drive was not tested end-to-end in development** because it needs the user's own OAuth client and Google
account. The connection workflow (rclone rc `config/create` state machine) was exercised up to the OAuth step with a
dummy client; the mount, cache and streaming layers are backend-independent and were tested through SimCloud.

## Filesystem behaviour on the virtual drive (rclone 1.75.2 + WinFsp, VFS full cache)

| Operation | Result | Evidence |
|---|---|---|
| Drive letter visible, `DriveInfo.IsReady` | ✅ verified | integration test |
| Directory listing, Unicode and spaces in names | ✅ verified | integration test |
| Whole-file reads, SHA-256 identical to source | ✅ verified | integration test |
| Random range reads (40 random offsets/sizes) | ✅ verified | integration test |
| Offsets beyond 2 GiB / 3 GiB file | ✅ verified | integration test (sparse 3 GiB file) |
| Multiple concurrent handles | ✅ verified | integration test |
| Memory-mapped reads | ✅ verified | integration test |
| File timestamps and sizes | ✅ verified | integration test |
| Executable launch from the drive | ✅ verified (requires `--file-perms 0777`, set by Cloud-SSD) | integration test (`where.exe`, `hostname.exe`, demo game) |
| Executable launch through a symbolic link into the drive | ✅ verified | probe + Hybrid launch |
| Writes, rename, mkdir, delete (writable remote) | ✅ verified | integration test (local backend) |
| Pending uploads survive an rclone crash | ✅ verified | integration test |
| Read-only mount rejects creates | ✅ verified (EROFS). Note: `DeleteFile` reports success but the file survives (delete-on-close fails at cleanup) | integration test |
| Share modes / byte-range locks | ⚠ characterized, see `.work/it/locking-result.txt` after a test run; WinFsp/rclone do not provide NTFS locking semantics across processes reliably | integration test (records behaviour) |
| NTFS alternate data streams, hard links, junctions *inside* the drive, ACLs, compression, EFS | ❌ not supported by rclone VFS | rclone documentation |
| Change notifications for remote edits | Depends on the backend (Google Drive polls every minute). `vfs/forget` (Refresh listing) forces re-validation; stale data was **not** served after a remote change | integration test |

## Execution modes

| Mode | Status |
|---|---|
| Cloud Direct | ✅ verified with the CPX demo game and Windows console tools |
| Hybrid Optimized | ✅ verified with the CPX demo game (exe/DLLs/config copied, archives symlinked); needs Developer Mode or admin |
| Full Local Fallback | ✅ implemented (rclone copy, resumable, explicit approval). Copy path exercised in code review; not part of the automated suite |

## Applications and games

| Title | Mode | Result |
|---|---|---|
| `where.exe`, `hostname.exe` (portable Windows tools) | Cloud Direct | ✅ ran, correct output |
| CPX Demo Game (DRM-free test program in `tests/CpxDemoGame`, ~384 MiB of archives) | Cloud Direct, Hybrid | ✅ ran from the drive; profile learned; Prepare verified; load times in BENCHMARKS.md |
| Any commercial game | — | **Not tested.** No licensed game files were available in the development environment. |
| Cyberpunk 2077, GTA V, Black Myth: Wukong | — | **Research targets only, not supported claims.** Expected issues: launcher/ownership checks (Steam/Epic/Rockstar), very large packed archives with scattered access, and (for some editions/online modes) anti-cheat. Treat each as its own compatibility project; Full Local is the safe path. |

## What the compatibility analyzer checks

Executable candidates (PE headers only), architecture, imported DLLs vs. game folder and System32/SysWOW64 with
redistributable suggestions, Steamworks/EOS/Ubisoft markers, kernel anti-cheat markers (EasyAntiCheat, BattlEye,
Vanguard, PunkBuster, XIGNCODE, nProtect), installers vs. installed games, engine hints (Unity, Unreal, RAGE,
REDengine, Source), file counts and long paths. A game is only marked **Verified** after a monitored launch ran for at
least 60 seconds without a failure signature.


---

# 6. Known Limitations
# Known limitations

These are real, current limitations — measured or confirmed, not hypothetical.

## Physics of the network

- **Uncached data arrives at internet speed.** 70 Mbps is at most 8.75 MB/s (measured 69.8 Mbps through the mount on
  a 70 Mbps simulated link). No caching or prediction makes a cold read as fast as an SSD. Cloud-SSD approaches SSD
  behaviour only for data that is already cached.
- **Prediction does not create bandwidth.** In the simulation benchmarks, a learned profile alone barely changed load
  time while the game itself saturated the link; the large gain came from *Optimize & Prepare*, which downloads the
  startup set **before** launch (simulated at 70 Mbps / 150 ms: 184 s of preparation cut a 236 s load to 76 s; measured with the demo game: a 32 s Prepare cut a 20 s cold load to 7.7 s). Total download time is the
  same; it is moved out of the loading screen.
- **Unseen content streams on demand.** A level never loaded before is not in any profile; its data is fetched when
  the game asks for it (demo game: level phase 7.6 s on a cold cache vs ~0.01 s on SSD).
- **A cache smaller than the working set defeats preparation** (simulated: prepared data was evicted before use).

## rclone / WinFsp behaviour (rclone 1.75.2)

- The cache size limit is **not instantaneous**: open files are never evicted and cleanup runs every poll interval
  (Cloud-SSD uses 30 s), so usage can briefly exceed the limit. A 10 % margin is reserved and Cloud-SSD pauses its
  own prefetching under disk pressure.
- **Range index persistence:** rclone writes a file's downloaded-range index only after the last handle closes plus
  its 5 s handle-caching delay. After a crash or forced kill, data for files open at that moment stays on disk but is
  treated as uncached and downloaded again. Graceful unmount waits for activity to settle before quitting.
- **First read after reconnecting may fail** for ~5 s (errored handle in rclone's handle cache), then recovers with
  correct data. Errors are always surfaced; data is never fabricated.
- `vfs/stats.inUse` stays at 1 on an idle mount, so idleness is judged from telemetry instead.
- rclone's stats do not separate upload and download speed: **upload speed is shown as Unavailable** (pending upload
  count is shown).
- When an rclone on-the-fly remote's connection parameters change (e.g. a different URL/port), rclone uses a
  different cache sub-folder. Named remotes such as `gdrive:` are stable.
- No NTFS alternate data streams, hard links, ACLs, EFS or reliable cross-process byte-range locking on the drive.
- Windows `DeleteFile` on a read-only mount reports success although the file is not deleted.

## Telemetry

- Per-read telemetry comes from rclone's debug log (`-vv`). It is exact (offset, size, rclone's own hit/miss verdict,
  wait time) but costs some CPU and log I/O while profiling is enabled (log rotates at 64 MiB). It can be turned off in
  Settings, which also disables profiles and hit/miss metrics.
- rclone does not record which process issued a read. Cloud-SSD separates its own prefetch reads by registering
  each prefetch's byte range and time window; a game read of the same range during that window would be counted as
  prefetch (rare, minor effect on accuracy figures).
- Windows Defender's on-access scan reads the start and end of files when they are opened; on a cold cache these
  reads cause extra downloads. Cloud-SSD does not change security settings.
- ETW file I/O tracing was not used: it requires administrator rights, and rclone's log already provides byte-exact
  VFS-level data.

## Games

- **No commercial game has been tested.** Only the DRM-free CPX demo game and Windows console tools were executed.
- Launcher-managed games (Steam, Epic, Ubisoft, Rockstar) generally need their launcher and a local installation;
  Cloud-SSD never bypasses ownership checks, DRM or anti-cheat.
- Kernel anti-cheat titles are flagged "Requires local installation".
- "Verified" requires a monitored run of ≥ 60 s; short test programs never become Verified.
- Hybrid mode needs Developer Mode (or admin) for unprivileged symbolic links. Writes into a *symlinked* file go to
  the cloud drive (and fail on read-only mounts); new files and copied files are local.
- Pinning is best effort: rclone evicts by least-recent access, so pinned files are kept "recent" by touching them;
  a budget smaller than the pinned set still evicts.
- **Game-Aware Mode has no real game integration yet.** The pipe protocol, region learning and scheduling are
  implemented and unit-tested with synthetic telemetry only.

## Providers

- **Google Drive end-to-end was not exercised in development** (requires the user's OAuth client and account).
  The OAuth state machine was tested up to the browser step with a dummy client.
- Google OAuth apps left in *Testing* mode expire refresh tokens after 7 days.
- **TeraBox is not supported** (no official API; see docs/TERABOX_RESEARCH.md).
- OneDrive, Dropbox, S3 and NAS work through rclone but are untested by Cloud-SSD and must be configured with
  `rclone config` against Cloud-SSD's config file.

## Packaging

- The release is a self-contained per-user ZIP with an install script (Start-menu shortcut, optional desktop
  shortcut, uninstall entry). An Inno Setup `.exe` is produced only if Inno Setup is installed; it was not available
  in the build environment.
- The installer was verified on the development machine only, not on a clean Windows installation.
- The binaries are not code-signed; Windows SmartScreen may warn on first start.

