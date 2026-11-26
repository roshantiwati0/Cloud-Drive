using CloudSSD.App;
using CloudSSD.Desktop;
using CloudSSD.Desktop.ViewModels;
using Xunit;

namespace CloudSSD.Tests;

/// <summary>Texts of the Devices page: activity lines, "x min ago", notes, and Hinglish/Hindi for every new text.</summary>
[Collection("Language")]   // never at the same time as a test that switches the app's language
public class DevicesPageTests
{
    private const string Hinglish = "Hinglish";
    private const string Hindi = "हिन्दी (Hindi)";

    [Fact]
    public void Every_device_text_has_Hinglish_and_Hindi()
    {
        foreach (var key in LocalizationManager.DeviceTextsHinglish.Keys)
        {
            Assert.True(LocalizationManager.HasTranslation(Hinglish, key), "Hinglish missing: " + key);
            Assert.True(LocalizationManager.HasTranslation(Hindi, key), "Hindi missing: " + key);
        }
        Assert.Equal(LocalizationManager.DeviceTextsHinglish.Keys.OrderBy(k => k), LocalizationManager.DeviceTextsHindi.Keys.OrderBy(k => k));
        foreach (var key in LocalizationManager.RemotePcTextsHinglish.Keys)
        {
            Assert.True(LocalizationManager.HasTranslation(Hinglish, key), "Hinglish missing: " + key);
            Assert.True(LocalizationManager.HasTranslation(Hindi, key), "Hindi missing: " + key);
        }
        Assert.Equal(LocalizationManager.RemotePcTextsHinglish.Keys.OrderBy(k => k), LocalizationManager.RemotePcTextsHindi.Keys.OrderBy(k => k));
        foreach (var key in LocalizationManager.PermissionTextsHinglish.Keys)
        {
            Assert.True(LocalizationManager.HasTranslation(Hinglish, key), "Hinglish missing: " + key);
            Assert.True(LocalizationManager.HasTranslation(Hindi, key), "Hindi missing: " + key);
        }
        Assert.Equal(LocalizationManager.PermissionTextsHinglish.Keys.OrderBy(k => k), LocalizationManager.PermissionTextsHindi.Keys.OrderBy(k => k));
        foreach (var key in LocalizationManager.ShareTextsHinglish.Keys)
        {
            Assert.True(LocalizationManager.HasTranslation(Hinglish, key), "Hinglish missing: " + key);
            Assert.True(LocalizationManager.HasTranslation(Hindi, key), "Hindi missing: " + key);
        }
        Assert.Equal(LocalizationManager.ShareTextsHinglish.Keys.OrderBy(k => k), LocalizationManager.ShareTextsHindi.Keys.OrderBy(k => k));
        foreach (var key in LocalizationManager.PeerTextsHinglish.Keys)
        {
            Assert.True(LocalizationManager.HasTranslation(Hinglish, key), "Hinglish missing: " + key);
            Assert.True(LocalizationManager.HasTranslation(Hindi, key), "Hindi missing: " + key);
        }
        Assert.Equal(LocalizationManager.PeerTextsHinglish.Keys.OrderBy(k => k), LocalizationManager.PeerTextsHindi.Keys.OrderBy(k => k));
        foreach (var key in LocalizationManager.ExtraTextsHinglish.Keys)
        {
            Assert.True(LocalizationManager.HasTranslation(Hinglish, key), "Hinglish missing: " + key);
            Assert.True(LocalizationManager.HasTranslation(Hindi, key), "Hindi missing: " + key);
        }
        Assert.Equal(LocalizationManager.ExtraTextsHinglish.Keys.OrderBy(k => k), LocalizationManager.ExtraTextsHindi.Keys.OrderBy(k => k));
        // Every category the PC can name has words in both.
        foreach (var c in CloudSSD.App.AccessCategories.All) Assert.True(LocalizationManager.HasTranslation(Hinglish, CloudSSD.App.AccessCategories.Describe(c)), c);
        Assert.Equal("Doosre PC", LocalizationManager.TranslateTo(Hinglish, "Other PCs"));
        Assert.Equal("+ Naya device jodo", LocalizationManager.TranslateTo(Hinglish, "+ Add a device"));
        Assert.Equal("{0} ghante pehle", LocalizationManager.TranslateTo(Hinglish, "{0} h ago"));
    }

