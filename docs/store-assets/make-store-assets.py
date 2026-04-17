"""Generate Microsoft Store / social listing assets from the 626Labs logo.

Takes the source 626Labs.png (hex-brain + wordmark), scales it onto a
navy canvas matching the brand palette, saves both:

  logo-square-1080x1080.png   — 1:1, full-bleed-ish, social post
  logo-portrait-720x1080.png  — 2:3, tall, Store banner / IG Story

The logo already has "626Labs LLC / Imagine Something Else" baked in,
so no extra text layout needed — just center on navy with breathing
room.

Run:
    python make-store-assets.py  [--source path/to/626Labs.png]
"""

import argparse
import pathlib
from PIL import Image

HERE = pathlib.Path(__file__).resolve().parent
DEFAULT_SOURCE = pathlib.Path.home() / "OneDrive" / "Pictures" / "626Labs.png"

# Navy sampled directly from the 626Labs.png source so the logo's own
# rounded-rect background blends seamlessly into the canvas (rather than
# sitting as a visible rectangle on a different navy).
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
    ap.add_argument("--source", type=pathlib.Path, default=DEFAULT_SOURCE,
                    help=f"Path to the 626Labs logo PNG (default: {DEFAULT_SOURCE})")
    args = ap.parse_args()

    if not args.source.exists():
        raise SystemExit(f"Source not found: {args.source}")

    src = Image.open(args.source).convert("RGBA")
    print(f"source: {args.source.name} ({src.size[0]}x{src.size[1]})")

    # 1:1 square — the logo is already balanced, fill it comfortably
    square = center_on_navy(src, 1080, 1080, scale=0.78)
    square_out = HERE / "logo-square-1080x1080.png"
    square.save(square_out, "PNG")
    print(f"wrote {square_out.name} (1080x1080)")

    # 2:3 portrait — tall canvas, logo centered with navy space top/bottom
    portrait = center_on_navy(src, 720, 1080, scale=0.62)
    portrait_out = HERE / "logo-portrait-720x1080.png"
    portrait.save(portrait_out, "PNG")
    print(f"wrote {portrait_out.name} (720x1080)")

    # App tile variants for Partner Center "Store logos" grid slots.
    # Smaller canvases need a bigger relative logo so it reads well at
    # small sizes, especially 71x71 where the wordmark becomes unreadable
    # if we pad too much.
    for size, scale in [(300, 0.90), (150, 0.92), (71, 0.96)]:
        tile = center_on_navy(src, size, size, scale=scale)
        out = HERE / f"app-tile-{size}x{size}.png"
        tile.save(out, "PNG")
        print(f"wrote {out.name} ({size}x{size})")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
