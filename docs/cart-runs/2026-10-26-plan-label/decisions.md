# Decisions: plan-label

## D1
- Decision: Run unattended with five go items in dependency order (1 → 2 → 3 and 4 → 5).
- Why: The ask said unattended, and nothing touches money, auth, secrets or deploys. The plan name is a display label, not a gate.
- Alternative not taken: hold item 3, since it touches account preferences. Rejected: it stores a display string, not a credential.
- Item: all
- How to reverse: re-class an item in state.json through the helper, then re-run.

## D2
- Decision: Add the What's New card now, with version "2.34.0", while Info.plist stays at 2.33.1.
- Why: The owner asked for the card and its tests in the run, and for no Info.plist bump. The ship commit moves the version after review.
- Alternative not taken: leave the card to the ship commit, as 2.33.0 did.
- Item: 5
- How to reverse: drop release2340 and its test lines.

## D3
- Decision: A seat that is neither active nor followed shows its plan only from the cache. The inactive-seat round's `UsageFetching` client gets no `plan()`.
- Why: The spec allows no new requests, and the cache fills whenever an account is active or followed. Adding `plan()` to the protocol would widen item 3.
- Alternative not taken: add `plan()` to `UsageFetching` so the inactive round resolves it too. That costs no request, since the org is cached per client.
- Item: 3
- How to reverse: add `plan()` to `UsageFetching` and call it in `SeatMeters.fetch`.

## D4
- Decision: The account row's accessibility element changes from `.combine` to `.ignore` plus an explicit label ("Work, Max ×20, Active").
- Why: The row holds only text, with no controls, so nothing becomes unreachable, and the plan reads as part of the account.
- Alternative not taken: keep `.combine` and let the plan text be read in view order.
- Item: 3
- How to reverse: restore `.combine` in AccountRow.

## D5
- Decision: Riff rotation runs on a `TimelineView(.periodic(by: 4))` scoped to the footer capsule. It exists only with 2 or more riffs and Reduce Motion off, so Max ×20 and Max accounts redraw one small Text every 4 s while the widget is visible.
- Why: It is the cheapest correct driver, matches Windows' 4 s riff timer, and builds nothing for Pro or Team. Idle CPU was fixed in 2.31 by removing an `.animation`-schedule TimelineView; a 4 s periodic one is a different order of cost.
- Alternative not taken: change the tooltip only on hover, or once per refresh. Rejected for Windows parity.
- Item: 4
- How to reverse: drop the TimelineView branch in `PlanCapsule` and show the first riff only.
- Follow-up: measure idle CPU on a Max account in the live check (cputime per minute, `idle-cpu-measurement` method).

## D6
- Decision: The plan reaches Claude Code only through the MCP's `get_usage` (`account.plan`). The PRIVACY, CHANGELOG, README and spec text claiming the statusline and meters mod read it was corrected on item 5's branch (a4dcc02).
- Why: `rg` over mac/integrations shows that only sanduhr_mcp.py reads `plan`. Privacy copy must be exactly true. The claim started in the spec and the worker carried it over.
- Alternative not taken: teach the statusline or meters mod to show the plan. That's a new feature, out of scope here.
- Item: 5
- How to reverse: n/a (a correction). A future statusline plan segment would add the claim back.
