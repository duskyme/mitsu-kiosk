# Phase 0 spike → Phase 1 filter

| Spike / phase | Status | Notes |
| --- | --- | --- |
| S1 Bridge | **Code pass** | `Cp9550SpoolBridge` → `SendSpool`. Dry-run verified. Live USB TBD. |
| S3 ICC | **Hooks pass** | Package ICC + ProgramData active profile. PS visibility needs real queue. |
| S2 V4 queue | **Stubs → Phase 1** | INF/GPD/manifest + **render filter DLL**. |
| Phase 1 filter | **Code pass** | `Cp9550RenderFilter` + `cp9550_spool` match `Protocol.BuildSpool`. XPS→spool on a signed queue still needs lab install. |

## Offline bring-up (always available)

```powershell
.\filter\build.ps1
.\filter\_out\Release\cp9550-spooltool.exe --solid 10 20 30 --out _capture\job.bin
dotnet run --project ..\Cp9550Native\Cp9550SpoolBridge -- --dry-run --spool-file _capture\job.bin
```

## What’s next

1. Hardware: `--spool-file` through bridge on a real CP-9550DW-S.
2. Test-sign V4 package; print one XPS/Photo job through the queue.
3. Phase 2: 5×7 / 6×9 + full quality/copies UI + ICC soft-proof confirmation.
