# Usage API audit — Fable limits, pipeline gaps, Google login (2026-07-19)

Ultracode run: 7 agents (live probe, pipeline audit, two research tracks, three adversarial
verifiers), 265k subagent tokens. Every load-bearing claim below survived adversarial
verification with primary or multiply-independent sources; refuted/uncertain items are marked.
Probe hygiene: the sessionKey was read in-process from Credential Manager, never printed to any
stream or file; the throwaway WebView2 profile was deleted after capture.

## 1. The Fable limit is ALREADY live in this account's payload — and invisible

The live probe of `claude.ai/api/organizations/{id}/usage` (account "Este", Max 20x —
`default_claude_max_20x`) found the Fable weekly limit present TODAY:

```json
"limits": [ ...,
  { "kind": "weekly_scoped", "group": "weekly", "percent": 7, "severity": "normal",
    "resets_at": "2026-07-26T05:59:59Z", "is_active": false,
    "scope": { "model": { "id": null, "display_name": "Fable" }, "surface": null } }
]
```

The widget renders none of it. Two structural facts:

- **There is no `seven_day_fable` key and there will not be one.** Six independent codebases
  (Runfusion live probe 7/11, claude-meter payload capture, ai-usagebar 7/08, TokenEater
  migrated-account fixture, claudeStateBar org-endpoint capture 7/13, otanitakeru's Chrome
  extension) all converge: per-model weekly caps ship in the top-level `limits[]` array, keyed
  by `scope.model.display_name` (model.id is null — the display name is the only stable
  handle). The legacy flat keys (`seven_day_opus`, `seven_day_sonnet`, …) are now **null** in
  current payloads — the per-model tier vocabulary migrated out from under our registry.
- **Our pipeline drops unknown payload members silently.** `WidgetViewModel.cs:881-882` builds
  utilization only from `TierModel.CanonicalOrder`; `limits[]` is never inspected. Verified
  drop sites: TierModel.cs:174-216 (ResolveOrder/ActiveTiers), UsageFetcher.cs:46+92,
  WidgetViewModel.cs:944 (alerts), HistoryTabViewModel.cs:121 + HistoryChart.cs:69 (charts).
  The only indirect signal is the anonymous tier COUNT in fetch-debug.log, which an
  array-valued member never ticks.

### Timeline (X post @claudeai status 2078302415804379218 + anthropic.com/news/redeploying-fable-5, confirmed)

- **July 20 (tomorrow):** Fable becomes a permanent Max/Team Premium inclusion at **50% of
  weekly limits** (use Fable up to half the weekly pool, then switch models for the rest).
  Pro/Team Standard: usage credits + one-time $100 credit; beyond that, API rates ($10/M in,
  $50/M out).
- **Also July 20:** the promo-era ~50% limit boost ends — regular limits drop ~33%
  (the-decoder + Forbes arithmetic). Expect visible number shifts tomorrow.
- The `limits[]` plumbing is live NOW (community tools shipped Fable bars off live data from
  July 8 onward); July 20 changes the entitlement, not the shape.

### The bar — minimal change-set (cross-check verified, file:line)

Follow the Routines-synthesis precedent — synthesize a flat key from `limits[]`:

1. `Sanduhr.Core/TierModel.cs:35-44` — add `public const string SevenDayFable = "seven_day_fable";`
2. `TierModel.cs:53-65` — insert into `CanonicalOrder` (after `SevenDayOpus`, line 58)
3. `TierModel.cs:71-84` — add `[SevenDayFable] = "Weekly - Fable"`
4. `Sanduhr.Core/UsageFetcher.cs` — after line 90 (before the history loop at 92): scan
   `data["limits"]` for `kind=="weekly_scoped" && scope.model.display_name=="Fable"`, inject
   `data["seven_day_fable"] = { "utilization": percent, "resets_at": resets_at }`. Render,
   history, alerts, chart, and CSV all cascade with zero further edits (verified).
5. Optional: `CcLogReader.cs:50-55` add `("claude-fable", TierModel.SevenDayFable)` so local CC
   burn attributes to the new card's `+Nk` badge (unmapped models currently fold into the
   footer total only).
6. NOT needed: `Pacing.cs:27-28` (weekly bucket falls through to `SevenDaySecs` correctly);
   `SpeculativeTiers` (the tier has live data).

**Recommended stronger variant:** synthesize one tier per scoped `limits[]` entry generically
(slug from display_name — `seven_day_${slug}`, the projectvelox/jens-duttke community
convention), so the NEXT model-scoped limit appears without a code push. Plus a debug-log line
naming unknown top-level keys, so upstream vocabulary changes are never silent again.

## 2. What else the payload carries that we drop

- **Five unregistered null codename keys:** `tangelo`, `omelette_promotional`, `nimbus_quill`,
  `cinder_cove`, `amber_ladder`. The day one lights up, it renders as nothing (same class as
  Fable). Precedent: omelette = Claude Design; iguana_necktie's null-bucket pattern.
