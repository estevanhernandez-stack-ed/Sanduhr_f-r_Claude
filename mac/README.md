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
- **One-click install (item 49).** `SettingsSection.integrations`, under Claude Usage: installing the
  MCP server is the other half of Share with Claude, and its consent sheet points back at Accounts.
  `IntegrationScripts`, `IntegrationInstaller`, `JSONEdit` and `PythonFinder` are pure or work on
  injected folders, and are tested on temp homes (see Claude Code integrations).
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

`mac/integrations/` holds a statusline segment and the `sanduhr` MCP server, Python 3.9+ standard
library only, and the `sanduhr-meters` Claude Code mod. The statusline and the mod read only
`snapshot.json`.

**Installing from Settings (item 49).** Settings, Integrations lists the Claude Code folders
(`ClaudeCodeFolders.discover`, the accounts' linked folders, folders installed into, and Add
Folder…) with Install, Update or Remove for each integration, behind a consent sheet. No `claude`
CLI: `IntegrationInstaller` writes the entries itself, the way Windows' `McpIntegrationInstaller`
and `StatuslineInstaller` do:

```jsonc
// <folder>/.claude.json, or ~/.claude.json for ~/.claude (the placement rule)
"mcpServers": { "sanduhr": { "type": "stdio", "command": "/usr/bin/python3",
  "args": ["~/Library/Application Support/Sanduhr/integrations/current/sanduhr_mcp.py"] } }
// <folder>/settings.json
"statusLine": { "type": "command",
  "command": "/usr/bin/python3 '~/Library/Application Support/Sanduhr/integrations/current/sanduhr_statusline.py'" }
```

(paths absolute in the files). The file is checked as strict JSON first (a malformed file is never
written; the row and the error say so); `JSONEdit` splices the one member into the text, copying
the file's indentation, so every other byte stays; the result is parsed and compared with the
original minus that member before writing; `<file>.sanduhr-backup` is kept before the first
change; the write is a temporary sibling renamed over the file (a symlinked file is written
where it points, permissions kept), retried up to three times if the file changed meanwhile.
`integrations/installs.json` records what each install did (file created, `mcpServers` created,
the inside of an object it filled from empty, the value it replaced), so Remove undoes exactly
that: byte for byte when nothing else changed, someone else's statusline put back. An entry is
Sanduhr's when it runs `sanduhr_mcp.py` (or Windows' `sanduhr-mcp`), or when the statusline
command is only `<python> <…/sanduhr_statusline.py>`; a statusline of your own that calls the
script among other commands is never touched. Status: Installed (names the current link and an
existing python3, scripts current), Outdated (Sanduhr's entry naming other scripts, such as
install.sh's copies, or a missing python3: Update rewrites it), Not installed, someone else's
entry, or not valid JSON.

**Scripts.** `build.sh` copies both into `Sanduhr.app/Contents/Resources/integrations/`.
`IntegrationScripts` copies them to `integrations/<stamp>/` (12 hex of a SHA-256 over the
scripts' names and bytes) and points the symlink `integrations/current` at it with one
`rename(2)`; the settings name `current/…`, so an app update never rewrites Claude Code's files.
It is Windows' stamped-folder design: there a running session pins the exe; here nothing is
locked, but a session starting mid-update must read the old set or the new one whole. The
refresh runs on install and at launch when `current` exists; the folder swapped out is kept one
refresh, older stamps are deleted, and Remove of the last install deletes them all. install.sh's
flat copies beside them are never touched.

**The meters mod (item 50).** `mac/integrations/mods/sanduhr-meters/` is a Claude Code mod (a
plugin of function hooks: `.claude-plugin/plugin.json`, `hooks/hooks.json`, `hooks/register.tsx`,
the pure logic in `hooks/meters.ts`, its state contract in `types/index.d.ts`). It needs a Claude
Code version that loads mods (`claude plugin test` exists). It draws a band above the prompt
from `snapshot.json`, read with `$.fs.read` on session start and every 30 seconds:

```
Session ▕█████│▏░░░▏ 61% resets 2h 14m   Weekly ▕███████│█▎▏ 93% ⚠ resets Tue
```

The bars are the widget's colors (green, yellow, orange, red from 90%) with eighth-cell fill, the
pink `│` is where pace says usage should be now, and ⚠ is the widget's meter warning (weekly 90%
with more than a day left; the session at 90% with more than an hour left, the widget's rule for
it when switched on). Stale (7.5 to 15 minutes) dims the band and adds `(9m ago)`; older is one
line, `Sanduhr: no update for 22m. Is the widget running?`; signed out is `Sanduhr: sign in to see
your meters.`; a failed fetch keeps the last numbers dimmed with `last known, offline`; a limit
whose reset passed shows `reset` instead of its stale percent; a newer schema asks for an update;
no snapshot draws nothing. Options (`userConfig`, Claude Code's config menu): `bars` (both,
session, weekly), `style` (compact, one line; full, a line per meter with the pace words and the
reset time, folding to compact when the band is short or narrow) and `label` (a name of your own
shown first; the snapshot only carries `account_ref`). Toasts, at most once per limit and reset
window across sessions (keys kept in `$.store`): when a limit starts warning, and when the
session limit resets (within ten minutes of it). Redraws come from a changed file or the minute
tick, and the tick redraws only when a countdown or age it shows changed. Windows reads
`%APPDATA%\Sanduhr\snapshot.json`; `SANDUHR_SNAPSHOT` names another file for testing. Tests:
`claude plugin test mac/integrations/mods/sanduhr-meters` (the engine's own test kit; the band is
mounted on the terminal surface with the file system, clock, env and store beneath mocked).

Install adds the mod's folder, `~/Library/Application Support/Sanduhr/integrations/current/mods/sanduhr-meters`,
to `env.CLAUDE_CODE_PLUGIN_DIRS` in the chosen folder's `settings.json` (the user settings, where
Claude Code reads that variable), joined with `:`, the path-list separator Claude Code splits it on
here (`;` on Windows): the `env` object and the key are made when missing, an existing list keeps
every entry and gets the mod last, an older Sanduhr entry is replaced where it stands. Remove takes
out only entries that are Sanduhr's mod (a folder ending in `mods/sanduhr-meters` inside
`Sanduhr/integrations/`; a copy elsewhere is yours), deletes the key or `env` only when Sanduhr made
them and nothing else is left, and puts the original bytes back when the list is otherwise what it
was. The mod needs no python3. The mod's files ride in the stamped folder (`build.sh` bundles it
without its tests); Claude Code writes its type declarations into `.claude-plugin/types/` of a mod
folder it loads, and the stamp covers only the shipped files, so that never reads as altered.

