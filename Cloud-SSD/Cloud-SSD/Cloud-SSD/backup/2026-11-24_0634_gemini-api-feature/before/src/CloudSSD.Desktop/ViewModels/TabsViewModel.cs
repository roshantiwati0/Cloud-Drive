using System.ComponentModel;
using CloudSSD.App;
using CloudSSD.Core;

namespace CloudSSD.Desktop.ViewModels;

/// <summary>One tab of a <see cref="TabsViewModel"/>: its name on the chip and the page it shows.</summary>
public sealed record TabEntry(string Label, PageViewModel Page);

/// <summary>
/// A section made of a few pages side by side, like the Files page: chips at the top, the chosen page below
/// ("PC care": Storage, RAM, Space and clean-up). What the chosen page says (busy, its message) shows as the
/// section's own, so the title bar and the message strip work as for any page.
/// </summary>
public sealed class TabsViewModel : PageViewModel
{
    private PageViewModel _current;

    public TabsViewModel(AppHost host, string title, string icon, params TabEntry[] tabs) : base(host, title, icon)
    {
        Tabs = tabs;
        _current = tabs[0].Page;
        _current.PropertyChanged += OnPageChanged;
    }

    public IReadOnlyList<TabEntry> Tabs { get; }

    /// <summary>The page of the chosen tab.</summary>
    public PageViewModel Current
    {
        get => _current;
        private set
        {
            if (ReferenceEquals(_current, value)) return;
            _current.PropertyChanged -= OnPageChanged;
            _current = value;
            _current.PropertyChanged += OnPageChanged;
            OnPropertyChanged();
            Mirror();
            _current.OnShown();
        }
    }

    /// <summary>Chooses the tab that shows <paramref name="page"/> (a chip, or a notification that leads to it).</summary>
    public void Select(PageViewModel page)
    {
        if (Tabs.Any(t => ReferenceEquals(t.Page, page))) Current = page;
    }

    public override void OnShown()
    {
        Mirror();
        _current.OnShown();
    }

    public override void OnMetrics(EngineMetrics m) => _current.OnMetrics(m);
    public override void OnStateChanged() => _current.OnStateChanged();

    private void OnPageChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Busy) or nameof(Message) or nameof(MessageTone)) Mirror();
    }

    private void Mirror()
    {
        Busy = _current.Busy;
        Message = _current.Message;
        MessageTone = _current.MessageTone;
    }
}

/// <summary>
/// Settings → Folders: where files from phones are saved on this PC, and moving them on to the cloud drive. The
/// values and commands are the phone section's (<see cref="MobileHubViewModel"/>); this only gives them their place.
/// </summary>
public sealed class PhoneFoldersViewModel
{
    public PhoneFoldersViewModel(MobileHubViewModel hub) => Hub = hub;
    public MobileHubViewModel Hub { get; }
}
