# Mac accounts (item 36)

Named Claude accounts on the Mac, ported from Windows 2.2 (`AccountStore`, `UsageHistory`,
`SnapshotContract.AccountRef`, `WidgetViewModel.SwitchAccount`). One account is active and only
it is fetched (decided 2026-10-03). The all-accounts chart and CSV export are item 37, later.

## What an account is

A label plus its own session key and optional `cf_clearance`.

- **Labels:** 1 to 32 characters, letters, digits, space, underscore and hyphen
  (`^[A-Za-z0-9 _-]{1,32}$`, the Windows rule). Unique. The first account is "Personal".
- **Registry:** the ordered list of labels and the active label, in the app's defaults
  (`accounts`, `activeAccount`). Windows keeps them in Credential Manager; on the Mac the
  defaults need no Keychain read, so the menus and the Accounts page never prompt. Labels are
  not secrets, but they never go into a log, `snapshot.json` or `state.yaml`.
- **Secrets:** the active store (the Keychain on signed builds, the file on dev builds, as
  2.3.2 set up), under the Windows slot names `sessionKey:{label}` and `cf_clearance:{label}`.

## Upgrade: the current key becomes Personal

On first launch, after the 2.3.2 file-to-Keychain move:

1. Registry empty and a legacy `sessionKey` slot present: write `sessionKey:Personal` (and
   `cf_clearance:Personal`), read them back, then delete the legacy slots. Any failure keeps the
   legacy slots and runs this launch on them (the 2.3.2 rule: never lose the key mid-move).
2. `history.json` is renamed to `history.Personal.json` once, never over an existing file.
3. The sign-in marker from #105 (`signedInFetchDone`) becomes per account: a set of labels that
   have fetched, seeded with Personal when the old flag is set. The widget's sign-in rule asks
   about the active account.
4. Nothing else changes: same theme, same settings, same widget visibility.

A fresh install has no accounts until the first key is saved, which creates Personal (the
onboarding sheet and Credentials keep working as today).

## Switching

From the widget's account chip, the Accounts menu or Settings, Accounts. A switch:

1. Sets the active label.
2. Deletes `snapshot.json` at once, so a reader never sees the new account's freshness on the
   old account's numbers (Windows WS-E). The next good fetch rewrites it, with `account_ref`.
3. Clears the shown usage and the menu bar percent, says "Switching account…", resets the
   alert memory for the new account's windows (as Windows resets its alert engine), loads that
   account's history.
4. Builds a new API client from the new account's key (a fresh client, so no cookie or org id
   carries over; the org rule from item 35 runs again) and fetches.

A switch never needs a relaunch. Desk and the notch follow the active account.

## Sign Out and Remove are different

Windows' account-scoped sign-out removes the account. The Mac keeps item 32's promise instead
(history and settings stay) and offers both:

- **Sign Out** (per account): deletes that account's key and `cf_clearance` from both stores.
  The account stays in the list, signed out, with its history. Signing in again is pasting a
  key into it. Signing out the active account shows the item-32 signed-out state.
- **Remove Account**: Sign Out, plus delete its history file and drop it from the list. Its own
  confirmation, naming what goes. Removing the active account switches to the next one, or to
  the no-account state when it was the last.

## Where it shows

- **Settings, Accounts** replaces Settings, Credentials: the list (the active one marked), and
  for the selected account its key fields (Save), Rename, Sign Out, Remove, Make Active; Add
  Account takes a label and a key.
- **Menus:** an Accounts submenu in the widget and menu bar menus (`SanduhrMenu`): each
  account, the active one checked, then "Manage Accounts…". Shown only with two or more.
- **Widget:** the active label as a chip in the title area, only with two or more accounts;
  a click cycles to the next (Windows `CycleAccount`).
- **Desk and notch:** unchanged with one account. With two or more, the Desk's claude line
  starts with the label. The notch stays as it is (no room).

## Privacy and readers

- `snapshot.json` carries `account_ref`: the first 4 bytes of SHA-256 of the label, lowercase
  hex, exactly `SnapshotContract.AccountRef`, so the statusline and `sanduhr-mcp` detect a
  switch the same way on both platforms. Never the label.
- `state.yaml` adds `account_ref` and `accounts_count`. No labels.
- Logs name no labels (Windows' PRIVACY.md rule); failures name the slot kind only.
- `docs/PRIVACY.md` and `mac/README.md` describe the per-account slots and files.

## Debug hooks

`sanduhr://debug/action?name=account&arg=next` cycles the active account (safe: nothing is
deleted). No hook adds, renames, signs out or removes an account.

## Testing

- swift-testing on fake backends and temp folders: label rules; add, rename (secrets, history
  file and marker follow), remove (active moves to the next); the legacy promotion including a
  failed write; history rename on upgrade; switch clears usage and deletes the snapshot;
  `account_ref` matches the Windows hash for known labels; Sign Out keeps the account and
  history, Remove drops both.
- Smoke: the settings scenario expects the Accounts page; `state.yaml` keys.
- By hand, on the file store with two keys (back up first, restore through the release app's
  file-wins migration, per the dev-build Keychain lesson).

## Build order

One branch, one commit each: (1) registry, slots and the Personal promotion; (2) history per
account and the per-account marker; (3) switching, the API client rebuild and `account_ref`;
(4) Settings, Accounts, the menus and the widget chip; (5) Desk line, smoke, docs. Ships as
2.4.0: a new capability, not a fix.

## Open questions

1. Sign Out and Remove as two actions (above), or Windows' single remove?
2. The registry in defaults (above), or the Keychain like Windows?
3. The label on the Desk's claude line with two or more accounts, or menus and widget only?
