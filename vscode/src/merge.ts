/**
 * The merger: pure, no I/O. Turns one local day of editor heartbeats and agent events
 * (Claude, Codex) into a DayRecord. Everything runs in epoch milliseconds; spool `time`
 * fields are unix seconds and are converted once, on the way in.
 *
 * Interval rules (the usage spec states them as the contract):
 * - Yours, per project: non-reload heartbeat times plus, for each prompt at t, TWO events:
 *   the span's start and t. The start is t - 2 min, or earlier when the prompt's own stream
 *   had agent activity within idleMinutes before it: then the span reaches back to that
 *   activity, because reading the agent's answer and dictating the next prompt is your time.
 *   Consecutive events join when the gap is <= idleMinutes; an interval with a single event
 *   lasts 2 minutes. A prompt with no recent activity in its stream therefore yields exactly
 *   [t - 2 min, t], and a prompt 1 minute after an edit joins it. A prompt to any agent counts.
 * - Each agent's, per project: its `activity` and `edit` events (an edit is also activity),
 *   gap 5 minutes, lone event 2 minutes. The agents bucket is the union of every agent's
 *   intervals, so a minute two agents share counts once. Stream time sums each stream's own
 *   intervals.
 */
import { clip, intersect, joinEvents, subtract, totalLength, union } from './intervals';
import type {
  AgentEvent,
  AgentName,
  ByAgent,
  DayCaveats,
  DayRecord,
  DayTotals,
  Heartbeat,
  Interval,
  LanguageTime,
  ProjectDay,
} from './types';
import { AGENT_NAMES } from './types';

export const MIN_MS = 60_000;
export const PROMPT_SPAN_MS = 2 * MIN_MS;
export const LONE_MS = 2 * MIN_MS;
export const AGENT_GAP_MS = 5 * MIN_MS;
export const DEFAULT_IDLE_MINUTES = 15;
/** A heartbeat and an agent edit on the same entity within this many ms is the agent's write. */
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
  agentEvents: readonly AgentEvent[];
  /** The previous day's events, at least its last hour. */
  previousDayTail?: { heartbeats: readonly Heartbeat[]; agentEvents: readonly AgentEvent[] };
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

interface AgentPool {
  /** Activity + edit times, all streams. */
  times: number[];
  /** Per stream: activity + edit times. */
  streams: Map<string, number[]>;
  lines: number;
}

interface Pool {
  yourTimes: number[];
  agents: Map<AgentName, AgentPool>;
  /** Non-reload heartbeats, for language stretches. */
  beats: { t: number; language: string }[];
  linesYou: number;
}

function newPool(): Pool {
  return { yourTimes: [], agents: new Map(), beats: [], linesYou: 0 };
}

