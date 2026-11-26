param([string]$Js = '', [string]$Shot = '', [switch]$Launch)
# Live test on the real phone over adb: runs a JS file in the Cloud-SSD app's page (WebView DevTools) and, with -Shot,
# saves a screenshot to .work\shots\<Shot>.png. It never sends keys or touches, and it stops when a call is on, the
# phone is locked, or another app is in front (-Launch opens Cloud-SSD first, only from the home screen).
#   tools\live-test\phone.ps1 -Launch -Js .work\t.js -Shot home
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$adb = 'C:\Android\Sdk\platform-tools\adb.exe'
$serial = (& $adb devices | Select-String -Pattern '^(\S+)\s+device$' | Select-Object -First 1)
if (-not $serial) { 'STOP: no phone on adb (switch wireless debugging on)'; exit 1 }
$env:ANDROID_SERIAL = $serial.Matches[0].Groups[1].Value
function Focus { ((& $adb shell "dumpsys window | grep -m1 mCurrentFocus" 2>$null) -join ' ') }
$call = (& $adb shell "dumpsys telephony.registry | grep -m1 mCallState" 2>$null) -join ' '
$lock = (& $adb shell "dumpsys window | grep -m1 mDreamingLockscreen" 2>$null) -join ' '
if ($call -notmatch 'mCallState=0') { "STOP: a call is on"; exit 1 }
if ($lock -notmatch 'mDreamingLockscreen=false') { "STOP: the phone is locked"; exit 1 }
$f = Focus
if ($f -notmatch 'com\.cloudssd\.mobile') {
    if ($Launch -and $f -match 'launcher') {
        & $adb shell monkey -p com.cloudssd.mobile -c android.intent.category.LAUNCHER 1 | Out-Null
        Start-Sleep -Seconds 6
        $f = Focus
    }
    if ($f -notmatch 'com\.cloudssd\.mobile') { "STOP: Cloud-SSD is not in front ($($f.Trim()))"; exit 1 }
}
if ($Js) {
    $procId = (& $adb shell "pidof com.cloudssd.mobile").Trim()
    & $adb forward tcp:9229 "localabstract:webview_devtools_remote_$procId" | Out-Null
    node (Join-Path $repo 'tools\live-test\cdp.js') 9229 $Js | Out-String | ForEach-Object { $_.Trim() -replace '\s+', ' ' }
}
if ($Shot) {
    $dir = Join-Path $repo '.work\shots'
    New-Item -ItemType Directory -Force $dir | Out-Null
    & $adb shell screencap -p /sdcard/cssd_shot.png
    & $adb pull /sdcard/cssd_shot.png (Join-Path $dir "$Shot.png") | Out-Null
    & $adb shell rm /sdcard/cssd_shot.png
    Join-Path $dir "$Shot.png"
}
