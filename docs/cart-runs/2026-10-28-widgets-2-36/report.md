# Report: widgets-2-36

**5 merged, 0 held, 0 parked, 0 stopped.**

## Ready to push

Integration branch `cart/widgets-2-36` (base `adf96201`), worktree `~/.cwt/sanduhr-f-r-clau-widgets-2-36/int`.

| Item | Commits |
|---|---|
| 1. WidgetProposal and WidgetOwnArt in Swift | dfc921a0 |
| 2. propose_widget in the Mac MCP | b8b7cf2d, c97065d5 (ship the validator beside the script) |
| 3. The app's review flow | 5c77ae53 |
| 4. WidgetSuggestionBanner and Use my own art | 2dd13aac, 77338efc, 3372f7c7 |
| 5. Docs, What's New, CHANGELOG | 82a5df96, d965ba34 |

Close-out on the integration branch (session-run):
- `./test.sh`: 2292 tests in 272 suites passed.
- `ruby smoke/selftest.rb`: 1033 checks passed.
- `python3 -m pytest -q integrations/tests`: 261 passed, 101 subtests.
- `./build.sh --debug`: built; `Contents/Resources/integrations/widgets/validate.py` present. (A release `./build.sh` stops at signing on this Mac, as expected: CI signs.)

To push and open the PR:

```
git push -u origin cart/widgets-2-36
gh pr create --base main --head cart/widgets-2-36 --title "feat(mac): 2.36.0 Claude-made widgets and your own art"
```

## Waiting on you

No held items. Live checks only a running Desk can confirm (dev build, signed out, Application Support backed up first):
1. `propose_widget` from Claude Code with a sprite: the notification opens Settings ▸ Widgets, the banner shows, Preview on the Desk shows the sprite as the companion even with the piece Hidden, and Dismiss / Apply / a newer proposal take it down.
2. A palette proposal: Preview on the Desk recolors the Desk ink (clock, Claude line, meters, now playing, watchers, meetings) and it returns after.
3. Apply with Make it the companion: the companion appears at once and Settings ▸ Widgets lists the pack without reopening.
4. Use my own art… on a made-here sprite: the PNG-only panel per stage, a bad file's sentence inline, a good one reinstalls at 1.0.1 and the Desk shows the new art.
5. Settings search for "own art" lands on the first eligible row.

## Parked

None.

## Stopped

None.

## Decisions to skim

`decisions.md`, D1 to D9. The ones that matter most:
- **D6:** the shipped app didn't carry `widgets/validate.py`; item 2 now ships it via build.sh, IntegrationScripts' stamp and install.sh (dashboard gotcha OxHuJlwCwsjS23m9eEiD).
- **D7:** preview starts only on the button and lives in memory; Windows previews on arrival (dashboard YFOPIRypnTMnS3h3vc0f).
- **D9:** the banner shows only while a proposal waits; no search entry for it.
- **D5:** the MCP checks a proposed pack in a transient temp folder, because the vendored validate.py reads only folders; PRIVACY says so.

Builder's tree at start: on `main`, no uncommitted files. Untouched by the run apart from this folder.
