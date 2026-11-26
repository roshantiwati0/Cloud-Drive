using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace CloudSSD.Mount;

/// <summary>
/// Starts a long-lived child process (rclone) in a way that does not depend on this process staying alive.
///
/// Why this exists: when a child writes into a pipe of this app, the pipe breaks as soon as this app closes
/// the read end. rclone is a Go program, and Go turns that EPIPE into SIGPIPE and exits. Measured on this
/// machine: the mount disappeared about a minute after the app was closed, even though the user had answered
/// "yes, keep the drives mounted", and the logs said nothing about it.
/// With the standard handles pointing at the NUL device there is no pipe that can break: rclone keeps running
/// and the drive stays mounted until it is stopped on purpose.
/// </summary>
internal static class DetachedProcess
{
    private const uint CreateNoWindow = 0x08000000;
    private const uint CreateUnicodeEnvironment = 0x00000400;
    private const uint StartfUseStdHandles = 0x00000100;
    private const uint FileShareReadWrite = 0x00000001 | 0x00000002;
    private const uint FileShareDelete = 0x00000004;
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint OpenExisting = 3;
    private const uint InvalidHandleValue = 0xFFFFFFFF;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public uint dwProcessId;
        public uint dwThreadId;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessW(string? applicationName, StringBuilder commandLine, IntPtr processAttributes,
        IntPtr threadAttributes, bool inheritHandles, uint creationFlags, IntPtr environment, string? currentDirectory,
        ref StartupInfo startupInfo, out ProcessInformation processInformation);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
        uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    /// <summary>
    /// Starts the program with the given arguments and environment. The standard handles point at the NUL device,
    /// so the child never writes into a pipe of this app and survives this app closing.
    /// </summary>
    public static Process Start(string fileName, IEnumerable<string> args, IDictionary<string, string?> env, string? workingDirectory = null)
    {
        var nulIn = CreateFileW("NUL", GenericRead, FileShareReadWrite | FileShareDelete, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        var nulOut = CreateFileW("NUL", GenericWrite, FileShareReadWrite | FileShareDelete, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (nulIn.IsInvalid || nulOut.IsInvalid) throw new IOException("Could not open the NUL device.");

        var si = new StartupInfo
        {
            cb = Marshal.SizeOf<StartupInfo>(),
            dwFlags = (int)StartfUseStdHandles,
            hStdInput = nulIn.DangerousGetHandle(),
            hStdOutput = nulOut.DangerousGetHandle(),
            hStdError = nulOut.DangerousGetHandle(),
        };

        var commandLine = new StringBuilder(Quote(fileName));
        foreach (var a in args) commandLine.Append(' ').Append(Quote(a));

        var envBlock = BuildEnvironment(env);
        var envPointer = Marshal.StringToHGlobalUni(envBlock);
        try
        {
            if (!CreateProcessW(fileName, commandLine, IntPtr.Zero, IntPtr.Zero, false,
                    CreateNoWindow | CreateUnicodeEnvironment, envPointer, workingDirectory, ref si, out var pi))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            CloseHandle(pi.hThread);
            CloseHandle(pi.hProcess);
            // The process object of a child that was started this way is reached by its id.
            return Process.GetProcessById((int)pi.dwProcessId);
        }
        finally
        {
            Marshal.FreeHGlobal(envPointer);
            nulIn.Dispose();
            nulOut.Dispose();
        }
    }

    /// <summary>The environment block of a Unicode process: "NAME=VALUE" lines, each ending with a NUL, plus a final NUL.</summary>
    private static string BuildEnvironment(IDictionary<string, string?> overrides)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
            values[(string)entry.Key] = entry.Value as string;
        foreach (var (k, v) in overrides) values[k] = v;

        var sb = new StringBuilder();
        foreach (var pair in values.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (pair.Value is null) continue; // a variable without a value is not allowed in the block
            sb.Append(pair.Key).Append('=').Append(pair.Value).Append('\0');
        }
        sb.Append('\0');
        return sb.ToString();
    }

    /// <summary>Quotes one argument for the Windows command line (the rules the C runtime uses).</summary>
    private static string Quote(string arg)
    {
        if (arg.Length > 0 && arg.IndexOfAny([' ', '\t', '"', '\n']) < 0) return arg;
        var sb = new StringBuilder("\"");
        for (int i = 0; i < arg.Length; i++)
        {
            int slashes = 0;
            while (i < arg.Length && arg[i] == '\\') { slashes++; i++; }
            if (i == arg.Length) { sb.Append('\\', slashes * 2); break; }
            if (arg[i] == '"') sb.Append('\\', slashes * 2 + 1).Append('"');
            else sb.Append('\\', slashes).Append(arg[i]);
        }
        sb.Append('"');
        return sb.ToString();
    }
}

