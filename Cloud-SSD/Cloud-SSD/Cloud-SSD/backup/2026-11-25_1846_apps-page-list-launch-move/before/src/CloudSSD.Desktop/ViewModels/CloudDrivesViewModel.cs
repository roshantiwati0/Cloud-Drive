using System.Collections.ObjectModel;
using System.Diagnostics;
using CloudSSD.App;
using CloudSSD.Cache;
using CloudSSD.Core;
using CloudSSD.Mount;
using CloudSSD.Providers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CloudSSD.Desktop.ViewModels;

public sealed partial class DriveItem : ObservableObject
{
    public required MountDefinition Definition { get; init; }
    [ObservableProperty] private string _letter = "";
    [ObservableProperty] private string _state = "";
    [ObservableProperty] private string _tone = "muted";
    [ObservableProperty] private string _detail = "";
    [ObservableProperty] private bool _isMounted;
    [ObservableProperty] private string _account = "";
    public string Name => Definition.DisplayName;
    public string Remote => Definition.RemoteSpec;
}

public sealed record ProviderRow(string Name, string Support, string Tone, string Notes);

public sealed partial class CloudDrivesViewModel : PageViewModel
{
    public CloudDrivesViewModel(AppHost host) : base(host, "Cloud Drives", "") { }

    // ---- Fast drive (hybrid): the drive letter is a folder on this PC, uploaded behind ----
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HybridButton))] private bool _hybridOn;
    [ObservableProperty] private string _hybridText = "";
    [ObservableProperty] private string _hybridItems = "";
    public string HybridButton => HybridOn ? "Switch the fast drive off" : "Switch the fast drive on";
    private bool _hybridWatched;

    private void UpdateHybrid()
    {
        // The fast drive reports from its own timer (links made, an upload started or ended); listen once.
        if (!_hybridWatched)
        {
            _hybridWatched = true;
            Host.Hybrid.Changed += () => Ui(UpdateHybrid);
            Host.Speed.Changed += () => Ui(UpdateSpeed);
            // A regular check found something that was fine before: say so where the person sees it (corner note + bell).
            Host.Speed.ProblemFound += r => Notifier.Show("Speed check", r.Summary, "page:Cloud Drives", "warn");
        }
        UpdateSpeed();
        var s = Host.Settings.Hybrid;
        var st = Host.Hybrid.Status;
        HybridOn = s.Enabled;
        HybridText = s.Enabled ? st.Message : "Off: everything you write goes straight to the cloud drive (slow for installs: every new folder asks Google).";
        HybridItems = !s.Enabled || st.Items.Count == 0 ? "" :
            "On this PC: " + string.Join(" · ", st.Items.Take(8).Select(i => i.Name + (i.State switch
            {
                "uploaded" => " (in the cloud too)", "uploading" => " (uploading…)", "failed" => " (upload failed, trying again)", _ => " (waiting to upload)"
            }))) + (st.Items.Count > 8 ? $" · and {st.Items.Count - 8} more" : "");
    }

    // ---- Speed check: is the drive still as fast as it was made? ----
    [ObservableProperty] private string _speedText = "";
    [ObservableProperty] private string _speedHead = "Not checked yet since the app started. It checks by itself a little after the start and every 3 hours.";

    private void UpdateSpeed()
    {
        if (Host.Speed.Last is not { } r) return;
        SpeedHead = (r.HasProblem ? "Something is slow" : "Fast") + $" — checked at {r.At:HH:mm}. The app checks by itself every 3 hours and tells you when this changes.";
        SpeedText = string.Join("\n", r.Checks.Select(c => (c.State switch { "ok" => "✔ ", "warn" => "!  ", _ => "✖ " }) + c.Text));
    }

    [RelayCommand]
    private async Task CheckSpeed()
    {
        SpeedHead = "Checking…";
        await RunAsync(async () => { await Host.Speed.RunAsync(); });
        UpdateSpeed();
    }

    [RelayCommand]
    private async Task ToggleHybrid()
    {
        var s = Host.Settings.Hybrid;
        if (s.Enabled)
        {
            var ok = Host.Hybrid.Disable(out var message);
            Say(message, ok ? "ok" : "warn");
            UpdateHybrid();
            return;
        }
        var letter = string.IsNullOrWhiteSpace(s.Letter) ? "Z" : s.Letter.Trim()[..1].ToUpperInvariant();
        if (!Confirm($"Switch the fast drive on?\n\n" +
                     $"{letter}: becomes a folder on this PC ({s.RootOrDefault}). New files and folders are written there at the disk's own speed " +
                     "and uploaded to the cloud behind. The folders already in the cloud show up in it as before.\n\n" +
                     $"• {letter}: then shows this PC's free space, not the cloud's.\n" +
                     "• What you make stays on this PC as well (it uses disk space here).\n" +
                     $"• The cloud drive itself moves to {s.BackingLetter}: and reconnects once. Close programs that run from it first.",
                "Fast drive")) return;
        await RunAsync(async () =>
        {
            await Host.Hybrid.EnableAsync();
            if (Host.Hybrid.CloudDriveHoldsLetter && await Host.Hybrid.MoveCloudDriveAsync() is { } why) Say(why, "warn");
            else Say(Host.Hybrid.Status.Message, "ok");
        });
        OnStateChanged();
    }

    [RelayCommand]
    private void OpenHybrid()
    {
        var root = Host.Settings.Hybrid.RootOrDefault;
        var st = Host.Hybrid.Status;
        var path = st.Enabled && st.LetterReady ? st.Letter + "\\" : root;
        if (Directory.Exists(path)) Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
    }

    public ObservableCollection<AccountRow> Accounts { get; } = new();
    public ObservableCollection<DriveItem> Drives { get; } = new();
    public ObservableCollection<ProviderRow> ProviderRows { get; } = new(ProviderRegistry.All.Select(p => new ProviderRow(
        p.DisplayName,
        p.Support switch { SupportLevel.Supported => "Supported", SupportLevel.AvailableUntested => "Via rclone · untested", _ => "Not supported" },
        p.Support switch { SupportLevel.Supported => "ok", SupportLevel.AvailableUntested => "warn", _ => "error" },
        p.Capabilities.Notes ?? p.SetupSummary)));

    [ObservableProperty] private AccountRow? _selectedAccount;
    [ObservableProperty] private DriveItem? _selectedDrive;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ShowKeyNotice))] private string _sharedKeyNotice = "";
    public bool ShowKeyNotice => SharedKeyNotice.Length > 0;

    /// <summary>
    /// True = what you read is kept on this PC, so opening the same archive again is instant (fast browsing).
    /// False = nothing is kept; every open goes to the cloud again.
    /// </summary>
    [ObservableProperty] private bool _fastBrowsing;

    /// <summary>
    /// Which rclone cache mode belongs to the "Fast browsing" switch.
    /// On  = "full": what was read is kept on this PC (sparse), so the next open is instant.
    /// Off = "writes": nothing is kept for reading, every open goes to the cloud again.
    /// </summary>
    public static string CacheModeFor(bool fastBrowsing) => fastBrowsing ? "full" : "writes";

    /// <summary>
    /// Saves the drive mode and reconnects the drives so it takes effect right away: the cache mode is fixed when
    /// rclone starts, so a running mount keeps the old one until it is remounted.
    /// </summary>
    [RelayCommand]
    private async Task ApplyDriveMode()
    {
        Host.Settings.Cache.VfsCacheMode = CacheModeFor(FastBrowsing);
        Host.SaveSettings();
        await RunAsync(async () =>
        {
            foreach (var m in Host.Settings.Mounts.Where(m => Host.Mounts.Get(m.Id) != null).ToList())
                await Host.UnmountAsync(m.Id);
            foreach (var m in Host.Settings.Mounts)
            {
                try { await Host.MountAsync(m); }
                catch (Exception e) { Say($"{m.DisplayName}: {e.Message}", "error"); }
            }
        }, FastBrowsing
            ? "Fast browsing is on: the parts you open are kept on this PC, so the next open is instant."
            : "Fast browsing is off: nothing is kept on this PC, every open goes to the cloud again.");
        OnStateChanged();
    }

    public override void OnShown()
    {
        OnStateChanged();
        // Storage figures come live from the provider; refresh them when the page opens (at most once a minute).
        if (Host.Remotes.Count > 0 && DateTime.UtcNow - Host.AccountsRefreshedAt > TimeSpan.FromMinutes(1)) _ = Host.RefreshAccountsAsync();
    }

    public override void OnStateChanged()
    {
        var shared = Host.SharedClientRemotes;
        Accounts.ReplaceWith(AccountText.Rows(Host));
        FastBrowsing = !Host.Settings.Cache.VfsCacheMode.Equals("writes", StringComparison.OrdinalIgnoreCase);
        UpdateHybrid();
        SharedKeyNotice = shared.Count == 0 ? "" :
            $"{shared.Count} Google account(s) ({string.Join(", ", shared.Select(Host.AccountLabel))}) still sign in through rclone's shared Google login. rclone is retiring it during 2026; " +
            "when Google stops accepting it, these drives stop working. Switch them to your own Google key — your files and drive letters stay the same.";
        var sessions = Host.Mounts.Sessions;
        var selected = SelectedDrive?.Definition.Id;
        Drives.ReplaceWith(Host.Settings.Mounts.Select(d =>
        {
            var s = sessions.FirstOrDefault(x => x.Id == d.Id);
            var state = s?.State ?? MountState.Stopped;
            return new DriveItem
            {
                Definition = d,
                Letter = s != null ? $"{s.DriveLetter}:" : (d.DriveLetter is { } l ? $"{l}:" : "auto"),
                State = state switch
                {
                    MountState.Mounted => "Connected",
                    MountState.Starting => "Connecting...",
                    MountState.Stopping => "Disconnecting...",
                    MountState.Stopped => "Disconnected",
                    MountState.Degraded => "Degraded",
                    MountState.Failed => "Failed",
                    _ => state.ToString()
                },
                Tone = Fmt.Tone(state),
                IsMounted = state is MountState.Mounted or MountState.Degraded,
                // The Gmail address(es) behind the drive, not the internal remote name; the folder only when one is set.
                Account = Host.AccountLabel(d.RemoteName) + (string.IsNullOrEmpty(d.RemotePath) ? "" : $" · /{d.RemotePath}")
                          + (AccountText.Storage(Host.Accounts.GetValueOrDefault(d.RemoteName)) is { } st ? $" · {st}" : ""),
                Detail = (s?.Message ?? "") + (s?.Warnings.Count > 0 ? " · " + string.Join(" ", s.Warnings) : "") + (d.ReadOnly ? " · read-only" : "")
            };
        }));
        SelectedDrive = Drives.FirstOrDefault(d => d.Definition.Id == selected);
    }

    [RelayCommand]
    private async Task ConnectAccount() => await ConnectGoogleDrive();

    [RelayCommand]
    private async Task ConnectGoogleDrive()
    {
        var w = new Views.ConnectDriveWindow(Host) { Owner = System.Windows.Application.Current.MainWindow };
        if (w.ShowDialog() != true) return;

        // No questions after sign-in: the account simply joins the two standard drives ("Each Account" shows every
        // account on its own, "All Accounts" joins them). Earlier accounts are always kept.
        Say("Signed in. Setting up your drives…", "accent");
        await RunAsync(async () =>
        {
            await Host.SyncStandardDrivesAsync();
            if (Host.Remotes.All(r => r.Name != w.RemoteName))
            {
                Say("This account was already connected, so nothing was added. To add another one, choose “Use another account” on Google's page.", "warn");
                return;
            }
            var storage = AccountText.Storage(Host.Accounts.GetValueOrDefault(w.RemoteName)) is { } st ? $" ({st})" : "";
            var count = Host.AccountsBehind(AppHost.CombinedRemote).Count;
            Say($"{Host.AccountName(w.RemoteName)} connected{storage}{(w.ReadOnlyScope ? ", read-only" : "")}. {count} account(s) are now in your two drives.", "ok");
        });
        OnStateChanged();
    }

    private const string KeyGuide =
        "Get your own Google key (free, about 5 minutes):\n\n" +
        "1. In Google Cloud Console create a project (any name).\n" +
        "2. APIs & Services → Library → Google Drive API → Enable.\n" +
        "3. Google Auth Platform → Branding: fill in an app name and your email. Audience: External.\n" +
        "4. Audience → Publish app (\"In production\"). If you leave it in \"Testing\", Google ends every login after 7 days.\n" +
        "   (Or keep Testing and add each Gmail as a test user — then you must sign in again every week.)\n" +
        "5. Clients → Create client → Application type: Desktop app → Create → Download JSON.\n" +
        "   The file (client_secret_….json) lands in Downloads; Cloud Drive finds it there.\n\n" +
        "When signing in, Google may say \"Google hasn't verified this app\": it is your own app — click Advanced → Go to (your app name).\n\n" +
        "Open Google Cloud Console now?";

    [RelayCommand]
    private void ShowKeyGuide()
    {
        if (Confirm(KeyGuide, "Your own Google key"))
            Process.Start(new ProcessStartInfo("https://console.cloud.google.com/auth/clients") { UseShellExecute = true });
    }

    /// <summary>
    /// Moves every Google account that still uses rclone's retiring shared login to the user's own Google key. Each
    /// account signs in again in the browser and must be the same Gmail as before (checked, otherwise left unchanged), so
    /// union drives built from several accounts keep their files and drive letters. Affected drives are disconnected while
    /// this runs, because a running mount would write its old login back.
    /// </summary>
    [RelayCommand]
    private async Task SwitchToOwnKey()
    {
        if (Busy) return;
        if (Host.Rclone is not { } rclone) { Say("rclone was not found; see Diagnostics.", "error"); return; }
        await Host.RefreshRemotesAsync();
        var targets = Host.SharedClientRemotes.ToList();
        if (targets.Count == 0) { OnStateChanged(); Say("All Google accounts already use your own Google key.", "ok"); return; }

        var key = GoogleClientFile.FindDownloaded() ?? GoogleClientFile.FindBundled() ?? PickKeyFile();
        if (key is null) return;

        var dump = await Host.RcloneConfig.DumpAsync(rclone);
        var users = targets.SelectMany(t => RcloneConfigService.RemotesUsing(dump, t)).ToHashSet();
        var affected = Host.Settings.Mounts.Where(m => users.Contains(m.RemoteName) && Host.Mounts.Get(m.Id) != null).ToList();
        if (Host.CurrentLaunch is { State: Games.LaunchState.Running } l && affected.Any(m => m.Id == l.Game.MountId))
        {
            Say($"{l.Game.Name} is running from a drive that needs to reconnect. Close the game first.", "warn");
            return;
        }
        if (!Confirm($"Switch {targets.Count} account(s) to your own Google key?\n\nKey: {Path.GetFileName(key.Path)}" +
                     (key.ProjectId != null ? $" (project {key.ProjectId})" : "") + "\nAccounts: " + string.Join(", ", targets) +
                     (affected.Count > 0 ? "\n\nThese drives disconnect for a few minutes and reconnect by themselves: " +
                                           string.Join(", ", affected.Select(m => $"{m.DisplayName} ({m.DriveLetter}:)")) : "") +
                     "\n\nFor each account Google's sign-in page opens. Choose the Gmail shown before each one. Your files are not touched.",
                "Switch to your own Google key")) return;

        await RunAsync(async () =>
        {
            var done = new List<string>();
            var failed = new List<string>();
            var progress = new Progress<string>(m => Say(m, "accent"));
            foreach (var m in affected)
            {
                Say($"Disconnecting {m.DisplayName}…", "accent");
                await Host.UnmountAsync(m.Id);
            }
            try
            {
                foreach (var remote in targets)
                {
                    Say($"{remote}: looking up which Google account it belongs to…", "accent");
                    var email = await Host.Drive.AccountEmailAsync(rclone, remote, CancellationToken.None);
                    var go = System.Windows.MessageBox.Show(System.Windows.Application.Current.MainWindow!,
                        email != null
                            ? $"Next: {remote}\n\nOn Google's page sign in as:\n\n      {email}\n\nThen allow access."
                            : $"Next: {remote}\n\nIts current login no longer works, so Cloud Drive cannot tell which Gmail it was. " +
                              "Sign in with the Gmail that holds this account's files — a different one would show other files on the drive.",
                        "Switch to your own Google key", System.Windows.MessageBoxButton.OKCancel, System.Windows.MessageBoxImage.Information);
                    if (go != System.Windows.MessageBoxResult.OK) { failed.Add($"{remote}: skipped"); continue; }
                    using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
                    try
                    {
                        var now = await Host.Drive.RekeyAsync(rclone, remote, key.ClientId, key.ClientSecret, email, progress, timeout.Token);
                        done.Add(now != null ? $"{remote} ({now})" : remote);
                    }
                    catch (OperationCanceledException) { failed.Add($"{remote}: Google sign-in did not finish within 5 minutes; left as it was"); }
                    catch (Exception e) { failed.Add($"{remote}: {Diagnostics.Redactor.Redact(e.Message)}"); }
                }
            }
            finally
            {
                foreach (var m in affected)
                {
                    Say($"Reconnecting {m.DisplayName}…", "accent");
                    try { await Host.MountAsync(m); }
                    catch (Exception e) { failed.Add($"{m.DisplayName}: could not reconnect ({Diagnostics.Redactor.Redact(e.Message)}). Press Connect on it."); }
                }
                Host.SaveSettings();
                await Host.RefreshRemotesAsync();
                if (Host.Settings.EncryptRcloneConfig) await Host.RcloneConfig.EnsureEncryptedAsync(rclone, Host.CpxPath);
            }
            Say((done.Count > 0 ? "Now using your own Google key: " + string.Join(", ", done) + ". " : "") +
                (failed.Count > 0 ? "Not switched: " + string.Join(" · ", failed) : "All done."),
                failed.Count == 0 ? "ok" : done.Count > 0 ? "warn" : "error");
        });
        OnStateChanged();
    }

    private static GoogleClientFile? PickKeyFile()
    {
        var owner = System.Windows.Application.Current.MainWindow!;
        var answer = System.Windows.MessageBox.Show(owner,
            "No Google key file (client_secret_….json) was found in Downloads or on the Desktop.\n\n" +
            "Yes — I already have the file, let me choose it.\nNo — show me how to make one.",
            "Your own Google key", System.Windows.MessageBoxButton.YesNoCancel, System.Windows.MessageBoxImage.Question);
        if (answer == System.Windows.MessageBoxResult.No)
        {
            if (Confirm(KeyGuide, "Your own Google key"))
                Process.Start(new ProcessStartInfo("https://console.cloud.google.com/auth/clients") { UseShellExecute = true });
            return null;
        }
        if (answer != System.Windows.MessageBoxResult.Yes) return null;
        var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Choose your Google key file", Filter = "Google OAuth client (*.json)|*.json" };
        if (dlg.ShowDialog(owner) != true) return null;
        var key = GoogleClientFile.TryLoad(dlg.FileName);
        if (key is null)
            System.Windows.MessageBox.Show(owner, "That file is not a Google OAuth client file (it needs a client_id and client_secret).", "Your own Google key");
        return key;
    }

    [RelayCommand]
    private async Task AddDrive(string? remote)
    {
        if (Host.Remotes.Count == 0)
        {
            Say("Connect a cloud account first.", "warn");
            return;
        }
        var w = new Views.AddDriveWindow(Host, remote) { Owner = System.Windows.Application.Current.MainWindow };
        if (w.ShowDialog() != true || w.Result is null) return;
        Host.Settings.Mounts.Add(w.Result);
        Host.SaveSettings();
        OnStateChanged();
        if (!await WinFspOffer.EnsureAsync(Host, evenAfterNo: true)) return;   // no WinFsp, no drive: offered here
        await RunAsync(async () =>
        {
            var s = await Host.MountAsync(w.Result);
            Host.SaveSettings();
            Say($"{w.Result.DisplayName} connected as {s.DriveLetter}: drive", "ok");
        });
        OnStateChanged();
    }

    [RelayCommand]
    private async Task Mount(DriveItem? d)
    {
        if (d is null) return;
        if (!await WinFspOffer.EnsureAsync(Host, evenAfterNo: true)) return;
        await RunAsync(async () =>
        {
            var s = await Host.MountAsync(d.Definition);
            Host.SaveSettings();
            Say($"{d.Name} connected as {s.DriveLetter}: drive", "ok");
        });
        OnStateChanged();
    }

    [RelayCommand]
    private async Task Unmount(DriveItem? d)
    {
        if (d is null) return;
        if (Host.CurrentLaunch is { State: Games.LaunchState.Running } l && l.Game.MountId == d.Definition.Id
            && !Confirm($"{l.Game.Name} is running from this drive. Disconnecting will break it. Continue?")) return;
        await RunAsync(() => Host.UnmountAsync(d.Definition.Id), $"{d.Name} disconnected.");
        OnStateChanged();
    }

    [RelayCommand]
    private void Open(DriveItem? d)
    {
        if (d is null || Host.Mounts.Get(d.Definition.Id) is not { } s) return;
        Process.Start(new ProcessStartInfo("explorer.exe", s.Root) { UseShellExecute = true });
    }

    [RelayCommand]
    private async Task Refresh(DriveItem? d)
    {
        if (d is null) return;
        await RunAsync(() => Host.Mounts.ForgetAsync(d.Definition.Id), "Directory cache refreshed; changed cloud files will be re-validated on next access.");
    }

    [RelayCommand]
    private async Task RemoveDrive(DriveItem? d)
    {
        if (d is null) return;
        if (!Confirm($"Remove the drive '{d.Name}' from Cloud Drive?\n\nYour cloud files are NOT touched. The drive will be disconnected.")) return;
        await RunAsync(async () =>
        {
            if (Host.Mounts.Get(d.Definition.Id) != null) await Host.UnmountAsync(d.Definition.Id);
            Host.Settings.Mounts.RemoveAll(m => m.Id == d.Definition.Id);
            Host.SaveSettings();
            var dir = Host.Paths.MountCacheDir(Host.Settings, d.Definition.Id);
            if (Directory.Exists(dir) && Confirm("Also delete this drive's local cache? (Only cached copies are deleted, never cloud files.)"))
            {
                var r = CacheMaintenance.ClearMountCache(dir, Host.Paths.CacheRootFor(Host.Settings));
                Say(r.Message, r.Cleared ? "ok" : "warn");
            }
            else Say("Drive removed.", "ok");
        });
        OnStateChanged();
    }

    [RelayCommand]
    private async Task RemoveAccount(AccountRow? a)
    {
        if (a is null || Host.Rclone is not { } rclone) return;
        if (!Confirm($"Disconnect {a.Title}? The saved login is deleted from this PC; your cloud files are not touched.\n\nYour two drives reconnect for a moment without this account.")) return;
        await RunAsync(async () =>
        {
            await Host.RcloneConfig.DeleteRemoteAsync(rclone, a.Remote);
            await Host.SyncStandardDrivesAsync();
        }, $"{a.Title} disconnected.");
        OnStateChanged();
    }
}
