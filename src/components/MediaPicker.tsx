import { MEDIA_PRESETS, type MediaKey } from '../lib/protocol'

interface Props {
  value: MediaKey
  onChange: (key: MediaKey) => void
}

export function MediaPicker({ value, onChange }: Props) {
  return (
    <div className="grid gap-2 sm:grid-cols-3">
      {MEDIA_PRESETS.map((m) => {
        const active = m.key === value
        return (
          <button
            key={m.key}
            type="button"
            onClick={() => onChange(m.key)}
            className={`rounded-xl border px-3 py-3 text-left transition ${
              active
                ? 'border-accent bg-accent/10 shadow-[inset_0_0_0_1px_rgba(232,165,75,0.35)]'
                : 'border-line bg-panel-2/60 hover:border-mute'
            }`}
          >
            <div className="font-display text-lg font-semibold tracking-tight">{m.label}</div>
            <div className="mt-1 font-mono text-[11px] text-mute">
              {m.cols}×{m.rows} · {m.ribbon}
            </div>
          </button>
        )
      })}
    </div>
  )
}
