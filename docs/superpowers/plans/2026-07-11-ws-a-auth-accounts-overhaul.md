# WS-A Auth & Accounts Overhaul Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Origin-aware reauth (manual-key accounts get the paste modal, not the browser), in-place manual reauth everywhere, per-account "Update sign-in…" in Settings, and account delete/rename that cleans up every store — per the approved spec `docs/superpowers/specs/2026-07-11-auth-accounts-overhaul-design.md`.

**Architecture:** A new `origin:{label}` Windows Credential Manager slot (additive, missing = legacy = embedded) feeds a pure Core routing function (`ReauthRouting`) consumed by the recovery card and Settings. `SignInCoordinator` gains the missing in-place manual persist. Delete/rename completeness lands in `UsageHistory` + `WidgetViewModel`.

**Tech Stack:** .NET 10 WPF (`windows-dotnet/`), CommunityToolkit.Mvvm (`[RelayCommand]`/`[ObservableProperty]`), xUnit, Windows Credential Manager via `ICredentialManager`.

## Global Constraints

- All code lives under `windows-dotnet/`. Core logic goes in `src/Sanduhr.Core` (testable, no WPF); UI in `src/Sanduhr.App`.
- Test command: `dotnet test windows-dotnet/tests/Sanduhr.Tests/Sanduhr.Tests.csproj` (no .sln exists — target csproj files directly). App build check: `dotnet build windows-dotnet/src/Sanduhr.App/Sanduhr.App.csproj`.
- **Theming (spec hard requirement):** zero hardcoded brushes/colors/fonts in new or modified XAML — only `{DynamicResource Sanduhr.Brush.*}` keys (see `src/Sanduhr.App/Theming/ThemePalette.cs:111-154` for the full key list). Dialogs use `ThemedDialog.Show(owner, title, text, buttons, kind)` — never `MessageBox`. No new theme tokens are introduced by this plan (so `docs/themes/AGENT_PROMPT.md` needs no update). Error/destructive text uses `Sanduhr.Brush.PaceMarker` (the existing convention — see `SettingsWindow.xaml:437`).
- **GitNexus (repo CLAUDE.md):** before modifying each symbol, run `gitnexus_impact({target, direction: "upstream"})` if the GitNexus MCP tools are available in your session. If unavailable, each task's Interfaces block lists the known callers (full recon 2026-07-11) — verify with Grep that no new callers appeared before editing. Run `gitnexus_detect_changes()` before commits when available; a PostToolUse hook re-runs `npx gitnexus analyze` after commits automatically.
- Conventional commits. Work on branch `feat/ws-a-auth-accounts` (created in Task 1). Commit at the end of every task.
- Copy rules: no emoji, periods at the end of microcopy, the delete confirm must state exactly what is destroyed.

---

### Task 1: `AccountOrigin` enum + origin slot in `AccountStore`

**Files:**
- Create: `windows-dotnet/src/Sanduhr.Core/AccountOrigin.cs`
- Modify: `windows-dotnet/src/Sanduhr.Core/AccountStore.cs` (RemoveAccount at ~line 96, RenameAccount at ~114, new methods after SaveCredentials ~147)
- Test: `windows-dotnet/tests/Sanduhr.Tests/AccountStoreTests.cs`

**Interfaces:**
- Consumes: existing `ICredentialManager` (`GetPassword`/`SetPassword`/`DeletePassword`), `FakeCredentialManager` (test fake, already exists).
- Produces: `enum AccountOrigin { Embedded, Manual }`; `AccountOrigin AccountStore.GetOrigin(string label)` (missing slot ⇒ `Embedded`); `void AccountStore.SetOrigin(string label, AccountOrigin origin)` (throws `ArgumentException` for unknown label). Tasks 3, 4, 6, 7 rely on these exact names.
- Known callers of modified symbols: `RemoveAccount` ← `WidgetViewModel.SignOutAccountAsync`; `RenameAccount` ← `WidgetViewModel.RenameAccount`. Both signatures unchanged — no caller updates needed in this task.

- [ ] **Step 1: Create the branch**

```bash
git checkout -b feat/ws-a-auth-accounts
```

- [ ] **Step 2: Write the failing tests**

Append inside the `AccountStoreTests` class in `windows-dotnet/tests/Sanduhr.Tests/AccountStoreTests.cs`:

```csharp
    // -- origin flag (WS-A: origin-aware reauth) ------------------------------

    [Fact]
    public void Origin_defaults_to_embedded_when_never_set()
    {
        var a = New();
        a.AddAccount("Personal", "placeholder-1");
        Assert.Equal(AccountOrigin.Embedded, a.GetOrigin("Personal"));
    }

    [Fact]
    public void Origin_round_trips()
    {
        var a = New();
        a.AddAccount("Personal", "placeholder-1");
        a.SetOrigin("Personal", AccountOrigin.Manual);
        Assert.Equal(AccountOrigin.Manual, a.GetOrigin("Personal"));
        a.SetOrigin("Personal", AccountOrigin.Embedded);
        Assert.Equal(AccountOrigin.Embedded, a.GetOrigin("Personal"));
    }

    [Fact]
    public void Set_origin_unknown_account_raises()
    {
        var a = New();
        Assert.Throws<ArgumentException>(() => a.SetOrigin("Nonexistent", AccountOrigin.Manual));
    }

    [Fact]
    public void Remove_account_clears_origin()
    {
        var a = New();
        a.AddAccount("Personal", "placeholder-1");
        a.SetOrigin("Personal", AccountOrigin.Manual);
        a.RemoveAccount("Personal");
        // Re-adding the same label must not inherit the old origin.
        a.AddAccount("Personal", "placeholder-2");
        Assert.Equal(AccountOrigin.Embedded, a.GetOrigin("Personal"));
    }

    [Fact]
    public void Rename_carries_origin()
    {
        var a = New();
        a.AddAccount("Personal", "placeholder-1");
        a.SetOrigin("Personal", AccountOrigin.Manual);
        a.RenameAccount("Personal", "Home");
        Assert.Equal(AccountOrigin.Manual, a.GetOrigin("Home"));
    }
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test windows-dotnet/tests/Sanduhr.Tests/Sanduhr.Tests.csproj --filter "FullyQualifiedName~AccountStoreTests"`
Expected: compile error — `AccountOrigin` / `GetOrigin` not defined.

- [ ] **Step 4: Create the enum**

Create `windows-dotnet/src/Sanduhr.Core/AccountOrigin.cs`:

```csharp
namespace Sanduhr.Core;

/// <summary>
/// How an account's credentials were last captured. Drives reauth routing: a
/// manually-pasted key can't be refreshed by the embedded browser login (the
/// Google-SSO population in particular), so the recovery card leads with the
/// paste modal for Manual accounts. Persisted per label in the credential slot
/// <c>origin:{label}</c>; a missing slot reads as <see cref="Embedded"/> so
/// pre-WS-A accounts keep today's behavior.
/// </summary>
public enum AccountOrigin
{
    /// <summary>Captured by the embedded WebView2 claude.ai login.</summary>
    Embedded,

    /// <summary>Pasted by hand into the manual key modal.</summary>
    Manual,
}
```

- [ ] **Step 5: Add the slot to `AccountStore`**

In `windows-dotnet/src/Sanduhr.Core/AccountStore.cs`, add after the `SaveCredentials` method (~line 147):

```csharp
    /// <summary>How this account's credentials were last captured. A missing
    /// slot (any pre-WS-A account) reads as <see cref="AccountOrigin.Embedded"/>.</summary>
    public AccountOrigin GetOrigin(string label)
        => _cred.GetPassword($"origin:{label}") == "manual"
            ? AccountOrigin.Manual
            : AccountOrigin.Embedded;

    /// <summary>Record how this account's credentials were last captured — called
    /// by every successful persist (add and in-place reauth, both flows).</summary>
    public void SetOrigin(string label, AccountOrigin origin)
    {
        if (!ReadList().Contains(label))
            throw new ArgumentException($"Account '{label}' not in registry", nameof(label));
        _cred.SetPassword($"origin:{label}", origin == AccountOrigin.Manual ? "manual" : "embedded");
    }
```

In `RemoveAccount`, after `DeleteSafely($"cf_clearance:{label}");` (line ~102) add:

```csharp
        DeleteSafely($"origin:{label}");
```

In `RenameAccount`, after the cf_clearance copy (line ~126) add the origin copy, and after `DeleteSafely($"cf_clearance:{oldLabel}");` (line ~128) add the origin delete:

```csharp
        var origin = _cred.GetPassword($"origin:{oldLabel}");
        if (origin is not null)
            _cred.SetPassword($"origin:{newLabel}", origin);
```

```csharp
        DeleteSafely($"origin:{oldLabel}");
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test windows-dotnet/tests/Sanduhr.Tests/Sanduhr.Tests.csproj --filter "FullyQualifiedName~AccountStoreTests"`
Expected: PASS (all, including the 17 pre-existing tests).

- [ ] **Step 7: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/AccountOrigin.cs windows-dotnet/src/Sanduhr.Core/AccountStore.cs windows-dotnet/tests/Sanduhr.Tests/AccountStoreTests.cs
git commit -m "feat(core): per-account origin flag (embedded vs manual) in the credential store"
```

---

### Task 2: `UsageHistory.Delete` + `UsageHistory.Rename`

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.Core/UsageHistory.cs` (add after `ClearAll`, ~line 194)
- Test: `windows-dotnet/tests/Sanduhr.Tests/UsageHistoryTests.cs`

**Interfaces:**
- Consumes: `Paths.HistoryFileFor(string)`, existing `Fixture` test helper in UsageHistoryTests (`new Fixture("Personal")` → temp-dir `Paths` + fake-backed `AccountStore` + `UsageHistory`).
- Produces: `void UsageHistory.Delete(string? account = null)` (unlinks `history.{label}.json`, best-effort, no-op when missing); `void UsageHistory.Rename(string oldLabel, string newLabel)` (moves the file, overwrites a stale target, no-op when no source). Task 8 relies on these exact names.
- Known callers of `ClearAll`: `WidgetViewModel.SignOutAccountAsync` (replaced in Task 8), `HistoryTabViewModel` ("Clear history" button — keeps `ClearAll`, do NOT change it).

- [ ] **Step 1: Write the failing tests**

Append inside the `UsageHistoryTests` class:

```csharp
    // -- Delete / Rename (WS-A: complete account removal + rename) ------------

    [Fact]
    public void Delete_unlinks_the_history_file()
    {
        using var f = new Fixture("Personal");
        f.History.Append("five_hour", 42);
        Assert.True(File.Exists(f.Paths.HistoryFileFor("Personal")));
        f.History.Delete("Personal");
        Assert.False(File.Exists(f.Paths.HistoryFileFor("Personal")));
    }

    [Fact]
    public void Delete_missing_file_is_noop()
    {
        using var f = new Fixture("Personal");
        f.History.Delete("Personal"); // no file yet — must not throw
        Assert.False(File.Exists(f.Paths.HistoryFileFor("Personal")));
    }

    [Fact]
    public void Delete_leaves_other_accounts_untouched()
    {
        using var f = new Fixture("Personal", "Work");
        f.History.AppendForAccount("five_hour", 10, "Personal");
        f.History.AppendForAccount("five_hour", 20, "Work");
        f.History.Delete("Personal");
        Assert.Equal(new[] { 20 }, f.History.Load("five_hour", "Work"));
    }

    [Fact]
    public void Rename_moves_the_history_file()
    {
        using var f = new Fixture("Personal");
        f.History.Append("five_hour", 42);
        f.History.Rename("Personal", "Home");
        Assert.False(File.Exists(f.Paths.HistoryFileFor("Personal")));
        Assert.Equal(new[] { 42 }, f.History.Load("five_hour", "Home"));
    }

    [Fact]
    public void Rename_without_source_file_is_noop()
    {
        using var f = new Fixture("Personal");
        f.History.Rename("Personal", "Home"); // no file yet — must not throw
        Assert.False(File.Exists(f.Paths.HistoryFileFor("Home")));
    }

    [Fact]
    public void Rename_overwrites_a_stale_target_file()
    {
        using var f = new Fixture("Personal");
        // A stale file left under the target name (e.g. from a pre-fix rename).
        File.WriteAllText(f.Paths.HistoryFileFor("Home"), """{"five_hour":[{"t":"2026-01-01T00:00:00+00:00","v":99}]}""");
        f.History.Append("five_hour", 42);
        f.History.Rename("Personal", "Home");
        Assert.Equal(new[] { 42 }, f.History.Load("five_hour", "Home"));
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test windows-dotnet/tests/Sanduhr.Tests/Sanduhr.Tests.csproj --filter "FullyQualifiedName~UsageHistoryTests"`
Expected: compile error — `Delete` / `Rename` not defined.

- [ ] **Step 3: Implement**

In `windows-dotnet/src/Sanduhr.Core/UsageHistory.cs`, add after `ClearAll` (~line 194):

```csharp
    /// <summary>Unlink the history file for the given (or active) account —
    /// used by full account removal, where <see cref="ClearAll"/>'s keep-the-file
    /// invariant is exactly wrong. Best-effort: a locked or missing file never
    /// throws. Other accounts are untouched.</summary>
    public void Delete(string? account = null)
    {
        var active = ResolveAccount(account);
        if (active is null)
            return;
        try
        {
            File.Delete(_paths.HistoryFileFor(active));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Best-effort — registry consistency wins over file cleanup.
        }
    }

    /// <summary>Move <c>history.{oldLabel}.json</c> to the new label so a renamed
    /// account keeps its chart. Overwrites a stale file already sitting at the
    /// target name. Best-effort no-op when there is no source file or the move
    /// fails — call only after the registry rename has succeeded.</summary>
    public void Rename(string oldLabel, string newLabel)
    {
        var src = _paths.HistoryFileFor(oldLabel);
        if (!File.Exists(src))
            return;
        try
        {
            File.Move(src, _paths.HistoryFileFor(newLabel), overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Best-effort — the account keeps working, only its chart resets.
        }
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test windows-dotnet/tests/Sanduhr.Tests/Sanduhr.Tests.csproj --filter "FullyQualifiedName~UsageHistoryTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/UsageHistory.cs windows-dotnet/tests/Sanduhr.Tests/UsageHistoryTests.cs
git commit -m "feat(core): UsageHistory.Delete unlinks and Rename carries the history file"
```

---

### Task 3: `ReauthRouting` decision table + origin-aware `SignInPromptCopy`

**Files:**
- Create: `windows-dotnet/src/Sanduhr.Core/ReauthRouting.cs`
- Modify: `windows-dotnet/src/Sanduhr.Core/SignInPromptCopy.cs` (whole file below)
- Test: create `windows-dotnet/tests/Sanduhr.Tests/ReauthRoutingTests.cs`; modify `windows-dotnet/tests/Sanduhr.Tests/SignInPromptCopyTests.cs`

**Interfaces:**
- Consumes: `SignInReason` (Task 0 state — unchanged), `AccountOrigin` (Task 1).
- Produces: `enum AuthFlow { EmbeddedAdd, ManualAdd, EmbeddedReauth, ManualReauth }`; `AuthFlow ReauthRouting.Primary(SignInReason, AccountOrigin)`; `AuthFlow ReauthRouting.Secondary(SignInReason, AccountOrigin)`; `SignInPrompt` record gains a 4th positional field `SecondaryLabel`; `SignInPromptCopy.For(SignInReason reason, AccountOrigin origin = AccountOrigin.Embedded)`. Task 6 consumes all of these by exactly these names.
- Known callers of `SignInPromptCopy.For`: `WidgetViewModel.PromptHeadline/PromptSubtitle/PromptPrimaryLabel` (WidgetViewModel.cs:110-114) — the default `origin` parameter keeps them compiling until Task 6 rewires them.

- [ ] **Step 1: Write the failing tests**

Create `windows-dotnet/tests/Sanduhr.Tests/ReauthRoutingTests.cs`:

