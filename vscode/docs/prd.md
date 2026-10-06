# Sanduhr Time — Product Requirements

## Problem Statement
A builder who works in VS Code with Claude Code writing much of the code, often in several sessions at once, checks WakaTime daily for total time and time per project, and looks to it for the AI-versus-me split. That split is wrong today: WakaTime's Claude parser reads only the default `~/.claude` home and had found zero Claude sessions on the reference machine. So every day's picture of who built what is missing its largest contributor. They need one honest record of their time and Claude's, per project, that never names employer work off the machine.

## User Stories

### Epic: Your time

- As the builder, I want my editor activity tracked automatically so that I never have to start or stop a timer.
  - [ ] Typing, saving, switching the active file and moving the cursor in a focused VS Code window each record a heartbeat.
  - [ ] Non-edit heartbeats on the same file are throttled to one per 2 minutes; every edit records its line delta.
  - [ ] Nothing records while the VS Code window is unfocused.
  - [ ] Heartbeats within 15 minutes of each other join into one interval; a lone heartbeat counts 2 minutes. The 15 is a setting (`sanduhrTime.idleMinutes`).
  - [ ] A file outside any workspace folder (untitled, loose file) counts under a `(no project)` bucket rather than being dropped.
  - [ ] In a multi-root workspace, each heartbeat goes to the folder that owns the file.
- As the builder, I want my prompts to Claude counted as my time so that steering Claude is credited to me.
  - [ ] Your own prompts in Claude Code transcripts become your activity, covering the two minutes before each one (composing it), under the session's project.
  - [ ] Machine-written user entries never count as yours: subagent task prompts, background-task notifications, messages from other sessions, meta entries and compact summaries.

### Epic: Claude's work

- As the builder, I want Claude's working time counted from its transcripts so that the AI side is real, not estimated.
  - [ ] The reader finds every config home: `CLAUDE_CONFIG_DIR` if set, plus every `~/.claude*` directory containing `projects/`, skipping `*.config-backup-*`.
  - [ ] Assistant and tool entries, subagent transcripts included, join into intervals at a 5 minute gap.
  - [ ] A Claude session in a project never opened in VS Code still counts, as Claude only.
  - [ ] Reading is incremental: after the first pass, each pass reads only bytes appended since the last offset for that file.
  - [ ] An unreadable line is skipped and counted in that day's caveats; the reader never crashes the extension.
  - [ ] Only days from the first activation date onward are built, so no day shows Claude-only time just because your side was not yet recorded.
- As the builder, I want parallel sessions visible so that running several Claudes at once shows up.
  - [ ] The time split uses the union of all of Claude's intervals for a project.
  - [ ] Agent-minutes sums every session's intervals separately and is shown next to the split.
- As the builder, I want Claude's lines counted exactly so that "who built this" is trustworthy.
  - [ ] Claude's lines are added plus removed from each Edit/Write result's `structuredPatch`, attributed to the project of the session's `cwd`.

### Epic: The merged day