- **`spend{}` (live, unrendered):** used $21.37 / limit $110.00, percent 19, severity,
  cap/balance/auto_reload, can_purchase_credits, can_toggle. A richer superset of
  `extra_usage` — the natural upgrade path for the Capped Extra Usage card.
- **Unparsed per-tier fields:** `limit_dollars`/`used_dollars`/`remaining_dollars` on
  five_hour/seven_day (null for subscription accounts); extra_usage's is_enabled,
  monthly_limit, used_credits, currency, decimal_places, disabled_reason, daily, weekly;
  `severity` + `is_active` on limits[] entries (server-driven warning state we currently
  derive locally); routines' `unified_billing_enabled`.
- **Org payload:** ~40 unread org fields + a ~50-flag `settings` object. No Fable/Mythos
  references anywhere in it.

## 3. Pipeline robustness findings (all cross-check confirmed)

- **Org selection is ordering-luck.** This account returns TWO orgs (Max org + API individual
  org); `ClaudeApiParsing.cs:45` hard-picks `orgs[0]`. If the API reorders, usage silently
  tracks the wrong org. Fix: prefer the org whose capabilities contain `claude_max`/`chat`.
- **Cloudflare challenge on HTTP 200 is misclassified** as generic "No connection — retrying…"
  (`ThrowForStatus` checks CF markup only under 403; a 200 challenge body lands as
  "returned non-JSON" → NetworkException). And a wedged-but-initialized WebView2 page is never
  re-navigated (`EnsureReadyOrResetAsync` clears `_ready` only on init failure).
- **The raw HttpClient transport is dead against claude.ai.** The probe's first attempt —
  CloudflareAwareHandler, stored cf_clearance, matched Chrome UA — got a CF managed challenge
  (403); the WebView2 mirror succeeded first try. `new ClaudeApiClient` has zero call sites in
  the app (grep-verified). It's dead code with drifting headers (sec-ch-ua pins Chromium 131 vs
  Chrome/150 UA). Candidates: delete, or keep as documented-dead test transport.
- No retry/backoff (flat 5-min timer), tokenless fetch, unbounded fetch-debug.log growth.
- **Community caveat:** the seven_day window has been observed resetting every ~72h (not 7d)
  with erratic July behavior — treat `resets_at` countdowns as display-only truth.

## 4. Google login — a real upgrade exists (verified, one live check pending)

**Route Google-account users through claude.ai's first-party "Continue with email"
magic-link/OTP path, inside the existing WebView2.** Claude accounts are keyed by email, not
provider (Anthropic's own help article describes email-only login on a Google-created
account). The flow never touches Google's OAuth endpoint, so the `disallowed_useragent` block
never fires. Effort LOW, policy risk NONE. This retires the guided manual sessionKey paste to
last-resort-fallback status.

**Verify live before building** (the one unproven dependency, flagged by researcher AND
verifier): (a) a Google-created account accepts email login with zero prior unlink steps;
(b) the 6-digit code-entry field renders in-window in WebView2 when the link is opened on
another device.

**Dead ends, confirmed dead — do not revisit:** system-browser cookie recovery (App-Bound
Encryption since Chrome/Edge 127; only infostealer techniques get through — unshippable);
UA spoofing (works technically per cnr.sh but is a Google ToS violation); localhost-loopback
OAuth (only works when you ARE the OAuth server — Claude Code is, Sanduhr isn't); passkeys in
WebView2 (supported since KB5072033, but Google rejects the UA before any WebAuthn challenge).

## Verification notes

Adversarial pass corrections worth keeping: Google webview enforcement began Sept 30 2021 (not
July 2023); TokenEater's flat-key fixture also carries a limits[]-fallback test agreeing with
the live shape (undersold, not contradicting); the claim "no public org-endpoint captures
exist" was REFUTED — claudeStateBar and otanitakeru both capture the org endpoint with the
same limits[] shape Sanduhr's own probe observed. One probe claim stays formally uncertain:
the 403-challenge HTML from the first transport attempt was overwritten by the successful
second run (the 403 itself is consistent with the code's own docs).

## Proposed build wave (pending owner scope call)

1. **Core (the bar, urgent — limits shift tomorrow):** generic scoped-limits synthesis
   (Fable first), unknown-key debug logging.
2. **Rider:** org selection by capabilities.
3. **Rider:** CF-on-200 classification + WebView2 re-navigation on wedge.
4. **Candidate (own mini-wave):** spend{} upgrade for the Capped Extra Usage card.
5. **Candidate (needs the 2-step live check first):** email-OTP sign-in path for Google users.

Probe artifacts (session-scoped, will not survive cleanup): scratchpad/probe/{organizations,usage,routines}.json.
