import * as fs from 'node:fs';
import * as path from 'node:path';
import type { DayRecord } from '../types';
import { daysDir } from './paths';
import { writeFileAtomic } from './state';

const DAY_FILE = /^(\d{4}-\d{2}-\d{2})\.json$/;

/** Write `days/<date>.json` atomically (tmp in the same folder, then rename). */
export function writeDay(dir: string, record: DayRecord): void {
  writeFileAtomic(path.join(daysDir(dir), `${record.date}.json`), JSON.stringify(record, null, 2));
}

function parseDay(text: string): DayRecord | undefined {
  try {
    const r = JSON.parse(text) as DayRecord;
    return r && r.v === 1 && typeof r.date === 'string' && r.totals && Array.isArray(r.projects) ? r : undefined;
  } catch {
    return undefined;
  }
}

/** One day's record, or undefined when it is missing or unreadable. */
export function readDay(dir: string, date: string): DayRecord | undefined {
  try {
    return parseDay(fs.readFileSync(path.join(daysDir(dir), `${date}.json`), 'utf8'));
  } catch {
    return undefined;
  }
}

/** Day records with `from <= date <= to` (inclusive), oldest first. Unreadable files are skipped. */
export function listDays(dir: string, from: string, to: string): DayRecord[] {
  let names: string[];
  try {
    names = fs.readdirSync(daysDir(dir));
  } catch {
    return [];
  }
  const out: DayRecord[] = [];
  for (const name of names.sort()) {
    const m = DAY_FILE.exec(name);
    if (!m || m[1] < from || m[1] > to) continue;
    const r = readDay(dir, m[1]);
    if (r) out.push(r);
  }
  return out;
}