function agentPool(p: Pool, agent: AgentName): AgentPool {
  let a = p.agents.get(agent);
  if (!a) p.agents.set(agent, (a = { times: [], streams: new Map(), lines: 0 }));
  return a;
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

function totalsFrom(u: Interval[], c: Interval[]): Pick<DayTotals, 'youMs' | 'agentsMs' | 'bothMs' | 'totalMs'> {
  const youMs = totalLength(subtract(u, c));
  const agentsMs = totalLength(subtract(c, u));
  const bothMs = totalLength(intersect(u, c));
  return { youMs, agentsMs, bothMs, totalMs: youMs + agentsMs + bothMs };
}

/** Agents in display order, keeping only those present. */
const orderedAgents = (present: Iterable<AgentName>): AgentName[] => {
  const set = new Set(present);
  return AGENT_NAMES.filter((a) => set.has(a));
};

export function merge(input: MergeInput): DayRecord {
  const idleMs = (input.settings.idleMinutes ?? DEFAULT_IDLE_MINUTES) * MIN_MS;
  const { start, end } = dayWindow(input.date);
  const readFrom = start - TAIL_MS;

  // Pool this day and the previous day's tail; events at or past the next midnight belong to the next day.
  const allBeats = [...(input.previousDayTail?.heartbeats ?? []), ...input.heartbeats];
  const allAgent = [...(input.previousDayTail?.agentEvents ?? []), ...input.agentEvents];

  const seenIds = new Set<string>();
  const agentEvents: { ev: AgentEvent; t: number }[] = [];
  for (const ev of allAgent) {
    if (seenIds.has(ev.id)) continue;
    seenIds.add(ev.id);
    const t = toMs(ev.time);
    if (t >= readFrom && t < end) agentEvents.push({ ev, t });
  }
  const beats: { hb: Heartbeat; t: number }[] = [];
  for (const hb of allBeats) {
    const t = toMs(hb.time);
    if (t >= readFrom && t < end) beats.push({ hb, t });
  }

  // Backstop: an edit heartbeat landing on the same file within 2 s of an agent's edit is the agent's write.
  const agentEditTimes = new Map<string, number[]>();
  for (const { ev, t } of agentEvents) {
    if (ev.kind !== 'edit' || !ev.entity) continue;
    const list = agentEditTimes.get(ev.entity);
    if (list) list.push(t);
    else agentEditTimes.set(ev.entity, [t]);
  }
  const isBackstopped = (hb: Heartbeat, t: number): boolean =>
    hb.kind === 'edit' && (agentEditTimes.get(hb.entity)?.some((ct) => Math.abs(ct - t) <= BACKSTOP_MS) ?? false);

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

  // Read-and-dictate gap: a prompt's span reaches back to its own stream's last agent activity
  // when that is within idleMinutes. Same stream only, so a long subagent run (its own stream)
  // never shortens the gap; a longer gap means the builder stepped away, and keeps the 2 minutes.
  const spanStart = new Map<string, number>();
  const lastActivity = new Map<string, number>();
  const ordered = [...agentEvents].sort(
    (a, b) => a.t - b.t || (a.ev.kind === 'prompt' ? -1 : 0) - (b.ev.kind === 'prompt' ? -1 : 0),
  );
  for (const { ev, t } of ordered) {
    if (ev.kind !== 'prompt') {
      lastActivity.set(ev.stream, t);
      continue;
    }
    const last = lastActivity.get(ev.stream);
    const read = last !== undefined && t - last <= idleMs ? last : t;
    spanStart.set(ev.id, Math.min(read, t - PROMPT_SPAN_MS));
  }

  for (const { ev, t } of agentEvents) {
    const p = poolFor(ev.project);
    if (ev.kind === 'prompt') {
      p.yourTimes.push(spanStart.get(ev.id) ?? t - PROMPT_SPAN_MS, t);
      continue;
    }
    const a = agentPool(p, ev.agent ?? 'claude');
    a.times.push(t);
    const s = a.streams.get(ev.stream);
    if (s) s.push(t);
    else a.streams.set(ev.stream, [t]);
    if (ev.kind === 'edit' && t >= start) a.lines += (ev.linesAdded ?? 0) + (ev.linesRemoved ?? 0);
  }

  const projects: ProjectDay[] = [];
  const uByProject: Interval[][] = [];
  /** Per agent, every project's clipped intervals: the day's wall time per agent. */
  const dayAgentIntervals = new Map<AgentName, Interval[]>();
  const cByProject: Interval[][] = [];

  for (const [key, p] of pools) {
    const u = clip(joinEvents(p.yourTimes, idleMs, LONE_MS), start, end);
    const perAgent: { agent: AgentName; iv: Interval[]; streamMs: number; lines: number }[] = [];
    for (const agent of orderedAgents(p.agents.keys())) {
      const a = p.agents.get(agent)!;
      const iv = clip(joinEvents(a.times, AGENT_GAP_MS, LONE_MS), start, end);
      let streamMs = 0;
      for (const times of a.streams.values()) {
        streamMs += totalLength(clip(joinEvents(times, AGENT_GAP_MS, LONE_MS), start, end));
      }
      perAgent.push({ agent, iv, streamMs, lines: a.lines });
    }
    const c = union(perAgent.flatMap((a) => a.iv));

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
    const linesAgents = perAgent.reduce((s, a) => s + a.lines, 0);
    if (t.totalMs === 0 && p.linesYou === 0 && linesAgents === 0) continue; // only tail activity, nothing today
    uByProject.push(u);
    cByProject.push(c);

    const byAgent: ByAgent = {};
    for (const a of perAgent) {
      const wallMs = totalLength(a.iv);
      if (wallMs === 0 && a.streamMs === 0 && a.lines === 0) continue; // tail only
      byAgent[a.agent] = { wallMs, streamMs: a.streamMs, lines: a.lines };
      const list = dayAgentIntervals.get(a.agent);
      if (list) list.push(...a.iv);
      else dayAgentIntervals.set(a.agent, [...a.iv]);
    }

    const alias = input.aliases[key];
    projects.push({
      key,
      realName:
        key === NO_PROJECT_KEY ? NO_PROJECT_NAME : (input.realNames?.[key] ?? UNKNOWN_PROJECT_NAME),
      alias: alias?.alias ?? null,
      masked: alias?.masked ?? false,
      ...t,
      agentMs: perAgent.reduce((s, a) => s + a.streamMs, 0),
      linesYou: p.linesYou,
      linesAgents,
      byAgent,
      languages,
    });
  }

  projects.sort((a, b) => b.totalMs - a.totalMs || (a.key < b.key ? -1 : 1));

  // Day totals pool every project's intervals, so a minute is counted once at day level.
  const uAll = union(uByProject.flat());
  const cAll = union(cByProject.flat());
  const dayByAgent: ByAgent = {};
  for (const agent of orderedAgents(dayAgentIntervals.keys())) {
    let streamMs = 0;
    let lines = 0;
    for (const p of projects) {
      streamMs += p.byAgent[agent]?.streamMs ?? 0;
      lines += p.byAgent[agent]?.lines ?? 0;
    }
    dayByAgent[agent] = { wallMs: totalLength(union(dayAgentIntervals.get(agent)!)), streamMs, lines };
  }
  const totals: DayTotals = {
    ...totalsFrom(uAll, cAll),
    agentMs: projects.reduce((s, p) => s + p.agentMs, 0),
    linesYou: projects.reduce((s, p) => s + p.linesYou, 0),
    linesAgents: projects.reduce((s, p) => s + p.linesAgents, 0),
    byAgent: dayByAgent,
  };

  const caveats: DayCaveats = {
    unreadableTranscriptLines: input.caveats.unreadableTranscriptLines,
    readerVersion: input.caveats.readerVersion,
    claudeCodeVersions: input.caveats.claudeCodeVersions ?? [],
  };

  return { v: 2, date: input.date, machine: input.machine, generatedAt: input.generatedAt, totals, projects, caveats };
}
