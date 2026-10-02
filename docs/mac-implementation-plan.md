# Mac implementation plan (for Claude Code)

Executable plan for `docs/mac-merge-plan.md`: one milestone per pull request, each with the
Windows code to port, the acceptance check, and the tests. Written to be handed to a Claude Code
session on a Mac, which can build and test (Cowork cannot run Swift).

## How to run this

1. Open the repo in VS Code with the Claude Code extension (or `claude` in a terminal) on a Mac.
2. Branch from `feat/mac-desk` per milestone: `feat/mac-m1-tiers`, `feat/mac-m2-accounts`, and so on.
3. Start the session with:

   > Read CLAUDE.md, docs/mac-merge-plan.md and docs/mac-implementation-plan.md. Implement
   > milestone Mn only. Port from the Windows files listed; keep file formats identical to
   > Windows. Work test first where the milestone lists tests. After each task run
   > `cd mac && ./build.sh --debug && swift test`, fix what fails, then commit (conventional
   > commits). Stop at the milestone's acceptance list and report what you verified.

4. Review, try the build (`./build.sh` for the universal release build), merge, next milestone.

## Ground rules

- **Formats match Windows exactly**: `snapshot.json`, `history.<account>.json`, the vault, CSV
  columns. Specs under `docs/superpowers/specs/` are binding; Windows `Sanduhr.Core` is the
  reference implementation and its tests are the fixtures to port.
- **No secrets anywhere but the credentials store**: never in logs, snapshot, history, vault.
- **Build without full Xcode**: `build.sh` picks the 26.x SDK; release builds are universal and
  fail if a slice is missing.
- **Mac sources only**: read `windows-dotnet/`, never edit it.
- **Every user-visible change** gets a line under `## Unreleased (mac)` in `CHANGELOG.md`.
- **Tests**: `mac/Tests/SanduhrTests`; pure logic goes in types with no AppKit so it tests headless.

## M0. Ship 2.1.0 (S)

Goal: the merged app on users' Macs, native on Apple silicon.

- Build `./build.sh` (universal) and smoke test: widget, alerts, Desk off by default, Desk on
  through Desk Settings, notch, Option+J.
- Add `.github/workflows/mac-ci.yml` (macos runner): `swift build` and `swift test` on push to
  `mac/**`; `lipo -archs` check on the release build.
- Release per `mac/README.md`: Developer ID sign (identity in `build.sh`), notarize, DMG,
  `docs/appcast.xml` entry, GitHub release `v2.1.0-mac`.
- Accept: a 2.0.4 install offers 2.1.0 through Sparkle; the installed binary is `x86_64 arm64`.

**Done 2026-10-02.** 2.1.0 shipped (`v2.1.0-mac`); a 2.0.4 install updated through Sparkle and
reports `x86_64 arm64`, Notarized Developer ID. What differed from the plan:

- Releases are built on GitHub, not locally: `mac-release.yml` drafts a notarized, Sparkle-signed
  release and opens the appcast and cask pull request; signing secrets live in the `mac-release`
  environment, uploaded once from the Mac holding them (`docs/mac-release.md`).
- `build.sh`'s command-line-tools universal path kept only x86_64 (SwiftPM 6.4 reuses one bin
  path for both triples); fixed. `swift test` needs `mac/test.sh` without full Xcode.
- Build number had to go to 3: 2.0.4 shipped as build 2, and Sparkle orders by it.
- The hand smoke test (`docs/mac-smoke-test.md`) found four bugs, all fixed before release: Desk
  Settings unreachable when Ice hides the menu bar item, notch wings 1 pt short of a 39 pt menu
  bar, the migration copying Sanduhr Desk's alert keys, and the wings window collapsing when the
  notch was toggled (hosting views now never size their windows).
- Option+J and Option+S were an skhd binding, not the app's; Desk now registers them. The notch is
  off by default (the plan said so; the merged code had it on).
- Still open from the merge plan: one Settings window with a Surfaces list (Desk keeps its own
  window and its own defaults suite, `com.626labs.sanduhr.desk`), and the Desk reading
  `UsageViewModel` directly instead of `snapshot.json`.

## M1. Tiers that keep up (M)

Port: `TierModel.cs`, the limits synthesis in `UsageFetcher.cs`, `PlanLabel.cs`,
`ClaudeApiParsing.cs`. Specs: `2026-07-19-scoped-limits-wave-design.md`,
`2026-06-06-subscription-tier-badge-design.md`.

- Replace the fixed `Tier` enum with a key-based tier registry: static canonical order plus
  dynamic `seven_day_<slug>` keys synthesized from `limits[]` (`kind == weekly_scoped`), never
  overwriting a flat key; `EffectiveOrder` used by every consumer (cards, alerts, history, snapshot).
- Routines tier from `/v1/code/routines/run-budget` (resets_at may be null: "unknown", not "none").
- Speculative-tier "future use" tag.
- Plan badge in the footer from the org capabilities.
- Unknown-key logging (key names only, never values).
- Tests: port the Windows limits-synthesis and plan-label fixtures.
- Accept: a payload with a new model in `limits[]` renders a card labeled "Weekly - <Model>"
  with no code change; snapshot `tiers` carries it; alerts fire for it.

