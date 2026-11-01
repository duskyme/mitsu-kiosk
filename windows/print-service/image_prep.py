"""Exact-pixel rasterization for CP-9550DW-S (no ICC / no Gutenprint LUT)."""

from __future__ import annotations

from pathlib import Path

from PIL import Image

from protocol import MediaPreset


def rasterize(path: Path, media: MediaPreset, fit: str = "cover", bg: str = "#FFFFFF") -> bytes:
    """
    Return tightly packed RGB bytes at media.cols × media.rows.
    Color values are left untouched beyond resampling — printer-native pipeline.
    """
    with Image.open(path) as im:
        im = im.convert("RGB")
        src_w, src_h = im.size
        scale_x = media.cols / src_w
        scale_y = media.rows / src_h
        scale = max(scale_x, scale_y) if fit == "cover" else min(scale_x, scale_y)
        new_w = max(1, round(src_w * scale))
        new_h = max(1, round(src_h * scale))
        # LANCZOS keeps edge detail better than CUPS default filters.
        resized = im.resize((new_w, new_h), Image.Resampling.LANCZOS)

        canvas = Image.new("RGB", (media.cols, media.rows), bg)
        dx = (media.cols - new_w) // 2
        dy = (media.rows - new_h) // 2
        canvas.paste(resized, (dx, dy))
        return canvas.tobytes()
