# Decisions waiting for the dashboard

The 626 dashboard MCP was down (connection timeout) when these were made. The next session with it connected logs each
one with `manage_decisions` (action `log`) to the project named, then deletes its entry here.

## 2026-11-13 · Sanduhr für Claude (Windows) · FX7zznEFlzArQ3CTY7RU

### Shipped Sanduhr Time 0.3.1: Codex's compressed rollouts are read (#495)

- **Category:** deployment
- **Decision:** Sanduhr Time 0.3.1 (`v0.3.1-time`) reads Codex's `.jsonl.zst` rollouts. `READER_VERSION` was bumped to
  0.3.1 with the package version.
- **Rationale:** The fix is PR #508 and the bump PR #509 (main at 423ff4de). The annotated tag ran `vscode-release.yml`
  (run 43434029489, success), and the release is published with `sanduhr-time-0.3.1.vsix` (33829 bytes) and its notes
  taken from the CHANGELOG. `READER_VERSION` is hard-coded in `vscode/src/extension.ts`, so a change to how files are read
  needs it bumped by hand.
- **Next:** install 0.3.1 on Neb and confirm older Codex days fill in from its 65 compressed rollouts.
- **Files:** `vscode/package.json`, `vscode/package-lock.json`, `vscode/CHANGELOG.md`, `vscode/src/extension.ts`
