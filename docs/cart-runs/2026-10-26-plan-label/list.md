# Run List: plan-label

- [ ] **1. PlanLabel ported from Windows, and the org decodes its tier**
  Spec ref: `docs/plan-label-spec.md > Resolution`
  What to build: `Organization` (mac/Sources/Sanduhr/Models/UsageData.swift) decodes optional `rate_limit_tier` and `billing_type`, defensively: a missing or wrong-typed field is nil and never fails the decode. A new pure Foundation-only `PlanLabel` + `PlanBadge` (mac/Sources/Sanduhr/Models/PlanLabel.swift), ported line for line from windows-dotnet/src/Sanduhr.Core/PlanLabel.cs (read it, never edit it): the subscription gate, lenient when there are no signals, max_20x → "Max ×20" (U+00D7), then max, team, pro, else nil, with the same riff lists in the same order, returned as copies. Size S.
  Acceptance: Done when a new swift-testing suite mirrors windows-dotnet/tests/Sanduhr.Tests/PlanLabelTests.cs case for case, decode tests cover the fields present, absent and wrong-typed, and `Organization.tracked` is unchanged.
  Verify: `cd mac && ./test.sh` passes.
  Stop class: go

- [ ] **2. The active account's plan in the view model and in snapshot.json**
  Spec ref: `docs/plan-label-spec.md > The snapshot`
  What to build: Keep the tracked org's resolved PlanBadge for the active account. ClaudeAPI caches only the org uuid today, so expose the tracked Organization, or its badge, from the same single `/organizations` call, with no extra request. `UsageViewModel` publishes `plan: PlanBadge?`, cleared on sign-out, account switch and demo. `SnapshotWriter.writeOk`, `writeError` and `writeSignedOut` take the plan name and write it into `plan` (signed out writes null). The per-seat snapshots (SeatSnapshots) carry their own account's plan where it is known, else null. Size M.
  Acceptance: Done when tests show a Max ×20 org writes "plan": "Max ×20", a prepaid org writes null, signed out writes null, and no new network call exists (one /organizations per client, as before).
  Verify: `cd mac && ./test.sh` passes.
  Stop class: go

- [ ] **3. Each account's plan in Settings, Accounts**
  Spec ref: `docs/plan-label-spec.md > Where it shows, 2`
  What to build: Store the resolved plan name (the name only) per account, next to that account's other per-account preferences (see AccountData / AccountRegistry), when its org is fetched: by the active client, and by AccountFollower when following. Clear it with Remove Account, Sign Out and Reset Sanduhr (Reset already removes both preferences domains; confirm and test). Show it as small secondary text beside the name in AccountRow and on the account's page. Nothing shows when unknown. Demo mode shows the sample plans (item 4 defines them) and never a stored real one. Size M.
  Acceptance: Done when tests cover set on fetch, set by the follower, cleared on remove and sign out, never written for a key that isn't an account's, and the row shows nothing when unknown.
  Verify: `cd mac && ./test.sh` passes.
  Stop class: go

- [ ] **4. Footer capsule with rotating riffs, demo plan, smoke state**
  Spec ref: `docs/plan-label-spec.md > Where it shows, 1; Demo mode`
  What to build: In FooterView, a capsule before Use Sonnet: 9 pt rounded semibold in the accent color on a faint fill, like the Windows footer Border. Hidden when the plan is nil, signed out or before the first fetch. `.help` shows the current riff, rotating every 4 s while there are 2 or more (no timer otherwise), stopping while the widget is hidden, and with Reduce Motion it stays on the first riff. With no riffs the help is the name. Accessibility label "Plan: <name>". Demo mode: invented plans, Max ×20 for the first sample account and Pro for the second, with riffs, never real. Debug hooks: `state.yaml` gains `plan` (name or null) for the active account, and update the smoke selftest fixture if it pins keys. Split the body if the type-checker complains (CI type-checks on macos-15). Size M.
  Acceptance: Done when tests cover visibility rules, the rotation helper (pure: index advance, Reduce Motion, single riff), the demo plans, and the state.yaml field, and `ruby smoke/selftest.rb` passes.
  Verify: `cd mac && ./test.sh && ruby smoke/selftest.rb` pass.
  Stop class: go

- [ ] **5. Docs, the 2.34.0 What's New card, CHANGELOG**
  Spec ref: `docs/plan-label-spec.md > Docs and release`
  What to build: docs/PRIVACY.md Mac section: the snapshot's plan name, and the per-account plan preference. Find the Mac snapshot text and keep every claim exactly true to the code. mac/README.md: a Plan label section. docs/mac-smoke-test.md: a new numbered section for the footer, tooltip, Accounts, demo, `smoke state` plan, and snapshot.json plan. WhatsNewCards: a `release2340` card, id "plan-label", title "Your plan on the widget", body under the house length, art `.symbol(...)`, destination `.accounts` (check the enum's case name), prepended to the table. WhatsNewTests: count +1, version list, the 2.34.0 id list, the destination map, and `updatingToTwoThirtyFour`. CHANGELOG: a `## Unreleased (mac)` section inserted right after `# Changelog` at the top. Do NOT touch mac/Info.plist. Size S.
  Acceptance: Done when the WhatsNew tests pass with the 2.34.0 card and the docs describe only what items 1-4 built.
  Verify: `cd mac && ./test.sh` passes.
  Stop class: go
