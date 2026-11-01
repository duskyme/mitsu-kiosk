# CP-9550 Native — Windows app

Natívna **WPF** aplikácia (.NET 8) pre Mitsubishi **CP-9550DW-S**.

- jeden `CP9550Native.exe` (self-contained)
- natívne rozlíšenie, planar BGR, **bez Gutenprint**
- WinUSB stačí nainštalovať **raz**

## Build na Windows 11

Potrebuješ [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```powershell
cd windows\Cp9550Native
.\publish.ps1
```

Výstup: `windows\Cp9550Native\dist\CP9550Native.exe`

## Prvé spustenie

1. Pripoj tlačiareň.
2. Admin PowerShell: `..\install.ps1` (alebo Zadig → WinUSB).
3. Spusti `CP9550Native.exe`.
4. Vyber fotku → Tlačiť.

## Dev build Core (aj na Linuxe)

```bash
dotnet test Cp9550Native.Core.Tests
```

WPF projekt (`net8.0-windows`) sa buildí len na Windowse.
