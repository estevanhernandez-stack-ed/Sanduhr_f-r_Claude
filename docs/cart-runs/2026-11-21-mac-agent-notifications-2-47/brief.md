# Run Brief: mac-agent-notifications-2-47

## Ask
"start the notifications design", then "approved, write the plan and run it". Owner's choices during design:
- drop Windows' taskbar flash (no fourth surface)
- Claude Code identity through a small hook script that writes a file per event
- notifications follow Alerts' quiet hours

## Source
- **Kind:** an approved design and its implementation plan.
- **Design:** `docs/superpowers/specs/2026-11-21-mac-agent-notifications-design.md`, which ports
  `docs/superpowers/specs/2026-11-17-agent-notifications-design.md`.
- **Plan:** `docs/superpowers/plans/2026-11-21-mac-agent-notifications.md`. Items 1 to 9 are its Tasks 1 to 9.

## Intent
A long agent turn that ends, or a Claude Code session waiting on you, reaches you through a macOS notification,
the notch glow and a Desk card, for all three agents. A Mac with the hook not updated and Codex and Antigravity
off behaves as 2.46.0.

## Done when
- Items 1 to 9 are merged on `cart/mac-agent-notifications-2-47`.
- `cd mac && ./test.sh`, `ruby smoke/selftest.rb` and `python3 -m pytest -q integrations/tests` pass there.
- `./build.sh` builds.

## Out of scope
- A waiting signal for Codex or Antigravity.
- The Antigravity IDE.
- A notification history.
- The Settings reorganization.
- The release.
- Any push.
- `windows-dotnet/`, which is read only.

## Inferred
- Base: `main` at `dde2e111`. The design and plan were untracked in the owner's tree, so the integration branch
  carries copies as its first commit (`42660bae`).
- Version 2.47.0 build 69.

## Mode
- **Mode:** Unattended.
- **Triggered by:** "run it", the same run shape as 2.46. Auto mode is on.
