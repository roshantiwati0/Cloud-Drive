using System.Windows;
using System.Windows.Controls;
using CloudSSD.Desktop.ViewModels;

namespace CloudSSD.Desktop.Views;

public partial class DevicesView : UserControl
{
    public DevicesView() => InitializeComponent();

    private void OnCardDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>Files dropped on a device's card: the send window opens with that device ticked.</summary>
    private void OnCardDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return;
        if ((sender as FrameworkElement)?.DataContext is not DeviceCardViewModel card || DataContext is not DevicesViewModel page) return;
        e.Handled = true;
        new SendToDevicesWindow(page.Host, files, card.Id) { Owner = Window.GetWindow(this) }.ShowDialog();
    }
}
