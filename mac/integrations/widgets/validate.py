#!/usr/bin/env python3
# Vendored, unchanged below this header, from 626Labs-LLC/sanduhr-widgets at commit 61b4247:
#   tools/validate.py  (https://github.com/626Labs-LLC/sanduhr-widgets/blob/61b4247/tools/validate.py)
# It imports only the standard library, so no other module came with it. The Swift port
# (mac/Sources/Sanduhr/Widgets/PackValidator.swift) matches its messages; mac/integrations/tests/
# test_widget_validate.py runs it on the same fixtures as the Swift tests. To update, re-copy the
# file at a new commit, regenerate Tests/SanduhrTests/Fixtures/widget-packs/python-expected.tsv and
# bump the commit here and in that folder's README.
"""Validate Sanduhr widget packs against docs/FORMAT.md (format v1, sprite.json v1 and v2).

Python 3, standard library only. Schemas are checked by hand-written logic so CI needs no pip.

    python tools/validate.py                 # every pack under packs/
    python tools/validate.py packs/hourglass # one pack
    python tools/validate.py path/to/packs   # a folder of packs
    python tools/validate.py --strict        # theme design-rule warnings fail too

One line per finding on stdout: `ERROR <pack>: <message>` or `WARN  <pack>: <message>`.
Exit status is 1 when any error was found (or any warning under --strict), else 0.
"""
import argparse
import json
import math
import os
import re
import struct
import sys
import zlib
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent

# ---- format constants (docs/FORMAT.md) ----------------------------------------------
KINDS = ("theme", "palette", "effects", "font", "sprite")
LICENSES = {
    # SPDX id -> phrases of which at least one must appear in LICENSE (whitespace-collapsed, lowercase)
    "MIT": ("permission is hereby granted",),
    "CC-BY-4.0": ("attribution 4.0 international",),
    "CC0-1.0": ("cc0 1.0 universal",),
    "Apache-2.0": ("apache license",),
    "OFL-1.1": ("sil open font license",),
    "BSD-3-Clause": ("redistribution and use in source and binary forms",),
    "ISC": ("permission to use, copy, modify, and/or distribute",),
}
FONT_LICENSES = ("OFL-1.1", "Apache-2.0")
ALLOWED_EXT = (".json", ".png", ".ttf", ".otf")
BANNED_EXT = (
    ".exe", ".dll", ".bat", ".cmd", ".ps1", ".sh", ".js", ".mjs", ".py", ".html", ".htm",
    ".svg", ".jar", ".msi", ".lnk", ".vbs", ".com", ".scr",
)
BANNED_SNIPPETS = (b"<script", b"javascript:", b"<iframe", b"<!doctype html")
EXEC_SIGNATURES = ((b"MZ", "a Windows executable"), (b"\x7fELF", "an ELF executable"),
                   (b"#!", "a script (shebang)"))
TM_SUBSTRINGS = ("claude", "anthropic")
TM_WORDS = ("opus", "sonnet", "haiku", "fable")

MAX_IMAGE_BYTES = 2 * 1024 * 1024
MAX_IMAGE_SIDE = 4096
MAX_FONT_BYTES = 5 * 1024 * 1024
MAX_PACK_BYTES = 10 * 1024 * 1024
MAX_FILES = 64
MAX_LICENSE_BYTES = 100 * 1024

# These patterns carry no ^ or $ anchors and are applied with fullmatch: `$` also matches before a
# trailing newline, so `re.match("^x$", "x\n")` succeeds.
ID_RE = re.compile(r"[a-z][a-z0-9]*(-[a-z0-9]+)*")
SEMVER_RE = re.compile(r"(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)")
HEX_RE = re.compile(r"#[0-9a-fA-F]{6}")
PATH_SEG_RE = re.compile(r"[A-Za-z0-9][A-Za-z0-9._-]*")
PRESET_NAME_RE = re.compile(r"[a-z0-9][a-z0-9-]{1,23}")
ANIM_NAME_RE = re.compile(r"[a-z][a-z0-9_]{1,23}")
THEME_FILE_RE = re.compile(r"themes/[a-z0-9]+(-[a-z0-9]+)*\.json")
CTRL_RE = re.compile(r"[\x00-\x1f\x7f]")

MANIFEST_REQUIRED = ("schema_version", "id", "name", "version", "author", "license", "kind",
                     "sanduhr_min", "description", "files")
MANIFEST_OPTIONAL = ("preview",)

THEME_COLOR_FIELDS = ("bg", "glass", "glass_on_mica", "title_bg", "border", "text",
                      "text_secondary", "text_dim", "text_muted", "accent", "bar_bg",
                      "footer_bg", "pace_marker", "sparkline")
THEME_OPTIONAL_KEYS = ("glass_alpha", "border_alpha", "border_tint", "accent_bloom",
                       "inner_highlight", "card_corner_radius", "breath_period_ms",
                       "opts_out_of_mica", "bg_grid", "monospace_font", "monospace_fallback")
THEME_MAX_NAME = 24

REQUIRED_ANIMATIONS = ("walk", "sit", "nap", "pop", "cheer", "worry", "focus", "sleep", "celebrate")
MOOD_KEYS = ("content", "focused", "worried", "exhausted", "celebrate")
TRIGGER_KEYS = ("needs_you", "job_passed", "near_limit", "idle")
ROAM_EDGES = ("bottom", "left", "right")
SPRITE_SCHEMAS = (1, 2)            # sprite.json schema_version; 2 adds .json pixel sheets
PIXEL_KEYS = ("palette", "frame_w", "frame_h", "frames")
PIXEL_CHARS = ".0123456789abcdef"  # "." is transparent, the rest index the palette
PIXEL_MAX_COLORS = 16
PIXEL_FRAME_MIN, PIXEL_FRAME_MAX = 8, 32
PIXEL_MAX_FRAMES = 256
PIXEL_MAX_BYTES = 2 * 1024 * 1024  # same ceiling as a PNG sheet
PIXEL_MAX_ERRORS = 12              # per sheet, so one bad sheet does not flood the report
EFFECT_TYPES = ("glow", "sweep", "shimmer", "pulse")


class Report:
    def __init__(self, label):
        self.label = label
        self.errors = []
        self.warnings = []

    def err(self, msg):
        self.errors.append(msg)

    def warn(self, msg):
        self.warnings.append(msg)

    def lines(self):
        out = ["ERROR %s: %s" % (self.label, m) for m in self.errors]
        out += ["WARN  %s: %s" % (self.label, m) for m in self.warnings]
        return out


