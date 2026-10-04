# Sanduhr für Claude — macOS (SwiftUI)

Native Mac rewrite of the [Python/tkinter widget](../sanduhr.py). Feature parity
plus real vibrancy, SF Pro, and no dock icon.

## Build

Requires macOS 14+ and Xcode 15+ command-line tools (`xcode-select --install`).

```
./build.sh                 # release build → Sanduhr.app, universal (arm64 + x86_64)
SIGN_IDENTITY=- ./build.sh # same, ad-hoc signed (no Developer ID needed)
./build.sh --debug         # native-arch debug build (faster iteration)
./test.sh                  # swift test, with the SDK and plugin paths the command line tools need
```

Smoke tools for a running dev build (window shots, the UI tree as YAML, scenarios): see
[smoke/README.md](smoke/README.md).

Run the result:

```
open Sanduhr.app
```

## Release

Signed, notarized releases are built by GitHub Actions; setup and steps are in
[docs/mac-release.md](../docs/mac-release.md).

## Package as a drag-install DMG

For the classic "drag the app onto Applications" experience:

```
./make-dmg.sh              # builds app if needed, produces Sanduhr.dmg
./make-dmg.sh --skip-build # reuse existing Sanduhr.app
```

`open Sanduhr.dmg` mounts a window showing the app next to an Applications
alias — drag across to install, eject the DMG, done. Uses only `hdiutil` +
`osascript`, no Homebrew or extra tools.

## First run

1. Go to <https://claude.ai>, sign in.
2. Open DevTools (⌥⌘I) → Application → Cookies → `claude.ai`.
3. Copy the `sessionKey` value.
4. In Sanduhr, click **Continue** on the onboarding sheet, then paste the key.

Where the key is stored depends on the build:

- **Release builds** (signed with the Developer ID, as every published release is): the
  macOS Keychain, as generic-password items under service `com.626labs.sanduhr` (accounts
  `sessionKey` and `cf_clearance`), readable after first unlock with no Touch ID prompt. The
  item trusts Sanduhr's signature, so updates read it without asking. Updating from 2.3.1 or
  earlier moves the key out of the old file once: Sanduhr writes the Keychain, reads it back,
  and only then deletes the file; if anything fails it keeps the file and carries on with it.
- **Dev builds** (`./build.sh --debug`, ad-hoc signed): a permissions-restricted plaintext file
  at `~/Library/Application Support/Sanduhr/credentials.json` (mode `0600`, readable only by
  your user account). Every ad-hoc rebuild has a new signature, which would make the Keychain
  ask for your login password each time. `SANDUHR_KEYCHAIN=1` in the environment forces the
  Keychain on a dev build to test that path; expect that prompt after a rebuild.

`smoke/smoke state` reports which one is in use as `credentials_store`.

### Accounts

Sanduhr keeps named Claude accounts (2.4.0+, ported from Windows 2.2). One is active: only it
is shown and fetched every 5 minutes. **Settings, Accounts** lists them with the active one
marked; for the selected account it has the key fields (Save), Rename, Sign Out, Remove
Account and Make Active, and **Add Account…** takes a label (1 to 32 letters, digits, spaces,
underscores or hyphens) and a key. With two or more accounts the widget's title area shows
the active label as a chip (click it for the next account), the widget and menu bar menus gain
an **Accounts** submenu (each account, then Manage Accounts…), and Desk's claude line starts
with the label. The notch stays as it is. A switch never needs a relaunch: the meters clear,
"Switching account…" shows, and the new account's history and numbers load.

- **Slots.** Each account has its own pair in the store the build uses (Keychain or file),
  `sessionKey:{label}` and `cf_clearance:{label}`, the Windows slot names. Updating from 2.3.x
  moves the single saved key to an account called **Personal** (written, read back, and only
  then the old slots deleted). A fresh install creates Personal with the first key saved.
- **Registry.** The labels and the active one are in the app's defaults (`accounts`,
  `activeAccount`), so menus and Settings never touch the Keychain to list them.
- **History.** One file per account, `history.{label}.json`; the old `history.json` becomes
  Personal's once. Rename moves the file, Remove deletes it, Sign Out keeps it. Each keeps 30
  days of readings (item 43), unless that account's Meter history is Off (below).
- **Data (item 44).** Settings, Accounts, Data holds each account's Meter history, Claude Code
  folder, Claude Code activity (`off`/`live`/`record`), Project names (`names`/`hidden`/`full`)
  and Share with Claude (`off`/`meters`/`activity`), in defaults `accountData`
  (`{label: {activity, names, share, folder}}`, missing means the default: nothing linked,
  tracked or shared, names). They follow Rename and go with Remove. Folders are found in the
  home folder (`.claude`, `.claude-*`, and `CLAUDE_CONFIG_DIR` when set) when they hold
  `projects/` or their `.claude.json` (beside the home for `~/.claude`, inside it otherwise, as on
  Windows); Choose… takes any folder that looks like one. One folder per account, one account
  per folder; a folder linked elsewhere moves only after a confirmation. The suggestion compares
  `oauthAccount.organizationUuid` (the only field decoded) with the account's organization, in
  memory. Sharing goes to the MCP server through `mcp-access.json` (item 47, see Claude Code integrations).
