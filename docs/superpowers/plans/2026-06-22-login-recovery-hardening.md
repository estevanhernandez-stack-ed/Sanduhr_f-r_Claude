# Login & Key-Entry Recovery Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: implementation is driven by **Vibe Cartographer build** (autonomous), per the maintainer's instruction, to preserve the checklist-and-breadcrumb-commit cadence the v3.0.0 rebuild used. Steps use checkbox (`- [ ]`) syntax for tracking. The superpowers subagent-driven / executing-plans sub-skills are NOT used here.

**Goal:** Make Sanduhr's login and session-recovery paths route through one trusted, state-aware affordance so a stuck user never reverts to the DevTools "copy the sessionKey cookie" ritual.

**Architecture:** Introduce a small `SignInReason` recovery state (pure `Sanduhr.Core`) that drives the widget's recovery-card copy and action, plus an in-place re-authentication path in `SignInCoordinator` that refreshes the active account instead of minting a new one. Pure decision logic lands in `Sanduhr.Core` (xUnit-testable, no WPF/browser); the WPF shell binds to it.

**Tech Stack:** .NET 10 (`net10.0` for Core, `net10.0-windows` WPF for App), CommunityToolkit.Mvvm (`[ObservableProperty]` / `[RelayCommand]`), WebView2 (`Microsoft.Web.WebView2`), xUnit. Build: `dotnet build windows-dotnet/Sanduhr.sln`. Test: `dotnet test windows-dotnet/Sanduhr.sln`.

## Global Constraints

- **Conventional commits, no emoji** in commit messages / code / working output. Breadcrumb cadence: one commit per task (Cartographer "complete step N" style).
- **Secret hygiene (load-bearing):** never log `sessionKey` / `cf_clearance` values; presence booleans only. Captured secrets go only to the persist delegate → Credential Manager, never onto a result object, never to disk in plaintext.
- **UA coupling:** `ClaudeSignIn.BrowserUserAgent` (Chrome/131) is shared verbatim by `SignInWindow` and `CloudflareAwareHandler`. Do not fork it. `cf_clearance` is UA-bound.
- **Test floor:** 298/298 tests pass today. New work only adds; no regression. Every task ends green (`dotnet test`).
- **Credential model:** all account state lives in Windows Credential Manager (service `com.626labs.sanduhr`) via `AccountStore` → `CredentialStore`. No JSON registry file.
- **GitNexus:** repo `CLAUDE.md` mandates `gitnexus_impact` before symbol edits and `gitnexus_detect_changes` before commit. MCP tools are NOT connected this session — impact analysis was done manually during design (caller lists embedded per task). After commits, the PostToolUse hook re-runs `npx gitnexus analyze`.
- **Spec of record:** `docs/superpowers/specs/2026-06-22-login-recovery-hardening-design.md`.

## Manual impact map (blast radius, verified by reading)

- `WidgetViewModel.ShowSignInPrompt` — consumed only by `MainWindow.xaml` empty-state card visibility (`BoolVis` converter). Set in `RebuildFetcher` (empty key). Safe to make derived.
- `WidgetViewModel.RefreshAsync` catches (`:486` Expired, `:490` Blocked) — internal; no external callers depend on the status string.
- `WidgetViewModel.SignInCommand` / `SignInRequested` — card primary button + tray "Add account" (tray calls `RunSignInAsync` directly, NOT via the command). Adding `PrimaryAuthCommand` does not disturb the tray path.
- `SignInCoordinator.SignInEmbeddedAsync` / `PersistEmbedded` — called from `App.RunSignInAsync(embedded:true)` (card + tray Add + Settings Add). Unchanged; new `ReauthenticateActiveAsync` is additive.
- `CredentialStore.Save(sessionKey, cfClearance)` — overwrites the active account's slots when an active account exists (`:67`). This is the in-place re-auth primitive; already covered by `CredentialStoreTests`.
- `MigrateFromV1()` — defined + unit-tested, called ONLY from tests. Wiring it into `Start()` is additive.

---

## TIER 1 — Close the real gap (items 1, 2, 5, 6, 7)

