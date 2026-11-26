# Apni Google key kaise banayein (ek baar, lagbhag 10–15 minute, muft)

## Yeh kyun chahiye

- Abhi Cloud-SSD Google mein ek **saajhi (shared) key** se sign-in karta hai. Woh dheemi hai (har kaam 40 second tak)
  aur Google use 2026 mein band kar raha hai.
- **Band PC ki files phone se kholne** ke liye phone ko khud Google mein sign-in karna padta hai. Uske liye aapki
  apni key zaroori hai. Key milte hi PC use phone ko de deta hai (sirf key; aapka Google login kabhi nahi bhejta).

Key matlab: Google ke yahan aapke naam ka ek chhota "app" jiske naam se Cloud-SSD sign-in maangta hai.
Isme koi paisa nahi lagta aur aapki files kisi aur ko nahi dikhtin.

## Kadam

1. Browser mein kholo: **https://console.cloud.google.com** aur apne Google account se sign-in karo.
2. **Project banao:** upar project wale naam par click → "New project" → koi bhi naam (jaise `Cloud-SSD`) → Create.
   Ban jaye to upar wahi project chuna hua ho.
3. **Drive chalu karo:** baayein menu → "APIs & Services" → "Library" → khojo `Google Drive API` → kholo → **Enable**.
4. **App ka naam bharo:** baayein menu → "Google Auth Platform" → "Branding":
   - App name: `Cloud-SSD` (kuch bhi)
   - User support email aur Developer email: apna e-mail
   - Audience: **External** → Save.
5. **"In production" karo (zaroori):** "Google Auth Platform" → "Audience" → **Publish app** → Confirm.
   Agar yeh "Testing" mein raha to Google har 7 din mein login khatam kar deta hai aur sab ruk jata hai.
6. **Key banao:** "Google Auth Platform" → "Clients" → "Create client":
   - Application type: **Desktop app**
   - Name: kuch bhi → Create
   - **Download JSON** dabao. File (`client_secret_....json`) **Downloads** folder mein aa jayegi. Use wahin rehne do.
7. **Cloud-SSD mein lagao:** PC app → **Cloud Drives** → **"Switch to my own Google key"**.
   App file ko Downloads mein khud dhoondh leta hai. Har Google account ke liye ek baar browser khulega: sign-in karke
   "Allow" dabao. (Aapke 3 account hain, to 3 baar.)

## Raaste mein yeh dikhe to

- **"Google hasn't verified this app"**: yeh aapka apna app hai. "Advanced" → "Go to Cloud-SSD (unsafe)" dabao. Theek hai.
- **Key file nahi mili**: app poochhega; Downloads se `client_secret_....json` chun do.
- **Kisi account mein dikkat**: baaki account ho jaate hain; us ek ko dobara "Switch to my own Google key" se karo.
  Aapki files aur drive ke letter (Z:, Y:) waise hi rehte hain.

## Key lagne ke baad

1. PC app → Devices → **When off** → **Continue** dabao (copy banna shuru) aur **"Start Cloud-SSD with Windows"** tick karo.
2. Phone app → More → Permissions → "Files when it is off" mein **"Sign in"** dikhega: usi Google account se sign-in
   karo jisme PC ki copy ja rahi hai. (Pakka na ho to teeno account se sign-in kar lo; har ek ke liye ek baar.)
3. Ab PC band karke phone ke **Cloud SSD** tab mein "‹PC ka naam› (cloud copy)" kholo.

(Kadam 2 aur 3 wala phone ka hissa 14 Nov 2026 ko bana hai; uski asli jaanch key lagne ke baad hi ho sakti hai.
Kuch alag dikhe to Claude / Codex ko batao.)

## Dhyan

- `client_secret_....json` file kisi ko mat bhejo aur GitHub par mat daalo.
- Apne Google account par **2-step verification** chalu rakho: PC ki files ab usi account mein hongi.
