#Requires -RunAsAdministrator
<#
.SYNOPSIS
  Bind Mitsubishi CP-9550DW-S (VID 06D3 / PID 03A5) to WinUSB on Windows 11.

.DESCRIPTION
  Official Mitsubishi CPD9550V targets the retail CP-9550DW (PID 03A1), not the
  kiosk -S unit. This script installs a WinUSB INF so CP9550Native.exe (or the
  Python print service) can send native-resolution planar BGR jobs.

  Also removes older CP9550DW-S packages that shipped a non-hex DeviceInterface
  GUID (c9550dws0001). That value makes LibUsbDotNet throw FormatException
  (Guid string should only contain hexadecimal characters).
#>

$ErrorActionPreference = 'Stop'
$inf = Join-Path $PSScriptRoot 'inf\CP9550DW-S.inf'
$expectedGuid = '{D3C9550A-06D3-4A55-9D77-C9550D050001}'
$badGuidNeedle = 'c9550dws'
$hardwareId = "USB\VID_06D3&PID_03A5"

Write-Host 'CP-9550 Native - WinUSB installer' -ForegroundColor Cyan
Write-Host "INF: $inf"
Write-Host "Expected DeviceInterfaceGUIDs: $expectedGuid"

if (-not (Test-Path $inf)) {
  throw "Missing INF at $inf"
}

function Test-GuidIsHex {
  param([string]$GuidText)
  return [bool]($GuidText -match '^\{?[0-9A-Fa-f]{8}-([0-9A-Fa-f]{4}-){3}[0-9A-Fa-f]{12}\}?$')
}

function Get-Cp9550PublishedDrivers {
  # pnputil /enum-drivers is hard to parse; map oem*.inf Original Name via Windows\INF.
  $results = @()
  Get-ChildItem "$env:WINDIR\INF\oem*.inf" -ErrorAction SilentlyContinue | ForEach-Object {
    $text = Get-Content $_.FullName -Raw -ErrorAction SilentlyContinue
    if (-not $text) { return }
    if ($text -notmatch 'VID_06D3&PID_03A5') { return }
    if ($text -notmatch 'CP9550|CP-9550DW-S') { return }
    $guidLine = ($text -split "`r?`n" | Where-Object { $_ -match 'DeviceInterfaceGUIDs' } | Select-Object -First 1)
    $results += [pscustomobject]@{
      PublishedName = $_.Name
      Path          = $_.FullName
      GuidLine      = $guidLine
      HasBadGuid    = ($text -match $badGuidNeedle)
    }
  }
  return $results
}

Write-Host ''
Write-Host 'Scanning for older CP9550DW-S WinUSB packages...'
$published = @(Get-Cp9550PublishedDrivers)
foreach ($pkg in $published) {
  $mark = if ($pkg.HasBadGuid) { 'BAD GUID' } else { 'ok' }
  Write-Host ("  {0}: {1}" -f $pkg.PublishedName, $mark)
  if ($pkg.HasBadGuid) {
    Write-Host ("  Removing {0} (non-hex DeviceInterfaceGUIDs)..." -f $pkg.PublishedName) -ForegroundColor Yellow
    # /force so the package goes away even if still referenced; device is rebound below.
    & pnputil.exe /delete-driver $pkg.PublishedName /uninstall /force 2>&1 | ForEach-Object { Write-Host "    $_" }
  }
}

Write-Host ''
Write-Host 'Adding driver package (bumped DriverVer wins over stale store copies)...'
& pnputil.exe /add-driver $inf /install 2>&1 | ForEach-Object { Write-Host "  $_" }

Write-Host ''
Write-Host 'Forcing PnP refresh for CP-9550DW-S...'
$devices = @(Get-PnpDevice -ErrorAction SilentlyContinue | Where-Object {
  $_.InstanceId -like ($hardwareId + '*')
})
if ($devices.Count -eq 0) {
  Write-Host '  No matching USB device found yet - plug the printer in after this script.' -ForegroundColor Yellow
}
else {
  foreach ($dev in $devices) {
    Write-Host ("  Device: {0} ({1})" -f $dev.FriendlyName, $dev.InstanceId)
    try {
      # Prefer pnputil update when available (Win10 1607+ / Win11).
      & pnputil.exe /update-driver $dev.InstanceId $inf 2>&1 | ForEach-Object { Write-Host "    $_" }
    }
    catch {
      Write-Host "    pnputil /update-driver failed: $_" -ForegroundColor Yellow
    }
    try {
      Disable-PnpDevice -InstanceId $dev.InstanceId -Confirm:$false -ErrorAction Stop
      Start-Sleep -Milliseconds 500
      Enable-PnpDevice -InstanceId $dev.InstanceId -Confirm:$false -ErrorAction Stop
      Write-Host '    Device disabled/enabled to re-apply HW AddReg.'
    }
    catch {
      Write-Host "    Could not cycle device (unplug/replug instead): $_" -ForegroundColor Yellow
    }
  }
}

Write-Host ''
Write-Host 'Verifying DeviceInterfaceGUIDs in registry...'
$regOk = $false
$usbKey = "HKLM:\SYSTEM\CurrentControlSet\Enum\USB\VID_06D3&PID_03A5"
if (Test-Path $usbKey) {
  Get-ChildItem $usbKey -ErrorAction SilentlyContinue | ForEach-Object {
    $diPath = Join-Path $_.PSPath 'Device Parameters'
    $di = Get-ItemProperty -Path $diPath -Name DeviceInterfaceGUIDs -ErrorAction SilentlyContinue
    if (-not $di) {
      Write-Host ("  {0}: (no DeviceInterfaceGUIDs yet)" -f $_.PSChildName)
      return
    }
    $guids = @($di.DeviceInterfaceGUIDs)
    foreach ($g in $guids) {
      $hexOk = Test-GuidIsHex $g
      $color = if ($hexOk) { 'Green' } else { 'Red' }
      Write-Host ("  {0}: {1}" -f $_.PSChildName, $g) -ForegroundColor $color
      if ($hexOk -and ($g.Trim('{}').ToUpperInvariant() -eq $expectedGuid.Trim('{}').ToUpperInvariant())) {
        $regOk = $true
      }
      elseif (-not $hexOk) {
        Write-Host '  Non-hex GUID still present - unplug/replug USB, then re-run this script.' -ForegroundColor Red
      }
    }
  }
}
else {
  Write-Host "  USB\VID_06D3&PID_03A5 not present in Enum yet."
}

Write-Host ''
if ($regOk) {
  Write-Host 'WinUSB GUID looks valid. Restart the kiosk / CP9550Native and retry preview.' -ForegroundColor Green
}
else {
  Write-Host 'If the GUID is still wrong, unplug/replug the USB cable and re-run this script.' -ForegroundColor Yellow
  Write-Host "Fallback: Zadig -> WinUSB for USB\VID_06D3&PID_03A5, then unplug/replug." -ForegroundColor Yellow
}

Write-Host ''
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