# ---- small helpers -----------------------------------------------------------------
def is_int(v):
    return isinstance(v, int) and not isinstance(v, bool)


def is_num(v):
    return isinstance(v, (int, float)) and not isinstance(v, bool) and math.isfinite(v)


def _no_dupes(pairs):
    seen = {}
    for k, v in pairs:
        if k in seen:
            raise ValueError("duplicate key %r" % k)
        seen[k] = v
    return seen


def load_json(path, rep, rel):
    """Parse a UTF-8 JSON file. Returns the value, or None after recording an error."""
    try:
        raw = path.read_bytes()
    except OSError as e:
        rep.err("%s: cannot read (%s)" % (rel, e))
        return None
    if raw.startswith(b"\xef\xbb\xbf"):
        rep.err("%s: has a UTF-8 byte-order mark; JSON must be plain UTF-8" % rel)
        return None
    try:
        text = raw.decode("utf-8")
    except UnicodeDecodeError:
        rep.err("%s: not valid UTF-8" % rel)
        return None
    try:
        value = json.loads(text, object_pairs_hook=_no_dupes)
    except (ValueError, RecursionError) as e:
        rep.err("%s: invalid JSON (%s)" % (rel, e))
        return None
    if value is None:
        # None means "no usable file" to every caller, so a literal null must be an error here
        rep.err("%s: must be a JSON object, got null" % rel)
        return None
    return value


def iter_strings(obj):
    if isinstance(obj, str):
        yield obj
    elif isinstance(obj, dict):
        for k, v in obj.items():
            yield k
            yield from iter_strings(v)
    elif isinstance(obj, list):
        for v in obj:
            yield from iter_strings(v)


def find_trademark(text):
    lower = text.lower()
    for w in TM_SUBSTRINGS:
        if w in lower:
            return w
    for w in TM_WORDS:
        if re.search(r"(?<![a-z0-9])" + w + r"(?![a-z0-9])", lower):
            return w
    return None


def check_trademark(rep, where, text):
    hit = find_trademark(text)
    if hit:
        rep.err("trademark: %s contains %r. Third-party marks are not allowed in packs "
                "(no Anthropic or Claude marks, no model names as branding); see FORMAT.md rule 6"
                % (where, hit))


def safe_rel_path(p):
    """True for a relative POSIX path with safe segments."""
    if not isinstance(p, str) or not p or len(p) > 128:
        return False
    if p.startswith("/") or "\\" in p or ":" in p:
        return False
    return all(PATH_SEG_RE.fullmatch(seg) for seg in p.split("/"))


# ---- PNG -------------------------------------------------------------------------------
PNG_SIG = b"\x89PNG\r\n\x1a\n"
_CHANNELS = {0: 1, 2: 3, 3: 1, 4: 2, 6: 4}
_VALID_DEPTHS = {0: (1, 2, 4, 8, 16), 2: (8, 16), 3: (1, 2, 4, 8), 4: (8, 16), 6: (8, 16)}


def parse_png(data):
    """Return (width, height, details) of a well-formed PNG, or raise ValueError with the reason."""
    if len(data) < 8 or data[:8] != PNG_SIG:
        raise ValueError("not a PNG (bad signature)")
    pos = 8
    first = True
    idat = bytearray()
    width = height = depth = ctype = interlace = None
    seen_iend = False
    while pos < len(data):
        if pos + 8 > len(data):
            raise ValueError("truncated chunk header")
        length, tag = struct.unpack(">I4s", data[pos:pos + 8])
        end = pos + 8 + length + 4
        if end > len(data):
            raise ValueError("truncated %s chunk" % tag.decode("latin-1"))
        body = data[pos + 8:pos + 8 + length]
        (crc,) = struct.unpack(">I", data[end - 4:end])
        if zlib.crc32(tag + body) & 0xFFFFFFFF != crc:
            raise ValueError("bad CRC in %s chunk" % tag.decode("latin-1"))
        if first:
            if tag != b"IHDR" or length != 13:
                raise ValueError("first chunk must be a 13-byte IHDR")
            width, height, depth, ctype, comp, filt, interlace = struct.unpack(">IIBBBBB", body)
            first = False
        elif tag == b"IHDR":
            raise ValueError("duplicate IHDR")
        elif tag == b"IDAT":
            idat += body
        elif tag == b"IEND":
            seen_iend = True
            pos = end
            break
        pos = end
    if first:
        raise ValueError("no IHDR chunk")
    if not seen_iend:
        raise ValueError("missing IEND chunk")
    if pos != len(data):
        raise ValueError("trailing data after IEND")
    if width == 0 or height == 0:
        raise ValueError("zero-sized image")
    if ctype not in _CHANNELS or depth not in _VALID_DEPTHS[ctype]:
        raise ValueError("invalid color type %s / bit depth %s" % (ctype, depth))
    if comp != 0 or filt != 0 or interlace not in (0, 1):
        raise ValueError("unsupported compression, filter or interlace method")
    if not idat:
        raise ValueError("no IDAT chunk")
    return width, height, (depth, ctype, interlace, bytes(idat))