```csharp
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>Truth table for the WS-A origin-aware recovery-card routing. The
/// primary action matches the account's origin (embedded accounts re-auth in
/// the browser, manual accounts re-paste); the secondary is always the OTHER
/// method, in place. FirstRun keeps add semantics on both.</summary>
public class ReauthRoutingTests
{
    [Theory]
    [InlineData(SignInReason.FirstRun, AccountOrigin.Embedded, AuthFlow.EmbeddedAdd)]
    [InlineData(SignInReason.FirstRun, AccountOrigin.Manual, AuthFlow.EmbeddedAdd)]
    [InlineData(SignInReason.Expired, AccountOrigin.Embedded, AuthFlow.EmbeddedReauth)]
    [InlineData(SignInReason.Expired, AccountOrigin.Manual, AuthFlow.ManualReauth)]
    [InlineData(SignInReason.Blocked, AccountOrigin.Embedded, AuthFlow.EmbeddedReauth)]
    [InlineData(SignInReason.Blocked, AccountOrigin.Manual, AuthFlow.ManualReauth)]
    public void Primary_flow(SignInReason reason, AccountOrigin origin, AuthFlow expected)
        => Assert.Equal(expected, ReauthRouting.Primary(reason, origin));

    [Theory]
    [InlineData(SignInReason.FirstRun, AccountOrigin.Embedded, AuthFlow.ManualAdd)]
    [InlineData(SignInReason.FirstRun, AccountOrigin.Manual, AuthFlow.ManualAdd)]
    [InlineData(SignInReason.Expired, AccountOrigin.Embedded, AuthFlow.ManualReauth)]
    [InlineData(SignInReason.Expired, AccountOrigin.Manual, AuthFlow.EmbeddedReauth)]
    [InlineData(SignInReason.Blocked, AccountOrigin.Embedded, AuthFlow.ManualReauth)]
    [InlineData(SignInReason.Blocked, AccountOrigin.Manual, AuthFlow.EmbeddedReauth)]
    public void Secondary_flow(SignInReason reason, AccountOrigin origin, AuthFlow expected)
        => Assert.Equal(expected, ReauthRouting.Secondary(reason, origin));
}
```

Replace the whole body of `SignInPromptCopyTests` (the record is growing a field and the copy is origin-aware now):

```csharp
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class SignInPromptCopyTests
{
    [Fact]
    public void FirstRun_sells_the_no_devtools_flow()
    {
        var p = SignInPromptCopy.For(SignInReason.FirstRun);
        Assert.Equal("Track your Claude usage", p.Headline);
        Assert.Contains("no DevTools", p.Subtitle);
        Assert.Equal("Sign in to Claude", p.PrimaryLabel);
        Assert.Equal("Paste a key instead", p.SecondaryLabel);
    }

    [Fact]
    public void Expired_embedded_points_at_browser_reauth_with_paste_escape()
    {
        var p = SignInPromptCopy.For(SignInReason.Expired, AccountOrigin.Embedded);
        Assert.Equal("Session expired", p.Headline);
        Assert.Equal("Sign in again", p.PrimaryLabel);
        Assert.Equal("Paste a key instead", p.SecondaryLabel);
        Assert.DoesNotContain("DevTools", p.Subtitle, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Expired_manual_leads_with_key_paste()
    {
        var p = SignInPromptCopy.For(SignInReason.Expired, AccountOrigin.Manual);
        Assert.Equal("Session expired", p.Headline);
        Assert.Equal("Paste a new key", p.PrimaryLabel);
        Assert.Equal("Use browser sign-in instead", p.SecondaryLabel);
        Assert.Contains("sessionKey", p.Subtitle);
    }

    [Fact]
    public void Blocked_embedded_explains_the_cloudflare_refresh()
    {
        var p = SignInPromptCopy.For(SignInReason.Blocked, AccountOrigin.Embedded);
        Assert.Equal("Connection challenged", p.Headline);
        Assert.Equal("Sign in again", p.PrimaryLabel);
        Assert.Equal("Paste a key instead", p.SecondaryLabel);
    }

    [Fact]
    public void Blocked_manual_leads_with_key_paste_and_mentions_cf()
    {
        var p = SignInPromptCopy.For(SignInReason.Blocked, AccountOrigin.Manual);
        Assert.Equal("Connection challenged", p.Headline);
        Assert.Equal("Paste a new key", p.PrimaryLabel);
        Assert.Equal("Use browser sign-in instead", p.SecondaryLabel);
        Assert.Contains("cf_clearance", p.Subtitle);
    }

    [Fact]
    public void None_has_no_card_copy()
        => Assert.Throws<System.ArgumentOutOfRangeException>(
            () => SignInPromptCopy.For(SignInReason.None));
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test windows-dotnet/tests/Sanduhr.Tests/Sanduhr.Tests.csproj --filter "FullyQualifiedName~ReauthRoutingTests|FullyQualifiedName~SignInPromptCopyTests"`
Expected: compile error — `AuthFlow` / `SecondaryLabel` not defined.

- [ ] **Step 3: Create `ReauthRouting`**

Create `windows-dotnet/src/Sanduhr.Core/ReauthRouting.cs`:

```csharp
namespace Sanduhr.Core;

/// <summary>Which auth flow a recovery-card button should launch.</summary>
public enum AuthFlow
{
    /// <summary>Embedded browser login that ADDS an account (first-run / add).</summary>
    EmbeddedAdd,

    /// <summary>Manual key paste that ADDS an account.</summary>
    ManualAdd,

    /// <summary>Embedded browser login overwriting the account IN PLACE.</summary>
    EmbeddedReauth,

    /// <summary>Manual key paste overwriting the account IN PLACE.</summary>
    ManualReauth,
}

/// <summary>
/// The recovery card's routing table, pure so it truth-table tests without WPF
/// (same pattern as <see cref="SignInPromptCopy"/>). Primary follows the
/// account's origin — an embedded account re-auths in the browser, a
/// manually-pasted account re-pastes (the browser login can't help the
/// Google-SSO population that was forced onto manual entry). Secondary is
/// always the other method, still in place; only FirstRun keeps add semantics.
/// </summary>
public static class ReauthRouting
{
    public static AuthFlow Primary(SignInReason reason, AccountOrigin origin) => reason switch
    {
        SignInReason.Expired or SignInReason.Blocked =>
            origin == AccountOrigin.Manual ? AuthFlow.ManualReauth : AuthFlow.EmbeddedReauth,
        _ => AuthFlow.EmbeddedAdd,
    };

    public static AuthFlow Secondary(SignInReason reason, AccountOrigin origin) => reason switch
    {
        SignInReason.Expired or SignInReason.Blocked =>
            origin == AccountOrigin.Manual ? AuthFlow.EmbeddedReauth : AuthFlow.ManualReauth,
        _ => AuthFlow.ManualAdd,
    };
}
```

- [ ] **Step 4: Rewrite `SignInPromptCopy`**

Replace the whole of `windows-dotnet/src/Sanduhr.Core/SignInPromptCopy.cs`:

```csharp
namespace Sanduhr.Core;

/// <summary>The headline / subtitle / button text the recovery card shows for a
/// given <see cref="SignInReason"/> + <see cref="AccountOrigin"/>. A pure record
/// so the copy unit-tests without WPF.</summary>
public sealed record SignInPrompt(
    string Headline, string Subtitle, string PrimaryLabel, string SecondaryLabel);

/// <summary>
/// Maps a <see cref="SignInReason"/> (and the active account's
/// <see cref="AccountOrigin"/>) to the card copy. Centralised here (pure Core) so
/// the widget never hardcodes recovery wording and the strings are testable. The
/// first-run copy keeps the headline feature's promise ("no DevTools, no
/// copy-paste"); expired/blocked copy leads with the method that actually works
/// for the account — browser re-auth for embedded accounts, key paste for
/// manually-entered ones — with the other method as the secondary escape hatch.
/// Routing itself lives in <see cref="ReauthRouting"/>; the two tables must stay
/// in step (primary copy describes <see cref="ReauthRouting.Primary"/>).
/// </summary>
public static class SignInPromptCopy
{
    public static SignInPrompt For(SignInReason reason, AccountOrigin origin = AccountOrigin.Embedded)
        => (reason, origin) switch
        {
            (SignInReason.FirstRun, _) => new SignInPrompt(
                "Track your Claude usage",
                "Sign in once in a secure window. Sanduhr reads your usage automatically — no DevTools, no copy-paste.",
                "Sign in to Claude",
                "Paste a key instead"),
            (SignInReason.Expired, AccountOrigin.Manual) => new SignInPrompt(
                "Session expired",
                "Your pasted sessionKey stopped working. Paste a fresh one — this account's history stays put.",
                "Paste a new key",
                "Use browser sign-in instead"),
            (SignInReason.Expired, _) => new SignInPrompt(
                "Session expired",
                "Your sign-in timed out. Sign in again — it only takes a few seconds.",
                "Sign in again",
                "Paste a key instead"),
            (SignInReason.Blocked, AccountOrigin.Manual) => new SignInPrompt(
                "Connection challenged",
                "Cloudflare needs a fresh check. Paste a new sessionKey (and cf_clearance if you have it).",
                "Paste a new key",
                "Use browser sign-in instead"),
            (SignInReason.Blocked, _) => new SignInPrompt(
                "Connection challenged",
                "Cloudflare needs a fresh check. Sign in again to refresh it automatically.",
                "Sign in again",
                "Paste a key instead"),
            _ => throw new System.ArgumentOutOfRangeException(
                nameof(reason), reason, "No card copy for a non-prompt reason."),
        };
}
```

