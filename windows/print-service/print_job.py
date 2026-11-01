#!/usr/bin/env python3
"""Build a native-resolution CP-9550DW-S job and optionally send it over USB."""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

from image_prep import rasterize
from protocol import MEDIA, Quality, build_spool


def main() -> int:
    p = argparse.ArgumentParser(
        description="Print an image to Mitsubishi CP-9550DW-S at native resolution "
        "without Gutenprint color management."
    )
    p.add_argument("image", type=Path, help="Source photo (JPEG/PNG/TIFF)")
    p.add_argument("--media", choices=sorted(MEDIA.keys()), default="4x6")
    p.add_argument(
        "--quality",
        choices=[q.value for q in Quality],
        default=Quality.SUPERFINE.value,
    )
    p.add_argument("--copies", type=int, default=1)
    p.add_argument("--fit", choices=["cover", "contain"], default="cover")
    p.add_argument(
        "--spool-out",
        type=Path,
        help="Write .mitsu9550s spool instead of (or before) printing",
    )
    p.add_argument(
        "--dry-run",
        action="store_true",
        help="Only build the spool; do not open USB",
    )
    p.add_argument(
        "--print",
        dest="do_print",
        action="store_true",
        help="Send the job to the printer over WinUSB/libusb",
    )
    args = p.parse_args()

    if not args.image.exists():
        print(f"File not found: {args.image}", file=sys.stderr)
        return 1

    media = MEDIA[args.media]
    quality = Quality(args.quality)
    print(
        f"Rasterizing {args.image.name} → {media.cols}×{media.rows} "
        f"({media.label}, {media.ribbon}), quality={quality.value}, fit={args.fit}"
    )
    rgb = rasterize(args.image, media, fit=args.fit)
    spool = build_spool(rgb, media, quality, copies=args.copies)
    print(f"Spool size: {len(spool):,} bytes (planar BGR, printer-native color)")

    out = args.spool_out
    if out is None and (args.dry_run or not args.do_print):
        out = args.image.with_suffix(".mitsu9550s")
    if out is not None:
        out.write_bytes(spool)
        print(f"Wrote {out}")

    if args.dry_run or not args.do_print:
        if not args.do_print:
            print("Tip: add --print to send over USB after WinUSB is installed.")
        return 0

    from device import open_printer

    prn = open_printer()
    try:
        info = prn.info()
        print(f"Printer: {info.serial or 'CP-9550DW-S'} · {info.media_name} · "
              f"{info.media_remain}/{info.media_max} remaining")

        def progress(done: int, total: int) -> None:
            pct = 100.0 * done / max(1, total)
            print(f"\rUSB transfer {pct:5.1f}%", end="", flush=True)

        prn.send_spool_payload(spool, progress=progress)
        print("\nJob submitted. Waiting for idle…")
        prn.wait_idle()
        print("Done.")
        return 0
    finally:
        prn.close()


if __name__ == "__main__":
    raise SystemExit(main())
