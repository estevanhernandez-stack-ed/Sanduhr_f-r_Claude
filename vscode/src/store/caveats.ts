/**
 * `state/caveats.json`: per local date, the Claude Code versions seen in transcripts and
 * the running count of unreadable transcript lines. The reader reports these per pass;
 * persisting them keeps a later merge of the same date from forgetting earlier passes.
 */
import * as fs from 'node:fs';
import * as path from 'node:path';
import { localDate } from './claudeSpool';
import { writeFileAtomic } from './state';

export interface DayCaveatState {
  versions: string[];
  unreadable: number;
}
export type CaveatsState = Record<string, DayCaveatState>;

const FILE = 'caveats.json';
const KEEP_DAYS = 90;

export function readCaveats(stateDirPath: string): CaveatsState {
  try {
    const o = JSON.parse(fs.readFileSync(path.join(stateDirPath, FILE), 'utf8')) as CaveatsState;
    return o && typeof o === 'object' ? o : {};
  } catch {
    return {};
  }
}

/** Fold one reader pass into the persisted state and prune entries older than 90 days. */
export function recordCaveats(
  stateDirPath: string,
  pass: { versionsByDate: Record<string, string[]>; unreadableByDate: Record<string, number> },
  now: number = Date.now(),
): CaveatsState {
  const state = readCaveats(stateDirPath);
  let changed = false;
  const entry = (d: string): DayCaveatState => (state[d] ??= { versions: [], unreadable: 0 });
  for (const [d, versions] of Object.entries(pass.versionsByDate)) {
    const e = entry(d);
    const merged = [...new Set([...e.versions, ...versions])].sort();
    if (merged.length !== e.versions.length) changed = true;
    e.versions = merged;
  }
  for (const [d, n] of Object.entries(pass.unreadableByDate)) {
    if (n <= 0) continue;
    entry(d).unreadable += n;
    changed = true;
  }
  const cutoff = localDate(now - KEEP_DAYS * 86_400_000);
  for (const d of Object.keys(state)) {
    if (d < cutoff) {
      delete state[d];
      changed = true;
    }
  }
  if (changed) writeFileAtomic(path.join(stateDirPath, FILE), JSON.stringify(state));
  return state;
}
