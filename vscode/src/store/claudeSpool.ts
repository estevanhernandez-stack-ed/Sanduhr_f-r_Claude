import * as fs from 'node:fs';
import * as path from 'node:path';
import type { ClaudeEvent } from '../types';
import { claudeDir } from './paths';

const DAY_FILE = /^(\d{4}-\d{2}-\d{2})\.jsonl$/;
const KEEP_DAYS = 90;

/** Local calendar date (YYYY-MM-DD) of an epoch-millisecond instant. */
export function localDate(ms: number): string {
  const d = new Date(ms);
  const p = (n: number): string => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}`;
}

const fileFor = (dir: string, date: string): string => path.join(claudeDir(dir), `${date}.jsonl`);

function endsWithNewline(file: string): boolean {
  let fd: number;
  try {
    fd = fs.openSync(file, 'r');
  } catch {
    return true; // no file: nothing to terminate
  }
  try {
    const size = fs.fstatSync(fd).size;
    if (size === 0) return true;
    const b = Buffer.alloc(1);
    fs.readSync(fd, b, 0, 1, size - 1);
    return b[0] === 0x0a;
  } finally {
    fs.closeSync(fd);
  }
}

/**
 * Append events to `claude/<local date>.jsonl`, one JSON object per line, grouped by the
 * local date of each event's time. A file left without a trailing newline by a torn write
 * is terminated first, so a new line never fuses onto a broken one.
 */
export function appendClaudeEvents(dir: string, events: readonly ClaudeEvent[]): void {
  if (events.length === 0) return;
  const byDate = new Map<string, string[]>();
  for (const ev of events) {
    const date = localDate(ev.time * 1000);
    let lines = byDate.get(date);
    if (!lines) byDate.set(date, (lines = []));
    lines.push(JSON.stringify(ev));
  }
  fs.mkdirSync(claudeDir(dir), { recursive: true });
  for (const [date, lines] of byDate) {
    const file = fileFor(dir, date);
    const prefix = endsWithNewline(file) ? '' : '\n';
    fs.appendFileSync(file, prefix + lines.join('\n') + '\n');
  }
}

/** One day's Claude events, de-duplicated by `id` (first occurrence wins). Bad lines are skipped. */
export function readClaudeDay(dir: string, date: string): ClaudeEvent[] {
  let text: string;
  try {
    text = fs.readFileSync(fileFor(dir, date), 'utf8');
  } catch (err) {
    if ((err as NodeJS.ErrnoException).code === 'ENOENT') return [];
    throw err;
  }
  const seen = new Set<string>();
  const out: ClaudeEvent[] = [];
  for (const line of text.split('\n')) {
    if (!line.trim()) continue;
    let ev: ClaudeEvent;
    try {
      ev = JSON.parse(line) as ClaudeEvent;
    } catch {
      continue;
    }
    if (!ev || typeof ev.id !== 'string' || seen.has(ev.id)) continue;
    seen.add(ev.id);
    out.push(ev);
  }
  return out;
}

/** Delete Claude spool files dated more than 90 days before `now`. Returns the deleted dates. */
export function pruneClaudeSpool(dir: string, now: Date = new Date(), keepDays = KEEP_DAYS): string[] {
  const cutoff = localDate(now.getTime() - keepDays * 86_400_000);
  const removed: string[] = [];
  let names: string[];
  try {
    names = fs.readdirSync(claudeDir(dir));
  } catch {
    return removed;
  }
  for (const name of names) {
    const m = DAY_FILE.exec(name);
    if (!m || m[1] >= cutoff) continue;
    try {
      fs.unlinkSync(path.join(claudeDir(dir), name));
      removed.push(m[1]);
    } catch {
      /* gone already */
    }
  }
  return removed;
}
