# Run List: mac-agent-sources-2-46

Every item: a Mac with only Claude on draws, alerts and reports exactly as 2.45.0, pinned by a test where the
item touches a Claude surface. The design doc is the contract: `docs/superpowers/specs/2026-11-19-mac-agent-sources-design.md`,
and for behavior and wording the two Windows specs it ports. Tests use made-up files in temp homes injected by URL;
nothing reads the real `~/.codex` or `~/.gemini`. Keep SwiftUI view bodies small for the macos-15 type-checker. Never
`sed -i` a tracked file. Never touch `windows-dotnet/`. Nothing committed names a home path or a person. Run every
command from the item's worktree.

- [ ] **1. The zstd decoder, built in**
  Spec ref: design § 1 (zstd); decision LkT5L4NSc0Rh5IrV9h8m
  What to build: Download zstd v1.5.7's source tarball from github.com/facebook/zstd (release asset, check its
  published SHA-256), generate `zstddeclib.c` with its `build/single_file_libs/combine.sh` (decoder only), and add it
  as a C target `CZstd` in `mac/Package.swift` (source under `mac/Sources/CZstd/` with a header comment naming the tag
  and the tarball's SHA-256, `include/` with the needed header). A Swift `Zstd.decompress(_ data:, cap:)` (streaming,
  default cap 64 MB, throws past it or on a bad frame). Licence in `THIRD-PARTY-NOTICES.txt`. Nothing downloaded lands
  in the tree except the generated file and its header. Size M.
  Acceptance: Done when tests decompress a checked-in small `.zst` fixture to its known text, refuse one whose output
  passes a small cap, and throw on garbage; `./build.sh --debug` builds universal; the suite passes.
  Verify: `cd mac && ./test.sh && ./build.sh --debug`
  Stop class: go

- [ ] **2. Agent sources: the model, colors, Primary and setup**
  Spec ref: design § 1 (Names), § 2 rows "Sources page rows", "setup.usage.codex"; agent layer § 3 (colors)
  What to build: `AgentSource` (`claude`, `codex`, `antigravity`) with on switches (Claude on by default, the others
  off), a color each (defaults `#E0875A`, `#2FB5A3`, near `#7B8CF0`, changeable), and Primary (default Claude, or the
  only one on; Antigravity never offered: activity only). Stored in the app defaults. Refuse turning off the last
  limits source while the Desk is off (Windows rule). `SourceContrastTests` checks each default against the 7
  built-ins and Match Desk with `ThemeLint.contrast` and pins the values. `SetupSettings.usage["codex"]`, a second
  choice in `SetupWindow`, copy in `SetupCopy`, greyed with "Codex isn't installed on this Mac." when no `~/.codex`.
  No UI beyond setup here. Size M.
  Acceptance: Done when tests show the defaults, Primary falling back to the only source on, Antigravity refused as
  Primary, the last-source refusal, setup's `codex` round-trip, and the contrast pins; the suite passes.
  Verify: `cd mac && ./test.sh`
  Stop class: go

