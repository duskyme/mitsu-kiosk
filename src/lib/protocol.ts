/**
 * Mitsubishi CP-9550DW-S native spool protocol.
 *
 * Protocol facts reverse-engineered by Solomon Peachy (selphy_print / Gutenprint).
 * This module reimplements the spool layout independently so Windows jobs can
 * keep exact native pixels and skip Gutenprint color management.
 *
 * USB: VID 0x06D3, PID 0x03A5
 */

export const USB_VID = 0x06d3
export const USB_PID_CP9550DW_S = 0x03a5

export type MediaKey = '4x6' | '5x7' | '6x9'
export type QualityMode = 'fine' | 'superfine' | 'finedeep'
export type ColorPipeline = 'printer-native' | 'srgb-identity'

export interface MediaPreset {
  key: MediaKey
  label: string
  ribbon: string
  mediaCode: number
  cols: number
  rows: number
  /** Approximate physical size in inches */
  widthIn: number
  heightIn: number
  /** Native print density used by the printer head for this frame */
  dpiApprox: number
}

/** Exact pixel frames accepted by CP-9550DW-S for each ribbon family. */
export const MEDIA_PRESETS: MediaPreset[] = [
  {
    key: '4x6',
    label: '4×6"',
    ribbon: 'CK9015',
    mediaCode: 0x02,
    cols: 2152,
    rows: 1416,
    widthIn: 4,
    heightIn: 6,
    dpiApprox: 359,
  },
  {
    key: '5x7',
    label: '5×7"',
    ribbon: 'CK9318',
    mediaCode: 0x04,
    cols: 1812,
    rows: 2452,
    widthIn: 5,
    heightIn: 7,
    dpiApprox: 346,
  },
  {
    key: '6x9',
    label: '6×9"',
    ribbon: 'CK9523',
    mediaCode: 0x05,
    cols: 2152,
    rows: 2792,
    widthIn: 6,
    heightIn: 9,
    dpiApprox: 359,
  },
]

export interface JobOptions {
  media: MediaPreset
  quality: QualityMode
  copies: number
  /** Cut 2×6 on 4×6 media (9550 only). Default false. */
  cut2x6?: boolean
  colorPipeline: ColorPipeline
}

export interface BuiltSpool {
  bytes: Uint8Array
  cols: number
  rows: number
  planeBytes: number
  totalBytes: number
  footer: string
  notes: string[]
}

function be16(n: number): [number, number] {
  return [(n >> 8) & 0xff, n & 0xff]
}

function fill(buf: Uint8Array, offset: number, values: number[]): number {
  buf.set(values, offset)
  return offset + values.length
}

/** Build the three 50-byte job headers used by CP-9550DW-S (no header 4). */
export function buildHeaders(options: JobOptions): Uint8Array {
  const { media, quality, copies, cut2x6 } = options
  const out = new Uint8Array(150)
  let o = 0

  // Header 1 — size
  o = fill(out, o, [0x1b, 0x57, 0x20, 0x2e, 0x00, 0x0a, 0x10, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00])
  o = fill(out, o, [...be16(media.cols), ...be16(media.rows)])
  // matte byte + 31 nulls already zero

  // Header 2 — copies / quality / cut
  o = 50
  const mode = quality === 'superfine' ? 0x80 : 0x00
  const cut = cut2x6 ? 0x83 : 0x00
  o = fill(out, o, [
    0x1b, 0x57, 0x21, 0x2e, 0x00, 0x80, 0x00, 0x22, 0x08, 0x03,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, ...be16(Math.max(1, Math.min(680, copies))), 0x00, 0x00,
    cut, 0x00, 0x00, 0x00, 0x00, 0x00, mode, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01,
  ])

  // Header 3 — FineDeep
  o = 100
  const fineDeep = quality === 'finedeep' ? 0x01 : 0x00
  o = fill(out, o, [
    0x1b, 0x57, 0x22, 0x2e, 0x00, 0x40, 0x00, 0x00, 0x00, 0x00, 0x00, fineDeep,
  ])

  return out
}

/**
 * Split RGBA/RGB image buffer into planar B, G, R (printer order).
 * Expects tightly packed RGB or RGBA rows of exact cols×rows.
 */
