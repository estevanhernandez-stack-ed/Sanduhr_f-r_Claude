# Run Report: mac-followups-2-48

6 merged, 0 held, 0 parked, 0 stopped.

## Ready to push
- **Integration branch:** `cart/mac-followups-2-48` (base `97b44fa4`, `main`). Worktree:
  `~/.cwt/sanduhr-f-r-clau-mac-followups-2-48/int`. Nothing touches `windows-dotnet/`.
- **Commits:**
  - `9017e40c` a sparkline on Codex's widget cards
  - `3dc264a3` a Codex limit pulses the Desk meters that show Codex (D3)
  - `65ed9327` a Codex chip in Combine, with `codex` and `nocodex` as `--mine` names (D4)
  - `d3212bc6` Trends includes every agent (D5)
  - `904c70d5` "Claude Code hook" replaces "Claude Code glow hook"; smoke section 22 brought up to the 2.47 hook
  - `3acaa40d` 2.48.0 build 71: What's New `codex-everywhere`, CHANGELOG `## Unreleased (mac)`, README, and the
    consent headline "Let Claude Code tell Sanduhr when it finishes or needs you?"
- **Checks on the integration branch:**
  - `./test.sh`: 3980 tests in 431 suites passed.
  - `ruby smoke/selftest.rb`: 1106 checks passed.
  - pytest: 384 passed.
  - `./build.sh`: the universal build ran at close-out.
  - Every item was also checked on its own branch.

To push:
```bash
git -C ~/.cwt/sanduhr-f-r-clau-mac-followups-2-48/int push -u origin cart/mac-followups-2-48
gh pr create --base main --head cart/mac-followups-2-48 --title "Mac 2.48.0: Codex in more places" --body-file <body>
```
Commit this run folder with the PR, with its `state.json` paths changed to `~`.

## Waiting on you
- A look at the new pieces live:
  - the Codex sparkline
  - a Codex pulse on a Desk meter showing Codex
  - the Combine chip
  - Trends with agents

  None needs a real Codex: the 2.46 fixture homes and smoke hooks cover them.

## Decisions to skim
`decisions.md`, D1 to D5. The ones with stakes:
- **D3:** Codex alerts now pulse the Desk, which reverses 2.46's D9. A Codex alert with Desk-only delivery and no
  meter showing Codex stays a notification instead. Combined meters now also pulse for Claude alerts.
- **D4:** Older statuslines print byte-identical output (pinned for 13 lists). In a sheet with Session and
  Weekly off, Codex now stays until its own chip is off.
- **D5:** Trends counts every agent with tokens in the span; idle agent roots change nothing.

## Your tree
Untouched; you're on `main`. This run folder is untracked.
