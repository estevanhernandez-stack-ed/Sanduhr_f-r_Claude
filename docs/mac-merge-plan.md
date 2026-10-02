# Mac merge and parity plan

Sanduhr for Mac and Sanduhr Desk (`~/Projects/Sanduhr_Desk_Mac`) become one app, then the Mac catches up with Windows where it still matters. Claude's own app now covers most of the usage views Sanduhr pioneered; what it does not have is the widget and the long, browsable history. Those are the parity targets. Supersedes the order in `docs/mac-parity-plan.md` (kept for the full feature inventory).

## Decisions

- **One app.** Sanduhr Desk folds into `mac/` here. It keeps Homebrew and Sparkle, so existing users get it as an update, and one usage engine serves every surface.
- **Three surfaces, each switched on in Settings:** the widget (on by default, as today), the desktop layer (clock, meetings, message, corner layout), the notch (wings and strip). The desktop layer and notch are off by default for everyone else; calendar access is asked only when meetings are turned on.
- **Same files as Windows.** `snapshot.json` (done), history, and the vault use the Windows formats, so the MCP tools and any reader work the same on both.
- **Native arm64 in every release.** See phase 0.

## Phase 0: Apple silicon warning and releases (S, do first)

macOS warns that an app "will not work in a future version" when it runs an Intel-only (x86_64) build through Rosetta. Local builds here are arm64 (`mac/Sanduhr.app` is arm64; Sparkle is universal), so the warning points at an Intel-only published build or helper.

1. Check what is installed: `lipo -archs /Applications/Sanduhr.app/Contents/MacOS/Sanduhr` and `lipo -archs /Applications/Sanduhr.app/Contents/Frameworks/Sparkle.framework/Versions/B/Autoupdate`.
2. Add a `mac-release.yml` workflow on a GitHub macOS runner (full Xcode there): `./build.sh --universal`, sign, notarize, zip, update `docs/appcast.xml` and the Homebrew cask. Fail the job if `lipo -archs` lacks arm64.
3. Cut the next Mac release from it.

## Phase 1: Merge (M)

1. Branch `feat/mac-desk` from `feat/mac-custom-font`.
2. Move Sanduhr Desk's `Desk/` sources to `mac/Sources/Sanduhr/Desk/`. Drop its `Usage/` copies (they came from here) and its `UsageEngine`; the desk reads `UsageViewModel` directly instead of polling `snapshot.json`.
3. One `AppDelegate` owns the widget panel, the desk window and the notch wings window, created only when their surface is on.
4. One Settings window: the widget's tabs (Pacing, Themes, Look, Alerts, Credentials) plus Desk, Message, Notch, and a Surfaces switch list.
5. Migration: copy `com.626labs.sanduhrdesk` defaults into `com.626labs.sanduhr` once; keep `estedesk://` working beside a new `sanduhr://` scheme.
6. Neutral defaults for other users: system font, no message file until they write one, notch off.
7. Retire the Sanduhr Desk repo (README points here; archive it on GitHub).

## Phase 2: Widget parity (M to L)

| Item | Why | Size |
|---|---|---|
| Tiers from `limits[]` instead of the fixed `Tier` enum | the Mac goes stale each time a meter is added | M |
| Routines tier (`/v1/code/routines/run-budget`) | Windows shows it | S |
| Plan badge | | S |
| Tier reorder and hide | | S |
| Local Claude Code burn on cards (worktree and subfolder folding) | the double-lag compensator Windows has | M |
| Horizon graphs | needs phase 3 history | M |
| Real Keychain once signed with a Developer ID | credentials are a 0600 file today | S |

## Phase 3: History you can see and keep (L, the main event)

1. **Longer utilization history.** Mac keeps 24 points (2 hours) in `history.json`; Windows keeps 8640 (30 days, five-minute ticks) per account in `history.<account>.json`. Match Windows.
2. **Usage vault.** Port `VaultIngester`, `VaultStore`, `VaultReader` per `docs/superpowers/specs/2026-07-12-usage-vault-design.md`: per Claude Code home folders under `~/Library/Application Support/Sanduhr/vault/`, monthly session shards (raw model strings, unconditional totals, cache tokens), rollups as a rebuildable cache, path-hash checkpoints written last, quarantine on parse failure, opt-in per home. Never conversation content, never uploaded.
3. **History window.** Overview (today and last 30 days, sent and received, 30-day bar strip, five-week calendar), Trends (weekly bars up to 26 weeks), Sessions ledger (scope chips, per-project stacking, sortable, CSV export), subagent runs folded into their parent session.
4. **Keep more.** Settings show the vault size and oldest day, with export (zip of the vault folder) and erase per home.

