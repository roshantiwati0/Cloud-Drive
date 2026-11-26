<#
Live re-test in one command, on a real phone against a throw-away test PC bridge (its own temp data, port 8091):
builds and starts the test PC, points the phone at it over adb reverse, pairs the phone with its code, runs the phone
checks with the page in English, puts the language back and stops the test PC.

Needs: node, the .NET SDK, the phone on adb (USB or wireless) with the debug app installed, unlocked, the app open.
Usage:  powershell -ExecutionPolicy Bypass -File tools\live-test\run.ps1 [-Checks p0,s4b,s5a] [-Language hinglish] [-KeepTestPc]

Each check is a script in phone\ that returns JSON (what the page shows or did); read the output. Scripts that need a
person (app lock, the folder picker, ringing a locked phone) are not in the default list: run them by hand with
lib.ps1's Phone. s4b.js lists every text still in Latin letters in Hindi (gaps in i18n-words.js).
#>
param(
    [string[]]$Checks = @('peek-ssd', 'p1peek', 's4a', 's4b', 's5a'),
    [string]$Language = 'hinglish',
    [switch]$KeepTestPc
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\lib.ps1"
$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet -or -not (& $dotnet --list-sdks)) { $dotnet = Join-Path $env:LOCALAPPDATA "Microsoft\dotnet\dotnet.exe" }

Write-Host "Building the test PC..."
& $dotnet build (Join-Path $lt "testpc") -c Debug -o $tpc -v q -nologo | Out-Null
if ($LASTEXITCODE -ne 0) { throw "The test PC did not build." }
Remove-Item (Join-Path $tpc "stop.txt") -ErrorAction SilentlyContinue
$proc = Start-Process $dotnet -ArgumentList "TestPc.dll", "8091" -WorkingDirectory $tpc -WindowStyle Hidden -PassThru
for ($i = 0; $i -lt 40 -and -not (Test-Path (Join-Path $tpc "code.txt")); $i++) { Start-Sleep -Milliseconds 500 }
& $adb reverse tcp:8091 tcp:8091 | Out-Null
if (Locked) { Write-Warning "The phone is locked: unlock it and open the Cloud-SSD app." }

try {
    Write-Host "Pairing the phone with the test PC..."
    Phone "set-lang.js" 30 "english" | Out-Null
    Write-Host (Phone "pair.js" 90 (Code))
    foreach ($c in $Checks) {
        Write-Host "`n=== $c"
        Write-Host (Phone "$c.js" 240)
    }
}
finally {
    Phone "set-lang.js" 30 $Language | Out-Null
    if (-not $KeepTestPc) {
        Set-Content (Join-Path $tpc "stop.txt") "stop" -Encoding ascii
        if (-not $proc.WaitForExit(20000)) { $proc.Kill() }
        Write-Host "`nTest PC stopped. Its data stays in %TEMP%\cssd_testpc_8091; forget it on the phone with phone\forget-test.js (PcId)."
    }
}
