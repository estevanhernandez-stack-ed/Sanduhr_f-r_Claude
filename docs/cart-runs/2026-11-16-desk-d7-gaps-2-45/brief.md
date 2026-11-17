# Run Brief: desk-d7-gaps-2-45

## Ask
"take on D7", after the 2.44.0 release, naming D7 of `docs/cart-runs/2026-11-14-desk-displays-2-44/decisions.md`:
the four known gaps on extra displays. Then, to the two questions: the companion **roams its display**; run it
**automated, in a worktree**.

## Source
- **Kind:** decision ledger entry plus the morning report of the 2.44 run.
- **Location:** `docs/cart-runs/2026-11-14-desk-displays-2-44/decisions.md` D7 and `report.md` ("Known gaps on extra
  displays"). The spec they follow: `docs/superpowers/specs/2026-11-14-mac-desk-per-monitor-design.md`.
- **House style:** `mac/Sources/Sanduhr/Desk/` (`DeskWindowPlan`, `DeskController`, `DeskView`, `DeskModel`,
  `DeskHitTest`, `DeskClickLayer`, `DeskFold`, `DockClearance`, `DockFollower`, `CompanionDriver`,
  `CompanionLayerView`, `DeskArrange*`, `DeskDisplayChoices`), `mac/integrations/sanduhr_mcp.py`.

## Intent
A Desk window on a display besides the main one behaves like the main one: its pieces keep clear of that display's
Dock and notch, every copy of a piece on All displays takes clicks, a slanted stack takes clicks on its slanted
shape, and the companion lives on the display it is placed on. A Mac with one display is unchanged.

## Done when
- Items 1 to 5 are merged on `cart/desk-d7-gaps-2-45`.
- `cd mac && ./test.sh`, `ruby smoke/selftest.rb` and `python3 -m pytest -q integrations/tests` pass there.
- The one-display path builds and draws as 2.44.0 does (tests pin it).

## Out of scope
- The live two-display pass: the owner's, in smoke section 79 (item 5 writes it). Never faked.
- The release itself (dispatch, appcast, cask), any push or PR.
- Drawers, the island (wings, pill) and the camera badge on other displays.
- Two companions; a companion on All displays draws on the main display only.
- `windows-dotnet/`: read for parity, never edited.

## Inferred
- Base: `main` at `ee34fb3d` (the 2.44.0 CHANGELOG rename), from the helper's init.
- Version 2.45.0 build 67 (from `mac/Info.plist` 2.44.0 build 66): the companion on any display is new behaviour, so
  a minor, with a What's New card.
- The Dock on an extra display: always-shown reservation from that screen's `visibleFrame`; the auto-hiding Dock's
  slide stays on the main display unless the worker finds DockFollower already generalises cheaply (D-entry).

## Mode
- **Mode:** Unattended
- **Triggered by:** the owner picked "Automated, worktree" when asked. Auto mode on for the session.
