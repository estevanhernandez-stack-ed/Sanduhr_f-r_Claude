# Security review — Sanduhr .NET 10 / WPF rebuild (`windows-dotnet/`)

- **Date:** 2026-06-06
- **Scope:** the .NET rebuild tree only (`windows-dotnet/`), checklist item 12.
- **Sensitivity:** this app captures and stores a claude.ai `sessionKey` (+
  `cf_clearance`) via an embedded WebView2. The credential + cookie surface is the
  load-bearing risk, so it gets the deepest pass; the rest is OWASP-basics
  proportional to a single-user desktop app.
- **Verdict:** clean. No hardcoded secrets, no secret values in logs or git
  history, zero vulnerable packages, credential/cookie handling sound. One
  build-hygiene gap (`dist/` not git-ignored) fixed in this pass. No code changes
  to app behavior.

---

## 1. Secrets scan

**Source + config grepped:** `*.cs`, `*.xaml`, `*.csproj`, `*.appxmanifest`,
`*.json`, `*.ps1` under `windows-dotnet/` (excluding `obj/`). Patterns: `sk-ant-`,
`sessionKey=`/`cf_clearance=`/`password=`/`secret=`/`api_key=` with literal string
values, `Bearer` tokens, JWT (`eyJ…`), long base64 blobs, `AKIA…`, `ghp_…`,
connection strings, PEM headers.

**Result: no real secrets.** The only matches are obvious test fixtures:

| File | Value | Why it's fine |
|---|---|---|
| `tests/Sanduhr.Tests/ClaudeSignInTests.cs` | `"sk-ant-sid01-EXAMPLE"` | Placeholder asserting `ClaudeSignIn.Extract` pulls the right cookie. Not a live key. |
| `tests/Sanduhr.Tests/CredentialManagerInteropTests.cs` | `"sk-ant-test-äöü-🔑"` | Synthetic Unicode value proving the UTF-16-LE credential-blob roundtrip. Not a live key. |

The string literals `"sessionKey"` / `"cf_clearance"` throughout Core are **cookie
names and Credential Manager slot identifiers**, not values — documented as such in
`AccountStore` and `ClaudeSignIn`.

**Git history:** `git log --all -p -- windows-dotnet/` filtered for `sk-ant-`,
literal `sessionKey=`/`cf_clearance=`/`password=` assignments, and PEM headers
returns only the two test fixtures above. No secret was ever committed.

### Logs are value-free (confirmed by reading every `Log()` call site)

