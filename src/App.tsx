import { useMemo, useState } from 'react'
import { DropZone } from './components/DropZone'
import { MediaPicker } from './components/MediaPicker'
import { downloadBytes, formatBytes, rasterizeToNative, type FitMode } from './lib/image'
import {
  MEDIA_PRESETS,
  buildSpool,
  mediaByKey,
  type ColorPipeline,
  type MediaKey,
  type QualityMode,
} from './lib/protocol'

export default function App() {
  const [mediaKey, setMediaKey] = useState<MediaKey>('4x6')
  const [quality, setQuality] = useState<QualityMode>('superfine')
  const [copies, setCopies] = useState(1)
  const [fit, setFit] = useState<FitMode>('cover')
  const [pipeline, setPipeline] = useState<ColorPipeline>('printer-native')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [previewUrl, setPreviewUrl] = useState<string | null>(null)
  const [fileName, setFileName] = useState<string | null>(null)
  const [spoolInfo, setSpoolInfo] = useState<{
    totalBytes: number
    notes: string[]
    cols: number
    rows: number
  } | null>(null)
  const [spoolBytes, setSpoolBytes] = useState<Uint8Array | null>(null)

  const media = useMemo(() => mediaByKey(mediaKey), [mediaKey])

  async function handleFile(file: File) {
    setBusy(true)
    setError(null)
    setSpoolBytes(null)
    setSpoolInfo(null)
    try {
      const raster = await rasterizeToNative(file, media, fit)
      const spool = buildSpool(raster.rgba, 4, {
        media,
        quality,
        copies,
        colorPipeline: pipeline,
      })
      setPreviewUrl(raster.previewUrl)
      setFileName(file.name)
      setSpoolBytes(spool.bytes)
      setSpoolInfo({
        totalBytes: spool.totalBytes,
        notes: spool.notes,
        cols: spool.cols,
        rows: spool.rows,
      })
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Rasterizácia zlyhala')
    } finally {
      setBusy(false)
    }
  }

  function exportSpool() {
    if (!spoolBytes) return
    const base = (fileName ?? 'print').replace(/\.[^.]+$/, '')
    downloadBytes(spoolBytes, `${base}_${media.key}_${spoolInfo?.cols}x${spoolInfo?.rows}.mitsu9550s`)
  }

  return (
    <div className="mx-auto min-h-screen max-w-6xl px-4 pb-16 pt-8 sm:px-6 lg:px-8">
      <header className="rise mb-10">
        <p className="font-mono text-xs uppercase tracking-[0.22em] text-accent-2">
          Windows 11 · kiosk dye-sub
        </p>
        <h1 className="font-display mt-2 text-4xl font-extrabold tracking-tight text-paper sm:text-5xl">
          CP-9550 Native
        </h1>
        <p className="mt-3 max-w-2xl text-base text-mute sm:text-lg">
          User-mode ovládač a spool composer pre Mitsubishi{' '}
          <span className="text-paper">CP-9550DW-S</span>. Natívne rozlíšenie, bez Gutenprint
          farebného soft-proofu — pixely idú do tlačovej hlavy ako planar RGB.
        </p>
      </header>

      <div className="grid gap-8 lg:grid-cols-[1.1fr_0.9fr]">
        <section className="rise rise-delay-1 space-y-6">
          <div className="rounded-2xl border border-line bg-panel/80 p-5 backdrop-blur">
            <h2 className="font-display text-xl font-semibold">1. Médium</h2>
            <p className="mt-1 text-sm text-mute">
              Presné pixelové rámce akceptované firmvérom -S (nie retail CP-9550DW).
            </p>
            <div className="mt-4">
              <MediaPicker value={mediaKey} onChange={setMediaKey} />
            </div>
          </div>

          <div className="rounded-2xl border border-line bg-panel/80 p-5 backdrop-blur">
            <h2 className="font-display text-xl font-semibold">2. Farebný pipeline a kvalita</h2>
            <div className="mt-4 grid gap-4 sm:grid-cols-2">
              <label className="block text-sm">
                <span className="text-mute">Color pipeline</span>
                <select
                  className="mt-1 w-full rounded-lg border border-line bg-ink px-3 py-2"
                  value={pipeline}
                  onChange={(e) => setPipeline(e.target.value as ColorPipeline)}
                >
                  <option value="printer-native">Printer native (odporúčané)</option>
                  <option value="srgb-identity">sRGB identity</option>
                </select>
              </label>
              <label className="block text-sm">
                <span className="text-mute">Kvalita</span>
                <select
                  className="mt-1 w-full rounded-lg border border-line bg-ink px-3 py-2"
                  value={quality}
                  onChange={(e) => setQuality(e.target.value as QualityMode)}
                >
                  <option value="fine">Fine</option>
                  <option value="superfine">SuperFine</option>
                  <option value="finedeep">FineDeep</option>
                </select>
              </label>
              <label className="block text-sm">
                <span className="text-mute">Fit</span>
                <select
                  className="mt-1 w-full rounded-lg border border-line bg-ink px-3 py-2"
                  value={fit}
                  onChange={(e) => setFit(e.target.value as FitMode)}
                >
                  <option value="cover">Cover (orezanie)</option>
                  <option value="contain">Contain (okraje)</option>
                </select>
              </label>
              <label className="block text-sm">
                <span className="text-mute">Kópie</span>
                <input
                  type="number"
                  min={1}
                  max={680}
                  className="mt-1 w-full rounded-lg border border-line bg-ink px-3 py-2"
                  value={copies}
                  onChange={(e) => setCopies(Number(e.target.value) || 1)}
                />
              </label>
            </div>
            <p className="mt-3 text-xs leading-relaxed text-mute">
              Gutenprint/CUPS často preženie ICC a medzikroky rasteru — tu ostáva 8-bit planar RGB v
              exaktnom {media.cols}×{media.rows}. Kalibráciu rob cez ICC profil vo Photoshope /
              Lightroome pred exportom, alebo nechaj printer-native.
            </p>
          </div>

          <div className="rounded-2xl border border-line bg-panel/80 p-5 backdrop-blur">
            <h2 className="font-display text-xl font-semibold">3. Fotografia → spool</h2>
            <div className="mt-4">
              <DropZone onFile={handleFile} disabled={busy} />
            </div>
            {busy && <p className="mt-3 font-mono text-xs text-accent">Rasterizujem…</p>}
            {error && <p className="mt-3 text-sm text-danger">{error}</p>}
          </div>
        </section>

        <aside className="rise rise-delay-2 space-y-6">
          <div className="overflow-hidden rounded-2xl border border-line bg-panel/80 backdrop-blur">
            <div className="border-b border-line px-5 py-4">
              <h2 className="font-display text-xl font-semibold">Náhľad natívneho rámca</h2>
              <p className="mt-1 font-mono text-xs text-mute">
                {media.cols}×{media.rows} px · ~{media.dpiApprox} dpi · {media.ribbon}
              </p>
            </div>
            <div className="relative flex min-h-64 items-center justify-center bg-ink/60 p-4">
              {previewUrl ? (
                <img
                  src={previewUrl}
                  alt="Náhľad spoolu"
                  className="max-h-[420px] w-full object-contain"
                />
              ) : (
                <div className="px-6 text-center text-sm text-mute">
                  Zatiaľ bez snímku. Po nahratí uvidíš presný pixelový rámec, ktorý pôjde do
                  tlačiarne.
                </div>
              )}
            </div>
            {spoolInfo && (
              <div className="space-y-3 border-t border-line px-5 py-4">
                <div className="flex flex-wrap items-center justify-between gap-3">
                  <div>
                    <div className="font-mono text-sm text-accent-2">
                      {formatBytes(spoolInfo.totalBytes)}
                    </div>
                    <div className="text-xs text-mute">
                      3 roviny × {spoolInfo.cols * spoolInfo.rows} B + hlavičky
                    </div>
                  </div>
                  <button
                    type="button"
                    onClick={exportSpool}
                    className="rounded-lg bg-accent px-4 py-2 font-display text-sm font-semibold text-ink transition hover:brightness-110"
                  >
                    Stiahnuť .mitsu9550s
                  </button>
                </div>
                <ul className="space-y-1.5 text-xs text-mute">
                  {spoolInfo.notes.map((n) => (
                    <li key={n} className="flex gap-2">
                      <span className="text-accent">▸</span>
                      <span>{n}</span>
                    </li>
                  ))}
                </ul>
              </div>
            )}
          </div>

          <div className="rounded-2xl border border-line bg-panel/80 p-5 backdrop-blur">
            <h2 className="font-display text-xl font-semibold">Inštalácia na Windows 11</h2>
            <ol className="mt-3 list-decimal space-y-2 pl-5 text-sm text-mute">
              <li>
                Pripoj CP-9550DW-S (USB <span className="font-mono text-paper">06D3:03A5</span>).
              </li>
              <li>
                Spusti <span className="font-mono text-paper">windows/install.ps1</span> ako
                Administrátor — nainštaluje WinUSB INF.
              </li>
              <li>
                V <span className="font-mono text-paper">windows/print-service</span> vytvor venv a
                nainštaluj závislosti, potom{' '}
                <span className="font-mono text-paper">python print_job.py foto.jpg --media 4x6</span>
                .
              </li>
              <li>
                Alebo pošli hotový <span className="font-mono text-paper">.mitsu9550s</span> cez{' '}
                <span className="font-mono text-paper">python send_spool.py job.mitsu9550s</span>.
              </li>
            </ol>
            <p className="mt-4 text-xs leading-relaxed text-mute">
              Oficiálny Mitsubishi CPD9550V ovládač je pre retail <em>CP-9550DW</em> (PID 03A1), nie
              pre kiosk -S. Tento balík hovorí natívnym protokolom -S priamo cez WinUSB.
            </p>
          </div>

          <div className="rounded-2xl border border-line bg-panel/80 p-5 backdrop-blur">
            <h2 className="font-display text-lg font-semibold">Podporované rámce</h2>
            <div className="mt-3 overflow-x-auto">
              <table className="w-full text-left font-mono text-xs">
                <thead className="text-mute">
                  <tr>
                    <th className="py-1 pr-3 font-medium">Médium</th>
                    <th className="py-1 pr-3 font-medium">Pixely</th>
                    <th className="py-1 font-medium">Ribbon</th>
                  </tr>
                </thead>
                <tbody>
                  {MEDIA_PRESETS.map((m) => (
                    <tr key={m.key} className="border-t border-line/70">
                      <td className="py-2 pr-3 text-paper">{m.label}</td>
                      <td className="py-2 pr-3">
                        {m.cols}×{m.rows}
                      </td>
                      <td className="py-2">{m.ribbon}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        </aside>
      </div>

      <footer className="rise rise-delay-3 mt-12 border-t border-line/80 pt-6 text-xs text-mute">
        Protokolový layout podľa reverse-engineeringu Solomona Peachya (selphy_print / Gutenprint).
        Tento projekt je samostatná reimplementácia pre Windows user-mode — nie je to oficiálny
        produkt Mitsubishi Electric.
      </footer>
    </div>
  )
}
