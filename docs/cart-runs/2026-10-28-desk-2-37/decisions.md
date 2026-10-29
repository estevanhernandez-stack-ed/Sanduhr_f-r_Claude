<!-- The ledger. Every inference, default, substitution and mid-run class
     upgrade lands here, numbered D1, D2, and so on. Append only. -->

# Decisions: desk-2-37

## D1
- **Decision:** All seven items are `go`.
- **Why:** Nothing touches money, auth, access gating or permissions. New handoff files sit beside the existing ones; PRIVACY covers them in item 7.
- **Alternative not taken:** Holding item 6 (Claude reshaping the whole Desk) for a walkthrough.
- **Item:** all
- **How to reverse:** Leave item 6's merge out of the push.

## D2
- **Decision:** If no app export already carries the Desk settings, the app writes an owner-only `desk-state.json` for `get_desk`, contrary to the spec's "no new file".
- **Why:** Desk settings live in the `com.626labs.sanduhr.desk` defaults suite; Python reading the plist directly races cfprefsd and can be stale. A small atomic export is what the other read tools already rely on (`desk-messages-state.json`).
- **Alternative not taken:** `get_desk` reading the preferences plist.
- **Item:** 5, 7
- **How to reverse:** Drop the export and read the plist in the MCP.

## D3
- **Decision:** Windows' C# is the line-by-line guide; the spec wins on Mac surfaces, Windows on rules and messages. Windows-only Desk design fields are refused with a reason on the Mac.
- **Why:** Same rule as the 2.35.0 and 2.36.0 runs; spec 8 says never drop silently.
- **Alternative not taken:** Accepting and ignoring Windows-only fields.
- **Item:** all
- **How to reverse:** n/a

## D4
- **Decision:** A layout string whose stack tilts disagree still reads (the stack slants by its first piece); the refusal lives in `DeskArrangement.tiltConflicts`, which item 5's design validator calls. Unknown style words read as bar. No monitor suffix on the Mac.
- **Why:** Stored layouts must never fail to load, and Windows reads tolerantly the same way; a whole-design proposal is where conflicts are refused.
- **Alternative not taken:** A throwing initializer.
- **Item:** 1, 5
- **How to reverse:** n/a

## D5
- **Decision:** Accepted item 2's fold: a `transformEffect` shear on the stack container plus a `DeskFoldLayout` that reserves the slanted height (Windows' LayoutTransform), only when tilt is non-zero. Plates stay per piece and slant with the stack; OLED drift sits inside the shear; a 0 to non-zero change rebuilds the stack (a message `{write}` replays then).
- **Why:** One static compositing transform keeps idle CPU flat; at 0 nothing changes. Gotcha recorded: `GeometryReader.frame(in:)` inside a `transformEffect` reports the sheared bounding box, so `DeskFold.layoutFrame(fromBounds:)` recovers layout frames for hit-testing.
- **Alternative not taken:** One shared plate per stack as on Windows.
- **Item:** 2
- **How to reverse:** Revert c57b914e.

## D6
- **Decision:** Accepted item 3's meter styles with two Mac departures: the ring shows the warning triangle above its value (Windows is red only), and the alert pulse is not applied to slim, ring or ticks. Settings previews still draw bars.
- **Why:** A colour-only cue fails the accessibility bar the bar style already meets; the spec says nothing animates in the new styles.
- **Alternative not taken:** Red-only ring, pulse on every style.
- **Item:** 3
- **How to reverse:** Drop the glyph in `DeskRingGauge`.

## D7
- **Decision:** The font-pack fixture is a tiny generated font ("Fixture Pack Sans", 3 glyphs, OFL-1.1) plus a truncated copy for the refused case, not a relabelled copy of a bundled font.
- **Why:** The only fonts in the repo are EsteFont Pro and EsteFont 26, which are 626Labs LLC's own and not OFL; labelling a copy OFL in a public fixture would grant that license.
- **Alternative not taken:** Copying EsteFont into the fixture.
- **Item:** 4
- **How to reverse:** Swap in a real OFL font with its license text.

## D8
- **Decision:** Mac font packs register from the pack's own folder (process scope) with no copy-to-cache step; refusal is counted per face.
- **Why:** Windows copies fonts out because WPF holds the file open and blocks Uninstall's rename; CoreText holds nothing open, and `release` runs before install or uninstall touches the folder.
- **Alternative not taken:** Porting Windows' `.fonts/<id>-<installedAt>/` cache and sweep.
- **Item:** 4
- **How to reverse:** n/a

## D9
- **Decision:** D2 confirmed: no app export carried the Desk settings, so item 5 added `desk-state.json` (0600, atomic, written at launch and 0.5 s after a change, only when the bytes differ). It holds the design (layout words, font, ink, plate, shadow), pieces, anchors, shown pieces, font names (Desk, message, bundled, installed families, capped at 2000), the message and notch colors, and limits; never usage, message text, paths or account names.
- **Why:** `get_desk` needs a reliable read; the installed font list matches what Windows' `get_desk` offers so Claude can suggest a font the Mac has.
- **Alternative not taken:** Reading the defaults plist from Python.
- **Item:** 5, 7
- **How to reverse:** Remove `DeskStateExporter` and have `get_desk` answer `no_data`.

## D10
- **Decision:** The Mac `propose_desk` refuses `messages`, `looks` and `theme` with a pointer to their own tools, refuses Windows-only fields (launcher, monitor, ink words auto/light/dark/theme, telemetry and glow pieces) and the 2.38 pieces (system, weather) with reasons, and skips Windows' wallpaper contrast rule. File names are `desk-request.json` / `desk-result.json`.
- **Why:** Spec 8: never drop silently. Accepting those three sub-fields would need three more apply paths; their tools already exist on the Mac.
- **Alternative not taken:** Accepting and applying messages, looks and theme inside a design.
- **Item:** 5, 6
- **How to reverse:** Wire those three into item 6's apply and drop the refusals.

## D11
- **Decision:** Item 6 gains "Put my Desk back": Apply saves the values a design changes (layout, font, inkColor, plate, inkShadow) as `deskDesignBefore` in the Desk suite, and Settings ▸ Desk offers one undo while it exists. Windows' saved-designs library and "Before <name>" auto-save are not ported.
- **Why:** As first built, Apply overwrote the Desk with no way back, the one real regression against Windows (which has Put my Desk back). One snapshot closes it without a designs library.
- **Alternative not taken:** Porting `DeskDesignLibrary` (saved designs, names, a picker).
- **Item:** 6, 7
- **How to reverse:** Revert the put-back commit; PRIVACY loses its sentence.
