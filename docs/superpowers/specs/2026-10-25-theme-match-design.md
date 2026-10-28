# One theme for the widget and the Desk: design

Windows. Brainstormed with the owner on 2026-10-25. Dashboard decision `zIsGHT5TiD5lbEwP4AiQ` (its own
slice, not 4.0 slice 3); task "Match the Desk to the widget theme". Builds on Settings v2 slice 3 (the
Desk Look page, `DeskArrangement.DefaultPlaceFor`, the Desk split), merged to `release/4.0` at `999baf1`.

## The goal

One theme styles the widget and the Desk so they read as a set: colors, gradients, the font, the plate
and the shadow. Picking a theme gives a Desk that belongs with it, with no separate styling session.
Gradients survive everywhere, meters included. A matched look is always kept in a theme, so it travels:
switch away and back, or share the file, and the Desk comes along.

## What a theme can carry (all optional; every existing theme file stays valid)

Two additions to the theme JSON (`ThemeDefinition`, snake_case):

- **`font`**: a font family that styles the widget's text and, when the Desk follows, the Desk. The
  built-in themes set none, so they keep Segoe UI; Matrix and Blueprint keep `monospace_font` for the
  values.
- **`desk`**: an object, every field optional:
  - `ink`: 1 to 4 hex stops for the clock, date and Claude line.
  - `bars`: 1 to 4 hex stops for meter bars, on the widget and the Desk.
  - `message`: 1 to 4 hex stops for the Message line.
  - `glow`: one hex color for the message glow.
  - `plate`: true or false (the back plate shows or not).
  - `plate_color`: one hex color for the plate tint.
  - `shadow`: one hex color for the text shadow.
  - `font`: a family for the Desk only, when it should differ from the theme's `font`.

## The rules: `ThemeDeskLook.From(theme)` (Core, pure, tested)

One function turns a theme into a full look. A `desk` field, when set, wins. Otherwise:

| Part | Default from the theme |
|---|---|
| The theme gradient | **accent → sparkline** (ThemeLint already keeps them in one hue family). If the two are equal, accent → text. |
| Ink: clock, date, Claude line | **text → accent** |
| Meter bars (Desk; the widget only when `desk.bars` is set, see "Meters") | the theme gradient, on a `bar_bg` track, with the `pace_marker` tick |
| Message | the theme gradient; glow = accent |
| Now playing: visualizer bars and position bar | the theme gradient, unless a song's own look is showing (`now_playing.style`) |
| Back plate | on, tinted from the theme's `bg`, at today's plate opacity (0.30) |
| Text shadow | a deep shade of the theme's `bg` (not black) |
| Desk font | `desk.font`, else the theme's `font`, else `monospace_font` (Matrix, Blueprint), else the Desk keeps its own font |

