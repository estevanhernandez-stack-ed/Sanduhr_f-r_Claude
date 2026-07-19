# Scoped-limits wave — Fable bar, generic limits[] synthesis, org selection, CF-on-200 (design)

Approved scope "A" 2026-07-19, from the ultracode usage-API audit
([usage-api-audit-2026-07-19.md](../../usage-api-audit-2026-07-19.md)). Urgency: Fable goes
standard on Max/Team Premium **July 20** and the promo limit boost ends the same day — the
limit already sits live and invisible in the owner's payload (7%, resets Jul 26).

## Why

Per-model weekly caps moved out of flat tier keys (`seven_day_opus`/`seven_day_sonnet` are now
null) into a top-level `limits[]` array keyed by `scope.model.display_name`. The widget's
registry-gated pipeline (`TierModel.CanonicalOrder` at every consumer) silently drops the
array, so the Fable limit — and every future model-scoped limit — renders as nothing with no
signal. Confirmed by our live org-endpoint capture plus six independent community codebases.

## 1. Generic scoped-limits synthesis (the bar)

**Contract** (UsageFetcher, immediately after the usage payload lands, before the history
loop — the Routines-synthesis precedent):

- For each entry in top-level `limits[]` (JsonArray; absent/malformed → no-op) where
  `kind == "weekly_scoped"` and `scope.model.display_name` is a non-empty string:
  - Synthesize key `seven_day_{slug}` where slug = display_name lowercased,
    `[^a-z0-9]+` runs collapsed to `_`, trimmed of leading/trailing `_`
    (community convention: "Fable" → `seven_day_fable`, "Haiku 5" → `seven_day_haiku_5`).
  - Inject `data[key] = { "utilization": percent, "resets_at": resets_at }` — **never
    overwriting** an existing top-level key of that name (jens-duttke rule; if upstream ever
    ships the flat key, upstream wins).
  - `is_active: false` and `percent: 0` entries still synthesize — an unused limit is a
    rendered zero-bar exactly like every other tier, not a hidden one.
  - Entries with null/missing `percent` synthesize with `utilization: null` (the render
    filter's existing null-drop then applies — same behavior as today's null tiers).
- Non-`weekly_scoped` kinds and entries without a model display_name are ignored (surface
  scopes, group aggregates that mirror existing flat keys).

**Registry integration** (TierModel):

- `SevenDayFable = "seven_day_fable"` becomes a first-class static tier: const (after
  `SevenDayOpus` in the const block), `CanonicalOrder` position directly after `SevenDayOpus`,
  label `"Weekly - Fable"`. Not speculative (it has live data).
- Other synthesized keys register dynamically: `TierModel.RegisterScopedTier(key, displayName)`
  adds to a runtime dictionary consulted by `IsKnown`/`Label` (label = `"Weekly - {displayName}"`).
  `ResolveOrder` appends dynamic keys after the last `seven_day_*` canonical entry (before
  `IguanaNecktie`), in registration order — so a brand-new model's bar appears mid-family
  without a code push, and the hide/reorder Settings list carries it automatically.
  Registration is idempotent and process-lifetime (a tier seen once stays orderable even if a
  later fetch omits it; its utilization goes null → card drops, order slot remains).
