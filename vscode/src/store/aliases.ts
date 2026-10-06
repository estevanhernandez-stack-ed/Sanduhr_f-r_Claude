/**
 * The alias store: `aliases.json`, the one place a project's neutral alias, mask flag and
 * the streamer switch live. Every write takes the merge lock (so concurrent windows never
 * lose each other's assignments) and goes through tmp + rename.
 *
 * Fail closed: a missing file is a fresh store only when this activation created
 * `installed.json`. Any other missing or unparseable file throws `AliasStoreUnavailable`,
 * and `maskState()` reports `readable: false`, so a publisher sends aliases or nothing
 * rather than silently unmasking every project.
 */
import * as fs from 'node:fs';
import * as path from 'node:path';
import { nextAlias } from './alias-pool';
import { stateDir } from './paths';
import { acquireLock, writeFileAtomic, type LockOptions, type MergeLock } from './state';

export const ALIASES_FILE = 'aliases.json';
/** How long a UI write retries for the merge lock before giving up. */
export const LOCK_WAIT_MS = 2000;
const LOCK_POLL_MS = 50;

export interface AliasEntryRecord {
  realName: string;
  alias: string;
  masked: boolean;
  /** ISO timestamp of the last change. */
  setAt: string;
}

export interface AliasesFile {
  v: 1;
  streamerMode: boolean;
  projects: Record<string, AliasEntryRecord>;
}

export interface MaskState {
  /** False when the store is missing or unparseable: publish aliases only, or nothing. */
  readable: boolean;
  streamerMode: boolean;
  projects: Record<string, { alias: string; masked: boolean }>;
}

export class AliasStoreUnavailable extends Error {
  constructor(reason: string) {
    super(`alias store unavailable: ${reason}`);
    this.name = 'AliasStoreUnavailable';
  }
}

export class AliasStoreBusy extends Error {
  constructor() {
    super('alias store busy: the merge lock stayed held');
    this.name = 'AliasStoreBusy';
  }
}

export interface AliasStoreOptions {
  /** True only when this activation created `state/installed.json`. */
  isNew: boolean;
  now?: () => Date;
  lockOptions?: LockOptions;
  /** Awaitable delay between lock attempts; injectable for tests. */
  sleep?: (ms: number) => Promise<void>;
  /** Lock wait budget; default 2 s. */
  lockWaitMs?: number;
}

const emptyFile = (): AliasesFile => ({ v: 1, streamerMode: false, projects: {} });

export class AliasStore {
  private readonly file: string;
  private readonly lockDir: string;
  /** Becomes false once the file has been read or written, so a later deletion is an error. */
  private fresh: boolean;
  private readonly now: () => Date;
  private readonly sleep: (ms: number) => Promise<void>;
  private readonly lockWaitMs: number;

  constructor(
    dir: string,
    private readonly opts: AliasStoreOptions,
  ) {
    this.file = path.join(dir, ALIASES_FILE);
    this.lockDir = stateDir(dir);
    this.fresh = opts.isNew;
    this.now = opts.now ?? (() => new Date());
    this.sleep = opts.sleep ?? ((ms) => new Promise((res) => setTimeout(res, ms)));
    this.lockWaitMs = opts.lockWaitMs ?? LOCK_WAIT_MS;
  }

  /** Read the store. Throws `AliasStoreUnavailable` unless it is a legitimately fresh store. */
  read(): AliasesFile {
    let text: string;
    try {
      text = fs.readFileSync(this.file, 'utf8');
    } catch (err) {
      if ((err as NodeJS.ErrnoException).code === 'ENOENT') {
        if (this.fresh) return emptyFile();
        throw new AliasStoreUnavailable('aliases.json is missing');
      }
      throw new AliasStoreUnavailable(`aliases.json is unreadable (${(err as NodeJS.ErrnoException).code ?? 'error'})`);
    }
    let parsed: unknown;
    try {
      parsed = JSON.parse(text);
    } catch {
      throw new AliasStoreUnavailable('aliases.json is not valid JSON');
    }
    const f = parsed as Partial<AliasesFile> | null;
    if (!f || typeof f !== 'object' || f.v !== 1 || typeof f.projects !== 'object' || f.projects === null) {
      throw new AliasStoreUnavailable('aliases.json has an unexpected shape');
    }
    this.fresh = false;
    return { v: 1, streamerMode: f.streamerMode === true, projects: f.projects as Record<string, AliasEntryRecord> };
  }