- [ ] **3. Codex limits from Codex's own logs**
  Spec ref: agent layer § 1, § 2 (tier shape), § 4 (freshness table); design § 1 (Codex homes, change watch)
  Depends on: 1, 2
  What to build: A pure `CodexLimitsReader` taking home URLs (`~/.codex` plus `CODEX_HOME` from the process
  environment when set), listing `sessions/**/rollout-*.jsonl` and `.jsonl.zst` newest first, reading each from its end
  (plain) or whole under the cap (`.zst`), and taking the newest `token_count` event with `rate_limits`. Windows'
  `CodexLimitsReader.cs` and its tests are the parity reference for field names and units. Map windows of 300 and
  10080 minutes to `Tier.fiveHour` / `.sevenDay` in a `UsageResponse`; drop and log once any other length. Freshness:
  fresh 30 minutes after `as_of`, then stale; a passed reset reads 0% fresh. Never opens `auth.json` (a test proves no
  file outside `sessions/` is opened). A `CodexUsageService` polls every 30 seconds while Codex is on and publishes
  `codexUsage`, its `as_of`, freshness and the empty states (not installed, no limits yet, couldn't read). Size L, hard.
  Acceptance: Done when tests show plain and `.zst` giving the same limits, newest wins across two homes, a passed
  reset, a stale limit, malformed lines skipped, a missing field meaning no limits, an odd window dropped, the cap,
  and the `auth.json` proof; the suite passes.
  Verify: `cd mac && ./test.sh`
  Stop class: go

- [ ] **4. Codex history, its snapshot and alerts per source**
  Spec ref: agent layer § 2 (history, snapshot, alerts); design § 2 rows "history.@codex.json", "snapshot.codex.json",
  "Alerts per source"
  Depends on: 3
  What to build: Codex readings recorded to `history.@codex.json` through `HistoryStore.Files`; `SnapshotWriter` writes
  `snapshot.codex.json` (same schema, root `source: "codex"`, no `account_ref`) when Codex is on, and removes it when
  Codex is turned off. `AlertRules` once-keys gain a source prefix for Codex only (Claude's keys unchanged, pinned);
  `Notifier` keeps a last usage per source; texts say "Codex 5-hour…" / "Codex weekly…"; a stale limit never alerts.
  A Codex alerts switch on Settings ▸ Alerts, off by default. Size M.
  Acceptance: Done when tests show the history file name, the snapshot file shape and removal, Claude's once-keys
  unchanged, a Codex alert firing on a fresh limit and not on a stale one, and the switch; the suite passes.
  Verify: `cd mac && ./test.sh`
  Stop class: go

- [ ] **5. The two-tone bar**
  Spec ref: agent layer § 3 (the two-tone bar); Windows `SourceBar.cs` for parity
  Depends on: 2
  What to build: A pure `SourceBar` layout: given sources with percents, colors, stale flags and a primary, the fill
  (primary 0 to its percent), the continuation segment to the furthest-out other source, and ticks for the rest, with
  an accessible name per mark ("Codex weekly, 62%"). `ProgressBarView` draws segments and ticks when given them; with
  one source it draws exactly as today. Size M.
  Acceptance: Done when tests cover every ordering of two and three sources, equal percents, a stale source dimmed,
  0 and 100, and one source producing today's single fill; the suite passes.
  Verify: `cd mac && ./test.sh`
  Stop class: go

- [ ] **6. The widget with two sources**
  Spec ref: agent layer § 3 (the widget, the Codex card), § 4 (table); design § 2 rows "Combined bars", "Codex card",
  § 3 (demo)
  Depends on: 3, 5
  What to build: `WidgetCardStack`: with Claude and Codex on, two combined bars (5-hour, weekly) on top, then a
  section per source with a header (name, color, collapse chevron, collapse remembered); a Codex section of
  `TierCardView`s with "as of 3:42 PM", dimmed when stale, and the empty states' lines. Compact keeps the highest card
  of the primary. Only Claude on: today's widget, pinned by a test on the rows built. Demo mode shows a sample Codex
  section when Codex is on, never reading `~/.codex`. Size L, hard.
  Acceptance: Done when tests show the rows built for Claude only (identical to 2.45.0), Claude plus Codex, Codex
  stale, each empty state, collapse, compact, and demo; the suite passes and `./build.sh --debug` builds.
  Verify: `cd mac && ./test.sh && ./build.sh --debug`
  Stop class: go

- [ ] **7. Sources in Settings and the menu bar**
  Spec ref: design § 2 rows "Sources page rows", "Tray follows Primary, tooltip"; agent layer § 3 (Settings ▸ Usage,
  the tray)
  Depends on: 3, 6
  What to build: A Sources group at the top of Settings ▸ Usage: one row per source with its switch, color well,
  Primary choice and a caption (Codex: "Reads ~/.codex. Activity and limits, no sign-in."; Antigravity: Windows'
  "Activity only, from the Antigravity CLI…" line; "Nothing found yet." when its folder is empty or absent). The menu
  bar reading follows Primary under `MenuBarMode` ("as of earlier" for a stale Codex primary); the status button
  gains a tooltip naming the other source when it is further out. Only Claude on: the menu bar text as today, no
  tooltip change beyond Claude's own reading. Size M.
  Acceptance: Done when tests show the menu bar text for each primary and mode, the tooltip text with and without
  another source further out, the captions, and Claude-only text unchanged; the suite passes.
  Verify: `cd mac && ./test.sh`
  Stop class: go

- [ ] **8. Desk meter pieces pick a source**
  Spec ref: agent layer § 3 (the Desk); design § 2 row "Desk meter source"
  Depends on: 5, 6
  What to build: `DeskPlacement` gains an optional source field (Combined, Claude, Codex) in the layout word, placed
  so every existing word parses unchanged and reads Combined. Desk meter rows take the source; bar, slim, ring and
  ticks draw the two-tone fill from `SourceBar`. The piece's settings and Arrange offer Shows (Combined, Claude, Codex)
  only when Codex is on. `propose_desk` and `get_desk` in the MCP accept and report the field (Python tests). Size M.
  Acceptance: Done when tests show every 2.45.0 layout word round-tripping unchanged, the new field parsing and
  writing, Combined with one source drawing as today, and the MCP field; the suite and the Python tests pass.
  Verify: `cd mac && ./test.sh && python3 -m pytest -q integrations/tests`
  Stop class: go

- [ ] **9. The statusline and get_usage carry Codex**
  Spec ref: agent layer § 2 (MCP, statusline); design § 2 rows "Statusline", "MCP get_usage codex block"
  Depends on: 4
  What to build: `sanduhr_statusline.py` reads `snapshot.codex.json` when its `captured_at` is under 30 minutes old and
  appends `cx 5h 40% wk 12%` after Claude's meters (absent file or stale: nothing added). `sanduhr_mcp.py`
  `build_usage` adds a `codex` block in the tier shape with `as_of` and `stale`. The script's content stamp changes
  so installed copies update. A Python test asserts the MCP and statusline import no network module. Size M.
  Acceptance: Done when Python tests show the statusline with fresh, stale and absent Codex snapshots, the `codex`
  block, Claude-only output unchanged, and the no-network import check; the suite and the Python tests pass.
  Verify: `cd mac && ./test.sh && python3 -m pytest -q integrations/tests`
  Stop class: go

- [ ] **10. Agent roots in the Vault, and Codex's burn**
  Spec ref: every-agents § 2, § 3 (Codex), § 5; design § 2 rows "Vault agent roots", "IAgentBurnSource", "Readers"
  Depends on: 1, 2
  What to build: Vault roots `codex` and `antigravity`; `VaultRootMeta` gains `agent` (absent reads Claude);
  `VaultCheckpointEntry` gains an optional parser state. An `AgentBurnSource` protocol and `VaultIngester.ingestAgent`
  (the Claude Code path untouched, pinned by the existing tests). `CodexBurnReader` per every-agents § 3 exactly
  (session key without `.jsonl`/`.jsonl.zst` so plain and compressed replace one row, segments, `turn_context`, deltas
  of `total_token_usage`, `last_token_usage` on a drop, per-event `by_day`, first-wins project via
  `CCProjectName.displayName`). Ingest runs while Codex is on, no Keep a record gate, each root in its own failure
  scope. `VaultStewardship` lists agent roots as agent records, never unlinked. Windows `CodexBurnSource.cs` and its
  tests are the parity reference. Size L, hard.
  Acceptance: Done when tests show the plain and `.zst` copies giving one identical row, a repeated reading counting
  once, a drop falling back, a null `info`, two segments, `cwd` changing, a resume mid-file from a checkpoint, the cap,
  an agent meta round-trip, a meta without `agent` reading Claude, stewardship, and a failing agent root not stopping
  Claude's ingest; the suite passes.
  Verify: `cd mac && ./test.sh`
  Stop class: go

- [ ] **11. The Antigravity CLI's burn**
  Spec ref: every-agents § 1, § 3 (Antigravity), § 5; design § 1 (SQLite)
  Depends on: 10
  What to build: `AntigravityBurnReader` per every-agents § 3: transcripts under
  `~/.gemini/antigravity-cli/brain/*/.system_generated/logs/transcript.jsonl` (never `transcript_full.jsonl`), project
  from `conversation_summaries` opened through the system `SQLite3` read-only with a 2-second busy timeout, selecting
  exactly `conversation_id` and `workspace_uris`. A locked database lists no files that pass. Ingest while the
  Antigravity source is on. Windows `AntigravityBurnSource.cs` and `AntigravitySummaries.cs` are the parity reference.
  Size M.
  Acceptance: Done when tests against a generated database and brain folders show per-turn dating across two days,
  non-token lines skipped without counting as bad, `transcript_full.jsonl` never read, no summaries row landing on
  "(none)" and moving once the row appears, a locked database keeping the offset, a resumed pass, and the column pin;
  the suite passes.
  Verify: `cd mac && ./test.sh`
  Stop class: go

- [ ] **12. Local burn shows every agent**
  Spec ref: every-agents § 4 (footer, strip, ledger); design § 2 rows "Footer", "Strip, ledger, CSV"
  Depends on: 11
  What to build: The widget footer gains "· Codex +3K · Agy +1K" (each the agent's today total after the latest ingest
  minus the one before, left out when zero, none in demo; Claude's footer text otherwise unchanged).
  `UsageStripChart` stacks each day by agent in source colors, its accessible name giving each agent's 30-day total;
  `UsageRankedList` rows give agent shares ("13.0M (Codex)" for one non-Claude agent); the Sessions tab gains an agent
  filter (shown only with more than one agent) and a label per row; `VaultLedgerCsv` appends an `agent` column. One
  agent: every view as 2.45.0. Size M.
  Acceptance: Done when tests show the footer pieces (new tokens, zero left out, a day change, demo), the strip's
  accessible name with one and three agents, the shares text, the filter hidden with one agent, the CSV column, and
  Claude-only views unchanged; the suite passes and `./build.sh --debug` builds.
  Verify: `cd mac && ./test.sh && ./build.sh --debug`
  Stop class: go

- [ ] **13. Claude may read agents' project totals, with consent**
  Spec ref: every-agents § 2 (the MCP), design § 2 row "MCP agents list + consent"
  Depends on: 12
  What to build: `mcp-access.json` gains `agents: {codex, antigravity}`, both false unless switched on, written by
  `MCPAccess`; a switch per agent in the Sources group ("Let Claude read Codex's project totals"). `get_local_burn_by_project`
  adds an `agents` list from Vault rollups (`read_vault_window`) for the fixed roots, with `agents_from`, only for
  consented agents; the fixed roots bypass `VAULT_ID_RE` by name, nothing else does. Size M.
  Acceptance: Done when Swift tests show the file's `agents` object and its default, and Python tests show the list
  absent without consent, present with it, a file without `agents` reading both off, and a non-hex root still refused;
  the suite and the Python tests pass.
  Verify: `cd mac && ./test.sh && python3 -m pytest -q integrations/tests`
  Stop class: hold

- [ ] **14. The 2.46.0 rows**
  Spec ref: design § 6; CLAUDE.md Mac gotchas
  Depends on: 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12
  What to build: `mac/Info.plist` 2.46.0, build 68. What's New cards (`version: "2.46.0"`, ids like `codex-limits` and
  `every-agents-burn`, destination the Usage settings) with their `WhatsNewTests` lines. CHANGELOG
  `## Unreleased (mac)` above `## v2.45.0-mac` with New (including item 13's MCP line, marked in the report as
  depending on the hold) and Fixes if any. `mac/README.md` sections (sources, Codex limits, agent burn, zstd).
  `docs/PRIVACY.md` Mac rows for Codex and Antigravity reads. `docs/adr.md` ADR-006 Mac note. Smoke section 80 "Codex
  and the Antigravity CLI (2.46.0)" in `docs/mac-smoke-test.md`: fixture-home steps in a dev profile (signed out, file
  store, settings backed up first) and a step for the real-data read later. Size S.
  Acceptance: Done when `WhatsNewTests` pin the new cards, the CHANGELOG heading is `## Unreleased (mac)`, and the
  suite, the selftest and the Python tests pass.
  Verify: `cd mac && ./test.sh && ruby smoke/selftest.rb && python3 -m pytest -q integrations/tests`
  Stop class: go

## Not this run
- The live pass on real Codex and Antigravity files: neither is installed on this Mac (decision xc0rewYY1FC8hU4SmYhu).
- Agent notifications (Windows 5.0 spec 4).
- The release dispatch, appcast and cask.
