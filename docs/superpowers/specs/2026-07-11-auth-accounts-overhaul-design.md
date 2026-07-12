# WS-A — Auth & accounts overhaul (design)

Approved 2026-07-11. First of four workstreams from the notes triage (`docs/roadmap-2026-07-11.md`). Windows build only (`windows-dotnet/`).

## Problem

Three account-lifecycle defects and one missing affordance, all confirmed by recon at exact sites:

1. **Reauth ignores account origin.** The account registry is a bare label list plus two credential slots (`AccountStore.cs`) — no record of whether an account was created via the embedded browser or manual key paste. The recovery card's primary button therefore routes *every* expired/blocked account to the embedded browser flow (`WidgetViewModel.PrimaryAuth`, `WidgetViewModel.cs:410-423` → `SignInCoordinator.ReauthenticateActiveAsync`, `SignInCoordinator.cs:60-61`). Manual-origin accounts — dominated by Google-SSO users, who are forced onto manual entry because Google login renders blank in WebView2 — get a sign-in flow that can't work for them.
2. **Manual re-auth duplicates accounts.** "Paste a key instead" during recovery runs `SignInManual` → `PersistManual` (`SignInCoordinator.cs:196-201`), which always `AddAccount` + `SetActive` with a fresh "Account N" label. There is no manual equivalent of `PersistReauth` (the in-place overwrite the browser path has), so re-authing by paste leaves the dead account in the registry and adds a duplicate.
3. **Delete and rename leak state.** "Sign out" (the delete) wipes `history.{label}.json` to `{}` but never unlinks it (`UsageHistory.ClearAll`, `UsageHistory.cs:188-194`) — while the confirm dialog claims the file is deleted. Deleting the active account when it's the last one leaves its live claude.ai session cookies in `%APPDATA%\Sanduhr\webview2-fetch\` indefinitely (the anti-bleed wipe only runs on next client init, which never comes — `WebView2ApiClient.cs:277-281`, `WidgetViewModel.cs:371-380`). Rename copies credential slots but not the history file, orphaning it under the old name (`AccountStore.RenameAccount`, `AccountStore.cs:114-133`).
4. **No per-account reauth.** Reauth exists only on the widget recovery card and only for the *active* account. An expired non-active account is unreachable without switching to it first; the Settings Accounts tab offers add/switch/rename/sign-out only.

## Goals

- Reauth routing respects account origin; manual-origin accounts land on the paste modal first.
- Manual re-auth is in-place for all origins; duplicates only ever come from explicit add flows.
- Delete removes every store that holds per-account state; rename carries state along.
- Any account can be re-authenticated from Settings, active or not.
- Every touched or new surface is fully themed (see Theming, which applies to all sections).

## Non-goals

- No changes to transports, `UsageFetcher`, tier model, or polling (WS-B/C/D territory).
- No multi-account background polling (WS-D).
- No change to how the embedded browser flow captures cookies.
- macOS build untouched.

## Design

### 1. Origin flag

New per-label credential slot in the existing store (service `com.626labs.sanduhr`):

- **Slot:** `origin:{label}`, value `"embedded"` or `"manual"`.
- **Written by:** `PersistEmbedded` (→ `embedded`), `PersistManual` (→ `manual`), and both in-place reauth persists (a successful browser reauth sets `embedded`; a successful manual reauth sets `manual` — origin tracks the *latest working* method, so an account "migrates" if the user switches methods).
- **Read as:** missing slot ⇒ `embedded` (legacy default — existing accounts behave exactly as today; no migration pass needed).
- **Lifecycle:** created/updated alongside `sessionKey:{label}`; deleted in `RemoveAccount`; renamed in `RenameAccount` (same copy-then-delete pattern as the secret slots).
- **API:** `AccountStore` gains `GetOrigin(label)` / `SetOrigin(label, origin)` with a small `AccountOrigin` enum in `Sanduhr.Core`. No change to the `accounts:list` format — additive only, byte-compatible with everything that reads the registry today.

Rejected alternatives: upgrading `accounts:list` to an object array (format change, migration, breaks other readers for one field); sidecar JSON in `%APPDATA%` (splits account truth across two stores).

