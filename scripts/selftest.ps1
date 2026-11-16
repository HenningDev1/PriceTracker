# Runs Price Tracker's live self-test against Steam, KeyForSteam and Geizhals (temporary data folder) and prints the report.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
dotnet build (Join-Path $root 'src\PriceTracker\PriceTracker.csproj') -c Release | Out-Null
if ($LASTEXITCODE) { throw 'Build failed' }
$exe = Join-Path $root 'src\PriceTracker\bin\Release\net10.0-windows\PriceTracker.exe'
$dir = Join-Path $env:TEMP 'pricetracker-selftest'
New-Item -ItemType Directory -Force $dir | Out-Null
$log = Join-Path $dir 'selftest.log'
if (Test-Path $log) { Remove-Item $log }
$env:PRICETRACKER_DATA_DIR = $dir
$env:PRICETRACKER_SELFTEST = $log
$p = Start-Process $exe -PassThru
if (-not $p.WaitForExit(240000)) { $p.Kill(); Write-Warning 'Self-test timed out' }
Remove-Item Env:PRICETRACKER_DATA_DIR, Env:PRICETRACKER_SELFTEST
Get-Content $log