    [Fact]
    public void Pages_and_list_items_shown_later_are_translated_too() => OnSta(() =>
    {
        // Found live: on the PC the Devices page and its cards kept "Rename", "Ring"… in English, because a page is
        // made when it is selected (after the window's pass) and a card can appear later still.
        var language = typeof(LocalizationManager).GetProperty(nameof(LocalizationManager.CurrentLanguage))!;
        var before = LocalizationManager.CurrentLanguage;
        language.SetValue(null, Hinglish);   // in memory only: the user's language.txt is not touched
        try
        {
            var cards = new System.Collections.ObjectModel.ObservableCollection<string> { "a" };
            var label = new System.Windows.FrameworkElementFactory(typeof(System.Windows.Controls.TextBlock));
            label.SetValue(System.Windows.Controls.TextBlock.TextProperty, "Rename");
            var list = new System.Windows.Controls.ItemsControl { ItemsSource = cards, ItemTemplate = new System.Windows.DataTemplate { VisualTree = label } };
            var panel = new System.Windows.Controls.StackPanel();
            panel.Children.Add(new System.Windows.Controls.TextBlock { Text = "Rename" });
            panel.Children.Add(list);
            var w = new System.Windows.Window { Left = -4000, Top = 0, Width = 300, Height = 300, ShowInTaskbar = false, WindowStyle = System.Windows.WindowStyle.None };
            w.Show();
            Pump();
            // The page, selected after the window showed: MainViewModel.OnSelectedChanged translates "soon".
            w.Content = new System.Windows.Controls.UserControl { Content = panel };
            LocalizationManager.TranslateSoon(w);
            Pump();
            cards.Add("b");                                                            // a card that comes later
            Pump();
            var texts = TextsIn(w);
            w.Close();
            Assert.False(texts.Contains("Rename"), "on screen: " + string.Join(" | ", texts));
            Assert.Equal(3, texts.Count(t => t == LocalizationManager.TranslateTo(Hinglish, "Rename")));
        }
        finally { language.SetValue(null, before); }
    });

    private static void OnSta(Action body)
    {
        Exception? error = null;
        var t = new Thread(() => { try { body(); } catch (Exception e) { error = e; } });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }

    /// <summary>Lets the window lay out and raise Loaded for what was just added.</summary>
    private static void Pump()
    {
        for (int i = 0; i < 3; i++)
        {
            var frame = new System.Windows.Threading.DispatcherFrame();
            System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, () => frame.Continue = false);
            System.Windows.Threading.Dispatcher.PushFrame(frame);
        }
    }

    private static List<string> TextsIn(System.Windows.DependencyObject root)
    {
        var found = new List<string>();
        if (root is System.Windows.Controls.TextBlock tb) found.Add(tb.Text);
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
            found.AddRange(TextsIn(System.Windows.Media.VisualTreeHelper.GetChild(root, i)));
        return found;
    }

    [Fact]
    public void Format_keys_keep_their_placeholders()
    {
        foreach (var (key, value) in LocalizationManager.DeviceTextsHinglish.Concat(LocalizationManager.DeviceTextsHindi)
                     .Concat(LocalizationManager.RemotePcTextsHinglish).Concat(LocalizationManager.RemotePcTextsHindi)
                     .Concat(LocalizationManager.PermissionTextsHinglish).Concat(LocalizationManager.PermissionTextsHindi)
                     .Concat(LocalizationManager.ShareTextsHinglish).Concat(LocalizationManager.ShareTextsHindi)
                     .Concat(LocalizationManager.PeerTextsHinglish).Concat(LocalizationManager.PeerTextsHindi)
                     .Concat(LocalizationManager.ExtraTextsHinglish).Concat(LocalizationManager.ExtraTextsHindi))
            for (int i = 0; i < 3; i++)
                Assert.Equal(key.Contains("{" + i + "}"), value.Contains("{" + i + "}"));
    }

    [Theory]
    [InlineData(20, "just now")]
    [InlineData(5 * 60, "5 min ago")]
    [InlineData(3 * 3600, "3 h ago")]
    [InlineData(2 * 86400, "2 days ago")]
    public void Ago_reads_naturally(int seconds, string english)
    {
        var now = DateTimeOffset.Now;
        var text = Fmt.Ago(now.AddSeconds(-seconds), now);
        // Whatever language the app is in, it is the translation of the expected English text.
        var lang = LocalizationManager.CurrentLanguage;
        var number = System.Text.RegularExpressions.Regex.Match(english, @"^\d+").Value;
        var key = number.Length > 0 ? english.Replace(number, "{0}") : english;
        var expected = lang == "English" ? english : string.Format(LocalizationManager.TranslateTo(lang, key), number);
        Assert.Equal(expected, text);
    }

    [Fact]
    public void Activity_lines_show_file_and_percent()
    {
        var now = DateTimeOffset.Now;
        var up = DeviceText.Activity(new DeviceActivity(DeviceActivityKind.Upload, "clip.mp4", 45, now));
        Assert.Contains("45", up);
        Assert.Contains("clip.mp4", up);
        Assert.Equal("", DeviceText.Activity(null));
        Assert.NotEqual("", DeviceText.Activity(new DeviceActivity(DeviceActivityKind.Browsing, null, null, now)));
    }

    [Fact]
    public void Several_devices_at_once_make_one_note()
    {
        DeviceSnapshot Snap(string id, string name) => new(id, name, DeviceKind.Phone, null, null, true, DateTimeOffset.Now, DateTimeOffset.Now,
            "wifi", "192.168.1.9", null, false, false, null);
        var one = DeviceNotifications.Compose([new DeviceConnectedEvent(Snap("a", "Redmi 12"), false)]);
        Assert.Contains("Redmi 12", one.Text);

        var many = DeviceNotifications.Compose([
            new DeviceConnectedEvent(Snap("a", "Redmi 12"), false),
            new DeviceConnectedEvent(Snap("b", "Dell"), true),
            new DeviceConnectedEvent(Snap("a", "Redmi 12"), false)]);
        Assert.Contains("2", many.Text);
        Assert.Contains("Redmi 12", many.Text);
        Assert.Contains("Dell", many.Text);
        Assert.DoesNotContain("192.168.1.9", many.Text);
    }
}
