# ⚡ RAM & CLOUD STORAGE GUIDE (Cloud-SSD)

> **Overview:** Unified guide for Laptop RAM / Virtual Memory optimization and Cloud Storage (Google Drive / Multi-Drive Pooling) in Cloud-SSD.  
> **Target OS:** Windows 10 / Windows 11 x64  
> **Source Documents Consolidated:** RAM.md, VIRTUAL_RAM_EXPLAINED.md, GOOGLE_DRIVE_SETUP.md, GOOGLE_KEY_KAISE_BANAYEIN.md, TERABOX_RESEARCH.md.

---

# 📑 TABLE OF CONTENTS
- [PART 1: RAM & VIRTUAL MEMORY](#part-1-ram--virtual-memory)
  - [1.1 Seedha Jawab: Kya Cloud se RAM ban sakti hai?](#11-seedha-jawab)
  - [1.2 Speed & Latency Comparison](#12-speed-comparison)
  - [1.3 Memory Compression & Pagefile Settings](#13-memory-settings)
- [PART 2: CLOUD STORAGE & GOOGLE DRIVE INTEGRATION](#part-2-cloud-storage--google-drive-integration)
  - [2.1 Google Drive Connection via rclone](#21-google-drive-connection)
  - [2.2 Apni Google Key Kaise Banayein](#22-apni-google-key-kaise-banayein)
  - [2.3 Multi-Drive Pooling & Storage Expansion](#23-multi-drive-pooling)
  - [2.4 Alternative Storage (TeraBox Research)](#24-terabox-research)

---

# PART 1: RAM & VIRTUAL MEMORY

# âš¡ RAM.md â€” Laptop RAM & Virtual Memory Guide (Cloud-SSD)

> **Topic:** Kya Storage ki tarah Laptop me RAM badhai ja sakti hai?  
> **Applicable for:** Windows 10 / Windows 11 (x64)  
> **Status:** Reviewed aur numbers is PC par naape gaye â€” 2026-11-10  
> **Last Updated:** 2026-11-10  

---

## ðŸ’¡ 1. Seedha Jawab: Kya Cloud se RAM ban sakti hai?

**Nahi.** Cloud/Internet se RAM nahi ban sakti.

Local SSD par **Pagefile (Virtual Memory)** se sirf itna hota hai ki RAM full hone par game/app **crash nahi hota**. Isse **speed nahi badhti** â€” jab Windows pagefile use karta hai tab game atakta (stutter) hai.

RAM sach me badhane ka ek hi tareeqa hai: **asli RAM stick lagana** (Section 5).

---

## ðŸ” 2. Cloud se direct RAM kyu nahi ban sakti? (Speed ka Sach)

| Type | Ek baar data laane ka time | RAM ke mukable | Kya Hoga? |
|---|---|---|---|
| **Real Physical RAM (DDR4/DDR5)** | 10â€“100 nanosecond (ns) | Base | Smooth gaming |
| **Local NVMe SSD (Pagefile)** | 10â€“100 microsecond (Âµs) | lagbhag 1,000 guna slow | Stutter / frame drop, par crash nahi |
| **Cloud / Internet (Google Drive)** | 100â€“500 millisecond (ms), aksar zyada | 10 lakh se kai crore guna slow | Poora Windows aur game freeze |

### Yeh kyu hota hai?
* **Storage (Cloud-SSD) me:** Game har second poori file nahi maangta. Naya sound ya texture chahiye to woh 1-2 second me download ho jata hai aur game chalta rehta hai.
* **RAM me:** CPU har second me karodon baar RAM se data padhta-likhta hai. Agar har baar internet ka intezaar karna pade to laptop poori tarah atak jayega. Ise **thrashing** kehte hain.

> âš ï¸ **Pagefile ko kabhi cloud drive (X:, Y:, Z:) par mat rakho.** Pagefile sirf laptop ki apni SSD (C:) par hona chahiye.

---

## ðŸ“ 3. Pehle Naapo: Kya RAM hi problem hai?

Sabse aasaan: Cloud-SSD app me **PC Memory (RAM)** page kholo (Section 7). Ya Windows me:

1. **Task Manager** (`Ctrl + Shift + Esc`) âž” **Performance** âž” **Memory**
   * **In use:** agar yeh total RAM ke 85â€“90% se upar rehta hai to RAM kam pad rahi hai.
   * **Committed (x / y GB):** `y` commit limit hai (RAM + pagefile). Agar `x` uske paas pahunch jaye tab "Out of Memory" crash aate hain.
2. **Resource Monitor** (`Win + R` âž” `resmon`) âž” **Memory** tab
   * **Hard Faults/sec:** game ke dauraan agar yeh lagataar sainkdon me rahe to Windows pagefile se data kheench raha hai â€” yahi stutter ki wajah hai.

**Is laptop ka data (2026-11-10):**

| Cheez | Value |
|---|---|
| RAM lagi hai | 4 GB + 8 GB = 12 GB (Windows ko 9.9 GB milti hai, baaki hardware/graphics ke liye reserved) |
| RAM free | aam taur par 0.6â€“2.5 GB (10â€“25%) |
| Pagefile | system-managed, 12 GB allocated, peak use sirf 2.6 GB |
| Commit | 21.9 GB limit me se ~15â€“18 GB use |
| SSD | 238 GB NVMe, C: par ~50 GB free |

Nateeja: is laptop me **asli RAM kam hai**. Pagefile pehle se kaafi bada hai â€” use aur badhane se fayda nahi hoga.

---

## âœ… 4. Kya kaam karta hai, kya nahi

### 4.1 Pagefile (Virtual Memory) â€” crash rokta hai, speed nahi deta
* Pagefile **commit limit** badhata hai (RAM + pagefile). Example: 8 GB RAM + 16 GB pagefile = 24 GB commit limit.
* **Fayda:** RAM full hone par game "Out of Memory" dekar crash nahi hota.
* **Nuksaan:** jo data pagefile me gaya use wapas laane me time lagta hai, isliye game atakta hai.
* Windows by default pagefile **khud manage** karta hai aur zarurat par bada kar deta hai. Zyada-tar logon ko kuch badalne ki zarurat nahi.

### 4.2 Windows Memory Compression â€” pehle se ON hai
* Windows 10/11 background apps ka data compress karke RAM me hi rakhta hai. Yeh **default ON** hota hai.
* Isme sirf ON/OFF hai, koi "tuning" setting nahi.
* Check karna ho to **Admin PowerShell** me: `Get-MMAgent` (`MemoryCompression : True` dikhna chahiye). Band ho to: `Enable-MMAgent -MemoryCompression` aur restart.
* Kitna dabega yeh data par depend karta hai. Game textures pehle se compressed hote hain, woh lagbhag nahi dabte. Compression thoda CPU bhi leta hai.

### 4.3 Apps band karo â€” yeh sabse pakka tareeqa hai
* Game se pehle browser tabs, Discord, IDE, launchers (jo chahiye nahi) band karo.
* Cloud-SSD ke PC Memory page par "Sabse zyada RAM lene wale Apps" list se seedha **Band Karein** daba sakte ho.
* Task Manager âž” **Startup apps** me faltu apps disable karo.

### 4.4 Bekaar pade background apps ko RAM se hatana (Cloud-SSD ka Auto mode)
* Yeh background apps ki memory ko RAM se bahar karta hai (Windows use compress karke ya pagefile me rakhta hai). Koi app band nahi hota.
* **Is PC par naapa gaya:** pehli baar me khali RAM ~1 GB badhi (jaise 1.4 â†’ 2.3 GB, kuch second baad 3+ GB).
* **Seema:** hataye gaye apps jab dobara chalte hain to apni RAM wapas le lete hain (yahan ~8â€“10 minute me). Jo apps aap use kar rahe ho unki RAM yeh nahi chhu sakta. Isliye yeh **RAM badhata nahi**, sirf saamne wale app/game ke liye jagah jaldi khali karta hai.
* Agar auto mode ke baad bhi RAM 30% se kam khali rahe to iska matlab hai: apps band karo ya RAM badhao.

---

## ðŸ§© 5. Asli Fix: RAM Upgrade

* Is laptop me **4 GB + 8 GB** lagi hai, aur dono ki speed alag hai (2667 aur 2400 MHz).
* **4 GB wali stick ko 8 GB ya 16 GB se badalna** sabse bada fayda dega (12 GB âž” 16 ya 24 GB).
* Kharidne se pehle check karo:
  * 4 GB wali RAM **slot me lagi hai ya motherboard par soldered** hai (laptop ka model search karo ya service center se poochho).
  * Type: **DDR4 SO-DIMM**. Dono stick ki speed same ho to behtar.

---

## ðŸ› ï¸ 6. Pagefile Manually Set Karna (sirf zarurat ho tab)

**Recommended:** "Automatically manage paging file size for all drives" ko **ON hi rehne do** aur C: par kam se kam **20 GB free** rakho.

Manual tabhi karo jab pagefile band ho, bahut chhota set ho, ya "Out of Memory" crash aa rahe hon:

1. Cloud-SSD âž” **PC Memory (RAM)** âž” **Page-File Settings Kholein** (ya `Win + R` âž” `sysdm.cpl` âž” **Advanced** âž” Performance **Settings**).
2. **Advanced** tab âž” **Virtual Memory** ke neeche **Change**.
3. **"Automatically manage paging file size for all drives"** ko uncheck karein.
4. Laptop ki apni **SSD (C:)** select karein (HDD ya cloud drive nahi) aur **Custom size** chunein:

   | Laptop ki RAM | Initial size (MB) | Maximum size (MB) |
   |---|---|---|
   | 8 GB | `8192` | `16384` |
   | 12 GB | `12288` | `24576` |
   | 16 GB | `8192` | `24576` |

   Yeh ek mota andaza hai. Maximum utna hi rakho jitni jagah C: par bach sake.
5. **Set** âž” **OK** âž” PC **Restart**.

**Wapas default par laana:** wahi dialog kholo, **"Automatically manage..."** ko wapas tick karo, **OK**, Restart.

> âš ï¸ **Cloud-SSD users dhyan dein:** bada pagefile aur Cloud-SSD ka cache dono C: ki free jagah lete hain. C: par 50 GB free ho aur 32 GB pagefile bana do to cache ke liye jagah nahi bachegi.

---

## ðŸš€ 7. Cloud-SSD App me: "PC Memory (RAM)" page

App ke sidebar me apna alag page hai: **PC Memory (RAM)** (Game Library aur Settings ke beech). Yeh Windows ki koi setting nahi badalta aur admin rights nahi maangta.

* **Upar salah ki patti**, jo halat ke hisaab se badalti hai:
  * 30% ya zyada RAM khali âž” hari "Memory theek hai".
  * 15â€“30% khali âž” "RAM kam ho rahi hai".
  * 15% se kam khali âž” "RAM lagbhag full hai", apps band karne ki salah.
  * Commit limit ke 90% par âž” "out of memory" crash ka khatra.
  * Page file wali drive par 20 GB se kam free âž” jagah ki warning.
  * Page file hi nahi hai âž” error.
* **Auto mode** (tick box, default ON) + **Abhi Chalao** button:
  * Jab 30% se kam RAM khali ho, bekaar pade background apps ko RAM se hata deta hai (Section 4.4).
  * Nahi chhedta: pichhle 15 minute me use kiye apps, chalta game, Windows, Cloud-SSD.
  * App khulne ke 5 minute baad se kaam karta hai; phir 10 minute me ek baar. Agar run ke baad bhi 30% tak na pahunche to agla intezaar dugna hota jata hai (20, 40, 60 minute).
  * Cloud-SSD se game start hote hi (RAM kam ho to) turant ek baar chalta hai.
* **4 dabbe:** RAM use, RAM khali, apps ne kitni reserve ki (limit ke saath), page file.
* **RAM use ka graph** (pichhle 5 minute).
* **Page file card:** path, size (Windows khud tay karta hai ya fixed), kitna use, sabse zyada kitna, drive par free jagah, aur button **Page-File Settings Kholein**.
* **Sabse zyada RAM lene wale Apps:** top 8 apps unke asli naam se. Jin apps ki apni window hai unke saamne **Band Karein** button: pehle poochta hai, phir app ko normal tareeke se band hone ko kehta hai (jaise X dabana) â€” zabardasti kill nahi. Windows aur Cloud-SSD ke process ek hi line me jode jate hain, un par button nahi.
* **Game Library âž” PLAY:** RAM 85%+ bhari ho to "started" message ke saath chetavni.

Code: `src/CloudSSD.App/MemoryHealth.cs`, `src/CloudSSD.App/MemoryRelief.cs`, `src/CloudSSD.Desktop/ViewModels/MemoryViewModel.cs`, `src/CloudSSD.Desktop/Views/MemoryView.xaml` (tests: `tests/CloudSSD.Tests/MemoryHealthTests.cs`).

Jo **nahi** banaya gaya aur nahi banana chahiye: compression "tuner", ya pagefile ko apne aap 16/32 GB karne wala slider (admin rights + restart chahiye, aur cache ki jagah kha jata hai).

---

## ðŸ“Œ Summary

* **Cloud se RAM:** nahi ban sakti (bahut zyada slow).
* **Pagefile:** crash rokta hai, speed nahi badhata. Default (system-managed) setting hi theek hai.
* **Memory Compression:** Windows me pehle se ON hai.
* **Auto mode:** game ke liye jagah jaldi khali karta hai, par RAM badhata nahi.
* **Sach me fayda:** apps band karna, aur RAM upgrade.


---

### Additional Virtual RAM Insights:
# VIRTUAL_RAM_EXPLAINED.md — Laptop RAM Expansion & Cloud Reality Guide

> **Topic:** Kya Cloud-SSD ki tarah Laptop ke liye RAM bhi banayi ja sakti hai?  
> **Language:** Hinglish (Roman Hindi)  
> **Author:** Antigravity (Google DeepMind)  
> **Last Updated:** 2026-11-10  

---

## 💡 Seedha Jawab: Kya Storage ki tarah RAM bhi banayi ja sakti hai?

**Direct Cloud/Internet se RAM nahi banai ja sakti**, lekin **laptop ki Fast SSD ka use karke Virtual RAM (Pagefile) aur Memory Compression zaroor banaya ja sakta hai.**

---

## 🔍 Cloud se direct RAM kyu nahi ban sakti? (Speed aur Latency ka Chakkar)

| Type | Speed / Latency (Access Time) | Kitna Fast / Slow? |
|---|---|---|
| **Laptop ki Real RAM (DDR4/DDR5)** | **10 se 50 Nanoseconds** (\(10^{-8}\text{ s}\)) | Palk jhapkaane se lakho guna tez (Base) |
| **Local Fast NVMe SSD (Virtual RAM)** | **10 se 50 Microseconds** (\(10^{-5}\text{ s}\)) | Real RAM se lagbhag 1,000 guna slow |
| **Cloud / Internet (Google Drive)** | **20 se 100 Milliseconds** (\(10^{-2}\text{ s}\)) | **RAM ke mukable 10 LAKH se 1 CRORE guna slow!** |

### Storage (Cloud-SSD) aur RAM me kya farak hai?

* **Storage (Cloud-SSD) me kaise chalta hai:**  
  Jab aap koi game ya software chalate ho, toh game poori 50 GB ya 100 GB ki file ek saath nahi maangta. Thoda sa sound, texture ya map file aana hota hai, jo background me 1-2 second me aaram se download ho jata hai aur game smooth chalta rehta hai.
* **Lekin RAM me kya hota hai:**  
  Aapka CPU har ek second me karodon baar RAM se data read aur write karta hai. Agar CPU ko har ek calculation ke liye internet/cloud ka intezaar karna pada, toh aapka laptop **poori tarah freeze (hang) ho jayega** aur screen atak jayegi (isse computer science me **Thrashing / Hard Page Fault Lock** bolte hain).

---

## ✅ Toh fir Laptop me RAM kaise badha sakte hain?

Aapke laptop ke liye 3 asli aur proven tareeqe hain jo kaam karte hain:

### 1. High-Speed SSD Virtual RAM (Windows Pagefile Expansion)
* Aapke laptop ki Fast SSD ka ek hissa **Virtual RAM (Swap Space)** banaya ja sakta hai.
* **Example:** Agar aapke laptop me **8 GB Real RAM** hai, toh SSD par **16 GB High-Speed Pagefile** set karke total **24 GB Committed Memory** banayi ja sakti hai.
* **Fayda:** Heavy games ya editing software "Out of Memory" ka error dekar crash nahi honge.

### 2. Windows Memory Compression (zRAM Tech)
* Windows 10/11 ke andar ek in-built compression manager hota hai (`MMAgent`).
* Jab RAM bharne lagti hai, toh yeh background me chal rahe apps ke data ko 50% se 70% chhota (compress) karke RAM ke andar hi compress kar deta hai, jisse system slow nahi hota.

### 3. Smart RAM Cleaner (Working-Set Trimmer)
* Jaise hi aap koi game ya heavy software shuru karein, yeh background me chal rahe Chrome tabs, Discord, Steam background web helpers ya updaters ki memory ko turant SSD swap me bhej deta hai.
* **Result:** **3 se 5 GB Real Physical RAM turant free ho jaati hai** aapke active game ke liye!

---

## 🛠️ Windows me Manual Virtual RAM Kaise Set Karein? (Step-by-Step)

1. Keyboard par `Win + R` dabayein aur likhein: `sysdm.cpl` aur Enter karein.
2. **Advanced** tab par jaayein aur **Performance** ke andar **Settings** par click karein.
3. Wahan fir se **Advanced** tab par jaayein aur **Virtual memory** ke neeche **Change...** par click karein.
4. `"Automatically manage paging file size for all drives"` ka tick hata dein.
5. Apni sabse fast drive (SSD - usually `C:`) select karein aur **Custom size** chunein:
   * **Initial size (MB):** `8192` (8 GB) ya `12288` (12 GB)
   * **Maximum size (MB):** `16384` (16 GB) ya `24576` (24 GB)
6. **Set** par click karein aur **OK** karke laptop restart karein.

---

## 🚀 Cloud-SSD App Feature Roadmap (Smart RAM Booster)

Hum **Cloud-SSD** desktop app ke andar ek naya module add kar rahe hain:  
**"⚡ Smart RAM & Virtual Memory Booster"**

* **1-Click RAM Cleaner:** Ek click me background bloat saaf karke 3-4 GB RAM free karega.
* **Auto Virtual Memory Slider:** SSD par 16 GB / 32 GB Virtual RAM auto-allocate karega.
* **Memory Compression Switch:** Windows zRAM engine ko auto-tune karega.

---

<div align="center">

*Documented by Antigravity for Cloud-SSD Project.*

</div>


---

# PART 2: CLOUD STORAGE & GOOGLE DRIVE INTEGRATION

# Connecting Google Drive

Cloud-SSD connects to Google Drive through rclone using **your own OAuth client**. rclone's built-in shared client
ID is being retired during 2026 (rclone 1.75.2 says so in `rclone help backend drive`), and a personal client also
avoids the shared client's rate limits.

You never type your Google password into Cloud-SSD. Sign-in happens on Google's own page in your browser; rclone
receives the resulting token on `http://127.0.0.1:53682/` and stores it in Cloud-SSD's private rclone config.

## Quick start: one-click sign-in

**Cloud Drives → Sign in with Google Drive → Sign in with Google.** Your browser opens Google's page; pick your
account and click *Allow* (the consent page says "rclone"). Your whole Google Drive is then mounted automatically as a
drive letter.

This uses rclone's built-in shared Google client. It works today (verified 2026-10-11), but rclone is retiring it
during 2026, and it is shared by all rclone users, so it can be throttled or stop working. If it fails, use your own
client as described below (tick **Advanced** in the same window).

## 1. Create an OAuth client (one time, ~5 minutes)

1. Open <https://console.cloud.google.com/> and create a project (any name, e.g. "Cloud-SSD").
2. **APIs & Services → Library** → search "Google Drive API" → **Enable**.
3. **APIs & Services → OAuth consent screen** → User type **External** → fill in the app name and your email →
   on **Test users** add your own Google account. (Leaving the app in "Testing" is fine for personal use; note that
   Google expires refresh tokens of apps in Testing mode after 7 days — publish the app to "In production"
   (no verification needed for personal use of the `drive` scope with a warning screen) to avoid weekly re-logins.)
4. **APIs & Services → Credentials → Create credentials → OAuth client ID** → Application type **Desktop app**.
5. Copy the **Client ID** (ends with `.apps.googleusercontent.com`) and the **Client secret**.

## 2. Connect in Cloud-SSD

1. **Cloud Drives → Connect Google Drive**.
2. Enter an account name (e.g. `gdrive`), the client ID and the client secret.
3. Optional: **Read-only access** (`drive.readonly`) — safest for a game library; save games must then stay local
   (use Hybrid mode or games that save to Documents/AppData, which most do).
4. Optional: a **root folder ID** to limit Cloud-SSD to one Drive folder (the ID is the last part of the folder's URL).
5. Click **Connect**. Your browser opens Google's consent page; approve it. Cloud-SSD verifies access with a
   quota query and then encrypts the saved login (see below).
6. **Add virtual drive**: pick the account, browse to your games folder (only folder names are listed), choose a
   drive letter and mount.

## Where the login is stored

- `%LOCALAPPDATA%\CloudPlayX\rclone.conf` — Cloud-SSD's own rclone config (your normal rclone config is not used).
- With **Settings → Encrypt the rclone config** (default on) the file is encrypted by rclone itself. The random
  encryption key is stored in `rclone.conf.key`, protected with Windows DPAPI for your user account, and handed to
  rclone only through an environment variable — never on a command line.
- The client secret is sent only to a local rclone daemon on 127.0.0.1 in an HTTP request body.
- Diagnostic exports and logs redact tokens, client secrets and passwords; the rclone config is never exported.

## Quotas and errors

- Google Drive API quotas apply. Cloud-SSD limits rclone to 10 API transactions/s by default
  (Settings → Network) and rclone retries rate-limit responses with exponential backoff.
- "Cloud authentication required": the token expired or was revoked (or the app is in Testing mode and 7 days have
  passed). Reconnect the account.
- Very large single files are fine: reads use HTTP byte ranges, so only the parts the game reads are downloaded.


---

## 🔑 Apni Personal Google Key Kaise Banayein (OAuth Client)
# Apni Google key kaise banayein (ek baar, lagbhag 10â€“15 minute, muft)

## Yeh kyun chahiye

- Abhi Cloud-SSD Google mein ek **saajhi (shared) key** se sign-in karta hai. Woh dheemi hai (har kaam 40 second tak)
  aur Google use 2026 mein band kar raha hai.
- **Band PC ki files phone se kholne** ke liye phone ko khud Google mein sign-in karna padta hai. Uske liye aapki
  apni key zaroori hai. Key milte hi PC use phone ko de deta hai (sirf key; aapka Google login kabhi nahi bhejta).

Key matlab: Google ke yahan aapke naam ka ek chhota "app" jiske naam se Cloud-SSD sign-in maangta hai.
Isme koi paisa nahi lagta aur aapki files kisi aur ko nahi dikhtin.

## Kadam

1. Browser mein kholo: **https://console.cloud.google.com** aur apne Google account se sign-in karo.
2. **Project banao:** upar project wale naam par click â†’ "New project" â†’ koi bhi naam (jaise `Cloud-SSD`) â†’ Create.
   Ban jaye to upar wahi project chuna hua ho.
3. **Drive chalu karo:** baayein menu â†’ "APIs & Services" â†’ "Library" â†’ khojo `Google Drive API` â†’ kholo â†’ **Enable**.
4. **App ka naam bharo:** baayein menu â†’ "Google Auth Platform" â†’ "Branding":
   - App name: `Cloud-SSD` (kuch bhi)
   - User support email aur Developer email: apna e-mail
   - Audience: **External** â†’ Save.
5. **"In production" karo (zaroori):** "Google Auth Platform" â†’ "Audience" â†’ **Publish app** â†’ Confirm.
   Agar yeh "Testing" mein raha to Google har 7 din mein login khatam kar deta hai aur sab ruk jata hai.
6. **Key banao:** "Google Auth Platform" â†’ "Clients" â†’ "Create client":
   - Application type: **Desktop app**
   - Name: kuch bhi â†’ Create
   - **Download JSON** dabao. File (`client_secret_....json`) **Downloads** folder mein aa jayegi. Use wahin rehne do.
7. **Cloud-SSD mein lagao:** PC app â†’ **Cloud Drives** â†’ **"Switch to my own Google key"**.
   App file ko Downloads mein khud dhoondh leta hai. Har Google account ke liye ek baar browser khulega: sign-in karke
   "Allow" dabao. (Aapke 3 account hain, to 3 baar.)

## Raaste mein yeh dikhe to

- **"Google hasn't verified this app"**: yeh aapka apna app hai. "Advanced" â†’ "Go to Cloud-SSD (unsafe)" dabao. Theek hai.
- **Key file nahi mili**: app poochhega; Downloads se `client_secret_....json` chun do.
- **Kisi account mein dikkat**: baaki account ho jaate hain; us ek ko dobara "Switch to my own Google key" se karo.
  Aapki files aur drive ke letter (Z:, Y:) waise hi rehte hain.

## Key lagne ke baad

1. PC app â†’ Devices â†’ **When off** â†’ **Continue** dabao (copy banna shuru) aur **"Start Cloud-SSD with Windows"** tick karo.
2. Phone app â†’ More â†’ Permissions â†’ "Files when it is off" mein **"Sign in"** dikhega: usi Google account se sign-in
   karo jisme PC ki copy ja rahi hai. (Pakka na ho to teeno account se sign-in kar lo; har ek ke liye ek baar.)
3. Ab PC band karke phone ke **Cloud SSD** tab mein "â€¹PC ka naamâ€º (cloud copy)" kholo.

(Kadam 2 aur 3 wala phone ka hissa 14 Nov 2026 ko bana hai; uski asli jaanch key lagne ke baad hi ho sakti hai.
Kuch alag dikhe to Claude / Codex ko batao.)

## Dhyan

- `client_secret_....json` file kisi ko mat bhejo aur GitHub par mat daalo.
- Apne Google account par **2-step verification** chalu rakho: PC ki files ab usi account mein hongi.


---

## 📦 Third-Party Backends & TeraBox Research
# TeraBox — integration research (status: not supported)

Researched 2026-10-10.

## Findings

- **rclone 1.75.2 has no TeraBox backend** (`rclone help backends` lists none). rclone's maintainers require a
  public API for new backends.
- A community **unofficial fork** (`gulp79/rclone-extra`, code from `x1arch/rclone` branch `backend/terabox`)
  adds TeraBox support. Its own documentation says it authenticates with the **browser session cookie** because
  TeraBox does not provide an official API, and it works by reproducing the web client's undocumented requests.
- TeraBox has announced an "open platform", but no public developer documentation describing OAuth authentication,
  file listing, or byte-range downloads could be found or verified.

## Decision

Cloud-SSD does **not** integrate TeraBox:

- The available method depends on session cookies and undocumented endpoints. That conflicts with the project rules
  (no bypassing authentication, no undocumented endpoints, no silent dependence on scraping) and is fragile:
  any web-client change breaks it, and accounts could be flagged.
- Streaming needs reliable HTTP range requests and stable file IDs; neither is documented.

The provider interface keeps a `TeraBox` adapter marked **Unsupported** (`src/CloudSSD.Providers/Providers.cs`),
so the UI explains the situation instead of offering a broken option.

## What would change this

An official TeraBox API with (1) OAuth or API-key authentication intended for third-party apps, (2) directory
listing with sizes/modification times, and (3) downloads that honour HTTP `Range`. With those, a rclone backend
(preferred, upstream) or a Cloud-SSD provider adapter becomes feasible.

## Alternatives for large cheap storage today

Google Drive (supported), or rclone-backed providers marked "via rclone (untested)": OneDrive, Dropbox,
S3-compatible storage (e.g. Backblaze B2, Wasabi, Cloudflare R2), or a NAS over SMB/SFTP.

