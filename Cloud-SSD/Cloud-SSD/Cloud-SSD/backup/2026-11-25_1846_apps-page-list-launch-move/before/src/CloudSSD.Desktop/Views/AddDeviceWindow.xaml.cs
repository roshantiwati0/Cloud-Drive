using System.Windows;
using System.Windows.Threading;
using CloudSSD.App;

namespace CloudSSD.Desktop.Views;

/// <summary>
/// "+ Add a device": the QR code for a phone, this PC's address for a laptop, and the six digits for both. Shows the new
/// code as soon as one is used up, and says when a device paired.
/// </summary>
public partial class AddDeviceWindow : Window
{
    private readonly AppHost _host;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(5) };
    private string _shownSecret = "";
    private string _shownCode = "";

    public AddDeviceWindow(AppHost host)
    {
        _host = host;
        InitializeComponent();
        Refresh();
        _timer.Tick += (_, _) => Refresh();   // the QR link is renewed every ten minutes
        _timer.Start();
        host.MobileBridge.StatusChanged += OnStatusChanged;
        host.MobileBridge.DeviceConnected += OnDeviceConnected;
        Loaded += async (_, _) => await CheckNetworkAsync();
        Closed += (_, _) =>
        {
            _timer.Stop();
            host.MobileBridge.StatusChanged -= OnStatusChanged;
            host.MobileBridge.DeviceConnected -= OnDeviceConnected;
        };
    }

    private void OnStatusChanged() => Dispatcher.BeginInvoke(Refresh);

    /// <summary>Asked in the background (Windows takes a moment): is the hotspot/cable rule there, is this network public?</summary>
    private async Task CheckNetworkAsync()
    {
        var (ruleThere, isPublic) = await Task.Run(() => (DirectLinkSetup.FirewallRuleExists(), DirectLinkSetup.OnPublicNetwork()));
        if (!IsLoaded) return;
        DirectBox.Visibility = ruleThere ? Visibility.Collapsed : Visibility.Visible;
        PublicNote.Visibility = isPublic ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void AllowDirect(object sender, RoutedEventArgs e)
    {
        DirectButton.IsEnabled = false;
        var port = _host.MobileBridge.Port;
        if (await Task.Run(() => DirectLinkSetup.AddFirewallRule(port))) DirectBox.Visibility = Visibility.Collapsed;
        else DirectButton.IsEnabled = true;
    }

    private void OnDeviceConnected(DeviceConnectedEvent e)
    {
        if (!e.JustPaired) return;
        Dispatcher.BeginInvoke(async () =>
        {
            Status.Text = string.Format(LocalizationManager.T("{0} paired ✓"), e.Device.Name);
            Status.Foreground = (System.Windows.Media.Brush)FindResource("Success");
            await Task.Delay(2000);
            if (IsLoaded) Close();
        });
    }

    private void Refresh()
    {
        var bridge = _host.MobileBridge;
        Address.Text = $"{bridge.LocalIpAddress}:{bridge.Port}";
        var code = bridge.PairingCode;
        if (code != _shownCode)
        {
            _shownCode = code;
            Code.Text = code.Length == 6 ? $"{code[..3]} {code[3..]}" : code;
        }
        var secret = bridge.PairingLinkSecret;
        if (secret != _shownSecret)
        {
            _shownSecret = secret;
            try { Qr.Source = QrCodeGenerator.GenerateQrBitmap(bridge.PairingLinkUrl, 8); }
            catch { Qr.Source = null; }
        }
    }
}
