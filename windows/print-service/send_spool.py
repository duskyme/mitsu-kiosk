#!/usr/bin/env python3
"""Send a pre-built .mitsu9550s spool to CP-9550DW-S."""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

from device import open_printer


def main() -> int:
    p = argparse.ArgumentParser(description="Send a .mitsu9550s spool to CP-9550DW-S")
    p.add_argument("spool", type=Path)
    args = p.parse_args()
    data = args.spool.read_bytes()
    print(f"Loaded {args.spool} ({len(data):,} bytes)")

    prn = open_printer()
    try:
        info = prn.info()
        print(f"Printer: {info.serial or 'CP-9550DW-S'} · {info.media_name}")

        def progress(done: int, total: int) -> None:
            pct = 100.0 * done / max(1, total)
            print(f"\rUSB transfer {pct:5.1f}%", end="", flush=True)

        prn.send_spool_payload(data, progress=progress)
        print("\nWaiting for idle…")
        prn.wait_idle()
        print("Done.")
        return 0
    finally:
        prn.close()


if __name__ == "__main__":
    raise SystemExit(main())
