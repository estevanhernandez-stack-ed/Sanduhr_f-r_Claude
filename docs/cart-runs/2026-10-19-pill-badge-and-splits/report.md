# Run Report: pill-badge-and-splits

4 merged, 0 held, 0 parked, 0 stopped.

## Ready to push
- **Integration branch:** `cart/pill-badge-and-splits` (base `2afdc64`, tip `680ef11`), worktree `~/.cwt/sanduhr-f-r-clau-pill-badge-and-splits/int`
- **Commits:** 6 item commits, each item merged with a `merge: item NN <slug>` commit:
  - `e953898` fix(mac): the camera and mic indicators count the pill as the island
  - `968025e` fix(mac): applyPill re-runs the camera and mic placement (review, D8)
  - `f6fb559` refactor(mac): ModsCatalogView.swift split into the view, the rows and the card
  - `952303f` refactor(mac): ModsSeatSections.swift split into the seat sections and the repo sections
  - `28cf838` refactor(mac): the seat sections' byte formatter is seatBytes, not a module-wide bytes (review, D10)
  - `2a89cbf` docs: BACKLOG #3 reads shipped and #6 records the live pill check and the badge fix
- **Checks:** items 1 to 3 ran `./build.sh --debug`, the full `./test.sh` and `ruby smoke/selftest.rb` in their worktrees and again before their merge (1,826 tests; selftest 1,008 checks); both splits were also checked as line multisets against their originals (only the headers and the named visibility changes differ). Item 4 ran its grep. Close-out on the integration tip: build ✓, 1,826 tests in 236 suites passed, selftest 1,008 checks passed, pytest 223 passed, and a secret and home-path grep over the run's diff (12 files, +744/-681, most of it the splits) found nothing. The vibe gates cannot run on this Mac (previous runs).
- **Not run:** the badge fix on a real no-notch Mac with the camera in use. The old Mac's session confirmed the bug before the fix (its camera dot mode must be set to always for the check); the same check after this ships is the proof.

To push and open the PR:
```bash
git push -u origin cart/pill-badge-and-splits
gh pr create --base main --head cart/pill-badge-and-splits --title "fix(mac): the camera and mic indicators count the pill as the island; the last two Mods views split" --body-file docs/cart-runs/2026-10-19-pill-badge-and-splits/report.md
```
Pushing waits for your say-so. Tell Cart "push it", or name a subset.

## Waiting on you
None.

## Parked
None.

## Stopped
None.

## Decisions to skim
Full ledger: `decisions.md`. Highest stakes:
- D2: the island is up for the indicators when the wings window or the pill window exists; D7: `pillWindow != nil`, not its visibility, so a pointer pass through the menu bar does not bounce them to the tab.
- D6: the Settings Notch preview draws the real pill in place of the tab when the pill switch is on.
- D8: `applyPill()` ends with an explicit indicator re-placement.
- D10: the seat split renamed `Caption`/`SubHeading` to `SeatCaption`/`SeatSubHeading` (name clashes) and the byte formatter to `seatBytes`.

## Found along the way, not fixed (for your call)
- The old Mac's menu-collision numbers recommend `NotchPill.maxWidthFraction` 0.30 instead of 0.36 (the pill then always clears the status items at 1680 pt wide); one constant and a test pin, not done here without your word.
- No CHANGELOG entries (these ride with the next minor): one "Unreleased (mac)" bullet for the badge under the pill; the splits are not user-visible.
- No Mods view file is over 500 lines now; the next largest files in `Sources/` are a later scan's business.

## Left untouched
Uncommitted files in your tree at run start. The run cut from `2afdc64` and did not stash, reset or check out over them.
- None.
