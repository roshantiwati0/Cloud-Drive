using System.Windows;
using System.Windows.Controls;

namespace CloudSSD.Desktop.Views;

/// <summary>
/// Settings → Customize: everything about how the app looks, in one place — the language, the colours (Ocean, Clean,
/// Midnight, or as Windows has it) and the glass effect. A change applies at once and is remembered.
/// </summary>
public sealed class CustomizeView : UserControl
{
    private readonly Dictionary<string, RadioButton> _languages = new();
    private readonly Dictionary<string, RadioButton> _themes = new();
    private readonly CheckBox _glass;
    private bool _syncing;

    private static string T(string s) => LocalizationManager.T(s);

    public CustomizeView()
    {
        var page = new StackPanel { Margin = new Thickness(0, 0, 0, 20) };

        // Language.
        var langs = new WrapPanel();
        foreach (var lang in LocalizationManager.SupportedLanguages)
        {
            var b = Pill(lang, "cssd-language");
            b.Checked += (_, _) => { if (!_syncing && lang != LocalizationManager.CurrentLanguage) LocalizationManager.SetLanguage(lang); };
            _languages[lang] = b;
            langs.Children.Add(b);
        }
        page.Children.Add(Card("\uE774", "Language", "The app's words. Names and file names stay as they are.", langs));

        // Colours.
        var themes = new WrapPanel();
        foreach (var o in ThemeManager.Options)
        {
            var b = Pill(o.Label, "cssd-theme");
            b.ToolTip = T(ThemeHint(o.Key));
            b.Checked += (_, _) => { if (!_syncing && o.Key != ThemeManager.Choice) ThemeManager.Apply(o.Key); };
            _themes[o.Key] = b;
            themes.Children.Add(b);
        }
        page.Children.Add(Card("\uE790", "Colours", "Ocean and Clean are light, Midnight is dark; \"Windows\" follows the Windows setting.", themes));

        // Glass.
        // Checked/Unchecked, not Click: switching it by keyboard or UI Automation raises no Click.
        _glass = new CheckBox { Content = "Glass effect", Margin = new Thickness(0, 2, 0, 0) };
        RoutedEventHandler glass = (_, _) => { if (!_syncing && (_glass.IsChecked == true) != ThemeManager.Glass) ThemeManager.SetGlass(_glass.IsChecked == true); };
        _glass.Checked += glass;
        _glass.Unchecked += glass;
        if (!ThemeManager.GlassSupported) _glass.ToolTip = "Needs Windows 11 (22H2 or later)";
        page.Children.Add(Card("\uE727", "Window", ThemeManager.GlassSupported
            ? "A see-through, frosted window background."
            : "A see-through, frosted window background. Needs Windows 11 (22H2 or later).", _glass));

        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = page };
        Sync();

        // Changed somewhere else (another window, or the language changing the texts): show it here too.
        // The words are kept in English here and shown in the app's language (again when it changes).
        Action sync = () => Dispatcher.BeginInvoke(() => { Sync(); LocalizationManager.TranslateTree(this); });
        Loaded += (_, _) => { ThemeManager.ThemeChanged += sync; LocalizationManager.LanguageChanged += sync; Sync(); LocalizationManager.TranslateTree(this); };
        Unloaded += (_, _) => { ThemeManager.ThemeChanged -= sync; LocalizationManager.LanguageChanged -= sync; };
    }

    private static string ThemeHint(string key) => key switch
    {
        ThemeManager.Ocean => "Light, with blue",
        ThemeManager.Clean => "Light and plain",
        ThemeManager.Midnight => "Dark",
        _ => "Light or dark, as Windows is set",
    };

    /// <summary>Ticks what is in use now.</summary>
    private void Sync()
    {
        _syncing = true;
        try
        {
            foreach (var (lang, b) in _languages) b.IsChecked = lang == LocalizationManager.CurrentLanguage;
            foreach (var (key, b) in _themes) b.IsChecked = key == ThemeManager.Choice;
            _glass.IsChecked = ThemeManager.Glass;
            _glass.IsEnabled = ThemeManager.GlassSupported;
        }
        finally { _syncing = false; }
    }

    private static RadioButton Pill(string text, string group)
    {
        var b = new RadioButton
        {
            Content = text, GroupName = group, Height = 34, MinWidth = 96, Padding = new Thickness(14, 0, 14, 0), Margin = new Thickness(0, 0, 8, 8),
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center
        };
        if (Application.Current?.TryFindResource("PillRadio") is Style s) b.Style = s;
        return b;
    }

    private static Border Card(string icon, string title, string caption, UIElement body)
    {
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        var glyph = new TextBlock { Text = icon, FontSize = 16, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        if (Application.Current?.TryFindResource("IconFont") is System.Windows.Media.FontFamily f) glyph.FontFamily = f;
        glyph.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
        head.Children.Add(glyph);
        var h = new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center };
        if (Application.Current?.TryFindResource("H2") is Style h2) h.Style = h2;
        head.Children.Add(h);
        var cap = new TextBlock { Text = caption, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        if (Application.Current?.TryFindResource("Caption") is Style c) cap.Style = c;
        var panel = new StackPanel();
        panel.Children.Add(head);
        panel.Children.Add(cap);
        panel.Children.Add(body);
        var card = new Border { Child = panel, Margin = new Thickness(0, 0, 0, 14) };
        if (Application.Current?.TryFindResource("Card") is Style cs) card.Style = cs;
        return card;
    }
}
