# WS-C.1 — Subagent coverage, sent/received split, % contrast, rolling calendar (design)

Approved 2026-07-13, same-day follow-up to the merged WS-C vault (PR #38, 2822c5c). All four items came out of the owner's live smoke walk. Owner decisions: subagent burn folds into its parent session; live surfaces adopt the recursive walk everywhere (the 2–3x jump is the correction, not an error); the calendar sits below the 30-day bar strip. "UTC aware" was clarified to mean timezone-change correctness only, which the vault already guarantees — no UTC display toggle.

## Why

The Local CC surfaces have never counted nested subagent/workflow transcripts (`projects\{dir}\{session-uuid}\subagents\**\*.jsonl`) — 1,589 files / 358 MB in the personal root alone at time of writing, all carrying real `message.usage` events. On subagent-heavy days the displayed burn is 2–3x under reality. Separately, the vault stores input+output combined, so the sent/received split cannot be derived from stored history. **Both fixes share CC's ~30-day retention clock: every day that ages out before this ships is permanently uncounted/unsplit.** The % contrast and calendar items are walk-findings riding the same wave.

## 1. Subagent-transcript coverage (lead)

- **Walk:** `CcLogReader.DiscoverLogFiles` and the ingester's walk both enumerate `*.jsonl` recursively under each project directory (`SearchOption.AllDirectories`). One change point per walker; every live consumer (tab, badges, footer CC delta, TokensSince family) inherits it — vault/live parity holds by construction.
- **Identity:** every file remains its own ingest unit — per-file checkpoints, tail-parse, seal, and fingerprint machinery untouched. Each nested file's vault row keys on its own filename-uuid and gains a schema-additive `parent_session` field (`JsonPropertyName("parent_session")`, omitted when null): the first path segment under the project directory when that segment is a session-uuid directory (i.e., `{projectDir}\{parent-uuid}\...\agent-x.jsonl` → `parent-uuid`); main transcripts (`{projectDir}\{uuid}.jsonl`) carry none.
- **Read side:** the logical-session key is `parent_session ?? uuid` (per root). `VaultReader.ReadSessions` groups rows by that key and merges via the existing `VaultRowMath.Merge` semantics extended across files: `first_ts` min / `last_ts` max, day buckets summed (same-day keys ADD — multiple files per session per day is the norm now), `event_count`/`skipped_lines`/`cache_tokens` summed across member primaries, project identity from the main transcript's row when present (fallback: first member). The Ledger shows one row per logical session; the expansion detail gains one line: `Agents: N · {tokens} of the total` when N > 0.
- **Rollups:** fold unchanged for totals/by_model/by_project/by_skill (file rows carry their own project keys — subagents inherit the repo cwd, so attribution already lands right). The per-day `sessions` count becomes DISTINCT logical sessions (`parent_session ?? uuid`) touching that day, not file rows.
- **Upgrade re-ingest:** `meta.json` gains additive `walk_version` (int, absent = 1). The ingester carries `CurrentWalkVersion = 2`; at the start of a root's cycle, `walk_version < 2` → delete that root's `checkpoints.json` (the existing quarantine-style invalidation) → the cycle full-re-ingests everything on disk → meta saves with `walk_version = 2`. One-shot per root, idempotent, heals all history still inside retention. Rows for files already aged out are preserved untouched (the vault outlives its sources) — they simply never gain subagent companions or splits.
- **Consequence, accepted and owner-approved:** live 30-day totals jump ~2–3x the day this ships. No transition copy — the number is finally right.

## 2. Sent/received token split

- **Schema (additive):** `VaultDayBucket` gains `input` / `output` (longs, always written on new rows; absent on legacy rows = unsplit). Session-row and rollup-day `input`/`output` are derived by the fold/merge from buckets, mirroring the `by_model` precedent (rollup-day gains the two fields; rollups fully rebuild on the upgrade re-ingest, so no rollup migration).
- **Ingest:** `ProcessLine` already reads `input_tokens`/`output_tokens` separately before summing — bucket the two sides alongside `total`. Conservation invariant: `input + output == total` on every NEW bucket (tested); legacy buckets report 0/0.
- **Live reader:** `LocalCcAggregate` gains per-day input/output alongside `ByDay` (both `AggregateForLocalCcTab` and `AggregateTodayOnly`). Its consumers (`LocalCcViewModel`, tests, the record's construct sites) update in the same task.
- **UI:** one small secondary line under each Overview headline figure — `↑ {sent} sent · ↓ {received} received` (sent = input, received = output; bare labels, tabular-figure formatting, `TextSecondary` ink). Sourcing follows the Overview's existing exclusion rule: vault rollups for closed days, live for hot days/today.
- **Honesty rule:** when the window's split coverage (`Σ input+output`) is less than 95% of the window total (legacy unsplit days present), the line appends `(partial)`. Today's line never needs it; the 30-day line loses it naturally as legacy days age out of the window.

## 3. Widget tier-card % contrast

The utilization percentage on the tier card currently renders directly over the sparkline and blends. It gets a rounded backplate chip (existing theme brushes only — `Sanduhr.Brush.Bg` or `Glass` family behind bold `Accent`-ink text; no new resource keys, no literals) so it reads over ANY sparkline style (Classic and Horizon) in every theme, dark/light/Matrix included. Isolated to `TierCard.xaml` (+ code-behind/VM only if the chip needs a computed brush). Smoke covers the sparkline-style × theme matrix.

## 4. Rolling calendar (Overview)

New `CcCalendarControl` (OnRender `FrameworkElement`, same family as `LocalCcBarStrip`/`CcTrendsControl`), placed directly below the 30-day bar strip:

- Grid of 5 week rows × 7 weekday columns (Monday-start, matching `VaultReader.WeekStart`), covering the last 35 local days ending today; leading cells before the window render empty.
- Cell fill: accent heat scaled to the window max (perceptual alpha steps, not linear-to-invisible — minimum visible step for any nonzero day); covered zero days get the faint baseline tick treatment; uncovered days get the dotted no-record texture (same brush recipe as Trends — never a blank that reads as zero).
- Weekday initials as a header row; hover shows `{MMM d} — {compact tokens}` via mouse-move hit-testing + ToolTip. Today's cell gets a 1px accent outline.
- Data: the same merged `ByDay` the Overview already computes (vault closed days + live hot days) plus `IsDayCovered` for the texture — no new reads.
- Theming: palette brushes only, frozen; re-tints on theme change via the existing `Changed` re-push.

## Privacy

PRIVACY.md vault row: "one summary row per session" → "one summary row per session (including its subagent transcripts)". No new data classes — subagent transcripts are the same JSONL family, same fields, same never-conversation-content rule. Logging contract unchanged (operation + exception type only).

## Testing (Core TDD; the upgrade path is the point)

Recursive discovery finds nested files (both walkers); parent_session derived correctly for nested/main/deeply-nested paths; logical-session merge sums same-day buckets across files and keeps project identity from the main transcript; rollup sessions-count is distinct-logical-sessions; walk_version upgrade: a v1 vault (checkpoints present, meta without walk_version) re-ingests once and equals a fresh-vault byte-for-byte (given same nowUtc), and a second cycle does NOT re-invalidate; aged-out rows survive the upgrade untouched; split conservation `input+output==total` per new bucket; legacy buckets read 0/0 and trigger the (partial) threshold math; live-vs-vault day-total parity holds WITH subagent files present (the fable-5 parity test pattern, recursive edition); `AggregateTodayOnly`/`AggregateForLocalCcTab` split fields agree with per-event sums. App layer untested by design — plan constraints + reviewer gate + smoke.

## Accepted + documented (not bugs)

The day-one 2–3x jump in live numbers (owner-approved correction). Pre-upgrade aged-out days permanently lack subagent burn and splits — "(partial)" marks the split case; totals for those days remain what the one-level walk saw. A subagent transcript whose parent directory aged out but which itself survives (unlikely — they age together) still folds by its recorded parent_session key. The calendar reads the Overview's already-computed window — its numbers inherit the Overview's hot-day residuals.

## Effort

M. Order: Core walk + parent_session + walk_version upgrade (TDD, the bulk) → split schema + ingest + live reader (TDD) → Ledger/rollup read-side merge (TDD) → Overview split line + calendar → tier-card chip → PRIVACY + smoke additions.
