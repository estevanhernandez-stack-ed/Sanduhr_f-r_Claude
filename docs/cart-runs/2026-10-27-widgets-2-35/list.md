# Run List: widgets-2-35

- [ ] **1. PackValidator.swift to byte-equal parity, and the vendored validate.py**
  Spec ref: `docs/mac-widgets-spec.md > 1. Trust` (validator and fixtures bullets)
  What to build: Copy `windows-dotnet/tests/Sanduhr.Tests/fixtures/widget-packs/` (README, good, bad, packs, python-expected.tsv) to `mac/Tests/SanduhrTests/fixtures/widget-packs/` and register it as a test resource. Port `PackJson.cs` and `PackValidator.cs` (Windows `Sanduhr.Core`) to pure Foundation Swift under `mac/Sources/Sanduhr/Widgets/`: every rule, messages byte-equal (including Python-style repr formatting that PackJson emulates), PNG chunk/CRC/IDAT checks via the Compression framework, themes reusing `ThemeLint` only if its rules are identical, otherwise the ported rules. Fonts are validated too. Port `PackValidatorTests.cs` and `PackValidatorFixtureTests.cs` case for case. Vendor `tools/validate.py` (and any module it imports) from `626Labs-LLC/sanduhr-widgets` at 61b4247 into `mac/integrations/widgets/` with a header comment naming the commit, plus a pytest that runs it on the same fixtures and matches python-expected.tsv. Size L.
  Acceptance: Done when the Swift fixture test matches python-expected.tsv for every pack, the ported unit tests pass, and the pytest passes.
  Verify: `cd mac && ./test.sh --filter PackValidator && python3 -m pytest -q integrations/tests`
  Stop class: go

- [ ] **2. WidgetZip (stored only) and the pixel-sheet rasterizer**
  Spec ref: `docs/mac-widgets-spec.md > 1. Trust` (Zip), `2. Packs on disk` (rasterize)
  What to build: Port `WidgetZip.cs` (stored entries only, never inflated; every refusal and limit) and `PixelSheet.cs` (rasterizing to PNG with Core Graphics, nearest-neighbour, exact palette colors). Port `WidgetZipTests.cs` and the pixel-sheet tests; copy `fixtures/widget-zips/` into the Mac fixtures. Size M.
  Acceptance: Done when the ported tests pass and the two fixture zips read to the same entries as Windows.
  Verify: `cd mac && ./test.sh --filter "WidgetZip|PixelSheet"`
  Stop class: go