### Task 1.1: Wire legacy migration at startup (item 7)

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/WidgetViewModel.cs` (`Start()`, ~line 224)

**Interfaces:**
- Consumes: `CredentialStore.MigrateFromV1()` → `V1MigrationResult` (idempotent; internally swallows `JsonException`/`IOException`).
- Produces: nothing new; makes `RebuildFetcher`'s empty-key check truthful for upgraders.

- [ ] **Step 1: Add a defensive migration call before the first fetcher build.** In `Start()`, before `RebuildFetcher();`, insert:

```csharp
// One-time legacy promotion: pre-v2.2.0 single-slot keyring keys / v1 plaintext
// config are promoted to a "Personal" account so an upgrader isn't shown the
// first-run prompt. MigrateLegacy is idempotent (no-ops on a populated registry);
// MigrateFromV1 swallows its own Json/IO faults. Guard so a migration fault never
// blocks first paint.
try { _credentials.MigrateFromV1(); }
catch (Exception ex) { Log($"[Sanduhr.Migrate] skipped: {ex.Message}"); }
```

- [ ] **Step 2: Build.** Run: `dotnet build windows-dotnet/Sanduhr.sln` — Expected: success, no new warnings.
- [ ] **Step 3: Confirm existing migration tests still pass.** Run: `dotnet test windows-dotnet/Sanduhr.sln --filter "FullyQualifiedName~CredentialStoreTests"` — Expected: PASS (logic unchanged; this is the regression guard).
- [ ] **Step 4: Manual upgrade check (documented, run at Tier 1 close).** With a legacy bare `sessionKey` slot present and no `accounts:list`, launch the app → account is promoted to "Personal" and the widget does NOT show the first-run card.
- [ ] **Step 5: Commit.**

```bash
git add windows-dotnet/src/Sanduhr.App/ViewModels/WidgetViewModel.cs
git commit -m "fix(login): wire MigrateFromV1 at startup so legacy upgraders keep their account"
```

### Task 1.2: `SignInReason` + `SignInPromptCopy` (pure Core, TDD)

**Files:**
- Create: `windows-dotnet/src/Sanduhr.Core/SignInReason.cs`
- Create: `windows-dotnet/src/Sanduhr.Core/SignInPromptCopy.cs`
- Test: `windows-dotnet/tests/Sanduhr.Tests/SignInPromptCopyTests.cs`

**Interfaces:**
- Produces:
  - `enum SignInReason { None, FirstRun, Expired, Blocked }` (namespace `Sanduhr.Core`)
  - `record SignInPrompt(string Headline, string Subtitle, string PrimaryLabel)`
  - `static class SignInPromptCopy { static SignInPrompt For(SignInReason reason) }`

- [ ] **Step 1: Write the failing test.**

```csharp
using Sanduhr.Core;
using Xunit;

public class SignInPromptCopyTests
{
    [Fact]
    public void FirstRun_sells_the_no_devtools_flow()
    {
        var p = SignInPromptCopy.For(SignInReason.FirstRun);
        Assert.Equal("Track your Claude usage", p.Headline);
        Assert.Contains("no DevTools", p.Subtitle);
        Assert.Equal("Sign in to Claude", p.PrimaryLabel);
    }

