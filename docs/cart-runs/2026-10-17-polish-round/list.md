# Run List: polish-round

Repo rules every item follows: never `sed -i` a tracked file (edit with the Edit tool or Python); stage by explicit paths; commit messages end with `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>` and `Claude-Session: https://claude.ai/code/session_01Jfz21BmnQKRbuExYPtRcWC`; the company is "626Labs LLC"; no home-folder paths, employer names or person names in the repo; `windows-dotnet/` is never touched. Swift checks: `cd mac && ./build.sh --debug && ./test.sh`. When CI's slower compiler would fail to type-check a SwiftUI `body`, split it into smaller properties.

- [ ] **1. Docs point only at things that exist, and PRIVACY covers Watchers and in-app sign-in**
  Spec ref: `tend check 3 (2026-10-17)` > mac/README.md:3, docs/PRIVACY.md:89, mac/README.md "writes only a Desk message request and a theme request"; `issue #156`
  What to build (S): (a) `mac/README.md` line 3: the link to `../sanduhr.py` points at a file removed 2026-09-13 (tag `legacy/python-v2.3.0`); reword so it names the legacy Python widget at that tag with no dead link. (b) `docs/PRIVACY.md` "No third-party SDKs" bullet names `windows/requirements.txt`, PySide6 and cloudscraper; the Windows app is now .NET (`windows-dotnet/`), so rewrite the bullet for the current apps (the Mac app's only dependency is Sparkle; the MCP server and statusline are Python standard library) without describing `windows-dotnet/` internals you haven't read. (c) In `mac/README.md`, the sentence saying the MCP server writes only a Desk message request and a theme request: list what it writes today (desk-messages, theme, now-playing-looks, watch, mod, mod-install and config requests), matching the header comment in `mac/integrations/sanduhr_mcp.py`. (d) Issue #156: `docs/PRIVACY.md` has no section for Watchers (the `watch_start`/`watch_update`/`watch_end` tools and the "Let agents show watchers" switch, `watchers.json`; descriptions kept in memory only) or for in-app sign-in (the sign-in sheet; what is stored where: the Keychain, per the existing Accounts section). Write both from the source of truth: `mac/integrations/sanduhr_mcp.py` (header and the watch tool guides) and the Swift sources for sign-in (`mac/Sources/Sanduhr/Services/KeychainStore.swift`, the sign-in view). Same voice and table/paragraph shape as the neighbouring sections; no secret values, no paths outside `~/Library/Application Support/Sanduhr/`.
  Acceptance: `grep -n 'sanduhr.py' mac/README.md` finds no markdown link to a missing file; `grep -n 'requirements.txt\|PySide6\|cloudscraper' docs/PRIVACY.md` is empty; `grep -c -i 'watch_start' docs/PRIVACY.md` ≥ 1; PRIVACY has a sign-in paragraph naming the Keychain; every file path named in the edited sentences exists in the repo or is a runtime file named in the Swift sources.
  Verify: run the greps above, then `git diff --stat` shows only `mac/README.md` and `docs/PRIVACY.md`.
  Stop class: go

- [ ] **2. The checklist and backlog say what the code says**
  Spec ref: `tend check 2 (2026-10-17)` > docs/checklist.md items 64, 65, 66, 71, 72, 75, 76; docs/BACKLOG.md #4
  What to build (S): in `docs/checklist.md`: check item 76 (every slice A–K built; `docs/config-transfer-spec.md` marks each "built 2026-10-15/16"; 2.26.0 shipped 2026-10-17) and item 75 (slices A–G built per `docs/mod-studio-spec.md`); add a one-line "Built" status to each, dated, in the style of items 67 and 73. Rewrite item 71's Status line: slices 0–4 are on disk (`ConfigImport`/`configEdit` receipts, `ClaudeCLI`, the seat dry run), slice 5 (drift actions, cleanup to the Trash, history archive) is not; keep the existing text's facts, only correct the "slices 2 to 5 not started" sentence. Item 72: add that slice 5 (Reset Sanduhr…) shipped, leaving only the by-hand pass open. Item 64: add a line that it was superseded by items 71 and 75 (the Mods page became Mods & Config and Mod Studio) and leave it unchecked. Items 65 and 66: add Status lines: 65 (a)–(d) and (f) built (font tags, sweep, the looks tool, band styles) with (e) EsteFont open; 66 slice 1 built (watch tools, cards), slice 2 (`propose_widget`, the watcher band) not. In `docs/BACKLOG.md` #4: the "empty notch wing collapsing" open item shipped with item 72 slice 4; say so. Dates are 2026-10-17. Do not reword anything else.
  Acceptance: `grep -c '^- \[ \]' docs/checklist.md` is 5 (64, 65, 66, 71, 72 remain) and `grep -c '^- \[x\]' docs/checklist.md` is 72; each touched item has a Status or Built line dated 2026-10-17; no other lines changed (`git diff --stat` shows the two files, under 40 lines changed).
  Verify: the greps above; `git diff docs/checklist.md docs/BACKLOG.md` reads as status edits only.
  Stop class: go

- [ ] **3. `docs/spec.md` has a Mac section**
  Spec ref: `tend check 1 (2026-10-17)` > spec.md mentions mac/ 0 times
  What to build (S): add a section "## The Mac app (`mac/`)" at the end of `docs/spec.md` (before any trailing notes if present): one paragraph saying the Mac app is specified per item rather than in this file; the actual `mac/` tree to two levels (`Sources/Sanduhr/{Debug,Desk,Helpers,Models,Services,Views}`, `Tests/SanduhrTests`, `integrations/` with `sanduhr_mcp.py` and the statusline, `smoke/`, `scripts/`, `Resources/`, `Vendor/`, `icon/`) with a one-line purpose each, read from disk and `mac/README.md`; and a table of the per-item specs with their items: `docs/mac-accounts-spec.md`, `docs/mods-and-config-spec.md` (item 71), `docs/settings-v2-spec.md` (72), `docs/mod-studio-spec.md` (75), `docs/config-transfer-spec.md` (76), `docs/agnostify-spec.md` (76 H), plus `docs/mac-parity-plan.md` and `docs/mac-implementation-plan.md`. Add a one-line pointer near the top of the file ("The Mac app: see the last section") so a reader of the Windows spec finds it.
  Acceptance: `grep -c 'mac/' docs/spec.md` ≥ 10; every path named in the new section exists (`for p in …; do test -e $p; done`); nothing above the new section changed except the pointer line.
  Verify: the existence loop over every backticked path in the new section; `git diff --stat docs/spec.md`.
  Stop class: go

- [ ] **4. `.vibe-doc/profile.json` describes the app as it is**
  Spec ref: `vibe-doc scan 2026-10-17` > stale intake profile dated 2026-09-30
  What to build (S): rewrite the fields of `.vibe-doc/profile.json` (tracked) to the current facts: description (a macOS SwiftUI app and a Windows .NET/WPF app showing claude.ai usage as pacing, with a Desk layer, a Claude Code mod, a stdio MCP server and config packs), mainPurpose, primaryUsers (individual Claude subscribers; public, not SaaS), coreFeatures (take 8–10 from `CHANGELOG.md` and `mac/README.md` headings), technologies (Swift/SwiftUI, Sparkle, Python 3 stdlib for the MCP server and statusline, .NET 10/WPF on Windows, GitHub Actions, Homebrew tap, Microsoft Store), deploymentModel (end-user desktop install: Homebrew cask and Sparkle on the Mac, Store and Velopack on Windows), architectureStyle (two native desktop apps sharing docs, release process and the MCP contract). Keep the JSON shape; no paths, no names.
  Acceptance: `python3 -c 'import json;json.load(open(".vibe-doc/profile.json"))'` passes; `grep -c 'PySide6\|tkinter\|cloudscraper' .vibe-doc/profile.json` is 0; the description names both apps.
  Verify: the two commands above.
  Stop class: go

- [ ] **5. One SHA-256 hex helper**
  Spec ref: `tend check 4 (2026-10-17)` > 1b: ConfigPack.swift:502,517; ConfigProposal.swift:138; ModPack.swift:59; ModPackReader.swift:189,213; VaultStore.swift:76; ModMarketplace.swift:198; IntegrationScripts.swift:156; SnapshotWriter.swift:110; existing ConfigImport.sha(_:) at ConfigImport.swift:630
  What to build (S): add `enum Digest` (new file `mac/Sources/Sanduhr/Helpers/Digest.swift`) with `static func sha256Hex(_ data: Data) -> String` and `static func sha256Hex(_ data: Data, prefix: Int) -> String` (the truncated form), implemented once with CryptoKit; make `ConfigImport.sha` call it or replace its callers; replace every inline `SHA256.hash(data:).map { String(format: "%02x", $0) }.joined()` and the three incremental-hasher-then-truncate copies with the helper, keeping each site's exact output (same input bytes, same prefix length) so stored hashes, stamps and kept-mod folder names do not change. No behaviour change: the kept-mod stamp (12 hex), the snapshot stamp (8 hex) and the pack manifest hashes must be byte-identical to before.
  Acceptance: `rg -n 'String\(format: "%02x"' mac/Sources` returns only `Digest.swift`; `./test.sh` passes with no test changed except to add a `Digest` test that pins `sha256Hex(Data("abc".utf8))` to `ba7816bf…` and the 12-char prefix.
  Verify: `cd mac && ./build.sh --debug && ./test.sh`; the rg above.
  Stop class: go

- [ ] **6. One ISO-8601 stamp and parser**
  Spec ref: `tend check 4 (2026-10-17)` > the fractional-seconds formatter rebuilt at MessageProposal:86,94,213; WatcherRequest:121; SnapshotWriter:82; StatuslineLiveInput:62; BandFrame:162; NowPlaying:176; SeatSnapshots:172; HistoryStore:157; existing HandoffFiles.stamp and Helpers/UsageMath.swift:8
  What to build (S): make `HandoffFiles.stamp(_:)` (or a new `ISO8601` enum in Helpers if HandoffFiles is the wrong home; pick one and say why in the commit) the single formatter, with a matching `parse(_:) -> Date?` that accepts the same forms the sites accept today (with and without fractional seconds, `Z` and offsets); replace each inline `ISO8601DateFormatter()` construction with it. Preserve each site's output format exactly (fractional seconds where they were emitted, none where they were not): read every site before touching it and keep a note per site in the commit message. Thread-safety: formatters are not thread-safe in all cases; keep a per-call instance or a documented lock, whichever the existing `HandoffFiles.stamp` does.
  Acceptance: `rg -n 'ISO8601DateFormatter\(\)' mac/Sources` returns at most the helper file(s); `./test.sh` passes; a new test round-trips both forms through the parser.
  Verify: `cd mac && ./build.sh --debug && ./test.sh`; the rg above.
  Stop class: go

- [ ] **7. One JSON-object file reader**
  Spec ref: `tend check 4 (2026-10-17)` > 1a: ConfigPack.swift:477 readObject, ConfigImport.swift:342 readObject, ModInventory.swift:356 readJSON; existing SeatScan.readObject at SeatScan.swift:358 (size cap, strict JSON, reports why)
  What to build (S/M): route the three private readers to one shared reader. If `SeatScan.readObject`'s stricter behaviour (rejecting comments and trailing commas, a size cap, a reason) is acceptable at all three sites, call it; where a site needs a different cap (ConfigPack's 8 MB) or goes through `JSONEdit.root` (ConfigImport), give the shared reader a `maxBytes:` parameter and keep ConfigImport's `JSONEdit` path by having it call the shared reader for the bytes only. Delete the three private copies. Behaviour on an unreadable file must match what each site did before (nil / skip); do not make a previously tolerant site strict without a test showing the old input still reads.
  Acceptance: `rg -n 'private static func readObject\|private static func readJSON' mac/Sources` is empty; `./test.sh` passes; `ConfigPackTests`, `ConfigImportTests` and the mod inventory tests unchanged.
  Verify: `cd mac && ./build.sh --debug && ./test.sh`; the rg above.
  Stop class: go

