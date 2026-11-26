using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CloudSSD.App;
using CloudSSD.App.Remote;
using CloudSSD.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CloudSSD.Desktop.ViewModels;

/// <summary>
/// "Other PCs": this PC (a laptop) browsing another PC it paired with — live when that PC answers, else what it showed
/// last time, with when. Files sent to it wait in a queue until it is back.
/// </summary>
public sealed partial class RemotePcsViewModel : PageViewModel
{
    private RemotePcSession? _session;
    private DateTimeOffset _lastTimes;
    private readonly HashSet<RemotePcSession> _hooked = new();

    public RemotePcsViewModel(AppHost host) : base(host, "Other PCs", "")
    {
        LocalizationManager.LanguageChanged += () => Ui(() => { RefreshPcs(); _ = LoadAsync(); });
    }

    private RemotePcManager Mgr => Host.RemotePcs;

    public ObservableCollection<RemotePcItem> Pcs { get; } = new();
    public ObservableCollection<CrumbItem> Crumbs { get; } = new();
    public ObservableCollection<RemoteEntryItem> Entries { get; } = new();
    public ObservableCollection<UploadItem> Uploads { get; } = new();

    [ObservableProperty] private RemotePcItem? _selectedPc;
    [ObservableProperty] private string? _currentPath;           // null = the drives
    [ObservableProperty] private bool _isOnline;
    [ObservableProperty] private bool _showBanner;
    [ObservableProperty] private string _bannerText = "";
    [ObservableProperty] private string _headerTitle = "";
    [ObservableProperty] private string _headerSub = "";
    [ObservableProperty] private bool _hasPcs;
    [ObservableProperty] private bool _hasUploads;
    [ObservableProperty] private string _emptyText = "";
    private DateTimeOffset _shownSavedAt;
    private readonly Dictionary<string, string> _rootTitles = new();

    private static string L(string format, params object[] a) => string.Format(LocalizationManager.T(format), a);
    private static string T(string s) => LocalizationManager.T(s);

    public override void OnShown()
    {
        base.OnShown();
        Mgr.Start();
        RefreshPcs();
        if (_session != null) _session.Foreground = true;
        _ = LoadAsync();
    }

    public override void OnMetrics(EngineMetrics m)
    {
        base.OnMetrics(m);
        if (_session != null) _session.Foreground = true;
        var now = DateTimeOffset.Now;
        if (now - _lastTimes < TimeSpan.FromSeconds(10)) return;
        _lastTimes = now;
        RefreshPcs();
        UpdateBanner();
    }

    private void Hook(RemotePcSession s)
    {
        if (!_hooked.Add(s)) return;
        s.StateChanged += () => Ui(() =>
        {
            RefreshPcs();
            if (s != _session) return;
            bool cameBack = s.IsOnline && !IsOnline;
            IsOnline = s.IsOnline;
            UpdateBanner();
            if (cameBack) _ = LoadAsync();
        });
        // Runs inline when raised on this thread (by our own load), so it must never start a request that raises it again.
        s.ListChanged += path => Ui(() =>
        {
            if (s != _session || path != CurrentPath || Busy) return;              // a load running now shows the fresh list anyway
            if (s.Saved(path) is { } saved && saved.SavedAt > _shownSavedAt) ShowList(s, saved);   // refreshed in the background
            else _ = LoadAsync();                                                   // changed there (a file we sent arrived): ask once
        });
        s.UploadsChanged += () => Ui(RefreshUploads);
        s.PinsChanged += () => Ui(() => { if (s == _session) OnPinsChanged(s); });
        s.InboxChanged += () => Ui(() => { if (s == _session) RefreshInbox(); });
        s.FileReceived += path => Ui(() => Say(L("{0} arrived from {1} (Downloads\\Cloud-SSD\\Received)", Path.GetFileName(path), s.Pc.DisplayName), "ok"));
    }

