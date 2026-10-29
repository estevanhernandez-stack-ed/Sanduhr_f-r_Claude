# Run Brief: desk-2-37

**Asked (verbatim):** "start 2.37.0", then "yes run it unattended".

**Source:** `docs/mac-widgets-spec.md`, sections 8 (`get_desk` and `propose_desk`), 9 (the fold), 10 (meter
styles), 11 (font packs) and the 2.37.0 rows of section 16. Windows reference: `docs/widgets-3.8-spec.md`
section 7 (E to G), issues #314 and #336, as built in `windows-dotnet/` (read it, never edit it).

**Intent:** The Desk can be shaped by hand or by Claude: a slant per stack, four meter styles, font packs that
join the pickers, and Claude reading the Desk and proposing a whole design that waits for Apply.

**Done when:** All seven items are merged on `cart/desk-2-37`, and `cd mac && ./test.sh`,
`ruby smoke/selftest.rb` and `python3 -m pytest -q integrations/tests` pass there. The owner then runs the
live checks (items 2, 3, 4 and 6), and the 2.37.0 ship commit comes after.

**Out of scope:** `mac/Info.plist`, the release, any push or PR, anything under `windows-dotnet/`, 2.38.0
(system and weather pieces, bars, CI watchers), 2.39.0 (own sprites), any live network request in tests.

**Inferences:**
- Windows' `DeskArrangement.cs`, `DeskMeters.cs`, `PackFonts.cs`, `DeskDesign*.cs`, `ToolLogic.Desk.cs`,
  `DeskDesignService.cs` and the `get_desk`/`propose_desk` entries in `ToolCatalog.cs` are the line-by-line
  guide; their tests are the case list. The spec wins on Mac surfaces, Windows on rules and messages.
- Windows' Desk design carries fields the Mac has no surface for (monitors, wallpaper, Start-menu apps, the
  launcher, saved designs, system and weather pieces). The Mac `get_desk` omits what it can't read and
  `propose_desk` refuses those fields with a reason, never silently (spec 8).
- The spec says `get_desk` reads "the settings file the app already exports; no new file". If the Mac has
  no such export (Desk settings live in the `com.626labs.sanduhr.desk` defaults suite), the app writes an
  owner-only `desk-state.json` beside the other handoff files and PRIVACY covers it (ledger D2).
- As in 2.36.0, the Python MCP mirrors the Swift validator and shared fixtures keep them agreeing.
- What's New cards are added with version "2.37.0" before Info.plist moves.

**Mode:** unattended. The owner confirmed the recommendation (7 items, 15:45); auto mode is on.
