# Mac agent notifications: design

Mac 2.47.0. The Mac port of Windows 5.0 spec 4, `2026-11-17-agent-notifications-design.md` ("the Windows spec").
Behavior, wording and the amendments in its section 11 carry over unless this doc says otherwise. Brainstormed
with the owner on 2026-11-21.

## Why

Someone runs a long agent turn, switches to something else, and wants to be told when it ends or when it needs
them, whichever agent it was. 2.46.0 gave the Mac readers for Codex and the Antigravity CLI, so all three agents
can be covered. Today the Mac only glows the notch when Claude Code says "waiting" or "done", with no project,
no duration and nothing for the other two agents.

**Success:** with Codex on in Sources, a Codex turn that runs two minutes while another app is in front ends in a
macOS notification, "Codex finished", "float-demo, 2m 04s", and a card on the Desk. If the glow is on, the notch
glows in Codex's color. The same turn with the Codex app in front shows only the card. A Claude Code permission
prompt with the terminal in the background notifies at once: "Claude Code is waiting on you", with the project.
There is no network call, no new sign-in, and no prompt or reply text is read.

## Goals

- Finished, stopped and waiting signals for Claude Code. Working and finished for Codex. Working and inferred
  finished for the Antigravity CLI (about 90 s late, as on Windows).
- Three surfaces, each with its own switch: a macOS notification, the notch glow, and a Desk turn card.
- One threshold, so short turns don't interrupt.
- An in-front rule: the agent's own app in front means you're watching, so nothing interrupts.
- Claude Code's hook now carries the folder and session of each event, through a small file per event.

## Non-goals

- **No fourth surface (owner, 2026-11-21).** Windows flashes the taskbar button; the Mac has no taskbar, and
  Sanduhr lives in the menu bar. The notification, the card and the notch glow cover it. No menu bar pulse, no
  Dock bounce.
- **No waiting signal for Codex or the Antigravity CLI.** Neither writes one (Windows spec, out of scope).
- **No Antigravity IDE.** As in 2.46, only the CLI is read.
- **Notifications don't bring the agent's window forward.** Clicking one shows the widget, as a limit alert does.
- **No notification history.** Turn state lives in memory only.
- **No Settings reorganization.** The new page goes after Alerts; regrouping is later work.

## Anti-goals

- Interrupting after every short turn. The threshold (default 1 minute) and the idle reminder's own switch, off
  by default, exist to prevent this.
- Ever reading message text: not `last_agent_message`, not a step's `content`, `thinking` or `tool_calls` value,
  and not the prompt the prompt hook receives.
- A hook that slows Claude Code. Hooks run in the background with a 5-second timeout, as today.
- Changing anything for someone who never installed the hook and never switches on Codex or Antigravity.

## Approaches considered (owner picked 1, 2026-11-21)

1. **A script writes a small file per event** (picked). Each Claude Code hook (prompt sent, waiting, done) runs
   `osascript -l JavaScript` (JavaScript for Automation, on every Mac). The script reads Claude Code's input,
   keeps only the folder and session id, and writes one small JSON file to Sanduhr's folder, under a temporary
   name that is then renamed. The hook then posts today's Darwin notification, which wakes Sanduhr to read and
   delete the file. This is Windows' design, and it reuses the pattern the Mac's Stop hook already uses for
   background tasks (`IntegrationInstaller.stopTasksScript`, item 66). Each prompt costs about 0.1 s of
   background work, which Claude Code doesn't wait on.
2. **A `sanduhr://` link carrying the folder and session.** Rejected: opening a link launches Sanduhr when it's
   quit (the reason the hooks moved to `notifyutil`), and a folder name escaped inside a shell command is fragile.
3. **A python3 hook script.** Rejected: python3 isn't on every Mac.

For Codex and the Antigravity CLI, the watcher follows their folders through FSEvents, so a turn's end shows in
about a second, not on a poll. It falls back to a 30-second poll if the stream fails. FSEvents costs nothing
while the folders are quiet, so the idle CPU target (under 1 percent of a core) holds. Re-measure it with
cputime deltas.

## 1. Claude Code: the hook

**Events.** Three hook entries in each linked Claude Code folder's settings:
- `UserPromptSubmit` gives `Started`.
- `Notification` with today's matcher (`permission_prompt|idle_prompt|elicitation_dialog`) gives `Waiting`, or
  `Idle` when no turn is open (Windows section 11).
- `Stop` gives `Finished`.

All are async, with a 5-second timeout.

