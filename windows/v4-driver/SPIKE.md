# Phase 0 spike → Phase 1 filter → MK-9 lab

| Spike / phase | Status | Notes |
| --- | --- | --- |
| S1 Bridge | **PASS (hardware)** | Live `--spool-file` → `SendSpool` on CP-9550DW-S (WinUSB). |
| S3 ICC | **PASS (queue)** | `CP9550_active.icc` associated; Color Mgmt path seeded. Photoshop Proof Setup not re-checked in this pass. |
| S2 V4 queue | **PASS (empty pipeline)** | Test-signed ClassVer 4.0 package; queue on `127.0.0.1:9100`. |
| Phase 1 filter | **Code + offline PASS** | `cp9550-spooltool` / parity OK. Pipeline wiring of `Cp9550RenderFilter.dll` still blocked (event 235). |

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

1. Core-companion / RequiredFiles story so `Cp9550RenderFilter.dll` can sit in the XPS pipeline.
2. One XPS/Photo print through the queue → bridge once the filter is wired.
3. Phase 2: 5×7 / 6×9 + full quality/copies UI.
4. Guard WinUSB vs usbprint after queue install (install.ps1 force-bind helps).
