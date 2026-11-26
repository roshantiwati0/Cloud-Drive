using System.Windows;
using System.Windows.Controls;
using CloudSSD.App;

namespace CloudSSD.Desktop.Views;

/// <summary>
/// "What Redmi 12 deleted on this PC": newest first, each with where it was and when. What is still in this PC's
/// Recycle Bin has a Restore button; cloud deletes say they are in the account's trash. Opened from the device's card.
/// </summary>
public sealed class DeviceDeletesWindow : Window
{
    private readonly AppHost _host;
    private readonly string _deviceId;
    private readonly StackPanel _list = new();
    /// <summary>The line just restored, and where it is now (shown on it until the window closes).</summary>
    private (string Id, string At)? _restored;

    private static string T(string s) => LocalizationManager.T(s);
    private static string L(string f, params object[] a) => string.Format(T(f), a);

    public DeviceDeletesWindow(AppHost host, string deviceId, string deviceName)
    {
        _host = host;
        _deviceId = deviceId;
        Title = L("Deleted by {0}", deviceName);
        Width = 620;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        if (Application.Current?.TryFindResource("DialogWindow") is Style st) Style = st;

        var panel = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
        var head = new TextBlock { Text = L("What {0} deleted on this PC", deviceName), TextWrapping = TextWrapping.Wrap };
        if (Application.Current?.TryFindResource("H1") is Style h1) head.Style = h1;
        panel.Children.Add(head);
        panel.Children.Add(Caption(T("Things still in this PC's Recycle Bin can be put back from here. Files deleted on a cloud drive are in that account's trash; restore those there."),
            new Thickness(0, 0, 0, 12)));

        Fill();
        panel.Children.Add(new ScrollViewer { Content = _list, MaxHeight = 380, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

        var close = new Button { Content = T("Close"), IsCancel = true, IsDefault = true, Padding = new Thickness(16, 6, 16, 6), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        close.Click += (_, _) => Close();
        panel.Children.Add(close);
        Content = panel;
    }

    /// <summary>Draws the lines: newest first, each with what can be done now (the bins are read again each time).</summary>
    private void Fill()
    {
        _list.Children.Clear();
        var items = _host.MobileBridge.DeleteLog.ForDevice(_deviceId);
        // Each disk's bin is read once, not once per line.
        var inBin = items.Where(i => i.Bin == "recycle" && i.OriginalPath != null)
            .Select(i => RecycleBin.BinDirectoryFor(i.OriginalPath!)).Where(d => d != null).Distinct()
            .SelectMany(d => RecycleBin.Items(d!))
            .ToLookup(b => b.OriginalPath.TrimEnd('\\'), StringComparer.OrdinalIgnoreCase);
        if (items.Count == 0) _list.Children.Add(Caption(T("Nothing deleted by this device."), new Thickness(0, 4, 0, 4)));
        foreach (var item in items) _list.Children.Add(Row(item, inBin));
    }

    private UIElement Row(DeletedItem item, ILookup<string, BinItem> inBin)
    {
        var row = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
        var state = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), TextAlignment = TextAlignment.Right };
        state.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        DockPanel.SetDock(state, Dock.Right);

        bool restorable = item.Bin == "recycle" && item.RestoredAt is null && item.OriginalPath != null && inBin[item.OriginalPath.TrimEnd('\\')].Any();
        if (restorable)
        {
            var restore = new Button { Content = T("Restore"), Padding = new Thickness(10, 3, 10, 3), FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            DockPanel.SetDock(restore, Dock.Right);
            restore.Click += (_, _) =>
            {
                try
                {
                    _restored = (item.Id, _host.MobileBridge.DeleteLog.Restore(item.Id));
                    // Other lines of the same place are no longer in the bin either.
                    Fill();
                    return;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or KeyNotFoundException)
                {
                    state.Text = L("Could not restore: {0}", e.Message);
                    state.SetResourceReference(TextBlock.ForegroundProperty, "Danger");
                }
                restore.Visibility = Visibility.Collapsed;
            };
            row.Children.Add(restore);
        }
        if (_restored is { } done && done.Id == item.Id)
        {
            // The name it came back as (it differs when something new took the old one); the full place on hover.
            state.Text = L("Restored · {0}", Path.GetFileName(done.At));
            state.ToolTip = done.At;
            state.SetResourceReference(TextBlock.ForegroundProperty, "Success");
        }
        else state.Text = item.RestoredAt is { } r ? L("Restored · {0}", r.ToLocalTime().ToString("dd MMM HH:mm"))
            : item.Bin switch
            {
                "recycle" => T(restorable ? "In the Recycle Bin" : "No longer in the Recycle Bin"),
                "cloud" => T("In the cloud account's trash"),
                _ => T("Deleted for good")
            };
        // The name and place keep most of the width.
        state.MaxWidth = 240;
        state.TextWrapping = TextWrapping.Wrap;
        row.Children.Add(state);

        var text = new StackPanel();
        var name = new TextBlock { Text = (item.Folder ? "📁 " : "") + item.Name, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis };
        name.SetResourceReference(TextBlock.ForegroundProperty, "Text");
        text.Children.Add(name);
        var where = new TextBlock { Text = $"{item.Where} · {item.At.ToLocalTime():dd MMM HH:mm}", FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis };
        where.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        text.Children.Add(where);
        row.Children.Add(text);
        return row;
    }

    private static TextBlock Caption(string text, Thickness margin)
    {
        var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = margin };
        if (Application.Current?.TryFindResource("Caption") is Style c) t.Style = c;
        return t;
    }
}
