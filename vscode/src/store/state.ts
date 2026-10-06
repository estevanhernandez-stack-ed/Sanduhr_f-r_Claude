import { randomBytes } from 'node:crypto';
import * as fs from 'node:fs';
import * as path from 'node:path';
import type { InstalledState, OffsetsState } from '../types';

const INSTALLED = 'installed.json';
const OFFSETS = 'offsets.json';
const LOCK = 'merge.lock';
const STALE_AFTER_MS = 2 * 60 * 1000;

/** Write a file atomically: tmp in the same folder, then rename. */
export function writeFileAtomic(file: string, data: string): void {
  fs.mkdirSync(path.dirname(file), { recursive: true });
  const tmp = `${file}.${process.pid}.${randomBytes(4).toString('hex')}.tmp`;
  fs.writeFileSync(tmp, data);
  fs.renameSync(tmp, file);
}

/**
 * Create `installed.json` on first call (ISO timestamp plus a random 32-byte hex salt).
 * Never overwrites an existing file. `isNew` is true only for the call that created it.
 */
export function ensureInstalled(
  dir: string,
  now: () => Date = () => new Date(),
): { installed: InstalledState; isNew: boolean } {
  const file = path.join(dir, INSTALLED);
  // Owner-only: aliases.json holds real project names and offsets.json real paths.
  // Applies to every directory this call creates (the data root on first run); no-op on Windows.
  fs.mkdirSync(dir, { recursive: true, mode: 0o700 });
  const fresh: InstalledState = {
    installedAt: now().toISOString(),
    salt: randomBytes(32).toString('hex'),
  };
  try {
    // Exclusive create: two windows racing cannot both win.
    fs.writeFileSync(file, JSON.stringify(fresh, null, 2), { flag: 'wx' });
    return { installed: fresh, isNew: true };
  } catch (err) {
    if ((err as NodeJS.ErrnoException).code !== 'EEXIST') throw err;
  }
  return { installed: JSON.parse(fs.readFileSync(file, 'utf8')) as InstalledState, isNew: false };
}

/** Read `offsets.json`; a missing file is an empty map. */
export function readOffsets(dir: string): OffsetsState {
  try {
    return JSON.parse(fs.readFileSync(path.join(dir, OFFSETS), 'utf8')) as OffsetsState;
  } catch (err) {
    if ((err as NodeJS.ErrnoException).code === 'ENOENT') return {};
    throw err;
  }
}

export function writeOffsets(dir: string, offsets: OffsetsState): void {
  writeFileAtomic(path.join(dir, OFFSETS), JSON.stringify(offsets));
}

interface LockBody {
  pid: number;
  /** Epoch milliseconds of the last create or refresh. */
  at: number;
}

export interface LockOptions {
  /** Epoch ms clock. */
  now?: () => number;
  /** Whether a pid is running. Default: `process.kill(pid, 0)`; ESRCH means gone. */
  isPidAlive?: (pid: number) => boolean;
  pid?: number;
}

export interface MergeLock {
  /** Rewrite `at` to now; call about every 30 seconds while held. */
  refresh(): void;
  /** Remove the lock if this holder still owns it. */
  release(): void;
}

export function defaultIsPidAlive(pid: number): boolean {
  try {
    process.kill(pid, 0);
    return true;
  } catch (err) {
    // EPERM means the process exists but belongs to someone else.
    return (err as NodeJS.ErrnoException).code !== 'ESRCH';
  }
}

/**
 * Take the merge lock with an exclusive create. Returns undefined when a live holder
 * has it. A lock is stale only when `at` is more than 2 minutes old AND its pid is not
 * running; a stale lock is replaced.
 */
export function acquireLock(dir: string, opts: LockOptions = {}): MergeLock | undefined {
  const now = opts.now ?? Date.now;
  const isPidAlive = opts.isPidAlive ?? defaultIsPidAlive;
  const pid = opts.pid ?? process.pid;
  const file = path.join(dir, LOCK);
  fs.mkdirSync(dir, { recursive: true });

  const body = (): string => JSON.stringify({ pid, at: now() } as LockBody);

  const tryCreate = (): boolean => {
    try {
      fs.writeFileSync(file, body(), { flag: 'wx' });
      return true;
    } catch (err) {
      if ((err as NodeJS.ErrnoException).code === 'EEXIST') return false;
      throw err;
    }
  };

  if (!tryCreate()) {
    let held: LockBody | undefined;
    try {
      held = JSON.parse(fs.readFileSync(file, 'utf8')) as LockBody;
    } catch {
      held = undefined; // unreadable or half-written: judged by file age below
    }
    let stale: boolean;
    if (held && typeof held.at === 'number' && typeof held.pid === 'number') {
      stale = now() - held.at > STALE_AFTER_MS && !isPidAlive(held.pid);
    } else {
      stale = now() - fs.statSync(file).mtimeMs > STALE_AFTER_MS;
    }
    if (!stale) return undefined;
    try {
      fs.unlinkSync(file);
    } catch (err) {
      if ((err as NodeJS.ErrnoException).code !== 'ENOENT') throw err;
    }
    if (!tryCreate()) return undefined; // someone else reclaimed it first
  }

  return {
    refresh() {
      fs.writeFileSync(file, body());
    },
    release() {
      try {
        const held = JSON.parse(fs.readFileSync(file, 'utf8')) as LockBody;
        if (held.pid !== pid) return;
        fs.unlinkSync(file);
      } catch {
        /* already gone */
      }
    },
  };
}
