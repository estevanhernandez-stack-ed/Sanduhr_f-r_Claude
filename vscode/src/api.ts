/**
 * The public API `activate` returns, for the 626 Labs extension's publisher and any other
 * consumer. Version 1 is the contract; consumers check `version` before calling.
 */
import type { Event } from 'vscode';
import { normalizeRemote, projectKey } from './project';
import type { AliasStore, MaskState } from './store/aliases';
import { localDate } from './store/claudeSpool';
import { listDays, readDay } from './store/days';
import type { DayRecord } from './types';

export type { MaskState } from './store/aliases';

export interface SanduhrTimeApi {
  version: 1;
  /** Today's merged day, or undefined before the first merge. */
  getToday(): Promise<DayRecord | undefined>;
  /** Day records with `from <= date <= to` (local dates, inclusive), oldest first. */
  getDays(from: string, to: string): Promise<DayRecord[]>;
  /** Fires after each day file is written. */
  onDayUpdated: Event<DayRecord>;
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
  return {
    version: 1,
    getToday: async () => readDay(deps.dir, localDate(now())),
    getDays: async (from, to) => listDays(deps.dir, from, to),
    onDayUpdated: deps.onDayUpdated,
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
