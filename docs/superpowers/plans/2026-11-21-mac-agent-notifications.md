# Mac Agent Notifications Implementation Plan

> **For agentic workers:** This plan runs as a Cart `/take` unattended run, one worker per task, each on its own
> branch and worktree with its own Verify. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Mac 2.47.0 tells you when a Claude Code, Codex or Antigravity CLI turn ends, or when Claude Code waits
on you, through a macOS notification, the notch glow and a Desk turn card.

**Architecture:** Pure readers turn each agent's signals into `AgentTurnEvent`s. A pure `NotificationRules`
decides the surfaces from the event, the settings, the front app, quiet hours and a limiter. An
`AgentActivityWatcher` owns the file following: FSEvents for Codex and Antigravity, Darwin posts plus signal
files for Claude Code. An `AgentNotifier` applies the rules and calls the three surfaces.

**Tech Stack:** Swift 5.9 / SwiftUI, Swift Testing, CoreServices FSEvents, `notify`, `UserNotifications`, JXA
(`osascript -l JavaScript`) in the hook.

**Spec:** `docs/superpowers/specs/2026-11-21-mac-agent-notifications-design.md` (the "design"), which ports
`docs/superpowers/specs/2026-11-17-agent-notifications-design.md` (the "Windows spec"; its section 11 is what
shipped). Read both.

## Global Constraints

- A Mac with the hook not updated, and Codex and Antigravity off, behaves exactly as 2.46.0. Every task that
  touches a shared surface pins that with a test.
- Never read message text: `last_agent_message`, a step's `content`, `thinking` or `tool_calls` value, or the
  prompt. Only `cwd` and `session_id` leave the hook.
- No network, no new library, no new permission.
- Threshold choices: Every turn, 30 s, 1 min (default), 2 min, 5 min. Limiter: one kind per agent and session at
  most every 20 s, and no `Finished` within 5 s of a `Waiting`.
- Signal files: `cc-signal-<unix ms>-<random>.json`, `{"v":1,"event":"prompt"|"waiting"|"done","cwd":…,
  "session":…,"at":<unix ms>}`. `cwd` is clipped to 1024 characters, `session` to 128. A file is dropped unread
  if it is older than 10 min or larger than 4 KB. The hook stops writing at 100 waiting files.
- Antigravity's finish is inferred 90 s after a `PLANNER_RESPONSE` `DONE` with no `tool_calls` key.
- Darwin names: `com.626labs.sanduhr.claude-code.prompt|waiting|done`.
- Keys:
  - New, in standard defaults: `agentNotifications.banner` (true), `agentNotifications.card` (true),
    `agentNotifications.threshold` (60, seconds; 0 means every turn), `agentNotifications.idle` (false; true on
    upgrade if `notchGlowClaudeWaiting` was on).
  - Kept, in `UserDefaults.desk`: `notchGlowClaudeDone`, `notchGlowClaudeWaiting`, `notchGlowClaudeSkipTerminal`.
- Never `sed -i` a tracked file. Never touch `windows-dotnet/`. Nothing committed names a home path or a person.
  Keep SwiftUI bodies small (macos-15 CI type-checker).

## Review Focus

1. **Two Claude Code sessions in the same folder at once:** each gets its own turn, card and duration, keyed by
   session id, never by folder. Test in Task 1.
2. **A signal file whose `cwd` has non-ASCII, spaces or quotes:** the project name shows correctly, and the hook
   still writes valid JSON. Tests in Tasks 1 and 4.
3. **Sanduhr launched mid-turn:** no notification for turns already done, and a turn that finishes after launch
   still notifies with a correct duration when its start is in the replay window. Test in Task 3.
4. **The Mac sleeps mid-turn and wakes after the threshold:** a `Finished` arriving after wake notifies once, not
   twice, and the running card does not show a negative or huge time. Test in Task 5.
5. **The notification permission is denied:** no crash or retry loop, the Notifications page says so, and the
   glow and card still work. Test in Task 7.

---

### Task 1: Turn events, the Claude Code signal file, and the rules (pure)

