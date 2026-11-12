# Run List: sprites-2-43

Every item: the user's art is read on the Mac and never sent anywhere or shown to Claude. Tests build their images and zips in the test (temp folders, made-up names). Keep SwiftUI view bodies small for the macos-15 type-checker. Never `sed -i` a tracked file. Never touch `windows-dotnet/`. Spec: `docs/mac-widgets-spec.md` sections 21 to 24 (item 1).

- [ ] **1. Spec 2.43.0, sections 21 to 24**
  Spec ref: `docs/BACKLOG.md > #9`
  What to build: A `## Release 2.43.0` block in `docs/mac-widgets-spec.md` after section 20: 21 Export a pack, 22 Import an animated GIF, 23 Import an Aseprite sheet, 24 Own art, a column per stage, each with rules, sentences, privacy and tests. BACKLOG #9 points at it. Size S.
  Acceptance: Done when the four sections exist and `docs` CI's link check would pass (no dangling paths).
  Verify: `git diff --check`
  Stop class: go

- [ ] **2. `WidgetZip.write`**
  Spec ref: `docs/mac-widgets-spec.md > 21`
  Depends on: 1
  What to build: A pure writer: `[String: Data]` in, zip bytes out. Stored entries (method 0), CRC-32, paths sorted, a fixed DOS date (1980-01-01), no extra fields, no directories, regular-file attributes. Refuses what `read` would refuse (unsafe names, too many entries, over the byte cap) with a `WidgetZipError`. Size S.
  Acceptance: Done when tests show write then `read` (both `fromUser` values) returns the same files, two writes of the same files are byte-identical, `unzip -t` accepts the output, and each refusal has a test. The suite passes.
  Verify: `cd mac && ./test.sh`
  Stop class: go

- [ ] **3. Export a pack**
  Spec ref: `docs/mac-widgets-spec.md > 21`
  Depends on: 2
  What to build: `WidgetExport` (pure): an installed pack's files from `WidgetStore`, validated strict, zipped with `WidgetZip.write`, the file name `<id>-<version>.zip`. `WidgetsModel` gains `canExport(row)` (local packs only) and `exportStatus`. The row's **Export…** button opens an `NSSavePanel` with the suggested name; the status line says where it went. Size M.
  Acceptance: Done when tests show a made-here sprite exports and `WidgetFileInstall.install` of the zip in a fresh store gives the same files byte for byte; a catalog pack is not exportable; a pack that no longer validates is refused with the validator's sentence and writes nothing. The suite passes.
  Verify: `cd mac && ./test.sh`
  Stop class: go

- [ ] **4. Import an animated GIF**
  Spec ref: `docs/mac-widgets-spec.md > 22`
  Depends on: 1
  What to build: `SpriteFrames` (ImageIO and Core Graphics only): a GIF's frames and delays read with `CGImageSource`, laid out row-major in a sheet (`PixelSheet.layout`, 16 across), encoded as PNG, and the sheet checked with `WidgetOwnArt.imageSize`. The fps is the frames' median delay rounded into 1...24. **New sprite from an image…** accepts `.gif` beside `.png`; a GIF opens the existing sheet with the cell set to the frame size, walk tagged with every frame (two or more), sit with frame 1, and the fps carried into `sprite.json`. Refusals in sentences: one frame only, a frame side outside 8...64, more than 256 frames, a sheet over 2 MB. `WidgetNewSprite.Input` gains an optional `fps`. Size M, hard.
  Acceptance: Done when tests write GIFs with `CGImageDestination` in the test (frames of different colours and delays, one with a partial later frame) and show the sheet's cells hold the frames in order, the fps rule, each refusal, and a made pack validates strict. The suite passes.
  Verify: `cd mac && ./test.sh`
  Stop class: go

- [ ] **5. Import an Aseprite sheet**
  Spec ref: `docs/mac-widgets-spec.md > 23`
  Depends on: 4
  What to build: `AsepriteSheet` (pure): parses Aseprite's export JSON, hash or array `frames`, `meta.image`, `meta.frameTags` (`forward`, `reverse`, `pingpong`, `pingpong_reverse`) and frame `duration`s. The frames must form a uniform grid of one cell size with no rotation or trim; each becomes its row-major cell. Tags named after an action (walk, sit, nap, cheer, worry; any case) pre-fill those tags; other tags are listed as ignored. **New sprite from an image…** accepts `.json`; the sheet named by `meta.image` is read only from the same folder (a plain file name, no `/`, no `..`, not a link). Opens the same sheet as item 4 with the tags and fps filled. Size M.
  Acceptance: Done when fixture JSONs built in the test cover hash and array forms, each direction, durations to fps, an untagged export (walk falls back to every frame, sit to frame 1, as for a GIF), a non-grid or trimmed export refused, and a `meta.image` with a path refused. The suite passes.
  Verify: `cd mac && ./test.sh`
  Stop class: go

- [ ] **6. Own art, a column per stage**
  Spec ref: `docs/mac-widgets-spec.md > 24`
  Depends on: 1
  What to build: **Use my own art…** on a sprite with two or more stages opens a sheet with one column per stage, in growth order: the stage name, "From N% growth", the current sheet's first cell, a **Choose…** button, and after a pick the new first cell or the check's sentence. **Apply** runs `WidgetOwnArt.apply` once with every picked stage; stages not picked keep their sheets. A one-stage sprite keeps today's single panel. An `OwnArtStagesModel` holds the picks so tests drive it without a panel. Size M.
  Acceptance: Done when tests show the columns follow `min_growth`, a bad pick shows its sentence and blocks Apply only for that column, Apply with two of three stages picked changes exactly those sheets and bumps the patch version once, and a one-stage sprite skips the sheet. The suite passes.
  Verify: `cd mac && ./test.sh`
  Stop class: go

- [ ] **7. The 2.43.0 rows**
  Spec ref: `docs/mac-widgets-spec.md > 16, 21 to 24`
  Depends on: 3, 4, 5, 6
  What to build: `mac/Info.plist` 2.43.0, build 63. A What's New card (`version: "2.43.0"`, id `sprites-in-and-out`, destination `.widgets`) with its `WhatsNewTests` lines. CHANGELOG `## Unreleased (mac)` ▸ New. `mac/README.md` Widgets paragraph. PRIVACY.md (Last updated 2026-11-11): GIF and Aseprite files are read on the Mac only; Export writes only where you save it. `SettingsIndex` entries for Export… and the import types. Smoke section 77 in `docs/mac-smoke-test.md`. Size M.
  Acceptance: Done when `WhatsNewTests` pin the new card, the CHANGELOG heading is `## Unreleased (mac)`, PRIVACY matches the code, and the suite and the selftest pass.
  Verify: `cd mac && ./test.sh && ruby smoke/selftest.rb`
  Stop class: go

## Not this run
- Reading native `.aseprite` files (a binary format with its own compression): BACKLOG #9.1 names the exported sheet and JSON; the binary waits for a user who needs it.
