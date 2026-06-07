# Sanduhr Theme Builder — AI Prompt

Paste this prompt into Claude, ChatGPT, or any chat agent along with a
**vibe description** or **a reference image**, and the agent will return
a ready-to-drop theme JSON that works on both the macOS and Windows
builds of Sanduhr.

## How to use

1. Copy the entire prompt below.
2. Attach a reference image (album cover, screenshot, painting, color
   palette — anything) and/or describe the vibe in one sentence.
3. Paste the returned JSON into your platform's themes folder:
   - **macOS:** `~/Library/Application Support/Sanduhr/themes/<yourname>.json`
   - **Windows:** `%APPDATA%\Sanduhr\themes\<yourname>.json`
4. Restart Sanduhr — the new theme appears in the theme strip automatically.

---

## The prompt

You are designing a color theme for Sanduhr für Claude — a small
always-on-top desktop widget that shows Claude.ai usage bars. The widget
runs on macOS (SwiftUI with NSVisualEffectView vibrancy) and Windows
(Qt with Win11 Mica glass). Themes are JSON dicts with hex colors and a
few glass-rendering dials that work identically on both platforms.

Given the reference image or vibe description I'm providing, return **only**
a valid JSON object matching this schema. No markdown, no preamble, no
commentary — just the raw JSON.

**Required color fields** (all lowercase `#rrggbb`):

| Field             | What it does                                                                 |
| ----------------- | ---------------------------------------------------------------------------- |
| `name`            | Display name shown in the theme strip (1–12 chars, Title Case)               |
| `bg`              | Window background solid fallback (darkest shade of the palette)              |
| `glass`           | Solid-fallback card background (Win10 non-Mica, macOS theme strip)           |
| `glass_on_mica`   | Card background tuned for α-blending over vibrancy (usually near `bg`, a touch darker) |
| `title_bg`        | Title bar solid fallback                                                     |
| `border`          | Neutral card border color                                                    |
| `footer_bg`       | Footer solid fallback                                                        |
| `bar_bg`          | Progress bar empty (unfilled) portion                                        |
| `text`            | Primary text (percentages, title) — HIGH contrast against `glass_on_mica`    |
| `text_secondary`  | Tier labels                                                                  |
| `text_dim`        | Reset countdown / footer                                                     |
| `text_muted`      | Reset date / burn projection empty state                                     |
| `accent`          | Brand color (pace marker, sparkline, top accent stripe, active theme name)   |
| `pace_marker`     | The tick on each progress bar (should stand out on all bar fills)            |
| `sparkline`       | Trend line color (often same as `accent`)                                    |

**Glass tuning dials** (optional — safe defaults apply if omitted):

| Field             | Range                 | What it does                                                    |
| ----------------- | --------------------- | --------------------------------------------------------------- |
| `glass_alpha`     | `0.70 – 0.90`         | Card density over vibrancy. Lower = airier, higher = more solid |
| `border_alpha`    | `0.20 – 0.60`         | Card border visibility                                          |
| `border_tint`     | hex or `null`         | Border color override; `null` uses `border`                     |
| `accent_bloom`    | `{"blur": 3–8, "alpha": 0.25–0.65}` | Glow around percentage numbers                      |
| `inner_highlight` | `{"color": "#..", "alpha": 0.15–0.30}` or `null` | 1-px top-edge light catch on cards (leave `null` for restrained themes) |

**Matrix-style overrides** (optional — use only for opaque / terminal themes):

| Field                | Value                   | What it does                                                    |
| -------------------- | ----------------------- | --------------------------------------------------------------- |
| `opts_out_of_mica`   | `true`                  | Disables translucency; panel renders solid                      |
| `monospace_font`     | `"Cascadia Code"` etc.  | macOS: presence alone switches digits to SF Mono. Windows: exact font name used, with `monospace_fallback` as backup. |
| `monospace_fallback` | `"Consolas"` etc.       | Windows-only; fallback if primary monospace isn't installed     |
| `card_corner_radius` | `2`                     | Sharper corners for a terminal/CRT feel                         |

**If you opt out of Mica, set `glass_alpha: 1.0` so cards stay opaque.**

### Design rules to follow

1. **Contrast first.** `text` must be legible on `glass_on_mica`. Simulate α-blending over a medium-gray desktop wallpaper — if text is hard to read, pick a brighter `text` or a darker `glass_on_mica`.
2. **Cohesion.** Pull colors from the reference image / vibe. Don't mix unrelated accents.
3. **One accent.** `accent`, `sparkline`, and typically `border_tint` + `inner_highlight.color` should all be the same hue. `pace_marker` can be complementary (e.g., red marker on a green accent).
4. **Dark base.** `bg`, `glass`, `glass_on_mica` should be in the dark half of the spectrum (value < 50%). Light themes don't work with translucent glass layering.
5. **Typography is monochrome across the lights.** `text` → `text_secondary` → `text_dim` → `text_muted` should be the same hue at decreasing luminance.

### Example (for reference, do not copy)

```json
{
  "name": "Sunset",
  "bg": "#1a0a1f",
  "glass": "#2a1835",
  "glass_on_mica": "#1d0f28",
  "title_bg": "#1a0a1f",
  "border": "#4a2a5e",
  "footer_bg": "#140a18",
  "bar_bg": "#2d1a38",
  "text": "#f5e0f0",
  "text_secondary": "#d4a5c8",
  "text_dim": "#8a6a82",
  "text_muted": "#5a4a58",
  "accent": "#ff6fa1",
  "pace_marker": "#fbbf24",
  "sparkline": "#ff6fa1",
  "glass_alpha": 0.80,
  "border_alpha": 0.40,
  "border_tint": "#ff6fa1",
  "accent_bloom": {"blur": 6, "alpha": 0.55},
  "inner_highlight": {"color": "#ff6fa1", "alpha": 0.20}
}
```

---

## Installing a theme

1. Save the JSON as `<lowercase-kebab-name>.json` (e.g. `sunset.json`,
   `midnight-metro.json`). The filename (minus `.json`) becomes the
   theme's internal id, so keep it lowercase and URL-safe.
2. Drop it into your platform's themes folder:
   - **macOS:** `~/Library/Application Support/Sanduhr/themes/`. Open
     Finder, press ⇧⌘G, and paste that path. The folder is created on
     first run.
   - **Windows:** `%APPDATA%\Sanduhr\themes\`. Paste that into File
     Explorer's address bar.
3. Restart Sanduhr. Your theme appears in the theme strip next to the
   built-ins. Click to apply.

If the widget doesn't pick it up, check Console.app (macOS) or
`%APPDATA%\Sanduhr\sanduhr.log` (Windows) for a warning — missing
required fields or invalid JSON are logged there.
