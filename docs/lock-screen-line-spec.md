# Lock screen line (issue #137)

Spec, 2026-10-25. Build attended, on a branch, after review: it adds the app's first code that runs as
root.

## What it is

The Mac lock screen has one line of free text: the loginwindow message (System Settings, Lock
Screen, "Show message when screen is locked"). It is the first thing seen on coming back to the
desk, and today it is static. Sanduhr composes a short line from what it already knows and keeps it
current:

```
Desk 3 · Claude 29% · resets 6:10 PM · 1 waiting for you
```

The line is built from slots, each slot is distilled to counts, percents, times and short labels,
and one sanitizer stands between every slot and the lock screen. A small privileged helper writes
the line; Sanduhr itself never runs as root.

## Non-goals

- No task titles, file names, repo names, account names or emails, prompt text or meeting titles on
  the lock screen, ever, whatever a source sends.
- No control on the lock screen (it is text only), and no notification or banner.
- No Windows parity in this item (Windows has no equivalent single line).
- No dependence on one tool for the review slot: any source that can report "something is waiting"
  feeds it.

## Slots

The line is `prefix · usage · reset · waiting`, slots left out when empty or switched off, joined by
` · `. Order is fixed; which slots show is a setting.

| Slot | Source | Text | Empty when |
|---|---|---|---|
| Prefix | Settings, a free-text field | as typed, after the sanitizer | the field is empty |
| Usage | `snapshot.json` (`SnapshotWriter`), the shown account's highest tier | `Claude 29%` | no snapshot, or stale (see below) |
| Reset | the same tier's `resets_at` | `resets 6:10 PM` (local time, the system's 12 or 24 hour clock) | no reset time, or usage is empty |
| Waiting | the watchers (`Services/Watchers.swift`), state `waiting` | `1 waiting for you`, `3 waiting for you` | none waiting |

**Stale usage.** When the snapshot's `status` is not `ok` or `captured_at` is older than 20 minutes,
the usage and reset slots show `Claude usage unavailable` once, instead of an old number. Old numbers
on a lock screen read as current; an honest gap is better.

**Waiting, source-agnostic.** The slot counts watchers in state `waiting`, which every feeder already
reaches: the MCP tools (`watch_start`, `watch_update` with state `waiting`), the statusline, and any
later feeder that creates a watcher. A future review queue or streaming mode becomes a watcher feeder;
the slot never learns their names. Only the count leaves. A watcher's title and note stay on the Desk.

## The sanitizer

One pure function, `LockLine.sanitize(_ slot: String, kind: SlotKind) -> String?`, every slot passes
through it, and the composed line passes through it once more before it is written. Rules:

- Plain text only: strip control characters, newlines, tabs, bidi overrides and zero-width
  characters; collapse runs of spaces.
- Drop the slot (return nil) when it contains anything that looks like:
  - a path (`/` followed by a word character, `~/`, a drive letter, a `.swift`, `.md`, `.json` style
    file extension),
  - an email or a URL (`@` between word characters, `://`, `www.`),
  - a secret shape (the same patterns the export's "looks like a secret" check uses, shared, not
    copied),
  - a long run of letters and digits without spaces (24 characters or more: hashes, tokens, ids).
- The prefix is the owner's own text, so it is length-capped (32 characters) and passed through the
  same rules: a prefix with a path or an email is dropped, and Settings says why under the field.
- The composed line is capped at a length the lock screen renders on one line (measure, see open
  questions; start at 80 characters) and cut at a slot boundary, never mid-word.

Tests feed it a task title, a file path, an email, a URL, a token, a bidi override and a 200-character
prefix, and assert none of them reaches the line. This is the outbound filter that a streaming mode
reuses later, so it lives in `Models/LockLine.swift`, not in the view.

## Writing it

Writing `LoginwindowText` in `/Library/Preferences/com.apple.loginwindow` needs root.

- **Helper.** A launch daemon registered with `SMAppService.daemon(plistName:)` (the app already
  uses `SMAppService.mainApp` for Open at Login), bundled in
  `Sanduhr.app/Contents/Library/LaunchDaemons/` with its executable in `Contents/MacOS/`. Registering
  asks for an admin password once (System Settings shows it under Login Items, Allow in the
  Background). The daemon is a separate small target in `Package.swift`, signed and notarized with
  the app in CI.
