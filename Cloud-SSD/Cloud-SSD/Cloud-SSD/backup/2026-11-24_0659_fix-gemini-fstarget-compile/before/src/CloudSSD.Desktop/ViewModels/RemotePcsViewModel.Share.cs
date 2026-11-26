using System.Collections.ObjectModel;
using System.IO;
using CloudSSD.App;
using CloudSSD.App.Remote;
using CloudSSD.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CloudSSD.Desktop.ViewModels;

/// <summary>
/// "Other PCs", part two: files sent to this laptop through that PC (Accept / Decline), and asking that PC for access
/// when it wants something allowed first.
/// </summary>
public sealed partial class RemotePcsViewModel
{
    public ObservableCollection<InboxOfferItem> Inbox { get; } = new();
    [ObservableProperty] private bool _hasInbox;

    [ObservableProperty] private bool _showAccessAsk;
    [ObservableProperty] private string _accessText = "";
    [ObservableProperty] private bool _canAskAccess;
    private IReadOnlyList<string> _accessNeeded = [];

    private void RefreshInbox()
    {
        var s = _session;
        Inbox.ReplaceWith(s is null ? Enumerable.Empty<InboxOfferItem>() : s.Inbox.Select(o => new InboxOfferItem(o,
            L("From {0} · {1}", o.From.Length > 0 ? o.From : s.Pc.DisplayName, o.Items.Count == 1 ? T("1 file") : L("{0} files", o.Items.Count)) + (o.Total > 0 ? " · " + ByteSize.Format(o.Total) : ""),
            string.Join(", ", o.Items.Select(i => i.Name).Take(4)) + (o.Items.Count > 4 ? "…" : ""),
            o.State == "accepted" ? T("Coming — the rest is still on its way to that PC") : "",
            o.State == "offered")));
        HasInbox = Inbox.Count > 0;
    }

    [RelayCommand]
    private async Task AcceptOfferAsync(InboxOfferItem? item)
    {
        if (item is null || _session is not { } s) return;
        try
        {
            Say(L("Saving {0}…", item.Title), "muted");
            var saved = await s.AcceptAsync(item.Offer);
            var folder = saved.Count > 0 ? Path.GetDirectoryName(saved[0]) : null;
            Say(saved.Count == 0 ? T("Accepted. The files come as soon as they reach that PC.")
                : L("{0} saved in {1}", saved.Count == 1 ? Path.GetFileName(saved[0]) : L("{0} files", saved.Count), folder ?? ""), "ok");
        }
        catch (RemotePcException e) { Say(e.Message, "warn"); }
        catch (IOException e) { Say(e.Message, "error"); }
        RefreshInbox();
    }

    [RelayCommand]
    private async Task DeclineOfferAsync(InboxOfferItem? item)
    {
        if (item is null || _session is not { } s) return;
        try { await s.DeclineAsync(item.Offer); Say(T("Declined."), "muted"); }
        catch (RemotePcException e) { Say(e.Message, "warn"); }
        RefreshInbox();
    }

    // ---- that PC's phones, looked at through it -------------------------------------------------------------------

    public ObservableCollection<RemotePhoneItem> Phones { get; } = new();
    [ObservableProperty] private bool _hasPhones;
    [ObservableProperty] private string _phonesTitle = "";

    /// <summary>The phones and tablets paired with that PC (when it lets this one look at them through it).</summary>
    private async Task RefreshPhonesAsync()
    {
        var s = _session;
        if (s is null || !s.IsOnline || !s.Pc.Features.Contains("relay")) { Phones.Clear(); HasPhones = false; return; }
        try
        {
            var list = await s.PhonesAsync();
            if (s != _session) return;
            PhonesTitle = L("PHONES ON {0}", s.Pc.DisplayName.ToUpperInvariant());
            Phones.ReplaceWith(list.Select(p => new RemotePhoneItem(p,
                p.Listening ? T("Its app answers now") : p.Online ? T("Online — open Cloud-SSD on it to look") : T("Not connected now"))));
        }
        catch (RemotePcException) { Phones.Clear(); }   // not allowed (relay) or away: nothing to show
        HasPhones = Phones.Count > 0;
    }

    [RelayCommand]
    private void BrowseRemotePhone(RemotePhoneItem? item)
    {
        if (item is null || _session is not { } s) return;
        var phone = item.Phone;
        new Views.BrowsePhoneWindow(phone.Name, (kind, args, _) => s.AskPhoneAsync(phone.Id, kind, args))
        {
            Owner = System.Windows.Application.Current?.MainWindow
        }.Show();
    }

    /// <summary>That PC wants something allowed first: say what, and offer to ask it.</summary>
    private void OfferToAskAccess(RemotePcSession s, RemotePcException e)
    {
        _accessNeeded = e.Categories.Count > 0 ? e.Categories : [AccessCategories.Browse];
        AccessText = L("{0} has not allowed this laptop to {1} yet. Ask it — someone at that PC answers there.",
            s.Pc.DisplayName, string.Join(", ", _accessNeeded.Select(c => T(AccessCategories.Describe(c)).ToLowerInvariant())));
        CanAskAccess = true;
        ShowAccessAsk = true;
    }

    [RelayCommand]
    private async Task AskAccessAsync()
    {
        if (_session is not { } s || !CanAskAccess) return;
        CanAskAccess = false;
        try
        {
            var req = await s.RequestAccessAsync(_accessNeeded);
            AccessText = L("Asked {0}. Waiting for someone there to answer…", s.Pc.DisplayName);
            // The person at that PC answers there (Allow always / for an hour / Deny); the question lasts two minutes.
            var until = DateTimeOffset.Now.AddSeconds(Math.Max(30, req.ExpiresIn) + 5);
            while (DateTimeOffset.Now < until && _session == s)
            {
                await Task.Delay(2000);
                var now = await s.AccessRequestAsync();
                if (now is null || now.Id != req.Id || now.State == "pending") continue;
                if (now.State == "allowed")
                {
                    ShowAccessAsk = false;
                    Say(L("{0} allowed it.", s.Pc.DisplayName), "ok");
                    await LoadAsync();
                    return;
                }
                AccessText = now.State == "denied" ? L("{0} said no.", s.Pc.DisplayName) : L("No answer from {0}. Ask again later.", s.Pc.DisplayName);
                CanAskAccess = now.State != "denied";
                return;
            }
            AccessText = L("No answer from {0}. Ask again later.", s.Pc.DisplayName);
            CanAskAccess = true;
        }
        catch (RemotePcException e)
        {
            AccessText = e.Message;
            CanAskAccess = e.Kind != RemoteErrorKind.Locked;
        }
    }
}

/// <summary>One of that PC's phones on the laptop page, with whether its app answers now.</summary>
public sealed record RemotePhoneItem(RemotePhone Phone, string Status)
{
    public string Name => Phone.Name;
}

/// <summary>One offer on the laptop page: who sent what; Accept/Decline while it waits for an answer.</summary>
public sealed record InboxOfferItem(RemoteShareOffer Offer, string Title, string Names, string StateText, bool CanAnswer)
{
    public bool HasState => StateText.Length > 0;
}
