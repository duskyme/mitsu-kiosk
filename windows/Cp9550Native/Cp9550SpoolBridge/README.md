# Cp9550SpoolBridge

Localhost **RAW TCP** bridge for the V4 hybrid path. Reuses `PrinterDevice.SendSpool` — no protocol reimplementation.

```
App / V4 filter → TCP 127.0.0.1:9100 → this process → WinUSB → CP-9550DW-S
```

## Preferred: tray (USB auto-start)

```powershell
cd ..\Cp9550BridgeTray
.\start-tray.ps1
```

The tray starts this process when USB `06D3:03A5` appears and stops it on unplug. Status JSON: `%ProgramData%\CP9550\bridge-status.json`.

## Manual run

```powershell
dotnet run --project Cp9550SpoolBridge -- --port 9100
dotnet run --project Cp9550SpoolBridge -- --dry-run --save-dir ..\v4-driver\_capture
dotnet run --project Cp9550SpoolBridge -- --spool-file job.bin
```

`--allow-non-native` accepts non-header-1 payloads for early S2 pipeline capture only.  
Default `--max-receive-bytes` is 24 MB (covers 6×9 SuperFine).

## Mutex

Only one USB owner: when the bridge is used, do not also open the device from `CP9550Native.exe`.
