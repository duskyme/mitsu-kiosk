#Requires -Version 5.1
<#
.SYNOPSIS
  Build a TEST Windows release package (Inno Setup) under dist/releases.

.DESCRIPTION
  Publishes SpoolBridge + BridgeTray (self-contained), stages WinUSB + V4 0.4.1+
  bits (filter DLL, ICC, install scripts), then compiles CP9550-Setup.iss.

  Does NOT elevate / does NOT trigger UAC. Signing of drivers happens later when
  the user runs Install-Drivers.ps1 from an already-elevated Admin shell.

.PARAMETER SkipBuild
  Reuse previously published/staged binaries.

.PARAMETER Configuration
  dotnet / filter configuration (default Release).
#>
param(
    [switch]$SkipBuild,
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$Version = "0.4.1"
)

$ErrorActionPreference = "Stop"
$repo = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$packaging = $PSScriptRoot
$native = Join-Path $repo "windows\Cp9550Native"
$v4 = Join-Path $repo "windows\v4-driver"
$stage = Join-Path $packaging "_stage"
$outDir = Join-Path $repo "dist\releases"
$iscc = Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"
if (-not (Test-Path $iscc)) {
    $iscc = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
}
if (-not (Test-Path $iscc)) {
    throw "ISCC.exe not found. Install Inno Setup 6 (winget install JRSoftware.InnoSetup)."
}

Write-Host "== CP-9550 release packager $Version ==" -ForegroundColor Cyan
Write-Host "Repo: $repo"

$filterDll = Join-Path $v4 "filter\_out\$Configuration\Cp9550RenderFilter.dll"
if (-not $SkipBuild) {
    Write-Host "== Build XPS render filter =="
    & (Join-Path $v4 "filter\build.ps1") -Configuration $Configuration
    if ($LASTEXITCODE -ne 0) { throw "filter build failed" }
}
if (-not (Test-Path $filterDll)) {
    throw "Missing filter DLL: $filterDll"
}

$pubBridge = Join-Path $stage "bin"
$pubTray = Join-Path $stage "bin"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
New-Item -ItemType Directory -Force -Path $pubBridge | Out-Null

if (-not $SkipBuild) {
    Write-Host "== Publish SpoolBridge (self-contained win-x64) =="
    dotnet publish (Join-Path $native "Cp9550SpoolBridge\Cp9550SpoolBridge.csproj") `
        -c $Configuration -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:Version=$Version -o $pubBridge
    if ($LASTEXITCODE -ne 0) { throw "SpoolBridge publish failed" }

    Write-Host "== Publish BridgeTray (self-contained win-x64) =="
    dotnet publish (Join-Path $native "Cp9550BridgeTray\Cp9550BridgeTray.csproj") `
        -c $Configuration -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:Version=$Version -o $pubTray
    if ($LASTEXITCODE -ne 0) { throw "BridgeTray publish failed" }
}

# WinUSB package tree (install.ps1 expects inf\ next to itself)
$winusb = Join-Path $stage "winusb"
New-Item -ItemType Directory -Force -Path (Join-Path $winusb "inf") | Out-Null
Copy-Item (Join-Path $repo "windows\install.ps1") $winusb
Copy-Item (Join-Path $repo "windows\inf\CP9550DW-S.inf") (Join-Path $winusb "inf\")

# V4 package tree (install-printer.ps1 layout)
$v4Stage = Join-Path $stage "v4-driver"
New-Item -ItemType Directory -Force -Path $v4Stage | Out-Null
foreach ($rel in @(
        "CP9550-manifest.ini",
        "install-printer.ps1",
        "start-bridge.ps1",
        "associate-icc.ps1",
        "README.md",
        "gpd\CP9550.gpd",
        "pipeline\CP9550-pipelineconfig.xml",
        "color\CP955_01.ICC",
        "color\README.md",
        "inf\CP9550DW-S-Printer.inf"
    )) {
    $src = Join-Path $v4 $rel
    $dst = Join-Path $v4Stage $rel
    New-Item -ItemType Directory -Force -Path (Split-Path $dst -Parent) | Out-Null
    Copy-Item $src $dst
}
$filterOut = Join-Path $v4Stage "filter\_out\$Configuration"
New-Item -ItemType Directory -Force -Path $filterOut | Out-Null
Copy-Item $filterDll $filterOut
# Keep start-bridge.ps1 working: it looks for ..\Cp9550Native\Cp9550SpoolBridge\.csproj
# Rewrite a release-friendly start-bridge that points at staged bin.
$startBridgeRelease = @'
#Requires -Version 5.1
param(
    [switch]$DryRun,
    [switch]$KillExisting,
    [int]$Port = 9100,
    [string]$SaveDir = ""
)
$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$exe = Join-Path $here "..\bin\Cp9550SpoolBridge.exe"
if (-not (Test-Path $exe)) { throw "Missing $exe" }

$owners = @(Get-NetTCPConnection -LocalPort $Port -ErrorAction SilentlyContinue |
    Select-Object -ExpandProperty OwningProcess -Unique)
if ($owners.Count -gt 0) {
    if (-not $KillExisting) {
        throw "Port $Port already in use by PID(s) $($owners -join ', '). Re-run with -KillExisting."
    }
    foreach ($p in $owners) {
        Stop-Process -Id $p -Force -ErrorAction SilentlyContinue
    }
    Start-Sleep -Milliseconds 400
}

$argsList = @("--port", "$Port")
if ($DryRun) { $argsList += "--dry-run" }
if ($SaveDir) { $argsList += @("--save-dir", $SaveDir) }
Write-Host "Starting SpoolBridge: $exe $($argsList -join ' ')"
Start-Process -FilePath $exe -ArgumentList $argsList
'@
Set-Content -LiteralPath (Join-Path $v4Stage "start-bridge.ps1") -Value $startBridgeRelease -Encoding UTF8

# Install / tray wrappers at stage root
Copy-Item (Join-Path $packaging "Install-Drivers.ps1") $stage
Copy-Item (Join-Path $packaging "Start-Tray.ps1") $stage
Copy-Item (Join-Path $packaging "START-HERE.txt") $stage
Copy-Item (Join-Path $repo "LICENSE") (Join-Path $stage "LICENSE.txt")
Get-ChildItem $stage -Recurse -Include *.pdb,*.xml,*.deps.json | Remove-Item -Force -ErrorAction SilentlyContinue

# Version stamp for Inno
$verFile = Join-Path $stage "VERSION.txt"
Set-Content -LiteralPath $verFile -Value $Version -Encoding ASCII

Write-Host "== Compile Inno Setup =="
$iss = Join-Path $packaging "CP9550-Setup.iss"
& $iscc "/DMyAppVersion=$Version" "/O$outDir" $iss
if ($LASTEXITCODE -ne 0) { throw "ISCC failed with exit $LASTEXITCODE" }

$setup = Get-ChildItem $outDir -Filter "CP9550-Native-*-Setup.exe" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $setup) { throw "Setup exe not found under $outDir" }

# Also zip the stage tree for users who prefer portable layout
$zip = Join-Path $outDir ("CP9550-Native-{0}-portable.zip" -f $Version)
if (Test-Path $zip) { Remove-Item -Force $zip }
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip -CompressionLevel Optimal

Write-Host ""
Write-Host "Release artifacts:" -ForegroundColor Yellow
Write-Host "  $($setup.FullName)"
Write-Host "  $zip"
Write-Host "Done."
