# 📋 FEATURE PLANS & ROADMAP (Cloud-SSD)

> **Overview:** Consolidated technical implementation plans for offline PC-to-Phone synchronization, when-off access, and roadmap tasks.  
> **Source Documents Consolidated:** WHEN_OFF_PLAN.md, WHEN_OFF_BINA_KEY_PLAN.md, PENDING_WORK.md.

---

# 📑 TABLE OF CONTENTS
- [1. Pending Work & Immediate Roadmap](#1-pending-work)
- [2. When Off Access (Phone-PC Sync Plan)](#2-when-off-access)
- [3. Bina Google Key Offline Access Plan](#3-bina-google-key-plan)

---

# 1. Pending Work & Immediate Roadmap
# Cloud-SSD Pending Work & Roadmap (`PENDING_WORK.md`)

## ðŸŽ¯ Active Status: v0.7.0 Complete & Released

All core requirements and major new features have been fully implemented, tested, and packaged:
- [x] Complete project-wide rebrand: `CloudPlayX` âž” `Cloud-SSD`
- [x] Primary executable rebranded: `Cloud-SSD.exe` (Assembly Name: `Cloud-SSD`)
- [x] Fixed installer busy mount error (auto-unmount & process kill in Inno Setup)
- [x] Mobile Hub (Offline Phone Bridge) embedded async HTTP server (`LocalMobileBridgeService.cs`)
- [x] Pure C# Zero-Dependency QR Code Generator (`QrCodeGenerator.cs`)
- [x] Desktop UI (`MobileHubView.xaml`, `MobileHubViewModel.cs`) integrated with live pairing and drop zone
- [x] Mobile PWA Client (`wwwroot/mobile`: 5 tabs - Home, Files, Gaming, Vault, Settings)
- [x] 100% Offline Service Worker (`sw.js`) and Web App Manifest (`manifest.json`)
- [x] 62/62 Unit & Integration tests passing cleanly with 0 errors
- [x] ReadyToRun release binaries & setup installer built:
  - `Cloud-SSD-0.7.0-setup.exe` (~67.5 MB / 70.8 MB)
  - `Cloud-SSD-0.7.0-win-x64.zip` (~89.3 MB / 93.7 MB)

---

## ðŸ“‹ Next Priorities & Future Enhancements

### Phase 1: Real-World Phone Verification
- [ ] Connect a physical Android phone to the PC running Cloud-SSD via local Wi-Fi or Phone Hotspot.
- [ ] Verify instant camera QR code scanning and pairing token authorization.
- [ ] Test 4K video playback seeking and range requests directly on mobile Chrome.
- [ ] Test IndexedDB "Save to Offline Vault" for offline access when PC is shutdown.

### Phase 2: Live Gaming HUD Expansion
- [ ] Expand `/api/hud` with real-time MSI Afterburner / RTSS or Windows Performance Counters integration for per-core CPU and GPU temperature telemetry.
- [ ] Add emulator-specific auto-sync profiles (AetherSX2, PPSSPP, RetroArch save states).


---

# 2. When Off Access (Phone-PC Sync Plan)
# Plan: "Band hone par bhi files" â€” device off ho tab bhi uski files khulà¥‡à¤‚

Repo: `C:\Users\Roshan\Downloads\Cloud-Drive\Cloud-Drive\Cloud-SSD\Cloud-SSD\Cloud-SSD`

## Context (kyun)
Aaj app tabhi kaam karta hai jab dono device chalu hon. Aap chahte ho:
- laptop **power off** ho tab bhi phone se uski saari files khulà¥‡à¤‚;
- phone **off** ho tab bhi laptop se uski files khulà¥‡à¤‚.

**Seedhi baat:** jo device band hai woh khud file nahi de sakta. Iska ek hi tareeka hai: device jab chalu ho tab uski
files ki copy kahin aur rakh li jaaye, aur band hone par wahan se khule.

**Aapke faisle (sawaalon ke jawab):**
- Copy **dono jagah**: sab kuch Google Drive cloud pe (app mein juda 15 TB), aur chune hue folders doosre device pe bhi.
- Laptop: **poori disk, sab kuch** (C:, ~357 GB, Windows aur programs ke saath).
- Phone: **poora internal storage** (~57 GB).
- Cloud pe **seedhi files** (encrypted nahi), asli naam aur folders ke saath.
- **Shart:** device pehle ek baar connect (pair) hua ho; aur ek **nayi permission** PC aur phone dono pe.
  Jab tak **dono taraf "haan"** na ho, yeh kaam nahi karega.

**Aaj kya hai, kya nahi (code se dekha):**

| Cheez | Aaj |
|---|---|
| PC ki apni files cloud pe copy | Nahi. Sirf Offloader hai, jo jagah kam hone par files cloud pe *khiska* deta hai (PC se hata ke). |
| Phone ki files cloud pe copy | Sirf camera photos/videos (Camera backup). Baaki folders nahi. |
| PC band ho tab phone se cloud kholna | Code hai (`DriveBackend`, `DriveApi`, `GoogleAuth`), par APK mein Google key nahi hai, isliye **band** hai. |
| Phone band ho tab PC ko phone ki files | Kuch nahi. PC phone se hamesha live poochta hai. |
| Doosre device pe copy | Phone: sirf cloud-drive ke folders ("Offline rakho"), PC-disk ke nahi. |
| PC app Windows ke saath chalu | Nahi. App band to upload band. |

Is plan ko ek alag jaanch se bhi guzaara gaya (rclone, Google Drive ki seemaayein, Windows, Android, suraksha). Usne
paanch badi kamiyaan pakdi; neeche ka plan unhe theek karke likha hai.

## Ek nazar mein: yeh kaise chalega
1. Phone aur laptop pair hain. Dono pe nayi permission **"Band hone par bhi files"** on karte ho.
2. Dono taraf haan hote hi:
   - laptop apni files Google Drive mein `Cloud-SSD Devices/<laptop>/files/â€¦` mein upload karna shuru karta hai (peeche, dheere-dheere);
   - phone apna internal storage `Cloud-SSD Devices/<phone>/files/â€¦` mein upload karta hai (Wi-Fi + charging pe);
   - phone pe **ek baar Google sign-in** hota hai (har account ke liye ek tap), taaki phone laptop ke bina seedhe Drive khol sake.
3. Laptop band hai â†’ phone app us laptop ke naam ke neeche **"Files (cloud copy Â· 14:32 tak)"** dikhata hai: wahi folders,
   wahi naam; file kholo, download karo, share karo.
4. Phone band hai â†’ laptop ke **Files â†’ Phone storage** page pe phone ki cloud copy khulti hai.
5. Chune hue folders doosre device pe bhi rakhe jaate hain, taaki bina internet ke bhi khulà¥‡à¤‚.

## Aapke haath se jo karna hoga (main har step bataunga)
1. **Apni Google key banana** (ek baar, ~15 min): Google Cloud Console mein ek "OAuth client" banana aur use "In production" karna.
   Kyun zaroori: aaj PC rclone ke *shared* Google login pe hai jo 2026 mein band ho raha hai (app ka peela banner yahi kehta hai),
   aur phone ko bina PC ke Drive kholne ke liye apni key chahiye. App mein "How do I get a key?" button pehle se hai.
2. Phone pe **Google sign-in** (har account ke liye ek baar).
3. Phone ki settings mein Cloud-SSD ko **autostart** aur **battery ki chhoot** dena (vivo warna peeche ka kaam maar deta hai).
4. PC pe "Windows ke saath chalu ho" ko haan kehna.

## Niyam: permission aur "ek baar connect"
- Nayi permission: **"Band hone par bhi files"** (code: `when-off`). **Default OFF** dono taraf, aur *alag se haan* karni padti hai:
  "sab permission do" wale default mein yeh shaamil nahi hogi (aaj `AccessControl.Defaults` naye device ko sab de deta hai â€” isme se bahar).
- Ek jodi (yeh PC â†” yeh phone) ke liye chalu tabhi, jab teeno sach hon: (1) dono pair hain (pair hona hi "ek baar connect" hai),
  (2) PC pe us phone ke liye **haan**, (3) phone pe us PC ke liye **haan**.
- Ek taraf haan, doosri taraf nahi â†’ kuch upload nahi hota; screen pe "Doosri taraf ki haan ka intezaar".
- **Chor darwaza band:** cloud copy wala folder (`Cloud-SSD Devices`) Z:/Y: drives aur phone ke "Cloud SSD" tab se **chhupa** rahega.
  Warna jis device ke paas sirf "browse" permission hai woh Z: ke raaste poori C: dekh leta. Yeh folder sirf is feature ke raaste khulega.
- Koi bhi taraf band kare â†’ us jodi ke liye padhna band, upload rukta hai, aur poocha jaata hai: "Cloud ki copy bhi mita dein?"
- **Guests** ko yeh permission kabhi nahi.
- **Imaandaar baat:** files aapke apne Google account mein seedhi rakhi hain. Permission band karne se *app* ka raasta band hota hai;
  jiske paas Google account ka login hai woh drive.google.com pe woh files tab bhi dekh sakta hai. Phone kho jaaye to Google account
  mein jaake app ka access hatana hoga. Isliye Google pe **2-step verification** zaroori.

## Cloud mein jagah (layout)
```
Cloud-SSD Devices/
  <deviceId> <naam>/
    device.json            â† haal: aakhri sync kab, kitni files/GB upload, kitni baaki, kitni chhodi (kyun)
    files/
      C/Users/â€¦            â† laptop: har drive ek folder (C, Dâ€¦)
      Internal storage/â€¦   â† phone
    trash/<yyyy-MM-dd>/â€¦   â† device pe mitayi/badli gayi files ki purani copy, 30 din tak
```
- **Har device ek hi Google account mein likhta hai** (laptop account A mein, phone account B mein â€” sabse zyada khaali wale).
  Kyun: teeno accounts mein baant ke likhne se har file pe teen guna Drive calls lagte; aur do accounts = dugni raftaar.
- Browsing ke liye alag index nahi: yahi folder khulta hai. Jo dikhta hai woh wahi hai jo sach mein upload ho chuka hai.

## Phase 0 â€” Neenv (pehle yeh, tabhi baaki)
1. **Apni Google key** PC pe lagana (maujooda "Switch to my own Google key" flow), aap ke saath step by step.
2. **Key phone tak:** PC sirf *client id/secret* phone ko deta hai (token nahi). Phone khud Google sign-in karta hai aur apne token rakhta hai.
   - Kyun token nahi bhejte: PCâ†”phone ka raasta LAN pe plain HTTP hai aur device ki chaabi har request mein jaati hai; Wi-Fi pe koi sun le
     to teeno accounts ka poora Drive uske haath lag jaata. Sirf client id/secret jaane se aisa nahi hota.
   - `GoogleAuth.java`: `GoogleConfig` (aaj build ke waqt bhara jaata hai, `build-apk.ps1`) ko **runtime** banana â€” PC se mili key `TokenStore` mein.
   - "Bhool jao" pe phone sirf apne token mitata hai; Google ka *revoke* call **nahi** (warna usi key pe PC ka rclone bhi logout ho sakta hai).
   - **Turant fayda:** isi se phone ka "Cloud SSD" tab laptop band hone par bhi chalne lagega (aaj ki cloud files ke liye).
3. **Teen chhoti jaanch** (koi feature code nahi), nateeja aapko bataunga:
   - rclone: chhote test folder pe `copy --files-from-raw --no-traverse --backup-dir` ek account remote pe; modified time, trash.
   - C: ka walk normal user ke roop mein: kitna time, kitni files, kitni Windows padhne nahi deta.
   - Phone: Android ka "user-initiated data transfer" job lambi upload ke liye vivo pe tikta hai ya nahi.

## Phase 1 â€” Permission + dono taraf haan
**PC**
- `src/CloudSSD.App/AccessControl.cs`: nayi category `WhenOff = "when-off"`, `AccessCategories.All` aur `Defaults` se **bahar**;
  `Has()` ise sirf saaf grant pe sach maane (aaj `Grants == null` pe sab sach hota hai; legacy callers ko bhi nahi).
- `src/CloudSSD.Core/Settings.cs` â†’ `PairedDevice`: `WhenOffPc`, `WhenOffDevice`, `WhenOffSince`.
- Nayi `src/CloudSSD.App/LocalMobileBridgeService.WhenOff.cs`: `GET /api/whenoff/state`, `POST /api/whenoff/consent?on=1|0`,
  `GET /api/whenoff/key` (client id/secret, sirf active jodi ko), `WhenOffActive(device)`, event `WhenOffChanged`.
- `src/CloudSSD.Mount/MountPlanning.cs`: Z: aur Y: ke mount pe `--filter "- /Cloud-SSD Devices/**"` (folder chhupana; saath hi mount ko
  lakhon files ki list yaad rakhne se bachana â€” warna games wale rclone ki RAM aur startup bigadta).
- `src/CloudSSD.Desktop/Views/DevicePermissionsWindow.cs`: row "Band hone par bhi files" + haal.
- Notification: "<phone> ne 'Band hone par bhi files' ke liye haan kaha" (`Notifier.Show`, target `perm:<id>`).

**Phone**
- Permissions page (`renderPermissions`, `assets/app.js` â€” doosri session ka page): naya group "Band hone par bhi files": switch + haal.
- `MainActivity.java`: `native.whenOff(pcId)` / `setWhenOff(pcId, on)`; state `PcProfiles` ke saath.
- `DriveBackend.java`: "Cloud SSD" tab aur search se `Cloud-SSD Devices` chhupana.
- `data.js`: `whenOffState()` â†’ 'off' | 'waiting-pc' | 'waiting-phone' | 'on' (+ tests).

**Tests:** ek taraf haan se kuch active nahi; naya device ise default mein nahi paata; guest ko kabhi nahi; "browse" wala device
mirror folder nahi dekh pata; band karne par state wapas.

## Phase 2 â€” Laptop ka mirror (PC ki files â†’ cloud)
**Nayi files: `src/CloudSSD.App/Mirror/`**
- `MirrorScope.cs` â€” kya jaayega, kis kram mein.
  - **Tiers:** 1) Desktop, Documents, Pictures, Videos, Music, Downloads (Windows ke known-folder API se, taaki OneDrive pe ho to bhi sahi mile)
    â†’ 2) baaki `Users\<aap>` â†’ 3) doosri NTFS drives aur C: ke baaki folders â†’ 4) Program Files, ProgramData â†’ 5) AppData, Windows.
    Har tier mein chhoti aur nayi files pehle. **Tier 4â€“5 ("Windows aur programs bhi") ek switch hai**; aapke faisle ke hisaab se ON,
    par tier 1â€“3 pure hone ke baad hi shuru hota hai.
  - **Hamesha chhodi jaati hain:** `pagefile.sys`, `hiberfil.sys`, `swapfile.sys`, `System Volume Information`, `$Recycle.Bin`,
    `WinSxS` (hard links, dohri ginti), **app ka poora data folder** `%LOCALAPPDATA%\Cloud-SSD` (isme rclone ki chaabi, device keys aur
    link sign karne wali key hain â€” inhe plain cloud pe daalna khatra hai), Y:/Z: aur "Google Drive for desktop" jaisi cloud drives,
    junctions/symlinks (tag dekh ke), "sirf online" placeholder files (padhne se poori download ho jaati), doosre device ki rakhi copies (Phase 6).
  - Jo file use mein hai â†’ agli baar phir koshish. Jo Windows padhne hi nahi deta (permission) â†’ "hamesha chhodi" mein gini jaati hai, baar-baar koshish nahi.
