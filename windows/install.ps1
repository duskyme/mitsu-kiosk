#Requires -RunAsAdministrator
#Requires -Version 5.1
<#
.SYNOPSIS
  Bind Mitsubishi CP-9550DW-S (VID 06D3 / PID 03A5) to WinUSB on Windows 11.

.DESCRIPTION
  Official Mitsubishi CPD9550V targets the retail CP-9550DW (PID 03A1), not the
  kiosk -S unit. This script installs a WinUSB INF so CP9550Native.exe (or the
  SpoolBridge / tray) can send native-resolution planar RGB jobs.

  Also removes older CP9550DW-S packages that shipped a non-hex DeviceInterface
  GUID (c9550dws0001). That value makes LibUsbDotNet throw FormatException
  (Guid string should only contain hexadecimal characters).

  -Sign mirrors windows\v4-driver\install-printer.ps1 -Sign:
    Inf2Cat → SignTool (WDKTestCert) → pnputil of the signed catalog package.

  Signing vs nagging:
    • Preferred: WDKTestCert in CurrentUser\My AND imported to LocalMachine\Root
      + TrustedPublisher. Then Driver Signature Enforcement accepts the package
      without bcdedit testsigning (same as V4 queue).
    • Fallback: bcdedit /set testsigning on (+ reboot) if the cert is not trusted
      machine-wide. Secure Boot machines still need a trusted test cert or
      testsigning — this script never pops interactive UAC beyond the required
      elevated shell (#Requires -RunAsAdministrator fails fast if not Admin).

.PARAMETER Sign
  Build CP9550DW-S.cat with Inf2Cat and sign with WDK/test cert before pnputil.

.PARAMETER CertSubject
  Certificate subject substring for SignTool /n (CurrentUser\My).

.PARAMETER SkipBind
  Only stage/sign/add the package; skip device update/cycle.
#>
param(
    [switch]$Sign,
    [switch]$SkipBind,
    [string]$CertSubject = "WDKTestCert"
)

$ErrorActionPreference = 'Stop'
$infSource = Join-Path $PSScriptRoot 'inf\CP9550DW-S.inf'
$expectedGuid = '{D3C9550A-06D3-4A55-9D77-C9550D050001}'
$badGuidNeedle = 'c9550dws'
$hardwareId = "USB\VID_06D3&PID_03A5"
$stage = Join-Path $PSScriptRoot '_stage_winusb'

Write-Host 'CP-9550 Native - WinUSB installer' -ForegroundColor Cyan
Write-Host "INF source: $infSource"
Write-Host "Expected DeviceInterfaceGUIDs: $expectedGuid"
Write-Host "Sign: $Sign  CertSubject: $CertSubject"

if (-not (Test-Path $infSource)) {
  throw "Missing INF at $infSource"
}

function Test-GuidIsHex {
  param([string]$GuidText)
  return [bool]($GuidText -match '^\{?[0-9A-Fa-f]{8}-([0-9A-Fa-f]{4}-){3}[0-9A-Fa-f]{12}\}?$')
}

function Find-KitTool([string]$Name) {
  $kit = "${env:ProgramFiles(x86)}\Windows Kits\10\bin"
  if (-not (Test-Path $kit)) { return $null }
  Get-ChildItem $kit -Recurse -Filter $Name -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match '\\x64\\|\\x86\\' } |
    Sort-Object FullName -Descending |
    Select-Object -First 1 -ExpandProperty FullName
}

function Test-TestSigningEnabled {
  $out = & bcdedit /enum 2>$null | Out-String
  if ($out -match "(?im)^\s*testsigning\s+Yes\s*$") { return $true }
  return $false
}

