namespace CloudSSD.Desktop;

/// <summary>Text for the Devices page, the "Add a device" window and device notes, in Hinglish and Hindi.</summary>
public static partial class LocalizationManager
{
    /// <summary>English key → Hinglish, for every text the device screens use (the test checks Hindi has them all too).</summary>
    public static readonly IReadOnlyDictionary<string, string> DeviceTextsHinglish = new Dictionary<string, string>
    {
        ["Devices"] = "Devices",
        ["{0} online · {1} paired"] = "{0} online · {1} jude hue",
        ["+ Add a device"] = "+ Naya device jodo",
        ["Notify me when a device connects"] = "Device judne par notification dikhao",
        ["● Online now"] = "● Online abhi",
        ["just now"] = "abhi abhi",
        ["{0} min ago"] = "{0} min pehle",
        ["{0} h ago"] = "{0} ghante pehle",
        ["{0} days ago"] = "{0} din pehle",
        ["○ Last seen {0}"] = "○ {0}",
        ["Rename"] = "Naam badlo",
        ["Block"] = "Block karo",
        ["Unblock"] = "Unblock karo",
        ["Permissions"] = "Permissions",
        ["Remove"] = "Hatao",
        ["Save"] = "Save karo",
        ["Cancel"] = "Cancel",
        ["Browsing files"] = "Files dekh raha hai",
        ["Viewing photos"] = "Photos dekh raha hai",
        ["Upload {0}%"] = "Upload {0}%",
        ["Download {0}%"] = "Download {0}%",
        ["Uploading"] = "Upload ho raha hai",
        ["Downloading"] = "Download ho raha hai",
        ["Watching a video"] = "Video dekh raha hai",
        ["Backing up photos"] = "Photos ka backup",
        ["Playing on the phone"] = "Phone pe game khel raha hai",
        ["Looking at Space & RAM"] = "Space & RAM dekh raha hai",
        ["Gaming HUD"] = "Gaming HUD dekh raha hai",
        ["Wi-Fi"] = "Wi-Fi",
        ["LAN cable"] = "LAN cable",
        ["Hotspot"] = "Hotspot",
        ["USB cable"] = "USB cable",
        ["This PC's hotspot"] = "Is PC ka hotspot",
        ["Phone's hotspot"] = "Phone ka hotspot",
        ["USB cable (adb)"] = "USB cable (adb)",
        ["Allow"] = "Allow karo",
        ["To connect over this PC's hotspot, a phone's hotspot or a USB cable, Windows' firewall must let phones on those networks in (Windows asks once)."] = "Is PC ke hotspot, phone ke hotspot ya USB cable se judne ke liye Windows firewall ko un networks ke phones ko andar aane dena hoga (Windows ek baar poochega).",
        ["Windows treats this Wi-Fi as a public network. Pair only on networks you trust: your home, your own hotspot or a USB cable."] = "Windows is Wi-Fi ko public network maanta hai. Sirf bharose wale network pe jodo: ghar, apna hotspot ya USB cable.",
        ["Internet (Tailscale)"] = "Internet (Tailscale)",
        ["This PC"] = "Yahi PC",
        ["Phone"] = "Phone",
        ["Tablet"] = "Tablet",
        ["Laptop"] = "Laptop",
        ["PC"] = "PC",
        ["Browser"] = "Browser",
        ["Blocked"] = "Blocked",
        ["old key"] = "purani key",
        ["Paired {0}"] = "Joda: {0}",
        ["Block {0}? It stays in the list but cannot connect until you unblock it."] = "{0} ko block karein? List me rahega, par jab tak unblock nahi karoge tab tak connect nahi ho payega.",
        ["Remove {0}? It has to pair again to connect."] = "{0} ko hatana hai? Dobara jodne ke liye phir se pair karna padega.",
        ["That name cannot be used (1–60 characters)."] = "Ye naam nahi chalega (1–60 akshar).",
        ["New device paired"] = "Naya device juda",
        ["Device connected"] = "Device connect hua",
        ["{0} devices connected: {1}"] = "{0} devices connect hue: {1}",
        ["No devices yet. Press “+ Add a device” and pair a phone or laptop."] = "Abhi koi device nahi juda. “+ Naya device jodo” dabao aur phone ya laptop jodo.",
        ["Add a device"] = "Naya device jodo",
        ["Phone or tablet"] = "Phone ya tablet",
        ["Laptop or PC"] = "Laptop ya PC",
        ["Scan the QR code with the phone, or type this code in the Cloud-SSD app:"] = "Phone se QR code scan karo, ya Cloud-SSD app me ye code daalo:",
        ["On the other laptop open Cloud-SSD → Other PCs → + Add a PC, then type this PC's address and the code."] = "Doosre laptop pe Cloud-SSD kholo → Doosre PC → + PC jodo, phir is PC ka address aur code daalo.",
        ["This PC's address"] = "Is PC ka address",
        ["PAIRING CODE"] = "PAIRING CODE",
        ["The code changes after it is used once."] = "Ek baar use hone ke baad code badal jaata hai.",
        ["Pairing works only on the same Wi-Fi or hotspot. After that it also works away from home through Tailscale, if you allow it."] = "Jodna sirf same Wi-Fi ya hotspot pe hota hai. Uske baad ghar se bahar bhi Tailscale se chalega (agar aap allow karo).",
        ["{0} paired ✓"] = "{0} jud gaya ✓",
        ["Waiting for a device…"] = "Device ka intezaar…",
        ["Close"] = "Band karo",
        ["Paired devices now have their own page: who is online and what they are doing."] = "Jude hue devices ka ab alag page hai: kaun online hai aur kya kar raha hai, sab wahan.",
        ["Open Devices"] = "Devices kholo",
        ["The phone bridge is off, so no device can connect. Turn it on in Devices → Add or connect."] = "Phone bridge band hai, isliye koi device connect nahi ho sakta. Devices → Jodo ya connect karo me chalu karo.",
    };

