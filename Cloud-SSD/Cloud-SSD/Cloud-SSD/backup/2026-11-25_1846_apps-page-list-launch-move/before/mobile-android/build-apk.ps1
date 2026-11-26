param(
    # A debug build lets the page be inspected over USB (chrome://inspect); a normal build does not.
    [switch]$Debug,
    # Android SDK folder (build-tools 35.0.0 + platforms\android-36). Found by itself when left out.
    [string]$Sdk = "",
    # JDK 17 folder (the one that contains bin\javac.exe). Found by itself when left out.
    [string]$Jdk = "",
    # Build somewhere else (a scratch folder): the APK and the build folder in the project are left as they are.
    [string]$OutDir = ""
)
$ErrorActionPreference = "Stop"

function FirstExisting([string[]]$paths, [string]$probe) {
    foreach ($p in $paths) { if ($p -and (Test-Path (Join-Path $p $probe))) { return $p } }
    return $null
}

$sdk = if ($Sdk) { $Sdk } else {
    FirstExisting @($env:ANDROID_HOME, $env:ANDROID_SDK_ROOT, "$env:LOCALAPPDATA\Android\Sdk", "C:\Android\Sdk") "build-tools\35.0.0"
}
if (-not $sdk) { throw "Android SDK not found. Pass -Sdk <folder> (needs build-tools 35.0.0 and platforms\android-36)." }

$jdkCandidates = @($env:JAVA_HOME)
foreach ($base in @("$env:LOCALAPPDATA\Programs", "C:\Program Files\Eclipse Adoptium", "C:\Program Files\Java", "C:\Program Files\Microsoft")) {
    if (Test-Path $base) { $jdkCandidates += Get-ChildItem $base -Directory -Recurse -Depth 1 -ErrorAction SilentlyContinue | Where-Object { $_.Name -match 'jdk-?17' } | ForEach-Object { $_.FullName } }
}
$jdkHome = if ($Jdk) { $Jdk } else { FirstExisting $jdkCandidates "bin\javac.exe" }
if (-not $jdkHome) { throw "JDK 17 not found. Install one (for example Eclipse Temurin 17) or pass -Jdk <folder>." }
Write-Host "Android SDK: $sdk  |  JDK: $jdkHome" -ForegroundColor DarkGray

$bt = "$sdk\build-tools\35.0.0"
$jar = "$sdk\platforms\android-36\android.jar"
$env:JAVA_HOME = $jdkHome
$env:PATH = "$jdkHome\bin;$env:PATH"
$jdk = "$jdkHome\bin"
$aapt2 = "$bt\aapt2.exe"
$d8 = "$bt\d8.bat"
$zipalign = "$bt\zipalign.exe"
$apksigner = "$bt\apksigner.bat"
$javac = "$jdk\javac.exe"
$keytool = "$jdk\keytool.exe"
$jarTool = "$jdk\jar.exe"

$root = $PSScriptRoot
$outRoot = if ($OutDir) { New-Item -ItemType Directory -Force $OutDir | Out-Null; (Resolve-Path $OutDir).Path } else { $root }
$build = Join-Path $outRoot "build"

if (Test-Path $build) { Remove-Item -Recurse -Force $build }
New-Item -ItemType Directory -Force (Join-Path $build "compiled_res") | Out-Null
New-Item -ItemType Directory -Force (Join-Path $build "gen\com\cloudssd\mobile") | Out-Null
New-Item -ItemType Directory -Force (Join-Path $build "classes") | Out-Null
New-Item -ItemType Directory -Force (Join-Path $build "dex") | Out-Null

