# Builder Profile

Autonomous `/onboard` run, 2026-10-06. Every value comes from the builder's unified profile or the approved design (`docs/design.md`); defaults are marked.

## Who They Are
Este (Estevan Hernandez), 626 Labs, Fort Worth. A builder who architects and ships through AI agents: about 25 Vibe Cartographer cycles, six Microsoft Store apps, Claude Code plugins, desktop utilities. Sanduhr für Claude is one of the shipped apps. This project gives Sanduhr its coding-time half and retires WakaTime.

## Technical Experience
Experienced. TypeScript, C#, Python, JavaScript, Swift, PowerShell and more; .NET/WPF/WinUI, React, Firebase, VS Code extension work (the 626 Labs extension). Deep AI-agent experience: runs Claude Code as an autonomous build system with subagent delegation and several parallel sessions.

## Mode
Builder. Persona: Architect. Tone terse and direct, pacing brisk.

## Project Goals
Uninstall WakaTime without losing anything that was being looked at (total time, time per project), and get the AI-versus-me split right, which WakaTime's Claude parser currently misses. Grow into a Sanduhr feature: the usage spec makes the later handoff a swap.

## Design Direction
Dark by default, following the VS Code theme; clean, high-contrast panels with visible accents rather than flat surfaces. Trademark disclaimer on any surface naming Claude, as across Sanduhr. Taste calls (panel layout, chart style) get side-by-side options before a ruling.

## Prior SDD Experience
Extensive. A full superpowers brainstorming session already produced and approved `docs/design.md` (architecture, measurement rules, data and spec, masking, failure modes, testing). Scope, PRD and spec derive from it rather than restarting discovery.

## Architecture Docs
`docs/design.md` is the architecture source. Conventions follow the 626 Labs VS Code extension (TypeScript, webpack, Vitest with a `vscode` stub) and the Sanduhr repo keystone (`../CLAUDE.md`: conventional commits, CRLF working tree, edit tracked files with the Edit tool or `perl -i`).

## Project Origin
Extending an existing repo: a new `vscode/` root in the Sanduhr monorepo, plus changes in the 626 Labs monorepo (publisher and `manage_time`).

## Deployment Target
Hand-installed `.vsix` on each machine for this cycle (default — confirm on next run); VS Marketplace listing with the Sanduhr feature boost.