**Files:**
- Create: `mac/Sources/Sanduhr/Services/AgentTurns.swift` (`AgentTurnEvent`, `ClaudeCodeSignalFile`)
- Create: `mac/Sources/Sanduhr/Services/NotificationRules.swift`
- Test: `mac/Tests/SanduhrTests/AgentTurnsTests.swift`, `mac/Tests/SanduhrTests/NotificationRulesTests.swift`

**Interfaces:**
- Produces:
  - `struct AgentTurnEvent: Equatable, Sendable { agent: AgentSource; kind: Kind; project: String?; session:
    String; at: Date; duration: TimeInterval? }`, where `enum Kind { started, finished, stopped, waiting, idle }`.
  - `enum ClaudeCodeSignalFile { static func parse(_ data: Data, now: Date) -> Signal?; static let maxBytes =
    4096; static let maxAge: TimeInterval = 600 }`, where `Signal { event: String, cwd: String?, session: String,
    at: Date }`.
  - `struct ClaudeCodeTurns { mutating func apply(_ s: Signal, replay: Bool) -> AgentTurnEvent? }`: tracks the
    open turn per session; `waiting` with an open turn is `.waiting`, without one `.idle`; replay records and
    returns nil.
  - `struct NotificationSettings { banner, card, glowFinished, glowWaiting, skipInFront, idle: Bool; threshold:
    TimeInterval; quiet: (enabled: Bool, start: Int, end: Int) }`.
  - `struct NotificationDecision: Equatable { banner, glow, card: Bool; glowColorHex: String? }`.
  - `struct NotificationLimiter { mutating func allow(_ e: AgentTurnEvent, now: Date) -> Bool }`.
  - `enum NotificationRules { static func decide(_ e: AgentTurnEvent, settings: NotificationSettings, frontBundleID:
    () -> String?, frontName: () -> String?, sourceColorHex: String, limiter: inout NotificationLimiter, now: Date,
    calendar: Calendar) -> NotificationDecision }`. The front app closures are called only when an interrupt is
    otherwise due and `skipInFront` is on.
  - `static func inFront(_ agent: AgentSource, bundleID: String?, name: String?) -> Bool`:
    - Claude Code and Antigravity: `ClaudeCodeGlowRules.terminalBundleIDs`.
    - Codex: that list, plus a front app named "Codex" or a bundle id with prefix `com.openai.` (D-entry: the real
      bundle id is unverified on this Mac).
  - Quiet hours reuse `AlertRules.isQuiet(_:start:end:calendar:)` (make it internal if it's private).

- [ ] **Step 1:** Write the failing tests:
  - One per row of design section 3: started, finished at or above and below the threshold, stopped, waiting,
    idle with the switch on and off.
  - Unknown duration under 1 min and under Every turn.
  - The in-front list per agent; the front app not read when no interrupt is due.
  - The limiter at 19 s and 21 s, and a finished at 4 s after waiting.
  - Quiet hours hold banner and glow while the card stays.
  - Signal parse: good, a missing session, oversized, stale, a non-ASCII `cwd`.
  - Two sessions in one folder (Review Focus 1), and replay returning nil.
- [ ] **Step 2:** Run `cd mac && ./test.sh --filter AgentTurns` and `--filter NotificationRules`: they fail (types
  missing).
- [ ] **Step 3:** Implement the types above.
- [ ] **Step 4:** Run `cd mac && ./test.sh`: all pass.
- [ ] **Step 5:** Commit `feat(mac): agent turn events and the notification rules`.

### Task 2: Codex and Antigravity turn readers (pure)

**Files:**
- Create: `mac/Sources/Sanduhr/Services/AgentTurnReaders.swift`
- Test: `mac/Tests/SanduhrTests/AgentTurnReadersTests.swift`

**Interfaces:**
- Consumes: `AgentTurnEvent` (Task 1).
- Produces:
  - `struct CodexTurnReader { mutating func read(lines: [Data], file: String) -> [AgentTurnEvent] }`:
    - `task_started` gives started; `task_complete` gives finished, with `duration_ms`; `turn_aborted` gives stopped.
    - The project is the `turn_context` `cwd` in force, through `CCProjectName.displayName`.
    - A sub-agent mark from `forked_from_id` or `parent_thread_id` sticks and silences the file.
    - The session is the file name without `.jsonl`.
  - `struct AntigravityTurnReader { mutating func read(lines: [Data], conversation: String, project: String?) ->
    [AgentTurnEvent]; mutating func tick(now: Date) -> [AgentTurnEvent] }`:
    - `USER_INPUT` gives started.
    - A `DONE` planner step with no `tool_calls` key arms a 90 s deadline; any line clears it.
    - `tick` raises finished, timed by the planner step's `created_at`.
- [ ] **Step 1:** Write the failing tests, ported from the Windows readers' tests
  (`windows-dotnet/tests/Sanduhr.Tests/*Turn*`, read only):
  - started, finished and stopped
  - sub-agent silence, including a forked file repeating its parent's meta without the keys
  - the look-alike ending: a final-looking step followed by a `SYSTEM_MESSAGE` within 90 s raises nothing
  - a torn line
  - a message-text guard: a fixture whose message fields hold a sentinel string, with the event never carrying it
- [ ] **Step 2:** Run `./test.sh --filter AgentTurnReaders`: it fails.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run `cd mac && ./test.sh`: all pass.
- [ ] **Step 5:** Commit `feat(mac): Codex and Antigravity turn readers`.

### Task 3: The activity watcher

**Files:**
- Create: `mac/Sources/Sanduhr/Services/AgentActivityWatcher.swift`
- Test: `mac/Tests/SanduhrTests/AgentActivityWatcherTests.swift`

**Interfaces:**
- Consumes: Tasks 1 and 2 readers; `CodexHomes`; `AntigravitySummaries` (2.46); `AgentSourceSettings.isOn`.
- Produces:
  - `final class AgentActivityWatcher { init(homes: AgentHomes, files: TurnFileSystem, clock: () -> Date, ticker:
    AudioBarsTicker, events: @escaping (AgentTurnEvent) -> Void); func update(codexOn: Bool, antigravityOn:
    Bool, claudeOn: Bool); func claudeCodePosted(); func stop() }`, where
    `AgentHomes { codex: [URL], antigravity: URL, signals: URL }`.
  - FSEvents streams are created per followed folder behind a small `FolderStream` protocol, with a fake in tests.
  - On a stream error the folder uses the 30 s ticker.
  - Offsets live in memory, with a torn last line kept.
  - At start, a file changed in the last 10 min is read from up to its last 4 MB as replay (no events out);
    older files start at their end.
  - `claudeCodePosted()` reads, deletes and parses `cc-signal-*.json` in `signals`. A stale, oversized or
    malformed file is deleted unread, and a temp file older than 10 min is removed. It returns
    `ClaudeCodePostResult.hadFiles` or `.bare`, so Task 4 can route a bare post to the legacy glow.
  - Activity on a still-writing Codex or Antigravity file is reported at most once a minute, through
    `AgentTurnEvent` kind `.started` with the same session. The card treats it as a keep-alive.
- [ ] **Step 1:** Write the failing tests over temp folders with a fake stream and ticker:
  - growth gives events
  - launch replay is silent, while a finish after launch notifies with a duration (Review Focus 3)
  - a stream error falls back to the poll
  - signal file handling: good, stale, oversized, malformed, temp leftovers, bare post
  - an agent switched off stops following
  - compressed rollouts are ignored
- [ ] **Step 2:** Run `./test.sh --filter AgentActivityWatcher`: it fails.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run `cd mac && ./test.sh`: all pass.
- [ ] **Step 5:** Commit `feat(mac): follow every agent's turns`.

### Task 4: The Claude Code hook

**Files:**
- Modify: `mac/Sources/Sanduhr/Services/IntegrationInstaller.swift` (`hookEvents`, `hookCommand`,
  `stopTasksScript` replaced by `signalScript`, recognition of older generations)
- Modify: `mac/Sources/Sanduhr/Desk/ClaudeCodeGlow.swift` (`ClaudeCodeEvent` gains `prompt`; `ClaudeCodeSignal`
  registers three names)
- Test: `mac/Tests/SanduhrTests/IntegrationInstallerTests.swift` (extend), `mac/Tests/SanduhrTests/SignalScriptTests.swift`

**Interfaces:**
- Produces:
  - `hookEvents = [("UserPromptSubmit", .prompt), ("Notification", .waiting), ("Stop", .done)]`.
  - Command for each event: `d="$HOME/Library/Application Support/Sanduhr"; /usr/bin/pgrep -xq Sanduhr && /usr/bin/osascript -l JavaScript -e '<signalScript>' "$d" <event> >/dev/null 2>&1; /usr/bin/notifyutil -p com.626labs.sanduhr.claude-code.<event> || true`.
  - `static let signalScript: String`, JXA with no single quotes. It reads stdin once, then:
    - writes the signal file (temp name, then rename, owner-only), keeping `cwd` and `session_id` clipped;
    - stops at 100 waiting `cc-signal-*` files;
    - on `done`, also writes item 66's `watch-stop-*.json` report with its existing fields and caps, only when
      `watchers.json` contains `"background":true`.
  - `ClaudeCodeEvent.prompt` has no glow kind (`kind` returns nil).
  - The hook's Install confirmation copy says exactly what the hook reads and keeps: it reads Claude Code's
    input, keeps the folder and the session id, writes them to a file Sanduhr deletes as it reads, and nothing
    else. A test pins the copy.
  - `IntegrationInstaller` reads all three events, plus the 2.46 Stop command and earlier, as Outdated. Install
    writes three; Remove removes every generation.
- [ ] **Step 1:** Measure: `time` five runs of the script via `osascript` with a made-up stdin. Record the median
  in the commit message and the report. If it's over 300 ms, write a D-entry and keep going (design section 9,
  check 2).
