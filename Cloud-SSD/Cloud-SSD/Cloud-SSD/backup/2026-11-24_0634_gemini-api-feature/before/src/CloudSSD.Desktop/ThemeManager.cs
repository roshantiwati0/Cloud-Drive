using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using CloudSSD.Desktop.Controls;
using Microsoft.Win32;

namespace CloudSSD.Desktop;

/// <summary>
/// The desktop app's look, like the phone app's: Ocean (light, a slim blue title bar), Clean (light, no bar), Midnight
/// (dark navy with teal) or like Windows (Ocean by day, Midnight when Windows is dark). Plus an optional glass effect:
/// Windows 11's frosted backdrop behind every window.
///
/// The palette is the first merged dictionary of the application resources (Themes/Light.xaml for Ocean and Clean,
/// Themes/Dark.xaml for Midnight); right after it comes a small dictionary of what differs per look and for the glass.
/// Views use these brushes as DynamicResource, so a switch recolours every open window at once. The choices are kept
/// in %LOCALAPPDATA%\Cloud-SSD\theme.txt and glass.txt.
/// </summary>
public static class ThemeManager
{
    public const string Ocean = "Ocean";
    public const string Clean = "Clean";
    public const string Midnight = "Midnight";
    public const string System = "System";
    // The names before the looks (theme.txt may still hold them): Light was Ocean, Dark was Midnight.
    public const string Light = "Light";
    public const string Dark = "Dark";

    /// <summary>Choices shown in the sidebar, in order.</summary>
    public static IReadOnlyList<ThemeOption> Options { get; } =
    [
        new(Ocean, "Ocean"),
        new(Clean, "Clean"),
        new(Midnight, "Midnight"),
        new(System, "Windows jaisa (System)"),
    ];

    /// <summary>What the user chose: Ocean, Clean, Midnight or System.</summary>
    public static string Choice { get; private set; } = Ocean;

    /// <summary>The look actually on screen (System resolved to Ocean or Midnight).</summary>
    public static string Look { get; private set; } = Ocean;

    /// <summary>The theme on screen is dark (Midnight).</summary>
    public static bool IsDark { get; private set; } = false;

    /// <summary>The glass effect is on (and Windows can show it).</summary>
    public static bool Glass { get; private set; }

