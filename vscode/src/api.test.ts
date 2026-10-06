import * as fs from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import * as vscode from 'vscode';
import { createApi, createEmptyApi } from './api';
import { normalizeRemote, projectKey } from './project';
import { Runner } from './runner';
import { AliasStore } from './store/aliases';
import { listDays, readDay, writeDay } from './store/days';
import { stateDir } from './store/paths';
import { ensureInstalled } from './store/state';
import type { DayRecord } from './types';

let dir: string;
beforeEach(() => {
  dir = fs.mkdtempSync(path.join(os.tmpdir(), 'sanduhr-api-'));
});
afterEach(() => {
  fs.rmSync(dir, { recursive: true, force: true });
});

const day = (date: string, totalMs = 0): DayRecord => ({
  v: 1,
  date,
  machine: 'machine-1',
  generatedAt: `${date}T18:00:00.000Z`,
  totals: { youMs: totalMs, claudeMs: 0, bothMs: 0, totalMs, agentMs: 0, linesYou: 0, linesClaude: 0 },
  projects: [],
  caveats: { unreadableTranscriptLines: 0, readerVersion: '0.1.0', claudeCodeVersions: [] },
});

describe('days store', () => {
  it('round-trips a day atomically and lists a range oldest first', () => {
    writeDay(dir, day('2026-06-10', 5));
    writeDay(dir, day('2026-06-08', 3));
    writeDay(dir, day('2026-06-12', 7));
    expect(readDay(dir, '2026-06-10')?.totals.totalMs).toBe(5);
    expect(readDay(dir, '2026-06-11')).toBeUndefined();
    expect(listDays(dir, '2026-06-08', '2026-06-10').map((d) => d.date)).toEqual(['2026-06-08', '2026-06-10']);
    expect(fs.readdirSync(path.join(dir, 'days')).every((n) => /^\d{4}-\d{2}-\d{2}\.json$/.test(n))).toBe(true);
  });

  it('skips unreadable files instead of failing the range', () => {
    writeDay(dir, day('2026-06-10', 5));
    fs.writeFileSync(path.join(dir, 'days', '2026-06-11.json'), '{ torn');
    expect(listDays(dir, '2026-06-01', '2026-06-30').map((d) => d.date)).toEqual(['2026-06-10']);
  });
});

describe('public API', () => {
  it('returns written days, fires onDayUpdated, and exposes live mask state and the key helpers', async () => {
    const emitter = new vscode.EventEmitter<DayRecord>();
    const { installed, isNew } = ensureInstalled(stateDir(dir));
    const store = new AliasStore(dir, { isNew });
    const api = createApi({
      dir,
      store,
      onDayUpdated: emitter.event as never,
      now: () => new Date(2026, 5, 10, 12, 0, 0).getTime(),
    });
    expect(api.version).toBe(1);
    expect(await api.getToday()).toBeUndefined();

    const seen: DayRecord[] = [];
    api.onDayUpdated((d) => seen.push(d));
    const runner = new Runner({
      dir,
      installed,
      machine: 'machine-1',
      store,
      now: () => new Date(2026, 5, 10, 12, 0, 0).getTime(),
      readPass: async () => ({ events: [], more: false, unreadableByDate: {}, touchedDates: [], versionsByDate: {} }),
      readerVersion: '0.1.0',
      onDayUpdated: (d) => emitter.fire(d),
    });
    await runner.mergeNow();

    expect(seen.map((d) => d.date)).toEqual(['2026-06-09', '2026-06-10']);
    expect((await api.getToday())?.date).toBe('2026-06-10');
    expect((await api.getDays('2026-06-09', '2026-06-10')).map((d) => d.date)).toEqual(['2026-06-09', '2026-06-10']);
    expect(await api.getDays('2026-01-01', '2026-01-02')).toEqual([]);

    await store.setStreamerMode(true);
    expect(api.maskState()).toEqual({ readable: true, streamerMode: true, projects: {} });
    expect(api.projectKey('git@example.com:Acme/Widget.git')).toBe(projectKey('git@example.com:Acme/Widget.git'));
    expect(api.normalizeRemote('git@example.com:Acme/Widget.git')).toBe('https://example.com/acme/widget');
    expect(api.normalizeRemote).toBe(normalizeRemote);
  });

  it('maskState is unreadable when the store is gone on an existing install', () => {
    ensureInstalled(stateDir(dir));
    const api = createApi({ dir, store: new AliasStore(dir, { isNew: false }), onDayUpdated: new vscode.EventEmitter<DayRecord>().event as never });
    expect(api.maskState().readable).toBe(false);
  });

  it('the empty API (remote windows) has no data and fails closed on masks', async () => {
    const api = createEmptyApi();
    expect(api.version).toBe(1);
    expect(await api.getToday()).toBeUndefined();
    expect(await api.getDays('2026-01-01', '2026-12-31')).toEqual([]);
    expect(api.maskState().readable).toBe(false);
    expect(() => api.onDayUpdated(() => undefined).dispose()).not.toThrow();
  });
});
