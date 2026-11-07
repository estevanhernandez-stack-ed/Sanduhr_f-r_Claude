# Run Report: widget-from-file-2-42

5 merged, 0 held, 0 parked, 0 stopped.

Mac 2.42.0, Install widget from file (#408, the Mac half), built in about 40 minutes on 2026-11-07 from 06:59 to 07:35, unattended layout, five items, every check green. Ready for the owner's live walk and the push step.

## Ready to push
- **Integration branch:** `cart/widget-from-file-2-42` (base `906f7feb`, the spec commit, which sits on local `main` one ahead of `origin/main`)
- **Commits** (feature commits; each item also has a local merge commit):
  - `857bcdc9` feat(widgets): WidgetFileInstall reads, checks and installs a pack from a zip
  - `15f24d76` test(widgets): prove the and-N-more join of WidgetFileInstall
  - `50823d52` feat(widgets): Install widget from file in Settings, Widgets; a sprite becomes the companion
  - `1890d5d2` test(smoke): section 75, install a widget pack from a file
  - `61b79ec6` release(mac): 2.42.0 lockstep, the widget-from-file card, CHANGELOG, PRIVACY and README
  - `04c89624` fix(widgets): review fixes for install widget from file
  - plus this run folder, committed last
- **Checks:**
  - Item 1: `./test.sh --filter WidgetFileInstall` 11 tests; full `./test.sh` 3063 tests, 338 suites. Pass.
  - Item 2: full `./test.sh` 3074 tests, 340 suites; `./build.sh --debug` built. Pass.
  - Item 3: `ruby smoke/selftest.rb` 1063 to 1090 checks; `./test.sh --filter Debug` 54 tests; full `./test.sh` 3078 tests, 342 suites. Pass.
  - Item 4: `plutil -lint mac/Info.plist` OK; `./test.sh --filter WhatsNew` 55 tests. Pass.
  - Item 5 (after its fixes): full `./test.sh` 3078 tests, 342 suites; `ruby smoke/selftest.rb` 1090; `python3 -m pytest -q integrations/tests` 310 passed; band-fixtures `generate.ts --check` 32 cases up to date; `./build.sh --debug` built; home-path scan over fixtures, scenario and tests: nothing. Pass.
  - Close-out gates (vibe-test, vibe-sec): not run, Node-oriented; the checks above stand in (D13), as on the two previous Mac runs.

What shipped on the branch:
- `WidgetFileInstall` (pure): byte cap, `WidgetZip.read`, `sanduhr_min` floor, strict `PackValidator`, `WidgetStore.install(source: .local, replaceMadeHere: true)`, the spec's sentences.
- Settings ▸ Widgets ▸ **Install widget from file…** beside **New sprite from an image…**: a zip-only `NSOpenPanel`, one read, the status line under the button; a sprite becomes the companion through `CompanionSettings` only, with the three Desk-state lines.
- Smoke: hooks `widget-from-file <abs .zip>` and `widget-remove <id>` (made-here packs only), `state.yaml` `widgets:` block, scenario `widget-from-file.yaml` with two committed fixture zips (invented names, 2.6 KB total, rebuilt by `build.py`), smoke doc section 75.
- Release: `mac/Info.plist` 2.42.0 build 57; What's New card `widget-from-file` ("Install a widget from a file", destination Widgets); CHANGELOG under `## Unreleased (mac)` ▸ New; PRIVACY's 2.42.0 paragraph and "A widget from a file" entry; `mac/README.md` section.

To push and open the PR, after the live walk:
```bash
git push -u origin cart/widget-from-file-2-42
gh pr create --repo estevanhernandez-stack-ed/Sanduhr_f-r_Claude --base main --head cart/widget-from-file-2-42 \
  --title "feat(mac): 2.42.0, install a widget pack from a file (#408)" --body-file docs/cart-runs/2026-11-07-widget-from-file-2-42/report.md
```
Pushing waits for your say-so. Tell Cart "push it". After the PR merges, `git pull --ff-only` on `main` works because the branch descends from the local spec commit.

## Waiting on you
None held. The live walk, on a dev build (quit `/Applications/Sanduhr.app`, back up `~/Library/Application Support/Sanduhr`, never sign in): smoke section 75, plus the things only a running app shows:
- The file panel: zip-only filter, Cancel leaves no sentence, a real SpriteForge export walks on the Desk.
- The status line wrapping at Settings width; a multi-error refusal is up to three sentences plus "and N more."
- Where "Desk off" and "Companion hidden" land when Set up Sanduhr's "A nicer desktop" is off. "Desk on" reads only `DeskController.enabledKey`.

## Parked
None.

## Stopped
None.

## Decisions to skim
Full ledger: `decisions.md`, D1 to D14. Highest stakes:
- D2: the spec commit sits on local `main` one ahead of origin; `main` fast-forwards after the PR merges.
- D5: no `get_widgets` tool exists and nothing reports `source`; docs say "listed as made here".
- D7: a `widget-remove` hook exists so a smoke run cleans up; it refuses catalog packs.
- D11: the review found refusal sentences joined without periods and fixed it with a test.
- D13: no vibe gates; the repo's own checks stand in.

## Left untouched
Uncommitted files in your tree at run start: none. The run cut from the last commit and did not stash, reset or check out over your tree; you are on `main` at `906f7feb`.

Item worktrees were removed on merge. The integration worktree stays at `~/.cwt/sanduhr-f-r-clau-widget-from-file-2-42/int` until the push step.