  /** Live mask state; `readable: false` when the store throws `AliasStoreUnavailable`. */
  maskState(): MaskState {
    try {
      const f = this.read();
      const projects: MaskState['projects'] = {};
      for (const [k, e] of Object.entries(f.projects)) projects[k] = { alias: e.alias, masked: e.masked === true };
      return { readable: true, streamerMode: f.streamerMode, projects };
    } catch (err) {
      if (err instanceof AliasStoreUnavailable) return { readable: false, streamerMode: false, projects: {} };
      throw err;
    }
  }

  private persist(f: AliasesFile): void {
    writeFileAtomic(this.file, JSON.stringify(f, null, 2));
    this.fresh = false;
  }

  /** Take the merge lock, retrying until the wait budget runs out. */
  private async lock(): Promise<MergeLock> {
    const deadline = Date.now() + this.lockWaitMs;
    for (;;) {
      const held = acquireLock(this.lockDir, this.opts.lockOptions);
      if (held) return held;
      if (Date.now() >= deadline) throw new AliasStoreBusy();
      await this.sleep(LOCK_POLL_MS);
    }
  }

  private async withLock<T>(fn: () => T): Promise<T> {
    const held = await this.lock();
    try {
      return fn();
    } finally {
      held.release();
    }
  }

  // Core mutations. Callers must hold the merge lock.

  private assign(f: AliasesFile, key: string, realName: string | undefined): { alias: string; changed: boolean } {
    const existing = f.projects[key];
    if (existing) {
      if (realName && realName !== existing.realName) {
        existing.realName = realName;
        existing.setAt = this.now().toISOString();
        return { alias: existing.alias, changed: true };
      }
      return { alias: existing.alias, changed: false };
    }
    const used = new Set(Object.values(f.projects).map((e) => e.alias));
    const alias = nextAlias(used);
    f.projects[key] = { realName: realName ?? '', alias, masked: false, setAt: this.now().toISOString() };
    return { alias, changed: true };
  }

  /**
   * Assign aliases for several projects in one write. The caller MUST already hold the
   * merge lock (the runner does); this never takes the lock itself, so it cannot deadlock.
   * Returns the live `{alias, masked}` map for every key given.
   */
  ensureHeld(entries: ReadonlyMap<string, string | undefined>): Record<string, { alias: string; masked: boolean }> {
    const f = this.read();
    let changed = false;
    const out: Record<string, { alias: string; masked: boolean }> = {};
    for (const [key, name] of [...entries].sort((a, b) => (a[0] < b[0] ? -1 : 1))) {
      const r = this.assign(f, key, name);
      changed ||= r.changed;
      out[key] = { alias: r.alias, masked: f.projects[key].masked === true };
    }
    if (changed) this.persist(f);
    return out;
  }

  /** Alias for a project, assigned lazily under the merge lock. */
  async aliasFor(key: string, realName?: string): Promise<string> {
    // Fast path: a known key with nothing new to record needs no lock and no write.
    const known = this.read().projects[key];
    if (known && (!realName || realName === known.realName)) return known.alias;
    return this.withLock(() => {
      const f = this.read();
      const r = this.assign(f, key, realName);
      if (r.changed) this.persist(f);
      return r.alias;
    });
  }

  /** Give a project a different alias from the pool. */
  async reroll(key: string, realName?: string): Promise<string> {
    return this.withLock(() => {
      const f = this.read();
      this.assign(f, key, realName);
      const current = f.projects[key];
      // The current alias counts as used, so the new one always differs.
      const used = new Set(Object.values(f.projects).map((e) => e.alias));
      current.alias = nextAlias(used, used.size + 1);
      current.setAt = this.now().toISOString();
      this.persist(f);
      return current.alias;
    });
  }

  async setMasked(key: string, masked: boolean, realName?: string): Promise<void> {
    await this.withLock(() => {
      const f = this.read();
      this.assign(f, key, realName);
      f.projects[key].masked = masked;
      f.projects[key].setAt = this.now().toISOString();
      this.persist(f);
    });
  }

  async setStreamerMode(on: boolean): Promise<void> {
    await this.withLock(() => {
      const f = this.read();
      f.streamerMode = on;
      this.persist(f);
    });
  }
}
