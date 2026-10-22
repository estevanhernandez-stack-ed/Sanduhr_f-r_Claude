# Changelog

## [Unreleased]

## [0.3.0]

- **Codex time counts.** Sanduhr Time now reads Codex's rollouts (`~/.codex/sessions`, or `CODEX_HOME`) as well as Claude Code's transcripts. On Neb's Oct 10, Codex made 232 of 246 WakaTime heartbeats and Sanduhr recorded none of it. A Codex thread can run for days, so every record counts on its own day. Your prompts to Codex are your time, with the same read-and-dictate gap as Claude's. Codex's subagent and self-review threads are Codex working, never your prompts. Patches Codex applies count as its lines.
- **One agents bucket, one row per agent.** The split is you, agents and both. The agents part is the union of every agent, so a minute Claude and Codex share counts once. The panel names the agent when only one ran, and lists each agent's own time and lines when several did. The status bar tooltip does the same.
- **Day files are v2.** `claudeMs` is now `agentsMs`, `linesClaude` is `linesAgents`, and `byAgent` holds each agent's wall time, session time and lines, at day and project level. Older day files are read as v2 with their time under Claude, so the 7-day strip keeps its history. The agent spool moves from `claude/` to `agents/`. Existing `claude/` files are still read, and transcript positions carry over, so nothing is lost or counted twice.
- **The API stays version 1.** Records from `getToday`, `getDays` and `onDayUpdated` keep `claudeMs` and `linesClaude` beside the new fields. Both now count every agent. A publisher should move to `agentsMs` and `byAgent`.
- Day files say which rule made them: `caveats.readerVersion` is `0.3.0`.

## [0.2.0]

- **Your time counts the read-and-dictate gap (#175).** A prompt's span now reaches back to the last Claude activity in its own session when that's within your idle minutes (default 15). The time spent reading Claude's answer, thinking and dictating the next prompt is yours. A builder who orchestrates and rarely types got only 2 minutes per prompt before. Longer gaps keep the 2-minute span, and a subagent's activity never shortens the gap. On Neb's Oct 21, your time went from 3.97 h to 5.07 h.
- Day files say which rule made them: `caveats.readerVersion` is `0.2.0`.
- **The WakaTime comparison compares like with like.** `wakatime-cli --today` totals every machine on your account and counts files any tool writes, so the panel now sets it against Sanduhr's full total on this machine, labels it "all machines", and shows your share beside it. Before, it compared every machine against your typing time on this one, so a Codex-heavy day elsewhere looked like missing hours here. `wakatime-compare.jsonl` lines gain `oursTotalSeconds`.

## [0.1.0]

- Initial scaffold.
