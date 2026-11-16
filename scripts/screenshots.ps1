# Regenerates docs\screenshots\*.png using sample data in a temporary folder (your real data is never touched).
# Usage: powershell -ExecutionPolicy Bypass -File scripts\screenshots.ps1 [-Only games]
param([string]$Only = '')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$proj = Join-Path $root 'src\PriceTracker\PriceTracker.csproj'
dotnet build $proj -c Release | Out-Null
if ($LASTEXITCODE) { throw 'Build failed' }
$exe = Join-Path $root 'src\PriceTracker\bin\Release\net10.0-windows\PriceTracker.exe'
$out = Join-Path $root 'docs\screenshots'
New-Item -ItemType Directory -Force $out | Out-Null

function Capture([string]$name, [string]$page, [hashtable]$settings) {
    if ($Only -and $name -notlike "*$Only*") { return }
    $dir = Join-Path $env:TEMP "pricetracker-shot-$name"
    if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
    New-Item -ItemType Directory -Force $dir | Out-Null
    $base = @{ WinW = 1240; WinH = 800; WinX = 80; WinY = 60; LastPage = $page }
    foreach ($k in $settings.Keys) { $base[$k] = $settings[$k] }
    $base | ConvertTo-Json | Set-Content (Join-Path $dir 'settings.json') -Encoding utf8

    $env:PRICETRACKER_DATA_DIR = $dir
    $env:PRICETRACKER_DEMO = '1'
    $env:PRICETRACKER_PAGE = $page
    $env:PRICETRACKER_SCREENSHOT = Join-Path $out "$name.png"
    $p = Start-Process $exe -PassThru
    if (-not $p.WaitForExit(40000)) { $p.Kill(); throw "Screenshot '$name' timed out" }
    Remove-Item Env:PRICETRACKER_DATA_DIR, Env:PRICETRACKER_DEMO, Env:PRICETRACKER_PAGE, Env:PRICETRACKER_SCREENSHOT
    Remove-Item $dir -Recurse -Force
    Write-Host "Saved docs\screenshots\$name.png"
}

Capture 'games'          'Games'    @{ ThemeMode = 'Light'; Accent = 'Green' }
Capture 'wishlist'       'Items'    @{ ThemeMode = 'Light'; Accent = 'Blue' }
Capture 'deals'          'Deals'    @{ ThemeMode = 'Light'; Accent = 'Green' }
Capture 'games-dark'     'Games'    @{ ThemeMode = 'Dark'; Accent = 'Purple' }
Capture 'deals-dark'     'Deals'    @{ ThemeMode = 'Dark'; Accent = 'Orange' }
Capture 'settings'       'Settings' @{ ThemeMode = 'Light'; Accent = 'Green' }

# Installer window (needs Release\PriceTrackerSetup.exe from build-release.ps1)
$setup = Join-Path $root 'Release\PriceTrackerSetup.exe'
if ((-not $Only -or 'installer' -like "*$Only*") -and (Test-Path $setup)) {
    $env:PRICETRACKER_SETUP_SCREENSHOT = Join-Path $out 'installer.png'
    $p = Start-Process $setup -PassThru
    if (-not $p.WaitForExit(30000)) { $p.Kill() }
    Remove-Item Env:PRICETRACKER_SETUP_SCREENSHOT
    Write-Host 'Saved docs\screenshots\installer.png'
}
