# Run Brief: mac-followups-2-48

## Ask
"start 1 as a run": the 2.46 and 2.47 follow-ups as one small release, with no separate design pass, as offered.

## Source
- **Kind:** the follow-ups noticed in two earlier runs.
- **Where:** the "Follow-ups noticed" sections of `docs/cart-runs/2026-11-20-mac-agent-sources-2-46/report.md`
  (D9, D10, D12, D13, D16) and `docs/cart-runs/2026-11-21-mac-agent-notifications-2-47/report.md`.

## Intent
Close the loose ends the agent-sources and notifications releases left. A Mac with only Claude on behaves as
2.47.1.

## Done when
- Items 1 to 6 are merged on `cart/mac-followups-2-48`.
- `cd mac && ./test.sh`, `ruby smoke/selftest.rb` and `python3 -m pytest -q integrations/tests` pass there.
- `./build.sh` builds.

## Out of scope
- The Desk looks (waits on Windows #532).
- The Settings reorganization (needs a design).
- Issue #314.
- The release.
- Any push.

## Inferred
- Version 2.48.0 build 71. Items 1 to 4 are features, so it's a minor with a What's New card.
- Base: `main` at `97b44fa4`.

## Mode
Unattended ("start 1 as a run"). Auto mode is on.
