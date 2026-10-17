# Run Brief: polish-round

## Ask
"let's put together a workflow, I want to use my vibe-toolkit to enhance this application, give it more depth and take a polishing round" — then, after the findings pass and the harvested list: "go" and "unattended".

## Source
- **Kind:** harvest (no argument): open GitHub issues, `docs/BACKLOG.md`, `docs/checklist.md`, and tonight's sibling findings (`/vibe-cartographer:tend` report in chat, `docs/vibe-sec/audit-report.md`, `.vibe-sec/state/findings.jsonl`, `docs/vibe-test/audit-2026-10-17.md`, `.vibe-doc/state.json`).
- **Location:** this repo, HEAD `54bad55` on `main`.

## Intent
Pay down the drift the findings pass surfaced in the Mac app, the MCP server and the shared docs: docs that point at things that no longer exist, a checklist that lags the code, six copy-paste patterns with a shared helper the copies skip, two dead-weight candidates, the CI hardening vibe-sec flagged, and the two documentation gaps vibe-doc rated required. Small, verifiable items; nothing that changes what the app does for a user.

## Done when
- Every `go` item is merged into `cart/polish-round` with its own check green (`./build.sh --debug` and `./test.sh` for Swift items; the named grep or parse for docs and workflow items; `npm test` for the vscode item).
- The `hold` item (release workflow) is built and checked on its branch, unmerged.
- `report.md` lists the push commands and the walkthrough for the hold branch.

## Out of scope
- `windows-dotnet/` and its workflows: the Windows agent owns them (owner, 2026-10-16).
- The seat/folder/root naming unification and the error-type shapes: 300+ call sites, a run of its own.
- The 331 `try?` sites and the four files over 1,100 lines: a refactor round, not a polish round.
- Issues #137 (lock-screen line), #175 (Sanduhr Time feature), #120 (screenshots need the app running with the owner), #144 (Windows Store), #119's Touch Bar spike; BACKLOG #3 (partly built as the Tour; needs a fresh look).
- Fixes to vibe-sec, vibe-test and vibe-doc themselves (their repos).
- Pushing, PRs, releases, Homebrew tap: the push step waits for the owner.

## Inferred
- Scope is the Mac app, the MCP server and shared docs (from: the owner's workflow plan earlier tonight and the memory `windows-work-belongs-to-windows-agent`).
- Item 14 (vscode vitest bump) stays on the list although `vscode/` is outside the stated scope, because the owner did not trim it when offered (from: the harvest confirmation).
- Commits carry the session's attribution lines and are staged by explicit paths (from: the session's git rules).
- Cart's own session, friction and wins logs resolve to `~/.claude/plugins/data/`, the work seat's folder on this Mac; they are not written in this run (from: the session's work-wall rule).
- Each item's Swift check builds the package from scratch in its worktree; a full compile per item is accepted as the price of isolation (from: unattended-run.md § Local branch layout).

## Mode
- **Mode:** Unattended
- **Triggered by:** builder confirmed the recommendation ("unattended"): late window (00:14 local) and 16 items.
