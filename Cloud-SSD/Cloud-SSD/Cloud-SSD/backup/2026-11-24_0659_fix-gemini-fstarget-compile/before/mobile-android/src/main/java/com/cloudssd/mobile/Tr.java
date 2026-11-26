package com.cloudssd.mobile;

import android.content.Context;

import java.util.HashMap;
import java.util.Locale;
import java.util.Map;

/**
 * The words of the phone's own notes (ring, requests from the PC, the listening note) in the language chosen in the
 * app (English, Hinglish or Hindi; the page tells it with setLanguage). English is the key, "{0}" a value.
 */
final class Tr {
    private static final Map<String, String> HINGLISH = new HashMap<>();
    private static final Map<String, String> HINDI = new HashMap<>();

    static {
        put("{0} is ringing this phone", "{0} is phone ki ghanti baja raha hai", "{0} इस फ़ोन की घंटी बजा रहा है");
        put("Found it? Tap here to stop.", "Mil gaya? Rokne ke liye yahan tap karo.", "मिल गया? रोकने के लिए यहाँ टैप करें।");
        put("Stop", "Roko", "रोकें");
        put("Found it? Tap the button to stop.", "Mil gaya? Rokne ke liye button dabao.", "मिल गया? रोकने के लिए बटन दबाएँ।");
        put("Found it — stop", "Mil gaya — roko", "मिल गया — रोकें");
        put("{0} wants to see this phone's files", "{0} is phone ki files dekhna chahta hai", "{0} इस फ़ोन की फ़ाइलें देखना चाहता है");
        put("{0} wants to add files to this phone", "{0} is phone me files daalna chahta hai", "{0} इस फ़ोन में फ़ाइलें डालना चाहता है");
        put("Allow only if you are at that PC. It can add files and folders for 1 hour.",
                "Tabhi allow karo jab aap us PC ke paas ho. 1 ghante tak wo files aur folders daal sakega.",
                "तभी अनुमति दें जब आप उस पीसी के पास हों। 1 घंटे तक वह फ़ाइलें और फ़ोल्डर डाल सकेगा।");
        put("Allow only if you are at that PC. Its whole storage for 1 hour.",
                "Tabhi allow karo jab aap us PC ke paas ho. Poora phone storage 1 ghante ke liye.",
                "तभी अनुमति दें जब आप उस पीसी के पास हों। पूरा फ़ोन स्टोरेज 1 घंटे के लिए।");
        put("Allow 1 hour", "1 ghante allow karo", "1 घंटे की अनुमति दें");
        put("Deny", "Mana karo", "मना करें");
        put("Cloud-SSD is listening for {0}", "Cloud-SSD {0} ki sun raha hai", "Cloud-SSD {0} की सुन रहा है");
        put("So it can ring this phone even with the app closed. Tap to switch it off.",
                "Taaki app band ho tab bhi ghanti baj sake. Band karne ke liye tap karo.",
                "ताकि ऐप बंद हो तब भी घंटी बज सके। बंद करने के लिए टैप करें।");
        put("It may look at this phone's files for now. Tap to see or stop it.",
                "Abhi woh is phone ki files dekh sakta hai. Dekhne ya rokne ke liye tap karo.",
                "अभी वह इस फ़ोन की फ़ाइलें देख सकता है। देखने या रोकने के लिए टैप करें।");
        put("Clipboard from {0}", "{0} se clipboard aaya", "{0} से क्लिपबोर्ड आया");
        put("Your PC", "Aapka PC", "आपका पीसी");
        put("your PC", "aapke PC", "आपके पीसी");
        put("the PC", "PC", "पीसी");

        // Transfers
        put("Sending {0}", "{0} bhej rahe hain", "{0} भेज रहे हैं");
        put("Downloading {0}", "{0} download ho raha hai", "{0} डाउनलोड हो रहा है");
        put("Sent to PC", "PC ko bhej diya", "पीसी को भेज दिया");
        put("Saved to Google Drive", "Google Drive me save hua", "Google Drive में सेव हुआ");
        put("Saved to Downloads", "Downloads me save hua", "Downloads में सेव हुआ");
        put("Upload stopped", "Upload ruk gaya", "अपलोड रुक गया");
        put("Download stopped", "Download ruk gaya", "डाउनलोड रुक गया");
        put("{0}: {1} — open the app to retry", "{0}: {1} — dobara try karne ke liye app kholo",
                "{0}: {1} — फिर से कोशिश करने के लिए ऐप खोलें");
        put("Moving files between this phone and the PC…", "Is phone aur PC ke beech files ja rahi hain…",
                "इस फ़ोन और पीसी के बीच फ़ाइलें जा रही हैं…");
        put("Sending 1 file to PC…", "1 file PC ko bhej rahe hain…", "1 फ़ाइल पीसी को भेज रहे हैं…");
        put("Sending {0} files to PC…", "{0} files PC ko bhej rahe hain…", "{0} फ़ाइलें पीसी को भेज रहे हैं…");

        // Camera backup
        put("Camera backup", "Camera backup", "कैमरा बैकअप");
        put("Camera backup · {0} of {1}", "Camera backup · {1} me se {0}", "कैमरा बैकअप · {1} में से {0}");
        put("{0} new item saved via {1}", "{0} naya item {1} se save hua", "{0} नया आइटम {1} से सेव हुआ");
        put("{0} new items saved via {1}", "{0} naye items {1} se save hue", "{0} नए आइटम {1} से सेव हुए");
        put("{0} waiting", "{0} baaki", "{0} बाक़ी");

        // Messages (toasts) and "Open with"
        put("Android has not indexed this file yet; try again in a moment.",
                "Android ne abhi ye file index nahi ki; thodi der me phir try karo.",
                "Android ने अभी यह फ़ाइल इंडेक्स नहीं की; थोड़ी देर में फिर कोशिश करें।");
        put("Download could not start", "Download shuru nahi ho paaya", "डाउनलोड शुरू नहीं हो पाया");
        put("Open Settings > Apps > Cloud-SSD > Permissions > Files", "Settings > Apps > Cloud-SSD > Permissions > Files kholo",
                "Settings > Apps > Cloud-SSD > Permissions > Files खोलें");
        put("Open Settings > Apps > Cloud-SSD", "Settings > Apps > Cloud-SSD kholo", "Settings > Apps > Cloud-SSD खोलें");
        put("Open Settings → Apps → Special app access → Usage access","Settings → Apps → Special app access → Usage access kholo",
                "Settings → Apps → Special app access → Usage access खोलें");
        put("Open Settings → Developer options → Running services", "Settings → Developer options → Running services kholo",
                "Settings → Developer options → Running services खोलें");
        put("Open Settings → Hotspot & tethering → USB tethering", "Settings → Hotspot & tethering → USB tethering kholo",
                "Settings → Hotspot & tethering → USB tethering खोलें");
        put("Open Settings → Storage", "Settings → Storage kholo", "Settings → Storage खोलें");
        put("Settings → Apps → Cloud-SSD → Full screen notifications", "Settings → Apps → Cloud-SSD → Full screen notifications kholo",
                "Settings → Apps → Cloud-SSD → Full screen notifications खोलें");
        put("Settings → Apps → Cloud-SSD → Battery → Unrestricted", "Settings → Apps → Cloud-SSD → Battery → Unrestricted chuno",
                "Settings → Apps → Cloud-SSD → Battery → Unrestricted चुनें");
        put("That file is not kept on this phone any more", "Woh file ab is phone par rakhi nahi hai", "वह फ़ाइल अब इस फ़ोन पर रखी नहीं है");
        put("The files could not be saved", "Files save nahi ho paayi", "फ़ाइलें सेव नहीं हो पाईं");
        put("The files could not be sent", "Files nahi bhej paaye", "फ़ाइलें नहीं भेज पाए");
        put("Update Cloud-SSD on the PC to open files in other apps safely.",
                "Doosre apps me files safely kholne ke liye PC par Cloud-SSD update karo.",
                "दूसरे ऐप्स में फ़ाइलें सुरक्षित खोलने के लिए पीसी पर Cloud-SSD अपडेट करें।");
        put("File not found: {0}", "File nahi mili: {0}", "फ़ाइल नहीं मिली: {0}");
        put("Cannot open file picker: {0}", "File picker nahi khula: {0}", "फ़ाइल पिकर नहीं खुला: {0}");
        put("Saved to Downloads/Cloud-SSD: {0}", "Downloads/Cloud-SSD me save hua: {0}", "Downloads/Cloud-SSD में सेव हुआ: {0}");
        put("Could not save {0}: {1}", "{0} save nahi ho paaya: {1}", "{0} सेव नहीं हो पाया: {1}");
        put("Cannot share: {0}", "Share nahi kar paaye: {0}", "शेयर नहीं कर पाए: {0}");
        put("Cannot open: {0}", "Khul nahi paaya: {0}", "खुल नहीं पाया: {0}");
        put("Share {0}", "{0} share karo", "{0} शेयर करें");
        put("Open {0} with", "{0} kis app se kholein", "{0} किस ऐप से खोलें");
        put("the file", "file", "फ़ाइल");
        put("Select files to send", "Bhejne ke liye files chuno", "भेजने के लिए फ़ाइलें चुनें");

        put("Could not sync that folder: {0}", "Woh folder sync nahi ho paaya: {0}", "वह फ़ोल्डर सिंक नहीं हो पाया: {0}");

        // App lock
        put("Unlock Cloud-SSD", "Cloud-SSD unlock karo", "Cloud-SSD अनलॉक करें");
        put("Use your fingerprint or screen lock", "Fingerprint ya screen lock use karo", "फ़िंगरप्रिंट या स्क्रीन लॉक इस्तेमाल करें");
        put("Cloud-SSD is locked", "Cloud-SSD lock hai", "Cloud-SSD लॉक है");
        put("Unlock", "Unlock karo", "अनलॉक करें");
    }