- `MirrorState.cs` â€” SQLite (`Microsoft.Data.Sqlite` pehle se hai), `DataRoot\mirror\pc.db`: path, size, mtime, tier, upload hua size/mtime, galti.
- `MirrorScanner.cs` â€” walk (`ProjectFilter.Walk` jaisa), state se tulna â†’ "bhejna hai" / "mit gayi" / "naam badla" lists.
  - Mitna tabhi maana jaata hai jab woh folder is pass mein poora padha gaya ho; ek saath bahut zyada "mit gayi" â†’ ruk ke poochna (galti se disk na dikhe to cloud copy na ude).
  - Folder ka naam badla (naam+size+mtime milte hain) â†’ cloud mein `moveto`, dobara upload nahi.
  - User folders har ghante; baaki disk din mein ek baar. (`FileSystemWatcher` baad mein.)
- `MirrorUploader.cs` â€” batch ke liye **alag rclone process** (`RcloneConfig.CreateRunner`), mount ke raaste nahi (Z: ka cache na bhare, games ke
  reads na ruke): `copy <root> <accountRemote>:Cloud-SSD Devices/<id>/files/<root> --files-from-raw â€¦ --no-traverse
  --backup-dir â€¦/trash/<date> --transfers 4 --tpslimit 8 --bwlimit <setting> --drive-stop-on-upload-limit --use-json-log --stats 2s`
  + mount wale `--drive-*` flags. Log se progress â†’ `TransferTasks` (id `mr-â€¦`). Khaali folders ke liye `mkdir`.
  30 din purana trash mitate waqt Drive ke apne trash mein na jaaye (`--drive-use-trash=false`). Badi, baar-baar badalne wali files (PST, VHDX) ke purane version ki seema.