- [ ] **Step 2:** Write the failing tests:
  - Install, Update from each older generation, and Remove on a temp settings file.
  - The script text pinned.
  - Running the script with `osascript` against a temp folder and made-up stdin containing a prompt field, a
    quote and a non-ASCII `cwd`: the file holds exactly `v`, `event`, `cwd`, `session`, `at`, and no prompt text
    (Review Focus 2).
  - The 100-file stop.
  - On done, the background report written only with the switch.
  - Three Darwin names registered.
- [ ] **Step 3:** Run `./test.sh --filter "IntegrationInstaller|SignalScript"`: it fails.
- [ ] **Step 4:** Implement.
- [ ] **Step 5:** Run `cd mac && ./test.sh`: all pass.
- [ ] **Step 6:** Commit `feat(mac): the Claude Code hook says which session and folder`.

### Task 5: The surfaces

**Files:**
- Create: `mac/Sources/Sanduhr/Services/AgentNotifier.swift`
- Modify:
  - `mac/Sources/Sanduhr/Services/Notifier.swift`: `postAgent(...)` and a `didReceive` branch for the
    `agent-` prefix that shows the widget.
  - `mac/Sources/Sanduhr/Desk/NotchGlow.swift`: `fire(topFallback:color:)`, with the glow views taking an optional
    color.
  - `mac/Sources/Sanduhr/Services/Watchers.swift` / `WatcherStore.swift`: `WatcherSource.turn`, turn cards.
