# Cp9550BridgeTray

Notification-area (near the clock) companion for `Cp9550SpoolBridge`.

## What it does

- Shows bridge status (USB present / listening / dry-run / stopped).
- **Auto-starts** the bridge when CP-9550DW-S USB (`06D3:03A5`) appears; **stops** it on unplug.
- Manual **Start / Restart / Stop** from the tray menu.

## Retail note

No Mitsubishi **CPD9550V** retail package for PID `03A1` was found on this lab machine. Retail stacks typically bind **usbprint** and ship a status monitor beside the spooler. Our kiosk path keeps **WinUSB** exclusive (required for `SendSpool` handshake reads), so the tray replaces that monitor by owning the localhost bridge process.

## Run (no UAC)

```powershell
cd windows\Cp9550Native\Cp9550BridgeTray
.\start-tray.ps1
# Optional:
.\start-tray.ps1 -NoAuto
.\start-tray.ps1 -DryRun   # lab only
```

Status JSON (written by the bridge): `%ProgramData%\CP9550\bridge-status.json`.