**The notch glow hooks (item 51).** The fourth kind needs no scripts and no python3: Install adds
one matcher group to each of `hooks.Notification` and `hooks.Stop` in the chosen folder's
`settings.json`, in the shape of Claude Code's settings schema (`{matcher?, hooks: [{type:
"command", command, async, timeout}]}`):

```json
"Notification": [{ "matcher": "permission_prompt|idle_prompt|elicitation_dialog",
                   "hooks": [{ "type": "command", "async": true, "timeout": 5,
                               "command": "/usr/bin/pgrep -xq Sanduhr && /usr/bin/open -g 'sanduhr://claude-code?event=waiting' || true" }] }],
"Stop":         [{ "hooks": [{ "type": "command", "async": true, "timeout": 5,
                               "command": "/usr/bin/pgrep -xq Sanduhr && /usr/bin/open -g 'sanduhr://claude-code?event=done' || true" }] }]
```

`async` runs it in the background, so Claude Code never waits on it; `open -g` hands the link to
Sanduhr without bringing it forward; `pgrep` keeps a quit Sanduhr from being launched by every
turn; `|| true` keeps Claude Code from reporting a hook error. The matcher keeps sign-in and other
notices out of "waiting". `hooks` and the two lists are made when missing; an existing list keeps
every entry and gets Sanduhr's last; an older entry of Sanduhr's is rewritten where it stands. A
group is Sanduhr's when every hook in it is a command opening `sanduhr://claude-code?`; a group of
yours that also runs it is yours and stays. Remove takes Sanduhr's groups out in the reverse
order, deletes a list or `hooks` only when Sanduhr made it and nothing else is in it, and gives an
emptied list or object back what it held, so the file is byte for byte what it was (the receipt's
`hooks` field records what was made). `JSONEdit` gained array appends, replacements and removals
for this.

The app side: `ClaudeCodeLink` accepts `sanduhr://claude-code?event=waiting|done` only (the host,
no path, exactly one `event` item with a known value; anything else on that host is dropped) and
needs no debug gate. `NotchGlowController.claudeCode` glows when the event's switch is on
(`notchGlowClaudeWaiting`, `notchGlowClaudeDone` in the Desk defaults, off by default), the front
app isn't a terminal or editor in `ClaudeCodeGlowRules.terminalBundleIDs` while
`notchGlowClaudeSkipTerminal` is on (the default), and `ClaudeCodeGlowLimiter` allows it (one glow
per kind per 20 seconds; `done` within 5 seconds of a `waiting` glow is dropped; only glows that
happen count). The glow is `fire(topFallback: true)`: around the island or the plain notch as
usual, the covered-strip rule included; with no notched screen, around a 200-point spot at the top
center of the main screen, the camera light's no-notch width (`glow_shape: top`).

**Python.** `PythonFinder` prefers `/usr/bin/python3` (stable across Homebrew changes), but only
when the developer folder it forwards to (`/var/db/xcode_select_link`, else the Command Line
Tools or Xcode) holds a python3, checked by file, because running the stub without them opens
macOS's install dialog. Then `/opt/homebrew/bin/python3`, `/usr/local/bin/python3` and
python.org's. Each candidate is run once with `-I -c` for its version (3.9+). With none, the page
explains and offers Install Command Line Tools… (`xcode-select --install`) only on a click.

**Scripts by hand.** `bash mac/integrations/install.sh` still copies both to
`~/Library/Application Support/Sanduhr/integrations/` and registers the server with
`claude mcp add sanduhr --scope user`; `--remove` undoes it. The page shows such an entry as
Outdated, and Update moves it to the app's copy.

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
- Claude Code integrations (items 49 to 51) → scripts and the meters mod in `~/Library/Application Support/Sanduhr/integrations/<stamp>/` behind the `current` link; what each install did in `integrations/installs.json` (mode 0600, holds folder paths); the entries themselves in the chosen folder's `.claude.json` / `settings.json` (the notch glow hooks in its `hooks`), with `<file>.sanduhr-backup` beside each. The mod's "already toasted" keys are in Claude Code's own store for the mod. `state.yaml` shows only `integrations: {mcp_installed, statusline_installed, meters_installed, hooks_installed}`
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
