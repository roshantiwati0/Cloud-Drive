# Makes the PC app's .ico files from its picture (src\CloudSSD.Desktop\Assets\clouddrive.png, made by png.js from the
# app's icon): clouddrive.ico (the program file's and the window's icon) and cloudssd.ico (the installer's).
# Frames of 16 to 256 px, each stored as PNG.
#   powershell -File tools\icons\make-ico.ps1
param([string]$Png = "$PSScriptRoot\..\..\src\CloudSSD.Desktop\Assets\clouddrive.png")
Add-Type -AssemblyName System.Drawing
$assets = Split-Path (Resolve-Path $Png) -Parent
$src = [System.Drawing.Image]::FromFile((Resolve-Path $Png))

function New-Frame([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = 'HighQualityBicubic'
    $g.PixelOffsetMode = 'HighQuality'
    $g.SmoothingMode = 'HighQuality'
    $g.CompositingQuality = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.DrawImage($src, (New-Object System.Drawing.Rectangle 0, 0, $s, $s))
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return , $ms.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$frames = $sizes | ForEach-Object { , (New-Frame $_) }
$src.Dispose()
foreach ($name in 'clouddrive.ico', 'cloudssd.ico') {
    $out = Join-Path $assets $name
    $fs = [System.IO.File]::Create($out)
    $w = New-Object System.IO.BinaryWriter $fs
    $w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $s = $sizes[$i]; $len = $frames[$i].Length
        $w.Write([byte]($(if ($s -ge 256) { 0 } else { $s }))); $w.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
        $w.Write([byte]0); $w.Write([byte]0); $w.Write([UInt16]1); $w.Write([UInt16]32)
        $w.Write([UInt32]$len); $w.Write([UInt32]$offset); $offset += $len
    }
    foreach ($f in $frames) { $w.Write($f) }
    $w.Close()
    Write-Host "Wrote $out ($((Get-Item $out).Length) bytes)"
}