## Phase 4: MCP parity (S once phase 3 lands)

`get_local_burn_by_project`, `get_model_usage`, `get_usage_history` in `mac/integrations/sanduhr_mcp.py`, reading the vault. Same tool names and shapes as `Sanduhr.Mcp`.

## Dropped or later

Publish usage, Theme Studio and lint, `propose_theme`, Store update notice: Claude's own app and Sparkle cover these, or they serve few users.

## Test plan per phase

Build with `./build.sh` on this Mac (26.x SDK without Xcode). Phase 3 gets Swift unit tests ported from the Windows vault fixtures (torn files, month boundaries, worktree folding) since the vault is the one irreplaceable record.

## Appendix: Windows feature inventory and Mac status (2026-10-02)

Taken from every Windows release in `CHANGELOG.md` (v1.0 to v3.4.2, `origin/main` at 86f2aa7, nothing newer upstream) and checked against `mac/Sources` on `feat/mac-desk`. Phase is where it lands in this plan; "drop" means Claude's own app or Sparkle covers it, or few users need it.

| Windows feature | Since | Mac today | Phase |
|---|---|---|---|
| Tier cards, sparklines, pace markers, burn projection | 1.1 | done | |
| Matrix and other themes, user themes, agent theme prompt | 1.1 | done | |
| Compact mode | 2.0.4 | done | |
| Pace ghost, breathing glass | 2.0.4 | done (theme effects) | |
| Horizon sparkline | 2.0.4 | partial (2 hour history only) | 3 |
| Advanced pacing tools, focus timer, cooldown snake | 2.0.4 | done | |
| Tool strip | 2.0.4 | done (ActionIconRow) | |
| Graph-mode cycling | 2.0.4 / 3.1 | missing | 2 |
| Edge-drag resize | 2.0.4 | partial (width only) | |
| Sign out / clear credentials | 2.0.4 | missing | 2 |
| Keyboard shortcuts, tooltips, first-run tip | 2.0.2 | done | |
| Help tab | 2.0.2 | missing | later |
| Extra usage tier ("Capped Extra Usage") | 2.2 | done (ExtraUsageCard) | |
| Multi-account registry, Accounts tab, active-account label, account-scoped sign-out | 2.2 | missing | 2 |
| Per-account history, All-accounts chart toggle, CSV export | 2.2 | missing | 3 |
| Routines tier | 2.3 | missing | 2 |
| Local Claude Code tab and burn delta on cards | 2.3 | missing | 2 |
| Cards tab: drag to reorder tiers, hide tiers | 2.3 | missing | 2 |
| Speculative-tier "future use" tag | 2.3 | missing | 2 (comes with limits[] tiers) |
| Custom sound chimes | 2.3 | missing (system sound only) | later |
| Embedded "Sign in to Claude" | 3.0 | missing (paste cookie) | 2 |
| Subscription-tier badge | 3.0 | missing | 2 |
| Themed dialogs, theme swatch flyout | 3.1 | partial | later |
| One-click in-place session recovery, Google SSO and email-code guidance, reconnecting status | 3.1 / 3.3 | missing | 2 (with embedded sign-in) |
| Claude Usage tab (overview, trends, sessions ledger, CSV) | 3.2 | missing | 3 |
| Usage history vault (opt-in, per home) | 3.2 | missing | 3 |
| Threshold alerts | 3.2 | done (branch) | |
| Model-scoped weekly meters from limits[] | 3.3 | missing (fixed enum) | 2 |
| Snapshot writer, statusline bridge | 3.4.0 | done (branch, manual install script) | |
| sanduhr-mcp get_usage, ping | 3.4.0 | done (branch) | |
| One-click MCP and statusline install from Settings | 3.4.1 | missing (install.sh) | 4 |
| MCP get_local_burn_by_project, get_model_usage, get_usage_history | 3.4.1 | missing | 4 |
| Deeper pacing in get_usage (cooldown, surplus, projected final) | 3.4.1 | done (branch) | |
| Worktree and subfolder burn attribution | 3.4.1 / 3.4.2 | missing (no burn yet) | 2 |
| Publish usage, publish_usage tool | 3.4.0 | missing | drop |
| Theme lint, Theme Studio, propose_theme | 3.4.1 | missing | drop |
| Store update notice | 3.4.2 | n/a (Sparkle) | drop |
| Mac only: font picker, subtle mode, desktop pin, Desk (desktop layer, notch) | | done (branch) | |
