# Sanduhr für Claude

> **Persona:** This repo inherits The Architect from the seat's global CLAUDE.md. Project context only below.

Desktop widget that turns a claude.ai subscription's usage into pacing: burn-rate projection, pace
markers, local Claude Code token burn, a usage vault, themes, and on the Mac a desktop layer (the
Desk) with meetings, watchers, now playing and a notch island. Three surfaces share this repo and
ship separately:

- `mac/` is the macOS SwiftUI app: Sparkle updates from `docs/appcast.xml`, a Homebrew cask, and
  `mac/integrations/` (the Python MCP server, statusline and mods Claude Code talks to).
- `windows-dotnet/` is the Windows app (.NET 10 / WPF, Store MSIX and Velopack). **A Windows agent
  owns it.** From a Mac, read it for parity and hand changes over; never edit it here.
- `vscode/` is Sanduhr Time, a VS Code extension with its own CHANGELOG and release.

The retired Python apps live at tag `legacy/python-v2.3.0`; nothing current depends on them.

## Tenancy

Personal 626 Labs estate, public repo. Company styling is `626Labs LLC`. Nothing committed names a
home path on this Mac, an employer or a person; screenshots and fixtures show made-up data only.
Dashboard project: **Sanduhr für Claude (Windows)**. Log decisions there; when the dashboard MCP is
not connected, append to `docs/decisions/_pending-dashboard-log.md` and let the next session flush.

## Gotchas, every surface

- CRLF working tree, LF index (`core.autocrlf=true`). Never `sed -i` a tracked file; use the Edit
  tool or a Python rewrite.
- `docs/` is also the GitHub Pages site: `docs/appcast.xml` is the live Sparkle feed. Only the
  release workflow's `release/mac-<v>` PR changes it; a hand edit ships an update to every Mac.
- `docs/cart-runs/` holds tracked run folders. Clearing "untracked run copies" with `rm -rf` on it
  deleted a shipped run once; use `git clean` on the specific untracked paths.

## Gotchas, Mac

- **Version lockstep is `mac/Info.plist` alone** (`CFBundleShortVersionString` + `CFBundleVersion`,
  build number +1 every release). A minor also needs a card in
  `mac/Sources/Sanduhr/Models/WhatsNewCards.swift`, and `WhatsNewTests` pins the table's count, the
  ids per version and each card's destination, so the card lands with its test lines or CI is red.
- **Keep the CHANGELOG heading `## Unreleased (mac)` in the ship commit.** `mac-release.yml` drafts
  the GitHub Release notes from that section; retitling it to the version first ships a draft with
  empty notes. Rename after publish.
- **The release gate is a human approval** on the `mac-release` environment. Never approve it
  yourself unless asked. Publishing the draft creates the tag, and the appcast and cask point at
  `releases/download/v<v>-mac/`, so: publish, confirm the release's tag is `v<v>-mac` and not a
  placeholder, then merge the appcast PR, then update `homebrew-tap` from `docs/distribution/sanduhr.rb`
  and check the DMG's `shasum -a 256` against the cask. `docs/mac-release.md` has the order,
  rollback and a bad update in the wild.
- **No signing keys on this Mac.** CI signs and notarizes; `mac/release.sh` is the fallback for a
  Mac that has the Keychain material. Apple's staple lookup drops connections; `notarize.sh` retries
  both staple and validate, so a run that fails there twice is Apple, not the build.
- **Dev builds run signed out, against the file store.** The Keychain partition list refuses a
  self-signed dev cert, so a dev build cannot read the release app's session key, and a signed-out
  build blanks the live meters in Claude Code sessions. Before testing: quit `/Applications/Sanduhr.app`,
  back up `~/Library/Application Support/Sanduhr`; never sign in or remove an account in a dev
  build; restore the folder and relaunch the release app after.
- `smoke/smoke snap` needs the debug hooks on first (`smoke/smoke enable`), and its `state.yaml`
  carries real usage numbers: read it, never paste it.
- CI runs `swift test` on macos-15 with no separate build. A SwiftUI body the type-checker
  times out on fails only there; split the body.
