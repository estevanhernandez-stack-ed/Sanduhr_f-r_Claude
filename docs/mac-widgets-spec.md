# Widgets and the Desk on the Mac (parity with Windows 3.8 and 3.9)

Spec, 2026-10-27, revised the same day with the owner's calls (end of this file). Dashboard task: "Mac:
parity with Windows 3.8 (widgets, companion, propose_widget, Desk upgrades)". Windows references:
`docs/widgets-3.8-spec.md` and `docs/ci-watchers-3.9-spec.md`, as built in `windows-dotnet/` (read
it, never edit it). BACKLOG #8 is the direction.

## Where the Mac starts

- **The catalog is live.** `626Labs-LLC/sanduhr-widgets` v1.0.0 is a signed `catalog.json` with four
  packs:
  - `hourglass`: a sprite with four stages.
  - `oled-neon`: a theme.
  - `oled-neon-palettes` and `oled-neon-effects`.
  It is signed with ECDSA P-256 and Key A, which Windows pins in `WidgetCatalog.PinnedKeys`. Every
  pack's `sanduhr_min` is `3.8.0`, a Windows version number (section 1).
- **Mac theme packs drop in.** `ThemeLint.colorFields` matches the catalog's theme fields one for one.
- **Sanduhr Time writes its day files on the Mac too:** `~/.sanduhr/time/days/*.json` (from the VS
  Code extension).
- **What the Mac has to build on:**
  - the Desk piece system: `DeskView.Widget` (`clock`, `claude`, `meters`, `nowPlaying`, `watchers`,
    `meetings`, `message`), `DeskPiece` and `DeskArrangement`'s `widget:anchor[:scale]` words;
  - the Claude handoff pattern: request and result files in `~/Library/Application Support/Sanduhr`,
    `HandoffWatch`, a banner to review, and an off-by-default direct switch;
  - `CCLogReader`'s activity days;
  - watchers and alerts, and `UsageMath.paceInfo`;
  - the idle-CPU rule: Core Animation layers, not redraw loops (`BreathLayerView`);
  - the bundled fonts and their process registration (`BundledFonts`).
- **What it doesn't have:**
  - a pack validator in Swift, P-256 signature checks (CryptoKit is only used for SHA-256 today) and
    a stored-zip reader;
  - OLED care;
  - `get_desk` and `propose_desk` (the Mac MCP has only the message tools);
  - a system piece and a weather piece;
  - level bars: the Mac's now-playing line has a position bar only;
  - any Docker reader.

## Scope

Four releases, each shippable on its own. Nothing from Windows 3.8 or 3.9 is left out.

| Release | What | Windows ref |
|---|---|---|
| **2.35.0: the catalog, the companion and OLED care** | Settings ▸ Widgets, the signed catalog, install, update and uninstall, the `companion` Desk piece, theme and palette packs joining the Mac's lists, effects packs listed only (as on Windows). OLED care for the whole Desk and the widget. | 3.8 spec A, 3.7 OLED care |
| **2.36.0: Claude-made widgets and your own art** | `propose_widget` in the Mac MCP, the review banner with a live Desk preview, made-here packs, and "Use my own art…". | 3.8 spec B and C |
| **2.37.0: the Desk, shaped by hand or by Claude** | `get_desk` and `propose_desk` in the Mac MCP, the fold (a per-stack slant), meter styles (bar, slim, ring, ticks), and font packs. | 3.8 E to G, #314, #336 |
| **2.38.0: new pieces** | The system piece (CPU, RAM, disk), the weather piece with its nickname, livelier now-playing bars (owner call below), and CI watchers from local runners. | 3.6 system and weather, 3.8 G, 3.9 |

Out of scope: pushing anything to the catalog from the app, and the Rainmeter spike (a report on
Windows skins, not a feature).

---

## Release 2.35.0

## 1. Trust: the same rules as Windows, in Swift

