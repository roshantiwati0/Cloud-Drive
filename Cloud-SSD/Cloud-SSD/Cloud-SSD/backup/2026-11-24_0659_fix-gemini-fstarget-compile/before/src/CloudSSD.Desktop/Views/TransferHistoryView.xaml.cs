using System.Windows;
using System.Windows.Controls;

namespace CloudSSD.Desktop.Views;

/// <summary>The full transfer history (Files → Transfers → History); "← Transfers" goes back to what is moving now.</summary>
public partial class TransferHistoryView : UserControl
{
    public TransferHistoryView() => InitializeComponent();

    /// <summary>"← Transfers" was pressed.</summary>
    public event Action? Back;

    private void OnBack(object sender, RoutedEventArgs e) => Back?.Invoke();
}
