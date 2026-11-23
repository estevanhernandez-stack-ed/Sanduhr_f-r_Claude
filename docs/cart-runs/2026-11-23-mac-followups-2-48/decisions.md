# Decisions: mac-followups-2-48

## D1
- **Decision:** 2.48.0 build 71, a minor with one What's New card. The list comes straight from the earlier runs'
  follow-ups, without a design pass.
- **Why:** Items 1 to 4 are user-visible features, and each item was already defined in an earlier ledger.
- **Alternative not taken:** 2.47.2 without a card.
- **Item:** all
- **How to reverse:** Info.plist and the card.

## D2
- **Decision:** All six items go to bulk-tier workers at high effort.
- **Why:** Each is small and bounded, and none changes stored data or a shared contract. Item 3 keeps old
  statuslines byte-identical, which a test pins.
- **Alternative not taken:** judgment tier for item 3.
- **Item:** all
- **How to reverse:** none needed.

## D3
- **Decision:** Desk pulses are keyed by source and limit. This reverses 2.46's D9 (Codex never pulses) and D12.
  A Codex alert with Desk delivery pulses the meter pieces that show Codex: a Codex piece, or a Combined row
  joined with Codex.
  - With Desk-only delivery and no such piece, it stays a banner.
  - Combined rows now pulse for Claude alerts too; before, they had no pulse hook.
  - Quiet hours still leave pulses alone.
  - A Claude-only Mac pulses exactly as before (pinned).
- **Why:** The owner listed this follow-up. The banner fallback keeps a Codex alert from going silent where no
  piece can show it.
- **Alternative not taken:** pulse anyway with only the notch glow; or pulse Combined pieces for Codex only.
- **Item:** 2
- **How to reverse:** `DeskPulses.swift`, `Notifier.deskCanPulse`.

## D4
- **Decision:** Combine's `--mine` gains `codex`, a chip, and `nocodex`, a marker.
  - **Old commands:** a list with neither name keeps 2.46's rule (cx shows when session or weekly is in the list),
    so every old command prints and rewrites the same bytes (pinned for 13 old lists).
  - **What the sheet writes:** the smallest form.
  - **When the chip is offered:** always, like the Account chip; its caption says when cx shows.
  - **Last-meter guard:** the "keep one" guard ignores Codex.
  - **One behavior change:** a sheet with Session and Weekly dropped now keeps Codex until its own chip is
    switched off.
- **Why:** Turning Codex off on its own needs an explicit marker, because a missing name has to keep its 2.46
  meaning.
- **Alternative not taken:** a separate `--no-codex` flag.
- **Item:** 3
- **How to reverse:** `StatuslineCombine`, `sanduhr_statusline.py`.

## D5
- **Decision:** In Trends:
  - Agent roots with tokens in the span stack into the weeks.
  - A week reads "no record" only when every contributing source has a gap that week.
  - Idle agent roots change nothing.
  - Trends shows agents alone when there's no Claude record (D16).
  - Top projects merge by name with shares.
  - The footer's "History kept since" date uses the earliest contributing root.
  - No Skills caption: Trends has no Skills list.
  - A Claude-only Trends is pinned as in 2.47.1.
- **Why:** Parity with Overview (2.46 item 12). Reading every root's gaps would have marked every week before an
  agent began as missing.
- **Alternative not taken:** passing every root to `readWeeks`.
- **Item:** 4
- **How to reverse:** `UsageTrends.build`, `UsagePageLoader.trends`.
