# The agent layer: design

Windows, 5.0 spec 2 of 4 (the frame is in `2026-10-28-drawers-design.md`). Brainstormed with the owner on
2026-11-09. Dashboard decisions: `BbeHEHW13RNoC3BvXhqA` (what Codex shows, logs only), `1QKGaDL4EO3uGVKVnSK3`
(stacked widget), `8Zg5H83tJtUkPyihcfry` (tray follows a picked source), `2DGKYMAQtbI7n29NGZAB` (source colors
and the two-tone bar), `A6gnihFnO4VaDMe96dzj` (the compressed-rollout gotcha).

## The goal

Someone pacing against more than one agent's plan sees every limit at once. Sanduhr reads Claude the way it
does today and Codex from Codex's own session logs, puts both in one widget, and draws them on shared meters
so the nearest wall is never hidden.

**Success:** someone with Claude and Codex ticks both in Set up Sanduhr, runs one Codex turn, and the widget
shows both plans' 5-hour and weekly limits within a minute, with no sign-in for Codex and no network call
on its behalf.

## Out of scope

- A live fetch of Codex limits from OpenAI's servers, and reading `~/.codex/auth.json` for any reason. Its own
  spec, if the "as of" staleness bothers people.
- Codex local activity (sessions, tokens, time per project). Stage 2 of this spec, below; it does not block
  stage 1.
- Gemini CLI and local models (spec 3), the rename (spec 4), the Mac (its own cycle).
- Two ChatGPT accounts on one PC told apart. They show as one (see Edge cases).

## 0. The check (blocks everything else)

On this machine Codex keeps its rollouts compressed (`sessions/**/rollout-*.jsonl.zst`, 65 files, none plain)
next to SQLite stores (`state_5.sqlite`, `thread_history_1.sqlite`). Decompress one recent rollout and confirm:

1. `token_count` events still carry `rate_limits`, with a primary (5-hour) and secondary (weekly) window, each
   with a used percent, a window length and a reset time.
2. Which field names and units they use (percent or fraction; reset as epoch seconds or seconds-from-now).
3. Whether a plain `.jsonl` rollout from an older Codex has the same shape.

The verdict goes in `windows-dotnet/docs/checklist.md`. If the limits are not in the logs, stop and come back to
the owner with routes (the SQLite stores, or the live-fetch spec moved up). Do not build on a guess.

Same finding applies to Sanduhr Time, whose Codex reader walks `rollout-*.jsonl` only: file a Sanduhr Time
issue for `.zst` once the check confirms the shape.

## 1. Sources (Core)

A **source** produces a list of **limits**. Each limit carries a key, a used percent, a reset time, a window kind
(`five_hour`, `weekly`, or none) and an `as_of` time.

- **Claude** is today's `UsageFetcher`, unchanged: claude.ai on the usual schedule. Its 5-hour and weekly tiers
  map to the two window kinds; per-model and other tiers carry none.
- **Codex** is a new `CodexLimitsReader` in `Sanduhr.Core`. It finds Codex homes (`CODEX_HOME` if set, plus
  `%USERPROFILE%\.codex`), lists rollouts newest first, reads each from its end, and takes the newest
  `token_count` event with `rate_limits`. It reads plain `.jsonl` and `.jsonl.zst` (ZstdSharp or the .NET
  decompressor the check settles on), with decompression capped by size like `WidgetZip`. It never opens
  `auth.json`, never touches the network, and extracts only percents, window lengths and times: no message
  text, no account id, no path leaves the reader.
- The widget polls the Codex reader every 30 seconds while Codex is ticked, and also on a file-change watch on
  the sessions folders, so a Codex turn shows within seconds.

## 2. Downstream: everything takes a source

- **History** points gain a `source` field. Existing points read as `claude`.
- **`snapshot.json`** gains a `source` on each account entry, additive, so older readers ignore it. Codex
  writes one entry, `source: "codex"`, with no account label.
- **The MCP and the statusline** read `snapshot.json` already, so `get_usage` returns Codex rows with no new
  Core file linked into `Sanduhr.Mcp`. The MCP stays network-free and `TrustBoundaryTests` do not change.
- **Alerts** are switched on per source in Settings ▸ Alerts. Codex alerts fire only on a fresh limit (see
  section 4), so an old log never raises one.
- **The tray** follows the primary source (section 3).

## 3. The widget, the meters and Settings

