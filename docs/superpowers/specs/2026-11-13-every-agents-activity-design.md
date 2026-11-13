# Every agent's activity: design

Windows, 5.0 spec 3 of 4 (the frame is in `2026-10-28-drawers-design.md`). Brainstormed with the owner on
2026-11-13. It takes over spec 2's stage 2 (Codex local activity) and adds Antigravity. Decisions are queued in
`docs/decisions/_pending-dashboard-log.md` until the dashboard is connected again.

## The goal

Someone who works across more than one coding agent sees where their week went, by project, across all of
them. Sanduhr already shows Claude Code's local burn; this spec adds Codex and Antigravity to the same
ledger, the same Vault and the widget's local-burn strip, read from each agent's own files on this PC.

**Success:** someone with Codex and Antigravity history switches both on in Sources. Within one refresh the
widget's strip shows today's burn split three ways, and the Local burn ledger shows each project's 30 days
with bars split by agent and a filter that narrows the session list to one agent. No sign-in, no network
call, and no prompt or reply text is read.

## Out of scope

- **Local models** (Ollama, LM Studio). Their logs carry token counts but nothing that ties a request to a
  project. Revisit if either one starts logging the caller or working directory.
- **Antigravity limits.** They are server-only (ADR-006 note below). Antigravity is activity-only.
- **The Gemini-app limits meter** (web sign-in to `gemini.google.com/usage`). Proven feasible on 2026-11-13,
  but it is a separate pool from Antigravity and the only piece with network and privacy cost. Parked.
- **Live agent burn in the MCP.** The MCP reads agent rows from the Vault; see section 5.
- **Per-turn dating for Antigravity.** Its transcripts carry no timestamps; see section 3.
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
- **Antigravity.** `~/.gemini/antigravity-cli/conversation_summaries.db` and
  `~/.gemini/antigravity/conversation_summaries.db` are ordinary SQLite, one row per conversation in
  `conversation_summaries` (71 rows each here), with `conversation_id`, `workspace_uris` (a JSON string),
  `last_modified_time`, `last_user_input_time` and `step_count` among others. Per-turn tokens
  (`input_tokens`, `output_tokens`, `cache_read_tokens`) are in
  `brain/<id>/.system_generated/logs/transcript*.jsonl`, with no timestamp, project or model.
  `conversations/*.pb` is undocumented protobuf and is not read.
- **Build check, before the plan's first task:** confirm on this machine that a brain folder id equals a
  `conversation_id` in the summaries database. If it does not, stop and revisit section 3 before any code.
  Also settle whether Antigravity's `input_tokens` already includes `cache_read_tokens` (compare the two
  across turns: a turn whose `cache_read_tokens` exceeds its `input_tokens` means they are separate). Total
  is fresh input plus output, the same sum Claude's rows use, so the answer decides whether input is used as
  is or less cache read.

## 2. Components

- **`CodexBurnReader`** (Core, new, pure). Walks `sessions/**/rollout-*.jsonl` and `.jsonl.zst` under every
  home from `CodexHomes.Discover`. Takes the decompressor as `Func<Stream, Stream>?`, the same injection
  `CodexLimitsReader` uses (the App passes `ZstdRollouts.Decompress`; Core keeps no package references),
  and the same 64 MB decompression cap. Yields token events.
- **`AntigravityBurnReader`** (Core, new, pure). Opens both summary databases read-only, sums each
  conversation's transcript tokens, yields one session per conversation.
- **`IAgentBurnSource`** (Core, new). The small interface both readers implement: discover items, read an
  item from a checkpoint, return the rows and the new checkpoint.
- **`VaultIngester`** gains one entry point that ingests an `IAgentBurnSource` into a named root. The Claude
  Code path is untouched.
- **Vault roots** `codex` and `antigravity`, beside the Claude Code roots. `VaultRootMeta` gains
  `agent` (`"codex"`, `"antigravity"`; absent means `"claude"`). Shards, rollups, checkpoints, quarantine
  and purge are the existing ones.
- **Read side.** `VaultReader` reports each row's agent from its root. `CcLedgerViewModel` and
  `LedgerRowViewModel` carry it; `LocalCcBarStrip` stacks by it.
- **`AgentSources`** gains `antigravity` and its default color.
- **The MCP** gains an `agent` field on `get_local_burn_by_project` rows and links nothing new.

## 3. Data flow

### Codex: a rollout line becomes a token event

- `session_meta` opens a segment, keyed by `codexStreamId(realPath, session_id)` as in Sanduhr Time.
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
- The checkpoint is the byte offset in the uncompressed stream plus the segment state in force (session id,
  model, cwd, last total). A `.jsonl.zst` is checkpointed under its uncompressed path, so when Codex
  compresses a rollout the checkpoint carries over and nothing is counted twice.

### Antigravity: a conversation becomes a session row

