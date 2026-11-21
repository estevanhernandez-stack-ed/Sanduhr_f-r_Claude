# Run Report: mac-agent-sources-2-46

13 merged, 1 held, 0 parked, 0 stopped. **Update 2026-11-20 evening:** the owner approved item 13 ("13 goes in").
It is merged into the integration branch as `120f5f71`. Checks after the merge: `./test.sh` passed 3662 tests in
405 suites, the selftest 1090 checks, pytest 372 passed with 220 subtests, and `./build.sh --debug` built.

## Ready to push
- **Integration branch:** `cart/mac-agent-sources-2-46` (base `4a205621`, `main`). Worktree:
  `~/.cwt/sanduhr-f-r-clau-mac-agent-sources-2-46/int`.
- **Commits:** 28 in total: the design doc, one commit or more per item, and the `merge: item NN` commits. Nothing
  touches `windows-dotnet/`.
  - `00bf6028` docs(mac): the agent sources design for 2.46.0 (D2)
  - `03071f68` feat(mac): the zstd decoder, built in
  - `63b9a52e` feat(mac): agent sources model, source colors and a Codex choice in setup
  - `9ce6e6ee` feat(mac): Codex limits from Codex's own rollouts
  - `99ad1943` feat(mac): Codex history, snapshot and alerts per source
  - `c2384a62` feat(mac): the two-tone bar
  - `7230cbb9` feat(mac): the widget with two sources
  - `4ac6aa7b` feat(mac): Sources in Settings, Usage and a menu bar that follows Primary
  - `1ccef62e`, `7b12e102` feat(mac): Desk meter pieces pick a source; propose_desk and get_desk carry it
  - `f9ab1ed9` feat(mac): the statusline and get_usage carry Codex
  - `4bf5ddeb` feat(mac): Codex's burn lands in the Vault as its own agent root
  - `89ea2d61` feat(mac): the Antigravity CLI's burn lands in the Vault
  - `63187077` feat(mac): Local burn shows every agent
  - `160e1fc1` feat(mac): the 2.46.0 rows (Info.plist 2.46.0 build 68, two What's New cards, CHANGELOG
    `## Unreleased (mac)`, README, PRIVACY, ADR-006 note, smoke section 80)
- **Checks on the integration branch (2026-11-20):**
  - `./test.sh`: 3653 tests in 405 suites passed. 2.45.0 had 3284.
  - `ruby smoke/selftest.rb`: 1090 checks passed.
  - `python3 -m pytest -q integrations/tests`: 358 passed, 220 subtests.
  - `./build.sh`: a universal release build, `x86_64 arm64`, so the vendored C decoder builds for both. The Swift 6
    actor warnings it prints come from `WidgetsModel.swift`, which this run didn't touch.
  - Every item was checked again on its own branch by the orchestrator, not just by its worker.
- **Close-out gates:** vibe-test and vibe-sec were not run; the repo's own checks above stand in for them. CI
  (`swift test` on macos-15, the Python tests, the build) runs on the PR, and it is the first type-check under
  macos-15.

To push and open the PR:
```bash
git -C ~/.cwt/sanduhr-f-r-clau-mac-agent-sources-2-46/int push -u origin cart/mac-agent-sources-2-46
gh pr create --base main --head cart/mac-agent-sources-2-46 --title "Mac 2.46.0: Codex limits and every agent's burn" --body-file <body>
```
After the PR merges: smoke section 80 on fixture homes, then
`gh workflow run mac-release.yml --ref main -f version=2.46.0`. Check the run's headSha before you publish.

## Waiting on you
- **Item 13, Claude may read agents' project totals, with consent.** Branch
  `cart/mac-agent-sources-2-46--13-mcp-agents-consent`, commit `6bed876f`, worktree `…/13`.
  - **Why it's held:** it adds an access-consent switch, which is permissions class.
  - **What to look at:**
    - The switch copy under each agent's row in Sources ("Let Claude read Codex's project totals", off by default).
    - `mcp-access.json` now always carries `agents: {codex, antigravity}`, so a Claude-only Mac's file gains this
      one object (D17).
    - The MCP lets exactly `codex` and `antigravity` past the hex vault-id check, through one allowlist.
  - **Checks on its branch:** both suites passed, and the debug build passed.
  - **If you approve:** merge it into the integration branch before pushing.
  - **If you decline:** drop three things from item 14:
    - the last bullet under `## Unreleased (mac)` in CHANGELOG
    - the third new Mac paragraph in `docs/PRIVACY.md`
    - smoke section 80 step 14
- **Smoke section 80** runs on fixture homes in a dev profile: signed out, file store, settings backed up first. A
  real Codex read waits for a Mac that has Codex (D1); the step for it is the last one.

## Parked
None.

## Stopped
None.

## Decisions to skim
See `decisions.md`, D1 to D17. The ones with stakes:
- **D8:** The Codex limits reader follows the Windows code over the spec. A candidate set by both modification time
  and session stamp, freshness per window, and a per-file cache: Windows found that Codex rewrites old rollouts.
- **D14:** Vault agent roots. Claude roots write exactly their 2.45.0 keys, and a test pins an old checkpoint
  re-encoding byte for byte. Erasing an agent's record stops it until its source is switched on again. Agent rows
  store project names only.
- **D16:** Agent rows join every account's Usage page, because they belong to no account. The ledger CSV always
  ends with an `agent` column, Claude-only included, as on Windows. Watch item: one unexplained failure of the
  loader test that adds Codex and Claude Code totals, seen by its worker once. It passed in 8 runs since.
- **D9:** Codex alerts are off by default (Windows: on) and never pulse the Desk.
- **D11:** The menu bar gets a tooltip only when another source is further out, so a Claude-only Mac shows none.
- **D12:** The Desk source is a `src=` field in the layout word, so every 2.45 layout round-trips unchanged.

## Follow-ups noticed
- A Codex sparkline from `history.@codex.json` (D10).
- Desk pulses for a Codex meter (D9, D12).
- A Codex chip in the statusline's Combine (D13).
- Trends and Skills don't yet include agents (D16).
- Set up Sanduhr's chat prompt doesn't mention the Desk `source` field (item 8).

## Your tree
Untouched: you're on `main`. Two untracked items were there at the start and are still untracked:
- `docs/superpowers/specs/2026-11-19-mac-agent-sources-design.md`. The same file is committed on the integration
  branch, so delete your copy before pulling the merged PR.
- This run folder. Commit it with the PR, as with 2.45.
