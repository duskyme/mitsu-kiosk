#Requires -Version 5.1
<#
.SYNOPSIS
  Build Cp9550RenderFilter.dll, cp9550_spool_test.exe, and cp9550-spooltool.exe with MSVC.

.NOTES
  Needs VS C++ tools + Windows 10/11 SDK (filterpipeline.h, xpsrassvc.h).
#>
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$SkipTest
)

$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$out = Join-Path $here "_out\$Configuration"
New-Item -ItemType Directory -Force -Path $out | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $out "obj") | Out-Null

function Find-VcVars {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
    if (-not (Test-Path $vswhere)) { throw "vswhere.exe not found" }
    $vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if (-not $vs) { throw "MSVC toolset not installed" }
    $vcvars = Join-Path $vs "VC\Auxiliary\Build\vcvars64.bat"
    if (-not (Test-Path $vcvars)) { throw "vcvars64.bat missing at $vcvars" }
    return $vcvars
}

$vcvars = Find-VcVars
$obj = Join-Path $out "obj"
$cflags = "/nologo /W3 /EHsc /DUNICODE /D_UNICODE /DWIN32 /D_WINDOWS /I`"$here\include`" /I`"$here\dll`" /Fo`"$obj\\`""
if ($Configuration -eq "Release") {
    $cflags += " /O2 /DNDEBUG"
} else {
    $cflags += " /Od /Zi /D_DEBUG"
}

$spoolSrc = Join-Path $here "spool\cp9550_spool.c"
$testSrc = Join-Path $here "spool\cp9550_spool_test.c"
$toolSrc = Join-Path $here "tools\spooltool.c"
$dllSrcs = @(
    (Join-Path $here "dll\dllmain.cpp"),
    (Join-Path $here "dll\filter.cpp"),
    (Join-Path $here "dll\omconvert.cpp"),
    (Join-Path $here "dll\streamutil.cpp"),
    $spoolSrc
) -join " "
$def = Join-Path $here "Cp9550RenderFilter.def"
$libs = "ole32.lib oleaut32.lib uuid.lib shlwapi.lib windowscodecs.lib msxml6.lib"

$bat = @"
@echo off
call "$vcvars" >nul
if errorlevel 1 exit /b 1

echo == spool unit test ==
cl $cflags /TC "$spoolSrc" "$testSrc" /Fe:"$out\cp9550_spool_test.exe" /link /nologo
if errorlevel 1 exit /b 1

echo == spooltool ==
cl $cflags /TC "$spoolSrc" "$toolSrc" /Fe:"$out\cp9550-spooltool.exe" /link /nologo
if errorlevel 1 exit /b 1

echo == Cp9550RenderFilter.dll ==
cl $cflags /DCP9550_SPOOL_STATIC $dllSrcs /LD /Fe:"$out\Cp9550RenderFilter.dll" /link /nologo /DEF:"$def" $libs
if errorlevel 1 exit /b 1

exit /b 0
"@

$batPath = Join-Path $out "_build.cmd"
Set-Content -LiteralPath $batPath -Value $bat -Encoding ASCII
& cmd.exe /c $batPath
if ($LASTEXITCODE -ne 0) {
    throw "Native build failed (exit $LASTEXITCODE)"
}

if (-not $SkipTest) {
    Write-Host "== run cp9550_spool_test =="
    & (Join-Path $out "cp9550_spool_test.exe")
    if ($LASTEXITCODE -ne 0) { throw "spool test failed" }
}

Write-Host "Built:"
Get-ChildItem $out -File | Where-Object { $_.Extension -match '\.(dll|exe|pdb|lib|exp)$' } | ForEach-Object {
    Write-Host ("  " + $_.FullName)
}
