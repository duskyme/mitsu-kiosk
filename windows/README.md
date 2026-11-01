# Windows 11 — CP-9550DW-S

## Odporúčané: natívna appka

```powershell
cd windows\Cp9550Native
.\publish.ps1
# výstup: dist\CP9550Native.exe
```

1. Pripoj tlačiareň  
2. Admin: `.\install.ps1` (odstráni starý nehex GUID `c9550dws`, nainštaluje WinUSB; alebo Zadig → WinUSB)  
3. Ak Device Manager ešte ukazuje starý stav: odpoj/zapoj USB a znova Admin `.\install.ps1`  
4. Spusti `CP9550Native.exe`

## Prečo nie oficiálny Mitsubishi ovládač?

| Model | USB PID | Oficiálny Win ovládač |
| --- | --- | --- |
| CP-9550DW (retail) | `03A1` | CPD9550V |
| **CP-9550DW-S (kiosk)** | **`03A5`** | **žiadny** |

## V4 hybrid (Print dialog path — Phase 1)

```powershell
# Build XPS → spool filter + offline spooltool
cd v4-driver\filter
.\build.ps1

# Bridge: TCP 127.0.0.1:9100 → SendSpool
cd ..\..\Cp9550Native
dotnet run --project Cp9550SpoolBridge -- --port 9100 --dry-run

# Queue + ICC: see v4-driver\README.md (install-printer.ps1)
# Offline spool without queue: v4-driver\filter\README.md
```


## Alternatíva: Python CLI

Stále v `print-service/` — rovnaký protokol, bez GUI.

```powershell
cd print-service
python -m venv .venv
.\.venv\Scripts\Activate.ps1
pip install -r requirements.txt
python status.py
```