    [Fact]
    public void Expired_points_at_re_auth_not_key_paste()
    {
        var p = SignInPromptCopy.For(SignInReason.Expired);
        Assert.Equal("Session expired", p.Headline);
        Assert.Equal("Sign in again", p.PrimaryLabel);
        Assert.DoesNotContain("DevTools", p.Subtitle, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Blocked_explains_the_cloudflare_refresh()
    {
        var p = SignInPromptCopy.For(SignInReason.Blocked);
        Assert.Equal("Connection challenged", p.Headline);
        Assert.Equal("Sign in again", p.PrimaryLabel);
    }

    [Fact]
    public void None_throws_no_card_copy_for_a_non_prompt_state()
        => Assert.Throws<System.ArgumentOutOfRangeException>(() => SignInPromptCopy.For(SignInReason.None));
}
```

- [ ] **Step 2: Run to verify it fails.** Run: `dotnet test windows-dotnet/Sanduhr.sln --filter "FullyQualifiedName~SignInPromptCopyTests"` — Expected: FAIL (types not defined).
- [ ] **Step 3: Implement `SignInReason.cs`.**

```csharp
namespace Sanduhr.Core;

/// <summary>Why the widget is showing a sign-in recovery card. Drives the card's
/// copy and which action its primary button fires. Runtime-missing is deliberately
/// NOT here — that is a transient SignInCoordinator-flow state, never a persistent
/// widget state.</summary>
public enum SignInReason
{
    None,
    FirstRun,
    Expired,
    Blocked,
}
```

- [ ] **Step 4: Implement `SignInPromptCopy.cs`.**

```csharp
namespace Sanduhr.Core;

/// <summary>The headline/subtitle/button text the recovery card shows for a given
/// <see cref="SignInReason"/>. Pure so it unit-tests without WPF.</summary>
public sealed record SignInPrompt(string Headline, string Subtitle, string PrimaryLabel);

public static class SignInPromptCopy
{
    public static SignInPrompt For(SignInReason reason) => reason switch
    {
        SignInReason.FirstRun => new SignInPrompt(
            "Track your Claude usage",
            "Sign in once in a secure window. Sanduhr reads your usage automatically — no DevTools, no copy-paste.",
            "Sign in to Claude"),
        SignInReason.Expired => new SignInPrompt(
            "Session expired",
            "Your sign-in timed out. Sign in again — it takes a few seconds, no DevTools.",
            "Sign in again"),
        SignInReason.Blocked => new SignInPrompt(
            "Connection challenged",
            "Cloudflare needs a fresh check. Sign in again to refresh it automatically.",
            "Sign in again"),
        _ => throw new System.ArgumentOutOfRangeException(nameof(reason), reason, "No card copy for a non-prompt reason."),
    };
}
```

- [ ] **Step 5: Run to verify pass.** Run: `dotnet test windows-dotnet/Sanduhr.sln --filter "FullyQualifiedName~SignInPromptCopyTests"` — Expected: PASS (4/4).
- [ ] **Step 6: Commit.**

```bash
git add windows-dotnet/src/Sanduhr.Core/SignInReason.cs windows-dotnet/src/Sanduhr.Core/SignInPromptCopy.cs windows-dotnet/tests/Sanduhr.Tests/SignInPromptCopyTests.cs
git commit -m "feat(login): SignInReason + pure SignInPromptCopy for state-aware recovery copy"
```

### Task 1.3: In-place re-auth in `SignInCoordinator` (item 5)

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.App/Services/SignInCoordinator.cs`
- Test: `windows-dotnet/tests/Sanduhr.Tests/CredentialStoreTests.cs` (add overwrite-active coverage)

**Interfaces:**
- Consumes: `CredentialStore.Save(sessionKey, cfClearance)` (overwrites active), `AccountStore.GetActive()`, existing `SignInWindow(dir, persist)` + `WebView2UserDataDirectory`.
- Produces: `Task<SignInOutcome> ReauthenticateActiveAsync(Window? owner)`; private `string PersistReauth(CapturedCookies cookies)`.

- [ ] **Step 1: Write the failing test for in-place overwrite.** In `CredentialStoreTests.cs`, add (adapt to the existing fixture `f`/`Credentials` pattern in that file):

```csharp
[Fact]
public void Save_overwrites_the_active_accounts_key_without_adding_a_slot()
{
    var f = NewFixture();                       // existing helper in this test file
    f.Credentials.Save(sessionKey: "old-key");  // auto-creates "Personal", active
    var before = f.Accounts.ListAccounts().Count;

    f.Credentials.Save(sessionKey: "new-key");  // re-auth: overwrite active in place

    Assert.Equal(before, f.Accounts.ListAccounts().Count);   // no new slot
    Assert.Equal("new-key", f.Credentials.Load().SessionKey); // refreshed in place
    Assert.Equal("Personal", f.Accounts.GetActive());         // same active label
}
```

- [ ] **Step 2: Run to verify it fails or passes.** Run: `dotnet test windows-dotnet/Sanduhr.sln --filter "FullyQualifiedName~CredentialStoreTests.Save_overwrites"` — Expected: PASS if `Save` already overwrites (it does, per `:67`); this test locks the behavior the re-auth path depends on. If the fixture helper name differs, fix the test to the file's actual fixture, not the production code.
- [ ] **Step 3: Add `ReauthenticateActiveAsync` + `PersistReauth` to `SignInCoordinator`.** Mirror `SignInEmbeddedAsync` but swap the persist delegate. Insert after `SignInEmbeddedAsync`:

```csharp
/// <summary>Re-authenticate the ACTIVE account in place — same embedded WebView2
/// flow as <see cref="SignInEmbeddedAsync"/>, but the captured cookies overwrite
/// the existing active slot instead of allocating a new label. Preserves the
/// account's history file and avoids registry litter. Used by the widget's
/// Expired/Blocked recovery card.</summary>
public async Task<SignInOutcome> ReauthenticateActiveAsync(Window? owner)
{
    if (!IsRuntimeAvailable())
        return ShowRuntimeMissingThenMaybeManual(owner);

    string dir;
    try
    {
        dir = _userData.AllocateNew();
        _userData.SweepStale(exclude: dir);
    }
    catch (Exception ex)
    {
        ShowMessage(owner, $"Couldn't prepare the sign-in browser profile: {ex.Message}", MessageBoxButton.OK);
        return SignInManual(owner);
    }

    var window = new SignInWindow(dir, PersistReauth);
    SetOwner(window, owner);
    var result = await window.RunAsync().ConfigureAwait(true);

    switch (result)
    {
        case SignInResult.Success s:
            _userData.SweepStale();
            return new SignInOutcome(true, s.Label);
        case SignInResult.RuntimeMissing:
            return ShowRuntimeMissingThenMaybeManual(owner);
        case SignInResult.Failed f:
            var retry = ShowMessage(owner, $"{f.Message}\n\nPaste a sessionKey by hand instead?", MessageBoxButton.YesNo);
            return retry == MessageBoxResult.Yes ? SignInManual(owner) : SignInOutcome.NotAdded;
        default:
            return SignInOutcome.NotAdded;
    }
}

/// <summary>Re-auth save: overwrite the ACTIVE account's slots in place. If somehow
/// no active account exists, fall back to first-run create-"Personal" semantics.</summary>
private string PersistReauth(CapturedCookies cookies)
{
    var active = _accounts.GetActive();
    if (active is null)
    {
        _credentials.Save(cookies.SessionKey, cookies.CfClearance);
        return _accounts.GetActive() ?? "Personal";
    }
    _credentials.Save(cookies.SessionKey, cookies.CfClearance); // overwrites active in place
    return active;
}
```

- [ ] **Step 4: Build + full test pass.** Run: `dotnet build windows-dotnet/Sanduhr.sln` then `dotnet test windows-dotnet/Sanduhr.sln` — Expected: build clean, all green (≥299).
- [ ] **Step 5: Commit.**

```bash
git add windows-dotnet/src/Sanduhr.App/Services/SignInCoordinator.cs windows-dotnet/tests/Sanduhr.Tests/CredentialStoreTests.cs
git commit -m "feat(login): in-place re-auth path that refreshes the active account, no duplicate slot"
```

### Task 1.4: `WidgetViewModel` recovery state + primary action routing (items 1, 6)

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/WidgetViewModel.cs`

**Interfaces:**
- Consumes: `SignInReason`, `SignInPromptCopy` (Task 1.2); existing `ShowSignInPrompt`, `SignInRequested`, `RefreshAsync` catches.
- Produces: `SignInReason Reason` (observable); derived `ShowSignInPrompt`, `PromptHeadline`, `PromptSubtitle`, `PromptPrimaryLabel`; `event Func<Task>? ReauthRequested`; `IRelayCommand PrimaryAuthCommand`.

- [ ] **Step 1: Replace the `ShowSignInPrompt` backing with a `Reason`-derived model.** Find the `[ObservableProperty] private bool _showSignInPrompt;` declaration and replace it with:

```csharp
[ObservableProperty] private SignInReason _reason = SignInReason.None;

/// <summary>Visibility of the recovery card. Derived: any non-None reason shows it.</summary>
public bool ShowSignInPrompt => Reason != SignInReason.None;
public string PromptHeadline => Reason == SignInReason.None ? "" : SignInPromptCopy.For(Reason).Headline;
public string PromptSubtitle => Reason == SignInReason.None ? "" : SignInPromptCopy.For(Reason).Subtitle;
public string PromptPrimaryLabel => Reason == SignInReason.None ? "" : SignInPromptCopy.For(Reason).PrimaryLabel;

partial void OnReasonChanged(SignInReason value)
{
    OnPropertyChanged(nameof(ShowSignInPrompt));
    OnPropertyChanged(nameof(PromptHeadline));
    OnPropertyChanged(nameof(PromptSubtitle));
    OnPropertyChanged(nameof(PromptPrimaryLabel));
}

/// <summary>Raised by the recovery card when the active account needs re-auth
/// (Expired/Blocked). App routes this to SignInCoordinator.ReauthenticateActiveAsync.</summary>
public event Func<Task>? ReauthRequested;
```

- [ ] **Step 2: Replace every `ShowSignInPrompt = true/false` assignment with a `Reason` assignment.**
  - In `RebuildFetcher` (`~:333`): `ShowSignInPrompt = true;` → `Reason = SignInReason.FirstRun;`
  - In `RebuildFetcher` (`~:338`): `ShowSignInPrompt = false;` → `Reason = SignInReason.None;`
  - Sign-out-to-empty path (`~:444`, if it sets the prompt): set `Reason = SignInReason.FirstRun;`
  - After a successful fetch in `RefreshAsync` (the `StatusText = "";` success block, `~:483`): add `Reason = SignInReason.None;`

- [ ] **Step 3: Make the recovery catches set the reason instead of a dead string.** In `RefreshAsync`:

```csharp
catch (SessionExpiredException)
{
    Reason = SignInReason.Expired;
    StatusText = "";
    TrayPercentChanged?.Invoke(-1);
}
catch (CloudflareBlockedException)
{
    Reason = SignInReason.Blocked;
    StatusText = "";
    TrayPercentChanged?.Invoke(-1);
}
```

(Leave `NetworkException` / `HttpRequestException` / generic `Exception` catches calling `Fail(...)` — those are transient, not auth.)

- [ ] **Step 4: Add the reason-routing primary command.** Add near `SignInCommand`:

```csharp
/// <summary>The recovery card's primary button. FirstRun → add-account sign-in;
/// Expired/Blocked → in-place re-auth of the active account.</summary>
[RelayCommand]
private async Task PrimaryAuth()
{
    if (Reason is SignInReason.Expired or SignInReason.Blocked)
    {
        if (ReauthRequested is not null) await ReauthRequested.Invoke();
    }
    else
    {
        if (SignInRequested is not null) await SignInRequested.Invoke();
    }
}
```

- [ ] **Step 5: Build.** Run: `dotnet build windows-dotnet/Sanduhr.sln` — Expected: clean (watch for any other `ShowSignInPrompt =` setters the grep missed; ShowSignInPrompt is now read-only — a leftover setter is a compile error that surfaces them).
- [ ] **Step 6: Run full tests.** Run: `dotnet test windows-dotnet/Sanduhr.sln` — Expected: all green.
- [ ] **Step 7: Commit.**

```bash
git add windows-dotnet/src/Sanduhr.App/ViewModels/WidgetViewModel.cs
git commit -m "feat(login): reason-driven recovery state; expired/blocked surface the actionable card"
```

### Task 1.5: Wire `ReauthRequested` in App + bind the card to reason copy (item 1)

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.App/App.xaml.cs` (event wiring + `RunReauthAsync`)
- Modify: `windows-dotnet/src/Sanduhr.App/MainWindow.xaml` (empty-state card bindings, ~lines 338-362)

**Interfaces:**
- Consumes: `WidgetViewModel.ReauthRequested`, `WidgetViewModel.PrimaryAuthCommand`, `PromptHeadline/Subtitle/PrimaryLabel`; `SignInCoordinator.ReauthenticateActiveAsync`.

- [ ] **Step 1: Wire the re-auth event in `App.OnStartup`.** Next to `_vm.SignInRequested += () => RunSignInAsync(embedded: true);` add:

```csharp
_vm.ReauthRequested += RunReauthAsync;
```

- [ ] **Step 2: Add `RunReauthAsync` to `App`.** Next to `RunSignInAsync`:

```csharp
/// <summary>Re-authenticate the active account in place (Expired/Blocked recovery),
/// then rebuild + refetch. Distinct from RunSignInAsync, which adds a new account.</summary>
private async Task RunReauthAsync()
{
    var coordinator = new SignInCoordinator();
    var outcome = await coordinator.ReauthenticateActiveAsync(_window);
    if (outcome.Added && _vm is not null)
        await _vm.ReloadAfterSignInAsync();
}
```

- [ ] **Step 3: Bind the card copy + primary command in `MainWindow.xaml`.** In the empty-state `Border` (visibility already `ShowSignInPrompt`):
  - Headline `TextBlock Text="Track your Claude usage"` → `Text="{Binding PromptHeadline}"`
  - Subtitle `TextBlock Text="Sign in once..."` → `Text="{Binding PromptSubtitle}"`
  - Primary `Button Command="{Binding SignInCommand}"` → `Command="{Binding PrimaryAuthCommand}"`, and its content text `Sign in to Claude` → bind via `<ContentPresenter>`/`Content="{Binding PromptPrimaryLabel}"` (replace the literal inner text with the bound label).

- [ ] **Step 4: Build + run the app.** Run: `dotnet build windows-dotnet/Sanduhr.sln`; launch. Expected: first-run shows "Track your Claude usage / Sign in to Claude".
- [ ] **Step 5: Manual recovery verification.** Corrupt the stored key (sign in, then overwrite the Credential Manager `sessionKey:Personal` slot with garbage, or wait for expiry) → trigger refresh → card flips to "Session expired / Sign in again" → click → embedded window → after sign-in the SAME "Personal" account refreshes, history intact, no "Account 2".
- [ ] **Step 6: Commit.**

```bash
git add windows-dotnet/src/Sanduhr.App/App.xaml.cs windows-dotnet/src/Sanduhr.App/MainWindow.xaml
git commit -m "feat(login): one-click in-place recovery from the expired/blocked card"
```

### Task 1.6: Docs — README first-run + CHANGELOG (item 2)

**Files:**
- Modify: `README.md` (root, "First-run setup", ~lines 130-141)
- Modify: `CHANGELOG.md`

- [ ] **Step 1: Rewrite the root README "First-run setup".** Replace the DevTools steps with the embedded flow as primary; manual paste named as the labeled power-user fallback; remove the stale "Settings → Credentials" reference (it is "Settings → Accounts"). Source the corrected wording from `windows-dotnet/README.md`.
- [ ] **Step 2: Add the v3.0.0 CHANGELOG entry** documenting the .NET rebuild's embedded WebView2 sign-in and (now) the in-place session-recovery, above the current v2.3.0 top entry.
- [ ] **Step 3: Commit.**

```bash
git add README.md CHANGELOG.md
git commit -m "docs(login): README first-run + CHANGELOG describe the embedded sign-in, not DevTools"
```

### Tier 1 gate
- [ ] `dotnet test windows-dotnet/Sanduhr.sln` fully green.
- [ ] Manual checks 1.1/1.5 pass.
- [ ] Optional: open a PR for Tier 1, or continue to Tier 2 on the same branch.

---

## TIER 2 — Harden the embedded window (item 4 + window robustness)

### Task 2.1: Timeout + error state + load-fail-vs-cancel in `SignInWindow`

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.App/Views/SignInWindow.xaml` (add an error panel)
- Modify: `windows-dotnet/src/Sanduhr.App/Views/SignInWindow.xaml.cs`

**Interfaces:**
- Produces: distinct `SignInResult.Failed` on load failure/timeout (vs `Cancelled` on user-close), so `SignInCoordinator`'s `Failed → offer manual paste` branch fires.

- [ ] **Step 1: Add a load-outcome flag.** Add `private bool _loadFailed;` and a `DispatcherTimer? _loadTimeout;`.
- [ ] **Step 2: Start a timeout in `OnLoaded`** (after `Navigate`): a 30s `DispatcherTimer` that, if `!_firstNavComplete && !_captured`, calls `ShowLoadError("Couldn't reach claude.ai. Check your connection.")`.
- [ ] **Step 3: Handle first-load failure.** In `OnNavigationCompleted`, if `!_firstNavComplete && !e.IsSuccess`, call `ShowLoadError("claude.ai didn't load.")` instead of leaving the overlay.
- [ ] **Step 4: Implement `ShowLoadError`** — collapse `LoadingOverlay`, show an error panel (message + "Try again" → re-`Navigate(LoginUrl)` + restart timeout; "Paste a key instead" → `CompleteAndClose(new SignInResult.Failed("..."))`), set `_loadFailed = true` when the user dismisses to manual.
- [ ] **Step 5: Distinguish cancel from failure in `OnClosed`.** `_tcs.TrySetResult(_loadFailed ? new SignInResult.Failed("Sign-in window closed after a load error.") : new SignInResult.Cancelled());`
- [ ] **Step 6: Stop `_loadTimeout` in `StopPoll`/`CompleteAndClose`** so it never fires post-capture.
- [ ] **Step 7: Build + manual check** (simulate offline: disconnect, open sign-in → timeout → error panel → "Paste a key instead" reaches the manual modal).
- [ ] **Step 8: Commit.**

```bash
git add windows-dotnet/src/Sanduhr.App/Views/SignInWindow.xaml windows-dotnet/src/Sanduhr.App/Views/SignInWindow.xaml.cs
git commit -m "feat(login): sign-in window timeout + error escape hatch; load-fail distinct from cancel"
```

### Task 2.2: WebView2 post-install retry (item 4)

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.App/Modals/WebView2NotInstalledWindow.xaml(.cs)`
- Modify: `windows-dotnet/src/Sanduhr.App/Services/SignInCoordinator.cs` (`ShowRuntimeMissingThenMaybeManual`)

- [ ] **Step 1: Add a Retry affordance.** After `OnInstallClick` opens the installer, instead of closing with `DialogResult=true`, swap the modal to a "waiting — click Retry once installed" state with a **Retry** button.
- [ ] **Step 2: Retry re-probes the runtime.** `OnRetryClick` re-checks `CoreWebView2Environment.GetAvailableBrowserVersionString`; if present → `DialogResult=true` (meaning "runtime now available, proceed embedded"); else show "Still not detected — give it a moment."
- [ ] **Step 3: Re-enter embedded in the coordinator.** In `ShowRuntimeMissingThenMaybeManual`, `choice == true` → re-call `IsRuntimeAvailable()`; if true → `await SignInEmbeddedAsync(owner)` (return its outcome); else `SignInOutcome.NotAdded`. `choice == false` → `SignInManual`. (Because this becomes async, thread `ShowRuntimeMissingThenMaybeManual` to return `Task<SignInOutcome>` and `await` it at both call sites in `SignInEmbeddedAsync` / `ReauthenticateActiveAsync`.)
- [ ] **Step 4: Build + full tests green.** Run: `dotnet test windows-dotnet/Sanduhr.sln`.
- [ ] **Step 5: Commit.**

```bash
git add windows-dotnet/src/Sanduhr.App/Modals/WebView2NotInstalledWindow.xaml windows-dotnet/src/Sanduhr.App/Modals/WebView2NotInstalledWindow.xaml.cs windows-dotnet/src/Sanduhr.App/Services/SignInCoordinator.cs
git commit -m "feat(login): WebView2 post-install retry re-enters embedded instead of dead-ending"
```

### Task 2.3: Name the account on embedded add

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.App/Services/SignInCoordinator.cs` (`PersistEmbedded`)

- [ ] **Step 1: Prompt for a name on the 2nd+ embedded add.** In `PersistEmbedded`, when `GetActive()` is not null (a subsequent account), open `TextPromptWindow("Name this account", "Account name", NextFreeLabel())` on the UI thread; use the returned name (validated like `ManualKeyWindow`'s regex `^[A-Za-z0-9 _-]{1,32}$`, falling back to `NextFreeLabel()` on empty/cancel) instead of silently assigning `NextFreeLabel()`. First-run ("Personal") and re-auth (existing label) paths unchanged.
- [ ] **Step 2: Build + manual check** (add a second account via tray → prompted for a name → appears under that name).
- [ ] **Step 3: Commit.**

```bash
git add windows-dotnet/src/Sanduhr.App/Services/SignInCoordinator.cs
git commit -m "feat(login): name an added account during embedded sign-in instead of 'Account N'"
```

---

## TIER 3 — Manual fallback polish (item 3)

### Task 3.1: `ManualKeyWindow` help affordance + bounce to embedded

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.App/Views/ManualKeyWindow.xaml(.cs)`
- Modify: `windows-dotnet/src/Sanduhr.App/Services/SignInCoordinator.cs` (handle a "bounce to embedded" result)
- Modify: `windows-dotnet/src/Sanduhr.App/Views/SignInResult.cs` (add a `UseEmbedded` outcome)

**Interfaces:**
- Produces: `SignInResult.UseEmbedded` (a new discriminated case) — signals the coordinator to launch the embedded flow from the manual modal.

- [ ] **Step 1: Add the result case.** In `SignInResult.cs`: `public sealed record UseEmbedded : SignInResult;`
- [ ] **Step 2: Soften manual copy + add help.** Reword the subtitle away from a bare "DevTools → Application → Cookies" instruction into a collapsed "Where do I find this?" expander that contains the steps.
- [ ] **Step 3: Add "Use the secure sign-in window instead".** A link/button that sets `Result = new SignInResult.UseEmbedded()` and closes (only shown when WebView2 is available — pass an `embeddedAvailable` bool into the window ctor).
- [ ] **Step 4: Handle it in the coordinator.** `SignInManual` returns the window's `Result`; when it is `UseEmbedded`, return `await SignInEmbeddedAsync(owner)` instead. (Requires `SignInManual` to become async `Task<SignInOutcome>`; update its call sites.)
- [ ] **Step 5: Build + full tests green + manual check** (open manual paste → "use secure sign-in instead" → embedded opens).
- [ ] **Step 6: Commit.**

```bash
git add windows-dotnet/src/Sanduhr.App/Views/ManualKeyWindow.xaml windows-dotnet/src/Sanduhr.App/Views/ManualKeyWindow.xaml.cs windows-dotnet/src/Sanduhr.App/Services/SignInCoordinator.cs windows-dotnet/src/Sanduhr.App/Views/SignInResult.cs
git commit -m "feat(login): manual paste can bounce to the secure sign-in window; help affordance"
```

---

## Final verification

- [ ] `dotnet build windows-dotnet/Sanduhr.sln` clean.
- [ ] `dotnet test windows-dotnet/Sanduhr.sln` fully green (≥ original 298 + new tests).
- [ ] All five manual scenarios from the spec's Testing section pass.
- [ ] `npx gitnexus analyze` (PostToolUse hook) leaves the index fresh.
- [ ] Open PR against `main`; link the spec; summarize the three tiers.

## Self-review (against the spec)

- **Spec coverage:** item 1 → 1.4/1.5; item 2 → 1.6; item 3 → 3.1; item 4 → 2.2; item 5 → 1.3; item 6 → 1.4 (Blocked reason); item 7 → 1.1. Timeout/escape-hatch (spec 2.1) → 2.1; account-naming (spec 2.3) → 2.3. Proactive probe → intentionally absent (cut). All covered.
- **Type consistency:** `SignInReason` / `SignInPrompt` / `SignInPromptCopy.For` used identically in 1.2/1.4. `ReauthRequested` (event) ↔ `RunReauthAsync` (1.5). `PrimaryAuthCommand` (1.4) ↔ card binding (1.5). `ReauthenticateActiveAsync`/`PersistReauth` (1.3) ↔ App wiring (1.5). `SignInResult.UseEmbedded` (3.1) defined before use. Consistent.
- **Async ripple noted:** Tasks 2.2 and 3.1 turn `ShowRuntimeMissingThenMaybeManual` / `SignInManual` async — call sites in `SignInEmbeddedAsync` and `ReauthenticateActiveAsync` must `await`. Flagged in those tasks.
