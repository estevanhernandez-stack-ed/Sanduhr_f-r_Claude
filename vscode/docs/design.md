# Sanduhr Time: design

Approved 2026-10-06 in a brainstorming session. Input to the Vibe Cartographer cycle that builds it; scope, PRD, spec and checklist in this folder derive from this file.

## Purpose

Replace WakaTime for one builder who codes in VS Code alongside Claude Code. Keep what gets looked at (total time, time per project) and get the AI-versus-me split right. Grow into a Sanduhr feature later: Sanduhr already reads Claude Code transcripts and publishes usage per project, and this becomes its coding-time half.

Success: WakaTime can be uninstalled with nothing lost that was being looked at, and the AI split counts Claude Code work correctly.

Facts that shaped it:

- On the reference machine, every WakaTime heartbeat came from the VS Code plugin. A VS Code-only tracker loses nothing.
- WakaTime's own Claude parser had recorded zero sessions: it reads the default `~/.claude` home and misses `CLAUDE_CONFIG_DIR` homes. Its AI split was blind to Claude Code.
- Claude Code transcripts carry `timestamp`, `cwd` and `gitBranch` on entries, and every Edit/Write tool result carries a `structuredPatch` with exact added and removed lines plus `filePath`.

## Decisions

| Question | Ruling |
|---|---|
| What "AI versus me" measures | Both: time is the headline, lines are the detail |
| WakaTime history | Run side by side for two weeks, then import as a diff (skip days already covered) |
| Overlap with Sanduhr | Merged into one record per project per day; overlap is subtracted, never added |
| Where it lives | Standalone extension now (this folder); Sanduhr takes over the merge later. The usage spec is the contract that makes that a swap |
| Masking | Streamer-mode aliases in the tracker; the 626 publisher enforces the tenant wall |

## Phases

1. **This cycle.** Tracker extension, Time panel and status bar, local files and the usage spec, the 626 publisher in the 626 Labs VS Code extension, the `manage_time` dashboard tool, the bulletin coding line, and the WakaTime side-by-side comparison.
2. **Next.** The dashboard Time page (weekly charts) and the WakaTime import.
3. **Later.** Sanduhr reads the day files, then owns the merge.

## Architecture

The tracker has no 626 code and makes no network calls. The 626 Labs extension reads its public API and publishes.

| Unit | Job |
|---|---|
| Heartbeat recorder | Editor events while the window is focused (typing, save, active-file change, cursor movement) become heartbeats: time, kind, project key, language, lines added and removed, salted hash of the relative file path. At most one non-edit heartbeat per file every 2 minutes; every edit records its line delta. |
| Project resolver | Workspace folder → git remote (normalized) → project key. No remote: the folder path. Worktrees share their main repo's remote, so they fold into one project. |
| Claude reader | Scans every config home: `CLAUDE_CONFIG_DIR` plus every `~/.claude*` directory with a `projects/` folder, skipping `*.config-backup-*`. Reads `timestamp`, `cwd`, entry type (user prompt, assistant, tool result), and Edit/Write `structuredPatch` line counts with `filePath`. Subagent transcripts count. Reads incrementally: remembers a byte offset per transcript file. An unreadable line is skipped and counted, never fatal. |
| Merger | Pure function: heartbeats plus Claude events in, one day record out. Rules below. |
| Alias store | Persistent, rerollable aliases from a pool of about 100 names, per project key. Real-name map is local only. |
| Local store | Spool, day files, alias map, reader offsets, under the data directory. |
| Public API | `getDays(from, to)`, `getToday()`, `onDayUpdated`. Returned records carry real names and the masked flag; consumers decide what to show or send. |
| Time panel and status bar | Today's total in the status bar (hover: you, Claude, both). The panel: today and last 7 days, per project, stacked you/Claude/both bars, lines, top languages, a streamer-mode toggle, per-project mask and reroll. |
| WakaTime comparison | During the side-by-side weeks: once a day, runs the local `wakatime-cli --today` if installed, logs its total next to ours, and the panel shows both. Fair comparison: WakaTime's total against our editor time (you only plus both). |

## Measurement rules

**Your activity:** editor heartbeats, plus your prompts to Claude from the transcripts (user entries that are not tool results). Activity joins into intervals when events are up to 15 minutes apart (setting); a lone event counts 2 minutes.

**Claude's activity:** assistant and tool entries in a session, subagents included, joined at a 5 minute gap.

**Parallel sessions:** the split uses the union of Claude's intervals. Agent-minutes separately sums every session's intervals, so parallelism stays visible.

