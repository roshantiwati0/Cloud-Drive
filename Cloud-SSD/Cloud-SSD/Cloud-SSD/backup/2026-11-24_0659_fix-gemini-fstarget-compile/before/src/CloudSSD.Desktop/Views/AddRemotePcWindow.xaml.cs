using System.Windows;
using System.Windows.Controls;
using CloudSSD.App;
using CloudSSD.App.Remote;

namespace CloudSSD.Desktop.Views;

/// <summary>Pairs this PC (a laptop) with another one: found on the network or typed, plus its 6-digit code.</summary>
public partial class AddRemotePcWindow : Window
{
    private readonly AppHost _host;
    private CancellationTokenSource? _scan;

    private sealed record FoundRow(FoundPc Pc, string Label);

    public AddRemotePcWindow(AppHost host)
    {
        _host = host;
        InitializeComponent();
        Loaded += (_, _) => Scan(this, new RoutedEventArgs());
        Closed += (_, _) => _scan?.Cancel();
    }

    private async void Scan(object sender, RoutedEventArgs e)
    {
        _scan?.Cancel();
        _scan = new CancellationTokenSource();
        var ct = _scan.Token;
        ScanButton.IsEnabled = false;
        ScanStatus.Text = LocalizationManager.T("Looking for Cloud-SSD PCs on this network…");
        try
        {
            var found = await _host.RemotePcs.DiscoverAsync(8090, null, ct);
            var saved = _host.RemotePcs.Store.Pcs.Select(p => p.PcId).ToHashSet();
            Found.ItemsSource = found.Select(f => new FoundRow(f, $"{f.PcName}   ·   {f.Url.Replace("http://", "")}" + (saved.Contains(f.PcId) ? "   ·   " + LocalizationManager.T("already added") : ""))).ToList();
            ScanStatus.Text = found.Count == 0 ? LocalizationManager.T("No PC found. Type its address below (shown on that PC under Devices → Add a device).") : "";
        }
        catch (OperationCanceledException) { }
        finally { if (!ct.IsCancellationRequested) ScanButton.IsEnabled = true; }
    }

    private void FoundChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Found.SelectedItem is FoundRow r) { Address.Text = r.Pc.Url.Replace("http://", ""); Code.Focus(); }
    }

    private async void Connect(object sender, RoutedEventArgs e)
    {
        Status.Text = "";
        var code = new string(Code.Text.Where(char.IsAsciiDigit).ToArray());
        if (Address.Text.Trim().Length == 0) { Status.Text = LocalizationManager.T("Choose a PC from the list or type its address."); return; }
        if (code.Length != 6) { Status.Text = LocalizationManager.T("Type the 6-digit code shown on that PC."); return; }
        ConnectButton.IsEnabled = false;
        try
        {
            await _host.RemotePcs.AddPcAsync(Address.Text, code);
            DialogResult = true;
        }
        catch (RemotePcException ex)
        {
            Status.Text = ex.Kind switch
            {
                RemoteErrorKind.WrongCode => LocalizationManager.T("Wrong code. Check the 6 digits shown on that PC (they change after each use)."),
                RemoteErrorKind.Locked => string.Format(LocalizationManager.T("Too many wrong codes. Try again in {0} s with the new code on that PC."), ex.RetryAfterSeconds),
                RemoteErrorKind.Offline => LocalizationManager.T("PC not found. Are both on the same Wi-Fi, with Cloud-SSD open on that PC?"),
                RemoteErrorKind.Self => LocalizationManager.T("That is this PC itself."),
                _ => ex.Message
            };
        }
        finally { ConnectButton.IsEnabled = true; }
    }
}
