/**
 * One reader pass over every agent's transcripts: Claude's, then Codex's. Each reader keeps
 * its own byte budget and shares `state/offsets.json` (they run one after the other, under the
 * merge lock the runner holds), and the results are pooled for the runner.
 */
import { readPass as readClaudePass, type ReadPassOptions, type ReadPassResult } from './claude/reader';
import { readCodexPass } from './codex/reader';

export interface AgentPassOptions extends ReadPassOptions {
  /** Explicit Codex homes; default is `discoverCodexHomes` (`CODEX_HOME`, `~/.codex`). */
  codexHomes?: string[];
}

export function combinePasses(a: ReadPassResult, b: ReadPassResult): ReadPassResult {
  const unreadableByDate = { ...a.unreadableByDate };
  for (const [d, n] of Object.entries(b.unreadableByDate)) unreadableByDate[d] = (unreadableByDate[d] ?? 0) + n;
  const versionsByDate: Record<string, string[]> = { ...a.versionsByDate };
  for (const [d, v] of Object.entries(b.versionsByDate)) {
    versionsByDate[d] = [...new Set([...(versionsByDate[d] ?? []), ...v])].sort();
  }
  return {
    events: [...a.events, ...b.events],
    more: a.more || b.more,
    unreadableByDate,
    touchedDates: [...new Set([...a.touchedDates, ...b.touchedDates])].sort(),
    versionsByDate,
  };
}

export async function readAgentsPass(opts: AgentPassOptions): Promise<ReadPassResult> {
  const claude = await readClaudePass(opts);
  const codex = await readCodexPass({
    dir: opts.dir,
    installed: opts.installed,
    homes: opts.codexHomes,
    homeDir: opts.homeDir,
    env: opts.env,
    maxBytes: opts.maxBytes,
  });
  return combinePasses(claude, codex);
}