## M2. Accounts (M)

Port: `AccountStore.cs`, `AccountOrigin.cs`, `CredentialStore.cs`. Spec:
`2026-07-11-auth-accounts-overhaul-design.md`.

- Multiple accounts (label, origin, credentials), one active; Accounts tab (add, rename,
  switch, sign out one, delete completely); active-account label in the widget.
- Sign out / clear credentials (Credentials tab).
- `history.json` becomes `history.<account>.json` with the Windows legacy migration; snapshot
  `account_ref` = first 8 hex of SHA-256 of the label (`SnapshotContract.AccountRef`).
- Keep credentials in the 0600 file for ad-hoc builds; Keychain behind a flag for signed builds.
- Accept: two accounts switch without restart; each keeps its own history; sign-out of one
  leaves the other working.

## M3. Sign in inside the app (M)

Port: `ClaudeSignIn.cs`, `ReauthRouting.cs`, `SignInPromptCopy.cs`, `SignInReason.cs`. Specs:
`2026-06-22-login-recovery-hardening-design.md`, `2026-07-19-email-code-signin-design.md`.

- WKWebView sign-in sheet on claude.ai (non-persistent data store); read the `sessionKey` cookie
  on success; Google SSO and email-code guidance copy.
- Expired session: in-place reauth routed by account origin; "Reconnecting" status.
- Keep paste-the-cookie as the fallback.
- Accept: a fresh install signs in without DevTools; an expired key recovers from the widget.

## M4. Cards and graphs (S)

Port: card order and hide settings, `SparklineStyle.cs`, `HorizonBands.cs`, `UsageHistory.cs`
retention.

- Cards tab: drag to reorder, hide per tier (works with dynamic tiers from M1).
- Graph-mode cycle on the tool strip (sparkline, horizon, off).
- History retention 8640 points (30 days of five-minute ticks), matching `UsageHistory.MaxHistory`.
- Accept: order and hidden tiers survive restart; a 30-day horizon graph draws from history.

## M5. Local Claude Code burn (M)

Port: `CcLogReader.cs` (and its tests), the burn delta in the card and footer views.

- Read `~/.claude*/projects/**/*.jsonl` for consented homes only (consent per home in a
  "Claude Code" settings tab, all off by default).
- Worktree and subfolder folding to the repository name (3.4.1 and 3.4.2 rules); mtime filter,
  single-pass aggregation, 30 s cache.
- Burn delta since the last fetch on cards and footer.
- Accept: token totals for a test fixture match the Windows `CcLogReader` tests.

## M6. Usage vault (L)

Port: `VaultModels.cs`, `VaultStore.cs`, `VaultIngester.cs`, `VaultReader.cs`, `VaultJson.cs`,
`VaultLedgerCsv.cs`. Spec: `2026-07-12-usage-vault-design.md` (binding).

- `~/Library/Application Support/Sanduhr/vault/<home>/`: monthly session shards, rollups as a
  rebuildable cache, path-hash checkpoints written last, timestamped quarantine of torn files,
  walk-version re-ingest, single writer.
- Opt-in per home (shares M5 consent); pause per home; erase all.
- Tests: port every Windows vault fixture (month boundaries, torn writes, resurrected files,
  worktree folding, subagent runs folded into the parent).
- Accept: ingest is idempotent (two runs, identical files); deleting rollups rebuilds them.

## M7. History window (L)

Port: the Claude Usage tab views (`CcCalendarControl.cs`, `CcTrendsControl.cs`,
`LocalCcBarStrip.cs`, `HistoryChart.cs`), `CsvExport.cs`.

- A History window (tool strip and status menu): Overview (today and last 30 days, sent and
  received, 30-day bar strip, five-week calendar, click a day for its sessions), Trends (weekly
  bars to 26 weeks), Sessions ledger (scope chips, per-project stacking, sortable, CSV export).
- Utilization history chart with per-account / all-accounts toggle and CSV export.
- Settings show vault size and oldest day, export (zip) and erase per home.
- Accept: the calendar and ledger agree with the vault for a fixture month; CSV opens in Excel.

## M8. MCP and installs (S)

Port: `ToolLogic.cs` burn, model-usage and history builders; `McpIntegrationInstaller.cs`,
`StatuslineInstaller.cs`.

- `get_local_burn_by_project`, `get_model_usage`, `get_usage_history` in
  `mac/integrations/sanduhr_mcp.py`, reading the vault and consented homes only, same shapes.
- Settings, Claude Code: "Install MCP server" and "Install statusline" with the Windows consent
  flow (pick the home, timestamped backup, revert only what Sanduhr wrote).
- Accept: Windows MCP test vectors produce the same JSON from the Mac server.

## Later

Help tab, custom chimes, themed dialogs and the theme swatch flyout. Dropped: publish usage,
Theme Studio and lint, `propose_theme`, Store update notice.
