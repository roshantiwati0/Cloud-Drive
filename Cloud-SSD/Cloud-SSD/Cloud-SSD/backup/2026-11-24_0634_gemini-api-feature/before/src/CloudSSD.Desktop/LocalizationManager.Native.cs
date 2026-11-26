namespace CloudSSD.Desktop;

/// <summary>Text of a phone's storage shown on this PC as the phone shows it (its categories, its whole storage).</summary>
public static partial class LocalizationManager
{
    public static readonly IReadOnlyDictionary<string, string> NativeTextsHinglish = new Dictionary<string, string>
    {
        ["Internal storage"] = "Internal storage",
        ["Categories"] = "Categories",
        ["Downloads"] = "Downloads",
        ["Images"] = "Tasveerein",
        ["APK and archives"] = "APK aur archives",
        ["Apps and archives"] = "Apps aur archives",
        ["Show the whole phone"] = "Poora phone dikhao",
        ["Empty"] = "Khaali",
        ["{0}: the whole storage, as the phone shows it."] = "{0}: poora storage, jaise phone me dikhta hai.",
    };

    public static readonly IReadOnlyDictionary<string, string> NativeTextsHindi = new Dictionary<string, string>
    {
        ["Internal storage"] = "इंटरनल स्टोरेज",
        ["Categories"] = "श्रेणियाँ",
        ["Downloads"] = "डाउनलोड",
        ["Images"] = "तस्वीरें",
        ["APK and archives"] = "APK और आर्काइव",
        ["Apps and archives"] = "ऐप्स और आर्काइव",
        ["Show the whole phone"] = "पूरा फ़ोन दिखाएँ",
        ["Empty"] = "ख़ाली",
        ["{0}: the whole storage, as the phone shows it."] = "{0}: पूरा स्टोरेज, जैसा फ़ोन में दिखता है।",
    };
}
