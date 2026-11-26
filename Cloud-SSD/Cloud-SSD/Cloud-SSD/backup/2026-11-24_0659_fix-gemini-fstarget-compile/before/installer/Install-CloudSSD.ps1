<#
.SYNOPSIS
  Installs Cloud-SSD for the current Windows user (no administrator rights needed).
.DESCRIPTION
  Copies the application to %LOCALAPPDATA%\Programs\Cloud-SSD, creates a Start menu shortcut (and optionally a
  desktop shortcut), and registers an uninstall entry under "Installed apps". WinFsp is required for the virtual
  drive; if it is missing this script tells you where to get it but does not install it silently.
.PARAMETER DesktopShortcut
  Also create a desktop shortcut.
.PARAMETER InstallDir
  Custom installation folder.
#>
param(
    [switch]$DesktopShortcut,
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA "Programs\Cloud-SSD"),
    [switch]$Quiet,
    # Do not install WinFsp even when it is missing (the app offers it at its first start).
    [switch]$SkipWinFsp
)
$ErrorActionPreference = "Stop"
$src = $PSScriptRoot
if (-not (Test-Path (Join-Path $src "Cloud-SSD.exe")) -and -not (Test-Path (Join-Path $src "CloudPlayX.exe"))) { throw "Run this script from the extracted Cloud-SSD release folder." }

$running = Get-Process Cloud-SSD, CloudPlayX, CloudSSD -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($InstallDir, 'OrdinalIgnoreCase') }
if ($running) { throw "Cloud-SSD is running from $InstallDir. Close it first." }

Write-Host "Installing Cloud-SSD to $InstallDir ..."
New-Item -ItemType Directory -Force $InstallDir | Out-Null
# Replace program files only; user data lives in %LOCALAPPDATA%\Cloud-SSD and is never touched here.
# Folders are merged into the ones that are there (robocopy): Copy-Item would put "tools" inside an existing "tools".
robocopy $src $InstallDir /E /XF Install.cmd /R:2 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "Copying the program files failed (robocopy $LASTEXITCODE)." }
$global:LASTEXITCODE = 0
# Leftovers of the earlier way of copying: a folder inside the folder of the same name (tools\tools, wwwroot\wwwroot…).
Get-ChildItem $src -Directory | ForEach-Object {
    $nested = Join-Path (Join-Path $InstallDir $_.Name) $_.Name
    if ((Test-Path $nested) -and -not (Test-Path (Join-Path $_.FullName $_.Name))) { Remove-Item $nested -Recurse -Force }
}

$exeName = if (Test-Path (Join-Path $InstallDir "Cloud-SSD.exe")) { "Cloud-SSD.exe" } else { "CloudPlayX.exe" }
$exe = Join-Path $InstallDir $exeName
$shell = New-Object -ComObject WScript.Shell
function New-Shortcut($path) {
    $lnk = $shell.CreateShortcut($path)
    $lnk.TargetPath = $exe
    $lnk.WorkingDirectory = $InstallDir
    $lnk.IconLocation = "$exe,0"
    $lnk.Description = "Cloud-SSD - Cloud-to-SSD virtual drive and storage engine"
    $lnk.Save()
}
$startMenu = Join-Path ([Environment]::GetFolderPath("Programs")) "Cloud-SSD.lnk"
New-Shortcut $startMenu
if ($DesktopShortcut) { New-Shortcut (Join-Path ([Environment]::GetFolderPath("Desktop")) "Cloud-SSD.lnk") }

# Uninstall entry (per user).
$key = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\CloudSSD"
New-Item -Path $key -Force | Out-Null
$version = (Get-Item $exe).VersionInfo.ProductVersion
Set-ItemProperty $key DisplayName "Cloud-SSD"
Set-ItemProperty $key DisplayVersion $version
Set-ItemProperty $key Publisher "Cloud-SSD"
Set-ItemProperty $key InstallLocation $InstallDir
Set-ItemProperty $key DisplayIcon "$exe,0"
Set-ItemProperty $key NoModify 1 -Type DWord
Set-ItemProperty $key NoRepair 1 -Type DWord
Set-ItemProperty $key UninstallString "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$InstallDir\Uninstall-CloudSSD.ps1`""
$size = [int]((Get-ChildItem $InstallDir -Recurse -File | Measure-Object Length -Sum).Sum / 1KB)
Set-ItemProperty $key EstimatedSize $size -Type DWord

# Add InstallDir to user PATH so "cloudssd" and "cpx" can be run from any CMD or terminal.
try {
    $userPath = [Environment]::GetEnvironmentVariable("Path", "User")
    $paths = ($userPath -split ';') | Where-Object { $_ }
    if ($paths -notcontains $InstallDir) {
        $newUserPath = ($paths + $InstallDir) -join ';'
        [Environment]::SetEnvironmentVariable("Path", $newUserPath, "User")
    }
} catch { }

# Start with Windows: the app comes up by itself after every restart, so the cloud drives are back without
# anyone opening the program by hand. This is the same entry the app's own checkbox writes
# (CloudSSD.App.StartWithWindows), and Uninstall-CloudSSD.ps1 removes it again.
try {
    $runKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
    New-Item -Path $runKey -Force | Out-Null
    Set-ItemProperty -Path $runKey -Name "Cloud-SSD" -Value ('"{0}" --minimized' -f $exe) -Type String
    Write-Host "Cloud-SSD will now start with Windows (minimized)." -ForegroundColor Green
} catch {
    Write-Host "Could not add the 'start with Windows' entry: $($_.Exception.Message)" -ForegroundColor Yellow
}

# Prerequisite check (informational only).
$winfsp = Test-Path "${env:ProgramFiles(x86)}\WinFsp\bin\winfsp-x64.dll"
Write-Host ""
Write-Host "Cloud-SSD $version installed." -ForegroundColor Green
$msi = Get-ChildItem (Join-Path $src "tools\winfsp") -Filter "winfsp*.msi" -ErrorAction SilentlyContinue | Sort-Object Name | Select-Object -Last 1
if (-not $winfsp -and $msi -and -not $SkipWinFsp -and -not $Quiet) {
    # The official installer comes in this folder: run it (Windows asks for permission once). Only the genuine one.
    $sig = Get-AuthenticodeSignature $msi.FullName
    if ($sig.Status -eq "Valid" -and $sig.SignerCertificate.Subject -match "NAVIMATICS LLC") {
        Write-Host "Installing WinFsp (needed for the virtual drive) - please approve the Windows prompt..."
        try {
            $p = Start-Process msiexec.exe -ArgumentList "/i `"$($msi.FullName)`" /passive /norestart" -Verb RunAs -Wait -PassThru
            $winfsp = Test-Path "${env:ProgramFiles(x86)}\WinFsp\bin\winfsp-x64.dll"
        } catch { Write-Host "WinFsp was not installed (the Windows prompt was declined)." -ForegroundColor Yellow }
    } else { Write-Host "The WinFsp installer in this folder is not the genuine one; it was not run." -ForegroundColor Yellow }
}
if (-not $winfsp) {
    Write-Host "WinFsp is NOT installed. The virtual drive needs it." -ForegroundColor Yellow
    Write-Host "  Cloud-SSD offers to install it when it starts (or run: winget install WinFsp.WinFsp)"
} else { Write-Host "WinFsp: found." }
Write-Host "Start Cloud-SSD from the Start menu."
if (-not $Quiet) { Start-Process $exe }
