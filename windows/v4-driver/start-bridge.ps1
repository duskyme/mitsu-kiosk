#Requires -Version 5.1
<#
.SYNOPSIS
  Start Cp9550SpoolBridge for the V4 queue (TCP 127.0.0.1:9100 → WinUSB SendSpool).

.PARAMETER DryRun
  Validate/save only — does NOT print. Lab use only; leaves the queue looking
  "successful" in Windows while the printer stays idle.

.PARAMETER Port
  Listen port (must match the Standard TCP/IP Port on the queue; default 9100).

.PARAMETER SaveDir
  Optional directory to save each received spool blob.

.PARAMETER KillExisting
  Stop whatever is listening on the port (may need Admin if the old process
  was started elevated — e.g. leftover --dry-run from a lab script).
#>
param(
    [switch]$DryRun,
    [switch]$KillExisting,
    [int]$Port = 9100,
    [string]$SaveDir = ""
)

$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$proj = Join-Path $here "..\Cp9550Native\Cp9550SpoolBridge\Cp9550SpoolBridge.csproj"
if (-not (Test-Path $proj)) { throw "Missing $proj" }

$owners = @(Get-NetTCPConnection -LocalPort $Port -ErrorAction SilentlyContinue |
    Select-Object -ExpandProperty OwningProcess -Unique)
if ($owners.Count -gt 0) {
    if (-not $KillExisting) {
        $pids = ($owners -join ', ')
        throw @"
Port $Port is already in use by PID(s) $pids.
If that is a leftover --dry-run bridge, Windows will report print SUCCESS while the printer stays idle.
Re-run with -KillExisting (elevate if Access Denied), or:
  Stop-Process -Id $pids -Force
"@
    }
    foreach ($pid in $owners) {
        Write-Host "Stopping PID $pid on port $Port..."
        Stop-Process -Id $pid -Force -ErrorAction Stop
    }
    Start-Sleep -Milliseconds 400
}

$args = @('--project', $proj, '-c', 'Release', '--', '--port', "$Port")
if ($DryRun) {
    Write-Warning "DRY-RUN: jobs will NOT reach the printer. Omit -DryRun for real prints."
    $args += '--dry-run'
}
if ($SaveDir) {
    New-Item -ItemType Directory -Force -Path $SaveDir | Out-Null
    $args += @('--save-dir', $SaveDir)
}

Write-Host "== Cp9550SpoolBridge =="
Write-Host "  port=$Port dryRun=$DryRun saveDir=$(if ($SaveDir) { $SaveDir } else { '-' })"
Write-Host "  Queue port must be Standard TCP/IP RAW → 127.0.0.1:$Port"
Write-Host "  Ctrl+C to stop."
Write-Host ""
& dotnet run @args
