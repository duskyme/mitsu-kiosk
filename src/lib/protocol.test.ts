import { buildHeaders, buildSpool, mediaByKey, FOOTER_9550S } from './protocol'

function assert(cond: unknown, msg: string): asserts cond {
  if (!cond) throw new Error(msg)
}

const media = mediaByKey('4x6')
const headers = buildHeaders({
  media,
  quality: 'superfine',
  copies: 2,
  colorPipeline: 'printer-native',
})

assert(headers.length === 150, 'headers must be 150 bytes')
assert(headers[0] === 0x1b && headers[1] === 0x57 && headers[2] === 0x20, 'hdr1 magic')
assert(headers[14] === 0x08 && headers[15] === 0x68, 'cols 2152')
assert(headers[16] === 0x05 && headers[17] === 0x88, 'rows 1416')
assert(headers[88] === 0x80, 'superfine mode')

const plane = media.cols * media.rows
const rgba = new Uint8ClampedArray(plane * 4)
for (let i = 0; i < plane; i++) {
  rgba[i * 4] = 10
  rgba[i * 4 + 1] = 20
  rgba[i * 4 + 2] = 30
  rgba[i * 4 + 3] = 255
}

const spool = buildSpool(rgba, 4, {
  media,
  quality: 'fine',
  copies: 1,
  colorPipeline: 'printer-native',
})

const expected = 150 + 3 * (12 + plane) + 4
assert(spool.totalBytes === expected, `spool size ${spool.totalBytes} != ${expected}`)
assert(
  spool.bytes.slice(-4).every((b, i) => b === FOOTER_9550S[i]),
  'footer mismatch',
)

console.log('protocol.test.ts: OK')
