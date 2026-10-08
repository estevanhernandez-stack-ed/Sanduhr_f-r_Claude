# Sanduhr für Claude — macOS (SwiftUI)

Native Mac rewrite of the [Python/tkinter widget](../sanduhr.py). Feature parity
plus real vibrancy, SF Pro, and no dock icon.

New to Sanduhr? The [setup guide](../docs/setup-guide.md) walks through signing in, the Desk, the notch,
accounts and Claude Code, one task at a time.

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

Click **Sign In to Claude…** on the welcome sheet. Sanduhr opens claude.ai's own sign-in in a
window of its own; once you're signed in, it keeps the session key and closes the window. The
window's web view stores nothing: its cookies live in memory for that one sign-in and are gone
when it closes, and no key, cookie or page is logged.

- **Signed up with Google?** Google refuses sign-in inside apps, and claude.ai sends an account
  made with Google to Google even from Continue with email, so the window can't finish it. When
  it lands on Google's sign-in, a panel takes over with the steps: sign in in your browser, copy
  the `sessionKey` cookie, and paste it (**Open claude.ai in Browser**, **Paste a Key Instead**).
- **Paste instead:** every sign-in window and the welcome sheet have **Paste a Key Instead**:
  on claude.ai, open DevTools (⌥⌘I) → Application → Cookies → `claude.ai`, copy `sessionKey`,
  and paste it in Settings → Accounts.
- The same window signs in a new account (**Add Account…**, **Sign In to Claude…**) and an
  account whose session expired (**Sign In Again…** on its page; a signed-in account offers **Replace Sign-In…** instead; the widget says "Session
  expired — sign in again").

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
  and Share with your agents (`off`/`meters`/`activity`), in defaults `accountData`
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
- **Share with your agents (item 47).** `MCPAccess` (pure, tested) turns the accounts' choices into
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
  MCP server is the other half of Share with your agents, and its consent sheet points back at Accounts.
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

**Combine with your statusline (item 63).** Claude Code has one `statusLine` and no way to
compose two, so when a folder already has one, the sheet offers Combine (the default), Replace or
Cancel. Combine writes a wrapper command:

```
<python> '<…/current/sanduhr_statusline.py>' --chain-b64 <base64 of your command> --join line|same [--padding <n>]
```

The script reads Claude Code's stdin JSON once and runs your command through `/bin/sh -c` with the
same bytes and environment, in its own process group, within 1.5 s (64 KiB of output kept); it
kills the group on timeout and when Claude Code cancels it (SIGTERM/SIGINT), and sets
`SANDUHR_CHAIN_DEPTH` so a combined line never chains again. `line` prints your rows, then
Sanduhr's on its own row; `same` appends `ESC[0m │ <segment>` to your last row and falls back to
`line` when that would not fit `COLUMNS − padding − 20` (escapes stripped, wide characters counted
as two). Your command hanging or failing never blanks Sanduhr's segment, a non-zero exit keeps
your output, the script always exits 0, and reasons go only to stderr (`claude --debug`). With a
dead or missing snapshot, the segment falls back on stdin's `rate_limits`, marked `*`
(`5h 23%* | wk 41%*`). The new value carries your `padding`, `refreshInterval` and
`hideVimModeIndicator` (Replace now does too). `isOurStatusline` accepts exactly that flag
grammar and nothing looser; an existing Sanduhr command, alone or combined (any seat or version),
is unwrapped to the innermost command of yours. The receipt gains `mode` (`replace` or
`combine`, optional so older receipts read); Update rebuilds the command from its shape, and
Remove puts back the exact bytes of your old `statusLine` as before. The row reads "Combined with
your statusline".

**Pick the segments (item 63b).** The grammar takes two more optional flags, in this order after
`--padding`:

```
… --join line|same [--padding <n>] [--keep-theirs-b64 <base64 of JSON>] [--mine <list>]
```

`--keep-theirs-b64` carries `{"keep": [...], "drop": [...], "new": false, "sep": [null, "pipe"],
"with": "bar"}` (every key optional): matchers kept and dropped, whether segments never seen stay
(`new`, default true), a separator per line to split on (null: detected) and the glyph to join
with (`with`: `bar`, `pipe`, `dot`, `bullet`, `powerline`, `powerline-thin`, `spaces`; absent
keeps your own separators). At run time each line of yours is cut on its
separator (`powerline` , `powerline-thin` , `bar` │, `pipe` |, `bullet` •, `dot` ·, `spaces`
for two or more, `none`), detected as the glyph the line holds most. A segment's matcher is its
leading token with escapes taken out: a word, `#` for a number, else the first glyph, so `⎇ main`
and `⎇ dev` are both `⎇` and a segment missing from a run shifts nothing. Dropped segments go and
the rest are rejoined on your own separators, each piece carrying the SGR state in force before it
and a reset after; powerline arrows between segments that weren't neighbors (and a new end cap)
are redrawn from the two backgrounds. A line nothing of survives goes; when nothing of yours is
left, Sanduhr's line prints alone, never the other way round. Any doubt (an unfinished escape, a
carriage return or other control character, an empty segment) keeps the line whole, and a line
where nothing drops prints byte for byte as it came, unless `with` is set. `with` replaces the
glyph between segments everywhere in the final line: between yours, between Sanduhr's (its usual
` | `) and at the same-row seam (its usual ` │ `); a powerline arrow takes the two neighbors'
backgrounds. `--mine` is a comma list in this order:
`session`, `weekly` (with hot per-model weeklies), `resets` (the default three), `context`
(`ctx 8%`) and `model` (`Opus`), both from Claude Code's stdin; notices (stale, update) always
show. Each flag is left out when it holds the default, so a combine without picks writes the same
command as before, and older combined commands read and run unchanged. Swift checks the payloads
exactly as the script does, so a hand-edited command it would refuse stays the user's.

The sheet runs `--inspect-b64 <base64 of your command>` once against the documented sample
statusline JSON (made-up numbers): your command runs, and the script prints JSON with your
output, each line split under every separator (the detected one named) and Sanduhr's segments one
by one. That becomes chips (`StatuslineChips`): yours in blue, Sanduhr's in amber, struck
through when dropped, chips that share a matcher toggling together, a line kept whole shown as
one chip that can't be clicked, one of Sanduhr's always kept. Each change runs `--compose-b64
<base64 of your output> --join … [picks]`, which prints what the runner would without running
your command again, and the preview draws it. Two controls sit apart: **Split yours on** (per
line, how the script reads your line into segments; it changes the chips, and the preview
composes again at once with the choice even when nothing is picked, while the command stays
without picks) and **Join with** ("Same as yours" or a glyph, which changes the line itself).
Every control has a caption under it and a tooltip.