    /// <summary>Windows 11 22H2 or later: the system backdrop (Mica / Acrylic) behind a window.</summary>
    public static bool GlassSupported { get; } = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621);

    public static event Action? ThemeChanged;

    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cloud-SSD");
    private static readonly string ConfigPath = Path.Combine(Folder, "theme.txt");
    private static readonly string GlassPath = Path.Combine(Folder, "glass.txt");

    private static bool _initialized;

    /// <summary>Loads the saved choices and applies them. Call once at start-up, before the first window is shown.</summary>
    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        try
        {
            if (File.Exists(ConfigPath)) Choice = Normalize(File.ReadAllText(ConfigPath).Trim());
            if (File.Exists(GlassPath)) Glass = GlassSupported && File.ReadAllText(GlassPath).Trim() == "on";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }

        // Every window gets a title bar and backdrop that match the look when it opens.
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((s, _) => { if (s is Window w) ApplyToWindow(w); }));
        // "Windows jaisa" follows the Windows setting while the app is running.
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category == UserPreferenceCategory.General && Choice == System)
                Application.Current?.Dispatcher.BeginInvoke(() => Apply(System, save: false));
        };
        Apply(Choice, save: false);
    }

    private static string Normalize(string choice) => choice switch
    {
        Light => Ocean,
        Dark => Midnight,
        _ when Options.Any(o => o.Key == choice) => choice,
        _ => Ocean,
    };

    /// <summary>Switches to <paramref name="choice"/> and, by default, remembers it.</summary>
    public static void Apply(string choice, bool save = true)
    {
        choice = Normalize(choice);
        Choice = choice;
        Look = choice == System ? (WindowsUsesLightApps() ? Ocean : Midnight) : choice;
        IsDark = Look == Midnight;
        var app = Application.Current;
        if (app is null) return;

        var merged = app.Resources.MergedDictionaries;
        var palette = new ResourceDictionary { Source = new Uri(IsDark ? "Themes/Dark.xaml" : "Themes/Light.xaml", UriKind.Relative) };
        // Take out the old palette and look dictionaries, put the new ones first.
        for (int i = merged.Count - 1; i >= 0; i--)
        {
            var src = merged[i].Source?.OriginalString ?? "";
            if (src.EndsWith("Dark.xaml", StringComparison.OrdinalIgnoreCase) || src.EndsWith("Light.xaml", StringComparison.OrdinalIgnoreCase)
                || merged[i].Contains(LookMarker))
                merged.RemoveAt(i);
        }
        merged.Insert(0, palette);
        merged.Insert(1, LookDictionary(palette));

        foreach (Window w in app.Windows)
        {
            ApplyToWindow(w);
            RefreshConvertedBrushes(w);
        }

        if (save) Save(ConfigPath, choice);
        ThemeChanged?.Invoke();
    }

    /// <summary>Turns the glass effect on or off and remembers it.</summary>
    public static void SetGlass(bool on)
    {
        Glass = on && GlassSupported;
        Save(GlassPath, Glass ? "on" : "off");
        Apply(Choice, save: false);
    }

    private const string LookMarker = "CloudSSD.LookDictionary";

    /// <summary>What differs per look (the title bar) and for the glass effect (see-through page and side menu).</summary>
    private static ResourceDictionary LookDictionary(ResourceDictionary palette)
    {
        var d = new ResourceDictionary { [LookMarker] = true };
        Color C(string key) => palette[key] is Color c ? c : Colors.Transparent;
        SolidColorBrush B(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
        Color A(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);

        if (Look == Clean)
        {
            // No coloured bar: the page title sits on the page, in the text colour.
            d["BandBrush"] = B(Colors.Transparent);
            d["BandText"] = B(C("TextColor"));
            d["BandTextMuted"] = B(C("BorderColor"));
            d["BandHover"] = B(C("Surface3Color"));
        }
        if (Glass)
        {
            // The page and the side menu let the frosted backdrop through; cards stay solid so text reads well.
            d["Bg"] = B(A(C("BgColor"), IsDark ? (byte)0x99 : (byte)0x8C));
            d["SidebarBg"] = B(A(C("SurfaceColor"), IsDark ? (byte)0xB3 : (byte)0xA6));
            if (Look == Ocean) d["BandBrush"] = B(A(C("AccentColor"), 0xE6));
        }
        return d;
    }

    private static void Save(string path, string text)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(path, text);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Windows "Choose your app mode": HKCU\...\Themes\Personalize\AppsUseLightTheme (1 = light).</summary>
    private static bool WindowsUsesLightApps()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v != 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or global::System.Security.SecurityException)
        {
            return true;
        }
    }

    /// <summary>
    /// Brushes handed out by converters (status colours) and drawn by charts are picked once; re-run those bindings
    /// and redraw the charts so they take the new palette too.
    /// </summary>
    private static void RefreshConvertedBrushes(DependencyObject root)
    {
        var stack = new Stack<DependencyObject>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var d = stack.Pop();
            var local = d.GetLocalValueEnumerator();
            var refresh = new List<BindingExpression>();
            while (local.MoveNext())
            {
                if (BindingOperations.GetBindingExpression(d, local.Current.Property) is { ParentBinding.Converter: ToneToBrush } be)
                    refresh.Add(be);
            }
            foreach (var be in refresh) be.UpdateTarget();
            if (d is Sparkline s) s.InvalidateVisual();

            if (d is Visual or global::System.Windows.Media.Media3D.Visual3D)
            {
                int n = VisualTreeHelper.GetChildrenCount(d);
                for (int i = 0; i < n; i++) stack.Push(VisualTreeHelper.GetChild(d, i));
            }
        }
    }

    // ---- title bar and backdrop -------------------------------------------------------------------------------------

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins { public int Left, Right, Top, Bottom; }

    private const int DwmUseImmersiveDarkMode = 20;       // Windows 10 20H1 and later
    private const int DwmUseImmersiveDarkModeOld = 19;    // Windows 10 1809-1909
    private const int DwmSystemBackdropType = 38;         // Windows 11 22H2 and later
    private const int BackdropNone = 1, BackdropAcrylic = 3;

    private static void ApplyToWindow(Window w)
    {
        var hwnd = new WindowInteropHelper(w).Handle;
        if (hwnd == IntPtr.Zero) return;
        int on = IsDark ? 1 : 0;
        if (DwmSetWindowAttribute(hwnd, DwmUseImmersiveDarkMode, ref on, sizeof(int)) != 0)
            DwmSetWindowAttribute(hwnd, DwmUseImmersiveDarkModeOld, ref on, sizeof(int));

        // Layered windows (toasts, see-through popups) draw their own shape: no backdrop for them.
        if (!GlassSupported || w.AllowsTransparency) return;
        var source = HwndSource.FromHwnd(hwnd);
        if (source?.CompositionTarget is null) return;
        if (Glass)
        {
            // WPF draws with alpha into the frame, which DWM fills with the frosted backdrop.
            source.CompositionTarget.BackgroundColor = Colors.Transparent;
            var m = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            DwmExtendFrameIntoClientArea(hwnd, ref m);
            int type = BackdropAcrylic;
            DwmSetWindowAttribute(hwnd, DwmSystemBackdropType, ref type, sizeof(int));
        }
        else
        {
            int type = BackdropNone;
            DwmSetWindowAttribute(hwnd, DwmSystemBackdropType, ref type, sizeof(int));
            var m = new Margins();
            DwmExtendFrameIntoClientArea(hwnd, ref m);
            source.CompositionTarget.BackgroundColor = SystemColors.WindowColor;
        }
    }
}

public sealed record ThemeOption(string Key, string Label)
{
    public override string ToString() => Label;
}
