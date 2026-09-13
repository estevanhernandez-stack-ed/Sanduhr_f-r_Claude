# Agentic theming: lint, the `propose_theme` tool, and the Theme Studio

**Date:** 2026-09-13. **Status:** approved in intent by Este ("one, two, and three... let's do all of them"); this document is the shape.

## The idea

Sanduhr's theming already has the right bones: a theme is fourteen role-named hex colors plus a few dials, the runtime derives everything else, and every view binds late so a palette swap re-tints the whole widget with no restart. The agent prompt in `docs/themes/AGENT_PROMPT.md` makes an LLM the theme author. What is missing is the loop. Today the author pastes JSON into the Themes tab or drops a file; a bad value is either silently skipped (missing field) or crashes the apply path (a malformed hex goes straight into `ColorConverter`). Nothing tells the author *why* a theme reads badly, and Claude Code, which is already talking to the widget through `sanduhr-mcp`, cannot hand it a theme at all.

Three pieces, one shared core:

1. **Theme lint** (Core). A pure validator that turns theme JSON into a `ThemeDefinition` plus a list of findings with a field, a severity, and a sentence: what is wrong and how to fix it. Enforces the schema (required fields, hex format, dial ranges) and the design rules the prompt only asks for (dark base, text contrast over the composited card, the monochrome text ramp). Both other pieces, and the existing paste flow, call it.
2. **`propose_theme`** (MCP + widget). An agent hands the widget a palette; the widget lints it, saves it to the themes folder, applies it, and answers with findings and the previous theme's key. Through the same request-file handoff `publish_usage` uses: the server stays network-free and never writes a theme itself.
3. **Theme Studio** (Settings ▸ Themes). Token-level editing with the widget itself as the live preview: fourteen swatch-plus-hex rows, two sliders, findings updating as you type, and Save, Apply, Revert, Copy JSON. Start from any installed theme.

## Piece 1: theme lint

### Where

`windows-dotnet/src/Sanduhr.Core/ThemeLint.cs`, next to `ThemeModel.cs`. Pure: no IO, no WPF. Source-linked into `Sanduhr.Mcp` (with `ThemeModel.cs`) so the server can refuse a broken theme instantly, without a widget round trip. Both files are credential-free and network-free; the trust-boundary allowlist widens by two names and `TrustBoundaryTests` pins them.

### API

```csharp
public enum ThemeFindingLevel { Error, Warning }
public sealed record ThemeFinding(ThemeFindingLevel Level, string Field, string Message);
public sealed record ThemeLintResult(ThemeDefinition? Theme, IReadOnlyList<ThemeFinding> Findings)
{
    public bool Ok => Theme is not null;   // no Error-level findings
}
public static class ThemeLint
{
    public static ThemeLintResult Lint(JsonObject data);
    public static ThemeLintResult Lint(string json);          // parse errors become one Error finding on field "json"
    public static JsonArray ToJson(IReadOnlyList<ThemeFinding> findings);   // [{level, field, message}]
}
```

`ThemeCatalog.TryValidate` keeps its signature and becomes a thin wrapper: `Lint(data)`, warn each Error, return the theme or null. `LoadUserThemes` therefore gets hex validation for free, and a malformed hex in a drop-in file is skipped with a reason instead of crashing the first apply.

### Rules

Errors (the theme is not applied):

- `name` missing or empty; longer than 24 characters is an error (the strip truncates at 12, the prompt says 1 to 12, but 24 is the hard stop so existing user themes keep loading).
- Any of the fourteen color fields missing, not a string, or not `#rrggbb` (six hex digits; `#rgb` and alpha forms are rejected with the message naming the accepted form).
- `glass_alpha`, `border_alpha`, `accent_bloom.alpha`, `inner_highlight.alpha` outside 0 to 1; `accent_bloom.blur` outside 0 to 20; `card_corner_radius` outside 0 to 24; `breath_period_ms` outside 500 to 20000. Non-numeric where a number is expected.
- `border_tint` and `inner_highlight.color` present but not `#rrggbb`.

Warnings (applied, but reported):

