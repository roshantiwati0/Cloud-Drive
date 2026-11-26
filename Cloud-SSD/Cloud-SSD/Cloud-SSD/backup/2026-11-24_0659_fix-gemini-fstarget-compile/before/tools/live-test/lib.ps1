# Helpers for live tests on a real phone (USB or wireless adb) against a throw-away "test PC" bridge (testpc\).
#   . .\tools\live-test\lib.ps1
#   Phone "s4b.js" 200            runs phone\s4b.js in the app's WebView (Chrome DevTools over adb) and returns its result
#   Phone "pair.js" 90 (Code)     the same with __CODE__ in the script replaced (here: the test PC's pairing code)
#   Pc "devices"                  one command for the test PC (stands in for the person at the PC); returns its log lines
# The app must be a debug build (WebView debugging on), open and in front, and the phone unlocked: a locked phone or an
# app in the background freezes the WebView.
$global:lt = $PSScriptRoot
$global:adb = if ($env:ANDROID_HOME) { Join-Path $env:ANDROID_HOME "platform-tools\adb.exe" } else { "C:\Android\Sdk\platform-tools\adb.exe" }
$global:tpc = Join-Path $lt "testpc\bin"
# Wireless debugging can list the same phone twice (address and mDNS name): use the first one unless one is chosen.
if (-not $env:ANDROID_SERIAL) {
    $first = (& $adb devices) | Select-String '^(\S+)\s+device$' | Select-Object -First 1
    if ($first) { $env:ANDROID_SERIAL = $first.Matches[0].Groups[1].Value }
}

function Phone([string]$file, [int]$timeout = 120, [string]$code = "") {
    $procId = (& $adb shell "pidof com.cloudssd.mobile").Trim()
    if (-not $procId) { return "APP NOT RUNNING" }
    & $adb forward tcp:9229 "localabstract:webview_devtools_remote_$procId" | Out-Null
    $path = Join-Path $lt "phone\$file"
    if ($code) {
        $tmp = Join-Path $env:TEMP "cssd_live_run.js"
        (Get-Content $path -Raw).Replace('__CODE__', $code) | Set-Content $tmp -Encoding utf8
        $path = $tmp
    }
    $j = Start-Job { param($cdp, $js) node $cdp 9229 $js 2>&1 } -ArgumentList (Join-Path $lt "cdp.js"), $path
    if (Wait-Job $j -Timeout $timeout) { $r = (Receive-Job $j) -join "`n" } else { Stop-Job $j; $r = "TIMEOUT after $timeout s" }
    Remove-Job $j -Force
    return $r
}

# One command for the test PC (cmd.txt → cmd-log.txt); returns the lines it logged for it.
function Pc([string]$cmd, [int]$timeout = 45) {
    $log = Join-Path $tpc "cmd-log.txt"
    $before = @(Get-Content $log -ErrorAction SilentlyContinue).Count
    Set-Content (Join-Path $tpc "cmd.txt") $cmd -Encoding ascii
    for ($i = 0; $i -lt $timeout * 4; $i++) {
        Start-Sleep -Milliseconds 250
        $now = @(Get-Content $log -ErrorAction SilentlyContinue)
        if ($now.Count -gt $before -and -not (Test-Path (Join-Path $tpc "cmd.txt"))) {
            Start-Sleep -Milliseconds 300
            return (@(Get-Content $log)[$before..999] -join "`n")
        }
    }
    return "PC COMMAND TIMEOUT: $cmd"
}

function LogCount { @(Get-Content (Join-Path $tpc "cmd-log.txt") -ErrorAction SilentlyContinue).Count }
function LogSince([int]$from) { @(Get-Content (Join-Path $tpc "cmd-log.txt"))[$from..9999] -join "`n" }
# The test PC writes "<pairing code> <its id>" to code.txt every second.
function Code { (Get-Content (Join-Path $tpc "code.txt")).Split(' ')[0] }
function PcId { (Get-Content (Join-Path $tpc "code.txt")).Split(' ')[1] }
function Locked { (& $adb shell "dumpsys window | grep -c 'isKeyguardShowing=true'").Trim() -ne "0" }
function Front { (& $adb shell "dumpsys activity activities | grep -m1 topResumedActivity") -replace '.*u0 ([^ ]+).*', '$1' }
