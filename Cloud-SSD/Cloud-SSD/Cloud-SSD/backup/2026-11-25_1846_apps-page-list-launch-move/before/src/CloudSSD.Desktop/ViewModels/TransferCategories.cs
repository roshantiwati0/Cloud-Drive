using System.Collections.ObjectModel;

namespace CloudSSD.Desktop.ViewModels;

/// <summary>
/// The kind of file a transfer was, for grouping the history: photos, videos, music, documents, apps and archives,
/// others — in this order, as the phone app groups it too.
/// </summary>
public static class TransferCategories
{
    public const string Photos = "Photos", Videos = "Videos", Music = "Music", Documents = "Documents", Apps = "Apps & archives", Others = "Others";

    /// <summary>The groups in the order they are shown.</summary>
    public static readonly IReadOnlyList<string> Order = [Photos, Videos, Music, Documents, Apps, Others];

    private static readonly Dictionary<string, string> ByExtension = Build();

    private static Dictionary<string, string> Build()
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void Add(string cat, params string[] exts) { foreach (var e in exts) d[e] = cat; }
        Add(Photos, "jpg", "jpeg", "png", "gif", "webp", "heic", "heif", "bmp", "tif", "tiff", "raw", "dng", "svg");
        Add(Videos, "mp4", "mkv", "mov", "avi", "webm", "m4v", "3gp", "wmv", "flv", "ts");
        Add(Music, "mp3", "m4a", "aac", "flac", "wav", "ogg", "opus", "wma", "amr");
        Add(Documents, "pdf", "doc", "docx", "xls", "xlsx", "ppt", "pptx", "txt", "md", "csv", "rtf", "odt", "ods", "odp", "epub", "json", "xml", "html", "htm");
        Add(Apps, "apk", "apks", "xapk", "exe", "msi", "zip", "rar", "7z", "tar", "gz", "bz2", "xz", "iso");
        return d;
    }

    /// <summary>"beach.PNG" → Photos; no or unknown extension → Others.</summary>
    public static string Of(string? fileName)
    {
        var name = fileName ?? "";
        int dot = name.LastIndexOf('.');
        if (dot < 0 || dot == name.Length - 1) return Others;
        return ByExtension.TryGetValue(name[(dot + 1)..], out var cat) ? cat : Others;
    }

    /// <summary>Segoe MDL2 glyph for a group's header.</summary>
    public static string Icon(string category) => category switch
    {
        Photos => "",
        Videos => "",
        Music => "",
        Documents => "",
        Apps => "",
        _ => ""
    };
}

/// <summary>One group of the history list: "Photos · 12" and its transfers, newest first.</summary>
public sealed class TransferGroup
{
    public TransferGroup(string category, IEnumerable<TransferRecordViewModel> items)
    {
        Category = category;
        Items = new ObservableCollection<TransferRecordViewModel>(items);
    }

    public string Category { get; }
    public string Title => LocalizationManager.T(Category);
    public string Icon => TransferCategories.Icon(Category);
    public int Count => Items.Count;
    public ObservableCollection<TransferRecordViewModel> Items { get; }
}
