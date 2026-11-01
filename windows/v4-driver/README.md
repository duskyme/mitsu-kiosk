# CP-9550DW-S — V4 hybrid spike (S1 + S2 stubs + S3)

Hybrid architecture from the project plan:

1. **WinUSB** keeps owning the device (`windows\inf\CP9550DW-S.inf`).
2. **`Cp9550SpoolBridge`** listens on `127.0.0.1:9100` and calls `PrinterDevice.SendSpool`.
3. **V4 queue** (stubs here) is what apps see; port = Standard TCP/IP → bridge.
4. **ICC** `color/CP955_01.ICC` installs to dirid 66003; `associate-icc.ps1` publishes `active.icc`.

## Quick start (S1 bridge)

```powershell
cd windows\Cp9550Native
dotnet run --project Cp9550SpoolBridge -- --port 9100 --dry-run --save-dir ..\v4-driver\_capture

# With printer + known-good spool:
dotnet run --project Cp9550SpoolBridge -- --spool-file C:\path\job.bin
```

TCP client smoke (PowerShell):

```powershell
$bytes = [IO.File]::ReadAllBytes('job.bin')
$client = [Net.Sockets.TcpClient]::new('127.0.0.1', 9100)
$client.GetStream().Write($bytes, 0, $bytes.Length)
$client.Close()
```

## ICC (S3)

```powershell
# Admin — printer queue must exist (name must match Color Management device)
.\associate-icc.ps1 -PrinterName "Mitsubishi CP-9550DW-S (V4)" -Mode reset
.\associate-icc.ps1 -PrinterName "Mitsubishi CP-9550DW-S (V4)" -Mode replace -IccPath .\my.icc
```

Layout: `%ProgramData%\CP9550\color\` (`CP955_01.ICC`, `user\active.icc`, `user\calibration.json`).

## Queue stubs (S2)

```powershell
# Admin
.\install-printer.ps1
```

Filter DLL is **not** built — see `filter/README.md`. Full XPS→spool is Phase 1.

## Status

| Spike | State |
| --- | --- |
| S1 Bridge | Implemented (`Cp9550SpoolBridge`) |
| S3 ICC | Package ICC + associate/replace/layer hooks |
| S2 Minimal V4 | INF/GPD/manifest/pipeline stubs; no filter binary |
