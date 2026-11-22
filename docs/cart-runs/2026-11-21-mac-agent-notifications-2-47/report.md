# Run Report: mac-agent-notifications-2-47

9 merged, 0 held, 0 parked, 0 stopped.

## Ready to push
- **Integration branch:** `cart/mac-agent-notifications-2-47` (base `dde2e111`, `main`). Worktree:
  `~/.cwt/sanduhr-f-r-clau-mac-agent-notifications-2-47/int`. Nothing touches `windows-dotnet/`.
- **Commits:** 11, plus merges:
  - `42660bae` the design and plan (D in brief)
  - `6fcd7a5a` turn events and the notification rules
  - `a3d0973f` Codex and Antigravity turn readers
  - `1a6c96b1` follow every agent's turns (FSEvents, replay, signal files)
  - `205fe350` the Claude Code hook says which session and folder (3 events, one JXA script, about 48 ms a prompt)
  - `085808fc` notifications, the glow in the agent's color, turn cards
  - `3ede3630` and `2943b8f8`: wiring, and the fix so Claude Code follows the hook, not Claude usage (D9)
  - `10b52e7f` Settings ▸ Notifications
  - `43faed28` smoke hooks
  - `3e298667` the 2.47.0 rows: Info.plist 2.47.0 build 69, What's New `agent-notifications`, CHANGELOG
    `## Unreleased (mac)`, README, PRIVACY, smoke section 81
- **Checks on the integration branch (2026-11-21):**
  - `./test.sh`: 3909 tests in 424 suites passed (2.46.0 had 3662).
  - `ruby smoke/selftest.rb`: 1106 checks passed.
  - pytest: 372 passed.
  - `./build.sh`, the universal build: `x86_64 arm64`, exit 0.
  - Every item was checked again on its own branch by the orchestrator.
- **Close-out gates:** vibe-test and vibe-sec weren't run; the repo's own checks stand in. CI on macos-15 runs on
  the PR.

To push and open the PR:
```bash
git -C ~/.cwt/sanduhr-f-r-clau-mac-agent-notifications-2-47/int push -u origin cart/mac-agent-notifications-2-47
gh pr create --base main --head cart/mac-agent-notifications-2-47 --title "Mac 2.47.0: agent notifications" --body-file <body>
```
Commit this run folder with the PR. Rewrite the home paths in `state.json` to `~` first, as 2.46's was.

## Waiting on you
- **Smoke section 81, before the release.** Run it on fixture homes through the `notify` action, plus:
  - **A real Claude Code turn in a scratch folder,** with the updated hook installed there only. This is design
    check 1, deferred by D2: it confirms `cwd` and `session_id` reach the hook.
  - **Try it, quiet hours and permission denied.**
  - **Idle CPU** with Codex and Antigravity on, which item 8 couldn't measure without launching the app.

## Parked
None.

## Stopped
None.

## Decisions to skim
See `decisions.md`, D1 to D10. The ones with stakes:
- **D9:** Claude Code notifications follow the updated hook alone. The first wiring tied them to Claude usage,
  which would have silenced the glow for "Just the Desk" users after updating the hook. Caught at review and
  fixed.
- **D7:** One JXA script per hook event. On Stop it also writes item 66's background report, since stdin is read
  once. Every older hook generation reads Outdated and keeps glowing until Update.
- **D6:** The watcher uses one ticker, which runs only while an Antigravity end is armed or a folder is polling.
  FSEvents watches each agent home, and paths go through `realpath`.
- **D8:** Repeat finishes are dropped, so sleep and wake notifies once. Turn cards stay out of the band and are
  hidden in demo mode.
- **D10:** Try it ignores the switches. An "Allow Notifications" line appears while macOS hasn't been asked, so a
  default-on switch isn't silently dropped.
- **D3:** The Codex app counts as in front by its name "Codex" or a `com.openai.` bundle id. It's unverified on
  this Mac.

## Follow-ups noticed
- Smoke section 22 still describes the 2.46 hook in places (item 9 fixed only its labels).
- Pin the Codex app's bundle id on a Mac with Codex (D3).
- The Mac still calls the piece "Claude Code glow hook"; Windows renamed it "Claude Code hook".

## Your tree
Untouched; you're on `main`. Untracked at start: the design and the plan, both also committed on the integration
branch. Delete your copies before pulling the merged PR. This run folder is untracked too.