- [ ] **8. A throwing owner-only atomic write, used by the six inline copies**
  Spec ref: `tend check 4 (2026-10-17)` > 1c: KeptMods.swift:93, KeptModSwitch.swift:52, KeychainStore.swift:250, MCPAccess.swift:104, SeatSnapshots.swift:207, IntegrationInstaller.swift:1116; existing HandoffFiles.writeOwnerOnly at HandoffFiles.swift:27 (swallows errors, honours ResetGate)
  What to build (M): add `HandoffFiles.writeOwnerOnlyOrThrow(_ data: Data, to url: URL) throws` (tmp file beside the target, `createFile` with 0o600, `rename` over the target; same ResetGate behaviour as the existing helper, documented) and make the existing `writeOwnerOnly` call it and drop the error as today. Route the six sites to the throwing variant where they threw before and to the silent one where they ignored errors. `IntegrationInstaller.atomicWrite(newFilePermissions:)` takes a permission argument; keep it if other permissions are used there, otherwise route it too. No change to which files are written or their modes (0600 stays 0600; a site that wrote 0644 keeps 0644 and is left alone, noted in the commit).
  Acceptance: `rg -n 'posixPermissions: 0o600' mac/Sources` returns only HandoffFiles.swift (and IntegrationInstaller if kept, with a reason); `./test.sh` passes, including the Keychain, kept-mods and MCP-access tests unchanged.
  Verify: `cd mac && ./build.sh --debug && ./test.sh`; the rg above.
  Stop class: go

