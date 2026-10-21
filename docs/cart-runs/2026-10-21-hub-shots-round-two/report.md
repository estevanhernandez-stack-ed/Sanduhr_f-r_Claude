# Run Report: hub-shots-round-two

6 merged, 0 held, 0 parked, 0 stopped.

## Ready to push
- **Integration branch:** `cart/hub-shots-round-two` (base `0681282`, tip `70a9412`), worktree `~/.cwt/sanduhr-f-r-clau-hub-shots-round-two/int`
- **Commits** (each item merged with a `merge: item NN <slug>` commit):
`  - `70a9412 docs(screenshots): keep the live Export Config and Compare shots; only Accounts is new (#292)
`  - `baca3cb docs(screenshots): Export Config, Compare on import and Accounts for the hub page (#292)
`  - `abd26f4 feat(mac): the export and import samples and demo Accounts show only invented data
`  - `fc2ced2 feat(mac): mods-config export and import-sample hooks open the two sheets for screenshots
`  - `910b98a fix(mac): the notchless Notch preview draws no empty bar with the pill off (#290)
`  - `8be8b84 perf(mac): the log walks sort paths by bytes, not Unicode comparison
`  - `2f094e9 docs(mac): the README names the Mods tabs as they read
`  - `ea27b61 fix(mac): the session-start card sits above the Mods tabs (#291)
`  - `4acf611 docs: BACKLOG #7 met in 2.31.0 on both Macs, and the pill's camera badge verified
`  - `14d24e2 test(mac): add 2.31.0 recheck pill shot from a no-notch Mac
- **Checks:** every code item ran `./build.sh --debug`, the full `./test.sh` and `ruby smoke/selftest.rb` in its worktree and again before its merge. Close-out on the integration tip: build ✓, 1,842 tests in 238 suites passed, selftest 1,008 checks passed, pytest 223 passed, and a secret and home-path grep over the run's diff found nothing. The vibe gates cannot run on this Mac (previous runs).
- **Seen live:** the export and import samples and the demo Accounts layout, on the integration build in demo mode; the notchless preview fix (item 5) only through its tests, since this Mac has a notch.

To push and open the PR:
```bash
git push -u origin cart/hub-shots-round-two
gh pr create --base main --head cart/hub-shots-round-two --title "fix(mac): #290 and #291, the last #292 shot, byte-order walks, and sample sheets for screenshots" --body-file docs/cart-runs/2026-10-21-hub-shots-round-two/report.md
```
Pushing waits for your say-so.

## Waiting on you
None.

## Parked
None.

## Stopped
None.

## Decisions to skim
Full ledger: `decisions.md`. Highest stakes:
- D7: #290 was half fixed in 2.30.0; the old Mac's check found the pill-off state still drew the empty bar, now item 5.
- D11: the first screenshot pass kept nothing, because Export Config, the import picker and the page behind it showed this Mac's real seat; item 6 added invented samples and a demo Accounts layout.
- D12: Export Config and Compare on import were already live on the hub from another session's capture; this run keeps those and adds only `settings-accounts.png`, the part of #292 still open.
- D8: the byte-order test pair in the list was wrong (Swift compares normalized scalars); the worker's decomposed pair is the real case.

## Found along the way, not fixed (for your call)
- #290 wants the old Mac to look once more at the pill-off preview after this ships.
- #292 can close once the hub places `settings-accounts.png`.
- No CHANGELOG entries: one "Unreleased (mac)" bullet for #290 and #291 at the next minor; the hooks and samples are harness-only.
- The demo Accounts detail pane leaves out the per-account Data section, which reads real Claude Code folders.

## Left untouched
Uncommitted files in your tree at run start: none.
