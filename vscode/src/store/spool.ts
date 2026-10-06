import { createHash } from 'node:crypto';
import * as fs from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';
import type { Heartbeat } from '../types';
import { endsWithNewline, localDate } from './claudeSpool';
import { spoolDir } from './paths';

const KEEP_DAYS = 90;
const FLUSH_MS = 5000;
/** Lines held when the disk keeps failing; the oldest are dropped beyond this. */
const MAX_BUFFER = 20_000;
const SPOOL_FILE = /^(\d{4}-\d{2}-\d{2})\.([a-z0-9-]+)\.([0-9a-f]{8})\.jsonl$/;

/**
 * Normalized machine label: lowercase, anything outside `[a-z0-9-]` becomes `-`.
 * Used in spool file names (and by the publisher), so it never contains a dot.
 */
export function machineLabel(hostname: string = os.hostname()): string {
  return hostname.toLowerCase().replace(/[^a-z0-9-]/g, '-');
}

/** First 8 hex chars of sha256(`vscode.env.sessionId`): one id per VS Code window. */
export function windowIdFor(sessionId: string): string {
  return createHash('sha256').update(sessionId).digest('hex').slice(0, 8);
}

export interface SpoolOptions {
  machine: string;
  windowId: string;
  /** Flush period; default 5 seconds. */
  flushMs?: number;
}

/**
 * Buffered append-only writer for this window's heartbeats:
 * `spool/<local date of the heartbeat>.<machine>.<windowId>.jsonl`.
 * Appends sit in memory and flush every 5 seconds and on `dispose()`, so the event
 * loop never waits on disk.
 */
export class Spool {
  private buffer: Heartbeat[] = [];
  private timer: ReturnType<typeof setInterval> | undefined;
  private readonly flushMs: number;

  constructor(
    private readonly dir: string,
    private readonly opts: SpoolOptions,
  ) {
    this.flushMs = opts.flushMs ?? FLUSH_MS;
  }

  append(hb: Heartbeat): void {
    this.buffer.push(hb);
    if (this.buffer.length > MAX_BUFFER) this.buffer.splice(0, this.buffer.length - MAX_BUFFER);
    if (!this.timer) {
      this.timer = setInterval(() => this.flush(), this.flushMs);
      this.timer.unref?.();
    }
  }

  /** Write the buffer out. On a disk error the lines stay buffered for the next tick. */
  flush(): void {
    if (this.buffer.length === 0) return;
    const pending = this.buffer;
    this.buffer = [];
    const byDate = new Map<string, string[]>();
    for (const hb of pending) {
      const date = localDate(hb.time * 1000);
      let lines = byDate.get(date);
      if (!lines) byDate.set(date, (lines = []));
      lines.push(JSON.stringify(hb));
    }
    try {
      fs.mkdirSync(spoolDir(this.dir), { recursive: true });
      for (const [date, lines] of byDate) {
        const file = path.join(spoolDir(this.dir), `${date}.${this.opts.machine}.${this.opts.windowId}.jsonl`);
        const prefix = endsWithNewline(file) ? '' : '\n';
        fs.appendFileSync(file, prefix + lines.join('\n') + '\n');
        byDate.delete(date);
      }
    } catch {
      // Keep whatever did not land, ahead of anything appended meanwhile.
      const unwritten = pending.filter((hb) => byDate.has(localDate(hb.time * 1000)));
      this.buffer = unwritten.concat(this.buffer);
    }
  }

  dispose(): void {
    if (this.timer) clearInterval(this.timer);
    this.timer = undefined;
    this.flush();
  }
}

/**
 * Every heartbeat on `date` from THIS machine's spool files, across all windows.
 * Files from other machines (a synced folder) are never read. Bad lines are skipped.
 */
export function readSpoolDay(dir: string, date: string, machine: string): Heartbeat[] {
  let names: string[];
  try {
    names = fs.readdirSync(spoolDir(dir));
  } catch {
    return [];
  }
  const out: Heartbeat[] = [];
  for (const name of names.sort()) {
    const m = SPOOL_FILE.exec(name);
    if (!m || m[1] !== date || m[2] !== machine) continue;
    let text: string;
    try {
      text = fs.readFileSync(path.join(spoolDir(dir), name), 'utf8');
    } catch {
      continue;
    }
    for (const line of text.split('\n')) {
      if (!line.trim()) continue;
      try {
        const hb = JSON.parse(line) as Heartbeat;
        if (hb && typeof hb.time === 'number' && typeof hb.entity === 'string') out.push(hb);
      } catch {
        /* torn line */
      }
    }
  }
  return out;
}

/** Delete spool files (any machine) dated more than 90 days before `now`. Returns the names removed. */
export function pruneSpool(dir: string, now: Date = new Date(), keepDays = KEEP_DAYS): string[] {
  const cutoff = localDate(now.getTime() - keepDays * 86_400_000);
  const removed: string[] = [];
  let names: string[];
  try {
    names = fs.readdirSync(spoolDir(dir));
  } catch {
    return removed;
  }
  for (const name of names) {
    const m = SPOOL_FILE.exec(name);
    if (!m || m[1] >= cutoff) continue;
    try {
      fs.unlinkSync(path.join(spoolDir(dir), name));
      removed.push(name);
    } catch {
      /* gone already */
    }
  }
  return removed;
}