- [ ] **9. One handoff result-JSON builder**
  Spec ref: `tend check 4 (2026-10-17)` > 1d: MessageProposal.swift:203, NowPlayingLook.swift:269, ModProposal.swift:139, ConfigProposal.swift:158, ThemeProposal.swift:179, ModProposalStore.swift:308 (answerInstall)
  What to build (S): add `HandoffFiles.resultJSON(id: String, status: String, fields: [String: Any?], reasons: [String] = [], now: Date) -> Data` building `{id, completed_at, result: {status, …non-nil fields, reasons when non-empty}}` with sorted keys, and make the six builders call it. Output must be byte-identical for every existing test fixture (the Python server's tests read these shapes; the Swift tests pin them).
  Acceptance: `rg -n '"completed_at"' mac/Sources` returns only HandoffFiles.swift; `./test.sh` passes with no fixture changed; `cd mac && python3 -m pytest -q integrations/tests` still passes.
  Verify: `cd mac && ./build.sh --debug && ./test.sh && python3 -m pytest -q integrations/tests`.
  Stop class: go

- [ ] **10. One process runner**
  Spec ref: `tend check 4 (2026-10-17)` > 1e: identical pair ModPack.swift:148 and ModPackReader.swift:275; three copies of pipe+DispatchGroup+timeout+Box at ClaudeCLI.swift:89, ModCheck.swift:209, ModMarketplace.swift:345 (each with its own Box class)
  What to build (M): add `struct ProcessRunner` (new file `mac/Sources/Sanduhr/Services/ProcessRunner.swift`) with one `static func run(_ executable: String, _ args: [String], cwd: URL? = nil, env: [String: String]? = nil, stdin: Data? = nil, timeout: TimeInterval) -> Result` returning `{status: Int32, stdout: String, stderr: String, timedOut: Bool}`; implement the pipe draining, DispatchGroup wait, timeout kill and the Box once. Replace the three copies and the ModPack/ModPackReader pair with it, preserving each caller's environment handling (ClaudeCLI strips `CLAUDE_*` and sets `CLAUDE_CONFIG_DIR`; ModMarketplace sets `GIT_TERMINAL_PROMPT=0`, `GIT_ASKPASS`, `GIT_SSH_COMMAND`; keep those at the call sites). Leave the one-off sites (StatuslineCombine, PythonFinder, ConfigPack's ditto, NowPlayingController) alone unless the swap is a pure drop-in; name any you left in the commit.
  Acceptance: `rg -n 'final class Box' mac/Sources` is empty; `rg -c 'Process\(\)' mac/Sources/Sanduhr/Services/ClaudeCLI.swift mac/Sources/Sanduhr/Services/ModCheck.swift mac/Sources/Sanduhr/Services/ModMarketplace.swift mac/Sources/Sanduhr/Services/ModPack.swift mac/Sources/Sanduhr/Services/ModPackReader.swift` is 0 for each; `./test.sh` passes, including `ModMarketplaceTests` (real git against a bare repo), `ClaudeCLI` tests against the fake `claude`, and `ModCheck` tests.
  Verify: `cd mac && ./build.sh --debug && ./test.sh`; the two rg commands.
  Stop class: go

