"""
Sanduhr Windows icon generator.
Ports mac/icon/generate.swift (CoreGraphics) to Pillow.

Brand palette:
  navyDeep  #0f182b   navyLight  #2a3a5c   navyRim  #1b2a4a
  cyan       #3bb4d9   paleCyan   #7ae0f5   magenta  #e13aa0
  magPink    #ff5eb6   magDark    #9a2a78

Canvas: 1024×1024 RGBA
Outputs: source.png  +  Sanduhr.ico (9 resolutions)
"""

import math
import os
import struct

from PIL import Image, ImageChops, ImageDraw, ImageFilter

# ---------------------------------------------------------------------------
# Palette
# ---------------------------------------------------------------------------
NAVY_DEEP  = (0x0f, 0x18, 0x2b, 255)
NAVY_LIGHT = (0x2a, 0x3a, 0x5c, 255)
NAVY_RIM   = (0x1b, 0x2a, 0x4a, 255)
CYAN       = (0x3b, 0xb4, 0xd9, 255)
PALE_CYAN  = (0x7a, 0xe0, 0xf5, 255)
MAGENTA    = (0xe1, 0x3a, 0xa0, 255)
MAG_PINK   = (0xff, 0x5e, 0xb6, 255)
MAG_DARK   = (0x9a, 0x2a, 0x78, 255)

SIZE = 1024


def lerp_color(c1, c2, t):
    return tuple(int(c1[i] + (c2[i] - c1[i]) * t) for i in range(4))


def gradient_stops(stops, t):
    """Interpolate between colour stops. stops = [(t0, c0), (t1, c1), ...]"""
    for i in range(len(stops) - 1):
        t0, c0 = stops[i]
        t1, c1 = stops[i + 1]
        if t0 <= t <= t1:
            local = (t - t0) / (t1 - t0) if t1 != t0 else 0
            return lerp_color(c0, c1, local)
    return stops[-1][1]


# ---------------------------------------------------------------------------
# 1. Background (radial gradient, rounded-rect clipped)
# ---------------------------------------------------------------------------
def make_background():
    cx, cy = SIZE // 2, SIZE // 2 + 60  # centre shifted +60 y (navyLight at centre)
    max_r = math.hypot(SIZE, SIZE) * 0.6

    try:
        import numpy as np
        ys, xs = np.mgrid[0:SIZE, 0:SIZE]
        r = np.hypot(xs - cx, ys - cy) / max_r
        t = np.clip(r, 0.0, 1.0)
        nl = np.array(NAVY_LIGHT[:3], dtype=np.float32)
        nd = np.array(NAVY_DEEP[:3],  dtype=np.float32)
        rgb = (nl[None, None] + (nd - nl)[None, None] * t[:, :, None]).astype(np.uint8)
        alpha = np.full((SIZE, SIZE, 1), 255, dtype=np.uint8)
        data = np.concatenate([rgb, alpha], axis=2)
        img = Image.fromarray(data, "RGBA")
    except ImportError:
        img = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
        px = img.load()
        for y in range(SIZE):
            for x in range(SIZE):
                r = math.hypot(x - cx, y - cy) / max_r
                t_val = min(r, 1.0)
                px[x, y] = lerp_color(NAVY_LIGHT, NAVY_DEEP, t_val)

    # Clip to rounded-rect (Big Sur template: 100–924 with radius 185)
    mask = Image.new("L", (SIZE, SIZE), 0)
    md = ImageDraw.Draw(mask)
    md.rounded_rectangle([100, 100, 924, 924], radius=185, fill=255)

    out = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    out.paste(img, mask=mask)
    return out, mask


# ---------------------------------------------------------------------------
# 2. Hex outline (faint, 626 Labs nod)
# ---------------------------------------------------------------------------
def draw_hex(draw):
    cx, cy = SIZE // 2, SIZE // 2
    r = 340
    pts = []
    for i in range(6):
        angle = math.radians(60 * i - 30)
        pts.append((cx + r * math.cos(angle), cy + r * math.sin(angle)))
    cyan_faint = (CYAN[0], CYAN[1], CYAN[2], int(255 * 0.10))
    draw.polygon(pts, outline=cyan_faint, width=3)


