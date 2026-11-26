using CloudSSD.App;

namespace CloudSSD.Desktop.ViewModels;

/// <summary>One row of a hub page: the page it opens and a line saying what is in it.</summary>
public sealed record HubItem(PageViewModel Page, string Subtitle);

/// <summary>
/// A section that holds other sections, like a phone's Settings: a list of rows; a row opens its page (which can be a
/// hub again), and the back arrow in the title bar returns here. Keeps the sidebar short: what is used less sits
/// one tap deeper.
/// </summary>
public sealed class HubViewModel : PageViewModel
{
    private readonly Action<PageViewModel> _open;

    public HubViewModel(AppHost host, string title, string icon, Action<PageViewModel> open, params HubItem[] items) : base(host, title, icon)
    {
        _open = open;
        Items = items;
        foreach (var i in items) i.Page.Parent = this;
    }

    public IReadOnlyList<HubItem> Items { get; }

    public void Open(PageViewModel page) => _open(page);
}

/// <summary>
/// "Files": a paired phone's storage inside the app, like a phone in File Explorer over USB — its folders, its files
/// by kind (photos, videos, audio, documents, apps, archives), both ways — and beside it the Cloud SSD's drives.
/// </summary>
public sealed class PhoneFilesPageViewModel : PageViewModel
{
    public PhoneFilesPageViewModel(AppHost host) : base(host, "Files", "") { }

    /// <summary>The device to show first (set by "Browse files" on a device's card).</summary>
    public string? WantedDeviceId { get; set; }
}