- [ ] **3. WidgetCatalog and the download client**
  Spec ref: `docs/mac-widgets-spec.md > 1. Trust` (Catalog signature, sanduhr_min, Downloads)
  What to build: Port `WidgetCatalog.cs` and `WidgetCatalogClient.cs`: P-256 with CryptoKit from the pinned SPKI list (Key A, the same bytes as Windows), P1363 64-byte signatures, verify before parse, strict keys, rollback, limits, `CheckDownload` with constant-time compare. `sanduhr_min` compares against `PackFormat.supported = "3.8.0"`, never the app version (test pins it). The download client: an ephemeral URLSession, no cookies, user agent `Sanduhr`, manual redirects (max 5, https, the three hosts), 20 s / 90 s timeouts, size cap. Tests use a URLProtocol stub; no live request. Port `WidgetCatalogTests.cs`. Size M.
  Acceptance: Done when the real `fixtures/widget-zips/catalog.json` with its signature verifies (or the Windows test's signed fixture does), tampering fails, rollback and redirect rules are tested, and the PackFormat test exists.
  Verify: `cd mac && ./test.sh --filter WidgetCatalog`
  Stop class: go

- [ ] **4. WidgetStore, Reset's Widgets part, and Settings ▸ Widgets**
  Spec ref: `docs/mac-widgets-spec.md > 2. Packs on disk; 3. Settings ▸ Widgets`
  What to build: Port `WidgetStore.cs` to `~/Library/Application Support/Sanduhr/widgets/` (staging, validate, rasterize, rename into place, `widgets.json`, uninstall, an id keeps its source), honoring `ResetGate`. Add `ResetPart.widgets` as its own checkbox next to Themes. A Settings ▸ Widgets page in the Desktop group after Lock Screen (SettingsIndex entry too): catalog list fetched only on open or "Check the catalog again", Install/Update, installed list with kind, version, source and Uninstall, Companion picker, "Let it roam" (on by default), effects note "Effects packs don't draw yet". Pack themes join the Themes strip (port `PackThemes.cs`), pack palettes join `MessagePalette.all` (port `PackInkPresets.cs`). Split SwiftUI bodies so CI's type-checker doesn't time out. Port `WidgetStoreTests.cs`. Size L.
  Acceptance: Done when store tests (install, update, uninstall, bad pack refused, reset removes the folder) pass, the page builds, and pack themes and palettes appear in their lists in tests.
  Verify: `cd mac && ./test.sh`
  Stop class: go

- [ ] **5. IdleClock and OLED care**
  Spec ref: `docs/mac-widgets-spec.md > 4 (Idle, shared) and 5. OLED care`
  What to build: `IdleClock` over `CGEventSource.secondsSinceLastEventType(.combinedSessionState, ...)`, polled only while the Desk shows. Port `OledCare.cs` (switch `deskOledCare`, off; title and caption), `OledDrift` (orbit, clamp, Drifted), `OledIdle` (5 min, edges), and `DeskInk` CapWhite. Wire: Settings ▸ Desk switch; the Desk moves piece layers by the orbit offset every 3 min with one implicit animation (clear of menu bar, notch and Dock via DockClearance); the Desk and the widget dim after idle; ink, message gradients and now-playing ink capped. The notch island doesn't drift. Port `OledCareTests.cs` and `OledCareSettingsTests.cs`. Size M.
  Acceptance: Done when the pure rules' tests pass, the switch is in Settings and SettingsIndex, and no timer runs while OLED care is off.
  Verify: `cd mac && ./test.sh`
  Stop class: go

- [ ] **6. The companion's rules**
  Spec ref: `docs/mac-widgets-spec.md > 4. The companion piece` (Growth, Mood, Triggers, Roam)
  What to build: Port `CompanionGrowth.cs` (curve, days from `~/.sanduhr/time/days/*.json` else CCLogReader activity dates, `widgets/companion.json` high-water mark), `CompanionMood.cs` (moods and triggers from the active account's limits, `paceInfo`, AlertSettings, watchers), `CompanionRoam.cs` (path and walk/rest clock), `CompanionSprite.cs` (stage and frame picking, 1 fps with Reduce Motion), `CompanionSettings.cs`. Pure, no drawing. Port `CompanionTests.cs` and `CompanionBehaviourTests.cs`. Size M.
  Acceptance: Done when the ported tests pass, including the growth curve points and every mood and trigger row in the spec tables.
  Verify: `cd mac && ./test.sh --filter Companion`
  Stop class: go

- [ ] **7. The companion piece on the Desk**
  Spec ref: `docs/mac-widgets-spec.md > 4. The companion piece` (Placement, Render, Pausing, Roam, Demo, Debug)
  What to build: `companion` in `DeskView.Widget`, `DeskLayout.widgets`, `DeskPiece`, default anchor `bc`, layout word scale; not hit-testable. A CALayer with the stage sheet (`.nearest`), frames by a discrete CAKeyframeAnimation on contentsRect (BreathLayerView pattern), roaming by CABasicAnimation on position inside the Desk window, paused (speed 0) when `MessageMotion.paused` holds, Reduce Motion = no roam, 1 fps. OLED drift and dim apply. Demo mode: hourglass at a fixed stage with sample moods, never real growth. `state.yaml` gains `companion: {pack, stage, mood, roaming}`; update the smoke selftest fixture if it pins keys. Settings preview and debug snapshot. Size L.
  Acceptance: Done when tests cover the layout word, demo state and the state.yaml field, `ruby smoke/selftest.rb` passes, and no per-frame timer or TimelineView drives the sprite.
  Verify: `cd mac && ./test.sh && ruby smoke/selftest.rb`
  Stop class: go

- [ ] **8. Docs, the 2.35.0 What's New cards, CHANGELOG**
  Spec ref: `docs/mac-widgets-spec.md > 16. Privacy, docs and release` (2.35.0 rows)
  What to build: docs/PRIVACY.md Mac section: the catalog host and three redirect hosts and when they're contacted, no account/cookie/identifier, user agent; the widgets folder, widgets.json, companion.json; Sanduhr Time and Claude Code activity dates read (dates only); the idle check (seconds only); OLED care's preference. Every claim exactly true to the code as built. mac/README.md Widgets, Companion and OLED care sections. docs/mac-smoke-test.md new numbered sections. WhatsNewCards: 2.35.0 cards (Widgets, The companion, OLED care) with destinations, WhatsNewTests updated (count, ids, destinations, an `updatingToTwoThirtyFive` test). CHANGELOG: `## Unreleased (mac)` inserted right after `# Changelog`. Do NOT touch mac/Info.plist. Size S.
  Acceptance: Done when WhatsNew tests pass and the docs describe only what items 1-7 built.
  Verify: `cd mac && ./test.sh`
  Stop class: go
