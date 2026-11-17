# Agent notifications: design

Windows, 5.0 spec 4 (the frame is `2026-11-17-windows-5-0-umbrella.md`). Brainstormed with the owner on
2026-11-17 and approved section by section. Decisions are on the dashboard under Sanduhr für Claude (Windows).

## The goal

Someone running a long agent turn does something else and is told when it ends, whichever agent it was.
Today only Claude Code reaches the glow and the Desk's watcher cards, and nothing reaches a Windows toast.

**Success:** with Codex on in Sources, a Codex turn that runs two minutes while another window is in front
ends in a toast reading "Codex finished" and "<project>, 2m 04s", a card on the Desk and, if the glow is on,
a glow in Codex's color. The same turn with the Codex app in front shows only the card. No network call, no
new sign-in, and no prompt or reply text is read.

## Out of scope

- **A waiting signal for Codex or the Antigravity CLI.** Neither writes one to disk on the machine checked.
  Task filed for after 5.0 (Codex's `notify` hook and approval events).
- **The Antigravity IDE.** As in spec 3, only the CLI is read.
- **Bringing the agent's own window forward** from a toast. Sanduhr cannot find it reliably.
- **A notification history.** Nothing is stored.
- **The Mac.** Its glow, watchers and notch have their own path; a Mac task follows.
- **The Settings reorganization.** Spec 6 places the new page in the reorganized sidebar.

## 1. What was checked (2026-11-17, event names, key names and timestamps only)

- **Codex.** Every `event_msg` type across this machine's 16 plain rollouts: `item_completed`, `token_count`,
  `task_started` (1177), `task_complete` (1172), `thread_settings_applied`, `turn_aborted` (4). No
  approval-request type. `task_complete` carries `duration_ms`, `started_at`, `completed_at`, `turn_id` and
  `last_agent_message`; the last is never read. A sub-agent's rollout has `forked_from_id` and
  `parent_thread_id` in its `session_meta`; a top-level one does not. Only the Codex desktop app was
  observed; a Codex CLI was not installed, so the CLI writing the same markers is unverified.
- **Antigravity CLI.** A transcript line has `type` (`USER_INPUT`, `PLANNER_RESPONSE`, `GENERIC`,
  `SYSTEM_MESSAGE`, `CHECKPOINT`), `status` (`RUNNING`, `DONE`, `ERROR`) and `created_at`. There is no
  turn-end marker. After a `PLANNER_RESPONSE` `DONE` the turn usually continues (gaps p90 30 s, max 174 s).
  A mid-turn planner step has a `tool_calls` key in 322 of 341 cases; a turn's last one lacks it in 8 of 9.
  The 19 steps that lacked it and were not the end were each followed by a `SYSTEM_MESSAGE` 5 to 83 s later.
  Sample: 10 conversations, 14 turns.
- **Claude Code.** The installed hooks (`Notification` with the waiting matcher, `Stop`) run an async
  PowerShell command that writes an empty `glow-waiting.signal` or `glow-done.signal` under
  `%USERPROFILE%\.sanduhr`. The signal names no folder or session. Sanduhr recognizes its own hook by exact
  command text, with the pre-3.5 command read as Outdated.
- **The app.** `CodexUsageService` already watches each Codex home's `sessions` folder (500 ms debounce,
  30 s fallback poll). Toasts are built in three places (`AlertService`, `RestartToUpdateService`,
  `DeskController.ShowOpenFailed`) with two separate activation handlers. `WatcherSource.Automatic` exists
  and nothing uses it. The in-front check is private to `ClaudeCodeGlowService` and lists terminals and
  editors only. Both glow switches default off.

## 2. The pieces

| Piece | Project | Does |
|---|---|---|
| `AgentTurnEvent` | Core | Agent, kind (`Started`, `Finished`, `Stopped`, `Waiting`), project folder, session id, time, duration when known |
| `CodexTurnReader`, `AntigravityTurnReader`, `ClaudeCodeSignalFile` | Core | Pure: lines or a decoded file in, events out. No IO |
| `NotificationRules` | Core | Pure: an event, the settings, what is in front and the limiter's state in; the surfaces to use out |
| `AgentActivityService` | App | Owns the file watching and per-file offsets, feeds the readers, raises events |
| `NotificationDispatcher` | App | Applies the rules once and calls the four surfaces |
| Surfaces: toast, glow, flash, Desk card | App | Thin adapters over what exists (`GlowOverlayWindow`, `GlowNative.FlashTray`, `WatcherStore`) plus one toast builder |

