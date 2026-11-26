using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CloudSSD.Desktop.Views;

/// <summary>A one-line text question ("Name of the new folder"); null when cancelled.</summary>
public sealed class PromptWindow : Window
{
    private readonly TextBox _box;

    private PromptWindow(string title, string label, string value)
    {
        Title = title;
        Width = 420;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        if (Application.Current?.TryFindResource("DialogWindow") is Style st) Style = st;
        var panel = new StackPanel { Margin = new Thickness(22, 18, 22, 18) };
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 8) });
        _box = new TextBox { Text = value, MaxLength = 200 };
        panel.Children.Add(_box);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var ok = new Button { Content = LocalizationManager.T("Save"), IsDefault = true, Padding = new Thickness(16, 6, 16, 6) };
        if (Application.Current?.TryFindResource("Primary") is Style p) ok.Style = p;
        ok.Click += (_, _) => { DialogResult = true; };
        var cancel = new Button { Content = LocalizationManager.T("Cancel"), IsCancel = true, Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(0) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        Content = panel;
        Loaded += (_, _) => { _box.Focus(); _box.SelectAll(); };
    }

    public static string? Ask(string title, string label, string value)
    {
        var w = new PromptWindow(title, label, value) { Owner = Application.Current?.MainWindow };
        return w.ShowDialog() == true ? w._box.Text : null;
    }
}
