# Sanduhr — .NET 10/WPF Rebuild — Technical Spec

Pairs with `docs/scope.md` + `docs/prd.md`. Architecture mirrors RORORO (`ROROROblox`). SDK present: .NET 10.0.202.

## Solution layout (`windows-dotnet/`)

```
windows-dotnet/
  Sanduhr.slnx
  src/
    Sanduhr.Core/           # net10.0 — PURE logic, no WPF, unit-testable
    Sanduhr.App/            # net10.0-windows — WPF shell (+ Package.appxmanifest)
  tests/
    Sanduhr.Tests/          # net10.0-windows — xUnit (ports the 286 Python tests)
```

MSIX manifest lives in `Sanduhr.App/Package.appxmanifest` (RORORO's approach — built via scripts/makeappx, not a separate wapproj), reusing the existing identity `626LabsLLC.SanduhrfrClaude` / Publisher `CN=177BCE59-...` and adding the `windows.startupTask` extension for auto-start.

## Stack / packages

- **TFM:** `net10.0-windows` (App/Tests), `net10.0` (Core). `Nullable` + `ImplicitUsings` on. `UseWPF` on App.
- **App packages** (mirror RORORO): `WPF-UI` (Fluent + Mica), `Microsoft.Web.WebView2`, `CommunityToolkit.Mvvm` (MVVM), `Hardcodet.NotifyIcon.Wpf` (tray), `Microsoft.Windows.CsWin32` (Win32/DWM Mica source-gen), `Velopack` (auto-update), `Serilog` + sinks. Custom `Program` startup so `VelopackApp.Build().Run()` precedes WPF.
- **Core packages:** none heavy — `System.Text.Json`; HTTP via `HttpClient` (+ a Cloudflare-aware handler to replace cloudscraper; UA + `Sec-Fetch-*` headers, CF-challenge detection → typed error).
- **Tests:** `xunit`, `Microsoft.NET.Test.Sdk`.

## Module map (Python → C#)

| Python (`windows/src/sanduhr/`) | C# home | Notes |
|---|---|---|
| `api.py` | `Core/ClaudeApiClient.cs` | HttpClient + CF handler; org discovery captures `rate_limit_tier`/`billing_type`/`capabilities`; `_account` on usage dict |
| `fetcher.py` | `Core/UsageFetcher.cs` | async fetch + Routines synth + history append; raises typed errors (App marshals to UI thread) |
| `pacing.py` | `Core/Pacing.cs` | pure math — **first port (proves the pattern)** |
| `tiers.py` | `Core/TierModel.cs` + `App/Views/TierCard.xaml` | model in Core, render in App |
| `plan.py` | `Core/PlanLabel.cs` | tier-badge mapping (port PR #25's logic) |
| `history.py` | `Core/UsageHistory.cs` | same `%APPDATA%\Sanduhr\history.{account}.json` schema |
| `history_chart.py` | `App/Views/HistoryChart.xaml(.cs)` | WPF drawing |
| `cc_logs.py` | `Core/CcLogReader.cs` | local JSONL token-burn |
| `accounts.py` | `Core/AccountStore.cs` | Credential Manager slots (same names) |
| `credentials.py` | `Core/CredentialStore.cs` | DPAPI / Credential Manager |
| `widget.py` | `App/MainWindow.xaml(.cs)` + `App/ViewModels/WidgetViewModel.cs` | the floating panel |
| `settings_dialog.py` | `App/Views/SettingsWindow.xaml` + VM | tabs |
| `focus.py` | `App/Views/FocusTimer.xaml(.cs)` | cert-load-bearing |
| `game.py` | `App/Views/CooldownGame.xaml(.cs)` | cert-load-bearing |
| `themes.py` | `Core/ThemeModel.cs` + `App/Theming/` | 5 palettes + user JSON drop-ins; same `%APPDATA%\Sanduhr\themes\` |
| `sounds.py` | `App/Sounds.cs` | chimes (SoundPlayer/NAudio); `SANDUHR_SILENT_SOUNDS` honored |
| `startup.py` | `App/Startup.cs` | auto-start (port PR #26: HKCU Run + MSIX startupTask) |

**Core/App boundary:** Core has zero WPF/Win32-UI deps → fully unit-testable (the parity bar). App is views + viewmodels binding to Core.

## The new piece — embedded WebView2 login

`App/Views/SignInWindow.xaml(.cs)` (RORORO `CookieCaptureWindow` pattern):
1. Host a `WebView2` (`CoreWebView2` with an app-owned user-data folder under `%APPDATA%\Sanduhr\webview2\`).
2. Navigate `https://claude.ai/login`; user signs in normally (Google/email/passkey — all handled by the real Anthropic login).
3. On navigation to a signed-in URL, call `CoreWebView2.CookieManager.GetCookiesAsync("https://claude.ai")`, pull the `sessionKey` (and `cf_clearance` if present) cookie values.
4. Persist via `CredentialStore` (same Credential Manager slots), close the window, kick a fetch.
5. **Fallback:** the manual sessionKey paste (current Add-Account modal) stays for power users / edge cases.

No browser-store prying (ABE-dead); we own the cookie jar. Login window is its own WebView2 profile, isolated from the user's Chrome/Edge.

## Glass / Mica

WPF borderless `WindowStyle=None`, `AllowsTransparency`, top-most; Mica via WPF-UI `SystemBackdrop` (or CsWin32 `DwmExtendFrameIntoClientArea` + `DWMWA_SYSTEMBACKDROP_TYPE`) with solid-color fallback < Win11 22H2. Pin/float toggle, frame persistence (save on move only — port the Python gotcha), taskbar-icon binding.

## Release

Velopack (GitHub `Setup.exe` + delta) + MSIX (Store) from one version. Follow RORORO's freshened `docs/store/release-playbook.md` (4th-version-component `.0`; Partner Center; reviewer letter; draft-release; listing "What's new") merged with Sanduhr's `docs/ms-store-submission-playbook.md`. Likely **v3.0.0** to signal the platform shift. GitNexus indexes the new tree.

## Test strategy

Port the 286 Python test intents to xUnit, Core-first (pacing, plan, tiers, history, cc-logs, accounts, api parsing — highest-fidelity 1:1 ports), then VM-level for App logic. `Pacing.cs` + `PacingTests.cs` are the first vertical slice that proves the port + harness.

## Build order (feeds `checklist`)

1. Scaffold solution (Core/App/Tests, slnx, package refs) → compiling skeleton.
2. Port pure Core (pacing → plan → tiers → history → cc-logs → accounts/credentials → api/fetcher) with tests as we go.
3. App shell: glass widget + tier cards binding to Core.
4. Embedded WebView2 login + manual fallback.
5. Settings, multi-account, themes/sounds, history charts, focus timer, game, tier badge, auto-start.
6. MSIX manifest + Velopack + release per the playbook.
