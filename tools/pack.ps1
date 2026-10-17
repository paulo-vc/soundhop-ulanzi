# Builds dist/SoundHop-<version>.zip with the plugin folder, the compiled helper
# and its runtime dependencies, ready to extract into %APPDATA%\Ulanzi\UlanziDeck\Plugins.
# Usage: powershell -ExecutionPolicy Bypass -File tools/pack.ps1
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$name = 'com.ulanzi.soundhop.ulanziPlugin'
$src = Join-Path $root $name
$version = (Get-Content (Join-Path $src 'manifest.json') -Raw | ConvertFrom-Json).Version

& (Join-Path $PSScriptRoot 'build-helper.ps1') | Out-Null

$stage = Join-Path $root 'dist\stage'
$zip = Join-Path $root "dist\SoundHop-$version.zip"
Remove-Item -Recurse -Force $stage -ErrorAction SilentlyContinue
Remove-Item -Force $zip -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $stage | Out-Null

Copy-Item -Recurse $src (Join-Path $stage $name)
$target = Join-Path $stage $name
Remove-Item -Recurse -Force (Join-Path $target 'node_modules'), (Join-Path $target 'runtime-icons') -ErrorAction SilentlyContinue

Push-Location $target
try { npm ci --omit=dev --no-audit --no-fund | Out-Null } finally { Pop-Location }

Compress-Archive -Path (Join-Path $stage $name) -DestinationPath $zip
Remove-Item -Recurse -Force $stage
Write-Output $zip