# ---------------------------------------------------------------------------
# 3. Top sheen (7% white → transparent, y 100→500)
# ---------------------------------------------------------------------------
def make_sheen(mask):
    y_top, y_bot = 100, 500
    h = y_bot - y_top
    stops = [
        (0.0, (255, 255, 255, int(255 * 0.07))),
        (1.0, (255, 255, 255, 0)),
    ]
    grad = vertical_gradient_image(SIZE, h, stops)
    sheen = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    sheen.paste(grad, (0, y_top))
    # Clip to rounded-rect
    out = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    out.paste(sheen, mask=mask)
    return out


# ---------------------------------------------------------------------------
# Bezier helpers
# ---------------------------------------------------------------------------
def quad_bezier_pts(p0, p1, p2, n=32):
    """Return n+1 points along a quadratic bezier p0→p2 with control p1."""
    pts = []
    for i in range(n + 1):
        t = i / n
        x = (1 - t) ** 2 * p0[0] + 2 * (1 - t) * t * p1[0] + t ** 2 * p2[0]
        y = (1 - t) ** 2 * p0[1] + 2 * (1 - t) * t * p1[1] + t ** 2 * p2[1]
        pts.append((x, y))
    return pts


def hourglass_path(cx, cy, hW, hH, nW, n=32):
    """
    Return polygon points for the hourglass outline.

    Swift uses y-up; Pillow uses y-down.
    Flipping: Swift cy+hH → Pillow cy-hH  (bottom of Swift = top in Pillow coords)
              Swift cy-hH → Pillow cy+hH  (top of Swift = bottom in Pillow coords)

    The Swift path (y-up):
      Start: bottom-left (cx-hW, cy-hH)   [Pillow: (cx-hW, cy+hH)]
      Curve → neck-left  (cx-nW, cy)       control (cx-70, cy-70) [Pillow ctrl: (cx-70, cy+70)]
      Curve → top-left   (cx-hW, cy+hH)   [Pillow: (cx-hW, cy-hH)]  control (cx-70, cy+70) [Pillow: (cx-70, cy-70)]
      Line  → top-right  (cx+hW, cy+hH)   [Pillow: (cx+hW, cy-hH)]
      Curve → neck-right (cx+nW, cy)       control (cx+70, cy-70) [Pillow: (cx+70, cy-70)]  -- wait, mirror
      Curve → bottom-right (cx+hW, cy-hH) [Pillow: (cx+hW, cy+hH)]  control (cx+70, cy+70) [Pillow: (cx+70, cy+70)]
      Line  → start
    """
    pts = []

    # bottom-left → neck-left
    pts += quad_bezier_pts(
        (cx - hW, cy + hH),          # bottom-left
        (cx - 70, cy + 70),          # control
        (cx - nW, cy),               # neck-left
        n
    )
    # neck-left → top-left
    pts += quad_bezier_pts(
        (cx - nW, cy),
        (cx - 70, cy - 70),
        (cx - hW, cy - hH),
        n
    )
    # top-left → top-right
    pts.append((cx + hW, cy - hH))
    # top-right → neck-right
    pts += quad_bezier_pts(
        (cx + hW, cy - hH),
        (cx + 70, cy - 70),
        (cx + nW, cy),
        n
    )
    # neck-right → bottom-right
    pts += quad_bezier_pts(
        (cx + nW, cy),
        (cx + 70, cy + 70),
        (cx + hW, cy + hH),
        n
    )
    # close
    pts.append((cx - hW, cy + hH))

    return pts


# ---------------------------------------------------------------------------
# 4. Hourglass glow (cyan fill + blur)
# ---------------------------------------------------------------------------
def make_glow(cx, cy, hW, hH, nW):
    glow_base = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    d = ImageDraw.Draw(glow_base)
    pts = hourglass_path(cx, cy, hW, hH, nW)
    cyan_glow = (CYAN[0], CYAN[1], CYAN[2], int(255 * 0.12))
    d.polygon(pts, fill=cyan_glow)
    glow_blurred = glow_base.filter(ImageFilter.GaussianBlur(radius=23))

    # Shadow layer: 60% cyan blur
    shadow = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    ds = ImageDraw.Draw(shadow)
    cyan_shadow = (CYAN[0], CYAN[1], CYAN[2], int(255 * 0.60))
    ds.polygon(pts, fill=cyan_shadow)
    shadow_blurred = shadow.filter(ImageFilter.GaussianBlur(radius=46))
    # Reduce shadow to ~12% overall
    shadow_blurred.putalpha(
        shadow_blurred.getchannel("A").point(lambda v: int(v * 0.12))
    )

    combined = Image.alpha_composite(shadow_blurred, glow_blurred)
    return combined


