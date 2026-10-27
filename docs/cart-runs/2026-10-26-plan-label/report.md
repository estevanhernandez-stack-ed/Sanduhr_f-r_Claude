# Run Report: plan-label

5 merged, 0 held, 0 parked, 0 stopped.

## Ready to push

Integration branch `cart/plan-label` (worktree `~/.cwt/sanduhr-f-r-clau-plan-label/int`), cut from `23b5701c`:

| Item | Commit | What |
|---|---|---|
| 1 | 2d220782 | `PlanLabel` and `PlanBadge`, ported from Windows; `Organization` decodes `rate_limit_tier` and `billing_type` |
| 2 | 9a4f14ad | `ClaudeAPI.plan()` from the cached org (no new request); `UsageViewModel.plan`; `plan` in `snapshot.json` and the active seat |
| 3 | 4e32c15d | `AccountPlans` (`accountPlans`, name per account); set by the active fetch and by Following; moves on rename; cleared on Sign Out and Remove; shown in Accounts rows and the detail header; carried in the per-seat snapshots |
| 4 | 50e0f787 | Footer `PlanCapsule` with riffs rotating every 4 s (Reduce Motion pins the first); demo plans; `plan` in `state.yaml` |
| 5 | be46ed8a, c8209eba | PRIVACY, README, smoke section 53, the 2.34.0 What's New card (`plan-label` → Accounts), CHANGELOG under `## Unreleased (mac)`; correction: only the MCP reads the plan |

Checks on the integration branch: `./test.sh` passed 1925 tests in 246 suites; `ruby smoke/selftest.rb` passed 1031 checks; `./build.sh --debug` built. The orchestrator re-ran every item's checks before merging it. The vibe-test and vibe-sec gates were not run; the repo's own suites stand in.

Not done, by design: mac/Info.plist (the ship commit), the release, any push.

To push:
```
git -C ~/Projects/Sanduhr_f-r_Claude push -u origin cart/plan-label
gh pr create --head cart/plan-label --title "feat(mac): the plan label (2.34.0)"
```

## Waiting on you

The live check, on a real Max ×20 account (spec § Done when):
- The footer shows "Max ×20", and its tooltip changes every 4 s.
- Settings → Accounts shows each account's plan.
- `snapshot.json` has `"plan": "Max ×20"`, and the MCP `get_usage` returns `account.plan`.
- Demo mode shows Work · Max ×20 and Personal · Pro, and nothing real.
- Idle CPU with the capsule rotating (D5): CPU seconds per minute, by the idle-cpu-measurement method.

## Parked

None.

## Stopped

None.

## Decisions to skim

`decisions.md`, D1 to D6. The ones worth a look:
- **D3:** a seat that is neither active nor followed shows its plan only from the cache.
- **D5:** a 4 s periodic TimelineView drives the riffs. Measure it.
- **D6:** the docs said the statusline and meters mod read the plan. They don't; only the MCP does. Fixed in PRIVACY, CHANGELOG, README and the spec.

Builder's tree at start: on `main`, clean.

## After the run: live check (2026-10-26)

Dev build of the integration branch, in demo mode:
- The footer shows Max ×20.
- Accounts shows Work · Max ×20 and Personal · Pro, and the detail header shows Max ×20.
- `state.yaml` has `plan`. With demo mode off and signed out, it is null.
- Idle CPU with the riffs rotating: 0.36% of one core over 180 s.

Fix 154d8d77: the capsule's white fill vanished on the light glass themes, so it now uses an accent tint.

Still open: the real Max ×20 account, including the MCP's `account.plan`, checked on the signed 2.34.0.
