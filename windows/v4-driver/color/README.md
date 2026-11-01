# Color profiles — CP-9550DW-S

| File | Role |
| --- | --- |
| `CP955_01.ICC` | Immutable base (class `prtr`, RGB→Lab, manufacturer `MITS`) |
| `%ProgramData%\CP9550\color\user\active.icc` | Effective profile for soft-proof **and** print |

## Soft-proof invariant

Active soft-proof ICC ≡ effective conversion ICC. Calibration always republishes `active.icc` and re-associates it with the printer queue.

## Calibration modes

1. **Replace** — user ICC → `active.icc` + associate  
2. **Reset** — copy base → `active.icc` + associate  
3. **Layer** (hook now, compose in Phase 3) — abstract/device-link archived; full `prtr` overlays publish immediately; `link` records `layer-pending` until LCMS2/WCS writes a new effective ICC

```powershell
# From repo root (Admin recommended for MSCMS association)
.\windows\v4-driver\associate-icc.ps1 -PrinterName "Mitsubishi CP-9550DW-S (V4)"
.\windows\v4-driver\associate-icc.ps1 -Mode replace -IccPath .\my-cal.icc -PrinterName "..."
.\windows\v4-driver\associate-icc.ps1 -Mode reset -PrinterName "..."
```

Core helpers: `ColorProfileStore` + `WindowsColorAssociation` in `Cp9550Native.Core`.
