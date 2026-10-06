# Backlog

## #1 — Sign Out must actually purge stored credentials, both platforms (shipped)

**Shipped.** Mac: Sign Out in Settings purges the account's Keychain items and the legacy file (2.3.3, PR #106; per-account slots in 2.4.0; a dev build never touches release items, `ace2f57`). Windows: Sign Out already removed the account's Credential Manager targets through `AccountStore.RemoveAccount`. Kept below for the record.

Uninstall cannot do this on either channel — this is architectural, not a gap
that gets fixed by adding an uninstall hook:

- **Windows:** Credential Manager (`advapi32.dll` CredWriteW / CRED_PERSIST_ENTERPRISE)
  is an OS-level per-user vault, not package-scoped storage. MSIX package removal
  never reaches it — `Package.appxmanifest` runs the app outside the MSIX sandbox
  specifically so it can read/write `%APPDATA%\Sanduhr\` directly, and Desktop
  Bridge full-trust packaging doesn't virtualize the Credential Manager API either
  way. The Velopack GitHub/.exe channel's only uninstall hook
  (`windows-dotnet/src/Sanduhr.App/Program.cs:27-34`, `OnBeforeUninstallFastCallback`)
  unregisters toast notifications only — no credential deletion call exists in it.
  Net: every `{slot}@com.626labs.sanduhr` compound target (one pair per saved
  account) survives uninstall on both channels.
- **macOS:** no uninstall hook exists at all — `mac/Sources/Sanduhr/AppDelegate.swift`
  has no `applicationWillTerminate` cleanup. Dragging to Trash never touches
  `~/Library/Application Support/Sanduhr/` (outside the `.app` bundle), so
  `credentials.json` survives indefinitely.

**The only correct fix on either platform is the in-app Sign Out path already wired
correctly today** — `windows-dotnet/src/Sanduhr.App/ViewModels/WidgetViewModel.cs:719-755`
→ `AccountStore.RemoveAccount` (`windows-dotnet/src/Sanduhr.Core/AccountStore.cs:96-113`)
already deletes the right Credential Manager targets on Windows. Work needed:

1. Confirm Sign Out reliably purges every credential slot it should, on every
   code path (including the "last account" case that should also clear the
   shared WebView2 profile — `WidgetViewModel.cs` handles this per the privacy
   sweep, but wasn't independently re-verified this session).
2. macOS has no Sign Out credential-purge path documented as existing yet —
   build one that deletes `~/Library/Application Support/Sanduhr/credentials.json`
   (see `mac/Sources/Sanduhr/Services/KeychainStore.swift`).
3. Make Sign Out (not uninstall) the one documented, tested, reliable way to
   clear credentials on both platforms — it already is in the docs (this session
   corrected README.md, SECURITY.md, docs/PRIVACY.md, docs/index.html,
   mac/README.md, docs/store/product-features.md to say so honestly); now the
   code needs the same bar macOS is missing.

## #2 — macOS: migrate credential storage from plaintext JSON to the system Keychain (shipped)

**Shipped in 2.3.2** (PR #100): credentials live in the Keychain under `com.626labs.sanduhr`, the plaintext file migrates in and is removed; docs updated in `259c20b`. Kept below for the record.

`mac/Sources/Sanduhr/Services/KeychainStore.swift:1-87` (type name is a holdover —
it does not call any Keychain API; grepped `SecItemAdd|SecItemCopyMatching|SecItemDelete|import Security`,
the only hit is the file's own doc comment referencing `SecItemAdd` as considered-and-abandoned).
Stores `sessionKey` + `cf_clearance` as plaintext JSON at
`~/Library/Application Support/Sanduhr/credentials.json`, protected only by
POSIX `0600` (`KeychainStore.swift:56-86`).

**Why now:** the file's own doc comment (`KeychainStore.swift:11-26`) states this
was a deliberate interim tradeoff — default Keychain ACLs bind to the exact code
signature that wrote an item, so unstable ad-hoc signing during iteration
triggered a Keychain prompt on every rebuild. `mac/release.sh:9-10` (prerequisite:
Developer ID Application cert) plus the signed+notarized build steps confirm
**the stated blocker is resolved** — the app ships with a stable signature today.
The precondition to revert to real Keychain has been met; the code hasn't been
changed back.

## On ship (both items above)

This repo's docs were updated with 2.3.2 (`259c20b`). Still to confirm: the Sanduhr subsection of the 626labs.dev privacy page describes the fixed behavior.

Update, in the same session the fix lands:

- This repo's docs: `README.md`, `SECURITY.md`, `docs/PRIVACY.md`, `docs/index.html`,
  `mac/README.md`, `docs/store/product-features.md` — all corrected 2026-08-03 to
  describe today's *actual* (broken) behavior; once Sign Out reliably purges
  credentials and macOS moves to Keychain, these need a second pass to describe
  the *fixed* behavior.
- `https://626labs.dev/privacy.html#privacy-sanduhr` — the canonical, cross-app
  privacy policy. Its Sanduhr subsection currently documents the un-fixed
  behavior honestly (uninstall doesn't clear credentials on either platform;
  macOS is plaintext, not Keychain). Update it once the fix ships.

