# Sanduhr für Claude — Windows (.NET 10 / WPF)

The Windows build of Sanduhr, rebuilt from the original Python/PySide6 app onto
**.NET 10 / WPF** at full behavioral parity. A floating, glass desktop widget that
shows a signed-in Claude.ai user their own subscription usage: five-hour and
seven-day limits, burn-rate projection, pacing markers, 30-day history, a
deep-work focus hourglass, a cooldown game, themes, and Win11 Mica.

This tree (`windows-dotnet/`) is the **next Microsoft Store submission** (v3.0.0.0).
It lives beside the legacy `windows/` (Python) and `mac/` (Swift) builds in the
same repo and reuses the same Store identity, so an existing user updates in place.

> The repo-root `README.md` documents the cross-platform/Python project. This file
> is scoped to the .NET Windows rebuild only.

---

## Architecture

Two assemblies, split on a hard testability boundary:

- **`src/Sanduhr.Core/`** (`net10.0`) — pure logic, zero WPF/Win32-UI dependencies.
  Pacing math, tier model, plan-badge mapping, usage history, account/credential
  storage, CC-log reader, API parsing, the sign-in decision logic. This is the
  parity bar: it ports the shipped Python app's behavior 1:1 and is covered by the
  full xUnit suite.
- **`src/Sanduhr.App/`** (`net10.0-windows`) — the WPF shell. Views + view models
  binding to Core, the tray icon, Mica interop, the WebView2 surfaces, packaging
  manifest. Custom `Program.Main` runs `VelopackApp.Build().Run()` before WPF so
  auto-update hooks fire first.

### The WebView2 fetch transport (the Cloudflare-beating pivot)

The original plan was a `HttpClient` + Cloudflare-aware handler (`Core/ClaudeApiClient.cs`,
Chrome UA + `Sec-Fetch-*` headers). That class is retained as the parity-tested
reference seam, but it is **not** the live transport: a raw `HttpClient` cannot
clear claude.ai's Cloudflare even with a valid `cf_clearance` and a matched Chrome
UA, because Cloudflare binds its challenge to the browser's TLS/JA3 fingerprint and
the short-lived `__cf_bm` cookie.

The production transport is **`App/Services/WebView2ApiClient.cs`**: a hidden,
off-screen, taskbar-less WebView2 host that injects the `sessionKey` (+ `cf_clearance`)
into its own cookie jar, navigates `https://claude.ai` (the real browser solves
Cloudflare natively), and runs the API requests from inside the page context via
in-page `fetch()`, correlating replies back over `WebMessageReceived`. It is a
drop-in for `ClaudeApiClient` — same `(sessionKey, cfClearance)` constructor, same
`IClaudeApiClient` surface, same shared `ClaudeApiParsing` and typed errors — so
`UsageFetcher` is untouched. `WidgetViewModel.RebuildFetcher()` constructs it.

### The embedded "Sign in to Claude" capture

`App/Views/SignInWindow.xaml(.cs)` (the RORORO `CookieCaptureWindow` pattern) hosts
a WebView2 against an app-owned, isolated user-data folder under
`%APPDATA%\Sanduhr\webview2\`. It navigates `https://claude.ai/login`; the user
signs in on Anthropic's real page; once the `sessionKey` cookie appears on the
claude.ai origin it is read straight off `CoreWebView2.CookieManager` and persisted
to the Windows Credential Manager. No browser-store prying — the app owns its own
cookie jar. Manual sessionKey paste (`ManualKeyWindow`) is retained as the
power-user fallback.

---

## Build / run / test

Requires the **.NET 10 SDK** (the repo was built against 10.0.202). All commands
run from the repo root.

```powershell
# Build everything (Core + App + Tests)
dotnet build windows-dotnet/Sanduhr.slnx -c Debug

# Run the widget
dotnet run --project windows-dotnet/src/Sanduhr.App/Sanduhr.App.csproj

# Run the test suite (297 tests, the ported Python behavioral spec)
dotnet test windows-dotnet/tests/Sanduhr.Tests/Sanduhr.Tests.csproj -c Debug
```

The App targets `net10.0-windows` with `UseWPF`, so it builds and runs on Windows
only. Core and Tests are `net10.0`. The embedded sign-in and the usage transport
need the **WebView2 runtime** installed (present on current Windows 11; the app
degrades to the manual-paste fallback when it is missing).

