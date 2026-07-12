# WS-C — Usage Vault + Local CC trends + Session Ledger (design)

Remediated design, 2026-07-12. Owner decisions: vault stores rollups AND a per-session index; monthly JSON shards (no SQLite); Session Ledger UI ships in this workstream. Five-lens adversarial panel ran against the draft (ingestion correctness, storage/schema, performance, UX, privacy/tenancy — all verdicts sound-with-changes); every must-fix is folded in below. Panel ground truth from this machine: 2,042 session JSONLs / 870 MB across two roots, ~68 sessions/day, largest live file 47 MB, and **49% of recent assistant events are `claude-fable-5` — unmapped by the tier prefix table**, the single highest-stakes schema fact in this design.

## Why

Claude Code deletes session logs after ~30 days. The Local CC tab recomputes from raw logs with a 30-second cache — history evaporates at the retention boundary. The vault makes Sanduhr the durable record and unblocks the gated slate (Session Ledger now; Spike Forensics, Wrapped, burn-split later).

## Storage

**Location: `%LOCALAPPDATA%\Sanduhr\vault\`** (new `Paths.VaultDir` on `LocalApplicationData` — NOT roaming AppData: enterprise roaming-profile tech would sync an indefinite work-activity archive to employer infrastructure, silently breaking "never transmitted anywhere").

**Per-root directories — tenant separation is structural, not logical:**

```
vault\
  .claude\              ← employer seat on this machine; separable by construction
    sessions-2026-07.json
    rollups-2026-07.json
    checkpoints.json
  .claude-personal\
    ...
```

Per-root purge = delete a folder; audit handover = hand a folder; future MCP tenant scoping = read only consented roots' folders.

**Session shards — the irreplaceable primary record.** `sessions-YYYY-MM.json`:

```json
{ "schema_version": 1, "writer_version": "3.3.0",
  "sessions": { "<uuid>": {
      "project_key": "api~3f2a91cc",        // basename + 8-hex SHA of cwd — disambiguates api-vs-api without storing the path
      "project_name": "api",
      "cwd": null,                            // populated ONLY when store_full_paths (off by default) is on
      "first_ts": "…UTC…", "last_ts": "…UTC…", "utc_offset_min": -300,
      "event_count": 812, "skipped_lines": 0,
      "continuation": false,                  // true on slices outside the session's first month
      "total": 1934000,                       // unconditional — every timestamped event, matching AggregateForLocalCcTab exactly
      "by_model": { "claude-fable-5": 1100000, "claude-sonnet-5": 830000, "<synthetic>": 4000 },
      "cache_tokens": { "read": 42000000, "creation": 3100000 },
      "by_skill": { "code-review": 400000 },
      "by_day": { "2026-07-12": { "total": 934000, "by_model": { "…": 0 } } }
  } } }