- Test: `mac/Tests/SanduhrTests/AgentNotifierTests.swift`, plus extended watcher and glow tests.

**Interfaces:**
- Consumes: `NotificationRules.decide`, `AgentTurnEvent` (Task 1).
- Produces:
  - `final class AgentNotifier { init(settings: () -> NotificationSettings, banner: (AgentBanner) -> Void, glow:
    (String?) -> Void, card: (AgentTurnEvent) -> Void, front: FrontApp, clock: () -> Date); func handle(_ e:
    AgentTurnEvent) }`. Each surface runs in its own failure scope.
  - `AgentBanner`:
    - `id: "agent-<agent>-<session>"`, or `"agent-idle-<session>"` for the idle reminder.
    - `title`: "<Agent> finished", "<Agent> stopped" or "<Agent> is waiting on you".
    - `body`: "<project>, 4m 12s", or the project alone for waiting. `AgentBanner.duration(_:)` formats it.
    - `thread: "sanduhr-agents"`.
  - Turn cards: a card per agent and session showing the agent, the project and a running "2m", then
    "finished · 4m" for about a minute before it fades. A running card loses touch after 1 h (Antigravity's after
    10 min) and is removed 60 s later; a keep-alive event refreshes it. It is shown only where the watchers piece
    is placed, with no "Let Claude show watchers" gate.
  - The legacy path: a bare Claude Code post calls today's `NotchGlowController.claudeCode` unchanged.
