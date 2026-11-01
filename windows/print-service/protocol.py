"""
Mitsubishi CP-9550DW-S native spool + USB helpers.

Protocol layout documented by Solomon Peachy (selphy_print). Reimplemented here
for Windows user-mode printing without Gutenprint color management.
"""

from __future__ import annotations

from dataclasses import dataclass
from enum import Enum
from typing import Iterable

USB_VID = 0x06D3
USB_PID_CP9550DW_S = 0x03A5

# WinUSB interface GUID from windows/inf/CP9550DW-S.inf
WINUSB_INTERFACE_GUID = "{D3C9550A-06D3-4A55-9D77-C9550D050001}"

FOOTER_9550S = bytes([0x1B, 0x50, 0x47, 0x00])
CLEAR_MEMORY_S = bytes([0x1B, 0x5A, 0x43, 0x00])
S_INIT = (
    bytes([0x1B, 0x53, 0xC5, 0x9D]),
    bytes([0x1B, 0x4B, 0x7F, 0x00]),
)
CMD_MEDIA = bytes([0x1B, 0x56, 0x24, 0x00])
CMD_STATUS = bytes([0x1B, 0x56, 0x30, 0x00])
CMD_STATUS_B = bytes([0x1B, 0x56, 0x21, 0x00])
CMD_SERIAL = bytes([0x1B, 0x72, 0x6E, 0x00])


class Quality(str, Enum):
    FINE = "fine"
    SUPERFINE = "superfine"
    FINEDEEP = "finedeep"


@dataclass(frozen=True)
class MediaPreset:
    key: str
    label: str
    ribbon: str
    media_code: int
    cols: int
    rows: int


MEDIA = {
    "4x6": MediaPreset("4x6", '4×6"', "CK9015", 0x02, 2152, 1416),
    "5x7": MediaPreset("5x7", '5×7"', "CK9318", 0x04, 1812, 2452),
    "6x9": MediaPreset("6x9", '6×9"', "CK9523", 0x05, 2152, 2792),
}


def _be16(n: int) -> bytes:
    return bytes([(n >> 8) & 0xFF, n & 0xFF])


def build_headers(media: MediaPreset, quality: Quality, copies: int, cut_2x6: bool = False) -> bytes:
    copies = max(1, min(680, copies))
    mode = 0x80 if quality is Quality.SUPERFINE else 0x00
    fine_deep = 0x01 if quality is Quality.FINEDEEP else 0x00
    cut = 0x83 if cut_2x6 else 0x00

    hdr1 = bytearray(50)
    hdr1[0:14] = bytes([0x1B, 0x57, 0x20, 0x2E, 0x00, 0x0A, 0x10, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00])
    hdr1[14:18] = _be16(media.cols) + _be16(media.rows)

    hdr2 = bytearray(50)
    hdr2[:] = bytes(
        [
            0x1B, 0x57, 0x21, 0x2E, 0x00, 0x80, 0x00, 0x22, 0x08, 0x03,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, *_be16(copies), 0x00, 0x00,
            cut, 0x00, 0x00, 0x00, 0x00, 0x00, mode, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01,
        ]
    )

    hdr3 = bytearray(50)
    hdr3[0:12] = bytes([0x1B, 0x57, 0x22, 0x2E, 0x00, 0x40, 0x00, 0x00, 0x00, 0x00, 0x00, fine_deep])

    return bytes(hdr1 + hdr2 + hdr3)


def split_planar_bgr(rgb_bytes: bytes, cols: int, rows: int) -> tuple[bytes, bytes, bytes]:
    """rgb_bytes: tightly packed RGB (3 bytes/pixel). Returns B, G, R planes."""
    plane = cols * rows
    if len(rgb_bytes) < plane * 3:
        raise ValueError(f"Need {plane * 3} RGB bytes, got {len(rgb_bytes)}")
    b = bytearray(plane)
    g = bytearray(plane)
    r = bytearray(plane)
    mv = memoryview(rgb_bytes)
    for i in range(plane):
        o = i * 3
        r[i] = mv[o]
        g[i] = mv[o + 1]
        b[i] = mv[o + 2]
    return bytes(b), bytes(g), bytes(r)


def _plane_block(plane: bytes, cols: int, rows: int) -> bytes:
    hdr = bytes([0x1B, 0x5A, 0x54, 0x00, 0x00, 0x00, 0x00, 0x00]) + _be16(cols) + _be16(rows)
    return hdr + plane


def build_spool(rgb_bytes: bytes, media: MediaPreset, quality: Quality, copies: int = 1) -> bytes:
    b, g, r = split_planar_bgr(rgb_bytes, media.cols, media.rows)
    return (
        build_headers(media, quality, copies)
        + _plane_block(b, media.cols, media.rows)
        + _plane_block(g, media.cols, media.rows)
        + _plane_block(r, media.cols, media.rows)
        + FOOTER_9550S
    )


def parse_media_status(buf: bytes) -> dict | None:
    if len(buf) < 48 or buf[0] != 0x24 or buf[1] != 0x2E:
        return None
    media_type = buf[14]
    maximum = (buf[28] << 8) | buf[29]
    remain = (buf[32] << 8) | buf[33]
    code = media_type & 0x0F
    name = {
        0x02: "CK9015 (4x6)",
        0x04: "CK9318 (5x7)",
        0x05: "CK9523 (6x9)",
    }.get(code, f"Unknown (0x{media_type:02x})")
    return {"type": media_type, "max": maximum, "remain": remain, "name": name}


def iter_chunks(data: bytes, size: int = 65536) -> Iterable[bytes]:
    for i in range(0, len(data), size):
        yield data[i : i + size]
