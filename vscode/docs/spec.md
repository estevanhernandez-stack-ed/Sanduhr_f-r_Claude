# Sanduhr Time — Technical Spec

Implements `prd.md`. Architecture source: `design.md`. Conventions match the 626 Labs VS Code extension (TypeScript, webpack, Vitest with a `vscode` stub) and the Sanduhr keystone (`../CLAUDE.md`).

## Stack

| Piece | Choice | Why |
|---|---|---|
| Language | TypeScript ~5.8, `strict` | Same as the 626 Labs extension |
| Bundler | webpack 5 + ts-loader, one node bundle `dist/extension.js`, `vscode` external | Same build; one entry, no webview bundle |
| Tests | Vitest 3 with a `vscode` stub aliased in `vitest.config.ts` | Same pattern; the merger and reader are pure and need no host |
| Lint | ESLint 9 flat config + typescript-eslint | Same |
| Panel UI | Plain HTML/CSS/TS in a `WebviewPanel`, VS Code theme CSS variables, no framework | A read-only dashboard; React would triple the bundle for no gain |
| Runtime deps | None | Node stdlib only (`fs`, `crypto`; `child_process` solely for `wakatime-cli`). Git remotes are read from git's config files, with no `git` process |

Docs: [VS Code API](https://code.visualstudio.com/api/references/vscode-api), [Webview guide](https://code.visualstudio.com/api/extension-guides/webview), [Extension anatomy and exports](https://code.visualstudio.com/api/get-started/extension-anatomy), [WakaTime heartbeat fields](https://wakatime.com/developers), [vsce](https://github.com/microsoft/vscode-vsce).

## Runtime & Deployment

- VS Code `^1.80.0` desktop, `extensionKind: ["ui"]` so the extension runs on the local machine where the transcripts live (remote workspaces are a non-goal; under a remote window it records nothing and says so in the panel).
- Activation: `onStartupFinished`. Activation must not wait on the first transcript catch-up; the reader runs in the background.
- Publisher id `626labs`, extension name `sanduhr-time`, display name "Sanduhr Time".
- Deployment target: a hand-installed `.vsix` per machine (`npx @vscode/vsce@4.0.0 package --no-dependencies`), no Marketplace this cycle, so no signing drill-down.
- The 626 Labs extension (Project-626Labs monorepo) gets a minor version bump for the publisher; the `mcp` Cloud Function gets `manage_time`; the bulletin function gets the coding line. Those deploy through that repo's CI on merge to `main`.

## Architecture Overview

```
 VS Code editor events ──► Heartbeat recorder ──► spool/<date>.<machine>.<window>.jsonl
                                  │ project key
                           Project resolver ◄── transcript cwd
                                  │
 ~/.claude*/projects/**/*.jsonl ─► Claude reader ──► Claude events (prompts, activity, edits)
                                  │                    (offsets in state/offsets.json)
                                  ▼
                 Merge runner (lock, every 5 min + window close)
                     reads spool + Claude events + aliases
                                  │ pure merge()
                                  ▼
                        days/<date>.json  ──► Public API (getToday / getDays / onDayUpdated)
                                  │                    │
                     Status bar + Time panel     626 Labs extension publisher
                                                       │ wall: real name only if bound + unmasked
                                                       ▼
                                              manage_time (mcp Cloud Function)
                                                       │
                                              users/{uid}/codingTime/{date} ──► bulletin coding line
 wakatime-cli --today ──► WakaTime comparison ──► state/wakatime-compare.jsonl ──► Time panel
```

## Spike: reload detection

The first build item; nothing in the recorder's line counting is built until it is answered. PRD ref: `prd.md > The merged day` (your lines), Open Questions.

- **Question:** when Claude Code writes a file that is open and saved in VS Code, can the extension tell the resulting `onDidChangeTextDocument` from typing?
- **Probe:** a throwaway dev-host extension (`spike/`, not shipped) logs every change event on a scratch file: `document.isDirty` after the event, `reason`, `contentChanges` count and sizes, timing relative to `workspace.createFileSystemWatcher` and `onDidSaveTextDocument`. Cases: typing; Claude Code (CLI) editing the open, saved file; Claude editing an open, dirty file; **the Claude Code VS Code extension's diff view, accepted** (this path may look exactly like typing); external edit by a script; undo to saved state; format-on-save.
- **Run in a separate Extension Development Host window** on a scratch folder under the OS temp directory; never in the builder's working VS Code windows. `docs/spike-reload.md` records event shapes only, never real paths or file contents.
- **Pass:** the reload case is identifiable from event data alone with no false positives on typing. **Result: pass with a two-event rule** (see Heartbeat recorder); the expected single-event `isDirty === false` signal gave false positives on the first edit after a save.
- **Outcomes:** pass → the recorder tags such changes `reload` and never counts them as yours. Partial → combine with the 2-second rule against Claude edits for the same file. Fail → your lines come from git working-tree diffs minus Claude's `structuredPatch` lines, computed in the merge.
- Results go in `docs/spike-reload.md`; the spike folder is deleted after.

## Tracker extension

### Heartbeat recorder (`src/recorder.ts`)
PRD ref: `prd.md > Your time`.
- Subscribes to `onDidChangeTextDocument`, `onDidSaveTextDocument`, `onDidChangeActiveTextEditor`, `onDidChangeTextEditorSelection` and `window.onDidChangeWindowState`. Records only while `window.state.focused`.
- Ignores non-file schemes except `untitled` (`(no project)` bucket); ignores output, git and other virtual documents.
- Heartbeat kinds: `edit` (line delta from `contentChanges`, counting inserted and removed lines), `save`, `focus` (active editor change), `nav` (selection change). Non-edit kinds throttle to one per file per 2 minutes; `edit` debounces into one heartbeat per file per 10 seconds with summed deltas.
- **Reload rule (from `docs/spike-reload.md`, verified on VS Code 1.80 and 1.140):** a change event with no content changes is a dirty-flag flip and counts no lines. `reason` Undo or Redo is never a reload. `isDirty === true` read synchronously in the handler is your edit. Otherwise the event is a candidate: if the next event for that document is an empty change with `isDirty === true` at the same `document.version`, it was your first edit after a clean state; any other next event, or 500 ms with none, makes it a reload. The single-event signal (`isDirty === false`) alone is wrong, because the first keystroke after every save also reports false.
- Changes identified as reloads are written with `reload: true` and zero `human_line_changes`, and never count as your activity (the merger skips them for intervals). The merger's 2-second rule against Claude edits stays as the backstop for the untested Claude Code VS Code extension diff-accept path.
- Writes through the spool writer. Never blocks the event loop on disk: an in-memory buffer flushes every 5 seconds and on deactivate.

### Spool writer (`src/store/spool.ts`)
PRD ref: `prd.md > Local data and the usage spec`.
- One file per window: `spool/<YYYY-MM-DD>.<machine>.<windowId>.jsonl`, where the date is the **local** date of the heartbeat, `machine` is the normalized hostname, and `windowId` = a short hash of `vscode.env.sessionId`. Append-only, one JSON object per line.
- Heartbeat line (WakaTime-compatible names where they overlap):
  `{ "v":1, "time": <unix seconds float>, "entity": hashPath(<absolute path>), "type":"file", "category":"coding", "kind":"edit|save|focus|nav", "project": <project key>, "branch": <git branch or null>, "language": <languageId>, "lines": <document line count>, "human_line_changes": <int>, "is_write": <bool>, "reload": <bool> }`
- `ai_line_changes` is never written in the spool: Claude's lines come from the Claude spool, never from editor events.
- **`hashPath(abs)`** (`src/store/hash.ts`) is the one path hash both sides use: resolve, forward slashes, lowercase the drive letter and case-fold the whole path on win32, then `sha256(salt + path)`, first 24 hex chars. The salt is per install (`state/installed.json`).
- Prune: spool files older than 90 days are deleted at activation.
- The merge reads only spool files whose machine segment equals this machine, so a synced folder from another machine is never counted twice.

### Project resolver (`src/project.ts`)
PRD ref: `prd.md > Projects and masking`.
- `resolve(fsPath) → { key, realName, root }`, cached per root.
- Root: the workspace folder containing the file, or for a transcript `cwd`, the nearest ancestor containing `.git` (file or directory).
- Remote: read `remote "origin"` `url` from the repo's git config file directly, with no `git` process. For a worktree, `.git` is a file containing `gitdir: <path>`; that directory's `commondir` file points at the main repo's git directory, whose `config` holds the remote. Branch: the `HEAD` file of the worktree's git directory.
- Normalization (`normalizeRemote`, exported and specified in the usage spec so the publisher applies the identical function): strip credentials, a trailing `.git` and slashes; convert `git@host:owner/repo` to `https://host/owner/repo`; lowercase the whole URL. This matches the 626 server's `normalizeRepoUrl`.
- Key (`projectKey(remote | root)`, exported): first 16 hex chars of sha256(normalized remote), or of `path:<case-folded root>` when there is no remote. Real name: the repo basename, or the folder name.
- Results are cached in memory per root.

### Claude reader (`src/claude/reader.ts`, `src/claude/homes.ts`)
PRD ref: `prd.md > Claude's work`.
- `homes.ts`: `CLAUDE_CONFIG_DIR` (if set) plus every directory in the home folder matching `.claude*` that contains `projects/`, excluding names containing `.config-backup-`; de-duplicated by real path.
- `reader.ts`: walks `projects/*/` for `*.jsonl` and `*/subagents/**/*.jsonl`; reads from the stored byte offset to EOF, splitting on newlines and keeping a trailing partial line for next time.
- Per line, extracts only: `timestamp`, `cwd`, `sessionId`, `agentId`, `isSidechain`, `isMeta`, `isCompactSummary`, `type`, `origin.kind`, whether a `user` entry's content is a `tool_result`, and `toolUseResult` (`type`, `filePath`, `structuredPatch`, `content` length in lines).
- **A prompt (your activity)** is a `user` entry with text content (not a `tool_result`), `isSidechain !== true`, `!isMeta`, `!isCompactSummary`, and `origin.kind === "human"` when `origin` is present. Everything else is excluded from your side: subagent task prompts (sidechain), `task-notification` and `peer` origins, meta entries, compact summaries. Measured on a real session: 119 of 285 text-type user entries were human.
- **Claude activity** is every `assistant` entry and every `user` entry carrying a `tool_result`, main and sidechain.
- **Claude's lines:** for each `toolUseResult` with a `filePath`: if `type === "create"`, added = the line count of `content`, removed = 0; otherwise added = the number of `structuredPatch[].lines[]` starting with `+`, removed = the number starting with `-`. Hunk `oldLines`/`newLines` are never used; they include context lines.
- **Stream id** for agent-minutes: the transcript file (main session or one subagent file), so parallel subagents stay separate even though they share the parent's `sessionId`.
- Emits `ClaudeEvent { time, date (local), project, stream, kind: 'prompt'|'activity'|'edit', linesAdded?, linesRemoved?, entity? }`, where `entity = hashPath(filePath)`. The prompt kind feeds your activity; the others feed Claude's.
- **Claude spool:** events are appended to `claude/<local date>.jsonl` before the file's offset advances (spool first, offsets second, both under the merge lock), so a crash re-reads rather than drops, and every window's merge sees every event. The merge reads the Claude spool, never the reader's memory. Re-reads after a crash can duplicate lines; events carry the transcript entry `uuid` (or file plus byte offset) and the merge de-duplicates on it.
- **First run:** transcript files whose mtime is before `installedAt` start at their current size; newer files start at zero and drop events older than `installedAt`. No gigabyte catch-up at install.
- **Bounded work:** each tick reads at most 8 MB across files, yielding between files; the rest continues next tick.
- Counts unreadable lines per day for caveats.
- Offsets persist in `state/offsets.json` keyed by real path, with file size and mtime; a file that shrank or was replaced restarts from zero.

### Merger (`src/merge.ts`)
PRD ref: `prd.md > The merged day`.
- Pure: `merge(date, heartbeats, claudeEvents, aliases, settings) → DayRecord`. No I/O.
- Inputs: the date's heartbeats and Claude events plus the previous day's last hour, so intervals that start before midnight join correctly before clipping.
- Intervals: sort events per side per project; consecutive events join when the gap ≤ `idleMinutes` (you, default 15) or ≤ 5 (Claude) and the interval spans first to last event. **Only a lone event** (an interval with a single event) gets a 2-minute duration. A **prompt** contributes the span [t − 2 min, t], the time spent composing it, and joins your intervals like any event. Clip to the local day.
- Per project: your union U_p, Claude union C_p (all streams), both = |U_p ∩ C_p|, you only = |U_p − C_p|, Claude only = |C_p − U_p|, total = |U_p ∪ C_p|. Agent-minutes = Σ over streams of each stream's own interval lengths.
- **Day totals pool all projects:** U_all = ∪ U_p, C_all = ∪ C_p; youMs = |U_all − C_all|, claudeMs = |C_all − U_all|, bothMs = |U_all ∩ C_all|, totalMs = |U_all ∪ C_all|. Per-project totals can sum to more than the day total (editing in project A while Claude works in project B is both at day level and one-sided per project); a test pins this.
- Lines: Claude = Σ added + removed from the Claude spool's edit events; yours = Σ `human_line_changes` excluding `reload` heartbeats and, per the spike outcome, edits whose `entity` matches a Claude edit's `entity` within 2 seconds.
- Languages: time in your editor intervals, attributed to the language of the heartbeat that opened or extended each stretch; prompt spans carry no language and are left out of the language breakdown.
- Output follows the day schema below.

### Merge runner (`src/runner.ts`)
- Every 5 minutes and on demand from the panel: take `state/merge.lock`, run one bounded reader pass, then merge every date in the **merge set** (today, yesterday, and every local date that gained events this pass), write `days/<date>.json.tmp`, rename, release the lock, fire `onDayUpdated` for each date written.
- **Lock:** created exclusively, containing `{ pid, at }`, refreshed every 30 seconds while held. It is stale only when `at` is over 2 minutes old **and** that pid is no longer running. If the lock is live, skip this tick.
- **Window close** (`deactivate`) only flushes this window's spool buffer; no reader pass, no merge, so it fits deactivate's time budget.

### Alias store (`src/store/aliases.ts`, `src/store/alias-pool.ts`)
PRD ref: `prd.md > Projects and masking`.
- `aliases.json`: `{ "v":1, "streamerMode": bool, "projects": { <key>: { "realName", "alias", "masked", "setAt" } } }`.
- Pool: about 100 neutral two-word names ("Project Kestrel", "Project Basalt"); assignment picks an unused pool name; when exhausted, append a number.
- `aliasFor(key)` assigns lazily; `reroll(key)`; `setMasked(key, bool)`; `setStreamerMode(bool)`. Every write happens under the merge lock (UI actions take the lock briefly, retrying for up to 2 seconds) and goes through tmp + rename, so concurrent windows never lose each other's assignments.
- A missing `aliases.json` is treated as a fresh store **only** when `state/installed.json` is also being created in this activation; otherwise it, like an unparseable file, throws a typed `AliasStoreUnavailable`. Callers that publish fail closed.
- **Streamer mode masks every project for publishing too**, not only in the panel.

### Public API (`src/api.ts`)
PRD ref: `prd.md > Local data and the usage spec`.
- `activate` returns `{ version: 1, getToday(): Promise<DayRecord|undefined>, getDays(from, to): Promise<DayRecord[]>, onDayUpdated: vscode.Event<DayRecord>, maskState(): MaskState, projectKey(remoteOrRoot: string): string, normalizeRemote(url: string): string }`.
- `maskState()` reads `aliases.json` live and returns `{ readable: boolean, streamerMode: boolean, projects: { [key]: { alias, masked } } }`; `readable: false` when the store throws `AliasStoreUnavailable`. The publisher uses it at publish time, never the possibly stale mask flags in a day file.

### Status bar (`src/ui/statusBar.ts`)
PRD ref: `prd.md > Seeing it`.
- Right-aligned item `$(watch) 3h 12m`; tooltip "You 1h 40m · Claude 2h 05m · Both 33m"; updates on `onDayUpdated` and every 5 minutes; click runs `sanduhrTime.openPanel`.
- Under a remote window: `$(watch) off` with tooltip "Local workspaces only".

### Time panel (`src/ui/panel.ts`, `media/panel.css`, `media/panel.js`)
PRD ref: `prd.md > Seeing it`, `prd.md > WakaTime side by side`.
- `WebviewPanel` in the active column, `retainContextWhenHidden: false`, strict CSP (`default-src 'none'`, nonce scripts, `vscode-resource` styles).
- Sections: today headline (total and the split), a 7-day strip of daily totals, per-project rows (stacked bar in three accent colors from the theme's chart variables, total, agent-minutes, lines, top three languages, mask and reroll buttons), the streamer toggle, the WakaTime comparison line, the caveats note, and the footer disclaimer: "Claude is a trademark of Anthropic. Sanduhr Time is not affiliated with or endorsed by Anthropic."
- Messages from the webview: `toggleStreamer`, `mask {key, masked}`, `reroll {key}`, `refresh`. The extension re-renders from the latest day records; no state lives in the webview.
- Empty first day: "Tracking started. Your first numbers appear after a few minutes of work."

### WakaTime comparison (`src/compare/wakatime.ts`)
PRD ref: `prd.md > WakaTime side by side`.
- Locate `wakatime-cli` in `~/.wakatime/` (platform-specific file name) or on PATH. If missing, the feature is hidden.
- Once per day after the first merge, run `wakatime-cli --today --output raw-json` (fall back to plain `--today` and parse `Xh Ym`) with a 15-second timeout; append `{ date, wakatimeSeconds, oursEditorSeconds, at }` to `state/wakatime-compare.jsonl`.
- Setting `sanduhrTime.compareWakaTime` (default true).

### Settings and commands (`package.json` contributes)
- Settings: `sanduhrTime.idleMinutes` (number, 15), `sanduhrTime.compareWakaTime` (bool, true), `sanduhrTime.dataDir` (string, empty = default or `SANDUHR_TIME_DIR`).
- Commands: `sanduhrTime.openPanel`, `sanduhrTime.toggleStreamerMode`, `sanduhrTime.mergeNow`, `sanduhrTime.revealData`.

### Public-repo hygiene
The Sanduhr repo is public, and transcripts carry real paths, repo names and branches, including employer repos.
- Every test fixture is synthetic: paths under `C:\fixture\` or `/fixture/`, invented project names, no lines copied from real transcripts or spools.
- `scripts/check-public.mjs` greps `vscode/**` (excluding `node_modules`, `dist`) for the user-profile path pattern (`C:\Users\<name>`, `/Users/<name>`), `.claude-` home names other than in documentation of the discovery rule, and a denylist file kept outside the repo (`$SANDUHR_TIME_DENYLIST`, optional locally, skipped in CI when unset). CI runs the profile-path check; the pre-commit routine for this folder runs the full check.
- `docs/spike-reload.md` and `process-notes.md` record shapes and outcomes, never real paths, file contents or repo names.

## The 626 side

### Publisher (`Project-626Labs-1/vscode-extension/src/timePublisher.ts`)
PRD ref: `prd.md > Publishing to the 626 dashboard`.
- On activate, `vscode.extensions.getExtension('626labs.sanduhr-time')`; absent or API `version` ≠ 1 → do nothing.
- Every 15 minutes while connected, publish today (debounced on `onDayUpdated`). At activation and after local midnight, publish every day from the last 7 whose `generatedAt` is newer than its last successful publish (tracked in the extension's `globalState`), so a weekend with VS Code closed still reaches the dashboard.
- **Bindings:** one `manage_projects list` call per session (refreshed hourly). For each project with a `githubRepo.url`, compute `projectKey(normalizeRemote(url))` through the tracker's API, which yields the set of bound keys with their 626 project ids. Matching is by key only, never by name.
- **Naming rule per project:** the real name only when the key is bound, `maskState().streamerMode` is false, and the project is not masked. Otherwise the alias. **Any failure means alias:** the list call fails or is denied, the API is missing a function, the key is not found.
- If `maskState().readable` is false, publish nothing and log one line.
- The per-machine extension keys must allow `manage_projects` (the extension already calls `findByRepo` for its project binding) and `manage_time`; a denied call behaves as "no bindings", so everything goes out as aliases.
- Payload: `{ action:'record', date, machine: os.hostname() normalized, source:'sanduhr-time', totals:{ youMs, claudeMs, bothMs, totalMs, agentMs, linesYou, linesClaude }, byProject:[{ name, projectId?, youMs, claudeMs, bothMs, totalMs, agentMs, linesYou, linesClaude, languages:[{ id, ms }] (max 5) }] (max 50) }`.
- Pure helper `buildTimePayload(day, bindings)` holds the wall logic and is what the tests pin.

### `manage_time` tool (`Project-626Labs-1/mcp-server/src/tools/time.ts`)
PRD ref: `prd.md > Publishing to the 626 dashboard`.
- Mirrors `tools/usage.ts` on `main` (read it from `origin/main`; a stale local checkout may lack it): zod `RecordSchema`, `latest`, `range` (`days` 1..90, default 7), Central-date validation, a transaction that swaps the machine's entry in `machines` and re-sums totals, `auditLog` `time.published`. Machine keys are stored the way `usage.ts` stores them (whole-object `tx.set`, so dots in hostnames are safe); never written through dotted `update()` field paths.
- Registered in `index.ts` beside `registerUsageTools`; read-only annotation false; added to the registry name test; `vars:resolve` rerun if the tool count is templated into docs.
- Doc: `users/{uid}/codingTime/{date}` = `{ date, machines: { <machine>: entry }, totals, updatedAt }`.

### Agent key grants
- `manage_agents update` adds `manage_time` to the per-machine VS Code extension keys' `allowedTools`. Done after the tool deploys.

### Bulletin coding line (`Project-626Labs-1/functions/src/domains/bulletin/`)
PRD ref: `prd.md > Publishing to the 626 dashboard`.
- `collect.ts`: read `users/{uid}/codingTime/{windowDate}` into `facts.codingTime`, fenced like the other side panels.
- `compose.ts`: one row in the usage section, "6h 20m coding · you 3h 10m · Claude 4h 40m · both 1h 30m · top: <project> 2h 05m", only when the doc exists.

## Data Model

### Day record (`days/<date>.json`, `docs/time-day.schema.json`)
```
{
  "v": 1,
  "date": "YYYY-MM-DD",
  "machine": "<hostname>",
  "generatedAt": "<ISO>",
  "totals": { "youMs", "claudeMs", "bothMs", "totalMs", "agentMs", "linesYou", "linesClaude" },
  "projects": [ {
    "key", "realName", "alias", "masked",
    "youMs", "claudeMs", "bothMs", "totalMs", "agentMs",
    "linesYou", "linesClaude",
    "languages": [ { "id", "ms" } ]
  } ],
  "caveats": { "unreadableTranscriptLines": int, "readerVersion": "<semver>", "claudeCodeVersions": [string] }
}
```
`youMs` = you only, `claudeMs` = Claude only, `bothMs` = overlap; `totalMs` = their sum (equal to the union). Real names stay in local day files; the publisher strips them per the wall.

### Claude spool (`claude/<local date>.jsonl`)
One `ClaudeEvent` per line: `{ "v":1, "id": <transcript entry uuid, or file hash + byte offset>, "time", "project", "stream", "kind", "linesAdded"?, "linesRemoved"?, "entity"? }`. Kept 90 days, like the editor spool.

### State files
- `state/installed.json` `{ "installedAt": ISO, "salt": hex }`.
- `state/offsets.json` `{ <path>: { "offset", "size", "mtimeMs" } }`.
- `state/merge.lock` `{ "pid", "at" }`, `state/caveats.json` `{ <date>: { "versions": [..], "unreadable": n } }`, `state/wakatime-compare.jsonl`.

### Firestore `codingTime/{date}` (626 side)
Same shape as `usage/{date}`: `machines` map of publisher payload entries plus `recordedAt`, top-level `totals` summed across machines, `updatedAt`.

## File Structure

```
Sanduhr/vscode/
├── package.json             # extension manifest: contributes settings, commands; extensionKind ui
├── tsconfig.json            # strict, ES2020, commonjs (matches the 626 Labs extension)
├── webpack.config.js        # single node bundle, vscode external
├── vitest.config.ts         # vscode stub alias
├── eslint.config.mjs
├── .vscodeignore
├── README.md                # what it tracks, privacy, data location, disclaimer
├── CHANGELOG.md
├── media/
│   ├── panel.css            # theme variables, accent bars
│   ├── panel.js             # renders state posted by the extension
│   └── icon.png
├── src/
│   ├── extension.ts         # activate: wiring, returns the public API
│   ├── api.ts               # public API types and factory
│   ├── recorder.ts          # editor events → heartbeats
│   ├── project.ts           # folder/cwd → project key, real name
│   ├── merge.ts             # pure merge into a DayRecord
│   ├── intervals.ts         # interval join, union, intersect, subtract, clip
│   ├── runner.ts            # lock, catch-up, merge, write, notify
│   ├── types.ts             # Heartbeat, ClaudeEvent, DayRecord
│   ├── claude/
│   │   ├── homes.ts         # discover config homes
│   │   └── reader.ts        # incremental transcript reader
│   ├── store/
│   │   ├── paths.ts         # data dir resolution
│   │   ├── hash.ts          # hashPath: the one normalized, salted path hash
│   │   ├── spool.ts         # buffered append, prune
│   │   ├── claudeSpool.ts   # Claude event spool, de-duplicated reads
│   │   ├── days.ts          # atomic day writes, reads
│   │   ├── state.ts         # installed, offsets, lock
│   │   ├── aliases.ts       # alias store
│   │   └── alias-pool.ts    # name pool
│   ├── compare/
│   │   └── wakatime.ts      # wakatime-cli comparison
│   ├── ui/
│   │   ├── statusBar.ts
│   │   └── panel.ts         # webview panel, CSP, messages
│   ├── test/
│   │   ├── vscodeStub.ts
│   │   └── fixtures/        # synthetic transcript and spool fixtures only (see Public-repo hygiene)
│   └── *.test.ts            # beside each unit
├── scripts/
│   └── check-public.mjs     # public-repo hygiene gate
├── docs/
│   ├── design.md, builder-profile.md, scope.md, prd.md, spec.md, checklist.md
│   ├── spike-reload.md      # spike results
│   ├── time-usage-spec.md   # the usage spec, v1
│   └── time-day.schema.json # JSON schema for day files
└── process-notes.md
```

Plus in the Sanduhr repo: `.github/workflows/vscode-ci.yml` (paths `vscode/**`: install, typecheck, lint, test, package). In Project-626Labs-1: `vscode-extension/src/timePublisher.ts` (+ test), `mcp-server/src/tools/time.ts` (+ test), registration in `mcp-server/src/index.ts`, bulletin `collect.ts` and `compose.ts` changes (+ tests).

## Key Technical Decisions

1. **Standalone tracker, publisher in the 626 extension.** The tracker stays network-free and liftable into Sanduhr; the 626 extension already holds the connection and the per-machine keys. Tradeoff: two extensions to install, and a second, narrow transcript reader beside Sanduhr's.
2. **WakaTime field names in the spool.** Free compatibility for the phase 2 import and a possible Wakapi export. Tradeoff: a few names (`entity`, `human_line_changes`) read oddly next to our own fields.
3. **Data in `~/.sanduhr/time/`, not AppData.** A packaged Sanduhr may see a redirected AppData; the user profile is shared and the path works on macOS. Tradeoff: one more dot-folder in the home directory.
4. **No backfill before installation.** Keeps every recorded day's split honest. Tradeoff: history starts on install day until the phase 2 import.
5. **No UI framework in the panel.** A read-only dashboard; plain HTML keeps the bundle small and the CSP simple.

## Dependencies & External Services
- **VS Code API** `^1.80.0`: [reference](https://code.visualstudio.com/api/references/vscode-api).
- **wakatime-cli** (optional, side-by-side weeks only): [docs](https://wakatime.com/developers).
- **626 Labs dashboard** `mcp` Cloud Function (`manage_time`) and the bulletin function; reached only by the 626 Labs extension, authenticated with its per-machine agent key.
- **vsce** 4.0.0 for packaging, the version the 626 Labs extension CI uses.

## Open Issues
- **Reload detection:** answered by the spike, first.
- **`wakatime-cli` flags:** whether `--output raw-json` is supported with `--today` is verified when that item is built; the plain-text fallback covers it.
- **Machine label:** `os.hostname()` lowercased; the dashboard shows it per machine. It names the machine on the 626 dashboard (private), never in public files.
- **Self-review:** an adversarial review found 2 blockers and 10 majors, all applied; see `process-notes.md > /spec`.
