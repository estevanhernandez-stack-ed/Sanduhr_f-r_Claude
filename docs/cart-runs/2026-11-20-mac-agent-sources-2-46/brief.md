# Run Brief: mac-agent-sources-2-46

## Ask
"let's get the new sources in", after the Mac was found to have neither part of Windows' Sources work. To the three
questions: **both, one release**; **vendor the zstd decoder**; **fixtures now, real later**. On the design and the
13-item list: "do it", which also approved downloading zstd v1.5.7's source from github.com/facebook/zstd.

## Source
- **Kind:** design doc, approved by the owner.
- **Location:** `docs/superpowers/specs/2026-11-19-mac-agent-sources-design.md`, which ports
  `2026-11-09-agent-layer-design.md` (Windows 4.8) and `2026-11-13-every-agents-activity-design.md` (Windows 4.9).
- **House style:** `mac/Sources/Sanduhr/Services/` (`UsageViewModel`, `HistoryStore`, `SnapshotWriter`,
  `AlertRules`, `Notifier`, `Vault*`, `CCLogReader`, `MCPAccess`), `Views/` (`WidgetCards`, `TierCardView`,
  `ProgressBarView`, `UsageSettings`, `UsageHistoryViews`), `Desk/DeskMeters.swift`, `Desk/DeskArrangement.swift`,
  `mac/integrations/sanduhr_mcp.py`, `sanduhr_statusline.py`. Windows parity: the files the design names under
  `windows-dotnet/src/`, read only.

## Intent
Someone pacing more than one agent's plan sees Codex's limits beside Claude's, and sees where their week went across
Claude Code, Codex and the Antigravity CLI, read from each agent's own files on this Mac. No sign-in, no network,
no prompt or reply read. A Mac with only Claude on draws exactly as 2.45.0.

## Done when
- Items 1 to 12 and 14 are merged on `cart/mac-agent-sources-2-46`; item 13 is built, checked and held.
- `cd mac && ./test.sh`, `ruby smoke/selftest.rb` and `python3 -m pytest -q integrations/tests` pass there.
- `./build.sh --debug` builds (the new C target included).

## Out of scope
- A real Codex or Antigravity read: neither is installed here (parked for a Mac that has one; smoke section 80
  carries the step).
- Agent notifications (Windows 5.0 spec 4), streamer mode, the Antigravity IDE, live Codex fetch, `auth.json`.
- The release (dispatch, appcast, cask), any push or PR.
- `windows-dotnet/`: read for parity, never edited.

## Inferred
- Base: `main` at `4a205621`, from the helper's init. The design doc was untracked in the owner's tree, so the
  integration branch carries a copy committed first (D2).
- Version 2.46.0 build 68 (from `mac/Info.plist` 2.45.0 build 67), a minor with What's New cards.
- Item 13 (Claude's read of agent project totals) is `hold`: it adds an access-consent switch (permissions).
- The design's 13 items became 14: the MCP consent split from the Local burn views so the hold doesn't hold the
  views (D3).

## Mode
- **Mode:** Unattended
- **Triggered by:** the ask named "automated" (as the 2.45 run); 14 items. Auto mode on for the session.