- From `conversation_summaries`, the reader selects only `conversation_id`, `workspace_uris` and
  `last_modified_time`. The project is the first `workspace_uris` entry, turned into a path and keyed and
  named by the same rules as Claude's `cwd` (`CcLogReader.ProjectDisplayName`). No workspace means the
  project "(no folder)".
- Tokens are the sum over `brain/<conversation_id>/.system_generated/logs/transcript*.jsonl`:
  `cache_read_tokens` is cache read, `output_tokens` is output, and input follows the section 1 check.
  Model is recorded as `antigravity`.
- **The date tradeoff:** the whole conversation lands on the local day of `last_modified_time`. A
  conversation that spans three days shows on its last one. The ledger says so (section 4).
- The checkpoint is `(conversation_id, last_modified_time, total transcript bytes)`. When either changes,
  the conversation is summed again and its row is replaced, never added to.
- A conversation listed in both the CLI and IDE databases counts once, keeping the newer
  `last_modified_time`.

### From the Vault outward

Both readers produce `VaultSessionRow`, so rollups, the 30-day ledger, CSV export and the MCP read agents
with no new storage shape. The widget's strip reads today's rows per root on the existing refresh tick.

## 4. What you see and switch

- **Sources page.** Antigravity joins as an activity-only source: its own switch, off by default, with the
  caption "Activity only: Sanduhr can't see Antigravity's limits. Conversations are dated by their last
  activity." It is never offered as the primary source. Codex's burn follows the existing Codex switch.
  Turning a source off stops its reader; the Vault rows it wrote stay, as a Claude folder's do. A source
  with nothing on disk shows "Nothing found yet."
- **Color.** `antigravity` defaults to a periwinkle near `#7B8CF0`, evoking Google's blue without copying it
  (Store 10.1.4.4). Locked only after `ThemeContrastTests` passes it against all six built-in themes and
  beside Claude's `#E0875A` and Codex's `#2FB5A3`.
- **The widget's strip.** `LocalCcBarStrip` stacks today's burn by agent in source colors. Its accessible
  name names each agent with a figure ("Today, Claude Code 1.4M, Codex 380K, Antigravity 90K"), so color is
  never the only cue. With one agent it looks exactly as it does today.
- **The ledger** (Local burn page and the Vault's view of it). The totals combine all agents. Each project
  bar is split by agent color; its accessible name and tooltip list each agent's share. An agent filter
  (All, Claude Code, Codex, Antigravity) sits above the session list and shows only when more than one agent
  has data. Session rows carry a small agent label; an Antigravity row's tooltip says "Dated by last
  activity". Sorting works as today. `VaultLedgerCsv` gains an `agent` column.
- **Not changing:** the tray tooltip, alerts, pace markers and limits. They are about plan limits.

## 5. Failure and privacy

- **Isolation.** Each root ingests in its own try block. One agent failing never stops another agent or
  Claude Code. A failure logs once per pass.
- **Missing folder or database:** no rows, no error.
- **Locked database** (Antigravity running): opened read-only (`mode=ro`) with a short busy timeout. Still
  locked means this pass skips it and keeps the last checkpoint.
- **Bad line or bad `.zst` frame:** counted in `skipped_lines`, as Claude's are. A corrupt Vault shard goes
  to the existing quarantine.
- **Format drift** (a missing `info`, a renamed column): the item yields nothing and the log records the
  shape found, never the content.
- **What is read.** Codex: timestamps, segment ids, `cwd`, model names, token counts. Antigravity:
  `conversation_id`, `workspace_uris`, `last_modified_time`, token counts. Never prompts, replies,
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
- **`AntigravityBurnReader`** against a generated SQLite database and brain folders: the same id in the CLI
  and IDE databases counts once; a changed `last_modified_time` replaces the row; a missing transcript; a
  locked database skips the pass. **A column pin**: the reader's SQL selects exactly the three columns in
  section 3, so `title`, `preview` and `raw_summary` can never be read.
- **Vault:** agent roots round-trip; a meta without `agent` reads as Claude; purge and quarantine per agent
  root; the CSV `agent` column.
- **App:** the strip's accessible name with one agent and with three; the ledger filter hidden with one
  agent; project bar shares; Antigravity off by default and absent from the primary choice;
  `ThemeContrastTests` for the new color.
- **MCP:** `agent` on `get_local_burn_by_project` rows from Vault fixtures; `TrustBoundaryTests` unchanged
  and green.
- **Live smoke on this machine:** a dev build ingests the real Codex and Antigravity history. Totals are
  compared with a one-off probe sum, and the strip and ledger are read through UI Automation, using only
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

0. The build check in section 1 (brain id equals `conversation_id`).
1. `IAgentBurnSource`, the `agent` meta field and the `VaultIngester` entry point, with Vault tests.
2. `CodexBurnReader` and its fixtures.
3. `AntigravityBurnReader` and its fixtures.
4. Wiring in the App: Sources switch, readers on the refresh tick, the new color.
5. The strip and the ledger: stacking, filter, labels, CSV.
6. The MCP `agent` field.
7. Live smoke, privacy copy, ADR note, release.
