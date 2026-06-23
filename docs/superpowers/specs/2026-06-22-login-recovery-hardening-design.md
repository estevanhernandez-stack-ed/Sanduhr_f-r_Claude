# Login & key-entry recovery hardening — design

- **Date:** 2026-06-22
- **App:** Sanduhr für Claude — Windows .NET 10 / WPF build (`windows-dotnet/`)
- **Branch base:** `polish/session-expired-signin-copy` (off the `feat/dotnet-rebuild` / v3.0.0 trunk)
- **Status:** Approved in principle (approach + tier ordering + probe cut). Pending spec review.

## Problem

The v3.0.0 rebuild shipped an embedded WebView2 sign-in that genuinely removes the old "open DevTools, copy the `sessionKey` cookie, paste it" ritual for a first-run user. That happy path works. Every remaining rough edge is on the **recovery and fallback paths** — the exact moments a stuck user reverts to the DevTools mental model.

The root cause is a single one: the app models only **two** auth states ("has a key" / "no key") when reality has **five** — no key, valid, expired, Cloudflare-blocked, runtime-missing. `WidgetViewModel.ShowSignInPrompt` is one bool that is only ever set true on an *empty* stored key (`RebuildFetcher`, [WidgetViewModel.cs:329](../../../windows-dotnet/src/Sanduhr.App/ViewModels/WidgetViewModel.cs)). An *expired* key is non-empty, so it slips past every recovery affordance: the `SessionExpiredException` catch writes a non-interactive status string and the trusted "Sign in to Claude" card never appears.

### Friction inventory (from the acquaint pass)

| # | Issue | Severity |
|---|-------|----------|
| 1 | Session-expired recovery is a dead-end string, not an action | HIGH |
| 2 | Root `README.md` still teaches the DevTools ritual as the primary first-run flow | HIGH |
| 3 | `ManualKeyWindow` instructs "DevTools → Application → Cookies" with no escape back to embedded | MED |
| 4 | WebView2-missing routes to manual paste; "Install Now" dead-ends with no post-install retry | MED |
| 5 | No in-place re-auth — recovery either mints a duplicate "Account 2" or deletes history | MED |
| 6 | Cloudflare 403 points the user back to the manual `cf_clearance` box | MED |
| 7 | **(newly found)** `MigrateFromV1()` is never wired at startup — legacy upgraders read as empty | HIGH |

Item 7 was found during pre-flight verification: `MigrateFromV1()` / `AccountStore.MigrateLegacy()` exist and are unit-tested, but are called **only from tests** — never from `App.OnStartup`, `WidgetViewModel.Start()`, or `RebuildFetcher`. A user upgrading from a pre-v2.2.0 Python install (bare `sessionKey` keyring slot, or a v1 plaintext config) is therefore not promoted to a `Personal` account, reads as empty, is shown the first-run prompt, and loses local history continuity. This sits directly on Tier 1's FirstRun-vs-Expired detection, so it is fixed here as a prerequisite.

## Goals

- Every auth-recovery surface routes through one trusted, state-aware affordance.
- Session expiry and Cloudflare challenge become **one-click, in-place re-auth** that refreshes the *active* account — history preserved, no duplicate slot.
- The embedded window degrades gracefully on slow load, load failure, and missing runtime, always with a path that does not require DevTools.
- The manual paste path remains for power users, but can bounce back to embedded.
- Docs (root README, CHANGELOG) describe the flow the app actually ships.
- Legacy upgraders keep their account and history.

## Non-goals (YAGNI)