    /// <summary>The PCs on the left with a dot: online now, or when they were last seen.</summary>
    private void RefreshPcs()
    {
        var sessions = Mgr.Sessions;
        foreach (var s in sessions) Hook(s);
        var activeId = Mgr.Active?.Pc.PcId;
        var items = sessions.Select(s => new RemotePcItem(s.Pc.PcId, s.Pc.DisplayName,
            s.State switch
            {
                RemoteLinkState.Online => T("● Online now"),
                RemoteLinkState.NeedsRepair => T("Pair again"),
                _ => s.Pc.LastOnlineAt is { } at ? L("○ Last seen {0}", Fmt.Ago(at, DateTimeOffset.Now)) : T("Not connected yet")
            },
            s.State switch { RemoteLinkState.Online => "ok", RemoteLinkState.NeedsRepair => "error", _ => "muted" })).ToList();
        // Update in place so the selection stays.
        for (int i = 0; i < items.Count; i++)
        {
            if (i < Pcs.Count && Pcs[i].PcId == items[i].PcId) { Pcs[i].Name = items[i].Name; Pcs[i].Status = items[i].Status; Pcs[i].Tone = items[i].Tone; }
            else if (i < Pcs.Count) Pcs[i] = items[i];
            else Pcs.Add(items[i]);
        }
        while (Pcs.Count > items.Count) Pcs.RemoveAt(Pcs.Count - 1);
        HasPcs = Pcs.Count > 0;
        if (SelectedPc?.PcId != activeId) SelectedPc = Pcs.FirstOrDefault(p => p.PcId == activeId) ?? Pcs.FirstOrDefault();
        EmptyText = HasPcs ? "" : T("No other PC yet. Press “+ Add a PC” and type the address and code shown on that PC (Devices → Add a device).");
    }

    partial void OnSelectedPcChanged(RemotePcItem? value)
    {
        if (value is null) { _session = null; Entries.Clear(); Crumbs.Clear(); return; }
        if (_session?.Pc.PcId == value.PcId) return;
        if (_session != null) _session.Foreground = false;
        Mgr.SetActive(value.PcId);
        _session = Mgr.Session(value.PcId);
        if (_session != null) { Hook(_session); _session.Foreground = true; IsOnline = _session.IsOnline; }
        CurrentPath = null;
        ShowAccessAsk = false;
        RefreshUploads();
        RefreshInbox();
        Phones.Clear();
        HasPhones = false;
        _ = LoadAsync();
    }

    /// <summary>The drives (CurrentPath null) or a folder: live, else kept, with the bar saying since when.</summary>
    private async Task LoadAsync()
    {
        var s = _session;
        if (s is null) { Entries.Clear(); Crumbs.Clear(); HeaderTitle = ""; HeaderSub = ""; ShowBanner = false; return; }
        HeaderTitle = s.Pc.DisplayName;
        s.OpenPath = CurrentPath;
        Busy = true;
        try
        {
            if (s.State == RemoteLinkState.Unknown) await s.TickAsync();
            IsOnline = s.IsOnline;
            if (CurrentPath is null)
            {
                var roots = await s.GetRootsAsync();
                if (s != _session || CurrentPath != null) return;                  // moved on meanwhile
                _shownSavedAt = roots.SavedAt;
                foreach (var r in roots.Value) _rootTitles[r.Id] = r.Title;
                Entries.ReplaceWith(roots.Value.Select(r => RemoteEntryItem.ForRoot(r, s.PinOf(r.Id + ":") != null)));
                HeaderSub = roots.FromCache ? L("Saved {0}", Fmt.Ago(roots.SavedAt, DateTimeOffset.Now))
                    : roots.Value.Count == 1 ? T("1 drive") : L("{0} drives", roots.Value.Count);
            }
            else
            {
                var path = CurrentPath;
                var list = await s.ListAsync(path);
                if (s != _session || CurrentPath != path) return;                  // moved on meanwhile
                ShowList(s, list.Value);
            }
            Message = null;
            if (CanAskAccess) ShowAccessAsk = false;   // allowed meanwhile (not while a question waits)
            if (CurrentPath is null) _ = RefreshPhonesAsync();   // its phones, with the drives
        }
        catch (RemotePcException e) when (e.Kind == RemoteErrorKind.NeedsPermission)
        {
            Entries.Clear();
            OfferToAskAccess(s, e);
        }
        catch (RemotePcException e)
        {
            Entries.Clear();
            Say(e.Kind == RemoteErrorKind.Offline ? L("{0} is offline, and this was not opened while it was on.", s.Pc.DisplayName) : e.Message, "warn");
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Started without awaiting, so nothing above would see it: show it instead of failing silently.
            Say(e.Message, "error");
        }
        finally { Busy = false; }
        BuildCrumbs();
        UpdateBanner();
    }

    private void ShowList(RemotePcSession s, CachedList list)
    {
        _shownSavedAt = list.SavedAt;
        var now = DateTimeOffset.Now;
        Entries.ReplaceWith(list.Entries.OrderBy(e => !e.IsDirectory).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .Select(e => RemoteEntryItem.For(e, ListDiff.IsNew(list, e.Id, now), s.PinOf(e.Id) != null)));
        _shownCounts = L("{0} folders · {1} files", list.Entries.Count(e => e.IsDirectory), list.Entries.Count(e => !e.IsDirectory));
        HeaderSub = _shownCounts + PinNote(s, list.Path);
    }

