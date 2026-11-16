# Builds the two release files into .\Release (that folder is git-ignored):
#   PriceTracker.exe       – portable app, one self-contained file (no .NET needed)
#   PriceTrackerSetup.exe  – installer with uninstaller (per-user, no admin rights)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

$app = Join-Path $root 'src\PriceTracker\PriceTracker.csproj'
$publish = Join-Path $root 'publish'
$release = Join-Path $root 'Release'
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
New-Item -ItemType Directory -Force $release | Out-Null

Write-Host '1/4  Publishing app (folder, for the installer)...'
dotnet publish $app -c Release -r win-x64 --self-contained true -p:DebugType=none -o (Join-Path $publish 'app') | Out-Null
if ($LASTEXITCODE) { throw 'Publishing the app failed' }
Remove-Item (Join-Path $publish 'app\*.pdb') -ErrorAction SilentlyContinue
# WebView2 ships WPF/WinForms wrappers the app never loads.
Remove-Item (Join-Path $publish 'app\Microsoft.Web.WebView2.Wpf.dll'), (Join-Path $publish 'app\Microsoft.Web.WebView2.WinForms.dll') -ErrorAction SilentlyContinue

Write-Host '2/4  Packing installer payload...'
$zip = Join-Path $root 'src\Installer\payload.zip'
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path (Join-Path $publish 'app\*') -DestinationPath $zip -CompressionLevel Optimal

Write-Host '3/4  Building installer...'
dotnet publish (Join-Path $root 'src\Installer\Installer.csproj') -c Release -o (Join-Path $publish 'setup') | Out-Null
if ($LASTEXITCODE) { throw 'Building the installer failed' }

Write-Host '4/4  Publishing portable single-file exe...'
dotnet publish $app -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none `
    -o (Join-Path $publish 'portable') | Out-Null
if ($LASTEXITCODE) { throw 'Publishing the portable exe failed' }

Copy-Item (Join-Path $publish 'setup\PriceTrackerSetup.exe') $release -Force
Copy-Item (Join-Path $publish 'portable\PriceTracker.exe') $release -Force
Get-ChildItem $release -Filter *.exe | Select-Object Name, @{n='MB';e={[math]::Round($_.Length/1MB,1)}}
