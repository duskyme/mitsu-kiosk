# CP-9550DW-S — V4 hybrid (Phase 2 media/quality)

Hybrid architecture from the project plan:

1. **WinUSB** keeps owning the device (`windows\inf\CP9550DW-S.inf`) — prefer `windows\install.ps1 -Sign`.
2. **`Cp9550SpoolBridge`** listens on `127.0.0.1:9100` and calls `PrinterDevice.SendSpool` (tray auto-starts on USB).
3. **V4 queue** is what apps see; port = Standard TCP/IP → bridge. GPD exposes **4×6 / 5×7 / 6×9** and **Fine / SuperFine / FineDeep**.
4. **`Cp9550RenderFilter`** turns XPS → planar RGB spool (wired in `CP9550-pipelineconfig.xml`; offline `cp9550-spooltool` still available).
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

# Force delete + re-add when pnputil returns 259 (identical package already present)
.\install-printer.ps1 -Sign -ForceReplace
```

Requires a trusted WDK/test cert (`WDKTestCert` in CurrentUser\My **and** LocalMachine Root + TrustedPublisher), **or** `testsigning` on.  
`pnputil` exit **259** with `Added driver packages: 0` means the package is **already** in DriverStore — the script treats that as OK and refreshes `Add-PrinterDriver` (not a signing failure).

## Diagnostics

- Filter: `%ProgramData%\CP9550\log\filter.log` (ParseJobTicket media/quality, spool len/hdr)  
- Bridge: `%ProgramData%\CP9550\log\bridge.log` + `last-spool.bin`  
- If USB OUT succeeds but the printer never starts: check bridge.log for `sts3 bez 0x40/0x80` or media mismatch; restart tray after pulling a build that enumerates USB without `OpenUsbDevice` (tray used to kill the bridge mid-`SendSpool`).

## Spool bridge (required for real prints)

The V4 queue writes CP-9550 spool bytes to `127.0.0.1:9100`. **`Cp9550SpoolBridge` must be listening without `--dry-run`**, or Windows reports success while the printer stays idle.

```powershell
# Preferred — tray near the clock (USB auto-start/stop, no UAC):
..\Cp9550Native\Cp9550BridgeTray\start-tray.ps1

# Manual console:
.\start-bridge.ps1
.\start-bridge.ps1 -KillExisting   # if 9100 held (elevate if Access Denied)

# Lab only (no USB):
.\start-bridge.ps1 -DryRun -SaveDir .\_capture\bridge-lab
```

## WinUSB device INF (test-sign)

```powershell
# Already-elevated Admin shell (script does not prompt UAC):
cd ..
.\install.ps1 -Sign
```

Same WDKTestCert / Inf2Cat / SignTool path as `-Sign` on this queue. If the cert is in LocalMachine **Root + TrustedPublisher**, `bcdedit testsigning` is usually unnecessary; otherwise turn testsigning on and reboot.

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
| XPS → filter → bridge | **PASS (package)** — filter DLL in CopyFiles + pipeline; RequiredFiles stays core-only (event 235 fixed). End-to-end XPS job through queue still lab-verify. |
| WinUSB vs usbprint | After some PnP churn usbprint can reclaim the device; `windows\install.ps1` now force-binds WinUSB via `UpdateDriverForPlugAndPlayDevices` |

### Packaging constraints discovered

- GPD must include `CmdCR` / `CmdLF` / `CmdFF` / `CmdSendBlockData` or `Add-Printer` fails with `0x800703eb`.
- Pipeline file must be named `CP9550-pipelineconfig.xml` (stem of `DataFile`).
- Do **not** put IHV filter DLLs in V4 `RequiredFiles` (those resolve from core packages only → event 235). Ship `Cp9550RenderFilter.dll` via INF `CopyFiles` / `DriverFiles` and reference it only from the pipeline XML. `XpsRasterService.dll` is a `FilterServiceProvider`, not a RequiredFiles entry.
- Pipeline XML comments must not contain `--` (illegal in XML). That made `CP9550-pipelineconfig.xml` unparsable and caused PrintService event 372 / `0x80004005` before `InitializeFilter`.
- Lab verify (0.3.10): filter log reaches `InitializeFilter enter`.
- `FixedPageToXpsOMPage` `0x8052000C` (`XPS_E_INVALID_MARKUP`) = OpenXPS page fed to classic `CreatePageFromStream`. Fixed in **0.3.11**: `IXpsOMObjectFactory1::CreatePageFromStream1` + manifest `XpsFormat=XPS,OpenXPS` (MSXPS preferred for GDI/MXDC).