The three existing toasts move onto the toast surface and one activation handler that routes by an `action`
argument. The MCP server links none of it; `TrustBoundaryTests` is unchanged.

## 3. How each agent is read

**Codex (exact).** The activity service shares `CodexUsageService`'s watcher and tails each changed plain
rollout from an in-memory offset, keeping a torn last line for the next read. Compressed rollouts are
finished history and are not tailed.

- `task_started` gives `Started`. `task_complete` gives `Finished`, with the duration from `duration_ms`.
  `turn_aborted` gives `Stopped`.
- The project is the `cwd` of the `turn_context` in force, as the burn parser reads it.
- A rollout whose `session_meta` has `forked_from_id` or `parent_thread_id` is a sub-agent: its events
  update nothing and notify nobody. One run, one notification.

**Antigravity CLI (inferred, late by design).** A watcher on `<cliHome>\brain` tails each
`transcript.jsonl`.

- `USER_INPUT` gives `Started`.
- A `PLANNER_RESPONSE` with status `DONE` and no `tool_calls` key arms a 90-second timer. Any new line
  cancels it. When it expires, `Finished` is raised with the planner step's time, so the duration is right
  and the notification is 90 seconds late.
- No `Stopped`: a transcript that ends on an error or on a step that asked for a tool cannot be told from a
  long tool run. Its Desk card goes to the board's existing lost-touch state after 10 minutes.
- The project comes from the summaries lookup spec 3 added, by conversation id.
- Only the presence of the `tool_calls` key is read, never its value.

**Claude Code (exact, with the updated hook).** The hook command reads the JSON Claude Code passes on
standard input and writes one small file per event, `cc-signal-<unix ms>-<pid>.json`, under
`%USERPROFILE%\.sanduhr`, written under a temporary name and renamed so a half-written file is never read:
`{"v":1,"event":"prompt"|"waiting"|"done","cwd":"…","session":"…","at":<unix ms>}`.

- Three hook events: `UserPromptSubmit` (`Started`), `Notification` with today's matcher (`Waiting`) and
  `Stop` (`Finished`). All async, 5-second timeout, as today.
- Duration is `Finished` minus the session's last `Started`. With no `Started` seen, the duration is
  unknown.
- Sanduhr reads and deletes each file at once, drops one older than 10 minutes or over 4 KB, and names the
  pattern in Reset.
- **An old hook keeps working exactly as in 4.9.** An empty `glow-*.signal` drives the glow and the flash
  under the glow and flash switches, with no threshold, and nothing else: no toast, no card, no project. The integration row reads
  Outdated and offers Update. Install writes all three events; Remove takes out all three and both
  generations of command.

**All three.** At launch nothing replays. For a file changed in the last 10 minutes the service reads up to
its last 4 MB to learn whether a turn is open, without notifying; older files start at their end.

## 4. The rules

| Event | Toast, glow, flash | Desk card |
|---|---|---|
| `Started` | Never | A card with a running timer |
| `Finished` | When the duration is at least the threshold and the agent's window is not in front | Always |
| `Stopped` | As `Finished`, worded "stopped" | Always |
| `Waiting` | At once, unless the agent's window is in front | Always |