```

- **Raw model strings, never tiers.** Tier is a read-time projection through the prefix map — a map update retroactively heals ALL history. (The draft's tier-keyed schema would have permanently destroyed the 49% of events the current map drops.) `<synthetic>` and any future oddity are kept as-is: raw capture is the rule because session shards are rebuildable from NOTHING once JSONLs age out.
- **Unconditional `total`** at session, day-bucket, and rollup levels — sums of filtered maps are display math, never the stored truth (prevents the vault's closed-day numbers under-reporting against the live tab).
- **Cache token counts** stored now (the burn-split slate feature cannot be backfilled later).
- **Month slicing:** a session's row lives in the shard of its **local-time first day**; when its `by_day` spans additional months, a slice row (same uuid, `continuation: true`, only that month's buckets) is written to each additional month's shard. Consequence: **every day's data lives entirely in its own month's shard** — the rollup fold for day D reads exactly one shard, no cross-shard join exists. Readers (Ledger) merge slices by uuid. Each file's checkpoint entry records the months it touched, so re-ingest rewrites/removes slices across exactly that union (covers first_ts moving across a month boundary when a parser fix changes what parses — exactly one logical session survives, tested).

**Rollup shards — a declared derived cache, never a second truth.** `rollups-YYYY-MM.json`: per local-day, per this root: `total`, `by_model`, `by_project` (keyed by `project_key`), `by_skill`, `sessions` count. Rules: session shards are the sole source of truth; the full-day fold is the ONLY rollup writer (no incremental patching); rollups are deletable at any time and rebuilt by folding; a startup self-check (`fold(sessions) == rollups` for the current month) triggers rebuild on mismatch. Drift is self-healing instead of permanent.

**Checkpoints.** Per root, `checkpoints.json`: keyed by **SHA-256 of the absolute path** (never readable paths — the encoded-cwd directory names would otherwise make this file a permanent path ledger defeating the basename policy), value `{ mtime_ticks, length, offset, tail_guard, months: ["2026-07"], sealed: false }`. Entries whose file has been missing from the walk for >7 days are **pruned** (bounds the file at ~files-on-disk ≈ 330 KB; re-ingest of a resurrected file is idempotent, so pruning risks nothing; the "vault outlives its sources" property belongs to shards, not bookkeeping).

**Writes:** serialize to `.tmp`, `File.Replace`/`Move(overwrite)`, 2–3 retries, failures logged (see Logging). **Write ordering invariant: session shard(s) → rollup shard(s) → checkpoints LAST** — a crash at any point leaves a stale checkpoint, and the next cycle's re-ingest converges; checkpoint-first would make the same crash permanent.

**Quarantine:** a shard that fails to parse moves to a **timestamped** name (`sessions-2026-07.json.20260715T031500.bad` — never overwritten, never auto-deleted; for months older than JSONL retention the `.bad` IS the archive), and quarantine **atomically invalidates that root's checkpoints** (delete `checkpoints.json`) so the already-designed full-re-ingest convergence rebuilds everything still on disk. Without that coupling, a quarantine strands a permanently empty month behind clean checkpoints.

**Schema evolution rules:** session shards store the rawest practical aggregates; meaning changes always take a NEW field name; readers handle every `schema_version ≤ current` forever (no in-place migration — the source to re-derive is gone); rollups are exempt (any rollup change = full rebuild).

## Ingestion — `VaultIngester` (Core)

- **Trigger:** `Task.Run` fire-and-forget from the 5-min fetch cycle + once at startup; **`Interlocked` single-flight** (previous run still going → skip this cycle); never awaited anywhere in `RefreshAsync` (the WS-B `EvaluateAlerts` call is synchronous-cheap — it is explicitly NOT the template here).
- **Cross-process writer exclusion:** named mutex `Global\Sanduhr.VaultWriter`, try-acquire per cycle, holder-skip + one log line. Store + Velopack builds can run side-by-side (no single-instance guard exists — verified), and interleaved whole-shard read-modify-write is a classic lost-update that permanently strands clean-checkpointed stale rows. The mutex also covers the in-process backfill-overlaps-next-cycle race.
- **File opens:** the ingester owns its open path (never `CcLogReader.IterUsageEvents`, whose silent empty-on-failure is fatal to a checkpointed ingest): explicit `FileStream` with **`FileShare.ReadWrite | FileShare.Delete`** (a live CC writer handle must never cause a sharing violation in either direction — named verification precondition). A failed or zero-byte-read open advances NO checkpoint and replaces NO row — "read failed" and "file empty" are distinct outcomes by construction.
- **Checkpoint stat is taken BEFORE opening the file.** Parsing more than the checkpointed length is safe (next cycle re-ingests, converges); a post-parse stat can record bytes the parse never saw — permanent loss.
- **Live files — guarded tail parse** (kills the O(size²) reparse curve: a 47 MB session reparsed every 5 minutes costs gigabytes of parse per day): checkpoint carries `offset` + `tail_guard` (hash of the 64 bytes before offset). Grown file + verified guard → parse the tail only, fold into a copy of the stored row (row replace still atomic). ANY guard mismatch or shrink → full reparse. When a file quiesces (mtime unchanged > 1 h) → one final whole-file verify parse, then `sealed: true`. Double-counting stays structurally impossible — the invariant holds eventually-per-file instead of expensively-per-cycle. (Residual accepted + documented: a same-length same-mtime content swap by a backup/restore tool defeats the (mtime,length) gate; the tail guard catches most, staleness not overcount is the failure direction.)
- **Backfill:** single-threaded, oldest-first (never `Parallel.ForEach` — disk contention with the UI-thread badge reads), substring prefilter (`"type":"assistant"`) before any `JsonNode.Parse` (the bulk of JSONL bytes are tool-result lines that would otherwise become gigabytes of transient DOM), enumeration reads `Length`/`LastWriteTimeUtc` off `FileSystemInfo` (no per-file stat pass).
- **Per-root consent:** first vault run shows a themed dialog listing each detected CC home as a checkbox (pre-checked, but PROMPTED — silent-on for an employer root is the breach WS-E's review already named). `VaultIngester` honors the toggles; toggling a root off stops ingestion and offers purge of that root's folder. The live 30-day tab keeps reading both roots exactly as today — only the forever-store is gated.
- Zero-assistant-event sessions: no row. Malformed lines: skipped + counted (`skipped_lines`), never fatal, never logged by content.

## UI — Local CC tab (rename to "Claude Code") grows a sub-nav: Overview / Trends / Sessions

Segmented control reusing the History tab's RangePill grammar. Sessions/Trends drop the intro blurb (full height for data); Overview keeps it — rewritten:

> *"Claude Code deletes session logs after ~30 days. Sanduhr keeps a local history vault so your trends survive — never uploaded, per-home opt-in, erase any time below."*

(The honesty and the marketing are the same sentence; the current "no network, no upload" copy stops being the whole truth the moment the vault exists.)

- **Overview** = today's content, sourced: vault for days strictly before local-today, live reader for today (never both — the vault always holds a partial today row; the exclusion rule prevents double-count). **Hot-day rule:** any day not confirmed fully ingested (last successful ingest < that day's midnight) serves live — plus an immediate ingest triggered at day-rollover — so yesterday's number never dips at 00:00 and recovers at 00:04. **Degraded mode:** last successful ingest > 3 cycles old → the whole 30-day window falls back to the full live-reader path (today's shipping behavior) with a themed status line *"history vault paused — showing live logs only"*; frozen closed-day numbers with no signal are the design lying. A today-only aggregate is added to the reader (its 30-day ByProject/BySkill sums can't compose with vault closed-days).
- **Trends** = weekly total bars + top projects over 4/12/26 weeks (RangePill). Current week rendered distinct (hatched, "week in progress"); **pre-vault and known-ingest-gap regions get a "no record" texture, never zero-height bars** (auto-start is off by default — a widget-off fortnight must not read as a vacation); footer carries the vault birth date (*"history preserved since July 12, 2026"*); day-1 empty state says the backfill seeded ~4 weeks. Tier-split-over-time is named v2 (healable, since by_model is stored).
- **Sessions (Ledger)** = the headline question is *"what ate 800k yesterday"*, so: **date-scope chips (Today / Yesterday / 7d / All, default 7d)** where the token column and its sort show tokens-within-scope computed from `by_day` — a lifetime-total sort ranks week-old monsters, not yesterday's culprit. Columns: last-active (relative), project (disambiguated `project_key` display; full-path tooltip only under `store_full_paths`), model-mix badge (read-time tier projection), scoped tokens (tabular figures). Row expansion: span (wall-clock, labeled as such — no fake "active time"), per-day/model breakdown, skill split, root, session uuid. Slices merged by uuid.
  **Virtualization constraints (binding — the App layer has no tests, the spec is the only gate):** the list owns its scrolling (star-sized row, NO ancestor ScrollViewer — the Overview's ScrollViewer-wrapping-ItemsControl pattern is the in-repo trap); recycling virtualization asserted; `IsExpanded` lives on the row VM; sorting via `ListCollectionView.CustomSort` with a typed comparer; live refresh diffs rows by uuid (never `Clear()`+re-add — that resets scroll every 5 minutes). Control: `ListBox` + `VirtualizingStackPanel` (`ScrollUnit=Pixel`) + custom themed sort-header row; `DataGrid` is the retemplate-hostile first-of-its-kind this codebase doesn't need.
- **Data stewardship buttons** (WS-A delete-completeness precedent — every other store has one): **Erase archive** (whole vault), per-root purge (also flips that root's consent off — consent state is the tombstone), **Open vault folder**, **Export CSV** on the Ledger. "Delete the folder to erase" alone is false while the app runs — it re-backfills within one cycle.
- **30s tick hygiene (in scope, exposed by backfill):** the tick's CC reads (`RefreshCcDelta` + footer) currently run synchronous file IO on the UI thread twice per tick — move to `Task.Run` + marshal back, and collapse the two `TokensSince` walks into one shared aggregate. Cheap, and it pays even before the vault exists.
- Theming: all `Sanduhr.Brush.*`, no literals; sort direction by glyph not color alone; tier badge carries text.

## Privacy (binding)

- PRIVACY.md, same release: a vault row (location, contents including "one summary row per session", **kept indefinitely — unlike Claude Code's own logs**, per-home opt-in, erase via Settings or delete-folder-with-app-closed, `.bad` quarantine files included); a checkpoints line ("hashed log-file identifiers — no readable paths"); the log row's never-contains list gains *"project paths or names, skill names, or session-log contents"*; the uninstall section documents that **Store uninstall does not remove `%LOCALAPPDATA%\Sanduhr`** — manual deletion is the post-uninstall erasure path.
- **Logging verification precondition** (all sinks: `sanduhr.log`, `fetch-debug.log`, `last_error.json`): operation + exception TYPE + counts only. Never raw JSONL lines (they are conversation content), never `e.Message` (file-op messages embed paths), never `e.ToString()`. Test asserts a failed ingest's log output contains no path-separator sequences.
- Banked for future consumers: Wrapped and the MCP read only consented roots' shards; Wrapped exports a curated field set (skill names carry tenant signal too).

## Testing (Core-heavy; the adversarial fixtures are the point)

Idempotent re-ingest (byte-identical vault); growing-file tail-parse then seal-verify convergence; **torn-last-line-completes-after-checkpoint converges**; **open-failure leaves checkpoint and row untouched**; midnight- and month-boundary sessions (slice model: day recompute reads one shard; slice merge by uuid); **first_ts month-move on re-ingest leaves exactly one logical session**; local-vs-UTC month placement; crash-between-shard-and-checkpoint converges; **second writer (mutex) skips cleanly**; **unmapped-model fixture: vault day total == live reader day total**; quarantine → checkpoint invalidation → full re-ingest converges; checkpoint-prune then file-resurrection is harmless; DST/25-hour-day bucketing; per-root consent honored; fold==rollups invariant after every ingest including crash recovery; log-output path-separator assertion.

## Accepted + documented (not bugs)

TZ-change day-key wobble (day keys are local-at-ingest; `utc_offset_min` stored for auditability; misattribution not corruption — tokens conserve). Same-length/same-mtime restore staleness residual. The triple-parse of live files (ingester + badge tick + footer) is **transitional**: banked in the roadmap that post-vault, the ingester's live rows become the single source for badges/footer/Overview-today and the ad-hoc walkers retire.

## Effort

L (was M — the Ledger UI plus the ingestion hardening the panel mandated). Suggested order: Paths/VaultStore + schema (Core, TDD) → VaultIngester + adversarial fixtures (the bulk) → consent + erasure + settings → Overview merge + degraded mode → Trends → Ledger → copy/PRIVACY → smoke.
