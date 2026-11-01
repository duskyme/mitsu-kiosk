"""Offline unit tests for spool layout (no printer required)."""

from __future__ import annotations

import unittest

from protocol import (
    FOOTER_9550S,
    MEDIA,
    Quality,
    build_headers,
    build_spool,
    parse_media_status,
    split_planar_rgb,
)


class ProtocolTests(unittest.TestCase):
    def test_headers_size_and_magic(self) -> None:
        media = MEDIA["4x6"]
        hdr = build_headers(media, Quality.SUPERFINE, copies=2)
        self.assertEqual(len(hdr), 150)
        self.assertEqual(hdr[0:4], bytes([0x1B, 0x57, 0x20, 0x2E]))
        self.assertEqual(hdr[50:54], bytes([0x1B, 0x57, 0x21, 0x2E]))
        self.assertEqual(hdr[100:104], bytes([0x1B, 0x57, 0x22, 0x2E]))
        # cols / rows big-endian in header 1
        self.assertEqual(hdr[14:18], bytes([0x08, 0x68, 0x05, 0x88]))  # 2152, 1416
        # superfine mode bit (offset 38 inside header 2)
        self.assertEqual(hdr[50 + 38], 0x80)
        # copies = 2
        self.assertEqual(hdr[50 + 28 : 50 + 30], bytes([0x00, 0x02]))

    def test_finedeep_flag(self) -> None:
        hdr = build_headers(MEDIA["5x7"], Quality.FINEDEEP, 1)
        self.assertEqual(hdr[100 + 11], 0x01)
        self.assertEqual(hdr[50 + 38], 0x00)  # fine, not superfine

    def test_planar_split_and_spool_size(self) -> None:
        media = MEDIA["4x6"]
        # Tiny synthetic pattern scaled mentally: build exact frame of gray
        plane = media.cols * media.rows
        rgb = bytes([10, 20, 30]) * plane
        r, g, b = split_planar_rgb(rgb, media.cols, media.rows)
        self.assertEqual(r, bytes([10]) * plane)
        self.assertEqual(g, bytes([20]) * plane)
        self.assertEqual(b, bytes([30]) * plane)

        spool = build_spool(rgb, media, Quality.FINE, 1)
        expected = 150 + 3 * (12 + plane) + 4
        self.assertEqual(len(spool), expected)
        self.assertTrue(spool.endswith(FOOTER_9550S))
        # First plane header + Red payload (planar RGB, not BGR).
        self.assertEqual(spool[150:154], bytes([0x1B, 0x5A, 0x54, 0x00]))
        self.assertEqual(spool[150 + 12], 10)

    def test_parse_media(self) -> None:
        buf = bytearray(48)
        buf[0:2] = bytes([0x24, 0x2E])
        buf[14] = 0x02
        buf[28:30] = bytes([0x01, 0x90])  # 400
        buf[32:34] = bytes([0x01, 0x2C])  # 300
        parsed = parse_media_status(bytes(buf))
        assert parsed is not None
        self.assertEqual(parsed["max"], 400)
        self.assertEqual(parsed["remain"], 300)
        self.assertIn("4x6", parsed["name"])


if __name__ == "__main__":
    unittest.main()