- [ ] **Step 1:** Write the failing tests:
  - Each surface honors its decision; a throwing surface doesn't stop the others.
  - The identifier replaces per session; idle has its own identifier.
  - Duration format: 4m 12s, 45s, 1h 02m.
  - Clicking an `agent-` notification shows the widget.
  - Glow color is passed for finished and nil for waiting.
  - Card lifecycle: start, finish, fade, lost touch, keep-alive.
  - Sleep and wake: one finish notifies once, and the running time is never negative (Review Focus 4).
  - Limit alerts' banners unchanged (pinned first).
- [ ] **Step 2:** Run `./test.sh --filter "AgentNotifier|Watcher|NotchGlow"`: it fails.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run `cd mac && ./test.sh && ./build.sh --debug`: both pass.
- [ ] **Step 5:** Commit `feat(mac): notifications, the glow in the agent's color, and turn cards`.

### Task 6: Wiring

**Files:**
- Modify: `mac/Sources/Sanduhr/AppDelegate.swift` (own the watcher and notifier; start and stop them from
  sources, demo and the hook state; route `ClaudeCodeSignal` posts to `claudeCodePosted()` and bare posts to the
  legacy glow)
- Test: `mac/Tests/SanduhrTests/AgentWiringTests.swift`

**Interfaces:**
- Consumes: Tasks 3, 4, 5.
- Produces: `AgentNotificationsWiring.wants(setup:sources:demo:) -> (claude: Bool, codex: Bool, antigravity:
  Bool)`, which is pure. Demo gives all false.
- [ ] **Step 1:** Write the failing tests:
  - wants() across sources, demo and the hook state
  - a post with files goes to the new path and does not call the legacy glow
  - a bare post goes to the legacy glow only
  - a 2.46-shaped Mac (no new hook, agents off) creates no FSEvents stream
- [ ] **Step 2:** Run `./test.sh --filter AgentWiring`: it fails.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run `cd mac && ./test.sh && ./build.sh --debug`: both pass.
- [ ] **Step 5:** Commit `feat(mac): wire agent notifications in`.

### Task 7: Settings ▸ Notifications

**Files:**
- Create: `mac/Sources/Sanduhr/Views/NotificationsSettingsView.swift`, `mac/Sources/Sanduhr/Models/AgentNotificationSettings.swift`
- Modify:
  - `mac/Sources/Sanduhr/Views/SettingsWindow.swift`: `case notifications` after `alerts`, titled
    "Notifications", icon `bell.badge`.
  - `mac/Sources/Sanduhr/Desk/SettingsView.swift`: the Notch glow captions widen from Claude Code to agents, and
    a link to the new page.
- Test: `mac/Tests/SanduhrTests/AgentNotificationSettingsTests.swift`

**Interfaces:**
- Consumes: the keys in Global Constraints; `IntegrationInstaller` hook state; `AgentSource`; the notification
  permission state.
- Produces: `AgentNotificationSettings.load(standard:desk:) -> NotificationSettings`, and `migrateOnce(standard:desk:)`,
  which sets `idle` from `notchGlowClaudeWaiting` once, guarded by `agentNotifications.migrated`.
