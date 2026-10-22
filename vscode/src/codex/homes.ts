import * as fs from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';
import type { HomeOptions } from '../claude/homes';

function hasSessions(dir: string): boolean {
  try {
    return fs.statSync(path.join(dir, 'sessions')).isDirectory();
  } catch {
    return false;
  }
}

/**
 * Codex homes that hold rollouts: `CODEX_HOME` when set, plus `~/.codex`, each only when it
 * contains `sessions/`. De-duplicated by real path. `archived_sessions/` is never read: a
 * thread moved there has a new path, so reading it would count its events a second time.
 */
export function discoverCodexHomes(opts: HomeOptions = {}): string[] {
  const homeDir = opts.homeDir ?? os.homedir();
  const env = opts.env ?? process.env;

  const candidates: string[] = [];
  const fromEnv = env.CODEX_HOME;
  if (fromEnv && fromEnv.trim() !== '') candidates.push(fromEnv.trim());
  candidates.push(path.join(homeDir, '.codex'));

  const seen = new Set<string>();
  const homes: string[] = [];
  for (const candidate of candidates) {
    let real: string;
    try {
      real = fs.realpathSync(candidate);
    } catch {
      continue;
    }
    if (seen.has(real) || !hasSessions(real)) continue;
    seen.add(real);
    homes.push(real);
  }
  return homes;
}

const ROLLOUT = /^rollout-.*\.jsonl$/;

/** Every `sessions/**\/rollout-*.jsonl` under one home, sorted for a stable order. */
export function listRollouts(home: string): string[] {
  const out: string[] = [];
  const walk = (dir: string): void => {
    let entries: fs.Dirent[];
    try {
      entries = fs.readdirSync(dir, { withFileTypes: true });
    } catch {
      return;
    }
    for (const e of entries) {
      const full = path.join(dir, e.name);
      if (e.isDirectory()) walk(full);
      else if (e.isFile() && ROLLOUT.test(e.name)) out.push(full);
    }
  };
  walk(path.join(home, 'sessions'));
  return out.sort();
}
