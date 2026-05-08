"""Generate Microsoft Store promotional images for Sanduhr für Claude.

Outputs three sizes covering Partner Center's promotional surfaces:
  - Sanduhr-Square-2160.png       — 2160x2160 (Store hero / 1:1 promo)
  - Sanduhr-Hero-3840x2160.png    — 3840x2160 (4K landscape hero)
  - Sanduhr-Portrait-1440x2160.png — 1440x2160 (2:3 portrait promo)

Composition obeys the 626Labs design system:
  - Surfaces: --brand-navy-deep (#0f1f31), --brand-navy (#192e44)
  - Signature duo: --brand-cyan (#17d4fa) + --brand-magenta (#f22f89)
    paired via 135deg gradient
  - Type: Space Grotesk (display, -0.02em tracking on big sizes),
    Inter (UI), JetBrains Mono (small UPPERCASE +0.12em meta labels)
  - Voice: builder-to-builder, sentence case, em-dashes welcome,
    no emoji, no hedging verbs

Run from windows/:
    .venv/Scripts/python.exe msix/promotional/make-promo-images.py
"""

import math
import pathlib
from typing import Optional

from PIL import Image, ImageDraw, ImageFilter, ImageFont

HERE = pathlib.Path(__file__).resolve().parent
WINDOWS_DIR = HERE.parent.parent
SOURCE_ICON = WINDOWS_DIR / "icon" / "source.png"
OUT = HERE
OUT.mkdir(parents=True, exist_ok=True)

# 626Labs hub fonts (variable TTFs cover the full weight axis we need).
HUB_FONTS = pathlib.Path(
    r"C:\Users\estev\Projects\626labs-hub\fonts"
)
SPACE_GROTESK = HUB_FONTS / "SpaceGrotesk-Variable.ttf"
INTER = HUB_FONTS / "Inter-Variable.ttf"
JETBRAINS_MONO = HUB_FONTS / "JetBrainsMono-Regular.ttf"

# 626Labs brand palette — exact values from colors_and_type.css.
NAVY_DEEP = (15, 31, 49)
NAVY = (25, 46, 68)
NAVY_SOFT = (34, 58, 84)
CYAN = (23, 212, 250)
CYAN_DIM = (15, 168, 201)
MAGENTA = (242, 47, 137)
MAGENTA_DIM = (194, 31, 108)
INK_0 = (255, 255, 255)
INK_200 = (196, 205, 218)
INK_300 = (142, 155, 173)


def font(path: pathlib.Path, size: int) -> ImageFont.FreeTypeFont:
    return ImageFont.truetype(str(path), size=size)


def radial_gradient_layer(size: tuple[int, int], cx: float, cy: float,
                          radius: float, color: tuple[int, int, int],
                          max_alpha: float) -> Image.Image:
    """A soft radial alpha bloom centered at (cx, cy). max_alpha is
    0.0–1.0 at the brightest center pixel; falloff is quadratic so the
    bloom blends instead of clipping."""
    w, h = size
    layer = Image.new("RGBA", size, (0, 0, 0, 0))
    px = layer.load()
    r2 = radius * radius
    for y in range(h):
        for x in range(w):
            dx, dy = x - cx, y - cy
            d2 = dx * dx + dy * dy
            if d2 >= r2:
                continue
            t = 1.0 - (d2 / r2)
            t = t * t  # quadratic falloff — softer at edges
            a = int(255 * max_alpha * t)
            if a > 0:
                px[x, y] = (*color, a)
    return layer


def hex_motif_layer(size: tuple[int, int], cx: float, cy: float,
                    radius: float, color: tuple[int, int, int],
                    alpha: int, stroke: int = 4) -> Image.Image:
    """A faint flat hexagon outline — the 626Labs 'circuit-trace
    accent' motif from the logo. Pointed-top orientation matches the
    Sanduhr icon's interior hex."""
    layer = Image.new("RGBA", size, (0, 0, 0, 0))
    draw = ImageDraw.Draw(layer)
    pts = []
    for i in range(6):
        angle = math.radians(60 * i - 90)
        pts.append((cx + radius * math.cos(angle),
                    cy + radius * math.sin(angle)))
    draw.polygon(pts, outline=(*color, alpha), width=stroke)
    return layer


