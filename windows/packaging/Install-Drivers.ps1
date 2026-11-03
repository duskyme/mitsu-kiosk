#Requires -RunAsAdministrator
#Requires -Version 5.1
<#
.SYNOPSIS
  Install WinUSB (-Sign) + V4 printer queue 0.4.1+ (-Sign) from a release layout.

.DESCRIPTION
  Must be run from an already-elevated Admin PowerShell. This script never
  triggers interactive UAC (#Requires -RunAsAdministrator fails fast if not Admin).

  Prerequisites:
    - Printer USB connected (VID 06D3 / PID 03A5)
    - Windows SDK Inf2Cat + SignTool (WDK kit)
    - WDKTestCert (or -CertSubject) in CurrentUser\My; preferably also imported
      into LocalMachine\Root + TrustedPublisher (avoids bcdedit testsigning)

.PARAMETER CertSubject
  Certificate subject substring for SignTool /n.

.PARAMETER SkipWinUsb
  Only install / refresh the V4 queue package.

.PARAMETER SkipV4
  Only install / refresh WinUSB.
#>
param(
    [switch]$SkipWinUsb,
    [switch]$SkipV4,
    [string]$CertSubject = "WDKTestCert"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$winusb = Join-Path $root "winusb\install.ps1"
$v4 = Join-Path $root "v4-driver\install-printer.ps1"

Write-Host "== CP-9550 TEST driver install ==" -ForegroundColor Cyan
Write-Host "Root: $root"
Write-Host "UNOFFICIAL TEST BUILD — not WHQL, not Mitsubishi-signed." -ForegroundColor Yellow

if (-not $SkipWinUsb) {
    if (-not (Test-Path $winusb)) { throw "Missing $winusb" }
    Write-Host ""
    Write-Host "== WinUSB (-Sign) ==" -ForegroundColor Cyan
    & $winusb -Sign -CertSubject $CertSubject
}

if (-not $SkipV4) {
    if (-not (Test-Path $v4)) { throw "Missing $v4" }
    Write-Host ""
    Write-Host "== V4 printer queue (-Sign -ForceReplace) ==" -ForegroundColor Cyan
    & $v4 -Sign -ForceReplace -CertSubject $CertSubject
}

Write-Host ""
Write-Host "Next (no Admin required):" -ForegroundColor Green
Write-Host "  .\Start-Tray.ps1"
Write-Host "Then print to queue 'Mitsubishi CP-9550DW-S (V4)' from any app."
Write-Host "If jobs succeed in Windows but printer stays idle, kill any --dry-run bridge."
