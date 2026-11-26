# "Apps" page: laptop ke apps apne aap list, ek click se chalu, laptop ⇄ cloud

**Kaam:** TASK-CLAUDE-APPS · 25 Nov 2026 · Claude Code (Claude Fable 5.2) · jaanch: APPROVED (build 0 warning / 0 error, C# 692/692)

## Yeh kya hai

Pehle game jodne ke liye haath se drive aur folder ka raasta dena padta tha, aur sirf wahi chalta tha jo pehle se cloud
drive par ho. Ab PC app ke sidebar mein **Apps** naam ka page hai:

- laptop par install program aur cloud drive ke `Apps` folder ke program **apne aap** list mein aate hain (icon, size, jagah);
- upar naam likh kar dhoondh sakte ho;
- **Open**: program waise hi khulta hai jaise Start menu se;
- **Play as game**: game mode ke saath khulta hai (bekaar pade apps RAM se hat jaate hain, upload dheema);
- **Move to cloud**: program ka folder cloud drive par chala jata hai, laptop ki jagah khali;
- **Bring back to this PC**: wapas laptop par (tez, bina internet).

## Kaise use hota hai

1. PC app → **Apps**. List khud bharti hai; naya program install kiya ho to "Look again".
2. Chalana: row ke **Open** par click.
3. Cloud par bhejna: **Move to cloud** → sawaal par Yes → copy hota hai (upar progress, "Stop" se rok sakte ho) →
   aakhir mein Windows ek baar "Yes" maangta hai (folder ki jagah link rakhne ke liye) → ho gaya.
4. Wapas lana: usi row par **Bring back to this PC** → Yes → poochhta hai cloud wali copy rakhni hai ya nahi.

Dhyan:
- Cloud par gaya program **internet se** chalta hai aur pehli baar dheere khulta hai.
- Bada program dheeme internet (phone hotspot) par ghanton lega. Pehle apni Google key lagao (Cloud Drives).
- `Program Files` wale program ke liye Cloud-SSD ko ek baar administrator ki tarah chalana padega.
- Microsoft Store ke app, Windows ke apne tool aur chalta hua program nahi bheje ja sakte (button par wajah likhi aati hai).
- RAM cloud par bhejne se nahi bachti; RAM "Play as game" (game mode) bachata hai.

## Andar se kaise chalta hai

```
Start menu + Desktop ke shortcut ─┐
"Installed apps" (size, folder) ──┼─► AppCatalog.Read() ─► AppsViewModel ─► AppsView (list)
cloud drive ka  Apps\*  ──────────┘                              │
                                                                 ├─ Open ─────────► AppCatalog.Start()
                                                                 ├─ Play as game ─► AppHost.GameFor() ─► PlayAsync()
                                                                 └─ Move / Bring back ─► AppMover

Move to cloud:   copy (rclone sync/copy, seedha cloud)
              ─► ginti + size milaan (operations/size)
              ─► apps-moved.json: "linking"
              ─► folder ─► folder.cloudssd-old
              ─► symlink  folder ─► Z:\Apps\<naam>      (Windows ka "Yes")
              ─► exe link se padha gaya?  nahi ─► sab wapas
              ─► apps-moved.json: "done" ─► .cloudssd-old delete

Bring back:      copy cloud ─► folder.cloudssd-new ─► milaan ─► link hatao ─► naam badlo ─► record hatao
```

Beech mein app band ho jaye / bijli chali jaye: agle start par `AppMover.Recover()` "linking" wale ko wapas apni jagah
rakh deta hai. Copy ke dauraan kuch bhi toote to laptop ka folder chhua hi nahi gaya hota.

## Files

Naye: `src\CloudSSD.App\AppCatalog.cs`, `src\CloudSSD.App\AppMover.cs`, `src\CloudSSD.Desktop\AppIcons.cs`,
`src\CloudSSD.Desktop\LocalizationManager.Apps.cs`, `src\CloudSSD.Desktop\ViewModels\AppsViewModel.cs`,
`src\CloudSSD.Desktop\Views\AppsView.xaml(.cs)`, `tests\CloudSSD.Tests\AppCatalogTests.cs`, `tests\CloudSSD.Tests\AppMoverTests.cs`.

Badle: `src\CloudSSD.App\AppHost.cs`, `src\CloudSSD.Desktop\MainWindow.xaml`,
`src\CloudSSD.Desktop\ViewModels\{MainViewModel,GameLibraryViewModel}.cs`,
`src\CloudSSD.Desktop\Views\GameSettingsWindow.xaml.cs`, `src\CloudSSD.Desktop\LocalizationManager.Devices.cs`, `docs\{CLAUDE,CHALU_KAAM}.md`.

## Jaanch

- `backup.ps1 validate` → APPROVED: build 0/0, 692 tests pass (31 naye: list banana / chhantna, move, wapas lana,
  mana karna, beech mein rukna, adhoora move wapas).
- Asli PC: Apps page par 11 program, search, Open se Inno Setup khula.
- Asli Google Drive par 300 KB ka test app: bheja → link bana → purane shortcut se khula → wapas laya → sab files wapas.
- **Nahi jaancha:** "Play as game" asli app par, GB wala bada move, `Program Files` wala app, Hinglish / Hindi mein page.
- **Nahi bana:** Steam / Epic ke games ko list mein lana (is PC par dono nahi hain).

## Wapas kaise jaayein

`powershell -ExecutionPolicy Bypass -File tools\backup\backup.ps1 restore -Folder "2026-11-25_1846_apps-page-list-launch-move"`
(pehle koi program cloud par bheja ho to use Apps page se "Bring back to this PC" kar lo; record
`%LOCALAPPDATA%\Cloud-SSD\apps-moved.json` mein hai).
