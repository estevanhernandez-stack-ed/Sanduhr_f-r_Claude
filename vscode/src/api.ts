/**
 * The public API `activate` returns, for the 626 Labs extension's publisher and any other
 * consumer. Version 1 is the contract; consumers check `version` before calling.
 *
 * Since 0.3.0 day records are v2 (`agentsMs`, `linesAgents`, `byAgent`). Version 1 of the API
 * stays: every record it hands out also carries the v1 names `claudeMs` (= `agentsMs`) and
 * `linesClaude` (= `linesAgents`) on the totals and each project, so a consumer written for
 * 0.2.0 keeps working and `totalMs = youMs + claudeMs + bothMs` still holds. Those two now
 * include every agent; `byAgent.claude` is Claude's own share.
 */
import type { Event } from 'vscode';
import { normalizeRemote, projectKey } from './project';
import type { AliasStore, MaskState } from './store/aliases';
import { localDate } from './store/agentSpool';
import { listDays, readDay } from './store/days';
import type { DayRecord, DayTotals, ProjectDay } from './types';

export type { MaskState } from './store/aliases';

/** The v1 field names, kept on API records for consumers written before 0.3.0. */
export interface LegacyTotals {
  /** @deprecated Same as `agentsMs` (every agent, not only Claude). */
  claudeMs: number;
  /** @deprecated Same as `linesAgents` (every agent, not only Claude). */
  linesClaude: number;
}

export interface ApiDayRecord extends Omit<DayRecord, 'totals' | 'projects'> {
  totals: DayTotals & LegacyTotals;
  projects: (ProjectDay & LegacyTotals)[];
}

const legacy = <T extends DayTotals>(t: T): T & LegacyTotals => ({ ...t, claudeMs: t.agentsMs, linesClaude: t.linesAgents });

/** A day record with the v1 field names added beside the v2 ones. */
export function withLegacyFields(d: DayRecord): ApiDayRecord {
  return { ...d, totals: legacy(d.totals), projects: d.projects.map(legacy) };
}

export interface SanduhrTimeApi {
  version: 1;
  /** Today's merged day, or undefined before the first merge. */
  getToday(): Promise<ApiDayRecord | undefined>;
  /** Day records with `from <= date <= to` (local dates, inclusive), oldest first. */
  getDays(from: string, to: string): Promise<ApiDayRecord[]>;
  /** Fires after each day file is written. */
  onDayUpdated: Event<ApiDayRecord>;
  /** Live read of `aliases.json`; `readable: false` means fail closed. */
  maskState(): MaskState;
  projectKey(remoteOrRoot: string): string;
  normalizeRemote(url: string): string;
}

export interface ApiDeps {
  dir: string;
  store: Pick<AliasStore, 'maskState'>;
  onDayUpdated: Event<DayRecord>;
  now?: () => number;
}

export function createApi(deps: ApiDeps): SanduhrTimeApi {
  const now = deps.now ?? Date.now;
  const onDayUpdated: Event<ApiDayRecord> = (listener, thisArg, disposables) =>
    deps.onDayUpdated((d) => listener.call(thisArg, withLegacyFields(d)), undefined, disposables);
  return {
    version: 1,
    getToday: async () => {
      const d = readDay(deps.dir, localDate(now()));
      return d && withLegacyFields(d);
    },
    getDays: async (from, to) => listDays(deps.dir, from, to).map(withLegacyFields),
    onDayUpdated,
    maskState: () => deps.store.maskState(),
    projectKey,
    normalizeRemote,
  };
}

/** The API of a window that records nothing (remote windows): no data, and unreadable masks so publishers fail closed. */
export function createEmptyApi(): SanduhrTimeApi {
  return {
    version: 1,
    getToday: async () => undefined,
    getDays: async () => [],
    onDayUpdated: () => ({ dispose: () => undefined }),
    maskState: () => ({ readable: false, streamerMode: false, projects: {} }),
    projectKey,
    normalizeRemote,
  };
}