- `DeviceMirrorService.cs` â€” sab jodta hai; `AppHost` se chalu (jaise `SmartStorageOffloaderService`); ek waqt mein ek pass.
  **Rukta hai:** game samne ho (`GameActivity.IsPlaying`), battery pe ho, metered network pe ho, ya koi jodi active na ho.
  Har pass ke baad `device.json`. Offloader jab koi file cloud pe khiskaye (`OffloadCompleted`) to mirror use "mit gayi" nahi maanta.
- `Settings.cs`: `MirrorSettings` (Enabled, Account, IncludeSystem (tier 4â€“5), extra/hataaye folders, UploadLimitMbps, PauseOnBattery, PauseOnMetered, KeepTrashDays=30).
- **Windows ke saath chalu hona (zaroori):** HKCU `Run` key + `MainWindow.xaml.cs` mein `--minimized` (`App.xaml.cs` nahi chhua jaayega).
  Bina iske mirror sirf tab chalta jab aap app kholte.

**Screen (PC):** page "Band hone par bhi files": chalu/band, progress ("Aapki files: 100% Â· Poori disk: 41%"), "abhi sync", speed limit,
chhodi gayi files ki ginti + wajah, "Windows aur programs bhi" switch, "cloud copy mitao". Jagah doosri session ke naye menu (Files ya Devices) mein.