- As the builder, I want one record per project per day so that nothing is counted twice.
  - [ ] Per project: both = overlap of your intervals and Claude's; you only and Claude only = the remainders; project total = the union of all three.
  - [ ] Day total = union across projects; switching projects never double counts.
  - [ ] Your lines = editor changes minus any change on a file within 2 seconds of a Claude edit to that file (the disk reload).
  - [ ] An interval that crosses local midnight is split at the day boundary.
  - [ ] The day is the local calendar day; timestamps are stored in UTC.
  - [ ] Time by language comes from your heartbeats only (Claude's edits carry no editor language).

### Epic: Projects and masking

- As the builder, I want projects identified the same way whether I or Claude touched them so that both sides land in one row.
  - [ ] Project key = hash of the normalized git remote; a folder with no remote uses its path.
  - [ ] A git worktree resolves to its main repo's remote, so worktrees fold into one project.
  - [ ] The same resolver handles editor folders and transcript `cwd` values.
- As the builder, I want streamer mode so that I can show the panel without exposing project names.
  - [ ] A toggle masks every project in the panel and status bar hover.
  - [ ] Any single project can be masked or unmasked from its row.
  - [ ] A masked project shows a persistent alias from a pool of about 100 names, unique across projects, the same every day.
  - [ ] Reroll gives a project a new alias immediately and persists it.
  - [ ] The real-name-to-alias map lives only in `aliases.json`.

### Epic: Seeing it

- As the builder, I want today's time in the status bar so that I can glance at it like WakaTime's.
  - [ ] The status bar shows today's day total (for example `3h 12m`) and updates at least every 5 minutes.
  - [ ] Hover shows you, Claude and both for today.
  - [ ] Clicking opens the Time panel.
- As the builder, I want a Time panel so that I can see where the day and the week went.
  - [ ] Today and the last 7 days, with a total per day.
  - [ ] Per project: a stacked you/Claude/both bar, total, agent-minutes, your lines and Claude's, top languages.
  - [ ] Streamer-mode toggle and per-project mask and reroll controls.
  - [ ] Follows the VS Code color theme (dark and light), with visible accent color on the bars.
  - [ ] The Claude trademark disclaimer appears in the panel.
  - [ ] An empty first day says tracking has started rather than showing zeros as if broken.
  - [ ] The day's caveats (unreadable transcript lines) show as a small note when non-zero.

### Epic: Local data and the usage spec

- As the builder, and as Sanduhr later, I want the data in documented local files so that another app can read or take over the merge.
  - [ ] Data lives in `~/.sanduhr/time/`, or `SANDUHR_TIME_DIR` when set.
  - [ ] Spool files are append-only, one per VS Code window, named `spool/<date>.<machine>.<window>.jsonl`, reusing WakaTime heartbeat field names where they overlap (`entity` hashed, `type`, `category`, `time`, `project`, `branch`, `language`, `lines`, `human_line_changes`, `is_write`). Claude's events go to a separate Claude spool.
  - [ ] Day files `days/<date>.json` validate against `docs/time-day.schema.json`.
  - [ ] `docs/time-usage-spec.md` defines every field, versioned v1.
  - [ ] Several open windows never corrupt a day file: the merge runs under a lock, writes a temp file and renames it.
  - [ ] Spool files older than 90 days are removed; day files are kept.
  - [ ] File paths never appear in any file except as salted hashes; the salt is per install.
- As another extension, I want a public API so that I can consume the data without reading files.
  - [ ] `getToday()`, `getDays(from, to)` and `onDayUpdated` are exported from the extension's `activate`.
  - [ ] Returned records carry the real name and the masked flag and alias; the consumer decides what to show.

### Epic: Publishing to the 626 dashboard

- As the builder, I want each machine's day on the dashboard so that the bulletin shows my coding next to Claude usage.
  - [ ] The 626 Labs extension reads the tracker API and publishes the current day every 15 minutes while VS Code is active, and on activation catches up any of the last 7 days changed since they were last published.
  - [ ] A real project name is sent only when the project is bound to a 626 dashboard project (matched by repo, never by name), not masked, and streamer mode is off; every other project is sent under its alias. Any failure to check bindings means alias.
  - [ ] Published fields: date, machine label, totals, and per project the display name, 626 project id when bound, the time split, agent-minutes, lines and top 5 languages. Never file hashes, never the alias map.
  - [ ] If the alias store cannot be read, nothing is published for that day.
  - [ ] A retry replaces the machine's entry for that date; it never adds to it.
  - [ ] When the tracker extension is not installed, the publisher does nothing and logs nothing alarming.
- As the dashboard, I want a `manage_time` tool so that day records have a home.
  - [ ] `record` stores one machine's day at `users/{uid}/codingTime/{date}` in a `machines` map, re-summing totals across machines.
  - [ ] `latest` returns the newest day; `range` returns up to 90 days plus a summary.
  - [ ] Each `record` writes a `time.published` audit event.
  - [ ] Invalid payloads are rejected with the failing field named.
  - [ ] The per-machine extension agent keys are granted `manage_time`.
- As the builder, I want a coding line in the bulletin so that the daily read covers my time too.
  - [ ] The bulletin's usage section shows total, you, Claude, both and the top project for the day, when a record exists.

### Epic: WakaTime side by side

- As the builder, I want our totals next to WakaTime's for two weeks so that I can trust the switch.
  - [ ] At most once an hour (a once-a-day run compares before the day has anything tracked), if `wakatime-cli` is installed, the tracker runs it for today's total and logs it next to ours in `state/wakatime-compare.jsonl`. A command runs it on demand.
  - [ ] The panel shows WakaTime's total next to our editor time (you only plus both), with the difference.
  - [ ] Without `wakatime-cli`, the comparison is hidden and nothing errors.
  - [ ] A setting turns the comparison off (`sanduhrTime.compareWakaTime`, default on).

## What We're Building
Everything in the epics above: the tracker extension (recorder, resolver, Claude reader, merger, alias store, local store, public API, status bar, Time panel, WakaTime comparison), the usage spec and day schema, the publisher in the 626 Labs extension, `manage_time` on the dashboard server with its agent-key grants, and the bulletin coding line. Plus, first: the spike that proves a disk reload is distinguishable from typing in VS Code's change events.

## What We'd Add With More Time
- The dashboard Time page with weekly and monthly charts (phase 2).
- The WakaTime import from its data export, skipping days already covered (phase 2).
- Sanduhr reading the day files in its widget, then owning the merge (phase 3).
- A Wakapi-compatible heartbeat export.
- Goals and streaks, if they turn out to matter.
- Remote, WSL and Codespaces workspaces.

## Non-Goals
- **Other editors:** every WakaTime heartbeat on the reference machine came from VS Code.
- **Network calls from the tracker:** the 626 Labs extension owns the dashboard connection and keys.
- **Backfilling days before installation:** a day without your side recorded would misstate the split; older history comes from the phase 2 import.
- **Paths or file names off the machine:** hashing in the spool, nothing in publishes.
- **Team features and leaderboards:** one builder.

## Open Questions
- **Can a disk reload be told apart from typing?** The spike answers this before any recorder code. Leading signal: a change that leaves `document.isDirty` false. Fallback: the 2-second rule; final fallback: git working-tree changes not attributable to Claude. Must be answered first in build.
- **Does `wakatime-cli --today` print a parseable total offline?** Can wait for the comparison item.
- **Which machine label is published?** Default: the OS hostname, normalized (the same rule the origin tag uses). Can wait for the publisher item.