**Readability guard.** The Desk draws on the wallpaper. A matched ink is judged against the wallpaper
sample the Desk already takes (`WallpaperSampler`, `DeskInk`'s readability check). When it would not
read, the plate turns on; Match now and Follow say so in one line ("The plate is on so the clock reads
over this wallpaper.").

## Meters: the usage signal is worked into the gradient

- **The widget keeps its ramp** (green under 50, yellow under 75, orange under 90, red: `ThemeCatalog.UsageColor`)
  **unless the active theme sets `desk.bars`**. No built-in theme does, so nothing changes at upgrade.
- **Any meter drawn with a gradient carries the usage signal.** That is the widget when the theme sets
  `desk.bars`, and the Desk's meters whenever their ink is a gradient (Desk meters follow the ink, as
  today). The gradient stays the user's. A **middle stop** is added: **yellow at Warn at**, **red at
  Urgent at**, using the alert thresholds (`alert_warn_pct`, default 80; `alert_urgent_pct`, default 95).
  The stop follows the meter live as usage crosses each level, and goes away below Warn at.
- **A solid meter** is a gradient from its color to itself, so it gets the same middle stop.
- This replaces the Desk's hard switch to solid red at 90% (`DeskMeters.WarningAt`, `WarningRed`). The
  percent text beside a meter takes the signal color at the same thresholds.
- **Only meters carry the signal.** The clock, date, message and Now playing keep their gradient exactly.

## Follow the widget theme, and Match now

Both live at the top of **Settings ▸ Desk Look**.

- **Follow the widget theme** (a switch). Turning it on makes the Desk draw from the active theme's
  look. Where that look is kept:
  - **A built-in or pack theme is active:** Sanduhr makes a **new user theme**: the active theme plus a
    `desk` block that starts as the theme's matched look (the rules above), so the Desk changes to match
    the widget; saved as `themes\<name>.json` (name: "<Theme> +
    Desk", made unique), and makes it active. Built-in and pack themes are never edited.
  - **A user theme is active:** Sanduhr asks: **"Add the Desk look to <theme>"** (writes its `desk`
    block in place) or **"Make a new theme"**.
  - Switching the widget theme while following restyles the Desk from the new theme.
- **Match now** (a button): a one-time copy. It writes the active theme's look (the rules above) into
  the Desk's own settings: ink, font, plate, shadow, message color and glow, Now playing bars. Follow
  stays off and no theme is made; the Desk keeps that look, freely tweakable, when the widget theme
  later changes.
- **A hand edit while following asks where it goes.** Changing the ink, a gradient, the font, the
  plate, the shadow, the message color or glow, or Now playing's bars on Desk Look asks, once per edit
  (on a slider release or a picker closing, never per tick):
  - **"Update <theme>"**: writes the change into the active user theme's `desk` block. Not offered for a
    built-in or pack theme.
  - **"Save as a new theme"**: a new user theme with the change, named by the person, made active.
  - Cancel leaves the setting as it was.
- **Follow off.** The Desk has its own look: Desk Look edits save to the Desk settings with no prompt,
  as today. Turning Follow off keeps the current look as the Desk's own settings, so nothing jumps.
- **Undo.** Turning Follow on and Match now save the current Desk first, like an applied Desk design
  ("Before <name>"), so **Put my Desk back** returns the Desk and the widget theme together.

## Settings and data

- **One new key: `desk_follows_theme`** (bool, default false). It is the switch; while true the Desk
  draws from the active theme's look and ignores its own look settings, which are kept untouched for
  when Follow goes off. Added to `SettingsKeysContractTests`, `DeskState` (get_desk reports it), the
  Desk design format (`follows_theme`, optional) and `DeskDesignApply`, so designs and undo carry it.
- **`desk_ink.mode: theme`** keeps meaning the theme's text color, unchanged. The new look comes only
  through Follow, so no existing setting changes its look at upgrade.
- **Theme files:** `font` and `desk` are read by `ThemeCatalog.BuildDefinition`, written by
  `ThemeDraft` (the canonical serializer used by designs and `get_desk`; a field it does not carry is
  silently dropped, so it must carry both), and linted by `ThemeLint`: stops are valid hex, ink and
  message stops meet the existing readable-contrast rule, `font` is a known family or a font pack, and
  unknown `desk` fields are a finding. Pack themes and pasted JSON accept the same fields; agents'
  `propose_theme` accepts them (schema in `ToolCatalog`).

## Theme Studio

The theme maker gains:

- a **Font** picker (system families, the bundled EsteFonts, font packs);
- a **Desk** section with the ink, bars and message gradients (1 to 4 stops each), glow, plate on/off
  and tint, and shadow. Each shows the rule's default, greyed, until set.

Its live preview covers the widget and the Desk; the Desk already re-renders on `WidgetViewModel.ThemeChanged`,
which the Studio preview raises.

## Trust boundary

`ThemeModel.cs` and `ThemeLint.cs` are source-linked into `Sanduhr.Mcp` and gain the new fields and
checks; the new `ThemeDeskLook.cs` (pure, no IO, network or process names) joins the MCP link list so
`get_desk` and the design validator compute the same look. Widening the link list is a deliberate
choice recorded here; `TrustBoundaryTests` changes only to admit `ThemeDeskLook.cs`.

## What does not change

- Sizes (clock size, message size), margins, OLED care, the layout and places.
- The Claude Code glow's color.
- The widget's ramp, for every theme without `desk.bars`.
- `desk_ink.mode: theme` and every existing Desk setting's meaning.

## Testing

- **Core:** `ThemeDeskLook.From` for each built-in theme (pinned looks), each `desk` override, the
  accent = sparkline fallback; the meter signal stops at Warn at and Urgent at, below Warn at, for a
  gradient and a solid color, with custom thresholds; `ThemeCatalog` and `ThemeDraft` round-trip a theme
  with and without `font`/`desk`; `ThemeLint` findings for bad stops, low contrast, an unknown font and
  unknown `desk` fields.
- **App:** Follow on a built-in makes a "<Theme> + Desk" user theme and activates it; on a user theme it
  asks add-or-copy; a hand edit while following asks Update / Save as new, and a built-in never offers
  Update; Follow off saves edits silently and keeps the look; Put my Desk back restores both sides; the
  widget's bars are unchanged for themes without `desk.bars` (render compare).
- **MCP:** `propose_theme` accepts and lints `font` and `desk`; `get_desk` reports `follows_theme`;
  `TrustBoundaryTests` with the widened list.
- **Harness:** render each built-in theme's matched Desk and widget, dark and light wallpapers, at the
  warn and urgent levels.

## Open for the plan

- The exact "deep shade" for the shadow and the plate tint mix (a fixed blend of `bg` toward black, and
  toward the ink's opposite, pinned by tests per built-in).
- Where in Desk Look the Follow switch and Match now sit relative to the jump links.