**Tests:** scope/tiers/hamesha-chhodna (DataRoot zaroor); scanner (nayi, badli, mitayi, naam badla, adha padha folder); uploader nakli rclone
runner ke saath; rukne ke niyam; `device.json`.

## Phase 3 â€” Phone se band laptop ki files (pehla bada nateeja)
- `DriveBackend.java`: naya root `device:<pcId>:<path>` â†’ us account ka `Cloud-SSD Devices/<pc>/files/<path>` (folder ids cache, taaki har path pe
  kai Drive calls na lagà¥‡à¤‚).
- `DriveApi.java`: Google ne jis file ko "flag" kiya ho use bhi padhna (`acknowledgeAbuse`, mount yahi karta hai).
- `assets/app.js`: PC offline + permission active + phone signed in â†’ us PC ke neeche "Files (cloud copy)" â€” maujooda Cloud SSD browser, viewer,
  thumbnails, download code se. Upar patti: "<PC> band hai Â· cloud copy <time> tak Â· <n>% upload hua" (`device.json`).
  **Sirf padhna** (kholna, download, share). Search v1 mein saada (naam se, isi copy ke andar).
- Android Files app (`CloudDocumentsProvider.java`): root "â€¹PCâ€º (cloud copy)".
- **Live:** test folder upload â†’ PC app band â†’ phone se folder kholo, file padho, download karo; permission phone pe band â†’ raasta gayab.

