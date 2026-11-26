using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using CloudSSD.Core;
using CloudSSD.Diagnostics;

namespace CloudSSD.App;

/// <summary>What installing WinFsp came to. Outcome: installed, already, no-installer, not-genuine, declined, failed.</summary>
public sealed record WinFspInstallResult(bool Installed, string Outcome, int ExitCode = 0);

/// <summary>
/// WinFsp is the small Windows driver that makes a cloud drive show up as Z: or Y:. Its official installer comes with
/// Cloud-SSD (tools\winfsp), so the app can put it on the PC itself when it is missing: the person only says Yes once
/// to Windows' own question. Only the genuine installer is ever run: it must carry a valid signature of its maker.
/// </summary>
public static class WinFspSetup
{
    /// <summary>The maker of WinFsp, as its installer's signature names it.</summary>
    public const string Publisher = "NAVIMATICS LLC";

    public static bool Installed => WinFspDetector.Detect().Installed;

    /// <summary>The installer that came with the app (the newest "winfsp*.msi" in tools\winfsp), or null.</summary>
    public static string? BundledInstaller(string? appDirectory = null)
    {
        var dir = Path.Combine(appDirectory ?? AppPaths.AppDirectory, "tools", "winfsp");
        try
        {
            return Directory.Exists(dir)
                ? Directory.EnumerateFiles(dir, "winfsp*.msi").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).LastOrDefault()
                : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>Is this file signed, the signature valid, and the signer WinFsp's maker?</summary>
    public static bool IsGenuine(string file)
    {
        try
        {
            if (!SignatureIsValid(file)) return false;
#pragma warning disable SYSLIB0057 // the signer of a signed file: there is no newer call for it
            using var cert = X509Certificate.CreateFromSignedFile(file);
#pragma warning restore SYSLIB0057
            return cert.Subject.Contains(Publisher, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is CryptographicException or IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>
    /// Installs WinFsp from the installer that came with the app. Windows asks for permission once (the installer needs
    /// administrator rights); saying No there is "declined". Nothing is downloaded.
    /// </summary>
    public static async Task<WinFspInstallResult> InstallAsync(string? appDirectory = null, CancellationToken ct = default)
    {
        if (Installed) return new WinFspInstallResult(true, "already");
        if (BundledInstaller(appDirectory) is not { } msi) return new WinFspInstallResult(false, "no-installer");
        if (!IsGenuine(msi)) return new WinFspInstallResult(false, "not-genuine");
        int code;
        try
        {
            var psi = new ProcessStartInfo("msiexec.exe", $"/i \"{msi}\" /passive /norestart") { UseShellExecute = true, Verb = "runas" };
            using var p = Process.Start(psi);
            if (p == null) return new WinFspInstallResult(false, "failed");
            await p.WaitForExitAsync(ct);
            code = p.ExitCode;
        }
        catch (Win32Exception e) when (e.NativeErrorCode == 1223)   // the person said No to Windows' question
        {
            return new WinFspInstallResult(false, "declined");
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException)
        {
            return new WinFspInstallResult(false, "failed");
        }
        // 3010 = installed, Windows wants a restart some time; 1602 = cancelled inside the installer.
        if (Installed) return new WinFspInstallResult(true, "installed", code);
        return new WinFspInstallResult(false, code == 1602 ? "declined" : "failed", code);
    }

    // ---- Windows' own check of a file's signature (WinVerifyTrust) -------------------------------------------------

    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustFileInfo
    {
        public uint Size;
        public IntPtr FilePath, File, KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustData
    {
        public uint Size;
        public IntPtr PolicyCallbackData, SipClientData;
        public uint UiChoice, RevocationChecks, UnionChoice;
        public IntPtr FileInfo;
        public uint StateAction;
        public IntPtr StateData, UrlReference;
        public uint ProvFlags, UiContext;
        public IntPtr SignatureSettings;
    }

    [DllImport("wintrust.dll", ExactSpelling = true)]
    private static extern int WinVerifyTrust(IntPtr window, ref Guid action, ref WinTrustData data);

    private static bool SignatureIsValid(string file)
    {
        const uint NoUi = 2, NoRevocation = 0, ChoiceFile = 1, Verify = 1, Close = 2, CacheOnly = 0x1000;
        var path = Marshal.StringToHGlobalUni(file);
        var info = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
        try
        {
            Marshal.StructureToPtr(new WinTrustFileInfo { Size = (uint)Marshal.SizeOf<WinTrustFileInfo>(), FilePath = path }, info, false);
            var data = new WinTrustData
            {
                Size = (uint)Marshal.SizeOf<WinTrustData>(), UiChoice = NoUi, RevocationChecks = NoRevocation, UnionChoice = ChoiceFile,
                FileInfo = info, StateAction = Verify, ProvFlags = CacheOnly
            };
            var action = GenericVerifyV2;
            int result = WinVerifyTrust(IntPtr.Zero, ref action, ref data);
            data.StateAction = Close;
            WinVerifyTrust(IntPtr.Zero, ref action, ref data);
            return result == 0;
        }
        finally
        {
            Marshal.FreeHGlobal(info);
            Marshal.FreeHGlobal(path);
        }
    }
}