- **Hand-off.** Sanduhr writes the composed line to a user-owned file,
  `~/Library/Application Support/Sanduhr/lock-line.txt`, atomically. The daemon watches that path
  (it knows the console user's home from `SCDynamicStoreCopyConsoleUser`), reads at most 256 bytes,
  runs the same length and plain-text checks again (it trusts nothing from a user-writable file), and
  sets `LoginwindowText` with `CFPreferencesSetValue` for the any-user, current-host domain, then
  synchronizes. An empty file or a missing file removes the key.
- **Nothing else.** The daemon has no network, takes no arguments, writes only that one key, and
  logs only "set", "cleared" or the reason it refused, never the text.
- **Off and uninstall.** Turning the switch off writes an empty file (the daemon clears the key), then
  unregisters the daemon. Unregistering also clears the key from the daemon's exit path, so a daemon
  removed by the system leaves nothing behind. Reset Sanduhr… does the same.
- **The owner's own message.** If `LoginwindowText` already holds a value Sanduhr did not write, the
  first install keeps a copy in the app's support folder and turning the feature off restores it.

## Refresh

The message is read at lock time, so the file is rewritten:

- every 5 minutes while the session is active, only when the composed line changed;
- on `NSWorkspace.sessionDidResignActiveNotification` and `screensDidSleepNotification` (the Desk
  controller already observes both), so the line is fresh as the screen locks;
- never while the session is inactive: no timer work while locked.

## Settings

A new section under Desktop, "Lock Screen" (`SettingsSection.lockScreen`), after Notch:

- **Show a line on the lock screen** (off by default). Turning it on installs the helper, with the
  admin prompt; turning it off removes it. The row says when macOS still needs approval in Login
  Items.
- **Prefix** text field with the sanitizer's reason under it when it drops the text.
- **Show** checkboxes: Claude usage, reset time, watchers waiting for you.
- **Preview**: the exact line, live, as it would be written, with the character count.
- A caption: "Anyone at the screen can read this line. Sanduhr only puts counts, percents and times
  in it."

Search Settings finds it under "lock screen", "login window", "loginwindow".

## Smoke harness

- `smoke/smoke do lock-line` prints nothing; `state.yaml` gains `lock_line: {on, installed, text,
  length}` where `text` is the composed line (it holds only sanitized counts and times by
  construction, so it may appear in snapshots).
- A scenario drives the composer with fake watchers (`watch-test`) and demo usage and expects the
  composed text, with no daemon installed (the composer runs without it).
- A by-hand section in `docs/mac-smoke-test.md` covers install, lock, the line, off, and the restored
  original message, on a scratch account.

## Build order

1. `LockLine` composer and sanitizer, pure, with the tests above. No UI, no daemon.
2. Settings section and preview, writing `lock-line.txt` only.
3. The daemon target, its plist, `SMAppService` registration, the CI signing step, and the
   install/uninstall paths, attended, on a scratch macOS user first.
4. Refresh hooks, Reset Sanduhr…, the smoke keys and the by-hand section.
5. What's New card and README section.

Items 1 and 2 can run unattended. Item 3 is attended: it is the first root code in the app, and the
CI signing change wants a dry run before a release.

## Open questions

- **Length.** How many characters render on one line on the macOS 26 lock screen at the default
  size, and whether a second line wraps or truncates. Measure on both Macs; the cap follows.
- **Timing.** Whether a changed `LoginwindowText` shows on the next lock without a logout on 26.x. If
  it needs a logout, the feature still works for the "back at the desk tomorrow" case, but the
  5-minute refresh promise in the issue changes; say so in Settings.
- **Approval friction.** Whether `SMAppService.daemon` on 26.x needs the user to flip the item in
  Login Items after the password prompt, and how the row explains it.
- **Stale threshold.** 20 minutes is twice the widget's refresh; confirm against what the Desk shows
  as stale.

## Acceptance (from the issue, kept)

- With the switch on, locking the Mac shows the composed line, and it updates within 5 minutes of a
  usage change (subject to the timing question above).
- A test feeds the sanitizer a task title, a file path and an email, and none of them reach the line.
- Turning it off or uninstalling leaves no daemon and no `LoginwindowText` (or the owner's original
  message, restored).
- Nothing changes with the switch off.
