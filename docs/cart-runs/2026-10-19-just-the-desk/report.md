# Run Report: just-the-desk

4 merged, 0 held, 0 parked, 0 stopped.

## Ready to push
- **Integration branch:** `cart/just-the-desk` (base `3452139`, tip `6e119c7`), worktree `~/.cwt/sanduhr-f-r-clau-just-the-desk/int`
- **Commits:** 5 item commits, each item merged with a `merge: item NN <slug>` commit:
  - `496b976` feat(mac): the welcome tour has a Desk-only variant, offered after Just the Desk
  - `8f9878c` feat(mac): a just-the-desk debug hook and scenario drive the Desk-only tour
  - `f5c8797` fix(mac): the just-the-desk scenario waits for the tour's offer before closing it (review catch, D11)
  - `e6ec737` docs: the landing page and README say the Mac's Desk works without a Claude subscription
  - `4ae98d6` docs: BACKLOG #4 closes on the Mac, with its open questions answered
- **Checks:** items 1 and 2 ran `./build.sh --debug`, the full `./test.sh` and `ruby smoke/selftest.rb` in their worktrees and again before their merge (1,820 then 1,822 tests; selftest 979 then 1,008 checks); items 3 and 4 are copy and ran their greps and an HTML parse. Close-out on the integration tip: build ✓, 1,822 tests in 236 suites passed, selftest 1,008 checks passed, pytest 223 passed, every scenario parses, and a secret and home-path grep over the run's diff (21 files, +255/-22) found nothing. The vibe gates cannot run on this Mac (previous runs, D7 of polish-leftovers).
- **Not run:** `smoke/scenarios/just-the-desk.yaml` against a live dev build. It rests on the tour tests and the fixture selftest, and its timing (the offer opens the tour a second after the choice) is pinned by a 1.5 s wait that only a live run proves. Needs the release app quit and the support folder backed up, as every dev-build pass does.

To push and open the PR:
```bash
git push -u origin cart/just-the-desk
gh pr create --base main --head cart/just-the-desk --title "feat(mac): Just the Desk completes on the Mac: a Desk-only tour, its hook, the landing copy" --body-file docs/cart-runs/2026-10-19-just-the-desk/report.md
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
- D1: the run was unattended by inference from "take it", not by the ask.
- D2: the tour gets a Desk-only variant (three steps, Sign in any time last), offered once after the choice; D9: Take the Tour… shows it for as long as Just the Desk is on.
- D3: the Keychain prompt question closes with no change.
- D11: the scenario waits for the offer before closing the tour; `show()` resets the step.
- D12: the smoke restore plan ends Just the Desk when a run turned it on.

## Found along the way, not fixed (for your call)
- No CHANGELOG entries were written (D5): the ship commit for 2.29.0 adds them under "Unreleased (mac)", together with the four user-visible leftovers from the polish-leftovers run (the Order caption, the At column, Needs the Desk once, hex shorthand). The What's New card: "A tour for the Desk on its own", destination `.notch` or the Desk page.
- The hub page at 626labs.dev/sanduhr still describes the Mac app as needing a subscription (the owner's other machine).
- `just-the-desk off` does not re-run the widget's visibility rule, so a widget the `on` hid stays hidden until the next event; the welcome sheet's button has no off, so nothing user-facing is affected.

## Left untouched
Uncommitted files in your tree at run start. The run cut from `3452139` and did not stash, reset or check out over them.
- None.