# ---------------------------------------------------------------------------
# Linear gradient helper (vertical, RGBA)
# ---------------------------------------------------------------------------
def vertical_gradient_image(width, height, stops):
    """
    stops: list of (t, color_rgba) sorted by t in [0, 1].
    Returns RGBA Image.
    """
    # Build a 1-pixel-wide column then resize — fast regardless of numpy
    col = Image.new("RGBA", (1, height))
    px = col.load()
    for y in range(height):
        t = y / max(height - 1, 1)
        px[0, y] = gradient_stops(stops, t)
    return col.resize((width, height), Image.NEAREST)


# ---------------------------------------------------------------------------
# 5 & 6. Sand fills (bottom bulb magenta, top bulb cyan) — clipped to hourglass
# ---------------------------------------------------------------------------
def make_sand(cx, cy, hW, hH, nW):
    pts = hourglass_path(cx, cy, hW, hH, nW)

    # ---- Bottom bulb (magenta heap) ----
    # In Pillow y-down: bottom of hourglass is cy+hH, neck is cy.
    # Gradient from y = cy + hH*0.30 ... cy+hH  (top to bottom)
    # Swift: start y = cy + hH*0.30 → Pillow: cy - hH*0.30 ... wait
    # Swift (y-up): sand fills bottom bulb from cy-hH (Swift bottom) to cy+hH*0.30 (Swift near-neck)
    # In Pillow (y-down): bottom of bulb = cy+hH, near-neck = cy + hH*0.30
    # Gradient goes top→bottom in Pillow: from cy+hH*0.30 down to cy+hH
    bot_top_y = int(cy + hH * 0.30)   # where magenta starts
    bot_bot_y = int(cy + hH)           # bottom rim

    mag_stops = [
        (0.0, (MAG_PINK[0],  MAG_PINK[1],  MAG_PINK[2],  255)),
        (0.6, (MAGENTA[0],   MAGENTA[1],   MAGENTA[2],   255)),
        (1.0, (MAG_DARK[0],  MAG_DARK[1],  MAG_DARK[2],  255)),
    ]
    bot_grad = vertical_gradient_image(SIZE, SIZE, mag_stops)
    # Scale: map y range [bot_top_y, bot_bot_y] → [0, 1]
    # Rebuild properly-positioned gradient
    bot_h = bot_bot_y - bot_top_y or 1
    bot_grad = vertical_gradient_image(SIZE, bot_h, mag_stops)
    bot_full = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    bot_full.paste(bot_grad, (0, bot_top_y))

    # Mask: hourglass shape intersected with lower half (y > cy)
    hg_mask = Image.new("L", (SIZE, SIZE), 0)
    md = ImageDraw.Draw(hg_mask)
    md.polygon(pts, fill=255)
    lower_mask = Image.new("L", (SIZE, SIZE), 0)
    ImageDraw.Draw(lower_mask).rectangle([0, cy, SIZE, SIZE], fill=255)
    combined_mask = ImageChops.multiply(hg_mask, lower_mask)

    bot_sand = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    bot_sand.paste(bot_full, mask=combined_mask)

    # ---- Top bulb (cyan sand) ----
    # Swift: fills top bulb from cy (neck) to cy+hH*0.40 above neck (y-up)
    # Pillow: neck at cy, sand fills from cy UP to cy - hH*0.40
    top_bot_y = int(cy)                   # neck level in Pillow
    top_top_y = int(cy - hH * 0.40)       # how far up

    top_h = top_bot_y - top_top_y or 1
    top_stops = [
        (0.0, (CYAN[0],      CYAN[1],      CYAN[2],      255)),
        (1.0, (PALE_CYAN[0], PALE_CYAN[1], PALE_CYAN[2], 255)),
    ]
    top_grad = vertical_gradient_image(SIZE, top_h, top_stops)
    top_full = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    top_full.paste(top_grad, (0, top_top_y))

    # Mask: hourglass shape intersected with upper half (y < cy)
    upper_mask = Image.new("L", (SIZE, SIZE), 0)
    ImageDraw.Draw(upper_mask).rectangle([0, 0, SIZE, cy], fill=255)
    top_combined = ImageChops.multiply(hg_mask, upper_mask)

    top_sand = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    top_sand.paste(top_full, mask=top_combined)

    return bot_sand, top_sand