**The command.** Each one runs, in order:
1. The JXA script, with Sanduhr's folder as its argument and the hook input on stdin. It writes
   `cc-signal-<unix ms>-<random>.json` owner-only, written under a hidden temporary name and then renamed:
   `{"v":1,"event":"prompt"|"waiting"|"done","cwd":"…","session":"…","at":<unix ms>}`.
   - It keeps `cwd` and `session_id` only. It reads all of stdin, which on the prompt event includes the prompt
     text, and drops everything else. The script's text is pinned by a test, as `stopTasksScript` is.
   - It stops writing once 100 signal files are waiting.
   - Strings are clipped: `cwd` to 1024 characters, `session` to 128.
   - Like `stopTasksScript`, it holds no single quotes.
2. `notifyutil -p com.626labs.sanduhr.claude-code.<event>`, with `prompt` a new name beside `waiting` and `done`.
   The post comes after the file's rename, in the same command, so a post from the new hook always finds its
   file already in place.

**Stop runs one script, not two (my call, from the builder check).** Claude Code's input can be read only once,
so on `Stop` the signal script also writes item 66's background-task report: same fields, same caps, same
`watch-stop-*.json` file, and only when `watchers.json` holds `"background":true`. That check moves from the shell
`grep` into the script. The shell keeps the `pgrep -xq Sanduhr` gate. `stopTasksScript`'s pinned text is
replaced by the merged script's, and the current Stop command counts as an older generation.

**Whether the script runs only while Sanduhr runs:** the item 66 report guards itself with `pgrep -xq Sanduhr`.
The signal script uses the same guard, so a quit Sanduhr leaves no files piling up. Windows' 100-file cap stays
as a backstop.

**The app side.** `ClaudeCodeSignal` registers the three names with `notify_register_dispatch`. On any of them, a
pure `ClaudeCodeSignalFile` parses the waiting files: each is read and deleted at once, and one older than
10 minutes, larger than 4 KB or malformed is deleted unread. A leftover temporary file older than 10 minutes is
removed. Reset names the patterns. Duration is `Finished` minus the session's last `Started`; with no `Started`
it is unknown.

**Which path a post takes (my call).** On a post, Sanduhr first reads the waiting signal files. If any are
there, they go down the new path: the rules and threshold decide everything, and the old glow handler doesn't
fire. A post with no file is an older hook (or a file dropped by the 100-file cap) and takes the legacy path.

**An older hook keeps working exactly as in 2.46.** A bare `waiting` or `done` post with no signal file drives
the notch glow under today's glow switches and rules, and nothing else: no notification, no card, no project.
The integration row reads Outdated and offers Update.
- **Install** writes all three events.
- **Update** rewrites in place.
- **Remove** takes out all three, including older command generations.

`IntegrationInstaller`'s recognition and its tests cover each older command.

**At launch nothing replays.** The first pass over waiting signal files records each turn's start and raises
nothing (Windows section 11).

## 2. Codex and the Antigravity CLI: the readers

Pure readers, lines in and events out, with the exact rules of the Windows spec section 3:
- **Codex.** `task_started` gives `Started`. `task_complete` gives `Finished`, with the duration from
  `duration_ms`. `turn_aborted` gives `Stopped`. The project is the `cwd` of the `turn_context` in force. A
  rollout whose `session_meta` has `forked_from_id` or `parent_thread_id` is a sub-agent and notifies nobody; the
  mark sticks once set. Compressed rollouts are finished history and are never followed.
- **Antigravity CLI.** `USER_INPUT` gives `Started`. A `PLANNER_RESPONSE` with status `DONE` and no `tool_calls`
  key arms a 90-second timer; any new line cancels it; on expiry it raises `Finished`, timed by the planner
  step. There is no `Stopped`. The project comes from 2.46's summaries lookup (`AntigravitySummaries`, two
  columns only). Only the presence of the `tool_calls` key is read, never its value.

An `AgentActivityWatcher` owns the FSEvents streams, the in-memory offsets per file, a torn last line kept for
the next read, and the 30-second fallback poll. It is separate from `CodexUsageService` and from the Vault
ingest: turn state is not tied to their lifetimes, as Windows decided. A file changed in the last 10 minutes at
launch is read from up to its last 4 MB to learn whether a turn is open, without notifying; older files start at
their end. Codex homes come from `CodexHomes`, the same `~/.codex` plus the app's own `CODEX_HOME` as 2.46.
Antigravity's home is `~/.gemini/antigravity-cli`. An agent is followed only while its source is on in Sources.

## 3. The rules

As in the Windows spec section 4 and its section 11 amendments, in a pure `NotificationRules`:

