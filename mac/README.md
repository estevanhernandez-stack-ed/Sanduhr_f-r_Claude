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
  Personal's once. Rename moves the file, Remove deletes it, Sign Out keeps it.
- **Readers.** `snapshot.json` names the active account by `account_ref` (first 4 bytes of the
  SHA-256 of the label, as on Windows) and is deleted at once on a switch; `state.yaml` has
  `account_ref`, `accounts_count`, `follow` and `follow_paused`. Labels never go into a log,
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

## Files

- `sessionKey:{label}` + `cf_clearance:{label}` per account → the Keychain, service `com.626labs.sanduhr` (release builds), or `~/Library/Application Support/Sanduhr/credentials.json` (mode `0600`, dev builds); see First run and Accounts above
- Account labels and the active one → `UserDefaults` (`accounts`, `activeAccount`); following → `followAccount`
- Selected theme → `UserDefaults` (`theme`)
- Sparkline history → `~/Library/Application Support/Sanduhr/history.{label}.json`, one per account
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
| Two-finger click a tier card | Accounts, Hide, Stop warnings, Meter Settings, then the widget menu |
| Click a Desk meter          | Nothing: the meters are passive, clicks there do nothing |
| Two-finger click a Desk meter | Show or Hide Widget, then the same limit menu |
| Right-click the hourglass   | The widget menu plus Menu Bar Shows (Session, Weekly, Whichever is higher, Rotate) |
| **×**                       | Hide the widget (Desk keeps running) |

## License

MIT. Python original by [626Labs LLC](https://626labs.dev).
