# Widgets on the Mac (parity with Windows 3.8)

Spec, 2026-10-27. Draft for the owner's review. Dashboard task: "Mac: parity with Windows 3.8 (widgets,
companion, propose_widget, Desk upgrades)". Windows reference: `docs/widgets-3.8-spec.md`, as built in
`windows-dotnet/` (read it, never edit it). BACKLOG #8 is the direction.

## Where the Mac starts

- **The catalog is live.** `626Labs-LLC/sanduhr-widgets` v1.0.0 is a signed `catalog.json` with four
  packs:
  - `hourglass`: a sprite with four stages.
  - `oled-neon`: a theme.
  - `oled-neon-palettes` and `oled-neon-effects`.
  It is signed with ECDSA P-256 and Key A, which Windows pins in `WidgetCatalog.PinnedKeys`.
- **Mac theme packs drop in.** `ThemeLint.colorFields` matches the catalog's theme fields one for one.
- **Sanduhr Time writes its day files on the Mac too:** `~/.sanduhr/time/days/*.json` (from the VS
  Code extension).
- **What the Mac has to build on:**
  - the Desk piece system: `DeskLayout.widgets`, `DeskView.Widget`, `DeskPiece` and
    `DeskArrangement`'s `widget:anchor[:scale]` words;
  - the Claude handoff pattern: request and result files in `~/Library/Application Support/Sanduhr`,
    `HandoffWatch`, a banner to review, and an off-by-default direct switch;
  - `CCLogReader`'s activity days;
  - watchers and alerts, and `UsageMath.paceInfo`;
  - the idle-CPU rule: Core Animation layers, not redraw loops (`BreathLayerView`).
- **What it doesn't have:**
  - a pack validator in Swift;
  - P-256 signature checks (CryptoKit is only used for SHA-256 today);
  - a stored-zip reader;
  - OLED care, `get_desk` and `propose_desk`;
  - Windows' extras: fold, weather, livelier bars, meter styles and CI watchers.

## Scope

Three releases, each shippable on its own.

| Release | What | Windows ref |
|---|---|---|
| **2.35.0: the catalog and the companion** | Settings ▸ Widgets, the signed catalog, install, update and uninstall, the `companion` Desk piece, palette and theme packs joining the Mac's lists. Effects packs install and list but draw nothing, as on Windows. | spec A |
| **2.36.0: Claude-made widgets** | `propose_widget` in the Mac MCP, the review banner with a live Desk preview, local "made here" packs. | spec B |
| **2.36.x or 2.37.0: your own art** | "Use my own art…" for made-here sprites. | spec C |

**Out of scope:**
- Windows' 3.8 and 3.9 extras: fold, weather nickname (the Mac has no weather piece), livelier bars,
  meter styles and CI watchers. Each is its own backlog item.
- Font packs. None are in the catalog, and they'd need process font registration and a license
  check.
- OLED care. The Mac has none, so the companion runs without it.
- Pushing anything to the catalog from the app.

## 1. Trust: the same rules as Windows, in Swift

- **Catalog signature.** `CryptoKit.P256.Signing.PublicKey(derRepresentation:)` from the pinned
  SubjectPublicKeyInfo (Key A, the same bytes as Windows; a list, so `.sig2` rotation works), and
  `ECDSASignature(rawRepresentation:)` for the 64-byte P1363 signature.
  - The signature is verified before `catalog.json` is parsed.
  - Rollback: a `generated_at` older than the newest one seen is refused.
  - Limits: catalog 1 MB, signature 1 KB, at most 1000 packs.
- **Downloads.**
  - Only `https://github.com/626Labs-LLC/sanduhr-widgets/releases/latest/download/` for the catalog
    and its signatures.
  - Pack URLs must start with `.../releases/download/`.
  - Redirects are followed by hand: at most 5, https only, and only to `github.com`,
    `objects.githubusercontent.com` and `release-assets.githubusercontent.com`.
  - An ephemeral `URLSession` with no cookies and the user agent `Sanduhr`.
  - Timeouts: 20 s for small files, 90 s for zips.
  - A zip is capped at its catalog `size`, then its SHA-256 is compared in constant time.
- **Zip.** A port of `WidgetZip`: stored entries only, never inflated.
  - Refused: compressed or encrypted entries, data descriptors, ZIP64, symlinks, directories,
    unsafe, non-ASCII or case-colliding names.
  - The local and central headers must agree, and the CRC must check.
  - Limits: 67 entries, 5 MB per entry, 10 MB total.