| Event | Notification, glow | Desk card |
|---|---|---|
| `Started` | Never | A card with a running timer |
| `Finished` | When the duration is at least the threshold and the agent's app is not in front | Always |
| `Stopped` | As `Finished`, worded "stopped" | Always |
| `Waiting` | At once, unless the agent's app is in front | Always |
| `Idle` (Claude Code) | Only with the idle switch on, then like `Waiting` | Never |

- **Unknown duration** never interrupts, unless the threshold is Every turn.
- **In front** is one check: `NSWorkspace.frontmostApplication`'s bundle id against a list per agent. Claude Code
  and the Antigravity CLI use today's `ClaudeCodeGlowRules.terminalBundleIDs`; Codex uses that list plus the
  Codex app's bundle id, which the builder looks up on a Mac with Codex or from the app bundle and records. The
  front app is read only when an interrupt is otherwise due and the option is on.
- **The limiter** is the glow's, keyed by agent and session: one kind at most every 20 s, and no `Finished`
  within 5 s of a `Waiting`.
- **An agent notifies only while its source is on**, and Claude Code only with the updated hook. Demo mode raises
  nothing. Focus (Do Not Disturb) is left to macOS, which holds notifications itself.
- **Quiet hours (owner, 2026-11-21).** Alerts' quiet hours (`AlertSettings.quietEnabled`, `quietStart`,
  `quietEnd`) hold agent notifications and the glow too; the Desk card still updates. They don't follow Alerts'
  master switch (`alertsEnabled`) or its delivery route (banner, Desk, both): agents have their own switches on
  the Notifications page. So the new surfaces get their own small posting path beside `Notifier.deliver`,
  sharing only the quiet-hours check.

## 4. The surfaces

- **Notification.** It goes through `Notifier`, the existing `UNUserNotificationCenter` path:
  - **Title:** "<Agent> finished", "<Agent> stopped" or "<Agent> is waiting on you".
  - **Body:** "<project>, 4m 12s", or the project alone for waiting.
  - **Identifier:** made from agent and session, so a session's newer notification replaces its older one. The
    idle reminder has its own identifier.
  - **Thread:** `sanduhr-agents`.
  - **Click:** shows the widget. This is a new branch in `Notifier`'s `didReceive`, keyed by the identifier's
    prefix; no notification does this today.
  - **Sound:** the alert sound setting already applies to banners.
  - **Permission:** if macOS notification permission is off, the Notifications page says so with a link to System
    Settings, as alerts do today.
- **Notch glow.** It goes through `NotchGlowController`. Today every glow uses the notch ink
  (`notchTextColor`). New work: `fire()` and the glow views take an optional color. `Finished` and `Stopped`
  glow in the agent's Sources color (`AgentSourceSettings.color(of:)`); `Waiting`, `Idle` and every other glow
  keep the ink. Where it shows follows the existing target settings. On a Mac
  without a notch it follows the island or pill rules the glow already has.
