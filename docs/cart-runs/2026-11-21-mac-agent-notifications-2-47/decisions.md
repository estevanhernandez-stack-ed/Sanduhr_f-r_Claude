# Decisions: mac-agent-notifications-2-47

## D1
- **Decision:** The owner's design choices:
  - no fourth surface
  - the JXA signal script (design approach 1)
  - FSEvents with a 30 s poll fallback
  - quiet hours apply, but Alerts' master switch and delivery route don't
- **Why:** Brainstormed and approved on 2026-11-21, logged on the dashboard.
- **Alternative not taken:**
  - a menu bar pulse
  - a `sanduhr://` link
  - python3 for the hook
  - ignoring quiet hours
- **Item:** all
- **How to reverse:** Design doc, sections 1, 3 and 4.

## D2
- **Decision:** Design section 9, check 1 (Claude Code passes `cwd` and `session_id` to hooks on macOS), moves to
  the live smoke after the run.
- **Why:** item 66's Stop script already reads `session_id` live on this Mac. Windows checked `cwd` and
  `session_id` on 2026-11-18. Running `claude` inside an unattended run would use the owner's account.
- **Alternative not taken:** a scratch-folder Claude Code run inside the run.
- **Item:** 4, 9
- **How to reverse:** Run the check first. If `cwd` is missing, drop the project from the notification body.

## D3
- **Decision:** Codex counts as in front when the front app is named "Codex" or its bundle id starts with
  `com.openai.`.
- **Why:** Codex isn't installed here, so its exact bundle id can't be read. A wrong match only means a
  notification while the Codex app is in front.
- **Alternative not taken:** guessing one exact bundle id.
- **Item:** 1
- **How to reverse:** Pin the real id after the real-data check.

## D4
- **Decision:** Items 3, 4 and 5 go to judgment-tier workers (the session model), the rest to bulk at high effort.
- **Why:**
  - 3 owns the file following and its launch replay.
  - 4 rewrites the hook in every user's Claude Code settings.
  - 5 touches the glow and notification paths limit alerts use.
- **Alternative not taken:** bulk for all.
- **Item:** 3, 4, 5
- **How to reverse:** None needed.

## D5
- **Decision:** `NotificationRules.decide` checks, in this order:
  1. idle switch
  2. started
  3. threshold
  4. surfaces on
  5. quiet hours
  6. front app, read lazily
  7. limiter, which quiet hours and the front-app rule never use up
- **More rules:**
  - Glow color applies only to finished and stopped.
  - Idle shares the waiting glow switch and limiter slot, and never makes a card.
  - Whether an agent's source is on is checked in the notifier (Task 5 and 6), not in `decide`.
  - The glow limiter is wrapped per agent and session, unchanged (pinned).
  - A signal with no session uses the `-` marker.
  - A timestamp more than 60 s ahead is dropped, as on Windows.
- **Why:** Windows parity. The settings value carries no per-agent switches.
- **Alternative not taken:** none.
- **Item:** 1 (it binds 5 and 6)
- **How to reverse:** `NotificationRules.swift`.

## D6
- **Decision:** How the watcher follows files:
  - Each FSEvents stream watches the whole agent home, filtered to the rollout and transcript paths.
  - One ticker runs: 5 s while an Antigravity end is armed, 30 s while polling, and off otherwise.
  - An old Codex file is read when it grows, not at launch.
  - Keep-alive is a `.started` at the current time, skipped when a real event fired.
  - `claudeCodePosted()` returns `.hadFiles` whenever any signal file existed.
  - Paths go through `realpath`, since Foundation strips `/private`.
  - A missing home isn't followed.
- **Handoffs:**
  - Task 4 writes temp files as `.cc-signal-….json.tmp`.
  - Task 5's card treats a `.started` on a running session as a refresh, and clears an agent's cards when it is
    switched off (there is no `AgentOff` event).
  - Task 6 calls `update` again so a home that appears later is noticed.
