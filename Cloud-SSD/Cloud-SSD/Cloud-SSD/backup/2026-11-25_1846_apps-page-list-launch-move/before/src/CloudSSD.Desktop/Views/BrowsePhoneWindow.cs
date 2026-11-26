using System.Windows;
using System.Windows.Controls;
using CloudSSD.App;
using CloudSSD.Core;

namespace CloudSSD.Desktop.Views;

/// <summary>
/// A phone's files in a window of their own (<see cref="PhoneFilesView"/>): used for another PC's phone, looked at
/// through that PC. A phone paired with this PC opens in the app's "Phone files" page instead.
/// </summary>
public sealed class BrowsePhoneWindow : Window
{
    /// <summary>A phone paired with this PC.</summary>
    public BrowsePhoneWindow(AppHost host, PairedDevice device) : this(device.Name, new PhoneFilesView(host, device)) { }

    /// <summary>Any phone, asked through <paramref name="ask"/> (another PC's phone, through that PC).</summary>
    public BrowsePhoneWindow(string name, Func<string, Dictionary<string, string>, TimeSpan, Task<PeerResult>> ask)
        : this(name, new PhoneFilesView(name, ask)) { }

    private BrowsePhoneWindow(string name, PhoneFilesView view)
    {
        Title = string.Format(LocalizationManager.T("{0} · Phone files"), name);
        Width = 980;
        Height = 660;
        MinWidth = 700;
        MinHeight = 440;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        if (Application.Current?.TryFindResource("DialogWindow") is Style st) Style = st;
        SizeToContent = SizeToContent.Manual;
        Content = new Border { Padding = new Thickness(22, 16, 22, 16), Child = view };
    }
}
