# CP-9550DW-S — V4 hybrid (Phase 1)

Hybrid architecture from the project plan:

1. **WinUSB** keeps owning the device (`windows\inf\CP9550DW-S.inf`).
2. **`Cp9550SpoolBridge`** listens on `127.0.0.1:9100` and calls `PrinterDevice.SendSpool`.
3. **V4 queue** is what apps see; port = Standard TCP/IP → bridge.
4. **`Cp9550RenderFilter`** turns XPS → planar RGB spool (offline via `cp9550-spooltool` today; pipeline wiring still blocked — see below).
5. **ICC** `color/CP955_01.ICC` installs to dirid 66003; `associate-icc.ps1` publishes `active.icc`.

## Build the filter

```powershell
cd windows\v4-driver\filter
.\build.ps1
```

## Quick start — bridge + offline spool (hardware path)

```powershell
cd windows\v4-driver
.\filter\_out\Release\cp9550-spooltool.exe --solid 220 40 40 --out _capture\job.bin

# Live USB (WinUSB must be bound — windows\install.ps1):
dotnet run --project ..\Cp9550Native\Cp9550SpoolBridge -- --spool-file _capture\job.bin

# Dry-run (no printer):
dotnet run --project ..\Cp9550Native\Cp9550SpoolBridge -- --dry-run --spool-file _capture\job.bin
```

## Queue install (Admin; testsigning or WDK test cert)

```powershell
# Admin — Inf2Cat + SignTool + pnputil + Add-PrinterDriver + queue + ICC
.\install-printer.ps1 -Sign
```

Requires `testsigning` on **or** a trusted WDK/test cert (this lab used `WDKTestCert` in CurrentUser\My + LocalMachine Root/TrustedPublisher).

## ICC (S3)

```powershell
# Admin — printer queue must exist
.\associate-icc.ps1 -PrinterName "Mitsubishi CP-9550DW-S (V4)" -Mode reset
.\associate-icc.ps1 -PrinterName "Mitsubishi CP-9550DW-S (V4)" -Mode replace -IccPath .\my.icc
```

Layout: `%ProgramData%\CP9550\color\` (`CP955_01.ICC`, `user\active.icc`, `user\calibration.json`).
Installed association file: `%SystemRoot%\System32\spool\drivers\color\CP9550_active.icc`.

## Lab status (MK-9)

| Piece | State |
| --- | --- |
| S1 Bridge live USB | **PASS** — `--spool-file` → `SendSpool` → CP-9550DW-S (solid 4×6) |
| S3 ICC / Color Mgmt | **PASS** — queue installed; `CP9550_active.icc` associated (511 824 B) |
| Test-signed queue | **PASS** — ClassVer 4.0 package, MajorVersion 4, port `CP9550_TCP_9100` |
| XPS → filter → bridge | **GAP** — empty pipeline in package; wiring `Cp9550RenderFilter.dll` triggers PrintService event 235 (RequiredFiles/core) |
| WinUSB vs usbprint | After some PnP churn usbprint can reclaim the device; `windows\install.ps1` now force-binds WinUSB via `UpdateDriverForPlugAndPlayDevices` |

### Packaging constraints discovered

- GPD must include `CmdCR` / `CmdLF` / `CmdFF` / `CmdSendBlockData` or `Add-Printer` fails with `0x800703eb`.
- Pipeline file must be named `CP9550-pipelineconfig.xml` (stem of `DataFile`).
- Do **not** put IHV filter DLLs in V4 `RequiredFiles` (those resolve from core packages only).
