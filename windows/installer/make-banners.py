"""Generate installer wizard banner BMPs from the 626 Labs icon source.

Inno Setup 6 uses two images during the install wizard:
  * WizardImageFile      — 164×314, shown on the Welcome + Finish pages (big)
  * WizardSmallImageFile — 55×58, shown in the top-right of interior pages

Both expect BMP. We composite the hourglass icon centered on a navy
background matching the icon's own palette (so the banner feels like it
cropped out of the icon rather than a separate graphic).

Run from windows/installer/:
    python make-banners.py
"""

import pathlib
from PIL import Image

HERE = pathlib.Path(__file__).resolve().parent
SOURCE = HERE.parent / "icon" / "source.png"

# Navy-deep from mac/icon/generate.swift (brand bg color)
NAVY = (15, 24, 43, 255)


def render(out: pathlib.Path, size: tuple[int, int], icon_scale: float) -> None:
    """Composite the icon on a navy canvas sized for Inno Setup."""
    w, h = size
    canvas = Image.new("RGBA", size, NAVY)

    src = Image.open(SOURCE).convert("RGBA")
    # Scale icon to fit the shorter dimension with a bit of breathing room
    short = min(w, h)
    target = int(short * icon_scale)
    icon = src.resize((target, target), Image.LANCZOS)

    # For the tall banner, bias the icon toward the top (classic installer feel).
    if h > w * 1.5:
        x = (w - target) // 2
        y = int((h - target) * 0.25)
    else:
        x = (w - target) // 2
        y = (h - target) // 2
    canvas.paste(icon, (x, y), icon)
    canvas.convert("RGB").save(out, "BMP")
    print(f"wrote {out.name} ({w}x{h})")


def main() -> int:
    render(HERE / "banner.bmp", (164, 314), icon_scale=0.92)
    render(HERE / "banner-small.bmp", (55, 58), icon_scale=0.85)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