**Mods in the picker.** "From your mods" lists the mods the folder loads that draw a status
entry: `env.CLAUDE_CODE_PLUGIN_DIRS` entries and `enabledPlugins` keys set to true (resolved through
`plugins/installed_plugins.json`), kept when `hooks/hooks.json` lists `modules` and the source
(`.ts`, `.tsx`, `.js`, `.mjs`, `.cjs`, `.jsx`, `node_modules` skipped, 400 files, 512 KB each)
calls `ui.status`; an `@inline` mod set to false in `enabledPlugins` is left out. Files are read
as text; no mod code runs and the `claude` CLI isn't called. The chips are read-only: Claude Code
draws those entries in its status area beside the statusline, so the statusline command can't
keep or drop them, and the sheet says to use the mod's own settings or turn it off for the
folder (a `userConfig` entry that mentions status is named in the chip's tooltip). The preview
adds a "Claude Code's status area" row with `⚠ <name>: …` placeholders, captioned that the text is
known only inside a session. With none: "No mods in this folder draw status entries."

**Test with live data.** The button builds the statusline input from the sample's shape with
snapshot.json's `five_hour` and `seven_day` (percent, and resets as epoch seconds), the current
time, and, from the newest `projects/*/*.jsonl` in the folder (its last 256 KB, only `model`,
`usage` and `cwd` of the last assistant turn), the model (`claude-opus-5-5` reads "Opus 5.5"),
context use (input plus cache tokens over a 200k window, 1M for `[1m]` or past 200k) and working
folder. It runs `--inspect-b64` again with that input, refills the chips keeping every pick (a
segment seen for the first time is a new chip, kept), composes the preview from it, and says
"Tested 12:04 with live numbers" (or which parts were sample values).

**Kinds and duplicates.** `classify` in the script names each segment of yours from its text,
escapes stripped, first fit wins: `branch` (⎇, U+E0A0, `git:`/`branch:`, `main`/`master`/`develop`
/`dev`/`trunk`/`HEAD`, `a/b` or kebab-case tokens with optional `*+!?`), `cost` (`$` then a digit, so
`$PATH` isn't), `model` (the whole segment is Opus/Sonnet/Haiku/Fable with an optional version, or a
`claude-*` id, so a branch `opus-fix` stays a branch), `context`, `session` (`5h`/`session`) and
`weekly` (`wk`/`weekly`/`7d`) each with a percent, `tokens` (`15.5k`, `1.2M`, `n tokens`), `time`
(clock-like), `directory` (`~`, `/`, `./`), else `segment` ("Your segment"). A leading icon and a
`model:`-style label are read past. `--inspect-b64` carries `kind` per segment; chips show the
kind's name, the live text and a tooltip ("Branch, from your statusline: ⎇ main"), in one chip
style for all three sections (blue yours, amber Sanduhr's, purple mods). `StatuslineDuplicate.find`
pairs what is kept: yours against Sanduhr's by kind (Model, Context, Session, Weekly), yours
against a mod whose name or description names the kind (git/branch, cost, model, context, and
usage words), and a usage-like mod (usage, limit, quota, meter, rate, token, or `sanduhr-meters`,
now listed even without a status entry, as "Above the prompt") against Sanduhr's Session and
Weekly. Both chips get a badge; a summary sits above the preview; yours against Sanduhr's offers
Keep yours (Sanduhr's segment goes, never its last one) and Keep Sanduhr's (yours is dropped); a
mod's duplicate only explains and points at its own settings or the Mods page.

**Styles.** The picks JSON takes `"style": {"<matcher>": {...}}` for yours and `"ours": {"session":
{...}}` for Sanduhr's. A style is `ink` (1 to 4 hex colors, `#rgb` or `#rrggbb`, `#` optional;
several are a per-character gradient), `font` (`bold`, `italic`, `bold-italic`, `sans`, `mono`,
`double-struck`, `script`, `fraktur`, `small-caps`) and the booleans `bold`, `italic`, `dim`,
`underline`; anything else, more than 64 entries or a key over 64 characters is refused by both
Swift and the script. `ink` and the letter-style key `font` follow the Desk's effects grammar so
item 65 can share them; there is no shimmer, since a statusline prints once per refresh. At run
time a styled segment of yours is rebuilt: with `ink` its own foreground colors go (backgrounds
stay, so a powerline block keeps its color) and each character gets its truecolor; the attributes
are set again after each of its own SGR escapes; letters map to the Mathematical Alphanumeric
forms with the Letterlike holes (italic h, script B E F H I L M R e g o, fraktur C H I R Z,
double-struck C H N P Q R Z), digits only in bold, double-struck, sans and mono, small caps from
the IPA and Latin Extended letters (x stays x); widths are unchanged; a reset ends it. Sanduhr's
parts are styled the same way. The chip's brush or its context menu opens the popover: Keep its
own colors (default), One color or Gradient (2 to 4 stops), Bold/Italic/Dim/Underline, Letters,
"Statuslines can't animate; the meters mod's band above the prompt draws Sanduhr's segments in
this look and moves it (sweep, shimmer, glow)." Mods' chips have no popover. Sanduhr's own looks
(session, weekly, resets) also reach the meters mod's band through `band.json` (below).

**Sanduhr's meters above the prompt instead (item 65f).** The Combine sheet's checkbox "Show
Sanduhr's meters above the prompt instead (animated)" adds a trailing `--band` to the combined
command (`StatuslineSelection.band`; the runner and `parseStatusline` both read it, and Update keeps
it): the runner then prints only their line, plus Sanduhr's context and model when `--mine` picks
them, and the session, weekly and reset segments (with any notice) are left to the meters mod's
band. Their segments are untouched; re-running their command in the band is out of scope. The mod
must be installed in the same folder (the caption says so); without it, Sanduhr's meters simply
don't show in that folder.

**Powerline glyphs.** U+E0B0 to U+E0B3 are Private Use Area glyphs only Nerd Fonts have, so
`PowerlineGlyph` draws them: menu items get a drawn icon and the label "Powerline arrow (needs a
Nerd Font)" / "Powerline thin arrow (needs a Nerd Font)" with the shapes described in the
tooltip; chips draw them inline; the preview cuts them out of the ANSI runs (`ANSIText.pieces`)
and draws the arrow in the run's foreground over its background, as a terminal would. U+E0A0
reads ⎇.

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

**The animated band and the watcher band (items 65f, 66; mod 0.2.0).** The mod also reads
`band.json` beside the snapshot every 2 seconds (`hooks/band.ts`, pure; `SANDUHR_BAND` names
another file for testing). The app writes it (`BandFile`, `BandFileWriter`), owner-only (0600) and
atomically, only when what it says changed, and deletes it when it would say nothing:

```json
{"meters":{"styles":{"session":{"bold":true,"font":"script","ink":["#ff2a6d","#05d9e8"]}}},
 "reduce_motion":false,"schema_version":1,
 "watchers":[{"done":4,"short":"CI","source":"agent","started_at":"…","state":"waiting","title":"CI on main","total":12},
             {"kind":"shell","source":"automatic","state":"running"}],
 "written_at":"2026-10-07T15:00:00.000Z"}
```

`meters.styles` are the Combine sheet's Style popover looks for Sanduhr's session, weekly and reset
segments (the statusline's style grammar; kept in `bandMeterStyles` in `com.626labs.sanduhr`, set
at each Combine, cleared by Replace): the band draws the meter's name and percent (session, weekly)
and the reset words (resets) in that look, one Text per character run with the ink's gradient,
the letter style mapped as the statusline maps it, bold, italic, dim and underline as Text props;
the bars keep the widget's usage colors. `reduce_motion` is macOS's Reduce Motion (rewritten when
it changes). `watchers` is there only while **Show watchers above the prompt** is on (Settings,
Integrations, Watchers; `watchersInBand`, off by default): the shown watchers in the board's order
(work ones left out in demo mode), an agent's as `title`, `short`, `state`, `done`/`total` and
`started_at`/`ended_at`, Claude Code's background work as `kind` and `state` only, never its
description, note or link. While watchers show the file is written again each minute; the mod
ignores watchers in a file older than three minutes (Sanduhr quit or crashed), and quitting writes
them empty.

What moves, drawn the way the owner's now-playing mod draws: a bar **sweeps** once when its limit
crosses a warning line (50, 75 or 90%) between two readings, a three-character light brightening
toward white across the bar and its percent in 1.1 s; a limit within ten minutes of its reset
**shimmers** (the light passes over its reset words every 4 s); a limit that warns **glows** (its
percent and ⚠ brighten and fade every 4 s). Watcher rows, one per watcher under the meters (at
most six, then "+N more", folding to what the band's rows allow): the state mark (● running, ◉
waiting on you pulsing amber, ✓ passed in green fading toward grey over its last seconds before
Sanduhr drops it, ⚠ failed in red, ○ lost touch and ✓ finished in grey), the title (the short one
below 60 columns; background work reads "background shell"), the time so far and `done/total`.
Frames: 80 ms (12.5 a second, under the engine's 30) only while a light, pulse or fade moves,
else once a second, and a frame redraws only when what the band shows changed (a minute's
countdown, a pace mark's cell, a watcher's seconds). Motion stops with Reduce Motion or the mod's
own `motion` option (`on`, `off`; Claude Code's config menu). A malformed `band.json` is ignored
whole (the meters draw as before); a malformed watcher row or style is dropped alone.

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

**The Claude Code glow hook (item 51; "the notch glow hooks" before Settings v2).** The fourth kind needs no scripts and no python3: Install adds
one matcher group to each of `hooks.Notification` and `hooks.Stop` in the chosen folder's
`settings.json`, in the shape of Claude Code's settings schema (`{matcher?, hooks: [{type:
"command", command, async, timeout}]}`):

```json
"Notification": [{ "matcher": "permission_prompt|idle_prompt|elicitation_dialog",
                   "hooks": [{ "type": "command", "async": true, "timeout": 5,
                               "command": "/usr/bin/notifyutil -p com.626labs.sanduhr.claude-code.waiting || true" }] }],
"Stop":         [{ "hooks": [{ "type": "command", "async": true, "timeout": 5,
                               "command": "… /usr/bin/notifyutil -p com.626labs.sanduhr.claude-code.done || true" }] }]
```

(The Stop command's first half is the background-work handoff, below.) `async` runs it in the
background, so Claude Code never waits on it; `notifyutil -p` posts a Darwin notification, which
reaches every running Sanduhr (`ClaudeCodeSignal`, registered with `notify_register_dispatch` at
launch) and never launches one; `|| true` keeps Claude Code from reporting a hook error. Hooks
before this opened `sanduhr://claude-code?event=…` behind `pgrep -xq Sanduhr`, but `open` goes to
the app LaunchServices registers for the scheme and launches it: with a dev build running, every
turn launched the installed copy. Those commands read as outdated and Install rewrites them. The
matcher keeps sign-in and other notices out of "waiting". `hooks` and the two lists are made when
missing; an existing list keeps every entry and gets Sanduhr's last; an older entry of Sanduhr's is
rewritten where it stands. A group is Sanduhr's when every hook in it is a command posting
`com.626labs.sanduhr.claude-code.` with `notifyutil -p` or opening `sanduhr://claude-code?`; a
group of yours that also runs it is yours and stays. Remove takes Sanduhr's groups out in the reverse
order, deletes a list or `hooks` only when Sanduhr made it and nothing else is in it, and gives an
emptied list or object back what it held, so the file is byte for byte what it was (the receipt's
`hooks` field records what was made). `JSONEdit` gained array appends, replacements and removals
for this.