def base_canvas(size: tuple[int, int],
                glow_offset: tuple[float, float] = (0.5, 0.4)) -> Image.Image:
    """Brand-conformant base: navy-deep field + brand-gradient-glow
    (cyan + magenta radials), reading as the duo without taking over.
    glow_offset positions the cyan bloom; magenta sits at the
    diagonally-opposite corner."""
    w, h = size
    canvas = Image.new("RGBA", size, (*NAVY_DEEP, 255))
    cyan_x, cyan_y = w * glow_offset[0], h * glow_offset[1]
    mag_x, mag_y = w * (1 - glow_offset[0]), h * (1 - glow_offset[1])
    glow_r = max(w, h) * 0.65
    cyan_glow = radial_gradient_layer(size, cyan_x, cyan_y, glow_r, CYAN, 0.22)
    magenta_glow = radial_gradient_layer(size, mag_x, mag_y, glow_r, MAGENTA, 0.18)
    canvas = Image.alpha_composite(canvas, cyan_glow)
    canvas = Image.alpha_composite(canvas, magenta_glow)
    return canvas


def composite_icon(canvas: Image.Image, icon: Image.Image,
                   center: tuple[int, int], target_size: int,
                   add_glow: bool = True) -> None:
    """Paste the Sanduhr icon at the given center with an optional
    cyan halo behind it (sells the 'live token-burn' lit-up energy)."""
    cx, cy = center
    icon_resized = icon.resize((target_size, target_size), Image.LANCZOS)
    if add_glow:
        glow_r = int(target_size * 0.7)
        glow = radial_gradient_layer(
            canvas.size, cx, cy, glow_r, CYAN, 0.30
        )
        canvas.alpha_composite(glow)
    paste_xy = (cx - target_size // 2, cy - target_size // 2)
    canvas.paste(icon_resized, paste_xy, icon_resized)


def draw_text_centered(draw: ImageDraw.ImageDraw, xy: tuple[int, int],
                       text: str, fnt: ImageFont.FreeTypeFont,
                       fill: tuple[int, int, int]) -> None:
    bbox = draw.textbbox((0, 0), text, font=fnt)
    w = bbox[2] - bbox[0]
    h = bbox[3] - bbox[1]
    draw.text((xy[0] - w // 2 - bbox[0], xy[1] - h // 2 - bbox[1]),
              text, font=fnt, fill=fill)


def draw_meta_label(draw: ImageDraw.ImageDraw, xy: tuple[int, int],
                    text: str, fnt: ImageFont.FreeTypeFont,
                    fill: tuple[int, int, int],
                    tracking_em: float = 0.12,
                    anchor: str = "lt") -> None:
    """Draw uppercase meta label with letter-spacing tracking
    (Pillow has no native tracking, so we render glyph-by-glyph)."""
    text = text.upper()
    x, y = xy
    cap_height = fnt.size
    extra = int(cap_height * tracking_em)
    if anchor == "mt":
        # measure full width with tracking, then center
        total = sum(
            draw.textbbox((0, 0), c, font=fnt)[2] -
            draw.textbbox((0, 0), c, font=fnt)[0]
            for c in text
        ) + extra * (len(text) - 1)
        x = x - total // 2
    for i, c in enumerate(text):
        draw.text((x, y), c, font=fnt, fill=fill)
        bbox = draw.textbbox((0, 0), c, font=fnt)
        x += (bbox[2] - bbox[0]) + extra


def gradient_underline(canvas: Image.Image, xy: tuple[int, int],
                       width: int, height: int = 6) -> None:
    """A 135deg cyan→magenta hairline used as a divider / underline.
    Echoes the brand-gradient swoosh under hero text."""
    grad = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    px = grad.load()
    for x in range(width):
        t = x / max(1, width - 1)
        r = int(CYAN[0] + (MAGENTA[0] - CYAN[0]) * t)
        g = int(CYAN[1] + (MAGENTA[1] - CYAN[1]) * t)
        b = int(CYAN[2] + (MAGENTA[2] - CYAN[2]) * t)
        for y in range(height):
            px[x, y] = (r, g, b, 255)
    canvas.paste(grad, xy, grad)


def feature_dot(draw: ImageDraw.ImageDraw, xy: tuple[int, int],
                index: int, radius: int = 14) -> None:
    """Alternating cyan / magenta filled dot — the 626Labs duo always
    pairs, so feature lists alternate the accent."""
    color = CYAN if index % 2 == 0 else MAGENTA
    x, y = xy
    draw.ellipse([x - radius, y - radius, x + radius, y + radius],
                 fill=(*color, 255))


# ---------------------------------------------------------------------
# Image 1 — Square hero (2160x2160)
# ---------------------------------------------------------------------

def make_square_hero(icon: Image.Image) -> Image.Image:
    size = 2160
    canvas = base_canvas((size, size), glow_offset=(0.30, 0.30))
    draw = ImageDraw.Draw(canvas)

    # Background hex motif — pointed-top, sized to give the icon a
    # 'frame within a frame' that nods at the 626Labs logo's hexagon.
    hex_layer = hex_motif_layer(
        (size, size), size // 2, size // 2 - 80,
        radius=900, color=CYAN, alpha=22, stroke=6
    )
    canvas = Image.alpha_composite(canvas, hex_layer)
    draw = ImageDraw.Draw(canvas)

    # Hero icon — large, with cyan halo behind it.
    composite_icon(canvas, icon, (size // 2, 880), target_size=1100)

    # Wordmark
    title_font = font(SPACE_GROTESK, 200)
    draw_text_centered(draw, (size // 2, 1620),
                       "Sanduhr für Claude", title_font, INK_0)

    # Gradient hairline under wordmark (brand swoosh nod)
    underline_w = 740
    gradient_underline(canvas,
                       ((size - underline_w) // 2, 1730),
                       underline_w, height=6)

    # Tagline
    tagline_font = font(INTER, 80)
    draw_text_centered(draw, (size // 2, 1820),
                       "An hourglass for your Claude.ai usage.",
                       tagline_font, INK_200)

    # Meta strip — JetBrains Mono uppercase, +0.12em tracking
    meta_font = font(JETBRAINS_MONO, 44)
    draw_meta_label(draw, (size // 2, 2010),
                    "v2.3.0  ·  windows desktop  ·  626labs llc",
                    meta_font, INK_300, anchor="mt")

    return canvas


# ---------------------------------------------------------------------
# Image 2 — 4K landscape hero (3840x2160)
# ---------------------------------------------------------------------

def make_landscape_hero(icon: Image.Image) -> Image.Image:
    w, h = 3840, 2160
    canvas = base_canvas((w, h), glow_offset=(0.20, 0.30))
    draw = ImageDraw.Draw(canvas)

    # Hex motif behind the icon (left third)
    hex_layer = hex_motif_layer(
        (w, h), 980, h // 2 - 30,
        radius=820, color=CYAN, alpha=22, stroke=6
    )
    canvas = Image.alpha_composite(canvas, hex_layer)
    draw = ImageDraw.Draw(canvas)

    # Hero icon (left), wordmark beneath it
    composite_icon(canvas, icon, (980, h // 2 - 80), target_size=1280)
    title_font = font(SPACE_GROTESK, 132)
    draw_text_centered(draw, (980, 1820),
                       "Sanduhr für Claude", title_font, INK_0)
    sub_font = font(INTER, 56)
    draw_text_centered(draw, (980, 1930),
                       "626Labs LLC  ·  Windows desktop widget",
                       sub_font, INK_300)

    # Right column — feature pitch
    col_x = 2100
    col_w = 1600

    # Eyebrow
    eyebrow_font = font(JETBRAINS_MONO, 48)
    draw_meta_label(draw, (col_x, 360),
                    "v2.3.0  ·  what's new",
                    eyebrow_font, CYAN, anchor="lt")

    # Headline. 138pt keeps the longest line ("right on your desktop.")
    # inside the 1600px column at this column-x position; bumping
    # higher than that clips the trailing period on the 3840x2160
    # canvas — we don't have margin to spare.
    headline_font = font(SPACE_GROTESK, 138)
    draw.text((col_x, 440), "Live token-burn,",
              font=headline_font, fill=INK_0)
    draw.text((col_x, 600), "right on your desktop.",
              font=headline_font, fill=INK_0)

    # Gradient hairline
    gradient_underline(canvas, (col_x, 800), 540, height=6)

    # Feature bullets
    body_font = font(INTER, 60)
    label_font = font(INTER, 48)
    features = [
        ("Daily Routines",
         "Run-quota card synced to claude.ai/code in real time."),
        ("Local Claude Code tab",
         "Reads your session JSONLs — no upload, no network."),
        ("Live token-burn delta",
         "+1.2k since last fetch, on every tier card."),
        ("Drag-reorder cards",
         "Pick what shows on the widget. Save is automatic."),
        ("Modern Win11 chrome",
         "Mica glass, frameless, taskbar icon binds reliably."),
    ]
    y = 920
    for i, (label, desc) in enumerate(features):
        feature_dot(draw, (col_x + 16, y + 32), i, radius=16)
        draw.text((col_x + 70, y - 4), label, font=body_font, fill=INK_0)
        draw.text((col_x + 70, y + 76), desc, font=label_font, fill=INK_200)
        y += 200

    # Footer tagline — sits at the bottom-right with comfortable margin
    # above the page edge.
    tagline_font = font(INTER, 44)
    draw.text((col_x, h - 100),
              "Imagine Something Else.",
              font=tagline_font, fill=INK_300)

    return canvas


# ---------------------------------------------------------------------
# Image 3 — Portrait hero (1440x2160)
# ---------------------------------------------------------------------

def make_portrait_hero(icon: Image.Image) -> Image.Image:
    w, h = 1440, 2160
    canvas = base_canvas((w, h), glow_offset=(0.50, 0.18))
    draw = ImageDraw.Draw(canvas)

    # Hex motif behind icon
    hex_layer = hex_motif_layer(
        (w, h), w // 2, 600,
        radius=620, color=CYAN, alpha=22, stroke=5
    )
    canvas = Image.alpha_composite(canvas, hex_layer)
    draw = ImageDraw.Draw(canvas)

    # Eyebrow
    eyebrow_font = font(JETBRAINS_MONO, 36)
    draw_meta_label(draw, (w // 2, 130),
                    "626labs llc  ·  windows v2.3.0",
                    eyebrow_font, CYAN, anchor="mt")

    # Hero icon (top half)
    composite_icon(canvas, icon, (w // 2, 600), target_size=820)

    # Wordmark
    title_font = font(SPACE_GROTESK, 124)
    draw_text_centered(draw, (w // 2, 1180),
                       "Sanduhr für Claude", title_font, INK_0)

    # Gradient hairline
    gradient_underline(canvas, (w // 2 - 240, 1265), 480, height=5)

    # Tagline
    tag_font = font(INTER, 54)
    draw_text_centered(draw, (w // 2, 1325),
                       "An hourglass for your Claude.ai usage.",
                       tag_font, INK_200)

    # Feature stack — vertical, centered
    body_font = font(INTER, 52)
    label_font = font(INTER, 38)
    features = [
        ("Daily Routines tier",
         "claude.ai/code daily run-quota."),
        ("Local CC token-burn delta",
         "Live, sourced from session JSONLs."),
        ("Drag-and-drop tier cards",
         "Reorder, hide, save automatically."),
        ("Win11 Mica chrome",
         "Frameless, always-on-top, ~3 MB RAM."),
    ]
    y = 1450
    left = 180
    for i, (label, desc) in enumerate(features):
        feature_dot(draw, (left + 14, y + 28), i, radius=14)
        draw.text((left + 60, y - 4), label, font=body_font, fill=INK_0)
        draw.text((left + 60, y + 64), desc, font=label_font, fill=INK_200)
        y += 142

    # Bottom tagline — sits below the feature stack with breathing
    # room. y of last feature description ends near 2086; tagline at
    # h - 50 lands at 2110 with ~24px of clearance and margin below.
    tagline_font = font(INTER, 38)
    draw_text_centered(draw, (w // 2, h - 50),
                       "Imagine Something Else.",
                       tagline_font, INK_300)

    return canvas


# ---------------------------------------------------------------------
# Driver
# ---------------------------------------------------------------------

def main() -> int:
    if not SOURCE_ICON.exists():
        raise SystemExit(f"Source icon missing: {SOURCE_ICON}")
    for f in (SPACE_GROTESK, INTER, JETBRAINS_MONO):
        if not f.exists():
            raise SystemExit(f"Brand font missing: {f}")

    icon = Image.open(SOURCE_ICON).convert("RGBA")

    images = [
        ("Sanduhr-Square-2160.png", make_square_hero(icon)),
        ("Sanduhr-Hero-3840x2160.png", make_landscape_hero(icon)),
        ("Sanduhr-Portrait-1440x2160.png", make_portrait_hero(icon)),
    ]
    for name, img in images:
        out_path = OUT / name
        img.convert("RGB").save(out_path, "PNG", optimize=True)
        size_mb = out_path.stat().st_size / 1_000_000
        print(f"wrote {name}  ({img.size[0]}x{img.size[1]}, {size_mb:.1f} MB)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
