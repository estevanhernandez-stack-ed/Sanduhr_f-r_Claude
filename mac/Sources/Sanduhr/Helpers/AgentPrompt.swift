import Foundation

/// The cross-platform AI prompt that Settings → Themes → "Copy Agent Prompt"
/// puts on the clipboard. Mirrors `docs/themes/AGENT_PROMPT.md` — kept here
/// as a constant so the shipping .app has the text without needing to
/// resolve a bundle resource path.
enum AgentPrompt {
    static let text: String = """
# Sanduhr Theme Builder — AI Prompt

You are designing a color theme for Sanduhr für Claude — a small
always-on-top desktop widget that shows Claude.ai usage bars. It runs on
macOS (SwiftUI with NSVisualEffectView vibrancy) and Windows (Qt with
Win11 Mica glass). Themes are JSON dicts with hex colors and a few
glass-rendering dials that work identically on both platforms.

Given the reference image or vibe description I'm providing, return
**only** a valid JSON object matching this schema. No markdown, no
preamble, no commentary — just the raw JSON.

**Required color fields** (all lowercase `#rrggbb`):

- `name` — display name (1–12 chars, Title Case)
- `bg` — window background solid fallback (darkest shade)
- `glass` — solid-fallback card bg
- `glass_on_mica` — card bg tuned for α-blending over vibrancy (usually near `bg`, a touch darker)
- `title_bg` — title bar solid fallback
- `border` — neutral card border
- `footer_bg` — footer solid fallback
- `bar_bg` — progress bar empty portion
- `text` — primary text (HIGH contrast against `glass_on_mica`)
- `text_secondary` — tier labels
- `text_dim` — reset countdown / footer
- `text_muted` — reset date / empty burn state
- `accent` — brand color (pace marker, sparkline, accent stripe)
- `pace_marker` — tick on each bar (must stand out on all fills)
- `sparkline` — trend line color (often same as `accent`)

**Optional glass dials** (defaults apply):

- `glass_alpha` (0.70–0.90) — card density
- `border_alpha` (0.20–0.60) — card border visibility
- `border_tint` — hex or `null`
- `accent_bloom` — `{"blur": 3–8, "alpha": 0.25–0.65}` glow around percentage numbers
- `inner_highlight` — `{"color": "#..", "alpha": 0.15–0.30}` or `null`

**Matrix-style overrides** (optional; opaque/terminal only):

- `opts_out_of_mica: true`
- `monospace_font: "Cascadia Code"`
- `card_corner_radius: 2`
- `breath_period_ms: 1800`

If you opt out of Mica, set `glass_alpha: 1.0`.

### Design rules

1. **Contrast first.** `text` must be legible on `glass_on_mica`.
2. **Cohesion.** Pull colors from the reference; don't mix unrelated accents.
3. **One accent.** `accent`, `sparkline`, `border_tint`, `inner_highlight.color` share a hue. `pace_marker` can be complementary.
4. **Dark base.** `bg`, `glass`, `glass_on_mica` below 50% luminance.
5. **Monochrome light ramp.** `text → text_secondary → text_dim → text_muted` same hue at decreasing luminance.

### Example output (do not copy, for shape reference)

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
"""
}