function Assert-WdkTestCertReady([string]$SubjectSubstr) {
  $my = @(Get-ChildItem Cert:\CurrentUser\My -ErrorAction SilentlyContinue |
    Where-Object { $_.Subject -like "*$SubjectSubstr*" -and $_.HasPrivateKey })
  if ($my.Count -eq 0) {
    throw "No CurrentUser\My cert matching '*$SubjectSubstr*' with a private key. Create/import a WDK test cert first (same as V4 -Sign)."
  }
  $tp = $my[0].Thumbprint
  $root = Get-ChildItem Cert:\LocalMachine\Root -ErrorAction SilentlyContinue | Where-Object Thumbprint -eq $tp
  $pub = Get-ChildItem Cert:\LocalMachine\TrustedPublisher -ErrorAction SilentlyContinue | Where-Object Thumbprint -eq $tp
  if (-not $root -or -not $pub) {
    Write-Warning @"
WDK test cert '$($my[0].Subject)' is in CurrentUser\My but not fully trusted for DriverStore.
Import the same cert (Admin) into LocalMachine\Root and LocalMachine\TrustedPublisher, e.g.:
  Export-Certificate -Cert Cert:\CurrentUser\My\$tp -FilePath `$env:TEMP\wdk.cer
  Import-Certificate -FilePath `$env:TEMP\wdk.cer -CertStoreLocation Cert:\LocalMachine\Root
  Import-Certificate -FilePath `$env:TEMP\wdk.cer -CertStoreLocation Cert:\LocalMachine\TrustedPublisher
Without that, enable testsigning (bcdedit /set testsigning on + reboot) or Windows will still nag / reject the package.
"@
  } else {
    Write-Host "WDK test cert OK: $($my[0].Subject) (Root+TrustedPublisher) — bcdedit testsigning NOT required" -ForegroundColor Green
  }
  return $my[0]
}

function Get-Cp9550PublishedDrivers {
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

# Stage a flat package (INF + optional .cat) so Inf2Cat / pnputil share one folder.
if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
New-Item -ItemType Directory -Force -Path $stage | Out-Null
Copy-Item -LiteralPath $infSource -Destination (Join-Path $stage 'CP9550DW-S.inf') -Force
$inf = Join-Path $stage 'CP9550DW-S.inf'

if ($Sign) {
  $null = Assert-WdkTestCertReady $CertSubject
  $inf2cat = Find-KitTool "Inf2Cat.exe"
  $signtool = Find-KitTool "signtool.exe"
  if (-not $inf2cat -or -not $signtool) {
    throw "Sign requested but Inf2Cat/signtool not found under Windows Kits\10\bin"
  }
  Write-Host "Inf2Cat..."
  & $inf2cat /driver:$stage /os:10_X64
  if ($LASTEXITCODE -ne 0) { throw "Inf2Cat failed ($LASTEXITCODE)" }
  $cat = Get-ChildItem $stage -Filter "*.cat" | Select-Object -First 1
  if (-not $cat) { throw "Inf2Cat produced no .cat in $stage" }
  $catPath = Join-Path $stage "CP9550DW-S.cat"
  if ($cat.FullName -ne $catPath) {
    Move-Item -LiteralPath $cat.FullName -Destination $catPath -Force
  }
  Write-Host "SignTool /fd SHA256 /n $CertSubject..."
  & $signtool sign /fd SHA256 /s My /n $CertSubject /tr http://timestamp.digicert.com /td SHA256 $catPath
  if ($LASTEXITCODE -ne 0) {
    Write-Warning "Timestamped sign failed; retrying without timestamp"
    & $signtool sign /fd SHA256 /s My /n $CertSubject $catPath
  }
  if ($LASTEXITCODE -ne 0) { throw "SignTool failed ($LASTEXITCODE)" }
  & $signtool verify /pa $catPath
  if ($LASTEXITCODE -ne 0) { throw "SignTool verify failed ($LASTEXITCODE)" }
  $sig = Get-AuthenticodeSignature $catPath
  Write-Host "Catalog signature: $($sig.Status) ($($sig.SignerCertificate.Subject))"
}

$ts = Test-TestSigningEnabled
Write-Host ("testsigning: " + $(if ($ts) { "ON" } else { "OFF (OK if WDK test cert is in Root+TrustedPublisher)" }))

Write-Host ''
Write-Host 'Scanning for older CP9550DW-S WinUSB packages...'
$published = @(Get-Cp9550PublishedDrivers)
foreach ($pkg in $published) {
  $mark = if ($pkg.HasBadGuid) { 'BAD GUID' } else { 'ok' }
  Write-Host ("  {0}: {1}" -f $pkg.PublishedName, $mark)
  if ($pkg.HasBadGuid) {
    Write-Host ("  Removing {0} (non-hex DeviceInterfaceGUIDs)..." -f $pkg.PublishedName) -ForegroundColor Yellow
    & pnputil.exe /delete-driver $pkg.PublishedName /uninstall /force 2>&1 | ForEach-Object { Write-Host "    $_" }
  }
}

Write-Host ''
Write-Host 'Adding driver package (bumped DriverVer wins over stale store copies)...'
$pnpOut = & pnputil.exe /add-driver $inf /install 2>&1 | Out-String
$pnpExit = $LASTEXITCODE
Write-Host $pnpOut.TrimEnd()
Write-Host "pnputil exit: $pnpExit"
# 0 = added. 259 = identical package already present (same as V4 install-printer.ps1).
$alreadyPresent = ($pnpExit -eq 259) -or (($pnpOut -match "(?i)Added driver packages:\s*0") -and ($pnpOut -match "(?i)Total driver packages:\s*1"))
if ($pnpExit -ne 0 -and -not $alreadyPresent) {
  Write-Warning @"
Driver store add failed (exit $pnpExit).
Checklist:
  1) Re-run elevated with -Sign (WDKTestCert in CurrentUser\My).
  2) Cert also in LocalMachine\Root + TrustedPublisher (see warning above).
  3) Or: bcdedit /set testsigning on  (reboot), then re-run -Sign.
"@
} elseif ($alreadyPresent -and $pnpExit -ne 0) {
  Write-Host "pnputil ${pnpExit}: package already in DriverStore (not a signing failure)."
}

if (-not $SkipBind) {
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
        & pnputil.exe /update-driver $dev.InstanceId $inf 2>&1 | ForEach-Object { Write-Host "    $_" }
      }
      catch {
        Write-Host "    pnputil /update-driver failed: $_" -ForegroundColor Yellow
      }
      # usbprint.sys often stays bound after a V4/USB001 experiment even when our
      # package is "up-to-date". Force the WinUSB INF onto the hardware ID.
      try {
        if (-not ("Cp9550UpdDrv" -as [type])) {
          Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class Cp9550UpdDrv {
  [DllImport("newdev.dll", CharSet=CharSet.Unicode, SetLastError=true)]
  public static extern bool UpdateDriverForPlugAndPlayDevices(
      IntPtr hwndParent, string HardwareId, string FullInfPath, uint InstallFlags, out bool bRebootRequired);
  public const uint INSTALLFLAG_FORCE = 0x1;
  public const uint INSTALLFLAG_NONINTERACTIVE = 0x4;
}
"@
        }
        $reboot = $false
        $ok = [Cp9550UpdDrv]::UpdateDriverForPlugAndPlayDevices(
            [IntPtr]::Zero, $hardwareId, $inf,
            [Cp9550UpdDrv]::INSTALLFLAG_FORCE -bor [Cp9550UpdDrv]::INSTALLFLAG_NONINTERACTIVE,
            [ref]$reboot)
        $err = [Runtime.InteropServices.Marshal]::GetLastWin32Error()
        Write-Host ("    UpdateDriverForPlugAndPlayDevices ok={0} reboot={1} win32={2}" -f $ok, $reboot, $err)
      }
      catch {
        Write-Host "    UpdateDriverForPlugAndPlayDevices failed: $_" -ForegroundColor Yellow
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
  Write-Host 'WinUSB GUID looks valid. Start the bridge tray (or CP9550Native) and retry.' -ForegroundColor Green
}
else {
  Write-Host 'If the GUID is still wrong, unplug/replug the USB cable and re-run this script.' -ForegroundColor Yellow
  Write-Host "Fallback: Zadig -> WinUSB for USB\VID_06D3&PID_03A5, then unplug/replug." -ForegroundColor Yellow
}

Write-Host ''
Write-Host 'Then verify in Device Manager that it shows as:'
Write-Host '  Mitsubishi CP-9550DW-S (WinUSB)' -ForegroundColor Yellow
Write-Host ''
Write-Host 'Next — tray (USB auto-start, no UAC):'
Write-Host '  cd Cp9550Native\Cp9550BridgeTray'
Write-Host '  .\start-tray.ps1'
Write-Host ''
Write-Host 'Or V4 queue (Admin):'
Write-Host '  cd ..\v4-driver'
Write-Host '  .\install-printer.ps1 -Sign -ForceReplace'
Write-Host ''
Write-Host 'Or native app:'
Write-Host '  cd Cp9550Native'
Write-Host '  .\publish.ps1'
Write-Host '  .\dist\CP9550Native.exe'
