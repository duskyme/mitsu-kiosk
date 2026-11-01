# Phase 0 spike status (S1 / S2 / S3)

Updated with the first hybrid-architecture spike on branch `cursor/v4-driver-spike-s1-02fd`.

| Spike | Status | Notes |
| --- | --- | --- |
| S1 Bridge | **Code pass** | `Cp9550SpoolBridge` listens on `127.0.0.1:9100`, validates header-1, calls `SendSpool`. Dry-run + TCP accept verified with a 4×6 spool (~9.1 MB). Live USB print pending hardware. |
| S3 ICC | **Hooks pass** | `color/CP955_01.ICC` in package; ProgramData `active.icc` replace/reset/layer; `associate-icc.ps1`. Photoshop/Color Mgmt visibility needs a real queue. |
| S2 V4 queue | **Stubs** | INF/manifest/GPD/pipeline + `install-printer.ps1`. **No render filter DLL** — XPS jobs will not produce native dye-sub data yet. |

## What’s next

1. Print a known-good spool through the bridge on a real CP-9550DW-S.
2. Test-sign the V4 package, create the TCP port queue, associate ICC, confirm soft-proof listing.
3. Implement `Cp9550RenderFilter` (Phase 1) writing `Protocol.BuildSpool` bytes to the port.
