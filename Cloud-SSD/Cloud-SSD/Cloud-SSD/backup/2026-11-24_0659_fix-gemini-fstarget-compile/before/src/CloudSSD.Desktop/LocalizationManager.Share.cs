namespace CloudSSD.Desktop;

/// <summary>Text for sending files to devices: the send window and the notes, in Hinglish and Hindi.</summary>
public static partial class LocalizationManager
{
    public static readonly IReadOnlyDictionary<string, string> ShareTextsHinglish = new Dictionary<string, string>
    {
        ["Send files"] = "Files bhejo",
        ["Send {0} file(s) · {1}"] = "{0} file bhejo · {1}",
        ["and {0} more"] = "aur {0} aur",
        ["TO"] = "KISKO",
        ["No device is paired yet. Add one in Devices first."] = "Abhi koi device juda nahi hai. Pehle Devices me jodo.",
        ["cannot take files yet"] = "abhi files nahi le sakta",
        ["It shows up on the device to accept. A device that is off gets it when it is back (within a day)."] = "Device pe accept karne ke liye dikhega. Jo device band hai, use wapas aane pe milega (ek din ke andar).",
        ["OR A LINK"] = "YA EK LINK",
        ["Copy link"] = "Link copy karo",
        ["for 1 hour"] = "1 ghante ke liye",
        ["for 24 hours"] = "24 ghante ke liye",
        ["for 7 days"] = "7 din ke liye",
        ["Anyone on this Wi-Fi can download it"] = "Is Wi-Fi pe koi bhi download kar sakta hai",
        ["Send"] = "Bhejo",
        ["Tick a device first."] = "Pehle ek device pe tick karo.",
        ["Nothing could be sent."] = "Kuch bhi nahi bhej paaye.",
        ["Link copied: {0}"] = "Link copy hua: {0}",
        ["{0} links copied."] = "{0} links copy hue.",
        ["Link"] = "Link",
        ["Files delivered"] = "Files pahunch gayi",
        ["{0} received {1} file(s)."] = "{0} ne {1} file le li.",
        ["Files declined"] = "Files mana kar di",
        ["{0} did not take the files."] = "{0} ne files nahi li.",
        ["{0} sent you files"] = "{0} ne aapko files bheji",
        ["{0} files · in the From phone folder"] = "{0} files · From phone folder me",
        ["Drop files here to send them to this device"] = "Is device ko bhejne ke liye files yahan daalo",
    };

    public static readonly IReadOnlyDictionary<string, string> ShareTextsHindi = new Dictionary<string, string>
    {
        ["Send files"] = "फ़ाइलें भेजें",
        ["Send {0} file(s) · {1}"] = "{0} फ़ाइलें भेजें · {1}",
        ["and {0} more"] = "और {0} अन्य",
        ["TO"] = "किसे",
        ["No device is paired yet. Add one in Devices first."] = "अभी कोई डिवाइस जुड़ा नहीं है। पहले डिवाइस में जोड़ें।",
        ["cannot take files yet"] = "अभी फ़ाइलें नहीं ले सकता",
        ["It shows up on the device to accept. A device that is off gets it when it is back (within a day)."] = "डिवाइस पर स्वीकार करने के लिए दिखेगा। जो डिवाइस बंद है, उसे वापस आने पर मिलेगा (एक दिन के अंदर)।",
        ["OR A LINK"] = "या एक लिंक",
        ["Copy link"] = "लिंक कॉपी करें",
        ["for 1 hour"] = "1 घंटे के लिए",
        ["for 24 hours"] = "24 घंटे के लिए",
        ["for 7 days"] = "7 दिन के लिए",
        ["Anyone on this Wi-Fi can download it"] = "इस वाई-फ़ाई पर कोई भी डाउनलोड कर सकता है",
        ["Send"] = "भेजें",
        ["Tick a device first."] = "पहले एक डिवाइस पर टिक करें।",
        ["Nothing could be sent."] = "कुछ भी नहीं भेजा जा सका।",
        ["Link copied: {0}"] = "लिंक कॉपी हुआ: {0}",
        ["{0} links copied."] = "{0} लिंक कॉपी हुए।",
        ["Link"] = "लिंक",
        ["Files delivered"] = "फ़ाइलें पहुँच गईं",
        ["{0} received {1} file(s)."] = "{0} ने {1} फ़ाइलें ले लीं।",
        ["Files declined"] = "फ़ाइलें मना की गईं",
        ["{0} did not take the files."] = "{0} ने फ़ाइलें नहीं लीं।",
        ["{0} sent you files"] = "{0} ने आपको फ़ाइलें भेजीं",
        ["{0} files · in the From phone folder"] = "{0} फ़ाइलें · From phone फ़ोल्डर में",
        ["Drop files here to send them to this device"] = "इस डिवाइस को भेजने के लिए फ़ाइलें यहाँ डालें",
    };
}