## Phase 4 â€” Phone ka mirror (phone ki files â†’ cloud)
- Nayi `DeviceMirror.java` + `DeviceMirrorService.java`:
  - **Pehla bada upload:** Android ka "user-initiated data transfer" job (app khol ke "Shuru karo" dabane par; notification ke saath lamba chal sakta hai).
    **Roz ka chhota upload:** periodic job, charging + Wi-Fi pe. (Saada job 10-10 minute ke tukdon mein chalta; 57 GB mein hafte lagte.)
  - walk: internal storage (`walkPaths` jaisa, All files access); chhodta hai `Android/data`, `Android/obb`, `.thumbnails`, `.trashed-*`;
    kram: DCIM/Pictures/Documents/Download pehle; chhoti/nayi pehle;
  - state `mirror.db`: path, size, mtime, upload hua, Drive file id, **folder ids**, adhoori upload ka session URL + kitna gaya;
  - upload **seedha Google Drive** (`DriveApi`): chhoti files ek hi call mein; badi resumable, ruk ke wahin se (aaj `DriveApi.upload` rukne par shuru se karta hai â€” theek karna);
  - mitayi/badli file â†’ Drive mein `trash/<date>`; shartein: Wi-Fi + charging (mobile data ka switch alag);
  - har pass ke baad `device.json`.