- **Live Claude Code activity (item 45).** With activity Live only or Keep a record and a linked
  folder, the widget reads that folder's session logs (`projects/<project>/**/*.jsonl`, nested
  subagent transcripts included) for the shown account and puts a small "+Nk" before a card's
  percent: the input and output tokens Claude Code used in that limit since the last refresh
  (opus, sonnet and haiku models map to the Opus, Sonnet and All Models weekly cards, as on
  Windows). claude.ai's numbers lag by minutes and the widget asks every 5; the badge covers
  both. It starts again from zero at each refresh and grows with a scan every 30 seconds, off
  the main thread. `CCLogReader` is a port of Windows `CcLogReader` (worktree and subfolder
  folding for project names, burn for a local day, by model, by project, by skill); it reads
  only the timestamp, model, token counts, cwd and skill of `assistant` lines, never message
  content, and keeps its parse in memory: per file the offset of the last whole line, so a
  grown file is read from there (after checking the 64 bytes before it) and an unchanged one
  isn't opened. Not tracked, or no folder, opens nothing. Nothing is stored.
- **The vault (item 46).** Keep a record writes the Windows usage vault, same formats and file
  names (`docs/superpowers/specs/2026-07-12-usage-vault-design.md`), for every account whose
  activity is Keep a record with a folder linked, not only the shown one:
  `~/Library/Application Support/Sanduhr/vault/<folder id>/` holds `sessions-YYYY-MM.json`
  (the record: one row per session per month it touched, continuation rows for later months,
  nested subagent transcripts as their own rows with `parent_session`), `rollups-YYYY-MM.json`
  (a cache rebuilt from the month's sessions), `checkpoints.json` (per log file, keyed by the
  SHA-256 of its lowercased path: size, .NET-ticks mtime, offset, the 64-byte tail guard, the
  months it touched) and `meta.json`. The folder id is the first 16 hex digits of the SHA-256 of
  the folder's standardized path, lowercased (Mac volumes are case-insensitive; checkpoint keys
  fold the same way, as on Windows). `VaultIngester` is a port of Windows `VaultIngester`:
  ingest order shards, rollups, checkpoints last; a grown file is read from its offset after the
  tail guard checks out, a shrunk or rewritten one whole, a file quiet for an hour once more and
  then sealed; a torn last line is never consumed; an unreadable shard becomes a timestamped
  `.bad` (never deleted) and the folder's checkpoints go, so the next cycle rebuilds. One writer:
  an `flock` on `vault/.writer.lock` in place of the Windows named mutex (a second Sanduhr skips
  its cycle). `VaultService` runs a cycle at launch, after each refresh and when a choice changes,
  on a utility queue, one at a time. Project names: Names as Windows (`name~` + 8 hex of the
  cwd's hash); Hidden `p-` + 10 hex of the SHA-256 of the folded project name in `project_key`
  and `project_name`, never the name or cwd; Full paths keeps `cwd`. Erase: the activity leaves
  Keep a record first (the tombstone), then the writer lock is taken (up to 10 s; an in-flight
  cycle checks the choice before each file and before writing, and stops), then the folder's
  directory is deleted. `VaultReader` (by day, week, project, model, tier, skill, sessions,
  coverage) and `VaultLedgerCsv` are pure and tested, for items 47 and 48.
- **Share with Claude (item 47).** `MCPAccess` (pure, tested) turns the accounts' choices into
  `mcp-access.json`; the view model rewrites it from `reloadAccounts`, so every change to a choice,
  a link, the account list or the active account lands, atomically (a temp file renamed over it,
  mode 0600) and only when its bytes change. The Project names picker is enabled for Keep a record
  or Meters and activity.
- **The Claude Usage page (item 48).** A Settings section under Accounts (`SettingsSection.usage`;
  Tools, Claude Usage… in every menu opens it), chosen over a separate window because the choices
  that decide what it can show are in Accounts, Data one row up, and Settings is already the one
  window every menu, Option+S and the notch open. One account at a time (the active one, a picker
  with two or more), three tabs. Overview: `CCLogReader.days` (one pass, per local day) for the
  live side, the vault's rollups for closed days. The hot-day rule: days before the local date of
  the record's last finished pass come from the record, that day and later from the live reader,
  never both; a pass older than 15 minutes (three cycles) or none at all is degraded mode, the
  whole 30 days live with a status line, and a page that finds yesterday still hot asks for one
  pass. Live only reads the live side alone. Trends: `VaultReader.readWeeks` and `topProjects`
  over 4, 12 or 26 weeks; a week without tokens and with an uncovered day (before `meta.since`,
  or a gap in `covered`) is drawn with a dot texture, never as a zero bar, and the current week
  is hatched. Sessions: `readSessions` rows keyed `root|uuid`, scoped tokens from each session's
  `by_day` for Today, Yesterday, 7d (default) or All, sorted with a typed comparator, rows
  expanded by id, in a `List` that owns its scrolling; Export CSV… sends the rows as shown
  through `VaultLedgerCsv` to an `NSSavePanel` file (root = the folder's name). Project names are
  whatever the record holds, and live days are named under the account's choice (`p-` codes for
  Hidden), never resolved back. Records on this Mac (`VaultStewardship`) lists every
  `vault/<id>` folder with its size, oldest day and linked account, or none; Erase… goes through
  `VaultService.erase(id:)`, after switching a still-recording account to Live only. The models
  (`UsagePage.swift`, `UsagePageLoader.swift`) are pure and tested on synthetic vaults; a 6,000-session
  record reads, sorts, rescopes and exports in well under a second. Reloads follow each ingest
  cycle (`VaultService.onCycleEnd`), each refresh, and every minute on Overview.
- **Readers.** `snapshot.json` names the active account by `account_ref` (first 4 bytes of the
  SHA-256 of the label, as on Windows) and is deleted at once on a switch; `state.yaml` has
  `account_ref`, `accounts_count`, `history_days`, `data` (the active account's choices and
  `folder_linked`), `local_activity` (`reading`, and `events` counted since the last refresh),
  `vault` (`recording`, `months`, `last_ingest_ok`), `usage_page` (`open`, `tab`), `follow` and
  `follow_paused`. Labels and folder paths never go into a log,
  `snapshot.json` or `state.yaml`.

#### Following the account in use

**Follow the account I'm using** (Settings, Accounts, shown with two or more accounts) is off by
default. With it on, every 15 minutes each signed-in inactive account gets one usage request
through its own client (its organization is looked up once, then cached). An account is *in
use* when its five-hour session rose since its previous check; a reset is not use. Sanduhr
switches only on a clear signal: one other account in use and the active one idle over the
same 15 minutes. When both are in use it stays, and the menu marks the other "· in use" with a
dot on the chip. At most one automatic switch per 30 minutes; switching by hand (chip, menu,
Settings) pauses following for 3 hours, or until the account you picked has been idle for 30
minutes. After an automatic switch the chip and Desk's line read "Work (in use)" for a minute.
No notification. A failed check is ignored; a refused key skips that account until a new key
is saved for it. The decision is `AccountFollow.decide`, pure and unit-tested.

### Signing out

**Settings, Accounts, Sign Out** (after a confirmation) deletes the selected account's session
key and `cf_clearance` from both the Keychain and the file on a release build. A dev build clears
only the file: it never deletes a Keychain item, which belongs to the release build on the same
Mac. Only Personal's Sign Out also clears the pre-accounts `sessionKey` slots. The account stays in the list, signed out, with its history. Signing out the active account stops
the refresh and clears the shown usage: the widget says "Signed out — sign in" (click it for
Settings, Accounts), Desk and the notch show the sign-in line instead of meters, and
`snapshot.json` says `session_expired` with no tiers so the statusline and MCP server stop
showing old numbers. Paste a key into the account and Save to sign in again; no relaunch
needed. **Remove Account…** (its own confirmation) signs out, deletes the account's history
file and drops it from the list; removing the active account switches to the next one.
Signing out only removes the key from this Mac; it does not end the session on claude.ai.

Dragging Sanduhr to the Trash removes neither store: sign out first, or delete the
`com.626labs.sanduhr` items in Keychain Access (release builds) or
`~/Library/Application Support/Sanduhr/credentials.json` (dev builds).

### Cloudflare fallback

If the widget shows "Cloudflare — add cf_clearance", copy the `cf_clearance`
cookie the same way and paste it into the second field in **Settings, Accounts**.
Most accounts don't need this.

## Claude Code integrations

`mac/integrations/` holds a statusline segment and the `sanduhr` MCP server, Python 3 standard
library only. `bash mac/integrations/install.sh` copies both to
`~/Library/Application Support/Sanduhr/integrations/` and registers the server with Claude Code
(`claude mcp add sanduhr --scope user`); `--remove` undoes it. The statusline reads only
`snapshot.json`.

The server (`sanduhr_mcp.py`) speaks the Windows `sanduhr-mcp` protocol with the same tool names
and result shapes: `get_usage`, `get_local_burn_by_project`, `get_model_usage`,
`get_usage_history`, `ping`. `publish_usage` is dropped on the Mac and `propose_theme` is not
ported. What it may read comes from `mcp-access.json`, which the app writes:

```json
{ "schema_version": 1,
  "accounts": [ { "account_ref": "1a2b3c4d", "active": true, "share": "activity",
                  "history_file": "history.Work.json", "names": "hidden",
                  "vault_id": "0123456789abcdef", "live_folder": "/Users/me/.claude-work" } ] }
```

Only accounts that share are listed. `names` and `live_folder` come with `activity` and a folder
that is read (Live only or Keep a record), `vault_id` with `activity` and Keep a record.
`live_folder` is there because the burn and model tools read the raw session logs for rolling
1, 7 and 30-day windows, as Windows does; the vault holds whole local days only.

| Tool | Off | Meters | Meters and activity |
| --- | --- | --- | --- |
| `get_usage` | `not_shared` | the active account's meters | also `local_burn_since_snapshot` from its folder |
| `get_local_burn_by_project` | nothing | nothing (`disabled` when no account shares activity) | tokens by project per account (`root` is its `account_ref`), names as chosen |
| `get_model_usage` | nothing | nothing | tokens by model; the meter join only with the snapshot of an account in the totals |
| `get_usage_history` | nothing | `meter_history`: daily peak per limit | also the record's days and top projects (Keep a record) |
| `ping` | counts only | counts only | counts only |

No access file, an unreadable one or another `schema_version` shares nothing (`not_shared`, and
`ping.sharing.access_file` says why). Hidden returns the record's `p-` code for every project,
full paths only for Full paths. The server never takes a path argument, never writes, never
logs and makes no network request. `SANDUHR_SUPPORT_DIR` points it at a test folder.

Tests: `python3 -m unittest discover -s mac/integrations/tests` (also a Mac CI step), over temp
folders: each sharing level, no access file, hidden names, and the Windows MCP tests' cases.

## Files

- `sessionKey:{label}` + `cf_clearance:{label}` per account → the Keychain, service `com.626labs.sanduhr` (release builds), or `~/Library/Application Support/Sanduhr/credentials.json` (mode `0600`, dev builds); see First run and Accounts above
- Account labels and the active one → `UserDefaults` (`accounts`, `activeAccount`); following → `followAccount`
- Selected theme → `UserDefaults` (`theme`)
- Meter history → `~/Library/Application Support/Sanduhr/history.{label}.json`, one per account, the Windows format. Each reading is kept 30 days (and at most 8640 points per limit, Windows' cap), trimmed when the next one is written; the sparklines draw the last 24 points (about 2 hours). Settings, Accounts, Meter history: Off stops recording an account (`UserDefaults` `meterHistoryOff`, the labels switched off) and offers to erase its file; Remove Account deletes it. `state.yaml` shows the active account's `history_days` (30, or 0 when off)
- Data choices per account (Claude Code folder, activity, project names, Share with Claude) → `UserDefaults` (`accountData`); the linked folder's path stays there, never in `state.yaml`
- What the MCP server may read → `~/Library/Application Support/Sanduhr/mcp-access.json` (mode 0600; see Claude Code integrations)
- Window position → `UserDefaults` (`windowFrame`)

## Controls

| Gesture                     | Action                              |
| --------------------------- | ----------------------------------- |
| Drag anywhere               | Move the widget                     |
| Drag any edge or corner     | Resize the widget                   |
| Double-click title          | Toggle compact mode                 |
| Click the account chip      | Switch to the next account (2+ accounts) |
| Click the account name on Desk | Switch to the next account (2+ accounts) |
| Click theme name            | Switch theme                        |
| **Focus** button            | Swap tier cards for hourglass timer |
| **Snake** button            | Play the cooldown snake game        |
| **Graph** button            | Cycle sparkline: Classic / Horizon  |
| **Pin** button              | Toggle always-on-top                |
| **Refresh** button          | Fetch usage now                     |
| **Gear** button             | Open Sanduhr Settings               |
| Hover a tier card           | Reveal cooldown / surplus metrics   |
| Two-finger click widget     | Tools, Refresh, Settings, Quit menu |
| Two-finger click a tier card | Accounts, Hide (temporary limits), Stop warnings, Hidden Limits, Meter Settings, then the widget menu |
| Click a Desk meter          | Nothing: the meters are passive, clicks there do nothing |
| Two-finger click a Desk meter | Show or Hide Widget, then the same limit menu |
| Right-click the hourglass   | The widget menu plus Menu Bar Shows (Session, Weekly, Whichever is higher, Rotate) |
| **×**                       | Hide the widget (Desk keeps running) |

## License

MIT. Python original by [626Labs LLC](https://626labs.dev).
