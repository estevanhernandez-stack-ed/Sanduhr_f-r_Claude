# Run Report: no-notch-island

4 merged, 0 held, 0 parked, 0 stopped.

## Ready to push
- **Integration branch:** `cart/no-notch-island` (base `8cc7ac2`, tip `c8d72c4`), worktree `~/.cwt/sanduhr-f-r-clau-no-notch-island/int`
- **Commits:** 4 item commits, each merged with a `merge: item NN <slug>` commit:
  - `a90817a` docs: the Mac app's scope page, and the spec says why the click layer is not icon-aware
  - `a074cf7` feat(mac): NotchPill rules for a top-center island on screens without a notch, pinned
  - `99df763` feat(mac): a top-center pill brings the island to screens without a notch
  - `b28a82f` docs(mac): a smoke scenario and the by-hand check for the top-center pill, and what still needs the old Mac
- **Checks:** item 1 ran the path loop and the spec greps; items 2 and 3 ran `./build.sh --debug`, the full `./test.sh` and `ruby smoke/selftest.rb` in their worktrees and again before each merge (1,798 then 1,805 tests; selftest 940 then 945); item 4 parsed the scenario, grepped the action and the doc section, and ran the selftest (962). Close-out on the integration tip: build ✓, 1,805 tests in 236 suites passed, selftest 962 checks passed, pytest 223 passed, a secret and home-path grep over the run's diff (25 files, +893/-52) found nothing. The vibe-test and vibe-sec gates themselves cannot run here (D10).
- **Not run:** the scenario against a live dev build, and any look at the pill on a real screen (D4). The pill cannot show on this Mac without the force override, and it has never been drawn on a no-notch display.

To push and open the PR:
```bash
git push -u origin cart/no-notch-island
gh pr create --base main --head cart/no-notch-island --title "feat(mac): a top-center pill brings the island to screens without a notch" --body-file docs/cart-runs/2026-10-18-no-notch-island/report.md
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
- D2: main screen only; the menu-collision rule ships conservative (a 0.36 width cap, hide while the pointer is in the menu bar); the real-hardware rule stays on #119.
- D3: a notched Mac shows the pill only through the `notchPillForce` key (hook `notch-pill force`); the island's own path is untouched.
- D6: `NotchWingsView` gained a `pill` flag instead of a second view, so content, click and menu stay one code path.
- D7: with the pill up, every glow outlines the pill and the camera light sits on its frame.
- D9: the camera and mic badge on a no-notch Mac can overlap the pill; deferred to BACKLOG #6.

## What the morning should do by hand
1. Back up `~/Library/Application Support/Sanduhr`, quit the release app, build and launch the dev build from the integration worktree, `smoke/smoke enable`, then `smoke/smoke run scenarios/notch-pill.yaml` and `docs/mac-smoke-test.md` section 51: the pill over the menu bar, the click, the two-finger menu, the hide under the pointer, full screen.
2. Settings, Desk, Notch: the new toggle under the island switch and its caption; the preview's spoken line with the pill on.
3. Restore the support folder and relaunch the release app.

## Live pass (2026-10-18 22:41, before the push was reviewed)
Run from the integration worktree's dev build on this notched Mac, with the support folder backed
up and restored after and the release app relaunched:
- `smoke/smoke run smoke/scenarios/notch-pill.yaml`: 1 passed, 0 failed, all eight steps.
- Forced on, the pill drew as a 228 by 39 pt black capsule at the top center with the wing text
  (the time and the signed-out line), over the real island as force implies here.
- `notch_pill.shown` read true with the pointer away, false with the pointer in the menu bar, true
  again after it left, false after `notch-pill off`.
- Settings, Desk, Notch shows the toggle under the island switch with its two-sentence caption.
Still unseen: the pill on a real no-notch display, full screen and Spaces, the glow and the camera
light on the pill. Those stay in section 51 of `docs/mac-smoke-test.md`.

## Found along the way, not fixed (for your call)
- The camera and mic badge on a no-notch Mac still goes to the top-center tab, under or over the pill (`AVIndicatorPlacement.spot` ties islandUp to the wings window; the pill view already carries the beside-wing slot). BACKLOG #6, D9.
- The pill's text size follows `textSize(barHeight)`, about 10 pt on a 24-pt external-display bar; a floor may be wanted once seen on hardware. BACKLOG #6.
- `mac/README.md` has no island section to sit next to, so "Top-center pill" went in before "Camera and mic indicators".
- The by-hand smoke doc numbers sections chronologically, so the pill is section 51 at the end rather than beside the notch sections.
- Item 4's listed verify grepped `DebugHooks.swift` for the action name; the literal lives in `DebugCommand.swift`. The check was run against the right file.

## Left untouched
Uncommitted files in your tree at run start. The run cut from `8cc7ac2` and did not stash, reset or check out over them.
- None.