- **Unknown duration never interrupts,** unless the threshold is Every turn. It still updates the card.
- **In front** is one shared check: the foreground window's process against a list per agent. Terminals and
  editors (today's `TerminalProcessNames`) for Claude Code and the Antigravity CLI; those plus the Codex
  app's process for Codex. It cannot see which terminal tab is showing, so any listed process in front
  counts as watching.
- **The limiter** is the glow's, keyed by agent and session: one kind at most every 20 s, no `Finished`
  within 5 s of a `Waiting`.
- **An agent notifies only while its source is on** in Sources, and Claude Code only with its hook
  installed. No further consent step: these are the files spec 3 reads.
- **Demo mode** raises nothing. Do Not Disturb is left to Windows, which holds toasts itself.

## 5. The surfaces

- **Toast.** Title "<Agent> finished", "<Agent> stopped" or "<Agent> is waiting on you". Body
  "<project>, 4m 12s" (the project alone for waiting). Group `sanduhr-agents`, tag from agent and session, so
  a session's newer toast replaces its older one. A click shows the widget, as a limit alert does.
- **Glow.** `Finished` and `Stopped` glow in the agent's Sources color. `Waiting` keeps today's color.
  Where it shows follows the existing target settings.
- **Taskbar flash.** As today, on the widget's button, with its own switch.
- **Desk card.** A `WatcherSource.Automatic` card per session: the agent's name, the project, "working 2m",
  then "finished, 4m 12s" for about a minute before it fades. Shown only where the watcher cards piece is
  placed. It does not need "Let Claude show watchers", which gates the MCP's cards.
- **Streamer mode** masks the project on every surface through `StreamerMode.Mask`, before a toast is sent.

## 6. Settings and defaults

A new **Notifications** page (`SettingsPage.Notifications`, after Alerts until spec 6 places it):

- A row per agent: what it supports ("Working and finished", "Working, finished about a minute and a half
  late", "Working, finished and waiting"), and a switch. Claude Code's row shows the hook's state and Update.
- Switches for the toast, the glow on finished, the glow on waiting, the taskbar flash and the Desk card.
- The threshold: Every turn, 30 seconds, 1 minute, 2 minutes, 5 minutes.
- "Not while the agent's window is in front", where the glow shows, and Try it buttons.

The Claude Code page keeps the hook's install row and links here.

| Setting | Default | On upgrade |
|---|---|---|
| Toast | On | On |
| Desk card | On | On |
| Glow on finished, glow on waiting | Off | The user's `claude_code_glow` values |
| Taskbar flash | Off | On if either glow switch was on |
| Threshold | 1 minute | 1 minute |
| Not while in front | On | The user's `skip_terminal` value |

Stored as `agent_notifications` in `settings.json`. `claude_code_glow` is read once to migrate and keeps its
target fields. **A change for people who had the glow on:** with the updated hook, a finished glow waits for
a one-minute turn. Every turn restores the old behaviour, and the changelog says so.

A What's New card for 5.0.0 points to the page. It may name Codex (the window carries that trademark line)
and must not name Antigravity (`No_card_names_Antigravity`).

## 7. Privacy

- No network destination, no capability, no library. `NetworkDisclosure` keeps its rows.
- The Codex and Antigravity files are the ones spec 3 reads, now also for turn markers and step status.
  Never message text: not `last_agent_message`, not a step's `content`, `thinking` or `tool_calls` value.
- New on disk: the hook's `cc-signal-*.json` files, each holding a folder path and a session id, deleted as
  read. Nothing else is written; turn state is in memory.
- Toast text (agent, project name, duration) stays in Windows' notification history until the user clears
  it.
- `docs/PRIVACY.md` gains a row, the hosted page a paragraph and the reviewer letter a line, before the
  Store commit.

## 8. Failures

- A line a reader cannot parse is skipped; a reader never throws out of the service.
- A watcher that fails falls back to the 30-second poll.
- A Codex rollout compressed or removed mid-turn drops that turn; no `Finished` is invented.
- A malformed, oversized or stale signal file is deleted unread.
- A surface that throws is logged by type and does not stop the others.

## 9. Testing

- **Readers:** fixture lines per agent; the Antigravity look-alike endings (a final-looking step followed by
  a `SYSTEM_MESSAGE` inside 90 s raises nothing); sub-agent suppression; a torn last line; and a test that
  no reader touches a message-text field.
- **Rules:** one case per row of section 4, unknown duration, each agent's in-front list, the limiter.
- **Pinned first:** limit alerts, restart-to-update and the Launcher toast get tests on today's behaviour
  before they move onto the toast surface.
- **Hook:** install, update from each older command, remove, and an old hook driving the glow as in 4.9.
- **Settings:** migration from `claude_code_glow`, the page read through UI Automation, streamer masking.
- **Live smoke before release:** one turn over the threshold per agent with its window in front and not;
  streamer mode; Do Not Disturb; an app restart mid-turn.

## 10. Verify first, in the build

1. On Windows, an async PowerShell hook receives `session_id` and `cwd` on standard input for
   `UserPromptSubmit`, `Notification` and `Stop`. The code's own note says the hook fields came from the
   docs and were not exercised live.
2. The Codex desktop app's process name, for the in-front list.
3. `forked_from_id` and `parent_thread_id` are absent on every top-level Codex session, across all rollouts.
4. The 90-second quiet time against any Antigravity transcripts added since the sample.

If 1 fails, Claude Code keeps today's glow and gets no toast or card until a signal with identity exists. If
4 shows a false ending, Antigravity drops to working-only.
