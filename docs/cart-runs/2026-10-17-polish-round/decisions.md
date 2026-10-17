# Decisions: polish-round

## D1
- **Decision:** Scope is the Mac app, the MCP server, shared docs and the CI workflows for those; `windows-dotnet/` and `windows-*.yml` are untouched.
- **Why:** The owner handed Windows work to the Windows agent (2026-10-16) and set this scope when planning the workflow.
- **Alternative not taken:** Harden the Windows workflows too (vibe-sec flagged them); they would have been cheap but cross the line.
- **Item:** run-wide
- **How to reverse:** Hand the Windows findings in `docs/vibe-sec/audit-report.md` to the Windows agent.

## D2
- **Decision:** Item 14 (vscode vitest bump) stays on the list although `vscode/` is outside the stated scope.
- **Why:** It is the one real critical in the vibe-sec audit and the owner did not trim it when the list was shown.
- **Alternative not taken:** Drop it to "Not this run".
- **Item:** 14
- **How to reverse:** Leave its branch unmerged; `git branch -D cart/polish-round--14-vscode-vitest-bump`.

## D3
- **Decision:** Cart's session, friction and wins logs are not written for this run.
- **Why:** They resolve to `~/.claude/plugins/data/`, which on this Mac is the work seat's folder; the session's rule is never to write there from the personal seat.
- **Alternative not taken:** Write them under `~/.claude-personal/plugins/data/` (the plugin does not read that path).
- **Item:** run-wide
- **How to reverse:** Run `/vibe-cartographer:reconnect` once the plugin resolves `CLAUDE_CONFIG_DIR`.

## D4
- **Decision:** Every item commit ends with `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>` and `Claude-Session: https://claude.ai/code/session_01Jfz21BmnQKRbuExYPtRcWC`, staged by explicit paths, no `sed -i` on tracked files.
- **Why:** The session's git rules; the repo is public.
- **Alternative not taken:** None.
- **Item:** run-wide
- **How to reverse:** Not needed.

## D5
- **Decision:** Item 13 (`mac-release.yml`) is `hold`; item 12 (the other Mac/docs/vscode workflows) is `go`.
- **Why:** The release workflow creates releases and runs under the `mac-release` environment the owner approves; a permissions mistake there blocks a release. The CI workflows only build and test.
- **Alternative not taken:** Hold all workflow edits.
- **Item:** 12, 13
- **How to reverse:** Merge the hold branch after the morning walkthrough, or drop it.

## D6
- **Decision:** Each Swift item compiles the package from scratch in its own worktree (`./build.sh --debug && ./test.sh`).
- **Why:** unattended-run.md § Local branch layout: one worktree per item, never the builder's tree. SwiftPM's `.build` is per worktree.
- **Alternative not taken:** Share a build directory between worktrees (risks cross-item contamination).
- **Item:** 5–11
- **How to reverse:** Not needed.

## D7
- **Decision:** The builder's uncommitted `.gitignore` change and the untracked `docs/vibe-sec/`, `docs/vibe-test/` stay where they are; the run cut from `54bad55`.
- **Why:** unattended-run.md: the builder's tree is never touched.
- **Alternative not taken:** Commit them first.
- **Item:** run-wide
- **How to reverse:** Commit them in the morning alongside the run folder.

## D8
- **Decision:** Accepted item 7's strictness change: `ConfigPack` and `ModInventory` now refuse a `settings.json` / `.claude.json` with a trailing comma, where before Foundation's parser let it through by accident.
- **Why:** The app's design (checklist item 71, "strict JSON: a comment or trailing comma is unreadable") and Claude Code itself refuse the same file; SeatScan already showed such a file as unreadable, so the two sites were out of step with the page beside them. A new test pins that well-formed files read value-for-value as before.
- **Alternative not taken:** A `strict:` flag on the shared reader to keep the accidental tolerance at those two sites.
- **Item:** 7 (json-reader)
- **How to reverse:** Add `strict: false` handling to `JSONFiles.object` and pass it at the ConfigPack and ModInventory call sites (3 + 1 sites, commit c0c837a).

## D9
- **Decision:** Accepted item 8's routing: the kept-mod record files (`mods/kept.json`, `mods/installs.json`) now go through the shared owner-only write and so are gated by ResetGate (no write after Reset Sanduhr… begins); the credentials file and the installer's writes were deliberately kept on their own writers because they are reset steps.
- **Why:** The ResetGate doc says every writer of the support folder stops after Reset begins, except the reset's own steps. KeptMods was an accidental exception. Which files are written and their modes are unchanged; handoff files now never pass through a 0644 window.
- **Alternative not taken:** Route the credentials file too (it would leave a removed key in the file on a partial reset).
- **Item:** 8 (owner-only-write)
- **How to reverse:** Point `KeptMods.save` and `KeptModSwitch.saveReceipts` back at `Data.write(.atomic)` + chmod (commit c9f2c06).

## D10
- **Decision:** Accepted item 10's two semantics additions: a 60 s timeout on the `ditto` zip/unzip (`ModPack.dittoTimeout`, which had none) and SIGKILL one second after a timed-out `git` is terminated (ModMarketplace used to wait unbounded).
- **Why:** The shared runner takes the strictest existing shape (ClaudeCLI and ModCheck already terminated then killed); an unbounded wait on a hung git was a latent hang in the publish flow.
- **Alternative not taken:** A runner with per-site "terminate only" and "no timeout" modes, which would have kept two more code paths.
- **Item:** 10 (process-runner)
- **How to reverse:** Pass a larger timeout at the ditto sites, or add a `kill: false` option to `ProcessRunner.run` (commit 5a5a762).

## D11
- **Decision:** Item 14 bumped vitest to `^4.1.11` (resolved 4.1.12), not the 5.0.5 that `npm audit` suggested.
- **Why:** vitest 5 requires Node ^22.12 / ^24 / ≥26 and `vscode-ci.yml` runs Node 20; 4.1.11+ is patched for both advisories and tinypool is gone from the tree. 330 tests pass, 0 vulnerabilities.
- **Alternative not taken:** Bump CI to Node 22 and take vitest 5 (two changes in one item, one of them to CI).
- **Item:** 14 (vscode-vitest-bump)
- **How to reverse:** `npm install vitest@^5.0.5 --save-dev` in vscode/ after raising `node-version` in vscode-ci.yml.

## D12
- **Decision:** Close-out gates: the vibe-test gate cannot run (the repo is an unsupported stack for it; exit 2 by design) and the vibe-sec gate reads cached state the integration worktree does not have (exit 2). Substituted the repo's own checks on `cart/polish-round`: `mac/build.sh --debug`, `mac/test.sh`, `pytest mac/integrations/tests`, `vscode: npm ci && npm test`, plus vibe-sec's in-house secret scan over the integration worktree (the one vibe-sec detector that reads Swift and Python).
- **Why:** unattended-run.md § Environmental blockers: gate exit 2 is a tool error, not a finding; fall back to local checks and log it.
- **Alternative not taken:** Skip the gates.
- **Item:** close-out
- **How to reverse:** Run `/vibe-sec:audit` and `/vibe-test:audit` on the branch after merge; their findings stand on their own.