- UsageFetcher calls `RegisterScopedTier` for every synthesized key (including
  seven_day_fable — a no-op there since it's static).

**Cascade — one seam change required:** the audit's "zero further edits" cascade holds for
STATIC keys only; five consumers iterate `TierModel.CanonicalOrder` directly and would never
see a dynamic tier (WidgetViewModel utilByKey loop :881 and alerts :944, UsageFetcher
HistoryTiers :46, HistoryTabViewModel :121, HistoryChart :69). Introduce
`TierModel.EffectiveOrder` — canonical + registered dynamic keys in their resolved positions
(exactly `ResolveOrder(null)`) — and switch those five sites from `CanonicalOrder` to it.
`CanonicalOrder` itself stays the immutable static registry. After the seam change, history
persistence (key-agnostic) and CSV (iterates stored keys) inherit dynamics with no edits;
seven_day_fable, being static, renders even before the seam change lands.

**Local CC attribution rider:** `CcLogReader.ModelTierPrefixes` gains
`("claude-fable", TierModel.SevenDayFable)` so Claude Code burn on fable models feeds the new
card's `+Nk` badge instead of only the footer total.

## 2. Unknown-key logging (never silent again)

In the fetch path (WebView2ApiClient's existing fetch-debug.log surface): after each usage
fetch, log top-level payload keys not in the effective registry (canonical + dynamic +
known-structural: `limits`, `spend`, `member_dashboard_available`, `_account`) and `limits[]`
kinds ≠ `weekly_scoped` — **once per key per process lifetime**, key/kind NAMES only, never
values. Format: `usage: unregistered keys: tangelo, cinder_cove` /
`usage: unhandled limit kinds: monthly_scoped`. The five known null codename keys (tangelo,
omelette_promotional, nimbus_quill, cinder_cove, amber_ladder) are deliberately NOT
special-cased — they log once as unregistered, which is the point.

## 3. Org selection by capabilities

`ClaudeApiParsing.ParseOrganizations`: replace the unconditional `orgs[0]` pick with:
first org whose `capabilities` contains `"claude_max"`; else first containing `"chat"`; else
`orgs[0]` (existing behavior, preserving single-org and API-only accounts). Plan fields
(`_account`) capture from the SELECTED org. The owner's live two-org payload (Max org +
API individual org) is the test fixture; a reordered variant must select the same org.

## 4. CF-on-200 classification + re-navigation

- `ClaudeApiParsing.ParseUsage`/`ParseOrganizations`: when the body fails JSON parse AND
  `ClaudeApiClient.LooksLikeCloudflare(body)` → throw `CloudflareBlockedException` (today:
  generic `NetworkException` "returned non-JSON" → widget shows "No connection — retrying…"
  forever). Valid-JSON bodies never reach the CF check (no false positives from payload text
  containing "cloudflare").
- WebView2ApiClient: on `CloudflareBlockedException` from a parse, clear `_ready` so the next
  cycle re-navigates the page (today a wedged-but-initialized page is never re-navigated).
  The UI's existing Blocked handling (cf_clearance prompt) is unchanged — it just becomes
  reachable from the 200-challenge path.

## Privacy / logging contract

Unchanged. fetch-debug.log gains key/kind NAMES only (Anthropic vocabulary, not user data).
sanduhr.log untouched. No new data classes; synthesized tiers persist in usage history
exactly like registry tiers (utilization percentages only).

## Testing (Core TDD)

- Synthesis: live-shape fixture (the captured payload's limits[] verbatim) → seven_day_fable
  injected with percent 7 + resets_at; no-overwrite when flat key pre-exists; slug rules
  ("Haiku 5" → seven_day_haiku_5); is_active:false still synthesizes; null percent →
  null utilization; absent/malformed limits[] → no-op; non-model scopes ignored.
- Registry: dynamic registration idempotent; Label fallback; ResolveOrder places dynamics
  after seven_day family and before iguana_necktie; saved-order interaction (a saved dynamic
  key resolves; unknown saved keys still drop); EffectiveOrder equals CanonicalOrder when no
  dynamics are registered (regression lock for every existing consumer) and includes dynamics
  in position when they are.
- Org selection: two-org fixture both orderings → Max org; chat-only fallback; single-org and
  empty-capabilities fallback to orgs[0].
- CF-on-200: challenge HTML body → CloudflareBlockedException from both parse entry points;
  valid JSON containing the substring "cloudflare" in a value → parses normally.
- Logging: once-per-process de-dup (unit over the tracking set, not the file).
- Cascade regression: existing suite stays green (history/CSV/alerts pick up synthesized keys
  through existing tests' pathways).

## Out of scope (parked, from the same audit)

spend{} card upgrade (option B), email-OTP Google sign-in (option C — needs the 2-step live
check), retry/backoff, fetch-debug.log size cap, HttpClient dead-transport removal, dollars
fields, `severity`-driven warning states. Version bump rides the next release cut, not this
wave.

## Effort

S/M — one Core seam (synthesis + registry), two parsing fixes, one logging surface. Order:
synthesis + registry (TDD, the bulk) → org selection (TDD) → CF-on-200 + re-navigation (TDD)
→ unknown-key logging → CcLogReader prefix + smoke additions.