- [ ] **Step 5: Run the full Core test suite**

Run: `dotnet test windows-dotnet/tests/Sanduhr.Tests/Sanduhr.Tests.csproj`
Expected: PASS. (`WidgetViewModel` still compiles against `For(reason)` via the default parameter; the record's new field is positional-only so no other construction sites exist — `SignInPrompt` is only built inside `SignInPromptCopy`.)

- [ ] **Step 6: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/ReauthRouting.cs windows-dotnet/src/Sanduhr.Core/SignInPromptCopy.cs windows-dotnet/tests/Sanduhr.Tests/ReauthRoutingTests.cs windows-dotnet/tests/Sanduhr.Tests/SignInPromptCopyTests.cs
git commit -m "feat(core): origin-aware reauth routing table and recovery-card copy"
```

---

### Task 4: `SignInCoordinator` — origin writes + in-place manual/embedded reauth for any label

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.App/Services/SignInCoordinator.cs`

**Interfaces:**
- Consumes: `AccountStore.SaveCredentials(label, sessionKey, cfClearance)` (existing), `AccountStore.SetOrigin` (Task 1), `ManualKeyWindow.ForReauth(label, persist, embeddedAvailable)` (Task 5 — this task and Task 5 must land together before an App build passes; implement both before building, or implement Task 5 first if executing strictly sequentially: the plan orders coordinator first because Task 5's factory is meaningless without a caller. **Build check for Task 4 happens in Task 5.**).
- Produces (Tasks 6–7 rely on these exact signatures):
  - `Task<SignInOutcome> ReauthenticateManualActiveAsync(Window? owner)`
  - `Task<SignInOutcome> ReauthenticateManualAsync(Window? owner, string label)`
  - `Task<SignInOutcome> ReauthenticateEmbeddedAsync(Window? owner, string label)`
- Known callers of modified symbols: `ReauthenticateActiveAsync` ← `App.RunReauthAsync` (App.xaml.cs:171-177, signature unchanged); `SignInManual` ← `App.RunSignInAsync` (App.xaml.cs:160, signature unchanged); `RunEmbeddedAsync` is private.

- [ ] **Step 1: Add the manual-fallback parameter to the embedded engine**

In `SignInCoordinator.cs`, change `RunEmbeddedAsync`'s signature and its three manual-fallback sites so a reauth that degrades to paste stays in place (spec §3). Replace the method declaration (line ~71) with:

```csharp
    private async Task<SignInOutcome> RunEmbeddedAsync(
        Window? owner,
        Func<CapturedCookies, string> persist,
        Func<Window?, Task<SignInOutcome>>? manualFallback = null)
    {
        // Add-flows fall back to the add-semantics manual modal (today's behavior);
        // reauth flows pass their own in-place manual variant.
        Func<Window?, Task<SignInOutcome>> manual =
            manualFallback ?? (o => SignInManual(o, x => RunEmbeddedAsync(x, persist, manualFallback)));
```

Then inside the method body replace, in order:

1. Line ~74 (runtime pre-check): `return await ShowRuntimeMissingThenMaybeManual(owner, o => RunEmbeddedAsync(o, persist));`
   → `return await ShowRuntimeMissingThenMaybeManual(owner, o => RunEmbeddedAsync(o, persist, manualFallback), manual);`
2. Line ~87 (profile-allocation catch): `return await SignInManual(owner, o => RunEmbeddedAsync(o, persist));`
   → `return await manual(owner);`
3. `case SignInResult.RuntimeMissing:` → `return await ShowRuntimeMissingThenMaybeManual(owner, o => RunEmbeddedAsync(o, persist, manualFallback), manual);`
4. The `SignInResult.Failed` retry: `? await SignInManual(owner, o => RunEmbeddedAsync(o, persist))` → `? await manual(owner)`
5. `case SignInResult.UseManual:` → `return await manual(owner);`

And update `ShowRuntimeMissingThenMaybeManual` (line ~233) to take the manual delegate instead of calling `SignInManual` directly:

```csharp
    private async Task<SignInOutcome> ShowRuntimeMissingThenMaybeManual(
        Window? owner,
        Func<Window?, Task<SignInOutcome>> retryEmbedded,
        Func<Window?, Task<SignInOutcome>> manual)
    {
        var modal = new WebView2NotInstalledWindow();
        SetOwner(modal, owner);
        var choice = modal.ShowDialog();
        // true  == the modal's Retry found the runtime — re-enter the embedded flow.
        // false == "Paste a key instead".  null == closed / Learn More only.
        if (choice == true)
            return await retryEmbedded(owner);
        return choice == false ? await manual(owner) : SignInOutcome.NotAdded;
    }
```

- [ ] **Step 2: Stamp origin in every existing persist**

Add this private helper after `NextFreeLabel()` (~line 212):

```csharp
    /// <summary>Best-effort origin stamp — the label is absent from the registry
    /// only in the theoretical first-run PersistReauth fallback, where Embedded
    /// is the default reading anyway.</summary>
    private void SetOriginSafe(string label, AccountOrigin origin)
    {
        if (_accounts.ListAccounts().Contains(label))
            _accounts.SetOrigin(label, origin);
    }
```

Then stamp each persist before its `return`:

- `PersistReauth` (line ~122): before both `return` statements — for the null-active branch: `var label = _accounts.GetActive() ?? "Personal"; SetOriginSafe(label, AccountOrigin.Embedded); return label;` — for the main branch: `SetOriginSafe(active, AccountOrigin.Embedded); return active;`
- `PersistEmbedded` (line ~159): first-run branch: `var label = _accounts.GetActive() ?? "Personal"; SetOriginSafe(label, AccountOrigin.Embedded); return label;` — named-add branch, after `_accounts.SetActive(label);`: `SetOriginSafe(label, AccountOrigin.Embedded);`
- `PersistManual` (line ~196): after `_accounts.SetActive(label);`: `SetOriginSafe(label, AccountOrigin.Manual);`

- [ ] **Step 3: Add the in-place reauth entry points**

Replace `ReauthenticateActiveAsync` (line ~60) and add the new methods directly under it:

```csharp
    /// <summary>
    /// Re-authenticate the ACTIVE account in place via the embedded browser —
    /// captured cookies overwrite the existing slot instead of allocating a new
    /// label, and a degrade-to-paste stays in place too. Used by the widget's
    /// Expired/Blocked recovery card for embedded-origin accounts.
    /// </summary>
    public Task<SignInOutcome> ReauthenticateActiveAsync(Window? owner)
    {
        var active = _accounts.GetActive();
        // No active account (theoretical): keep the historic create-"Personal" fallback.
        return active is null
            ? RunEmbeddedAsync(owner, PersistReauth)
            : ReauthenticateEmbeddedAsync(owner, active);
    }

    /// <summary>In-place embedded re-auth for a SPECIFIC label (Settings "Update
    /// sign-in…" works on non-active accounts). Manual fallback stays in place.</summary>
    public Task<SignInOutcome> ReauthenticateEmbeddedAsync(Window? owner, string label)
        => RunEmbeddedAsync(
            owner,
            cookies => PersistReauthFor(label, cookies),
            manualFallback: o => ReauthenticateManualAsync(o, label));

    /// <summary>In-place MANUAL re-auth of the active account — the recovery card's
    /// route for manual-origin accounts (and its "Paste a key instead" during
    /// recovery). Falls back to add semantics only when no account exists.</summary>
    public Task<SignInOutcome> ReauthenticateManualActiveAsync(Window? owner)
    {
        var active = _accounts.GetActive();
        return active is null ? SignInManual(owner) : ReauthenticateManualAsync(owner, active);
    }

    /// <summary>In-place manual re-auth for a SPECIFIC label: the paste modal in
    /// reauth mode (label locked), persisting via <see cref="PersistReauthManual"/> —
    /// no new account, no label prompt. "Use the secure sign-in window instead"
    /// bounces to the embedded reauth for the SAME label.</summary>
    public async Task<SignInOutcome> ReauthenticateManualAsync(Window? owner, string label)
    {
        var window = ManualKeyWindow.ForReauth(
            label, (_, cookies) => PersistReauthManual(label, cookies), IsRuntimeAvailable());
        SetOwner(window, owner);
        window.ShowDialog();

        return window.Result switch
        {
            SignInResult.Success s => new SignInOutcome(true, s.Label),
            SignInResult.UseEmbedded => await ReauthenticateEmbeddedAsync(owner, label),
            _ => SignInOutcome.NotAdded,
        };
    }

    /// <summary>Embedded re-auth save targeting a specific label (not the active
    /// pointer): overwrite its slots in place and stamp Embedded origin.</summary>
    private string PersistReauthFor(string label, CapturedCookies cookies)
    {
        _accounts.SaveCredentials(label, cookies.SessionKey!, cookies.CfClearance);
        SetOriginSafe(label, AccountOrigin.Embedded);
        return label;
    }

    /// <summary>Manual re-auth save: overwrite the label's slots in place — the
    /// missing counterpart to <see cref="PersistReauth"/> that used to make every
    /// recovery-paste allocate a duplicate "Account N".</summary>
    private string PersistReauthManual(string label, CapturedCookies cookies)
    {
        _accounts.SaveCredentials(label, cookies.SessionKey, cookies.CfClearance);
        SetOriginSafe(label, AccountOrigin.Manual);
        return label;
    }
```

- [ ] **Step 4: Commit** (build verification happens at the end of Task 5 — `ManualKeyWindow.ForReauth` does not exist yet)

```bash
git add windows-dotnet/src/Sanduhr.App/Services/SignInCoordinator.cs
git commit -m "feat(app): in-place manual + label-targeted reauth in SignInCoordinator"
```

---

### Task 5: `ManualKeyWindow` reauth mode + full re-theme

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.App/Views/ManualKeyWindow.xaml` (whole file below — it is currently 100% hardcoded hex, the audit the spec demanded)
- Modify: `windows-dotnet/src/Sanduhr.App/Views/ManualKeyWindow.xaml.cs`

**Interfaces:**
- Consumes: `Sanduhr.Brush.*` DynamicResources (applied to `Application.Current.Resources` by `ThemePalette.Apply` at startup and on every live theme switch — windows opened later inherit automatically).
- Produces: `static ManualKeyWindow ManualKeyWindow.ForReauth(string label, Func<string, CapturedCookies, string> persist, bool embeddedAvailable)` (consumed by Task 4, already written against it).

- [ ] **Step 1: Re-theme the XAML**

Replace the whole of `windows-dotnet/src/Sanduhr.App/Views/ManualKeyWindow.xaml`:

```xml
<Window x:Class="Sanduhr.App.Views.ManualKeyWindow"
        x:ClassModifier="internal"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Add account by sessionKey"
        Height="470" Width="520"
        WindowStartupLocation="CenterScreen"
        ResizeMode="NoResize"
        Background="{DynamicResource Sanduhr.Brush.Bg}">
    <Grid Margin="24">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
            <RowDefinition Height="Auto" />
        </Grid.RowDefinitions>

        <TextBlock Grid.Row="0" x:Name="HeadlineText" Text="Add account by sessionKey"
                   FontSize="16" FontWeight="Bold"
                   Foreground="{DynamicResource Sanduhr.Brush.Accent}" />
        <StackPanel Grid.Row="1" Margin="0,6,0,0">
            <TextBlock x:Name="IntroText"
                       Text="The power-user path. Paste your claude.ai sessionKey cookie — it's stored in the Windows Credential Manager, never in a file."
                       TextWrapping="Wrap" FontSize="11"
                       Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" />
            <TextBlock x:Name="UseEmbeddedLink" Margin="0,8,0,0" Cursor="Hand"
                       Visibility="Collapsed" MouseLeftButtonUp="OnUseEmbeddedClick">
                <Run Text="Use the secure sign-in window instead"
                     Foreground="{DynamicResource Sanduhr.Brush.Accent}"
                     TextDecorations="Underline" FontSize="11" />
            </TextBlock>
            <TextBlock x:Name="HelpToggle" Margin="0,6,0,0" Cursor="Hand"
                       MouseLeftButtonUp="OnHelpToggleClick">
                <Run Text="Where do I find the sessionKey?"
                     Foreground="{DynamicResource Sanduhr.Brush.TextDim}"
                     TextDecorations="Underline" FontSize="11" />
            </TextBlock>
            <TextBlock x:Name="HelpSteps" Margin="0,6,0,0" Visibility="Collapsed"
                       TextWrapping="Wrap" FontSize="11"
                       Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}"
                       Text="On claude.ai: open DevTools (F12) → Application → Cookies → claude.ai, then copy the value of the sessionKey cookie." />
        </StackPanel>

        <TextBlock Grid.Row="2" Text="Account name" FontSize="11"
                   Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" Margin="0,16,0,4" />
        <TextBox Grid.Row="3" x:Name="LabelBox" FontSize="13" Padding="8,6"
                 Background="{DynamicResource Sanduhr.Brush.Glass}"
                 Foreground="{DynamicResource Sanduhr.Brush.Text}"
                 BorderBrush="{DynamicResource Sanduhr.Brush.Border}"
                 CaretBrush="{DynamicResource Sanduhr.Brush.Text}" />

        <TextBlock Grid.Row="4" Text="sessionKey (required)" FontSize="11"
                   Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" Margin="0,14,0,4" />
        <TextBox Grid.Row="5" x:Name="SessionKeyBox" FontSize="13" Padding="8,6"
                 Background="{DynamicResource Sanduhr.Brush.Glass}"
                 Foreground="{DynamicResource Sanduhr.Brush.Text}"
                 BorderBrush="{DynamicResource Sanduhr.Brush.Border}"
                 CaretBrush="{DynamicResource Sanduhr.Brush.Text}"
                 FontFamily="Consolas, monospace" />

        <TextBlock Grid.Row="6" Text="cf_clearance (optional — only if Cloudflare blocks)" FontSize="11"
                   Foreground="{DynamicResource Sanduhr.Brush.TextSecondary}" Margin="0,14,0,4" />
        <TextBox Grid.Row="7" x:Name="CfClearanceBox" FontSize="13" Padding="8,6"
                 Background="{DynamicResource Sanduhr.Brush.Glass}"
                 Foreground="{DynamicResource Sanduhr.Brush.Text}"
                 BorderBrush="{DynamicResource Sanduhr.Brush.Border}"
                 CaretBrush="{DynamicResource Sanduhr.Brush.Text}"
                 FontFamily="Consolas, monospace" />

        <TextBlock Grid.Row="8" x:Name="ErrorText"
                   Foreground="{DynamicResource Sanduhr.Brush.PaceMarker}" FontSize="11"
                   TextWrapping="Wrap" Margin="0,12,0,0" VerticalAlignment="Top"
                   Visibility="Collapsed" />

        <StackPanel Grid.Row="9" Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,12,0,0">
            <Button x:Name="CancelButton" Content="Cancel" Padding="16,8" Margin="0,0,8,0"
                    Background="{DynamicResource Sanduhr.Brush.Glass}"
                    Foreground="{DynamicResource Sanduhr.Brush.Text}"
                    BorderBrush="{DynamicResource Sanduhr.Brush.Border}" BorderThickness="1"
                    Cursor="Hand" IsCancel="True" Click="OnCancelClick" />
            <Button x:Name="SaveButton" Content="Save" Padding="20,8" IsDefault="True"
                    Background="{DynamicResource Sanduhr.Brush.Accent}"
                    Foreground="{DynamicResource Sanduhr.Brush.Bg}" BorderThickness="0"
                    FontWeight="Bold" Cursor="Hand" Click="OnSaveClick" />
        </StackPanel>
    </Grid>
</Window>
```

(Every hex literal is gone; `HeadlineText` and `IntroText` gained names for the reauth factory. The monospace `Consolas` on the key boxes is input-field ergonomics, not theme identity — it stays, matching the spec's "no new tokens" rule.)

- [ ] **Step 2: Add the reauth factory**

In `ManualKeyWindow.xaml.cs`, add after the constructor (~line 35):

```csharp
    /// <summary>Reauth mode: update the key for an EXISTING account. The label is
    /// shown but locked (in-place overwrite — the persist delegate targets the
    /// label by closure, so what's displayed can't drift from what's written).</summary>
    public static ManualKeyWindow ForReauth(
        string label, Func<string, CapturedCookies, string> persist, bool embeddedAvailable)
    {
        var w = new ManualKeyWindow(label, persist, embeddedAvailable);
        w.Title = "Update sessionKey";
        w.HeadlineText.Text = $"Update sessionKey for '{label}'";
        w.IntroText.Text = "Paste a fresh claude.ai sessionKey for this account. Its history and settings stay put.";
        w.LabelBox.IsEnabled = false;
        w.LabelBox.Opacity = 0.6;
        w.SaveButton.Content = "Update";
        return w;
    }
```

- [ ] **Step 3: Build both projects**

Run: `dotnet build windows-dotnet/src/Sanduhr.App/Sanduhr.App.csproj`
Expected: build succeeds (this also validates Task 4's coordinator changes — first App build since).

- [ ] **Step 4: Run the full test suite**

Run: `dotnet test windows-dotnet/tests/Sanduhr.Tests/Sanduhr.Tests.csproj`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add windows-dotnet/src/Sanduhr.App/Views/ManualKeyWindow.xaml windows-dotnet/src/Sanduhr.App/Views/ManualKeyWindow.xaml.cs
git commit -m "feat(app): ManualKeyWindow reauth mode + retheme to Sanduhr.Brush resources"
```

---

### Task 6: Recovery-card routing in `WidgetViewModel` + card XAML

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/WidgetViewModel.cs` (prompt properties ~lines 108-122, events ~134-142, `PrimaryAuth`/`PasteKey` commands ~408-430)
- Modify: `windows-dotnet/src/Sanduhr.App/MainWindow.xaml` (secondary button, line ~431)
- Modify: `windows-dotnet/src/Sanduhr.App/App.xaml.cs` (event wiring ~40-42, reauth runner ~171)

**Interfaces:**
- Consumes: `ReauthRouting.Primary/Secondary`, `SignInPromptCopy.For(reason, origin)`, `AccountOrigin`, `AccountStore.GetOrigin` (Tasks 1+3), `SignInCoordinator.ReauthenticateManualActiveAsync` (Task 4).
- Produces: `event Func<Task>? ManualReauthRequested` on `WidgetViewModel`; `PromptSecondaryLabel` property; `SecondaryAuthCommand` (generated from `SecondaryAuth()`, replacing the XAML's `PasteKeyCommand` binding — the `PasteKeyRequested` event stays, now meaning strictly "manual ADD").
- Known callers: `PrimaryAuthCommand` ← MainWindow.xaml:414 (unchanged); `PasteKeyCommand` ← MainWindow.xaml:431 (rebound in this task); `PasteKeyRequested` ← App.xaml.cs:42 (unchanged).

- [ ] **Step 1: Origin-aware prompt properties**

In `WidgetViewModel.cs`, replace lines 109-114 (the three `Prompt*` properties) with:

```csharp
    /// <summary>The active account's credential origin — routes the recovery card.
    /// Embedded when signed out (FirstRun copy doesn't branch on origin anyway).</summary>
    private AccountOrigin ActiveOrigin
        => _accounts.GetActive() is { } label ? _accounts.GetOrigin(label) : AccountOrigin.Embedded;

    /// <summary>Card headline for the current reason (empty when hidden).</summary>
    public string PromptHeadline => Reason == SignInReason.None ? "" : SignInPromptCopy.For(Reason, ActiveOrigin).Headline;
    /// <summary>Card subtitle for the current reason (empty when hidden).</summary>
    public string PromptSubtitle => Reason == SignInReason.None ? "" : SignInPromptCopy.For(Reason, ActiveOrigin).Subtitle;
    /// <summary>Primary-button label for the current reason (empty when hidden).</summary>
    public string PromptPrimaryLabel => Reason == SignInReason.None ? "" : SignInPromptCopy.For(Reason, ActiveOrigin).PrimaryLabel;
    /// <summary>Secondary-link label for the current reason (empty when hidden).</summary>
    public string PromptSecondaryLabel => Reason == SignInReason.None ? "" : SignInPromptCopy.For(Reason, ActiveOrigin).SecondaryLabel;
```

And in `OnReasonChanged` (line ~116) add:

```csharp
        OnPropertyChanged(nameof(PromptSecondaryLabel));
```

- [ ] **Step 2: Add the manual-reauth event**

After the `ReauthRequested` event declaration (~line 138) add:

```csharp
    /// <summary>Raised when the ACTIVE account needs an IN-PLACE manual key paste
    /// (manual-origin account expired, or "Paste a key instead" during recovery) —
    /// App routes this to <c>SignInCoordinator.ReauthenticateManualActiveAsync</c>.
    /// Distinct from <see cref="PasteKeyRequested"/>, which ADDS a new account.</summary>
    public event Func<Task>? ManualReauthRequested;
```

- [ ] **Step 3: Route both card buttons through the table**

Replace `PrimaryAuth` (lines ~408-423) and `PasteKey` (lines ~425-430) with:

```csharp
    /// <summary>The recovery card's primary button, routed by the origin-aware
    /// table: FirstRun → add-account sign-in; Expired/Blocked → in-place re-auth
    /// via the method that matches how the account was created.</summary>
    [RelayCommand]
    private async Task PrimaryAuth()
    {
        switch (ReauthRouting.Primary(Reason, ActiveOrigin))
        {
            case AuthFlow.ManualReauth:
                if (ManualReauthRequested is not null) await ManualReauthRequested.Invoke();
                break;
            case AuthFlow.EmbeddedReauth:
                if (ReauthRequested is not null) await ReauthRequested.Invoke();
                break;
            default: // EmbeddedAdd (FirstRun)
                if (SignInRequested is not null) await SignInRequested.Invoke();
                break;
        }
    }

    /// <summary>The recovery card's secondary link — always the OTHER method. In
    /// recovery it re-auths IN PLACE (the pre-WS-A behavior of adding a duplicate
    /// "Account N" here was a bug); on FirstRun it stays a manual add.</summary>
    [RelayCommand]
    private async Task SecondaryAuth()
    {
        switch (ReauthRouting.Secondary(Reason, ActiveOrigin))
        {
            case AuthFlow.ManualReauth:
                if (ManualReauthRequested is not null) await ManualReauthRequested.Invoke();
                break;
            case AuthFlow.EmbeddedReauth:
                if (ReauthRequested is not null) await ReauthRequested.Invoke();
                break;
            default: // ManualAdd (FirstRun)
                if (PasteKeyRequested is not null) await PasteKeyRequested.Invoke();
                break;
        }
    }
```

- [ ] **Step 4: Rebind the card's secondary button**

In `MainWindow.xaml` line ~431, change the secondary button from a hardcoded label + add-flow command to the routed command + copy-driven label:

```xml
                            <Button Command="{Binding SecondaryAuthCommand}" Cursor="Hand"
                                    Content="{Binding PromptSecondaryLabel}"
                                    HorizontalAlignment="Center" Margin="0,12,0,0"
                                    Background="Transparent" BorderThickness="0" FontSize="11"
                                    Foreground="{DynamicResource Sanduhr.Brush.TextDim}">
```

(The inner `<Button.Template>` block stays exactly as-is.)

- [ ] **Step 5: Wire the event in App**

In `App.xaml.cs`, after line 41 (`_vm.ReauthRequested += RunReauthAsync;`) add:

```csharp
        _vm.ManualReauthRequested += RunManualReauthAsync;
```

And add next to `RunReauthAsync` (~line 171):

```csharp
    /// <summary>In-place MANUAL re-auth of the active account (recovery card,
    /// manual-origin primary or paste-during-recovery secondary).</summary>
    private async Task RunManualReauthAsync()
    {
        var coordinator = new SignInCoordinator();
        var outcome = await coordinator.ReauthenticateManualActiveAsync(_window);
        if (outcome.Added && _vm is not null)
            await _vm.ReloadAfterSignInAsync();
    }
```

- [ ] **Step 6: Build + full test suite**

Run: `dotnet build windows-dotnet/src/Sanduhr.App/Sanduhr.App.csproj && dotnet test windows-dotnet/tests/Sanduhr.Tests/Sanduhr.Tests.csproj`
Expected: both pass.

- [ ] **Step 7: Commit**

```bash
git add windows-dotnet/src/Sanduhr.App/ViewModels/WidgetViewModel.cs windows-dotnet/src/Sanduhr.App/MainWindow.xaml windows-dotnet/src/Sanduhr.App/App.xaml.cs
git commit -m "feat(app): origin-aware recovery card — manual accounts get the paste modal, in place"
```

---

### Task 7: Settings "Update sign-in…" per account

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/AccountsViewModel.cs` (ctor ~line 60, commands ~89-147)
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/SettingsViewModel.cs` (ctor, line 34)
- Modify: `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml` (per-selection actions row, ~line 431)
- Modify: `windows-dotnet/src/Sanduhr.App/App.xaml.cs` (`ShowSettingsAsync` ~line 140, new runner)

**Interfaces:**
- Consumes: `SignInCoordinator.ReauthenticateManualAsync(owner, label)` / `ReauthenticateEmbeddedAsync(owner, label)` (Task 4), `WidgetViewModel.AccountStore.GetOrigin` (Task 1), `WidgetViewModel.ActiveAccount` / `ReloadAfterSignInAsync` (existing).
- Produces: `AccountsViewModel` ctor becomes `(WidgetViewModel widget, Func<Task> addAccountAsync, Func<string, Task> updateSignInAsync)`; new `UpdateSignInCommand`. `SettingsViewModel` ctor becomes `(WidgetViewModel widget, Func<Task> addAccountAsync, Func<string, Task> updateSignInAsync)`.
- Known callers: `new SettingsViewModel(...)` ← `App.ShowSettingsAsync` only (App.xaml.cs:140). `new AccountsViewModel(...)` ← `SettingsViewModel` ctor only.

- [ ] **Step 1: Extend `AccountsViewModel`**

Add the field + ctor param (lines ~44 and ~60):

```csharp
    private readonly Func<string, Task> _updateSignInAsync;
```

```csharp
    public AccountsViewModel(WidgetViewModel widget, Func<Task> addAccountAsync, Func<string, Task> updateSignInAsync)
    {
        _widget = widget;
        _addAccountAsync = addAccountAsync;
        _updateSignInAsync = updateSignInAsync;
        Reload();
    }
```

Add `UpdateSignInCommand` to the `SelectedAccount` notify list (lines ~54-58):

```csharp
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SwitchToCommand))]
    [NotifyCanExecuteChangedFor(nameof(RenameCommand))]
    [NotifyCanExecuteChangedFor(nameof(SignOutCommand))]
    [NotifyCanExecuteChangedFor(nameof(UpdateSignInCommand))]
    private AccountItemViewModel? _selectedAccount;
