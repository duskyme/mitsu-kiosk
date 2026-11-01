# Windows 11 — CP-9550DW-S

## Odporúčané: natívna appka

```powershell
cd windows\Cp9550Native
.\publish.ps1
# výstup: dist\CP9550Native.exe
```

1. Pripoj tlačiareň  
2. Admin: `.\install.ps1` (alebo Zadig → WinUSB)  
3. Spusti `CP9550Native.exe`

## Prečo nie oficiálny Mitsubishi ovládač?

| Model | USB PID | Oficiálny Win ovládač |
| --- | --- | --- |
| CP-9550DW (retail) | `03A1` | CPD9550V |
| **CP-9550DW-S (kiosk)** | **`03A5`** | **žiadny** |

## Alternatíva: Python CLI

Stále v `print-service/` — rovnaký protokol, bez GUI.

```powershell
cd print-service
python -m venv .venv
.\.venv\Scripts\Activate.ps1
pip install -r requirements.txt
python status.py
```
