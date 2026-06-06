# Subscription Tier Badge — Design Spec

- **Date:** 2026-06-06
- **Status:** Design approved (brainstorming) — pending spec review, then build
- **Repo / branch:** `Sanduhr_f-r_Claude` (Windows PySide6 build) · `feat/subscription-tier-badge` off `origin/main` (37f5c5b, #24)
- **Surface:** Windows MS Store product (the live distribution). Mac/Python parity is a later follow-up, not this PR.

## 1. Goal

Show the user which Claude **subscription tier** they're on (Pro / Team / Max / Max ×20) in the widget footer, so the display makes it instantly clear *what plan's usage they're looking at*. The plan name is the clear, store-safe visible label; a rotating tongue-in-cheek "easter egg" rides in the hover tooltip for delight.

## 2. Finding — the tier is already in a response we fetch

`GET /api/organizations` (already called in `api.py:_get_org_id`, line 83) returns the org object. We currently keep only `orgs[0]["uuid"]` and discard the rest. The plan lives there:

- **`rate_limit_tier`** — primary signal. Confirmed value on a live Max ×20 account: `default_claude_max_20x`. Distinguishes 20x from 5x **in the string itself**.
- **`billing_type`** — `stripe_subscription` for a claude.ai subscription vs `prepaid` for an API/console org. Gates "is this a subscription at all."
- **`capabilities`** — e.g. `["claude_max", "chat"]` (subscription) vs `["api", "api_individual"]` (API org). Corroborator.

The account also returns a second org (API/prepaid, `auto_prepaid_tier_0`). The app already tracks `orgs[0]`, which is the subscription org for this user, so usage + tier read consistently. **We capture tier from the same org `_get_org_id` already selects** — no change to org selection in this PR (see §9).

## 3. Detection logic

In `api.py`, when the org is resolved, capture the selected org's `rate_limit_tier`, `billing_type`, and `capabilities` alongside `uuid`. Expose them (e.g. store on the client + return in/alongside the usage dict). Parse **defensively** — substring match, not a brittle exact-key table, because we've only directly observed `default_claude_max_20x` and `auto_prepaid_tier_0`; the other plan strings are best-guess:

| Match (within `rate_limit_tier`), gated on `billing_type == "stripe_subscription"` | Display |
|---|---|
| contains `max_20x` | **Max ×20** |
| contains `max_5x` | **Max** |
| contains `pro` | **Pro** |
| contains `team` | **Team** |
| anything else / `prepaid` / `api` / missing | **None** → badge hidden |

## 4. Mapping module — `plan.py` (new, pure)

A pure, Qt-free, network-free module so it's fully unit-testable.

```
plan_label(rate_limit_tier, billing_type, capabilities) -> PlanBadge | None
```

`PlanBadge = (display_name: str, riffs: list[str])`. Returns `None` when not a recognized subscription (badge hidden).

Easter-egg riff collections (the rotating tooltip set):

- **Max ×20:** Plaid Max · Maximum Overdrive · Ridiculous Speed · Galaxy Brain Max
- **Max:** Maximum Effort · Max Headroom · Big Max
- **Pro / Team:** no riffs for v1 (clean name only) — lists left empty, trivially extensible later.

## 5. Display — footer badge + rotating tooltip (`widget.py`)

- New `self._plan_lbl` (QLabel), inserted in the footer layout (`widget.py:337-368`) **after the stretch, before the "Use Sonnet" button**. Its own label so it doesn't crowd the "Updated … | Pinned" status segment.
- **Badge text = real plan name** (`Max ×20`). Clear, store-safe. The MS Store cert history (prior 10.1.4.4 "navigation/clarity" rejection) is why the *visible* label stays unambiguous.
- **Tooltip = `"<plan name> — <riff>"`**, rotating through the riff collection so it's "something different each time they go through it." Rotation state (`self._plan_riff_idx`) advances on each footer refresh and on panel show; round-robin (wraps), so every riff eventually appears. When `riffs` is empty (Pro/Team), tooltip is just the plan name.
- Hidden entirely (`setVisible(False)`) when `plan_label` returns `None`.
- Styling: match existing footer label idiom (same QSS `#Footer` treatment, theme-aware); subtle, not shouty.

## 6. Fallback

v1: if tier isn't a recognized subscription, **hide the badge** — no clutter, no wrong label. The manual-pick override (brainstorm option C) is **deferred** — only build it if a real account surfaces where the field is genuinely absent. YAGNI.

## 7. Tests — `windows/tests/test_plan.py`

Pure-function coverage of `plan_label`:

- `default_claude_max_20x` + `stripe_subscription` → (`Max ×20`, 4 riffs)
- `default_claude_max_5x` + `stripe_subscription` → (`Max`, 3 riffs)
- `*pro` / `*team` + `stripe_subscription` → (`Pro`/`Team`, [])
- `auto_prepaid_tier_0` + `prepaid` → `None`
- API caps / missing field / unrecognized string → `None`
- **Real-payload fixture** (`windows/tests/fixtures/organizations_sample.json`) — built from the live response, **with `uuid` values and the email in `name` redacted** (`XXXX…`). Pins the parse to a real shape. Real plan string `default_claude_max_20x` is retained (not sensitive); identifiers are not.
- Light test that the riff index advances + wraps.

## 8. Files touched

- `windows/src/sanduhr/api.py` — capture `rate_limit_tier` / `billing_type` / `capabilities` on org resolution; expose.
- `windows/src/sanduhr/plan.py` — **new** pure mapping module.
- `windows/src/sanduhr/widget.py` — footer `_plan_lbl` + rotation in the refresh path.
- `windows/tests/test_plan.py` + `windows/tests/fixtures/organizations_sample.json` — **new**.
- `CHANGELOG.md` — entry under next version.

## 9. Out of scope (explicit)

- **Sign-out / clear-credentials button** — separate deferred feature.
- **"Find your sessionKey" / Import-from-browser helper** — separate feature, its own spec + PR right after this one.
- **Smarter org selection** (prefer the `stripe_subscription` org over `orgs[0]` for multi-org users) — a real latent robustness improvement, but it touches the core usage path; flagged, not done here, to keep this PR small.
- **Mac / Python parity** for the badge — follow-up.

## 10. Build & safety notes

- **One PR**, small diff, on `feat/subscription-tier-badge`. No change to usage fetching, theming, or the parked themes/audio WIP (`stash@{0}` on `chore/store-visuals-v2.3.0`).
- **GitNexus:** repo convention mandates impact analysis before editing symbols, but the GitNexus MCP server is **not connected this session**. Compensate with manual blast-radius checks (grep callers of `_get_org_id` / `get_usage` / `_update_footer`) before editing. A PostToolUse hook re-runs `npx gitnexus analyze` on commit.
- **Secrets:** the org payload contains real org `uuid`s + the account email — these are **redacted** in the committed fixture. No `sessionKey` is ever in scope here.
- TDD: write `test_plan.py` first against `plan.py`'s contract, then implement.
