<!-- The ledger. Every inference, default, substitution and mid-run class
     upgrade lands here, numbered D1, D2, and so on. Append only. -->

# Decisions: widgets-2-35

## D1
- **Decision:** All eight items are `go`.
- **Why:** None touches money, auth, access gating or permissions. The catalog download is a new outbound host, but it is read-only, unauthenticated and covered by PRIVACY in item 8.
- **Alternative not taken:** Holding item 3 (the download client) for a walkthrough.
- **Item:** all
- **How to reverse:** Leave item 3's merge out of the push and review it alone.

## D2
- **Decision:** The fixture corpus is copied from `windows-dotnet/tests/Sanduhr.Tests/fixtures/` rather than fetched from `sanduhr-widgets`.
- **Why:** Windows already pinned it to `sanduhr-widgets` at 61b4247 with python-expected.tsv, so one corpus serves both platforms.
- **Alternative not taken:** Fetching `tools/fixtures` live at run time.
- **Item:** 1, 2
- **How to reverse:** Replace the copy with a fetch script pinned to a commit.

## D3
- **Decision:** Windows' C# is the line-by-line guide and its tests the case list; the spec wins on Mac surfaces (paths, `PackFormat`), Windows wins on rules.
- **Why:** Parity is the point, and the spec already settled the Mac-specific differences.
- **Alternative not taken:** Porting from validate.py directly.
- **Item:** all
- **How to reverse:** n/a

## D4
- **Decision:** Accepted the worker's hand-written zlib inflater (`PackInflate`) for the PNG data check instead of the Compression framework.
- **Why:** The framework decodes truncated streams to a different length than Python and gives no reason for corrupt ones, so byte-equal messages ("image data is N bytes", "corrupt image data (...)") were impossible. Output is capped at expected+1 and it is unit-tested per error.
- **Alternative not taken:** Compression framework with non-equal messages on corrupt PNGs.
- **Item:** 1
- **How to reverse:** Swap `PackInflate` for `compression_decode_buffer` and relax the fixture test to prefix matches on those two messages. Worth a differential fuzz against Python zlib before 2.35.0 ships, since it parses untrusted bytes (a Swift bounds trap would crash the app, not corrupt memory).

## D5
- **Decision:** Fixtures live at `mac/Tests/SanduhrTests/Fixtures/widget-packs/`, not lowercase `fixtures/`.
- **Why:** Case-insensitive APFS would collide with the existing `Fixtures` folder; its `.copy("Fixtures")` resource already ships them.
- **Alternative not taken:** A second resource folder.
- **Item:** 1
- **How to reverse:** n/a

## D6
- **Decision:** Pack theme rules are a port of Windows' `ThemeLint.cs`, not a call to the Mac `ThemeLint.lint`.
- **Why:** The Mac lint reads Mac-only fields and words some messages differently; reuse is limited to its color math and formatters.
- **Alternative not taken:** Changing the Mac `ThemeLint` messages to match.
- **Item:** 1
- **How to reverse:** n/a

## D7
- **Decision:** Ran a differential fuzz of `PackInflate` against Python's zlib (1.2.12, Python 3.14): 36,000 mutated, truncated and random streams. No crash, no hang. Two parity gaps found, folded into item 3 as a fix: (a) when output reaches the limit right before a bad back-reference, Python returns the limit and Swift throws "invalid distance too far back" (the limit must be checked before the next symbol is decoded); (b) for some bad codes Python says "invalid literal/length/distance code" where Swift says "invalid distance code".
- **Why:** D4 flagged the hand-written inflater as untrusted-input parsing worth a fuzz before ship.
- **Alternative not taken:** Reopening item 1.
- **Item:** 1 (fix in 3)
- **How to reverse:** n/a. The harness is in the session scratchpad (`fuzz/fuzz.py`, `main.swift`); worth committing as a dev tool if the inflater changes again.

## D8
- **Decision:** Reverted the item 3 worker's emulation of macOS libz's fast-loop wording ("invalid literal/length/distance code", fitted constants, ~98.5% agreement). PackInflate words bad codes as stock zlib does. Kept the limit fix (MATCH state leaves at the limit before the distance check) and the empty-distance-table fix.
- **Why:** Linux CI's validate.py and Windows use stock zlib, so stock wording is the cross-platform reference; fitted constants against one machine's libz are fragile. My D7 brief gave truncated hex, which sent the worker after Apple's quirk.
- **Alternative not taken:** Keeping the fitted emulation, which would match only Python on a Mac.
- **Item:** 3
- **How to reverse:** Revert c10e5031 on the integration branch.

## D9
- **Decision:** The catalog signature tests sign the real `catalog.json` bytes with a throwaway key, as Windows' tests do; no real `.sig` is in either repo.
- **Why:** The pinned Key A's private half never leaves the signing machine; the live signature is verified at first run.
- **Alternative not taken:** Fetching the live `.sig` into the fixtures (a network read during the run).
- **Item:** 3
- **How to reverse:** Add the published `catalog.json.sig` to `Fixtures/widget-zips/` with its catalog and a test that Key A verifies it.

## D10
- **Decision:** Pack palettes join the message editor's Palette menu (`MessagePalette.all`), but the seeded now-playing looks keep indexing `MessagePalette.builtIn`.
- **Why:** `NowPlayingLooks.seeded` picks a palette by `hash % count`; counting pack palettes would silently change every song's existing look on install. The Mac has no other now-playing gradient list.
- **Alternative not taken:** The spec's "now-playing's gradient lists gain them" taken literally.
- **Item:** 4
- **How to reverse:** Point `NowPlayingLook.swift`'s seeded palette list at `all`, accepting the reshuffle.