- **Proactive "is the key still valid?" startup probe — cut.** Once Tier 1 lands, the existing `Start() → RefreshAsync()` first fetch *is* the probe: an expired key throws on it and now surfaces the actionable card. A second network round-trip at startup adds offline-regression risk for zero new coverage.
- No formal `AuthState` state-machine refactor of the fetch loop (considered as approach #3, rejected — blast radius exceeds the value here).
- No change to the credential storage backend, the API transport, or the parsing layer.

## Approach

**Unify around an explicit recovery reason, then fix each edge against it** (approach #2 of 3 considered).

Introduce a small `SignInReason` concept on `WidgetViewModel` — `None` / `FirstRun` / `Expired` / `Blocked` — that drives both the recovery card's copy and its action. Add one new **in-place re-authentication** path in `SignInCoordinator` that overwrites the active account's credential slots instead of allocating a new label. The pure decision logic (which reason, which copy) lands in `Sanduhr.Core` where it unit-tests without a browser, matching the existing `ClaudeSignIn` seam.

Rejected alternatives: per-item patches (#1 — edges drift and we revisit in three months); full auth state-machine (#3 — over-built for this surface).

## Design

### Tier 1 — close the real gap (items 1, 2, 5, 6, 7)

**1.1 Wire legacy migration at startup (item 7, prerequisite).**
Call `_credentials.MigrateFromV1()` once in `WidgetViewModel.Start()` **before** the first `RebuildFetcher()`. `MigrateLegacy()` is idempotent (no-ops when the registry already has accounts), so this is safe on every launch. This makes the FirstRun signal truthful for upgraders.

**1.2 `SignInReason` drives the recovery card.**
Add an enum `SignInReason { None, FirstRun, Expired, Blocked }` (in `Sanduhr.Core`) and a `SignInReason Reason` observable on `WidgetViewModel`. (Runtime-missing is deliberately *not* a card reason — it is a transient state handled inside `SignInCoordinator`'s modal flow, never a persistent widget state.) `ShowSignInPrompt` becomes a derived `Reason != None`. The card's headline/subtitle bind to the reason via a pure `Sanduhr.Core` helper `SignInPromptCopy.For(reason) → (Headline, Subtitle, PrimaryLabel)`:

- `FirstRun` → "Track your Claude usage" / "Sign in once in a secure window. Sanduhr reads your usage automatically — no DevTools, no copy-paste." / "Sign in to Claude"
- `Expired` → "Session expired" / "Your sign-in timed out. Sign in again — it takes a few seconds, no DevTools." / "Sign in again"
- `Blocked` → "Connection challenged" / "Cloudflare needs a fresh check. Sign in again to refresh it automatically." / "Sign in again"

The card XAML ([MainWindow.xaml:324](../../../windows-dotnet/src/Sanduhr.App/MainWindow.xaml)) keeps its layout; the three `TextBlock`/`Button` literals become bindings. The "Paste a key instead" secondary link stays.

**1.3 Catches set the reason instead of a dead string.**
In `RefreshAsync` ([WidgetViewModel.cs:486](../../../windows-dotnet/src/Sanduhr.App/ViewModels/WidgetViewModel.cs)):
- `catch (SessionExpiredException)` → `Reason = Expired` (reveals the card). Drop the `Fail("Session expired…")` status-string write.
- `catch (CloudflareBlockedException)` → `Reason = Blocked`. Drop the `Fail("Cloudflare — add cf_clearance.")` write.
- Network/HTTP/other catches keep `Fail(...)` (transient, not an auth problem).
On a successful fetch, `Reason = None`.

**1.4 In-place re-auth path (item 5).**
- New `SignInCoordinator.ReauthenticateActiveAsync(Window? owner)`: runs the same embedded WebView2 flow as `SignInEmbeddedAsync`, but persists via a new `PersistReauth(CapturedCookies)` delegate that writes to the **active** account (`_credentials.Save(sessionKey, cfClearance)` — overwrites in place) rather than `NextFreeLabel` + `AddAccount`. History file `history.{label}.json` is keyed by label and untouched.
- `WidgetViewModel` raises a new `ReauthRequested` event when the card's primary action fires while `Reason ∈ {Expired, Blocked}`; `App` wires it to `coordinator.ReauthenticateActiveAsync(_window)`. When `Reason == FirstRun` (or tray "Add account"), the existing `SignInRequested → RunSignInAsync(embedded:true)` add-account path is unchanged.
- Mismatch guard: if the user signs into a *different* claude.ai account during a re-auth, we still overwrite the active slot's credentials (the label is a local alias, not the identity). Documented behavior, not an error. No silent account-identity tracking in this pass.

**1.5 Docs (item 2).**
- Rewrite root [README.md](../../../README.md) "First-run setup" (lines ~130-141) to describe the embedded WebView2 sign-in as the primary flow, with manual paste named as the labeled fallback. Remove the stale "Settings → Credentials" reference (the tab is "Accounts"). Mirror the already-correct `windows-dotnet/README.md` copy.
- Add the v3.0.0 entry to `CHANGELOG.md` documenting the embedded sign-in (it currently tops out at v2.3.0).

### Tier 2 — harden the embedded window (items 4 + window robustness)

**2.1 Sign-in timeout + in-window error + escape hatch.**
`SignInWindow` ([SignInWindow.xaml.cs](../../../windows-dotnet/src/Sanduhr.App/Views/SignInWindow.xaml.cs)) gains:
- A load timeout (e.g. 30s from `OnLoaded`): if no successful first navigation, swap `LoadingOverlay` for an in-window error panel with "Try again" and "Paste a key instead" buttons.
- A `NavigationCompleted` failure (`e.IsSuccess == false`) on the first load collapses the overlay into the same error panel rather than stranding the user on "Loading claude.ai…".
- Distinguish **load-failure from user-cancel** at the `OnClosed` boundary: a window closed due to error reports `SignInResult.Failed`, a window closed by the user reports `Cancelled`. Today both report `Cancelled`, so `SignInCoordinator`'s `Failed → offer manual paste` branch never fires on a hung load.

**2.2 WebView2 post-install retry (item 4).**
`WebView2NotInstalledWindow` keeps its three actions but, after "Install Now," swaps to a "waiting — click Retry when it's installed" state with a **Retry** button that re-probes `CoreWebView2Environment.GetAvailableBrowserVersionString`. Extend the result contract: a new outcome means "runtime now present — proceed embedded." `SignInCoordinator.ShowRuntimeMissingThenMaybeManual` re-enters `SignInEmbeddedAsync` when the runtime becomes available, instead of mapping "Install Now" to `NotAdded`.

**2.3 Name the account on embedded add.**
When `PersistEmbedded` is about to allocate the 2nd+ account (`NextFreeLabel`), prompt for a name first via the existing `TextPromptWindow` (already used by Accounts ▸ Rename), defaulting to the next free label. First-run ("Personal") and re-auth (existing label) are unchanged — no prompt.

### Tier 3 — manual fallback polish (item 3)

**3.1 Copy + escape back to embedded.**
`ManualKeyWindow` ([ManualKeyWindow.xaml](../../../windows-dotnet/src/Sanduhr.App/Views/ManualKeyWindow.xaml)) keeps the "power-user path" framing, but:
- Softens the subtitle and adds a brief inline "Where do I find this?" affordance with the DevTools steps (collapsed by default).
- Adds a "Use the secure sign-in window instead" link that, when WebView2 is available, closes the manual modal with a result that tells `SignInCoordinator` to launch the embedded flow. Closes the loop so even the fallback can escape DevTools.

## Components and boundaries

| Unit | Responsibility | Depends on |
|------|----------------|------------|
| `SignInReason` (enum, Core) | The four recovery states | — |
| `SignInPromptCopy` (Core) | Pure reason → (headline, subtitle, label) | `SignInReason` |
| `WidgetViewModel` | Holds `Reason`; sets it from fetch catches; raises `SignInRequested` / `ReauthRequested` | Core helpers, `SignInCoordinator` via App |
| `SignInCoordinator` | Adds `ReauthenticateActiveAsync` + `PersistReauth`; post-install retry re-entry | `AccountStore`, `CredentialStore`, `SignInWindow`, modal |
| `SignInWindow` | Timeout, error panel, load-fail vs cancel distinction | WebView2, `SignInResult` |
| `WebView2NotInstalledWindow` | Retry-after-install state | runtime probe |
| `ManualKeyWindow` | Help affordance + bounce-to-embedded result | `SignInResult` |

## Data flow (recovery, the new path)

```
fetch 401 → SessionExpiredException
  → WidgetViewModel.Reason = Expired  (card reveals, copy = "Session expired / Sign in again")
  → user clicks primary → ReauthRequested
  → App → SignInCoordinator.ReauthenticateActiveAsync(owner)
  → SignInWindow (embedded) → capture sessionKey (+cf_clearance)
  → PersistReauth → CredentialStore.Save(active)   [overwrite in place; history kept]
  → SignInOutcome(Added) → vm.ReloadAfterSignInAsync() → Reason = None → cards repopulate
```

## Error handling

- Re-auth cancelled → `Reason` stays `Expired`/`Blocked` (card remains, user can retry). No silent revert to a broken widget.
- Re-auth capture fails → existing `Failed → offer manual paste` branch (now also reachable from a hung load via 2.1).
- Migration throw at startup → `MigrateFromV1` already swallows `JsonException`/`IOException` internally and falls back to `MigrateLegacy`; wrap the `Start()` call defensively so a migration fault never blocks first paint.

## Testing

- **Core unit tests (no browser):** `SignInPromptCopy.For` for each reason; `SignInReason` derivation; the in-place vs new-label persistence decision (extract the choice into a pure helper if it eases testing). Extend `CredentialStoreTests` to cover the in-place overwrite-active path.
- **Existing suite stays green:** 298/298 today. New tests add to that; no regression to the parsing/credential suites.
- **Manual verification (run the app):**
  1. First run (no creds) → FirstRun card → embedded sign-in → cards populate.
  2. Corrupt/expire the stored key → fetch → Expired card → "Sign in again" → in-place refresh, **same account label**, history intact, no "Account 2".
  3. Rename WebView2 runtime unavailable (or simulate) → install prompt → Retry → embedded proceeds.
  4. Slow/blocked first load → timeout → error panel → "Paste a key instead".
  5. Legacy single-slot keyring present, no `accounts:list` → launch → account promoted, **not** shown FirstRun.

## Risks and open verifications

- **UA currency (empirical, not code):** `ClaudeSignIn.BrowserUserAgent` is hardcoded `Chrome/131`, shared by `SignInWindow` and `CloudflareAwareHandler`. `cf_clearance` is UA-bound. Whether the live claude.ai/Cloudflare edge rejects 131 is external behavior — verify empirically by running the app and watching for `CloudflareBlockedException`, not by assumption. If stale, bumping the constant is a one-line follow-up, out of scope here.
- **Migration idempotency:** confirmed `MigrateLegacy()` no-ops on a populated registry; safe to call every launch.
- **Scope:** one cohesive surface (login/recovery). Single spec, shipped in Tier waves so Tier 1 (the real fix) can land first.

## Sequencing

Tier 1 → Tier 2 → Tier 3, each independently shippable. Tier 1 is the value; Tiers 2–3 harden and polish. One branch off the current `polish/session-expired-signin-copy` base, or a fresh `feat/login-recovery-hardening` branch — decide at plan time.
