using System.Security.Cryptography;
using System.Text;

namespace CloudSSD.Core;

/// <summary>
/// Protects the small secrets that live inside settings.json (the phone pairing token and the media
/// signing key) with Windows DPAPI, so the file on disk does not hand the token to anyone who can read
/// it. DPAPI ties the value to the same Windows user on the same machine, so a copied settings file is
/// useless elsewhere - the phone simply has to pair again.
/// </summary>
/// <remarks>
/// Format: the protected value is stored as "dpapi:&lt;base64&gt;". A value without that prefix is plain
/// text from an older settings file and is kept as it is, so upgrading never breaks an existing setup.
/// On a platform without DPAPI (or when protection fails) the value stays plain: a working but unprotected
/// setting beats a broken one.
/// </remarks>
public static class SecretProtector
{
    private const string Prefix = "dpapi:";

    // Same purpose as the rclone config password: only this app on this machine can unprotect it.
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("CloudSSD.v1.settings");

    /// <summary>True when the value is stored protected.</summary>
    public static bool IsProtected(string? value) =>
        value is not null && value.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>Returns the value as it should be written to disk: protected when possible, plain otherwise.</summary>
    public static string Protect(string? value)
    {
        if (string.IsNullOrEmpty(value) || IsProtected(value)) return value ?? "";
        try
        {
            var blob = ProtectedData.Protect(Encoding.UTF8.GetBytes(value), Entropy, DataProtectionScope.CurrentUser);
            return Prefix + Convert.ToBase64String(blob);
        }
        catch (Exception e) when (e is PlatformNotSupportedException or CryptographicException)
        {
            return value;   // no DPAPI here: keep the plain value rather than losing it
        }
    }

    /// <summary>Returns the real value: the protected one is opened, a plain one is returned as it is.</summary>
    public static string Unprotect(string? value)
    {
        if (string.IsNullOrEmpty(value) || !IsProtected(value)) return value ?? "";
        try
        {
            var blob = Convert.FromBase64String(value[Prefix.Length..]);
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(blob, Entropy, DataProtectionScope.CurrentUser));
        }
        catch (Exception e) when (e is PlatformNotSupportedException or CryptographicException or FormatException)
        {
            // Wrong machine or a damaged value: the token cannot be recovered, but nothing else breaks.
            return value;
        }
    }
}
