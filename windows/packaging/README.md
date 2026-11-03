# Release packaging

Builds the unofficial TEST installer + portable zip.

## Prerequisites

- .NET 8 SDK
- VS C++ tools + Windows 10/11 SDK (XPS filter)
- [Inno Setup 6](https://jrsoftware.org/isinfo.php) (`winget install JRSoftware.InnoSetup`)

## Build

```powershell
cd windows\packaging
.\build-release.ps1
```

Outputs (gitignored under repo `dist/`):

- `dist/releases/CP9550-Native-<ver>-Setup.exe`
- `dist/releases/CP9550-Native-<ver>-portable.zip`

Setup uses `PrivilegesRequired=lowest` (no UAC). Drivers are installed afterward via `Install-Drivers.ps1` from an already-elevated Admin shell.

Optional installer task `logonstartup` writes `HKCU\...\Run\CP9550BridgeTray` (unchecked by default). Same via `Start-Tray.ps1 -RegisterStartup` or the tray menu.
