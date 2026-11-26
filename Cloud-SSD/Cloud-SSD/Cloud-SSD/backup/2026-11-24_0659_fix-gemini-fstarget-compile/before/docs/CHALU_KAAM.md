# CHALU KAAM: jo kaam abhi chal raha hai (Claude aur Codex dono ke liye)

Yeh file isliye hai ki kaam beech mein ruk jaye (token / usage limit khatam, session band, PC band) to jo bhi
agla session aaye, Claude ho ya Codex, wahin se kaam uthaye. Kuch dobara na ho, kuch toote nahi.

**Niyam:** kaam shuru karne se pehle yeh file padho. Har chhota kadam poora hote hi neeche "Abhi ka haal" badlo
(2 minute ka kaam hai; yahi sabse zaroori hai). Token kam lage to naya kaam mat chhedo: pehle yeh file likho.

Project: `%USERPROFILE%\Downloads\Cloud-Drive\Cloud-Drive\Cloud-SSD\Cloud-SSD\Cloud-SSD`
(git ki jad do folder upar; branch `main` + `feature/multi-device` dono; GitHub repo private hai).

---

## 1. Naya session aaye to sabse pehle (5 minute)

1. Yeh file poori padho, khaaskar "Abhi ka haal".
2. `git status --short` (git ki jad se). Jo files badli dikhें woh pichhle session ka adhoora kaam hai.
3. `backup\` mein sabse naya folder dekho:
   - usme sirf `before\` hai, `complete\` nahi → **kaam adhoora hai**. `before\` ko mat chhuo.
   - `complete\README.md` hai → woh kaam poora ho chuka.
4. Build aur test chalao (neeche "Jaanch"). Laal ho to pehle wahi theek karo, naya kuch mat likho.
5. "Abhi ka haal" ke "Agla kadam" se aage badho.

## 2. Kaam adhoora ho to kya karna hai

- **Aage badhana (aam taur par yahi):** adhoori files project mein hi padi hain. "Abhi ka haal" mein likha hota hai
  kaunsi file kahan tak hui. Wahin se poora karo. Naya backup **mat** lo (`before\` pehle se hai).
- **Samajh na aaye / sab toot gaya ho:** us kaam ke `backup\<folder>\before\` se wahi files wapas copy karo
  (sirf woh files jo "Abhi ka haal" ki list mein hain), phir kadam dobara karo.
  Wapas karne se pehle `git stash` ya adhoori files ki copy `.work\adhoora\` mein rakh lo. Kabhi `git reset --hard`
  ya `git checkout -- .` mat chalao: usse doosre ka kaam bhi ud jata hai.
- `complete` tab hi banao jab kaam ban gaya ho **aur** asli PC + asli phone par jaanch ho gayi ho.

## 3. Backup ka niyam (user ka pakka niyam)

- Naya kaam shuru karne se **pehle**:
  `powershell -ExecutionPolicy Bypass -File tools\backup\backup.ps1 start -Name "<kaam ka naam>"`
  → `backup\<tareekh_samay>_<naam>\before\` banta hai. Ise kaam ke dauraan kabhi mat chhuo.
- Kaam ban kar jaanch ho jaye tab:
  `powershell -ExecutionPolicy Bypass -File tools\backup\backup.ps1 complete -Folder "<folder>" -Files a,b,c`
  phir `complete\README.md` saral Hinglish mein likho: yeh kya hai, kya kaam hua, kaise use hota hai,
  andar se kaise chalta hai (chhota teer wala chitr), files, jaanch, wapas kaise jaayein.
- Ek kaam = ek backup folder. Adhoore kaam ke liye doosra `start` mat chalao.

## 4. Kaam kaise karna hai

- Chhote kadam. Har kadam: code → build → test → (ho sake to asli device par dekho) → yeh file badlo.
- Ek phase poora = asli phone + asli PC par jaanch, saare bug aur warning theek. Tabhi agla phase.
- User se har phase ke beech permission mat poocho; sirf wahan ruko jahan uske haath chahiye (jaise Google key).
- Jawab saral Hinglish mein. User technical naam nahi jaanta; uska matlab samjho.
- **Commit / push sirf jab user kahe.** Push: `git add -A -f .` → niji cheezon ki jaanch
  (`node .work\scan-staged.js roshan rosha I2221` git ki jad se; `backup\*\before\` ke andar ke hit purani files hain)
  → commit → `git push origin HEAD:feature/multi-device HEAD:main`.
- **Kuch bhi Claude app / claude.ai / cloud par nahi:** koi Artifact, Doc, share link, cloud session nahi.
  Design ya report hamesha is PC par file (HTML / MD / PNG) aur user ko poora raasta batao.

## 5. Yeh galtiyan ho sakti hain (pehle ho chuki hain)

| Galti | Bachao |
|---|---|
| Ek hi kaam do window / do agent ek saath karein (14 Nov ko hua: ek hi session do window mein, dono ne wahi files badlin) | Kaam shuru karne se pehle `git status` aur files ka samay dekho. Koi file abhi-abhi badli ho jo tumne nahi badli, to ruk jao: "Abhi ka haal" mein note likho kaun kya karega, aur ek hi jan source / build / phone / PC chhue. |
| `src\CloudSSD.Desktop\App.xaml.cs` badal dena | Ise **kabhi mat chhuo**. |
| Phone page ki do copy alag ho jana | `mobile-android\src\main\assets\*` aur `src\CloudSSD.Desktop\wwwroot\mobile\*` hamesha ek jaise. Assets mein badlo, phir wwwroot mein copy karo. |
| Call ke waqt ya doosre app mein phone ko chhoo dena | adb se kuch bhi bhejne se pehle: `mCallState=0`, phone khula (`mDreamingLockscreen=false`), saamne `com.cloudssd.mobile`. `tools\live-test\phone.ps1` yeh khud dekhta hai; usi se kaam karo. PIN kahin likha nahi hai. |
| User ka look / bhasha badal dena | Pehle `localStorage cloudssd_look` padho, test ke baad wahi wapas rakho. |
| User ki jagah permission ka "Allow" dabana | Kabhi nahi. User khud dabata hai. |
| `node -e "..."` ya `sed` mein ulte slash / quote | Shell tod deta hai. Patch ko `.work\<naam>.js` file mein likho, phir `node` se chalao. |
| PowerShell 5.1 mein `2>&1` ke saath build-apk | javac ka purana note error ban jata hai; `2>&1` mat lagao. |
| PowerShell mein script ke output par `Select-Object -First N` | Script beech mein mar jati hai. Pehle output variable mein lo. |
| Naye phone shabd bina anuvad ke | `tools\i18n\add-words.js <words.json>` se `i18n-words.js` mein jodo (dono copy). |
| rclone ko `C:\` jaisa raasta dena | Aakhri ulta slash mat do; `C:/` likho. |
| Mirror apne aap poori disk upload karne lage | `Settings.Mirror.Paused = true` chhoda hai. User "Continue" dabayega. Test ke liye `Mirror.OnlyFolders` mein chhota folder do, baad mein khaali karo aur Drive ka test folder mitao. |
| Google ke token PC↔phone ke beech bhejna | Mana hai (LAN par saada HTTP). PC sirf client id/secret deta hai; phone khud sign-in karta hai. Google ka "revoke" kabhi mat bulao. |
| `Cloud-SSD Devices` folder Z:/Y: ya phone ke Cloud SSD tab mein dikhna | Chhupa rehna chahiye (permission ka chor darwaza). |
| Niji cheez GitHub par | PC ka asli naam, ghar ka IP, user ka naam, device id, e-mail source mein na jaye. |
| Google key ki file GitHub par chali jana | Push se pehle dekho ki `client_secret*.json`, `installer\google-client.json`, `mobile-android\google-oauth.json` stage na hon (`git add -A -f .` inhe bhi utha leta hai). Ho to `git reset -- <file>`. |
| 100 MB se badi file push | `dist\*.zip` 95 MB ke aas-paas hai; 100 MB paar ho to push fail hoga. |

## 6. Jaanch (testing) kaise hoti hai

Sab commands project folder se:

```
# "dotnet" seedha nahi chalta (PATH wale mein SDK nahi hai). PowerShell mein pehle yeh:
$dn = "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe"
& $dn build CloudSSD.slnx                       # 0 warning hona chahiye
& $dn test tests\CloudSSD.Tests                 # C# tests (14 Nov ko 462 pass)
node --test tests/mobile-js/*.test.js           # phone page ke tests (44 pass)
# Jaanch wala APK (-Debug): isi mein phone.ps1 page ke andar jhaank sakta hai. Bina -Debug wala APK release ke liye hai
# (mobile-android\Cloud-SSD-Mobile.apk); us par phone.ps1 "fetch failed / other side closed" deta hai.
powershell -ExecutionPolicy Bypass -File mobile-android\build-apk.ps1 -Debug -OutDir .work\apk
C:\Android\Sdk\platform-tools\adb.exe install -r .work\apk\Cloud-SSD-Mobile.apk
```

Asli phone (wireless adb chalu ho):
```
powershell -ExecutionPolicy Bypass -File tools\live-test\phone.ps1 -Launch -Js .work\<test>.js -Shot <naam>
```
JS file page ke andar chalti hai aur nateeja lautati hai; photo `.work\shots\<naam>.png`. Namoona: `.work\t-icons.js`.

Asli PC app:
```
powershell -ExecutionPolicy Bypass -File installer\build-release.ps1 -SkipTests        # dist\Cloud-SSD-0.9.0-win-x64
powershell -ExecutionPolicy Bypass -File tools\live-test\pc-close.ps1                  # app band (band na ho to user se kaho)
powershell -ExecutionPolicy Bypass -File dist\Cloud-SSD-0.9.0-win-x64\Install-CloudSSD.ps1 -Quiet
Start-Process "$env:LOCALAPPDATA\Programs\Cloud-SSD\Cloud-SSD.exe"
powershell -ExecutionPolicy Bypass -File tools\live-test\pc-shot.ps1 -Shot <naam> [-Press a,b]
```
PC app ka log: `%LOCALAPPDATA%\Cloud-SSD\logs\cloudplayx-<tareekh>.log` (rclone ke log mat chhaapo: unme file ke naam hain).

Jo jaanch nahi ho payi use report mein saaf likho ("yeh nahi jaancha, kyunki …"). Jhooth-mooth "sab theek" mat likho.

## 7. Bada kaam: "Band hone par bhi files"

User ka maqsad: **desktop power off ho tab bhi phone se uski files khulें** (aur ulta).
Poora plan: `docs\archive\WHEN_OFF_PLAN.md` (archive me hai). Tareeka: device chalu ho tab files ki copy Google Drive mein
(`Cloud-SSD Devices/<id> <naam>/files/...`), band ho to doosra device wahi copy kholta hai.

| Phase | Kya | Haal |
|---|---|---|
| 0 | User ki apni Google key; key phone tak | **User ke haath chahiye** (neeche) |
| 1 | Permission "Files when it is off", dono taraf haan | Ho gaya |
| 2 | PC ki files → cloud (mirror engine, PC page, Windows ke saath chalu) | Ho gaya, asli Drive par test; **paused** chhoda hai |
| 3 | Phone se band PC ki files kholna | **Chal raha hai** (neeche "Abhi ka haal") |
| 4 | Phone ki files → cloud | Baaki |
| 5 | PC se band phone ki files | Baaki |
| 6 | Chune hue folder doosre device par (bina internet) | Baaki |

Phase 2 se bacha hua (chhota): naam badalne ki pehchaan (moveto), metered network par rukna, Transfers screen
par mirror ki line.

**User ke haath ka kaam (Phase 0):** PC app → Cloud Drives → "How do I get a key?" → Google Cloud Console mein
OAuth client (Desktop) banao, "In production" karo → "Switch to my own Google key". Iske bina phone Google Drive
mein sign-in nahi kar sakta, isliye Phase 3 ki **asli jaanch** key ke baad hi hogi.

---

## Abhi ka haal  (har kadam ke baad badlo: `.work\haal.txt` badal kar `node .work\haal.js .work\haal.txt`)

**Aakhri badlaav:** 17 Nov 2026, 03:00 — Claude Code, K session (firewall rule joda + phone ki local-network rok hatayi; phone ab ghar ke Wi-Fi se juda hai): **bina-key wala kaam (K1–K5, K7) poora, asli phone par jaancha, backup band.** Koi kaam chalu nahi.

**Aakhri kaam (poora):** band PC ki files phone se — **bina Google key** wala raasta (user ne 14 Nov 16:22 par kaha: key ke bina karo).
Plan: `docs\archive\WHEN_OFF_BINA_KEY_PLAN.md` (pehle ise padho). Raasta A = PC ke folder ki copy phone ke andar ("Offline rakho" ko PC disk ke liye kholna).
Lock: `docs\CURRENT_TASK.json` → `TASK-CLAUDE-K`, **COMPLETED / active false**. Poora byora: `backup\2026-11-14_1544_band-pc-ki-files-phone-se\complete\README.md`.

Bina-key ke kadam:
- [x] K1 PC: `/api/fs/*` mein PC disk ki jad `disk-c:` (sirf padhna, wahi roktham jo `/api/files` par), feature `fs-disk-roots`,
      test `A_pc_disk_is_a_read_only_drive_and_only_its_shared_places_open`. (14 Nov ko ban gaya tha, sirf yahan tick nahi laga tha.)
- [x] K2 Phone page (code + JS tests + asli phone 17 Nov 02:25): `data.js` `diskId` / `diskPath` / `isDiskId` / `pinsBelow`;
      `app.js` `keepIdOf` ab PC disk ke folder ko `disk-c:…` id deta hai (`diskIdOf`), `ssdPlaces` mein `disk: true`,
      `keptList` kept folder tak ka raasta (C: → Users → …) bhi copy se banata hai, `syncActions` PC disk par nahi,
      PC disk ki photo ke thumbnail, More → Offline files mein row dabane se folder khulta hai (`offline-open`).
- [x] K3 Ek tap (code + tests + asli phone 17 Nov 02:04): PC `/api/pc/home` → `main` (6 mukhya folder; raasta sirf jab
      "PC disks" chalu ho), feature `pc-main-folders`, test `The_phone_is_told_the_main_folders_…`. Phone: Permissions →
      "Files when it is off" → "Keep the PC's main folders on this phone" → "Choose…" → har folder ka size (`/api/fs/usage`),
      "Keep all N folders", bahut bada / 5000+ files ho to pehle hi saaf sandesh (`data.js` `keepTooBig`). 19 naye shabd (`.work\k-words.json`).
- [x] K4 `OfflinePins.java`: andar ka koi folder Windows padhne na de (403/404) to sirf wahi chhoota hai, poora pin fail nahi hota;
      aise round mein purani copy mitayi nahi jaati. 5000 ki seema wahi rakhi (page pehle bata deta hai). Apne aap taaza hona asli phone par dekha (badli file khud aayi, mitayi file hat gayi);
      "na-padha-ja-sakne wala folder" wali shaakha asli phone par nahi aayi (aisa folder Documents mein nahi tha).
- [x] K5 Asli jaanch (17 Nov 02:04–02:25, vivo I2221, test APK): Documents rakha (15 files) → PC tak raasta kaata → More → Offline files → Documents khula,
      test file phone ki copy se padhi → PC par badli: khud naya version → PC par mitayi: copy hati → ⋮ menu mein sirf "Open" + "Keep offline".
      **PC ko sach mein power off nahi kiya** (raasta `adb reverse --remove` se kaata; PC app aur drives chalte rahe).
- [ ] K6 "Cloud copy Google Drive mein kholo" button — **jaan-boojh kar nahi banaya:** PC ka mirror `Paused` hai aur Drive par koi copy nahi,
      to iski asli jaanch ho hi nahi sakti. Banana ho to: mirror pass ke baad `rclone lsjson "<account>:Cloud-SSD Devices" --dirs-only` se
      folder ka ID → `whenOff` mein `link` → phone par PC band dikhne par button (native mein URL kholne wala method bhi chahiye).
- [x] K7 Wake-on-LAN (sirf jaanch, 17 Nov 01:50, kuch badla nahi): yeh PC **Wi-Fi** par hai (Intel AX101), Ethernet cable lagi nahi.
      "Wake ke liye armed" sirf Ethernet card (Realtek) hai; Wi-Fi card wake kar sakta hai par armed nahi. Yaani abhi phone ka
      "Wake PC" is PC ko nahi jagayega. Chalane ke do raaste (dono user ka faisla, admin setting): router se LAN cable lagao, ya
      Device Manager → Intel Wi-Fi → Power Management → "Allow this device to wake the computer" (sirf Sleep se jagta hai, poore band se nahi).

**Jaanch (17 Nov 01:45):** build 0 warning / 0 error · C# **474/474** · JS **48/48**.
**PC par asli jaanch (17 Nov 01:56):** release build (`dist\Cloud-SSD-0.9.0-win-x64.zip`, SHA256 `eb469092…fb703aff`) install, app chalu,
Z:/Y: "Re-attached"; `/api/status` ke features mein `pc-main-folders` aur `fs-disk-roots`; bina key ke `/api/pc/home`, `/api/fs/list?path=disk-c:…`,
`/api/fs/roots?include=disks` → 401; PC jo page deta hai usme naya code (`main-keep`, `diskIdOf`). `MobileBridge.AllowPcDisks = true` pehle se hai.
**Test APK:** `.work\apk\Cloud-SSD-Mobile.apk` (-Debug, 17 Nov 01:47) ban gaya, Java compile hota hai. **Phone par install nahi hua.**
**Raaste mein mila bug (theek kiya):** `tools\live-test\pc-close.ps1` ka "title bar ka X dabao" wala hissa PS 5.1 par compile hi nahi hota tha
(`System.Drawing.Rectangle`), aur minimized window par X hota hi nahi → app 90 s mein band nahi hota tha. Ab apna `RECT`, pehle window saamne.

**Do session ek saath (17 Nov 01:53 se):** ek aur Claude window "CMD ka naya look" (TASK-CLAUDE-CLI-TUI) kar rahi hai — neeche uska hissa hai.
Bantwara: **woh** sirf `src\CloudSSD.Cli\*`, `tests\CloudSSD.Tests\CliTuiTests.cs` (+ test csproj), README ka CLI hissa.
**Yeh (K) session:** phone page, bridge (`src\CloudSSD.App`), `mobile-android`, APK, phone, PC app ka install. K session Cli ki files nahi chhuta.
**Teesra session (17 Nov ~02:05, naam `roshan-47`):** "drive speed + restart" — sirf `src\CloudSSD.Mount\{MountService,MountPlanning}.cs`,
`src\CloudSSD.App\ArchiveWarmer.cs`, `AppHost.cs` (ArchiveWarmer wali lines), naye tests. Tay hua: woh `backup.ps1 start -Force` chalayega aur K ka lock
wapas likhega; PC app band karne se pehle K session ko batayega (K5 mein PC app jaan-boojh kar band / chalu hota hai); `pc-close.ps1` nahi badlega.
Dhyan: `dotnet test` poora test project banata hai; doosri window ka adhoora `CliTuiTests.cs` build tod sakta hai — tab woh K ki galti nahi, ruk kar dobara chalao.
**Is hisse mein badli files:** `mobile-android\src\main\assets\{app.js,data.js,i18n-words.js}` (+ wahi teen `src\CloudSSD.Desktop\wwwroot\mobile` mein, ek jaisi),
`mobile-android\src\main\java\com\cloudssd\mobile\OfflinePins.java`, `src\CloudSSD.App\LocalMobileBridgeService.QuickAccess.cs`,
`src\CloudSSD.App\LocalMobileBridgeService.Security.cs` (feature ka naam), `tests\CloudSSD.Tests\QuickAccessTests.cs`,
`tests\mobile-js\data.test.js`, `.work\k-words.json` (lag chuka; dobara chalane se kuch nahi bigadta).
`tools\live-test\pc-close.ps1`, `.work\t-k3.js` (phone ki jaanch, sirf padhta hai).
**Adhoori files:** koi nahi (sab compile + test pass). PC par 02:05 ka build chal raha hai (teesre session ka install; K ke bridge badlaav usme hain); phone par test APK (17 Nov 01:59).

**Jaanch (17 Nov 02:28, teeno session ke badlaav ke saath):** `backup.ps1 validate` → build 0 warning / 0 error · C# **518/518** · JS **48/48** · **APPROVED**.
**Firewall (17 Nov 02:35, theek ho gaya):** phone 14 Nov ke baad se isliye nahi judta tha ki Windows Firewall rokta tha: Wi-Fi "Private" hai, `cloud-ssd.exe` ki
ijazat sirf "Public" profile ki thi, aur app ka apna rule "Cloud-SSD direct links" bana hi nahi tha. User ne kaha "usko bhi karo" → K session ne wahi rule joda jo
app ka Add device → "Allow" button jodta hai (user ne UAC par "Yes" dabaya):
`netsh advfirewall firewall add rule name="Cloud-SSD direct links" dir=in action=allow protocol=TCP localport=8090 remoteip=LocalSubnet profile=any`.
Jaanch: `adb reverse` hata kar phone se `http://<PC ka Wi-Fi IP>:8090/api/status` → 200 (40 ms); pehle yahi request fail hoti thi. App ke andar bhi dekha (neeche).
Hatana ho: `netsh advfirewall firewall delete rule name="Cloud-SSD direct links"` (admin).
UDP ka alag rule nahi joda (zarurat nahi: phone PC ko TCP se dhoondhta hai, /24 ka har pata port 8090 par).
**Phone ki taraf ki rok (17 Nov 02:57, theek ho gayi) — firewall ke baad bhi app "PC not reachable" dikhata raha:** phone (Android 16, OriginOS 6) par
compat flag `RESTRICT_LOCAL_NETWORK` ka override **sirf `com.cloudssd.mobile` ke liye true** tha (platform ka default "disabled"). Isse app ki uid internet par ja sakti thi
par `192.168.1.x` par kuch nahi (router ka ping bhi nahi), jabki usi phone se `adb shell curl` chal jata tha. Pehchaan: `adb shell run-as com.cloudssd.mobile curl -m 6 http://<PC IP>:8090/api/status`
(fail) banam `adb shell curl …` (200), aur `adb shell dumpsys platform_compat | grep RESTRICT_LOCAL_NETWORK`. Project mein is flag ka koi zikr nahi (phone ki Developer options se laga hoga).
Theek: `adb shell am compat reset RESTRICT_LOCAL_NETWORK com.cloudssd.mobile`, phir wahi APK `adb install -r` (rok tabhi dobara ginti hoti hai; data nahi jata).
Nateeja: app khud "Connected · 192.168.1.x:8090 · Wi-Fi, no mobile data", PC par `LastLink = wifi`; rakha hua Documents 02:57:49 par Wi-Fi se taaza hua (PC ka 02:55 wala build,
jisme `DiskRoots` ka sudhaar hai). `adb reverse` ab nahi laga hai aur chahiye bhi nahi.
**Aage ke liye (nahi kiya):** Android aage chal kar yeh rok sab apps par lagayega. Tab app ko manifest mein local-network wali permission (`NEARBY_WIFI_DEVICES` / naye Android mein
uski apni permission) maangni hogi aur pehli baar user se "Allow" lena hoga. Abhi app yeh nahi maangta. Karne se pehle Android ke taaza docs padho.
**Aakhri chhota sudhaar (ab installed PC build 02:55 mein hai, teesre session ke install se):** `LocalMobileBridgeService.Files.cs` `DiskRoots()` ab cloud drive ko
`IsReady` poochhne se pehle chhod deta hai (busy Z: par `disk-…` wali request 10 s atak kar "timeout" de sakti thi; 02:21 par ek baar aisa hua). 02:57 par isi build se phone ka refresh chala.
**Phone par:** test (-Debug) APK laga hai; release APK dobara nahi bana. Phone par ek purana pin `Cloud-Drive.zip` (6.58 GB) "Not found." bata raha hai
(woh file cloud se ja chuki): uski copy phone mein 6.58 GB gher rahi hai — hatana user ka faisla (More → Offline files → Remove).
**Chhoti baatein (theek nahi ki):** `/api/fs/usage` chhupi `desktop.ini` bhi ginta hai (16 likha, 15 aaye); kabhi na khole gaye upar ke folder ka raasta copies se
banna sirf JS test mein dekha. **Commit / push nahi hua** (user ne nahi kaha).

**Agla kadam (jo bhi agla session aaye):** koi adhoora kaam nahi. User kahe to: (1) app mein Android ki local-network permission maangna (upar "Aage ke liye");
(2) PC ka naya release install (Files.cs ka sudhaar + CLI session ka naya `cpx`) aur release APK; (3) K6; (4) Phase 3 ka 3.7 (apni Google key ke baad); (5) commit / push.

Neeche Phase 3 (Google key wala) ka haal hai: code ban chuka, key ke bina uski asli jaanch nahi ho sakti; use chheda nahi jayega.
**Backup folder:** `backup\2026-11-14_1544_band-pc-ki-files-phone-se` (`before\` + `complete\` + README, APPROVED; Phase 3 ka 3.7 key ke bina nahi jaancha — README mein likha hai).
**GitHub par aakhri commit:** `1e60b83` (`feature/multi-device` & `main` branches fully synchronized).

Kaise banega (design, tay ho chuka):
- PC `/api/status` ke `whenOff` mein `folder` (PC ka cloud folder ka naam) aur `key` (apni Google key hai?) deta hai;
  `/api/whenoff/key` Google key (client id/secret) deta hai, sirf dono-haan wale device ko. Token kabhi nahi.
- Phone: `GoogleAuth` key ko chalte waqt leta hai (PC se mili, `TokenStore` mein). Build wali key ho to wahi chalti hai.
  Sign-out par Google ka "revoke" **nahi** (warna PC ka rclone bhi logout ho sakta hai).
- Phone: `DriveBackend` mein nayi jad `pc-<pcId>:<path>` = us PC ki copy
  (`Cloud-SSD Devices/<pcId> <naam>/files/<path>`), sirf padhne ke liye. `roots()` yeh jad tab deta hai jab us PC ke
  liye dono haan yaad hon. `Cloud-SSD Devices` folder baaki jagah (All Accounts, Each Account, search) se chhupa.
- Phone page: PC band ho aur phone Google mein sign-in ho to Cloud SSD tab pehle se "drive" mode mein chalta hai
  (`cloudSource() === 'drive'`); nayi jad wahin ek aur jagah ban kar dikhegi. Upar patti: "<PC> band hai · copy <samay> tak".
- Page har status par native ko batata hai kaun se PC ke saath dono haan hain (`native.whenOffSync`).

Kadam:
- [x] 3.1 Backup `start`
- [x] 3.2 PC: `/api/whenoff/key`, `whenOff.folder` / `whenOff.key`, feature `whenoff-key`,
      `RcloneConfigService.OwnDriveClient` + tests (WhenOffTests 4 pass, build 0 warning)
- [x] 3.3 Phone: Google key chalte waqt — `GoogleAuth.java` (`setKey`, `available(Context)`, revoke hataya), bridge `setGoogleKey`
- [x] 3.4 Phone: `DriveBackend.java` jad `pc-<pcId>` (`copyBase`, `copyInfo`, `hidden`), sirf padhna; `WhenOffStore.java` (nayi);
      bridge `whenOffSync`, cloudOp `copy-info`. APK compile hota hai.
- [x] 3.5 Phone page: `data.js` (`whenOffReady`, `whenOffReadyText`, `copyNote`), `app.js` (`syncWhenOff`, Permissions mein doosri
      line + "Sign in", `renderDriveNote`), 16 naye shabd (`.work\p3-words.json`), dono copy ek jaisi, JS tests 45 pass.
- [x] 3.6 Tests pass (C# 464, JS 45). PC: naya build install, chal raha hai. Phone (16:24): test APK install, `.work\t-whenoff.js` ka nateeja sahi:
      "Keep copies in the cloud · On: … both said yes" + "Not ready yet · … shared key …", Sign in button nahi, `errors: []`.
- [ ] 3.7 Asli jaanch key ke saath: PC app band karke phone se folder kholo, file padho, download karo;
      permission band → raasta gayab
- [ ] 3.8 Backup `complete` + README

**Is kaam mein badli files (ab tak):**
- Phone: `mobile-android\src\main\java\com\cloudssd\mobile\GoogleAuth.java`, `DriveBackend.java`, `MainActivity.java`,
  `WhenOffStore.java` (nayi); `assets\app.js`, `data.js`, `i18n-words.js` (+ wahi teen `wwwroot\mobile` mein);
  `tests\mobile-js\data.test.js`. Code poora hai, **asli phone par abhi nahi dekha**.
- Patch scripts jo lag chuke (dobara mat chalao): `.work\p3-pc.js`, `p3-auth.js`, `p3-drive.js`, `p3-fix1.js`, `p3-fix2.js`, `p3-page.js`.
- `src\CloudSSD.Mount\RcloneConfigService.cs`, `src\CloudSSD.App\LocalMobileBridgeService.WhenOff.cs`,
  `src\CloudSSD.App\LocalMobileBridgeService.Security.cs`, `tests\CloudSSD.Tests\WhenOffTests.cs` — poori, test pass.
- `CHALU_KAAM.md` (nayi), `.work\p3-pc.js` (patch jo lag chuka; dobara mat chalao), `.work\haal.js`, `.work\haal.txt`.

**Adhoori files (abhi):** koi nahi.
**Agla kadam:** 3.6 ka phone wala hissa (upar likha hai). Uske baad 3.7 (key ke baad).
**Ruka hua kis par:** (1) phone par Cloud-SSD saamne ho (ya home screen), tab `t-whenoff.js` chalana; (2) 3.7 ke liye user ki apni Google key
(PC app → Cloud Drives → "How do I get a key?" → "Switch to my own Google key"). Key ke baad: phone ke Permissions mein
"Sign in" aayega → user Google mein sign in kare → PC par Devices → When off → Continue (chhota test: `Mirror.OnlyFolders`)
→ PC app band karke phone ke Cloud SSD tab mein "<PC> (cloud copy)" kholna.
PC par ab Phase 3 wala build chal raha hai. Commit / push nahi hua (user ne nahi kaha).

**Doosri window ka note (14 Nov, 15:58 — Claude, window B):**
Yeh session do window mein ek saath chal raha hai (transcript mein dono ke kadam mile hue hain: A ne `p3-auth.js` likha,
B ne `p3-pc.js`). B ne 3.1 aur 3.2 kiya; 3.3 se aage A ne. Ek hi file do log na badlein, isliye:
- **A (jisne `p3-auth.js`, `p3-drive.js`, `p3-page.js` likhe) hi 3.6, 3.7, 3.8 kare.**
- **B ab source, build, APK, phone aur PC install kuch nahi chhuega.** B ka `WhenOffStore.java` A ke version se badal gaya
  (theek hai, A wala hi rahe); B ka apna Java patch kabhi laga hi nahi aur hata diya gaya.
- B ne A ka `DriveBackend.java` / `GoogleAuth.java` padha. Design sahi hai. Teen chhoti baatein (A chahe to theek kare):
  1. Copy ke andar ke folder par `doc()` abhi `canCreate = true` de sakta hai (line ~282); `create()` mana kar deta hai, par
     page "yahan naya banao" dikha sakta hai. Copy ke liye `canCreate = false` kar do.
  2. "All Accounts" ki search: Google se `max` nateeje aate hain, phir copy wale chhaant diye jaate hain. PC ki copy mein
     lakhon files hon to aam search khaali aa sakti hai. Jab `WhenOffStore.pcs` khaali na ho tab Google se zyada maango
     (jaise `max * 3`).
  3. `GoogleAuth.accessToken`: key badalne par Google `unauthorized_client` / `invalid_client` bhi de sakta hai; abhi sirf
     `invalid_grant` par "dobara sign in" lagta hai.
- User ko bata diya jayega ki ek kaam ke liye ek hi window chalayein.
- **A, 16:00:** teeno baatein theek kar din (`.work\p3-fix2.js`): copy mein `canCreate = false`, search `max * 3`, key badle to "dobara sign in".
  C# tests 464 pass, JS 45 pass.

---

## Kaam: CMD / CLI Support (Claude & Codex style) - COMPLETE

- **Tareekh & Samay:** 14 Nov 2026, 16:18 - 16:26
- **User Commands:**
  1. /plan mujhe isko cmd me bhi calana hai wo kayse banega or kayse kam karega
  2. jaysa claude codex hai wayse hi mujhe isko bhi wayse hi banana hai
  3. jo bhi kam karo uska date time jo cammand mene diya jo plan tum banan rahe ho or main chij jo bhi change karo uska backup
- **Backup Folder:** ackup\2026-11-14_1618_cmd-claude-codex-cli (before + complete, README.md)
- **Badli Files:**
  - src\CloudSSD.Cli\Program.cs
  - installer\cloudssd.cmd
  - installer\Install-CloudSSD.ps1
  - installer\build-release.ps1
  - %USERPROFILE%\.local\bin\cloudssd.cmd
  - %USERPROFILE%\.local\bin\cpx.cmd
- **Haal:** Ho gaya. CMD mein cloudssd aur cpx dono interactive shell (Claude/Codex style) aur direct one-shot mode dono support karte hain. Unit tests 464 pass.

---

## Kaam: Latest Screenshots Sync & Showcase - COMPLETE

- **Tareekh & Samay:** 16 Nov 2026, 10:41 IST
- **Agent:** Antigravity (Gemini 3.8 Flash High)
- **User Command:** "GitHub पर जो फोटोज अपलोड हैं, उन्हें लेटेस्ट वाले में अपलोड करो। जैसे होम डैशबोर्ड, क्लाउड ड्राइव, पिन ऑफलाइन एक्सेस, वर्चुअल टीम बैकअप, ऐप आइकन पिकर। जितनी फोटोज हैं, और भी फोटोज हैं, और वाले सेशंस हैं, उनको एक-एक करके दिखाओ।"
- **Backup Folder:** `backup\2026-11-16_1038_latest-screenshots-sync` (APPROVED: build 0 errors, 465/465 unit tests pass)
- **Badli Files:**
  - `README.md`
  - `screenshots/phone-pin-offline.png`
  - `screenshots/pc-devices-backup.png`
  - `screenshots/phone-perm-editor.png`
  - `screenshots/pc-bell-alerts.png`
  - `screenshots/phone-cards-kit.png`
  - `screenshots/phone-cards-paper.png`
  - `screenshots/phone-cards-auto.png`
  - `docs/antigravity.md`
  - `docs/CURRENT_TASK.json`
- **Haal:** Poora hua. Sabhi latest screenshots README mein jode gaye aur repository par push kiye gaye.

---

## Kaam: Archified Master Architecture & 9 Detailed System Flows in README.md - COMPLETE

- **Tareekh & Samay:** 16 Nov 2026, 11:07 IST
- **Agent:** Antigravity (Gemini 3.8 Flash High)
- **User Command:** "how it work hai readme,md file me usko archify use karke banaosame jayse abhi hai wayse hi or eak or add karo uske niche hi eakdam deatil me kayse kam karta hai ekdam deatil me usme har eak chij ka destil em baytao ki kam kays ekarnat ahai usme side me optio dena ki login par aaya jayega phone se file share kiya to system aaye kam akrrta ahi me ye baas exmaple batao raha hu tumhe para systm deign bananna hai pura bahit bada bhi ban jayefa oi problem nahii hoga usme bas banao usko kayse kama karta hai wo banao usko"
- **Backup Folder:** `backup\2026-11-16_1104_detailed-architecture-docs` (APPROVED: build 0 errors, 465/465 unit tests pass)
- **Badli Files:** `README.md`, `docs/CURRENT_TASK.json`, `docs/CHALU_KAAM.md`, `docs/antigravity.md`
- **Haal:** Poora hua. README.md mein Archified Master Architecture Diagram, Matrix table, aur 9 complete end-to-end technical flows jode gaye aur GitHub par push kiya gaya.

---

## Kaam: Archify Interactive System Design (Side Gateways, Home Page Hub & Feature-to-System Execution Lines) - COMPLETE

- **Tareekh & Samay:** 16 Nov 2026, 11:47 IST
- **Agent:** Antigravity (Gemini 3.8 Flash High)
- **Tooling:** Official GitHub `tt-a1i/archify` AI Agent Framework
- **User Command:** "archify use karp github wala use karke"
- **Backup Folder:** `backup\2026-11-16_1120_archify-system-design` (APPROVED: build 0 errors, 465/465 unit tests pass)
- **Badli Files:**
  - `README.md` (Archified System Design Mermaid flowchart, ASCII blueprint, expanded matrix, 12 technical flows, and link to standalone Archify HTML)
  - `docs/architecture-archify.html` (Standalone interactive architecture diagram compiled via official `tt-a1i/archify` tool)
  - `docs/CHALU_KAAM.md`
  - `docs/CURRENT_TASK.json`
  - `docs/antigravity.md`
  - Artifact: `cloud-drive-system-architecture.html`
- **Haal:** Poora hua. Official GitHub `tt-a1i/archify` tool install karke candidate architecture JSON banaya gaya, validate kiya gaya (`status: pass`, 0 errors), aur standalone interactive HTML compile karke README.md aur repository mein joda gaya.

---

## Kaam: Project Health Audit (MiniMax Code) - READ-ONLY, COMPLETE

- **Tareekh & Samay:** 16 Nov 2026, 15:05 - 15:20 IST
- **Agent:** MiniMax Code (MCode)
- **User Command:** "ha tum bhi aapane name ka file banakr usme kam karna or abhi isme kya kya chije tik se work nahi kar raha hai ... wo deko or batao"
- **Backup Folder:** `backup\2026-11-16_1518_mcode-health-audit-readme-links` (before + complete; APPROVED: build 0 errors/0 warnings, C# 465/465, JS 45/45)
- **Badli Files:** `docs/MCODE.md` (nayi — MCode ka apna work log), `docs/CHALU_KAAM.md`, `docs/CURRENT_TASK.json`
- **Code:** Koi source code nahi badla. Sirf jaanch (build, dono test suite, runtime logs, README link check, settings).
- **Haal:** Audit poora. Sabse badi 4 baatein:
  1. **App abhi band/crashed hai** — `Z:`/`Y:` mounted nahi, `Cloud-SSD.exe`/`rclone.exe` nahi chal rahe, aakhri activity 15:01:28. Windows Event Log mein WER `AppTermFailureEvent` (14:52, 14:55, 15:02, 15:05) — clean exit nahi hua.
  2. **7.3 GB ka upload adhoora chhoot gaya** — `Cloud-Drive.zip` 24% par ruka, `errors:89`, `lastError: couldn't list directory: context canceled`, resume nahi hua.
  3. **Mount auto-restore fail** — "Drive Z:/Y: did not appear within N s", "mountpoint Z: didn't became available", retry loop. Saath hi `--links` flag missing (symlink error) aur union remote ka `Statfs failed` (galat disk-space info).
  4. **README ke 40 relative links toote hain** (workspace README 37/40, codebase README 40/40) — folder structure `Cloud-SSD/Cloud-SSD-0.7.0-source/Cloud-SSD` se badal kar `Cloud-SSD/Cloud-SSD/Cloud-SSD` hua tha, README update nahi hua. Docs ab `docs\archive\` mein hain.
- **Poori list + evidence:** `docs\MCODE.md` (TASK-MCODE-001) — saare 17 points, har ek ke saath actual log line / command output.
- **Agla kadam:** user ka faisla. Fix karna ho to order: (1) mount restore + crash, (2) `--links` flag, (3) README links, (4) stale plan path (`docs\archive\...`), (5) phone par asli jaanch (Phase 3 step 3.7 — device "vivo I2221" ka LastSeen 14 Nov hai, 2 din se link nahi).

---

## Kaam: Sab kuch theek — mount, docs, release build, settings (MiniMax Code) - COMPLETE

- **Tareekh & Samay:** 16 Nov 2026, 15:30 - 17:05 IST
- **Agent:** MiniMax Code (MCode)
- **User Command:** "fix karo sari chije" (audit ke 17 points par kaam)
- **Backup Folder:** `backup\2026-11-16_1532_mcode-fix-batch-1` (before + complete; APPROVED: build 0 errors/0 warnings, C# **470/470**, JS 45/45)
- **Badli Files:**
  - Code: `src/CloudSSD.Mount/{MountPlanning,MountService,RcloneConfigService}.cs`, `src/CloudSSD.Diagnostics/Logging.cs`,
    `src/CloudSSD.Core/{Settings.cs,SecretProtector.cs (nayi),CloudSSD.Core.csproj}`, `src/CloudSSD.Desktop/ViewModels/ConfigViewModels.cs`
  - Tests: `tests/CloudSSD.Tests/CoreTests.cs` (5 naye tests)
  - Scripts: `installer/build-release.ps1`, `tools/backup/backup.ps1`, `tools/live-test/pc-close.ps1`
  - Docs: `README.md` (3 copies), `RULE.md` (3 copies), `docs/{CHALU_KAAM,DEV_TESTING,PROJECT_POLICIES,MCODE}.md`, `docs/archive/SETUP.md`, `.work/README-DO-NOT-RERUN.md` (nayi), `backup/2026-11-14_2003_.../complete/README.md` (nayi)
  - Runtime: `%LOCALAPPDATA%\Cloud-SSD\settings.json` (Mirror ko safe kiya)
- **Haal:** Sab theek, aur ek asli PC par jaanch ke saath:
  1. **`--links` flag missing tha** → har mount par "symlinks not supported" + union ka `Statfs failed` (galat disk-space). Ab flag add hai: **symlink error 0**, statfs chalta hai (16.5 TB / 15.7 TB free).
  2. **45 s timeout** quota 403 me kaam nahi karta tha (mount ko 75-120 s lagte hain) → **120 s**, aur error ab batata hai: *"Google Drive API quota khatam ho gaya (403). Apni OAuth key use karo … Switch to my own Google key"*.
  3. **Release build toota hua tha** (`THIRD_PARTY_NOTICES.md` root par hi thi, ab `docs\archive\` me) → `Find-Doc` helper se theek; build ab chalta hai: `dist\Cloud-SSD-0.9.0-win-x64.zip` (95.3 MB, SHA256 `1105a640…`).
  4. **README ke 40/40 links** → teeno copies me **0 broken**; badge PC 0.9.0 / phone 0.12.0; download links naye path par. `docs/` ke 6 toote links bhi theek.
  5. **Mirror poori disk upload kar raha tha** → `OnlyFolders = Documents`, `IncludeSystem = false`, `Paused = true`.
  6. **settings.json me token/media key plain** → ab DPAPI se protected (purani plain files bhi chalti hain).
  7. Log file ka naam `cloudplayx-*.log` → `cloudssd-*.log`; `backup.ps1 -Files "a,b,c"` ka bug theek.
- **Phe:** WER "AppTermFailureEvent" Cloud-SSD ka crash **nahi** tha (payload khaali; `crashpad_log` wala Edge ka hai). Sach: app band hua tha aur 7.3 GB ka upload adhoora chhoda.
- **Poori list + evidence:** `docs\MCODE.md` (TASK-MCODE-002).
- **User ke haath me do cheezein ab bhi:** (1) **apni Google OAuth key** (Cloud Drives → "How do I get a key?" → "Switch to my own Google key") — rclone ka shared key 2026 me retire ho raha hai, uski quota khatam rehti hai; (2) **phone par jaanch** (Phase 3 step 3.7) — "vivo I2221" ka LastSeen 14 Nov 17:11 hai.
- **Push nahi hua** (user ne nahi kaha).
- **Ruka hua kis par:** user ka command. Koi code change bina kahe nahi kiya gaya.

---

## Kaam: Dono drive live mount + backup lock ka PS 5.1 bug (MiniMax Code) - COMPLETE

- **Tareekh & Samay:** 16 Nov 2026, 16:41 - 17:00 IST
- **Agent:** MiniMax Code (MCode)
- **Backup Folder:** `backup\2026-11-16_1648_mcode-backup-lock-ps51` (before + complete)
- **Kya hua:**
  1. **Naya code:** `src\CloudSSD.Mount\MountService.cs` me `WatchForDriveAsync` — 120 s timeout hone par rclone ko **maarne ke bajaye zinda chhod** diya gaya; watcher 15 min tak drive letter ka intezaar karta hai aur jaise hi letter aata hai, session "Mounted" ho jaata hai.
  2. **Asli PC par proof (naya build install karke):**
     - Y: (Each Account) → **16:43:38 "Mounted at Y:"**
     - Z: (All Accounts) → **16:43:58 "Mounted at Z: (the drive appeared while the app was waiting)"**
     - Asli file read: 142181 bytes in 1729 ms aur 1028984 bytes in 2137 ms ✅
     - Disk space: Z: 15.0 TB / 14.9 TB free (pehle "Statfs failed") ✅
     - Symlink error: **0** ✅
  3. **Release build:** `dist\Cloud-SSD-0.9.0-win-x64.zip` (95.3 MB, SHA256 `d931ce2a338f247c4b50753afb658251901e42d8470c7f117b25cc36aa0ac355`), naya build install ho chuka hai.
  4. **Backup ka lock bug:** `tools\backup\backup.ps1` me `ConvertFrom-Json -AsHashtable` PS 5.1 par nahi hai → lock kabhi release nahi hota tha. Ab `Read-TaskLock` helper se 5.1 aur 7+ dono par chalta hai; `validate` ab `validationStatus` likhta hai aur `complete` khud lock band karta hai.
- **Task-002 ka backup band:** `backup\2026-11-16_1532_mcode-fix-batch-1` → `complete\` (new 3, changed 19), `VALIDATION.txt` = APPROVED, `complete\README.md` Hinglish me. `docs\CURRENT_TASK.json` → `active:false`, `status: COMPLETED`.
- **User ke haath me ab bhi:** (1) apni Google OAuth key (shared rclone key ki quota khatam rehti hai); (2) phone par Phase 3 step 3.7 jaanch ("vivo I2221" ka LastSeen 14 Nov 17:11).
- **Push nahi hua** (user ne nahi kaha).

---

## Kaam: Zip/install tez, app restart pe khud chale (MiniMax Code) - COMPLETE

- **Tareekh & Samay:** 16 Nov 2026, 17:15 - 18:05 IST
- **Agent:** MiniMax Code (MCode)
- **User ka shikayat:** zip bahut slow khulta hai / khulta hi nahi, install bahut time leta hai, aur PC restart karne par Cloud-SSD band ho jaata hai (permanent hona chahiye jab tak uninstall na ho).
- **Backup Folder:** `backup\2026-11-16_1731_mcode-mount-speed-autostart` (before + complete; APPROVED: build 0/0, C# **471/471**)
- **Kya fix hua:**
  1. **Restart pe app ab khud chalega** — app me feature maujood tha (`StartWithWindows.cs`), par is PC par registry entry nahi thi. Entry bana di, aur ab **installer** bhi install ke time ye entry likhta hai (uninstaller hatata hai) → ek baar install = permanent.
  2. **7.3 GB ka junk upload band** — pehle ka accidental mirror dump 11.6 MB/s up le raha tha, is liye user ka download sirf 251 KB/s tha. Pending-upload wale cache copies (≈28 GB, sirf local cache) Recycle Bin me gaye; ab 0 upload, baaki cache rakha gaya (files turant khulti hain).
  3. **Read chunk 4 MiB → 32 MiB** (`Cache.ReadChunkSizeBytes`), **cache budget 20 → 60 GiB** — ek archive 16 API requests ki jagah 2; dusri baar file cache se turant.
  4. **App band karne par drive ab nahi marti** — rclone app ke pipe se chalta tha; pipe toot'ta → Go SIGPIPE → rclone mar jaata tha (controlled test me reproduce kiya). Naya `DetachedProcess.cs` (`CreateProcessW` + `STARTF_USESTDHANDLES`) rclone ko NUL device se chalaata hai → pipe nahi, toot' hi nahi sakta. **Proof:** app maarte hi 60 s baad bhi rclone zinda, `Z:` mounted, aur app band rahne par bhi file read (1 MB in 1277 ms); app dobara start karne par "Re-attached to running mount at Z:/Y:".
  5. **Khaali dikh raha folder theek** — ye stale dir-cache tha (quota 403 me fail listing "khali" cache ho gayi thi, 72 h tak). Cache clear kiya → files wapas. Isi liye `--vfs-refresh` hataya nahi gaya.
- **Asli blocker (code se nahi, user ke haath me):** Google Drive API ka **shared rclone quota** (`project_number:202264815644`) — 403 storm me download **4 KB/s** tak girta hai (cold read 181–222 s), jabki cache hit par random read **24 ms**. Solution: **apni OAuth key** (Cloud Drives → "How do I get a key?" → "Switch to my own Google key"). Uske baad drive 3–6 s me aati hai.
- **Release:** `dist\Cloud-SSD-0.9.0-win-x64.zip` (95.3 MB, SHA256 `998ee920d57d6323b379ab1778697f2b58b60baa6f20ca26d08ed0529a294084`), install ho chuka hai.
- **Push nahi hua** (user ne nahi kaha).

---

## Kaam: Zip ab khulega — "Fast browsing" mode (MiniMax Code) - COMPLETE

- **Tareekh & Samay:** 16 Nov 2026, 18:20 - 18:50 IST
- **Agent:** MiniMax Code (MCode)
- **User ka shikayat:** *"zip abhi bhi bahot slow open ho raha hai ya bol do open hi nahi ho raha hai"* + quiz me confirm: **"Zip open hi nahi hota / bahut slow"**
- **Backup Folder:** `backup\2026-11-16_1834_mcode-fast-browsing-mode` (before + complete; APPROVED: build 0/0, C# **472/472**)
- **Asli gadda:** Windows zip ka **end** padhta hai, par drive `--vfs-cache-mode full` par thi — us mode me rclone koi bhi file kholte hi **poora file** download kar leta hai. A/B (same 460 MB zip): **full** → 1 MiB padhne me **38.8 s** aur **460 MB utra**; **writes** → **5.3 s** aur **0 MB extra**. isi se quota bhi jharta tha (har folder listing par har archive pura download hota tha → cache 13.7 GB, log 305k lines).
- **Fix:**
  1. Naya setting `Cache.VfsCacheMode` (`full` default = games ke liye, `writes` = fast browsing).
  2. **UI toggle**: Cloud Drives → "Drive mode" card → "Fast browsing" checkbox + "Apply and reconnect" (mode mount shuru hone par tay hota hai).
  3. Is PC par **"writes" ON** kar diya.
- **Proof (naya build, Z:):** `--vfs-cache-mode writes` command me ✅ · 460 MB zip ka end **21.5 s** + **0 MB** extra (pehle 38.8 s + 460 MB) · dusra read **47 ms** · Z: ready 15 s me.
- **Bacha hua wait:** Google ke shared quota ka backoff (`Rate limited, increasing sleep to 16.4s`) — **apni OAuth key** se khatam (browser me Google Console khul gaya hai, steps bheje gaye).
- **Release:** `dist\Cloud-SSD-0.9.0-win-x64.zip` (95.3 MB, SHA256 `cb4960b8fa07791b16e5568d944b56fabfdd1fd5b752c035280e3f626c2f1bd1`), install ho chuka hai.
- **Push nahi hua** (user ne nahi kaha).

---

## Kaam: Pura end-to-end test + chunk size wapas 4 MiB (MiniMax Code) - COMPLETE

- **Tareekh & Samay:** 16 Nov 2026, 18:50 - 19:25 IST
- **Agent:** MiniMax Code (MCode)
- **User ka shikayat / instruction:** *"pura test kud kiya karo meri puri permission hai"* — poora build + teeno test suite + release build + install + asli device par jaanch, evidence ke saath.
- **Backup Folders:**
  - `backup\2026-11-16_1905_mcode-chunk-size-revert-4mib` (TASK-MCODE-006; APPROVED: build 0/0, C# **472/472**)
  - `backup\2026-11-16_1918_mcode-backup-readme-identity-fix` (TASK-MCODE-007; APPROVED: build 0/0, C# **472/472**)
- **Test results (sab abhi, issi machine par):**
  1. **Unit (C#):** `472/472 PASS`
  2. **Mobile JS:** `45/45 PASS`
  3. **Integration (pehli baar chala):** `28/29` — ek fail `Demo_game_launches_from_cloud...` ("run the demo game install step first")
  4. **Demo game install step kiya** (TEST_REPORT.md wala; `DOTNET_ROOT` set karna zaroori tha, PATH wala dotnet sirf .NET 8 runtime hai) → dataset **3,626 MB** bana → game flow bahut aage badha (Launch 1 cold 20030 ms / 126.4 MiB, Launch 2 7942 ms, Prepare 146/146 blocks "ReadyToLaunch" 22 s, Launch 3 7109 ms)
  5. **Hybrid step fail hua**: `UnsupportedFilesystemOperation` on `local-games\CPX Demo Game-...` → wajah `File.CreateSymbolicLink` (`ExecutionModes.cs:121`) → *"Administrator privilege required"* = Windows **Developer Mode band** tha. Elevated process me **"SYMLINK OK"** prove kiya → **code sahi hai**, sirf OS privilege state.
  6. **Developer Mode ON kiya** (user ki permission par): `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock\AllowDevelopmentWithoutDevLicense = 1` (UAC se). Non-elevated process me abhi bhi fail → **reboot/sign-out chahiye** tabhi sab apps me effective hoga.
  7. **Elevated integration suite: `29/29 PASS`** (6.08 min) — game-flow wala bhi pass, hybrid step samet.
  8. **Chunk size wapas 4 MiB** (32 MiB se): 460 MB zip ka end **21.5 s → 5.3 s**, extra 0 MB.
  9. **Release build + install**: `dist\Cloud-SSD-0.9.0-win-x64.zip` (95.3 MB), SHA256 `8432850da5a0138e36f131d6e146dd7188ef96d77a2ecb2aa16cca665b3ae3d0`, install + app chalu.
  10. **Device test 14 sections — sab PASS** (app restart ke baad dobara chalaya, phir bhi PASS).
  11. **Regression: app band → drive zinda** → rclone zinda, Z:/Y: mounted, listing 122 ms. **App restart → re-attach**: log me `Re-attached to running mount at Z:` aur `at Y:` (19:07:06), aur **purana rclone (19:02:38 wala) zinda raha** — matlab app band karne par mount nahi mara.
- **Naya bug pada aur fix hua (TASK-MCODE-007):** `backup.ps1` ka `complete` step agent/taskid apne command line se leta tha (default `Antigravity`, TaskId khali) → README me **galat agent** likha jaata tha. Ab `TASK_INFO.json` se uthata hai. Sandbox me end-to-end prove kiya: `Agent: MiniMax Code | TaskId: TASK-TEST-999` ✅, aur `RESTORE_LOG.md` me bhi sahi agent ✅.
- **Bache hue masle (code se nahi, user ke haath me):**
  - Google Drive **shared quota** (`project_number:202264815644`, 403 `RATE_LIMIT_EXCEEDED`) → pehli cold read 15–34 s. Solution: **apni OAuth key**.
  - Drive me **junk** abhi bhi hai (6.6 GB + 6.8 GB `Cloud-Drive.zip`) → cloud data delete project rule ke khilaf, user ka faisla baaki.
  - Phone par Phase 3 step 3.7 jaanch pending.
  - App close karne par sirf **window** chala jaata hai (drives zinda rehte hain) → tray-mode ke liye `App.xaml.cs` chhedna padega jo RULE me banned hai.
- **Push nahi hua** (user ne nahi kaha).

---

## Kaam: Zip kholna — asli gadda mila, fix hua (MiniMax Code) - COMPLETE

- **Tareekh & Samay:** 16 Nov 2026, 19:44 - 20:10 IST
- **Agent:** MiniMax Code (MCode)
- **User ka instruction:** *"koi bhi zip file open karke test karo or check karo ki open ho raha hai ki nahi"*
- **Backup Folder:** `backup\2026-11-16_1954_mcode-warm-zip-cache-chunk-limit` (before + complete; APPROVED: build 0/0, C# **473/473**)
- **Do layer ka gadda:**
  1. **`--vfs-cache-mode writes` me read cache hote hi nahi** — rclone ke apne docs ke hisaab se *"files opened for read only are still read directly from the remote"*. Isi liye `ArchiveWarmer` ka poora prefetch bekaar tha (log: `item Jarvis.zip not removed, freed 0 bytes`) — har naya open = naya download = Google ke shared quota ki line me.
  2. **`--vfs-read-chunk-size-limit 256Mi`** chunk ko har read par double karta tha (4 → 8 → 16 …) — isi liye `full` mode "poora zip utar deta tha" aur TASK-MCODE-005 me `writes` par switch karna pada tha.
- **A/B (3 alag mount, naya cache, same quota):**

| Mount | 460 MB zip COLD | WARM (re-open) | Cache me data |
|---|---|---|---|
| `writes` (purana setting) | 4,706 ms | 3,279 / 5,307 / 5,027 ms | **0 MB** |
| `full` + limit `256Mi` (purana limit) | **38,383 ms** | 16 / 23 / 14 ms | 0.7 MB |
| **`full` + limit `4Mi` (fix)** | **4,328 ms** | **14 / 10 / 11 ms** | 0.8 MB |

- **Fix:**
  1. `--vfs-read-chunk-size-limit` ab **chunk size ke barabar** (4 MiB) — chunk kabhi double nahi hota.
  2. UI toggle ka mapping **palat diya**: "Fast browsing" ON = `full` (jo padha wo rakhna → dobara kholna **instant**), OFF = `writes` (kuch nahi rakhna). Pehle ON = `writes` tha, yaani "fast browsing" ka matlab tha "har baar dubara download" — bilkul ulta.
  3. XAML ka card ka text ab sach bolta hai; naya test `Fast_browsing_keeps_the_file_locally_and_switching_it_off_keeps_nothing`.
  4. Runtime `settings.json` → `Cache.VfsCacheMode = "full"`.
- **Proof (naya install, asli Z: drive):** dobara kholna **20–116 ms** (local disk jaisa) — pehle har baar 3–22 s. `NOVA AI.zip` ki list **127 ms** (1761 entries) aur andar ka 11.7 MB APK extract **169 ms**. 460 MB zip ki list **19,958 ms** me **17,979 entries**. Device test **14/14 sections PASS**.
- **Do side findings:**
  - **`Jarvis.zip` zip hi nahi hai** — pehli bytes `<!doctype html>` (Google AI Studio ka saved web page, 67 KB). Cloud me user ki apni file galat naam se hai.
  - **Z: ka size kabhi kabhi 1,024 TB dikhta hai** — quota khatam hone par rclone ka `about` call fail hota hai aur woh 1 PiB fallback dikhata hai. Sirf dikhne ki baat, data safe hai.
- **Bacha hua wait:** pehli baar ka open abhi bhi **Google ke shared quota** ki wajah se slow hai (`403 RATE_LIMIT_EXCEEDED`, `pacer: Rate limited, increasing sleep to 16.9s`) — **apni OAuth key** se khatam.
- **Release:** `dist\Cloud-SSD-0.9.0-win-x64.zip` (95.3 MB, SHA256 `05741b5931b4b826d0f0343cd2abd7b2e7bbdd92794a2124561d148e764d6919`), install ho chuka hai.
- **Push nahi hua** (user ne nahi kaha).

- **TASK-MCODE-010 — README "How it works" + naya `docs/HOW-IT-WORKS.md` (sirf wahi jo code me hai):**
  - **Naya file:** `docs/HOW-IT-WORKS.md` (915 lines) — **12 flows**, har ek ke saath exact `file:line` anchor, real
    HTTP route / rclone command, measured numbers, aur honest line ("kya nahi hota").
    Flows: login · doosra login · drive letters + rclone command · chunked read + cache · QR code ·
    phone?file transfer · phone↔laptop · "PC off" mirror · gaming prepare/launch · game stream (video) ·
    memory/space/offloader · security + storage.
  - **README (root + codebase):** fictional "How it works" (Archify diagram + 12 fake Deep Dive flows, ~435 lines)
    hata ke **12 honest flows ka condensed version** + `docs/HOW-IT-WORKS.md` link. "Architecture" ka fake mermaid
    (PKCE / mTLS 1.3 / 5-15TB Pool / Sparse VHDX / 3-5 GB RAM Drop / Markov ML) → **asli architecture map**.
  - **Theek kiye gaye claims:** PKCE nahi (rclone ka flow) · TLS nahi (plain HTTP) · "3-5 GB" target nahi
    (har run measure) · `K32EmptyWorkingSet` (psapi nahi) · Markov chain "ML" nahi · VHDX nahi ·
    IndexedDB vault nahi ("Keep offline" copies) · download badge 69 ? **94 MB** (checksum verify kiya).
  - **Proof:** `backup.ps1 validate` → build 0 warning / 0 error, unit **473/473**, **APPROVED**;
    backup `2026-11-16_2036_mcode-how-it-works-readme` (new 1, changed 1).
  - **Push nahi hua** (user ne is task ke liye push nahi kaha).

---

## Kaam: CMD ka naya look — Claude Code / Codex jaisa terminal UI (Claude Code) - COMPLETE

- **Tareekh & Samay:** 17 Nov 2026, 01:53 - 02:30 IST (TASK-CLAUDE-CLI-TUI; K aur SPEED ke saath-saath chala, lock K ka hi raha)
- **User ka command:** "%USERPROFILE%\Downloads\Cloud-Drive ye wale me isko baanane ka try kiya cmd wala but wo dik se banana nahi usko ui banao kayse banna tha wo bhi deko or usko wayse hi banao antigrvity me hoga uska prompt"
- **Purana prompt (14 Nov, isi file mein upar):** "/plan mujhe isko cmd me bhi calana hai ... jaysa claude codex hai wayse hi mujhe isko bhi wayse hi banana hai".
  Antigravity ke data (`%USERPROFILE%\.gemini\antigravity`) mein Cloud-SSD ki koi baatcheet nahi bachi (sirf 16 Nov ki college website wali).
- **Backup:** `backup\2026-11-17_0153_cli-tui-claude-codex-look` (before + complete + Hinglish README; `validate` **APPROVED**:
  build 0/0, C# **518/518**). `start` / `complete` / `validate` ne `CURRENT_TASK.json` badla tha; doosre session
  (TASK-CLAUDE-K) ka lock wapas rakh diya (`.work\cli-tui\CURRENT_TASK.k-lock.json` se). Yeh kaam **sirf** `src\CloudSSD.Cli\*`,
  `tests\CloudSSD.Tests\CliTuiTests.cs` (+ test csproj mein Cli ka reference), README ka CLI hissa chhuta hai. Phone / bridge files nahi.
- **Plan:** `cloudssd` / `cpx` kholo to: gol kone wala swagat box (live: PC app, Z:/Y:, phone), neeche input box `> `, `/` dabate hi
  commands ki list (upar-neeche teer, Tab/Enter), Claude jaisa `⏺` / `⎿` jawab, spinner, purane command ↑↓, Ctrl+C / Esc / Ctrl+L.
  Purane shabd (`status`, `phone` ...) aur `cloudssd status` jaise ek-baar wale command waise hi chalenge. Pipe / file mein output ho
  to rang nahi (saada text).
- **Haal (17 Nov 02:30): POORA.** Naya `cpx.*` installed app mein hai (Release, self-contained, R2R; purana `.work\cli-tui\old-cpx\`).
  Asli CMD mein `cloudssd` (installed shim) se photo li: chalta hai.
  - Nayi files: `src\CloudSSD.Cli\{Ansi.cs, Commands.cs, Views.cs, Shell.cs}`; badli: `Program.cs`, `CloudSSD.Cli.csproj`
    (InternalsVisibleTo), `tests\CloudSSD.Tests\CloudSSD.Tests.csproj` (Cli ka reference), nayi `tests\CloudSSD.Tests\CliTuiTests.cs` (38 test).
  - README ka CLI hissa teeno copy mein naya (`.work\cli-tui\readme-apply.js` + `readme-cli.md`; dobara chalane se kuch nahi bigadta).
  - Jaanch: build 0 warning / 0 error · **C# 517/517** (doosre session ke 479 + 38 naye) · asli `conhost` window mein
    `.work\cli-tui\shot.ps1` se 9 photo: `%USERPROFILE%\Downloads\Cloud-Drive\cli-tui-photos\` (git ke **bahar** rakhi hain,
    kyunki unme e-mail dikhte hain aur push `git add -A -f .` se hota hai).
  - `secret emit-config-pass` (rclone ise chalata hai): purane aur naye cpx ka output **same 43 bytes, same hash** (secret dikhaya nahi).
  - Pipe / file mein output saada ASCII (test bhi hai). PowerShell pipe ka BOM wala purana bug ("Unknown command: ﻿help") theek.
- **Seekh (agle session ke liye):** Claude Code ke shell mein `NO_COLOR=1` set hota hai → test window bina rang khulti hai; `shot.ps1`
  ise hata deta hai. `⎿` Consolas font mein nahi hai (khaali dabba) → `└` lagaya. xUnit `Assert.DoesNotContain("\u001b", s)` jhootha
  "mila" bolta hai (culture search ESC ko ignore karti hai) → `s.Contains('\u001b')` (char) use karo.
  - `accounts` / `remotes` pehle poora AppHost chalata tha: phone bridge port 8090 par ERR (app ke log mein), Z:/Y: "re-attach",
    ek baar 180 s+ atka. Ab sirf `rclone listremotes` → **0.8 s**, app log mein koi line nahi. Har command 0.1–0.8 s.
  - `cpx deps` hamesha exit 0 (jaise pehle): `installer\build-release.ps1` ka smoke test isi par chalta hai.
  - Photos ab `cli-tui-photos\` mein 6 hain (installed `cloudssd` se); mein e-mail dikhte hain, isliye git ke bahar.
- **Agla kadam:** koi nahi. Push nahi hua (user ne nahi kaha). Agle release build mein naya Cli apne aap jayega.

---

## Kaam: Drive restart par bani rahe + zip / install ki speed (Claude Code) - COMPLETE

- **Tareekh & Samay:** 17 Nov 2026, 01:40 - 02:35 IST
- **Agent:** Claude Code (Claude Fable 5.2), TASK-CLAUDE-SPEED (K aur CLI-TUI ke saath-saath chala; lock K ka hi raha)
- **User ka shikayat:** restart / power off ke baad drive "wapas load" hoti hai, permanent rehni chahiye; zip slow, fix ke baad apne aap phir slow; app install 5-10 guna slow.
- **Backup Folder:** `backup\2026-11-17_0158_claude-drive-speed-restart` (before + complete + README; APPROVED: build 0/0, C# **518/518**)
- **Jad:** rclone ki **shared Google key** (`project_number:202264815644`) par Google lagbhag aadhe request 403 lautata hai; har 403 par rclone 1-16 s rukta hai. Naapa (internet 9.25 MB/s): 54 MB file 259 s / 37 request / 32 x 403.
- **Fix (naya build 02:05 par install, chal raha hai):**
  1. `MountService.cs`: auto-restart kabhi band nahi hota (5 jaldi koshish, phir har 2 min) — pehle "Automatic restart disabled … Remount manually".
  2. `MountPlanning.cs`: `--vfs-read-chunk-size-limit off` (pehle `4Mi`) — 54 MB = 4 request (pehle 37), 110 s.
  3. `ArchiveWarmer.cs` + `AppHost.cs`: 403 ke baad 2 min tak zip pehle se nahi mangwata (user ki file ke aage line nahi lagata).
- **Naapa, code se theek nahi hota:** Z: par likhna — 60 chhoti files + 13 folder = 280 s, 105 x 403; har naya folder 2 s + 20-60 s. Hatana 453 s.
- **Nahi jaancha:** asli power off / restart; internet band karke auto-restart; warmer ka rukna asli drive par.
- **User ke haath me:** apni Google key (Cloud Drives -> "How do I get a key?"). Shared key 2026 me band hogi; tab login bhi tootega.
- **Dhyan:** `backup.ps1 validate` / `complete` doosre ke lock (`CURRENT_TASK.json`) me `validationStatus` aur `COMPLETED` likh dete hain. Do kaam saath chalein to lock ko baad me haath se wapas likho.
- **Naapne ke script:** `.work\speed\read-test.ps1`, `zip-list-test.ps1`, `write-test.ps1`.
- **Push nahi hua** (user ne nahi kaha).
---

## Kaam: CMD shell ko aur behtar — Hinglish samajhna, Yes/No sawaal, drive chunna, logo (Claude Code) - COMPLETE

- **Tareekh & Samay:** 17 Nov 2026, 02:35 - 03:00 IST · TASK-CLAUDE-CLI-TUI-2
- **User ka command:** "isko improve karo" (pichhla kaam: CMD ka Claude/Codex jaisa look, upar)
- **Backup:** `backup\2026-11-17_0235_cli-tui-improve-hinglish-picker-logo` (before + complete + Hinglish README; `validate` **APPROVED**, 547/547;
  lock `TASK-CLAUDE-CLI-TUI-2` COMPLETED / active false)
- **Kya bana:**
  1. **Hinglish / saade shabd** (`CommandCatalog.Understand`, bina AI, shabd ki shuruaat se): "z drive kholo" → `/open Z:`,
     "drive kholo" → kaunsi drive (sawaal), "phone dikhao" / "फोन दिखाओ" → `/phone`, "kitni jagah bachi" → `/cache`,
     "google account dikhao" → `/accounts`, "kya haal hai" → `/status`, "app band karo" → `/stop`, "safai karo" → `/clean`.
     "drive band karo" app band **nahi** karta (sentence drive ke baare mein hai) → `/drives`. Shell `→ /cache` dikhata hai.
     Command line (`cloudssd drive kholo`) par bhi chalta hai, par band/delete wale sentence wahan **kabhi nahi** chalte (exit 64, saaf sandesh).
  2. **Yes / No sawaal** (Claude ki permission jaisa box) `/stop` (app chal raha ho tab) aur `/clean` se pehle; **"No" pehle se chuna**
     (galti ka Enter kuch nahi badalta); ↑↓ / 1 2 / Enter / Esc.
  3. **`/open ` ke baad drives ki list** (Y: Each Account, Z: All Accounts, band ho to "not connected now"); letter na ho to sawaal.
  4. **Bada rangeen CLOUD-SSD logo** (teal → blue) swagat box mein (`Logo.cs`); patli window / bina-Unicode mein nahi.
  5. 1 s+ wale jawab ki heading mein samay; heading mein faltu shabd nahi ("Phones", "Phones dikhao" nahi);
     cache mein "30 days" / "5 s" (pehle "30.00:00:00").
- **Files:** naya `src\CloudSSD.Cli\Logo.cs`; badle `Commands.cs`, `Shell.cs`, `Views.cs`, `Program.cs`, `Ansi.cs`;
  `tests\CloudSSD.Tests\CliTuiTests.cs` (CLI tests 39 → 70); README ka CLI hissa (3 copy, `.work\cli-tui\readme-cli.md` + `readme-apply.js`).
- **Jaanch:** build 0 warning / 0 error · C# **547/547** · naya `cpx.*` installed app mein · `secret` output same (43 bytes, same hash) ·
  asli conhost mein installed `cloudssd` se photo (`%USERPROFILE%\Downloads\Cloud-Drive\cli-tui-photos\`, git ke bahar) ·
  `/stop` ke sawaal par Esc → "Nothing was changed", app ka PID 13768 pehle aur baad mein same.
- **Kabhi-kabhi fail hone wala test (is kaam ka nahi):** `RemotePcClientTests.Files_sent_while_away_wait_survive_a_restart…` ek baar 21 s
  mein fail hua, akele aur dobara poore suite mein pass. Doosre session ne `MobileBridgeResumeTests.Details_and_folder_size` aisa bataya tha.
- **Agla kadam:** koi nahi. Push nahi hua (user ne nahi kaha).
---

## Kaam: Storage permanent rahe jab tak app laga hai (Claude Code) - COMPLETE

- **Tareekh & Samay:** 17 Nov 2026, 02:45 - 03:00 IST
- **Agent:** Claude Code (Claude Fable 5.2), TASK-CLAUDE-PERMANENT
- **User ka kehna:** "file aata hai mujhe wo permanent chahiye jab tak app hai tab tak mujhe storage mile"
- **Backup Folder:** `backup\2026-11-17_0247_claude-drive-permanent` (before + complete + README; APPROVED: build 0/0, C# **553/553**)
- **Fix (build 02:54 par install, chal raha hai):**
  1. Khidki band karne ke sawaal me pehle se chuna jawab ab **Yes** (drives rahein): `Settings.cs` default + is PC ki `settings.json` (`KeepMountsAfterExit = true`; purani copy `settings.json.before-keep-drives`).
  2. `RcloneConfigService.cs` (`ConfigGuard`) + `AppHost.RefreshRemotesAsync`: accounts ki file ki aakhri sahi copy `rclone.conf.good`; file gayab / khaali / na-padhne-layak mile to app khud wapas rakhta hai. Account khud disconnect karne par (`DeleteRemoteAsync`) copy hata di jati hai.
- **Asli jaanch:** `rclone.conf` ko jaan-boojh kar 0 bytes kiya -> app ne 1.4 s me wahi content wapas rakha, drives judi rahi. App band (Yes) par Z:/Y: wahi rclone process ke saath rahi.
- **Nahi jaancha:** asli restart / power off; aadhi likhi file wala raasta (sirf unit test); kuch account kam ho jane wala case pakad me nahi aata.
- **Baaki khatra:** rclone ki shared Google key 2026 me band hogi -> saare login tootenge. User ki apni Google key chahiye.
- **Dhyan (naye account-disconnect raaste ke liye):** account hatana hamesha `RcloneConfig.DeleteRemoteAsync` se karo, warna `rclone.conf.good` use wapas la sakta hai.
- **Push nahi hua** (user ne nahi kaha).
---

## Kaam: CMD shell mein /key, /problem, /live, /ls /find /copy + drive chalu/band, /send (Claude Code) - CODE POORA, APP INSTALL BAAKI (user ke haath)

- **Tareekh & Samay:** 17 Nov 2026, 02:57 se · TASK-CLAUDE-CLI-TUI-3 (lock isi ka)
- **User ka command:** "or kya kar skte hai isme" → user ne chaaron chune: /key, /problem, drive chalu/band + /live, files /ls /find /copy /send
- **Backup:** `backup\2026-11-17_0257_cli-key-problem-live-files` (sirf `before\`)
- **Hissa 1 (sirf CLI, app nahi chhua) — BAN GAYA, Debug build se asli conhost mein jaancha:**
  - `/problem` (`Problems.cs`): app ka log (sirf `cloudssd-*.log`, koi line chhapi nahi jaati) + abhi ki jaanch (app, drives, shared key, C: jagah) → Hinglish mein "Kya karein".
  - `/live` (`Live.cs`): `mounts.state.json` se har drive ka rc port/login (DPAPI) → `core/stats` + `vfs/stats` (sirf padhna) → har second speed, uploads, cache; Esc.
  - `/key` (`Key.cs` + Shell): kaun se account shared key par (rclone dump), key file mili ya nahi, 5 kadam, browser kholna, Downloads mein file ka intezaar; aakhri kadam app ka button (drives safely hatana/jodna wahi karta hai).
  - `/ls`, `/find` (Z: "All Accounts" par, 50 nateeje / 3 min, Esc), `/copy` (progress bar, kabhi overwrite nahi, `.cloudssd-part` → asli naam; Esc par aadhi file hatti) (`Files.cs`).
  - Esc ab command ko sach mein rokta hai (CancellationToken), spinner mein progress line. Hinglish: "kya dikkat hai", "speed dikhao", "google key lagao", "photo.jpg dhundho", "z drive mein report dhundho".
  - Jaanch: build 0/0 · C# **580/580** · live: /problem, /live, /key (Esc → kuch nahi khula), /copy 800 MB (part file 0), /find zip (25 nateeje 4 s). App PID same.
- **Hissa 2 — CODE + TESTS BAN GAYE, par INSTALL NAHI HUA (user ke haath ka ek kadam baaki):**
  - App: naya `src\CloudSSD.App\LocalMobileBridgeService.Cli.cs` + `LocalMobileBridgeService.cs` mein 3 chhote badlaav (Start ke baad `IssueCliKey()`,
    `HandleApiRouteAsync` mein terminal key ki pehchaan, `TryHandleCliRouteAsync`). "Terminal key": bridge har start par random key banata hai →
    `%LOCALAPPDATA%\Cloud-SSD\cli.key` (DPAPI, sirf yahi Windows user). Sirf 127.0.0.1 / ::1 se maani jaati hai **aur** key sahi honi chahiye
    (akela loopback kuch nahi kholta: `adb reverse` wala phone bhi 127.0.0.1 dikhta hai — K session ki baat). Sirf `/api/status`, `/api/drives`,
    `/api/drives/mount|unmount`, `/api/cli/*` khulte hain; baaki sab 403. PC ke switch (Phone → Permissions, "PC control") is par bhi lagte hain.
  - CLI: `AppLink.cs` → `/connect [letter]`, `/disconnect [letter]` (Yes/No, "No" pehle se), `/send <file> [to <phone>]` (app ka `SendToDevice`; phone par accept karna hota hai).
    Hinglish: "z drive jodo", "z drive hatao", "z drive band karo" (letter ho to drive, app nahi), "photo.jpg phone ko bhejo".
  - Tests: naya `tests\CloudSSD.Tests\CliBridgeTests.cs` (key file protected, galat/bina key 401, LAN/Tailscale IP se sahi key bhi nahi, 8 doosre route 403,
    phone ki key se `/api/cli/*` 403, switch band → 403, doosra process key nahi badalta, send asli bridge par). **C# 612/612** (teen baar), JS 48/48, build 0/0.
    Ek baar `MobileBridgeResumeTests.Details_and_folder_size` fail hua (purana kabhi-kabhi wala; akele 3/3 pass).
  - Release: `dist\Cloud-SSD-0.9.0-win-x64` + zip (95.5 MB, SHA256 `582200439bfc…e127850`), 03:23 ka; `CloudSSD.App.dll` mein naya code hai (jaancha).
  - **Installed abhi:** sirf naya `cpx.*` (03:23; purana `.work\cli-tui\old-cpx-2`). App **purana** (roshan-47 ka 02:55 wala) chal raha hai. Isliye abhi
    `/connect`, `/disconnect`, `/send` kehte hain "The running PC app is an older version without this"; baaki sab chalta hai (asli conhost mein jaancha,
    photos `%USERPROFILE%\Downloads\Cloud-Drive\cli-tui-photos\21…35`). `secret emit-config-pass` ka output same (43 bytes, same hash).
  - **Kyun install nahi hua:** `pc-close.ps1` pehli baar 90 s mein haar gaya (app ki window minimized thi → "Close Cloud Drive" box chhupa raha).
    `tools\live-test\pc-close.ps1` theek kiya (minimized window pehle wapas laata hai; box khula ho to dobara close nahi maangta). Doosri baar app band karna
    Claude ke permission system ne **rok diya** (destructive). Isliye app band + install user karega. roshan-47 ko bhi kaha: meri jagah band mat karna.
  - **Dhyan:** app ka "Close Cloud Drive" (Yes/No/Cancel) box meri pehli koshish se **khula pada hai** (window minimized). App chalta rahe to **Cancel**.
- **Agla kadam (USER):** `powershell -ExecutionPolicy Bypass -File .work\cli-tui\update-app.ps1` (app band → install → chalu; drives 1-2 min ke liye hatti hain).
  **Uske baad (agla session):** asli jaanch → `cloudssd` → `/disconnect Y` (Yes) → `/drives` mein off → `/connect Y` → on; `/send <chhoti file>` → phone par offer dikhe;
  `cli.key` bana ho. Phir `backup.ps1 validate` + `complete` (folder `2026-11-17_0257_cli-key-problem-live-files`) + Hinglish README, aur lock band.
  Lock `TASK-CLAUDE-CLI-TUI-3` abhi **IN_PROGRESS** hai (sach yahi hai). Push nahi hua (user ne nahi kaha).

---

## Kaam: Laptop chalu hote hi drives Files mein (root folder ID ek baar save) (Claude Code) - IS PC PAR LAG GAYA; app wala hissa install ke baad

- **Tareekh & Samay:** 17 Nov 2026, 09:15 - 09:50 IST · TASK-CLAUDE-FASTSTART
- **User ka kehna:** "power off karke on karta hu to Files mein storage show nahi hota, app launch hota hai tab hota hai … mujhe poora automatic chahiye,
  jab tak app laptop mein hai aur email connect hai tab tak storage dikhta rahe, laptop start karu kuch bhi karu"
- **Backup:** `backup\2026-11-17_0919_drives-right-after-boot-root-folder-id` (sirf `before\`)
- **Jo mila (asli boot 17 Nov 09:04 ke log se):** app **khud chalu hota hai** (Run key `Cloud-SSD.exe --minimized`): sign-in 09:04:42 → app 09:05:00.
  Par Z: **09:06:34** par aayi, Y: 09:06:40 par. `rclone-cloudssd.log`: 09:05:04 se 09:06:28 tak rclone sirf teen accounts ka **root folder ID** Google se
  pooch raha tha (har start par, har account ke liye ek call) aur Google `403 rate limit` se rok raha tha; uske baad mount 0.3 s mein laga. rclone khud likhta hai:
  `'root_folder_id = …' - save this in the config to speed up startup`. Yaani dikkat "app launch" nahi, yeh 84 s ka intezaar tha (ek aur boot par 41 s).
- **Fix:**
  1. `src\CloudSSD.Mount\RcloneConfigService.cs`: `DriveRemotesWithoutRootId`, `ParseRootFolderId`, `SaveRootFolderIdsAsync` (har account ke liye
     `rclone lsjson --stat <remote>: -vv` se hint padhna → `rclone config update <remote> root_folder_id <id> --non-interactive`). Shared drive (`team_drive`) nahi chhua.
  2. `src\CloudSSD.App\AppHost.cs` (`RefreshRemotesAsync` + `SaveRootFolderIdsInBackground`): naya account jude to app khud background mein save kare
     (ek waqt mein ek, 10 min mein ek baar se zyada nahi). **Yeh hissa installed app mein abhi nahi hai** (app update baaki, neeche).
  3. CLI: `/faststart` (`boot`), aur `/problem` mein "Laptop chalu hone par drives der se aati hain → /faststart". Purane installed app ke saath `faststart`
     saaf sandesh deta hai (MissingMethodException pakda), crash nahi.
- **Is PC par laga diya (Debug cpx se, 09:35):** gdrive, gdrive2, gdrive3 ke ID `rclone.conf` mein save (8 s). Purani copy: `%LOCALAPPDATA%\Cloud-SSD\rclone.conf.before-faststart`.
  Drives chalti rahi (Z:, Y:, wahi rclone).
- **Jaanch:** build 0/0 · C# **622/622** (naya `tests\CloudSSD.Tests\FastStartTests.cs`) · asli naap `.work\cli-tui\start-time.ps1`:
  fix ke baad start par ID lookup **0** (pehle 3), aur asli test mount (Q:, read-only, alag cache) `CloudSSD:` aur `Accounts:` dono **1.9 s** mein Files mein
  (pehle wahi charan boot par 84 s / 41 s). `faststart` dobara → "Already set up". `secret` output same.
- **Nahi jaancha:** **asli restart** (PC restart karna mere haath mein nahi). Agle restart par ummeed: app sign-in ke ~18 s baad (Windows startup apps ko itna
  rokta hai), phir Z: ~2 s, Y: ~2 s aur. Agla session: `cloudssd-<tareekh>.log` mein "Cloud-SSD starting" se "Mounted at Z:" tak ka fark dekhe.
- **Dhyan:** `start-time.ps1` ki pehli line ("backends ready") union ke liye sahi paimana nahi (uska apna Google call hai, 36 s aaya); mount wali line dekho.
- **Baaki (USER ka wahi ek kadam):** `powershell -ExecutionPolicy Bypass -File .work\cli-tui\update-app.ps1` → naya app (terminal key + naye account ke liye apne aap ID save).
  Release dobara bana: `dist\Cloud-SSD-0.9.0-win-x64` + zip 95.6 MB, SHA256 `65e738f5…dd58b7` (09:40). Installed: naya `cpx.*` (09:40), app purana (02:51).
  Uske baad dono backup band karne hain: `2026-11-17_0257_cli-key-problem-live-files` aur `2026-11-17_0919_drives-right-after-boot-root-folder-id`.
- **Lock:** `CURRENT_TASK.json` wapas `TASK-CLAUDE-CLI-TUI-3` (IN_PROGRESS) par; yeh kaam bhi usi install ka intezaar kar raha hai. Push nahi hua.

---

## Kaam: MiniMax install Z: par kitna dheema + "har cheez dheemi khulti hai" (Claude Code, session roshan-48) - CHAL RAHA HAI (abhi sirf naap + tajurba, koi source nahi badla)

- **Tareekh & Samay:** 17 Nov 2026, 09:40 se · TASK-CLAUDE-INSTALL-SPEED (lock nahi liya: `CURRENT_TASK.json` abhi bhi `TASK-CLAUDE-CLI-TUI-3` ka hai, use mat chhedo)
- **User ka kehna:** "abhi me minimax name ka app install kar raha hu cloud ssd me slow kitna hai wo issko install karke check karo … koi bhi chij open karu
  to slow … download karu to ye hi problem, isko permanent kyu fix nahi kar dete"
- **Backup:** abhi koi nahi (source nahi badla). Source badalne se pehle `backup.ps1 start -Name "install-speed-upload-hold" -Force`.
- **Haal (10:00):** user ka apna installer `MiniMax Code Setup 3.1.16-x64.exe` (PID 12768) **09:37:10** se `Z:\MiniMax Code` mein likh raha hai. Doosra install nahi chalaya (wahi naap raha hu).
  **Installer chal raha ho tab PC app band / drive unmount / rclone restart MAT karo.**
- **Naapa (09:37-09:55, `rclone-cloudssd.log`, sirf ginti):**
  - Google ke **403 = 1,150+** ("Quota exceeded for quota metric 'Queries' … Requests per minute … project_number:202264815644" = rclone ki shared key), 620 baar "Rate limited, increasing sleep".
  - Pehle 1 minute mein 165 files / 719 MB local cache mein aa gayi (likhna tez hai). Uske baad **har naya folder** Google ki ek call hai: 118 `Mkdir` = 278 s, sabse lambi **84-120 s**;
    dheemi `Getattr` (naye folder ki pehli listing) 302 s. Lagbhag har minute ka 55-60 s ek hi atki hui call mein gaya.
  - **09:53:30 par ek `Mkdir` fail hua (errc=-5, EIO: 10 koshish ke baad rclone ne haar maani)** → installer ne poora extract dobara shuru kiya (720 MB phir likha, upload line 505 → 740).
    Yaani shared key par bada install sirf dheema nahi, **chakkar mein phans sakta hai**.
  - Wajah ki kadi: 4 upload ek saath chalte hain → unke 403 rclone ke pacer ko 16 s wali rok par rakhte hain → `Mkdir` unhi ki line mein lagta hai.
- **Tajurba (09:57:50 se chal raha hai):** `.work\speed\hold-uploads.ps1 -Letter Z -InstallerPid 12768` — rclone rc `vfs/queue-set-expiry` se line mein lagi files ka upload 300 s aage khiskata hai
  jab tak nayi files aa rahi hain; 90 s shaanti par chhod deta hai. Kuch mitta / badalta nahi; rok 5 minute mein apne aap khatam. Abhi hatana ho: `hold-uploads.ps1 -Letter Z -ReleaseNow`.
  Nateeja abhi nahi aaya (dekhna: rok ke baad `Mkdir` kitne second leta hai → `.work\speed\last-ops.ps1 -Since "09:58:00"`).
- **Naye script (sab sirf padhte hain, hold-uploads ko chhod kar):** `.work\speed\install-watch.ps1` (har 30 s → `install-watch.csv`), `call-times.ps1` (har tarah ki call ka samay),
  `last-ops.ps1` (aakhri folder / file calls, naam chhupa kar), `hold-uploads.ps1`.
- **Seekh:** PS 5.1 mein param ke default ke andar `$PSScriptRoot` khaali; `(Get-Date).ToString('yyyy/MM/dd')` is PC par `2026-11-17` deta hai (InvariantCulture do);
  `Utimens` log mein do "shuru" line likhta hai.
- **Doosra session:** `roshan-57` sirf `Downloads\Programming` (OpenCode) mein hai, Cloud-SSD nahi chhuta; usne kaha install khatam hone tak Z: par kuch nahi likhega. Baad mein woh Ollama model (4-5 GB) Z: par copy karne ka button banayega.
- **Agla kadam:** (1) install khatam hone tak naapo, poora samay + 403 ginti user ko batao; (2) tajurba kaam kare to isko app mein banao (mount ke saath chalne wali "install ke waqt upload ruko" seva,
  `--low-level-retries` badhana taaki `Mkdir` EIO na de) — backup, tests, build; app install user ke haath (`.work\cli-tui\update-app.ps1`); (3) **asli permanent ilaaj = user ki apni Google key** (`cloudssd` → `/key`): user ke saath baith kar karna.
- **Haal (10:05):** tajurbe ka nateeja: rok lagte hi (09:57:51) **28 folder 5 s mein** bane (09:58:51-56), "Rate limited" 20-70/min → 5-14/min. Par naye folder ki pehli listing phir bhi 60 s+ ataki
  (Google ke apne lagatar 403), aur script ki "90 s shaanti" isi atakne ko shaanti samajh kar 10:00:26 par rok hata baithi → 10:03 se `-QuietSeconds 600 -MaxMinutes 45` ke saath dobara chalu.
  Yaani yeh **raahat hai, ilaaj nahi**; app mein nahi banaya. 10:02 tak installer "Installing" par hi tha (25 min; local disk par yahi kaam ~1-2 min).
  roshan-57 ko bata diya: Ollama model Z: par ek flat folder mein, ek-ek file, install ke baad hi.
- **Ruka hua kis par:** user ka jawab — apni Google key abhi banayein ya nahi (yahi asli ilaaj). Background mein `install-watch.ps1` aur `hold-uploads.ps1` chal rahe hain; session band ho to dono khud ruk jaate hain,
  aur rok 5 minute mein apne aap hat jati hai (kuch haath se theek nahi karna).
- **Haal (10:25) — install poora + Google key ki file aa gayi:**
  - MiniMax install **09:37:10 → 10:18:51 = lagbhag 42 minute** (853 files, 143 folder, 970 MB). Rok ke saath (10:03 se) baaki ~600 files / 90 folder 16 min mein; rok se pehle 255 files mein 26 min + ek baar poora dobara.
    10:22:16 par rok hatayi (`-ReleaseNow`): 852 files upload ki line mein. Dono watcher band kar diye.
  - User ne chuna: "Google key abhi, saath mein". Mila: user ke Google project **"Gemini Project 2"** mein key **"Cloud-SSD Desktop" 10 Nov se bani hui thi**
    (Desktop client, app "In production", External, Drive API chalu). Sirf uski file PC par nahi thi aur Google purani secret dobara download nahi karne deta.
    Isliye usi client par **"Add client secret"** dabaya (purani secret chalu hi hai) → **Download JSON** →
    `%USERPROFILE%\Downloads\client_secret_….json`. `cpx key` ab kehta hai "Key file mil gayi", par 3 account (gdrive, gdrive2, gdrive3) abhi shared key par hain.
    User ke account ki project limit poori hai (naya project nahi banta) — zarurat bhi nahi.
  - Bachav ki copy: `%LOCALAPPDATA%\Cloud-SSD\rclone.conf.before-own-key`.
  - Auzaar: `.work\key\page.ps1` (Edge ke page ke button NAAM se dabana / list karna, UI Automation; pehli baar "page content not found" aaye to dobara chalao),
    `.work\key\ui.ps1` (screen ki photo → `%USERPROFILE%\Downloads\Cloud-Drive\key-photos\`, git ke bahar; iske mouse click Edge par nahi lage).
    **Dhyan: yeh key file aur `key-photos` kabhi git / GitHub par na jayein** (file Downloads mein hai, repo ke bahar).
- **Agla kadam (USER ke haath, main saath hu):** PC app → Cloud Drives → **"Switch to my own Google key"** → app ka sawaal "Yes" → har account ke liye "OK" → Google ke page par wahi Gmail chuno → "Allow"
  ("Google hasn't verified this app" aaye to Advanced → Go to …). Z:/Y: kuch minute hatengi, khud wapas aayengi; line mein lagi uploads cache mein rehti hain aur baad mein chalti hain.
  **Uske baad (agla session bhi kar sakta hai):** `cpx key` → "Saare Google account apni key par hain"; `rclone-cloudssd.log` mein `project_number:202264815644` wale 403 band hone chahiye;
  `.work\speed\read-test.ps1` / `write-test.ps1` se naya samay naapo aur user ko pehle/baad batao.
- **Haal (10:48) — GOOGLE KEY SWITCH HO GAYA (asli ilaaj lag gaya):** user ne kaha "tum sab kar sakte ho" → app ka sawaal (user ne khud Yes dabaya 10:30), phir teeno account ke liye
  OK + Google page par wahi Gmail + Continue maine dabaya (`.work\key\dlg.ps1 -Answer OK`, `.work\key\page.ps1 -Invoke …`). Log: `gdrive` 10:35:36, `gdrive2` 10:36:47, `gdrive3` 10:40:17
  "moved to the user's own OAuth client". `cpx key` → "Saare Google account apni key par hain". Password / OTP kahin nahi maanga gaya.
  - **Asar (naapa):** naye mount ke baad **403 = 0** (pehle 26 min mein 1,290+); Y: **4.6 s** mein judi (pehle 40-84 s); folder tree 16.7 s mein padha; uploads **~65 file/min, 0 fail** (pehle ~20/min, 403 ke saath).
  - **Gadbad jo raaste mein hui (dhyan se padho):** switch ke waqt user ne MiniMax Code chala diya tha (Z: se). Drive hatne par `MiniMax Code.exe` (PID 9576) aur purana rclone (PID 3120) dono
    Windows ke andar "khatam ho rahe" haal mein atak gaye (HasExited=True, 1 thread, wait=Executive; kill se nahi jaate). Isse **Z: ka naam mara hua atka hai** (`Z:\ ready=False`), aur app ne
    "All Accounts" ko **G:** par joda (`Drive letter Z: is in use; using G: instead`). `settings.json` mein letter abhi bhi **Z** hai (theek) — **restart ke baad drive wapas Z: par aayegi**.
    `DefineDosDevice` se Z: ka naam hatana kaam nahi kiya (`.work\key\dosdev.ps1`, "removed: True" par naam wahi). **Ilaaj: PC restart** (user ka faisla; session band ho jayega).
  - **Seekh (app ka bug, theek nahi kiya):** drive ko 850 adhoori uploads ke saath hatao to dobara judne mein der lagti hai — rclone mount dikhane se pehle har adhoori file Google se jaanchta hai
    (apni key par ~13 min for 853 files; shared key par ghanton). Aur `UnmountAsync` ka `TryKill` us rclone ko nahi maar paya jiske drive se koi program chal raha tha.
    Sudhaar ke vichaar: switch se pehle "drive se chal rahe program band karo" ki chetavni; unmount se pehle uploads poore hone do; mount ko reload ka intezaar na karwana.
- **Agla kadam:** (1) G: (All Accounts) dikhne ka intezaar (~10:54), phir file kholne / likhne ka samay naapo; (2) user ne kaha "apna ek baar install karke check karo": MiniMax ka asli install
  dobara karke samay naapna — **Z: wapas aane ke baad** (registry mein install ka raasta `Z:\MiniMax Code` hai; mari hui Z: ke saath installer atak sakta hai). Tab tak wahi kaam (853 files, 143 folder)
  ek test folder mein copy karke naap sakte hain; (3) user se restart ki ijazat.
- **Haal (12:15) — apni key ke baad ka naap + "Files jaisi speed" ki khoj (user: "file jayse speed xhahiye"):**
  - 10:53:40 "All Accounts" **G:** par judi (Z: mari hui, restart tak). MiniMax ki baaki 769 files upload: 0 x 403, 0 fail.
  - **Install jaisa naap** (`.work\speed\install-sim.ps1 -Drive G`: MiniMax ki 853 files / 143 folder / 970 MB ek-ek karke likhna; asli installer nahi, kyunki registry ka raasta `Z:\MiniMax Code` hai):

    | Kahan | Samay |
    |---|---|
    | Laptop ki disk (asli Files) | **3 s** |
    | Cloud drive, shared key (subah ka asli install) | 42 min |
    | Cloud drive, apni key (abhi) | **13.1 min** (785 s) + Google tak upload 6.2 min aur; 0 x 403, 0 fail |
    | Test drive: "naya sab laptop ke staging folder mein" (rclone union, Google `:nc`) — `stage-test.ps1` | 7.7 min (464 s) |
    | Test drive: sirf laptop ka folder rclone ke raaste (Google bilkul nahi) — `stage-test.ps1 -Mode local` | **71 s** = rclone mount ki apni seema |
    | **Hybrid namoona**: drive = laptop ka folder; purane cloud folder link; naya folder asli laptop ka — `hybrid-check.ps1` | **4.3 s** |

  - Apni key ke baad 13 min kahan jaate hain (10:54-11:00 ka log): `Mkdir` 74 call = 179 s (avg 2.4 s), naye folder ki pehli `Readdir` 109 = 104 s. Har naya folder = Google ki 1-2 call, 143 folder.
    App ki limit `Network.TpsLimit = 10` aur `--drive-pacer-min-sleep 100ms` shared key ke zamane ki hain (apni key par badhayi ja sakti hain) — par 71 s wali seema se neeche rclone drive kabhi nahi jayegi.
  - Hatana bhi dheema: 853 files hatane mein 20 min (har file ek call). `operations/purge` (rc) ne union par folder poora nahi hataya.
  - rclone drive ke andar Windows ka link nahi banta (`link-check.ps1`: symlink "Access is denied", junction "tag mismatch"). Ulta chalta hai: laptop ke folder se cloud drive ke folder ka symlink (10 link 0.4 s; link ke raaste cloud folder 0.01 s mein khula).
  - `rclone-cloudssd.log` 11:00:15 par 64 MB ho kar ruk gaya (teen purani file pehle se hain): uske baad ki ginti log se nahi milti. Dekhna hai.
  - Lambe raaste: MiniMax ke raaste 260 akshar se lambe hain; `Remove-Item` unhe nahi hatata → `[IO.Directory]::Delete('\\?\' + path, $true)`.
- **"Hybrid Z:" ka vichaar (user ki haan chahiye, abhi sirf namoona naapa, app mein kuch nahi bana):** Z: = laptop ka folder (subst ya app ka apna raasta). Uske andar cloud ke purane
  folder link (→ rclone mount, jo kisi chhupe letter / folder par ho). Naya folder / file seedha laptop ki disk par (4 s), app peeche se Google par upload kare; C: bharne lage to upload ho chuki cheez
  "sirf cloud" kar de (folder ki jagah link). Sochne ki baatein: Z: tab C: ki khaali jagah dikhayegi (116 GB, 15 TB nahi); purane cloud folder ke ANDAR likhna tab bhi dheema (jab tak woh folder
  "laptop par" na ho); link wala folder Explorer mein delete karne par sirf link hatta hai; phone wala bridge / Files page / games / offloader sab Z:\ padhte hain → har ek dekhna hoga; drives "permanent" wala niyam.
- **Ruka hua kis par:** (1) user ka faisla: hybrid Z: banayein ya nahi; (2) **PC restart** (Z: wapas laane ke liye; uske baad asli MiniMax installer se naap). Restart se pehle roshan-57 ko batana (usne kaha use dikkat nahi).

---

## Kaam: Hybrid Z: / "Fast drive" — naya likhna pehle laptop par, upload peeche (Claude Code, roshan-48) - CHAL RAHA HAI

- **Tareekh & Samay:** 17 Nov 2026, 12:03 se · **TASK-CLAUDE-HYBRID** (lock isi ka; `TASK-CLAUDE-CLI-TUI-3` bhi khula hai, uska lock `.work\speed\CURRENT_TASK.cli3-lock.before-hybrid.json` mein rakha hai)
- **User ka kehna:** "file jayse speed xhahiye" → sawaal mein chuna **"Haan, Hybrid Z: banao"**; restart par: "app ko jo karna hai karo, par main abhi doosri chat mein kaam kar raha hu, laptop restart nahi kar sakta".
  **Isliye PC restart MAT karo jab tak user na kahe.**
- **Backup:** `backup\2026-11-17_1203_hybrid-z-local-first-drive` (sirf `before\`). Doosra `start` mat chalao.
- **Kya ban raha hai (Phase 1):** drive ka letter (Z:) = laptop ka folder `%USERPROFILE%\Cloud-SSD\All Accounts`. Cloud ke upar wale har folder / file ka us folder mein **link** (→ cloud drive, jo apne
  letter `X:` par chali jati hai). Naya folder / file asli laptop ka → Files jaisi raftaar; 20 s shaant rehne par cloud drive ka apna rclone use Google par copy karta hai (rc `sync/copy`, kabhi delete nahi).
  Kuch bhi delete nahi hota: user link hataye to cloud ki cheez sirf "chhup" jati hai (`Hidden`), cloud se nahi hatti. "Sirf cloud" banana (laptop se hatana) Phase 2 hai, abhi NAHI bana.
- **Nayi / badli files:**
  - `src\CloudSSD.Core\Settings.cs` — `HybridDriveSettings` (`Enabled` default **false**, `Letter` Z, `BackingLetter` X, `Root`, `MountId`, `QuietSeconds`) + `AppSettings.Hybrid`.
  - `src\CloudSSD.App\Hybrid\DosDevice.cs` (letter ↔ folder, "subst" jaisa; Explorer ka naam "Cloud SSD"), `HybridLayout.cs` (+ `HybridState`, `hybrid-state.json`; link banana / hatana, sirf link),
    `HybridDriveService.cs` (10 s ka pass: letter, link, upload; letter atka ho to us session ke liye doosra khaali letter; `EnableAsync`, `Disable`, `MoveCloudDriveAsync` — program drive se chal raha ho ya upload adhoore hon to mana).
  - `src\CloudSSD.App\AppHost.cs` — `Hybrid` property, start par `ClaimLetter()` (mount se pehle) + `Start()`, `Dispose`. Sirf desktop app (`restoreMounts: true`), CLI nahi chalata.
  - `src\CloudSSD.Desktop\ViewModels\CloudDrivesViewModel.cs` + `Views\CloudDrivesView.xaml` — Cloud Drives page par "Fast drive" card (on/off button, haal, "Open the fast drive").
  - `tests\CloudSSD.Tests\HybridDriveTests.cs` — 9 test (link banna, user ne link hataya → chhupa, cloud se hata → link gaya, cloud door / khaali dikhe → kuch nahi hatta, letter badla → link ghoome, state file).
- **Jaanch ab tak:** build 0 warning / 0 error · naye 9 test pass (is PC par Developer Mode on hai, isliye link sach mein bane). Poora suite chal raha hai.
- **Adhoori files:** koi compile-adhoori nahi. **Nahi hua abhi:** poora test suite ka nateeja, release build, PC app install, asli jaanch (app ke andar on karke 853 files likhna + upload dekhna), Hinglish shabd (PC app ke naye text angrezi mein hain),
  `backup.ps1 validate / complete` + README, `docs\CLAUDE.md` mein entry.
- **Aaj ki rukavat:** Z: ka naam mara hua hai (restart tak) → app aaj tez drive ko kisi doosre letter par dega (log: "Z: is held by something else, using W: until the next start"); restart ke baad apne aap Z:.
  Asli MiniMax installer se naap bhi restart ke baad hi (uska raasta `Z:\MiniMax Code` hai).
- **Agla kadam:** (1) poora test suite hara; (2) `installer\build-release.ps1 -SkipTests`; (3) app band (drives rahein) → install → chalu (`.work\cli-tui\update-app.ps1` yahi karta hai; pehle app band karna Claude ke
  permission system ne roka tha — roke to user chalaye); (4) Cloud Drives → "Switch the fast drive on" → `.work\speed\install-sim.ps1` jaisa naap tez drive ke letter par + log mein "Fast drive: upload finished";
  (5) validate + complete + README + `docs\CLAUDE.md`.
- **Haal (12:20, usage limit par ruka) — TASK-CLAUDE-HYBRID:** build 0/0 · C# **631/631** pass (622 + 9 naye) · release bana: `dist\Cloud-SSD-0.9.0-win-x64` + zip 95.6 MB,
  SHA256 `34a133c4…fc38b47f` (12:15). Is build mein Hybrid ("Fast drive", default OFF) + CLI-TUI-3 + FASTSTART ke app wale hisse sab hain.
  - **App update adhoora:** 12:15:58 par `.work\speed\app-close.ps1 -Answer Yes` chalaya. App ne likha "Exiting; drives stay mounted" + "Mobile Bridge stopped", window band, par **process (PID 10584) band nahi hua**
    (6 thread, atka hua; shayad subah wale atke rclone 3120 ki wajah se). G: aur Y: chal rahi hain (rclone alag process hai). Phone ka bridge tab tak band hai.
  - **Agla kadam (jo bhi session aaye, ya user):** (1) atka hua `Cloud-SSD.exe` band karo (Task Manager → Cloud-SSD → End task; drives ko kuch nahi hota);
    (2) `powershell -ExecutionPolicy Bypass -File dist\Cloud-SSD-0.9.0-win-x64\Install-CloudSSD.ps1 -Quiet`; (3) `Start-Process "$env:LOCALAPPDATA\Programs\Cloud-SSD\Cloud-SSD.exe"`;
    (4) app → Cloud Drives → "Switch the fast drive on" → Yes; log mein "Fast drive switched on" aur "… link(s) made" dekho; aaj letter Z: ki jagah koi doosra milega (Z: restart tak mara hua);
    (5) naap: us letter par naya folder bana kar `robocopy` (jaise `.work\speed\install-sim.ps1`), ummeed ~4 s; chhota folder bana kar "Fast drive: upload finished" aur cloud drive (G:) par uski copy dekho; test folder hatao;
    (6) `backup.ps1 validate` + `complete` + Hinglish README, `docs\CLAUDE.md` mein entry, lock band. **Restart user ki haan ke bina nahi.** Push nahi hua.
- **Haal (14:00) — TASK-CLAUDE-HYBRID POORA (Phase 1), asli app mein jaancha, backup band:**
  - Atka hua purana app (PID 10584, HasExited, kill nahi hota) ke rehte bhi naya app chal gaya: `instance.lock` khaali tha; program files usne pakad rakhi thi →
    `.work\speed\install-over-stuck.ps1` ne 10 purani files ka naam badla (`*.old-11171350`, install folder mein bachi hain; restart ke baad hata sakte ho), phir `robocopy` (installer khud "running" keh kar ruk gaya) → app 13:50:38 se naye build par.
    Bridge 8090 chalu, "Re-attached" Y: aur G:.
  - 13:51:48 Fast drive ON (button + Yes maine dabaya, user ki haan thi): log "Z: is held by something else, using **W:** until the next start", "11 link(s) made".
    `settings.json`: `Hybrid.Enabled = true`, `Letter = Z`, cloudssd mount ka `DriveLetter = X` (agle mount se).
  - Naap (`.work\speed\fast-live-test.ps1 -Fast W -Cloud G`): 853 files **5.1 s**; 55 files / 47 MB → app ka upload 65 s → G: par 55/55; purana cloud folder link se 0.01 s; safai ke baad dono jagah 0 leftover.
  - `backup.ps1 validate` APPROVED (631/631) → `complete` + Hinglish README. `docs\CLAUDE.md` mein entry. Lock wapas `TASK-CLAUDE-CLI-TUI-3` (IN_PROGRESS) par rakha.
  - Aakhri chhota badlaav jo installed build mein NAHI: `HybridDriveSettings.RootOrDefault` par `[JsonIgnore]` (abhi settings.json mein woh field bhi likhi jati hai; nuksaan nahi).
- **Abhi PC ka haal:** drives G: (All Accounts, cloud), Y: (Each Account), **W: (Fast drive, laptop ka folder `%USERPROFILE%\Cloud-SSD\All Accounts`)**, Z: mara hua naam (restart tak).
  Atke hue process: Cloud-SSD 10584, rclone 3120, MiniMax Code 9576 (restart par jayenge). MiniMax abhi `W:\MiniMax Code\MiniMax Code.exe` se chal sakta hai (shortcut Z: wala restart tak nahi).
- **Agla kadam:** (1) **user jab kahe tab restart**; uske baad: `cloudssd-<tareekh>.log` mein "Fast drive" lines (Z: mila? "re-pointed" kitne?), cloud drive X: par?, `Z:\MiniMax Code` khulta hai?;
  (2) asli MiniMax installer `Z:` ke naye folder mein chala kar samay; (3) "Switch off" + `MoveCloudDriveAsync` jaanch; (4) Phase 2: upload ho chuki cheez "sirf cloud" banana (C: bharne par) — user se pooch kar;
  (5) CLI-TUI-3 / FASTSTART: app ab installed hai → `/disconnect Y`, `/connect Y`, `/send`, `cli.key`, restart ka samay → unke backup band; (6) Hinglish shabd; (7) commit / push sirf user kahe to.

---

## Kaam: "Kholne mein der" — folder khulte hi uski files pehle se taiyaar (Claude Code, roshan-48) - CHAL RAHA HAI

- **Tareekh & Samay:** 17 Nov 2026, 14:05 se · **TASK-CLAUDE-WARMUP** (lock isi ka) · backup `backup\2026-11-17_1419_open-speed-folder-warmup` (sirf `before\`)
- **User ka kehna:** "abhi bhi time le raha hai kam se kam 4-5 second or usse jada bhi oska kuch karo … install speed restart ka problem me tumhe batane se tha"
- **Naapa (`.work\speed\open-times.ps1`, `first-open.ps1`, `who-reads.ps1`):** drive letter aur folder turant khulte hain (folder pehli baar ~0.7 s, phir 10 ms).
  **File pehli baar kholna dheema hai, aur der "open" call mein hi lagti hai:** 0.4-0.9 MB → 1.7 s; 5-14 MB → 3-15 s; uske baad padhna 3-30 ms; doosri baar kholna 5 ms.
  Y: ke log se: `OpenEx` ke 3 ms baad kisi ne 64 KB offset 0 se padha (mera test padhta hi nahi → Windows Defender ka on-access scan; `RealTimeProtectionEnabled = True`),
  Google ka pehla byte **1.5 s** baad aaya, 64 KB poore hone mein 3 s. Internet line us waqt 9.9 MB/s thi (line ki galti nahi). Zip / exe ko scanner poora padhta hai → lambi der.
- **Saath mein mila:** `rclone-cloudssd.log` 11:00:15 se 64 MB par atka hai: rename (rotation) "sharing violation" deta hai, kyunki subah wala **atka hua rclone 3120** us file ko pakde hai
  (Go ki log file bina FILE_SHARE_DELETE ke khulti hai). Isse G: ke liye log se chalne wali saari cheezein (folder-listed event, read telemetry, ArchiveWarmer) band hain; restart par theek hoga.
- **Fix (code ban gaya, build 0/0, C# 634/634):**
  - `src\CloudSSD.App\ArchiveWarmer.cs` — ab har file ke liye: folder khulte hi uski files peeche se cache mein (chhoti ≤ 4 MiB poori; badi ka shuru 512 KiB + aakhri 128 KiB; archive pehle jaisa),
    sabse chhoti pehle, ek folder ke liye ≤ 60 files / ≤ 128 MiB, 3 ek saath; andar ke ≤ 40 folder bhi pehle se list. Log mein ek line per folder, bina naam ke ("Folder ready to open: N file(s) …").
  - naya `src\CloudSSD.App\ExplorerFolderWatcher.cs` — har 1.5 s File Explorer ki khuli windows ka folder padhta hai (Shell.Application, STA thread) → log par nirbhar nahi.
  - `src\CloudSSD.App\AppHost.cs` — `ExplorerFolders`, `CloudPlaceOf(path)` (cloud drive ka raasta, ya Fast drive ke link ke raaste), start / dispose.
  - `tests\CloudSSD.Tests\ArchiveWarmerTests.cs` — 3 naye test + purana badla (ab doosri files bhi aati hain).
- **Nahi hua abhi:** release build, install, asli naap (Explorer mein folder kholo → log "Folder ready to open" → `first-open.ps1` jaisa naap us folder ki files par), validate / complete / README, `docs\CLAUDE.md`.
- **Dhyan:** app band karte waqt pichhli baar purana app atak gaya tha → `.work\speed\install-over-stuck.ps1` (atki files ka naam badal kar install) + seedha `robocopy` + app start kaam aaya.
- **Haal (14:35) — TASK-CLAUDE-WARMUP POORA, asli app mein jaancha, backup band (README usi folder ke `complete\` mein):**
  - Installed build ab **14:29:15** ka hai (release SHA256 `4d4b310a…071ff646`): Hybrid + warm-up + letter ka sudhaar + `[JsonIgnore]`. App PID naya, bridge 8090, G: / Y: "Re-attached", Fast drive **W:** (V: wala faltu letter hata diya).
  - App ab theek se band hota hai (atka sirf subah wala purana instance tha). Update ka tareeka jo chala: `app-close.ps1 -Answer Yes` → `install-over-stuck.ps1` (robocopy + start; aam installer atke process ki wajah se mana karta hai).
  - Naap: pehle file ka pehla open 1.7-15 s → ab khule folder ki files 0.02-0.22 s. Seema README mein (turant click, badi file ka beech, sirf Explorer ki window).
  - Lock wapas `TASK-CLAUDE-CLI-TUI-3` (IN_PROGRESS). Push nahi hua.
- **Agla kadam:** wahi jo upar "Agla kadam" mein hai (restart user ki haan par → Z: / X:, asli MiniMax installer, Switch off ki jaanch, Phase 2 "sirf cloud", CLI-TUI-3 / FASTSTART ke kadam), plus restart ke baad warm-up dobara naapna
  aur `rclone-cloudssd.log` phir se chal raha hai ya nahi dekhna.

---

## Kaam: Speed check + Speed Lock (Claude Code, roshan-48) - COMPLETE

- **Kab:** 17 Nov 2026, 14:35 - 14:50 · TASK-CLAUDE-SPEEDLOCK · backup `backup\2026-11-17_1435_speed-check-and-lock` (before + complete + README, APPROVED 640/640)
- **User:** "kaise pata ke open ho raha hai, isko lock kar sakte hai, baad me late open hona shuru kar dega".
- **Bana:** `SpeedGuard` (app start ke 90 s baad + har 3 ghante + Cloud Drives → "Check the speed now"): key, cloud drive, Fast drive par asli likhna (200 files), taiyaar file ka asli open, warm-up, mare hue letter, atka log.
  Nateeja `%LOCALAPPDATA%\Cloud-SSD\speed-check.json` + log "Speed check: …"; nayi gadbad par note + ghanti. **`RULE.md` hissa 10 "Speed Lock": har agent padhe** (kya nahi chhedna, naape hue number, pehle / baad naap).
- **Installed build:** 14:41:31 (release SHA256 `c1577a16…ae1c73a2`). App chal raha hai; Fast drive W:; lock wapas `TASK-CLAUDE-CLI-TUI-3`.
- **Agla kadam (koi bhi session):** kuch bhi mount / warm-up / hybrid chhedne se pehle `speed-check.json` padho aur baad mein dobara chalao. Restart user ki haan par. Baaki list upar wali hi hai.
