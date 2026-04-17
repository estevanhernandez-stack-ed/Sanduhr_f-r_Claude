"""Generate MSIX tile/logo image assets from the committed icon source.

MSIX packages need a handful of specific square logo sizes plus a Store
badge. We composite `windows/icon/source.png` onto a navy canvas at each
size (a transparent background would give the tile a raw edge against
the Start Menu).

Outputs to `windows/msix/Images/`.

Run:
    python make-msix-images.py
"""

import pathlib
from PIL import Image

HERE = pathlib.Path(__file__).resolve().parent
SOURCE = HERE.parent / "icon" / "source.png"
OUT = HERE / "Images"
OUT.mkdir(parents=True, exist_ok=True)

# Brand-navy bg so the tile reads as a deliberate color on any accent.
NAVY = (15, 24, 43, 255)


def render(name: str, size: int, icon_scale: float = 0.82) -> None:
    canvas = Image.new("RGBA", (size, size), NAVY)
    src = Image.open(SOURCE).convert("RGBA")
    target = int(size * icon_scale)
    icon = src.resize((target, target), Image.LANCZOS)
    offset = (size - target) // 2
    canvas.paste(icon, (offset, offset), icon)
    canvas.convert("RGB").save(OUT / name, "PNG")
    print(f"wrote {name} ({size}x{size})")


def render_store_logo() -> None:
    """50x50 StoreLogo shown in the Store listing thumbnails."""
    render("StoreLogo.png", 50, icon_scale=0.86)


def render_tiles() -> None:
    # App tile on Start Menu small (44) + medium (150) + launcher icon 44
    render("Square44x44Logo.png", 44, icon_scale=0.86)
    render("Square150x150Logo.png", 150, icon_scale=0.82)


def main() -> int:
    render_tiles()
    render_store_logo()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
