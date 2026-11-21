# Decisions: mac-agent-sources-2-46

## D1
- **Decision:** One Mac release (2.46.0) carries both Windows parts: Codex limits (Windows 4.8) and every agent's
  burn (Windows 4.9). The zstd decoder is vendored, and the live check runs on made-up files.
- **Why:** The owner's three answers on 2026-11-19 (dashboard Y5SbjCXEbPIsVhuigpgy, LkT5L4NSc0Rh5IrV9h8m,
  xc0rewYY1FC8hU4SmYhu). Both parts read the same Codex rollouts.
- **Alternative not taken:** Limits in 2.46 and burn in 2.47.
- **Item:** all
- **How to reverse:** Park items 10 to 13 and ship the rest as 2.46.0.

## D2
- **Decision:** The design doc, untracked in the owner's tree, is committed on the integration branch as the run's
  first commit. The owner's untracked copy is left alone.
- **Why:** Workers run in worktrees cut from the integration branch and must read the design. The owner's tree is
  never touched.
- **Alternative not taken:** Workers read the doc from the owner's tree by absolute path, which breaks if it moves.
- **Item:** all
- **How to reverse:** Drop that commit before the PR; the owner commits the doc themselves.

## D3
- **Decision:** The design's item 12 is split. The Local burn views are item 12 (`go`); Claude's consented read of
  agent project totals is item 13 (`hold`, since it adds an access-consent switch). Item 14 doesn't depend on 13, and
  its CHANGELOG MCP line is flagged as resting on the hold.
- **Why:** A `hold` holds every dependent. Kept together, the hold would also have kept the views and the release
  rows off the integration branch.
- **Alternative not taken:** One `go` item, against the default class for permissions.
- **Item:** 12, 13, 14
- **How to reverse:** Merge 13 after the walkthrough, or drop its CHANGELOG line if it's declined.

## D4
- **Decision:** Items 3, 6 and 10 go to judgment-tier workers (the session model). The others go to the bulk tier at
  high effort.
- **Why:**
  - 3 decides what every Codex user's limits read as.
  - 6 rebuilds the widget every user sees.
  - 10 changes the Vault's root and checkpoint shapes, which hold real users' history.
  - A wrong call in any of the three is expensive, which is the same reasoning as D3 of the 2.45 run.
- **Alternative not taken:** The bulk tier for every item.
- **Item:** 3, 6, 10
- **How to reverse:** None needed; the tier changes cost, not output.

## D5
- **Decision:** 2.46.0 build 68, with What's New cards.
- **Why:** A minor version with new features; CLAUDE.md asks for a card on every minor.
- **Alternative not taken:** None.
- **Item:** 14
- **How to reverse:** Edit Info.plist and the cards.

## D6
- **Decision:** Claude's and Codex's on switches live in `SetupSettings.usage` (`claude`, `codex`), as on Windows,
  where the Sources page writes `setup.usage.codex`. Only Antigravity, which is activity only, has its own key,
  `sources.antigravity.on`. Colors and Primary sit under `sources.*`.
- **Why:** A second on switch for each source would drift from setup. A Claude-only Mac keeps writing exactly
  `{claude: Bool}`, which two tests pin.
- **Alternative not taken:** `sources.<id>.on` keys for every source, kept in sync with setup.
- **Item:** 2, and 7 (the Sources rows write through `SetupStore`)
- **How to reverse:** Add the keys and a migration from `setup.usage`.

## D7
- **Decision:** The source colors are checked at 3:1 or better against each built-in theme's glass and bar track,
  the Windows `AgentSourceColorTests` rule. Match Desk, which has no card, is checked as default white ink at 22%
  over black.
- **Why:** This is parity with Windows. On a light desktop the colors can fall short with Match Desk, which is one
  reason the colors can be changed.
- **Alternative not taken:** A text-contrast (4.5:1) bar, which no meter mark needs.
- **Item:** 2
- **How to reverse:** Raise the threshold in `SourceContrastTests`.

## D8
- **Decision:** In four places the Mac Codex limits reader follows the Windows code rather than the spec:
  1. **Which file wins.** It considers the top 20 rollouts by modification time plus the top 20 by the session stamp
     in their names, and the newest event inside them wins. Codex rewrites old rollouts when it compresses them, so
     modification time alone picks the wrong one.
  2. **Freshness per tier.** Each window is fresh or stale on its own (`freshTiers`); "fresh" overall means at
     least one window is.
  3. **A per-file cache keyed by size and modification time.** An unchanged `.zst` isn't decompressed again on
     every 30-second poll.
  4. **Plain files are read from their end** in doubling chunks, 64 KB up to the 4 MB tail Windows reads.