- **Dark base.** `bg`, `glass`, `glass_on_mica` with relative luminance above 0.25 (the prompt's "value under 50%" rule, measured as luminance so a saturated mid-tone is caught too). Message: translucent layering does not work on light surfaces.
- **Text contrast.** WCAG contrast ratio of `text` against the composited card (`glass_on_mica` at `glass_alpha` over a neutral mid-gray desktop, `#808080`) below 4.5; `text_secondary` below 3.0. Message carries the measured ratio and the threshold.
- **Text ramp.** Luminance of `text` > `text_secondary` > `text_dim` > `text_muted` not strictly decreasing; or hue distance between any two of them above 30 degrees (the ramp is one hue at decreasing luminance).
- **Pace marker legibility.** Contrast of `pace_marker` against the darkest usage-ramp fill (`#4ade80` green) or against `bar_bg` below 1.5.
- **One accent.** Hue distance between `accent` and `sparkline` above 30 degrees, or between `accent` and `border_tint` when set.
- **Mica opt-out.** `opts_out_of_mica` true with `glass_alpha` below 1.0 (cards would render translucent on Win10 fallbacks).

Color math lives in the same file: hex parse, sRGB to linear, relative luminance, contrast ratio, RGB to hue, alpha composite. About 60 lines. Tests in `Sanduhr.Tests/ThemeLintTests.cs`: each rule has a passing and a failing case; the six built-ins lint clean with zero warnings (a warning on a built-in means the rule is miscalibrated, not the theme).

## Piece 2: `propose_theme`

### Handoff

`windows-dotnet/src/Sanduhr.Core/ThemeHandoff.cs`, the sibling of `PublishHandoff`, same discipline (typed request, expiry, atomic result write, request cleared with the result):

- Request file `%APPDATA%\Sanduhr\theme-request.json`: `{ "id", "requested_at", "theme": { ...the palette JSON... }, "save_as": "kebab-name" | null, "apply": true | false }`.
- Result file `theme-result.json`: `{ "id", "completed_at", "result": { status, reason, remedy, key, name, previous_key, saved_path, findings[] } }`.
- Expiry 10 minutes. A theme proposal is interactive; one that arrives after the session moved on must not surprise the user later. Expired or malformed requests are deleted, not run.
- The MCP project mirrors the two file names and field names literally, as it does for publish; tests on both sides pin them.

### Server side (`Sanduhr.Mcp`)

New tool `propose_theme`, `readOnlyHint: false`, `destructiveHint: false`, `openWorldHint: false`. Input schema: `theme` (object, required; the palette per the agent prompt), `save_as` (string, optional, `^[a-z0-9][a-z0-9-]{0,39}$`), `apply` (boolean, default true). Description carries the schema summary, the design rules in one line each, and the trigger: "call when the user asks for a theme, a new look, colors from an image or a vibe; lint first, then hand the widget the palette; on a `rejected` result fix the named fields and call again".

`ToolLogic.BuildProposeTheme(JsonObject theme, string? saveAs, bool apply)`:

1. `ThemeLint.Lint(theme)`. Any Error finding: return `status: rejected`, `reason: invalid_theme`, `remedy: "Fix the fields named in findings and call again."`, `findings[]`. No file is written. This is the fast path an agent iterates against.
2. Validate `save_as` against the pattern, else `rejected` / `invalid_params`.
3. Write the request, wait up to 20 seconds polling the result at 250 ms (a theme apply is a few milliseconds on the widget; the wait covers a widget that is busy or starting).
4. Result present: return it as-is (the widget's typed payload). Timeout: `status: queued`, `reason: widget_not_responding`, remedy naming the widget, and the request stays for the widget's next tick within the 10-minute window.

`get_usage`, `ping`, and the rest are untouched. `ping` gains nothing; `tools/list` is seven tools.

### Widget side (`Sanduhr.App`)

`Services/ThemeHandoffService.cs`, constructed in `App.xaml.cs` beside `UsagePublishService`, sharing its 30-second tick **and** a `FileSystemWatcher` on `%APPDATA%\Sanduhr` filtered to `theme-request.json` so an interactive proposal lands within a second. The watcher's event debounces 100 ms and marshals to the dispatcher; the tick is the fallback when the watcher misses (it can, on network profiles).

On a request:

1. `ThemeLint.Lint(request.Theme)`. Errors: write `rejected` with findings (the server linted already, but the widget is the authority; the two lints are the same code).
2. Key = `save_as` or `Slugify(name)` (the Themes tab's existing slug rule). A key that collides with a built-in is rejected (`reason: reserved_name`); a key that collides with an installed user theme is overwritten (last proposal wins, same as Save & apply in the tab).
3. Write `themes\<key>.json` (indented, the palette as received), `ReloadUserThemes()`.
4. If `apply`: remember the active key as `previous_key`, `ApplyThemeByKey(key)`, set the widget `StatusText` to "Theme 'Name' applied from Claude Code" for eight seconds (the existing transient status slot; the previous theme is one tile away in the strip and the Settings swatch grid, and the result names it).
5. Write the result: `status: applied` or `saved`, `key`, `name`, `previous_key`, `saved_path`, `findings[]` (warnings ride along so the agent can offer a second pass).

No new consent dialog. The MCP install consent already gates the server's presence, and applying a theme is reversible in one click. The consent dialog's details text and `docs/PRIVACY.md`'s MCP row say the server can also propose a theme, which the widget saves under the themes folder and applies.

### Settings

Nothing new in the Publish tab. The Themes tab (piece 3) shows proposals in the installed list like any other user theme.

## Piece 3: Theme Studio

### Placement

Settings ▸ Themes, between the swatch grid and the installed list, replacing the paste box row with a two-mode panel: **Paste** (the current box, unchanged behavior, now showing lint findings under it before Save & apply) and **Studio** (the editor). A small segmented toggle switches modes; the mode is remembered in `settings.json` as `themes_editor_mode`.

The Settings window is 540 wide; the studio fits in two columns of seven rows at that width. Height grows: the window default goes 700 to 760.

### Editor

`ViewModels/ThemeStudioViewModel.cs`, owned by `ThemesViewModel`:

- Fourteen `ThemeTokenRow` entries (`Field`, `Label`, `Job` tooltip from the prompt's table, `Hex` two-way, `Swatch` brush derived from `Hex`, `HasError`). Typing a valid `#rrggbb` updates the swatch; anything else marks the row and leaves the last good color in place.
- Two sliders: `glass_alpha` (0.60 to 1.00) and `border_alpha` (0.10 to 0.80). The remaining dials stay JSON-only (Paste mode or the file); YAGNI.
- `Name` text.
- `Findings`: the lint result of the current state, re-run on every change (it is microseconds), shown as a compact list under the rows with the field name in accent.
- **Live preview is the widget.** Every valid change builds a `ThemePalette("studio", def)` and calls `WidgetViewModel.PreviewTheme(palette)`, which applies resources without changing the saved key or the strip's active tile. `Revert` (and closing Settings, and switching to Paste mode) calls `EndPreview()`, which re-applies the saved active theme. The studio never leaves the widget in a half state: preview is a resource swap, and the saved key is the truth.
- **Start from**: a dropdown of the strip's themes (built-ins and user). Loads that definition into the rows. Default: the active theme.
- **Save as** (filename, slug-autofilled from Name like Paste mode) writes `themes\<key>.json` with all fields the studio edits plus the untouched dials carried from the start-from theme, reloads, applies, ends preview. **Copy JSON** puts the same document on the clipboard (the round trip back to an agent: "here is what I have, make the accent warmer"). **Apply** without saving is not offered; an unsaved theme cannot be the active key. Preview covers that need.

`WidgetViewModel` gains `PreviewTheme(ThemePalette)` and `EndPreview()`; both are two-line wrappers around the existing `ApplyThemeResources` path with a `_preview` field. `ThemePalette.Hex` becomes tolerant: it goes through the lint parser and falls back to magenta `#ff00ff` on a bad value, so a preview mid-keystroke can never throw (lint marks the row anyway).

## Docs and surfaces touched

- `docs/themes/AGENT_PROMPT.md`: a short "or just ask Claude Code" section at the top pointing at `propose_theme`, and the rules list gains the measurable thresholds lint enforces (contrast 4.5, luminance 0.25, hue 30 degrees) so an agent can self-check.
- `README.md`: the tool list gains `propose_theme`; the Themes paragraph mentions Studio.
- `docs/PRIVACY.md`: the MCP registration row's last sentence: the server can propose a theme, which the widget saves under `%APPDATA%\Sanduhr\themes\` and applies; nothing is read.
- `CHANGELOG.md` Unreleased, three entries.
- `agent-access.json`: the seventh MCP affordance.
- `scripts/smoke-mcp.ps1`: `tools/list` expects seven; a `propose_theme` call with `apply: false` and a deliberately broken theme asserts `rejected` with a finding on the named field (no widget needed, no file written).
- The MCP consent dialog details text: one clause.

## Testing

- `ThemeLintTests` (Core): per-rule pass and fail, built-ins clean, parse error surfaces as a finding, `ToJson` shape.
- `ThemeHandoffTests` (Core): request round trip, expiry, malformed cleared, result atomic.
- `ToolLogicThemeTests` (MCP): rejected fast path with findings and no file; `save_as` pattern; request written with the right fields; result returned; timeout returns `queued`. The trust-boundary allowlist test updated for `ThemeModel.cs` and `ThemeLint.cs`.
- `ThemeHandoffServiceTests` (App, headless where possible): applied result carries `previous_key`; reserved name rejected; `apply: false` saves without switching.
- `ThemeStudioViewModelTests`: a bad hex marks the row and keeps the swatch; findings update; Save writes the expected JSON; Revert ends preview.
- Cold smoke on the published exe, as above.

## Out of scope

- Editing the Matrix-style overrides or `accent_bloom` in the studio (JSON only).
- Theme sync across machines, theme sharing, a gallery.
- Undo history in the studio beyond Revert.
- macOS. The lint rules are documented so the Swift port can match them later.

## Order of work

Lint first (everything else consumes it), then the handoff and tool (server side is testable without the widget), then the widget service, then the studio, then docs and the smoke. Each lands as its own commit on `feat/agentic-theming`; one PR.
