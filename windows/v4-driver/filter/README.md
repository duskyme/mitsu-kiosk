# Cp9550RenderFilter (not built yet)

Spike S2 only ships INF/GPD/manifest/pipeline **stubs**. The IHV XPS render filter that turns `IXpsDocumentProvider` into CP-9550 planar RGB spool bytes is Phase 1.

## Intended job

1. Read PrintTicket → media / quality / copies / fit.
2. Rasterize at exact `cols×rows` (4×6 = 2152×1416).
3. Honor app-managed vs printer-managed color (no double convert).
4. Call the same spool layout as `Protocol.BuildSpool` (planar RGB — R then G then B).
5. Write bytes to `IPrintWriteStream` → Standard TCP/IP Port → `Cp9550SpoolBridge` → `PrinterDevice.SendSpool`.

## Until the filter exists

- Bridge S1 can already print a **known-good** `.bin` spool (`--spool-file` or TCP).
- Queue install may be attempted with test-signing, but notepad/XPS jobs will not produce native dye-sub data.
- Prefer FILE: or bridge `--save-dir` to capture whatever the pipeline emits during bring-up.
