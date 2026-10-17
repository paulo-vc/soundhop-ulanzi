# Compiles native/SoundHopHelper.cs with the C# compiler that ships with Windows
# (.NET Framework 4). No SDK or Visual Studio needed.
# Usage: powershell -ExecutionPolicy Bypass -File tools/build-helper.ps1
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$outDir = Join-Path $root 'com.ulanzi.soundhop.ulanziPlugin\bin'
New-Item -ItemType Directory -Force $outDir | Out-Null
$out = Join-Path $outDir 'soundhop-helper.exe'
# winexe: no console window and no conhost.exe process; stdio still works over pipes.
& $csc /nologo /target:winexe /optimize+ /platform:anycpu "/out:$out" (Join-Path $root 'native\SoundHopHelper.cs')
if ($LASTEXITCODE -ne 0) { throw "csc failed ($LASTEXITCODE)" }
Get-Item $out | Select-Object Name, Length
