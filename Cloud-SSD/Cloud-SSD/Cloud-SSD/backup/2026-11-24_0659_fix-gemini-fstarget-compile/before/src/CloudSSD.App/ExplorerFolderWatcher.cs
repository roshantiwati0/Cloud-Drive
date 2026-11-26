using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace CloudSSD.App;

/// <summary>
/// Tells which folders are open in File Explorer right now, so their files can be fetched ahead before the person
/// clicks one. It asks Windows' shell for its open windows every couple of seconds (the same object the "Quick
/// access" reader uses); it reads only each window's folder path and changes nothing.
/// It does not depend on rclone's log: on 17 Nov 2026 that log stood still for hours (a stuck older rclone kept the
/// file from being rotated) and with it everything that learned from the log.
/// </summary>
public sealed class ExplorerFolderWatcher : IDisposable
{
    private readonly Action<string> _opened;
    private readonly ILogger _log;
    private readonly CancellationTokenSource _cts = new();
    private readonly Dictionary<string, DateTime> _seen = new(StringComparer.OrdinalIgnoreCase);
    private Thread? _thread;

    /// <summary>A folder is reported again when it is still (or again) open after this long.</summary>
    public static readonly TimeSpan Again = TimeSpan.FromMinutes(5);

    public ExplorerFolderWatcher(Action<string> opened, ILogger log)
    {
        _opened = opened;
        _log = log;
    }

    public void Start()
    {
        if (_thread != null) return;
        _thread = new Thread(Loop) { IsBackground = true, Name = "Explorer folders" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    /// <summary>"file:///G:/My%20Photos" → "G:\My Photos"; null for anything that is not a folder on a disk (This PC, Control Panel…).</summary>
    public static string? PathOf(string? locationUrl)
    {
        if (string.IsNullOrWhiteSpace(locationUrl)) return null;
        if (!Uri.TryCreate(locationUrl, UriKind.Absolute, out var uri) || !uri.IsFile) return null;
        var path = uri.LocalPath;
        return path.Length >= 3 && path[1] == ':' ? path : null;
    }

    private void Loop()
    {
        object? shell = null;
        try
        {
            var type = Type.GetTypeFromProgID("Shell.Application");
            if (type is null) return;
            shell = Activator.CreateInstance(type);
            if (shell is null) return;
            var failures = 0;
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    foreach (var path in OpenFolders(shell)) Report(path);
                    failures = 0;
                }
                catch (Exception e) when (e is COMException or TargetInvocationException or InvalidCastException or MissingMethodException)
                {
                    // Explorer restarts now and then; a few misses are normal, a long row of them is logged once.
                    if (++failures == 20) _log.LogDebug("Reading File Explorer's open folders keeps failing: {Error}", e.Message);
                }
                if (_cts.Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(1.5))) break;
            }
        }
        catch (Exception e) when (e is COMException or TargetInvocationException) { _log.LogDebug("File Explorer could not be asked for its open folders: {Error}", e.Message); }
        finally
        {
            if (shell != null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell);
        }
    }

    private void Report(string path)
    {
        var now = DateTime.UtcNow;
        if (_seen.TryGetValue(path, out var last) && now - last < Again) return;
        _seen[path] = now;
        if (_seen.Count > 500) foreach (var old in _seen.Where(kv => now - kv.Value > Again).Select(kv => kv.Key).ToList()) _seen.Remove(old);
        try { _opened(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* a folder that went away meanwhile */ }
    }

    private static List<string> OpenFolders(object shell)
    {
        var found = new List<string>();
        var windows = Call(shell, "Windows", BindingFlags.InvokeMethod);
        if (windows is null) return found;
        try
        {
            var count = Convert.ToInt32(Call(windows, "Count", BindingFlags.GetProperty) ?? 0);
            for (var i = 0; i < count; i++)
            {
                var w = Call(windows, "Item", BindingFlags.InvokeMethod, i);
                if (w is null) continue;
                try
                {
                    if (PathOf(Call(w, "LocationURL", BindingFlags.GetProperty) as string) is { } p) found.Add(p);
                }
                finally { if (Marshal.IsComObject(w)) Marshal.ReleaseComObject(w); }
            }
        }
        finally { if (Marshal.IsComObject(windows)) Marshal.ReleaseComObject(windows); }
        return found;
    }

    private static object? Call(object target, string name, BindingFlags how, params object[] args) =>
        target.GetType().InvokeMember(name, how, null, target, args);

    public void Dispose() => _cts.Cancel();
}