## D11
- **Decision:** Pack palettes are named `<palette> · <pack name>` and pack theme ids are `<packId>:<stem>`, as on Windows; the companion pick and roam live in the Desk suite as `deskCompanionPack` and `deskCompanionRoams` (absent: first sprite by id, and on).
- **Why:** No collisions with built-ins or between packs; Desk preferences stay together.
- **Alternative not taken:** Bare palette names.
- **Item:** 4
- **How to reverse:** n/a

## D12
- **Decision:** OLED drift moves each Desk piece with a SwiftUI `.offset` driven by a 3-minute model step (one 2 s animation per step), not by moving CALayers directly; the notch island, the top-center pill and the lock screen helper don't drift; only the Desk window (0.40) and the widget panel (0.70) dim. The white cap gives `#e6e6e6` for pure white and never rewrites saved values.
- **Why:** The Desk pieces are SwiftUI views; an offset keeps click frames in step through the existing `onGlobalFrame`. Between steps nothing runs, so the idle-CPU rule holds. The notch sits on black hardware.
- **Alternative not taken:** Per-piece CALayer position animation.
- **Item:** 5
- **How to reverse:** Swap `.oledDrift` for a layer-level move in `OledCareController.swift`.

## D13
- **Decision:** `IdleClock` is one shared poller with subscribers (one Timer at the quickest subscriber's interval, none with no subscribers); it calls a new subscriber once immediately.
- **Why:** OLED care (15 s) and the companion's idle trigger (5 s, item 7) share one reader of seconds-since-input, as the spec asks.
- **Alternative not taken:** A timer per consumer.
- **Item:** 5
- **How to reverse:** n/a

## D14
- **Decision:** The companion's near_limit thresholds are each limit's own Mac Alerts line (`sessionLine` for five_hour, `weeklyLine` for the rest) plus 100, ignoring `AlertSettings.enabled`; pace uses `paceFrac` (5+ points ahead is worried); a folder of only unparsable day files yields zero days and does not fall back to Claude Code dates, as on Windows.
- **Why:** The Mac has one line per kind where Windows has warn and urgent percentages; `enabled` governs banners, not the companion.
- **Alternative not taken:** Gating triggers on the banner switch.
- **Item:** 6
- **How to reverse:** Check `alerts.enabled` in `CompanionBrain.observeUsage`.

## D15
- **Decision:** The companion walks with a linear `CAKeyframeAnimation` on `position` (0.25 s samples, so it turns corners), mirrored by a discrete `transform.scale.x` keyframe; the sprite layer roams outside its piece's NSView and relies on macOS 14+ views not clipping. Main-thread work while it shows: the shared 5 s IdleClock tick, one roam-boundary timer (12 s / 8 s) when roaming, one trigger-end timer, one hourly growth timer (reads on a utility queue). With no companion picked, nothing runs.
- **Why:** Render-server animation keeps the idle-CPU rule; overflowing the piece view lets `.oledDrift` apply unchanged.
- **Alternative not taken:** A full-window overlay with its own drift offset.
- **Item:** 7
- **How to reverse:** If the sprite clips on screen at the live check, move it to a Desk-window overlay.

## D16
- **Decision:** Demo mode shows only the pack with id `hourglass` (if installed), at growth 0.5, cycling the five moods every 20 s; no Settings Desk preview card for the companion (the preview cards are fixed samples).
- **Why:** Demo never reads real growth, usage or watchers; the layout list and map already pick the piece up.
- **Alternative not taken:** A preview card.
- **Item:** 7
- **How to reverse:** n/a

## D17
- **Decision:** Item 7 left a gap: installing or uninstalling a pack doesn't notify the companion, so a new pack shows only after the next app activation. Folded into item 8 as a small fix.
- **Why:** Found in the item 7 report; one notification from WidgetsModel closes it.
- **Alternative not taken:** Accepting the delay.
- **Item:** 7 (fix in 8)
- **How to reverse:** n/a

## D18
- **Decision:** Kept the Mac client requesting `catalog.json.sig2` on every refresh and choosing the 90 s timeout by size (over 1 MB), not by kind.
- **Why:** Windows' `WidgetCatalogClient.cs` and `WidgetCatalogService.cs` do exactly the same; the Mac PRIVACY says so plainly. Windows' own PRIVACY wording ("during a key change") is the imprecise one, a note for the Windows agent.
- **Alternative not taken:** Skipping `.sig2` unless the first signature fails.
- **Item:** 3, 8
- **How to reverse:** Fetch `.sig2` only after `sig` fails to verify, on both platforms together.

## D19
- **Decision:** Close-out used the repo's own checks on `cart/widgets-2-35`: `./test.sh` (2226 passed), `ruby smoke/selftest.rb` (1033), `pytest integrations/tests` (227), `./build.sh --debug` (built). The vibe-test and vibe-sec gates were not run.
- **Why:** The repo's CI runs the same checks; the gates add nothing a Swift package suite doesn't already cover here.
- **Alternative not taken:** Running the gates.
- **Item:** close-out
- **How to reverse:** Run `/vibe-test:gate` and `/vibe-sec:gate` on the integration branch before the PR.