```

Add the command after `Rename` (~line 123):

```csharp
    /// <summary>Refresh the selected account's credentials IN PLACE — works for
    /// non-active accounts too (pre-WS-A, only the active account could reauth,
    /// and only from the widget's recovery card). Routing by origin happens in
    /// the injected delegate (App owns the coordinator + window ownership).</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task UpdateSignIn()
    {
        var item = SelectedAccount;
        if (item is null)
            return;
        await _updateSignInAsync(item.Label);
        Reload();
    }
```

- [ ] **Step 2: Thread the delegate through `SettingsViewModel`**

Change the ctor (line 34-36):

```csharp
    public SettingsViewModel(WidgetViewModel widget, Func<Task> addAccountAsync, Func<string, Task> updateSignInAsync)
    {
        Accounts = new AccountsViewModel(widget, addAccountAsync, updateSignInAsync);
```

(rest of the ctor unchanged.)

- [ ] **Step 3: Implement the runner in `App` and pass it**

In `App.xaml.cs` `ShowSettingsAsync`, change line ~140:

```csharp
        var svm = new SettingsViewModel(_vm, () => RunSignInAsync(embedded: true), RunUpdateSignInAsync);
```

Add next to `RunManualReauthAsync`:

```csharp
    /// <summary>Settings "Update sign-in…": in-place credential refresh for ANY
    /// account, routed by that account's origin. Only reload the live fetcher
    /// when the refreshed account is the active one.</summary>
    private async Task RunUpdateSignInAsync(string label)
    {
        if (_vm is null)
            return;
        Window? owner = _settingsWindow ?? (Window?)_window;
        var coordinator = new SignInCoordinator();
        var outcome = _vm.AccountStore.GetOrigin(label) == AccountOrigin.Manual
            ? await coordinator.ReauthenticateManualAsync(owner, label)
            : await coordinator.ReauthenticateEmbeddedAsync(owner, label);
        if (outcome.Added && _vm.ActiveAccount == label)
            await _vm.ReloadAfterSignInAsync();
    }
```

Add `using Sanduhr.Core;` to `App.xaml.cs`'s usings (for `AccountOrigin`) if not already present.

- [ ] **Step 4: Add the button**

In `SettingsWindow.xaml`, in the per-selection actions row (after the `Rename…` button, line ~435):

```xml
                                <Button Style="{StaticResource FlatButton}" Content="Update sign-in…"
                                        Command="{Binding UpdateSignInCommand}" />
```

- [ ] **Step 5: Build + full test suite**

Run: `dotnet build windows-dotnet/src/Sanduhr.App/Sanduhr.App.csproj && dotnet test windows-dotnet/tests/Sanduhr.Tests/Sanduhr.Tests.csproj`
Expected: both pass.

- [ ] **Step 6: Commit**

```bash
git add windows-dotnet/src/Sanduhr.App/ViewModels/AccountsViewModel.cs windows-dotnet/src/Sanduhr.App/ViewModels/SettingsViewModel.cs windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml windows-dotnet/src/Sanduhr.App/App.xaml.cs
git commit -m "feat(app): per-account Update sign-in in Settings, routed by origin"
```

---

### Task 8: Complete delete, rename carries history, honest copy

**Files:**
- Modify: `windows-dotnet/src/Sanduhr.Core/Paths.cs` (add `WebView2FetchDir`)
- Modify: `windows-dotnet/src/Sanduhr.App/Services/WebView2ApiClient.cs` (line ~99, use the new Paths property)
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/WidgetViewModel.cs` (`RenameAccount` ~492, `SignOutAccountAsync` ~507)
- Modify: `windows-dotnet/src/Sanduhr.App/ViewModels/AccountsViewModel.cs` (`SignOut` confirm copy ~131-135)
- Modify: `windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml` ("Sign out" → "Remove account…", line ~436)
- Test: `windows-dotnet/tests/Sanduhr.Tests/PathsTests.cs`

**Interfaces:**
- Consumes: `UsageHistory.Delete` / `UsageHistory.Rename` (Task 2).
- Produces: `string Paths.WebView2FetchDir` (single source of truth for the transport profile — `%APPDATA%\Sanduhr\webview2-fetch`).
- Known callers of `SignOutAccountAsync`: `AccountsViewModel.SignOut` only. Of `RenameAccount(old,new)` on the VM: `AccountsViewModel.Rename` only. Signatures unchanged.

- [ ] **Step 1: Write the failing Paths test**

Append inside the `PathsTests` class in `windows-dotnet/tests/Sanduhr.Tests/PathsTests.cs` (match the existing test style in that file — it constructs `new Paths(temp)` the same way UsageHistoryTests' fixture does):

```csharp
    [Fact]
    public void WebView2FetchDir_is_under_appdata_sanduhr()
    {
        using var temp = new TempDir();
        var p = new Paths(temp.Path);
        Assert.Equal(Path.Combine(temp.Path, "Sanduhr", "webview2-fetch"), p.WebView2FetchDir);
    }
```

Run: `dotnet test windows-dotnet/tests/Sanduhr.Tests/Sanduhr.Tests.csproj --filter "FullyQualifiedName~PathsTests"`
Expected: compile error — `WebView2FetchDir` not defined.

- [ ] **Step 2: Add the path + repoint the transport**

In `Paths.cs`, after `SoundsDir` (~line 76):

```csharp
    /// <summary>Shared fetch-transport browser profile:
    /// <c>%APPDATA%\Sanduhr\webview2-fetch</c>. Holds the ACTIVE account's
    /// claude.ai cookies on disk — account deletion purges it when no account
    /// remains (no future client init would ever wipe it otherwise).</summary>
    public string WebView2FetchDir => Path.Combine(AppDataDir, "webview2-fetch");
```

In `WebView2ApiClient.cs` line ~99, replace:

```csharp
        _profileDir = Path.Combine(paths.AppDataDir, "webview2-fetch");
```

with:

```csharp
        _profileDir = paths.WebView2FetchDir;
```

Run the Paths tests again — expected: PASS.

- [ ] **Step 3: Rework `SignOutAccountAsync` + rename**

In `WidgetViewModel.cs`, replace `RenameAccount` (~lines 492-497):

```csharp
    /// <summary>Rename an account in place (same secrets, new label). The active
    /// pointer follows the rename inside <see cref="AccountStore"/>, and the
    /// history file moves with it so the chart survives the rename.</summary>
    public void RenameAccount(string oldLabel, string newLabel)
    {
        _accounts.RenameAccount(oldLabel, newLabel);
        _history.Rename(oldLabel, newLabel);
        RefreshAccountLabel();
        AccountsChanged?.Invoke();
    }
```

Replace `SignOutAccountAsync` (~lines 507-524):

```csharp
    /// <summary>
    /// Remove an account completely: unlink its per-account history file, drop its
    /// Credential-Manager slots (sessionKey / cf_clearance / origin), and — when it
    /// was the active one — <see cref="AccountStore.RemoveAccount"/> advances the
    /// active pointer to the first remaining account (or none). Removing the LAST
    /// account also purges the shared webview2-fetch transport profile: no future
    /// client init would ever run its anti-bleed cookie wipe, so the deleted
    /// account's live claude.ai cookies would otherwise sit on disk indefinitely.
    /// </summary>
    public async Task SignOutAccountAsync(string label)
    {
        bool wasActive = _accounts.GetActive() == label;
        if (wasActive)
        {
            // Release the transport's hold on the shared profile BEFORE cleanup.
            (_client as IDisposable)?.Dispose();
            _client = null;
            _fetcher = null;
        }
        _history.Delete(label);
        _accounts.RemoveAccount(label);

        if (wasActive)
        {
            Tiers.Clear();
            _lastData = null;
            if (_accounts.ListAccounts().Count == 0)
                await PurgeTransportProfileBestEffortAsync();
            RebuildFetcher();
            await RefreshAsync();
        }
        RefreshAccountLabel();
        AccountsChanged?.Invoke();
    }

    /// <summary>Delete the shared webview2-fetch profile directory. WebView2's
    /// browser processes release their file locks asynchronously after Dispose,
    /// so retry briefly; a stubborn lock is logged and left for the next client
    /// init's cookie wipe (best-effort — the app must keep working regardless).</summary>
    private async Task PurgeTransportProfileBestEffortAsync()
    {
        var profile = _paths.WebView2FetchDir;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                if (Directory.Exists(profile))
                    Directory.Delete(profile, recursive: true);
                return;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                await Task.Delay(300);
            }
        }
        Debug.WriteLine("[Sanduhr] webview2-fetch purge failed — cookies remain until the next transport init");
    }
```

(`System.IO` and `System.Diagnostics` are already imported in this file; verify and add if not.)

- [ ] **Step 4: Honest confirm + button rename**

In `AccountsViewModel.SignOut` (~lines 131-135), replace the dialog text + title:

```csharp
        var text =
            $"Remove the '{item.Label}' account from Sanduhr?\n\n" +
            "This deletes its saved sign-in from the Windows Credential Manager " +
            $"and its usage-history file (history.{item.Label}.json). Cannot be undone.";
        var result = ThemedDialog.Show(_owner, "Remove account", text, MessageBoxButton.YesNo, ThemedDialogKind.Warning);
```

In `SettingsWindow.xaml` line ~436, change the button label:

```xml
                                <Button Style="{StaticResource FlatButton}" Content="Remove account…"
                                        Command="{Binding SignOutCommand}" Foreground="{DynamicResource Sanduhr.Brush.PaceMarker}" />
```

- [ ] **Step 5: Sweep for leftover "Sign out" strings**

Run: `grep -rn "Sign out" windows-dotnet/src/`
Expected: no user-facing occurrences remain (code identifiers like `SignOutCommand`/`SignOutAccountAsync` may stay — renaming symbols is out of scope; doc comments referring to "the sign-out flow" in `CredentialStore.cs:76` may stay).

- [ ] **Step 6: Build + full test suite**

Run: `dotnet build windows-dotnet/src/Sanduhr.App/Sanduhr.App.csproj && dotnet test windows-dotnet/tests/Sanduhr.Tests/Sanduhr.Tests.csproj`
Expected: both pass.

- [ ] **Step 7: Commit**

```bash
git add windows-dotnet/src/Sanduhr.Core/Paths.cs windows-dotnet/src/Sanduhr.App/Services/WebView2ApiClient.cs windows-dotnet/src/Sanduhr.App/ViewModels/WidgetViewModel.cs windows-dotnet/src/Sanduhr.App/ViewModels/AccountsViewModel.cs windows-dotnet/src/Sanduhr.App/Views/SettingsWindow.xaml windows-dotnet/tests/Sanduhr.Tests/PathsTests.cs
git commit -m "feat(app): complete account removal + rename carries history + honest confirm copy"
```

---

### Task 9: Smoke-test plan, final verification, wrap-up

**Files:**
- Modify: `docs/smoke-test-plan.md` (append the WS-A section below)

**Interfaces:** none — documentation + verification only.

- [ ] **Step 1: Append the WS-A section to the smoke-test plan**

Append at the end of `docs/smoke-test-plan.md` (adjust the heading level to match the file's existing structure):

```markdown
## WS-A — auth & accounts overhaul (2026-07-11)

Theming rule for every scenario below: run once in the default theme, then flip through one dark, one light, and Matrix with the surface open — zero unstyled or stale-colored elements, live re-tint included.

1. **Manual-origin expiry → paste-primary card.** Add an account via "Paste a key instead" with a deliberately bogus sessionKey. Wait for the fetch to 401. Expect the recovery card to lead with "Paste a new key" (NOT "Sign in again"); the secondary link reads "Use browser sign-in instead". Primary opens the Update sessionKey modal with the account name locked.
2. **Embedded-origin expiry unchanged.** For a browser-signed-in account with a dead session, the card still leads with "Sign in again"; secondary reads "Paste a key instead" and opens the paste modal IN PLACE (see 3).
3. **No duplicate accounts from recovery paste.** From an Expired card, use the paste path and save a key. Expect: same account label, no new "Account N" in Settings ▸ Accounts, history intact.
4. **Settings ▸ Update sign-in, non-active account.** With two accounts, select the non-active one → "Update sign-in…". Expect the flow matching that account's origin, no active-account switch, and no fetcher hiccup on the active account.
5. **Remove account is complete.** Remove a signed-in account. Expect: gone from the list, `history.{label}.json` gone from `%APPDATA%\Sanduhr`, confirm-dialog text matches what actually happened.
6. **Last-account removal purges transport cookies.** Remove the only account. Expect the widget to drop to first-run, and `%APPDATA%\Sanduhr\webview2-fetch` to be deleted (or, if locked, a debug log line about the deferred purge).
7. **Rename carries history.** Rename an account with a visible history chart. Expect the chart intact under the new name and no `history.{old}.json` left behind.
8. **First-run unchanged.** Fresh install: primary "Sign in to Claude", secondary "Paste a key instead" ADDS the account (no reauth semantics).
```

- [ ] **Step 2: Full verification**

Run: `dotnet build windows-dotnet/src/Sanduhr.App/Sanduhr.App.csproj && dotnet test windows-dotnet/tests/Sanduhr.Tests/Sanduhr.Tests.csproj`
Expected: build clean, all tests pass (297 pre-existing + ~19 new).

If the GitNexus MCP tools are available: run `gitnexus_detect_changes({scope: "compare", base_ref: "main"})` and confirm only the symbols named in this plan changed.

- [ ] **Step 3: Commit the smoke plan + push the branch**

```bash
git add docs/smoke-test-plan.md
git commit -m "docs(test): WS-A smoke scenarios — origin routing, complete delete, theming flips"
git push -u origin feat/ws-a-auth-accounts
```

- [ ] **Step 4: Manual smoke** — run scenarios 1-3 and 5 from the new smoke section against a debug build (`dotnet run --project windows-dotnet/src/Sanduhr.App/Sanduhr.App.csproj`) before opening the PR. Scenario 6 requires deleting your only account — use a throwaway.

---

## Plan self-review (done at authoring time)

- **Spec coverage:** §1 origin flag → Task 1; §2 routing + copy → Tasks 3+6; §3 in-place manual reauth + fallback rewiring → Task 4; §4 Settings per-account reauth → Task 7; §5 complete delete → Tasks 2+8; §6 rename carries state → Tasks 1+2+8; §7 copy/naming → Task 8; Theming → Task 5 (ManualKeyWindow re-theme) + Global Constraints + smoke section; Testing → Tasks 1-3 TDD + Task 9 smoke; Compatibility → Task 1 (missing-slot default, no migration).
- **Type consistency verified:** `AccountOrigin` (1) ← 3,4,6,7; `AuthFlow`/`ReauthRouting` (3) ← 6; `SignInPrompt.SecondaryLabel` (3) ← 6; coordinator signatures (4) ← 6,7; `ManualKeyWindow.ForReauth` (5) ← 4; `UsageHistory.Delete/Rename` (2) ← 8; `Paths.WebView2FetchDir` (8, self-contained).
- **Ordering note:** Task 4 references `ManualKeyWindow.ForReauth` before Task 5 creates it — Task 4 therefore commits without a build check and Task 5's build validates both. Executors running strictly one-task-per-subagent should hand Tasks 4+5 to the same worker or accept the deferred build gate.
