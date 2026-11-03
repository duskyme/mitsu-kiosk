#Requires -Version 5.1
<#
.SYNOPSIS
  Build (if needed) and launch Cp9550BridgeTray in the notification area.

.DESCRIPTION
  No elevation / no UAC prompt. WinUSB must already be bound (windows\install.ps1).
  Tray auto-starts Cp9550SpoolBridge when USB 06D3:03A5 appears and stops it on unplug.

  Windows logon auto-start is OPTIONAL (HKCU Run) - use -RegisterStartup /
  -UnregisterStartup. That is separate from -NoAuto (USB plug control).

.PARAMETER NoAuto
  Disable USB auto-start (manual Start/Stop only).

.PARAMETER DryRun
  Lab only - bridge will not SendSpool.

.PARAMETER Port
  TCP port (default 9100).

.PARAMETER RegisterStartup
  Write HKCU Run so the tray starts at user logon.

.PARAMETER UnregisterStartup
  Remove the HKCU Run value.

.PARAMETER RegisterOnly
  With -RegisterStartup: only write the Run key.

.PARAMETER UnregisterOnly
  With -UnregisterStartup: only remove the Run key.
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

$argList = [System.Collections.Generic.List[string]]::new()
$argList.AddRange([string[]]@("--port", "$Port", "--bridge-exe", $bridgeExe))
if ($NoAuto) { $argList.Add("--no-auto") }
if ($DryRun) { $argList.Add("--dry-run") }
if ($SaveDir) { $argList.AddRange([string[]]@("--save-dir", $SaveDir)) }

if ($UnregisterStartup) {
    Write-Host "Unregistering logon startup (HKCU Run\CP9550BridgeTray)..."
    $unArgs = @("--unregister-startup") + $argList.ToArray()
    & $trayExe @unArgs
    if ($UnregisterOnly) { return }
}

if ($RegisterStartup) {
    Write-Host "Registering logon startup (HKCU Run\CP9550BridgeTray)..."
    $regArgs = @("--register-startup") + $argList.ToArray()
    if (-not $RegisterOnly) { $regArgs += "--and-run" }
    if ($RegisterOnly) {
        & $trayExe @regArgs
        Write-Host "Registered for next logon."
        return
    }
    Write-Host "Starting tray with logon registration (no UAC): $trayExe"
    Start-Process -FilePath $trayExe -ArgumentList $regArgs
    return
}

Write-Host "Starting tray (no UAC): $trayExe"
Write-Host "  Look near the clock / notification area for CP-9550 Bridge."
Write-Host "  Optional logon: .\start-tray.ps1 -RegisterStartup"
Start-Process -FilePath $trayExe -ArgumentList $argList
