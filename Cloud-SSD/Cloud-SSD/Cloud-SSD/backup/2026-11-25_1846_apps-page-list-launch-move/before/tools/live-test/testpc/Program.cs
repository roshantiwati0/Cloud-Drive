// A second, throw-away Cloud-SSD bridge for testing "several PCs" on the phone. It uses its own temp data folder
// (never the real app's), listens on the port given (default 8091), and writes its pairing code to code.txt.
// The phone reaches it through `adb reverse tcp:<port> tcp:<port>` as http://127.0.0.1:<port>. Stops when stop.txt appears.
using CloudSSD.App;
using CloudSSD.Core;

int port = args.Length > 0 ? int.Parse(args[0]) : 8091;
string dir = AppContext.BaseDirectory;
var temp = Path.Combine(Path.GetTempPath(), "cssd_testpc_" + port);
Directory.CreateDirectory(temp);
var host = AppHost.Create(new AppPaths(temp));
var b = host.Settings.MobileBridge;
b.Port = port;
b.LegacyTokenAccepted = false;
var uploads = Directory.CreateDirectory(Path.Combine(temp, "uploads")).FullName;
b.UploadDirectory = uploads;
foreach (var n in new[] { "laptop-notes.txt", "report-2026.pdf", "holi-photo.jpg" })
    if (!File.Exists(Path.Combine(uploads, n))) File.WriteAllText(Path.Combine(uploads, n), "test file " + n);
host.SaveSettings();
// A fake cloud drive ("Test Cloud", id cloudssd) so the phone's Files app has a root to show (set through reflection:
// the override is internal to the app).
var cloud = Directory.CreateDirectory(Path.Combine(temp, "cloud")).FullName;
Directory.CreateDirectory(Path.Combine(cloud, "Docs"));
if (!File.Exists(Path.Combine(cloud, "Docs", "readme.txt"))) File.WriteAllText(Path.Combine(cloud, "Docs", "readme.txt"), "Readme from the test cloud");
if (!File.Exists(Path.Combine(cloud, "Docs", "plan.txt"))) File.WriteAllText(Path.Combine(cloud, "Docs", "plan.txt"), "Plan: test the Files app offline");
var rootType = typeof(AppHost).Assembly.GetType("CloudSSD.App.CloudRoot")!;
var rootObj = Activator.CreateInstance(rootType, AppHost.CombinedMountId, cloud, false, false, temp, "Test Cloud")!;
var listType = typeof(List<>).MakeGenericType(rootType);
var roots = (System.Collections.IList)Activator.CreateInstance(listType)!;
roots.Add(rootObj);
var roType = typeof(IReadOnlyList<>).MakeGenericType(rootType);
var fn = System.Linq.Expressions.Expression.Lambda(typeof(Func<>).MakeGenericType(roType),
    System.Linq.Expressions.Expression.Convert(System.Linq.Expressions.Expression.Constant(roots), roType)).Compile();
