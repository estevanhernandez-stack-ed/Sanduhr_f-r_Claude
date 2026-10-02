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