    public static readonly IReadOnlyDictionary<string, string> DeviceTextsHindi = new Dictionary<string, string>
    {
        ["Devices"] = "डिवाइस",
        ["{0} online · {1} paired"] = "{0} ऑनलाइन · {1} जुड़े हुए",
        ["+ Add a device"] = "+ नया डिवाइस जोड़ें",
        ["Notify me when a device connects"] = "डिवाइस जुड़ने पर सूचना दिखाएँ",
        ["● Online now"] = "● अभी ऑनलाइन",
        ["just now"] = "अभी-अभी",
        ["{0} min ago"] = "{0} मिनट पहले",
        ["{0} h ago"] = "{0} घंटे पहले",
        ["{0} days ago"] = "{0} दिन पहले",
        ["○ Last seen {0}"] = "○ आख़िरी बार {0}",
        ["Rename"] = "नाम बदलें",
        ["Block"] = "ब्लॉक करें",
        ["Unblock"] = "अनब्लॉक करें",
        ["Permissions"] = "अनुमतियाँ",
        ["Remove"] = "हटाएँ",
        ["Save"] = "सहेजें",
        ["Cancel"] = "रद्द करें",
        ["Browsing files"] = "फ़ाइलें देख रहा है",
        ["Viewing photos"] = "फ़ोटो देख रहा है",
        ["Upload {0}%"] = "अपलोड {0}%",
        ["Download {0}%"] = "डाउनलोड {0}%",
        ["Uploading"] = "अपलोड हो रहा है",
        ["Downloading"] = "डाउनलोड हो रहा है",
        ["Watching a video"] = "वीडियो देख रहा है",
        ["Backing up photos"] = "फ़ोटो का बैकअप",
        ["Playing on the phone"] = "फ़ोन पर गेम खेल रहा है",
        ["Looking at Space & RAM"] = "स्पेस और RAM देख रहा है",
        ["Gaming HUD"] = "गेमिंग HUD देख रहा है",
        ["Wi-Fi"] = "वाई-फ़ाई",
        ["LAN cable"] = "LAN केबल",
        ["Hotspot"] = "हॉटस्पॉट",
        ["USB cable"] = "USB केबल",
        ["This PC's hotspot"] = "इस पीसी का हॉटस्पॉट",
        ["Phone's hotspot"] = "फ़ोन का हॉटस्पॉट",
        ["USB cable (adb)"] = "USB केबल (adb)",
        ["Allow"] = "अनुमति दें",
        ["To connect over this PC's hotspot, a phone's hotspot or a USB cable, Windows' firewall must let phones on those networks in (Windows asks once)."] = "इस पीसी के हॉटस्पॉट, फ़ोन के हॉटस्पॉट या USB केबल से जुड़ने के लिए Windows फ़ायरवॉल को उन नेटवर्क के फ़ोन अंदर आने देने होंगे (Windows एक बार पूछेगा)।",
        ["Windows treats this Wi-Fi as a public network. Pair only on networks you trust: your home, your own hotspot or a USB cable."] = "Windows इस वाई-फ़ाई को सार्वजनिक नेटवर्क मानता है। सिर्फ़ भरोसेमंद नेटवर्क पर जोड़ें: घर, अपना हॉटस्पॉट या USB केबल।",
        ["Internet (Tailscale)"] = "इंटरनेट (Tailscale)",
        ["This PC"] = "यही PC",
        ["Phone"] = "फ़ोन",
        ["Tablet"] = "टैबलेट",
        ["Laptop"] = "लैपटॉप",
        ["PC"] = "PC",
        ["Browser"] = "ब्राउज़र",
        ["Blocked"] = "ब्लॉक",
        ["old key"] = "पुरानी कुंजी",
        ["Paired {0}"] = "जोड़ा: {0}",
        ["Block {0}? It stays in the list but cannot connect until you unblock it."] = "{0} को ब्लॉक करें? यह सूची में रहेगा, पर अनब्लॉक करने तक जुड़ नहीं पाएगा।",
        ["Remove {0}? It has to pair again to connect."] = "{0} को हटाएँ? दोबारा जोड़ने के लिए फिर से पेयर करना होगा।",
        ["That name cannot be used (1–60 characters)."] = "यह नाम नहीं चलेगा (1–60 अक्षर)।",
        ["New device paired"] = "नया डिवाइस जुड़ा",
        ["Device connected"] = "डिवाइस जुड़ा",
        ["{0} devices connected: {1}"] = "{0} डिवाइस जुड़े: {1}",
        ["No devices yet. Press “+ Add a device” and pair a phone or laptop."] = "अभी कोई डिवाइस नहीं जुड़ा। “+ नया डिवाइस जोड़ें” दबाएँ और फ़ोन या लैपटॉप जोड़ें।",
        ["Add a device"] = "नया डिवाइस जोड़ें",
        ["Phone or tablet"] = "फ़ोन या टैबलेट",
        ["Laptop or PC"] = "लैपटॉप या PC",
        ["Scan the QR code with the phone, or type this code in the Cloud-SSD app:"] = "फ़ोन से QR कोड स्कैन करें, या Cloud-SSD ऐप में यह कोड डालें:",
        ["On the other laptop open Cloud-SSD → Other PCs → + Add a PC, then type this PC's address and the code."] = "दूसरे लैपटॉप पर Cloud-SSD खोलें → दूसरे PC → + PC जोड़ें, फिर इस PC का पता और कोड डालें।",
        ["This PC's address"] = "इस PC का पता",
        ["PAIRING CODE"] = "पेयरिंग कोड",
        ["The code changes after it is used once."] = "एक बार इस्तेमाल होने के बाद कोड बदल जाता है।",
        ["Pairing works only on the same Wi-Fi or hotspot. After that it also works away from home through Tailscale, if you allow it."] = "जोड़ना सिर्फ़ एक ही वाई-फ़ाई या हॉटस्पॉट पर होता है। उसके बाद घर से बाहर भी Tailscale से चलेगा (अगर आप अनुमति दें)।",
        ["{0} paired ✓"] = "{0} जुड़ गया ✓",
        ["Waiting for a device…"] = "डिवाइस का इंतज़ार…",
        ["Close"] = "बंद करें",
        ["Paired devices now have their own page: who is online and what they are doing."] = "जुड़े हुए डिवाइस का अब अलग पेज है: कौन ऑनलाइन है और क्या कर रहा है।",
        ["Open Devices"] = "डिवाइस खोलें",
        ["The phone bridge is off, so no device can connect. Turn it on in Devices → Add or connect."] = "फ़ोन ब्रिज बंद है, इसलिए कोई डिवाइस नहीं जुड़ सकता। Devices → जोड़ें या कनेक्ट करें में चालू करें।",
    };

