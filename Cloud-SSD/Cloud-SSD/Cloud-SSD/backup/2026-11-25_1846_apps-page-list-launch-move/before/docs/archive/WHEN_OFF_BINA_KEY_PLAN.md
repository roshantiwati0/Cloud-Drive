# Plan: PC band ho tab bhi uski files phone se — bina Google key ke

Tareekh: 14 November 2026. User ka sawaal: "koi aisi cheez nahi kar sakte jisme Google key bhi na lage aur kaam bhi ho jaye?"
Jawab: **haan**. Neeche teen raaste hain jinme app ke andar koi Google key nahi lagti. Pehla (A) mukhya hai.

Purana plan (`docs\WHEN_OFF_PLAN.md`) wahi rehta hai; uska "phone khud Google mein sign in kare" wala hissa (Phase 3)
ban chuka hai aur tab kaam aayega jab kabhi key bane. Yeh plan uske bina kaam chalata hai.

## Seedhi baat

Band PC khud file nahi de sakta. Isliye file ki copy pehle se kahin aur honi chahiye. Google key isliye chahiye thi
ki phone seedha Google Drive khole. Key ke bina copy ko aisi jagah rakhte hain jahan phone bina Google ke pahunch sake:

```
Raasta A (mukhya):   PC (chalu) --Wi-Fi--> phone ke andar copy      PC band -> phone apni copy kholta hai
Raasta B:            PC (chalu) --> Google Drive (jaise abhi)       PC band -> phone ka "Google Drive" app wahi folder kholta hai
Raasta C:            phone --"jaag jao"--> PC chalu ho jata hai      -> files seedhi PC se (bilkul taaza)
```

## Raasta A — copy phone ke andar (internet bhi nahi chahiye)

App mein "Offline rakho" pehle se hai (`OfflinePins.java`): chuna hua folder phone par utar jata hai, peeche-peeche
taaza hota rehta hai, aur PC door ho tab phone ki copy se khulta hai. Aaj yeh sirf cloud drive (Z:, Y:) ke folder ke
liye hai; **PC ki apni disk (C:) ke folder ke liye band hai**. Bas wahi kholna hai.

Kyun yeh aapke liye theek hai: aapke apne folder chhote hain (Desktop, Documents, Pictures, Videos, Music mein kul
~40 files; Downloads 5.7 GB) aur phone mein ~74 GB khaali hai. Bhaari cheezein (Steam games 155 GB, Windows) phone par
nahi aayengi; unke liye Raasta B hai.

Kadam:
- **K1 (PC):** PC ki disk ko bhi "drive id" se dena: `disk-c:Users/…` (sirf padhna). Wahi permission jo aaj PC ki files
  dekhne ke liye hai (Browse), wahi roktham. Feature ka naam `fs-disk-roots`. Tests.
- **K2 (phone page):** PC disk ke folder / file par bhi "Keep offline" dikhe; rakhi hui copy PC band hone par khule;
  folder ki list phone ki copy se bane.
- **K3 (ek tap):** More → Permissions → "Files when it is off" mein button **"Is PC ke mukhya folder phone par rakho"**:
  Desktop, Documents, Pictures, Videos, Music, Downloads. Dabane se pehle kitni jagah lagegi woh dikhe; phone mein
  hamesha 500 MB khaali rahe (yeh niyam pehle se hai).
- **K4 (apne aap taaza):** jab bhi phone aur PC judein, badli hui files apne aap aa jayein; PC par mitayi file phone
  se bhi jaye (yeh `OfflinePins` pehle se karta hai; PC disk ke liye jaanchna hai). Ek folder mein 5000 files ki seema
  hai: zarurat ho to badhana.
- **K5 (asli jaanch):** chhota folder rakho → PC app band karo → phone se folder kholo, file padho → PC par file badlo /
  mitao → dobara judne par phone par bhi badle. Jagah kam ho to saaf sandesh.

Seemaayein: sirf chune hue folder; copy utni hi taaza jitni aakhri baar judne par thi (upar samay likha rahega);
band PC par badlaav nahi (sirf padhna).

## Raasta B — cloud copy ko phone ke "Google Drive" app se kholna

PC ki files ki cloud copy wala engine ban chuka hai (Devices → When off → Continue). Files Google Drive mein seedhi
(asli naam, asli folder) rakhi jaati hain: `Cloud-SSD Devices/<PC>/files/…`. Inhe kholne ke liye hamare app ko Google
key nahi chahiye agar hum **Google ka apna Drive app** (ya drive.google.com) use karein.

- **K6:** PC phone ko us folder ka pata (Drive folder link) bata de; phone use yaad rakhe. PC band ho to phone par button
  **"Cloud copy Google Drive mein kholo"** → Drive app usi folder par khulta hai.
- Shart: phone ke Drive app mein wahi Google account ho jisme PC copy rakhta hai (abhi `gdrive2` wala).
- Seema: upload dheema hai (saajhi key par har baar ~40 second) aur rclone 2026 mein saajhi key band kar raha hai;
  tab PC ka Google Drive hi rukega, isliye key kabhi na kabhi banani padegi. Tab tak yeh chalta hai.

## Raasta C — phone se PC ko chalu karna (Wake)

App mein "PC ko jagao" (Wake-on-LAN) pehle se hai. PC so raha ho (ya kuch PC mein poora band ho tab bhi) phone ek
sandesh bhej kar use chalu kar sakta hai; phir files seedhi PC se, bilkul taaza.

- **K7:** is PC par jaanchna ki yeh chalta hai ya nahi (network card ki setting, BIOS). Chale to phone par PC band dikhne
  par "PC chalu karo" sabse upar.
- Seema: sirf ghar ke Wi-Fi par (ya ghar mein koi doosra device sandesh pahunchaye); har PC / Wi-Fi card nahi karta;
  PC sach mein chalu ho jata hai.

## Kram

1. K1 → K2 → K5 ka chhota hissa (ek folder haath se rakh kar PC band karke kholna). Yahin pehla asli nateeja.
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
