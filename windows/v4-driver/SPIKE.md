# Phase 0 spike → Phase 1 filter → MK-9 lab

| Spike / phase | Status | Notes |
| --- | --- | --- |
| S1 Bridge | **PASS (hardware)** | Live `--spool-file` → `SendSpool` on CP-9550DW-S (WinUSB). |
| S3 ICC | **PASS (queue)** | `CP9550_active.icc` associated; Color Mgmt path seeded. Photoshop Proof Setup not re-checked in this pass. |
| S2 V4 queue | **PASS (filter pipeline)** | Test-signed ClassVer 4.0 package; queue on `127.0.0.1:9100`; `Cp9550RenderFilter` in pipeline. |
| Phase 1 filter | **PASS (package + offline)** | `cp9550-spooltool` / parity OK. Filter DLL via INF CopyFiles (not RequiredFiles — that caused event 235). |

## Offline bring-up

```powershell
.\filter\build.ps1
.\filter\_out\Release\cp9550-spooltool.exe --solid 10 20 30 --out _capture\job.bin
dotnet run --project ..\Cp9550Native\Cp9550SpoolBridge -- --spool-file _capture\job.bin
```

## Queue install

```powershell
# Admin
.\install-printer.ps1 -Sign
```

## What’s next

1. One XPS/Photo print through the queue → filter → bridge.
2. Phase 2: 5×7 / 6×9 + full quality/copies UI.
3. Guard WinUSB vs usbprint after queue install (install.ps1 force-bind helps).