- **One validator, ported twice, one fixture corpus.**
  - **The MCP (Python)** vendors `sanduhr-widgets/tools/validate.py` unchanged, with its version
    pinned in a header comment. The Python validator is the original, and the Mac MCP is already
    Python.
  - **The app** gets `PackValidator.swift`, a port of the same rules (Windows'
    `PackValidator.cs` is the line-by-line guide).
    - Messages are byte-equal, so both sides report the same reasons.
    - Covered: the manifest, files, banned extensions and magic bytes, LICENSE, PNG (full chunk,
      CRC and IDAT checks via the Compression framework's zlib), sprite v1/v2, pixel sheets,
      palettes, effects, themes (through the existing `ThemeLint`), and the trademark rule.
  - **Fixtures:**
    - Copy `tools/fixtures` (8 good, 42 bad, plus the 4 real packs) and `python-expected.tsv` into
      `mac/Tests/SanduhrTests/fixtures/widget-packs/`, as Windows did.
    - A Swift test asserts parity with the expected file.
    - A Python test runs the vendored `validate.py` on the same fixtures.
  - The app validates on every install, and again after "Use my own art…".
- **The MCP stays network-free and propose-only.** Validation is pure. Downloads and installs
  happen only in the app.

## 2. Packs on disk

- **Location:** `~/Library/Application Support/Sanduhr/widgets/<id>/`, plus `widgets/widgets.json`:
  `{schema_version: 1, catalog_generated_at, packs: [{id, version, source: catalog|claude|local,
  installed_at, enabled}]}`, the same shape as Windows'.
- **Install:** unpack into `widgets/.staging/<id>/`, validate, rasterize pixel sheets into
  `.raster/<sheet>.png` with Core Graphics, then rename into place. An id keeps its source.
- **Uninstall** removes the folder and its row.
- **Reset Sanduhr…** gets a `Widgets` part of its own, next to Themes, so it shows as its own
  checkbox and isn't swept silently with "records".
- `ResetGate` blocks writes after a reset, as it does for every writer.

## 3. Settings ▸ Widgets (2.35.0)

A new page in the Desktop group, after Lock Screen.
- **The catalog**, shown with Install or Update. It's fetched only when the page opens or on
  "Check the catalog again"; nothing polls. A failure is one quiet line.
- **Installed widgets:** kind, version and source ("from the catalog" or "made here"), with
  Uninstall.
- **Companion** picker: one companion at a time. With no pick, the first installed sprite by id.
- **Let it roam**, on by default.
- **Pack themes** join the Themes strip.
- **Pack palettes** join `MessagePalette.all`, so the message editor's and now-playing's gradient
  lists gain them, named as in the pack.
- **Effects packs** are listed as installed only, with the note "Effects packs don't draw yet", as
  on Windows.

## 4. The companion piece (2.35.0)

**Placement.** A new Desk piece, `companion`, added the usual way:
- `DeskLayout.widgets`, `DeskView.Widget` and `DeskPiece`;
- default anchor `bc`, its scale from the layout word;
- the Settings preview, the debug snapshot and tests.
It isn't hit-testable.

**Render.**
- A `CALayer` whose `contents` is the stage's sheet (`magnificationFilter = .nearest`).
- Frames are stepped by a discrete `CAKeyframeAnimation` on `contentsRect` at the sprite's fps, so
  the render server animates it and the app's CPU stays out of the loop. This follows the
  `BreathLayerView` pattern.
- Bottom-centre anchoring, cell size × pack scale × piece scale.

**Pausing.**
- It pauses (`speed = 0`) whenever `MessageMotion.paused` holds: screensaver, sleep, session or
  occlusion.
- Reduce Motion (`NSWorkspace.accessibilityDisplayShouldReduceMotion`) means no roaming and 1 fps.

**Roam.**
- Walks the pack's edges of the Desk's screen: out and back along the bottom, hopping between the
  side edges when the pack has no bottom edge.
- 12 s walking, 8 s resting.
- It holds still while asleep (exhausted) and while a trigger plays.
- The motion is a `CABasicAnimation` on `position` inside the Desk window. No extra window.

**Growth.** Piecewise linear through (0, 0), (3, 0.10), (10, 0.35), (30, 0.75), (60, 1) coding
days. This is Windows' `CompanionGrowth` curve, pinned by tests.
- The days come from `~/.sanduhr/time/days/*.json` when it's present; otherwise from the dates of
  Claude Code activity in `CCLogReader`.
- `widgets/companion.json` keeps the highest growth reached, so it never goes backwards.
- It's re-read once an hour.
- Never from tokens.

**Mood**, from the active account's limits, as Windows' `CompanionMoods`:

| Mood | When |
|---|---|
| exhausted | any limit at 100% or more |
| worried | weekly at 85% or more, or 5 or more points ahead of pace (`paceInfo`) |
| focused | within 5 points of pace |
| content | otherwise, and when signed out |
| celebrate | for 10 minutes after a weekly limit, or one that was at 100%, resets |

**Triggers:**

| Trigger | Fires when |
|---|---|
| needs_you | a watcher goes to `waiting` |
| job_passed | a watcher goes to `passed` |
| near_limit | a limit crosses the Alerts line or 100% upward (`AlertSettings`) |
| idle | no input for 20 minutes: `CGEventSource.secondsSinceLastEventType(.combinedSessionState, ...)`, checked every 5 s only while the Desk is showing. It needs no permission. |

**Demo mode** shows the hourglass at a fixed stage with sample moods. It never reads real growth.

**Debug state.** `state.yaml` gains `companion: {pack, stage, mood, roaming}` (ids and enums only).

## 5. Claude-made widgets (2.36.0)

**`propose_widget` in `mac/integrations/sanduhr_mcp.py`**, with the same input schema and guide
text as Windows' `ToolCatalog.cs` `propose_widget`:
- `kind`: sprite, palette or effects. A theme redirects to `propose_theme`; fonts are refused.
- `pack` fields: the id is namespaced `my-`; default license CC0-1.0 and default version 1.0.0.
  `sanduhr_min` is the first Mac version with widgets (2.36.0), or, if the catalog format's
  `sanduhr_min` is shared across platforms, the shared rule. Owner call below.
- The tool runs the vendored `validate.py` plus the trademark rule, and returns `rejected` with
  reasons before writing anything. Otherwise it writes `widget-request.json` atomically
  (`{schema_version, id, requested_at, pack_id, note?, files: {path: text}}`, at most 4 MB) and polls
  `widget-result.json` the way the other proposals do (`queued` when the app doesn't answer).

**The app** takes the request through `HandoffWatch`, re-validates it in Swift, and holds it in
memory only (a newer one replaces it; requests older than 600 s are dropped).
- It posts the existing suggestion notification and shows a **WidgetSuggestionBanner** in Settings
  ▸ Widgets. The banner has:
  - the name, Claude's note, "What this widget is", the files and their license, and "made here,
    on this Mac only";
  - for a sprite, **Make it the companion**;
  - **Preview on the Desk**, which puts the sprite in as the companion temporarily, or the palette
    as the Desk ink;
  - **Apply** and **Dismiss**.
- Result statuses: `previewing`, `applied` (with `companion`), and `rejected` (reasons `dismissed`,
  `replaced`, `busy`, `install_failed`, `not_ready`).
- **No direct switch covers widgets.** A new pack always waits for Apply.

## 6. Your own art (2.36.x or 2.37.0)

On a made-here sprite, **Use my own art…** opens an `NSOpenPanel` for one PNG per stage.
- It's checked with the pack image rules: PNG signature and chunks, at most 2 MB and 4096 px, an
  exact multiple of the cell, and cells for every frame used.
- The PNG is copied to `art/<stage>.png`, the patch version is bumped, the whole pack is
  re-validated, and it's reinstalled as `local`.
- A user's PNG is never sent anywhere.

## 7. Privacy, docs and release

- **`docs/PRIVACY.md`, Mac section, for 2.35.0.**
  - The new outbound destination: GitHub releases of `626Labs-LLC/sanduhr-widgets`, only when Settings
    ▸ Widgets opens or on Install or Update. Name the three redirect hosts.
  - No account, cookie or identifier is sent, and the user agent is `Sanduhr`.
  - The widgets folder, `widgets.json` and `companion.json`, and the companion's reads of Sanduhr Time
    day files and Claude Code activity dates (dates only), and the idle check (seconds since input,
    never what).
  - **For 2.36.0:** the handoff files.
  - The hub's privacy page gets the same rows (hub issue) before each release.
- **Docs:** `mac/README.md` Widgets and Companion sections; `docs/mac-smoke-test.md` sections for the
  catalog, install, the companion, and propose_widget.
- **Release:** each release gets a What's New card (Widgets, The companion, Claude-made widgets) and
  its CHANGELOG, as usual.
- **The ship check this time:** every new file, preference, host and helper has its PRIVACY
  paragraph in the same release. This is the 2.33.0 lesson.

## 8. Order of work (2.35.0)

1. The fixture corpus and `PackValidator.swift`, to byte-equal parity. This is the largest item.
2. `WidgetZip` (stored-only reader), `WidgetCatalog` (signature, rollback, limits), the download
   client with the redirect rules, and the Core Graphics pixel-sheet rasterizer.
3. `WidgetStore` (staging, install, uninstall, `widgets.json`, Reset part) and Settings ▸ Widgets.
4. The `companion` piece: render, roam, growth, mood, triggers, demo and debug state.
5. Pack themes and palettes join their lists. Effects are listed only.
6. PRIVACY, README, smoke tests, What's New card, CHANGELOG.

Items 1 and 2 are pure and testable on their own, so they're suited to an unattended run. Items 3
and 4 need live checks on the Desk.

## Owner calls

1. **Three releases, or one big one?** Recommended: three. 2.35.0 alone already adds the first new
   network destination since Sparkle.
2. **The validator: port it to Swift, or call the vendored `validate.py` from the app?** Recommended:
   Swift. The app can't rely on Python at install time; the Mac MCP can, since it already needs
   Python.
3. **Reset.** Recommended: a Widgets checkbox of its own, as above.
4. **`sanduhr_min` for made-here packs.** Recommended: Windows' rule, with the Mac's version number
   when the pack is made on a Mac. Check how the catalog treats `sanduhr_min` across platforms
   before building B.
5. **OLED care.** Leave it out now, or bring Windows' drift and dim to the whole Mac Desk as its
   own item first? Recommended: leave it out; the Mac has never had it.
