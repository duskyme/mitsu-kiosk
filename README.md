# CP-9550 Native

Natívny **Windows 11** print stack pre Mitsubishi **CP-9550DW-S** (`USB 06D3:03A5`).

Oficiálny Mitsubishi `CPD9550V` je pre retail **CP-9550DW** (`03A1`), nie kiosk **-S**. Gutenprint/CUPS farby a ostrosť kazí soft-proofom — toto posiela **exaktné pixely** ako 8-bit planar BGR **bez Gutenprint LUT**.

## Odporúčané: Windows app

```powershell
cd windows\Cp9550Native
.\publish.ps1
```

Potom raz `..\install.ps1` (WinUSB) a spusti `dist\CP9550Native.exe`.

Detaily: [`windows/Cp9550Native/README.md`](windows/Cp9550Native/README.md)

## Čo je v repo

| Piece | Role |
| --- | --- |
| **`windows/Cp9550Native/`** | **WPF app (.NET 8) → `CP9550Native.exe`** |
| `windows/inf/CP9550DW-S.inf` | WinUSB binding (raz) |
| `windows/print-service/` | Python CLI (voliteľné) |
| Web composer (`npm run dev`) | Browser spool preview |

Toto **nie je** Microsoft-signed kernel printer class driver. WinUSB + user-mode app je správna cesta pre unsigned community stack a drží plnú kontrolu farieb/rozlíšenia.

## Natívne rámce (CP-9550DW-S)

| Media | Ribbon | Pixels |
| --- | --- | --- |
| 4×6" | CK9015 | 2152 × 1416 |
| 5×7" | CK9318 | 1812 × 2452 |
| 6×9" | CK9523 | 2152 × 2792 |

## Composer (náhľad spoolu)

```bash
npm install
npm run dev
```

[http://127.0.0.1:43155](http://127.0.0.1:43155)

## Kredit protokolu

USB/spool layout: Solomon Peachy (`selphy_print` / Gutenprint). Toto je samostatná Windows reimplementácia — nie produkt Mitsubishi Electric.

## License

MIT (pôvodný kód v tomto repo).
