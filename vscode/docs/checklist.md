# Build Checklist

## Build Preferences

- **Build mode:** Autonomous (from the builder's profile: `autonomy_level: fully-autonomous`; confirmed by "build autonomously").
- **Comprehension checks:** N/A (autonomous mode).
- **Git:** Commit after each item, conventional commits (`feat(time): ...`, `test(time): ...`). Sanduhr work on branch `feat/sanduhr-time`; 626 Labs work on branch `feat/time-publisher` in a separate worktree. One PR per repo at the end.
- **Verification:** Yes, agent-run. Every item ends green on typecheck, lint and tests, plus the item's own Verify step. Checkpoints for the builder only where the record says a human call is required (profile patterns (rrr) and (uuu)): items 2, 10 and 12, each marked **Builder checkpoint**.
- **Check-in cadence:** N/A (autonomous mode).
- **Standing rules for this build** (from the builder profile): never run anything inside the builder's own VS Code windows or change OS or VS Code user settings (sss); every test fixture is synthetic (spec > Public-repo hygiene); each fix sweeps its call sites and adds a fence test (qqq); a constraint claim is tested before it is obeyed (ppp).

## Checklist

- [x] **1. Extension scaffold, CI and the public-repo gate**
  Spec ref: `spec.md > Stack`, `spec.md > Runtime & Deployment`, `spec.md > Tracker extension > Public-repo hygiene`, `spec.md > File Structure`
  What to build: Under `Sanduhr/vscode/`: `package.json` (name `sanduhr-time`, publisher `626labs`, engine `^1.80.0`, `extensionKind: ["ui"]`, `onStartupFinished`, settings and commands from the spec, scripts mirroring the 626 Labs extension), `tsconfig.json`, `webpack.config.js` (single node bundle), `vitest.config.ts` with a `vscode` stub at `src/test/vscodeStub.ts`, `eslint.config.mjs`, `.vscodeignore`, `.gitignore`, a minimal `src/extension.ts` that activates and returns `{ version: 1 }`, `scripts/check-public.mjs`, and `.github/workflows/vscode-ci.yml` (paths `vscode/**`, Node 20, `npm ci`, typecheck, lint, test, the profile-path check, `vsce package`). `CHANGELOG.md` with 0.1.0 Unreleased.
  Acceptance: The extension builds, lints and tests clean; `check-public` fails on a planted Windows user-profile path and passes without it.
  Verify: `npm ci && npx tsc --noEmit && npm run lint && npm test && node scripts/check-public.mjs && npm run package` in `vscode/`, all exit 0; `npx @vscode/vsce@4.0.0 package --no-dependencies` produces a `.vsix`.

- [x] **2. Reload-detection spike**
  Spec ref: `spec.md > Spike: reload detection`
  What to build: A throwaway probe under `vscode/spike/` run in an isolated VS Code downloaded by `@vscode/test-electron` (never the builder's windows) on a scratch folder in the OS temp directory. It opens a saved file and records every `onDidChangeTextDocument` with `isDirty` after the event, `reason`, change counts and sizes, and timing against `onDidSaveTextDocument` and a file-system watcher, for: typing (the `type` command), an external write to the open saved file (what Claude Code's CLI does), an external write while the document is dirty, undo back to saved, and format-on-save. The Claude Code VS Code extension's diff-accept path cannot be driven in an isolated host; record it as untested. Write `docs/spike-reload.md` (shapes and outcomes only) with the ruling: pass, partial or fail, and which recorder rule follows. Delete `spike/` afterwards.
  Acceptance: A written ruling, with event evidence, on whether a disk reload is identifiable; the recorder rule chosen accordingly.
  Verify: The spike's test run prints the event table for every case; `docs/spike-reload.md` holds it. **Builder checkpoint** (only if the diff-accept path matters to the ruling): one manual accept of a Claude Code diff in a scratch file, with the panel's log open, to confirm it is recorded as typing or not.

- [x] **3. Core: types, intervals, path hash, data paths, project resolver**
  Spec ref: `spec.md > Tracker extension > Project resolver`, `spec.md > Tracker extension > Spool writer` (hashPath), `spec.md > Data Model`
  What to build: `src/types.ts`; `src/intervals.ts` (join with gap, lone-event duration, union, intersect, subtract, clip, length); `src/store/hash.ts` (`hashPath`); `src/store/paths.ts` (data dir: setting, `SANDUHR_TIME_DIR`, default `~/.sanduhr/time`); `src/store/state.ts` (installed.json with salt, offsets, the pid-aware lock); `src/project.ts` (git config read without a process, worktree `commondir`, `normalizeRemote`, `projectKey`, path fallback). Tests for each, including worktrees, SSH and HTTPS remote variants, credentials in URLs, no remote, case-folding on Windows, a stale and a live lock.
  Acceptance: `prd.md > Projects and masking` resolver criteria hold; the same file reached through two path spellings hashes identically.
  Verify: `npm test` green with the new suites.

- [x] **4. Claude reader, homes and the Claude spool**
  Spec ref: `spec.md > Tracker extension > Claude reader`, `spec.md > Data Model > Claude spool`
  What to build: `src/claude/homes.ts`, `src/claude/reader.ts`, `src/store/claudeSpool.ts`. Prompt rule (human origin only), activity rule, line counting from `+`/`-` hunk lines and `create` content, stream ids per transcript file, first-run start-at-size for old files, bounded reads, partial trailing lines, offset reset on shrink, spool-before-offsets, de-duplication by entry id, unreadable-line counting. Synthetic fixtures: a main session with human prompts, task notifications, peer and meta entries, a compact summary, an Edit and a create Write; a subagent file sharing the parent's sessionId; a malformed line; two homes plus a backup home to skip.
  Acceptance: `prd.md > Claude's work` criteria hold, including machine-written user entries never counting as yours and parallel subagents staying separate streams.
  Verify: `npm test` green; a fixture test proves a restart (new reader, same state dir) loses no events and duplicates none.

- [x] **5. Heartbeat recorder and editor spool**
  Spec ref: `spec.md > Tracker extension > Heartbeat recorder`, `spec.md > Tracker extension > Spool writer`
  What to build: `src/recorder.ts` and `src/store/spool.ts`: focused-window gating, scheme filtering, the four heartbeat kinds, throttle and debounce, line deltas, the reload rule chosen in item 2, multi-root routing, the `(no project)` bucket, buffered flush, 90-day prune, per-window file names with the local date. Tests drive the `vscode` stub's events.
  Acceptance: `prd.md > Your time` criteria hold; reload-tagged changes carry zero human lines.
  Verify: `npm test` green; a stub-driven test shows no heartbeats while the window is unfocused.

- [x] **6. Merger, day schema and the usage spec**
  Spec ref: `spec.md > Tracker extension > Merger`, `spec.md > Data Model > Day record`
  What to build: `src/merge.ts` (pure) per the spec's rules, `docs/time-day.schema.json`, and `docs/time-usage-spec.md` v1 (every field of spool, Claude spool, day file, aliases and state; `normalizeRemote`, `projectKey` and `hashPath` defined exactly; the fresh-store rule). Tests: overlap union; parallel streams and agent-minutes; the lone-event rule; prompt spans; midnight split with the previous day's tail; project switching; pooled day totals where per-project sums exceed the day total; reload subtraction; languages without prompts. Day outputs validate against the schema in tests.
  Acceptance: `prd.md > The merged day` criteria hold; every generated fixture day validates.
  Verify: `npm test` green including schema validation.

- [x] **7. Alias store, merge runner and the public API**
  Spec ref: `spec.md > Tracker extension > Alias store`, `spec.md > Tracker extension > Merge runner`, `spec.md > Tracker extension > Public API`
  What to build: `src/store/alias-pool.ts` (about 100 neutral names), `src/store/aliases.ts` (lock-guarded writes, reroll, mask, streamer mode, fail closed), `src/store/days.ts` (atomic writes), `src/runner.ts` (5-minute tick, lock refresh, bounded reader pass, merge set, `onDayUpdated`, close-only flush), `src/api.ts`, wiring in `extension.ts`.
  Acceptance: `prd.md > Projects and masking` alias criteria and `prd.md > Local data and the usage spec` criteria hold; two runners on one data dir never corrupt a file or lose an alias.
  Verify: `npm test` green, including a two-runner concurrency test and the missing-store fail-closed test.

- [x] **8. Status bar and the Time panel**
  Spec ref: `spec.md > Tracker extension > Status bar`, `spec.md > Tracker extension > Time panel`
  What to build: `src/ui/statusBar.ts`, `src/ui/panel.ts`, `media/panel.css`, `media/panel.js`: headline split, 7-day strip, per-project rows with stacked bars, lines, languages, mask and reroll, streamer toggle, caveats note, empty first-day state, remote-window state, the trademark disclaimer, strict CSP, theme variables for dark and light.
  Acceptance: `prd.md > Seeing it` criteria hold.
  Verify: Unit tests for the view model; then render the panel in the isolated test VS Code from item 2's harness against a synthetic data dir in both a dark and a light theme, capture screenshots, and inspect them.

- [x] **9. WakaTime side-by-side comparison**
  Spec ref: `spec.md > Tracker extension > WakaTime comparison`
  What to build: `src/compare/wakatime.ts`: locate `wakatime-cli`, run it once a day with a timeout, parse raw JSON or the `Xh Ym` text, append to `state/wakatime-compare.jsonl`, show it in the panel, honor the setting, hide when missing.
  Acceptance: `prd.md > WakaTime side by side` criteria hold.
  Verify: Tests with a fake CLI for both output forms and a missing CLI; then one real run of the installed `wakatime-cli --today` from a terminal (read-only), to confirm the flag and output shape before trusting the parser.

- [ ] **10. Dashboard: `manage_time` tool and the bulletin coding line**
  Spec ref: `spec.md > The 626 side > manage_time tool`, `spec.md > The 626 side > Bulletin coding line`
  What to build: In a Project-626Labs-1 worktree off `origin/main` on branch `feat/time-publisher`: `mcp-server/src/tools/time.ts` mirroring `usage.ts` (+ tests), registration in `index.ts`, the registry name test, annotations; bulletin `collect.ts` reads `codingTime/{date}` (fenced) and `compose.ts` adds the coding row (+ tests). Run `vars:resolve` if the tool count is templated.
  Acceptance: `prd.md > Publishing to the 626 dashboard` tool and bulletin criteria hold.
  Verify: `mcp-server` tsc, full vitest; `functions` tsc and bulletin suite; root `vars:check`. **Builder checkpoint:** the agent-key grant (`manage_time` and `manage_projects` on the per-machine extension keys) widens access, so it waits for the builder's go after the tool is deployed.

- [ ] **11. Publisher in the 626 Labs extension**
  Spec ref: `spec.md > The 626 side > Publisher`
  What to build: `vscode-extension/src/timePublisher.ts` with the pure `buildTimePayload(day, maskState, boundKeys)` holding the wall, bindings from `manage_projects list` through the tracker's `projectKey`/`normalizeRemote`, live `maskState()`, the 15-minute and catch-up schedule with `globalState` tracking, fail-closed paths; wiring in `extension.ts`; minor version bump with a changelog entry.
  Acceptance: `prd.md > Publishing to the 626 dashboard` publisher criteria hold: a real name leaves only for a bound, unmasked project with streamer mode off; every failure path publishes aliases or nothing.
  Verify: Extension tsc, lint, full vitest, with tests for each wall branch (unbound, masked, streamer on, list denied, store unreadable, tracker absent) and the catch-up selection.

- [ ] **12. Install, live check, documentation and security review**
  Spec ref: `prd.md > What We're Building` + `spec.md > [all sections]`
  What to build: `vscode/README.md` (what it tracks, what never leaves the machine, data location, settings, the disclaimer); confirm every `docs/` artifact matches what was built. Secrets and hygiene: `check-public` full run, a scan of both branches for credentials, `npm audit` in `vscode/` (no runtime deps, so dev-only findings get documented). Open both PRs; let CI pass; merge; confirm the `mcp` and bulletin deploys on `main`. Package both `.vsix` files and install them on this machine; copy them to the shared LabShare folder for the other machine. After an hour of real work, check that today's day file is sane against `wakatime-cli --today`, and that a publish reached `codingTime/{today}` with aliases for unbound projects.
  Acceptance: README clear; no secrets or private names in either diff; audits clean or documented; both PRs merged and deployed; a real day recorded, compared and published under the wall.
  Verify: `git log -p origin/main..feat/sanduhr-time | node scripts/check-public.mjs --stdin` clean; the dashboard's `manage_time latest` shows this machine's record with only bound projects named. **Builder checkpoint:** reload VS Code windows to pick up the new extensions (the agent never reloads the builder's windows), and the key grant from item 10.
