# Mac lock screen helper repair: short design (2.47.1)

Issue #356. Owner-approved 2026-11-22 ("go with it as written").

## Why
On the owner's Mac the lock screen helper stopped running on 2026-11-03 and nobody knew for 19 days. The lock
screen showed a stale line. After the Login Items toggle, the switch read on while macOS reported the helper
`notRegistered`, and the caption said "Off…". Nothing offered to fix it.

## Goals
1. **A new state: on but not registered.** With the switch on (`enabled`) and `SMAppService.status` not
   registered, the caption reads "The lock screen helper isn't registered, so the lock screen keeps an old
   line.", and a **Turn it on again** button calls `LockLineDaemon.install()`. `state.yaml`
   `lock_line.helper` reports it.
2. **A notification when the line stops updating.** It fires when the switch is on, the state is `.stalled`
   or the new not-registered state, and the existing 3-minute judge window has passed.
   - Text: "The lock screen line stopped updating."
   - Clicking it opens Settings ▸ Lock Screen.
   - At most once per 24 hours, and never in demo mode.
   - It is checked at launch, on wake, and on the existing 5-minute recompose.

## Non-goals
- **No automatic re-registering.** It would bring an unexpected password prompt, and 2.39.3 saw macOS refuse it
  without the user's OK.
- **No menu bar or widget badge.** The owner picked the notification only.

## Approaches
- **Notification (picked).**
- **A badge on the menu bar or widget:** quieter, but missable, which is how the 19 days happened.
- **Both:** rejected.

## Plan (one item)
- `LockLineDaemon`:
  - A new `State.unregistered` (raw value `unregistered`).
  - `state(enabled:status:legacy:)` maps `enabled && .notRegistered` (or not found as legacy) to `.unregistered`.
    Switch off with not registered stays `.off`.
  - Its caption.
  - `static func shouldWarn(state:, enabled:, demo:, lastWarned: Date?, now:, watchingSince:) -> Bool`,
    which is pure and uses the 24 h and 3 min rules.
- `LockScreenSettings`: the **Turn it on again** button for `.unregistered`, beside the existing **Open Login
  Items…** for stalled. The state is read with the switch.
- A warning poster: through `Notifier`'s own path, identifier `lockline-stopped`, its click routed to Settings
  ▸ Lock Screen. The last warning's time lives in defaults (`lockLineWarnedAt`).
- Debug snapshot: `lock_line.helper` gains `unregistered`.
- Rows:
  - `mac/Info.plist` 2.47.1, build 70, with no What's New card.
  - CHANGELOG `## Unreleased (mac)` Fixes.
  - The README's lock screen section.
  - Smoke section 52 gains a step: Login Items off and on with the switch on → the caption and the button, then
    the notification after 3 minutes.
- Tests:
  - every switch × status combination
  - the warning rule: the 24 h repeat, demo mode, the judge window, the stalled and unregistered states, and no
    warning when the switch is off or the helper is on
  - the click route
  - the caption copy pinned
