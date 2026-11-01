# Windows 11 — CP-9550DW-S

## Prečo nie oficiálny Mitsubishi ovládač?

| Model | USB PID | Oficiálny Win ovládač |
| --- | --- | --- |
| CP-9550DW (retail) | `03A1` | CPD9550V |
| **CP-9550DW-S (kiosk)** | **`03A5`** | **žiadny** |

Kiosk -S hovorí iným dialektom (handshake `1B 53 C5 9D`, clear-memory `1B 5A 43 00`, footer `1B 50 47 00`) a používa ribbon CK90xx/CK93xx/CK95xx.

## Inštalácia

```powershell
# Admin
.\install.ps1

cd print-service
python -m venv .venv
.\.venv\Scripts\Activate.ps1
pip install -r requirements.txt
python status.py
```

Ak `pnputil` odmietne unsigned INF: Zadig → vyber zariadenie → WinUSB → Install Driver.

## Tlač

```powershell
python print_job.py .\photo.jpg --media 4x6 --quality superfine --print
```

Len spool (bez USB):

```powershell
python print_job.py .\photo.jpg --media 5x7 --dry-run
```

## Farebná kalibrácia

`print_job.py` **neaplikuje** Gutenprint LUT ani ICC. Posiela 8-bit planar BGR v natívnom rozlíšení. Ak potrebuješ managed color, skonvertuj / proofni snímok *pred* volaním tohto toolu.