- **Desk turn card.** A new `WatcherSource.turn` on the existing watcher board:
  - **Card:** the agent's name, the project, a running "2m", then "finished · 4m" for about a minute before it
    fades.
  - **Where:** only where the watchers piece is placed. It does not need "Let Claude show watchers", which gates
    the MCP's cards. `.automatic` stays item 66's background-work cards.
  - **Lost touch:** a running card loses touch after an hour (Antigravity's after 10 minutes) and is removed
    60 seconds later. A Codex or Antigravity turn that is still writing keeps its card alive, with activity
    reported at most once a minute.

## 5. Settings

A new **Notifications** section in Settings, after Alerts (`SettingsSection.notifications`, icon `bell.badge`):

- **A row per agent:** what it supports ("Working, finished and waiting"; "Working and finished"; "Working,
  finished about a minute and a half late") and a switch. Claude Code's row shows the hook's state per linked
  folder and offers Update. Codex and Antigravity rows say "Switch it on in Usage ▸ Sources" while their source
  is off.
- **Surface switches:** notification, glow on finished, glow on waiting, Desk card.
- **The threshold:** Every turn, 30 seconds, 1 minute, 2 minutes, 5 minutes.
- **"Not while the agent's app is in front"**, and the idle reminder switch.
- **Try it** buttons per surface.

Settings ▸ Claude Code keeps the hook's install row and links here. Notch ▸ Glow keeps the glow's targets and
its other kinds.

**One set of glow switches (my call, from the builder check).** "Glow on finished", "glow on waiting" and "not
while the agent's app is in front" are the existing keys in the Desk defaults store (`UserDefaults.desk`):
`notchGlowClaudeDone`, `notchGlowClaudeWaiting`, `notchGlowClaudeSkipTerminal`. Their captions widen from
Claude Code to every agent. The Notifications page and Notch ▸ Glow show the same switches, so nothing is
migrated and the legacy path keeps reading them.

| Setting | Key | Default | On upgrade |
|---|---|---|---|
| Notification | `agentNotifications.banner` (new) | On | On |
| Desk card | `agentNotifications.card` (new) | On | On |
| Glow on finished, glow on waiting | the existing two `notchGlowClaude*` keys | Off | Unchanged |
| Threshold | `agentNotifications.threshold` (new) | 1 minute | 1 minute |
| Not while in front | the existing `notchGlowClaudeSkipTerminal` | On | Unchanged |
| Idle reminder | `agentNotifications.idle` (new) | Off | On if `notchGlowClaudeWaiting` was on |

The new keys live in the app's standard defaults; the upgrade rule reads the Desk store. **A change for people who had the glow on:**
with the updated hook, a finished glow waits for a one-minute turn. Every turn restores the old behavior, and
the CHANGELOG says so.

A What's New card for 2.47.0 points to the page.

## 6. Privacy

- No network, no new library, no new permission. The notification permission the app already asks for is used.
- The Codex and Antigravity files are the ones 2.46 reads, now also for turn markers and step status. Never
  message text.
- **New on disk:** `cc-signal-*.json` in Sanduhr's support folder, each holding a folder path and a session id,
  deleted as read. Turn state stays in memory.
- Notification text (agent, project name, duration) stays in macOS Notification Center until the user clears it.
- `docs/PRIVACY.md` gains a Mac row, and the consent copy on the hook's Install says exactly what the hook reads
  and keeps.

## 7. Failures

- A line a reader can't parse is skipped; readers never throw out of the watcher.
- If an FSEvents stream fails, that folder falls back to the 30-second poll.
- A Codex rollout compressed or removed mid-turn drops that turn; no `Finished` is invented.
- A malformed, oversized or stale signal file is deleted unread.
- A surface that throws is logged by type and doesn't stop the others.

## 8. Testing

- **Readers:**
  - Fixture lines per agent, for Codex, Antigravity and the signal file.
  - The Antigravity look-alike endings.
  - Sub-agent suppression.
  - A torn last line.
  - A test that no reader touches a message-text field.
- **Rules:** one case per table row, unknown duration, each agent's in-front list, the limiter, idle vs waiting.
- **Hook:**
  - Install and remove.
  - Update from each older command generation.
  - An old hook driving the glow exactly as in 2.46.
  - The JXA script's text pinned.
  - A script run against made-up stdin that writes only `cwd` and `session`. `osascript` is on CI's macOS runner;
    if a sandboxed test can't run it, test the same logic through JavaScriptCore.
- **Pinned first:** limit alerts' banners and today's glow behavior, before they share a path with the new
  surfaces.
- **Settings:** the migration from the glow keys, the defaults, the page's copy.
- **Idle CPU** with all three agents on and quiet.
- **Smoke:**
  - A new smoke section on fixture homes, driving signals through the debug hooks: a `notify` action that posts
    a made-up event, and state.yaml blocks for turn cards and the last notification's title and body. No real
    agent needed.
  - A real Claude Code turn on this Mac, since Claude Code is installed here.

## 9. Verify first, in the build

1. **The stdin fields.** An async hook on macOS receives `session_id` and `cwd` on stdin for `UserPromptSubmit`,
   `Notification` and `Stop`. Windows checked this on 2026-11-18; check it once on this Mac with a throwaway
   command in a scratch Claude Code folder, never the owner's own folders.
2. **The cost of `osascript` per prompt:** time it. If it is noticeably slow, the prompt event alone may skip the
   script and record only a timestamp-less start, which degrades duration to unknown.
3. **The Codex app's bundle id**, for the in-front list.

If check 1 fails, Claude Code keeps today's glow and gets no notification or card until a signal with identity
exists.

## Calls made after the builder check (2026-11-21)

- On `Stop`, one merged script writes both the signal and item 66's report (section 1).
- A post with a waiting file takes the new path, and a bare post the legacy glow (section 1).
- The glow switches are the existing keys, shown on both pages; no migration (section 5).
- Glow color is new work, and only finished and stopped take the agent's color (section 4).
- Clicking a notification is a new handler (section 4).
- Quiet hours apply; Alerts' master switch and delivery route don't (owner's answer, section 3).

## Left to the builder

- Exact copy of the page captions and the Try it notifications, in the house voice.
- File and type names beyond the ones named here.
- Where the notification permission prompt is triggered, if the app hasn't asked yet: on first switching a
  surface on, not at launch.
