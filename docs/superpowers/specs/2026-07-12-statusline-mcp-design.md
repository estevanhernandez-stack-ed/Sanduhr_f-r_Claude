# WS-E — Statusline Bridge + Sanduhr MCP server (design)

Remediated design, 2026-07-12. Source: Tier-1 slate (`docs/roadmap-2026-07-11.md`) as amended by the five-lens adversarial review (`docs/vibe-access/design-review-2026-07-12-sanduhr-mcp.md`) — every must-fix from that register is folded in here. Windows build only.

## Spike result (the review's gate — CLEARED 2026-07-12)

The installed Store build (3.1.0.0, verified running from `C:\Program Files\WindowsApps\626LabsLLC.SanduhrfrClaude_3.1.0.0_x64__wz1chhb2h2v4a\Sanduhr.exe`) writes to the **real** `%APPDATA%\Sanduhr` — `fetch-debug.log`, `history.Este.json`, `settings.json` all updated post-launch; the package's `LocalCache\Roaming` overlay stayed empty. **No AppData write virtualization applies to this app.** Consequence: `snapshot.json` lives at `%APPDATA%\Sanduhr\snapshot.json` (via a new `Paths.SnapshotFile`), readable by unpackaged consumers on both channels. No `unvirtualizedResources` capability, no cert-letter budget spent. The per-channel verify step stays in the test plan regardless — a future manifest change could alter the posture, and the check is cheap.

## Goals

- A Claude Code statusline that shows real cap numbers where the burning happens, honestly (age or explicit staleness, never a silent lie, never a blank that reads as "all clear").
- An MCP server that makes the agent quota-aware: check headroom before spawning subagents or picking Opus vs Sonnet.
- One snapshot seam feeding both, with a staleness/error contract a cold agent can verify per the vibe-access methodology.

## Non-goals (drift watchlist — these stay out, permanently or until named preconditions)

- `can_i_afford(tokens, model)` — not honestly buildable; nobody has the tokens-per-utilization conversion. Precondition: WS-C vault correlates local deltas with observed utilization over matched windows. Until then, pace + projection fields are the honest substitute.
- `switch_account`, `refresh_now`/poll-trigger, anything reading Credential Manager, MCP-side rollup/vault writes. All four are structurally excluded (see Trust boundary). A future PR adding free-form path/glob params to any tool is a design violation, not a feature.
- Multi-account snapshots (WS-D watchtower changes the schema — additive keys or a new file, never repurposing v1 fields; every verify stamp resets when it lands).

## Architecture

One writer, one rendezvous file, N read-only consumers, derivations at read time:

```
WidgetViewModel/UsageFetcher (5-min fetch, ACTIVE account)
        │ atomic write (temp + File.Replace)
        ▼
%APPDATA%\Sanduhr\snapshot.json  (raw facts + status, schema_version 1)
        │                              │
        ▼ read-only                    ▼ read-only
statusline script                 sanduhr-mcp.exe (stdio MCP)
(CC renders per prompt)           (CC spawns per session)
```

## snapshot.json — schema v1

```json
{
  "schema_version": 1,
  "writer_version": "3.2.0",
  "captured_at": "2026-07-12T06:15:00.000000+00:00",
  "status": "ok",                     // ok | error
  "error_kind": null,                 // session_expired | cloudflare | network (when status=error; tiers hold last_good)
  "account_ref": "a1b2c3d4",          // short SHA-256 prefix of the label — NEVER the raw label
  "plan": "Max 20x",
  "tiers": [
    { "key": "five_hour", "utilization": 42, "resets_at": "2026-07-12T09:00:00+00:00" },
    { "key": "seven_day", "utilization": 62, "resets_at": "2026-07-17T00:00:00+00:00" },
    { "key": "routines",  "utilization": null, "used": 3, "limit": 25, "resets_at": null }
  ],
  "statusline": "wk 62% · opus 41% · wk resets Fri 7p"   // optional pre-render, human-glance only
}
```

Rules:

