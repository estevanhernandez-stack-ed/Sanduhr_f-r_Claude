# Cold-verify plan + record — sanduhr-mcp surface

The repeatable verification for the five affordances in `agent-access.json`
(three MCP tools, the snapshot file, the statusline render). Runs cold: the
verifying agent gets the manifest, the fixture kit, and the drive mechanics —
never the implementation source. Per the WS-E design review, **the full plan
runs once per channel**: dev (done), MSIX, and Velopack differ in exactly the
ways the review flagged (virtualization, alias launch, update-under-lock).

## Record

| Run | Date | Channel | Result | runId |
|---|---|---|---|---|
| 1 | 2026-07-27 | dev (Debug exe) | **5/5 affordances PASS, 27 checks** | af4bba22938cc9f2a |
| 2 | 2026-07-27 | dev (Debug exe) | **Delta (abilities wave): 3/3 in-scope PASS + regression spot-check** | abb80276ee14af7ae |

Run-1 notes: one plan deviation (fresh-state burn expected 3100, got 0) was
counter-probed by the agent and traced to fixture event timestamps predating
the snapshot anchor — server correct, kit fixed (`mcp-verify-fixtures.ps1`).
Seven contract observations were folded back into the manifest descriptions.

Run-2 notes (7-tool wave: pacing depth + get_model_usage + get_usage_history):
pace anchor proven to be CALL time (cooldown = ideal − age_seconds — manifest
updated to say so); the kit gained `state-fresh-fable` (meter-join coverage)
and the no-consent probe guidance below; history's `to` is the LOCAL calendar
day while fixture keys derive from UTC — schedule verify runs away from
midnight or expect day-count skew.

## How to re-run

1. Copy `mcp-verify-fixtures.ps1` to a scratch dir; the agent runs it first
   (age bands are stamped relative to now — never reuse old fixture output).
2. Build the channel's server binary (dev: `dotnet build src/Sanduhr.Mcp`).
3. Dispatch a cold agent with: `agent-access.json`, the exe path, the fixture
   dir, and the plan below. The agent must not read `windows-dotnet/src` or
   `windows-dotnet/tests`.
4. Stamp results into `agent-access.json` (`verified.status/at/runId/detail`).

## The plan

- **Protocol/catalog:** initialize echoes protocolVersion; tools/list = exactly
  get_usage, get_local_burn_by_project, ping; all readOnlyHint:true; burn
  schema admits only window_days enum [1,7,30] + full_paths bool.
- **ping:** snapshot_found/age/path track the env-selected fixture;
  cc_roots_consented reflects the override; found reflects the machine scan.
- **get_usage, one server run per state:** fresh→ok; stale→stale; dead→stale +
  widget_not_polling + remedy; error→stale + fetch_error + last-good tiers;
  missing→no_data/missing; malformed→no_data/malformed; schema2→
  no_data/schema_unsupported. Invariant across all: normal JSON-RPC result,
  isError:false. Fresh with roots consented: burn since captured_at (mapped
  tokens under their tier, unmapped kept in total); without: null.
- **get_local_burn_by_project:** no consent→disabled; per-root keying;
  basenames by default (no fixture full path leaks); full_paths opt-in;
  window filter at 1 vs 7; window 3 → typed invalid_params; roots_scanned
  named in every ok response.
- **snapshot.file.read:** parses per documented shape; account_ref is 8-hex;
  no readable identity.
- **statusline.render** (installed script + APPDATA redirect): fresh→numbers;
  stale→numbers + age marker; dead→'start widget' line (never blank);
  missing→empty. Pure ASCII in every case (raw-byte capture).

- **get_model_usage:** window guard (enum 1|7|30); ranked models with share;
  tier mapping; meter join from the fable-tier state; unmapped model visible
  with null tier; missing snapshot still serves tokens with null meters.
- **get_usage_history:** window guard (enum 7|30|90); totals + sent/received
  split; gap days OMITTED (never zero); project display names only (no ~hash);
  empty vault → missing + vault remedy; disabled checked before vault.

Env redirection (dev tier only, never shipped config):
`SANDUHR_SNAPSHOT_PATH=<state>\snapshot.json`,
`SANDUHR_CC_ROOTS=<roots dir>` (';' list),
`SANDUHR_VAULT_DIR=<vault dir>`. Drive: newline-delimited JSON-RPC
on stdin; tool payloads arrive as `result.content[0].text` (JSON string).

No-consent probe on a machine with LIVE consent: an absent/empty
`SANDUHR_CC_ROOTS` falls through to the real settings map (by design) — reach
the disabled path by pointing the override at a nonexistent directory instead.
