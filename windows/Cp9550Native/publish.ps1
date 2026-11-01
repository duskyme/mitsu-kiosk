#Requires -Version 5.1
<#
.SYNOPSIS
  Build a self-contained CP9550Native.exe for Windows 11 x64.
#>
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

Write-Host "Publishing CP-9550 Native (win-x64, self-contained)..." -ForegroundColor Cyan
dotnet publish "$root\Cp9550Native.App\Cp9550Native.App.csproj" `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -o "$root\dist"

Write-Host "`nOutput: $root\dist\CP9550Native.exe" -ForegroundColor Yellow
Write-Host "One-time WinUSB: run $root\..\install.ps1 as Administrator (or use Zadig)."
Write-Host "Then launch CP9550Native.exe — no Python required."