- **Raw facts only.** No precomputed pace, no countdowns — they decay with wall clock and are wrong by read time. Readers compute age, `resets_in`, pace, projection at read time (formulas live in `Pacing`; the statusline script reimplements the one-liner).
- **No raw account label anywhere** — `account_ref` is a hash, sufficient for switch-detection and delete-targeting. Raw-label opt-in is a WS-D decision, not v1. (PRIVACY.md's log promise extends to this file.)
- **Timestamps:** UTC ISO-8601 with explicit offset (`UsageHistory.NowIso` precedent). Readers compare in UTC, render `resets_at` local, clamp negative ages to 0.
- **Atomic write:** serialize to `snapshot.json.tmp` (same directory) then `File.Replace`/`File.Move(overwrite)`, 2–3 retries on `IOException`, failures logged to `sanduhr.log` (no labels, no contents — operation + exception type, the WS-A `LogBestEffortFailure` convention). Never `File.WriteAllText` direct.
- **Reader contract (ships in BOTH consumers):** open `FileShare.ReadWrite|Delete` or read-all-in-one-shot, one retry on `IOException`, any parse failure = treat as missing. Atomicity makes malformed always-a-bug, never a race.
- **Failure-status writes:** when the fetch throws (`SessionExpiredException`/`CloudflareBlockedException`/`NetworkException` — the widget already differentiates), write `status: "error"` + `error_kind`, keeping the last-good tiers. "Stale" becomes actionable ("reauth needed"), not "is the widget running?"
- **On account switch:** delete or rewrite the snapshot synchronously before the switch completes — no window where the new account's freshness wraps the old account's numbers.
- **Lifecycle deletes:** toggle-off deletes the file; account removal deletes it when `account_ref` matches; last-account delete always purges (piggyback on WS-A's `SignOutAccountAsync` flow next to the `WebView2FetchDir` purge). Consent revocation revokes the artifact, not just the writer.

## Staleness contract — shared constants in `Sanduhr.Core` (new `SnapshotContract` class)

| Band | Age | Statusline | MCP |
|---|---|---|---|
| fresh | < 7.5 min (1.5× cadence) | numbers | `status: "ok"` |
| stale | 7.5–15 min | numbers + age suffix (`· 12m`) | `status: "stale"`, data present, `age_seconds` set |
| dead | > 15 min (2 missed polls) | `sanduhr: stale 43m — start widget` (NEVER blank) | `status: "stale"` + `remedy` |
| missing/malformed/opt-out | — | empty output (degrades to invisible — this is the *uninstalled* look, not the *broken* look) | `status: "no_data"` + `reason` + `remedy` |

`reason` enum (closed): `missing | malformed | disabled | widget_not_polling | schema_unsupported`. All failures are **typed tool results — never MCP protocol errors** (protocol errors read as "server broken" and poison the healthy tools). The server computes `age_seconds` and `resets_in_seconds`; the agent never does clock math. Reset-crossing check before serving any tier: if `resets_at <= now`, `reset_crossed: true` and `utilization_pct: null` — never the stale percentage (a 4-minute-old snapshot is arbitrarily wrong across a boundary; the five_hour tier crosses daily).

## Sanduhr MCP server

**Project:** new `windows-dotnet/src/Sanduhr.Mcp/` console project referencing ONLY a snapshot-reader slice + `CcLogReader` + `Pacing` + `TierModel` (extract `Sanduhr.Contracts` if needed). **It must be impossible to reference `CredentialStore`/`WindowsCredentialManager`** — the cert letter's "cannot touch credentials" claim is structural, not disciplinary. Single-file publish, no writes except optional logging outside the snapshot path.

**Tools — exactly three:**

1. **`get_usage`** — merged usage + resets (the review killed the separate `get_reset_schedule`: a strict subset tool creates the wrong-single-call path where an agent sees "resets Fri 7p" and never sees the 91%). Response:

```json
{
  "status": "ok",
  "reason": null, "remedy": null,
  "as_of": "…", "age_seconds": 120,
  "scope": "active_account_only",
  "account": { "ref": "a1b2c3d4", "plan": "Max 20x" },
  "data_lag_note": "claude.ai's own numbers lag consumption by several minutes",
  "tiers": [{
    "key": "seven_day", "label": "Weekly (all models)",
    "utilization_pct": 62, "headroom_pct": 38,
    "resets_at": "…", "resets_in_seconds": 412000, "reset_crossed": false,
    "pace": { "verdict": "ahead", "delta_pct": 9 },
    "projection": { "expires_before_reset": false, "expires_in_seconds": null },
    "used": null, "limit": null
  }],
  "local_burn_since_snapshot": {
    "total_tokens": 34000, "by_tier": { "seven_day_sonnet": 30000 },
    "caveat": "token-count proxy from local CC logs (input+output only); not convertible to utilization %"
  }
}
```

   `pace`/`projection` nullable per tier (routines has `resets_at: null` by design — "unknown" must be distinct from "none"). `local_burn_since_snapshot` = `CcLogReader.TokensSinceByTier(as_of)` — the staleness compensator for the double lag; labeled a proxy, never fake-converted.
   Description (behavioral trigger, verbatim intent): *"Check Claude subscription quota headroom. Call BEFORE spawning subagents, launching long autonomous runs, or choosing Opus vs Sonnet for a large job. Reflects the Sanduhr widget's active account, which may not be the account this session bills to — confirm with the user if they run multiple accounts. A stale or no_data status means unknown headroom — never assume budget."*

2. **`get_local_burn_by_project`** — attribution ("where did the tokens go"), NOT affordability, and its description says so. **Tenant-wall scoping (the review's most-corroborated finding):** results keyed **per root** (`.claude`, `.claude-personal`), project **basenames** by default (`full_paths: false` param, opt-in), scanned roots chosen in the consent flow — each detected CC home is a checkbox, **unchecked by default** (the tool scans only user-selected roots; zero selected means the tool returns `no_data`/`disabled`), and the response always names the roots it covered. Params: `window_days` (enum 1|7|30), `full_paths` (bool). No free-form paths, ever. Response includes `roots_scanned`, `files_scanned`, `window_days`, `since`, and the retention caveat ("CC deletes session logs after ~30 days; totals are lower bounds; cache tokens excluded"). Reuses the 30s aggregate cache; works when the widget has never run (per-tool data dependencies declared in the manifest).

3. **`ping`** — the verify anchor and health surface: `{ server_version, schema_version, snapshot_path, snapshot_found, snapshot_age_seconds, cc_roots_found }`. Distinguishes server-broken from data-absent; first step of every cold verify.

All tools carry `readOnlyHint: true` annotations. Schema-mismatch handling: on snapshot major-version above its own, the server returns `no_data`/`schema_unsupported` ("update Sanduhr") — never a lenient best-effort parse (a stale exe silently returning 0% is how the quota tool green-lights a fleet into a 94% week).

## Statusline script

- Installed OUTSIDE the package trees (survives uninstall), reads the snapshot per the reader contract, renders per the staleness table. Tier-attached reset copy (`wk resets Fri 7p` — never a dangling instant that misreads as the wrong tier's). Tier selection honors existing settings (`hidden_tiers` or a new `statusline_tiers`), not hardcoded. Pace vocabulary from `Pacing` ("ahead 9%", not sign-ambiguous "+9%"). ~55 chars worst case.
- Defensive on `schema_version`: unknown major → `sanduhr: update statusline`, and the widget re-checks/reinstalls the script on its own updates (the script has no update channel of its own — the widget is its updater).

## Registration, consent, uninstall

- **Consent dialog** (themed) names everything it touches: which file(s), which CC home, what gets written, how to remove. **CC-home picker whenever more than one home exists** (`~/.claude` + `~/.claude-personal` are both live on the primary dev machine and one is an employer tenant — silent default is a tenant breach). The chosen home is recorded so removal targets the same file.
- **Config-write safety:** re-read immediately before write, mutate only the owned key (`statusLine` / the `sanduhr` MCP entry), preserve unknown keys, temp + `File.Replace`, timestamped backup beside the file. Advise installing while CC is idle.
- **Registration strings:** MSIX → **appExecutionAlias** `sanduhr-mcp.exe` (one `uap3:Extension` on the existing Application node; absolute WindowsApps paths are version-dead AND ACL-blocked). Velopack → `%LOCALAPPDATA%\Sanduhr\current\sanduhr-mcp.exe`. **Decision: ship a thin shadow-copy launcher** (installed under `%APPDATA%\Sanduhr\bin\`) that copies the single-file exe to a per-session temp path and execs it — collapses both channels to one registration string and solves **update-under-lock** (parallel CC sessions would otherwise pin the exe and stall Velopack swaps and Store servicing indefinitely). User-scope registration only; never project `.mcp.json`.
- **Dual-install collision:** installer detects an existing `sanduhr` registration, shows what it points at, re-owns it. Last explicit install wins. `writer_version` in the snapshot lets the server detect a dead writer's file.
- **Uninstall, three layers:** (1) in-app "Remove Claude Code integration" reverts both entries; (2) Velopack `OnUninstall` hook deregisters (the `VelopackApp` seam in `Program.cs` exists today); (3) MSIX can't hook uninstall — statusline degrades to empty output (invisible, by design), the MCP orphan is documented (`claude mcp remove sanduhr`), and the widget health-checks/repairs registrations on every start while installed.

## Packaging & release

- appxmanifest gains the alias extension — no second `<Application>`, no tile.
- **Velopack channel signs both exes before this feature ships there** (Smart App Control on new Win11 consumer installs blocks unsigned exes regardless of launch path; EDR heuristics on "node spawns unknown unsigned exe reading ~/.claude" risk quarantine; SmartScreen itself is a non-issue for CreateProcess). Azure Trusted Signing is the cheap route. This is a channel prerequisite, trackable separately.
- **Cert letter rebudget now** (current letter: 1709 of ~2000 chars): front-load the second-exe paragraph — on-demand stdio child of the user's own CLI, no service, no autostart, no network listener, declared alias, removed on uninstall — keep the runFullTrust justification, push detail to a hosted URL, pre-write the sub-1k fallback per the playbook.

## PRIVACY.md — ships in the same release

New rows: `snapshot.json` (contents, location, who reads it, opt-in + revocation-deletes-it); the MCP caveat (tool results become part of the Claude conversation, governed by Anthropic's policy — scoping the existing "never transmitted anywhere" absolute); the two CC config entries Sanduhr writes and their manual removal.

## Testing & verification

- **Core (xUnit):** `SnapshotContract` band math (fresh/stale/dead boundaries, negative-age clamp), snapshot serializer round-trip + schema_version, reset-crossing suppression, `account_ref` hashing, atomic-writer behavior (temp file cleanup, retry), reader parse-failure = missing.
- **Fixtures for cold verify:** dev-tier env overrides `SANDUHR_SNAPSHOT_PATH` + `SANDUHR_CC_ROOTS` (read-redirection only, env-gated, never ship enabled in config) drive the four `get_usage` states (fresh / never-ran / stale / malformed) and the CC-log fixtures (well-formed, malformed line skipped, no roots). `ping` anchors every run.
- **agent-access.json manifest** at the app root: every affordance (three tools, the snapshot file itself, `statusline.render`) with tier `prod-safe`, `auth: none-local-ambient`, error enums, staleness constants, `data_class` markers (`cross-tenant-risk` on the burn tool), all stamped `unverified` until `:verify` passes. **The verify plan runs once per channel** (MSIX + Velopack) — they differ in exactly the ways the review flagged.
- **Manual smoke** additions: consent flow with two CC homes present; uninstall → statusline invisible, MCP removal documented; update while a CC session holds the launcher-spawned server.

## Effort

Statusline Bridge: **M** (resized from S — lifecycle, failure-status writes, in-script reader contract, home picker are real work). MCP server: **M**. Suggested build order inside WS-E: snapshot writer + contract (Core-heavy, testable) → statusline → MCP server → registration/consent UX → manifest + verify.