- **Camera backup waise hi chalta rahega** (uska hisaab "phone se jagah khaali karo" ke kaam aata hai). Photos cloud pe do jagah honge
  (Phone Backup + mirror); 15 TB mein yeh chalega.
- Phone "Band hone par bhi files" page: haal, progress, "Shuru karo / abhi sync", mobile data switch, autostart/battery ki yaad.
- **Live:** test folder â†’ Drive mein dikhe; file mitao â†’ trash mein; app ko peeche bhej ke upload chalta rahe.

## Phase 5 â€” Laptop se band phone ki files
- `PhoneFilesView.cs` (doosri session ki file, uske release ke baad): phone jawab na de aur permission active ho â†’ source cloud copy,
  **Z: se nahi** (woh chhupa hai) balki rclone se seedha: list `RcloneConfigâ€¦ListDirectoryAsync`, file `copyto`. Patti "Phone band hai Â·
  cloud copy <time> tak"; sirf padhna + "PC pe save karo". Dono device pe "aakhri baar N din pehle sync hua" dikhe.
- **Live:** phone airplane mode / band â†’ PC pe uski files khulà¥‡à¤‚ aur save hon.

## Phase 6 â€” Doosre device pe bhi copy (bina internet ke)
- **Phone pe laptop ke folders:** `OfflinePins.java` + `fs.js`: PC-disk ke folders bhi "Offline rakho" (aaj mana hai); roz refresh (JobScheduler);
  PC band ho to cloud copy se refresh. Phone pe 500 MB hamesha khaali rehta hai.
- **Laptop pe phone ke folders:** chune hue phone folders PC ke received folder mein (`SavePhoneItemsToPc` / PeerLink `fs.tree`) â€” phone on ho to
  LAN se, warna cloud copy se; jagah ka budget setting mein. (PC pe abhi 113 GB khaali hai, isliye poora phone nahi, chune hue folders.)
- Yeh rakhi hui copies mirror mein dobara nahi jaatin.

## Phase 7 â€” Baad ke kaam (aap chaho to)
- Band device ko file bhejna (cloud "inbox"; device chalu hote hi le le).
- "Sab upload karke band karo" button.
- Naye laptop/phone pe "meri files wapas lao".
- Doosra PC/laptop bhi isi tareeke se.
- Purane versions dekhna (trash se wapas), `FileSystemWatcher` se turant upload.

## Phase 8 â€” Release
Tests, live re-test, PC + phone build, `docs/WHEN_OFF.md`, GitHub (aap kaho to).

## Imaandaar seemaayein (pehle se jaan lo)
- **Pehla upload lamba hai.** Google Drive har account pe lagbhag **2 files/second** hi banne deta hai, internet kitna bhi tez ho.
  - Aapki apni files (Desktop, Documents, photos, Downloadsâ€¦): laptop chalu rahe to **1â€“2 din**.
  - Poori disk (~10 lakh files, Windows + programs): lagataar ~6 din; asal mein (laptop kabhi band, kabhi game, kabhi battery pe) **3â€“6 hafte**.
- **Windows aur programs ki copy "chalegi" nahi.** Woh files cloud pe dikhengi aur download hongi, par unse Windows ya koi program wapas
  khada nahi hota (permissions, registry, hard links copy nahi hote). Yeh disk ki "image" nahi, files ki copy hai. Har mahine Windows update ke
  baad kai GB dobara upload honge aur 30 din trash mein rahenge. Kuch system files Windows padhne hi nahi deta; woh ginti ke saath dikhengi.
  Isiliye yeh hissa switch pe hai: jab chaho band kar do, apni files pe asar nahi.
- **Cloud copy thodi purani ho sakti hai.** Band hone se theek pehle badli file shayad upload na hui ho; har jagah "â€¹timeâ€º tak ki copy" likha rahega.
- **Band device pe badlaav nahi** (v1): cloud copy sirf padhne ke liye.
- **Phone:** apps ka andar ka data (`Android/data`, chat databases), SMS, contacts Android dene nahi deta; mirror 57 GB se kam hoga.
- **Privacy:** poori disk seedhi files ke roop mein Drive pe hogi (browser profile, app data bhi, agar tier 5 ON hai). Google account ka
  password + 2-step hi taala hai. Google upload ki hui files scan bhi karta hai.
