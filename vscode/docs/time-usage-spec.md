# Sanduhr Time usage spec, v1

The on-disk contract for Sanduhr Time. A second implementation (Sanduhr, a publisher, a script) that follows this document reads and writes the same files and gets the same numbers. Nothing here depends on the code that wrote it; all example values are invented.

Conventions used throughout:

- **Time units are never mixed.** `time` fields in spool lines are unix **seconds** as a float. Every duration in a day file (`*Ms`) and every interval bound in the algorithms below is **milliseconds** (epoch milliseconds for instants). Convert once, on read: `ms = round(seconds * 1000)`.
- **Dates are local.** `YYYY-MM-DD` always means the calendar date in the machine's local time zone, never UTC.
- **Files are JSON Lines or JSON, UTF-8.** A JSONL reader skips blank lines and any line that fails to parse (a torn write); it never fails the whole file.
- **`v: 1`** marks every record. A reader that sees another `v` skips the record.
- **Public safety.** No file stores a real path, file content, prompt text or transcript text. Paths are hashed; names live only in the day file and `aliases.json`.

## 1. Directory layout and the data directory

Everything lives under one **data directory**. Resolution order, first non-empty wins:

1. The `sanduhrTime.dataDir` setting (the extension only).
2. The `SANDUHR_TIME_DIR` environment variable.
3. `~/.sanduhr/time` (the user's home directory, on every platform).

```
<dataDir>/
  aliases.json                              alias store (section 6)
  spool/<date>.<machine>.<window>.jsonl     editor heartbeats (section 3)
  claude/<date>.jsonl                       Claude events (section 4)
  days/<date>.json                          merged day files (section 5)
  state/installed.json                      install time and salt (section 7)
  state/offsets.json                        transcript read positions (section 7)
  state/merge.lock                          the merge lock (section 7)
  state/caveats.json                        Claude Code versions and unreadable-line counts per date (section 7)
  state/wakatime-compare.jsonl              optional daily comparison (section 7)
```

- `<date>` in `spool/` and `claude/` is the local date of the line's `time`. A merge for date D reads D's files plus the last hour of the previous date's files.
- `<machine>` is the normalized hostname: lowercase, every character outside `[a-z0-9-]` replaced by `-`. It never contains a dot.
- `<window>` is the first 8 hex characters of sha256 of the VS Code window's session id: one spool file per window, so concurrent windows never write the same file.
- A merge reads only spool files whose `<machine>` segment equals its own machine label. A data directory synced between machines is therefore never double-counted. Claude spool files carry no machine segment; each machine reads transcripts only from its own disk.
- Retention: spool files and Claude spool files older than 90 days are deleted. Day files are kept.
- Writers append whole lines. Before appending, a writer checks that the file ends with a newline and writes one first if it does not, so a torn line never fuses with the next record.
- Whole-file writes (`days/*.json`, `aliases.json`, `state/*.json`) go to a temporary file in the same folder and are renamed into place.

## 2. Hashes, project keys and the remote normalizer

### 2.1 `hashPath(absolutePath, salt)`

The one path hash. Both the editor recorder and the Claude reader use it, so the same file gets the same `entity` on both sides.

1. Resolve the path to an absolute path using the platform's rules.
2. Replace every backslash with a forward slash.
3. On Windows only, lowercase the whole string (drive letter included).
4. Compute `sha256(salt + path)` as UTF-8, hex-encoded; the result is the **first 24 hex characters**.

`salt` is the per-install random value in `state/installed.json`. Two paths that name the same file through different spellings (separators, drive-letter case on Windows, `..` segments) hash identically.

### 2.2 `normalizeRemote(url)`

Canonical form of a git remote URL, applied identically wherever a remote is compared. In order:

1. Trim whitespace. Remove any `?query` or `#fragment` (everything from the first `?` or `#`). An empty result returns `""`.
2. If the string has a scheme (`^[a-z][a-z0-9+.-]*://`), split the remainder into authority and path at the first `/`. Drop credentials: keep only what follows the last `@` in the authority. Then:
   - scheme `ssh`, `git` or `ssh+git`: remove a `:port` suffix from the host and rewrite as `https://<host><path>`.
   - scheme `http` or `https`: rewrite as `https://<host><path>` (port retained, credentials gone).
   - any other scheme: keep `<scheme>://<host><path>` with the scheme lowercased.
3. Otherwise, if the string matches scp-like syntax `[user@]host:path` (the host part has at least 2 characters, so a one-letter Windows drive is not mistaken for a host, and the part after the colon does not start with `//`), rewrite as `https://<host>/<path>` with leading slashes of `<path>` removed.
4. Rewrite a leading `https://www.github.com/` as `https://github.com/`.
5. Strip trailing slashes, then a trailing `.git` (case-insensitive), then trailing slashes again.
6. Lowercase the whole string.

Examples (all synthetic):

| Input | Output |
|---|---|
| `git@example.com:Acme/Widget.git` | `https://example.com/acme/widget` |
| `https://user:token@example.com/Acme/Widget.git/` | `https://example.com/acme/widget` |
| `ssh://git@example.com:2222/Acme/Widget` | `https://example.com/acme/widget` |
| `http://www.github.com/Acme/Widget` | `https://github.com/acme/widget` |

### 2.3 `projectKey(remoteOrRoot)`

A 16-hex-character project identifier:

- **With a remote:** `sha256(normalizeRemote(remote))`, first 16 hex characters. The input counts as a remote when it has a scheme (`^[a-z][a-z0-9+.-]*://`) or matches the scp-like form of 2.2 step 3 and does not begin a Windows path.
- **Without a remote:** `sha256("path:" + foldedRoot)`, first 16 hex characters, where `foldedRoot` is the project root directory with backslashes turned into forward slashes, trailing slashes removed and the whole string lowercased.

The reserved key **`none`** (a literal, not a hash) is the bucket for untitled files that belong to no folder. Its real name is `(no project)`.

The project **root** is the nearest ancestor directory (or the path itself) that contains a `.git` entry, as a directory or a file; with none, the folder itself (for a file, its parent). The **remote** is `[remote "origin"] url` read from the git config file. For a linked worktree, `.git` is a file whose body is `gitdir: <path>`; that directory's `commondir` file points at the main git directory, whose `config` holds the remote. No `git` process is run. The **real name** is the last path segment of the remote with `.git` removed (case preserved), or the root folder's name. The **branch** comes from the `HEAD` file of the checkout's own git directory (`ref: refs/heads/<name>`), or null when detached.

## 3. The editor spool line

File: `spool/<date>.<machine>.<window>.jsonl`. One JSON object per line:

| Field | Type | Meaning |
|---|---|---|
| `v` | `1` | Format version. |
| `time` | number | Unix seconds, float. |
| `entity` | string | `hashPath` of the file's absolute path (24 hex). Never a real path. |
| `type` | `"file"` | Always `file`. |
| `category` | `"coding"` | Always `coding`. |
| `kind` | `"edit"` \| `"save"` \| `"focus"` \| `"nav"` | `edit`: content changed. `save`: file saved. `focus`: active editor changed. `nav`: selection changed. |
| `project` | string | Project key (16 hex) or `none`. |
| `branch` | string \| null | Git branch, or null. |
| `language` | string | The editor's language id, for example `typescript`. |
| `lines` | integer | The document's line count at the time. |
| `human_line_changes` | integer >= 0 | Lines the human inserted plus removed in this heartbeat. A **line count, not keystrokes**. Always 0 when `reload` is true. |
| `is_write` | boolean | True for `save` heartbeats. |
| `reload` | boolean | True when the change was the editor reloading the file from disk. |

The names follow WakaTime where they overlap. There is no `ai_line_changes`: Claude's lines come only from the Claude spool.

**Recording rules.** Recording happens only while the window is focused. Only `file` and `untitled` documents are recorded (output panes, git and other virtual documents are ignored); `untitled` files use project `none`. `focus`, `nav` and `save` heartbeats are limited to one per file per kind per 2 minutes. `edit` heartbeats are coalesced: all edits to one file inside a 10-second window become one heartbeat with summed `human_line_changes`, stamped with the time of the last edit.

**Line delta.** For each content change in an event: `removed = range.end.line - range.start.line`, `added = number of '\n' characters in the inserted text`; `human_line_changes` is the sum of `removed + added` over the changes. Typing inside one line is 0 lines (still an `edit` heartbeat, which is activity); pressing Enter is 1; deleting three whole lines is 3; replacing two lines with four is 2 + 3.

**Reload rule.** A change event with no content changes only flips the dirty flag and counts no lines. For events with content changes:

1. A change whose `reason` is Undo or Redo is the human's edit.
2. Otherwise, if the document is dirty when the handler runs (read synchronously), it is the human's edit.
3. Otherwise the event is a *candidate*. If the next event for that document is an empty change with the document dirty and the **same document version**, the candidate was the human's first edit after a clean state, and it is counted as an edit. Any other next event, or 500 ms with no event, makes the candidate a **reload**: it is written with `reload: true`, `human_line_changes: 0`, and never counts as the human's activity.

The single signal "the document is not dirty after the change" is not enough: the first keystroke after every save also reports not dirty. A reload is never activity and never adds lines. As a backstop, the merger also discards an `edit` heartbeat that lands on the same `entity` as a Claude edit within 2 seconds (section 8.4).

## 4. The Claude spool line

File: `claude/<date>.jsonl`, written by reading Claude Code's own transcripts (JSONL files under a Claude config home's `projects/` folder, including `subagents/` subfolders). One JSON object per line:

| Field | Type | Meaning |
|---|---|---|
| `v` | `1` | Format version. |
| `id` | string | The transcript entry's `uuid`, or `<stream>:<byte offset>` when the entry has none. Readers de-duplicate on `id`, first occurrence wins. |
| `time` | number | Unix seconds, float (the entry's `timestamp`). |
| `project` | string | Project key of the entry's working directory (`cwd`; a line with none inherits the most recent `cwd` seen in that file). |
| `stream` | string | First 16 hex of sha256 of the transcript file's real path (backslashes to slashes, lowercased on Windows). One stream per file, so parallel subagents are separate streams even when they share the parent's session id. |
| `kind` | `"prompt"` \| `"activity"` \| `"edit"` | See below. |
| `linesAdded` | integer | `edit` only. |
| `linesRemoved` | integer | `edit` only. |
| `entity` | string | `edit` only: `hashPath` of the edited file. |

Only the fields listed here are extracted from transcript lines; no prompt text or file content is ever stored.

**Human-prompt rule.** A transcript entry is the human's prompt (`kind: "prompt"`) when all hold: its `type` is `user`; its message content is a string, or an array containing no `tool_result` part; `isSidechain` is not true; `isMeta` is not true; `isCompactSummary` is not true; and, when an `origin` object is present, `origin.kind` is `"human"`. Machine-written user entries (subagent task prompts, task notifications, peer messages, meta entries, compaction summaries) are therefore never the human's.

**Claude activity rule.** Every `assistant` entry, and every `user` entry whose content carries a `tool_result`, in the main session or a sidechain, is Claude activity (`kind: "activity"`).

**Claude line rule.** When an activity entry has a `toolUseResult` object with a non-empty `filePath`, the event's `kind` is `edit` and:

- if `toolUseResult.type == "create"`: `linesAdded` = the number of lines in `content` (split on `\n`; one trailing newline does not add a line; empty content is 0), `linesRemoved` = 0.
- otherwise: over every hunk's `lines` array in `structuredPatch`, `linesAdded` = the count of strings starting with `+`, `linesRemoved` = the count starting with `-`. Hunk header counts (`oldLines`, `newLines`) are never used because they include context lines.

An `edit` event is also Claude activity: one transcript entry produces one event, and the merger uses `activity` plus `edit` events for Claude's time.

Entries older than the install time (`state/installed.json`) are dropped, so history before install never appears. Lines that are not valid JSON, or have no usable timestamp or working directory, are counted per local date as unreadable (`caveats.unreadableTranscriptLines`).

## 5. The day file

File: `days/<date>.json`, validated by `time-day.schema.json` (JSON Schema draft-07). Durations are integer milliseconds.

```json
{
  "v": 1,
  "date": "2026-06-10",
  "machine": "machine-1",
  "generatedAt": "2026-06-10T18:00:00.000Z",
  "totals": {
    "youMs": 360000, "claudeMs": 240000, "bothMs": 120000, "totalMs": 720000,
    "agentMs": 480000, "linesYou": 12, "linesClaude": 80
  },
  "projects": [
    {
      "key": "<16 hex chars>", "realName": "widget", "alias": "Project Kestrel", "masked": false,
      "youMs": 360000, "claudeMs": 240000, "bothMs": 120000, "totalMs": 720000,
      "agentMs": 480000, "linesYou": 12, "linesClaude": 80,
      "languages": [ { "id": "typescript", "ms": 300000 } ]
    }
  ],
  "caveats": { "unreadableTranscriptLines": 0, "readerVersion": "0.1.0", "claudeCodeVersions": [] }
}
```

| Field | Meaning |
|---|---|
| `v` | `1`. |
| `date` | The local date. |
| `machine` | The machine label that wrote it. |
| `generatedAt` | ISO 8601 timestamp of the merge. |
| `totals` | The pooled day totals (8.5). |
| `projects[]` | One row per project with any time or lines in the day, sorted by `totalMs` descending (ties by `key`). |
| `projects[].key` | Project key or `none`. |
| `projects[].realName` | The project's real name; `(no project)` for `none`. Real names stay in local files; a publisher decides what leaves the machine. |
| `projects[].alias` | The alias from `aliases.json`, or null when none has been assigned. |
| `projects[].masked` | The mask flag from `aliases.json`, false when absent. At publish time, read the live store, not this snapshot. |
| `youMs` | Time only you were active (`|U - C|`). |
| `claudeMs` | Time only Claude was active (`|C - U|`). |
| `bothMs` | Time both were active (`|U intersect C|`). |
| `totalMs` | The union (`|U union C|`); always `youMs + claudeMs + bothMs`. |
| `agentMs` | Agent time: the sum over Claude streams of each stream's own interval length (parallel streams add). |
| `linesYou`, `linesClaude` | Line counts (8.4). |
| `languages[]` | Up to 10 `{ id, ms }` entries, sorted by `ms` descending then `id`, `ms >= 1`. The publisher sends the top 5. |
| `caveats.unreadableTranscriptLines` | Unreadable transcript lines for the day. |
| `caveats.readerVersion` | The tracker's semver. |
| `caveats.claudeCodeVersions` | Claude Code versions seen, possibly empty. |

## 6. `aliases.json` and the fresh-store rule

File: `<dataDir>/aliases.json`.

```json
{
  "v": 1,
  "streamerMode": false,
  "projects": {
    "<16 hex chars>": { "realName": "widget", "alias": "Project Kestrel", "masked": false, "setAt": "2026-06-10T18:00:00.000Z" }
  }
}
```

| Field | Meaning |
|---|---|
| `v` | `1`. |
| `streamerMode` | When true, every project is treated as masked, in the panel **and** when publishing. |
| `projects` | Map from project key to an entry. |
| `realName` | The project's real name when the alias was assigned. |
| `alias` | A neutral two-word display name (for example `Project Basalt`) drawn from a pool of about 100; when the pool is exhausted, a number is appended. Assigned lazily the first time a project needs one, and unused names are preferred. |
| `masked` | The per-project mask. |
| `setAt` | ISO 8601 timestamp of the last change. |

Every write happens while holding the merge lock (section 7.3) and goes through a temporary file and rename, so concurrent windows never lose each other's assignments.

**Fresh-store rule.** A missing `aliases.json` is a fresh, empty store (`streamerMode: false`, no projects) **only when `state/installed.json` is being created in the same activation**, meaning this is the first run on this data directory. In every other case a missing file, like an unparseable one, is an error ("alias store unavailable"), not an empty store. A caller that publishes must then fail closed: publish nothing, or publish aliases only, never real names. Reason: a deleted or corrupted store would otherwise silently unmask every project.

**Project registry.** `realName` is also how windows share names: the first time a window resolves a project it records `{ realName, alias }` here (under the lock), so any window's merge can name a project another window saw. A merge for a project whose name is unknown writes `(unknown project)`.

Naming rule for publishing: a real name may leave the machine only when the project is bound to a dashboard project, `streamerMode` is false and the project is not masked; every other case, and every failure, publishes the alias.

## 7. The `state/` files

### 7.1 `state/installed.json`

```json
{ "installedAt": "2026-06-10T12:00:00.000Z", "salt": "<64 hex characters>" }
```

`installedAt` is an ISO 8601 timestamp; `salt` is 32 random bytes, hex-encoded, the salt for `hashPath`. Created with an exclusive create so two windows racing cannot both win; never overwritten. Losing it changes every `entity` hash.

### 7.2 `state/offsets.json`

```json
{ "/fixture/home/projects/p1/session.jsonl": { "offset": 12345, "size": 12400, "mtimeMs": 1780000000000 } }
```

A map from a transcript file's real path to the position already consumed: `offset` bytes read through the last complete line, and the `size` and `mtimeMs` observed. A file that shrank, or whose `offset` exceeds its size, restarts from 0. A file with no entry whose mtime is before `installedAt` starts at its current size (no catch-up of old history); a newer file starts at 0. This is the one state file that holds real paths; it is local only and never published. Order of operations: append events to the Claude spool first, advance the offset second, so a crash re-reads rather than drops (the spool de-duplicates by `id`).

### 7.3 `state/merge.lock`

```json
{ "pid": 4242, "at": 1780000000000 }
```

Created with an exclusive create by whoever runs a merge or writes `aliases.json`; `at` is epoch milliseconds, refreshed about every 30 seconds while held. A lock is **stale** only when `at` is more than 2 minutes old **and** the process `pid` is no longer running; a lock whose body cannot be read is judged by the file's age alone. A stale lock is deleted and re-created; a live lock means skip this tick (a merge) or retry for a short while (an alias write). Release removes the lock only if `pid` is still the owner's.

### 7.4 `state/caveats.json`

```json
{ "2026-06-10": { "versions": ["2.1.5"], "unreadable": 2 } }
```

Per local date: the distinct Claude Code versions seen in transcript lines (their `version` field) and the running count of unreadable transcript lines. Each reader pass adds to it under the merge lock, so a later merge of the same date still reports earlier passes. A merge writes `versions` into `caveats.claudeCodeVersions` and `unreadable` into `caveats.unreadableTranscriptLines`. Entries older than 90 days are dropped.

### 7.5 `state/wakatime-compare.jsonl`

Optional. One line per day: `{ "date", "wakatimeSeconds", "oursEditorSeconds", "at" }`, appended at most once an hour (when today's record is merged and the last run for today is over 60 minutes old, or on the `compareNow` command) while a `wakatime-cli` is present. A day can therefore hold several lines; the newest is the day's freshest comparison and the last of the day is its final one. `wakatimeSeconds` is from `wakatime-cli --today --output raw-json` (`data.grand_total.total_seconds`, rounded), falling back to the text form (`2 hrs 32 mins`, minute precision); `oursEditorSeconds` is (`youMs` + `bothMs`) / 1000 from today's day record at the time of the run, rounded. Nothing is appended when the setting is off, the CLI is missing, or the run fails.

## 8. Day computation (the merger)

Inputs for date D: D's heartbeats and Claude events, the previous date's events (at least its last hour), the alias map, the real names by key, the idle setting (`idleMinutes`, default 15), and the caveats. The merger is a pure function; it performs no I/O.

### 8.1 The day window

`start` = local midnight at the start of D, `end` = local midnight at the start of D + 1 day. Both are computed with local calendar arithmetic (construct the date for D and for D + 1 in local time), **never by adding 24 hours**, so a daylight-saving day is 23 or 25 hours long. Events at or after `end` belong to the next day and are ignored; events before `start - 1 hour` are ignored. Every interval is clipped to `[start, end]` after joining. Lines count only events with `time >= start`.

Claude events are de-duplicated by `id` across the day and tail inputs before anything else.

### 8.2 Joining events into intervals

`join(times, gap, lone)`: sort the times, collapse identical timestamps, and start a new interval whenever the distance to the previous time is **greater** than `gap` (a distance of exactly `gap` still joins). An interval spans its first to its last event. **Only an interval that holds a single event** gets duration `lone`: `[t, t + lone]`.

### 8.3 Whose events, which gaps

Per project:

- **Your events:** the `time` of every heartbeat with `reload` false (any kind), plus, for each Claude `prompt` event at `t`, two events: `t - 2 min` and `t` (the prompt span's start and end). `gap = idleMinutes`, `lone = 2 min`. A prompt on its own therefore yields `[t - 2 min, t]`; a prompt one minute after an edit joins it into one interval; a prompt joins an earlier edit whenever its span start is within `idleMinutes` of it.
- **Claude's events:** the `time` of every Claude event of kind `activity` or `edit` (an edit is also activity). `gap = 5 min`, `lone = 2 min`. Prompts are not Claude activity.
- **Reload heartbeats** are not your activity and add no lines.

Name the clipped results `U_p` (yours) and `C_p` (Claude's, all streams together). For **agent time**, join each stream's own events separately (gap 5 min, lone 2 min), clip, and sum the stream lengths per project: `agentMs = sum over streams of |stream intervals|`. Two streams working in parallel over the same minutes add up to twice the wall-clock time.

### 8.4 Totals and lines

Per project: `bothMs = |U_p intersect C_p|`, `youMs = |U_p - C_p|`, `claudeMs = |C_p - U_p|`, `totalMs = |U_p union C_p|`, where `|.|` is total covered milliseconds with overlaps counted once. A project row exists when it has any time or any line in the day.

Lines:

- `linesClaude` = sum of `linesAdded + linesRemoved` over the project's `edit` events.
- `linesYou` = sum of `human_line_changes` over non-reload heartbeats, **excluding** an `edit` heartbeat whose `entity` equals the `entity` of a Claude `edit` event within 2 seconds of it (the backstop for reloads the recorder could not identify). An excluded heartbeat still counts as your time.

### 8.5 Day totals pool all projects

`U_all` = union of every `U_p`; `C_all` = union of every `C_p`. The day's `youMs`, `claudeMs`, `bothMs` and `totalMs` are computed from `U_all` and `C_all` with the same four formulas. `agentMs` and both line counts are sums over projects.

Consequence: per-project totals can add up to more than the day total. Editing in project A while Claude works in project B is, at the day level, time you and Claude were both active; at the project level it is you-only in A and Claude-only in B. Do not "fix" this by summing rows.

### 8.6 Languages

Per project, from the non-reload heartbeats only. Group them into stretches with the same gap rule as 8.2 (`idleMinutes`). Within a group, the span from each heartbeat to the next is attributed to the language of the **later** heartbeat, the one that extended the stretch. A group with a single heartbeat gets `[t, t + 2 min]` for its language. Clip to the day, sum per language, sort by milliseconds descending (ties by id ascending), keep up to 10. Prompt spans carry no language, so composing time is never attributed to a language; the languages therefore sum to at most `youMs + bothMs`.

### 8.7 Names and caveats

`realName` is the resolver's real name for the key (`(no project)` for `none`); `alias` and `masked` come from the alias map, with a missing entry meaning `alias: null, masked: false`. `caveats` are passed through unchanged.
