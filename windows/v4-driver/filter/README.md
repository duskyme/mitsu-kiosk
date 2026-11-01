# Cp9550RenderFilter (Phase 1)

IHV XPS render filter: `IXpsDocumentProvider` → CP-9550 planar RGB spool → `IPrintWriteStream` (TCP bridge).

## Build

```powershell
cd windows\v4-driver\filter
.\build.ps1            # Release; runs cp9550_spool_test
.\build.ps1 -Configuration Debug
```

Outputs under `_out\Release\`:

| Artifact | Role |
| --- | --- |
| `Cp9550RenderFilter.dll` | Pipeline filter (CLSID `{C9550F17-06D3-4A55-9D77-C9550D0500F1}`) |
| `cp9550_spool_test.exe` | Layout / planar-RGB self-check |
| `cp9550-spooltool.exe` | Offline spool builder (documented bring-up path) |

## Filter job (Phase 1)

1. Read XPS parts; first `IFixedPage` only (extra pages ignored).
2. Light PrintTicket scrape → quality / copies (default **SuperFine**, 1 copy).
3. Convert page → XPS OM + **XpsRasterService** at exact **2152×1416**.
4. Packed RGB → `cp9550_build_spool` (R then G then B — same as `Protocol.BuildSpool`).
5. Write spool bytes to the port → `127.0.0.1:9100` → `Cp9550SpoolBridge` → `PrinterDevice.SendSpool`.

Media is **4×6 only** in Phase 1 (matches GPD).

## Offline path (no queue / no XPS)

Use when the V4 package is not yet test-signed, or to validate the bridge alone:

```powershell
.\filter\_out\Release\cp9550-spooltool.exe --solid 220 40 40 --out ..\_capture\job.bin

dotnet run --project ..\..\Cp9550Native\Cp9550SpoolBridge -- `
  --spool-file ..\v4-driver\_capture\job.bin --dry-run

# Or TCP:
# Bridge listening on 9100, then:
$bytes = [IO.File]::ReadAllBytes('..\_capture\job.bin')
$c = [Net.Sockets.TcpClient]::new('127.0.0.1', 9100)
$c.GetStream().Write($bytes, 0, $bytes.Length)
$c.Close()
```

`install-printer.ps1` stages the built DLL next to the INF when present.
