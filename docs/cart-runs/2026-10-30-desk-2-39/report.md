# Run Report: desk-2-39

**7 merged (item 6 after the owner's live check), 0 held, 0 parked, 0 stopped.**

## Ready to push

`cart/desk-2-39` (worktree `~/.cwt/sanduhr-f-r-clau-desk-2-39/int`), cut from `a2961bc4` (main after #397).

| Item | Commit | What |
|---|---|---|
| 1 | `d396eb92` | `WidgetNewSprite`: one PNG, a cell size and tags into a strict-valid `my-<slug>` sprite pack |
| 2 | `d6b26125` | Settings ▸ Widgets ▸ **New sprite from an image…**: the cell grid sheet, tagging, Make |
| 3 | `745f12aa` | `propose_sprite_motion`; `desk-state.json` lists made-here sprites (id, name, frame size, cells) for `get_desk` |
| 4 | `e903ac6c`, `7bd8da99` | Motion review banner: Preview on the Desk, Apply, Decline; Apply keeps the sprite's source (session fix) |
| 5 | `9f6597c1` | `AudioBars` / `AudioBarMotion` ported from Windows, pure, nothing user-visible |
| 7 | `76d29e26`, `b29f15c5` | PRIVACY, README, smoke sections 71-72, What's New `new-sprite` and `sprite-motion`, CHANGELOG |
| live fix | `9fc9ccb6` | The companion stopped after making or moving a sprite: a rebuilt view's onAppear ran before the old one's onDisappear; the driver now counts its piece views (since 2.35.0) |
| 6 | `f2b445ca`, `701dcb94`, `f3bcec0a`, merge `002fc1f7` | The audio tap and live bars; the tap takes the player's helper processes (Chrome plays through one); copy finalised from the live check |
| owner add | `c73849d2` | Hide while paused waits 15 s before hiding, so a short pause leaves the title to click |
| card | `98452dda` | What's New `live-bars` (table at 78), CHANGELOG bars line |

Checks on the integration branch after the live session: `./test.sh` 2719 passed, `python3 -m pytest -q integrations/tests`
310 passed, `ruby smoke/selftest.rb` 1033 passed. Each item's checks were re-run by the session before its merge.

```
git push -u origin cart/desk-2-39
gh pr create --base main --head cart/desk-2-39 --title "Mac 2.39.0: a new sprite from your own art, Claude maps its motion"
```

The ship commit (Info.plist 2.39.0 / 51) keeps the `## Unreleased (mac)` heading, after the live checks below
and after the call on item 6.

## Live check results (2026-10-31)

- New sprite, motion proposal, Preview and Apply: worked; Apply kept the art and the source.
- Companion: fixed in `9fc9ccb6`, then moved through three corners and kept running.
- Audio bars: the prompt came at the first tap and was allowed; the grant is under Screen & System Audio
  Recording, **System Audio Recording Only**; bars move with YouTube Music in Chrome after `701dcb94`. The
  exact prompt wording was not recorded. Still open: Safari, the denied path, and the grant across a
  Sparkle update (after release).
- Hide while paused: the owner's own setting; the 15 s grace was added on request.

## Live checks before the ship commit

- **New sprite (smoke 71):** a small and a large sheet, crisp pixels, clicks hit the right cell, walk order
  `#1, #2`, a cell size that misses refuses with its sentence, Make installs it as made here and it is pickable
  as the companion.
- **Motion (smoke 72):** `get_desk` lists the sprite; `propose_sprite_motion` raises the banner and a
  notification; Preview on the Desk moves it with the piece hidden; Apply bumps the patch and keeps the art;
  Decline; a Claude-made sprite stays Claude's after Apply. Check `desk-state.json` is mode 600.
- **Known cosmetic gap (D8):** start a widget preview while a motion preview shows; the motion banner may still
  say "End preview".

## Parked

None.

## Stopped

None.

## Decisions to skim

`decisions.md`, D1 to D10. The ones with stakes:
- **D4:** a new sprite needs `sit` and two `walk` cells; the other three fall back to `sit`.
- **D5/D6:** Claude reads sprites only as id, name, frame size and cell count, from `desk-state.json`, and
  proposes the motion tables whole.
- **D7:** a motion Apply keeps the sprite's source; caught at review, the worker had turned it local.
- **D9/D10:** the tap's keys, the 3 s denied rule, the Settings URL, and the deviations listed above.

## Notes

- The builder's tree had no uncommitted files at run start and is still on `main`.
- Item 1's worker hand-back came back empty; the session read the commit and re-ran the suite before merging.
- Item 7's CHANGELOG first carried a "Not in this release" note about the bars, written to the owner; removed.
- Close-out gates: vibe-test and vibe-sec are not run on this Swift repo; the repo's own three checks stand in.
- Old worktrees still on disk from earlier runs: `~/.cwt/sanduhr-f-r-clau-desk-2-37/int`,
  `~/.cwt/sanduhr-f-r-clau-widgets-2-36/int`.
