# Run Brief: sprites-2-43

## Ask
"1 Backlog 9 please", answering the offer: "Backlog #9, the sprite follow-ups (my pick, as 2.43.0): GIF and Aseprite import, a page for each stage, and Export… for sprite packs ... it can be built overnight with `/take` and shipped once you've reviewed it."

## Source
- **Kind:** backlog
- **Location:** `docs/BACKLOG.md` > `#9 — Sprite sheets, later (Mac first)`, its three numbered items.
- **Spec context:** `docs/mac-widgets-spec.md` sections 6 (own art), 7 (image rules), 18 (new sprite from an image), 19 (motion, never the art) and 20 (install from a file).
- **House style:** `mac/Sources/Sanduhr/Widgets/` (`WidgetNewSprite`, `NewSpriteModel`, `WidgetOwnArt`, `WidgetFileInstall`, `WidgetZip`, `WidgetStore`, `PixelSheet`), `Views/WidgetsSettings.swift`, `Views/NewSpriteSheet.swift`.
- **Windows:** has none of the three (Mac first, as the backlog says). Nothing under `windows-dotnet/` is read for parity or touched.

## Intent
Three ways to bring your own sprite to the Mac, and one way to take it out:
- an animated GIF, or a sheet and its JSON exported from Aseprite, becomes a new sprite through the existing New sprite sheet;
- Use my own art on a sprite with growth stages shows one column per stage, so the growth is visible while you pick;
- Export… on a pack made here writes a zip that installs anywhere Install widget from file… works.

The image boundary holds throughout: the art is read on the Mac, never sent anywhere and never shown to Claude.

## Done when
- Items 1 to 7 are merged on `cart/sprites-2-43`.
- `cd mac && ./test.sh` and `ruby smoke/selftest.rb` pass on the integration branch.
- A pack made here round-trips: Export… writes a zip, and `WidgetFileInstall.install` installs that zip as the same pack with byte-identical files.
- A GIF built in the test becomes a sheet whose cells are its frames, and a fixture Aseprite export maps its frame tags onto the sprite's actions.

## Out of scope
- The release itself (dispatch, appcast, cask), any push or PR. Item 7 writes the release rows (version, card, docs) only.
- Reading the native `.aseprite` binary format: only Aseprite's exported sheet plus JSON (`File ▸ Export Sprite Sheet`, JSON Data, hash or array).
- Any upload or submission to the catalog. Export writes a local file only (spec Scope; BACKLOG #9.3).
- Turning a picture into a pixel sheet Claude can read (BACKLOG #9, "Not planned").
- Drag and drop onto the Widgets page.
- `windows-dotnet/`.

## Inferred
- Release 2.43.0, build 63, a minor with one What's New card. (from: the offer said 2.43.0; Info.plist is 2.42.5/62)
- The spec is written first, as item 1, in `docs/mac-widgets-spec.md` sections 21 to 24, so every worker reads the same rules. (from: the drawers run shape, which had a spec before code)
- GIF and Aseprite enter through **New sprite from an image…**, which opens the existing tagging sheet with the frames laid out and the tags pre-filled. No second sheet. (from: section 18 already owns cell tagging)
- An Aseprite import reads the JSON the user picks and the sheet its `meta.image` names in the same folder. Nothing else is read. (from: the app is not sandboxed; picking one file is the simpler panel. D3)
- Export is offered on `local` packs only (made here, own art, installed from a file), never on a catalog pack. (from: BACKLOG #9.3 "a pack made here")
- Item stop classes are all `go`. Nothing touches money, auth, permissions, secrets or user data beyond a file the user picks or saves. (from: stop-class defaults)

## Mode
- **Mode:** Unattended
- **Triggered by:** the ask accepted an offer of an overnight `/take`; the args passed to the skill said "overnight". Auto mode is on.
