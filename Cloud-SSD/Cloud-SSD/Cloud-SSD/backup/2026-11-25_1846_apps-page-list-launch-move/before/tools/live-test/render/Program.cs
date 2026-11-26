// Renders a page of the Cloud-SSD desktop app to a PNG with sample data, without starting the app itself
// (the installed app is running and owns the single-instance lock and the real data folder).
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CloudSSD.App;
using CloudSSD.Core;
using CloudSSD.Desktop.ViewModels;
using CloudSSD.Desktop.Views;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var outDir = args.Length > 0 ? args[0] : ".";
        Directory.CreateDirectory(outDir);
        var page = args.Length > 1 ? args[1] : "devices";
        if (page == "thumb") return ThumbProbe.Run(args[2]);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Cloud-SSD;component/Themes/" + (Environment.GetEnvironmentVariable("CSSD_THEME") ?? "Dark") + ".xaml") });
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Cloud-SSD;component/Themes/Theme.xaml") });
        app.Resources["Vis"] = new CloudSSD.Desktop.Controls.BoolToVisibility();
        app.Resources["NotVis"] = new CloudSSD.Desktop.Controls.BoolToVisibility { Invert = true };
        app.Resources["Tone"] = new CloudSSD.Desktop.Controls.ToneToBrush();

        if (page == "remote") return RemoteProbe.Run(app, outDir, args[2], args[3], (e, f, w) => { Render(e, f, w); return 0; });
        if (page == "deletes")
        {
            // Stage 5 E2: the test PC's own data (its device-deletes.json), "Deleted by <device>"; "restore" presses the first Restore.
            var tpHost = AppHost.Create(new AppPaths(args[2]));
            var dev = tpHost.Settings.MobileBridge.PairedDevices.First(d => d.Name == args[3]);
            Console.WriteLine("log: " + string.Join(" | ", tpHost.MobileBridge.DeleteLog.ForDevice(dev.Id).Select(i => $"{i.Name} {i.Bin} {i.Where} restored={i.RestoredAt != null}")));
            var dw = new DeviceDeletesWindow(tpHost, dev.Id, dev.Name) { Left = -3000 };
            dw.Show();
            RemoteProbe.Pump(() => false, 600);
            RenderWindow(dw, Path.Combine(outDir, "deletes-before.png"));
            if (args.Length > 4 && args[4] == "restore")
            {
                var button = Find<Button>(dw, b => b.Content as string is { } s && s != CloudSSD.Desktop.LocalizationManager.T("Close"));
                Console.WriteLine("pressing: " + button?.Content);
                button?.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                RemoteProbe.Pump(() => false, 600);
                RenderWindow(dw, Path.Combine(outDir, "deletes-after.png"));
                Console.WriteLine("log after: " + string.Join(" | ", tpHost.MobileBridge.DeleteLog.ForDevice(dev.Id).Select(i => $"{i.Name} restored={i.RestoredAt != null}")));
            }
            dw.Close();
            return 0;
        }
        var temp = Path.Combine(Path.GetTempPath(), "cssd_render_" + Guid.NewGuid().ToString("N"));
        var host = AppHost.Create(new AppPaths(temp));
        var now = DateTimeOffset.Now;
        var list = host.Settings.MobileBridge.PairedDevices;
        list.Add(new PairedDevice { Name = "Redmi 12", Kind = "phone", Platform = "Android 14", IpAddress = "192.168.42.129", LastLink = "usb", LastSeen = now.AddSeconds(-3), PairedAt = now.AddDays(-12), TokenHash = new string('a', 64) });
        list.Add(new PairedDevice { Name = "Dell Inspiron", Kind = "laptop", Platform = "Windows 11", IpAddress = "192.168.1.40", LastLink = "wifi", LastSeen = now.AddSeconds(-8), PairedAt = now.AddDays(-2), TokenHash = new string('b', 64) });
        list.Add(new PairedDevice { Name = "Pixel 7", Kind = "phone", Platform = "Android 15", IpAddress = "100.101.7.9", LastLink = "internet", LastSeen = now.AddHours(-2), PairedAt = now.AddDays(-30), TokenHash = new string('c', 64) });
        list.Add(new PairedDevice { Name = "Samsung Tab S9", Kind = "tablet", Platform = "Android 14", IpAddress = "192.168.137.12", LastLink = "pc-hotspot", LastSeen = now.AddDays(-3), PairedAt = now.AddDays(-40), TokenHash = new string('d', 64) });
        list.Add(new PairedDevice { Name = "Old phone", Kind = "phone", IpAddress = "10.12.5.1", LastLink = "phone-hotspot", LastSeen = now.AddDays(-9), PairedAt = now.AddDays(-90), TokenHash = new string('e', 64), IsBlocked = true });

        FrameworkElement view;
        object vm;
        if (page == "browse")
        {
            var bw = new CloudSSD.Desktop.Views.BrowsePhoneWindow(host, list[0]) { Left = -3000 };
            bw.Show();
            RemoteProbe.Pump(() => false, 22000);
            RenderWindow(bw, Path.Combine(outDir, "browse-window.png"));
            bw.Close();
            return 0;
        }
        if (page == "send")
        {
            var files = new[] { "beach-photo.png", "guide.pdf", "notes.txt" }.Select(n => Path.Combine(temp, n)).ToArray();
            Directory.CreateDirectory(temp);
            foreach (var f in files) File.WriteAllBytes(f, new byte[123_456]);
            var sw = new CloudSSD.Desktop.Views.SendToDevicesWindow(host, files) { Left = -3000 };
            sw.Show();
            RemoteProbe.Pump(() => false, 600);
            RenderWindow(sw, Path.Combine(outDir, "send-window.png"));
            sw.Close();
            return 0;
        }
        if (page == "sendfolder")
        {
            var root = Path.Combine(temp, "MyApp");
            void Put(string rel, int size) { var p = Path.Combine(root, rel); Directory.CreateDirectory(Path.GetDirectoryName(p)!); File.WriteAllBytes(p, new byte[size]); }
            Put("README.md", 1200); Put(@"src\index.js", 5000); Put(@"src\lib\index.js", 7000);
            for (int i = 0; i < 300; i++) Put($@"node_modules\pkg{i}\index.js", 40_000);
            Put(@".git\HEAD", 40); Put(@"bin\Debug\app.dll", 900_000);
            var loose = Path.Combine(temp, "notes.txt"); File.WriteAllBytes(loose, new byte[500]);
            var sw = new CloudSSD.Desktop.Views.SendToDevicesWindow(host, [root, loose]) { Left = -3000 };
            sw.Show();
            RemoteProbe.Pump(() => false, 1500);
            RenderWindow(sw, Path.Combine(outDir, "send-folder.png"));
            var box = FindAll<CheckBox>(sw).First(c => (c.Content as string ?? "").Contains("build output"));
            box.IsChecked = true; box.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            RemoteProbe.Pump(() => false, 1500);
            RenderWindow(sw, Path.Combine(outDir, "send-folder-all.png"));
            box.IsChecked = false; box.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            RemoteProbe.Pump(() => false, 1500);
            try { sw.SendNow(); } catch (InvalidOperationException) { }   // DialogResult needs ShowDialog
            Console.WriteLine("sent: " + string.Join(" | ", sw.Sent));
            foreach (var o in host.MobileBridge.Shares.SentBy("pc"))
                Console.WriteLine($"offer {o.Items.Count} items, top={string.Join(",", o.TopFolders)}, skipped={o.SkippedFiles} files {o.SkippedBytes} bytes [{string.Join(",", o.SkippedFolders)}]");
            return 0;
        }
        if (page == "perm")
        {
            var dev = list[0];
            dev.Grants = AccessControl.Defaults(host.Settings.MobileBridge, now);
            host.MobileBridge.Access.Grant(dev, [AccessCategories.Manage], AccessMode.Hour);
            host.MobileBridge.Access.Grant(dev, [AccessCategories.Control], AccessMode.Session);
            host.MobileBridge.Access.Revoke(dev, AccessCategories.Space);
            host.Settings.MobileBridge.AllowGameStream = false;
            var log = host.MobileBridge.AccessLog;
            log.Write(dev.Id, "browse", "/api/files", @"Z:\Photos\Holi 2026", true);
            log.Write(dev.Id, "stream", "/api/fs/read", "cloudssd:Photos/Holi 2026/IMG_2041.jpg", true);
            log.Write(dev.Id, "space", "/api/space/summary", null, false);
            var pw = new CloudSSD.Desktop.Views.DevicePermissionsWindow(host, dev, false, () => { }) { Left = -3000 };
            pw.Show();
            RemoteProbe.Pump(() => false, 800);
            RenderWindow(pw, Path.Combine(outDir, "perm-editor.png"));
            pw.Close();
            var (req, _, _) = host.MobileBridge.Access.Ask(dev, [AccessCategories.Space, AccessCategories.Upload], "Check free space before the trip");
            if (req is null) { var again = host.MobileBridge.Access.Ask(dev, [AccessCategories.Space], "Check free space before the trip"); req = again.Request; }
            var rw = new CloudSSD.Desktop.Views.AccessRequestWindow(host, req!) { Left = -3000 };
            rw.Show();
            RemoteProbe.Pump(() => false, 300);
            RenderWindow(rw, Path.Combine(outDir, "perm-request-0s.png"));
            RemoteProbe.Pump(() => false, 1500);
            RenderWindow(rw, Path.Combine(outDir, "perm-request.png"));
            Console.WriteLine("request state before closing: " + req!.State);
            rw.Close();
            Console.WriteLine("request state after closing unanswered: " + req.State);
            return 0;
        }
        if (page == "customize")
        {
            // The sidebar's "Customize" opens Settings on its Customize tab (language, colours, glass). Nothing is changed:
            // the theme and the language are saved in the real app folder, so this only looks.
            var mvm = new MainViewModel(host);
            var mw = new CloudSSD.Desktop.MainWindow { DataContext = mvm, Left = -3000, Top = 0, ShowInTaskbar = false };
            app.MainWindow = mw;
            mw.Show();
            RemoteProbe.Pump(() => false, 800);
            Render((FrameworkElement)mw.Content, Path.Combine(outDir, "customize-sidebar.png"), 1180);
            var btn = Find<Button>(mw, b => b.Name == "BtnCustomize");
            Console.WriteLine($"sidebar button: {btn != null}");
            btn?.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            RemoteProbe.Pump(() => false, 1200);
            var settings = mvm.Selected as SettingsViewModel;
            Console.WriteLine($"page='{mvm.Selected.Title}' tab='{settings?.ActiveTab}' content={settings?.CurrentContent?.GetType().Name}");
            var cv = Find<CustomizeView>(mw, _ => true);
            Console.WriteLine($"customize view shown: {cv != null}");
            if (cv != null)
            {
                var pills = FindAll<RadioButton>(cv).ToList();
                Console.WriteLine($"options: {string.Join(" | ", pills.Select(p => (p.IsChecked == true ? "*" : "") + p.Content))}");
                Console.WriteLine($"glass: {FindAll<CheckBox>(cv).FirstOrDefault()?.Content}");
            }
            Render((FrameworkElement)mw.Content, Path.Combine(outDir, "customize-page.png"), 1180);
            mw.Close();
            return 0;
        }
        if (page == "bell")
        {
            // The bell (top right): a few notes, the list opened, then clicks on a request and on a page note.
            var mvm = new MainViewModel(host);
            var mw = new CloudSSD.Desktop.MainWindow { DataContext = mvm, Left = -3000, Top = 0, ShowInTaskbar = false };
            app.MainWindow = mw;
            mw.Show();
            var dev = list[0];
            dev.Grants = AccessControl.Defaults(host.Settings.MobileBridge, now);
            host.MobileBridge.Access.Revoke(dev, AccessCategories.Space);
            var (req, _, _) = host.MobileBridge.Access.Ask(dev, [AccessCategories.Space], "Check free space");
            CloudSSD.Desktop.NotificationCenter.Clear();
            CloudSSD.Desktop.NotificationCenter.Add("Files delivered", "Dell Inspiron received 2 file(s).", "page:Devices", "files");
            CloudSSD.Desktop.NotificationCenter.Add("RAM auto mode", "Moved 3 idle app(s) out of RAM. Free RAM went from 2.1 GB to 3.4 GB.", "page:PC Memory (RAM)", "auto");
            CloudSSD.Desktop.NotificationCenter.Add("Redmi 12 asks for access", "See Space & RAM", CloudSSD.Desktop.DeviceNotifications.AccessTarget(req!), "access");
            RemoteProbe.Pump(() => false, 600);
            mvm.NotificationsOpen = true;
            RemoteProbe.Pump(() => false, 800);
            var popup = Find<System.Windows.Controls.Primitives.Popup>(mw, p => p.Name == "BellPopup");
            Console.WriteLine($"unread badge: {mvm.UnreadText} open: {popup?.IsOpen}");
            if (popup?.Child is FrameworkElement pc) Render(pc, Path.Combine(outDir, "bell-list.png"), 380);
            Render((FrameworkElement)mw.Content, Path.Combine(outDir, "bell-window.png"), 1180);
            // Click the request: its window opens (it waits for an answer).
            mvm.OpenNotificationCommand.Execute(CloudSSD.Desktop.NotificationCenter.Items[0]);
            RemoteProbe.Pump(() => false, 800);
            var reqWin = app.Windows.OfType<AccessRequestWindow>().FirstOrDefault();
            Console.WriteLine($"after the request click: window={reqWin != null}, list open={mvm.NotificationsOpen}, unread={mvm.UnreadCount}");
            if (reqWin != null) { RenderWindow(reqWin, Path.Combine(outDir, "bell-request.png")); reqWin.Close(); }
            // Click the delivery note: the Devices page.
            mvm.OpenNotificationCommand.Execute(CloudSSD.Desktop.NotificationCenter.Items.First(i => i.Target == "page:Devices"));
            RemoteProbe.Pump(() => false, 400);
            Console.WriteLine($"after the page click: page='{mvm.Selected.Title}', unread={mvm.UnreadCount}");
            mw.Close();
            return 0;
        }
        if (page == "hub")
        {
            // The PC's transfer history in groups by kind of file, then only Documents.
            foreach (var (n, dir) in new[] { ("beach.png", "upload"), ("trip.mp4", "upload"), ("guide.pdf", "download"), ("IMG_2041.jpg", "upload"),
                         ("song.m4a", "stream"), ("game.apk", "download"), ("notes.txt", "upload"), ("1b63.bin", "upload") })
                host.MobileBridge.RecordTransfer(n, null, 123_456, dir, "Redmi 12", "192.168.1.5", 4.2);
            var hub = new MobileHubViewModel(host, _ => { });
            var hv = new MobileHubView { DataContext = hub };
            var hshell = new Border { Background = (Brush)app.Resources["Bg"], Padding = new Thickness(24), Child = hv, Width = 1000 };
            var hw = new Window { Content = hshell, Left = -3000, Top = 0, Width = 1050, Height = 1400, ShowInTaskbar = false };
            hw.Show();
            var tab = Find<RadioButton>(hv, r => r.Name == "TabHistory");
            if (tab != null) tab.IsChecked = true;
            RemoteProbe.Pump(() => false, 800);
            Console.WriteLine("groups: " + string.Join(" | ", hub.TransferGroups.Select(g => $"{g.Category} {g.Count}")));
            Render(hshell, Path.Combine(outDir, "hub-history.png"), 1000);
            hub.SetCategoryCommand.Execute("Documents");
            RemoteProbe.Pump(() => false, 400);
            Console.WriteLine("documents: " + string.Join(" | ", hub.TransferGroups.Select(g => $"{g.Category}: {string.Join(",", g.Items.Select(i => i.FileName))}")));
            hw.Close();
            return 0;
        }
        if (page == "guest")
        {
            // Stage 5 E3: "Add a guest" with the code made (1 day, Browse + Play ticked by default).
            var gw = new AddGuestWindow(host) { Left = -3000 };
            gw.Show();
            RemoteProbe.Pump(() => false, 400);
            RenderWindow(gw, Path.Combine(outDir, "guest-before.png"));
            var make = Find<Button>(gw, b => b.IsDefault);
            make?.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            RemoteProbe.Pump(() => false, 1200);
            RenderWindow(gw, Path.Combine(outDir, "guest-code.png"));
            Console.WriteLine("invites: " + string.Join(",", host.MobileBridge.GuestInvites.Select(i => i.Code + " " + i.For + " " + string.Join("+", i.Categories))));
            gw.Close();
            Console.WriteLine("after close: " + host.MobileBridge.GuestInvites.Count);
            return 0;
        }
        if (page == "add")
        {
            var w = new AddDeviceWindow(host);
            w.Show();
            RemoteProbe.Pump(() => false, 8000);   // the firewall and network checks run in the background
            Render((FrameworkElement)w.Content, Path.Combine(outDir, "add-device.png"), 720);
            w.Close();
            return 0;
        }
        // Phase 9: the phone's battery and storage, as its peer long-poll would have reported them.
        var vitals = (System.Collections.Concurrent.ConcurrentDictionary<string, DeviceVitals>)typeof(LocalMobileBridgeService)
            .GetField("_vitals", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(host.MobileBridge)!;
        vitals[list[0].Id] = new DeviceVitals(94, true, 70_135_898_112, 108_334_231_552, now);
        // Redmi's app is waiting for questions (Browse / Ring / Send clipboard show); Pixel 7 is away (they hide).
        _ = host.MobileBridge.Peer.WaitForJobAsync(list[0].Id, TimeSpan.FromMinutes(1), CancellationToken.None);
        // Stage 5 E3: Pixel 7 is a guest for 3 more hours.
        list[2].GuestUntil = now.AddHours(3);
        // Stage 5 E2: Redmi deleted two things (the card offers "Deleted (2)").
        host.MobileBridge.DeleteLog.Add(list[0].Id, "Redmi 12", "beach.png", "From phone › Trip", false, "recycle", Path.Combine(temp, "beach.png"));
        host.MobileBridge.DeleteLog.Add(list[0].Id, "Redmi 12", "Old", "Test Cloud", true, "cloud", null);
        vm = new DevicesViewModel(host);
        view = new DevicesView { DataContext = vm };
        var shell = new Border { Background = (Brush)app.Resources["Bg"], Padding = new Thickness(24), Child = view, Width = 1000, Height = 640 };
        Render(shell, Path.Combine(outDir, "devices.png"), 1000);
        return 0;
    }

    private static T? Find<T>(DependencyObject root, Func<T, bool> match) where T : DependencyObject
    {
        if (root is T t && match(t)) return t;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (Find(VisualTreeHelper.GetChild(root, i), match) is { } hit) return hit;
        return null;
    }

    /// <summary>A shown window's content as it is laid out (with the page background behind it).</summary>
    private static IEnumerable<T> FindAll<T>(DependencyObject d) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
        {
            var c = VisualTreeHelper.GetChild(d, i);
            if (c is T t) yield return t;
            foreach (var x in FindAll<T>(c)) yield return x;
        }
    }

    private static void RenderWindow(Window w, string file)
    {
        var c = (FrameworkElement)VisualTreeHelper.GetChild(w, 0);
        int width = (int)Math.Ceiling(c.ActualWidth), height = (int)Math.Ceiling(c.ActualHeight);
        var bmp = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle((Brush)Application.Current.Resources["Bg"], null, new Rect(0, 0, width, height));
            dc.DrawRectangle(new VisualBrush(c) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null, new Rect(0, 0, width, height));
        }
        bmp.Render(dv);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(file);
        enc.Save(fs);
        Console.WriteLine($"{file} {width}x{height}");
    }

    private static void Render(FrameworkElement element, string file, double width)
    {
        // As inside MainWindow / a DialogWindow: text takes the window's colour.
        if (element is not Window && element.ReadLocalValue(System.Windows.Documents.TextElement.ForegroundProperty) == DependencyProperty.UnsetValue)
            element.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Text");
        element.Measure(new Size(width, double.PositiveInfinity));
        element.Arrange(new Rect(element.DesiredSize));
        element.UpdateLayout();
        // Let bindings and item containers settle.
        Application.Current.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        element.UpdateLayout();
        var w = (int)Math.Ceiling(element.ActualWidth);
        var h = (int)Math.Ceiling(element.ActualHeight);
        var bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(element);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(file);
        enc.Save(fs);
        Console.WriteLine($"{file} {w}x{h}");
    }
}