---

## Where credentials live

The user's claude.ai session credential **never** touches the repo, a config file,
or a log. It is stored only in the **Windows Credential Manager** (Generic
credential, DPAPI-protected, per-user/per-machine).

- Service: `com.626labs.sanduhr`
- Per-account slots: `sessionKey:{label}` and `cf_clearance:{label}`
- Registry slots: `accounts:list`, `accounts:active`
- On-disk target name (the keyring/WinVault format the Python build also wrote):
  **`{slot}@com.626labs.sanduhr`** — e.g. `sessionKey:Personal@com.626labs.sanduhr`

The diagnostic logs (`signin-debug.log`, `fetch-debug.log` under `%APPDATA%\Sanduhr\`)
record presence booleans, host names, HTTP status, and cookie counts only — never
a cookie value.

---

## Sign-in notes

- The login runs on Anthropic's **real** `claude.ai/login` page inside WebView2.
  Sanduhr never sees the password; it only reads back the resulting session cookie
  from its own jar.
- **Google OAuth is unreliable inside an embedded WebView2** — Google routinely
  refuses embedded user-agents (the `disallowed_useragent` block). The reliable
  path is **email + login-code** (or passkey). Use that when the Google button
  dead-ends. The sign-in window already presents a clean desktop-Chrome UA as a
  mitigation, but Google may still block.
- Cookies are read **only from the `claude.ai` origin** (`https://claude.ai`); the
  capture never fires on OAuth-provider, analytics, or captcha hosts.

---

## Data compatibility (zero migration)

The .NET build reads and writes the **same** `%APPDATA%\Sanduhr\` files
(`settings.json`, `history.{account}.json`, `themes/`, `sounds/`) and the **same**
Windows Credential Manager slots as the shipped Python build. An existing user who
updates keeps their accounts, history, and settings untouched — schemas match the
Python app byte-for-byte (verified by the data-compat tests in the suite).

---

## Tech stack

| Concern | Choice |
|---|---|
| Runtime / language | .NET 10, C#, WPF (`net10.0-windows`) |
| MVVM | `CommunityToolkit.Mvvm` |
| Fluent + Mica | `WPF-UI` (+ CsWin32 `DwmExtendFrameIntoClientArea` / `DWMWA_SYSTEMBACKDROP_TYPE`) |
| Embedded login + usage transport | `Microsoft.Web.WebView2` |
| Tray icon | `Hardcodet.NotifyIcon.Wpf` |
| Win32 / DWM source-gen | `Microsoft.Windows.CsWin32` |
| Auto-update (GitHub channel) | `Velopack` |
| HTTP (reference transport) | `HttpClient` + a Cloudflare-aware `DelegatingHandler` |
| Credentials | Windows Credential Manager via `advapi32` P/Invoke (DPAPI at rest) |
| Tests | `xUnit` + `Microsoft.NET.Test.Sdk` (297 tests) |

Core carries no heavy dependencies (`System.Text.Json` only). Diagnostics are
lightweight file-append logs, not a logging framework.

---

## Packaging

Dual-channel from one version (Store MSIX + GitHub/Velopack), version `3.0.0.0`
(4th component must be `.0` for the Store). The full per-release loop —
version bump, reviewer letter, MSIX build, Velopack build, tag, GitHub release,
Partner Center submission, and the 10.1.4.4 acceptance gates — lives in
[`docs/release-runbook.md`](../docs/release-runbook.md). Build scripts are under
`windows-dotnet/scripts/` and emit to `windows-dotnet/dist/` (git-ignored).

---

## Screenshots

_TODO._ Add widget / focus-hourglass / sign-in screenshots here. Final Store tiles
and marketing graphics are generated through the **`626labs-design`** skill on the
`#0f182b` navy field with the 626 cyan→magenta accent — do not ship programmatic
placeholders (see `src/Sanduhr.App/Package/Logos/README.md`).

---

## Trademark

"Claude" and "claude.ai" are trademarks of Anthropic PBC, used nominatively to
describe integration. Sanduhr für Claude is an independent third-party tool, not
affiliated with, endorsed by, or sponsored by Anthropic PBC.
