import * as fs from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';

export interface HomeOptions {
  /** User home directory. Default `os.homedir()`. */
  homeDir?: string;
  /** Environment. Default `process.env`. */
  env?: NodeJS.ProcessEnv;
}

function hasProjects(dir: string): boolean {
  try {
    return fs.statSync(path.join(dir, 'projects')).isDirectory();
  } catch {
    return false;
  }
}

/**
 * Claude config homes that hold transcripts: `CLAUDE_CONFIG_DIR` when set, plus every
 * directory in the user's home whose name starts with `.claude` and contains `projects/`.
 * Names containing `.config-backup-` are skipped; results are de-duplicated by real path.
 */
export function discoverHomes(opts: HomeOptions = {}): string[] {
  const homeDir = opts.homeDir ?? os.homedir();
  const env = opts.env ?? process.env;

  const candidates: string[] = [];
  const fromEnv = env.CLAUDE_CONFIG_DIR;
  if (fromEnv && fromEnv.trim() !== '') candidates.push(fromEnv.trim());
  try {
    for (const entry of fs.readdirSync(homeDir, { withFileTypes: true })) {
      if (entry.name.startsWith('.claude')) candidates.push(path.join(homeDir, entry.name));
    }
  } catch {
    /* unreadable home: env candidate only */
  }

  const seen = new Set<string>();
  const homes: string[] = [];
  for (const candidate of candidates) {
    if (path.basename(candidate).includes('.config-backup-')) continue;
    let real: string;
    try {
      real = fs.realpathSync(candidate);
    } catch {
      continue;
    }
    if (seen.has(real) || !hasProjects(real)) continue;
    seen.add(real);
    homes.push(real);
  }
  return homes;
}