- [ ] **11. Test-only code out of the app; `calculateCooldown` tested or gone**
  Spec ref: `tend check 5 (2026-10-17)` > SeatMtimes at Services/SeatChecks.swift:424 (callers only in Tests/SanduhrTests/ModsBySeatTests.swift:290,294); calculateCooldown at Helpers/UsageMath.swift:89 (1 caller, no tests)
  What to build (S): move `SeatMtimes` into `mac/Tests/SanduhrTests/` (a `SeatMtimes.swift` test helper, or into `ModsBySeatTests.swift` if it is small) and delete it from `Sources`; confirm with `rg` that nothing in `Sources` referenced it. For `calculateCooldown`: find its one caller; if the caller is live UI, add a swift-testing test pinning its arithmetic on three inputs (zero, mid-window, past); if the caller is itself dead, remove both and say so.
  Acceptance: `rg -n 'SeatMtimes' mac/Sources` is empty and `./test.sh` passes; either `rg -n 'calculateCooldown' mac/Tests` finds a test or `rg -n 'calculateCooldown' mac` is empty.
  Verify: `cd mac && ./build.sh --debug && ./test.sh`; the rg commands.
  Stop class: go

- [ ] **12. CI workflows run with least privilege and pinned actions (non-release)**
  Spec ref: `vibe-sec audit 2026-10-17` > supply-chain: missing-permissions-block and unpinned-first-party-action in .github/workflows/mac-ci.yml, docs-check.yml, vscode-ci.yml
  What to build (S): in `mac-ci.yml`, `docs-check.yml` and `vscode-ci.yml`: add a top-level `permissions: contents: read` (widen per job only where a job writes, e.g. `actions/cache` needs none; `upload-artifact` needs none); pin every `uses: actions/<name>@vN` to the full commit SHA of that major's latest release with the tag in a trailing comment (`# v4.2.2`), resolving SHAs with `gh api repos/actions/<name>/git/ref/tags/<tag>` (read-only) at build time. Do not touch `windows-*.yml` or `mac-release.yml` (item 13).
  Acceptance: the three files parse (`python3 -c "import yaml"` if available, else `ruby -ryaml -e 'YAML.load_file'`); `grep -n 'uses: actions/' .github/workflows/mac-ci.yml .github/workflows/docs-check.yml .github/workflows/vscode-ci.yml` shows only 40-hex SHAs with a `# vX.Y.Z` comment; each file has `permissions:` at the top level.
  Verify: the parse and greps above; `git diff --stat` shows only the three files.
  Stop class: go