Two diagnostic logs are written under `%APPDATA%\Sanduhr\` (outside the repo):

- **`signin-debug.log`** (`SignInWindow.Log`): records the URL host, claude.ai
  cookie *count*, and `sessionKey`/`cf` *presence booleans*. On capture it writes
  the literal `"captured + persisted (no value logged)"`. Cookie values never
  appear.
- **`fetch-debug.log`** (`WebView2ApiClient.Log`): records nav status, per-fetch
  HTTP status, success/fail, tier counts, the resolved tier *name*, and — for the
  routines-null diagnostic — top-level JSON *keys only* (`TopLevelJsonKeys`, never
  values). The cookie-injection line logs `sessionKey present={bool} cf={bool}` —
  booleans, not values.

`UpdateChecker` and `WidgetViewModel` use `Debug.WriteLine` for version/theme
messages only. No framework logger (no Serilog) is wired, so there is no sink that
could capture request bodies.

---

## 2. `.gitignore`

**Before:** `windows-dotnet/.gitignore` covered `bin/`, `obj/`, `.vs/`, test
results, `[Rr]eleases/`, `*.msix`, `*.appx`, `AppPackages/`.

**Gap found:** the build scripts (`build-msix.ps1`, `build-velopack-release.ps1`)
emit to **`windows-dotnet/dist/`** and `dist/release/` — the latter holds
`sanduhr-win-Setup.exe`, `*.nupkg`, and `releases.win.json`, none of which matched
the existing patterns. A `dist/` that survived a build could have been committed.

**Fixed (this pass):** added to `windows-dotnet/.gitignore`:

- `dist/` — build-script output root
- `*.nupkg`, `*.appxbundle` — Velopack/MSIX artifacts
- `*.pfx`, `*.snk` — signing material
- `webview2/`, `webview2-fetch/` — defensive guard against a stray in-tree WebView2
  profile (production profiles live under `%APPDATA%\Sanduhr\`, outside the repo)

**Verified:** `git ls-files windows-dotnet/dist/` is empty (nothing tracked);
`git check-ignore` confirms `dist/foo.msix`, `dist/release/Setup.exe`, and
`bin/*.dll` are all ignored. No build artifact or WebView2 user-data can be
committed.

---

## 3. Dependency audit

`dotnet list package --vulnerable --include-transitive`, run against the solution
and each project (the `.slnx` form skips `Sanduhr.Core` because it has no
`PackageReference`, so it was audited directly):

| Project | Result |
|---|---|
| `Sanduhr.Core` | No vulnerable packages |
| `Sanduhr.App` | No vulnerable packages |
| `Sanduhr.Tests` | No vulnerable packages |

**No findings — nothing to bump or mitigate.** Direct App dependencies and their
pinned versions: `CommunityToolkit.Mvvm` 8.4.2, `Hardcodet.NotifyIcon.Wpf` 2.0.1,
`Microsoft.Web.WebView2` 1.0.3912.50, `Microsoft.Windows.CsWin32` 0.3.275,
`Velopack` 0.0.1298, `WPF-UI` 4.3.0. Tests: `xunit` 2.9.3,
`Microsoft.NET.Test.Sdk` 17.14.1, `coverlet.collector` 6.0.4. Core: none
(`System.Text.Json` via the SDK). All transitive packages clean as of the audit
date. Re-run before each release.

---

## 4. Input-validation / handling spot-check (credential + cookie surface)

- **Cookies persisted only from the real claude.ai origin.** `SignInWindow`
  reads via `CookieManager.GetCookiesAsync(ClaudeSignIn.CookieOrigin)` where
  `CookieOrigin = "https://claude.ai"`. The host gate (`ClaudeSignIn.IsClaudeUrl`)
  accepts only `claude.ai` / `*.claude.ai` over HTTPS, so OAuth-provider,
  analytics, and captcha hosts are never harvested. `Extract` pulls only the two
  named cookies (`sessionKey`, `cf_clearance`) and treats whitespace-only values
  as absent.
- **Credentials go only to the Credential Manager — never a plaintext file.**
  `AccountStore` writes through `ICredentialManager` →
  `WindowsCredentialManager` (advapi32 `CredWriteW`, Generic credential, DPAPI at
  rest). The v1 migration (`CredentialStore.MigrateFromV1`) reads a legacy
  plaintext `session_key` and routes it through `Save()` to the Credential Manager,
  then deletes the legacy file — it never writes the key back to disk. No code path
  writes a `sessionKey`/`cf_clearance` value to a file or log.
- **Account-label validation enforced.** `AccountStore.ValidateLabel` applies
  `^[A-Za-z0-9 _-]{1,32}$` on every `AddAccount` / `RenameAccount`, rejecting
  empty, over-long, or injection-shaped labels (the label is interpolated into slot
  names like `sessionKey:{label}`, so this also bounds the credential target name).
- **WebView2 transport anti-bleed (multi-account).** The fetch profile at
  `%APPDATA%\Sanduhr\webview2-fetch\` is shared across accounts, so
  `WebView2ApiClient.InjectAuthCookies` calls `CookieManager.DeleteAllCookies()`
  on every (re)build **before** injecting the new account's cookies and **before**
  navigating — a stale auth cookie cannot out-rank the injected one and leak the
  previous account's usage. The sign-in window gets the same isolation from a
  fresh per-capture GUID user-data folder (`WebView2UserDataDirectory`), swept
  after use.
- **Cookie attributes.** Injected cookies are set `IsSecure = true` (and the
  session cookie `IsHttpOnly = true`) on both `.claude.ai` and `claude.ai`, with a
  30-day expiry; the live browser re-solves Cloudflare on navigation regardless.
- **In-page fetch is injection-safe.** `BuildFetchScript` JSON-serializes the id,
  URL, and headers into the injected JS, so a crafted URL/header cannot break out
  of the string literal. Requests are GET-only against fixed `claude.ai` endpoints.
- **WebView2 isolation + UA.** Both WebView2 surfaces run in app-owned user-data
  folders isolated from the user's Chrome/Edge (no browser-store prying), under a
  clean desktop-Chrome UA shared with the API client so a `cf_clearance` captured
  at sign-in stays valid on replay.

---

## 5. Verification commands (re-runnable)

```powershell
# Secrets in working tree
#   (expect only the two test fixtures: sk-ant-sid01-EXAMPLE, sk-ant-test-…)
git grep -nE "sk-ant-|sessionKey *= *\"|cf_clearance *= *\"" -- windows-dotnet/

# Secrets in history
git log --all -p -- windows-dotnet/ | Select-String -Pattern "sk-ant-|-----BEGIN"

# Ignore rules
git check-ignore windows-dotnet/dist/x.msix windows-dotnet/src/Sanduhr.App/bin/x.dll

# Dependency audit (per project — slnx skips Core)
dotnet list windows-dotnet/Sanduhr.slnx package --vulnerable --include-transitive
dotnet list windows-dotnet/src/Sanduhr.Core/Sanduhr.Core.csproj package --vulnerable --include-transitive

# Build + tests
dotnet build windows-dotnet/Sanduhr.slnx -c Debug
dotnet test windows-dotnet/tests/Sanduhr.Tests/Sanduhr.Tests.csproj -c Debug
```

---

## Findings summary

| # | Finding | Severity | Action |
|---|---|---|---|
| 1 | No hardcoded secrets in source/config | — | None (clean) |
| 2 | No secret values in logs (presence/keys/counts only) | — | None (clean) |
| 3 | No secrets in git history | — | None (clean) |
| 4 | `dist/` + signing material not git-ignored | Low (hygiene) | **Fixed** — added to `.gitignore` |
| 5 | Zero vulnerable packages (all 3 projects) | — | None (re-run per release) |
| 6 | Credential/cookie handling sound (origin-gated, Credential-Manager-only, label-validated, anti-bleed) | — | None (clean) |

No finding required an app-behavior code change. The only change is the
`.gitignore` build-hygiene fix.
