# Cp9550BridgeTray

Notification-area (near the clock) companion for `Cp9550SpoolBridge`.

## What it does

- Shows bridge status (USB present / listening / dry-run / stopped).
- **Auto-starts** the bridge when CP-9550DW-S USB (`06D3:03A5`) appears; **stops** it on unplug.
- Manual **Start / Restart / Stop** from the tray menu.
- **Optional** Windows logon start via `HKCU\...\Run` (installer task, `-RegisterStartup`, or tray menu **Start with Windows (logon)**). Off by default.

## Retail note

No Mitsubishi **CPD9550V** retail package for PID `03A1` was found on this lab machine. Retail stacks typically bind **usbprint** and ship a status monitor beside the spooler. Our kiosk path keeps **WinUSB** exclusive (required for `SendSpool` handshake reads), so the tray replaces that monitor by owning the localhost bridge process.

## Run (no UAC)

```powershell
cd windows\Cp9550Native\Cp9550BridgeTray
.\start-tray.ps1
# Optional:
.\start-tray.ps1 -NoAuto
.\start-tray.ps1 -DryRun   # lab only
.\start-tray.ps1 -RegisterStartup     # HKCU Run — start tray at logon
.\start-tray.ps1 -UnregisterStartup   # remove logon Run value
```

Status JSON (written by the bridge): `%ProgramData%\CP9550\bridge-status.json`.

Logs / capture (no UAC):

- `%ProgramData%\CP9550\log\bridge.log` — accept / SendSpool / errors  
- `%ProgramData%\CP9550\log\last-spool.bin` — last TCP spool  
- `%ProgramData%\CP9550\spool-capture\` — default tray `--save-dir` (disable with `--no-save-dir`)

**Important:** USB presence uses WinUSB **registry enumeration** (never `OpenUsbDevice`). Opening the device from the tray while `SendSpool` held the handle used to look like “USB gone” and **Kill()** the bridge mid-job — TCP already accepted bytes, printer never got a clean footer / sts3 accept.