- [ ] **13. The release workflow runs with least privilege and pinned actions**
  Spec ref: `vibe-sec audit 2026-10-17` > .github/workflows/mac-release.yml:35 (checkout), :150 (upload-artifact), missing permissions block
  What to build (S): same treatment as item 12 for `mac-release.yml`. This workflow creates the GitHub release draft and needs `contents: write` on the job that does so; read the file to find which steps write (release creation, appcast PR via `gh`) and give only those jobs `contents: write` and `pull-requests: write` as needed, `contents: read` at the top. Do not change any step's logic, secrets or the `environment:` gate.
  Acceptance: the file parses; every `uses:` is SHA-pinned with a version comment; `permissions:` exists at the top and the writing job(s) declare what they need; `git diff` touches only `permissions` and `uses` lines.
  Verify: parse; `git diff .github/workflows/mac-release.yml` reviewed line by line in the morning (held: a release surface).
  Stop class: hold

- [ ] **14. `vscode/` tests run on a vitest without the tinypool advisory**
  Spec ref: `vibe-sec audit 2026-10-17` > dependency-cve: tinypool ≤2.1.0 (GHSA-5gmw-xhrv-c9v3, critical), vitest/@vitest/mocker <4.1.11 (GHSA-82fw-gwwq-j7x9); `npm audit` fix: vitest 5.0.5
  What to build (M): in `vscode/`, bump `devDependencies.vitest` to `^5.0.5`, run `npm install` to refresh `package-lock.json`, fix whatever vitest 5 breaks in `vitest.config.ts` or the 16 test files (API renames only; no test logic changes), and confirm `npm audit` reports 0 vulnerabilities. If vitest 5 needs a Node version CI lacks (`vscode-ci.yml` sets it), fall back to `^4.1.11` and say so.
  Acceptance: `cd vscode && npm ci && npm test` green; `npm audit --audit-level=low` reports 0; `git diff --stat` shows `package.json`, `package-lock.json` and only the test/config files that needed the API rename.
  Verify: the three commands above.
  Stop class: go

