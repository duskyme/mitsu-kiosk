# CP-9550DW-S — V4 hybrid (Phase 1)

Hybrid architecture from the project plan:

1. **WinUSB** keeps owning the device (`windows\inf\CP9550DW-S.inf`).
2. **`Cp9550SpoolBridge`** listens on `127.0.0.1:9100` and calls `PrinterDevice.SendSpool`.
3. **V4 queue** is what apps see; port = Standard TCP/IP → bridge.
4. **`Cp9550RenderFilter`** turns XPS → planar RGB spool (`Protocol.BuildSpool` layout).
5. **ICC** `color/CP955_01.ICC` installs to dirid 66003; `associate-icc.ps1` publishes `active.icc`.

## Build the filter

```powershell
cd windows\v4-driver\filter
.\build.ps1
```

## Quick start — bridge + offline spool (works without test-signed queue)

```powershell
cd windows\v4-driver
.\filter\_out\Release\cp9550-spooltool.exe --solid 220 40 40 --out _capture\job.bin

dotnet run --project ..\Cp9550Native\Cp9550SpoolBridge -- --port 9100 --dry-run --spool-file _capture\job.bin
```

TCP client smoke (PowerShell) while the bridge listens:

```powershell
$bytes = [IO.File]::ReadAllBytes('_capture\job.bin')
$client = [Net.Sockets.TcpClient]::new('127.0.0.1', 9100)
$client.GetStream().Write($bytes, 0, $bytes.Length)
$client.Close()
```

## Queue install (needs testsigning / test cert)

```powershell
# Admin
.\install-printer.ps1 -BuildFilter
```

## ICC (S3)

```powershell
# Admin — printer queue must exist
.\associate-icc.ps1 -PrinterName "Mitsubishi CP-9550DW-S (V4)" -Mode reset
.\associate-icc.ps1 -PrinterName "Mitsubishi CP-9550DW-S (V4)" -Mode replace -IccPath .\my.icc
```

Layout: `%ProgramData%\CP9550\color\` (`CP955_01.ICC`, `user\active.icc`, `user\calibration.json`).

## Status

| Piece | State |
| --- | --- |
| S1 Bridge | Implemented (`Cp9550SpoolBridge`) |
| S3 ICC | Package ICC + associate/replace/layer hooks |
| Phase 1 filter | `Cp9550RenderFilter.dll` + `cp9550_spool` + spooltool |
| Phase 1 scope | 4×6, single page, SuperFine default |
