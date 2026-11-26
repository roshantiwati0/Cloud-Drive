using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CloudSSD.Desktop.ViewModels;

namespace CloudSSD.Desktop.Views;

/// <summary>
/// A section of sections (<see cref="HubViewModel"/>), like a phone's Settings: one row per page — icon, name, a line
/// saying what is in it, an arrow. A row opens its page; the title bar's back arrow returns here.
/// </summary>
public sealed class HubView : UserControl
{
    private readonly StackPanel _rows = new() { MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 14, 20) };

    public HubView()
    {
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _rows };
        DataContextChanged += (_, _) => Build();
        Build();
    }

    /// <summary>A row: a rounded card across the whole width, its content from the left (the app's button centres it).</summary>
    private static ControlTemplate RowTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border), "b");
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(16));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(BorderThicknessProperty));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(PaddingProperty));
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        border.AppendChild(content);
        var t = new ControlTemplate(typeof(Button)) { VisualTree = border };
        // Hover and keyboard focus: the accent border.
        foreach (var p in new[] { IsMouseOverProperty, IsKeyboardFocusedProperty })
        {
            var trigger = new Trigger { Property = p, Value = true };
            trigger.Setters.Add(new Setter(Border.BorderBrushProperty, new DynamicResourceExtension("Accent"), "b"));
            t.Triggers.Add(trigger);
        }
        return t;
    }

    private void Build()
    {
        _rows.Children.Clear();
        if (DataContext is not HubViewModel hub) return;
        var icons = Application.Current?.TryFindResource("IconFont") as FontFamily;
        foreach (var item in hub.Items)
        {
            var row = new DockPanel();
            var arrow = new TextBlock { Text = "", FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            if (icons != null) arrow.FontFamily = icons;
            arrow.SetResourceReference(TextBlock.ForegroundProperty, "Dim");
            DockPanel.SetDock(arrow, Dock.Right);
            row.Children.Add(arrow);
            var circle = new Border { Width = 42, Height = 42, CornerRadius = new CornerRadius(21), Margin = new Thickness(0, 0, 14, 0) };
            circle.SetResourceReference(Border.BackgroundProperty, "AccentSoft");
            var icon = new TextBlock { Text = item.Page.Icon, FontSize = 17, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            if (icons != null) icon.FontFamily = icons;
            icon.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
            circle.Child = icon;
            DockPanel.SetDock(circle, Dock.Left);
            row.Children.Add(circle);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var title = new TextBlock { Text = LocalizationManager.T(item.Page.Title), FontSize = 15, FontWeight = FontWeights.SemiBold };
            title.SetResourceReference(TextBlock.ForegroundProperty, "Text");
            text.Children.Add(title);
            var sub = new TextBlock { Text = LocalizationManager.T(item.Subtitle), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
            sub.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            text.Children.Add(sub);
            row.Children.Add(text);

            // The whole row is the button.
            var button = new Button
            {
                Content = row, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(16, 14, 16, 14),
                Margin = new Thickness(0, 0, 0, 10), Cursor = System.Windows.Input.Cursors.Hand,
            };
            button.SetResourceReference(BackgroundProperty, "Surface");
            button.SetResourceReference(BorderBrushProperty, "BorderBrush");
            button.BorderThickness = new Thickness(1);
            button.Template = RowTemplate();
            System.Windows.Automation.AutomationProperties.SetName(button, item.Page.Title);
            var page = item.Page;
            button.Click += (_, _) => hub.Open(page);
            _rows.Children.Add(button);
        }
    }
}
