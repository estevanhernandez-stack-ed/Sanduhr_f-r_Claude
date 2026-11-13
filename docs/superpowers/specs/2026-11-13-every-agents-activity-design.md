# Every agent's activity: design

Windows, 5.0 spec 3 of 4 (the frame is in `2026-10-28-drawers-design.md`). Brainstormed with the owner on
2026-11-13. It takes over spec 2's stage 2 (Codex local activity) and adds Antigravity. Decisions are queued in
`docs/decisions/_pending-dashboard-log.md` until the dashboard is connected again.

## The goal

Someone who works across more than one coding agent sees where their week went, by project, across all of
them. Sanduhr already shows Claude Code's local burn; this spec adds Codex and Antigravity to the same
ledger, the same Vault and the widget's footer, read from each agent's own files on this PC.

**Success:** someone with Codex and Antigravity history switches both on in Sources. Within two refreshes the
widget's footer shows each agent's new tokens beside Claude Code's, and the Local burn ledger shows each project's 30 days
with bars split by agent and a filter that narrows the session list to one agent. No sign-in, no network
call, and no prompt or reply text is read.

## Out of scope

- **Local models** (Ollama, LM Studio). Their logs carry token counts but nothing that ties a request to a
  project. Revisit if either one starts logging the caller or working directory.
- **Antigravity limits.** They are server-only (ADR-006 note below). Antigravity is activity-only.
- **The Gemini-app limits meter** (web sign-in to `gemini.google.com/usage`). Proven feasible on 2026-11-13,
  but it is a separate pool from Antigravity and the only piece with network and privacy cost. Parked.
