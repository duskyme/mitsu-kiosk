#Requires -Version 5.1
<#
.SYNOPSIS
  Launch Cp9550BridgeTray (notification area). No elevation / no UAC.

.PARAMETER NoAuto
  Disable USB auto-start (manual Start/Stop only).

.PARAMETER DryRun
  Lab only — bridge will NOT SendSpool (printer stays idle).

.PARAMETER Port
  TCP listen port (default 9100; must match the V4 Standard TCP/IP Port).
#>
param(
    [switch]$NoAuto,
    [switch]$DryRun,
    [int]$Port = 9100,
    [string]$SaveDir = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$tray = Join-Path $root "bin\Cp9550BridgeTray.exe"
$bridge = Join-Path $root "bin\Cp9550SpoolBridge.exe"
if (-not (Test-Path $tray)) { throw "Missing $tray — reinstall the package." }
if (-not (Test-Path $bridge)) { throw "Missing $bridge — reinstall the package." }

$argList = @(
    "--port", "$Port",
    "--bridge-exe", $bridge
)
if ($NoAuto) { $argList += "--no-auto" }
if ($DryRun) {
    Write-Warning "DryRun: Windows may report print SUCCESS while the printer stays idle."
    $argList += "--dry-run"
}
if ($SaveDir) { $argList += @("--save-dir", $SaveDir) }

Write-Host "Starting tray (no UAC): $tray"
Write-Host "  Look near the clock / notification area for CP-9550 Bridge."
Start-Process -FilePath $tray -ArgumentList $argList