- `mac/integrations/band-fixtures/` is generated from the meters mod by its `generate.ts`; CI runs
  it with `--check`, so a band change without a regenerate fails there.

## Gotchas, Windows (the Windows agent's, kept here until `windows-dotnet/` has its own keystone)

- The Store build's AppData is virtualized. `runFullTrust` does not stop MSIX write virtualization,
  and Microsoft denied `rescap:unvirtualizedResources` (2026-10-20), so the manifest declares
  `runFullTrust` only (`PackageManifestTests` pins it; MinVersion 10.0.17763.0). A new file the
  package writes under `%APPDATA%` goes to the package `LocalCache`, which shadows a real file of
  the same name on read. Anything a process outside the package touches therefore lives in
  `%USERPROFILE%\.sanduhr` (`Paths.ExternalDir`), never AppData. A new file for Claude Code, the
  MCP or the statusline goes there, with its own name in `Reset`.
- `~\.sanduhr` is shared ground: other estate tools keep their own files there (`time\`). Sanduhr
  creates, reads and deletes only its own names in it, never the folder wholesale; Reset removes
  the folder only when it is empty.
- The MCP process is network-free by contract: `Sanduhr.Mcp.csproj` has no ProjectReference or
  PackageReference, source-links an allowlist of pure Core files (the usage readers, theme lint, the
  vault readers and, since 3.6.0, the Desk message and watcher validators), and `TrustBoundaryTests`
  pins the allowlist and reads the newest files for IO, network and process names.
  `publish_usage` hands uploads to the widget through request/result files under `%USERPROFILE%\.sanduhr`.
  Widening the link list is a design decision that must break CI.
- The publish token lives in Credential Manager slot `publish:token` (service `com.626labs.sanduhr`);
  a legacy `626labs:agentKey` entry migrates on first use. Never in `settings.json` or a log.
- `%USERPROFILE%\.sanduhr\snapshot.json` is written only by 3.4.0+ builds, stamped `writer_version`; a
  stale one stamped `1.0.0` came from a dev build.
- Velopack packId `626Labs.Sanduhr` is frozen and the install tree must stay disjoint from
  `%LOCALAPPDATA%\Sanduhr` (the vault) or a fresh install deletes it. Store versions need a `.0`
  fourth component. Quit `Sanduhr.exe` before `dotnet publish`.
- Store copy's source of truth is the Store Listing Console (`../store-listing-console/apps/sanduhr/copy/`);
  `docs/store/` is the archive. Reviewer letters stay here and are pasted into Partner Center. Every
  submission passes the 10.1.4.4 tests: trademark disclaimer wherever Claude is named, the
  unique-value features live, tool-strip navigation with accessible names and light-mode dialogs.

## Commands

```
cd mac && ./build.sh --debug          # debug app at mac/Sanduhr.app, then open it
./test.sh [--filter Name]             # swift-testing suites
ruby smoke/selftest.rb                # the smoke harness against its fixtures
python3 -m pytest -q integrations/tests
gh workflow run mac-release.yml --ref main -f version=X.Y.Z   # after the ship commit is on main
```

## Pointers

- Mac architecture and every feature's rules: `mac/README.md`; the smoke harness: `mac/smoke/README.md`
- Mac release, rollback, bad update: `docs/mac-release.md`. Windows releases run `docs/release-runbook.md`
  phases 1 to 8 (version lockstep is the csproj trio + manifest there), with `docs/ms-store-submission-playbook.md`
- Windows architecture and the WebView2 transport: `windows-dotnet/README.md`
- Specs under `docs/`: `spec.md` is the Windows technical spec, and its Mac section points at the per-item
  Mac specs. `docs/BACKLOG.md`, `docs/PRIVACY.md`, ADRs in `docs/adr.md`
- Cart runs and the ux-polish scans: `docs/cart-runs/`, `docs/vibe-iterate/`

<!-- gitnexus:start -->

## GitNexus index

Indexed as **Sanduhr** — 3510 symbols, 14429 relationships, 281 execution flows. Resources are under `gitnexus://repo/Sanduhr/`.
How to use the tools is in the global `gitnexus` skill. If a tool reports the
index stale, run `npx gitnexus analyze`.

<!-- gitnexus:end -->
