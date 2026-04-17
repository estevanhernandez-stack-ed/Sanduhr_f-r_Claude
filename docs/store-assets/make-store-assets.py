"""Generate Microsoft Store / social listing assets.

Two brand marks are used in Partner Center:

  1. "Store logo" / publisher identity → the **626Labs company logo**
     (hex-brain + "626Labs LLC / Imagine Something Else" wordmark).
     Source: ~/OneDrive/Pictures/626Labs.png (1:1 and 2:3 canvases).

  2. "App tile" / product identity → the **Sanduhr app icon** (hourglass)
     Source: windows/icon/source.png (71×71, 150×150, 300×300 canvases).

Store logos carry the publisher; app tiles carry this specific product.
Don't mix them.

Run:
    python make-store-assets.py  [--company path] [--app path]
"""

import argparse
import pathlib
from PIL import Image

HERE = pathlib.Path(__file__).resolve().parent
REPO = HERE.parent.parent
DEFAULT_COMPANY = pathlib.Path.home() / "OneDrive" / "Pictures" / "626Labs.png"
DEFAULT_APP = REPO / "windows" / "icon" / "source.png"

# Navy sampled directly from the 626Labs.png source so the company logo's
# rounded-rect background blends seamlessly into the canvas. The Sanduhr
# app icon's own navy is slightly different but close enough; either uses
# this canvas color.
NAVY = (25, 46, 69, 255)


def center_on_navy(src: Image.Image, w: int, h: int, scale: float) -> Image.Image:
    canvas = Image.new("RGBA", (w, h), NAVY)
    # Scale to fit short side × scale factor
    target_h = int(h * scale)
    aspect = src.width / src.height
    target_w = int(target_h * aspect)
    # Clamp to canvas width with some padding
    max_w = int(w * 0.88)
    if target_w > max_w:
        target_w = max_w
        target_h = int(target_w / aspect)
    logo = src.resize((target_w, target_h), Image.LANCZOS)
    canvas.paste(logo, ((w - target_w) // 2, (h - target_h) // 2), logo)
    return canvas.convert("RGB")


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--company", type=pathlib.Path, default=DEFAULT_COMPANY,
                    help=f"Path to the 626Labs company logo (default: {DEFAULT_COMPANY})")
    ap.add_argument("--app", type=pathlib.Path, default=DEFAULT_APP,
                    help=f"Path to the Sanduhr app icon (default: {DEFAULT_APP})")
    args = ap.parse_args()

    for p, label in [(args.company, "company"), (args.app, "app")]:
        if not p.exists():
            raise SystemExit(f"{label} source not found: {p}")

    # -- Store logos: 626Labs company identity -----------------------------
    company = Image.open(args.company).convert("RGBA")
    print(f"company: {args.company.name} ({company.size[0]}x{company.size[1]})")

    square = center_on_navy(company, 1080, 1080, scale=0.78)
    (HERE / "logo-square-1080x1080.png").parent.mkdir(exist_ok=True)
    square.save(HERE / "logo-square-1080x1080.png", "PNG")
    print("wrote logo-square-1080x1080.png (1080x1080)")

    portrait = center_on_navy(company, 720, 1080, scale=0.62)
    portrait.save(HERE / "logo-portrait-720x1080.png", "PNG")
    print("wrote logo-portrait-720x1080.png (720x1080)")

    # -- App tiles: Sanduhr hourglass icon ---------------------------------
    app = Image.open(args.app).convert("RGBA")
    print(f"app: {args.app.name} ({app.size[0]}x{app.size[1]})")

    # Bigger scales than the company logo — the hourglass icon has its
    # own internal padding (Big Sur icon template) and reads best when it
    # fills the tile almost edge to edge.
    for size, scale in [(300, 0.96), (150, 0.98), (71, 1.0)]:
        tile = center_on_navy(app, size, size, scale=scale)
        out = HERE / f"app-tile-{size}x{size}.png"
        tile.save(out, "PNG")
        print(f"wrote {out.name} ({size}x{size})")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
