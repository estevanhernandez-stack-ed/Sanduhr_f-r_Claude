# Builder Profile

Autonomous `/onboard` run, 2026-10-11, cycle: Windows parity for Sanduhr für Claude. Every value comes from the builder's unified profile (`$CLAUDE_CONFIG_DIR/profiles/builder.json`) or the cycle brief agreed with Este; defaults are marked.

## Who They Are
Este, 626 Labs (Fort Worth). Ships Sanduhr on two platforms: the WPF Windows app in the Microsoft Store and through Velopack, the SwiftUI Mac app through Sparkle and Homebrew. Six Store apps live, all released through the Store Listing Console loop.

## Technical Experience
Experienced. C#, TypeScript, Python, Swift, PowerShell and more; .NET 10, WPF, WinUI 3, MSIX, Velopack, SwiftUI. Deep AI-agent experience: runs Claude Code as an autonomous build system with subagent workers and several parallel sessions that hand off to each other.

## Mode
Builder. Persona: Architect. Tone terse and direct, pacing brisk. Autonomy: fully-autonomous (opted in for this cycle explicitly; the orchestrator runs the chain and Este cuts releases).

## Project Goals
The Windows app (3.4.3, in Store certification) catches up to the Mac (2.16.0 on main at onboard; the brief names 2.4 to 2.14 as the gap). Success: each of seven stages ends in a releasable increment, merged to main as its own PR, so Este can cut a Windows release whenever a stage lands. This run: scope, PRD, spec and checklist for all seven stages, then Stage 1 built and merged.

## Design Direction
Match the Mac's behavior 1:1 and the Windows app's existing look: the glass widget, Mica, themed dialogs, the tool strip. The Mac sources under `mac/Sources/Sanduhr/` are the behavioral reference; the shared specs (`docs/welcome-tour-spec.md`, `docs/settings-v2-spec.md`, `docs/mods-and-config-spec.md`) are the design reference. Every Store-visible surface keeps the 10.1.4.4 bar: the trademark disclaimer on every surface naming Claude, the unique-value features live, tool-strip navigation with accessible names, light-mode-legible dialogs. Taste calls get rendered options before a ruling (Este rules from what he sees), so visual work notes what was verified and what still needs his eyes.

## Prior SDD Experience
Extensive: 26 Cart cycles started, 21 completed. Zero deepening rounds when the vision is formed; this cycle's vision is formed by the port plan and the shared specs. The Desk stages (S5 to S7) may earn one deepening pass before their build.

## Architecture Docs
- Repo keystone `../CLAUDE.md` (gotchas: CRLF working tree, never `sed -i` a tracked file; Velopack packId frozen; MCP network-free with a source-link allowlist pinned by `TrustBoundaryTests`; MSIX AppData virtualization opt-out pinned by `PackageManifestTests`).
- `README.md` in this folder (Core/App split, WebView2 transport, sign-in capture).
- `../docs/windows-port-plan.md` (W1 to W15), `../docs/welcome-tour-spec.md`, `../docs/settings-v2-spec.md`, `../docs/mods-and-config-spec.md`, `../docs/BACKLOG.md`.
- Conventions: pure logic in `Sanduhr.Core` with xUnit coverage, WPF in `Sanduhr.App`, conventional commits.

## Project Origin
Extending an existing repo: the `windows-dotnet/` root of the Sanduhr monorepo, in the worktree `.claude/worktrees/windows-parity`. Mac work lands on main concurrently, so every stage branches from the latest `origin/main`.

## Deployment Target
Microsoft Store (MSIX, Partner Center) and GitHub Releases through Velopack. Out of scope for this cycle: Este cuts every release; no version bumps, tags, packages, Partner Center or Store copy from the agent.