The app side: both notification names and the link reach `NotchGlowController.claudeCode`.
`ClaudeCodeLink` (kept for manual tests and hooks not yet updated) accepts `sanduhr://claude-code?event=waiting|done` only (the host,
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
`get_usage_history`, `ping`, `propose_theme` (below), plus two Mac tools for Desk messages (below),
the watcher tools and `propose_now_playing_looks` (Now playing, below).
`publish_usage` is dropped on the Mac. What it may read comes from
`mcp-access.json`, which the app writes:

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
full paths only for Full paths. The server never takes a path argument, never logs and makes no
network request; the files it writes are a Desk message request and a theme request (below). `SANDUHR_SUPPORT_DIR`
points it at a test folder (and Desk's folder beside it).

**Desk messages from Claude (item 54).** `get_desk_messages` returns `{status, file_found, lines,
pinned, rotate, mix_daily, special_mode, special_seconds, today, today_special, limits}`: every raw line of Desk's `messages.txt`,
whether a line is pinned, the rotation, the mix switch, the usual raw line the Desk shows now and the
date lines it shows above it today. Its description teaches the syntax (plain, `Mon:`, `MM-DD:`,
`#`, which line shows when, rotation, pinning; that birthdays, anniversaries and holidays are date
lines that add to the day, one per person, added with mode add) and the effects, with taste tips.
`propose_desk_messages {lines, mode: add|replace, note?}` checks the lines (1 to 60, 120 characters
each, no control characters, at least one message line, prefixes and effects must parse; refusals
come back at once with `reasons` and write nothing), then writes `desk-messages-request.json`
atomically (mode 0600) and waits up to 10 seconds for the app's `desk-messages-result.json`:

```jsonc
// request (server → app)
{ "schema_version": 1, "id": "<32 hex>", "requested_at": "2026-10-04T10:00:00.1234560+00:00",
  "lines": ["{ink:#ff2a6d,#05d9e8} {glow} hello there."], "mode": "add", "note": "for the week" }
// result (app → server), rewritten when the user decides
{ "id": "<same>", "completed_at": "…", "result": { "status": "pending_approval" | "applied" | "rejected",
  "mode": "add", "reasons": ["…"], "lines_added": 1, "lines_skipped": 0 } }
```

No answer within the wait is `queued` / `app_not_responding`; the app takes a request up to ten
minutes old. Neither tool is gated by Share with your agents: the messages are on the desktop already,
and a proposal only asks. The server never writes `messages.txt`. The app (`DeskMessageHandoff`,
watching Sanduhr's folder with the themes watcher) checks the request again (`MessageProposal`,
the server's rules and wording, pinned by a test reading the server's constants), then either
applies it, with Settings, Message, "Let Claude change the messages directly" on
(`messageClaudeDirect`, off by default), or holds it as a suggestion: a banner on Settings, Message
("Claude suggested N lines", the note, Dismiss, Review…, Add or Replace), a badge on Message in the
sidebar, and a notification without sound only while alerts are on (their delivery and quiet hours;
`MessageSuggestionNotice`). Add appends and skips lines already in the list; Replace keeps the
comment block at the top of the file and replaces the rest. Either way the previous file is kept as
`messages.txt.previous`. A suggestion waits in memory (quitting drops it); a newer one replaces it.
The app writes `desk-messages-state.json` (`{schema_version, pinned, pinned_line, rotate,
mix_daily, special_mode, special_seconds}`) when the pin, the rotation, the mix switch or On special
days changes, for `get_desk_messages`, which reports them.

**Which lines show (item 69).** Each day the Desk draws one usual line and, on a date with its own
lines, those too (`MessageEngine.today`). The usual line comes from today's weekday lines when there
are any, else from the plain lines; with Settings, Message's "Mix every-day lines in on days with
their own line" (`messageMixDaily`, off by default) the weekday lines and the plain lines take
turns together. Plain lines rotate by day; a weekday's lines rotate by week (they used to step by
day, which comes round to a weekday every 7, so seven Friday lines always showed the same one).
Date lines (`MM-DD:`) no longer replace the usual line: every line for today's date shows, stacked
above it, each with its own effects; more than 3 take turns hourly, 3 at a time. Hourly rotation
keeps hour steps within each pool. The message piece draws the date's lines at 0.8 times the
message size (full size when no usual line shows) with a gap of 0.12 times the message size
between lines (`DeskMessageStack`). Places with room for one line (the notch's Message choice)
show the first date line, else the usual one. A pinned line replaces the usual line only: the
date's lines still stack above it, so a pinned "good vibes only" and a birthday both show.
`get_desk_messages` mirrors the rules (`desk_today`).

**On special days (item 69).** Settings, Message's bar has "On special days": **Stack** (the
default, as above), **Take turns** or **Scroll** (`messageSpecialMode` `stack`, `turns`, `scroll`),
and, unless Stack, "Each line shows for" 5 s, 10 s (default), 30 s, 1 min or 5 min
(`messageSpecialSeconds`). Take turns and Scroll show one line at a time at the full message size
(a line's own `{size:}` still applies), cycling through the date's lines and then the usual line.
The line showing is decided by the clock alone (`MessageSpecialMode.index`: the seconds since 1970
divided by the time each line shows, modulo the number of lines), so the Desk, the notch's Message
and the previews show the same line at the same moment. `DeskModel.updateCycle` sets the index and
waits with one one-shot timer for the next change; while the Desk can't be seen (covered, screens
asleep, screen saver, session away: `motionPaused`, as `{shimmer}` rests) it holds the line and
schedules nothing, and on return it jumps to the clock's line. On a day without date lines, or with
a date line and no usual line, nothing cycles and nothing is scheduled. Every line is laid out
unseen underneath the one showing, so the piece keeps the size of the biggest and nothing around it
moves. Take turns crossfades in 0.4 s. Scroll is a slow ticker: the new line glides up in from below
by its own height while the old one glides up out of view, both fading as they move, in 0.8 s, so
one line is visible at a time. With Reduce Motion both swap at once. Each line that comes in is a new
view, so its `{write}` plays again; `{shimmer}` and `{sweep}` run as on a single line. The Message
preview card shows the chosen mode live, with a sample date line ("Sample: special day") until today
has one. "Today:" lists the lines in cycle order.

**Message effects.** Tags at the start of a line's text, after any prefix, in any order:
`{ink:#hex,…}` (1 to 4 colors, a gradient from two), `{glow}` / `{noglow}` (over Settings, Desk,
Look's new "Glow around the message", `messageGlow`), `{size:0.5…2}` (times the message size),
`{write}` (the line draws itself in, left to right, over 1.5 s when it first appears, once) and
`{shimmer}` (a light band sweeps across it in 1.6 s every 8 s), `{sweep}` (item 65: a light three
characters wide crosses the line in 1.1 s, brightening each character toward white as it passes,
the now-playing mod's glow; once, 0.6 s after the line appears, or with `{sweep:<seconds>}` again
every 2 to 3600 seconds) and `{font:<style>}` (item 65: a letter style, the statusline's names:
`bold`, `italic`, `bold-italic`, `sans`, `mono`, `double-struck`, `script`, `fraktur`, `small-caps`;
case, spaces, hyphens and underscores don't matter, so `{font:smallcaps}` works). Bold, italic and
small caps draw in the line's own font (`MessageTypography`): bold is the family's Bold face from
`BundledFonts.face` (EsteFont's), or a synthesized weight for a family without one; italic is a
12-degree slant of the line; small caps are the capitals with lowercase letters drawn as capitals
at the font's x-height. The other five have no face in any handwriting font, so they draw as
Unicode math letters (`LetterMap`, a port of the statusline's table with its Letterlike holes such
as script B and double-struck R; `test_sanduhr_statusline.py` checks the two tables match) in the
system font: A to Z, a to z and, where the style has them, digits; accents and punctuation stay as
written, and VoiceOver reads the plain text. The notch shows the message without its tags, with
its letter style. `MessageMarkup` parses strictly for
proposals and leniently on the Desk: an unknown or malformed tag ends the tags and draws, with the
rest, as plain text; a line of tags alone draws as written. Reduce Motion shows `{write}` at once and
turns `{shimmer}` and `{sweep}` off. Cost: a line without `{write}`, `{shimmer}` or `{sweep}` draws
as before (no mask, no task); `{write}` is one animation; `{shimmer}` is a task that sleeps between
sweeps and stops while the Desk is covered (window occlusion), the screens sleep, the screen saver
runs or the session is switched away (`MessageMotion.paused`); `{sweep}` draws 30 frames a second
only during its 1.1 s run, sleeps between runs and rests the same way (a once-only sweep that has
run does not repeat when the Desk comes back into sight; one that was covered before it ran runs
then). Settings, Message's Replay sweeps a `{sweep}` line at once.

**The Message editor (item 69).** Settings, Message is a list of lines, each a row with the line
drawn as the Desk draws it (`MessageRowPreview`: `DeskMessageLine` at 22 points on the preview
wallpaper, in the Desk's message font, color and glow; Reduce Motion stills it as it does the
Desk). A row's Edit opens its controls: **When** (Every day, Monday to Sunday, or A date as a month
and a day menu, written as `Mon:` or `10-31:`), **Text** (plain, no tags), **Color** (As the Desk,
One color, or a Gradient of 2 to 4 color wells, and a Palette menu with the now-playing mod's
synthwave, sunset, ocean, aurora, ember, bubblegum, toxic and gold), **Glow** (On, Off, As the
Desk), **Size** (0.5 to 2 times, in 0.05 steps), **Letters** (As written and the nine letter
styles) and **Motion** (None, Write in, Shimmer, Sweep), each with a one-line tooltip. The controls
write the existing tags only, in the order ink, glow, size, font, motion: Fridays, "ship it." in a
sunset gradient in script with a sweep is `Fri: {ink:#ff7e5f,#feb47b,#ffd86f} {font:script}
{sweep} ship it.` Rows have Pin (today's line stays: the pin holds the row's tags and text),
Duplicate, Move Up, Move Down and Delete (the row's menu; VoiceOver has Move Up and Move Down too),
and drag to reorder. Add Line, the rotation (Once a day, Every hour), the mix switch, Save
(Command-S) and Revert sit above the list, with "Today:" naming everything the Desk draws today
(the date's lines, then the usual one, joined by " / "). Each row's note says what happens to it
(`MessageDocument.note`): "October 31 · shows above the day's message" (plus "· with N others that
day", and "· 3 at a time, taking turns hourly" past three), "Fridays · takes turns with N others"
or "Fridays · shows instead of the every-day lines" ("· mixes with every-day lines" with the switch
on), "Every day · takes turns with N others · steps aside on days with their own line" ("· mixes in
on days with their own line" with the switch on). The **List | Text** switch shows the file itself (Text) with the tag reference beside it and the
free pin field; List goes back. Both views edit one document (`MessageEditorModel`), and
switching carries unsaved edits across. `MessageLineModel` reads the file into rows: a styled row
(when, text, look) for each line the controls can represent; a raw row, shown as written and kept
verbatim, for one they can't (a tag Sanduhr doesn't know or that is malformed, two motions on one
line, a date that isn't one, a prefix with nothing after it); comments and blank lines are kept in
place and not listed. Each row writes the line it was read from, byte for byte, until it is changed,
so an unchanged file saves back exactly (CRLF endings and a missing final newline included) and a
changed row rewrites only its own line; a new line with no text is left out. A row whose text the
Desk would read differently (it starts with `{` or `#`, or like a day on an Every day line) says so
under it. Claude's suggestion card stays at the top of the page, and **Ask Claude** below the list
has a copyable example prompt and the "Let Claude change the messages directly" switch.

**Themes from Claude (item 55).** `propose_theme {theme, save_as?, apply?}` is the Windows tool:
same name, inputs and result shape. `theme` is the theme JSON in `docs/themes/template.json`'s
snake_case fields (`name`, the fourteen `#rrggbb` colors, optional `description` and the glass
dials); `save_as` a file key (`^[a-z0-9][a-z0-9-]{0,39}$`, default the name slugged); `apply`
default true. The description teaches the fields, the dials' ranges, what the lint measures and
Match Desk. The server lints first (`lint_theme`, a Python copy of the Windows `ThemeLint`, the same
rules and wording) and refuses a broken theme or a built-in's key at once (`rejected`, `reason`
`invalid_params` / `invalid_theme` / `reserved_name`, `findings: [{level, field, message}]`),
writing nothing. A clean theme becomes `theme-request.json` (atomic, mode 0600) and the server
waits up to 10 seconds for `theme-result.json`:

```jsonc
// request (server → app)
{ "schema_version": 1, "id": "<32 hex>", "requested_at": "…", "theme": { "name": "Tidepool", … },
  "save_as": null, "apply": true }
// result (app → server), rewritten when the user decides
{ "id": "<same>", "completed_at": "…", "result": {
  "status": "applied" | "saved" | "pending_approval" | "rejected" | "error",
  "reason": "invalid_theme" | "reserved_name" | "dismissed" | "name_taken" | "save_failed" | …,
  "remedy": "…", "key": "tidepool-2", "name": "Tidepool", "previous_key": "obsidian",
  "saved_path": "…/themes/tidepool-2.json", "renamed_from": "tidepool", "findings": [ … ] } }
```

No answer is `queued` / `app_not_responding`; the app takes a request up to ten minutes old. The
app (`ThemeProposalHandoff`, on the same folder watch as the Desk messages, `HandoffWatch`) lints
again with `ThemeLint` (the Swift port; errors refuse, warnings ride along) and refuses a built-in's
key (`ThemeRegistry.builtIn`, Match Desk included). With Settings, Widget, Themes, "Let Claude
change themes directly" on (`themeClaudeDirect`, off by default) it saves and applies as asked;
otherwise the Themes page shows "Claude suggested a theme" with the theme's gallery card, its name
and description, the lint's notes (hover), and Dismiss, Save, Save & Apply; Themes in the sidebar
gets a badge and a notification posts as for Desk messages. **A user theme is never overwritten:**
when the key's file holds a different theme the next free key is used (`tidepool-2`, up to `-99`)
and the result names `renamed_from`; a file already holding the same theme is reused. A partial
`accent_bloom` or `inner_highlight` gets the Windows defaults and `breath_period_ms` a whole number,
so the Mac's loader reads what Windows reads. `glass_on_mica` is required here as on Windows, though
a hand-written Mac theme may leave it out. The theme's name and description are never logged.

**Themes with a title style (item 65d).** A theme may carry `title_ink` (2 to 4 `#rrggbb` stops,
the widget title's gradient left to right) and `title_style` (a letter style by the `{font:…}`
names: `bold`, `italic`, `bold-italic`, `small-caps` drawn by the widget font, `sans`, `mono`,
`double-struck`, `script`, `fraktur` as Unicode letters in the system font). `TitleBarView` draws
the title through `ThemeTitleText`; without them it draws as before, in `text`, semibold. Both
lints check the shape (an error names the field) and each stop's contrast on the card, as for
`text` (a warning at under 4.5:1, "title_ink stop 2 reads at 2.1:1 on the card"). A hand-written
file with a value the widget can't draw (one stop, a style it doesn't know) still loads, with the
title as before. `propose_theme` passes them through; `docs/themes/template.json` lists both as
`null` and `docs/themes/AGENT_PROMPT.md` explains them.

Tests: `python3 -m unittest discover -s mac/integrations/tests` (also a Mac CI step), over temp
folders: each sharing level, no access file, hidden names, and the Windows MCP tests' cases. The
two lints share `mac/Tests/SanduhrTests/Fixtures/theme-builtins.json`, the built-ins they must pass
clean.

## Mods

Item 64, slice 1: Settings, Mods (under Integrations) lists every mod and plugin each Claude Code
folder loads, read-only. The folders are the ones Integrations lists: `~/.claude`, the
`~/.claude-*` folders and `CLAUDE_CONFIG_DIR`'s (`ClaudeCodeFolders.discover`, which now skips
`*.config-backup-*` copies a config tool leaves beside a home, unless `CLAUDE_CONFIG_DIR` names
one), the accounts' linked folders and the folders Sanduhr installed into.

**What a folder loads** (`ModInventory.scan`, files only, no CLI): the folders in its
settings.json's `env.CLAUDE_CODE_PLUGIN_DIRS` (`@inline`; off when `enabledPlugins["<name>@inline"]`
is false), every plugin in `plugins/installed_plugins.json` or keyed `<name>@<marketplace>` in
`enabledPlugins` (on only when that key is true; a key without a record, such as a `@synced`
plugin, is listed without files), plugin folders under `skills/` (`<name>@skills-dir`), and the
mods a session made under `dev-mods/<session>/` (the session folder itself, or each plugin folder
in it), which load in that session only. A row shows the name, version (the manifest's, else the
install record's), description, where it loads from, On, Off, Its session only or Missing (a
listed folder that isn't there), Mod or Plugin (a mod's `hooks/hooks.json` lists `modules`), and
what it touches. A `@synced` plugin from claude.ai that no settings file names has no trace on
disk, so `claude plugin list --json` (not called here) can count more than this page.

**What it touches** comes from the static scan shared with Integrations' status entries
(`ModStatusEntries.forEachSource`): a mod's source files read as text, never node_modules, hidden
folders, `.d.ts` files or tests (which name APIs without the mod calling them), at most 400 files
of 512 KB. Surfaces: a band above the prompt (`AbovePrompt`), a pane (`component: 'Pane'` or
`ui.open`), a status entry (`ui.status`), toasts (`ui.toast`) and slash commands (`command.run`
matchers, `command.register`, and any plugin's `commands/*.md`). Capabilities: runs programs
(`process.run`/`spawn`, a settings-style `"type": "command"` hook, an MCP server), uses the network
(`http.fetch`, an `"type": "http"` hook) and reads or writes files (`fs.*`). A mod's surfaces are
drawn in a TerminalPreviewFrame as a sketch (`ModSketch`): blocks stand in for its content, so
nothing is live text, labeled "Sketch: drawn by the mod in Claude Code".

**Check** runs `claude plugin validate <folder> --json` (`ModCheck`), which reads a plugin without
running its code; `claude plugin test`, which runs it, is never called. `claude` is the first
executable on PATH, then `~/.local/bin`, `~/.claude/local`, `/opt/homebrew/bin`, `/usr/local/bin`,
`~/.npm-global/bin` and `~/.bun/bin`; it runs with its own folder and Homebrew's first on PATH
(so a node script finds node), from the temp folder, and is stopped after 10 seconds. The JSON
becomes a risk card (`ModCheckReport`): whether Claude Code would load it, errors and warnings
with the folder's paths shortened, the hooks and `$` calls, and a risk level with its reasons:
high for programs, the network, writing files or settings, gating or rewriting what Claude does
(`tool.*`, `prompt.*`, `session.append`, `agent.*`, `telemetry.*`, `gatingHooks`) and
secret-looking environment names; medium for reading files or the environment; low otherwise.
Without `claude` the button is off and the page says why.

Other mods are read-only: the page says switching them comes later, and a copy of Sanduhr's mod
outside Sanduhr's folder (a checkout) is marked read-only too.

**Sanduhr's own mod** (slice 2, `ModSwitch.swift`, `ModsOwnSettings.swift`): a box at the top for
`sanduhr-meters` with a switch per Claude Code folder, Update and Remove, all through the receipt
in `integrations/installs.json`, so each undoes byte for byte. A row in the inventory is Sanduhr's
when its path is a `…/Sanduhr/integrations/…/mods/sanduhr-meters` entry (`isOurModEntry`).

- **Off** writes `enabledPlugins["sanduhr-meters@inline"]: false` into the folder's settings.json
  (the documented switch for a plugin folder mod), keeping the list entry, the version and the
  mod's own options. The receipt's `switched` (`SwitchReceipt`) records the member as it was:
  its old value's bytes, or that the key (and `enabledPlugins` itself) was made, and what sat
  between empty braces.
- **On** undoes that switch. Where the user's own `false` is there, it writes `true` and records
  the `false`, which Off puts back. Where the folder's list has no entry, it asks, then adds one
  pinned to the app's stamped version, `integrations/<stamp>/mods/sanduhr-meters` (the receipt's
  `pinned`), through the same JSONEdit splice as Integrations' Install. Refresh keeps every stamp
  a receipt pins, so an app update never pulls a pinned version out from under a folder.
- **Update** shows when a pinned entry names a stamp other than the app's. It compares what the
  two versions can do: the validator's `$` calls and gating hooks when `claude` answers for both,
  else the static scan's capabilities for both (`ModCapabilities.added`). Anything new stops the
  update for a question ("The new version can also: …"); the version in use stays until you
  agree. Then the entry moves in place and the old stamp goes once nothing pins it. An entry
  through the `current` link (installed from Integrations) follows the app's updates as before.
- **Remove** is Integrations' Remove for the mod, which now undoes the switch first: the folder's
  settings.json is back to its bytes from before Sanduhr touched it. A member someone changed
  since is left as it is.
- **What else decides.** Claude Code merges `enabledPlugins` key by key, and a project's value
  beats the user's. After each switch the page re-reads the managed settings
  (`/Library/Application Support/ClaudeCode/managed-settings.json`) and every project the folder's
  `.claude.json` lists (`.claude/settings.json` and `.claude/settings.local.json`, at most 400)
  and says so when one disagrees: "A project setting keeps it on in ~/code/app
  (.claude/settings.json)." Otherwise: "Takes effect in new Claude Code sessions (or after
  /reload-plugins)."

The Combine sheet's mod chips (Integrations, Statusline) link to the page: "Switch it on the Mods
page". A summary card (item 68's pattern) counts mods, plugins, how
many are on, the folders and any missing. state.yaml's `mods_page` holds flags and counts only.
Tests: `ModsPageTests` (temp folders and the validator's JSON captured as fixtures under
`Tests/SanduhrTests/Fixtures/mods-validate/`; Check against a stand-in `claude` script) and
`ModSwitchTests` (temp folders: off and on round trips byte for byte, the user's own off, a
member changed meanwhile, project and managed overrides, an update that asks and one that
doesn't, pinned stamps surviving refresh, Remove back to the original bytes).

## Camera and mic indicators

Item 67. Indicators only, read-only: a red recording dot while any app uses a camera, and an
orange mic glyph while any app uses the microphone. In Settings, Desk, Notch, Camera and mic (all
in `com.626labs.sanduhr.desk`):

- **Show the red dot** (`avCameraDotMode`): Never (the default), For cameras without a visible
  light (`hiddenLight`), or Always. A MacBook's built-in camera has its own green light that can't
  be turned off, so a dot for it only repeats that light. `hiddenLight` shows the dot only when a
  running camera isn't built in (an external or Continuity camera: CoreMediaIO's
  `kCMIODevicePropertyTransportType` isn't `'bltn'`, read with the device list, never opening it),
  or the built-in one runs with the lid closed (IOPMrootDomain's `AppleClamshellState`, read again
  on every camera reading and screen change). `CameraLightVisibility` decides, pure. The switch
  before the picker (`avCameraDot`, a Bool) migrates once: on becomes `hiddenLight`.
- **Show a mic while the microphone is on** (`avMicGlyph`), off by default.
- **Pulse the dot gently** (`avPulse`, on) and **Beside the camera** (`avSide`, left or right,
  right by default).

They run only while Desk runs and they are on.

**Motion** (`AVIndicatorMotion`). Coming and going is a 0.25-second ease-in-out opacity fade, the
island's room growing or shrinking with it in the same transaction; the tab fades its window. The
dot breathes between 0.55 and full opacity on a 1.6-second cosine, computed from each frame's time
(`TimelineView(.animation)`), not a state that flips. With Reduce Motion there is no breath and the
fades are instant. The slot beside the camera (`AVBesideSlot`) is always in the island's row: its
width is the layout's room (0 when nothing shows), its content clipped and faded with it, so it is
never inserted or removed and leaves nothing behind; `avDrawn` keeps the last indicators so the fade
out shows what was there.

**Signals.** The camera's is `CameraMonitor`, the camera light's own (CoreMediaIO's
`kCMIODevicePropertyDeviceIsRunningSomewhere`); `AVIndicatorController` runs a second instance, so
the indicator works with the camera light off. The microphone's is `MicMonitor`: Core Audio's
`kAudioDevicePropertyDeviceIsRunningSomewhere` on the default input device, with a listener on it
and one on `kAudioHardwarePropertyDefaultInputDevice`, so it follows a headset as it connects
(`MicWatch`, pure). No polling: C function listeners with a retained target as client data, as in
`CameraMonitor`, each hopping to the main queue. Both settle through `CameraActivity`: on at once,
off 0.5 s after the last device stops. Reading those properties opens no stream, reads no audio
and needs no permission, so macOS never asks; nothing in Sanduhr touches `AVCaptureDevice` or opens
an input. Sanduhr learns only in-use booleans, never which app; nothing is logged (a failed
listener call logs its OSStatus once) or saved. One limit: the flag is per device, so a headset
whose playback and microphone are one Core Audio device can show the mic while it only plays.

**Where they show** (`AVIndicatorPlacement`, `AVIndicatorSpot`). With the island up, beside the
camera on the chosen side: the island grows by their room (`AVIndicatorLayout.besideRoom`, the
dot, the mic and a 6-point spacing), so the wings keep theirs, and the strip under the camera
follows the same width. Picking **Camera and mic** for a wing or the strip (`NotchContent.avIndicators`)
moves them there instead; with nothing in use that place shows its own default, as Watchers does.
With the island off, or on a screen without a notch, they get their own small black tab at the
top (`badge`): against the notch on the chosen side, or at the top center like the camera light.
A click or a two-finger click opens a menu of read-only lines ("Camera in use", "Microphone in
use", disabled) and Notch Settings…, which opens Settings, Notch. Nothing mutes or changes a
device. The strip takes its clicks through `DeskHitTest` (`av_indicators`, key `strip`).

The camera dot is the only red dot in Sanduhr: a failed watcher draws a red triangle instead.

**Debug.** `smoke/smoke do av-test camera on|off` and `av-test mic on|off` fake a signal in
memory (shown through the same settings, a faked camera counting as one without a visible light;
off hands back the real one). `state.yaml` has
`av_indicators: {camera, mic, shown}`, `shown` one of `none`, `beside_left`, `beside_right`,
`places`, `badge`; the tab's window is kind `indicators`. `scenarios/av-indicators.yaml` runs it.

## Watchers

Live cards for work in flight (item 66, slice 1), on a notch place or in a Desk corner: a state mark
(running blue, waiting on you amber, passed green with a check, failed a red exclamation-mark
triangle rather than a dot, since a red dot means the camera is on, finished and lost touch
grey), the title, the time so far, `done/total` when there is a total, and a one-line note. Two
switches in Settings, Integrations, Watchers, both off by default (`watchersAgents`,
`watchersBackground` in `com.626labs.sanduhr`). Placement is the usual: Watchers as a notch wing's
or the strip's content (Settings, Notch; the most urgent watcher plus "+N", the place's default
while there is none), or the Watchers element in Settings, Desk, Layout (the stack, up to four, then
"+N more"). A click opens the watcher's link (https only); a two-finger click opens Dismiss, Dismiss
All and Integrations Settings…. "Waiting on you" pulses (not with Reduce Motion) and fires the notch glow
once, when watchers show somewhere and Desk runs. Passed and finished fade after 6 seconds; failed
stays until dismissed; a watcher with no update for its window greys as lost touch (10 minutes for
an agent's; an automatic one is confirmed at each Stop and greys only after an hour without one, the
session closed mid-task). Most urgent first: waiting, failed, running, lost touch, then the rest.
Everything is in memory: quitting drops every watcher.

**The notch line.** A notch place on Watchers plays an intro when the top watcher changes (its id)
or its state does (`WatcherIntro`): the full line, "PR 140 CI: combine statuslines · 2m · 4/12 +1",
scrolling through once with now playing's timing and fade (`ScrollOnceText`, `NowPlayingScroll`)
when it doesn't fit, or held 4 seconds when it does. Then it rests on "<short> · <done/total>"
("PR 140 · 4/12 +1"), "<short> · <elapsed>" without a total. With Reduce Motion it goes straight to
rest. The phase lives on `DeskModel.watcherIntroUntil` (its length measured for the widest wing),
so the wings' width follows it: up to the wing maximum during the intro, back to the short form
after. The short title is the agent's `short` (up to 12 characters), else derived from the title:
"PR 140" from "PR 140", "PR #140" or "pull 140", else "#140" from a number anywhere, else "v2.8.0"
from "Release 2.8.0" or "v2.8.0", else the first word clipped to 10 characters. Background tasks use
a workflow's name or the description's first word. The Desk rows keep the full title.

**Agents (`watch_start`, `watch_update`, `watch_end`).** The MCP server checks the arguments (title
1 to 80 characters on one line, short up to 12, link `https://` only and at most 2048 characters, total 1 to
1,000,000, note up to 140 characters, state running or waiting, result passed or failed, ids it
started this session) and refuses with `watchers_off` unless `watchers.json` (written by the app)
says `"agents": true`. Then it writes `watch-request-<ms>-<seq>-<id>.json` (0600, atomic) and returns
the id at once; the app (`WatcherStore`, on the shared `HandoffWatch`) reads each request in name
order, deletes it, decodes it again (`WatcherRequest`: stale after ten minutes, a bad link or total
dropped) and updates the board (`WatcherBoard`, pure). `work: true` from the agent, or a request from
a Claude Code folder (`CLAUDE_CONFIG_DIR`, else `~/.claude`) linked to an account marked **Work
account** in Settings, Accounts, Data, tags the watcher: work watchers hide while demo mode is on.

**Claude Code's background work.** The Stop hook the notch glow install writes now also hands over
the session's `background_tasks` (Claude Code 2.1.288's `StopHookInput`), only while `watchers.json`
says `"background":true`:

```sh
d="$HOME/Library/Application Support/Sanduhr"; /usr/bin/pgrep -xq Sanduhr &&
  /usr/bin/grep -qs '"background":true' "$d/watchers.json" &&
  /usr/bin/osascript -l JavaScript -e '<stopTasksScript>' "$d" >/dev/null 2>&1;
  /usr/bin/notifyutil -p com.626labs.sanduhr.claude-code.done || true
```

`pgrep` only gates the report (watchers.json outlives a quit Sanduhr): no report waits in the
folder for a later launch, and one written longer ago than a request's ten minutes is deleted
unread anyway (`WatcherStore.isStale`). The notification is posted either way.

`stopTasksScript` (JavaScript for Automation, on every Mac; python3 may not be) keeps the session id,
`CLAUDE_CONFIG_DIR` and per task (at most 20) only `id`, `type`, `status`, `description` (clipped to
120) and a workflow's `name`; never `command`, `last_assistant_message`, `transcript_path` or `cwd`.
It writes `watch-stop-<ms>-<uuid>.json` (0600, renamed into place) and the app deletes it on reading.
Each task becomes a watcher (`b:<session>:<task id>`, the description as title, "background shell",
"workflow deploy" and so on as the note); a task missing from the session's next Stop ends as
finished (result unknown). The glow notification is posted either way, so item 51's glow is
unchanged.
Installs from before this version show **Outdated**; Install rewrites the Stop entry in place.

**Above Claude Code's prompt.** **Show watchers above the prompt** (off by default) hands the
watchers to the meters mod through `band.json` (see the meters mod above): a row each above the
prompt in every Claude Code folder with the mod installed. Only an agent's watcher's title, short
title, state, progress and times are written, and for background work its kind and state; it is
rewritten on each change and each minute while watchers show, and switching off takes them out.

`state.yaml` has `watchers: {count, states, placements, agents, background}`, never a title, note,
link or description. `smoke do watch-test start|wait|pass|fail|clear` drives a made-up watcher
through the same decoding, whatever the switches say; `scenarios/watchers.yaml` runs it.

## Desk layout

Settings, Desk, Layout places each Desk piece (item 59; the pure pieces in `DeskArrangement.swift`):

- **Eight places.** The four corners, Top center, Bottom center, Middle left and Middle right.
  The top and bottom of a side share a column with a spacer between them, as before, so a growing
  meeting list pushes against the message instead of drawing over it. A side's middle sits in the
  same column, halfway between its top and bottom stacks, so a tall corner pushes it rather than
  drawing over it. The centers share a column of their own between the sides: while a center has
  a piece, the sides draw only in the room it leaves (`DeskColumnsLayout`,
  `DeskAnchorGeometry.columnWidths`, 24 points either side), so a long message at Top left wraps
  and shrinks instead of running under the meters at Top center. Top center sits below the
  notch, and below the island's strip while the island draws (`DeskAnchorGeometry.centerDrop`,
  10 points of room); on a screen without a notch it sits on the top margin like the corners.
  Margins stay global.
- **Your order.** The Order list shows each place that has pieces, top to bottom. Drag a piece up
  or down to reorder it, or onto a piece in another place to move it there; VoiceOver has Move Up
  and Move Down. Picking a new place for a piece keeps the other pieces' order: it goes before the
  first piece there that Settings lists after it.
- **A size per piece.** 60% to 160% in 10% steps, kept when the piece moves. Every piece scales
  from the clock size in Desk Look, the message from its own size.
- **The map.** The Layout card (`DeskLayoutMap`) draws the screen with the menu bar, the notch (and
  the island), the Dock on its edge and each piece outlined at its place, in its order and at its
  size, live, from the same `DeskArrangement.stacks` call the Desk draws from.

The layout string grows backward compatibly: each word is `widget:anchor`, or `widget:anchor:scale`
with a size other than 1 (`clock:bl:1.2`), and the pieces at one anchor stack in the string's
order. An older string (corners only, no sizes) draws exactly as before (pinned by a test against
the old reader) and is written back unchanged. An anchor this build does not know puts the piece at
its default (the message top left, the rest bottom left) instead of hiding it; an unreadable size is
1, a size outside the range is clamped; a widget named twice keeps its last word; a widget word this
build does not know is dropped on the next change. Older builds skip a word with a size, so going
back to one hides the sized pieces until they are placed again.

`desk_pieces` in the smoke state lists each drawn piece's `piece`, `anchor`, `order` (its place in
the stack, 0 at the top) and `scale`; `scenarios/desk-layout.yaml` reorders a corner and moves
pieces to the new places, checking `desk_frames_ok` each time.

### Arrange on the desktop

Arrange mode (item 60) edits the same layout on the desktop itself, without Settings. It starts
from **Arrange Desk…**, which is in the shared menu (`MenuCommand.arrangeDesk`: the menu bar item's
menu, the widget's menu, the Desk's clock menu in the menu bar, and the shared part of the meters'
menu), at the end of now playing's, a watcher's and the camera and mic indicators' menus, in the
shared menu a two-finger click on a meeting row, the calendar note or the account name opens
(`DeskHitTest.hasSharedMenu`), and on Settings, Desk, Layout's **On the desktop** row. While Desk
is off the menu item is off and says why ("Turn on Desk to arrange it on the desktop.", under the
item and as its tooltip), as the Settings button does. The clock, the message and the claude line
open the same shared menu while **Clock and message take clicks** is on (see Desk clicks below);
off, they let every click through to the Finder and the menu bar and widget menus cover them. The
pure pieces are in `DeskArrange.swift`, the views and the bar's panel in `DeskArrangeViews.swift`.

- **What shows.** Every piece gets a dashed outline with its name and a round resize handle on the
  corner facing the middle of the screen (`DeskArrange.handle`). A piece with nothing to draw (now
  playing while nothing plays) still gets a small box to grab.
- **The bar floats.** The bar with what to do, Cancel and Done is not on the Desk window (which sits
  just below normal windows, so any app window covered it). It is its own small borderless panel
  (`DeskArrangeBarController`, an `NSPanel` at the floating level on every Space) centered on the
  visible frame of the Desk's screen (`DeskArrange.barOrigin`), shown while arranging, moved with
  the Desk on a display change, and closed when Arrange ends. It reads "Return or Done keeps the
  new layout. Escape or Cancel puts it back." Sanduhr activates and the panel takes the key.
- **Keys.** While arranging, a local key monitor ends Arrange mode wherever the key focus is in
  Sanduhr (the panel, the Desk window after a click on a piece, the widget): Return and keypad
  Enter are Done, Escape is Cancel (`DeskArrange.endKey`, which answers `DeskArrangeEnd`).
- **Settings steps aside.** An open Settings window (where Arrange is often started) is ordered out
  for the duration and brought back, key and in front, when Arrange ends by any route
  (`DeskArrangeStash`).
- **Move and reorder.** Dragging a piece fades it, an outline follows the pointer, and the eight
  anchors light up as dots, the one it would land on bigger. A drop over a stack (its pieces'
  frames plus 26 points for the outline and the name, `DeskArrange.stackBounds`) goes to that
  stack, the dragged piece's own first (`DeskArrange.target`), so a tall stack whose top reaches
  past the halfway line to the next anchor still reorders. Anywhere else it goes to the nearest
  anchor (`DeskArrange.nearest`, measured as shares of the content's width and height so every
  screen shape has the same zones). It lands in front of the first piece there whose middle is
  below the pointer (`DeskArrange.pieceAfter`, `DeskArrangement.put`).
  Anchors, not free placement: a layout survives other displays and Dock moves.
- **Resize.** The handle's drag grows or shrinks the piece by its share of the piece's width plus
  height, snapped live to Settings' 10% steps within 60% to 160% (`DeskArrange.scale`).
- **Nothing saved until Done.** `DeskArrangeMode` (on `DeskModel`) holds the saved string verbatim
  and a working copy that DeskView draws. Done or Return write the working layout once, and only
  when it differs; Cancel or Escape drop it, so the saved string is exactly as it was. Settings' places
  and order are greyed meanwhile. Desk turning off cancels it.
- **Clicks.** While arranging, a full-screen plate at the hit-plate alpha (`DeskArrangePlate`) gives
  every point a drawn pixel and the window takes the mouse over its whole frame
  (`DeskArrange.takesMouse`); the pieces' join, account and menu clicks are off. The window may take
  the key only then (`DeskWindow.takesKey`). Afterwards it goes back to clicks only where something
  is drawn, exactly as before.
- **Same rules.** The Desk lays the working layout out with the same margins, Dock clearance and
  notch drop, and the anchor dots use the same geometry (`DeskAnchorGeometry`). With Reduce Motion a
  drop has no snap animation. VoiceOver gets Move Up, Move Down, Bigger and Smaller on each piece.

`desk_arrange` in the smoke state has `active`, `changed`, `working` (the layout string being
edited), `click_through` (`whole` or `drawn`) and `bar_visible` (the floating bar is on screen);
the `desk-arrange start|test|done|cancel` hook drives it, and `scenarios/desk-arrange.yaml` starts
it from Settings, checks that Settings steps aside and comes back, that the bar shows only while
arranging, that Cancel leaves `layout` alone and that Done writes it.

### Desk clicks

The Desk window is transparent and sits just below app windows, and the window server hands a
transparent window a click only where a pixel is drawn and only while it takes the mouse. So Desk
takes the mouse only over its click areas (`DeskHitTest`, from the frames the pieces report through
`onGlobalFrame`), and a near-invisible plate behind each one (`DeskPointerMenu.hitPlateOpacity`,
alpha 3 of 255) makes the gaps between letters count. Everywhere else clicks go to the desktop.

**Clock and message take clicks** (Settings, Desk Look, Clicks; desk suite key
`piecesTakeClicks`, on by default) adds the clock (time and date), the message (the day's line, a
special day's stack, or the line taking its turn with Take turns or Scroll) and the claude line to
those areas (`DeskPieceClicks`; kinds `clock`, `message` and `claude_line`). A two-finger click on
any of them opens the shared Sanduhr menu (Arrange Desk…, Settings… and the rest) instead of the
Finder's desktop menu; the clock's adds **Desk Look Settings…** and the message's **Edit Messages…** at the
top, and the shared Settings… reads All Settings… there. A plain click on the clock opens Settings, Desk Look, on the message Settings, Desk, Message;
on the claude line it does nothing, like the meters (the account name inside the line still
switches accounts). The cost: a desktop icon right beneath one of them can't be clicked there while
the switch is on. Off, nothing is drawn behind them and they are not click areas, exactly as before.

File drags: while a mouse button is down, Desk never changes whether its window takes the mouse
(`DeskPointerDrag`). A file picked up elsewhere on the desktop is dragged with the window still
ignoring the mouse, so passing over the clock or the message never makes the plates a drop target,
and the drop lands on whatever is beneath. The button coming up lets the window catch up with the
pointer. A drag that starts on a piece itself is Desk's until the button comes up. This rests on
`ignoresMouseEvents` being set before the drag starts, not on toggling it mid-drag, which
the window server is not documented to honour for a drag already under way; it has not been
checked against a live Finder drag in a test (see the smoke test).

## Desk and the Dock

Desk's pieces stay clear of the Dock on the Desk's screen (item 56, `DockFollower`, the pure
pieces in `DockClearance.swift`). The Dock's own settings are read, never written: `com.apple.dock`
`orientation` (no key is bottom), `autohide`, `tilesize`, `autohide-delay`. A bottom Dock moves the
bottom places up (`bl`, `bc`, `br`); a side Dock moves its whole column in (`tl`, `ml` and `bl` on
the left, `tr`, `mr` and `br` on the right). The Dock's reach is added to the margins (`left`,
`right`, `bottom`) every place sits inside, so the usual margin is kept from the Dock's edge instead
of the screen's.

- **Always shown.** The reach is the screen's `visibleFrame` against its `frame` on the Dock's side
  (the menu bar never counts), read whenever the screen parameters change (the Dock moving,
  resizing, starting or stopping to hide, a screen coming or going) and when the Dock's settings
  change (`com.apple.dock.prefchanged` where macOS posts it, plus every app switch and Space change).
  On two screens only the Desk's screen counts: a Dock on the other one moves nothing.
- **Auto-hiding.** Items rest at the edge. When the Dock comes up they slide clear of it in
  0.25 s (ease in and out, about as long as the Dock's own slide) and settle back when it hides;
  with Reduce Motion they jump. Whether the Dock shows comes from `CGWindowListCopyWindowInfo`:
  the on-screen windows owned by the Dock process (`com.apple.dock`) at the Dock's window layer
  (`CGWindowLevelForKey(.dockWindow)`, 20) that cover the Desk's screen. Bounds and the on-screen
  flag only, so no Screen Recording permission. A window shaped like a strip gives the Dock's
  depth; on macOS 26 the Dock draws in one window as large as the screen, which only says that the
  Dock shows, and the depth is then the always-shown reach last seen at the same tile size, else an
  estimate from `tilesize` (tiles, shelf and gap, rounded up, so it errs clear of the Dock).
  The slide is `DockSlide`: hidden, showing, shown, hiding.
- **Cost.** Nothing polls while the pointer is away from the Dock. The window list is read only on
  the Desk's close pointer watch (the 50 ms timer that already runs near Desk blocks), which also
  runs while the pointer is in the Dock's trigger zone (its screen edge, 6 points deep) and while
  the Dock has not hidden again (`DockWatch`): every tick while the pointer moves in the zone or
  within the Dock's delay plus a second of resting there, every fourth tick once the pointer leaves
  a shown Dock, and not at all once the Dock is hidden and the pointer is away.
- **Missed on purpose.** A Dock that comes up without the pointer near it (Mission Control, App
  Exposé, a keyboard shortcut) is followed once the pointer comes near; one that comes and goes
  without the pointer near the edge is missed. Full-screen Spaces don't show the Desk, so nothing
  there is affected.
- **Click areas** move with the items: the frames are reported as the layout moves, so
  `DeskHitTest` and `desk_frames` follow and `desk_frames_ok` holds during and after the slide.
  `state.yaml` has `dock: {side, autohide, inset}`, `inset` the points applied now.

## Now playing

Now playing (items 53, 53b) shows what plays on the Mac, in any app that publishes now playing
(Music, Spotify, Pandora, browser players such as YouTube Music): "▶ Title · Artist" wherever it is
placed, like the rest of Desk: a notch wing or the strip under the camera (pick Now playing in
Settings, Notch) and the `nowPlaying` element in Settings, Desk, Layout (a line with a position bar,
in a corner like the other elements; off by default). There is no switch: it runs while it is placed
somewhere and Desk is on (`NowPlayingPlacement`). Click it to play or pause; two-finger click for
Previous, Play/Pause, Next and Now Playing Settings…. It hides when nothing plays, optionally while
paused, and for apps switched off on the Now Playing page (which also has the source, the AppleScript
switch and "Notch Settings…" / "Layout Settings…").

- **When nothing is playing.** A notch wing or the strip on Now playing doesn't go blank when there
  is no line (nothing plays, Hide while paused while paused, the app switched off, now playing
  unavailable): it shows the Now Playing page's "When nothing is playing" choice instead, by default
  what that spot shows when Now playing isn't picked (left wing: next meeting or the time; right
  wing: the Claude meters; strip: next meeting or the meters), or any other notch content, or Nothing
  (plain black, as before). The stand-in behaves fully like that content: its text, its wing width
  (no Next room), the island's click (Settings) and no now playing click areas. One pure rule,
  `NotchContent.effective`, decides it for the wings, the strip, `desk_frames` and the glow's
  outline. The saved choice still places now playing, so it keeps running and the next track takes
  the spot back. The Desk line has no stand-in; it simply hides. Settings, Notch says what a place on
  Now playing shows meanwhile ("When nothing plays: Claude meters (its default).").
  `state.yaml` has `now_playing_idle` and `notch_shows: {left, right, strip}` (each place's
  effective content, never its text).

- **Style what's playing (item 65c).** A switch on the Now Playing page, off by default. On, each
  song draws in its own gradient and letter style on the notch wings, the strip and the Desk line
  (its position bar too). A song without a saved look wears one seeded from a hash of its app,
  title and artist (FNV-1a, so the same song looks the same on every launch): one of the eight
  named palettes (`MessagePalette`: synthwave, sunset, ocean, aurora, ember, bubblegum, toxic,
  gold) and small caps, italic, bold or bold italic (`NowPlayingLooks.seedStyles`, the styles the
  hand font draws itself). Resolved looks are cached per song in memory only
  (`NowPlayingLookStore`, keyed by app, title and artist, at most 200, never on disk). The play
  glyph stays plain; a styled title is measured as drawn (`MessageTypography.width`), so a wing
  grows and scrolls for it as for a plain one.
  Looks come from Claude: `propose_now_playing_looks {looks: [{artist, title, colors, font?,
  mood?}]}`, 1 to 50 looks, artist up to 100 and title up to 200 characters on one line, 2 to 4
  hex colors each 3:1 or better on black (the notch), a `{font:…}` style name, a mood of at most 3
  words and 40 characters. The server checks them (`rejected` with reasons, writing nothing),
  writes `now-playing-looks-request.json` (atomic, 0600) and waits up to 10 seconds for
  `now-playing-looks-result.json` (`{status: pending_approval | applied | rejected, reasons?,
  looks_saved?, style_on}`; no answer is `queued`). The app checks again with the same rules and
  wording (`NowPlayingLookProposal`; a test on each side pins them) and either saves at once ("Let
  Claude style songs directly", off by default) or shows "Claude suggested looks for N songs" at
  the top of the page's Looks section, each song drawn in its look with its mood, with Save and
  Dismiss (a newer suggestion replaces a waiting one, which is answered `rejected`). Saved looks
  go to `now-playing-looks.json` (owner-only, `{schema_version: 1, looks: [...]}`, at most 500,
  the oldest dropped) and match by artist and title, ignoring case and extra spaces, in any app.
  This file is the only place a song name is kept: the user's own approved data. **Clear Looks…**
  deletes it. The server never reads what is playing. Desk preferences: `nowPlayingStyle`,
  `nowPlayingLooksClaudeDirect`.

- **Long titles.** A title that doesn't fit its wing (or the strip) scrolls through once when a new
  track starts or the place first shows it: 1.2 s at the beginning, 30 pt/s with ease in and out
  until its end clears the 12 pt fade, 1.0 s at the end, 0.6 s back, then it rests at the beginning.
  It never loops, and never moves with Reduce Motion (`NowPlayingScroll`).
- **While paused** a wing shows a Next button at its outer edge (away from the camera) and the strip
  at its end; the title keeps its beginning visible. The title plays, the button skips
  (`NowPlayingWingLayout`; the strip's button is the `now_playing_next` Desk element).
- **Upgrading from item 53's switch.** Once at launch, a saved `nowPlaying` on with `nowPlayingDesk`
  on (its default) and no now playing in the layout puts `nowPlaying` right after the meters' word in
  the layout (the Claude line's when the meters are off the desktop), where the line used to sit.
  The old keys are ignored after that (`nowPlayingPlacementUpgraded` marks it done).

- **Source.** macOS 15.4 and later answer the MediaRemote "now playing" calls only for Apple-signed
  processes, so Sanduhr bundles [mediaremote-adapter](https://github.com/ungive/mediaremote-adapter)
  (BSD-3, vendored unchanged in `Vendor/mediaremote-adapter/`, built by
  `scripts/build-mediaremote-adapter.sh`) and runs its script with `/usr/bin/perl`: `test` once at
  start and after a wake, then `stream --no-artwork --micros` as a child process, restarted with a
  growing wait if it exits and given up after four quick exits in a row. The findings behind this are
  in `docs/mac-now-playing-spike.md`.
- **Fallback.** When `test` fails (or the stream keeps dying), Music's and Spotify's distributed
  notifications (`com.apple.Music.playerInfo`, `com.spotify.client.PlaybackStateChanged`): no prompt,
  but only those two apps and only from their next change. "Ask Music and Spotify directly" adds
  AppleScript to the running ones when the fallback starts (the Automation prompt, once per app; the
  app's `com.apple.security.automation.apple-events` entitlement and `NSAppleEventsUsageDescription`
  exist for this). The page's Source line says Adapter working, Fallback (Music and Spotify only) or
  Off. Force the fallback with `open -n --env SANDUHR_NOWPLAYING_TEST=fail mac/Sanduhr.app`.
- **Controls** go from Sanduhr's own process through `MRMediaRemoteSendCommand` (not gated).
- **In the bundle:** `Contents/Frameworks/MediaRemoteAdapter.framework`,
  `Contents/Helpers/MediaRemoteAdapterTestClient`, `Contents/Resources/NowPlaying/mediaremote-adapter.pl`,
  all universal. Release builds sign the helper, then the framework, then the app (Developer ID,
  hardened runtime, timestamp); CI checks their architectures and the release workflow their
  signatures.
- **Privacy.** Titles and artists stay in memory: never on disk, in a log or in `state.yaml`
  (`now_playing: {enabled, placed, source, state}` only). No network.

## Fonts

The Desk draws in **EsteFont Pro** (Regular and Bold, version 3.000), the author's handwriting,
which ships inside the app beside its predecessor **EsteFont 26** (Regular and Bold), kept as the
heritage choice: `mac/Resources/Fonts/EsteFontPro-Regular.ttf`, `EsteFontPro-Bold.ttf`,
`EsteFont26-Regular.ttf` and `EsteFont26-Bold.ttf`, copied by `build.sh` to
`Sanduhr.app/Contents/Resources/Fonts/` and sealed by the signature (the build fails if any is
missing or unsealed). At launch Sanduhr registers all four for its own process only
(`CTFontManagerRegisterFontsForURL`, `.process` scope), so nothing is installed on the Mac and other
apps never see them; a copy already installed in Font Book simply draws instead. `BundledFonts`
lists both families; each maps the design's heavier weights (semibold, bold, heavy, black) to its
Bold face (`BundledFonts.face`, `FontSettings.wantsBold`).

- **The Desk** (desk preference `font`): a new install draws in EsteFont Pro. An upgrade keeps the
  font that was on screen, once each (`DeskFont.keepExistingDefault`): a Desk an earlier version
  than 2.6.0 ran with no font picked keeps the system font (`""`; `fontDefaultSettled` marks it
  done), and a Desk 2.6.0 to 2.8.0 ran with no font picked keeps EsteFont 26, the default then
  (`"EsteFont 26"`; `fontProDefaultSettled` marks it done). A picked font stays picked, EsteFont 26
  and EsteFont 2.1 included; one that is no longer installed (the standalone apps' EsteFont 2.1 on a
  Mac without it, say) draws in EsteFont Pro instead (`DeskFont.resolve`). The clock's time uses
  the Bold face of either bundled family.
- **The widget** (`UserDefaults` `fontFamily`) keeps its theme fonts (the system font) unless you pick
  one; with EsteFont Pro or EsteFont 26 picked, its semibold and bold text draws in that family's Bold
  face. Match Desk draws in the Desk's font, either bundled family included.
- Both font pickers (Settings, Desk Look and Settings, Widget Look) list EsteFont Pro first and
  EsteFont 26 right after it, after System.

EsteFont Pro and EsteFont 26 are © 2009-2026 Estevan Hernandez / 626Labs LLC and licensed only for
use by 626Labs LLC and Estevan Hernandez: they are not covered by the MIT license. Their license is
in `THIRD-PARTY-NOTICES.txt`; Settings, About credits both.

## What's New

After Sanduhr updates, a What's New window shows once, a moment after the widget and Desk are up:
one header for the releases it covers ("New in 2.4.0 – 2.6.0"), then a card for each big feature
since the version you last saw (newest first, at most 8), each with a symbol or a small live
preview and a Show me button that opens the right page of Settings. A
fresh install never shows it (onboarding covers that), and while onboarding is up it waits for a
later launch. Tick **Don't show after updates** in the window to stop the automatic showing;
Settings, About has **What's New…** any time, as do the menu bar, widget and Desk clock menus.
The cards live in `Sources/Sanduhr/Models/WhatsNewCards.swift`, one array per release: a release
adds its own array and nothing else changes. Related features share a card, which sits with the
newest release it covers and lists every release it spans; it shows when any of them is new. The last version seen is the `whatsNewLastSeen`
default (`defaults write com.626labs.sanduhr whatsNewLastSeen 2.3.4` fakes an update).

## Welcome tour

A fresh install gets a short tour once, after its first sign-in, as soon as the first fetch
succeeds, so its first card shows your own meters. It uses the What's New window's look, one step
at a time ("Welcome to Sanduhr", "1 of 5"): your limits and the pace tick, the Desk (with Show the
Desk and Match Desk switches), the menu bar (with what it shows) and, on a Mac with a notch, the
notch, more than one account, and Claude Code (the Claude Usage page and Integrations). Choices are
the real settings, written as you make them; Show me opens the widget or the right Settings page
and leaves the tour open. Next and Return move on, Back goes back, Skip the Tour (or Escape) closes
it with every setting as it is, and the last step's Finish ends it. It never installs anything.

It shows once: never after an update, never on a Mac that already had a key, accounts or a What's
New version when this build first ran, and What's New waits while it is pending. Finishing or
skipping records `welcomeTourState` (`finished` or `skipped`) and What's New's last-seen version,
so the next update shows only newer cards. Take the Tour… in Settings, About and the menus opens it
again any time with the current settings, recording nothing. The cards live in
`Sources/Sanduhr/Models/WelcomeTourSteps.swift`; a card that needs a feature this Mac lacks hides.

## Settings previews

Every Settings page that controls something visible opens with a preview card about 160 points
tall (Notch, Layout, Desk Look, Meters, Message, Now Playing, Widget Look, Pacing & Focus, General,
Integrations; Themes keeps its gallery; Mods has a summary card of its counts). Each card is drawn by the surface's own views, never a
mock: NotchView, NotchWingsView, NotchGlowView and AVIndicatorBadge for the notch, DeskPiece (the
Desk's pieces, factored out of DeskView) for Desk Look, Meters, Message and Now Playing,
WidgetCardStack in WidgetGlass (factored out of RootView) for the widget, MenuBarText and
MenuBarItemLook (shared with the status item) for the menu bar, and TerminalPreviewFrame for
Sanduhr's statusline. The views read the same saved settings the page writes, so a change shows
within a frame; their data is a preview `DeskModel` filled from the live one by
`SurfacePreviewData.fill` (through `DeskModel.update`, so the rows come from the Desk's own
functions), with sample meters, demo mode's meetings, a sample track and a sample watcher where
there is no live data yet, named on a "Sample" label. Layout draws `DeskLayoutMap`, a screen-shaped
map with each piece at its place, in its order and at its size, the notch and the Dock on its edge. Previews run under
`isSurfacePreview`: they take no clicks, report no frames and register no click areas; content is
scaled to fit, never cropped; Reduce Motion stills them; each has a one-sentence VoiceOver label.
Nothing captures the screen. `TerminalPreviewFrame` (a dark terminal frame, monospaced, ANSI colors
by ANSIText, powerline glyphs by PowerlineGlyph, an optional animation clock) is shared for later
terminal previews. state.yaml's `settings_preview` names the card the open page shows.

## Settings names

Settings v2 (item 72, `docs/settings-v2-spec.md`) gives each control one name across Settings,
the menus, the tour, What's New and the setup guide. `Models/SettingsNames.swift` holds the names
(Menu Bar Shows, Meetings menu in the menu bar, Notch: the island around the camera, Notch glow,
Claude Code glow hook, Claude meters (line) and (bars), Check for Updates…, Save & Apply, List and
Text) and the retired ones each replaced. A button or menu item that opens Settings reads
`SettingsSection.linkTitle`, "<Page> Settings…" with the sidebar title exactly (Layout Settings…,
Meters Settings…, Integrations Settings…, Accounts Settings…), through `SettingsLinkButton` in
SwiftUI. A click on the notch island opens Settings at Notch. Menu Bar Shows is a submenu of every
Sanduhr menu (`SanduhrMenu.submenus`), not only the menu bar item's. `SettingsNamesTests` checks
the tour, What's New, every menu and every string literal under `Sources/` against the retired
names, and that every "<Page> Settings…" names a real page. No storage key, section raw value or
`sanduhr://` link changed. Smoke: `settings-link "<Page> Settings…"`, state.yaml's `menu_submenus`
and `scenarios/settings-names.yaml`.

## Files

- `sessionKey:{label}` + `cf_clearance:{label}` per account → the Keychain, service `com.626labs.sanduhr` (release builds), or `~/Library/Application Support/Sanduhr/credentials.json` (mode `0600`, dev builds); see First run and Accounts above
- Account labels and the active one → `UserDefaults` (`accounts`, `activeAccount`); following → `followAccount`
- Selected theme → `UserDefaults` (`theme`)
- Meter history → `~/Library/Application Support/Sanduhr/history.{label}.json`, one per account, the Windows format. Each reading is kept 30 days (and at most 8640 points per limit, Windows' cap), trimmed when the next one is written; the sparklines draw the last 24 points (about 2 hours). Settings, Accounts, Meter history: Off stops recording an account (`UserDefaults` `meterHistoryOff`, the labels switched off) and offers to erase its file; Remove Account deletes it. `state.yaml` shows the active account's `history_days` (30, or 0 when off)
- Data choices per account (Claude Code folder, activity, project names, Share with your agents) → `UserDefaults` (`accountData`); the linked folder's path stays there, never in `state.yaml`
- What the MCP server may read → `~/Library/Application Support/Sanduhr/mcp-access.json` (mode 0600; see Claude Code integrations)
- Themes from Claude (item 55) → `theme-request.json` (server) and `theme-result.json` (app), mode 0600 in `~/Library/Application Support/Sanduhr/`; a saved theme in `themes/<key>.json`; the opt-in in `UserDefaults` (`themeClaudeDirect`)
- Desk messages from Claude (item 54) → `desk-messages-request.json` (server), `desk-messages-result.json` and `desk-messages-state.json` (app), all mode 0600 in `~/Library/Application Support/Sanduhr/`; the previous list in `~/Library/Application Support/Desk/messages.txt.previous`; the opt-in in the desk preference `messageClaudeDirect`, the glow in `messageGlow`
- Watchers (item 66) → `watchers.json` (the two switches, written by the app), `watch-request-*.json` (MCP server) and `watch-stop-*.json` (the Stop hook), mode 0600 in `~/Library/Application Support/Sanduhr/`, each deleted as the app reads it; the switches `watchersAgents` and `watchersBackground` in `com.626labs.sanduhr`; the work mark in the account's `accountData` entry (`"work": "true"`). Watchers themselves are in memory only
- Claude Code integrations (items 49 to 51) → scripts and the meters mod in `~/Library/Application Support/Sanduhr/integrations/<stamp>/` behind the `current` link; what each install did in `integrations/installs.json` (mode 0600, holds folder paths); the entries themselves in the chosen folder's `.claude.json` / `settings.json` (the notch glow hooks in its `hooks`), with `<file>.sanduhr-backup` beside each. The mod's "already toasted" keys are in Claude Code's own store for the mod. `state.yaml` shows only `integrations: {mcp_installed, statusline_installed, meters_installed, hooks_installed}`
- Window position → `UserDefaults` (`windowFrame`)
- Now playing (items 53, 53b) → where it shows is the desk preferences `notchLeft`, `notchRight`, `notchStrip` and the `nowPlaying` word in `layout`; the rest is `nowPlayingHidePaused`, `nowPlayingAskApps`, `nowPlayingExcluded` (bundle ids switched off) and `nowPlayingIdle` (When nothing is playing: `automatic` when unset, or a notch content's raw value). Item 53's `nowPlaying` and `nowPlayingDesk` are read once by the upgrade (`nowPlayingPlacementUpgraded`); what plays stays in memory
- EsteFont Pro and EsteFont 26 → `Sanduhr.app/Contents/Resources/Fonts/`, from `mac/Resources/Fonts/` (see Fonts); the Desk's choice in the desk preference `font`
- Third-party notices (Sparkle, mediaremote-adapter, EsteFont Pro, EsteFont 26) → `Sanduhr.app/Contents/Resources/THIRD-PARTY-NOTICES.txt`, from `mac/THIRD-PARTY-NOTICES.txt`; Settings, About opens it

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
| Two-finger click widget     | Menu Bar Shows, Tools, Refresh, Settings, Quit menu |
| Two-finger click a tier card | Accounts, Hide (temporary limits), Stop warnings, Hidden Limits, Meters Settings…, then the widget menu |
| Click a Desk meter          | Nothing: the meters are passive, clicks there do nothing |
| Two-finger click a Desk meter | Show or Hide Widget, then the same limit menu |
| Click the Desk clock / message | Settings, Desk Look / Settings, Desk, Message (while "Clock and message take clicks" is on) |
| Two-finger click the Desk clock, message or claude line | Desk Look Settings… (clock) or Edit Messages… (message), then the shared menu (its Settings… as All Settings…) |
| Click now playing (notch or Desk) | Play or pause (item 53) |
| Click Next on a paused wing or strip | Next track (item 53b) |
| Two-finger click now playing | Previous, Play/Pause, Next, Now Playing Settings… |
| Right-click the hourglass   | The widget menu plus Menu Bar Shows (Session, Weekly, Whichever is higher, Rotate) |
| **×**                       | Hide the widget (Desk keeps running) |

## License

MIT. Python original by [626Labs LLC](https://626labs.dev). Third-party code: Sparkle (MIT) and
mediaremote-adapter (BSD-3-Clause), with their licenses in `THIRD-PARTY-NOTICES.txt`. The bundled
EsteFont Pro and EsteFont 26 are proprietary (626Labs LLC), not MIT; their license is in the same file.
