"""Theme palettes and glass-tuning dials.

Each theme is a dict of colors + glass rendering parameters. Non-Matrix
themes get the full glass-stacking pass (see spec section 4c); Matrix
explicitly opts out to preserve its phosphor-on-black aesthetic.

Users can drop additional theme JSON files into %APPDATA%\\Sanduhr\\themes\\;
call `load_user_themes()` at app bootstrap to merge them into THEMES.
"""

import json
import logging

from sanduhr import paths

_log = logging.getLogger(__name__)

_REQUIRED_COLOR_FIELDS = (
    "name", "bg", "glass", "glass_on_mica", "title_bg", "border",
    "text", "text_secondary", "text_dim", "text_muted",
    "accent", "bar_bg", "footer_bg", "pace_marker", "sparkline",
)

_DEFAULT_GLASS_TUNING = {
    "glass_alpha": 0.80,
    "border_alpha": 0.40,
    "border_tint": None,
    "accent_bloom": {"blur": 4, "alpha": 0.45},
    "inner_highlight": None,
}


THEMES = {
    "obsidian": {
        "name": "Obsidian",
        "bg": "#0d0d0d",
        "glass": "#1c1c1c",
        "glass_on_mica": "#1a1a1c",
        "title_bg": "#161616",
        "border": "#333333",
        "text": "#e8e4dc",
        "text_secondary": "#b8b4ac",
        "text_dim": "#777777",
        "text_muted": "#555555",
        "accent": "#6c63ff",
        "bar_bg": "#2a2a2a",
        "footer_bg": "#111111",
        "pace_marker": "#ff6b6b",
        "sparkline": "#6c63ff",
        "glass_alpha": 0.85,
        "border_alpha": 0.30,
        "border_tint": None,
        "accent_bloom": {"blur": 4, "alpha": 0.35},
        "inner_highlight": None,
    },
    "aurora": {
        "name": "Aurora",
        "bg": "#0a0f1a",
        "glass": "#161e30",
        "glass_on_mica": "#141d2e",
        "title_bg": "#0f172a",
        "border": "#334155",
        "text": "#e2e8f0",
        "text_secondary": "#94a3b8",
        "text_dim": "#64748b",
        "text_muted": "#475569",
        "accent": "#38bdf8",
        "bar_bg": "#1e293b",
        "footer_bg": "#0c1220",
        "pace_marker": "#f472b6",
        "sparkline": "#38bdf8",
        "glass_alpha": 0.80,
        "border_alpha": 0.50,
        "border_tint": "#38bdf8",
        "accent_bloom": {"blur": 6, "alpha": 0.55},
        "inner_highlight": {"color": "#38bdf8", "alpha": 0.20},
    },
    "ember": {
        "name": "Ember",
        "bg": "#1a0a0a",
        "glass": "#261414",
        "glass_on_mica": "#211010",
        "title_bg": "#1f0e0e",
        "border": "#442222",
        "text": "#f5e6e0",
        "text_secondary": "#d4a89c",
        "text_dim": "#8b6b60",
        "text_muted": "#6b4b40",
        "accent": "#f97316",
        "bar_bg": "#2d1a1a",
        "footer_bg": "#150808",
        "pace_marker": "#fbbf24",
        "sparkline": "#f97316",
        "glass_alpha": 0.82,
        "border_alpha": 0.40,
        "border_tint": "#f97316",
        "accent_bloom": {"blur": 6, "alpha": 0.55},
        "inner_highlight": {"color": "#f97316", "alpha": 0.18},
    },
    "mint": {
        "name": "Mint",
        "bg": "#0a1a14",
        "glass": "#122a1e",
        "glass_on_mica": "#0e2419",
        "title_bg": "#0c1f14",
        "border": "#22543d",
        "text": "#e0f5ec",
        "text_secondary": "#9cd4b8",
        "text_dim": "#5a9a78",
        "text_muted": "#3a7a58",
        "accent": "#34d399",
        "bar_bg": "#163020",
        "footer_bg": "#081510",
        "pace_marker": "#f472b6",
        "sparkline": "#34d399",
        "glass_alpha": 0.78,
        "border_alpha": 0.45,
        "border_tint": "#34d399",
        "accent_bloom": {"blur": 4, "alpha": 0.35},
        "inner_highlight": {"color": "#34d399", "alpha": 0.22},
    },
    "matrix": {
        "name": "Matrix",
        "bg": "#020a02",
        "glass": "#0a140a",
        "glass_on_mica": "#0a140a",
        "title_bg": "#040d04",
        "border": "#0f2a0f",
        "text": "#00ff41",
        "text_secondary": "#00cc33",
        "text_dim": "#00802b",
        "text_muted": "#005a1e",
        "accent": "#00ff41",
        "bar_bg": "#0a1a0a",
        "footer_bg": "#020802",
        "pace_marker": "#ff0040",
        "sparkline": "#00ff41",
        "glass_alpha": 1.0,
        "border_alpha": 0.50,
        "border_tint": "#00ff41",
        "accent_bloom": {"blur": 4, "alpha": 0.60},
        "inner_highlight": None,
        "opts_out_of_mica": True,
        "monospace_font": "Cascadia Code",
        "monospace_fallback": "Consolas",
        "card_corner_radius": 2,
    },
    "blueprint": {
        "name": "Blueprint",
        "bg": "#1a1625",
        "glass": "#2a2438",
        "glass_on_mica": "#241f30",
        "title_bg": "#15121e",
        "border": "#3a324d",
        "text": "#e0e2f5",
        "text_secondary": "#a2a6cc",
        "text_dim": "#6a6e99",
        "text_muted": "#484b70",
        "accent": "#4dffc4",
        "bar_bg": "#1f1a2e",
        "footer_bg": "#100d16",
        "pace_marker": "#ff4d88",
        "sparkline": "#4dffc4",
        "glass_alpha": 0.85,
        "border_alpha": 0.70,
        "border_tint": "#4dffc4",
        "accent_bloom": {"blur": 8, "alpha": 0.65},
        "inner_highlight": {"color": "#4dffc4", "alpha": 0.15},
        "bg_grid": True,
    },
}