**Source colors.** Every source has a default color, changeable in Settings ▸ Usage and checked against each
built-in theme for contrast: Claude a warm orange, Codex a teal (exact values set at build time and pinned by
a contrast test). Colors evoke the brands and never copy their exact brand hexes (Store 10.1.4.4). Color is
never the only signal: every tick and segment has an accessible name and a tooltip ("Codex weekly, 62%").

**The two-tone bar.** On a meter that shows more than one source:

- The **primary** source fills from 0 to its percent in its color.
- If another source is further out, the bar continues from the primary's level to that source's percent in
  that source's color.
- Every other source is a tick in its color at its percent.

The fill's length always means "where my main plan is"; the second segment shows the nearest wall past it.

**Combined bars pair matching windows.** Claude and Codex both have a 5-hour and a weekly limit, so there are
two combined bars, one per window. A limit with no window kind (Claude's per-model limits) stays on its own
card only.

**The widget:** the combined bars on top, then a stacked section per ticked source: a header (name, color,
collapse chevron) over that source's cards. Collapsing every source leaves only the combined bars, the slim
view. With only one source ticked there are no combined bars, only that source's section, which is today's widget.

**The Codex card** has the same meters, pace marker and burn projection as Claude's. Under its header: "as of
3:42 PM". It dims when stale (section 4).

**The Desk:** each meter piece gains a source setting, Combined or one source. The ring and slim looks draw the
two-tone fill and the ticks.

**The tray** draws the primary source's fill and, only when another source is further out, the second
segment. The tooltip spells out both.

**Settings ▸ Usage:** one row per source with its on switch, its color and a Primary choice. Primary defaults to
Claude, or to the only ticked source. Choosing the primary also sets the tray. The setup screen's "Track my AI
usage" list (`setup.usage`, setup spec) gains `codex`.

## 4. Freshness and edge cases

A Codex limit is **fresh** for 30 minutes after its `as_of`. After that it is **stale**: shown dimmed with its
"as of" time and date, left out of alerts, and still drawn on combined bars but dimmed. A window whose reset
time has passed shows **0%** with "reset at 3:10 PM", fresh again, since the logs alone prove the reset.

| State | What shows |
|---|---|
| Codex not installed (no Codex home) | Codex greyed out in setup and Settings: "Codex isn't installed on this PC." No card. |
| Installed, no limits in the logs yet | The card: "Run Codex once to see your limits." No meters; nothing joins the combined bars. |
| No rollout in the last 7 days | The last known numbers, stale, with the date. Passed resets show 0%. |
| A log can't be read (locked, corrupt, unknown format) | Skip it, use the next-newest. If none: "Couldn't read Codex's logs." Detail to the local log only, never a path on screen (streamer mode). |
| Two Codex homes | The newest limits event across both wins (one plan, one account). Two ChatGPT accounts show as one: a known limit. |
| Codex changes its log format | The reader pins the fields it needs, ignores unknown ones, and treats a missing one as "no limits", never a crash. A fixture test catches the change. |
| A large compressed rollout | Read from the end; decompression capped by size. |

## 5. Stage 2: Codex local activity

After stage 1 ships: port Sanduhr Time's Codex reader (`vscode/src/codex/`, with `.zst` added) into the
existing local-burn view, so Codex sessions and tokens show per project beside Claude Code's. Same privacy
rules as that reader: timestamps, kinds, hashed streams, project keys, counts. Its own plan when stage 1 is in.

## Testing

- **Core:** `CodexLimitsReader` against fixture rollouts, plain and `.zst`: newest-wins across two homes, a passed
  reset, malformed lines, missing fields, a size-capped file. A test that the reader never opens `auth.json`.
- **The two-tone bar** is layout maths in Core: segments and ticks for every ordering of two and three sources,
  equal percents, a stale source, a source at 0 and 100.
- **Colors:** each default against all six built-ins, pinned by `ThemeContrastTests`.
- **App:** setup and Settings rows, Primary moving the tray, collapse state, and the accessible names on ticks
  and segments.
- **Trust boundary:** `TrustBoundaryTests` unchanged, proving the MCP link list did not grow.
- **Live:** this machine's real Codex logs, after section 0 passes.

## Build order

0. The check.
1. `CodexLimitsReader` and its fixtures.
2. Source tags through history, `snapshot.json` and alerts.
3. The two-tone bar maths and the source colors.
4. The widget's combined bars and stacked sections; the Codex card.
5. Settings ▸ Usage rows, Primary and the tray; `setup.usage.codex`.
6. Desk meter pieces' source setting.
7. What's New card, CHANGELOG, PRIVACY.md (the new local read of Codex's logs), screenshots.
