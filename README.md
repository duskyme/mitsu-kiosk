# CP-9550 Native

User-mode **Windows 11** print stack for the Mitsubishi **CP-9550DW-S** kiosk dye-sub printer (`USB 06D3:03A5`).

Retail Mitsubishi drivers (`CPD9550V`) target **CP-9550DW** (`03A1`), not the kiosk **-S**. Gutenprint/CUPS works on Linux but applies its own color management and intermediate raster steps — that is what usually shifts calibration and softens detail. This project talks the native -S protocol and sends **exact pixel frames** as 8-bit planar BGR with **no Gutenprint LUT / soft-proof**.

## What you get

| Piece | Role |
| --- | --- |
| Web composer (`npm run dev`) | Build/preview native spools in the browser |
| `windows/inf/CP9550DW-S.inf` | Bind the device to **WinUSB** |
| `windows/print-service/` | Python USB print tool (Pillow + pyusb) |

This is **not** a Microsoft-signed kernel printer class driver. WinUSB + user-mode is the correct path for an unsigned community stack on Windows 11, and it preserves full control over color and resolution.

## Native media frames (CP-9550DW-S)

| Media | Ribbon | Pixels (cols × rows) |
| --- | --- | --- |
| 4×6" | CK9015 | 2152 × 1416 |
| 5×7" | CK9318 | 1812 × 2452 |
| 6×9" | CK9523 | 2152 × 2792 |

Quality flags: Fine / SuperFine / FineDeep. Job footer for -S: `1B 50 47 00`.

## Windows 11 install

1. Plug in the CP-9550DW-S.
2. Open **Admin PowerShell** in this repo:

```powershell
.\windows\install.ps1
```

If Windows blocks the unsigned INF, either enable test signing temporarily or use [Zadig](https://zadig.akeo.ie/) to bind **WinUSB** to `USB\VID_06D3&PID_03A5`.

3. Set up the print service:

```powershell
cd windows\print-service
python -m venv .venv
.\.venv\Scripts\Activate.ps1
pip install -r requirements.txt
python status.py
python print_job.py C:\photos\shot.jpg --media 4x6 --quality superfine --print
```

Dry-run (build spool only):

```powershell
python print_job.py shot.jpg --media 4x6 --dry-run
python send_spool.py shot.mitsu9550s
```

## Composer (any OS)

```bash
npm install
npm run dev
```

Open [http://127.0.0.1:43155](http://127.0.0.1:43155), drop a photo, export `.mitsu9550s`, then `send_spool.py` on the Windows machine attached to the printer.

## Color / resolution policy

- **Default: printer-native** — no ICC transform in the driver path.
- Raster size is forced to the firmware frame (no 300/600 DPI CUPS guesses).
- Resize uses high-quality Lanczos (Python) / canvas high-quality (web).
- Do color grading in Lightroom/Photoshop and export sRGB (or your measured profile) **before** printing if you need managed color.

## Protocol credit

USB command set and spool layout were reverse-engineered by **Solomon Peachy** (`selphy_print` / Gutenprint dye-sub backend). This repository reimplements the -S path for Windows user-mode; it is not affiliated with Mitsubishi Electric.

## License

MIT for original code in this repository. Protocol documentation derived from publicly documented reverse engineering; respect GPL obligations if you copy GPL sources from Gutenprint/`selphy_print` directly.
