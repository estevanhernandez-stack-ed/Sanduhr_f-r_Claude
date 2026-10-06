/**
 * Pure view models for the status bar and the Time panel. No `vscode`, no I/O: everything
 * the UI shows is computed here from day records and the live mask state, so the masking
 * and formatting rules are testable without a window.
 */
import type { MaskState } from '../store/aliases';
import type { DayRecord, DayTotals, ProjectDay } from '../types';

export const MASKED_FALLBACK = 'Masked project';
export const FOOTER = 'Claude is a trademark of Anthropic. Sanduhr Time is not affiliated with or endorsed by Anthropic.';
export const EMPTY_MESSAGE = 'Tracking started. Your first numbers appear after a few minutes of work.';
export const REMOTE_MESSAGE = 'Local workspaces only. Sanduhr Time records nothing in a remote window.';
export const UNREADABLE_MESSAGE = 'The alias store is unreadable, so every project name is hidden until it recovers.';

const MINUTE = 60_000;

/** `3h 12m`, `45m`, `<1m`. Minutes are padded once hours show (`2h 05m`). Zero is `0m`. */
export function formatDuration(ms: number): string {
  if (!Number.isFinite(ms) || ms <= 0) return '0m';
  if (ms < MINUTE) return '<1m';
  const totalMin = Math.floor(ms / MINUTE);
  const h = Math.floor(totalMin / 60);
  const m = totalMin % 60;
  if (h === 0) return `${m}m`;
  return `${h}h ${String(m).padStart(2, '0')}m`;
}

export interface Split {
  you: string;
  claude: string;
  both: string;
  total: string;
}

export interface Fractions {
  you: number;
  claude: number;
  both: number;
}

const split = (t: Pick<DayTotals, 'youMs' | 'claudeMs' | 'bothMs' | 'totalMs'>): Split => ({
  you: formatDuration(t.youMs),
  claude: formatDuration(t.claudeMs),
  both: formatDuration(t.bothMs),
  total: formatDuration(t.totalMs),
});

/** Shares of the three parts. They sum to exactly 1, or are all 0 when there is no time. */
export function fractions(t: Pick<DayTotals, 'youMs' | 'claudeMs' | 'bothMs'>): Fractions {
  const you = Math.max(0, t.youMs);
  const claude = Math.max(0, t.claudeMs);
  const both = Math.max(0, t.bothMs);
  const sum = you + claude + both;
  if (sum <= 0) return { you: 0, claude: 0, both: 0 };
  const f = { you: you / sum, claude: claude / sum, both: both / sum };
  // Absorb floating-point residue into the largest part so the sum is exactly 1.
  const keys = ['you', 'claude', 'both'] as const;
  const largest = keys.reduce((a, b) => (f[b] > f[a] ? b : a));
  const others = keys.filter((k) => k !== largest);
  f[largest] = 1 - f[others[0]] - f[others[1]];
  return f;
}

export interface StatusBarModel {
  text: string;
  tooltip: string;
}

/** The status bar item. `today` is undefined before the first merge. */
export function buildStatusBar(today: DayRecord | undefined, opts: { remote?: boolean } = {}): StatusBarModel {
  if (opts.remote) return { text: '$(watch) off', tooltip: 'Local workspaces only' };
  if (!today) return { text: '$(watch) 0m', tooltip: 'Sanduhr Time: nothing tracked yet today' };
  const t = today.totals;
  return {
    text: `$(watch) ${formatDuration(t.totalMs)}`,
    tooltip: `You ${formatDuration(t.youMs)} · Claude ${formatDuration(t.claudeMs)} · Both ${formatDuration(t.bothMs)}`,
  };
}

export interface ProjectRowModel {
  key: string;
  /** Alias or real name, per the masking rules. Safe to show. */
  name: string;
  /** True when the name shown is an alias. */
  masked: boolean;
  /** The project's own mask flag (the toggle state), distinct from streamer mode forcing aliases. */
  maskFlag: boolean;
  split: Split;
  fractions: Fractions;
  agent: string;
  linesYou: number;
  linesClaude: number;
  languages: { id: string; time: string }[];
  totalMs: number;
}

export interface DayStripEntry {
  date: string;
  label: string;
  split: Split;
  fractions: Fractions;
  /** This day's total relative to the busiest day in the strip, 0..1. */
  height: number;
  isToday: boolean;
}

export interface PanelModel {
  remote: boolean;
  empty: boolean;
  emptyMessage: string;
  today: string;
  headline: Split;
  headlineFractions: Fractions;
  strip: DayStripEntry[];
  projects: ProjectRowModel[];
  streamerMode: boolean;
  /** False when the alias store failed to read; names are hidden. */
  maskReadable: boolean;
  notice: string | null;
  caveats: string | null;
  compare: string | null;
  footer: string;
}