### 2. Origin-aware reauth routing

`WidgetViewModel.PrimaryAuth` (the single routing decision point) consults the active account's origin when `Reason` is `Expired` or `Blocked`:

- **Embedded-origin (and legacy):** unchanged primary — embedded browser reauth (`ReauthenticateActiveAsync`), in-place via `PersistReauth`.
- **Manual-origin:** primary opens the paste modal in **reauth mode** — label pre-filled and read-only, sessionKey/cf fields empty — persisting in place. Secondary link: "Use browser sign-in instead" → embedded reauth.
- **Both origins:** the recovery card's "Paste a key instead" secondary becomes in-place (see §3); it no longer adds an account when `Reason` is `Expired`/`Blocked`. FirstRun keeps today's add semantics.

`SignInPromptCopy` grows origin-aware copy variants (the doc comment at `SignInPromptCopy.cs:13` currently hard-codes the "never key paste" assumption — it goes). Button copy stays declarative: "Paste a new key" primary for manual-origin, "Sign in again" for embedded.

### 3. In-place manual reauth

`SignInCoordinator` gains the missing half:

- **`ReauthenticateManualAsync(owner, label)`** — opens `ManualKeyWindow` in reauth mode with a **`PersistReauthManual`** delegate: validate, then `AccountStore.SaveCredentials(label, creds)` + `SetOrigin(label, Manual)` — overwrite in place, no `AddAccount`, no label prompt. Mirrors `PersistReauth` (`SignInCoordinator.cs:122-132`).
- `ManualKeyWindow` gets a constructor/mode flag: reauth mode hides/locks the label field and takes the persist delegate as today (the delegate is already the seam — the window itself doesn't decide add-vs-overwrite).
- The degraded paths inside `ReauthenticateActiveAsync` that currently fall back to `SignInManual` (`SignInCoordinator.cs:87, 108, 113`) fall back to the manual **reauth** variant instead, so a browser reauth that bounces to paste stays in-place.
- The bounce in the other direction (manual modal → "use browser instead") re-enters the embedded flow with the in-place persist, preserving reauth semantics both ways.

### 4. Per-account reauth in Settings

Accounts tab gains **"Update sign-in…"** for the selected account:

- Routes by *that account's* origin: manual → `ReauthenticateManualAsync(label)`; embedded → an embedded capture persisting to that label (a label-targeted variant of `PersistReauth` using `AccountStore.SaveCredentials(label, …)` instead of the active-account facade).
- Works without switching the active account; the embedded capture already uses an isolated per-capture browser profile, so no cookie cross-bleed with the fetch transport.
- On success for the *active* account, trigger the usual fetcher rebuild + refresh so the recovery card clears immediately.
- `AccountsViewModel` gets the command + wiring via the same delegate-injection pattern the add flow uses (`App.xaml.cs:140`).

### 5. Complete delete

`WidgetViewModel.SignOutAccountAsync` becomes a true removal:

- `UsageHistory` gains `Delete(label)` — unlink `history.{label}.json` (best-effort; `ClearAll` remains for other callers).
- `AccountStore.RemoveAccount` also deletes `origin:{label}`.
- **Cookie jar:** when the removed account was active, dispose the current `WebView2ApiClient` first; if accounts remain, the existing rebuild path's init-time wipe covers it (unchanged). If **no accounts remain** (the leak case), clear the `webview2-fetch` profile's cookies best-effort after disposal — implementation may clear cookies via a short-lived client or delete the profile directory; either way failures are logged, never thrown, and the directory is recreated on demand.
- Order: dispose client → remove credentials → delete history → clear jar (if last) → update UI state. Any step's failure still proceeds to the next; the account must never remain half-registered.

### 6. Rename carries state

`RenameAccount` (or its `WidgetViewModel` caller) moves `history.{old}.json` → `history.{new}.json` (`File.Move`, best-effort, after the credential-slot copy succeeds) and renames the `origin` slot with the other slots. Rename of a label that collides with an existing history file overwrites only after the registry rename succeeded.

### 7. Copy and naming

- Settings button "Sign out" → **"Remove account…"**; tray/menu strings audited for the same verb.
- Confirm dialog copy states exactly what happens: removes saved sign-in from Windows Credential Manager, deletes the usage-history file, cannot be undone. (Today's copy claims a file deletion that doesn't happen.)
- Recovery-card copy per §2. All new strings live where the existing copy lives (`SignInPromptCopy` for card copy; XAML resources for buttons) — no inline literals.

### Theming (applies to every section)

Hard requirement carried from approval: **every surface this workstream touches or creates uses the app's theming system end-to-end.**

- **No hardcoded brushes, colors, or fonts** in any new or modified XAML/control — only existing theme resource keys. New UI must not introduce new theme tokens unless unavoidable; if a new token is genuinely required, it gets a sensible default derived from existing tokens so **user JSON drop-in themes and AI-agent-generated themes inherit it without edits**, and `docs/themes/AGENT_PROMPT.md` is updated in the same PR.
- **Dialogs:** all confirms and notices (remove-account confirm, reauth success/failure notices) use the themed in-app dialog system — never `MessageBox`.
- **`ManualKeyWindow`:** audited for theme compliance while adding reauth mode (verify it consumes theme resources today; fix any hardcoded styling found). Reauth mode's read-only label styling uses themed disabled/readonly states.
- **Recovery card variants:** the manual-primary variant renders identically across themes — verify against at least the default theme, one dark, one light, and Matrix (the Mica opt-out case).
- **Settings Accounts tab:** the "Update sign-in…" button matches the existing button row styles exactly.
- **Live apply:** theme switching while any of these surfaces is open re-styles them live, matching existing behavior.
- **Acceptance:** flipping through built-in themes with the reauth modal, recovery card, and Accounts tab visible shows zero unstyled or stale-colored elements.

## Error handling

- Reauth persist failures (Credential Manager write errors) surface via the themed dialog with the exact failing operation; the account's previous credentials remain untouched (write-then-verify, never delete-then-write).
- Manual reauth validates key format like the add flow before persisting.
- Delete/rename file operations are best-effort with logged failures (`sanduhr.log`); registry consistency always wins over file cleanup.
- A reauth targeting a label deleted mid-flow (race with Settings) aborts with a themed notice, never recreates the account.

## Testing

- **Core (xUnit, existing suite):** origin round-trip + legacy-missing default; `RemoveAccount` deletes origin slot; rename moves origin slot + history file; delete unlinks history file; `PersistReauthManual` semantics via the delegate seam (overwrite, no add).
- **Coordinator logic:** routing table (origin × reason → flow) extracted into a pure, testable decision function rather than branching inline in the viewmodel — mirrors `SignInPromptCopy`'s pure-copy pattern and gets a truth-table test.
- **Manual smoke (added to `docs/smoke-test-plan.md`):** manual-origin expiry → paste-primary card; embedded-origin expiry unchanged; paste-during-recovery does not duplicate; Settings reauth of non-active account; last-account delete leaves no cookies dir content; rename carries history; theme flip across all touched surfaces.

## Compatibility & migration

None required. Missing origin slot ⇒ embedded (today's behavior). Downgrade-safe: older builds ignore the extra slot. `accounts:list` format unchanged.

## Implementation process

Repo rules apply in full: `gitnexus_impact` (upstream) on every symbol before editing — expected hot spots `PrimaryAuth`, `ReauthenticateActiveAsync`, `SignInManual`, `RemoveAccount`, `RenameAccount`, `SignOutAccountAsync` — `gitnexus_detect_changes` before commit, index refresh after. Blast radius is contained to: `AccountStore`, `CredentialStore` (read-only usage), `SignInCoordinator`, `SignInPromptCopy`, `SignInReason` consumers, `WidgetViewModel` (auth/account methods only), `AccountsViewModel`, `ManualKeyWindow`, `MainWindow.xaml` (recovery card), `SettingsWindow.xaml` (Accounts tab), `UsageHistory` (new `Delete`), `WebView2ApiClient` (disposal/cookie-clear only).
