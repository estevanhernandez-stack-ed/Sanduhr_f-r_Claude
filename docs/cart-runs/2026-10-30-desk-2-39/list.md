# Run List: desk-2-39

- [ ] **1. Sprite sheet rules (pure)**
  Spec ref: `docs/mac-widgets-spec.md > 18`, `> 7`; `WidgetOwnArt.swift`; hourglass `sprite.json`
  What to build: A pure `WidgetNewSprite` (or similar) beside `WidgetOwnArt`: the PNG checked with the pack image rules (`WidgetOwnArt.checkImage`'s rules: signature, chunks, 2 MB, 4096 px); cell size 8 to 64 px on a side with the sheet a whole number of cells; cell indices in reading order; tags for `walk` (two or more, in order), `sit`, `nap`, `cheer`, `worry`, an untagged action falling back to `sit`; a name (slugged to `my-<slug>`, colliding ids get `-2`) and optional scale. Writes `pack.json` (id `my-<slug>`, license CC0-1.0, version 1.0.0, `sanduhr_min` 3.8.0), `sprite.json` (one stage, animations, moods and triggers mapped from the tags the way the hourglass maps them) and `art/sprite.png`, then validates the whole pack strict with `PackValidator`. Errors as sentences for Settings. Size M.
  Acceptance: Done when tests cover every rule (bad cell sizes, partial cells, walk with one cell, fallback to sit, slug collisions, an oversize PNG) and a made pack passes `PackValidator` strict.
  Verify: `cd mac && ./test.sh`
  Stop class: go

- [ ] **2. New sprite from an image… in Settings**
  Spec ref: `docs/mac-widgets-spec.md > 18`
  Depends on: 1
  What to build: Settings ▸ Widgets ▸ **New sprite from an image…**: `NSOpenPanel` for one PNG, a sheet that shows the image as a cell grid at the chosen cell size, a way to tag cells to the five actions (walk in click order), name and scale fields, a Make button that installs the pack as `local` through the same install path as own art (`WidgetStore`), and the error sentences inline. SettingsIndex entry. The image is never sent anywhere. Keep view bodies small for the macos-15 type-checker. Size L.
  Acceptance: Done when the model behind the sheet is tested (tag order, fallback, install call with a fake store) and the suite passes; the drawing is a live check.
  Verify: `cd mac && ./test.sh && ruby smoke/selftest.rb`
  Stop class: go

- [ ] **3. propose_sprite_motion in the Mac MCP**
  Spec ref: `docs/mac-widgets-spec.md > 19`, `> 6`; `mac/integrations/sanduhr_mcp.py` (`propose_widget`)
  Depends on: 1
  What to build: The app reports each made-here sprite's id, frame size and cell count (never the image) where the MCP can read it, on the smaller of the two surfaces the spec allows. `propose_sprite_motion` beside `propose_widget`: input is the sprite id and `sprite.json` fields only (`animations` as cell indices, `fps`, `scale`, `moods`, `triggers`, `roam`); no image or pixel field accepted; checked with the vendored validator against that sprite's cell count; handed over in a request file shaped like `widget-request.json`, same statuses and limits as `propose_widget`. pytest and Swift tests for the handoff file shape. Size M.
  Acceptance: Done when pytest proves image or pixel fields are refused, out-of-range cell indices are refused, and a good proposal writes the request file; Swift reads it; both suites pass.
  Verify: `cd mac && ./test.sh && python3 -m pytest -q integrations/tests`
  Stop class: go

- [ ] **4. Motion review: banner, Preview, Apply**
  Spec ref: `docs/mac-widgets-spec.md > 19`, `> 6`; `WidgetReview.swift`, `WidgetProposalHandoff.swift`
  Depends on: 3
  What to build: The app picks up the motion request, shows the review banner with **Preview on the Desk** (the sprite with the proposed motion, in memory only) and **Apply**, which rewrites `sprite.json`, bumps the patch version, re-validates and reinstalls. Decline and expiry as for widget proposals, with the same result statuses back to the MCP. Size M.
  Acceptance: Done when Apply, Decline and the preview path are tested with a fake store and the suite passes.
  Verify: `cd mac && ./test.sh && python3 -m pytest -q integrations/tests`
  Stop class: go

- [ ] **5. Bar motion rules (pure port)**
  Spec ref: `docs/mac-widgets-spec.md > 14`; Windows `AudioBars.cs`, `AudioBarsTests.cs`
  What to build: Swift ports of `AudioBars` and `AudioBarMotion` (attack, decay, falloff, the 9-frame cap hold then faster fall, calm constants for Reduce Motion, the idle threshold), and the "Gradient bars" preference key `nowPlaying.barsGradient` (off) as a constant only. No audio, no drawing. Windows' tests ported to swift-testing. Size S.
  Acceptance: Done when the ported tests pass.
  Verify: `cd mac && ./test.sh`
  Stop class: go

- [ ] **6. The audio tap and the live bars (opt-in)**
  Spec ref: `docs/mac-widgets-spec.md > 14`; spike `docs/spikes/2026-10-30-audio-tap-bars.md`
  Depends on: 5
  What to build: An off-by-default switch under Now playing (SettingsIndex), the only place the System Audio Recording prompt can appear. A tap service: `CATapDescription` on the playing app's process only (from `NowPlayingInfo.pid`, re-tapped when it changes), a private aggregate device, unmuted, one peak per callback into a lock-protected value; nothing kept, written or sent; torn down on pause, switch off and while the Desk is hidden. The bars as a `CAReplicatorLayer` driven by item 5's motion, still at idle (timer stops). Denied: the position bar stays and the row shows "Allow in System Settings" when `kAudioProcessPropertyIsRunningOutput` is true but buffers are all zero for a few seconds; never re-prompt, no private TCC calls. `NSAudioCaptureUsageDescription` in `mac/Info.plist` (this item only). Draft PRIVACY, README and Settings copy marked for the owner to finalise from the real prompt text. Tests for the pure parts (pid change, teardown decisions, denied inference) with a fake tap. Never trigger the prompt in tests or during the run. Size L.
  Acceptance: Done when the pure-part tests pass, the suite passes, and nothing in the run asked for the permission.
  Verify: `cd mac && ./test.sh && ruby smoke/selftest.rb`
  Stop class: hold

- [ ] **7. Docs, the 2.39.0 What's New cards, CHANGELOG**
  Spec ref: `docs/mac-widgets-spec.md > 16` (2.39.0 rows)
  Depends on: 1, 2, 3, 4
  What to build: PRIVACY Mac section (the new-sprite panel and `art/sprite.png`, the motion handoff files and that Claude sees only a sprite's frame size and cell count), README sections, `docs/mac-smoke-test.md` sections, What's New 2.39.0 card(s) with their `WhatsNewTests` lines, CHANGELOG `## Unreleased (mac)` after `# Changelog`. The audio tap is item 6's, not here. Every claim true to the code. No Info.plist. Size S.
  Acceptance: Done when WhatsNew tests pass and docs describe only what items 1-4 built.
  Verify: `cd mac && ./test.sh`
  Stop class: go

## Not this run

- Art per growth stage, GIF and Aseprite import, pack export: backlog #9, per settled call 8.
