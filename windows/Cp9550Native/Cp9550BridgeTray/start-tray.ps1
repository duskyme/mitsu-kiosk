#Requires -Version 5.1
<#
.SYNOPSIS
  Build (if needed) and launch Cp9550BridgeTray in the notification area.

.DESCRIPTION
  No elevation / no UAC prompt. WinUSB must already be bound (windows\install.ps1).
  Tray auto-starts Cp9550SpoolBridge when USB 06D3:03A5 appears and stops it on unplug.

.PARAMETER NoAuto
  Disable USB auto-start (manual Start/Stop only).

.PARAMETER DryRun
  Lab only — bridge will not SendSpool.

.PARAMETER Port
  TCP port (default 9100).
#>
param(
    [switch]$NoAuto,
    [switch]$DryRun,
    [int]$Port = 9100,
    [string]$SaveDir = ""
)

$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$trayProj = Join-Path $here "Cp9550BridgeTray.csproj"
$bridgeProj = Join-Path $here "..\Cp9550SpoolBridge\Cp9550SpoolBridge.csproj"

Write-Host "== Build SpoolBridge =="
dotnet build $bridgeProj -c Release
if ($LASTEXITCODE -ne 0) { throw "SpoolBridge build failed" }

Write-Host "== Build BridgeTray =="
dotnet build $trayProj -c Release
if ($LASTEXITCODE -ne 0) { throw "Tray build failed" }

$bridgeExe = Join-Path $here "..\Cp9550SpoolBridge\bin\Release\net8.0-windows\Cp9550SpoolBridge.exe"
$trayExe = Join-Path $here "bin\Release\net8.0-windows\Cp9550BridgeTray.exe"
if (-not (Test-Path $bridgeExe)) { throw "Missing $bridgeExe" }
if (-not (Test-Path $trayExe)) { throw "Missing $trayExe" }

$argList = @(
    "--port", "$Port",
    "--bridge-exe", $bridgeExe
)
if ($NoAuto) { $argList += "--no-auto" }
if ($DryRun) { $argList += "--dry-run" }
if ($SaveDir) { $argList += @("--save-dir", $SaveDir) }

Write-Host "Starting tray (no UAC): $trayExe"
Write-Host "  Look near the clock / notification area for CP-9550 Bridge."
Start-Process -FilePath $trayExe -ArgumentList $argList
