/**
 * The agent spool: `agents/<local date>.jsonl`, one AgentEvent per line, written by every
 * transcript reader (Claude, Codex). The 0.1 and 0.2 Claude spool, `claude/<local date>.jsonl`,
 * is still read: its lines have no `agent` field and become `agent: 'claude'`, so an existing
 * install loses nothing and its transcript offsets keep working.
 */
import * as fs from 'node:fs';
import * as path from 'node:path';
import { AGENT_NAMES, type AgentEvent, type AgentName } from '../types';
import { agentsDir, claudeDir } from './paths';

const DAY_FILE = /^(\d{4}-\d{2}-\d{2})\.jsonl$/;
const KEEP_DAYS = 90;

/** Local calendar date (YYYY-MM-DD) of an epoch-millisecond instant. */
export function localDate(ms: number): string {
  const d = new Date(ms);
  const p = (n: number): string => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}`;
}

const fileFor = (dir: string, date: string): string => path.join(agentsDir(dir), `${date}.jsonl`);
const legacyFileFor = (dir: string, date: string): string => path.join(claudeDir(dir), `${date}.jsonl`);

export function endsWithNewline(file: string): boolean {
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
 * Append events to `agents/<local date>.jsonl`, one JSON object per line, grouped by the
 * local date of each event's time. A file left without a trailing newline by a torn write
 * is terminated first, so a new line never fuses onto a broken one.
 */
export function appendAgentEvents(dir: string, events: readonly AgentEvent[]): void {
  if (events.length === 0) return;
  const byDate = new Map<string, string[]>();
  for (const ev of events) {
    const date = localDate(ev.time * 1000);
    let lines = byDate.get(date);
    if (!lines) byDate.set(date, (lines = []));
    lines.push(JSON.stringify(ev));
  }
  fs.mkdirSync(agentsDir(dir), { recursive: true });
  for (const [date, lines] of byDate) {
    const file = fileFor(dir, date);
    const prefix = endsWithNewline(file) ? '' : '\n';
    fs.appendFileSync(file, prefix + lines.join('\n') + '\n');
  }
}

const isAgent = (a: unknown): a is AgentName => (AGENT_NAMES as readonly unknown[]).includes(a);

function readLines(file: string): string[] {
  try {
    return fs.readFileSync(file, 'utf8').split('\n');
  } catch (err) {
    if ((err as NodeJS.ErrnoException).code === 'ENOENT') return [];
    throw err;
  }
}

/**
 * One day's agent events: the legacy Claude file first, then the agent file, de-duplicated
 * by `id` (first occurrence wins). A line without `agent` is Claude's; a line naming an agent
 * this version does not know, or any other bad line, is skipped.
 */
export function readAgentDay(dir: string, date: string): AgentEvent[] {
  const seen = new Set<string>();
  const out: AgentEvent[] = [];
  for (const line of [...readLines(legacyFileFor(dir, date)), ...readLines(fileFor(dir, date))]) {
    if (!line.trim()) continue;
    let raw: Partial<AgentEvent> & { v?: number };
    try {
      raw = JSON.parse(line) as typeof raw;
    } catch {
      continue;
    }
    if (!raw || typeof raw.id !== 'string' || seen.has(raw.id)) continue;
    const agent = raw.agent ?? 'claude';
    if (!isAgent(agent)) continue;
    seen.add(raw.id);
    out.push({ ...raw, v: 2, agent } as AgentEvent);
  }
  return out;
}

/** Delete agent and legacy Claude spool files dated more than 90 days before `now`. Returns the deleted dates. */
export function pruneAgentSpool(dir: string, now: Date = new Date(), keepDays = KEEP_DAYS): string[] {
  const cutoff = localDate(now.getTime() - keepDays * 86_400_000);
  const removed = new Set<string>();
  for (const folder of [claudeDir(dir), agentsDir(dir)]) {
    let names: string[];
    try {
      names = fs.readdirSync(folder);
    } catch {
      continue;
    }
    for (const name of names) {
      const m = DAY_FILE.exec(name);
      if (!m || m[1] >= cutoff) continue;
      try {
        fs.unlinkSync(path.join(folder, name));
        removed.add(m[1]);
      } catch {
        /* gone already */
      }
    }
  }
  return [...removed].sort();
}