    private Tr() {
    }

    private static void put(String english, String hinglish, String hindi) {
        HINGLISH.put(english, hinglish);
        HINDI.put(english, hindi);
    }

    /** english, hinglish or hindi: what the page chose; until then like the page (Hindi on a Hindi phone, else Hinglish). */
    static String lang(Context c) {
        String saved = c.getSharedPreferences("cloudssd", Context.MODE_PRIVATE).getString("lang", "");
        if ("english".equals(saved) || "hinglish".equals(saved) || "hindi".equals(saved)) return saved;
        return "hi".equals(Locale.getDefault().getLanguage()) ? "hindi" : "hinglish";
    }

    static void setLang(Context c, String lang) {
        if (!"english".equals(lang) && !"hinglish".equals(lang) && !"hindi".equals(lang)) return;
        c.getSharedPreferences("cloudssd", Context.MODE_PRIVATE).edit().putString("lang", lang).apply();
    }

    /** The text in the language chosen, with {0}, {1}… filled in. */
    static String t(Context c, String english, Object... values) {
        String lang = lang(c);
        String s = "hindi".equals(lang) ? HINDI.get(english) : "hinglish".equals(lang) ? HINGLISH.get(english) : null;
        if (s == null) s = english;
        for (int i = 0; i < values.length; i++) s = s.replace("{" + i + "}", String.valueOf(values[i]));
        return s;
    }
}