    private string _shownCounts = "";

    /// <summary>" · Kept offline · updated 10:42" when the open folder is kept on this PC.</summary>
    private string PinNote(RemotePcSession s, string path)
    {
        if (s.PinOf(path) is not { } pin) return "";
        var state = pin.Error is { } err ? L("Kept offline · {0}", err)
            : pin.NeedsOkBytes is not null ? T("Kept offline · waiting for your OK")
            : pin.LastSyncAt is { } at ? L("Kept offline · updated {0}", Fmt.Ago(at, DateTimeOffset.Now))
            : T("Kept offline · getting the files…");
        return "   ·   " + state;
    }

    private readonly HashSet<string> _askedPins = new();

    /// <summary>Badges and the header follow the pins; a big folder asks once before it is fetched.</summary>
    private void OnPinsChanged(RemotePcSession s)
    {
        if (CurrentPath is { } path && !Busy && s.Saved(path) is { } list) ShowList(s, list);
        foreach (var pin in s.Pins.Where(p => p.NeedsOkBytes is not null).ToList())
        {
            if (!_askedPins.Add(pin.Path + "|" + pin.NeedsOkBytes)) continue;
            var name = pin.Path[(Math.Max(pin.Path.LastIndexOf('/'), pin.Path.IndexOf(':')) + 1)..];
            if (Confirm(L("{0} is {1}. Keep all of it on this PC?", name.Length > 0 ? name : pin.Path, ByteSize.Format(pin.NeedsOkBytes!.Value)))) s.ApprovePin(pin.Path);
            else s.Unpin(pin.Path);
        }
    }

    /// <summary>The menu's wording for the chosen item.</summary>
    public string KeepOfflineText(RemoteEntryItem? item) =>
        item != null && _session?.Pins.Any(p => string.Equals(p.Path, item.Id, StringComparison.OrdinalIgnoreCase)) == true
            ? T("Remove offline copy") : T("Keep offline");

    [RelayCommand]
    private void KeepOffline(RemoteEntryItem? item)
    {
        var s = _session;
        if (item is null || s is null) return;
        if (s.Pins.Any(p => string.Equals(p.Path, item.Id, StringComparison.OrdinalIgnoreCase)))
        {
            s.Unpin(item.Id);
            Say(L("Offline copy of {0} removed.", item.Name), "ok");
        }
        else if (s.PinOf(item.Id) != null) Say(T("The folder above is already kept offline."), "ok");
        else
        {
            try { s.Pin(item.Id, isFile: !item.IsFolder); }
            catch (RemotePcException e) { Say(e.Message, "warn"); return; }
            Say(s.IsOnline ? L("{0} is kept on this PC: it opens even when {1} is off.", item.Name, s.Pc.DisplayName)
                           : L("{0} will be kept on this PC when {1} is back.", item.Name, s.Pc.DisplayName), "ok");
        }
        if (CurrentPath is { } path && s.Saved(path) is { } list) ShowList(s, list);
        else if (CurrentPath is null) _ = LoadAsync();
    }

    private void UpdateBanner()
    {
        var s = _session;
        ShowBanner = s != null && !s.IsOnline && HasPcs;
        if (!ShowBanner || s is null) return;
        var when = _shownSavedAt != default ? _shownSavedAt : s.Pc.LastOnlineAt ?? default;
        BannerText = s.State == RemoteLinkState.NeedsRepair
            ? L("{0} no longer accepts this PC. Forget it and add it again with a new code.", s.Pc.DisplayName)
            : when == default ? L("{0} is offline.", s.Pc.DisplayName)
            : L("Offline · last update {0} ({1}) — showing what was saved on this PC.", when.ToLocalTime().ToString("HH:mm"), Fmt.Ago(when, DateTimeOffset.Now));
    }

    private void BuildCrumbs()
    {
        var list = new List<CrumbItem> { new(T("Drives"), null) };
        if (CurrentPath is { } p)
        {
            int colon = p.IndexOf(':');
            var drive = p[..(colon + 1)];
            list.Add(new CrumbItem(_rootTitles.GetValueOrDefault(drive.TrimEnd(':')) ?? drive.TrimEnd(':'), drive));
            var acc = drive;
            foreach (var part in p[(colon + 1)..].Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                acc = acc.EndsWith(':') ? acc + part : acc + "/" + part;
                list.Add(new CrumbItem(part, acc));
            }
        }
        Crumbs.ReplaceWith(list);
    }

