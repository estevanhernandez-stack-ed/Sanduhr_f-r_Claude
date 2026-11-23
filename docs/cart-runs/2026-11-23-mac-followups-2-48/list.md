# Run List: mac-followups-2-48

Every item: a Mac with only Claude on draws, alerts and reports as 2.47.1, pinned by a test where it touches a Claude surface.
Never `sed -i` a tracked file. Never touch `windows-dotnet/` (read it for parity only). Nothing committed names a home path or a person.
Keep SwiftUI bodies small (macos-15 CI). Tests use made-up data in temp folders.

- [ ] **1. A sparkline on Codex's cards**
  Spec ref: 2.46 report Follow-ups; D10 of the 2.46 run
  What to build: Codex cards in the widget's Codex section draw the same sparkline Claude's cards do, from
  `history.@codex.json` (`HistoryStore`, label `@codex`). They honor meter history being off for `@codex`. Demo
  mode gets a made-up Codex sparkline. Claude's sparklines are unchanged. Size S.
  Acceptance: Done when tests show a Codex card's sparkline points from a made-up `@codex` history, none when that
  history is off or empty, and Claude's cards unchanged; the suite passes.
  Verify: `cd mac && ./test.sh && ./build.sh --debug`
  Stop class: go

- [ ] **2. A Desk pulse for a Codex limit**
  Spec ref: D9 and D12 of the 2.46 run
  What to build: When a Codex alert fires and Alerts' delivery includes the Desk (desk or both) with the Desk
  running, the meter pieces that show Codex pulse: a piece set to Codex, or a Combined piece whose row carries
  Codex. A Claude-only piece never pulses for Codex, and Claude's pulses are unchanged. Per-source pulse keys go
  in `DeskModel.pulses` (or its equivalent). Size M.
  Acceptance: Done when tests show a Codex alert pulsing a Codex piece and a Combined piece but not a Claude
  piece, banner-only delivery not pulsing, and Claude's pulse behavior pinned unchanged; the suite passes.
  Verify: `cd mac && ./test.sh && ./build.sh --debug`
  Stop class: go

- [ ] **3. A Codex chip in Combine**
  Spec ref: D13 of the 2.46 run
  What to build:
  - Combine's segment picker gets a Codex chip (`cx 5h 40% wk 12%`), with its own `--mine` name (for example
    `codex`), alongside the Swift `SanduhrSegment` enum, the chips, the preview and the `--mine` handling in
    `sanduhr_statusline.py`.
  - Existing combined statuslines keep their output exactly: today `cx` rides with session and weekly. An older
    `--mine` list without the codex name keeps showing cx when session or weekly is in it, so nothing
    disappears on update.
  - The installed script's content stamp changes.

  Size M.
  Acceptance: Done when Python and Swift tests show the codex chip toggling cx on and off, an older `--mine` list
  unchanged, the chip's preview text, and Claude-only output byte-identical; the suite and pytest pass.
  Verify: `cd mac && ./test.sh && python3 -m pytest -q integrations/tests`
  Stop class: go

- [ ] **4. Agents in the Trends tab**
  Spec ref: D16 of the 2.46 run
  What to build: The Usage page's Trends tab includes the Codex and Antigravity Vault roots, as Overview and
  Sessions already do: totals combine agents, the weekly bars stack by agent in source colors, and the accessible
  names give each agent's total. With one agent, Trends is unchanged. The Skills list stays Claude Code's (say so
  in its caption if it now sits beside agent totals). Size M.
  Acceptance: Done when tests show Trends' weeks with agent rows stacked, accessible names per agent, and a
  Claude-only Trends equal to 2.47.1's; the suite passes.
  Verify: `cd mac && ./test.sh && ./build.sh --debug`
  Stop class: go

- [ ] **5. "Claude Code hook", and smoke section 22**
  Spec ref: 2.47 report Follow-ups
  What to build:
  - Where people read the piece's name, "Claude Code glow hook" becomes "Claude Code hook" (`SettingsNames` and
    every Settings, consent, Notifications and docs string), as Windows renamed it.
  - Settings search keeps finding it by "glow".
  - `docs/mac-smoke-test.md` section 22 is updated to the 2.47 three-event hook. Its steps still quote the 2.46
    command and two entries.

  Size S.
  Acceptance: Done when no user-facing string says "glow hook", search for "glow" and "hook" reaches the row,
  the pinned copy tests are updated, and section 22 matches the code; the suite and the selftest pass.
  Verify: `cd mac && ./test.sh && ruby smoke/selftest.rb`
  Stop class: go

- [ ] **6. The 2.48.0 rows**
  Spec ref: CLAUDE.md Mac gotchas
  What to build:
  - `mac/Info.plist` 2.48.0, build 71.
  - A What's New card for Codex in more places (sparkline, Desk pulse, Combine chip, Trends), with its
    `WhatsNewTests` lines. It may name Codex, not Antigravity.
  - CHANGELOG `## Unreleased (mac)` above `## v2.47.1-mac`, with New and Changed (the hook rename).
  - The `mac/README.md` touch-ups.

  Size S.
  Acceptance: Done when `WhatsNewTests` pin the card and the CHANGELOG heading is right; the suite, the selftest
  and pytest pass.
  Verify: `cd mac && ./test.sh && ruby smoke/selftest.rb && python3 -m pytest -q integrations/tests`
  Stop class: go