**Per project per day:** both = intersection of your intervals and Claude's; you only and Claude only are the remainders; project total = union. **Day total** = union across projects.

**Lines:** Claude's = added + removed from `structuredPatch`, per file, mapped to the project. Yours = editor content changes, minus any change on the same file within 2 seconds of a Claude edit to it (VS Code reloading the file from disk). Total = yours + Claude's.

**Project of a Claude event:** the entry's `cwd`, through the same resolver.

**Day:** local calendar day. Timestamps stored in UTC.

**Risk, first task:** prove that a disk reload of an open file is distinguishable from typing in VS Code's change events. If it is not, your lines fall back to git: working-tree changes not attributable to Claude.

## Data and the usage spec

Data directory: `~/.sanduhr/time/`, overridable with `SANDUHR_TIME_DIR`. Not under AppData, because a packaged Sanduhr build may see a redirected AppData; the user profile is shared and the path works on macOS.

| File | Contents |
|---|---|
| `spool/<date>.<machine>.<window>.jsonl` | Append-only heartbeats, one file per VS Code window. Kept 90 days. |
| `days/<date>.json` | Merged day record: per project (key, display name, masked, alias), you/Claude/both/total milliseconds, agent-minutes, time by language, your lines and Claude's; day totals; caveats (unreadable transcript lines, reader version). Kept forever. |
| `aliases.json` | Project key → real name, alias, masked flag, set time. The only real-to-alias map. |
| `state/offsets.json` | Transcript byte offsets for incremental reads. |

Project key is a hash of the normalized git remote, so day files never name repos on their own.

`docs/time-usage-spec.md` (versioned, v1) defines every field, with a JSON schema at `docs/time-day.schema.json`. Tests validate against the schema; Sanduhr validates against the same file later.

## Masking and the tenant wall

- **The tracker:** streamer mode (mask everything) and per-project mask, with persistent, rerollable aliases. Sanduhr reuses the same alias store and pool so both show the same alias.
- **The 626 publisher:** a real project name leaves the machine only if the project is bound to a 626 dashboard project and not masked. Everything else is published under its alias. Employer repos are never bound, so they can only leave as aliases.
- **Published fields:** date, machine label, totals, and per project: display name, 626 project id when bound, the time split, lines, top languages. Never file hashes, never the alias map.
- **Fail closed:** if the alias store cannot be read, the publisher sends nothing that day.

## The 626 side

- **`manage_time`** MCP tool mirroring `manage_usage`: `record`, `latest`, `range`. Stored at `users/{uid}/codingTime/{date}` with a `machines` map: each machine's record replaces its own entry, totals re-sum. Each `record` writes a `time.published` audit event. The per-machine extension keys get `manage_time` added to their scope.
- **Publisher** in the 626 Labs VS Code extension: reads the tracker API, applies the wall, publishes the current day every 15 minutes while active and the previous day once after midnight. A retry replaces, never adds.
- **Bulletin:** a coding line next to Claude usage: total, you, Claude, both, top project.

## Failure modes

- Offline or dashboard down: everything is local first; the publisher retries.
- Transcript format change: per-line soft failure, counted in the day's caveats with the Claude Code version.
- Several windows: one spool file per window; the merge runs under a lock file every 5 minutes and on window close; day files are written to a temp file and renamed.
- Large transcript sets (gigabytes): incremental reads by byte offset.
- Missing `wakatime-cli`: the comparison simply does not run.

## Privacy

- The tracker makes zero network calls; the only process it runs is the local `wakatime-cli --today` during the comparison weeks.
- No file path leaves the machine; spool entries hash them.
- Every surface that names Claude carries the trademark disclaimer used across Sanduhr.

## Testing

- Merger: pure-function tests for overlap union, parallel sessions, gap rules, reload subtraction, project switching, day boundaries.
- Claude reader: fixture transcripts with malformed lines, subagent files, multiple homes, incremental offsets.
- Resolver: worktrees, no remote, remote URL variants.
- Alias store: persistence, reroll, uniqueness, fail-closed.
- Schema conformance for day files.
- Extension: tests against a `vscode` stub, following the 626 Labs extension's pattern.
- Server: `manage_time` tests mirroring `manage_usage`'s; bulletin coding-line tests.

## Rollout

Build `.vsix` packages and install them on each machine by hand, like the 626 Labs extension. The 626 Labs extension gets a minor version for the publisher. Marketplace listing waits for the Sanduhr feature boost.
