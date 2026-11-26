using Microsoft.Win32;

namespace CloudSSD.App;

/// <summary>
/// "Start Cloud-SSD with Windows": an entry for this user under Windows' Run key that starts the app minimized after
/// sign-in. The cloud copy of the PC's files ("Files when it is off") only keeps up while the app runs.
/// </summary>
public static class StartWithWindows
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run", Name = "Cloud-SSD";
    /// <summary>The argument the entry starts the app with: the window opens minimized.</summary>
    public const string MinimizedArgument = "--minimized";

    /// <summary>The app's own program file, or null when it runs inside another one (dotnet.exe, a test runner).</summary>
    public static string? AppPath =>
        Environment.ProcessPath is { } p && Path.GetFileName(p).StartsWith("Cloud-SSD", StringComparison.OrdinalIgnoreCase) ? p : null;

    public static bool IsOn()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(Name) is string s && s.Length > 0;
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return false; }
    }

    /// <summary>Adds or removes the entry. False when it could not be written, or the app's own program file is not known.</summary>
    public static bool Set(bool on)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (!on) { key.DeleteValue(Name, false); return true; }
            if (AppPath is not { } exe) return false;
            key.SetValue(Name, $"\"{exe}\" {MinimizedArgument}");
            return true;
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return false; }
    }
}
