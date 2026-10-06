/**
 * The merge runner. Every 5 minutes (and on demand) it takes `state/merge.lock`, runs one
 * bounded transcript pass, merges every date in the merge set (today, yesterday and each
 * date that gained events), writes the day files and notifies. Clock, timers, paths and
 * the reader are injected, so tests drive two runners on one data dir with fake time.
 * Window close does nothing here beyond `dispose()`; no pass and no merge at deactivate.
 */
import { readPass as realReadPass, type ReadPassOptions, type ReadPassResult } from './claude/reader';
import { dayWindow, merge, NO_PROJECT_KEY, NO_PROJECT_NAME, TAIL_MS } from './merge';
import { AliasStoreUnavailable, type AliasStore } from './store/aliases';
import { recordCaveats } from './store/caveats';
import { localDate, pruneClaudeSpool, readClaudeDay } from './store/claudeSpool';
import { writeDay } from './store/days';
import { stateDir } from './store/paths';
import { pruneSpool, readSpoolDay } from './store/spool';
import { acquireLock, type LockOptions } from './store/state';
import type { ClaudeEvent, DayRecord, Heartbeat, InstalledState } from './types';

export const TICK_MS = 5 * 60 * 1000;
export const REFRESH_MS = 30 * 1000;
export const FOLLOW_UP_MS = 30 * 1000;

export interface RunnerTimers {
  setInterval(fn: () => void, ms: number): unknown;
  clearInterval(handle: unknown): void;
  setTimeout(fn: () => void, ms: number): unknown;
  clearTimeout(handle: unknown): void;
}

const realTimers: RunnerTimers = {
  setInterval: (fn, ms) => {
    const h = setInterval(fn, ms);
    h.unref?.();
    return h;
  },
  clearInterval: (h) => clearInterval(h as ReturnType<typeof setInterval>),
  setTimeout: (fn, ms) => {
    const h = setTimeout(fn, ms);
    h.unref?.();
    return h;
  },
  clearTimeout: (h) => clearTimeout(h as ReturnType<typeof setTimeout>),
};

export interface RunnerDeps {
  /** Data directory. */
  dir: string;
  installed: InstalledState;
  /** Normalized machine label; only this machine's editor spool files are merged. */
  machine: string;
  store: AliasStore;
  /** Epoch milliseconds. */
  now: () => number;
  timers?: RunnerTimers;
  lockOptions?: LockOptions;
  /** Transcript reader; default is the real `readPass`. */
  readPass?: (opts: ReadPassOptions) => Promise<ReadPassResult>;
  /** Extra reader options (homes, byte budget). `dir` and `installed` are filled in. */
  readerOptions?: Partial<Omit<ReadPassOptions, 'dir' | 'installed'>>;
  /** `sanduhrTime.idleMinutes`, read fresh at each merge. */
  idleMinutes?: () => number;
  /** Real names the resolver has seen, by project key. */
  knownNames?: () => ReadonlyMap<string, string>;
  readerVersion: string;
  onDayUpdated?: (record: DayRecord) => void;
  /** Non-fatal problems (alias store down, a failed pass); logged by the host. */
  onError?: (err: unknown) => void;
}

export interface TickResult {
  /** False when a live lock held by another runner made this tick skip. */
  ran: boolean;
  /** Dates written, oldest first. */
  merged: string[];
}

function previousDate(date: string): string {
  const { start } = dayWindow(date);
  const d = new Date(start);
  return localDate(new Date(d.getFullYear(), d.getMonth(), d.getDate() - 1).getTime());
}

export class Runner {
  private readonly timers: RunnerTimers;
  private interval: unknown;
  private followUp: unknown;
  private running = false;
  private disposed = false;

  constructor(private readonly deps: RunnerDeps) {
    this.timers = deps.timers ?? realTimers;
  }

  /** Prune old spool files, schedule the 5-minute tick and kick off a first one in the background. */
  start(): void {
    const now = new Date(this.deps.now());
    try {
      pruneSpool(this.deps.dir, now);
      pruneClaudeSpool(this.deps.dir, now);
    } catch (err) {
      this.deps.onError?.(err);
    }
    this.interval = this.timers.setInterval(() => void this.tick(), TICK_MS);
    this.scheduleFollowUp(0);
  }

  /** Run one tick now (the `mergeNow` command). */
  mergeNow(): Promise<TickResult> {
    return this.tick();
  }

  dispose(): void {
    this.disposed = true;
    if (this.interval !== undefined) this.timers.clearInterval(this.interval);
    if (this.followUp !== undefined) this.timers.clearTimeout(this.followUp);
    this.interval = undefined;
    this.followUp = undefined;
  }

