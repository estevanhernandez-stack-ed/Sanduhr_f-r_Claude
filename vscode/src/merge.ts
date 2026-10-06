/**
 * The merger: pure, no I/O. Turns one local day of editor heartbeats and Claude events
 * into a DayRecord. Everything runs in epoch milliseconds; spool `time` fields are unix
 * seconds and are converted once, on the way in.
 *
 * Interval rules (the usage spec states them as the contract):
 * - Yours, per project: non-reload heartbeat times plus, for each prompt at t, TWO events:
 *   t - 2 min and t (the span's start and end). Consecutive events join when the gap is
 *   <= idleMinutes; an interval with a single event lasts 2 minutes. A prompt on its own
 *   therefore yields exactly [t - 2 min, t], and a prompt 1 minute after an edit joins it.
 * - Claude's, per project: `activity` and `edit` events (an edit is also activity), gap
 *   5 minutes, lone event 2 minutes. Agent time sums each stream's own intervals.
 */
import { clip, intersect, joinEvents, subtract, totalLength, union } from './intervals';
import type {
  ClaudeEvent,
  DayCaveats,
  DayRecord,
  DayTotals,
  Heartbeat,
  Interval,
  LanguageTime,
  ProjectDay,
} from './types';

export const MIN_MS = 60_000;
export const PROMPT_SPAN_MS = 2 * MIN_MS;
export const LONE_MS = 2 * MIN_MS;
export const CLAUDE_GAP_MS = 5 * MIN_MS;
export const DEFAULT_IDLE_MINUTES = 15;
/** A heartbeat and a Claude edit on the same entity within this many ms is Claude's write. */
export const BACKSTOP_MS = 2000;
/** How much of the previous day's end is read so intervals crossing midnight join. */
export const TAIL_MS = 60 * MIN_MS;
export const MAX_LANGUAGES = 10;
export const NO_PROJECT_KEY = 'none';
export const NO_PROJECT_NAME = '(no project)';
export const UNKNOWN_PROJECT_NAME = '(unknown project)';

export interface AliasEntry {
  alias: string | null;
  masked: boolean;
}

export interface MergeInput {
  /** Local date, YYYY-MM-DD. */
  date: string;
  machine: string;
  /** The date's heartbeats (more are harmless: only this day's window is counted). */
  heartbeats: readonly Heartbeat[];
  claudeEvents: readonly ClaudeEvent[];
  /** The previous day's events, at least its last hour. */
  previousDayTail?: { heartbeats: readonly Heartbeat[]; claudeEvents: readonly ClaudeEvent[] };
  /** `{ [projectKey]: { alias, masked} }`; a missing key means alias null, masked false. */
  aliases: Readonly<Record<string, AliasEntry>>;
  /** `{ [projectKey]: realName }`, from the resolver. */
  realNames?: Readonly<Record<string, string>>;
  settings: { idleMinutes?: number };
  /** ISO timestamp stamped into the record. */
  generatedAt: string;
  caveats: {
    unreadableTranscriptLines: number;
    readerVersion: string;
    claudeCodeVersions?: string[];
  };
}

/** Local midnight to the next local midnight for `date`. Calendar math, so DST days are 23 or 25 hours. */
export function dayWindow(date: string): { start: number; end: number } {
  const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(date);
  if (!m) throw new Error(`Invalid date: ${date}`);
  const y = Number(m[1]);
  const mo = Number(m[2]) - 1;
  const d = Number(m[3]);
  return { start: new Date(y, mo, d).getTime(), end: new Date(y, mo, d + 1).getTime() };
}

const toMs = (seconds: number): number => Math.round(seconds * 1000);

interface Pool {
  yourTimes: number[];
  claudeTimes: number[];
  /** Per Claude stream: activity + edit times. */
  streams: Map<string, number[]>;
  /** Non-reload heartbeats, for language stretches. */
  beats: { t: number; language: string }[];
  linesYou: number;
  linesClaude: number;
}

function newPool(): Pool {
  return { yourTimes: [], claudeTimes: [], streams: new Map(), beats: [], linesYou: 0, linesClaude: 0 };
}

/** Language stretches from heartbeats: [previous, this] goes to the heartbeat that extended it; a lone one gets 2 minutes. */
function languageStretches(
  beats: readonly { t: number; language: string }[],
  idleMs: number,
): { iv: Interval; language: string }[] {
  const sorted = [...beats].sort((a, b) => a.t - b.t);
  const out: { iv: Interval; language: string }[] = [];
  let groupStart = 0;
  for (let i = 0; i < sorted.length; i++) {
    const end = i === sorted.length - 1 || sorted[i + 1].t - sorted[i].t > idleMs;
    if (!end) continue;
    if (i === groupStart) {
      out.push({ iv: { start: sorted[i].t, end: sorted[i].t + LONE_MS }, language: sorted[i].language });
    } else {
      for (let k = groupStart + 1; k <= i; k++) {
        if (sorted[k].t > sorted[k - 1].t) {
          out.push({ iv: { start: sorted[k - 1].t, end: sorted[k].t }, language: sorted[k].language });
        }
      }
    }
    groupStart = i + 1;
  }
  return out;
}

function totalsFrom(u: Interval[], c: Interval[]): Pick<DayTotals, 'youMs' | 'claudeMs' | 'bothMs' | 'totalMs'> {
  const youMs = totalLength(subtract(u, c));
  const claudeMs = totalLength(subtract(c, u));
  const bothMs = totalLength(intersect(u, c));
  return { youMs, claudeMs, bothMs, totalMs: youMs + claudeMs + bothMs };
}