export interface CompareInput {
  /** WakaTime's total for today, in seconds. */
  wakatimeSeconds: number;
}

const WEEKDAYS = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];

function parseDate(date: string): Date {
  const [y, m, d] = date.split('-').map(Number);
  return new Date(y, m - 1, d);
}

function shiftDate(date: string, days: number): string {
  const d = parseDate(date);
  d.setDate(d.getDate() + days);
  const p = (n: number): string => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}`;
}

function dayLabel(date: string): string {
  return `${WEEKDAYS[parseDate(date).getDay()]} ${date.slice(5)}`;
}

/**
 * The name to show for a project. An alias when the project is masked, streamer mode is on,
 * or the mask store is unreadable (fail closed); the real name otherwise. A missing alias in
 * any masked case is "Masked project", never the real name.
 */
export function displayName(p: ProjectDay, mask: MaskState): { name: string; masked: boolean; maskFlag: boolean } {
  const live = mask.projects[p.key];
  const maskFlag = live ? live.masked : p.masked === true;
  const masked = !mask.readable || mask.streamerMode || maskFlag;
  if (!masked) return { name: p.realName || MASKED_FALLBACK, masked: false, maskFlag };
  const alias = (live?.alias || p.alias || '').trim();
  return { name: alias || MASKED_FALLBACK, masked: true, maskFlag };
}

function topLanguages(p: ProjectDay): { id: string; time: string }[] {
  return [...p.languages]
    .sort((a, b) => b.ms - a.ms || (a.id < b.id ? -1 : 1))
    .slice(0, 3)
    .map((l) => ({ id: l.id, time: formatDuration(l.ms) }));
}

export function buildPanelModel(
  days: DayRecord[],
  mask: MaskState,
  today: string,
  compare?: CompareInput,
  opts: { remote?: boolean } = {},
): PanelModel {
  const byDate = new Map(days.map((d) => [d.date, d]));
  const todayRec = byDate.get(today);
  const zero: DayTotals = { youMs: 0, claudeMs: 0, bothMs: 0, totalMs: 0, agentMs: 0, linesYou: 0, linesClaude: 0 };
  const t = todayRec?.totals ?? zero;

  const dates = Array.from({ length: 7 }, (_, i) => shiftDate(today, i - 6));
  const maxTotal = Math.max(0, ...dates.map((d) => byDate.get(d)?.totals.totalMs ?? 0));
  const strip: DayStripEntry[] = dates.map((date) => {
    const dt = byDate.get(date)?.totals ?? zero;
    return {
      date,
      label: dayLabel(date),
      split: split(dt),
      fractions: fractions(dt),
      height: maxTotal > 0 ? dt.totalMs / maxTotal : 0,
      isToday: date === today,
    };
  });

  const projects: ProjectRowModel[] = (todayRec?.projects ?? [])
    .map((p): ProjectRowModel => {
      const n = displayName(p, mask);
      return {
        key: p.key,
        name: n.name,
        masked: n.masked,
        maskFlag: n.maskFlag,
        split: split(p),
        fractions: fractions(p),
        agent: formatDuration(p.agentMs),
        linesYou: p.linesYou,
        linesClaude: p.linesClaude,
        languages: topLanguages(p),
        totalMs: p.totalMs,
      };
    })
    .sort((a, b) => b.totalMs - a.totalMs || (a.key < b.key ? -1 : 1));

  const unreadable = todayRec?.caveats.unreadableTranscriptLines ?? 0;
  const caveats =
    unreadable > 0
      ? `${unreadable} transcript line${unreadable === 1 ? '' : 's'} could not be read, so Claude time may be slightly low.`
      : null;

  let compareLine: string | null = null;
  if (compare && Number.isFinite(compare.wakatimeSeconds)) {
    const wakaMs = compare.wakatimeSeconds * 1000;
    const oursMs = t.youMs + t.bothMs;
    const diff = oursMs - wakaMs;
    const sign = diff > 0 ? '+' : diff < 0 ? '-' : '';
    compareLine = `WakaTime ${formatDuration(wakaMs)} · Sanduhr editor time ${formatDuration(oursMs)} · difference ${sign}${formatDuration(Math.abs(diff))}`;
  }

  const remote = opts.remote === true;
  return {
    remote,
    empty: !remote && (!todayRec || (t.totalMs === 0 && todayRec.projects.length === 0)),
    emptyMessage: EMPTY_MESSAGE,
    today,
    headline: split(t),
    headlineFractions: fractions(t),
    strip,
    projects,
    streamerMode: mask.streamerMode,
    maskReadable: mask.readable,
    notice: remote ? REMOTE_MESSAGE : mask.readable ? null : UNREADABLE_MESSAGE,
    caveats,
    compare: compareLine,
    footer: FOOTER,
  };
}
