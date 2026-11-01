import type { MediaPreset } from './protocol'

export type FitMode = 'cover' | 'contain'

export interface RasterResult {
  rgba: Uint8ClampedArray
  cols: number
  rows: number
  previewUrl: string
}

/**
 * Rasterize a source image to the printer's exact pixel grid.
 * Uses high-quality canvas resampling — no Gutenprint soft-proof /
 * intermediate DPI that would shift color or soften detail.
 */
export async function rasterizeToNative(
  file: Blob,
  media: MediaPreset,
  fit: FitMode = 'cover',
  background = '#ffffff',
): Promise<RasterResult> {
  const bitmap = await createImageBitmap(file)
  const canvas = document.createElement('canvas')
  canvas.width = media.cols
  canvas.height = media.rows
  const ctx = canvas.getContext('2d', { colorSpace: 'srgb' })
  if (!ctx) throw new Error('2D canvas unavailable')

  ctx.imageSmoothingEnabled = true
  ctx.imageSmoothingQuality = 'high'
  ctx.fillStyle = background
  ctx.fillRect(0, 0, media.cols, media.rows)

  const scaleX = media.cols / bitmap.width
  const scaleY = media.rows / bitmap.height
  const scale = fit === 'cover' ? Math.max(scaleX, scaleY) : Math.min(scaleX, scaleY)
  const drawW = bitmap.width * scale
  const drawH = bitmap.height * scale
  const dx = (media.cols - drawW) / 2
  const dy = (media.rows - drawH) / 2
  ctx.drawImage(bitmap, dx, dy, drawW, drawH)
  bitmap.close()

  const { data } = ctx.getImageData(0, 0, media.cols, media.rows)
  const previewUrl = canvas.toDataURL('image/jpeg', 0.92)
  return { rgba: data, cols: media.cols, rows: media.rows, previewUrl }
}

export function downloadBytes(bytes: Uint8Array, filename: string) {
  const copy = new Uint8Array(bytes)
  const blob = new Blob([copy], { type: 'application/octet-stream' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = filename
  a.click()
  URL.revokeObjectURL(url)
}

export function formatBytes(n: number): string {
  if (n < 1024) return `${n} B`
  if (n < 1024 * 1024) return `${(n / 1024).toFixed(1)} KB`
  return `${(n / (1024 * 1024)).toFixed(2)} MB`
}
