#Requires -Version 5.1
<#
.SYNOPSIS
  Launch Cp9550BridgeTray (notification area). No elevation / no UAC.

.DESCRIPTION
  By default this only starts the tray for the current session.
  Windows logon auto-start is OPTIONAL — use -RegisterStartup / -UnregisterStartup
  (HKCU Run; no Admin). Distinct from -NoAuto (USB plug auto-start of SpoolBridge).

.PARAMETER NoAuto
  Disable USB auto-start (manual Start/Stop only).

.PARAMETER DryRun
  Lab only — bridge will NOT SendSpool (printer stays idle).

.PARAMETER Port
  TCP listen port (default 9100; must match the V4 Standard TCP/IP Port).

.PARAMETER RegisterStartup
  Write HKCU\...\Run\CP9550BridgeTray so the tray starts at user logon.
  Still launches the tray unless -RegisterOnly.

.PARAMETER UnregisterStartup
  Remove the HKCU Run value. Still launches the tray unless -UnregisterOnly.

.PARAMETER RegisterOnly
  With -RegisterStartup: only write the Run key; do not start the tray now.

.PARAMETER UnregisterOnly
  With -UnregisterStartup: only remove the Run key; do not start the tray now.
#>
param(
    [switch]$NoAuto,
    [switch]$DryRun,
    [int]$Port = 9100,
    [string]$SaveDir = "",
    [switch]$RegisterStartup,
    [switch]$UnregisterStartup,
    [switch]$RegisterOnly,
    [switch]$UnregisterOnly
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$tray = Join-Path $root "bin\Cp9550BridgeTray.exe"
$bridge = Join-Path $root "bin\Cp9550SpoolBridge.exe"
if (-not (Test-Path $tray)) { throw "Missing $tray — reinstall the package." }
if (-not (Test-Path $bridge)) { throw "Missing $bridge — reinstall the package." }

$argList = [System.Collections.Generic.List[string]]::new()
$argList.AddRange([string[]]@("--port", "$Port", "--bridge-exe", $bridge))
if ($NoAuto) { $argList.Add("--no-auto") }
if ($DryRun) {
    Write-Warning "DryRun: Windows may report print SUCCESS while the printer stays idle."
    $argList.Add("--dry-run")
}
if ($SaveDir) { $argList.AddRange([string[]]@("--save-dir", $SaveDir)) }

if ($UnregisterStartup) {
    Write-Host "Unregistering logon startup (HKCU Run\CP9550BridgeTray)..."
    $unArgs = @("--unregister-startup") + $argList
    & $tray @unArgs
    if ($LASTEXITCODE -ne 0) { throw "unregister-startup failed (exit $LASTEXITCODE)" }
    if ($UnregisterOnly) { return }
}

if ($RegisterStartup) {
    Write-Host "Registering logon startup (HKCU Run\CP9550BridgeTray)..."
    $regArgs = @("--register-startup") + $argList
    if (-not $RegisterOnly) { $regArgs += "--and-run" }
    if ($RegisterOnly) {
        & $tray @regArgs
        if ($LASTEXITCODE -ne 0) { throw "register-startup failed (exit $LASTEXITCODE)" }
        Write-Host "Registered. Tray will start at next logon (or run without -RegisterOnly to start now)."
        return
    }
    # --and-run: tray registers then shows UI — do not Start-Process again
    Write-Host "Starting tray with logon registration (no UAC): $tray"
    Start-Process -FilePath $tray -ArgumentList $regArgs
    return
}

Write-Host "Starting tray (no UAC): $tray"
Write-Host "  Logon auto-start is OFF unless you used -RegisterStartup or the installer task."
Write-Host "  Look near the clock / notification area for CP-9550 Bridge."
Start-Process -FilePath $tray -ArgumentList $argList
