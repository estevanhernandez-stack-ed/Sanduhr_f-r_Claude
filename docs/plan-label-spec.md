# Plan label (Mac 2.34.0)

Spec, 2026-10-26. Owner calls in the dashboard decision of the same day: footer and Accounts, exact
Windows parity, the snapshot filled, the riffs folded in, demo mode shows a plan, ships as 2.34.0.
It can run unattended: no new network request, no new permission, no new file.

## What it is

The subscription plan of the signed-in account, as a short label: **Max ×20**, **Max**, **Team** or
**Pro**. Windows has shown it in the widget footer since the Python build (`plan.py`, PR #25), with a
tooltip that rotates through jokes for the Max plans. The Mac already asks `/api/organizations` for
the tracked org but keeps only `uuid` and `capabilities`, and `snapshot.json` carries `"plan": null`,
so Claude Code sessions (the MCP's `get_usage` `account.plan`, the statusline, the meters mod) get
nothing on a Mac.

## Non-goals

- No new request. The plan comes from the `/organizations` response the client already fetches.
- No guessing. A tier string the rules don't recognize shows no label, never a best guess.
- Not in the notch, the menu bar, the lock screen line or the Desk.
- No Windows change. Windows is the reference; any difference found goes to the Windows agent as a
  note, never as an edit under `windows-dotnet/`.

## Resolution: `PlanLabel` (Swift, pure, Foundation only)

A line-for-line port of `windows-dotnet/src/Sanduhr.Core/PlanLabel.cs`:

- `Organization` also decodes `rate_limit_tier` and `billing_type` (both optional; a missing or
  malformed field decodes as nil and never fails the org decode).
- `PlanLabel.resolve(rateLimitTier:billingType:capabilities:) -> PlanBadge?`, where
  `PlanBadge { name: String; riffs: [String] }`.
- Nil when the tier is nil or empty, or when the org is not a subscription.
  - **Subscription** means `billing_type == "stripe_subscription"`, or a capability among
    `claude_max`, `claude_pro`, `claude_team`.
  - With no billing type and no capabilities at all, it is lenient and counts as a subscription,
    as Windows is.
- Matching runs on the lowercased tier, in this order:
  1. `max_20x` → "Max ×20" (U+00D7)
  2. `max` → "Max"
  3. `team` → "Team"
  4. `pro` → "Pro"
  5. anything else → nil
- Riffs, the same lists in the same order:
  - Max ×20: "Plaid Max", "Maximum Overdrive", "Ridiculous Speed", "Galaxy Brain Max"
  - Max: "Maximum Effort", "Max Headroom", "Big Max"
  - Team and Pro: none
- Tests mirror `PlanLabelTests.cs` case for case: the four plans, prepaid, unknown future tier, nil,
  empty, a max-looking tier on a prepaid org, the lenient no-signal case, the riffs coming back as
  copies, and the two-org login where the API org gets no label.

## Where it shows

1. **Widget footer, the active account.** A small rounded capsule at the right of the footer,
   before Use Sonnet, in the footer's 9 pt rounded type and the theme's accent. This matches the
   Windows badge (`MainWindow.xaml` footer Border). It is hidden when the plan is nil, signed out,
   or before the first fetch. `.help` carries the tooltip: the first riff, rotating every 4 s while
   there are two or more (`RiffInterval`), or the plain name when there are none. Rotation stops
   when the widget is hidden, and with Reduce Motion on it shows the first riff only. The
   accessibility label is "Plan: <name>", never a riff.
2. **Settings → Accounts, every account.** The same name as small secondary text beside each
   account's name in its row and on its page. An account's plan is known once its org has been
   fetched: the active account's on every fetch, and a followed account's when the follower checks
   it. The resolved name (only the name) is kept per account in that account's preferences, next
   to its other per-account settings, so a row can show it before the next fetch. It is cleared
   with the account (Remove, Sign Out, Reset Sanduhr…) and is never stored for a key that isn't an
   account's. An account whose org was never fetched shows no plan text.

## The snapshot

`SnapshotWriter` writes the active account's resolved name into `plan` (null when nil), in the
main `snapshot.json` and in the per-seat `snapshot.<account_ref>.json` files the same way. This
mirrors Windows, which passes `PlanBadgeName` to `WriteOk`, `WriteError` and the seat meters. The
MCP server already returns `snap.get("plan")` as `account.plan`
(`mac/integrations/sanduhr_mcp.py:1335`), so no Python change is needed. Add a test that a fetch
on a Max ×20 org writes "Max ×20" and a prepaid org writes null.

## Demo mode

Demo mode shows a fixed invented plan for the sample accounts, for example "Max ×20" on the first
and "Pro" on the second, with the riffs. It never reads, shows or writes a real account's plan
while demo mode is on, and demo mode writes no snapshot, as today.

## Docs and release

- `docs/PRIVACY.md`, Mac section: the snapshot row lists the plan name, which is no longer always
  null, and the per-account preference holding the plan name is named.
- `mac/README.md`: a short Plan label section with the rule, where it shows, and the riffs.
- `docs/mac-smoke-test.md`: a new section covering the footer capsule and its tooltip, the
  Accounts text, demo mode, `smoke state` (a `plan` field on the active account, the name only),
  and `snapshot.json`'s `plan`.
- Smoke harness: `state.yaml` gains `plan` (name or null) for the active account. No riff, tier
  string or billing type goes in `state.yaml`.
- Ship as **2.34.0**: Info.plist 2.34.0, build 46. A What's New card "Your plan on the widget":
  the footer shows your plan, and Settings → Accounts shows each account's, destination
  `.accounts`. WhatsNewTests count +1, the 2.34.0 id list, its destination, and
  `updatingToTwoThirtyFour`. CHANGELOG under `## Unreleased (mac)`.

## Done when

- `swift test` passes with the PlanLabel parity suite, the decode tests (fields present, absent,
  wrong type), the snapshot test, the per-account cache tests (set on fetch, cleared on remove),
  and the WhatsNew lines.
- On a real Max ×20 account, checked by the owner: the footer shows "Max ×20" and its tooltip
  rotates, Accounts shows it, and the MCP's `get_usage` returns `account.plan: "Max ×20"`.
- Demo mode shows the sample plans and nothing real.
