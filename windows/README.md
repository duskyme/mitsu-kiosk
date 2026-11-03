# Windows 11 — CP-9550DW-S

## Odporúčané: natívna appka

```powershell
cd windows\Cp9550Native
.\publish.ps1
# výstup: dist\CP9550Native.exe
```

1. Pripoj tlačiareň  
2. Admin (už elevated shell — skript nevie UAC prompt): `.\install.ps1 -Sign`  
   (Inf2Cat + WDKTestCert catalog; bez `-Sign` ostáva unsigned lab path)  
3. Ak Device Manager ešte ukazuje starý stav: odpoj/zapoj USB a znova Admin `.\install.ps1 -Sign`  
4. Spusti `CP9550Native.exe` **alebo** tray bridge nižšie

## SpoolBridge tray (Print dialog / V4)

```powershell
cd windows\Cp9550Native\Cp9550BridgeTray
.\start-tray.ps1
```

Ikona pri hodinách: status + Start/Restart/Stop; auto-start pri USB.

## Prečo nie oficiálny Mitsubishi ovládač?

| Model | USB PID | Oficiálny Win ovládač |
| --- | --- | --- |
| CP-9550DW (retail) | `03A1` | CPD9550V |
| **CP-9550DW-S (kiosk)** | **`03A5`** | **žiadny** |

## V4 hybrid (Print dialog — Phase 2 media/quality)

```powershell
# Build XPS → spool filter + offline spooltool
cd v4-driver\filter
.\build.ps1

# Admin — test-signed queue (4×6 / 5×7 / 6×9 + Fine/SuperFine/FineDeep)
cd ..
.\install-printer.ps1 -Sign -ForceReplace

# Bridge via tray (preferred) or:
.\start-bridge.ps1
```

WinUSB device package: `..\install.ps1 -Sign` (same WDKTestCert story as the V4 queue).  
Trusted cert in LocalMachine Root + TrustedPublisher ⇒ `bcdedit testsigning` usually **not** required; otherwise enable testsigning + reboot.

## Alternatíva: Python CLI

Stále v `print-service/` — rovnaký protokol, bez GUI.

```powershell
cd print-service
python -m venv .venv
.\.venv\Scripts\Activate.ps1
pip install -r requirements.txt
python status.py
```
