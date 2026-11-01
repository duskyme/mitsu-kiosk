import { useCallback, useRef, useState, type DragEvent } from 'react'

interface Props {
  onFile: (file: File) => void
  disabled?: boolean
}

export function DropZone({ onFile, disabled }: Props) {
  const inputRef = useRef<HTMLInputElement>(null)
  const [over, setOver] = useState(false)

  const take = useCallback(
    (file: File | undefined) => {
      if (!file || disabled) return
      if (!file.type.startsWith('image/')) return
      onFile(file)
    },
    [disabled, onFile],
  )

  const onDrop = (e: DragEvent) => {
    e.preventDefault()
    setOver(false)
    take(e.dataTransfer.files?.[0])
  }

  return (
    <button
      type="button"
      disabled={disabled}
      onClick={() => inputRef.current?.click()}
      onDragOver={(e) => {
        e.preventDefault()
        setOver(true)
      }}
      onDragLeave={() => setOver(false)}
      onDrop={onDrop}
      className={`group relative flex min-h-52 w-full flex-col items-center justify-center overflow-hidden rounded-2xl border border-dashed px-6 py-10 text-center transition ${
        over
          ? 'dropzone-active border-accent bg-accent/10'
          : 'border-line bg-panel/70 hover:border-accent/60 hover:bg-panel-2/80'
      } disabled:cursor-not-allowed disabled:opacity-50`}
    >
      <div
        aria-hidden
        className="pointer-events-none absolute inset-0 opacity-40"
        style={{
          backgroundImage:
            'linear-gradient(rgba(255,255,255,0.03) 1px, transparent 1px), linear-gradient(90deg, rgba(255,255,255,0.03) 1px, transparent 1px)',
          backgroundSize: '28px 28px',
        }}
      />
      <span className="font-display relative text-2xl font-semibold tracking-tight">
        Nahraj fotografiu
      </span>
      <span className="relative mt-2 max-w-sm text-sm text-mute">
        JPEG, PNG alebo TIFF. Snímok sa rasterizuje na natívne pixely tlačiarne — bez
        Gutenprint soft-proofu.
      </span>
      <span className="relative mt-5 rounded-full border border-line bg-ink/40 px-4 py-1.5 font-mono text-xs text-accent-2">
        drag & drop · alebo klikni
      </span>
      <input
        ref={inputRef}
        type="file"
        accept="image/*"
        className="hidden"
        onChange={(e) => take(e.target.files?.[0])}
      />
    </button>
  )
}
