# CP-9550 Native (`mitsu-kiosk`)

Unofficial **Windows 11** print stack for the Mitsubishi **CP-9550DW-S** kiosk printer (`USB VID 06D3 / PID 03A5`).

> **TEST / lab software.** Not WHQL. Not a Mitsubishi Electric product. Drivers are **test-signed** (`WDKTestCert`). Use on machines you own. See [Releases](https://github.com/duskyme/mitsu-kiosk/releases) for the packaged installer.

Official Mitsubishi `CPD9550V` targets retail **CP-9550DW** (`PID 03A1`), not the **-S** kiosk unit. This stack sends **exact planar RGB pixels** (native resolution) over WinUSB — no Gutenprint LUT.

**License:** [MIT](LICENSE)

## What you get

| Piece | Role |
| --- | --- |
| **WinUSB INF** (`windows/inf`) | Binds `06D3:03A5` to WinUSB (`install.ps1 -Sign`) |
| **V4 class driver 0.4.1+** | Print dialog: GPD sizes/quality + XPS IHV filter → planar spool |
| **Cp9550SpoolBridge** | `127.0.0.1:9100` RAW → `PrinterDevice.SendSpool` |
| **Cp9550BridgeTray** | Notification-area status; USB auto-start/stop of the bridge |
| **ICC** `CP955_01.ICC` | Base profile for Windows soft-proof / color management |
| **CP9550Native** (optional WPF) | Direct WinUSB print app (no V4 queue required) |
| **Web composer** | Browser spool preview (`npm run dev`) |

## Architecture

```
Photoshop / Lightroom / any app
        │  Print dialog
        ▼
V4 queue  "Mitsubishi CP-9550DW-S (V4)"
  GPD (4×6 / 5×7 / 6×9 + Fine / SuperFine / FineDeep)
  XPS render filter  →  planar RGB spool bytes
        │
        ▼  Standard TCP/IP Port  →  127.0.0.1:9100
Cp9550SpoolBridge  (tray auto-starts on USB)
        │
        ▼  WinUSB
CP-9550DW-S hardware
```

V4 forbids custom language/port monitors that do USB reads for the CP-9550 handshake, so the hybrid TCP bridge is required.

## Install (release package)

1. Download the latest **Setup** from [Releases](https://github.com/duskyme/mitsu-kiosk/releases).
2. Run the installer (**no UAC** — `PrivilegesRequired=lowest`). Files land under `%ProgramFiles%\CP9550 Native` (or the folder you chose).
3. Connect the printer USB.
4. Open an **already-elevated** Admin PowerShell (scripts never pop interactive UAC):

   ```powershell
   cd "C:\Program Files\CP9550 Native"
   .\Install-Drivers.ps1
   ```

   This runs WinUSB `-Sign` and V4 `-Sign -ForceReplace` (needs Windows SDK Inf2Cat/SignTool + `WDKTestCert`).
5. Non-Admin:

   ```powershell
   .\Start-Tray.ps1
   ```

6. Print to **Mitsubishi CP-9550DW-S (V4)**.

### Signing / testsigning

- **Preferred:** put `WDKTestCert` in `CurrentUser\My` **and** import the same cert into `LocalMachine\Root` + `TrustedPublisher`. Then Driver Signature Enforcement accepts the package without `bcdedit testsigning`.
- **Fallback:** `bcdedit /set testsigning on` and reboot if the cert is not machine-trusted.
- Secure Boot machines still need a trusted test cert or testsigning.

## Install (from source)

```powershell
# WinUSB (Admin elevated shell already open)
cd windows
.\install.ps1 -Sign

# Build XPS filter
cd v4-driver\filter
.\build.ps1

# V4 queue (Admin)
cd ..
.\install-printer.ps1 -Sign -ForceReplace

# Tray (no Admin)
cd ..\Cp9550Native\Cp9550BridgeTray
.\start-tray.ps1
```

Packaged build (Inno Setup 6 required):

```powershell
cd windows\packaging
.\build-release.ps1
# → dist\releases\CP9550-Native-0.4.1-Setup.exe
# → dist\releases\CP9550-Native-0.4.1-portable.zip
```

## Modify

| Change | Where |
| --- | --- |
| Media sizes / quality | `windows/v4-driver/gpd/CP9550.gpd` + bump `DriverVer` in INF / `install-printer.ps1` flat INF |
| Spool / USB protocol | `windows/Cp9550Native/Cp9550Native.Core` (`Protocol.cs`, `PrinterDevice.cs`) |
| XPS → planar filter | `windows/v4-driver/filter/dll/` then `filter\build.ps1` |
| Bridge / tray behavior | `Cp9550SpoolBridge`, `Cp9550BridgeTray` |
| WinUSB GUID / bind | `windows/inf/CP9550DW-S.inf`, `windows/install.ps1` |
| ICC association | `windows/v4-driver/associate-icc.ps1`, `color/CP955_01.ICC` |

After driver file changes: Admin `install-printer.ps1 -Sign -ForceReplace` (and rebuild filter if native code changed).

## Test

```powershell
# Core protocol unit tests
cd windows\Cp9550Native
dotnet test Cp9550Native.Core.Tests

# Filter spool unit test (built by filter\build.ps1)
windows\v4-driver\filter\_out\Release\cp9550_spool_test.exe

# Live path: tray without -DryRun, print 4×6 Fine from Paint / Photoshop
# Confirm printer leaves idle and completes; Event Viewer → PrintService if filter fails
```

**Do not** leave SpoolBridge running with `--dry-run` for real prints — Windows reports success while the printer stays idle.

## Troubleshooting

| Symptom | Likely fix |
| --- | --- |
| `Guid` FormatException / WinUSB bind | Re-run `install.ps1 -Sign`. Purge old non-hex GUID packages (`c9550dws…`). Expected `{D3C9550A-06D3-4A55-9D77-C9550D050001}`. |
| PrintService **372** before filter init | Pipeline XML comment contained `--` — use shipped `CP9550-pipelineconfig.xml`. |
| `XPS_E_INVALID_MARKUP` `0x8052000C` | Need `CreatePageFromStream1` + `XpsFormat=XPS,OpenXPS` (0.3.11+). |
| Job OK in Windows, printer idle | Kill leftover `--dry-run` bridge; `Start-Tray.ps1` without `-DryRun`. |
| Mid-job reject / kill while tray polls USB | Use build with reject-status gates (PR #6) — no `OpenUsbDevice` while `SendSpool` is in flight. |
| `pnputil` exit **259** | Package already in DriverStore; `-ForceReplace` to refresh. |

## Native media (CP-9550DW-S)

| Media | Ribbon | Pixels |
| --- | --- | --- |
| 4×6" | CK9015 | 2152 × 1416 |
| 5×7" | CK9318 | 1812 × 2452 |
| 6×9" | CK9523 | 2152 × 2792 |

## Web composer (spool preview)

```bash
npm install
npm run dev
```

Open [http://127.0.0.1:43155](http://127.0.0.1:43155).

## Protocol credit

USB/spool layout informed by Solomon Peachy’s `selphy_print` / Gutenprint work. This is an independent Windows reimplementation — not a Mitsubishi product.

## Slovak notes

Krátky postup a história projektu: [`POSTUP.txt`](POSTUP.txt) a `windows/README.md`.
