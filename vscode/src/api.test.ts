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
  v: 2,
  date,
  machine: 'machine-1',
  generatedAt: `${date}T18:00:00.000Z`,
  totals: { youMs: totalMs, agentsMs: 0, bothMs: 0, totalMs, agentMs: 0, linesYou: 0, linesAgents: 0, byAgent: {} },
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

  it('reads a v1 day file (0.1.0, 0.2.0) as v2: claudeMs becomes agentsMs and byAgent.claude', () => {
    const MIN = 60_000;
    const v1Totals = { youMs: 10 * MIN, claudeMs: 20 * MIN, bothMs: 5 * MIN, totalMs: 35 * MIN, agentMs: 30 * MIN, linesYou: 4, linesClaude: 90 };
    const v1 = {
      v: 1,
      date: '2026-06-05',
      machine: 'machine-1',
      generatedAt: '2026-06-05T18:00:00.000Z',
      totals: v1Totals,
      projects: [
        { key: 'a'.repeat(16), realName: 'alpha', alias: null, masked: false, languages: [], ...v1Totals },
        { key: 'b'.repeat(16), realName: 'beta', alias: null, masked: false, languages: [], youMs: MIN, claudeMs: 0, bothMs: 0, totalMs: MIN, agentMs: 0, linesYou: 0, linesClaude: 0 },
      ],
      caveats: { unreadableTranscriptLines: 0, readerVersion: '0.2.0', claudeCodeVersions: [] },
    };
    fs.mkdirSync(path.join(dir, 'days'), { recursive: true });
    fs.writeFileSync(path.join(dir, 'days', '2026-06-05.json'), JSON.stringify(v1));
    const d = readDay(dir, '2026-06-05')!;
    expect(d.v).toBe(2);
    expect(d.totals).toEqual({
      youMs: 10 * MIN, agentsMs: 20 * MIN, bothMs: 5 * MIN, totalMs: 35 * MIN, agentMs: 30 * MIN, linesYou: 4, linesAgents: 90,
      byAgent: { claude: { wallMs: 25 * MIN, streamMs: 30 * MIN, lines: 90 } },
    });
    expect(d.projects[0].byAgent).toEqual({ claude: { wallMs: 25 * MIN, streamMs: 30 * MIN, lines: 90 } });
    expect(d.projects[1].byAgent).toEqual({}); // no Claude that day in this project
    expect('claudeMs' in d.totals).toBe(false);
    expect(d.caveats.readerVersion).toBe('0.2.0');
    expect(readDay(dir, '2026-06-05') && listDays(dir, '2026-06-05', '2026-06-05')).toHaveLength(1);
  });

  it('skips a day file of an unknown version', () => {
    fs.mkdirSync(path.join(dir, 'days'), { recursive: true });
    fs.writeFileSync(path.join(dir, 'days', '2026-06-06.json'), JSON.stringify({ ...day('2026-06-06', 5), v: 3 }));
    expect(readDay(dir, '2026-06-06')).toBeUndefined();
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

  it('keeps API v1 consumers working: every record also carries claudeMs and linesClaude', async () => {
    const emitter = new vscode.EventEmitter<DayRecord>();
    const now = () => new Date(2026, 5, 10, 12, 0, 0).getTime();
    const api = createApi({ dir, store: { maskState: () => ({ readable: true, streamerMode: false, projects: {} }) }, onDayUpdated: emitter.event as never, now });
    const rec: DayRecord = {
      ...day('2026-06-10', 30),
      totals: { youMs: 10, agentsMs: 15, bothMs: 5, totalMs: 30, agentMs: 25, linesYou: 1, linesAgents: 7, byAgent: { codex: { wallMs: 20, streamMs: 25, lines: 7 } } },
    };
    writeDay(dir, rec);
    const seen: unknown[] = [];
    api.onDayUpdated((d) => seen.push(d));
    emitter.fire(rec);
    const today = (await api.getToday())!;
    for (const r of [today, (await api.getDays('2026-06-10', '2026-06-10'))[0], seen[0] as typeof today]) {
      expect(r.totals.claudeMs).toBe(15);
      expect(r.totals.linesClaude).toBe(7);
      expect(r.totals.agentsMs).toBe(15);
      expect(r.totals.totalMs).toBe(r.totals.youMs + r.totals.claudeMs + r.totals.bothMs);
    }
    // the file on disk stays clean v2
    expect(JSON.parse(fs.readFileSync(path.join(dir, 'days', '2026-06-10.json'), 'utf8')).totals.claudeMs).toBeUndefined();
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
