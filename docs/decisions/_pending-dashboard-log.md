# Decisions waiting for the dashboard

The 626 dashboard MCP was disconnected when these were made. The next session with it connected logs each one with
`manage_decisions` (action `log`) to the project named, then deletes its entry here.

## 2026-11-13 · Sanduhr für Claude (Windows) · FX7zznEFlzArQ3CTY7RU

### Antigravity keeps no plan limits on disk (5.0 spec 3 feasibility check)

- **Category:** architecture
- **Decision:** Antigravity's limits (Gemini models, Claude and GPT models, Gemini 4 Argon; 5-hour and weekly) cannot be read
  locally the way Codex's are. Spec 3 starts from activity (tokens, sessions, time per project), unless a Google web page
  turns out to show the limits behind a normal sign-in.
- **Evidence:** `~/.gemini/antigravity-cli/cli.log` shows `quota_manager.go: doRefreshQuota` fetching from Google at
  startup, with no values logged. A full scan of `%APPDATA%\Antigravity` (7,863 files) and `~/.gemini/antigravity`
  (274 files) with the IDE fully closed found no quota fields. The only hits were VS Code's local edit history of the
  user's own code, and cached Claude Code extension JavaScript (2.1.73 to 2.1.88, March 2026) whose UI strings map
  `five_hour` to "session limit". The IDE's Models & Usage screen gets its numbers from Antigravity's local service,
  which fetches them over the user's Google sign-in. Querying that private local API is fragile, and likely against the
  Antigravity terms (unverified). Per-turn token counts (`input_tokens`, `output_tokens`, `cache_read_tokens`) are in
  `brain/<id>/.system_generated/logs/transcript*.jsonl`.
- **Revisit if:** Antigravity starts writing its quota to disk, or a Google web page (Google One AI Credits or another)
  shows the 5-hour and weekly limits. Then a Claude-style WebView2 sign-in becomes a candidate, after a test that Google
  allows sign-in in an embedded WebView2 window.
- **Also:** record this under ADR-006 in `docs/adr.md` when spec 3's branch opens, so the check is not repeated.

### Gemini plan limits can be read like Claude's: web sign-in to gemini.google.com/usage

- **Category:** architecture
- **Decision:** Spec 3 can read the Gemini plan's 5-hour and weekly limits the way Sanduhr reads claude.ai: sign in to the
  website in Sanduhr's own WebView2 window and read what gemini.google.com/usage reads.
- **Evidence:** The Google One AI Credits page shows only credit packs, not limits. The Gemini desktop app (1.19.0) is an
  Electron shell around `https://gemini.google.com/app`, and its Usage limits screen (Current usage with a reset time,
  Weekly limit with a reset date, as percentages) is `https://gemini.google.com/usage`. Test, 2026-11-13: a throwaway
  WebView2 window, set up like Sanduhr's sign-in (Microsoft.Web.WebView2 1.0.3912.50, `ClaudeSignIn.BrowserUserAgent`,
  its own profile), completed a real Google sign-in. Google sent the account through
  `myaccount.google.com/verification/selfie/precollection` first, likely a new-device check, so expect it on a first
  sign-in. On relaunch with the same profile, /usage rendered "Usage limits", "Current usage" and "Weekly limit". The
  test profile was deleted afterwards.
- **Separate pools (tested 2026-11-13):** Este used Gemini 4 Argon in Antigravity on another machine; gemini.google.com/usage
  did not move off 0% and has no Argon line. The web meter is the Gemini app's own allowance only. Antigravity's pools
  (Gemini models, Claude and GPT models, Argon) are not readable by Sanduhr, so Antigravity stays activity-only.
- **Scope call (Este, 2026-11-13):** spec 3 is "every agent's activity" (Antigravity, local models, Codex stage 2),
  with no network and no new credential. The Gemini-app meter is parked: it is the only part with network and privacy
  cost, and it does not cover Antigravity, the Google tool actually in use. Revisit if users pace against the Gemini app.
- **Open (Gemini meter, parked):** how the page fetches its numbers (the RPC to read) is not
  examined yet. The spec adds a network destination (gemini.google.com) and a stored Google session, so the privacy
  policy, the hub page and the reviewer letter all change, as they did for Claude.

### Spec 3 ledger shape: combined totals, per-agent detail

- **Category:** architecture
- **Decision:** The local-burn ledger stays one ledger across Claude Code, Codex and Antigravity. Top totals and project
  bars combine all agents, each bar split into agent colors (Claude orange, Codex teal, a new Antigravity color); the
  session list underneath stays one list with an agent filter. Chosen over an agent column only (no visual split) and
  over one section per agent (no cross-agent project totals). Local models deferred: no project attribution in Ollama
  or LM Studio logs.
