using System.Collections.ObjectModel;
using System.Windows.Media;
using CloudSSD.App;
using CloudSSD.Core;
using CloudSSD.Games;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CloudSSD.Desktop.ViewModels;

/// <summary>One program in the list of the Apps page.</summary>
public sealed partial class AppRow : ObservableObject
{
    public AppRow(AppItem item, MovedApp? moved = null)
    {
        Item = item;
        Moved = moved;
        Initial = item.Name.Length > 0 ? item.Name[..1].ToUpperInvariant() : "?";
        OnCloud = item.Where == AppPlace.Cloud || moved != null;
        WhereText = LocalizationManager.T(OnCloud ? "On the cloud drive" : "On this PC");
        long bytes = moved?.Bytes ?? item.Bytes;
        Detail = (bytes > 0 ? ByteSize.Format(bytes) + " · " : "") + (moved?.CloudDir ?? item.InstallDir);
        // A program that was on the cloud drive from the start has no place on this PC to come back to.
        ShowMove = moved != null || item.Where == AppPlace.Laptop;
        CanMove = moved != null || item.CanMove;
        MoveText = LocalizationManager.T(moved != null ? "Bring back to this PC" : "Move to cloud");
        MoveTip = moved != null ? LocalizationManager.T("Copies it back from the cloud drive; it then starts fast and without the internet.")
            : item.WhyNot is { } why ? LocalizationManager.T(why)
            : LocalizationManager.T("Its files go to the cloud drive and the space on this PC becomes free.");
    }

    public AppItem Item { get; }
    /// <summary>Set when this program's folder was sent to the cloud from this page (a link stands where it was).</summary>
    public MovedApp? Moved { get; }
    public bool ShowMove { get; }
    public bool CanMove { get; }
    public string MoveText { get; }
    public string MoveTip { get; }
    public string Name => Item.Name;
    public string Initial { get; }
    public bool OnCloud { get; }
    public string WhereText { get; }
    public string WhereTone => OnCloud ? "accent" : "muted";
    public string Detail { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasIcon))]
    private ImageSource? _icon;

    public bool HasIcon => Icon != null;
}

/// <summary>
/// "Apps": every program on this PC and on the cloud drives in one list, found by the app itself (Start menu,
/// installed programs, the Apps folder of the cloud drives). One click starts it; "Play as game" starts it through the
/// game library so game mode frees RAM first.
/// </summary>
public sealed partial class AppsViewModel : PageViewModel
{
    public AppsViewModel(AppHost host) : base(host, "Apps", "") { }

    public ObservableCollection<AppRow> Apps { get; } = new();
    private List<AppRow> _all = new();
    private CancellationTokenSource? _icons;
    private bool _loaded;

    [ObservableProperty] private string _search = "";
    [ObservableProperty] private string _countText = "";
    [ObservableProperty] private bool _loading;
    [ObservableProperty] private bool _isEmpty;

    // The rows are rebuilt on every search, so their button texts are bound (static text in a row is not translated again).
    public string OpenLabel => LocalizationManager.T("Open");
    public string GameLabel => LocalizationManager.T("Play as game");

    public override void OnShown()
    {
        OnPropertyChanged(nameof(OpenLabel));
        OnPropertyChanged(nameof(GameLabel));
        OnPropertyChanged(nameof(StopLabel));
        if (!_loaded) _ = LoadAsync();
    }

    partial void OnSearchChanged(string value) => Apply();

    [RelayCommand]
    private Task Refresh() => LoadAsync();

    private async Task LoadAsync()
    {
        if (Loading) return;
        Loading = true;
        CountText = LocalizationManager.T("Looking for the programs on this PC…");
        try
        {
            var roots = Host.Mounts.Sessions.Where(s => s.State == MountState.Mounted).Select(s => s.Root).ToList();
            var items = await Task.Run(() => AppCatalog.Read(roots));
            var moved = Host.AppMover.Moved;
            // A program sent to the cloud shows once: as the program it is on this PC (through its link), not a second
            // time as a folder of the cloud drive.
            _all = items.Where(i => !(i.Where == AppPlace.Cloud && moved.Any(m => SameDir(m.CloudDir, i.InstallDir))))
                .Select(i => new AppRow(i, moved.FirstOrDefault(m => SameDir(m.LocalDir, i.InstallDir)))).ToList();
            _loaded = true;
            Apply();
            LoadIcons();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Say(Diagnostics.Redactor.Redact(e.Message), "error");
        }
        finally { Loading = false; }
    }