    /// <summary>Adds the extra texts to the main dictionaries (an existing entry there wins).</summary>
    private static void MergeExtraTranslations()
    {
        Merge("Hinglish", DeviceTextsHinglish);
        Merge("हिन्दी (Hindi)", DeviceTextsHindi);
        Merge("Hinglish", RemotePcTextsHinglish);
        Merge("हिन्दी (Hindi)", RemotePcTextsHindi);
        Merge("Hinglish", PermissionTextsHinglish);
        Merge("हिन्दी (Hindi)", PermissionTextsHindi);
        Merge("Hinglish", ShareTextsHinglish);
        Merge("हिन्दी (Hindi)", ShareTextsHindi);
        Merge("Hinglish", NativeTextsHinglish);
        Merge("हिन्दी (Hindi)", NativeTextsHindi);
        Merge("Hinglish", PeerTextsHinglish);
        Merge("हिन्दी (Hindi)", PeerTextsHindi);
        Merge("Hinglish", ExtraTextsHinglish);
        Merge("हिन्दी (Hindi)", ExtraTextsHindi);
        Merge("Hinglish", AppsTextsHinglish);
        Merge("हिन्दी (Hindi)", AppsTextsHindi);
    }

    private static void Merge(string language, IReadOnlyDictionary<string, string> extra)
    {
        if (!Translations.TryGetValue(language, out var dict)) return;
        foreach (var (k, v) in extra) dict.TryAdd(k, v);
    }

    /// <summary>True when <paramref name="english"/> has a translation in <paramref name="language"/>.</summary>
    public static bool HasTranslation(string language, string english) =>
        Translations.TryGetValue(language, out var dict) && dict.ContainsKey(english.Trim());

    /// <summary>The text in <paramref name="language"/> without switching the app's language (for tests).</summary>
    public static string TranslateTo(string language, string english) =>
        Translations.TryGetValue(language, out var dict) && dict.TryGetValue(english.Trim(), out var t) ? t : english;
}
