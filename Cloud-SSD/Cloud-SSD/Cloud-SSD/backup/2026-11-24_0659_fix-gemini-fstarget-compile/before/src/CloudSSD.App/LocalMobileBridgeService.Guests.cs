using System.Security.Cryptography;
using System.Text;
using CloudSSD.Core;

namespace CloudSSD.App;

/// <summary>
/// A guest code made at the PC ("Add guest"): six digits for one device, for 15 minutes. The device that pairs with it
/// may do only the categories chosen, until <see cref="For"/> after pairing; then it is removed.
/// </summary>
public sealed record GuestInvite(string Code, DateTimeOffset CodeExpiresAt, TimeSpan For, IReadOnlyList<string> Categories);

public sealed partial class LocalMobileBridgeService
{
    /// <summary>How long a guest code can be typed in.</summary>
    public static readonly TimeSpan GuestCodeLifetime = TimeSpan.FromMinutes(15);
    /// <summary>The longest a guest may stay.</summary>
    public static readonly TimeSpan MaxGuestTime = TimeSpan.FromDays(7);

    private readonly List<GuestInvite> _guestInvites = new();

    /// <summary>Raised when a guest code is made, used or runs out (the window showing it updates).</summary>
    public event Action? GuestInvitesChanged;

    /// <summary>A new one-time guest code (never the same as the pairing code or another waiting guest code).</summary>
    public GuestInvite CreateGuestInvite(TimeSpan stay, IEnumerable<string> categories)
    {
        var cats = categories.Where(AccessCategories.IsKnown).Distinct().ToList();
        if (cats.Count == 0) throw new ArgumentException("Choose at least one thing the guest may do.", nameof(categories));
        if (stay <= TimeSpan.Zero || stay > MaxGuestTime) throw new ArgumentOutOfRangeException(nameof(stay), "A guest stays up to 7 days.");
        GuestInvite invite;
        lock (_codeLock)
        {
            var now = Now();
            _guestInvites.RemoveAll(i => i.CodeExpiresAt <= now);
            string code;
            do code = NewPairingCode();
            while (code == PairingCode || _guestInvites.Any(i => i.Code == code));
            invite = new GuestInvite(code, now + GuestCodeLifetime, stay, cats);
            _guestInvites.Add(invite);
        }
        GuestInvitesChanged?.Invoke();
        return invite;
    }

    /// <summary>The guest codes that can still be used.</summary>
    public IReadOnlyList<GuestInvite> GuestInvites
    {
        get
        {
            lock (_codeLock)
            {
                var now = Now();
                return _guestInvites.Where(i => i.CodeExpiresAt > now).ToList();
            }
        }
    }

    public void CancelGuestInvite(string code)
    {
        lock (_codeLock) _guestInvites.RemoveAll(i => i.Code == code);
        GuestInvitesChanged?.Invoke();
    }

    /// <summary>A typed code that is a waiting guest code: used up (once only), from the local network only.</summary>
    internal GuestInvite? TakeGuestInvite(string? code, string clientIp)
    {
        if (string.IsNullOrWhiteSpace(code) || !IsLocalNetworkAddress(clientIp)) return null;
        var digits = Encoding.ASCII.GetBytes(new string(code.Where(char.IsAsciiDigit).ToArray()));
        GuestInvite? hit = null;
        lock (_codeLock)
        {
            var now = Now();
            foreach (var i in _guestInvites)   // every one compared: the same time for any code
                if (i.CodeExpiresAt > now && digits.Length == i.Code.Length && CryptographicOperations.FixedTimeEquals(digits, Encoding.ASCII.GetBytes(i.Code)))
                    hit = i;
            if (hit != null) _guestInvites.Remove(hit);
        }
        if (hit != null) GuestInvitesChanged?.Invoke();
        return hit;
    }

    /// <summary>The device that just paired with a guest code: only its categories, until its time is up.</summary>
    private void MakeGuest(PairedDevice d, GuestInvite invite)
    {
        var now = Now();
        var until = now + invite.For;
        lock (_devicesLock)
        {
            d.GuestUntil = until;
            d.Grants = invite.Categories.Select(c => new AccessGrant { Category = c, Mode = "timed", ExpiresAt = until, GrantedAt = now }).ToList();
        }
        _host.SaveSettings();
        RaiseDevicesChanged();
    }

    /// <summary>Removes the guests whose time is up (from the presence sweep). Returns how many.</summary>
    internal int EndExpiredGuests()
    {
        var now = Now();
        List<string> ids;
        lock (_devicesLock)
            ids = _host.Settings.MobileBridge.PairedDevices.Where(d => d.GuestUntil is { } u && u <= now).Select(d => d.Id).ToList();
        foreach (var id in ids) RemoveDevice(id);
        return ids.Count;
    }
}
