#Requires -RunAsAdministrator
<#
.SYNOPSIS
  Bind Mitsubishi CP-9550DW-S (VID 06D3 / PID 03A5) to WinUSB on Windows 11.

.DESCRIPTION
  Official Mitsubishi CPD9550V targets the retail CP-9550DW (PID 03A1), not the
  kiosk -S unit. This script installs a WinUSB INF so CP9550Native.exe (or the
  Python print service) can send native-resolution planar BGR jobs.
#>

$ErrorActionPreference = 'Stop'
$inf = Join-Path $PSScriptRoot 'inf\CP9550DW-S.inf'

Write-Host 'CP-9550 Native - WinUSB installer' -ForegroundColor Cyan
Write-Host "INF: $inf"

if (-not (Test-Path $inf)) {
  throw "Missing INF at $inf"
}

Write-Host ''
Write-Host 'Adding driver package...'
pnputil /add-driver $inf /install

Write-Host ''
Write-Host 'If the device was already plugged in, unplug/replug the USB cable.'
Write-Host 'Then verify in Device Manager that it shows as:'
Write-Host '  Mitsubishi CP-9550DW-S (WinUSB)' -ForegroundColor Yellow
Write-Host ''
Write-Host 'Next (native app):'
Write-Host '  cd Cp9550Native'
Write-Host '  .\publish.ps1'
Write-Host '  .\dist\CP9550Native.exe'
Write-Host ''
Write-Host 'Or Python CLI:'
Write-Host '  cd print-service'
Write-Host '  python -m venv .venv'
Write-Host '  .\.venv\Scripts\Activate.ps1'
Write-Host '  pip install -r requirements.txt'
Write-Host '  python print_job.py C:\photos\photo.jpg --media 4x6 --quality superfine --print'
