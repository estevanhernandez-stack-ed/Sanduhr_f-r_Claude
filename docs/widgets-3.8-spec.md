# Spec: Sanduhr 3.8 (Windows), widgets on the Desk, made with Claude

Status: draft for review, 2026-10-22. Scope set by Este the same day (626 dashboard decision
gEJQ8NCafJDh2SUnbgRN). BACKLOG #8 is the direction; this spec is the 3.8 slice of it.

3.8 is the next Store release: 3.7.2 ships on GitHub only, so the Store goes 3.7.1 to 3.8 and
everything users see lands here. Windows first; the Mac follows with the same pack format.

## 1. What ships

| # | Item | One line |
|---|---|---|
| A | **Widgets engine** | Sanduhr installs packs from the signed `sanduhr-widgets` catalog and draws them: sprites as Desk companions, themes, palettes, effects, fonts. |
| B | **Claude-made widgets** | `propose_widget`: Claude Code designs a pack in conversation (pixel sprite, palette, effects); it previews on the Desk and installs only on Apply. |
| C | **Your own art** | A user adds their own PNG sprite sheet to a widget; Claude never supplies one. |
| D | CI watchers | Cards for jobs this machine's self-hosted runners run (dashboard task, held from 3.7). |
| E | Weather nickname | "Show it as" for the weather's place (#315). |
| F | Desk fold | A per-stack slant, the affine half of #314. |
| G | Livelier bars | Smoothing, falloff, peak caps and gradients from the level we already read. No audio capture. |
| H | Rainmeter spike | Parse Este's Knighthawk skins and measure what maps. A report, not a feature. |
| I | Guided setup skill | A `sanduhr-mods` Claude Code skill that runs Desk setup as a conversation. Not in the app. |
| J | **Restart to update** | With the user's OK, Sanduhr closes its WebView2 helpers, asks the Store to install now and relaunches on the new version. |

## 2. Trust and signing

**The catalog signature is ECDSA P-256 / SHA-256, not Ed25519.** `sanduhr-widgets/docs/FORMAT.md`
section 7 says Ed25519, but .NET has no Ed25519 in `System.Security.Cryptography` before .NET 11
and Sanduhr is on .NET 10. RoRoRo (`RobloxCompatSignature`) and the 626 Mod Launcher
(`ManifestSignature`) already settled this: P-256, `DSASignatureFormat.IeeeP1363FixedFieldConcatenation`
(fixed 64-byte signatures), the public key pinned in the binary as SubjectPublicKeyInfo, the private
key only in CI. Nothing is signed or released yet (no releases, no `SIGNING_KEY` secret), so the
switch is free:

- `sanduhr-widgets`: FORMAT.md section 7, `tools/sign_catalog.sh` (OpenSSL 3, `ec` P-256,
  `dgst -sha256`, converted to P1363), the CI release job, and a verify test.
- One key per trust surface: a **new** keypair, not RoRoRo's or the Mod Launcher's.
- **Este generates it** (the private key never touches this conversation or the repo):
  `openssl ecparam -name prime256v1 -genkey -noout -out sanduhr-widgets-signing.pem`, then
  `openssl ec -in sanduhr-widgets-signing.pem -pubout -outform DER | base64` for the SPKI that
  Sanduhr pins. The PEM goes into the repo secret `SIGNING_KEY`.
- Rotation: FORMAT's `.sig2` overlap is kept (a release that pins both keys).

**Client rules** stay as FORMAT section 7 lists them, in order: verify before parsing, reject a
rollback by `generated_at`, check `sanduhr_min`, `size` and `sha256` before opening the zip,
extract safely, **re-validate the pack locally**, install atomically.

**One validator, three callers.** `tools/validate.py` (1,045 lines) is ported to C# as a pure
`Sanduhr.Core` `PackValidator` with the same rules and the same test fixtures (`make_fixtures.py`
cases become xUnit cases). The app runs it on every install; the MCP server runs it on every
`propose_widget` before anything is written. Linking it into `Sanduhr.Mcp` widens
`TrustBoundaryTests`' allowlist, which is a design decision that must break CI: the validator is
pure (no IO, no network, no process), so it qualifies, and the test is updated deliberately.