    private void RefreshUploads()
    {
        var s = _session;
        Uploads.ReplaceWith(s is null ? Enumerable.Empty<UploadItem>() : s.Uploads.Where(u => u.State != "done" || DateTimeOffset.Now - u.CreatedAt < TimeSpan.FromMinutes(10))
            .Select(u => new UploadItem(u.Name, u.State switch
            {
                "queued" when s.IsOnline => T("Waiting to send"),
                "queued" => L("Queued — sends when {0} is back", s.Pc.DisplayName),
                "running" => u.Size > 0 ? L("Sending {0}%", u.SentBytes * 100 / u.Size) : T("Sending"),
                "done" => T("Sent"),
                _ => u.Error ?? T("Failed")
            }, u.Size > 0 ? u.SentBytes * 100.0 / u.Size : 0, u.State == "failed")));
        HasUploads = Uploads.Count > 0;
    }

    // ---- moving around -----------------------------------------------------------------------------------------------

    [RelayCommand]
    private void OpenEntry(RemoteEntryItem? item)
    {
        if (item is null || _session is null) return;
        if (item.IsFolder)
        {
            if (CurrentPath != null) _session.MarkSeen(CurrentPath);
            CurrentPath = item.Id.EndsWith(':') || item.IsRoot ? item.Id.TrimEnd(':') + ":" : item.Id;
            _ = LoadAsync();
            return;
        }
        _ = OpenFileAsync(item);
    }

    private async Task OpenFileAsync(RemoteEntryItem item)
    {
        if (_session is null || item.Entry is null) return;
        if (!RemoteFiles.IsSafeToOpen(item.Name)) { Say(T("Programs and scripts are not opened directly. Download them first."), "warn"); return; }
        await RunAsync(async () =>
        {
            var path = await _session.OpenAsync(item.Entry);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        });
    }

    [RelayCommand]
    private void NavigateCrumb(CrumbItem? c)
    {
        if (c is null || _session is null) return;
        if (CurrentPath != null) _session.MarkSeen(CurrentPath);
        CurrentPath = c.Path;
        _ = LoadAsync();
    }

    [RelayCommand]
    private void Up()
    {
        if (CurrentPath is null || _session is null) return;
        _session.MarkSeen(CurrentPath);
        int slash = CurrentPath.LastIndexOf('/');
        CurrentPath = slash > 0 ? CurrentPath[..slash] : CurrentPath.EndsWith(':') ? null : CurrentPath[..(CurrentPath.IndexOf(':') + 1)];
        _ = LoadAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (_session is null) return;
        await _session.TickAsync();
        await LoadAsync();
        RefreshPcs();
    }

    // ---- files -----------------------------------------------------------------------------------------------------------

