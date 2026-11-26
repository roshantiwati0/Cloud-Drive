# Closes the running Cloud-SSD PC app the way a person does: asks its window to close, then answers its
# "Close Cloud Drive" box with No (the drives are unmounted; pending uploads get up to 60 s). When the box says a game
# is still running it answers Cancel and leaves the app open.
#
# Two things that used to break this helper:
#  * The "Close Cloud Drive" box is its OWN top level window (a WPF MessageBox is owned by the main window, not a
#    child of it), so it has to be looked up on the desktop - looking inside the main window never finds it and the
#    app then waits for a person forever.
#  * CloseMainWindow() posts WM_CLOSE, but this app's window (custom title bar) does not always take it: the call
#    returns False and nothing happens. Then the X of the title bar is clicked for real.
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$p = Get-Process Cloud-SSD -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if (-not $p) { 'not running'; exit 0 }

# A minimized app shows its "Close Cloud Drive" box minimized too (the box is owned by the main window), and UI
# Automation does not list it then: this helper waited its 90 s and gave up while the box sat there unseen. Bring the
# window back first; this also shows a box left open by an earlier try.
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class CloudSsdShow {
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool IsWindowEnabled(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int cmd);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
}
'@
if ([CloudSsdShow]::IsIconic($p.MainWindowHandle)) {
    [void][CloudSsdShow]::ShowWindow($p.MainWindowHandle, 9)
    [void][CloudSsdShow]::SetForegroundWindow($p.MainWindowHandle)
    Start-Sleep -Milliseconds 700
    'restored the minimized window'
}
# A disabled main window means its close box is already open (an earlier try): do not ask it to close a second time.
$boxOpen = -not [CloudSsdShow]::IsWindowEnabled($p.MainWindowHandle)
if ($boxOpen) { 'the close box is already open' }

if (-not $boxOpen -and -not $p.CloseMainWindow()) {
    # Own RECT (left, top, right, bottom): System.Drawing.Rectangle is x/y/width/height and is not referenced by
    # Add-Type on Windows PowerShell 5.1, so this block never compiled and the X was never clicked.
    Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class CloudSsdClose {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int cmd);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    public const uint LEFTDOWN = 0x0002, LEFTUP = 0x0004;
    public static bool ClickX(IntPtr h) {
        // A minimized window has no X to click: bring it back and to the front first.
        if (IsIconic(h)) ShowWindow(h, 9);
        SetForegroundWindow(h);
        System.Threading.Thread.Sleep(500);
        RECT r;
        if (!GetWindowRect(h, out r)) return false;
        int x = r.Right - 24, y = r.Top + 24;
        SetCursorPos(x, y);
        System.Threading.Thread.Sleep(300);
        mouse_event(LEFTDOWN, 0, 0, 0, IntPtr.Zero);
        mouse_event(LEFTUP, 0, 0, 0, IntPtr.Zero);
        return true;
    }
}
'@
    if ([CloudSsdClose]::ClickX($p.MainWindowHandle)) { 'clicked the X of the title bar' }
}

$root = [System.Windows.Automation.AutomationElement]::RootElement
$isWindow = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Window)
$deadline = (Get-Date).AddSeconds(90)
$answered = $false
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 700
    if ($p.HasExited) { 'exited'; exit 0 }
    if ($answered) { continue }
    $box = $null
    try {
        $windows = $root.FindAll([System.Windows.Automation.TreeScope]::Children, $isWindow)
        foreach ($w in $windows) {
            if ($w.Current.Name -eq 'Close Cloud Drive' -or ($w.Current.ProcessId -eq $p.Id -and $w.Current.Name -ne $p.MainWindowTitle)) { $box = $w; break }
        }
    } catch { $box = $null }
    if (-not $box) { continue }
    $all = $box.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    $text = ($all | ForEach-Object { $_.Current.Name }) -join ' '
    $want = if ($text -match 'game is still running') { 'Cancel' } else { 'No' }
    $button = $all | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button -and $_.Current.Name -eq $want } | Select-Object -First 1
    if ($button) {
        $pat = $null
        if ($button.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$pat)) { $pat.Invoke(); $answered = $true; "answered: $want" }
        if ($want -eq 'Cancel') { 'a game is running: left open'; exit 1 }
    }
}
if ($p.HasExited) { 'exited' } else { 'still running after 90 s'; exit 1 }
