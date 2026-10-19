<!-- The ledger. Every inference, default, substitution and mid-run class
     upgrade lands here, numbered D1, D2, and so on. Append only. -->

# Decisions: no-notch-island

## D1
- **Decision:** "1" is read as the no-notch island (issue #119) with the two reflection debts folded in, exactly as the offer after 2.27.0 proposed.
- **Why:** The reply "start 1 unattended" answered that offer; the offer's item 1 named the island and said it would fold the two debts into its spec.
- **Alternative not taken:** Issue #119 alone, leaving the Mac scope and the spec note for another run.
- **Item:** run
- **How to reverse:** Drop item 1 from the list; the three pill items stand alone.

## D2
- **Decision:** Main screen only, and the menu-collision rule ships conservative: a width cap of 0.36 of the screen and hide while the pointer is in the menu bar. The real-hardware decision stays on the issue.
- **Why:** This Mac has a notch, so no rule can be measured on real no-notch hardware tonight; the issue itself allows "a conservative max width and hide when the pointer is over the bar".
- **Alternative not taken:** Reading other apps' menu extents (needs Accessibility) or every notch-less screen in the first cut.
- **Item:** 2, 3
- **How to reverse:** Change `NotchPill.maxWidthFraction` and the `shows` rule once measured; a per-screen pill is a follow-up item.

## D3
- **Decision:** The pill is tested on this notched Mac through a debug force key (`notchPillForce`, hook `notch-pill force`), never by faking `cameraNotch`.
- **Why:** The island's own code path must stay untouched on a notched Mac; a force key is additive and the smoke tools can drive it.
- **Alternative not taken:** An environment variable, which the smoke harness cannot flip at runtime.
- **Item:** 2, 3, 4
- **How to reverse:** Remove the key and the hook once a no-notch Mac is in the loop.

## D4
- **Decision:** The smoke scenario is written and fixture-checked but not run against a live dev build in this run.
- **Why:** A dev build on this Mac shares the release app's settings and support folder and runs signed out; the owner backs the folder up by hand around every dev session (CLAUDE.md, Gotchas, Mac). An unattended run should not quit the release app and touch that folder.
- **Alternative not taken:** Launching the dev build with the hooks on and running `smoke run scenarios/notch-pill.yaml`.
- **Item:** 4
- **How to reverse:** In the morning: back up the support folder, launch the dev build, `smoke run scenarios/notch-pill.yaml`, restore, relaunch the release app.

## D5
- **Decision:** Cart's session log, friction log and wins log are not written by this run; commits carry the session attribution lines instead.
- **Why:** The plugin's data path resolves to `~/.claude`, the work seat on this Mac, and nothing from a personal project is written there (the 2026-10-17 runs, D5 of polish-ux).
- **Alternative not taken:** Writing the logs to the work seat's folder.
- **Item:** run
- **How to reverse:** Point the plugin's data path at the personal seat and backfill with `/vibe-cartographer:reconnect`.

## D6
- **Decision:** `NotchWingsView` gained a `pill: Bool` parameter (default false) that joins the wings into one capsule with a 12-pt gap where the camera cut-out was, instead of a separate pill view.
- **Why:** The wing content, the click to Settings and the two-finger menu stay one code path; a second view would have duplicated all three.
- **Alternative not taken:** A `NotchPillView` of its own.
- **Item:** 3
- **How to reverse:** Split the capsule branch out into its own view; the `pill` flag marks every line that differs.

## D7
- **Decision:** With the pill up, every glow (alerts, meetings, pulses, Claude Code) outlines the pill, ranked after the island and before the plain notch, and the camera light places itself on the pill's frame.
- **Why:** The island's rule is "every glow outlines the island"; the pill stands in for the island on that Mac. The old top fallback was a fixed 200-pt spot unrelated to any drawn shape.
- **Alternative not taken:** Only Claude Code's top-fallback glow moving to the pill.
- **Item:** 3
- **How to reverse:** Narrow `NotchGlowLayout.shape`'s `hasPill` branch to the Claude Code glow.

## D8
- **Decision:** The snapshot's `notch_pill` map keeps the key `on`, which the YAML emitter quotes as `"on"` (a YAML reserved word); the fixture carries the quoted form and the selftest reads a string key.
- **Why:** The brief named the keys; renaming to `enabled` would have broken the symmetry with the other maps' `on` keys only after the fact.
- **Alternative not taken:** Renaming the key to `enabled`.
- **Item:** 3, 4
- **How to reverse:** Rename in `DebugSnapshot.swift`, the fixture, the selftest and the scenario together.

## D9
- **Decision:** The camera and mic badge on a no-notch Mac still places itself at the top center (`AVIndicatorPlacement.spot` ties islandUp to the wings window), so with the pill up it can overlap the pill. Left for a follow-up, recorded in BACKLOG by item 4.
- **Why:** `AVIndicatorTests` had to pass unchanged in this run, and the pill view already carries the beside-wing slot, so the fix is a placement change with its own tests.
- **Alternative not taken:** Treating the pill as islandUp in this run.
- **Item:** 3
- **How to reverse:** Not applicable; it is a deferral.

## D10
- **Decision:** The close-out gates were replaced by the repo's own checks on the integration tip (build, the full Swift suite, the smoke selftest, pytest) plus a grep over the run's diff for secret shapes and home paths.
- **Why:** The vibe-test and vibe-sec gates are not runnable on this Mac (run 1 of 2026-10-17, D12: the CLI produced no output), and the in-house scanner's scratchpad copy from that run is gone.
- **Alternative not taken:** Skipping the secret check.
- **Item:** run (close-out)
- **How to reverse:** Run the gates on a machine where they work before the PR merges.
