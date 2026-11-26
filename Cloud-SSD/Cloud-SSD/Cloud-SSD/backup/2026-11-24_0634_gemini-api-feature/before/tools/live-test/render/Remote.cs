// Live test of the "Other PCs" page against the throw-away test PC bridge (scratchpad\testpc, port 8091).
//   remote <out> <testpcBinDir> step1  : pair (if needed), render drives + a folder, send a file, stop the bridge,
//                                        render the offline banner + saved lists.
//   remote <out> <testpcBinDir> step2  : (bridge running again, with a file added) render the folder: NEW badge;
//                                        queue a file while away is checked in step1.
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CloudSSD.App;
using CloudSSD.Core;
using CloudSSD.Desktop.ViewModels;
using CloudSSD.Desktop.Views;

internal static class RemoteProbe
{
    private static readonly string Data = Path.Combine(Path.GetTempPath(), "cssd_render_remote");

    public static int Run(Application app, string outDir, string testpcBin, string step, Func<FrameworkElement, string, double, int> render)
    {
        Directory.CreateDirectory(Data);
        // As in the app: async work started here comes back to the UI thread.
        System.Threading.SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var host = AppHost.Create(new AppPaths(Data));
        var mgr = host.RemotePcs;
        if (mgr.Store.Pcs.Count == 0)
        {
            var code = File.ReadAllText(Path.Combine(testpcBin, "code.txt")).Split(' ')[0];
            var t = mgr.AddPcAsync("127.0.0.1:8091", code);
            Pump(() => t.IsCompleted, 15000);
            if (t.IsFaulted) { Console.WriteLine("pair failed: " + t.Exception!.GetBaseException().Message); return 2; }
            Console.WriteLine("paired: " + t.Result.Pc.DisplayName);
        }
        if (step == "pin")
        {
            var pvm = new RemotePcsViewModel(host);
            var pview = new RemotePcsView { DataContext = pvm };
            var pshell = new Border { Background = (Brush)app.Resources["Bg"], Padding = new Thickness(24), Child = pview, Width = 1100, Height = 620 };
            var pwin = new Window { Content = pshell, Width = 1120, Height = 680, WindowStyle = WindowStyle.None, ShowInTaskbar = false, Left = -3000, Top = 0 };
            pwin.Show();
            pvm.OnShown();
            Pump(() => pvm.Entries.Count > 0 && !pvm.Busy, 15000);
            pvm.OpenEntryCommand.Execute(pvm.Entries.First(e => e.Name.Contains("Received")));
            Pump(() => pvm.Entries.Any(e => e.Name == "Trip") && !pvm.Busy, 15000);
            var session = host.RemotePcs.Active!;
            pvm.KeepOfflineCommand.Execute(pvm.Entries.Single(e => e.Name == "Trip"));
            var sync = session.SyncPinsAsync();
            Pump(() => sync.IsCompleted, 30000);
            Pump(() => false, 800);
            Dump(pvm, "kept Trip");
            foreach (var pin in session.Pins) Console.WriteLine($"    pin: {pin.Path} files={pin.Files} bytes={pin.Bytes} synced={pin.LastSyncAt:HH:mm:ss} err={pin.Error}");
            render(pshell, Path.Combine(outDir, "remote-pin-kept.png"), 1100);
            pvm.OpenEntryCommand.Execute(pvm.Entries.Single(e => e.Name == "Trip"));
            Pump(() => pvm.Entries.Any(e => e.Name == "notes.txt") && !pvm.Busy, 15000);
            Dump(pvm, "inside Trip");
            render(pshell, Path.Combine(outDir, "remote-pin-trip.png"), 1100);
            File.WriteAllText(Path.Combine(testpcBin, "stop.txt"), "stop");
            Pump(() => !pvm.IsOnline && pvm.ShowBanner, 40000);
            var notes = pvm.Entries.Single(e => e.Name == "notes.txt").Entry!;
            var open = session.OpenAsync(notes);
            Pump(() => open.IsCompleted, 5000);
            Console.WriteLine("offline open: " + (open.IsCompletedSuccessfully ? open.Result + " -> " + File.ReadAllText(open.Result).Trim() : open.Exception?.GetBaseException().Message));
            Dump(pvm, "offline Trip");
            render(pshell, Path.Combine(outDir, "remote-pin-offline.png"), 1100);
            pwin.Close();
            return 0;
        }
        if (step == "inbox" || step == "access")
        {
            var ivm = new RemotePcsViewModel(host);
            var iview = new RemotePcsView { DataContext = ivm };
            var ishell = new Border { Background = (Brush)app.Resources["Bg"], Padding = new Thickness(24), Child = iview, Width = 1100, Height = 620 };
            var iwin = new Window { Content = ishell, Width = 1120, Height = 680, WindowStyle = WindowStyle.None, ShowInTaskbar = false, Left = -3000, Top = 0 };
            iwin.Show();
            ivm.OnShown();
            var session = host.RemotePcs.Active!;
            if (step == "inbox")
            {
                session.ReceivedFolder = Path.Combine(Data, "received-test");
                Pump(() => !ivm.Busy && ivm.IsOnline, 15000);
                var chk = session.CheckInboxAsync();
                Pump(() => chk.IsCompleted && ivm.HasInbox, 15000);
                Console.WriteLine("inbox: " + string.Join(" | ", ivm.Inbox.Select(i => $"{i.Title} / {i.Names} / canAnswer={i.CanAnswer}")));
                render(ishell, Path.Combine(outDir, "remote-inbox.png"), 1100);
                foreach (var item in ivm.Inbox.ToList())
                {
                    ivm.AcceptOfferCommand.Execute(item);
                    Pump(() => !ivm.AcceptOfferCommand.IsRunning, 30000);
                    Console.WriteLine("accepted: " + ivm.Message);
                }
                Pump(() => false, 800);
                Console.WriteLine("inbox after: " + ivm.Inbox.Count);
                if (Directory.Exists(session.ReceivedFolder))
                    foreach (var f in Directory.GetFiles(session.ReceivedFolder, "*", SearchOption.AllDirectories))
                    {
                        string zone = "";
                        try { zone = File.ReadAllText(f + ":Zone.Identifier").Replace("\r\n", " ").Trim(); } catch (Exception) { zone = "(no mark)"; }
                        Console.WriteLine($"  got {Path.GetRelativePath(session.ReceivedFolder, f)} {new FileInfo(f).Length} B  {zone}");
                    }
            }
            else
            {
                Pump(() => ivm.ShowAccessAsk || (!ivm.Busy && ivm.Entries.Count > 0), 15000);
                Console.WriteLine($"access bar: {ivm.ShowAccessAsk} '{ivm.AccessText}' canAsk={ivm.CanAskAccess} entries={ivm.Entries.Count}");
                render(ishell, Path.Combine(outDir, "remote-access.png"), 1100);
                if (ivm.ShowAccessAsk)
                {
                    ivm.AskAccessCommand.Execute(null);
                    Pump(() => ivm.AccessText.Contains("Waiting") || ivm.AccessText.Contains("intezaar"), 8000);
                    Console.WriteLine("after asking: " + ivm.AccessText);
                    File.WriteAllText(Path.Combine(testpcBin, "cmd.txt"), "decide always");
                    Pump(() => !ivm.ShowAccessAsk && ivm.Entries.Count > 0, 30000);
                    Console.WriteLine($"after the PC said yes: bar={ivm.ShowAccessAsk} entries={string.Join(", ", ivm.Entries.Select(e => e.Name))} msg='{ivm.Message}'");
                    render(ishell, Path.Combine(outDir, "remote-access-allowed.png"), 1100);
                }
            }
            iwin.Close();
            return 0;
        }
        if (step == "relay")
        {
            // This laptop looks at the test PC's phone through it.
            var rs = host.RemotePcs.Active!;
            var tick = rs.TickAsync(); Pump(() => tick.IsCompleted, 15000);
            var ph = rs.PhonesAsync(); Pump(() => ph.IsCompleted, 15000);
            Console.WriteLine("phones: " + (ph.IsCompletedSuccessfully ? string.Join(" | ", ph.Result.Select(p => $"{p.Name} listening={p.Listening}")) : ph.Exception?.GetBaseException().Message));
            var phone = ph.IsCompletedSuccessfully ? ph.Result.FirstOrDefault(p => p.Listening) : null;
            if (phone is null) return 1;
            for (int round = 0; round < 3; round++)
            {
                var a = rs.AskPhoneAsync(phone.Id, "fs.roots", new Dictionary<string, string>()); Pump(() => a.IsCompleted, 40000);
                Console.WriteLine($"roots try {round}: {a.Result.Status} {(a.Result.Json is { } j ? j.GetRawText()[..Math.Min(160, j.GetRawText().Length)] : a.Result.Error)}");
                if (a.Result.Ok) break;
                Pump(() => false, 8000);   // the phone's owner answers its note meanwhile
            }
            var l = rs.AskPhoneAsync(phone.Id, "fs.list", new Dictionary<string, string> { ["path"] = "Download/Cloud-SSD" }); Pump(() => l.IsCompleted, 40000);
            Console.WriteLine($"list: {l.Result.Status} {(l.Result.Json is { } lj ? lj.GetRawText()[..Math.Min(300, lj.GetRawText().Length)] : l.Result.Error)}");
            var r = rs.AskPhoneAsync(phone.Id, "fs.read", new Dictionary<string, string> { ["path"] = "Download/Cloud-SSD/beach-photo.png" }); Pump(() => r.IsCompleted, 60000);
            Console.WriteLine($"read: {r.Result.Status} {r.Result.Bytes} B {r.Result.FilePath} {r.Result.Error}");
            if (r.Result.FilePath is { } got && File.Exists(got)) { Console.WriteLine("read sha256: " + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(got)))); File.Delete(got); }
            return 0;
        }
        if (step == "pairback")
        {
            // This "laptop" runs its own bridge (port 8092), pairs with the test PC again, and so gives it a key back.
            host.Settings.MobileBridge.Port = 8092;
            host.MobileBridge.Start();
            Pump(() => host.MobileBridge.IsRunning, 5000);
            Console.WriteLine($"own bridge running: {host.MobileBridge.IsRunning} on {host.MobileBridge.Port}, own pcId {host.Settings.MobileBridge.PcId}");
            foreach (var old in mgr.Store.Pcs.ToList()) { var f = mgr.ForgetAsync(old.PcId); Pump(() => f.IsCompleted, 10000); }
            var code2 = File.ReadAllText(Path.Combine(testpcBin, "code.txt")).Split(' ')[0];
            var add = mgr.AddPcAsync("127.0.0.1:8091", code2); Pump(() => add.IsCompleted, 20000);
            if (add.IsFaulted) { Console.WriteLine("pair failed: " + add.Exception!.GetBaseException().Message); return 2; }
            Console.WriteLine($"paired; key given back as device {add.Result.Pc.BackDeviceId}");
            var back = host.MobileBridge.GetDevice(add.Result.Pc.BackDeviceId ?? "");
            Console.WriteLine($"the test PC on this laptop: {back?.Name} kind={back?.Kind} allowed=[{string.Join(",", host.Settings.MobileBridge.PairedDevices.First(d => d.Id == back?.Id).Grants ?? new())}]");
            string Cmd(string c)
            {
                var log = Path.Combine(testpcBin, "cmd-log.txt");
                int before = File.Exists(log) ? File.ReadAllLines(log).Length : 0;
                File.WriteAllText(Path.Combine(testpcBin, "cmd.txt"), c);
                Pump(() => !File.Exists(Path.Combine(testpcBin, "cmd.txt")) && File.ReadAllLines(log).Length > before, 30000);
                Pump(() => false, 300);
                return string.Join(" / ", File.ReadAllLines(log).Skip(before));
            }
            var own = host.Settings.MobileBridge.PcId;
            Console.WriteLine("test PC: " + Cmd("remotes"));
            Console.WriteLine("test PC: " + Cmd("remote-roots " + own));
            Console.WriteLine("test PC: " + Cmd("remote-ask " + own));
            var pending = host.MobileBridge.Access.Pending().ToList();
            Console.WriteLine($"asked here: {string.Join(" | ", pending.Select(p => $"{p.DeviceName}: {string.Join(",", p.Categories)}"))}");
            foreach (var p in pending) host.MobileBridge.Access.Decide(p.Id, AccessMode.Always);
            Console.WriteLine("test PC: " + Cmd("remote-roots " + own));
            // Forgetting here: the test PC drops its paired-back entry, and the key given to it stops working.
            var forget = mgr.ForgetAsync(add.Result.Pc.PcId); Pump(() => forget.IsCompleted, 10000);
            Pump(() => false, 1500);
            Console.WriteLine("after forgetting, test PC: " + Cmd("remotes") + $" | key given back still here: {host.MobileBridge.GetDevice(add.Result.Pc.BackDeviceId ?? "") != null}");
            // Pair again for the later steps.
            code2 = File.ReadAllText(Path.Combine(testpcBin, "code.txt")).Split(' ')[0];
            add = mgr.AddPcAsync("127.0.0.1:8091", code2); Pump(() => add.IsCompleted, 20000);
            Console.WriteLine("paired again: " + (add.IsCompletedSuccessfully ? "yes" : add.Exception?.GetBaseException().Message));
            host.MobileBridge.Stop();
            return 0;
        }
        if (step == "discover")
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var dt = mgr.DiscoverAsync(8090);
            Pump(() => dt.IsCompleted, 30000);
            Console.WriteLine($"discover: {sw.ElapsedMilliseconds} ms, status {dt.Status} {dt.Exception?.GetBaseException().Message}");
            if (dt.IsCompletedSuccessfully) foreach (var f in dt.Result) Console.WriteLine($"  {f.PcName} {f.Url} {f.PcId}");
            var one = new CloudSSD.App.Remote.RemotePcClient().ProbeAsync("http://192.168.31.73:8090", default, TimeSpan.FromMilliseconds(800));
            Pump(() => one.IsCompleted, 5000);
            Console.WriteLine("probe own: " + (one.IsCompletedSuccessfully ? one.Result?.PcName ?? "null" : one.Exception?.GetBaseException().Message));
            return 0;
        }
        if (step == "add")
        {
            CloudSSD.Desktop.LocalizationManager.T("x");   // as at app start: windows are translated when they open
            var w = new AddRemotePcWindow(host) { Left = -3000 };
            w.Show();
            Pump(() => false, 9000);
            var c = (FrameworkElement)w.Content;
            var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)c.ActualWidth, (int)c.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen()) { dc.DrawRectangle((Brush)app.Resources["Bg"], null, new Rect(0, 0, c.ActualWidth, c.ActualHeight)); dc.DrawRectangle(new VisualBrush(c), null, new Rect(0, 0, c.ActualWidth, c.ActualHeight)); }
            bmp.Render(dv);
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
            using (var fs = File.Create(Path.Combine(outDir, "remote-add.png"))) enc.Save(fs);
            w.Close();
            return 0;
        }
        var vm = new RemotePcsViewModel(host);
        var view = new RemotePcsView { DataContext = vm };
        var shell = new Border { Background = (Brush)app.Resources["Bg"], Padding = new Thickness(24), Child = view, Width = 1100, Height = 620 };
        var win = new Window { Content = shell, Width = 1120, Height = 680, WindowStyle = WindowStyle.None, ShowInTaskbar = false, Left = -3000, Top = 0 };
        win.Show();
        vm.OnShown();
        Pump(() => vm.Entries.Count > 0 && !vm.Busy, 15000);
        Dump(vm, "drives");
        render(shell, Path.Combine(outDir, $"remote-{step}-drives.png"), 1100);

        var received = vm.Entries.FirstOrDefault(e => e.Name.Contains("Received", StringComparison.OrdinalIgnoreCase)) ?? vm.Entries.First();
        vm.OpenEntryCommand.Execute(received);
        Pump(() => vm.CurrentPath != null && !vm.Busy && vm.Entries.Count > 0, 15000);
        Dump(vm, "folder");
        render(shell, Path.Combine(outDir, $"remote-{step}-folder.png"), 1100);

        if (step == "step1")
        {
            var file = Path.Combine(Path.GetTempPath(), "from-laptop-" + DateTime.Now.ToString("HHmmss") + ".txt");
            File.WriteAllText(file, "sent from the laptop page " + DateTime.Now);
            vm.EnqueueDrop(new[] { file });
            Pump(() => vm.Uploads.Count > 0 && vm.Uploads.All(u => u.StateText == CloudSSD.Desktop.LocalizationManager.T("Sent")), 20000);
            Pump(() => vm.Entries.Any(e => e.Name == Path.GetFileName(file)), 8000);
            Dump(vm, "after upload");
            render(shell, Path.Combine(outDir, "remote-step1-sent.png"), 1100);

            // The other PC goes away.
            File.WriteAllText(Path.Combine(testpcBin, "stop.txt"), "stop");
            Pump(() => !vm.IsOnline && vm.ShowBanner, 40000);
            Dump(vm, "offline folder");
            render(shell, Path.Combine(outDir, "remote-step1-offline-folder.png"), 1100);

            // Send while away: it waits in the queue.
            var file2 = Path.Combine(Path.GetTempPath(), "queued-while-away.txt");
            File.WriteAllText(file2, "queued while the PC was off " + DateTime.Now);
            vm.EnqueueDrop(new[] { file2 });
            Pump(() => false, 1500);
            Dump(vm, "queued");
            vm.UpCommand.Execute(null);
            Pump(() => vm.CurrentPath is null && !vm.Busy, 8000);
            Dump(vm, "offline drives");
            render(shell, Path.Combine(outDir, "remote-step1-offline-drives.png"), 1100);
        }
        else
        {
            // Bridge back: the queued file goes out by itself, and what is new since last time has a badge.
            Pump(() => vm.Uploads.Count == 0 || vm.Uploads.All(u => u.StateText == CloudSSD.Desktop.LocalizationManager.T("Sent")), 30000);
            Pump(() => vm.Entries.Any(e => e.Name == "queued-while-away.txt"), 10000);
            Dump(vm, "back online");
            render(shell, Path.Combine(outDir, "remote-step2-back.png"), 1100);
        }
        win.Close();
        Console.WriteLine("message: " + vm.Message);
        return 0;
    }

    private static void Dump(RemotePcsViewModel vm, string what)
    {
        Console.WriteLine($"--- {DateTime.Now:HH:mm:ss.fff} {what}: online={vm.IsOnline} banner={vm.ShowBanner} path={vm.CurrentPath ?? "(drives)"} header='{vm.HeaderTitle}' sub='{vm.HeaderSub}' msg='{vm.Message}'");
        if (vm.ShowBanner) Console.WriteLine("    banner: " + vm.BannerText);
        foreach (var p in vm.Pcs) Console.WriteLine($"    pc: {p.Name} | {p.Status} | {p.Tone}");
        foreach (var e in vm.Entries) Console.WriteLine($"    {(e.IsFolder ? "D" : "F")} {e.Name} | {e.SizeText} | {e.ModifiedText} {(e.IsNew ? "NEW" : "")}");
        foreach (var u in vm.Uploads) Console.WriteLine($"    up: {u.Name} | {u.StateText}");
        Console.WriteLine("    crumbs: " + string.Join(" > ", vm.Crumbs.Select(c => c.Title)));
    }

    /// <summary>Runs the WPF loop until <paramref name="until"/> is true or the time is up.</summary>
    public static void Pump(Func<bool> until, int ms)
    {
        var end = DateTime.UtcNow.AddMilliseconds(ms);
        var frame = new DispatcherFrame();
        int ticks = 0; var t0 = DateTime.Now;
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, (_, _) =>
        {
            ticks++;
            if (until() || DateTime.UtcNow > end) frame.Continue = false;
        }, Dispatcher.CurrentDispatcher);
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
        Console.WriteLine($"    (pump {t0:HH:mm:ss.fff} -> {DateTime.Now:HH:mm:ss.fff}, {ticks} ticks)");
    }
}