    [RelayCommand]
    private Task DownloadAsync(RemoteEntryItem? item)
    {
        if (item?.Entry is null || item.IsFolder || _session is null) return Task.CompletedTask;
        return RunAsync(async () =>
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            var path = await _session.DownloadAsync(item.Entry, folder);
            Say(L("Saved to {0}", path), "ok");
        });
    }

    [RelayCommand]
    private void Upload()
    {
        if (_session is null) return;
        if (CurrentPath is null) { Say(T("Open a drive or folder first, then send files into it."), "warn"); return; }
        var dlg = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Title = T("Send files to this folder") };
        if (dlg.ShowDialog() != true) return;
        EnqueueDrop(dlg.FileNames);
    }

    /// <summary>Files dropped on the page (or chosen): queued for the open folder; sent now or when that PC is back.</summary>
    public void EnqueueDrop(string[] paths)
    {
        if (_session is null || CurrentPath is null) { Say(T("Open a drive or folder first, then send files into it."), "warn"); return; }
        int n = _session.EnqueueFiles(paths, CurrentPath);
        Say(_session.IsOnline ? L("Sending {0} file(s)…", n) : L("{0} file(s) queued — they go when {1} is back.", n, _session.Pc.DisplayName), "ok");
        RefreshUploads();
    }

    [RelayCommand]
    private Task NewFolderAsync()
    {
        if (_session is null || CurrentPath is null) return Task.CompletedTask;
        var name = Views.PromptWindow.Ask(T("New folder"), T("Name of the new folder"), "");
        if (string.IsNullOrWhiteSpace(name)) return Task.CompletedTask;
        return RunAsync(async () => { await _session.CreateFolderAsync(CurrentPath, name.Trim()); await LoadAsync(); });
    }

    [RelayCommand]
    private Task RenameAsync(RemoteEntryItem? item)
    {
        if (item?.Entry is null || _session is null || item.IsRoot) return Task.CompletedTask;
        var name = Views.PromptWindow.Ask(T("Rename"), T("New name"), item.Name);
        if (string.IsNullOrWhiteSpace(name) || name == item.Name) return Task.CompletedTask;
        return RunAsync(async () => { await _session.RenameAsync(item.Id, name.Trim()); await LoadAsync(); });
    }

    [RelayCommand]
    private Task DeleteAsync(RemoteEntryItem? item)
    {
        if (item?.Entry is null || _session is null || item.IsRoot) return Task.CompletedTask;
        if (!Confirm(L("Delete {0} on {1}? It can be brought back from the trash on Google Drive, or from that PC's Recycle Bin.", item.Name, _session.Pc.DisplayName))) return Task.CompletedTask;
        return RunAsync(async () => { await _session.DeleteAsync(item.Id); await LoadAsync(); });
    }

    // ---- PCs ------------------------------------------------------------------------------------------------------------

    [RelayCommand]
    private void AddPc()
    {
        var w = new Views.AddRemotePcWindow(Host) { Owner = Application.Current?.MainWindow };
        if (w.ShowDialog() == true) { RefreshPcs(); _session = null; SelectedPc = Pcs.FirstOrDefault(p => p.PcId == Mgr.Active?.Pc.PcId); }
    }

    [RelayCommand]
    private Task ForgetPcAsync()
    {
        var s = _session;
        if (s is null) return Task.CompletedTask;
        if (!Confirm(L("Forget {0}? This PC removes its key and everything saved from it. Adding it again needs a new code.", s.Pc.DisplayName))) return Task.CompletedTask;
        return RunAsync(async () =>
        {
            await Mgr.ForgetAsync(s.Pc.PcId);
            _session = null;
            _hooked.Remove(s);
            RefreshPcs();
        });
    }

    [RelayCommand]
    private void ClearSent()
    {
        _session?.ClearFinishedUploads();
        RefreshUploads();
    }
}

public sealed partial class RemotePcItem(string pcId, string name, string status, string tone) : ObservableObject
{
    public string PcId { get; } = pcId;
    [ObservableProperty] private string _name = name;
    [ObservableProperty] private string _status = status;
    [ObservableProperty] private string _tone = tone;
}

public sealed record CrumbItem(string Title, string? Path);

public sealed record UploadItem(string Name, string StateText, double Percent, bool Failed);

/// <summary>One row: a drive, folder or file on the other PC.</summary>
public sealed record RemoteEntryItem(string Id, string Name, string Glyph, string SizeText, string ModifiedText, bool IsFolder, bool IsRoot, bool IsNew, bool KeptOffline, RemoteEntry? Entry)
{
    public static RemoteEntryItem ForRoot(RemoteRoot r, bool kept = false) => new(r.Id + ":", r.Title, "",
        r.TotalBytes is { } t && r.FreeBytes is { } f ? string.Format(LocalizationManager.T("{0} free of {1}"), ByteSize.Format(f), ByteSize.Format(t)) : "", "", true, true, false, kept, null);

    public static RemoteEntryItem For(RemoteEntry e, bool isNew, bool kept) => new(e.Id, e.Name,
        e.IsDirectory ? "" : RemoteFiles.Glyph(e.Name),
        e.IsDirectory ? "" : e.Size is { } s ? ByteSize.Format(s) : "",
        e.Modified > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(e.Modified).ToLocalTime().ToString("d MMM yyyy HH:mm") : "",
        e.IsDirectory, false, isNew, kept, e);
}

/// <summary>Small rules for files from another PC.</summary>
public static class RemoteFiles
{
    private static readonly HashSet<string> Unsafe = new(StringComparer.OrdinalIgnoreCase)
    { ".exe", ".msi", ".bat", ".cmd", ".com", ".scr", ".ps1", ".vbs", ".js", ".jse", ".wsf", ".hta", ".lnk", ".reg", ".cpl", ".msc", ".jar", ".pif", ".url" };

    /// <summary>Programs and scripts are never started by a double-click; they are downloaded on purpose instead.</summary>
    public static bool IsSafeToOpen(string name) => !Unsafe.Contains(Path.GetExtension(name));

    public static string Glyph(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" or ".heic" or ".bmp" => "",
        ".mp4" or ".mkv" or ".mov" or ".avi" or ".webm" => "",
        ".mp3" or ".wav" or ".flac" or ".m4a" or ".ogg" => "",
        ".pdf" => "",
        ".zip" or ".rar" or ".7z" => "",
        _ => ""
    };
}
