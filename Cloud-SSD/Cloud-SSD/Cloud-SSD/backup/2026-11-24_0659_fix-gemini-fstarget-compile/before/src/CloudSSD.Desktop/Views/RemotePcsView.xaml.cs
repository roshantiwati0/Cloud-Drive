using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CloudSSD.Desktop.ViewModels;

namespace CloudSSD.Desktop.Views;

public partial class RemotePcsView : UserControl
{
    public RemotePcsView()
    {
        InitializeComponent();
        // A context menu and a tooltip are not part of the page's tree, so the page-wide translation never reaches them.
        List.ContextMenu.Opened += (_, _) =>
        {
            foreach (var mi in List.ContextMenu.Items.OfType<MenuItem>())
            {
                mi.Tag ??= mi.Header;
                mi.Header = LocalizationManager.T((string)mi.Tag);
            }
            if (Vm is { } vm) KeepMenu.Header = vm.KeepOfflineText(Selected);
        };
        UpButton.ToolTipOpening += (_, _) => UpButton.ToolTip = LocalizationManager.T("Up");
    }

    private RemotePcsViewModel? Vm => DataContext as RemotePcsViewModel;
    private RemoteEntryItem? Selected => List.SelectedItem as RemoteEntryItem;

    private void OnOpen(object sender, MouseButtonEventArgs e) { if (Selected is { } s) Vm?.OpenEntryCommand.Execute(s); }
    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Selected is { } s) Vm?.OpenEntryCommand.Execute(s);
        else if (e.Key == Key.Back) Vm?.UpCommand.Execute(null);
        else if (e.Key == Key.Delete && Selected is { } d) Vm?.DeleteCommand.Execute(d);
        else if (e.Key == Key.F2 && Selected is { } r) Vm?.RenameCommand.Execute(r);
    }
    private void OnOpenMenu(object sender, RoutedEventArgs e) { if (Selected is { } s) Vm?.OpenEntryCommand.Execute(s); }
    private void OnDownloadMenu(object sender, RoutedEventArgs e) { if (Selected is { } s) Vm?.DownloadCommand.Execute(s); }
    private void OnKeepMenu(object sender, RoutedEventArgs e) { if (Selected is { } s) Vm?.KeepOfflineCommand.Execute(s); }
    private void OnRenameMenu(object sender, RoutedEventArgs e) { if (Selected is { } s) Vm?.RenameCommand.Execute(s); }
    private void OnDeleteMenu(object sender, RoutedEventArgs e) { if (Selected is { } s) Vm?.DeleteCommand.Execute(s); }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>Files dropped here go to the open folder on that PC (now, or when it is back).</summary>
    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0) Vm?.EnqueueDrop(files);
    }
}
