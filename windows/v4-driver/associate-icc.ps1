#Requires -Version 5.1
<#
.SYNOPSIS
  Seed ProgramData color layout and associate effective ICC with a printer queue.

.PARAMETER Mode
  reset | replace | layer | associate-only

.PARAMETER IccPath
  Source ICC for replace/layer (defaults to package CP955_01.ICC for reset/associate).

.PARAMETER PrinterName
  Exact Windows printer queue name (Color Management device name).
#>
param(
    [ValidateSet("reset", "replace", "layer", "associate-only")]
    [string]$Mode = "reset",

    [string]$IccPath = "",

    [Parameter(Mandatory = $true)]
    [string]$PrinterName,

    [string]$PackageIcc = ""
)

$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $PackageIcc) {
    $PackageIcc = Join-Path $here "color\CP955_01.ICC"
}
if (-not (Test-Path -LiteralPath $PackageIcc)) {
    throw "Package base ICC not found: $PackageIcc"
}

Add-Type -TypeDefinition @"
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.ComponentModel;

public static class Cp9550Mscms {
  [DllImport("mscms.dll", CharSet = CharSet.Unicode, SetLastError = true)]
  public static extern bool InstallColorProfile(string machineName, string profileName);

  [DllImport("mscms.dll", CharSet = CharSet.Unicode, SetLastError = true)]
  public static extern bool AssociateColorProfileWithDevice(
      string machineName, string profileName, string deviceName);

  public static void InstallAndAssociate(string iccPath, string deviceName) {
    var full = Path.GetFullPath(iccPath);
    if (!InstallColorProfile(null, full))
      throw new Win32Exception(Marshal.GetLastWin32Error(), "InstallColorProfile failed");
    var name = Path.GetFileName(full);
    if (!AssociateColorProfileWithDevice(null, name, deviceName))
      throw new Win32Exception(Marshal.GetLastWin32Error(), "AssociateColorProfileWithDevice failed");
  }
}
"@

$root = Join-Path $env:ProgramData "CP9550\color"
$user = Join-Path $root "user"
$uploads = Join-Path $user "uploads"
$base = Join-Path $root "CP955_01.ICC"
$active = Join-Path $user "active.icc"
$meta = Join-Path $user "calibration.json"

New-Item -ItemType Directory -Force -Path $root, $user, $uploads | Out-Null

# Always refresh immutable base from the package when the package copy is present
# and larger than a stub (lab spikes left 128-byte placeholders in ProgramData).
$needBase = -not (Test-Path -LiteralPath $base)
if (-not $needBase) {
    $needBase = ((Get-Item -LiteralPath $base).Length -lt 1024) -or
                ((Get-Item -LiteralPath $PackageIcc).Length -ne (Get-Item -LiteralPath $base).Length)
}
if ($needBase) {
    if (Test-Path -LiteralPath $base) {
        try { (Get-Item $base).IsReadOnly = $false } catch { }
    }
    Copy-Item -LiteralPath $PackageIcc -Destination $base -Force
    try { (Get-Item $base).IsReadOnly = $true } catch { }
    Write-Host "Seeded base ICC → $base ($((Get-Item $base).Length) B)"
}

function Publish-Active([string]$Source) {
    Copy-Item -LiteralPath $Source -Destination $active -Force
}

function Write-Meta($ModeName, $SourcePath, $Method, $Notes) {
    $obj = [ordered]@{
        Mode       = $ModeName
        Source     = $SourcePath
        AppliedUtc = (Get-Date).ToUniversalTime().ToString("o")
        Method     = $Method
        Notes      = $Notes
    }
    ($obj | ConvertTo-Json) | Set-Content -LiteralPath $meta -Encoding UTF8
}

function Get-IccDeviceClass([string]$Path) {
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 128) { throw "ICC too small: $Path" }
    $magic = [System.Text.Encoding]::ASCII.GetString($bytes, 36, 4)
    if ($magic -ne "acsp") { throw "ICC magic 'acsp' missing in $Path (got '$magic')" }
    return [System.Text.Encoding]::ASCII.GetString($bytes, 12, 4)
}

function Assert-Icc([string]$Path, [switch]$AllowLink) {
    $cls = Get-IccDeviceClass $Path
    $ok = @("prtr", "scnr", "mntr", "spac")
    if ($AllowLink) { $ok += "link" }
    if ($ok -notcontains $cls) {
        throw "Unsupported ICC device class '$cls' in $Path (prefer prtr)"
    }
    return $cls
}

$skipAssociate = $false

switch ($Mode) {
    "reset" {
        Publish-Active $base
        Write-Meta "reset" $base "reset" "Restored immutable base profile"
    }
    "replace" {
        if (-not $IccPath -or -not (Test-Path -LiteralPath $IccPath)) {
            throw "replace requires -IccPath to an existing ICC"
        }
        Assert-Icc $IccPath | Out-Null
        $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
        $arch = Join-Path $uploads "$stamp-$(Split-Path $IccPath -Leaf)"
        Copy-Item -LiteralPath $IccPath -Destination $arch -Force
        Publish-Active $IccPath
        Write-Meta "replace" $arch "replace" "User ICC published as active.icc"
    }
    "layer" {
        if (-not $IccPath -or -not (Test-Path -LiteralPath $IccPath)) {
            throw "layer requires -IccPath"
        }
        $cls = Assert-Icc $IccPath -AllowLink
        $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
        $arch = Join-Path $uploads "$stamp-layer-$(Split-Path $IccPath -Leaf)"
        Copy-Item -LiteralPath $IccPath -Destination $arch -Force
        # Align with ColorProfileStore.Layer: full profiles publish; link stays pending
        # until LCMS2/WCS compose writes a new effective ICC (Phase 3).
        if (@("prtr", "scnr", "mntr", "spac") -contains $cls) {
            Publish-Active $IccPath
            Write-Meta "layer" $arch "layer-passthrough" "Overlay published as active.icc; compose TBD"
        } else {
            if (-not (Test-Path -LiteralPath $active)) { Publish-Active $base }
            Write-Meta "layer-pending" $arch "layer-hook" "Device class '$cls' archived; active unchanged until compose"
            $skipAssociate = $true
            Write-Host "layer-pending: active.icc unchanged (class=$cls). Association skipped."
        }
    }
    "associate-only" {
        if (-not (Test-Path -LiteralPath $active)) {
            Publish-Active $base
            Write-Meta "reset" $base "associate-only" "Created active from base before associate"
        }
    }
}

if (-not (Test-Path -LiteralPath $active)) {
    throw "active.icc missing after mode=$Mode"
}

if ($skipAssociate) {
    Write-Host "ProgramData active: $active"
    Write-Host "Meta: $meta"
    return
}

$colorDir = Join-Path $env:SystemRoot "System32\spool\drivers\color"
New-Item -ItemType Directory -Force -Path $colorDir | Out-Null
$installed = Join-Path $colorDir "CP9550_active.icc"
Copy-Item -LiteralPath $active -Destination $installed -Force

Write-Host "Installing/associating $installed → printer '$PrinterName'"
[Cp9550Mscms]::InstallAndAssociate($installed, $PrinterName)
Write-Host "OK. Soft-proof should list CP9550_active / active profile for this queue."
Write-Host "ProgramData active: $active"
Write-Host "Meta: $meta"
