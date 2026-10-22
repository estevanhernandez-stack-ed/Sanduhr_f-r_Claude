import * as fs from 'node:fs';
import * as path from 'node:path';
import type { ByAgent, DayRecord, DayTotals, ProjectDay } from '../types';
import { daysDir } from './paths';
import { writeFileAtomic } from './state';

const DAY_FILE = /^(\d{4}-\d{2}-\d{2})\.json$/;

/** Write `days/<date>.json` atomically (tmp in the same folder, then rename). */
export function writeDay(dir: string, record: DayRecord): void {
  writeFileAtomic(path.join(daysDir(dir), `${record.date}.json`), JSON.stringify(record, null, 2));
}

/** The v1 totals shape (0.1.0 and 0.2.0): Claude was the only agent. */
interface V1Totals {
  youMs: number;
  claudeMs: number;
  bothMs: number;
  totalMs: number;
  agentMs: number;
  linesYou: number;
  linesClaude: number;
}

const num = (v: unknown): number => (typeof v === 'number' && Number.isFinite(v) ? v : 0);

/**
 * v1 totals as v2: `claudeMs` is the agents-only time, and Claude's own wall time is that plus
 * the overlap with you (`claudeMs + bothMs`); `agentMs` was Claude's stream time.
 */
export function v1TotalsToV2<T extends Partial<V1Totals>>(t: T): Omit<T, 'claudeMs' | 'linesClaude'> & DayTotals {
  const { claudeMs, linesClaude, ...rest } = t;
  const agentsMs = num(claudeMs);
  const lines = num(linesClaude);
  const wallMs = agentsMs + num(t.bothMs);
  const streamMs = num(t.agentMs);
  const byAgent: ByAgent = wallMs > 0 || streamMs > 0 || lines > 0 ? { claude: { wallMs, streamMs, lines } } : {};
  return {
    ...rest,
    youMs: num(t.youMs),
    agentsMs,
    bothMs: num(t.bothMs),
    totalMs: num(t.totalMs),
    agentMs: streamMs,
    linesYou: num(t.linesYou),
    linesAgents: lines,
    byAgent,
  } as Omit<T, 'claudeMs' | 'linesClaude'> & DayTotals;
}

/**
 * A day file's JSON as a v2 record. v2 passes through; v1 (written by 0.1.0 and 0.2.0) maps
 * `claudeMs` to `agentsMs` and `byAgent.claude`, and `linesClaude` to `linesAgents`, so the
 * panel and API show history the same way as new days. Anything else is undefined.
 */
export function normalizeDay(raw: unknown): DayRecord | undefined {
  const r = raw as { v?: unknown; date?: unknown; totals?: unknown; projects?: unknown } | null;
  if (!r || typeof r.date !== 'string' || !r.totals || typeof r.totals !== 'object' || !Array.isArray(r.projects)) {
    return undefined;
  }
  if (r.v === 2) return r as DayRecord;
  if (r.v !== 1) return undefined;
  const v1 = r as unknown as Omit<DayRecord, 'v' | 'totals' | 'projects'> & {
    totals: V1Totals;
    projects: (Omit<ProjectDay, keyof DayTotals> & V1Totals)[];
  };
  return {
    ...v1,
    v: 2,
    totals: v1TotalsToV2(v1.totals),
    projects: v1.projects.map((p) => v1TotalsToV2(p) as ProjectDay),
  };
}

function parseDay(text: string): DayRecord | undefined {
  try {
    return normalizeDay(JSON.parse(text));
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
