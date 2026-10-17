# Run Report: polish-round

16 merged, 0 held, 0 parked, 0 stopped. (Item 13 was held, then merged on the owner's say-so after the walkthrough.)

## Ready to push
- **Integration branch:** `cart/polish-round` (base `54bad55`, tip `2b8969f`), worktree `~/.cwt/sanduhr-f-r-clau-polish-round/int`
- **Commits:** 15 item commits, each merged with a `merge: item NN <slug>` commit:
  - `88be86e` docs: point only at things that exist; PRIVACY covers watchers and in-app sign-in (closes #156)
  - `6382b0b` docs(checklist): say what the code says for items 64 to 76 and backlog #4
  - `12bf070` docs(spec): add the Mac app section and a pointer to it
  - `860841c` docs(vibe-doc): describe the current Mac and Windows apps in profile.json
  - `9acdfbc` refactor(mac): one SHA-256 hex helper behind every stamp and manifest hash
  - `203ca57` refactor(mac): one ISO 8601 stamp and parser behind every file stamp
  - `c0c837a` refactor(mac): one JSON object file reader behind every seat file read
  - `c9f2c06` refactor(mac): one throwing owner-only atomic write behind the seat file writers
  - `4d32686` refactor(mac): one builder for the handoff result files
  - `5a5a762` refactor(mac): one process runner behind claude, git and ditto
  - `0b1ea02` refactor(mac): move SeatMtimes to the test target and pin calculateCooldown
  - `3bfc683` ci: least-privilege token and SHA-pinned actions in mac-ci, docs-check, vscode-ci
  - `1f5c937` build(vscode): bump vitest to ^4.1.11 to close the tinypool and mocker advisories
  - `28ac9b1` docs(mac-release): add rollback, bad-update and after-release sections
  - `48c29ae` docs(adr): add Format block, Status and Consequences to every entry
- **Checks:** every Swift item ran `./build.sh --debug` and the full `./test.sh` in its worktree and again before its merge (1,705 → 1,732 tests as helpers gained tests). Item 9 also ran `pytest mac/integrations/tests` (223). Item 14 ran `npm ci && npm test && npm audit` (330 tests, 0 vulnerabilities). Docs and workflow items ran their greps, path-existence loops and YAML parses; the pinned action SHAs were resolved against GitHub a second time at merge. Close-out on the integration tip: `./build.sh --debug` ✓, `./test.sh` 1,732 passed, pytest 223 passed, vscode 330 passed, and vibe-sec's in-house secret scan over 9,335 files found only the two known false positives (the Sparkle public key in `mac/scripts/export-release-secrets.sh`, `process.env.TZ` in a vscode test). The vibe-test and vibe-sec gates themselves could not run (D12).

To push and open the PR:
```bash
git push -u origin cart/polish-round
gh pr create --base main --head cart/polish-round --title "chore(mac): polish round from the findings pass (16 items)" --body-file docs/cart-runs/2026-10-17-polish-round/report.md
```
Pushing waits for your say-so. Tell Cart "push it", or name a subset.

## Waiting on you
- **Item 13: the release workflow runs with least privilege and pinned actions** (`cart/polish-round--13-release-workflow-harden`, commit `ba98ca2`, worktree `~/.cwt/sanduhr-f-r-clau-polish-round/13`)
  - Look at: `.github/workflows/mac-release.yml`. The file already had a workflow-wide `permissions: contents: write, pull-requests: write` (vibe-sec's "missing permissions block" was wrong). The change narrows it: `contents: read` at the top, `contents: write` + `pull-requests: write` on the one `release` job, which needs them for `gh release create`, the `git push` of `release/mac-<version>` and `gh pr create`; every `gh` call runs on `github.token`, no PAT. `actions/checkout` → `11d5960a…` (v4.4.0), `actions/upload-artifact` → `ea165f8d…` (v4.6.2). Nothing else changed: not the `mac-release` environment gate, secrets, signing or notarization.
  - Held because: it is the release surface; a wrong permission blocks the next release. Merge with `git -C ~/.cwt/sanduhr-f-r-clau-polish-round/int merge --no-ff cart/polish-round--13-release-workflow-harden` after reading the diff, or drop the branch.

## Parked
None.

## Stopped
None.

## Decisions to skim
Full ledger: `decisions.md`. Highest stakes:
- D8: `ConfigPack` and `ModInventory` now refuse a `settings.json` with a trailing comma, matching Claude Code and the By Seat page (item 7).
- D9: the kept-mod record files are now gated by ResetGate like every other support-folder writer; the credentials file and the installer stay on their own writers because they are reset steps (item 8).
- D10: `ditto` zip/unzip gained a 60 s timeout, and a hung `git` is killed one second after terminate instead of waited on without bound (item 10).
- D11: vitest 4.1.12, not 5, because `vscode-ci.yml` runs Node 20 (item 14).
- D12: the close-out gates were substituted with the repo's own checks plus a secret scan.

## Found along the way, not fixed (for your call)
- `CHANGELOG.md`'s newest Mac heading is still `v2.20.0-mac` while the app is at 2.26.0; "Unreleased (mac)" has carried forward since 2.21, and `mac-release.yml` cuts the draft's notes from that section. The new "After a release" checklist names the step.
- A pulled release's build number stays burnt: the next `CFBundleVersion` must exceed the pulled one, and the workflow's check would let the same number through (noted in the new rollback section).
- `IntegrationInstaller.atomicWrite` reads `errno` after `unlink(tmp)`, so a rename failure can report the wrong POSIX code (one line; the site was out of item 8's scope).
- `calculateSurplus` (UsageMath) has the same no-test gap `calculateCooldown` had.
- Test files construct `ISO8601DateFormatter()` in ~12 places and `ModPackTests.swift:80` recomputes the inline SHA expression; both could use the new helpers.
- Natural next candidates for `JSONFiles`: `ModStatusEntries` (4 reads), `ModSwitch` (2), `SeatSnapshots.json(_:)`; for `ProcessRunner`: `ConfigPack`'s `ditto`.
- Vite 8 (pulled in by vitest 4) warns that `vscode/vitest.config.ts` is loaded as CommonJS; renaming it to `.mts` silences it.
- `docs/PRIVACY.md`'s "Last updated" line was not bumped (item 1 did not name it), and its network section does not mention Sparkle's feed fetch, which the new SDK bullet does.
- Plugin bugs found tonight, for their own repos: vibe-sec skips `build/` but not `.build/`, does not downgrade fixtures under `Tests/`, reports `sk-ant` under the `OPENAI_API_KEY` pattern name, and flagged a `permissions:` block that existed; vibe-test's audit-state schema cannot hold an unsupported-stack decline; vibe-doc has no desktop-application category.

## Left untouched
Uncommitted files in your tree at run start. The run cut from `54bad55` and did not stash, reset or check out over them.
- `.gitignore` (modified: `.vibe-sec/` and `.vibe-test/` added)
- `docs/vibe-sec/` (untracked: `audit-report.md`, `threat-model.md`)
- `docs/vibe-test/` (untracked: `audit-2026-10-17.md`)
- `docs/cart-runs/2026-10-17-polish-round/` (this run folder, untracked)
