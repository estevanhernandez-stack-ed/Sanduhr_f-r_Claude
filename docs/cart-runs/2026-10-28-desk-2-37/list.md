# Run List: desk-2-37

- [ ] **1. Layout words gain tilt and style**
  Spec ref: `docs/mac-widgets-spec.md > 9 (layout word, stack rules); 10 (fifth field)`
  What to build: Extend the Mac `DeskArrangement` (and `DeskPiece`/layout parsing) to `widget:anchor:scale:tilt:style`, ported from Windows `DeskArrangement.cs` and the parsing parts of `DeskMeters.cs`: tilt whole degrees -20..20 (stack-shared: a piece joining a stack takes its slant, one alone keeps its own; conflicting tilts in one stack are refused), style `bar|slim|ring|ticks` on `claude` and `meters` only (the piece's own), each field written only when not default with earlier fields holding their places, so every 2.36 layout reads and writes byte-identically. Port the matching cases from `DeskRideAlongsTests.cs` and `DeskMeterStylesTests.cs`. Pure. Size M.
  Acceptance: Done when round-trip of existing layouts is unchanged, tilt/style parse and write per the Windows tests, and conflicts are refused.
  Verify: `cd mac && ./test.sh`
  Stop class: go

- [ ] **2. The fold**
  Spec ref: `docs/mac-widgets-spec.md > 9. The fold`
  What to build: Draw each stack's tilt as a `CGAffineTransform` shear on the stack's container (negative rises to the right), plate included; Arrange Desk hit-tests, outlines and drags the slanted quad (`DeskHitTest` quad math with tests); Settings ▸ Desk a Slant stepper per stack (SettingsIndex entry). Keep idle CPU rules (no per-frame redraw). OLED drift still applies. Size L.
  Acceptance: Done when quad hit-test tests pass, the stepper writes the layout word, and the suite passes.
  Verify: `cd mac && ./test.sh && ruby smoke/selftest.rb`
  Stop class: go

- [ ] **3. Meter styles**
  Spec ref: `docs/mac-widgets-spec.md > 10. Meter styles`
  What to build: `bar` (unchanged), `slim`, `ring` (270-degree arcs, value inside, short name in the gap), `ticks` (ten cells, unlit outlined) for the `claude` and `meters` pieces, ported from Windows' renderers' rules; pace mark in every style; slim/ring/ticks drop the reset line; nothing animates; OLED white cap applies. Settings ▸ Desk Style picker under each of those pieces' places (SettingsIndex). Demo mode renders each style. Split SwiftUI bodies for CI's type-checker. Size L.
  Acceptance: Done when per-style model tests (what each style shows, pace mark present) pass and the suite passes.
  Verify: `cd mac && ./test.sh && ruby smoke/selftest.rb`
  Stop class: go

- [ ] **4. Font packs**
  Spec ref: `docs/mac-widgets-spec.md > 11. Font packs`
  What to build: Port `PackFonts.cs`: register an installed font pack's faces with `CTFontManagerRegisterFontsForURL(..., .process)` (as `BundledFonts` does), at launch and on install; unregister on uninstall; faces join the message editor, Desk and widget font pickers; a face CoreText refuses is reported on the pack's row and left out. A fixture font pack under the test fixtures (an OFL font already in the repo, e.g. a bundled one, so no download). Size M.
  Acceptance: Done when tests cover registration, a refused face, and the picker lists, and the suite passes.
  Verify: `cd mac && ./test.sh`
  Stop class: go

- [ ] **5. get_desk and propose_desk**
  Spec ref: `docs/mac-widgets-spec.md > 8`
  What to build: In `mac/integrations/sanduhr_mcp.py`, `get_desk` and `propose_desk` with Windows' names, schemas and guide text (`ToolCatalog.cs`), returning what the Mac has (layout words, ink, pieces shown, tilts, styles, `limits`) and refusing fields the Mac lacks with a reason. If no existing app export carries the Desk settings, the app writes an owner-only `desk-state.json` (atomic) whenever they change, and `get_desk` reads it (ledger D2). Swift port of the Mac subset of `DeskDesign`/`DeskDesignValidator`/`DeskDesignHandoff` (request decode, re-validate, answers); Python mirrors the validator; `desk-request.json`/`desk-result.json` like the other proposals; shared fixtures (`mac/Tests/SanduhrTests/Fixtures/desk-designs/`) both sides must agree on. Port `ToolLogicDeskDesignTests.cs` to pytest. Size L.
  Acceptance: Done when pytest and Swift tests pass, the shared fixtures agree, and a refused design writes no request.
  Verify: `cd mac && python3 -m pytest -q integrations/tests && ./test.sh`
  Stop class: go

- [ ] **6. The app's Desk design review**
  Spec ref: `docs/mac-widgets-spec.md > 8 (review bullets)`
  What to build: Port `DeskDesignService.cs` as a Mac handoff via `HandoffWatch`: a banner on Settings ▸ Desk (only while a design waits), Preview on the Desk (in memory, saved settings untouched, Dismiss/Apply/newer request end it, like 2.36.0's widget preview), Apply writes the settings, Dismiss; result statuses as Windows; the direct switch never covers a whole design. Port `DeskDesignServiceTests.cs`. Size L.
  Acceptance: Done when the ported tests pass (preview, apply, dismiss, replaced, busy, direct switch ignored) and smoke selftest passes.
  Verify: `cd mac && ./test.sh && ruby smoke/selftest.rb`
  Stop class: go

- [ ] **7. Docs, the 2.37.0 What's New cards, CHANGELOG**
  Spec ref: `docs/mac-widgets-spec.md > 16` (2.37.0 rows)
  What to build: PRIVACY Mac section (desk-request/result, desk-state.json if added, font packs registered for this app only), README, smoke sections, What's New 2.37.0 cards with tests, CHANGELOG `## Unreleased (mac)` after `# Changelog`. Every claim true to the code. No Info.plist. Size S.
  Acceptance: Done when WhatsNew tests pass and docs describe only what items 1-6 built.
  Verify: `cd mac && ./test.sh`
  Stop class: go
