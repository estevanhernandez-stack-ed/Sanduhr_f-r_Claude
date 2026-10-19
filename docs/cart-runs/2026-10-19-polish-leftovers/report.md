# Run Report: polish-leftovers

6 merged, 0 held, 0 parked, 0 stopped.

## Ready to push
- **Integration branch:** `cart/polish-leftovers` (base `9afee2a`, tip `af89cb3`), worktree `~/.cwt/sanduhr-f-r-clau-polish-leftovers/int`
- **Commits:** 6 item commits, each merged with a `merge: item NN <slug>` commit:
  - `6eed67e` fix(mac): the Desk page's Order caption is two sentences, the rest in the setup guide
  - `8b085b1` fix(mac): the meter-warning card's At column lines up on the shared value width
  - `72710ea` fix(mac): the Notch page says Needs the Desk once
  - `7808720` fix(mac): the Desk hex fields accept three-digit shorthand and expand it
  - `e9708a4` feat(mac): Arrange mode's Hide and Show have debug hooks, and the snapshot lists the hidden pieces
  - `09a64cd` refactor(mac): ModsSettings.swift split into the model, the types, the rows and the page
- **Checks:** every item ran `./build.sh --debug`, the full `./test.sh` and `ruby smoke/selftest.rb` in its worktree and again before its merge (1,806 to 1,813 tests; selftest 962 to 979). Close-out on the integration tip: build ✓, 1,813 tests in 236 suites passed, selftest 979 checks passed, pytest 223 passed, a secret and home-path grep over the run's diff (24 files, +1309/-1076, most of it the split) found nothing. The vibe gates cannot run on this Mac (previous runs, D10).
- **Not run:** the two touched scenarios (`settings-notch.yaml`'s new Desk-off segment, `desk-arrange.yaml`'s hide and show) against a live dev build; both pass the selftest's fixture run and rest on unit tests of the same model.

To push and open the PR:
```bash
git push -u origin cart/polish-leftovers
gh pr create --base main --head cart/polish-leftovers --title "fix(mac): the six leftovers from the ux-polish run" --body-file docs/cart-runs/2026-10-19-polish-leftovers/report.md
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
- D2: three-digit hex shorthand is accepted and expanded (case kept as typed), not refused.
- D5: `desk_arrange.hidden` means "not placed", so the Arrange scenario pins exact keys; `[]` holds only after cancel.
- D6: the model split hoisted eight stored properties to the end of the class (an `@Observable` extension cannot hold them); the one reorder in a move-only split.

## Found along the way, not fixed (for your call)
- `ModsCatalogView.swift` (727 lines) and `ModsSeatSections.swift` (503) are the next two files over 500.
- No CHANGELOG entries were written: the brief said these ride with the next minor. At ship time, one "Unreleased (mac)" bullet covers the user-visible four (the caption, the At column, Needs the Desk once, hex shorthand); the hooks and the split are not user-visible.

## Left untouched
Uncommitted files in your tree at run start. The run cut from `9afee2a` and did not stash, reset or check out over them.
- None.