def check_png(rep, rel, data):
    """Full PNG check. Returns (w, h) or None after recording errors."""
    if len(data) > MAX_IMAGE_BYTES:
        rep.err("%s: image is %.1f MB, limit is 2 MB" % (rel, len(data) / 1048576))
        return None
    try:
        w, h, extra = parse_png(data)
    except ValueError as e:
        rep.err("%s: %s" % (rel, e))
        return None
    if w > MAX_IMAGE_SIDE or h > MAX_IMAGE_SIDE:
        rep.err("%s: image is %dx%d, limit is %d pixels on a side" % (rel, w, h, MAX_IMAGE_SIDE))
        return None
    depth, ctype, interlace, idat = extra
    if interlace == 0:
        bits = depth * _CHANNELS[ctype]
        expected = h * (1 + (w * bits + 7) // 8)
        try:
            raw = zlib.decompressobj().decompress(idat, expected + 1)
        except zlib.error as e:
            rep.err("%s: corrupt image data (%s)" % (rel, e))
            return None
        if len(raw) != expected:
            rep.err("%s: image data is %d bytes, expected %d for %dx%d" % (rel, len(raw), expected, w, h))
            return None
    return w, h


def check_font(rep, rel, data):
    if len(data) > MAX_FONT_BYTES:
        rep.err("%s: font is %.1f MB, limit is 5 MB" % (rel, len(data) / 1048576))
        return
    magic = data[:4]
    if magic == b"ttcf":
        rep.err("%s: font collections (.ttc) are not allowed" % rel)
    elif magic not in (b"\x00\x01\x00\x00", b"true", b"OTTO"):
        rep.err("%s: not a TrueType/OpenType font (bad magic number)" % rel)


# ---- theme lint (port of Sanduhr.Core ThemeLint.cs) ---------------------------------------
def _rgb(hexstr):
    return tuple(int(hexstr[i:i + 2], 16) / 255.0 for i in (1, 3, 5))


def _lin(v):
    return v / 12.92 if v <= 0.03928 else ((v + 0.055) / 1.055) ** 2.4


def luminance(c):
    return 0.2126 * _lin(c[0]) + 0.7152 * _lin(c[1]) + 0.0722 * _lin(c[2])


def contrast(l1, l2):
    hi, lo = max(l1, l2), min(l1, l2)
    return (hi + 0.05) / (lo + 0.05)


def saturation(c):
    mx, mn = max(c), min(c)
    return 0.0 if mx <= 0 else (mx - mn) / mx


def hue(c):
    r, g, b = c
    mx, mn = max(c), min(c)
    d = mx - mn
    if d <= 0:
        return 0.0
    if mx == r:
        h = math.fmod((g - b) / d, 6)
    elif mx == g:
        h = (b - r) / d + 2
    else:
        h = (r - g) / d + 4
    h *= 60
    return h + 360 if h < 0 else h


def hue_distance(a, b):
    if saturation(a) < 0.15 or saturation(b) < 0.15:
        return None
    d = abs(hue(a) - hue(b)) % 360
    return 360 - d if d > 180 else d


def lint_theme(data):
    """Return (errors, warnings), each a list of strings, mirroring ThemeLint.Lint."""
    errs, warns = [], []
    if not isinstance(data, dict):
        return ["theme must be a JSON object"], []
    allowed = {"name", *THEME_COLOR_FIELDS, *THEME_OPTIONAL_KEYS}
    for k in data:
        if k not in allowed:
            errs.append("unknown key %r (catalog themes allow only the documented fields)" % k)

    name = data.get("name")
    if not isinstance(name, str):
        errs.append("name is required (a string of 1 to %d characters)" % THEME_MAX_NAME)
    elif not name.strip():
        errs.append("name must not be empty")
    elif len(name) > THEME_MAX_NAME:
        errs.append("name is %d characters; the limit is %d" % (len(name), THEME_MAX_NAME))

    colors = {}
    for f in THEME_COLOR_FIELDS:
        v = data.get(f)
        if f not in data:
            errs.append("%s is required (a #rrggbb color)" % f)
        elif not isinstance(v, str) or not HEX_RE.fullmatch(v):
            errs.append("%s must be a #rrggbb color (six hex digits), got %r" % (f, v))
        else:
            colors[f] = _rgb(v)

    def check_range(obj, key, lo, hi, label=None):
        label = label or key
        if key not in obj:
            return None
        v = obj[key]
        if not is_num(v):
            errs.append("%s must be a number between %g and %g, got %r" % (label, lo, hi, v))
            return None
        if v < lo or v > hi:
            errs.append("%s must be between %g and %g, got %g" % (label, lo, hi, v))
            return None
        return v

    def check_opt_hex(obj, key, label=None):
        label = label or key
        if key not in obj:
            return
        v = obj[key]
        if v is None:
            return
        if not isinstance(v, str) or not HEX_RE.fullmatch(v):
            errs.append("%s must be a #rrggbb color or null, got %r" % (label, v))

    glass_alpha = check_range(data, "glass_alpha", 0, 1)
    if glass_alpha is None:
        glass_alpha = 0.80
    check_range(data, "border_alpha", 0, 1)
    check_opt_hex(data, "border_tint")
    if "accent_bloom" in data:
        ab = data["accent_bloom"]
        if isinstance(ab, dict):
            check_range(ab, "blur", 0, 20, "accent_bloom.blur")
            check_range(ab, "alpha", 0, 1, "accent_bloom.alpha")
        else:
            errs.append('accent_bloom must be an object {"blur": 3-8, "alpha": 0.25-0.65}')
    if "inner_highlight" in data and data["inner_highlight"] is not None:
        ih = data["inner_highlight"]
        if isinstance(ih, dict):
            if ih.get("color") is None:
                errs.append("inner_highlight.color is required (#rrggbb) or set inner_highlight to null")
            else:
                check_opt_hex(ih, "color", "inner_highlight.color")
            check_range(ih, "alpha", 0, 1, "inner_highlight.alpha")
        else:
            errs.append('inner_highlight must be {"color": "#rrggbb", "alpha": 0.15-0.30} or null')
    check_range(data, "card_corner_radius", 0, 24)
    check_range(data, "breath_period_ms", 500, 20000)
    for key in ("opts_out_of_mica", "bg_grid"):
        if key in data and not isinstance(data[key], bool):
            errs.append("%s must be true or false" % key)
    for key in ("monospace_font", "monospace_fallback"):
        if key in data and not isinstance(data[key], str):
            errs.append("%s must be a string" % key)
    opts_out = data.get("opts_out_of_mica") is True

    if errs:
        return errs, warns

    # design rules (warnings in the app; the catalog treats them as errors under --strict)
    for f in ("bg", "glass", "glass_on_mica"):
        lum = luminance(colors[f])
        if lum > 0.25:
            warns.append("%s is light (luminance %.2f, keep it under 0.25)" % (f, lum))
    alpha = 1.0 if opts_out else min(max(glass_alpha, 0.0), 1.0)
    desk = (0x80 / 255.0,) * 3
    card = tuple(colors["glass_on_mica"][i] * alpha + desk[i] * (1 - alpha) for i in range(3))
    card_l = luminance(card)
    tr = contrast(luminance(colors["text"]), card_l)
    if tr < 4.5:
        warns.append("text reads at %.1f:1 on the card (needs 4.5:1)" % tr)
    sr = contrast(luminance(colors["text_secondary"]), card_l)
    if sr < 3.0:
        warns.append("text_secondary reads at %.1f:1 on the card (needs 3.0:1)" % sr)
    ramp = ("text", "text_secondary", "text_dim", "text_muted")
    for i in range(1, 4):
        if luminance(colors[ramp[i]]) >= luminance(colors[ramp[i - 1]]):
            warns.append("%s is not darker than %s; the text ramp is one hue at decreasing luminance"
                         % (ramp[i], ramp[i - 1]))
    for i in range(1, 4):
        d = hue_distance(colors["text"], colors[ramp[i]])
        if d is not None and d > 30:
            warns.append("%s is a different hue from text (%.0f degrees apart)" % (ramp[i], d))
    green = _rgb("#4ade80")
    marker = colors["pace_marker"]

    def hidden_on(other):
        hd = hue_distance(marker, other)
        return contrast(luminance(marker), luminance(other)) < 1.4 and (hd or 0) < 60

    on_green, on_bar = hidden_on(green), hidden_on(colors["bar_bg"])
    if on_green or on_bar:
        warns.append("pace_marker disappears on the %s"
                     % ("green bar fill" if on_green else "empty bar"))
    sd = hue_distance(colors["accent"], colors["sparkline"])
    if sd is not None and sd > 30:
        warns.append("sparkline is a different hue from accent (%.0f degrees apart)" % sd)
    tint = data.get("border_tint")
    if isinstance(tint, str):
        td = hue_distance(colors["accent"], _rgb(tint))
        if td is not None and td > 30:
            warns.append("border_tint is a different hue from accent (%.0f degrees apart)" % td)
    if opts_out and glass_alpha < 1.0:
        warns.append("opts_out_of_mica is true, so set glass_alpha to 1.0")
    return errs, warns


# ---- per-kind checks ------------------------------------------------------------------------
def check_palettes(rep, data):
    rel = "palettes.json"
    if not isinstance(data, dict):
        rep.err("%s: must be a JSON object" % rel)
        return
    extra = set(data) - {"schema_version", "palettes"}
    if extra:
        rep.err("%s: unknown keys %s" % (rel, sorted(extra)))
    if data.get("schema_version") != 1 or isinstance(data.get("schema_version"), bool):
        rep.err("%s: schema_version must be 1" % rel)
    items = data.get("palettes")
    if not isinstance(items, list) or not 1 <= len(items) <= 64:
        rep.err("%s: palettes must be an array of 1 to 64 entries" % rel)
        return
    seen = set()
    for i, p in enumerate(items):
        where = "%s: palettes[%d]" % (rel, i)
        if not isinstance(p, dict):
            rep.err("%s must be an object" % where)
            continue
        extra = set(p) - {"name", "colors", "mood"}
        if extra:
            rep.err("%s has unknown keys %s" % (where, sorted(extra)))
        n = p.get("name")
        if not isinstance(n, str) or not PRESET_NAME_RE.fullmatch(n):
            rep.err("%s: name must be 2 to 24 lowercase letters, digits or hyphens" % where)
        elif n in seen:
            rep.err("%s: duplicate palette name %r" % (where, n))
        else:
            seen.add(n)
        cols = p.get("colors")
        if not isinstance(cols, list) or not 1 <= len(cols) <= 4:
            rep.err("%s: colors must be 1 to 4 #rrggbb stops" % where)
        else:
            for c in cols:
                if not isinstance(c, str) or not HEX_RE.fullmatch(c):
                    rep.err("%s: %r is not a #rrggbb color" % (where, c))
        if "mood" in p and (not isinstance(p["mood"], str) or len(p["mood"]) > 24):
            rep.err("%s: mood must be a string of at most 24 characters" % where)


def check_effects(rep, data):
    rel = "effects.json"
    if not isinstance(data, dict):
        rep.err("%s: must be a JSON object" % rel)
        return
    extra = set(data) - {"schema_version", "effects"}
    if extra:
        rep.err("%s: unknown keys %s" % (rel, sorted(extra)))
    if data.get("schema_version") != 1 or isinstance(data.get("schema_version"), bool):
        rep.err("%s: schema_version must be 1" % rel)
    items = data.get("effects")
    if not isinstance(items, list) or not 1 <= len(items) <= 64:
        rep.err("%s: effects must be an array of 1 to 64 presets" % rel)
        return
    seen = set()
    for i, e in enumerate(items):
        where = "%s: effects[%d]" % (rel, i)
        if not isinstance(e, dict):
            rep.err("%s must be an object" % where)
            continue
        extra = set(e) - {"name", "type", "intensity", "period_ms", "color"}
        if extra:
            rep.err("%s has unknown keys %s" % (where, sorted(extra)))
        n = e.get("name")
        if not isinstance(n, str) or not PRESET_NAME_RE.fullmatch(n):
            rep.err("%s: name must be 2 to 24 lowercase letters, digits or hyphens" % where)
        elif n in seen:
            rep.err("%s: duplicate preset name %r" % (where, n))
        else:
            seen.add(n)
        if e.get("type") not in EFFECT_TYPES:
            rep.err("%s: type must be one of %s" % (where, ", ".join(EFFECT_TYPES)))
        iv = e.get("intensity")
        if not is_num(iv) or not 0 <= iv <= 1:
            rep.err("%s: intensity must be a number from 0 to 1" % where)
        pm = e.get("period_ms")
        if not is_int(pm) or not 400 <= pm <= 10000:
            rep.err("%s: period_ms must be an integer from 400 to 10000" % where)
        c = e.get("color")
        if not (c == "ink" or (isinstance(c, str) and HEX_RE.fullmatch(c))):
            rep.err('%s: color must be a #rrggbb color or "ink"' % where)


def check_pixel_sheet(rep, rel, data, w, h, max_index, label):
    """Check a sprite v2 pixel sheet (FORMAT 5.5) drawn into a w x h cell. Records errors only."""
    errors = []

    def err(msg):
        errors.append("%s: %s" % (rel, msg))

    if not isinstance(data, dict):
        rep.err("%s: a pixel sheet must be a JSON object with %s" % (rel, ", ".join(PIXEL_KEYS)))
        return
    extra = set(data) - set(PIXEL_KEYS)
    if extra:
        err("unknown keys %s (a pixel sheet has exactly %s)" % (sorted(extra), ", ".join(PIXEL_KEYS)))
    for k in PIXEL_KEYS:
        if k not in data:
            err("missing required key %r" % k)

    pal = data.get("palette")
    ncolors = None
    if "palette" in data:
        if not isinstance(pal, list) or not 1 <= len(pal) <= PIXEL_MAX_COLORS:
            got = " (got %d)" % len(pal) if isinstance(pal, list) else ""
            err("palette must be 1 to %d #rrggbb colors%s" % (PIXEL_MAX_COLORS, got))
        else:
            ncolors = len(pal)
            for i, c in enumerate(pal):
                if not isinstance(c, str) or not HEX_RE.fullmatch(c):
                    err("palette[%d] %r is not a #rrggbb color" % (i, c))
                elif c.lower() == "#ffffff":
                    err("palette[%d] is pure white (%s), which sprites may not use (it burns on "
                        "OLED panels); use an off-white such as #f0f0f0" % (i, c))

    fw, fh = data.get("frame_w"), data.get("frame_h")
    size_ok = all(is_int(v) and PIXEL_FRAME_MIN <= v <= PIXEL_FRAME_MAX for v in (fw, fh))
    if "frame_w" in data and "frame_h" in data:
        if not size_ok:
            err("frame_w and frame_h must be integers from %d to %d, got %r x %r (pixel sheets are "
                "sized for a companion)" % (PIXEL_FRAME_MIN, PIXEL_FRAME_MAX, fw, fh))
        elif (fw, fh) != (w, h):
            err("frame size %dx%d does not match the %s frame size %dx%d" % (fw, fh, label, w, h))
    if not size_ok:
        fw = fh = None

    frames = data.get("frames")
    if "frames" in data:
        if not isinstance(frames, list) or not 1 <= len(frames) <= PIXEL_MAX_FRAMES:
            err("frames must be an array of 1 to %d frames" % PIXEL_MAX_FRAMES)
        else:
            if max_index >= len(frames):
                err("animations use frame index %d but the pixel sheet has only %d frame%s (%s)"
                    % (max_index, len(frames), "" if len(frames) == 1 else "s", label))
            for fi, frame in enumerate(frames):
                if not isinstance(frame, list) or not all(isinstance(r, str) for r in frame):
                    err("frames[%d] must be an array of strings, one per pixel row" % fi)
                    continue
                if fh is not None and len(frame) != fh:
                    err("frames[%d] has %d rows, expected %d (frame_h)" % (fi, len(frame), fh))
                for ri, row in enumerate(frame):
                    if fw is not None and len(row) != fw:
                        err("frames[%d][%d] is %d characters, expected %d (frame_w)"
                            % (fi, ri, len(row), fw))
                    for ci, ch in enumerate(row):
                        if ch not in PIXEL_CHARS:
                            err("frames[%d][%d][%d] is %r; a pixel is '.' (transparent) or 0-9, a-f "
                                "(a palette index, lowercase)" % (fi, ri, ci, ch))
                            break
                        if ch != "." and ncolors is not None and int(ch, 16) >= ncolors:
                            err("frames[%d][%d][%d] is palette index %r (%d) but the palette has "
                                "only %d color%s" % (fi, ri, ci, ch, int(ch, 16), ncolors,
                                                    "" if ncolors == 1 else "s"))
                            break
                if len(errors) > PIXEL_MAX_ERRORS:
                    break

    for m in errors[:PIXEL_MAX_ERRORS]:
        rep.err(m)
    if len(errors) > PIXEL_MAX_ERRORS:
        rep.err("%s: more errors in this pixel sheet are not shown; fix the ones above first" % rel)


def check_sprite(rep, pack_dir, data, files_field, jload=None):
    rel = "sprite.json"
    if not isinstance(data, dict):
        rep.err("%s: must be a JSON object" % rel)
        return
    allowed = {"schema_version", "frame_w", "frame_h", "scale", "fps", "sheet", "stages",
               "animations", "moods", "triggers", "roam"}
    extra = set(data) - allowed
    if extra:
        rep.err("%s: unknown keys %s" % (rel, sorted(extra)))
    sv = data.get("schema_version")
    if not is_int(sv) or sv not in SPRITE_SCHEMAS:
        rep.err("%s: schema_version must be 1 or 2 (2 allows .json pixel sheets)" % rel)
        sv = None

    def sheet_path_error(path):
        """None for an acceptable sheet path, else the reason it is not."""
        if not isinstance(path, str) or not safe_rel_path(path):
            return "must be a safe relative path"
        if path.endswith(".png"):
            return None
        if path.endswith(".json"):
            if path == "sprite.json":
                return "must not be sprite.json itself"
            if sv == 2:
                return None
            return ("%r is a .json pixel sheet, which needs sprite.json schema_version 2 "
                    "(schema_version 1 sheets are .png only)" % path)
        return "must be a .png sheet%s" % (" or a .json pixel sheet" if sv == 2 else "")

    def size_ok(v):
        return is_int(v) and 8 <= v <= 64

    fw, fh = data.get("frame_w"), data.get("frame_h")
    if not size_ok(fw) or not size_ok(fh):
        rep.err("%s: frame_w and frame_h must be integers from 8 to 64" % rel)
        fw = fh = None
    if not is_int(data.get("scale")) or not 1 <= data["scale"] <= 4:
        rep.err("%s: scale must be an integer from 1 to 4" % rel)
    if not is_int(data.get("fps")) or not 1 <= data["fps"] <= 24:
        rep.err("%s: fps must be an integer from 1 to 24" % rel)

    # animations
    anims = data.get("animations")
    max_index = -1
    anim_names = set()
    if not isinstance(anims, dict):
        rep.err("%s: animations must be an object" % rel)
    else:
        if len(anims) > 32:
            rep.err("%s: more than 32 animations" % rel)
        for name, frames in anims.items():
            if not ANIM_NAME_RE.fullmatch(name):
                rep.err("%s: animation name %r must match [a-z][a-z0-9_]{1,23}" % (rel, name))
            anim_names.add(name)
            if (not isinstance(frames, list) or not 1 <= len(frames) <= 64
                    or not all(is_int(i) and i >= 0 for i in frames)):
                rep.err("%s: animation %r must be 1 to 64 non-negative integer frame indexes" % (rel, name))
            else:
                max_index = max(max_index, max(frames))
        for need in REQUIRED_ANIMATIONS:
            if need not in anim_names:
                rep.err("%s: required animation %r is missing" % (rel, need))

    # moods and triggers
    for key, keys in (("moods", MOOD_KEYS), ("triggers", TRIGGER_KEYS)):
        m = data.get(key)
        if not isinstance(m, dict):
            rep.err("%s: %s must be an object with keys %s" % (rel, key, ", ".join(keys)))
            continue
        for k in keys:
            if k not in m:
                rep.err("%s: %s is missing %r" % (rel, key, k))
            elif not isinstance(m[k], str) or m[k] not in anim_names:
                rep.err("%s: %s.%s names animation %r, which is not defined" % (rel, key, k, m[k]))
        for k in m:
            if k not in keys:
                rep.err("%s: %s has unknown key %r" % (rel, key, k))

    # roam
    roam = data.get("roam")
    if not isinstance(roam, dict):
        rep.err("%s: roam must be an object" % rel)
    else:
        extra = set(roam) - {"edges", "speed_px_s"}
        if extra:
            rep.err("%s: roam has unknown keys %s" % (rel, sorted(extra)))
        edges = roam.get("edges")
        if (not isinstance(edges, list) or not edges or len(set(map(str, edges))) != len(edges)
                or any(e not in ROAM_EDGES for e in edges)):
            rep.err("%s: roam.edges must be a non-empty unique subset of %s" % (rel, ", ".join(ROAM_EDGES)))
        sp = roam.get("speed_px_s")
        if not is_num(sp) or not 5 <= sp <= 120:
            rep.err("%s: roam.speed_px_s must be a number from 5 to 120" % rel)

    # sheets and stages
    sheets = []   # (path, frame_w, frame_h, label)
    has_sheet, has_stages = "sheet" in data, "stages" in data
    if has_sheet == has_stages:
        rep.err("%s: give exactly one of `sheet` (single sheet) or `stages`" % rel)
    elif has_sheet:
        s = data["sheet"]
        why = sheet_path_error(s)
        if why:
            rep.err("%s: sheet %s" % (rel, why))
        elif fw:
            sheets.append((s, fw, fh, "sheet"))
    else:
        stages = data["stages"]
        if not isinstance(stages, list) or not 1 <= len(stages) <= 6:
            rep.err("%s: stages must be 1 to 6 entries" % rel)
            stages = []
        ids, used = set(), set()
        prev = None
        for i, st in enumerate(stages):
            where = "%s: stages[%d]" % (rel, i)
            if not isinstance(st, dict):
                rep.err("%s must be an object" % where)
                continue
            extra = set(st) - {"id", "name", "min_growth", "sheet", "frame_w", "frame_h"}
            if extra:
                rep.err("%s has unknown keys %s" % (where, sorted(extra)))
            sid = st.get("id")
            if not isinstance(sid, str) or not ID_RE.fullmatch(sid) or not 2 <= len(sid) <= 20:
                rep.err("%s: id must be kebab-case, 2 to 20 characters" % where)
            elif sid in ids:
                rep.err("%s: duplicate stage id %r" % (where, sid))
            else:
                ids.add(sid)
            nm = st.get("name")
            if not isinstance(nm, str) or not 1 <= len(nm) <= 24:
                rep.err("%s: name must be 1 to 24 characters" % where)
            mg = st.get("min_growth")
            if not is_num(mg) or not 0 <= mg <= 1:
                rep.err("%s: min_growth must be a number from 0 to 1" % where)
            else:
                if i == 0 and mg != 0:
                    rep.err("%s: the first stage must have min_growth 0" % where)
                if prev is not None and mg <= prev:
                    rep.err("%s: min_growth %g must be greater than the previous stage's %g" % (where, mg, prev))
                prev = mg
            sw, sh = st.get("frame_w"), st.get("frame_h")
            if not size_ok(sw) or not size_ok(sh):
                rep.err("%s: frame_w and frame_h must be integers from 8 to 64" % where)
                sw = sh = None
            elif i == 0 and fw and (sw, sh) != (fw, fh):
                rep.err("%s: stage 0 frame size %dx%d must equal the top-level frame_w x frame_h %dx%d"
                        % (where, sw, sh, fw, fh))
            sp = st.get("sheet")
            why = sheet_path_error(sp)
            if why:
                rep.err("%s: sheet %s" % (where, why))
            elif sp in used:
                rep.err("%s: sheet %r is used by another stage" % (where, sp))
            else:
                used.add(sp)
                if sw:
                    sheets.append((sp, sw, sh, "stage %r" % sid))

    expected_files = {"sprite.json"} | {s[0] for s in sheets}
    listed = {f for f in files_field if isinstance(f, str)} if isinstance(files_field, list) else set()
    if sheets:
        if listed != expected_files:
            missing = sorted(expected_files - listed)
            extra = sorted(listed - expected_files)
            if missing:
                rep.err("pack.json: files must list %s" % ", ".join(missing))
            if extra:
                rep.err("pack.json: sprite packs may list only sprite.json and the sheets it "
                        "references; remove %s" % ", ".join(extra))

    for path, w, h, label in sheets:
        f = pack_dir / path
        if not f.is_file():
            continue  # reported by the exact-listing check
        if path.endswith(".json"):
            try:
                size = f.stat().st_size
            except OSError:
                continue
            if size > PIXEL_MAX_BYTES:
                rep.err("%s: pixel sheet is %.1f MB, limit is 2 MB" % (path, size / 1048576))
                continue
            sheet = jload(path) if jload else load_json(f, rep, path)
            if sheet is not None:
                check_pixel_sheet(rep, path, sheet, w, h, max_index, label)
            continue
        try:
            data_png = f.read_bytes()
        except OSError:
            continue
        dims = check_png(rep, path, data_png)
        if not dims:
            continue
        pw, ph = dims
        if pw % w or ph % h:
            rep.err("%s: sheet is %dx%d, not a whole number of %dx%d frames (%s)"
                    % (path, pw, ph, w, h, label))
            continue
        cells = (pw // w) * (ph // h)
        if max_index >= cells:
            rep.err("%s: animations use frame index %d but the sheet has only %d frames (%s)"
                    % (path, max_index, cells, label))


# ---- one pack ---------------------------------------------------------------------------------
def validate_pack(pack_dir, label=None):
    pack_dir = Path(pack_dir)
    if label is None:
        try:
            label = pack_dir.resolve().relative_to(REPO).as_posix()
        except ValueError:
            label = str(pack_dir)
    rep = Report(label)

    if not pack_dir.is_dir():
        rep.err("not a directory")
        return rep
    if os.path.islink(pack_dir):
        rep.err("the pack folder is a symlink")
        return rep

    # -- walk the folder -------------------------------------------------------------------
    on_disk = {}
    total = 0
    for root, dirs, names in os.walk(pack_dir, followlinks=False):
        for d in list(dirs):
            full = os.path.join(root, d)
            is_junction = getattr(os.path, "isjunction", lambda _p: False)(full)
            if os.path.islink(full) or is_junction:
                rep.err("%s is a symlink or junction; links are not allowed" %
                        Path(full).relative_to(pack_dir).as_posix())
                dirs.remove(d)
        for n in names:
            full = Path(root) / n
            rel = full.relative_to(pack_dir).as_posix()
            if os.path.islink(full):
                rep.err("%s is a symlink; links are not allowed" % rel)
                continue
            on_disk[rel] = full
            try:
                total += full.stat().st_size
            except OSError:
                pass
    if total > MAX_PACK_BYTES:
        rep.err("pack is %.1f MB, limit is 10 MB" % (total / 1048576))
    if len(on_disk) > MAX_FILES + 3:
        rep.err("pack has %d files, limit is %d" % (len(on_disk), MAX_FILES))

    for rel in sorted(on_disk):
        if not safe_rel_path(rel):
            rep.err("%s: unsafe file name (segments must match [A-Za-z0-9][A-Za-z0-9._-]*)" % rel)

    # -- manifest ---------------------------------------------------------------------------
    if "pack.json" not in on_disk:
        rep.err("pack.json is missing")
        return rep
    manifest = load_json(on_disk["pack.json"], rep, "pack.json")
    if manifest is None:
        return rep
    if not isinstance(manifest, dict):
        rep.err("pack.json: must be a JSON object")
        return rep
    for k in manifest:
        if k not in MANIFEST_REQUIRED and k not in MANIFEST_OPTIONAL:
            rep.err("pack.json: unknown key %r" % k)
    for k in MANIFEST_REQUIRED:
        if k not in manifest:
            rep.err("pack.json: missing required key %r" % k)

    def text_field(key, lo, hi):
        v = manifest.get(key)
        if key not in manifest:
            return None
        if not isinstance(v, str) or not lo <= len(v) <= hi:
            rep.err("pack.json: %s must be a string of %d to %d characters" % (key, lo, hi))
            return None
        if CTRL_RE.search(v):
            rep.err("pack.json: %s contains control characters" % key)
            return None
        return v

    if "schema_version" in manifest and (manifest["schema_version"] != 1
                                         or isinstance(manifest["schema_version"], bool)):
        rep.err("pack.json: schema_version must be 1")
    pid = text_field("id", 3, 40)
    if pid is not None:
        if not ID_RE.fullmatch(pid):
            rep.err("pack.json: id %r must be lowercase kebab-case ([a-z][a-z0-9]*(-[a-z0-9]+)*)" % pid)
        elif pid != pack_dir.name:
            rep.err("pack.json: id %r does not match the folder name %r" % (pid, pack_dir.name))
    name = text_field("name", 1, 40)
    version = text_field("version", 1, 32)
    if version is not None and not SEMVER_RE.fullmatch(version):
        rep.err("pack.json: version %r must be MAJOR.MINOR.PATCH" % version)
    author = text_field("author", 1, 80)
    smin = text_field("sanduhr_min", 1, 32)
    if smin is not None and not SEMVER_RE.fullmatch(smin):
        rep.err("pack.json: sanduhr_min %r must be MAJOR.MINOR.PATCH" % smin)
    desc = text_field("description", 1, 200)

    kind = manifest.get("kind")
    if "kind" in manifest and kind not in KINDS:
        rep.err("pack.json: kind must be one of %s" % ", ".join(KINDS))
        kind = None

    lic = manifest.get("license")
    if "license" in manifest and (not isinstance(lic, str) or lic not in LICENSES):
        rep.err("pack.json: license %r is not allowed; use one of %s" % (lic, ", ".join(LICENSES)))
        lic = None
    if kind == "font" and lic is not None and lic not in FONT_LICENSES:
        rep.err("pack.json: font packs must use %s (got %s)" % (" or ".join(FONT_LICENSES), lic))

    # trademark scan over the manifest strings
    for where, text in (("id", pid), ("name", name), ("description", desc), ("author", author)):
        if text:
            check_trademark(rep, "pack.json %s" % where, text)
    for rel in on_disk:
        check_trademark(rep, "file name %r" % rel, rel)

    files_field = manifest.get("files")
    listed = []
    if "files" in manifest:
        if not isinstance(files_field, list) or not 1 <= len(files_field) <= MAX_FILES:
            rep.err("pack.json: files must be an array of 1 to %d paths" % MAX_FILES)
        else:
            for p in files_field:
                if not isinstance(p, str):
                    rep.err("pack.json: files entries must be strings")
                elif not safe_rel_path(p):
                    rep.err("pack.json: files entry %r is not a safe relative path" % p)
                elif p in ("pack.json", "LICENSE"):
                    rep.err("pack.json: files must not list %s" % p)
                else:
                    listed.append(p)
            if len(set(listed)) != len(listed):
                rep.err("pack.json: files has duplicate entries")

    preview = manifest.get("preview")
    if "preview" in manifest:
        if not isinstance(preview, str) or not safe_rel_path(preview) or not preview.endswith(".png"):
            rep.err("pack.json: preview must be a safe relative .png path")
            preview = None
        elif preview in listed:
            rep.err("pack.json: preview is declared in `preview`; do not repeat it in files")

    # -- exact file listing -----------------------------------------------------------------
    declared = set(listed)
    exempt = {"pack.json", "LICENSE"} | ({preview} if isinstance(preview, str) else set())
    for rel in sorted(set(on_disk) - declared - exempt):
        rep.err("%s: file is not listed in pack.json files" % rel)
    for rel in sorted(declared - set(on_disk)):
        rep.err("%s: listed in pack.json files but missing from the pack" % rel)
    if isinstance(preview, str) and preview not in on_disk:
        rep.err("%s: preview file is missing" % preview)

    # -- per-file content checks ------------------------------------------------------------
    for rel in sorted(on_disk):
        if rel in ("pack.json", "LICENSE"):
            ext = ""
        else:
            ext = os.path.splitext(rel)[1]
            if ext != ext.lower():
                rep.err("%s: file extension must be lowercase" % rel)
            ext = ext.lower()
            if ext in BANNED_EXT:
                rep.err("%s: %s files are banned (packs are data only: no scripts, executables, HTML or SVG)"
                        % (rel, ext))
                continue
            if ext not in ALLOWED_EXT:
                rep.err("%s: file type %r is not allowed (allowed: %s)"
                        % (rel, ext or "none", ", ".join(ALLOWED_EXT)))
                continue
        try:
            raw = on_disk[rel].read_bytes()
        except OSError as e:
            rep.err("%s: cannot read (%s)" % (rel, e))
            continue
        for sig, what in EXEC_SIGNATURES:
            if raw.startswith(sig):
                rep.err("%s: content looks like %s" % (rel, what))
        low = raw.lower() if len(raw) <= MAX_PACK_BYTES else raw[:MAX_PACK_BYTES].lower()
        for snip in BANNED_SNIPPETS:
            if snip in low:
                rep.err("%s: contains %r; active content is banned" % (rel, snip.decode()))
        if ext == ".png":
            if rel == preview:
                check_png(rep, rel, raw)  # sprite sheets are checked with their frame grid
        elif ext in (".ttf", ".otf"):
            check_font(rep, rel, raw)

    # -- LICENSE ----------------------------------------------------------------------------
    if "LICENSE" not in on_disk:
        rep.err("LICENSE is missing; every pack carries its own license text")
    else:
        lp = on_disk["LICENSE"]
        size = lp.stat().st_size
        if size == 0:
            rep.err("LICENSE is empty")
        elif size > MAX_LICENSE_BYTES:
            rep.err("LICENSE is larger than 100 KB")
        else:
            try:
                text = lp.read_text(encoding="utf-8")
            except UnicodeDecodeError:
                rep.err("LICENSE is not valid UTF-8")
                text = None
            if text is not None and lic in LICENSES:
                flat = " ".join(text.lower().split())
                if not any(p in flat for p in LICENSES[lic]):
                    rep.err("LICENSE text does not look like %s (expected the standard license text)" % lic)

    # -- kind-specific ----------------------------------------------------------------------
    if kind is None:
        return rep
    parsed = {}

    def jload(rel):
        if rel in on_disk:
            if rel not in parsed:
                parsed[rel] = load_json(on_disk[rel], rep, rel)
            return parsed[rel]
        return None

    # trademark scan over every string in every JSON asset
    for rel in sorted(on_disk):
        if rel.endswith(".json") and rel != "pack.json":
            data = jload(rel)
            if data is not None:
                for s in iter_strings(data):
                    hit = find_trademark(s)
                    if hit:
                        check_trademark(rep, "%s value %r" % (rel, s[:40]), s)
                        break

    if kind == "theme":
        if not 1 <= len(listed) <= 8:
            rep.err("pack.json: theme packs list 1 to 8 theme files (got %d)" % len(listed))
        for rel in listed:
            if not THEME_FILE_RE.fullmatch(rel):
                rep.err("%s: theme files must be themes/<kebab-name>.json" % rel)
                continue
            data = jload(rel)
            if data is None:
                continue
            errs, warns = lint_theme(data)
            for m in errs:
                rep.err("%s: %s" % (rel, m))
            for m in warns:
                rep.warn("%s: %s" % (rel, m))
    elif kind == "palette":
        if listed != ["palettes.json"]:
            rep.err('pack.json: palette packs must list exactly ["palettes.json"]')
        data = jload("palettes.json")
        if data is not None:
            check_palettes(rep, data)
    elif kind == "effects":
        if listed != ["effects.json"]:
            rep.err('pack.json: effects packs must list exactly ["effects.json"]')
        data = jload("effects.json")
        if data is not None:
            check_effects(rep, data)
    elif kind == "font":
        if not 1 <= len(listed) <= 4:
            rep.err("pack.json: font packs list 1 to 4 font files (got %d)" % len(listed))
        for rel in listed:
            if os.path.splitext(rel)[1] not in (".ttf", ".otf"):
                rep.err("%s: font packs may list only .ttf and .otf files" % rel)
    elif kind == "sprite":
        data = jload("sprite.json")
        if data is None:
            if "sprite.json" not in on_disk:
                rep.err("sprite.json is missing")
        else:
            check_sprite(rep, pack_dir, data, files_field, jload)
    return rep


def find_packs(paths):
    """Expand CLI paths into pack folders. A folder with pack.json is a pack, else a folder of packs."""
    packs = []
    for p in paths:
        p = Path(p)
        if (p / "pack.json").exists():
            packs.append(p)
        elif p.is_dir():
            packs.extend(sorted(d for d in p.iterdir() if d.is_dir() and not d.name.startswith(".")))
        else:
            packs.append(p)  # reported as 'not a directory'
    return packs


def validate_paths(paths):
    reports = [validate_pack(p) for p in find_packs(paths)]
    # ids must be unique across packs
    seen = {}
    for rep, p in zip(reports, find_packs(paths)):
        pj = Path(p) / "pack.json"
        try:
            pid = json.loads(pj.read_text(encoding="utf-8")).get("id")
        except (OSError, ValueError, RecursionError, AttributeError):
            continue
        if isinstance(pid, str):
            if pid in seen:
                rep.err("id %r is already used by %s" % (pid, seen[pid]))
            else:
                seen[pid] = rep.label
    return reports


def main(argv=None):
    ap = argparse.ArgumentParser(description="Validate Sanduhr widget packs (format v1).")
    ap.add_argument("paths", nargs="*", help="a pack folder or a folder of packs (default: packs/)")
    ap.add_argument("--strict", action="store_true", help="treat warnings as errors")
    args = ap.parse_args(argv)
    paths = args.paths or [str(REPO / "packs")]
    reports = validate_paths(paths)
    if not reports:
        print("ERROR no packs found under %s" % ", ".join(paths))
        return 1
    errors = warnings = 0
    for rep in reports:
        for line in rep.lines():
            print(line)
        errors += len(rep.errors)
        warnings += len(rep.warnings)
    verdict = "FAIL" if errors or (args.strict and warnings) else "OK"
    print("%s: %d pack(s), %d error(s), %d warning(s)" % (verdict, len(reports), errors, warnings))
    return 1 if verdict == "FAIL" else 0


if __name__ == "__main__":
    sys.exit(main())
