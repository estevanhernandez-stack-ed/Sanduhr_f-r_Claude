# Run Report: mac-parity-3-6-0

**6 of 7 items built, 1 dropped on its spike verdict.** Attended, on `feat/mac-parity`, 2026-10-21 13:44-15:50. Nothing pushed.

| # | Item | Result | Commits |
|---|---|---|---|
| 1 | Under-icons spike | Verdict written (spec 5.0b): pixels below the icons only with opaque paint | `cf03a19`, `bc25546` |
| 2 | Tour: Desk card + Desk-only tour | Built | `f326f53`, `af39be9` |
| 3 | Plate behind Desk text | Built (orchestrator fix: off written as false) | `b88bbb3`, `1198973` |
| 4 | Quit in Settings + hide/show while arranging | Built (fix: Done waits while every piece is hidden) | `f23dede`, `342fe52`, `37d996f` |
| 5 | Statusline folder label | Built; nothing stored | `7b2e37e` |
| 6 | Desk under desktop icons | **Dropped** by Este: needs an opaque second paint path | — |
| 7 | Watchers + Desk-message MCP tools (hold) | Built; allowlist 11 → 17 approved by Este, then committed | `b5c6411` |

**Checks at close:** `dotnet test windows-dotnet` green, Sanduhr.Tests 2082/2082, Sanduhr.Mcp.Tests 180/180. smoke-mcp passed on a trimmed temp publish.

## What the orchestrator caught in worker reports
- Item 2: the Desk-only tray card said the hourglass opens Settings; a left-click toggles the widget. Copy corrected.
- Item 3: turning the plate off removed the key, so a re-seed could switch it back on. Off is now written as false, with a test.
- Item 4: hiding every piece and pressing Done saved a blank layout that reads back as Standard. Done now waits.
- Item 1: the earlier "under icons is fragile on 24H2" claim was wrong; the real limit is clicks and per-pixel alpha. Rainmeter measured above the icons, not under them.

## Not checked on screen (needs the pre-release sideload)
- The tour windows (full and Desk-only), the plate's look in light and dark ink, the Quit button and arrange Hide/Show/Hidden strip.
- Item 7 live: a real `watch_start`/`update`/`end`, the propose/Add round trip, and `get_desk_messages` on a Store install (MSIX virtualization of `messages.txt`, unmeasured).

## Decisions logged (dashboard, project FX7zznEFlzArQ3CTY7RU)
`jKcxlY6KNcch787LzH5y` scope · `NAXBbyspkoqoEDXI8bv6` under-icons mechanism · `bAxUAPUySq2z3VnEIreE` spike gotcha · `er9ARz71ZsYcap8YZFcK` drop · `rEja4MJlDHSKQ35d0UjD` plate · item 7 trust boundary.

## Next
Push `feat/mac-parity` and open a PR (on Este's word). Then the 3.6.0 release: version lockstep, CHANGELOG and release notes, What's New cards for the new features, Store copy, and the sideload visual pass above.
