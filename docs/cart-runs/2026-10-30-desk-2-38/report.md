# Run Report: desk-2-38

**6 merged, 1 held, 0 parked, 0 stopped.**

## Ready to push

`cart/desk-2-38` (worktree `~/.cwt/sanduhr-f-r-clau-desk-2-38/int`), cut from `95000929` (main after #389).

| Item | Commit | What |
|---|---|---|
| 1 | `cb61e4a0` | The system piece: `telemetry` layout word, CPU, memory, disk every 5 s while shown, meter styles |
| 2 | `41513b80` | Weather: Open-Meteo lookup and forecast service, Settings ▸ Weather, nickname rules |
| 3 | `7715b8ed` | The weather piece on the Desk; `telemetry` and `weather {nickname}` in `propose_desk`/`get_desk`, Swift and Python |
| 5 | `995032a6` | Runner log rules ported from Windows (containers, frames, grammar, board, follow) |
| 6 | `631e73d9` | CI watchers over Docker's local socket: two-request enum, boundary test, off-by-default switch |
| 7 | `0e4b763a`, `4ea6e46a` | PRIVACY, README, smoke sections 68-70, three What's New cards, CHANGELOG; nickname wording fix |

Checks on each item's branch, re-run by the session before each merge: `./test.sh` (2601 tests at the end),
`python3 -m pytest -q integrations/tests` (298), `ruby smoke/selftest.rb` (1033). The same three are run
on the integration branch at close.

To ship, after the live checks below: push the branch, open the PR, then the ship commit (Info.plist
2.38.0 / 50) keeps the `## Unreleased (mac)` heading.

```
git push -u origin cart/desk-2-38
gh pr create --base main --head cart/desk-2-38 --title "Mac 2.38.0: system and weather pieces, CI runner cards"
```

## Waiting on you

- **4. Bars spike** (`cart/desk-2-38--04-bars-spike`, `0aeda0a2`): `docs/spikes/2026-10-30-audio-tap-bars.md`.
  The only per-app level on macOS is a Core Audio process tap, and it needs System Audio Recording. A denied
  tap reads as silence with no error. The call: is that permission, opt-in and for one peak value, an
  acceptable ask for decorative bars? Recommendation: not in 2.38.0; keep the position bar and the backlog
  item open. The spike doc can merge either way as the record.

## Live checks before the ship commit

- **System:** place it (Settings ▸ Desk), readings move about every 5 s, stop with the Desk off; all four
  styles; the memory figure against Activity Monitor (D4: it reads a little higher).
- **Weather:** look up a real city, the piece draws the reading, the nickname shows, Settings edits show at
  once; a `propose_desk` with `weather {nickname}` through review, Preview and Apply; `get_desk` returns it.
- **CI runners:** start Docker Desktop (its socket is absent while it is quit), turn on Show my CI runners'
  jobs, check cards for a real runner container, the "Docker isn't running." line with Docker quit, and the
  notch glow on a failed job.

## Parked

None.

## Stopped

None.

## Decisions to skim

`decisions.md`, D1 to D10. The ones with stakes:
- **D1:** the system piece's layout word is `telemetry`, as on Windows.
- **D6:** `DeskModel` reaches the main-actor weather service with `MainActor.assumeIsolated`; every current
  caller is on main, a new off-main caller would crash.
- **D9:** runner cards reach the Claude Code band as kind and state only, no job or repo name.
- The nickname copy was corrected at review: it is never sent to Open-Meteo, but `get_desk` hands it to Claude.

## Notes

- The builder's tree had no uncommitted files at run start.
- PR #366 (cask homepage and description) is still open and green; the merge was refused by the permission
  check, so it needs the owner.
- A bare `swift build` on this Mac picks SDK 27 and fails in `WidgetSettings.swift`; `build.sh` and `test.sh`
  pin a 26.x SDK on purpose. Not a run defect.
- Close-out gates: vibe-test and vibe-sec are not run on this Swift repo; the repo's own three checks stand in.