- **Why:** The Windows code was checked against real Codex logs; the spec was written before that check. This Mac
  has no Codex to check against (D1).
- **Alternative not taken:** The spec's single newest-by-modification-time rule and one freshness state.
- **Item:** 3 (item 6 shows per-tier staleness)
- **How to reverse:** `CodexLimitsReader` candidate selection and `CodexUsageService.freshness`.

## D9
- **Decision:** Codex alerts:
  - They are off by default; Windows defaults them on.
  - They always arrive as banners and never pulse the Desk's meters, which show Claude's limits.
  - `snapshot.codex.json` is removed only when Codex is turned off in setup or a reading comes back empty, not when
    demo mode or Reset stops the reader.
- **Why:**
  - Off by default is what list item 4 said, and it keeps an upgrade quiet; a 2.46 user turns Codex alerts on next
    to the Codex switch.
  - Pulsing Claude's meters for a Codex limit would point at the wrong plan.
- **Alternative not taken:** Windows' default of on, and Desk pulse delivery for Codex.
- **Item:** 4 (item 8 may let a Codex meter pulse once the Desk draws Codex)
- **How to reverse:** `Notifier.Key.codexAlerts` default; the routing in `Notifier`.

## D10
- **Decision:** On the widget with two sources:
  - Combined bars draw a bare label and the bar, with no warning glow, breath or pace tick; pace and warnings stay
    on each source's own card.
  - Codex's "as of" sits in its section header.
  - A lone Claude section has no header.
  - Codex cards leave out Claude's limit menu, local burn and sparkline.
  - Compact shows only the primary's highest card.
- **Why:** Parity with Windows' `CombinedBar` and stacked sections. Claude's local burn is Claude Code's, never
  Codex's.
- **Alternative not taken:** The primary's warning glow on the combined bar, and an "as of" line on each card.
- **Item:** 6
- **How to reverse:** `WidgetSourcesView`, `WidgetSources.build`. A Codex sparkline from `history.@codex.json` is
  a follow-up.

## D11
- **Decision:** The Sources switches for Claude and Codex go through `SetupService.apply`, the same path as Set up
  Sanduhr's Continue, so turning Claude off has the same consequences and lines as in setup. The menu bar gets a
  tooltip only when another source is further out or a Codex primary is stale ("Claude week 40%. Codex week 70%."),
  without Windows' app-name prefix. A Claude-only Mac shows no tooltip, as in 2.45.0.
- **Why:** One path for on and off means no drift. A tooltip on every hover would change a Claude-only Mac.
- **Alternative not taken:** Windows' always-on tooltip, "Sanduhr für Claude — Claude week 40%."
- **Item:** 7
- **How to reverse:** `MenuBarText.display`.

## D12
- **Decision:** A Desk meter piece's source is stored in the layout word as a `src=` field, as on Windows. Combined
  writes nothing, so every 2.45 word round-trips byte for byte. Only `claude` and `meters` take a source. More choices:
  - A piece saved as Codex draws as today while Codex is off; Windows blanks it.
  - The ticks style lights cells in the primary's color, then the continuation's.
  - A joined row carries the primary's number and no warning (D10).
  - A Codex meter never pulses (D9).
  - `propose_desk` and `get_desk` take and report `source`.
- **Why:**
  - Each choice keeps a Mac with Codex off, or with one source, drawing as 2.45.0.
  - Pulsing a Codex meter would need a Codex route in `Notifier` and per-source pulse keys.
- **Alternative not taken:** A new positional field in the layout word (which breaks older words), Windows' blank
  piece, and primary-only ticks.
- **Item:** 8
- **How to reverse:** `DeskMeterSource`; the `src=` parse in `DeskPlacement`. Codex pulses are a follow-up.
- **Gotcha:** The Mac design-JSON error message now names `source`, while Windows' C# message still says "tilt and
  style". The fixtures aren't shared, so neither CI breaks.

## D13
- **Decision:**
  - **Statusline.** Codex's segment `cx 5h 40% wk 12%` follows Claude's after the Mac's ` | ` separator. It shows
    only while `snapshot.codex.json` is under 30 minutes old, and it rides with the session and weekly meters under
    Combine's `--mine`; it is not a new chip.
  - **MCP.** `get_usage` gains a `codex` block (`status`, `stale`, `as_of`, `age_seconds`, `plan`, `tiers`), only
    when the snapshot reads. The MCP server becomes 0.6.0-mac.
  - **New test.** One checks that the MCP and the statusline import no network module.