- **Catalog signature.** `CryptoKit.P256.Signing.PublicKey(derRepresentation:)` from the pinned
  SubjectPublicKeyInfo (Key A, the same bytes as Windows; a list, so `.sig2` rotation works), and
  `ECDSASignature(rawRepresentation:)` for the 64-byte P1363 signature.
  - The signature is verified before `catalog.json` is parsed.
  - Rollback: a `generated_at` older than the newest one seen is refused.
  - Limits: catalog 1 MB, signature 1 KB, at most 1000 packs.
  - The catalog parser is as strict as Windows': entry keys exactly `id, kind, name, sanduhr_min,
    sha256, size, url, version`.
- **`sanduhr_min` is a pack-format level, not the Mac's version.** The catalog numbers it in Windows
  versions (`3.8.0`), and Windows' parser refuses any extra entry key, so the catalog can't carry a
  Mac field without breaking every Windows 3.8 install. The Mac therefore compares `sanduhr_min`
  with `PackFormat.supported`, a constant in Windows numbering (`"3.8.0"` in 2.35.0), never with
  `CFBundleShortVersionString`. A pack that needs a newer format shows "Needs a newer Sanduhr" and
  doesn't install. The constant moves only when the Mac gains a newer format's features, with a test
  that pins it. Same `MeetsMinimum` rule: a minimum that isn't MAJOR.MINOR.PATCH is never met.
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
- **One validator, ported to Swift, one fixture corpus.**
  - **The MCP (Python)** vendors `sanduhr-widgets/tools/validate.py` unchanged, with its version
    pinned in a header comment.
  - **The app** gets `PackValidator.swift`, a port of the same rules (Windows' `PackValidator.cs` is
    the line-by-line guide). The app never runs Python.
    - Messages are byte-equal, so every side reports the same reasons.
    - Covered: the manifest, files, banned extensions and magic bytes, LICENSE, PNG (full chunk,
      CRC and IDAT checks via the Compression framework's zlib), sprite v1/v2, pixel sheets,
      palettes, effects, themes (through the existing `ThemeLint`), fonts (for 2.37.0, ported now so
      parity is whole), and the trademark rule.
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
- **Reset Sanduhr…** gets a `Widgets` part of its own, next to Themes, shown as its own checkbox.
- `ResetGate` blocks writes after a reset, as it does for every writer.

## 3. Settings ▸ Widgets

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

## 4. The companion piece

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
- OLED care's drift and dim apply to it (section 5).

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
| idle | no input for 20 minutes |

**Idle, shared with OLED care.** One `IdleClock` reads
`CGEventSource.secondsSinceLastEventType(.combinedSessionState, ...)` (it needs no permission and
learns only how long ago, never what), every 5 s and only while the Desk is showing.

**Demo mode** shows the hourglass at a fixed stage with sample moods. It never reads real growth.

**Debug state.** `state.yaml` gains `companion: {pack, stage, mood, roaming}` (ids and enums only).

## 5. OLED care

A port of Windows' `OledCare` (3.7.0): Settings ▸ Desk, **OLED care**, off by default (`deskOledCare`).
The caption is Windows': "For OLED screens: the Desk drifts a few pixels every few minutes, dims
when you're away, and never holds pure white." macOS can't reliably tell an OLED panel either, so it
stays a switch.
- **Drift.** `OledDrift`'s figure-eight: 16 points, `x = 6 sin(kπ/8)`, `y = 6 sin(kπ/4)`, rounded,
  one step every 3 minutes, a 48-minute loop that averages to the anchor. The Desk moves each
  piece's layer by the offset (in points; Windows' pixels at 1×), never re-lays it out, and the
  clamp keeps it clear of the menu bar, the notch and the Dock (`DockClearance`). A piece already
  outside that area is never pulled in. The companion and the lock screen line drift with it; the
  notch island doesn't, since it sits on a black notch.
- **Dim.** After 5 minutes without input (the shared `IdleClock`), the Desk and the widget dim, easing
  in; the first input wakes them. Windows' `OledIdle` thresholds and edges, pinned by tests.
- **No pure white.** `DeskInk.capWhite`: with OLED care on, the Desk ink, message gradients and
  now-playing ink are capped at Windows' off-white.
- A one-step move every 3 minutes is a single implicit layer animation: no timer runs between steps.

---

## Release 2.36.0

## 6. Claude-made widgets

**`propose_widget` in `mac/integrations/sanduhr_mcp.py`**, with the same input schema and guide
text as Windows' `ToolCatalog.cs` `propose_widget`:
- `kind`: sprite, palette or effects. A theme redirects to `propose_theme`; fonts are refused.
- `pack` fields: the id is namespaced `my-`; default license CC0-1.0 and default version 1.0.0.
  `sanduhr_min` is written by Sanduhr as Windows' `WidgetProposal.SanduhrMin` (`3.8.0`), the
  pack-format level of section 1, so a pack made on either platform installs on both.
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

## 7. Your own art

On a made-here sprite, **Use my own art…** opens an `NSOpenPanel` for one PNG per stage.
- It's checked with the pack image rules: PNG signature and chunks, at most 2 MB and 4096 px, an
  exact multiple of the cell, and cells for every frame used.
- The PNG is copied to `art/<stage>.png`, the patch version is bumped, the whole pack is
  re-validated, and it's reinstalled as `local`.
- A user's PNG is never sent anywhere.

---

## Release 2.37.0

## 8. `get_desk` and `propose_desk`

The Mac MCP gains Windows' two Desk tools, same names, schemas and guide text, so one prompt works
on both platforms. They're the base the fold, meter styles and the weather nickname ride on.
- **`get_desk`** reads the Desk's layout words, ink, which pieces show, and (as each lands) tilts,
  styles and the weather nickname, plus `limits` (`piece_style`, tilt range). It reads the settings
  file the app already exports; no new file.
- **`propose_desk`** takes a design and writes `desk-request.json`; the app reviews it in a banner
  on Settings ▸ Desk with a live preview on the Desk, Apply and Dismiss, through `HandoffWatch`, as
  the message proposals do. Same result statuses. The existing off-by-default direct switch for
  Claude's suggestions does **not** cover a whole-Desk design; it always waits for Apply.
- Fields Windows has that the Mac lacks (the system and weather pieces until 2.38.0) are refused
  with a reason, never dropped silently.

## 9. The fold

Windows' #314, affine only: `tilt`, whole degrees from -20 to 20, is the slant of a piece's
**stack**, so the pieces at one anchor share one slant.
- The layout word grows a fourth field, `widget:anchor:scale:tilt` (`clock:ml:1:-18`), written only
  when not 0, so every older layout reads and writes unchanged and a 2.36 build reads the scale and
  ignores the tilt. `DeskArrangement` parses and writes it with Windows' rules: a piece that joins a
  stack takes its slant, one alone keeps its own, and a design that gives two pieces of one stack
  different tilts is refused.
- Drawn with a `CGAffineTransform` shear on the stack's container layer (negative rises to the
  right), plate included. Arrange Desk hit-tests, outlines and drags the slanted quad
  (`DeskHitTest` gains the quad math). Perspective stays out.
- Settings ▸ Desk: a Slant stepper per stack; `propose_desk` takes `tilt`.

## 10. Meter styles

Windows' #336: a per-piece `style` on `claude` and `meters` (and `system` from 2.38.0): `bar` (the
default, drawn exactly as today), `slim` (label and value on one line over a thin bar, about half
the height; the Claude line becomes two short rows), `ring` (small 270-degree arc gauges with the
value inside and a short name in the open bottom, side by side) and `ticks` (ten cells, unlit ones a
faint outline, the OLED-friendly one).
- A Claude limit keeps its pace mark in every style; slim, ring and ticks leave out the reset line.
- The layout word's fifth field, `widget:anchor:scale:tilt:style` (`meters:tr:1:0:ring`), written
  only when not `bar`, with the scale and tilt holding their places.
- A style is the piece's own (the tilt stays the stack's). Nothing animates. Settings ▸ Desk gets a
  Style picker under each of those pieces' places; `propose_desk` refuses a style on any other piece
  or an unknown word.

## 11. Font packs

The validator already checks them (section 1): `.ttf`/`.otf` only, 1 to 4 files, 5 MB each, no
`.ttc`, OFL-1.1 or Apache-2.0, magic bytes.
- An installed font pack's faces are registered with `CTFontManagerRegisterFontsForURL(..., .process)`,
  the way `BundledFonts` registers EsteFont: this app only, never the system, gone when the pack is
  uninstalled. They join the font pickers (the message editor, the Desk ink font, the widget font).
- A face CoreText refuses to register is reported on the pack's row and the pack stays installed
  but unused.
- Claude can't propose fonts, as on Windows. The catalog has none today, so the live check uses a
  fixture pack.

---

## Release 2.38.0

## 12. The system piece

Windows' system readings as a Desk piece, `system`: CPU, memory and disk, every 5 s and only while
the Desk shows.
- CPU from `host_processor_info` tick deltas, memory from `host_statistics64` (used against
  physical), disk from the boot volume's `volumeAvailableCapacityForImportantUsage`.
- Meter styles apply. Read locally, never sent or stored.

## 13. The weather piece and its nickname

Windows' Settings ▸ Weather and Desk piece, with the #315 nickname.
- **Settings ▸ Weather:** type a city; Look up sends that name, and only that, to
  `geocoding-api.open-meteo.com`. The chosen place's latitude and longitude go to
  `api.open-meteo.com` for the forecast every 30 minutes while the piece is placed; stale after 3
  hours. No Location Services, no account, no key. The user agent is `Sanduhr`.
- **"Show it as"**: a nickname up to 24 characters, written only when set, shown instead of the
  place's name; the search and the forecast never see it. `propose_desk` takes `weather {nickname}`
  (`""` goes back to the place's name) and `get_desk` reports it.
- Two new outbound hosts, both in PRIVACY in this release.

## 14. Livelier now-playing bars

Windows draws the bars from the playing app's peak meter, an audio level only. The Mac has no
public per-app level that doesn't involve capture: the route macOS offers is a Core Audio process
tap (macOS 14.2+), which hands the app the audio itself and asks for the System Audio Recording
permission. A spike confirms that before anything is built (owner call 1).
- **If the owner takes the tap:** its own switch, off by default, under Now playing; the tap reads
  only the playing app's output, computes a peak per frame in memory, and keeps and sends nothing.
  Then Windows' `AudioBarMotion` as built: attack, decay, a peak cap that holds 9 frames and then
  falls faster, "Gradient bars" (`nowPlaying.barsGradient`, off), and calm bars with Reduce Motion.
  Bars are a `CAReplicatorLayer` driven by the level, so the idle rule holds.
- **If not:** the Mac keeps its position bar, and the backlog item stays open. Bars that move
  without a real level are not built.

## 15. CI watchers from local runners

Windows 3.9's rule, unchanged: **list and logs, never inspect.** Watcher cards for every job a
GitHub Actions runner container on this Mac runs.
- Docker's local socket (`~/.docker/run/docker.sock`, then `/var/run/docker.sock`), over a small
  HTTP/1.1 client on a Unix socket (`NWConnection`). Exactly two requests, built from a two-value
  enum: `GET /containers/json` and `GET /containers/{id}/logs?follow=1&stdout=1&stderr=1&timestamps=1&tail=200`.
  The id must match `^[0-9a-f]{64}$` or the request isn't built. No redirects, a 10 s limit on the
  headers. Never inspect (its response holds the runner's registration token), never write, start,
  stop or exec.
- Which containers count, the log grammar (`Running job:`, `Job … completed with result:`,
  `Listening for Jobs`) and the multiplexed frame decoder are ports of `RunnerLog` and
  `DockerLogFrames`, tested on Windows' captured fixtures.
- Cards land in the Mac's existing watchers piece, and the companion's triggers see them.
- **A structural boundary test,** like `DockerPipeBoundaryTests`: the socket path appears in one file
  only, and that file's only request call takes the enum.
- Off by default, behind the same switch name as Windows.

---

## 16. Privacy, docs and release (every release)

- **`docs/PRIVACY.md`, Mac section, in the same release as the thing it describes:**
  - **2.35.0:** the catalog host and its three redirect hosts, only when Settings ▸ Widgets opens or
    on Install or Update; no account, cookie or identifier, user agent `Sanduhr`. The widgets folder,
    `widgets.json` and `companion.json`; the companion's reads of Sanduhr Time day files and Claude
    Code activity dates (dates only); the idle check (seconds since input, never what). OLED care's
    preference.
  - **2.36.0:** the widget handoff files; a user's own art stays on the Mac.
  - **2.37.0:** the Desk handoff files; font packs registered for this app only.
  - **2.38.0:** the system readings (local only); the two Open-Meteo hosts and exactly what each
    sees; the bars' audio tap if it ships; the Docker socket and its two requests.
  - The hub's privacy page gets the same rows (hub issue) before each release.
- **Docs:** `mac/README.md` sections per feature; `docs/mac-smoke-test.md` sections for each.
- **Release:** each release gets its What's New card(s) and CHANGELOG, as usual.
- **The ship check:** every new file, preference, host and helper has its PRIVACY paragraph in the
  same release. This is the 2.33.0 lesson.

## 17. Order of work (2.35.0)

1. The fixture corpus and `PackValidator.swift`, to byte-equal parity. This is the largest item.
2. `WidgetZip` (stored-only reader), `WidgetCatalog` (signature, rollback, `PackFormat`, limits),
   the download client with the redirect rules, and the Core Graphics pixel-sheet rasterizer.
3. `WidgetStore` (staging, install, uninstall, `widgets.json`, Reset part) and Settings ▸ Widgets.
4. `IdleClock` and OLED care (drift, dim, white cap).
5. The `companion` piece: render, roam, growth, mood, triggers, demo and debug state.
6. Pack themes and palettes join their lists. Effects are listed only.
7. PRIVACY, README, smoke tests, What's New cards, CHANGELOG.

Items 1, 2 and 4's pure rules are testable on their own, so they suit an unattended run. Items 3, 4's
drawing and 5 need live checks on the Desk.

## Settled (owner, 2026-10-27)

1. **Everything in.** Windows' 3.8 and 3.9 extras, font packs and OLED care join the plan, as four
   releases instead of three. Own art folds into 2.36.0.
2. **The validator is ported to Swift.** The app never runs Python; the MCP vendors `validate.py`.
3. **Reset** gets a Widgets checkbox of its own.
4. **`sanduhr_min`** stays the shared, Windows-numbered pack-format level (section 1).
5. **OLED care** ships in 2.35.0 with the companion.

## Owner calls left

1. **Bars and the audio tap (2.38.0).** Take the tap behind its own off-by-default switch and the
   System Audio Recording permission, or keep the position bar on the Mac? Recommended: run the
   spike first; ship the tap only if the permission prompt reads as honestly as the PRIVACY line
   will.
2. **CI watchers on a Mac (2.38.0).** Worth building if a Mac runs runner containers; today the
   runners live on Neb and Dunder. Recommended: build it for parity, after the rest of 2.38.0.
