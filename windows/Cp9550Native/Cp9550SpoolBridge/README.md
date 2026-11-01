# Cp9550SpoolBridge

Localhost **RAW TCP** bridge for the V4 hybrid path. Reuses `PrinterDevice.SendSpool` — no protocol reimplementation.

```
App / V4 filter → TCP 127.0.0.1:9100 → this process → WinUSB → CP-9550DW-S
```

## Run

```powershell
dotnet run --project Cp9550SpoolBridge -- --port 9100
dotnet run --project Cp9550SpoolBridge -- --dry-run --save-dir ..\v4-driver\_capture
dotnet run --project Cp9550SpoolBridge -- --spool-file job.bin
```

`--allow-non-native` accepts non-header-1 payloads for early S2 pipeline capture only.

## Mutex

Only one USB owner: when the bridge is used, do not also open the device from `CP9550Native.exe`.