  private scheduleFollowUp(ms: number): void {
    if (this.disposed || this.followUp !== undefined) return;
    this.followUp = this.timers.setTimeout(() => {
      this.followUp = undefined;
      void this.tick();
    }, ms);
  }

  async tick(): Promise<TickResult> {
    if (this.running || this.disposed) return { ran: false, merged: [] };
    this.running = true;
    let lock: ReturnType<typeof acquireLock> = undefined;
    let refresh: unknown;
    try {
      lock = acquireLock(stateDir(this.deps.dir), this.deps.lockOptions);
      if (!lock) return { ran: false, merged: [] };
      const held = lock;
      refresh = this.timers.setInterval(() => {
        try {
          held.refresh();
        } catch (err) {
          this.deps.onError?.(err);
        }
      }, REFRESH_MS);
      return await this.runLocked();
    } catch (err) {
      this.deps.onError?.(err);
      return { ran: lock !== undefined, merged: [] };
    } finally {
      if (refresh !== undefined) this.timers.clearInterval(refresh);
      lock?.release();
      this.running = false;
    }
  }

  private async runLocked(): Promise<TickResult> {
    const { deps } = this;
    const read = deps.readPass ?? realReadPass;
    const pass = await read({ ...deps.readerOptions, dir: deps.dir, installed: deps.installed });
    const caveats = recordCaveats(stateDir(deps.dir), pass, deps.now());

    const today = localDate(deps.now());
    const set = new Set<string>([today, previousDate(today), ...pass.touchedDates]);
    const merged: string[] = [];
    for (const date of [...set].sort()) {
      try {
        const record = this.mergeDate(date, caveats[date]);
        writeDay(deps.dir, record);
        merged.push(date);
        deps.onDayUpdated?.(record);
      } catch (err) {
        deps.onError?.(err);
      }
    }
    if (pass.more) this.scheduleFollowUp(FOLLOW_UP_MS);
    return { ran: true, merged };
  }

  private mergeDate(date: string, caveat: { versions: string[]; unreadable: number } | undefined): DayRecord {
    const { deps } = this;
    const prev = previousDate(date);
    const tailFromSec = (dayWindow(date).start - TAIL_MS) / 1000;

    const heartbeats: Heartbeat[] = readSpoolDay(deps.dir, date, deps.machine);
    const claudeEvents: ClaudeEvent[] = readClaudeDay(deps.dir, date);
    const prevBeats = readSpoolDay(deps.dir, prev, deps.machine).filter((h) => h.time >= tailFromSec);
    const prevClaude = readClaudeDay(deps.dir, prev).filter((e) => e.time >= tailFromSec);

    // Every project key seen: aliases are assigned here, under the lock this tick already holds.
    const names = deps.knownNames?.() ?? new Map<string, string>();
    const wanted = new Map<string, string | undefined>();
    for (const key of [
      ...heartbeats.map((h) => h.project),
      ...claudeEvents.map((e) => e.project),
      ...prevBeats.map((h) => h.project),
      ...prevClaude.map((e) => e.project),
    ]) {
      wanted.set(key, key === NO_PROJECT_KEY ? NO_PROJECT_NAME : names.get(key));
    }

    let aliases: Record<string, { alias: string | null; masked: boolean }> = {};
    try {
      aliases = deps.store.ensureHeld(wanted);
    } catch (err) {
      // Fail closed upstream (publishers read maskState); the day still gets written, alias null.
      if (!(err instanceof AliasStoreUnavailable)) throw err;
      deps.onError?.(err);
    }

    let storeNames: Record<string, string> = {};
    try {
      const f = deps.store.read();
      storeNames = Object.fromEntries(Object.entries(f.projects).map(([k, e]) => [k, e.realName]));
    } catch {
      /* reported above */
    }
    const realNames: Record<string, string> = {};
    for (const key of wanted.keys()) {
      const name = names.get(key) ?? (storeNames[key] || undefined);
      if (name) realNames[key] = name;
    }

    return merge({
      date,
      machine: deps.machine,
      heartbeats,
      claudeEvents,
      previousDayTail: { heartbeats: prevBeats, claudeEvents: prevClaude },
      aliases,
      realNames,
      settings: { idleMinutes: deps.idleMinutes?.() },
      generatedAt: new Date(deps.now()).toISOString(),
      caveats: {
        unreadableTranscriptLines: caveat?.unreadable ?? 0,
        readerVersion: deps.readerVersion,
        claudeCodeVersions: caveat?.versions ?? [],
      },
    });
  }
}