- The page:
  - **Agent rows:** "Working, finished and waiting" / "Working and finished" / "Working, finished about a minute
    and a half late". Claude Code's row shows the hook state per folder, with Update. Codex and Antigravity rows
    say "Switch it on in Usage ▸ Sources" while off.
  - **Surface switches:** notification, glow on finished, glow on waiting (the desk keys), Desk card.
  - **The threshold.**
  - **"Not while the agent's app is in front"** (desk key).
  - **The idle switch:** "Remind me when Claude Code has waited a minute for my next prompt".
  - **Try it buttons.**
  - **A line when notification permission is denied,** with a System Settings link (Review Focus 5).
- [ ] **Step 1:** Write the failing tests:
  - defaults
  - the migration, run once
  - load reads the desk keys
  - the copy pinned
  - the permission-denied line
  - a 2.46 Mac's glow switches unchanged after migration
- [ ] **Step 2:** Run `./test.sh --filter AgentNotificationSettings`: it fails.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run `cd mac && ./test.sh && ./build.sh --debug`: both pass.
- [ ] **Step 5:** Commit `feat(mac): Settings, Notifications`.

### Task 8: Smoke hooks and idle CPU

**Files:**
- Modify: `mac/Sources/Sanduhr/Debug/DebugCommand.swift`, `DebugHooks.swift`, `DebugSnapshot.swift`; `mac/smoke/README.md`, `mac/smoke/selftest.rb`, `mac/smoke/fixtures/snapshot/state.yaml`
- Test: `mac/Tests/SanduhrTests/DebugTests.swift`, `DebugSourcesTests.swift` (extend)

**Interfaces:**
- Produces:
  - A `notify` action: `agent=claude|codex|antigravity kind=started|finished|stopped|waiting|idle session=<s>
    project=<p> duration=<seconds>`. It feeds `AgentNotifier.handle` directly.
  - state.yaml blocks:
    - `agent_notifications`: settings, plus the last decision's banner, glow and card flags, with its title and
      body.
    - `turn_cards`: agent, project, state, seconds.
    - `agent_watch`: the folders followed and whether each is on FSEvents or polling.
- [ ] **Step 1:** Write the failing parse and YAML-shape tests.
- [ ] **Step 2:** Run them: they fail.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run `cd mac && ./test.sh && ruby smoke/selftest.rb`: both pass.
- [ ] **Step 5:** Measure idle CPU on a debug build with Codex and Antigravity on over empty fixture homes,
  following `memory/idle-cpu-measurement.md`'s method: cputime deltas over 60 s, under 1 percent. Record the
  figure in the report. Launch with `SANDUHR_DEBUG_HOOKS=1` and fixture homes. Never sign in. Back up and restore
  settings exactly as CLAUDE.md's dev-build rule says, or skip and say so.
- [ ] **Step 6:** Commit `test(mac): smoke hooks for agent notifications`.

### Task 9: The 2.47.0 rows

**Files:** `mac/Info.plist` (2.47.0, build 69), `mac/Sources/Sanduhr/Models/WhatsNewCards.swift` and
`WhatsNewTests`, `CHANGELOG.md`, `mac/README.md`, `docs/PRIVACY.md`, `docs/mac-smoke-test.md` (section 81).

- [ ] **Step 1:** What's New card `agent-notifications` for 2.47.0, destination the new Notifications section. It
  may name Codex and must not name Antigravity (Windows rule).
- [ ] **Step 2:** CHANGELOG `## Unreleased (mac)` above `## v2.46.0-mac`. It must include the change for people
  with the glow on: a finished glow now waits for a one-minute turn, and Every turn restores it.
- [ ] **Step 3:** README section. PRIVACY row: `cc-signal-*.json` (folder and session, deleted as read), turn
  markers read from Codex and Antigravity, notification text kept in Notification Center.
- [ ] **Step 4:** Smoke section 81:
  - fixture-home steps through the `notify` action and file growth
  - a real Claude Code turn on this Mac in a scratch folder, which checks design section 9 check 1 (stdin carries
    `cwd` and `session_id`)
  - restore steps
- [ ] **Step 5:** Run `cd mac && ./test.sh && ruby smoke/selftest.rb && python3 -m pytest -q integrations/tests`:
  all pass.
- [ ] **Step 6:** Commit `chore(mac): 2.47.0, agent notifications`.