- **Gotcha (corrected by the build check below):** Antigravity CLI transcripts do carry `created_at` per line, so
  turns are dated individually; only the project comes from `conversation_summaries.db` (`workspace_uris`).
- **Revisit if:** users want per-agent sections over combined bars.

### Spec 3 architecture: each agent is a Vault root

- **Category:** architecture
- **Decision:** Codex and Antigravity get their own readers, each writing a Vault root (`codex`, `antigravity`) with an
  `agent` field on the root meta. Shards, rollups, checkpoints, quarantine and purge carry over unchanged; the ledger,
  strip and MCP read all roots grouped by agent. Chosen over per-agent pipelines (triples every Vault fix) and over
  generalizing `CcLogReader`/`VaultIngester` first (rewrites the shipped, most-tested Claude path).
- **Tradeoff:** Antigravity is SQLite plus transcripts, not append-only JSONL, so its reader needs its own checkpoint rule
  keyed on `last_modified_time` rather than byte offsets.
- **Revisit if:** a third non-JSONL agent arrives; then the generalized pipeline earns its refactor.

### Codex burn comes from total_token_usage deltas, not Sanduhr Time's reader

- **Category:** gotcha
- **Decision:** Sanduhr Time's Codex reader tracks activity only (time, prompts, edits), never tokens. The Windows Codex
  burn reader ports its discovery, segment and `.zst` rules, and takes tokens from `event_msg` `token_count`
  `info.total_token_usage` (input, cached_input, cache_write_input, output, reasoning_output, total), counting the delta
  from the previous reading in the same segment so repeated readings count once. Model from `turn_context.model`,
  project from `turn_context.cwd` (falling back to `session_meta.cwd`).
- **Evidence:** key-shape probe 2026-11-13 of this machine's rollouts: 15 plain, 65 `.jsonl.zst`, so most history is
  compressed and `.zst` support is required, not optional. `info` and `rate_limits` can be null on a reading.
- **Revisit if:** Codex changes the token_count shape or starts resetting totals mid-segment.

### Spec 3: the burn reader reuses the App's zstd decompressor; the MCP reads agents from the Vault only

- **Category:** architecture
- **Decision:** .NET 10 ships no zstd decoder, and 65 of this machine's 80 Codex rollouts are `.jsonl.zst`. The App
  already references ZstdSharp.Port 0.8.5 and hands `ZstdRollouts.Decompress` to `CodexLimitsReader` (spec 2); the
  Codex burn reader takes the same injected callback, so Core stays package-free and nothing new is added. The MCP does
  not link the new readers: `get_local_burn_by_project` reads agent rows from the Vault the widget writes, so the MCP
  allowlist and `TrustBoundaryTests` do not change.
- **Tradeoff:** agent rows reach Claude Code only as fresh as the widget's last ingest; Claude Code's own rows stay live.
- **Revisit if:** .NET ships zstd in `System.IO.Compression` (drop the package), or people need live agent burn in the MCP.

### Spec 3 build check: Antigravity CLI is per-turn and readable; the IDE keeps tokens only in protobuf

- **Category:** gotcha
- **Finding (2026-11-13, counts and shapes only):** brain folder id equals `conversation_id` (CLI 8 of 8, IDE 2 of 2).
  `cache_read_tokens` is separate from `input_tokens` (676 of 704 turns exceed their input), so input is used as is.
  CLI transcripts carry `created_at` (ISO, UTC) on every line, so Antigravity CLI dates per turn like Codex, and the
  spec's last-activity tradeoff goes away. `transcript_full.jsonl` repeats `transcript.jsonl` token for token, so only
  `transcript.jsonl` is read (a `transcript*` glob would double-count). The IDE writes 62 conversations as
  `conversations/*.pb` (undocumented protobuf) plus markdown artifacts in `brain/`; its two transcripts carry no token
  fields, and its one per-conversation SQLite keeps generation data as binary blobs. IDE token use is not readable.

### Spec 3 scope: Antigravity CLI tokens only, not the IDE

- **Category:** scope
- **Decision (Este, 2026-11-13):** Antigravity in the ledger is the CLI only. The IDE keeps its token use in
  undocumented protobuf, and decoding it was rejected as fragile across updates. The Sources caption says the IDE's
  usage isn't readable. Showing IDE conversations as zero-token sessions was rejected too: a zero row reads like a bug.
  With per-turn `created_at`, the Antigravity reader becomes append-only JSONL with byte-offset checkpoints, like Codex.
- **Revisit if:** the IDE starts writing token fields to `transcript.jsonl`.
