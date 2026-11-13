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

### The desk-setup skill gets an eval suite, against a mock of the Windows tool contract

- **Category:** architecture
- **Decision:** `sanduhr-mods` branch `feat/desk-setup-evals` (46d33bc, not pushed): nine single-prompt `claude plugin eval`
  cases against a standalone mock of the `sanduhr` MCP server. The mock's `_tools.json` is the Windows server's real
  `tools/list`. The suite runs locally only, not in CI.
- **Rationale:** A probe showed that `evals/mocks/sanduhr/` serves a server the plugin doesn't declare. It runs as a
  "standalone" mock with tools named `mcp__sanduhr__*`, the way the skill calls them; the docs don't cover this. The
  Windows `Sanduhr.Mcp` (net10.0, no package refs) builds on the Mac into the scratchpad, so its `tools/list` was
  captured as is. Gotchas: `tool_used` with `max: 0` needs `min: 0`, or it asks for 1..0 calls. Prose LLM criteria
  flaked on the haiku judge, and numbered checklists held. A fixture that contradicts the prompt makes the model
  correctly refuse. Measured with 3 runs per arm: 0.98 with the plugin and a mean +0.30 over the no-plugin baseline,
  about $9 per full run.
- **Next:** not covered: the rejected-and-fix path (an agent mock proved unreliable) and the multi-round iterate/Apply
  flow. The skill targets Windows; Mac `propose_desk` refuses `monitor`, so Mac parity is a separate question.
- **Added later the same day (83659ac, PR #3):** a `prose-guard` regex grader in every case, the standard and conclusion
  patterns of `/vibe-thesis:guard` plus the voice rules' banned corporate phrases. Regex over an LLM judge because it is
  free and deterministic; it has to be kept in step with those two lists by hand.