**Network.** The app (never the MCP process) fetches `catalog.json`, `.sig` and pack zips from
`github.com/626Labs-LLC/sanduhr-widgets/releases`, only when the user opens Settings ▸ Widgets or
installs. **That is a new network destination**: `docs/PRIVACY.md`, the hosted privacy page (hub
issue) and the 3.8 reviewer letter say so before the Store submission.

## 3. Packs on disk

- Installed packs: `%LOCALAPPDATA%\Sanduhr\widgets\<id>\` (the app's own files, so package
  virtualization is fine), one folder per pack, plus `widgets.json` (id, version, source:
  `catalog` | `claude` | `local`, installed_at, enabled).
- Local packs (Claude-made or the user's) are **unsigned and local-only**, installed with the
  user's Apply and listed as "made here". Sharing one to the catalog is a pull request to
  `sanduhr-widgets`, where CI validates it like any other; nothing uploads from the app.
- Uninstall removes the folder and its row. Reset removes the widgets folder.

## 4. Sprites on the Desk (A)

A new Desk piece, `companion`, placed like any other (`layout.companion`), drawing the enabled
sprite pack. Behaviour is Sanduhr's (FORMAT section 6), the pack supplies art and mapping:

- **Render:** frames cut from the stage sheet, drawn with nearest-neighbour scaling at `scale`,
  at `fps`, anchored bottom-centre. It roams the chosen `edges` of its monitor at `speed_px_s` and
  sits at its anchor otherwise. OLED care's drift and dimming apply to it.
- **Growth** (0 to 1, never backwards, never from tokens): from Sanduhr Time's day files when
  Sanduhr Time is installed (`~\.sanduhr\time\days`: coding days and total active time); otherwise
  from days with Claude Code activity in the local session logs the app already reads. The curve
  is a constant in `Sanduhr.Core`, pinned by tests.
- **Mood** from the widget's own pacing (under, near, over pace, at the limit, the reset).
- **Triggers:** `needs_you` from a watcher waiting on you, `job_passed` from a watcher or CI job
  finishing green, `near_limit` from the existing alert thresholds, `idle` after 20 minutes with no
  activity.
- Reduce motion: no roaming, frame animation at 1 fps.
- First-party packs at launch: the `hourglass` sprite (four stages) and the OLED Neon theme,
  palette and effects packs, already built and validated in `sanduhr-widgets`.

Themes, palettes, effects and fonts from packs join the existing Themes strip, the Desk ink
gradient list, the effects list and the font list respectively.

## 5. Claude-made widgets (B)

**`propose_widget`** on the MCP server, beside `propose_desk`, with the same shape of flow:

- Input: a whole pack as data: `pack` (the `pack.json` fields; `id` is namespaced `my-<id>`),
  plus the kind's file contents: `sprite` (sprite.json with **pixel sheets**, below), `palettes`,
  `effects`, or `theme` (which already has `propose_theme`; reuse it).
- The server runs `PackValidator` and the trademark rule (no Claude, Anthropic or model names,
  FORMAT rule 6) and returns `rejected` with reasons, exactly like `propose_desk`.
- Otherwise it hands the pack to the widget through `widget-request.json` / `widget-result.json`
  in `%USERPROFILE%\.sanduhr` (the design handoff's pattern). The widget previews it on the Desk
  with the review bar (Apply / Dismiss, "What this widget is", the file list), and installs it as a
  local pack only on Apply. "Let Claude change the Desk directly" does **not** cover widgets: a new
  pack always waits for Apply.

**Pixel sheets: art Claude can draw as data.** FORMAT v1 sprites are PNG only, and Claude never
supplies an image. 3.8 adds one sheet type, so `sprite.json` becomes `schema_version: 2` (v1 stays
valid):

```json
{ "palette": ["#22d3ee", "#a855f7", "#ec4899"], "frame_w": 16, "frame_h": 16,
  "frames": [ ["....0000....", "...011110...", "..."] ] }