- **Why:**
  - A new `--mine` name would change the Swift segment enum and Combine's chips, which is out of scope.
  - `as_of` matches the Mac's Claude block; `status` and `age_seconds` keep Windows' shape.
- **Alternative not taken:** A Codex chip in Combine, which is a follow-up, and Windows' `captured_at` key.
- **Item:** 9
- **How to reverse:** `sanduhr_statusline.py` codex segment; `build_usage` in `sanduhr_mcp.py`.

## D14
- **Decision:**
  - **Erasing a record.** Erasing an agent's Vault record sets `vault.agentStopped.<agent>` (Windows' tombstone)
    and then purges. Switching the source on again clears the stop.
  - **Names only.** Agent rows store project names only, never `cwd`.
  - **Ingest trigger.** The Codex poll also triggers an agent ingest, at most every 5 minutes.
  - **Zstd.** It gained a streaming decode so a `.zst` is walked without being held whole.
  - **Parser state.** It holds the model and last total, not the cwd, as on Windows.
  - **Old data.** Claude roots write exactly the keys they did in 2.45.0: `agent` and `parser_state` are left out
    when nil, and a test pins an old checkpoint re-encoding byte for byte.
- **Why:**
  - Agent roots belong to no account, so the full-paths setting has no owner.
  - Without the poll trigger, a Mac with Claude off would only ingest at launch.
  - The Vault holds users' real history.
- **Alternative not taken:** Storing `cwd` under the global full-paths setting, and ingest only on the Claude refresh.
- **Item:** 10
- **How to reverse:** `VaultAgentStop`, `CodexBurnReader`, `VaultService` agent scheduling.

## D15
- **Decision:** The Antigravity reader reads its database with exactly `SELECT conversation_id, workspace_uris FROM
  conversation_summaries`, a query a test pins. With no database it lists no files, as on Windows. A Sources change
  (switch or color) now calls `vault.trigger()`, so a Mac with only Antigravity on ingests on a switch and not just
  at launch. A summaries row that appears later moves its session once the transcript changes or seals after an
  hour, as on Windows.
- **Why:** The pinned query is the privacy line: `title`, `preview` and `raw_summary` can't be read. Ingest has no
  other trigger once Claude is off.
- **Alternative not taken:** A dedicated Antigravity timer.
- **Item:** 11
- **How to reverse:** `AntigravityBurnReader`, the `.sanduhrSourcesDidChange` observer.

## D16
- **Decision:** Local burn and agent roots:
  - **The page.** Agent roots belong to no account, so they join every account's Overview and Sessions. They show
    while their Vault rows exist, even after the source is switched off.
  - **No folder linked.** With no Claude Code folder, an agent's rows show under the setup note (`.agentsOnly`).
  - **CSV.** The `agent` column is always appended last, Claude-only included, as on Windows.
  - **Footer.** Pieces use `TokenFormat.compact` ("+3.0k"), matching Windows' test.
  - **Not covered.** Trends and Skills stay Claude-only.
- **Why:** Parity with Windows, which reads agents on every path. Claude-only views are pinned as in 2.45.0 except
  for the new trailing CSV column.
- **Alternative not taken:** Agent rows only while their source is on, and no CSV column on a Claude-only Mac.
- **Item:** 12
- **How to reverse:** `UsagePageLoader`, `VaultLedgerCsv`.
- **Watch:** The worker saw one full-suite failure of `theLoaderReadsAgentRootsForAnAccountWithAFolderAndWithout`
  (4150 printed on both sides), with the cause not established. The orchestrator then ran it 6 times filtered and
  in 2 full suites, all passing.

## D17
- **Decision:** Item 13 (held):
  - `mcp-access.json` always carries `agents: {codex, antigravity}`, both false unless consented, so a Claude-only
    Mac's file gains this one object.
  - The MCP's fixed agent roots pass the vault-id check only through an allowlist in `read_vault_window`.
  - Consent alone answers `get_local_burn_by_project`, as on Windows.
  - The server stays 0.6.0-mac, since item 9's bump is unreleased in the same 2.46.0.
- **Why:** A missing object reads as both off anyway, but writing it keeps the file's shape fixed. One allowlist
  keeps the bypass in a single place.
- **Alternative not taken:** Writing `agents` only once something is consented, and bumping to 0.6.1-mac.
- **Item:** 13
- **How to reverse:** `MCPAccess.encode`; `AGENT_ROOTS` in `sanduhr_mcp.py`.