## Evidence pointers (for the next session)

- `C:\Users\estev\Projects\626labs-hub\.superpowers\sdd\privacy-findings-2026-08-03.md`
  — §2 (Sanduhr), full six-app privacy sweep.
- `C:\Users\estev\Projects\626labs-hub\.superpowers\sdd\sanduhr-uninstall-rororo-signing-2026-08-03.md`
  — §Q1, exact file:line evidence for both platforms + drafted honest policy sentences.
- `windows-dotnet/src/Sanduhr.App/Program.cs:27-34` — the one Windows uninstall
  hook that exists, and what it actually does (not credential cleanup).
- `windows-dotnet/src/Sanduhr.Core/WindowsCredentialManager.cs:66-213` — the real
  Credential Manager P/Invoke layer (`CredReadW`/`CredWriteW`/`CredDeleteW`).
- `windows-dotnet/src/Sanduhr.App/ViewModels/WidgetViewModel.cs:719-755` — the
  working Sign Out → credential-delete path to build on.
- `mac/Sources/Sanduhr/Services/KeychainStore.swift:1-87` — the plaintext store
  masquerading under a Keychain-shaped name; doc comment explains the original
  tradeoff and its now-met precondition to revert.
- `mac/release.sh:9-10,47-54` — confirms Developer ID signing + notarization are
  live today, closing the stated blocker to real Keychain.

## #3 — New user walkthrough (spec'd)

**Spec'd 2026-10-05 as a shared tour on the What's New cards: `docs/welcome-tour-spec.md`** (checklist item 61). The sketch below is kept for the record.

Raised 2026-10-05. A new install today gets the Welcome sheet (paste the `sessionKey`) and then a
widget, and nothing points at the rest: the Desk, the notch wings and glow, the menu bar, accounts,
the Claude Usage page, the integrations, themes. What's New (2.6.0) covers what changed for people
who updated; nothing covers what exists for people who just arrived.

Shape to spec:

- After the first successful sign-in, a short skippable walkthrough: four to six steps, each
  pointing at the real thing on screen (the widget, a Desk corner, the notch, the menu bar icon)
  with a sentence and a Show me / Next, plus "Set this up later" that leaves everything off.
- Choices made along the way are real settings: Desk on or off, the notch wings, the menu bar
  readout, a theme or Match Desk. Defaults stay what they are when skipped.
- Reuse What's New's card table and Show me routing where it fits; the walkthrough is data, so
  later features add a step without code.
- Reopen any time: Settings, About and the Help menu ("Take the Tour…").
- Never during the Welcome sheet; never again after it finishes or is skipped (a defaults flag),
  and updates never show it (What's New handles those).
- Accessibility: keyboard through every step, VoiceOver labels, Reduce Motion respected.
- Windows parity as its own item once the Mac version settles.

Open questions: whether the walkthrough also offers the MCP integrations (likely a last, optional
step), and whether a demo mode (made-up numbers until real usage arrives) helps the first minute.