# ---------------------------------------------------------------------------
# 7. Neck stream
# ---------------------------------------------------------------------------
def make_stream(cx, cy, nW):
    stream = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    x0, x1 = cx - nW // 2, cx + nW // 2
    stream_top    = cy - 40
    stream_bottom = cy + 40
    stream_h = stream_bottom - stream_top

    stops = [
        (0.0, (PALE_CYAN[0], PALE_CYAN[1], PALE_CYAN[2], 230)),
        (0.5, (CYAN[0],      CYAN[1],      CYAN[2],      230)),
        (1.0, (MAGENTA[0],   MAGENTA[1],   MAGENTA[2],   230)),
    ]
    grad = vertical_gradient_image(x1 - x0, stream_h, stops)
    stream.paste(grad, (x0, stream_top))
    return stream


# ---------------------------------------------------------------------------
# 8. Hourglass frame outline
# ---------------------------------------------------------------------------
def draw_frame(draw, cx, cy, hW, hH, nW):
    pts = hourglass_path(cx, cy, hW, hH, nW)
    pale_cyan_stroke = (PALE_CYAN[0], PALE_CYAN[1], PALE_CYAN[2], 255)
    draw.line(pts + [pts[0]], fill=pale_cyan_stroke, width=16, joint="curve")


# ---------------------------------------------------------------------------
# 9. Rim caps
# ---------------------------------------------------------------------------
def draw_rim_caps(draw, cx, cy, hW, hH):
    cap_w  = (hW + 36) * 2        # 372 * 2 = 372px wide centred
    cap_h  = 26
    x0     = cx - hW - 36
    x1     = cx + hW + 36

    # Top cap (top edge of hourglass)
    top_y  = cy - hH
    draw.rectangle([x0, top_y, x1, top_y + cap_h], fill=NAVY_RIM)
    cyan_stripe = (CYAN[0], CYAN[1], CYAN[2], int(255 * 0.70))
    draw.rectangle([x0, top_y, x1, top_y + 2], fill=cyan_stripe)

    # Bottom cap
    bot_y  = cy + hH - cap_h
    draw.rectangle([x0, bot_y, x1, cy + hH], fill=NAVY_RIM)
    draw.rectangle([x0, bot_y, x1, bot_y + 2], fill=cyan_stripe)


# ---------------------------------------------------------------------------
# Main composition
# ---------------------------------------------------------------------------
def generate(out_dir=None):
    if out_dir is None:
        out_dir = os.path.dirname(os.path.abspath(__file__))

    cx, cy = SIZE // 2, SIZE // 2
    hW, hH, nW = 150, 230, 20

    print("Building background...")
    bg, bg_mask = make_background()

    print("Building sheen...")
    sheen = make_sheen(bg_mask)

    print("Building hex overlay...")
    hex_layer = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    draw_hex(ImageDraw.Draw(hex_layer))

    print("Building glow...")
    glow = make_glow(cx, cy, hW, hH, nW)

    print("Building sand fills...")
    bot_sand, top_sand = make_sand(cx, cy, hW, hH, nW)

    print("Building stream...")
    stream = make_stream(cx, cy, nW)

    print("Compositing layers...")
    canvas = bg.copy()
    canvas = Image.alpha_composite(canvas, sheen)
    canvas = Image.alpha_composite(canvas, hex_layer)
    canvas = Image.alpha_composite(canvas, glow)
    canvas = Image.alpha_composite(canvas, bot_sand)
    canvas = Image.alpha_composite(canvas, top_sand)
    canvas = Image.alpha_composite(canvas, stream)

    # Frame outline and rim caps on top
    frame_layer = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    fd = ImageDraw.Draw(frame_layer)
    draw_frame(fd, cx, cy, hW, hH, nW)
    draw_rim_caps(fd, cx, cy, hW, hH)
    canvas = Image.alpha_composite(canvas, frame_layer)

    png_path = os.path.join(out_dir, "source.png")
    ico_path = os.path.join(out_dir, "Sanduhr.ico")

    print(f"Saving {png_path} ...")
    canvas.save(png_path, format="PNG")

    print(f"Saving {ico_path} ...")
    canvas.save(
        ico_path,
        format="ICO",
        sizes=[(s, s) for s in (16, 20, 24, 32, 40, 48, 64, 128, 256)],
    )

    print("Done.")
    return png_path, ico_path


if __name__ == "__main__":
    generate()
