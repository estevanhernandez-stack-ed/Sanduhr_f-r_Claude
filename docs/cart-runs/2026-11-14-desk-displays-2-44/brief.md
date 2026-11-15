# Run Brief: desk-displays-2-44

## Ask
"1 yes 2 yes 3 I'll plug one in 4 automated", answering the four open questions of the Mac per-monitor
Desk design (2.44.0), after "1" picked "Per-monitor placement on the Mac Desk, a 2.44 minor".

## Source
- **Kind:** plan file
- **Location:** `docs/superpowers/specs/2026-11-14-mac-desk-per-monitor-design.md` (owner-approved
  2026-11-14, answers in its last section). Uncommitted in the builder's tree at run start; item 1 carries it
  onto the integration branch.
- **Parity source (read only):** `docs/superpowers/specs/2026-10-21-desk-per-monitor-design.md` and
  `windows-dotnet/src/Sanduhr.Core/DeskArrangement.cs` (`DeskMonitors`), `DeskMonitorNames.cs`,
  `DeskDesign.cs` (monitor validation), `DeskState.cs`.
- **House style:** `mac/Sources/Sanduhr/Desk/` (`DeskArrangement`, `DeskController`, `DeskModel`, `DeskView`,
  `DeskArrange*`, `DeskDesign*`, `DeskStateExport`, `OledCareController`), `Desk/SettingsView.swift`,
  `Desk/DeskLayoutMap.swift`, `mac/integrations/sanduhr_mcp.py` and its shared fixtures in
  `mac/Tests/SanduhrTests/Fixtures/desk-designs/`.

## Intent
On a Mac with more than one display, each Desk piece can sit on the main display, on every display or on one
named display, set in Settings, in Arrange Desk or by Claude through `propose_desk`. A Mac with one display, and
every 2.43 layout, looks exactly as it does today.

## Done when
- Items 1 to 8 are merged on `cart/desk-displays-2-44`.
- `cd mac && ./test.sh`, `ruby smoke/selftest.rb` and `python3 -m pytest -q integrations/tests` pass there.
- A 2.43 layout string reads and writes back unchanged and draws on the main display only.
- `get_desk` reports `monitors` and each placed piece's `monitor`; `propose_desk` accepts `monitor` and
  refuses an id that is not connected.

## Out of scope
- The live two-display pass and the two-window idle-CPU measurement: they need the owner's external
  display. Parked for the owner, never faked; the morning report says what to check.
- A wallpaper summary on the Mac, per-display anchors for one piece, Drawers, the companion's roam and the
  island on other displays (spec, Out of scope).
- The sanduhr-mods 0.2.1 skill follow-up: another repo, held for 2.44.0's release day.
- The release itself (dispatch, appcast, cask), any push or PR. Item 8 writes the release rows only.
- `windows-dotnet/`: read for parity, never edited.

## Inferred
- Integration and item branches cut from `main` at `83e5509b` (from: the run helper's init, builder's branch).
- The layout suffix is written so a 2.43 build reading a 2.44 string keeps the anchor (D1).
- Display identity is the `CGDisplayCreateUUIDFromDisplayID` string (from: spec, Behaviour).
- The idle-CPU bar with two windows is measured live, not in tests (from: spec, Testing; backlog #7).
- Version 2.44.0 build 66 (from: `mac/Info.plist` 2.43.2 build 65).

## Mode
- **Mode:** Unattended
- **Triggered by:** The ask said so ("automated", with "same run shape as" the 2.40 and 2.43 overnight runs).
  Auto mode was on for the session.