export function merge(input: MergeInput): DayRecord {
  const idleMs = (input.settings.idleMinutes ?? DEFAULT_IDLE_MINUTES) * MIN_MS;
  const { start, end } = dayWindow(input.date);
  const readFrom = start - TAIL_MS;

  // Pool this day and the previous day's tail; events at or past the next midnight belong to the next day.
  const allBeats = [...(input.previousDayTail?.heartbeats ?? []), ...input.heartbeats];
  const allClaude = [...(input.previousDayTail?.claudeEvents ?? []), ...input.claudeEvents];

  const seenIds = new Set<string>();
  const claude: { ev: ClaudeEvent; t: number }[] = [];
  for (const ev of allClaude) {
    if (seenIds.has(ev.id)) continue;
    seenIds.add(ev.id);
    const t = toMs(ev.time);
    if (t >= readFrom && t < end) claude.push({ ev, t });
  }
  const beats: { hb: Heartbeat; t: number }[] = [];
  for (const hb of allBeats) {
    const t = toMs(hb.time);
    if (t >= readFrom && t < end) beats.push({ hb, t });
  }

  // Backstop: an edit heartbeat landing on the same file within 2 s of a Claude edit is Claude's write.
  const claudeEditTimes = new Map<string, number[]>();
  for (const { ev, t } of claude) {
    if (ev.kind !== 'edit' || !ev.entity) continue;
    const list = claudeEditTimes.get(ev.entity);
    if (list) list.push(t);
    else claudeEditTimes.set(ev.entity, [t]);
  }
  const isBackstopped = (hb: Heartbeat, t: number): boolean =>
    hb.kind === 'edit' && (claudeEditTimes.get(hb.entity)?.some((ct) => Math.abs(ct - t) <= BACKSTOP_MS) ?? false);

  const pools = new Map<string, Pool>();
  const poolFor = (key: string): Pool => {
    let p = pools.get(key);
    if (!p) pools.set(key, (p = newPool()));
    return p;
  };

  for (const { hb, t } of beats) {
    if (hb.reload) continue; // not activity, adds no lines
    const p = poolFor(hb.project);
    p.yourTimes.push(t);
    p.beats.push({ t, language: hb.language });
    if (t >= start && !isBackstopped(hb, t)) p.linesYou += hb.human_line_changes;
  }

  for (const { ev, t } of claude) {
    const p = poolFor(ev.project);
    if (ev.kind === 'prompt') {
      p.yourTimes.push(t - PROMPT_SPAN_MS, t);
      continue;
    }
    p.claudeTimes.push(t);
    const s = p.streams.get(ev.stream);
    if (s) s.push(t);
    else p.streams.set(ev.stream, [t]);
    if (ev.kind === 'edit' && t >= start) p.linesClaude += (ev.linesAdded ?? 0) + (ev.linesRemoved ?? 0);
  }

  const projects: ProjectDay[] = [];
  const uByProject: Interval[][] = [];
  const cByProject: Interval[][] = [];

  for (const [key, p] of pools) {
    const u = clip(joinEvents(p.yourTimes, idleMs, LONE_MS), start, end);
    const c = clip(joinEvents(p.claudeTimes, CLAUDE_GAP_MS, LONE_MS), start, end);
    let agentMs = 0;
    for (const times of p.streams.values()) {
      agentMs += totalLength(clip(joinEvents(times, CLAUDE_GAP_MS, LONE_MS), start, end));
    }

    const byLang = new Map<string, number>();
    for (const { iv, language } of languageStretches(p.beats, idleMs)) {
      const ms = totalLength(clip([iv], start, end));
      if (ms > 0) byLang.set(language, (byLang.get(language) ?? 0) + ms);
    }
    const languages: LanguageTime[] = [...byLang]
      .map(([id, ms]) => ({ id, ms }))
      .sort((a, b) => b.ms - a.ms || (a.id < b.id ? -1 : 1))
      .slice(0, MAX_LANGUAGES);

    const t = totalsFrom(u, c);
    if (t.totalMs === 0 && p.linesYou === 0 && p.linesClaude === 0) continue; // only tail activity, nothing today
    uByProject.push(u);
    cByProject.push(c);

    const alias = input.aliases[key];
    projects.push({
      key,
      realName:
        key === NO_PROJECT_KEY ? NO_PROJECT_NAME : (input.realNames?.[key] ?? UNKNOWN_PROJECT_NAME),
      alias: alias?.alias ?? null,
      masked: alias?.masked ?? false,
      ...t,
      agentMs,
      linesYou: p.linesYou,
      linesClaude: p.linesClaude,
      languages,
    });
  }

  projects.sort((a, b) => b.totalMs - a.totalMs || (a.key < b.key ? -1 : 1));

  // Day totals pool every project's intervals, so a minute is counted once at day level.
  const uAll = union(uByProject.flat());
  const cAll = union(cByProject.flat());
  const totals: DayTotals = {
    ...totalsFrom(uAll, cAll),
    agentMs: projects.reduce((s, p) => s + p.agentMs, 0),
    linesYou: projects.reduce((s, p) => s + p.linesYou, 0),
    linesClaude: projects.reduce((s, p) => s + p.linesClaude, 0),
  };

  const caveats: DayCaveats = {
    unreadableTranscriptLines: input.caveats.unreadableTranscriptLines,
    readerVersion: input.caveats.readerVersion,
    claudeCodeVersions: input.caveats.claudeCodeVersions ?? [],
  };

  return { v: 1, date: input.date, machine: input.machine, generatedAt: input.generatedAt, totals, projects, caveats };
}
