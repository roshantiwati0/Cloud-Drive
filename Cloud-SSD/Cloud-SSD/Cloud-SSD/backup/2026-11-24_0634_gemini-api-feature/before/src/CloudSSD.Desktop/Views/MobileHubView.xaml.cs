using System.Windows;
using System.Windows.Controls;
using CloudSSD.Desktop.ViewModels;

namespace CloudSSD.Desktop.Views;

public partial class MobileHubView : UserControl
{
    public MobileHubView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (Main is not { } main) return;
            main.HubTabRequested -= ApplyTab;
            main.HubTabRequested += ApplyTab;
            ApplyTab();
        };
        Unloaded += (_, _) => { if (Main is { } main) main.HubTabRequested -= ApplyTab; };
    }

    private static MainViewModel? Main => Application.Current?.MainWindow?.DataContext as MainViewModel;

    /// <summary>A part asked for from elsewhere (a notification, the Home page): that tab is shown.</summary>
    private void ApplyTab()
    {
        if (Main is not { HubTab: { } tab } main) return;
        var chip = tab switch { "Devices" => TabDevices, "Connect" => TabConnect, "Permissions" => TabPermissions, "When off" => TabWhenOff, "Other PCs" => TabOther, _ => null };
        if (chip != null) chip.IsChecked = true;
        main.HubTab = null;
    }

    /// <summary>"Other PCs" was chosen: its list is read (as when a page is opened).</summary>
    private void OnOtherPcsShown(object sender, RoutedEventArgs e) => Main?.OtherPcs.OnShown();

    /// <summary>The Devices list sits inside this page's scroller: the wheel over it scrolls the page.</summary>
    private void OnInnerWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        if (e.Handled) return;
        e.Handled = true;
        var args = new System.Windows.Input.MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = MouseWheelEvent, Source = sender };
        ((sender as FrameworkElement)?.Parent as UIElement)?.RaiseEvent(args);
    }
}
