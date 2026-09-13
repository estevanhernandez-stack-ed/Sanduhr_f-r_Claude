# Sanduhr für Claude

Desktop widget that turns a claude.ai subscription's usage into pacing: burn-rate projection, pace
markers, local Claude Code token burn, a usage vault, themes. Three apps share this repo:

- `windows-dotnet/` is the shipping Windows app (.NET 10 / WPF). `src/Sanduhr.App` is the WPF shell,
  `src/Sanduhr.Core` the pure, xUnit-covered logic, `src/Sanduhr.Mcp` the `sanduhr-mcp` read-only
  stdio MCP server over the snapshot seam. Tests under `tests/`. Build scripts under `scripts/`.
- The retired Python apps (`windows/` PySide6 build, root `sanduhr.py` tkinter v1) were removed on
  2026-09-13 and live at tag `legacy/python-v2.3.0`. Nothing current depends on them.
- `mac/` is the macOS SwiftUI app (Sparkle updates, Homebrew tap, its own `mac/README.md`).

## Tenancy

Personal 626 Labs estate. Dashboard project: **Sanduhr für Claude (Windows)**. Log decisions there;
conventional commits.

## Gotchas

- `windows-dotnet/Sanduhr.slnx` does not include the MCP projects. `dotnet build windows-dotnet` and
  `dotnet test windows-dotnet` cover App + Core + `Sanduhr.Tests` only. Test the MCP server by path:
  `dotnet test windows-dotnet/tests/Sanduhr.Mcp.Tests/Sanduhr.Mcp.Tests.csproj`.
- The MCP process is network-free by contract. `Sanduhr.Mcp.csproj` has no ProjectReference or
  PackageReference; it source-links four Core files, and `TrustBoundaryTests` pins that allowlist.
  `publish_usage` hands the upload to the widget through request/result files under
  `%APPDATA%\Sanduhr`. Widening the link list is a design decision that must break CI.
- The publish token lives in Credential Manager slot `publish:token` (service `com.626labs.sanduhr`);
  a legacy `626labs:agentKey` entry migrates on first use. It is never in `settings.json` or a log.
- `%APPDATA%\Sanduhr\snapshot.json` is written only by 3.4.0+ builds, stamped with `writer_version`.
  A 3.3.0 install never writes it: a stale snapshot carrying `writer_version` `1.0.0` came from a dev
  build, not from the installed widget.
- CRLF working tree, LF index (`core.autocrlf=true`). Never `sed -i` a tracked file; use the Edit
  tool or `perl -i`.
- Velopack packId `626Labs.Sanduhr` is frozen: the install tree must stay disjoint from
  `%LOCALAPPDATA%\Sanduhr` (the vault) or a fresh install deletes it. Store versions need a `.0`
  fourth component. Quit `Sanduhr.exe` before `dotnet publish`.

## Non-standard conventions

- Releases run `docs/release-runbook.md` phases 1 through 8: version lockstep (csproj trio +
  manifest), reviewer letter + release notes, Store MSIX, Velopack, tag (drafts the GitHub
  Release), finalize the release, Partner Center, announce.
- Store copy source of truth is `../store-listing-console/apps/sanduhr/copy/` (listing copy,
  `voice.md`, `whats-new-<v>.md`), written to the Store through that console's API. `docs/store/`
  here is the archive of what shipped. Reviewer letters (`docs/store/reviewer-letter-<v>.md`) stay
  here because `notesForCertification` never goes through the API; they are pasted in Partner Center.
- Every submission passes the 10.1.4.4 acceptance tests from the Python build's two rejections:
  (a) trademark disclaimer on every surface naming Claude, (b) the unique-value features live,
  (c) tool-strip navigation with accessible names and light-mode-legible dialogs.

## Pointers

- `docs/release-runbook.md`, `docs/ms-store-submission-playbook.md`, `docs/PRIVACY.md`, `docs/BACKLOG.md`
- `windows-dotnet/README.md` for the architecture and the WebView2 transport story
- Store Listing Console: `C:\Users\estev\Projects\store-listing-console` (`apps/sanduhr/`)

<!-- gitnexus:start -->

## GitNexus index

Indexed as **Sanduhr** — 3510 symbols, 14429 relationships, 281 execution flows. Resources are under `gitnexus://repo/Sanduhr/`.
How to use the tools is in the global `gitnexus` skill. If a tool reports the
index stale, run `npx gitnexus analyze`.

<!-- gitnexus:end -->