```

- `palette`: 1 to 16 `#rrggbb`, no `#ffffff` (FORMAT's art rule, now enforced).
- Each frame: exactly `frame_h` strings of exactly `frame_w` characters; `.` is transparent and
  `0`-`9`, `a`-`f` index the palette. Frame sizes 8 to 32 (smaller than PNG sheets: pixel art
  sized for a companion).
- A stage's `sheet` may be a `.json` pixel sheet instead of a `.png`. Sanduhr rasterizes it at
  install; the validator checks dimensions, characters, palette range and frame counts exactly.
- `validate.py` and `PackValidator` both learn v2; CI and the app reject a malformed sheet.

What Claude may propose: **sprite** (pixel sheets), **palette**, **effects**, **theme**. Not fonts
(binaries) and not PNGs.

## 6. Your own art (C)

Settings ▸ Widgets ▸ a local sprite pack ▸ **Use my own art…**: the user picks a PNG sprite sheet
for a stage. Sanduhr checks it with the pack image rules (PNG signature and chunks, at most 2 MB and
4096 px, dimensions an exact multiple of the cell size, cells for every frame the animations use),
copies it into the pack and re-validates the whole pack. The Launcher's image picker already works
this way. A user PNG makes the pack local-only; it is never sent anywhere.

## 7. The ride-alongs (D to G)

- **D. CI watchers:** per the dashboard task: read this machine's self-hosted runner containers'
  stdout through the Docker Engine named pipe (no GitHub token, no network), "Running job: <name>"
  starts a card, "Job <name> completed with result: …" ends it (glow on failure). Settings ▸ Desk
  "Show my CI runners' jobs", off by default. First capture real job lines from a Neb run for
  fixtures. Feeds the companion's `job_passed`.