export function splitPlanarBgr(
  pixels: Uint8ClampedArray | Uint8Array,
  cols: number,
  rows: number,
  channels: 3 | 4,
): { b: Uint8Array; g: Uint8Array; r: Uint8Array } {
  const plane = cols * rows
  const b = new Uint8Array(plane)
  const g = new Uint8Array(plane)
  const r = new Uint8Array(plane)
  for (let i = 0, p = 0; p < plane; p++, i += channels) {
    r[p] = pixels[i]!
    g[p] = pixels[i + 1]!
    b[p] = pixels[i + 2]!
  }
  return { b, g, r }
}

function planeBlock(plane: Uint8Array, cols: number, rows: number): Uint8Array {
  const hdr = new Uint8Array(12)
  hdr.set([0x1b, 0x5a, 0x54, 0x00, 0x00, 0x00, 0x00, 0x00, ...be16(cols), ...be16(rows)])
  const out = new Uint8Array(12 + plane.length)
  out.set(hdr, 0)
  out.set(plane, 12)
  return out
}

/** CP-9550DW-S job footer / start-print command. */
export const FOOTER_9550S = new Uint8Array([0x1b, 0x50, 0x47, 0x00])

/** Clear-memory command used on -S variants before plane data. */
export const CLEAR_MEMORY_S = new Uint8Array([0x1b, 0x5a, 0x43, 0x00])

/** Handshake commands unique to -S kiosk printers. */
export const S_INIT_CMDS = [
  new Uint8Array([0x1b, 0x53, 0xc5, 0x9d]),
  new Uint8Array([0x1b, 0x4b, 0x7f, 0x00]),
] as const

export const STATUS_CMDS = {
  media: new Uint8Array([0x1b, 0x56, 0x24, 0x00]),
  status: new Uint8Array([0x1b, 0x56, 0x30, 0x00]),
  statusB: new Uint8Array([0x1b, 0x56, 0x21, 0x00]),
  serial: new Uint8Array([0x1b, 0x72, 0x6e, 0x00]),
} as const

export function buildSpool(
  pixels: Uint8ClampedArray | Uint8Array,
  channels: 3 | 4,
  options: JobOptions,
): BuiltSpool {
  const { media, colorPipeline } = options
  const notes: string[] = []

  if (colorPipeline === 'printer-native') {
    notes.push(
      'Printer-native color: no ICC / no Gutenprint LUT. Pixels go to the head as 8-bit planar BGR.',
    )
  } else {
    notes.push('sRGB identity: values are sent as-is; calibrate externally if needed.')
  }

  notes.push(
    `Native frame ${media.cols}×${media.rows} px (~${media.dpiApprox} dpi on ${media.label}).`,
  )
  notes.push('CP-9550DW-S does not use header 4; footer is 1B 50 47 00.')

  const { b, g, r } = splitPlanarBgr(pixels, media.cols, media.rows, channels)
  const headers = buildHeaders(options)
  const planeB = planeBlock(b, media.cols, media.rows)
  const planeG = planeBlock(g, media.cols, media.rows)
  const planeR = planeBlock(r, media.cols, media.rows)

  const total =
    headers.length + planeB.length + planeG.length + planeR.length + FOOTER_9550S.length
  const bytes = new Uint8Array(total)
  let o = 0
  bytes.set(headers, o)
  o += headers.length
  bytes.set(planeB, o)
  o += planeB.length
  bytes.set(planeG, o)
  o += planeG.length
  bytes.set(planeR, o)
  o += planeR.length
  bytes.set(FOOTER_9550S, o)

  return {
    bytes,
    cols: media.cols,
    rows: media.rows,
    planeBytes: media.cols * media.rows,
    totalBytes: total,
    footer: '1B 50 47 00',
    notes,
  }
}

export function parseMediaStatus(buf: Uint8Array): {
  type: number
  max: number
  remain: number
  name: string
} | null {
  if (buf.length < 48 || buf[0] !== 0x24 || buf[1] !== 0x2e) return null
  const type = buf[14]!
  const max = (buf[28]! << 8) | buf[29]!
  const remain = (buf[32]! << 8) | buf[33]!
  const code = type & 0x0f
  const name =
    code === 0x02
      ? 'CK9015 (4×6)'
      : code === 0x04
        ? 'CK9318 (5×7)'
        : code === 0x05
          ? 'CK9523 (6×9)'
          : `Unknown (0x${type.toString(16)})`
  return { type, max, remain, name }
}

export function mediaByKey(key: MediaKey): MediaPreset {
  const found = MEDIA_PRESETS.find((m) => m.key === key)
  if (!found) throw new Error(`Unknown media ${key}`)
  return found
}