    private void Apply()
    {
        var q = Search.Trim();
        var shown = q.Length == 0 ? _all : _all.Where(a => a.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase)).ToList();
        Apps.ReplaceWith(shown);
        IsEmpty = _loaded && shown.Count == 0;
        if (_loaded)
            CountText = string.Format(LocalizationManager.T("{0} programs · {1} on this PC · {2} on the cloud drive"),
                _all.Count, _all.Count(a => !a.OnCloud), _all.Count(a => a.OnCloud));
    }

    /// <summary>Icons only for programs on this PC: reading one from the cloud would download part of the program.</summary>
    private void LoadIcons()
    {
        _icons?.Cancel();
        _icons = new CancellationTokenSource();
        var byPath = _all.Where(a => !a.OnCloud).GroupBy(a => a.Item.ExePath, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        AppIcons.LoadInBackground(byPath.Keys.ToList(), (path, icon) => Ui(() => { if (byPath.TryGetValue(path, out var row)) row.Icon = icon; }), _icons.Token);
    }

    [RelayCommand]
    private async Task Open(AppRow? row)
    {
        if (row is null) return;
        // Windows reads the program file before it starts it; from the cloud drive on a slow line that takes minutes
        // (measured: MiniMax, 224 MB, at 0.4 Mbps), and done on this thread it froze the whole window for that long.
        if (row.OnCloud) Say(string.Format(LocalizationManager.T("{0} is being fetched from the cloud drive. On a slow internet the first start can take many minutes; it opens by itself when it is ready."), row.Name), "accent");
        await RunAsync(async () =>
        {
            await Task.Run(() => Host.LaunchAppAsync(row.Item, asGame: false));
            Say(string.Format(LocalizationManager.T("{0} is starting."), row.Name), "ok");
        });
    }

    private static bool SameDir(string a, string b) => string.Equals(a.TrimEnd('\\'), b.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    // ---- sending a program to the cloud drive and bringing it back ----

    private CancellationTokenSource? _move;
    [ObservableProperty] private bool _moving;
    [ObservableProperty] private string _moveStatus = "";
    [ObservableProperty] private double _moveFraction;
    public string StopLabel => LocalizationManager.T("Stop");

    [RelayCommand]
    private async Task Move(AppRow? row)
    {
        if (row is null || Moving) return;
        if (Host.AppMover.Busy) { Say(LocalizationManager.T("Another program is being moved. Try again when it is done."), "warn"); return; }
        var progress = new Progress<MoveProgress>(ShowProgress);
        MoveResult result;
        if (row.Moved is { } moved)
        {
            if (!Confirm(string.Format(LocalizationManager.T("Bring {0} back to this PC?\n\nIt needs {1} of space here. After that it starts fast and works without the internet."),
                    row.Name, ByteSize.Format(moved.Bytes)), LocalizationManager.T("Apps"))) return;
            bool keep = System.Windows.MessageBox.Show(System.Windows.Application.Current.MainWindow!,
                LocalizationManager.T("Keep the copy on the cloud drive as well?\n\nYes: it stays there as a spare copy.\nNo: it is deleted from the cloud drive after the program is back on this PC."),
                LocalizationManager.T("Apps"), System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question, System.Windows.MessageBoxResult.Yes) != System.Windows.MessageBoxResult.No;
            result = await RunMove(ct => Host.AppMover.ToPcAsync(moved, keep, progress, ct));
        }
        else
        {
            var why = await Task.Run(() => Host.AppMover.WhyNotNow(row.Item));
            if (why != null) { Say(LocalizationManager.T(why), "warn"); return; }
            long bytes = await Task.Run(() => AppCatalog.FolderBytes(row.Item.InstallDir));
            string text = string.Format(LocalizationManager.T("Move {0} ({1}) to the cloud drive?\n\nIts files leave this PC and that space becomes free. It then starts from the cloud drive: the first start is slower and it needs the internet. You can bring it back any time.\n\nHow long it takes depends on your upload speed: 1 GB is about 15 minutes at 10 Mbps, and hours on a slow phone hotspot."),
                row.Name, ByteSize.Format(bytes));
            if (Host.SharedClientRemotes.Count > 0)
                text += "\n\n" + LocalizationManager.T("Note: this PC still signs in to Google with the shared key, so Google may refuse a big upload. Switch to your own Google key first (Cloud Drives).");
            if (AppMover.LinkNeedsPermission)
                text += "\n\n" + LocalizationManager.T("At the end Windows asks once for your permission (Yes) to put a link where the folder was.");
            if (!Confirm(text, LocalizationManager.T("Apps"))) return;
            result = await RunMove(ct => Host.AppMover.ToCloudAsync(row.Item, progress, ct));
        }
        Say(result.Message, result.Success ? "ok" : "warn");
        if (result.Success) await LoadAsync();
    }

    private async Task<MoveResult> RunMove(Func<CancellationToken, Task<MoveResult>> work)
    {
        var cts = _move = new CancellationTokenSource();
        Moving = true;
        MoveFraction = 0;
        MoveStatus = "";
        try { return await Task.Run(() => work(cts.Token)); }
        finally
        {
            Moving = false;
            _move = null;
            cts.Dispose();
        }
    }

    [RelayCommand]
    private void StopMove() => _move?.Cancel();

    private void ShowProgress(MoveProgress p)
    {
        MoveFraction = p.TotalBytes > 0 ? Math.Clamp((double)p.DoneBytes / p.TotalBytes, 0, 1) : 0;
        var text = LocalizationManager.T(p.Step);
        if (p.TotalBytes > 0) text += $"  {ByteSize.Format(p.DoneBytes)} / {ByteSize.Format(p.TotalBytes)}";
        if (p.BytesPerSecond > 0)
        {
            text += $" · {ByteSize.Format((long)p.BytesPerSecond)}/s";
            if (p.TotalBytes > p.DoneBytes)
                text += " · " + string.Format(LocalizationManager.T("about {0} left"), Bandwidth.FormatDuration(TimeSpan.FromSeconds((p.TotalBytes - p.DoneBytes) / p.BytesPerSecond)));
        }
        MoveStatus = text;
    }

    [RelayCommand]
    private async Task PlayAsGame(AppRow? row)
    {
        if (row is null) return;
        if (Host.CurrentLaunch is { State: LaunchState.Running } running)
        {
            Say(string.Format(LocalizationManager.T("{0} is still running. Close it first."), running.Game.Name), "warn");
            return;
        }
        if (row.OnCloud) Say(string.Format(LocalizationManager.T("{0} is being fetched from the cloud drive. On a slow internet the first start can take many minutes; it opens by itself when it is ready."), row.Name), "accent");
        await RunAsync(async () =>
        {
            await Task.Run(() => Host.LaunchAppAsync(row.Item, asGame: true));
            Say(string.Format(LocalizationManager.T("{0} is starting with game mode: apps you do not need are moved out of RAM."), row.Name), "ok");
        });
    }
}