- [ ] **15. The Mac release doc has rollback, incident and post-release sections**
  Spec ref: `vibe-doc scan 2026-10-17` > runbook: 1 of 6 sections found
  What to build (M): extend `docs/mac-release.md` (read it and `docs/release-runbook.md` first; match their voice) with three sections written from what the repo actually does: **Rolling back a release** (mark the GitHub release as pre-release or delete it, revert the appcast entry in `docs/appcast.xml` by a PR, revert the Homebrew tap cask commit, what Sparkle does for users who already updated: a lower version is not offered, so a fixed higher version is the real rollback); **A bad update in the wild** (who notices, the Sparkle check cadence, how to ship a hotfix patch release fast: the existing steps, what to tell users in the release notes); **After a release** (the checks this repo already runs by hand: `brew fetch`, the DMG sha against the cask, the appcast entry, the What's New card showing). Only steps that exist; mark anything not yet automated as "by hand".
  Acceptance: `grep -c '^## ' docs/mac-release.md` increases by 3; the three headings exist; every command named exists in the repo's scripts or is a standard `gh`/`brew`/`git` command; no secrets, no paths outside the repo.
  Verify: the grep; a read of the diff.
  Stop class: go

- [ ] **16. ADRs carry status and consequences**
  Spec ref: `vibe-doc scan 2026-10-17` > adr: 2 of 5 sections found (`docs/adr.md`, `docs/decisions/`)
  What to build (S): read `docs/adr.md` and `docs/decisions/`; add a short ADR template block at the top of `docs/adr.md` (Status, Context, Decision, Consequences, Date) and bring each existing entry to that shape by adding a **Status** (accepted / superseded, with the date it already carries) and a **Consequences** line drawn from the entry's own text and the code that followed. Do not invent decisions; where consequences are not knowable from the text, write "Consequences: as described above." Keep every existing sentence.
  Acceptance: every `## ` entry in `docs/adr.md` has a `Status:` and a `Consequences:` line; the file's word count only grows; `git diff --stat` shows `docs/adr.md` (and `docs/decisions/*` only if they are ADRs in the same shape).
  Verify: `grep -c '^## ' docs/adr.md` equals `grep -c 'Status:' docs/adr.md` equals `grep -c 'Consequences:' docs/adr.md`.
  Stop class: go

## Not this run

- Seat/folder/root naming unification and the three error-type shapes (`tend check 4` > 2): 179 + 75 + 63 + 7 parameter labels across Services; a refactor run of its own with a mechanical rename plan.
- The 331 `try?` sites and the four files over 1,100 lines (`tend check 6`): a refactor round, not a polish round.
- Issue #137, a lock-screen line: a feature with its own spec.
- Issue #119's Touch Bar spike: the no-notch handling already exists in 7 Desk files (CameraLight.noNotchWidth, AVIndicators, DeskNowPlaying.stripShows); the spike is exploratory.
- Issue #120, screenshots for the 626labs.dev page: needs the app running with made-up data and the owner's eye.
- Issue #144 and the `windows-*.yml` workflows: the Windows agent's.
- Issue #175, Sanduhr Time's read-and-dictate gap: a vscode product feature.
- BACKLOG #3, the new-user walkthrough: partly built as the Sanduhr Tour (item 72 slice 4); needs a fresh scope pass before anything is built.
- Fixes to vibe-sec (`.build` not skipped, `Tests/` fixtures not downgraded, `sk-ant` reported as OPENAI), vibe-test (the audit-state schema cannot hold an unsupported-stack decline) and vibe-doc (no desktop-application category): their own repos.