- **Internet chahiye** cloud copy kholne ko; bina internet ke sirf Phase 6 wale chune hue folders.

## Doosri session ke saath bantwara
- Meri: engines aur andar ka kaam (`Mirror/*`, WhenOff routes, mount filter, `DeviceMirror.java`, `DriveBackend`/`GoogleAuth`/`DriveApi` badlaav, tests).
- Uski files jinhe chhoona padega (uske release ke baad, bata ke): `PhoneFilesView.cs` (cloud source), phone Permissions page (ek group),
  PC ke naye menu mein page ki jagah, `SettingsView` (start with Windows).
- Naye phone shabd uski `merge.js` se; naya CSS class ho to use batana. Plan file dono sessions ki saajhi hai.

## Verification (har phase)
- `dotnet build CloudSSD.slnx` 0 warning; `dotnet test tests/CloudSSD.Tests`; `node --test tests/mobile-js/*.test.js`; APK 0 warning; page copies ek jaise.
- Live (asli PC + asli phone, kyunki test PC ke paas cloud nahi): chhota test folder scope aur test device naam; Drive mein `Cloud-SSD Devices/<id>` dekhna;
  PC app band karke phone se kholna; phone airplane mode karke PC se kholna; permission ek taraf band â†’ sab ruke; "browse" wale raaste se mirror na dikhe.
  Test ke baad Drive ka test folder aur test files hatana.
- Har phase ke baad Hinglish mein report; agle phase pe bina pooche badhna (aapka purana niyam). Phase 0 ke nateeje pe ruk ke bataunga, kyunki wahan aapke haath chahiye.

## Save
Yeh plan is file mein saved hai. Approve hone par iski copy repo mein `docs/WHEN_OFF_PLAN.md` aur meri memory mein bhi rakhunga.



---

# 3. Bina Google Key Offline Access Plan
# Plan: PC band ho tab bhi uski files phone se â€” bina Google key ke

Tareekh: 14 November 2026. User ka sawaal: "koi aisi cheez nahi kar sakte jisme Google key bhi na lage aur kaam bhi ho jaye?"
Jawab: **haan**. Neeche teen raaste hain jinme app ke andar koi Google key nahi lagti. Pehla (A) mukhya hai.

Purana plan (`docs\archive\WHEN_OFF_PLAN.md`) wahi rehta hai; uska "phone khud Google mein sign in kare" wala hissa (Phase 3)
ban chuka hai aur tab kaam aayega jab kabhi key bane. Yeh plan uske bina kaam chalata hai.

## Seedhi baat

Band PC khud file nahi de sakta. Isliye file ki copy pehle se kahin aur honi chahiye. Google key isliye chahiye thi
ki phone seedha Google Drive khole. Key ke bina copy ko aisi jagah rakhte hain jahan phone bina Google ke pahunch sake:

```
Raasta A (mukhya):   PC (chalu) --Wi-Fi--> phone ke andar copy      PC band -> phone apni copy kholta hai
Raasta B:            PC (chalu) --> Google Drive (jaise abhi)       PC band -> phone ka "Google Drive" app wahi folder kholta hai
Raasta C:            phone --"jaag jao"--> PC chalu ho jata hai      -> files seedhi PC se (bilkul taaza)
```

## Raasta A â€” copy phone ke andar (internet bhi nahi chahiye)

App mein "Offline rakho" pehle se hai (`OfflinePins.java`): chuna hua folder phone par utar jata hai, peeche-peeche
taaza hota rehta hai, aur PC door ho tab phone ki copy se khulta hai. Aaj yeh sirf cloud drive (Z:, Y:) ke folder ke
liye hai; **PC ki apni disk (C:) ke folder ke liye band hai**. Bas wahi kholna hai.

Kyun yeh aapke liye theek hai: aapke apne folder chhote hain (Desktop, Documents, Pictures, Videos, Music mein kul
~40 files; Downloads 5.7 GB) aur phone mein ~74 GB khaali hai. Bhaari cheezein (Steam games 155 GB, Windows) phone par
nahi aayengi; unke liye Raasta B hai.

Kadam:
- **K1 (PC):** PC ki disk ko bhi "drive id" se dena: `disk-c:Users/â€¦` (sirf padhna). Wahi permission jo aaj PC ki files
  dekhne ke liye hai (Browse), wahi roktham. Feature ka naam `fs-disk-roots`. Tests.
