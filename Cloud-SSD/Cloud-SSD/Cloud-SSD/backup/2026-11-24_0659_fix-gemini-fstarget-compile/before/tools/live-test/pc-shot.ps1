param([Parameter(Mandatory = $true)][string]$Shot, [string[]]$Press = @())
# A screenshot of the Cloud-SSD PC app's main window (it need not be in front), saved to .work\shots\<Shot>.png.
# -Press a,b: first press these things by name through UI Automation (a navigation item is named by its view-model
# type, e.g. CloudSSD.Desktop.ViewModels.MobileHubViewModel; a chip or button by its text). No mouse, no keys.
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class CssdWin {
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
}
"@
[void][CssdWin]::SetProcessDPIAware()
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$p = Get-Process Cloud-SSD -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if (-not $p) { 'Cloud-SSD window not found'; exit 1 }
$root = [System.Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
foreach ($n in $Press) {
    $el = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition) |
        Where-Object { $_.Current.Name -eq $n -and $_.Current.ControlType.ProgrammaticName -match 'Button|RadioButton|ListItem|CheckBox|TabItem' } | Select-Object -First 1
    if (-not $el) { "not found: $n"; continue }
    $pat = $null
    if ($el.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$pat)) { $pat.Invoke() }
    elseif ($el.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$pat)) { $pat.Select() }
    Start-Sleep -Milliseconds 1200
}
$r = New-Object CssdWin+RECT
[void][CssdWin]::GetWindowRect($p.MainWindowHandle, [ref]$r)
$w = $r.R - $r.L; $h = $r.B - $r.T
if ($w -le 0 -or $h -le 0) { 'the window is minimized'; exit 1 }
$bmp = New-Object System.Drawing.Bitmap $w, $h
$g = [System.Drawing.Graphics]::FromImage($bmp)
$dc = $g.GetHdc()
[void][CssdWin]::PrintWindow($p.MainWindowHandle, $dc, 2)
$g.ReleaseHdc($dc); $g.Dispose()
$dir = Join-Path $repo '.work\shots'
New-Item -ItemType Directory -Force $dir | Out-Null
$out = Join-Path $dir "$Shot.png"
$bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
"$out ${w}x$h"