- **Live agent burn in the MCP.** The MCP reads agent rows from the Vault; see section 5.
- **The Antigravity IDE's token use.** The IDE keeps its conversations as undocumented protobuf
  (`conversations/*.pb`) and its transcripts carry no token fields; only the Antigravity CLI writes readable
  tokens. Decoding the protobuf was rejected as fragile (owner's call, 2026-11-13). Revisit if the IDE starts
  writing token fields to `transcript.jsonl`.
- **The Mac.** The Mac app has its own Vault; a Mac task follows this spec.

## 1. What was checked (2026-11-13, key shapes only, no content)

- **Codex.** On this machine: 15 plain rollouts and 65 `rollout-*.jsonl.zst`, so compressed history is the
  common case. A `token_count` event is `event_msg` with
  `payload.info.total_token_usage` and `payload.info.last_token_usage`, each holding `input_tokens`,
  `cached_input_tokens`, `cache_write_input_tokens`, `output_tokens`, `reasoning_output_tokens` and
  `total_tokens`. `info` and `rate_limits` can be null. `turn_context` carries `model` and `cwd`;
  `session_meta` carries `session_id`, `cwd` and `thread_source`. Sanduhr Time's Codex reader
  (`vscode/src/codex/reader.ts`) emits activity events only and never reads tokens, so token counting is
  new work. Its discovery, segment and `.zst` rules carry over.
- **Antigravity.** `~/.gemini/antigravity-cli/conversation_summaries.db` is ordinary SQLite, one row per
  conversation in `conversation_summaries` (71 rows here), with `conversation_id`, `workspace_uris` (a JSON
  array of `file:` URIs, empty for one row) and `last_modified_time` among others. Per-turn tokens are in
  `brain/<conversation_id>/.system_generated/logs/transcript.jsonl`, one JSON object per line with
  `created_at` (ISO 8601, UTC), `input_tokens`, `output_tokens` and `cache_read_tokens` on the turns that
  used the model. `transcript_full.jsonl` beside it repeats the same tokens line for line and is never read.
  The IDE's folder (`~/.gemini/antigravity`) has a summaries database of its own, but its conversations live
  in `conversations/*.pb` and its transcripts carry no token fields, so it is not read (out of scope).
- **Build check, done 2026-11-13 (counts and shapes only):** a brain folder id equals a `conversation_id`
  (8 of 8 CLI transcripts). `cache_read_tokens` is separate from `input_tokens` (676 of 704 turns read more
  from cache than their input), so `input_tokens` is fresh input as is.

## 2. Components

- **`CodexBurnReader`** (Core, new, pure). Walks `sessions/**/rollout-*.jsonl` and `.jsonl.zst` under every
  home from `CodexHomes.Discover`. Takes the decompressor as `Func<Stream, Stream>?`, the same injection
  `CodexLimitsReader` uses (the App passes `ZstdRollouts.Decompress`; Core keeps no package references),
  and the same 64 MB decompression cap. Yields token events.
- **`AntigravityBurnReader`** (Core, new, pure). Walks the CLI's `brain/*/.system_generated/logs/transcript.jsonl`
  like Codex's rollouts, and looks each conversation's project up in the CLI summaries database, opened
  read-only. Yields token events.
- **`IAgentBurnSource`** (Core, new). The small interface both readers implement: list the files to read,
  and read one file from a byte offset, returning token events and the new offset.
- **`VaultIngester`** gains one entry point that ingests an `IAgentBurnSource` into a named root. The Claude
  Code path is untouched.
- **Vault roots** `codex` and `antigravity`, beside the Claude Code roots. `VaultRootMeta` gains
  `agent` (`"codex"`, `"antigravity"`; absent means `"claude"`). Shards, rollups, checkpoints, quarantine
  and purge are the existing ones.
- **Read side.** `VaultReader` reports each row's agent from its root. `CcLedgerViewModel` and
  `LedgerRowViewModel` carry it; `LocalCcBarStrip` (the Local burn page's 30-day strip) stacks by it.
- **`AgentSources`** gains `antigravity` and its default color.
- **The MCP** gains an `agents` list on `get_local_burn_by_project`, read from Vault rollups, and links
  nothing new. Claude reading an agent's project totals is consented the way a Claude Code folder is: a
  switch per agent, off by default, beside the folders on Settings, Claude Code, Folders ("Let Claude read
  Codex's project totals"), saved as `mcp_agents` and exported to the MCP's settings projection.

## 3. Data flow

### Codex: a rollout line becomes a token event

- One rollout file is one Vault session, keyed by its file name without `.jsonl` or `.jsonl.zst`, so the
  plain and compressed copies of a rollout write the same row. `session_meta` opens a segment (a subagent or
  guardian thread inside the file); segments fold into the file's row and only scope the running total.
- `turn_context` sets the model in force (`model`) and the project in force (`cwd`). Before any
  `turn_context`, the segment's `session_meta.cwd` is the project.
- `event_msg` / `token_count` with a non-null `info`: the event's tokens are the change in
  `total_token_usage` since the segment's previous reading, so a reading repeated with the same total counts
  zero. When the total goes down, the event counts its `last_token_usage` instead.
- Into the Vault row: `input_tokens − cached_input_tokens` is input, `cached_input_tokens` is cache read,
  `cache_write_input_tokens` is cache creation, `output_tokens` is output (it already includes reasoning).
  `total` is input plus output, the same sum Claude's rows use.
- Each event is dated by its own `timestamp` into `by_day`. A rollout grows for days, so the file's time
  never dates anything.
- A plain rollout's checkpoint is its byte offset plus the parser state in force (segment, model, cwd,
  last total), so a grown file resumes mid-segment. A `.jsonl.zst` is a finished rollout: it is parsed whole
  once and then skipped while unchanged. Because it writes the same session key as its plain original, the
  row is replaced, never added to, and nothing counts twice.
- The row's project is the `cwd` in force at the file's first counted token event, the same first-wins rule
  Claude's rows use.

### Antigravity CLI: a transcript line becomes a token event

- A transcript line with any of `input_tokens`, `output_tokens` or `cache_read_tokens` is a token event,
  dated by its own `created_at`. Lines without them (user input, tool steps) are skipped without counting
  as bad.
- Into the Vault row: `input_tokens` is input, `cache_read_tokens` is cache read, `output_tokens` is output.
  `total` is input plus output. Model is recorded as `antigravity`; the transcript does not name one.
- The project comes from the conversation's summaries row: the first `workspace_uris` entry, turned from a
  `file:` URI into a path and keyed and named by the same rules as Claude's `cwd`
  (`CcLogReader.ProjectDisplayName`). No workspace, or no row yet, means the project "(none)", Claude's
  existing name for an unattributed session; once the summaries row appears, the next parse of that
  transcript moves the whole session to its project.
- The reader selects only `conversation_id` and `workspace_uris` from the database.
- The checkpoint is the transcript's byte offset, the same as a Claude transcript. The session is the
  conversation id.

### From the Vault outward

Both readers produce `VaultSessionRow`, so rollups, the 30-day ledger, CSV export and the MCP read agents
with no new storage shape. The widget's footer reads each agent root's today total after every ingest.

## 4. What you see and switch

- **Sources page.** Antigravity joins as an activity-only source: its own switch, off by default, with the
  caption "Activity only, from the Antigravity CLI. Sanduhr can't see Antigravity's limits or the IDE's
  usage." It is never offered as the primary source. Codex's burn follows the existing Codex switch.
  Turning a source off stops its reader; the Vault rows it wrote stay, as a Claude folder's do. A source
  with nothing on disk shows "Nothing found yet."
- **Color.** `antigravity` defaults to a periwinkle near `#7B8CF0`, evoking Google's blue without copying it
  (Store 10.1.4.4). Locked only after `ThemeContrastTests` passes it against all six built-in themes and
  beside Claude's `#E0875A` and Codex's `#2FB5A3`.
- **The widget's footer.** Today it reads "Updated 4:10 PM · Float · CC +12K", where CC is Claude Code's
  tokens since the last fetch. It gains a piece per agent: "· Codex +3K · Agy +1K". An agent's figure is its
  Vault total for today after the latest ingest minus the total after the ingest before it, so it is the
  burn of the last cycle (about five minutes) and lags Claude Code by one ingest. A piece with nothing new is
  left out, as CC's is. At a day change the earlier total counts as zero. Demo mode shows none.
- **The Local burn page's 30-day strip.** `LocalCcBarStrip` stacks each day's bar by agent in source colors.
  Its accessible name gives the window's total per agent ("30 days, Claude Code 41M, Codex 6.2M, Antigravity
  1.1M"), so color is never the only cue. With one agent it looks exactly as it does today.
- **The ledger** (Local burn page and the Vault's view of it). The totals combine all agents. The page's
  project list (text rows, no bars today) gives each project's agent shares in the row: "api, 1.2M (Claude
  Code 900K, Codex 300K)", one agent's project reading exactly as today. An agent filter
  (All, Claude Code, Codex, Antigravity) sits above the session list and shows only when more than one agent
  has data. Session rows carry a small agent label. Sorting works as today. `VaultLedgerCsv` gains an `agent` column.
- **Not changing:** the tray tooltip, alerts, pace markers and limits. They are about plan limits.

## 5. Failure and privacy

- **Isolation.** Each root ingests in its own try block. One agent failing never stops another agent or
  Claude Code. A failure logs once per pass.
- **Missing folder or database:** no rows, no error.
- **Locked database** (Antigravity running): opened read-only (`mode=ro`) through Windows' own
  `winsqlite3.dll` (no new package) with a two-second busy timeout. Still locked means this pass lists no
  Antigravity files, so no checkpoint moves and the next pass reads them with their project.
- **Bad line or bad `.zst` frame:** counted in `skipped_lines`, as Claude's are. A corrupt Vault shard goes
  to the existing quarantine.
- **Format drift** (a missing `info`, a renamed column): the item yields nothing and the log records the
  shape found, never the content.
- **What is read.** Codex: timestamps, segment ids, `cwd`, model names, token counts. Antigravity:
  `conversation_id`, `workspace_uris`, `created_at`, token counts. Never prompts, replies,
  `title`, `preview` or `raw_summary`. Full paths follow `store_full_paths` (off by default).
- **No network, no credential.** Nothing new is written outside the Vault folder.
- **Store build.** Reading `%USERPROFILE%\.codex` and `%USERPROFILE%\.gemini` works under `runFullTrust` as
  reading `.claude` does; reads are not virtualized. The manifest does not change.
- **The MCP trust boundary does not move.** The new readers are not linked into `Sanduhr.Mcp`. Agent rows
  reach `get_local_burn_by_project` through `VaultReader`, already on the allowlist, so they are as fresh as
  the widget's last ingest. `TrustBoundaryTests` stays unchanged.

## Testing

- **`CodexBurnReader`** against made-up rollouts: the same rollout plain and `.zst` gives identical rows and
  a checkpoint that carries over; a repeated reading counts once; a total that drops falls back to
  `last_token_usage`; a null `info`; two `session_meta` segments in one file; `cwd` changing mid-segment; a
  pass resumed mid-file from a saved checkpoint; a file over the decompression cap.
- **`AntigravityBurnReader`** against a generated SQLite database and brain folders: per-turn dating across
  two days; non-token lines skipped without counting as bad; `transcript_full.jsonl` never read; a
  conversation with no summaries row lands on "(no folder)"; a locked database skips the pass and keeps the
  offset; a pass resumed from an offset. **A column pin**: the reader's SQL selects exactly
  `conversation_id` and `workspace_uris`, so `title`, `preview` and `raw_summary` can never be read.
- **Vault:** agent roots round-trip; a meta without `agent` reads as Claude; purge and quarantine per agent
  root; the CSV `agent` column.
- **App:** the footer's agent pieces (new tokens shown, nothing new left out, day change, demo mode); the
  strip's accessible name with one agent and with three; the ledger filter hidden with one
  agent; project bar shares; Antigravity off by default and absent from the primary choice;
  `ThemeContrastTests` for the new color.
- **MCP:** the `agents` list on `get_local_burn_by_project` from Vault fixtures, absent unless `mcp_agents`
  consents the agent; `TrustBoundaryTests` unchanged
  and green.
- **Live smoke on this machine:** a dev build ingests the real Codex and Antigravity history. Totals are
  compared with a one-off probe sum, and the footer, strip and ledger are read through UI Automation, using only
  figures and names the UI shows.

## Shipping

- The privacy policy, the hub privacy page and the reviewer letter each gain one short paragraph: Sanduhr
  reads Codex's and Antigravity's local usage files when you switch them on, with no network and no new
  sign-in.
- Store listing copy names Antigravity with the trademark disclaimer (10.1.4.4).
- `docs/adr.md` ADR-006 gains a 2026-11-13 note: Antigravity limits are server-only, the Gemini-app meter is
  parked, and agents are activity-only Vault roots.
- A Mac task for agent roots in the Mac Vault is filed when this spec is approved.

## Build order

1. `IAgentBurnSource`, the `agent` meta field and the `VaultIngester` entry point, with Vault tests.
2. `CodexBurnReader` and its fixtures.
3. `AntigravityBurnReader` and its fixtures.
4. Wiring in the App: Sources switch, readers on the refresh tick, the new color.
5. The footer, the strip and the ledger: agent pieces, stacking, filter, labels, CSV.
6. The MCP `agents` list and its consent switches.
7. Live smoke, privacy copy, ADR note, release.
