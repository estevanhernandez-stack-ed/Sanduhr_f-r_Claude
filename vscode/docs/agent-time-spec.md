# Spec: unified agent time (Codex, then Copilot CLI, alongside Claude)

Status: draft for review, 2026-10-22. Dashboard task "Sanduhr Time: unified agent time".

## Why

Sanduhr Time reads Claude Code transcripts and nothing else. On 2026-10-10, Codex in VS Code made
232 of Neb's 246 WakaTime heartbeats and Sanduhr Time recorded 0 for it. The builder this is for
orchestrates several agents and rarely types (`linesYou` is honestly 0 on Neb), so a day with Codex
in it reads as an empty day. The aim is one picture: you, each agent, and the overlap.

## What Codex writes (measured on Neb, 2026-10-22, Codex Desktop 0.162–0.163 alpha)

Shape only; no message content was read for this spec.

- **Where:** `~/.codex/sessions/YYYY/MM/DD/rollout-<local start>-<thread id>.jsonl`. The folder date
  is the thread's start, not the day of its events.
- **One file per thread, and threads live for days.** One rollout started 2026-10-08 and was still
  being appended on 2026-10-22 (9,433 lines). Read every rollout incrementally by byte offset (the
  existing `state/offsets.json`), and assign each record to the local day of its own `timestamp`.
- **Every line:** `{ type, timestamp (ISO 8601, UTC "Z"), ordinal, payload, metadata? }`.
- **`session_meta`:** `cwd`, `originator` (`Codex Desktop`), `source` (`vscode`, or an object for
  spawned threads), `thread_source` (`user`, `subagent`, `guardian_review`), `session_id`,
  `cli_version`. Credentials and account ids also appear here and are **never stored**.
- **`turn_context`:** per turn, carries `cwd` and `workspace_roots` (a turn can move folders).
- **Activity records:** `response_item` of type `reasoning`, `message` (role `assistant`),
  `agent_message`, `function_call` / `function_call_output`, `custom_tool_call` /
  `custom_tool_call_output`; `event_msg` `task_started` and `item_completed`.
- **User messages:** `response_item` `message` role `user`, content a list of `input_text` (and
  `input_image`). `metadata.client_authored` was `false` on 53 and absent on 16, never `true`, so it
  does **not** mark the human's own prompts. See the probe below.
- **Not used:** `token_count` / `token_usage_record` (cost, not time), `world_state`,
  `thread_settings_applied`.

## Copilot CLI

Not measured yet: Neb's `~/.copilot` holds only `config.json`, IDE lock files and process logs, no
session transcripts. Copilot CLI gets its reader when a machine has run it and its session files can
be measured the same way. Don't guess its format.

## Model

Generalize the Claude event into an agent event; Claude's reader and rules don't change.

| Field | Claude today | Codex |
|---|---|---|
| `agent` | `claude` (new field, default) | `codex` |
| `stream` | sha of the transcript path | sha of the rollout path plus `session_id`, so subagent and guardian threads inside one file are their own streams |
| `kind: prompt` | the human-prompt rule (spec 4) | the Codex human-prompt rule below |
| `kind: activity` | assistant entries, tool results | the activity records above |
| `kind: edit` | Edit/Write tool calls with hashed path and line counts | `apply_patch` tool calls: one edit per file in the patch, hashed path, lines added/removed counted from the patch |
| `project` | the resolver on the transcript's cwd | the resolver on `turn_context.cwd` (fallback `session_meta.cwd`) |

Spool: `claude/<date>.jsonl` becomes `agents/<date>.jsonl` with the `agent` field. Old Claude spool
files are read as `agent: claude`. Nothing but timestamps, kinds, hashed entities, project keys,
line counts and the agent name is ever written, the same contract as Claude's reader.

## Merge rules

Today's three buckets stay: **you**, **agents**, **both**. "Claude" becomes "agents": the union of
every agent's intervals, so `totalMs = youMs + agentsMs + bothMs` still holds and a minute is
counted once.

- Each agent's activity joins with the existing gap (5 min) and lone event (2 min) rules.
- Prompts to any agent are your time, with the read-and-dictate gap (#175) measured inside that
  prompt's own stream.
- `subagent` and `guardian_review` threads are agent activity, never prompts.
- New per-agent breakdown at day and project level: `byAgent: { claude: { wallMs, streamMs },
  codex: { ... } }`. `wallMs` is that agent's own union; `streamMs` is the sum of its streams (today's
  `agentMs`).

**Schema:** day files go to `v: 2`. `claudeMs` is renamed `agentsMs`; readers of v1 files map
`claudeMs` to `agentsMs` and `byAgent.claude`. The panel's "Claude" row becomes one row per agent
plus "Overlap". The 626 extension's publisher and `manage_time` pass `byAgent` through, and the
bulletin says "Claude 3h, Codex 1h" when more than one agent ran.

## The Codex human-prompt rule (probe run 2026-10-22)

Este sent `sanduhr probe 1` in a fresh Codex thread. In that rollout, the probe was the only `user`
message carrying `metadata.user_input_order` (with `retained_source`, `client_authored: false` and
`mcp_attribution`). The two `user` messages Codex injected before it (instructions and context) had
no metadata at all. Across all of Neb's Codex history:

| thread_source | user_input_order | count | meaning |
|---|---|---|---|
| `user` | yes | 54 | your prompts |
| `user` | no | 16 | injected context; one carried only `mcp_attribution` |
| `guardian_review` | yes | 63 | Codex's self-review prompts: machine-written |
| `guardian_review` | no | 1 | injected |
| `subagent` | no | 4 | task prompts: machine-written |

**Rule.** A Codex record is the human's prompt (`kind: "prompt"`) when all hold: `type` is
`response_item`; `payload.type` is `message` with `role` `user`; `metadata.user_input_order` is
present; and the latest `session_meta` above it in the file has `thread_source` `user`. One rollout
can hold several `session_meta` segments (subagent and guardian threads inside one file), so the
reader tracks the current segment, and its `session_id` keys the stream. `event_msg`
`item_completed` repeats the prompt's text and is not counted. Fixtures copy these shapes with
made-up text; no real transcript content enters the repo.

## Order of work

1. The Codex reader with fixtures and tests, using the rule above (reader only, no merge change).
2. Agent events and spool v2 with the `agent` field; Claude's reader writes `agent: claude`.
3. The merger: agents bucket, `byAgent`, day schema v2 with the v1 mapping, tests for overlap
   between Claude and Codex running together.
4. Panel rows per agent; WakaTime comparison unchanged (it already compares full totals).
5. The 626 extension's publisher and `manage_time` / bulletin carry `byAgent` (626 Labs repo).
6. Copilot CLI, once measured.

## Open questions

- Should `guardian_review` (Codex reviewing its own work) count as agent time or be dropped? Draft:
  count it, since it is Codex working on your project.
- Codex Desktop outside VS Code: does it write the same `sessions/` tree? The one non-VS-Code session
  measured did, so draft: yes, one reader for both.
- Do Codex's cloud tasks write rollouts locally? Out of scope until seen.