typeof(LocalMobileBridgeService).GetField("CloudRootsOverride", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
    .SetValue(host.MobileBridge, fn);
host.MobileBridge.Start();
Console.WriteLine($"test PC bridge on {port}, pcId {b.PcId}, data {temp}");
var stop = Path.Combine(dir, "stop.txt");
if (File.Exists(stop)) File.Delete(stop);
// Stands in for the person at the PC: cmd.txt holds one command, done once and deleted.
//   decide always|hour|session|deny   — answers the oldest waiting access request
//   revoke <device name> <category>   — takes a permission away
//   grant <device name> <category>    — gives one (always)
var cmdFile = Path.Combine(dir, "cmd.txt");
var log = Path.Combine(dir, "cmd-log.txt");
host.MobileBridge.Access.RequestCreated += r => File.AppendAllText(log, $"{DateTime.Now:HH:mm:ss} request {r.Id} from {r.DeviceName}: {string.Join(",", r.Categories)}\n");
host.MobileBridge.Shares.Changed += o => File.AppendAllText(log, $"{DateTime.Now:HH:mm:ss} share {o.Id} {o.FromName} -> {o.ToName}: {o.State} ({string.Join(",", o.Items.Select(i => i.Name + (i.Delivered ? "✓" : i.Ready ? "" : "…")))})\n");
host.MobileBridge.ClipboardReceived += (d, t) => File.AppendAllText(log, $"{DateTime.Now:HH:mm:ss} clipboard from {d.Name}: {t}\n");
host.MobileBridge.FileReceived += (d, p) => File.AppendAllText(log, $"{DateTime.Now:HH:mm:ss} received from {d.Name}: {Path.GetFileName(p)}\n");
// Wake-on-LAN: logs every packet that reaches this PC on UDP 9 (the phone's "Wake PC"), with the MAC inside.
_ = Task.Run(async () =>
{
    try
    {
        using var udp = new System.Net.Sockets.UdpClient(new System.Net.IPEndPoint(System.Net.IPAddress.Any, 9));
        while (!File.Exists(stop))
        {
            var r = await udp.ReceiveAsync();
            var mac = WakeOnLan.MacOf(r.Buffer);
            File.AppendAllText(log, $"{DateTime.Now:HH:mm:ss} wol {mac ?? "not a magic packet"} from {r.RemoteEndPoint.Address} ({r.Buffer.Length} B)\n");
        }
    }
    catch (Exception e) { File.AppendAllText(log, $"{DateTime.Now:HH:mm:ss} wol listener: {e.Message}\n"); }
});
for (int i = 0; i < 8 * 60 * 60 && !File.Exists(stop); i++)
{
    File.WriteAllText(Path.Combine(dir, "code.txt"), host.MobileBridge.PairingCode + " " + b.PcId);
    if (File.Exists(cmdFile))
    {
        var parts = File.ReadAllText(cmdFile).Trim().Split(' ', 3);
        File.Delete(cmdFile);
        string result;
        try
        {
            var access = host.MobileBridge.Access;
            if (parts[0] == "guest")
            {
                // guest <minutes> <category,category…>: a one-time guest code, as "Add a guest" on the PC makes it.
                var inv = host.MobileBridge.CreateGuestInvite(TimeSpan.FromMinutes(int.Parse(parts[1])), parts[2].Split(','));
                result = $"guest code {inv.Code} for {parts[1]} min: {string.Join(",", inv.Categories)}";
            }
            else if (parts[0] is "remotes" or "remote-roots" or "remote-ask")
            {
                // This PC's own "Other PCs" list (a laptop that paired back shows here), and looking at one of them.
                var mgr = host.RemotePcs;
                if (parts[0] == "remotes")
                    result = "remotes: " + string.Join(" || ", mgr.Store.Pcs.Select(p => $"{p.DisplayName} {p.PcId} pairedBack={p.PairedBack} url={p.LanUrl}"));
                else
                {
                    var s = mgr.Session(parts[1])!;
                    await s.TickAsync();
                    if (parts[0] == "remote-ask")
                    {
                        var req = await s.RequestAccessAsync(["browse"]);
                        result = $"asked {req.Id}: {req.State}";
                    }
                    else
                    {
                        try
                        {
                            var rootsGot = await s.GetRootsAsync();
                            result = "remote roots: " + string.Join(", ", rootsGot.Value.Select(r => r.Title)) + (rootsGot.FromCache ? " (saved)" : "");
                        }
                        catch (CloudSSD.App.Remote.RemotePcException e) { result = $"remote roots refused: {e.Kind} {string.Join(",", e.Categories)}"; }
                    }
                }
            }
            else if (parts[0] == "devices")
            {
                // What the PC's Devices page shows for every paired device.
                result = "devices: " + string.Join(" || ", host.MobileBridge.GetDevices().Select(d =>
                    $"{d.Name} kind={d.Kind} online={d.Online} link={d.Link} listening={d.Listening} blocked={d.Blocked} activity={d.Activity?.ToString() ?? "-"}"));
            }
            else if (parts[0] is "block" or "unblock" or "rename")
            {
                // As the Devices page's Block / Unblock / Rename buttons do.
                var dev = b.PairedDevices.First(d => d.Name == parts[1].Replace('_', ' ') || d.Id == parts[1]);
                if (parts[0] == "rename") host.MobileBridge.RenameDevice(dev.Id, parts[2]);
                else host.MobileBridge.SetBlocked(dev.Id, parts[0] == "block");
                var s = host.MobileBridge.GetDevice(dev.Id)!;
                result = $"{parts[0]}: {s.Name} blocked={s.Blocked} online={s.Online} id={s.Id}";
            }
            else if (parts[0] == "grants")
            {
                var dev = b.PairedDevices.First(d => d.Name == parts[1].Replace('_', ' '));
                result = $"grants {dev.Name}: " + string.Join(",", access.Current(dev).Select(g => g.Category + ":" + g.Mode));
            }
            else if (parts[0] is "ring" or "clip" or "vitals")
            {
                var dev = b.PairedDevices.First(d => d.Name == parts[1].Replace('_', ' '));
                if (parts[0] == "vitals")
                {
                    var v = host.MobileBridge.VitalsOf(dev.Id);
                    result = v is null ? "no vitals" : $"vitals battery={v.Battery} charging={v.Charging} free={v.FreeBytes} total={v.TotalBytes}";
                }
                else
                {
                    var r = parts[0] == "ring" ? await host.MobileBridge.RingDeviceAsync(dev.Id) : await host.MobileBridge.SendClipboardAsync(dev.Id, parts.Length > 2 ? parts[2] : "");
                    result = $"{parts[0]}: {r.Status} {r.Error}";
                }
            }
            else if (parts[0] == "ask")
            {
                // ask <device> <kind> [path]
                var dev = b.PairedDevices.First(d => d.Name == parts[1].Replace('_', ' '));
                var kp = parts[2].Split(' ', 2);
                var args2 = new Dictionary<string, string>();
                if (kp.Length > 1) args2["path"] = kp[1];
                var r = await host.MobileBridge.AskDeviceAsync(dev.Id, kp[0], args2, TimeSpan.FromSeconds(30));
                var detail = r.Json is { } j ? j.GetRawText() : r.FilePath is { } fp ? $"file {new FileInfo(fp).Length} bytes" : r.Error;
                if (detail != null && detail.Length > 600) detail = detail[..600] + "…";
                result = $"ask {kp[0]} {(kp.Length > 1 ? kp[1] : "")}: {r.Status} {detail}";
            }
            else if (parts[0] == "send")
            {
                var dev = b.PairedDevices.First(d => d.Name == parts[1].Replace('_', ' '));
                var offer = host.MobileBridge.SendToDevice(dev, parts[2].Split('|'));
                result = offer is null ? "nothing to send" : $"offered {offer.Id} to {dev.Name}: {string.Join(",", offer.Items.Select(i => i.Name))}";
            }
            else if (parts[0] == "decide")
            {
                var r = access.Pending().FirstOrDefault();
                AccessMode? mode = parts[1] switch { "always" => AccessMode.Always, "hour" => AccessMode.Hour, "session" => AccessMode.Session, _ => null };
                result = r is null ? "no request waiting" : $"decided {r.Id} {parts[1]}: {access.Decide(r.Id, mode)}";
            }
            else
            {
                var dev = b.PairedDevices.First(d => d.Name == parts[1].Replace('_', ' '));
                if (parts[0] == "revoke") access.Revoke(dev, parts[2]); else access.Grant(dev, [parts[2]], AccessMode.Always);
                result = $"{parts[0]} {parts[2]} for {dev.Name}: now {string.Join(",", access.Current(dev).Select(g => g.Category))}";
            }
        }
        catch (Exception e) { result = "error: " + e.Message; }
        File.AppendAllText(log, $"{DateTime.Now:HH:mm:ss} {result}\n");
    }
    await Task.Delay(1000);
}
await host.MobileBridge.DisposeAsync();
Console.WriteLine("stopped");