- **K2 (phone page):** PC disk ke folder / file par bhi "Keep offline" dikhe; rakhi hui copy PC band hone par khule;
  folder ki list phone ki copy se bane.
- **K3 (ek tap):** More â†’ Permissions â†’ "Files when it is off" mein button **"Is PC ke mukhya folder phone par rakho"**:
  Desktop, Documents, Pictures, Videos, Music, Downloads. Dabane se pehle kitni jagah lagegi woh dikhe; phone mein
  hamesha 500 MB khaali rahe (yeh niyam pehle se hai).
- **K4 (apne aap taaza):** jab bhi phone aur PC judein, badli hui files apne aap aa jayein; PC par mitayi file phone
  se bhi jaye (yeh `OfflinePins` pehle se karta hai; PC disk ke liye jaanchna hai). Ek folder mein 5000 files ki seema
  hai: zarurat ho to badhana.
- **K5 (asli jaanch):** chhota folder rakho â†’ PC app band karo â†’ phone se folder kholo, file padho â†’ PC par file badlo /
  mitao â†’ dobara judne par phone par bhi badle. Jagah kam ho to saaf sandesh.

Seemaayein: sirf chune hue folder; copy utni hi taaza jitni aakhri baar judne par thi (upar samay likha rahega);
band PC par badlaav nahi (sirf padhna).

## Raasta B â€” cloud copy ko phone ke "Google Drive" app se kholna

PC ki files ki cloud copy wala engine ban chuka hai (Devices â†’ When off â†’ Continue). Files Google Drive mein seedhi
(asli naam, asli folder) rakhi jaati hain: `Cloud-SSD Devices/<PC>/files/â€¦`. Inhe kholne ke liye hamare app ko Google
key nahi chahiye agar hum **Google ka apna Drive app** (ya drive.google.com) use karein.

- **K6:** PC phone ko us folder ka pata (Drive folder link) bata de; phone use yaad rakhe. PC band ho to phone par button
  **"Cloud copy Google Drive mein kholo"** â†’ Drive app usi folder par khulta hai.
- Shart: phone ke Drive app mein wahi Google account ho jisme PC copy rakhta hai (abhi `gdrive2` wala).
- Seema: upload dheema hai (saajhi key par har baar ~40 second) aur rclone 2026 mein saajhi key band kar raha hai;
  tab PC ka Google Drive hi rukega, isliye key kabhi na kabhi banani padegi. Tab tak yeh chalta hai.

## Raasta C â€” phone se PC ko chalu karna (Wake)

App mein "PC ko jagao" (Wake-on-LAN) pehle se hai. PC so raha ho (ya kuch PC mein poora band ho tab bhi) phone ek
sandesh bhej kar use chalu kar sakta hai; phir files seedhi PC se, bilkul taaza.

- **K7:** is PC par jaanchna ki yeh chalta hai ya nahi (network card ki setting, BIOS). Chale to phone par PC band dikhne
  par "PC chalu karo" sabse upar.
- Seema: sirf ghar ke Wi-Fi par (ya ghar mein koi doosra device sandesh pahunchaye); har PC / Wi-Fi card nahi karta;
  PC sach mein chalu ho jata hai.

## Kram

1. K1 â†’ K2 â†’ K5 ka chhota hissa (ek folder haath se rakh kar PC band karke kholna). Yahin pehla asli nateeja.
2. K3, K4, phir poori K5.
3. K7 (sirf jaanch, 10 minute).
4. K6.
5. Report; user kahe to GitHub.

Har kadam ke baad `CHALU_KAAM.md` ka "Abhi ka haal" badalna. Backup wahi chalta rahega
(`backup\2026-11-14_1544_band-pc-ki-files-phone-se`): yeh usi kaam ka hissa hai, naya `start` nahi.

## Jo is plan mein nahi badalta

- Dono taraf ki "haan" (Files when it is off) ka niyam wahi. Raasta A PC ki files dekhne ki maujooda permission par
  chalta hai; ek-tap wala button "Files when it is off" ke andar hai.
- Google ke token PC aur phone ke beech kabhi nahi bheje jaate.
- Kuch bhi Claude app / claude.ai par nahi; yeh plan sirf is PC ki file hai.

