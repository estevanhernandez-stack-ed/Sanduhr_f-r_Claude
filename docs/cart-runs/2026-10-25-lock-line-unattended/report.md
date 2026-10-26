# Run Report: lock-line-unattended

3 merged, 0 held, 0 parked, 0 stopped.

## Ready to push
- **Integration branch:** `cart/lock-line-unattended` (base `750dc99`, tip `02f4778`), worktree `~/.cwt/sanduhr-f-r-clau-lock-line-unattended/int`
- **Commits** (each item merged with a `merge: item NN <slug>` commit):
  - `5e2082c9` feat(mac): lock_line in the smoke snapshot, a scenario and the by-hand section (#137)
  - `f3fcd297` docs(mac): the Settings page count reads seventeen
  - `69e2e961` feat(mac): Settings, Lock Screen, and the lock-line.txt writer (#137)
  - `abaf7a6e` feat(mac): LockLine composes the lock screen line and sanitizes every slot (#137)
- **Checks:** every item ran `./build.sh --debug`, the full `./test.sh` and `ruby smoke/selftest.rb` in its worktree and again before its merge. Close-out on the integration tip: build ✓, 1,866 tests in 240 suites passed, selftest 1,030 checks passed, pytest 223 passed, and a secret and home-path grep over the run's diff found nothing.
- **Seen live:** `smoke/scenarios/lock-line.yaml` passed on the integration build, 10 of 10 steps (demo usage, the page, `lock_line.text` with "Claude N%", a waiting test watcher adding "1 waiting for you"). The page itself was looked at in demo mode: the switch with the later-build line, the prefix, the three slots, the preview `Claude 63% · resets 8:03 PM · 1 waiting for you` and "47 of 80 characters".

To push and open the PR:
```bash
git push -u origin cart/lock-line-unattended
gh pr create --base main --head cart/lock-line-unattended --title "feat(mac): the lock screen line, composer, settings and file writer (#137)" --body-file docs/cart-runs/2026-10-25-lock-line-unattended/report.md
```
Pushing waits for your say-so. Merging is safe; **releasing is not until the root writer lands** (D4): the switch writes a file nothing reads yet.

## Waiting on you
None.

## Parked
None.

## Stopped
None.

## Decisions to skim
Full ledger: `decisions.md`. Highest stakes:
- D4: nothing from this run ships until the attended daemon lands; the switch row says so.
- D2: in-app usage reads `UsageViewModel`, not `snapshot.json`.
- D7: the writer runs its own controller and observers, independent of the Desk, with its timer only while on and the session active; the tier is the highest across every tier.
- D6: one rejection function serves the sanitizer and the Settings reason; narrow no-break spaces become plain spaces.

## Found along the way, not fixed (for your call)
- The page shows "Prefix" as both the group heading and the row label; one of them can go.
- The slots are switches, not checkboxes as the spec said; consistent with the rest of Settings.
- `state.yaml` has no `installed` key yet; it comes with the daemon.
- Next is the attended session: measure the line length and whether `LoginwindowText` updates without a logout on macOS 26, then the daemon, its CI signing and install and uninstall (spec, Build order, step 3).

## Left untouched
Uncommitted files in your tree at run start: none.