# ---- Google key for "phone without the PC" (optional) ------------------------------------------------------------
# mobile-android\google-oauth.json (never committed) holds either {"iosClientId": "...apps.googleusercontent.com"}
# or {"desktopClientId": "...", "desktopClientSecret": "..."}. Without it, the PC's own key file
# (installer\google-client.json, Google's "installed" format) is used as a Desktop key. With neither, the app is built
# without Google sign-in and simply works through the PC.
$clientId = ""; $clientSecret = ""; $redirect = ""; $mode = ""; $scheme = ""
$oauthFile = Join-Path $root "google-oauth.json"
$pcKeyFile = Join-Path $root "..\installer\google-client.json"
if (Test-Path $oauthFile) {
    $o = Get-Content -Raw $oauthFile | ConvertFrom-Json
    if ($o.iosClientId) {
        $clientId = $o.iosClientId.Trim(); $mode = "ios"
        $scheme = "com.googleusercontent.apps." + ($clientId -replace '\.apps\.googleusercontent\.com$', '')
        $redirect = "${scheme}:/oauth2redirect"
    } elseif ($o.desktopClientId) {
        $clientId = $o.desktopClientId.Trim(); $clientSecret = "$($o.desktopClientSecret)".Trim(); $mode = "desktop"
    }
} elseif (Test-Path $pcKeyFile) {
    $o = Get-Content -Raw $pcKeyFile | ConvertFrom-Json
    if ($o.installed.client_id) { $clientId = $o.installed.client_id; $clientSecret = "$($o.installed.client_secret)"; $mode = "desktop" }
}
function JavaString([string]$s) { '"' + ($s -replace '\\', '\\' -replace '"', '\"') + '"' }
# Written without a byte-order mark: javac refuses one.
[IO.File]::WriteAllText((Join-Path $build "gen\com\cloudssd\mobile\GoogleConfig.java"), @"
package com.cloudssd.mobile;

/** Written by build-apk.ps1 from google-oauth.json; empty when the app was built without a Google key. */
final class GoogleConfig {
    static final String CLIENT_ID = $(JavaString $clientId);
    static final String CLIENT_SECRET = $(JavaString $clientSecret);
    static final String REDIRECT = $(JavaString $redirect);
    static final String MODE = $(JavaString $mode);

    private GoogleConfig() {
    }
}
"@, (New-Object Text.UTF8Encoding($false)))
Write-Host ("Google sign-in on the phone: " + $(if ($mode) { "$mode key" } else { "not included (no google-oauth.json)" })) -ForegroundColor Cyan

# The manifest with the Google redirect filled in (or that block removed).
$manifest = Get-Content -Raw "$root\src\main\AndroidManifest.xml"
if ($mode -eq "ios") { $manifest = $manifest.Replace("GOOGLE_REDIRECT_SCHEME", $scheme) }
else { $manifest = [regex]::Replace($manifest, '(?s)\s*<!-- GOOGLE-REDIRECT-BEGIN.*?GOOGLE-REDIRECT-END -->', '') }
$builtManifest = Join-Path $build "AndroidManifest.xml"
[IO.File]::WriteAllText($builtManifest, $manifest, (New-Object Text.UTF8Encoding($false)))

Write-Host "1. Compiling Android resources with AAPT2..." -ForegroundColor Cyan
& $aapt2 compile --dir "$root\src\main\res" -o "$build\compiled_res"
if ($LASTEXITCODE -ne 0) { throw "AAPT2 compile failed" }

Write-Host "2. Linking resources and generating R.java..." -ForegroundColor Cyan
$flatFiles = Get-ChildItem "$build\compiled_res\*.flat" | ForEach-Object { $_.FullName }
$linkArgs = @("link", "-I", $jar, "--manifest", $builtManifest, "--java", "$build\gen", "-o", "$build\app-unaligned.apk", "-A", "$root\src\main\assets")
if ($Debug) { $linkArgs += "--debug-mode" }
& $aapt2 @linkArgs @flatFiles
if ($LASTEXITCODE -ne 0) { throw "AAPT2 link failed" }

Write-Host "3. Compiling Java sources..." -ForegroundColor Cyan
$javaSources = Get-ChildItem "$build\gen", "$root\src\main\java" -Recurse -Filter "*.java" | ForEach-Object { $_.FullName }
# -Xlint:-options: no "bootstrap class path not set" warning. android.jar cannot be the boot class path (its
# LambdaMetafactory is a stub javac cannot build lambdas against), and D8 checks the result against Android anyway.
& $javac -source 1.8 -target 1.8 -Xlint:-options -encoding UTF-8 -classpath $jar -d "$build\classes" @javaSources
if ($LASTEXITCODE -ne 0) { throw "javac failed" }

Write-Host "4. Dexing classes with D8..." -ForegroundColor Cyan
# One jar instead of every .class file on the command line (Windows limits its length).
& $jarTool -cf "$build\classes.jar" -C "$build\classes" .
if ($LASTEXITCODE -ne 0) { throw "jar of classes failed" }
& $d8 "$build\classes.jar" --lib $jar --min-api 24 --output "$build\dex"
if ($LASTEXITCODE -ne 0) { throw "d8 failed" }

Write-Host "5. Packaging classes.dex into APK..." -ForegroundColor Cyan
& $jarTool -uf "$build\app-unaligned.apk" -C "$build\dex" classes.dex
if ($LASTEXITCODE -ne 0) { throw "jar update failed" }

Write-Host "6. Zipalign APK..." -ForegroundColor Cyan
& $zipalign -p 4 "$build\app-unaligned.apk" "$build\app-aligned.apk"
if ($LASTEXITCODE -ne 0) { throw "zipalign failed" }

Write-Host "7. Signing APK with debug keystore..." -ForegroundColor Cyan
$keystore = Join-Path $root "debug.keystore"
if (-not (Test-Path $keystore)) {
    & $keytool -genkey -v -keystore $keystore -alias androiddebugkey -storepass android -keypass android -keyalg RSA -keysize 2048 -validity 10000 -dname "CN=CloudSSD,O=CloudSSD,C=US"
}

$outputApk = Join-Path $outRoot "Cloud-SSD-Mobile.apk"
& $apksigner sign --ks $keystore --ks-pass pass:android --key-pass pass:android --out $outputApk "$build\app-aligned.apk"
if ($LASTEXITCODE -ne 0) { throw "apksigner failed" }

$sizeMb = [math]::Round((Get-Item $outputApk).Length / 1MB, 2)
Write-Host ("Successfully built: " + $outputApk + " (" + $sizeMb + " MB)") -ForegroundColor Green