def usage_color(pct: int) -> str:
    """Return the bar fill color for a given utilization %."""
    if pct < 50:
        return "#4ade80"
    if pct < 75:
        return "#facc15"
    if pct < 90:
        return "#fb923c"
    return "#f87171"


def _validate_theme(key: str, data: dict) -> dict | None:
    """Return a normalized theme dict, or None if invalid (logged)."""
    missing = [f for f in _REQUIRED_COLOR_FIELDS if f not in data]
    if missing:
        _log.warning("User theme '%s' missing required fields: %s", key, missing)
        return None
    merged = dict(_DEFAULT_GLASS_TUNING)
    merged.update(data)
    return merged


def load_user_themes() -> dict:
    """Scan %APPDATA%\\Sanduhr\\themes\\ for *.json theme files and merge into THEMES.

    File stem becomes the theme key (sunset.json -> "sunset"). Invalid JSON
    or missing required fields -> logged warning, file skipped. Safe to call
    repeatedly; later files for the same key replace earlier ones.

    Returns a dict of {key: theme} for only the user themes loaded (for tests).
    """
    themes_dir = paths.app_data_dir() / "themes"
    themes_dir.mkdir(parents=True, exist_ok=True)

    loaded = {}
    for path in sorted(themes_dir.glob("*.json")):
        key = path.stem.lower()
        if not key or key in loaded:
            continue
        try:
            data = json.loads(path.read_text(encoding="utf-8"))
        except (json.JSONDecodeError, OSError) as e:
            _log.warning("Could not read user theme %s: %s", path.name, e)
            continue
        theme = _validate_theme(key, data)
        if theme is None:
            continue
        THEMES[key] = theme
        loaded[key] = theme
        _log.info("Loaded user theme '%s' from %s", key, path.name)
    return loaded
