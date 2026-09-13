# Contributing to Sanduhr für Claude

## Getting started

```powershell
git clone https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude.git
cd Sanduhr_f-r_Claude/windows-dotnet
dotnet build
dotnet test tests/Sanduhr.Tests/Sanduhr.Tests.csproj
dotnet test tests/Sanduhr.Mcp.Tests/Sanduhr.Mcp.Tests.csproj
```

.NET 10 SDK on Windows 10/11. The app is `windows-dotnet/` (WPF); `mac/` is the macOS SwiftUI app
with its own README. The original Python widgets were retired and removed on 2026-09-13 and live at
tag `legacy/python-v2.3.0`.

## Where things live

- `src/Sanduhr.App` is the WPF shell: views, view models, settings, timers, the tray.
- `src/Sanduhr.Core` is the pure logic, xUnit-covered: API parsing, pacing, tiers, themes, the local
  Claude Code log reader, the usage vault, the snapshot writer, the usage publisher.
- `src/Sanduhr.Mcp` is the `sanduhr-mcp` stdio MCP server. It reads the snapshot seam only: no Core
  reference, no credentials, no network. `TrustBoundaryTests` pins that; keep it true.
- `Sanduhr.slnx` does not include the MCP projects; test them by project path (above).

## Conventions

- Put logic in Core with a test before wiring it into the App. UI-only changes still get a
  view-model test where one is possible.
- Conventional commits (`feat:`, `fix:`, `docs:`, `chore:`). One feature per PR.
- Update `CHANGELOG.md` under `## Unreleased`.
- Never store a secret outside Windows Credential Manager (`CredentialStore`, `PublishTokenStore`).
- Store-facing text (description, features, what's-new) lives in the Store Listing Console repo under
  `apps/sanduhr/copy/`; reviewer letters live here under `docs/store/`. See `docs/release-runbook.md`.

## Adding a theme

Themes are JSON (`ThemeModel` in Core). Add a built-in under the themes resources in
`src/Sanduhr.App`, or ship one as a user theme through Settings, Themes, Import. All color keys are
required; the app validates the file and names the missing key.

## Adding a tier

Tiers are discovered from the API response (`TierModel` in Core). A new usage key needs a label and a
family position in `TierModel`; add a `TierModelTests` case that registers it.
