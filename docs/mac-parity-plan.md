# Mac parity plan

Windows Sanduhr (3.4.x) has pulled well ahead of the Mac app. This ranks each Windows feature for porting by value against effort, so we can pick what comes over first.

Effort: S is an evening, M is a weekend, L is a week or more.

## Where the Mac stands

Already on Mac: themes and user themes, focus and snake, sparklines and pacing, compact mode, Sparkle updates, plus this branch's font picker, subtle mode, desktop pin, threshold alerts and `snapshot.json` (schema v1, same as Windows).

Missing underneath: the Mac tier list is a fixed enum (`Models/Tier.swift`), it does not read `limits[]` or the routines budget, and nothing reads local Claude Code logs. Credentials are a `0600` plaintext file, not Keychain, despite the `KeychainStore` name.

## Ranking

| # | Feature | Mac today | Value | Effort | Depends on |
|---|---|---|---|---|---|
| 1 | Claude Code statusline bridge | done (`mac/integrations`) | high | S | snapshot.json (done) |
| 2 | Sanduhr MCP: `get_usage`, `ping` | done (`mac/integrations`) | high | S | snapshot.json (done) |
| 3 | Model-scoped meters from `limits[]`, dynamic tiers | partial (fixed enum) | high | M | API audit doc |
| 4 | Local Claude Code burn (per project, worktree aware) | none | high | M | none |
| 5 | MCP: `get_local_burn_by_project`, `get_model_usage` | none | high | S | 2, 4 |
| 6 | Routines tier (`/v1/code/routines/run-budget`) | none | medium | S | 3 |
| 7 | Usage vault (opt-in history, per-folder consent) | partial (2 hr sparkline only) | medium | M | 4 |
| 8 | Claude Usage tab: overview, trends, sessions, CSV | none | medium | L | 4, 7 |
| 9 | MCP: `get_usage_history` | none | medium | S | 2, 7 |
| 10 | Horizon graphs | none | medium | M | 7 |
| 11 | Real Keychain credentials | plaintext file | medium | S | none |
| 12 | Embedded sign-in (WKWebView) and reauth routing | none (paste cookie) | medium | M | 11 |
| 13 | Plan badge | none | low | S | 3 |
| 14 | Tier reorder and hide, speculative tags | none | low | S | 3 |
| 15 | One-click MCP install with consent | none | low | S | 2 |
| 16 | Theme lint and Theme Studio, MCP `propose_theme` | none | low | M | 2 |
| 17 | Chime synth for alerts | system sound | low | S | none |
| 18 | Publish usage (opt-in daily) | none | low | M | 7 |
| 19 | Store update notice | Sparkle covers it | none | skip | none |

## Suggested order

1. **Quick wins on the snapshot (1, 2).** A statusline script and a small MCP server that both read `snapshot.json`. Shared shape with Windows, so the same MCP tool names work from either machine. Desk already reads the same file.
2. **Get the meters right (3, 6, 13, 14).** Parse `limits[]` into tiers instead of the fixed enum. This is what keeps the Mac from going stale every time Anthropic adds a meter.
3. **Local burn (4, 5).** Read `~/.claude*/projects/**/*.jsonl`, attribute to project with worktree and subfolder folding, show the delta on cards, expose it over MCP.
4. **History depth (7, 9, 10, then 8).** The vault first, then the views on top of it. The Usage tab is the biggest single item; do it last in this group.
5. **Sign-in hardening (11, 12)** whenever cookie pasting gets old.
6. **Nice to have (15 to 18).**

## Notes

- The MCP server can be Swift (ships inside the app bundle) or match `Sanduhr.Mcp` on Windows. Keeping the tool names and JSON shapes identical matters more than the language.
- The snapshot never holds the session key; the MCP server and statusline only read the snapshot and local logs, never claude.ai.
- Port from the Windows Core classes named in the CHANGELOG: `TierModel`, `CcLogReader`, `Vault*`, `HorizonBands`, `StatuslineScript`, `McpServer`.