- **Why:** These keep idle CPU low and keep the new hook out of the legacy path. A test caught the `/private`
  mismatch.
- **Alternative not taken:** Windows' re-listing on every event, and a separate `Active` event.
- **Item:** 3 (it binds 4, 5 and 6)
- **How to reverse:** `AgentActivityWatcher.swift`.

## D7
- **Decision:** The hook has three events, each running one JXA `signalScript` that replaces `stopTasksScript`;
  on done it also writes item 66's report.
  - **Cost:** the measured median per prompt is 47.5 ms, and 50.5 ms on done with background on, well under the
    300 ms bar.
  - **Recognized as Outdated:** every older generation (item 51's open-link, item 66's, and 2.46's), all
    updated in place and removed as they are.
  - **Malformed stdin** still writes a signal, with no identity (the `-` session).
  - **The `sanduhr://claude-code` link** keeps only 2.46's two events.
  - **The consent copy** says the hook reads the prompt and keeps only the folder and session.
- **Why:** design section 1 and section 9, check 2.
- **Alternative not taken:** writing nothing on malformed stdin.
- **Item:** 4
- **How to reverse:** `IntegrationInstaller.signalScript` and `hookEvents`. The README's old Stop text goes to
  Task 9.

## D8
- **Decision:** For notifications and turn cards:
  - The notifier drops a repeat finish or stop (same agent, session, kind and time) that the limiter would let
    through after 20 s.
  - Turn cards stay out of the Claude Code band.
  - Turn cards are hidden in demo mode.
  - A running card starts at the event's own time, clamped to the last hour, and a bad duration leaves the start
    unchanged.
  - Durations print zero-padded ("4m 05s"), as on Windows.
  - Limit alerts' banners and clicks are unchanged (pinned). A click on an `agent-` notification shows the widget
    through `AppDelegate.showPanel()`.
- **Why:**
  - Review Focus 4 (sleep and wake): one notification, and never a negative time.
  - The band already sits inside Claude Code.
- **Alternative not taken:** the limiter alone, and a turn kind in the band.
- **Item:** 5
- **How to reverse:** `AgentNotifier.swift`, `WatcherBoard.applyTurn`.

## D9
- **Decision:** Claude Code notifications follow the updated hook alone. Being installed in any folder counts;
  being outdated doesn't. They don't follow the Claude usage source.
  - Other wiring choices:
    - The watcher follows a home that appears later on the next `update`.
    - A new `.sanduhrHooksDidChange` notification follows a hook install or remove.
    - Demo raises nothing on the new path; a bare post still glows as in 2.46.
    - `interimSettings` stands in for Task 7's loader.
- **Why:** The orchestrator caught a regression in the first version. A "Just the Desk" user (Claude usage off,
  BACKLOG #4) with the hook would have lost even the glow after updating the hook, because the new path dropped
  the files as Claude being off. The Claude Code hook has nothing to do with the claude.ai account.
- **Alternative not taken:** keying Claude Code to the usage source, which is what the first commit did.
- **Item:** 6
- **How to reverse:** `AgentNotificationsWiring.wants`.

## D10
- **Decision:** On the Notifications page:
  - Try it shows each surface through `AgentNotifier.tryIt`, whatever the switches, as on Windows.
  - macOS permission is asked when the notification switch turns on or when its Try it is pressed. An "Allow
    Notifications" line appears while macOS hasn't been asked, so someone who keeps the default-on switch can
    still grant it.
  - The idle entry's search title is "Idle reminder".
  - Turning the card switch off clears every agent's turn cards.
  - The piece keeps its Mac name, "Claude Code glow hook".
- **Why:** Without the Allow line, a default-on notification switch would never trigger the macOS permission
  prompt, and every notification would be dropped.
- **Alternative not taken:** asking only on toggle.
- **Item:** 7 (Task 9 updates the old Glow labels in the setup guide, the settings spec and smoke section 72-ish)
- **How to reverse:** `NotificationsSettingsView`, `NotificationPermissionModel`.