- **E. Nickname (#315):** Settings ▸ Weather "Show it as", 24 characters, local only;
  `propose_desk` gains `weather.nickname` under the same review.
- **F. Fold (#314):** `layout.<piece>.tilt` from -20 to 20 degrees, a `SkewTransform` on the
  stack's `LayoutTransform` beside the existing scale. Arrange Desk hit-testing follows the
  transform. Perspective (`Viewport3D`) stays out. As built: the tilt is the stack's (one slant per
  anchor; a piece joining a stack takes it; two different tilts for one stack in a design are
  refused), stored as a fourth field of the layout word (`clock:ml:1:-18`, written only when not 0).
- **G. Livelier bars:** per-bar smoothing, a falloff, peak caps and a gradient ink, all from the
  existing session peak level. The privacy promise "a level only: no audio is captured" stands.
- **Meter styles (#336), as built:** `layout.<piece>.style` on `claude`, `meters` and `telemetry` only:
  `bar` (default, unchanged), `slim` (label and value on one line over a thin bar, about half the
  height; the Claude line becomes two short rows), `ring` (small arc gauges, one piece's readings side
  by side) and `ticks` (ten cells, the OLED-friendly one). Pace marks stay on every Claude limit. A
  fifth field of the layout word after the tilt (`meters:tr:1:0:ring`), written only when not `bar`;
  older builds read the size and tilt and draw bars. A style is per piece, the tilt per stack.
  `get_desk` reports styles and `limits.piece_style`; Settings, Desk has a Style picker per piece.

## 8. Rainmeter spike (H) and the setup skill (I)

- **H.** Parse `Knighthawk` (Este's suite: `OneDrive\Documents\Rainmeter\Skins\Knighthawk`) with
  variables and `@include` resolved; classify every meter and measure into tier 1 (look: fonts,
  colours, images, positions), tier 2 (safe meters and measures that map to Desk pieces) or tier 3
  (Lua, plugins, bangs that run programs, WebParser: never). Output: a report with the fractions
  and a recommendation for 3.9. No code ships in the app.
- **I.** A `sanduhr-mods` skill (`desk-setup`): reads `get_desk`, asks what the person wants to see
  and where, proposes with `propose_desk`, iterates on the live preview, then offers the widget
  (`propose_widget`) or mod that would finish it. It ships with the mods marketplace, not the app,
  so it can change daily.

## 8b. Restart to update (J)

Today `StoreUpdateService` notices a waiting Store update and offers **Quit and update**: it quits
and leaves the install to the Store. That fails two ways: quitting can orphan a WebView2 helper,
which keeps the package "running" and makes the install fail with 0x80073D02, and the Store
installs whenever it gets around to it.

3.8 replaces it with **Restart to update**, with the user's approval:

1. The toast and the tray say "Sanduhr 3.x is ready. Restart to update?" Nothing happens without
   a click.
2. On Yes: dispose both WebView2 environments (`webview2`, `webview2-fetch`) and wait, bounded,
   for `BrowserProcessExited` (the 3.7.2 fix); register for restart (`RegisterApplicationRestart`);
   then `StoreContext.RequestDownloadAndInstallStorePackageUpdatesAsync`, which shows Windows' own
   consent dialog, installs, and closes the app.
3. The new version starts on its own and shows its What's New.
4. If the Store call fails or the user cancels, Sanduhr stays running and the tray keeps the offer.

The Velopack channel restarts itself (`ApplyUpdatesAndRestart`); it gets the same WebView2
teardown first, so both channels behave the same. Before 3.8 `UpdateChecker` only logged a newer
release, so 3.8 also downloads it in the background and joins the same offer and button.

Built as `RestartToUpdate` (Core: the order and the undo, tested against fakes) and
`RestartToUpdateService` (App: toast, tray, the widget's notice). Proving it end to end needs a real
Store update from a 3.8 build to a 3.8.x: see `docs/release-runbook.md`, Phase 7.

**The 3.7.1 to 3.8 hop can't use this.** That install happens while 3.7.1's code runs, and 3.7.1
only has Quit and update. So 3.8's Store release notes keep the known-issues line: *"If the update
won't install (error 0x80073D02), restart Windows and try again."* It comes out of the notes in the
first release after 3.8.

## 9. Store and privacy, before submission

- `docs/PRIVACY.md` + the hosted page (hub issue): the widgets folder, `widget-request.json` /
  `widget-result.json`, local packs, the catalog fetch from GitHub releases, CI watcher reads of
  local Docker logs.
- Reviewer letter 3.8.0.0: data-only packs (no code), signature check, local re-validation, Apply
  before install, the new network destination, CI watchers off by default.
- 10.1.4.4: first-party packs carry no Anthropic marks (the validator enforces it); trademark
  disclaimer unchanged.
- Store screenshots: the companion on the Desk joins the set from 2026-10-22.

## 10. Order of work

1. `sanduhr-widgets`: P-256 signing (format, script, CI, test) and sprite v2 pixel sheets in
   `validate.py` with fixtures. Este generates the key; a v1.0.0 signed release of the existing
   packs.
2. `Sanduhr.Core` `PackValidator` (port, same fixtures) and pixel-sheet rasterizer.
3. Catalog client + install/uninstall + Settings ▸ Widgets (A).
4. The `companion` piece: render, roam, growth, mood, triggers (A).
5. `propose_widget` + handoff + review bar + local packs (B), then Use my own art (C).
6. D, E, F, G in parallel once 3 lands (independent files).
7. H (spike report) and I (the skill) alongside; neither blocks the release.
8. Privacy page, reviewer letter, screenshots, release runbook phases 1 to 8.

## 11. Open questions

- Growth curve: how many coding days to the last stage? Draft: stage 2 at 3 days, 3 at 10, 4 at
  30, so a daily user sees the full hourglass in about a month.
- Should a Claude-made sprite be able to replace the first-party hourglass as the companion, or
  sit beside it? Draft: one companion at a time, chosen in Settings ▸ Widgets.
- Catalog refresh: on opening Settings ▸ Widgets only, or also once a day for update badges?
  Draft: on open only (fewer network calls to disclose).
