import * as fs from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import type { ReadPassResult } from './claude/reader';
import { FOLLOW_UP_MS, REFRESH_MS, Runner, TICK_MS, type RunnerDeps, type RunnerTimers } from './runner';
import { AliasStore } from './store/aliases';
import { appendClaudeEvents } from './store/claudeSpool';
import { listDays, readDay } from './store/days';
import { stateDir } from './store/paths';
import { Spool } from './store/spool';
import { ensureInstalled } from './store/state';
import type { ClaudeEvent, DayRecord, Heartbeat } from './types';

const MACHINE = 'machine-1';
const NOW = new Date(2026, 5, 10, 14, 0, 0).getTime(); // local 14:00
const sec = (h: number, m = 0, day = 10): number => new Date(2026, 5, day, h, m, 0).getTime() / 1000;

let dir: string;
beforeEach(() => {
  dir = fs.mkdtempSync(path.join(os.tmpdir(), 'sanduhr-runner-'));
});
afterEach(() => {
  fs.rmSync(dir, { recursive: true, force: true });
});

class FakeTimers implements RunnerTimers {
  intervals = new Map<number, { fn: () => void; ms: number }>();
  timeouts = new Map<number, { fn: () => void; ms: number }>();
  private seq = 0;
  setInterval(fn: () => void, ms: number): unknown {
    this.intervals.set(++this.seq, { fn, ms });
    return this.seq;
  }
  clearInterval(h: unknown): void {
    this.intervals.delete(h as number);
  }
  setTimeout(fn: () => void, ms: number): unknown {
    this.timeouts.set(++this.seq, { fn, ms });
    return this.seq;
  }
  clearTimeout(h: unknown): void {
    this.timeouts.delete(h as number);
  }
  timeoutMs(): number[] {
    return [...this.timeouts.values()].map((t) => t.ms);
  }
  fireTimeouts(): void {
    const due = [...this.timeouts.entries()];
    this.timeouts.clear();
    for (const [, t] of due) t.fn();
  }
}

const emptyPass = (over: Partial<ReadPassResult> = {}): ReadPassResult => ({
  events: [],
  more: false,
  unreadableByDate: {},
  touchedDates: [],
  versionsByDate: {},
  ...over,
});

const hb = (project: string, time: number, over: Partial<Heartbeat> = {}): Heartbeat => ({
  v: 1,
  time,
  entity: 'e'.repeat(24),
  type: 'file',
  category: 'coding',
  kind: 'edit',
  project,
  branch: null,
  language: 'typescript',
  lines: 10,
  human_line_changes: 1,
  is_write: false,
  reload: false,
  ...over,
});

const cev = (id: string, project: string, time: number, kind: ClaudeEvent['kind'] = 'activity'): ClaudeEvent => ({
  v: 1,
  id,
  time,
  project,
  stream: 's1',
  kind,
});

function seedSpool(heartbeats: Heartbeat[], windowId = 'aaaaaaaa'): void {
  const s = new Spool(dir, { machine: MACHINE, windowId });
  for (const h of heartbeats) s.append(h);
  s.dispose();
}

interface Rig {
  runner: Runner;
  store: AliasStore;
  timers: FakeTimers;
  updated: DayRecord[];
  errors: unknown[];
}

function rig(pid: number, over: Partial<RunnerDeps> = {}): Rig {
  const { installed, isNew } = ensureInstalled(stateDir(dir));
  const store = new AliasStore(dir, { isNew, lockOptions: { pid } });
  const timers = new FakeTimers();
  const updated: DayRecord[] = [];
  const errors: unknown[] = [];
  const runner = new Runner({
    dir,
    installed,
    machine: MACHINE,
    store,
    now: () => NOW,
    timers,
    lockOptions: { pid },
    readPass: async () => emptyPass(),
    readerVersion: '0.1.0',
    onDayUpdated: (d) => updated.push(d),
    onError: (e) => errors.push(e),
    ...over,
  });
  return { runner, store, timers, updated, errors };
}

describe('tick', () => {
  it('merges today and yesterday, assigns aliases, writes day files and fires onDayUpdated', async () => {
    seedSpool([hb('proj-a', sec(13, 0)), hb('proj-a', sec(13, 5))]);
    appendClaudeEvents(dir, [cev('c1', 'proj-b', sec(13, 2))]);
    const r = rig(10, { knownNames: () => new Map([['proj-a', 'widget']]) });
    const res = await r.runner.tick();
    expect(res).toEqual({ ran: true, merged: ['2026-06-09', '2026-06-10'] });
    const today = readDay(dir, '2026-06-10')!;
    expect(today.machine).toBe(MACHINE);
    expect(today.projects.map((p) => p.key).sort()).toEqual(['proj-a', 'proj-b']);
    const a = today.projects.find((p) => p.key === 'proj-a')!;
    expect(a.realName).toBe('widget');
    expect(a.alias).toMatch(/^Project /);
    expect(a.youMs).toBeGreaterThan(0);
    // aliases landed in the store, so the day matches the live mask state
    expect(r.store.maskState().projects['proj-a'].alias).toBe(a.alias);
    expect(r.updated.map((d) => d.date)).toEqual(['2026-06-09', '2026-06-10']);
    expect(fs.existsSync(path.join(dir, 'state', 'merge.lock'))).toBe(false);
  });

  it('joins the previous day tail across midnight', async () => {
    seedSpool([hb('p', sec(23, 50, 9)), hb('p', sec(0, 5, 10))]);
    const r = rig(10);
    await r.runner.tick();
    const today = readDay(dir, '2026-06-10')!;
    // 23:50 -> 00:05 is one stretch; only the 5 minutes after midnight count today
    expect(today.projects[0].youMs).toBe(5 * 60_000);
  });

  it('includes a touched old date in the merge set', async () => {
    appendClaudeEvents(dir, [cev('old1', 'proj-a', sec(9, 0, 1)), cev('old2', 'proj-a', sec(9, 3, 1))]);
    const r = rig(10, { readPass: async () => emptyPass({ touchedDates: ['2026-06-01'] }) });
    const res = await r.runner.tick();
    expect(res.merged).toEqual(['2026-06-01', '2026-06-09', '2026-06-10']);
    expect(readDay(dir, '2026-06-01')!.projects[0].claudeMs).toBeGreaterThan(0);
  });

  it('feeds the reader versions and unreadable counts into caveats, and remembers them next tick', async () => {
    let n = 0;
    const r = rig(10, {
      readPass: async () =>
        n++ === 0
          ? emptyPass({ versionsByDate: { '2026-06-10': ['2.1.5'] }, unreadableByDate: { '2026-06-10': 2 } })
          : emptyPass({ versionsByDate: { '2026-06-10': ['2.0.9'] }, unreadableByDate: { '2026-06-10': 1 } }),
    });
    await r.runner.tick();
    expect(readDay(dir, '2026-06-10')!.caveats).toEqual({
      unreadableTranscriptLines: 2,
      readerVersion: '0.1.0',
      claudeCodeVersions: ['2.1.5'],
    });
    await r.runner.tick();
    expect(readDay(dir, '2026-06-10')!.caveats).toMatchObject({
      unreadableTranscriptLines: 3,
      claudeCodeVersions: ['2.0.9', '2.1.5'],
    });
  });

  it('a more result schedules the follow-up in 30 s, not 5 minutes', async () => {
    let calls = 0;
    const r = rig(10, {
      readPass: async () => {
        calls++;
        return emptyPass({ more: calls === 1 });
      },
    });
    await r.runner.tick();
    expect(r.timers.timeoutMs()).toEqual([FOLLOW_UP_MS]);
    expect(FOLLOW_UP_MS).toBe(30_000);
    r.timers.fireTimeouts();
    await new Promise((res) => setImmediate(res));
    expect(calls).toBe(2);
    expect(r.timers.timeoutMs()).toEqual([]); // second pass was complete: nothing more scheduled
  });

  it('refreshes the lock every 30 s while held and clears the timer after', async () => {
    let during: number[] = [];
    let release!: () => void;
    const gate = new Promise<void>((res) => (release = res));
    const r = rig(10, {
      readPass: async () => {
        during = [...r.timers.intervals.values()].map((i) => i.ms);
        await gate;
        return emptyPass();
      },
    });
    const p = r.runner.tick();
    await new Promise((res) => setImmediate(res));
    expect(during).toEqual([REFRESH_MS]);
    release();
    await p;
    expect(r.timers.intervals.size).toBe(0);
  });

  it('releases the lock even when the pass throws', async () => {
    const r = rig(10, {
      readPass: async () => {
        throw new Error('disk gone');
      },
    });
    const res = await r.runner.tick();
    expect(res.merged).toEqual([]);
    expect(r.errors).toHaveLength(1);
    expect(fs.existsSync(path.join(dir, 'state', 'merge.lock'))).toBe(false);
  });

  it('start() schedules the 5-minute interval and a background first tick; dispose() stops both', () => {
    const r = rig(10);
    r.runner.start();
    expect([...r.timers.intervals.values()].map((i) => i.ms)).toEqual([TICK_MS]);
    expect(r.timers.timeoutMs()).toEqual([0]);
    r.runner.dispose();
    expect(r.timers.intervals.size).toBe(0);
    expect(r.timers.timeouts.size).toBe(0);
  });
});

describe('two runners on one data dir', () => {
  it('only one merges per tick, and no day file or alias is lost', async () => {
    seedSpool([hb('proj-a', sec(13, 0)), hb('proj-b', sec(13, 10))]);
    let release!: () => void;
    const gate = new Promise<void>((res) => (release = res));
    let aReads = 0;
    const a = rig(111, {
      readPass: async () => {
        aReads++;
        await gate;
        return emptyPass();
      },
    });
    const b = rig(222);

    const first = a.runner.tick(); // holds the lock while its pass is "reading"
    await new Promise((res) => setImmediate(res));
    const skipped = await b.runner.tick();
    expect(skipped).toEqual({ ran: false, merged: [] });
    expect(b.updated).toEqual([]);

    release();
    const done = await first;
    expect(done.ran).toBe(true);
    expect(aReads).toBe(1);

    const second = await b.runner.tick(); // B's turn
    expect(second.ran).toBe(true);

    // Interleave again with more ticks; every file stays valid JSON and aliases never change.
    const aliasesBefore = a.store.maskState().projects;
    expect(Object.keys(aliasesBefore).sort()).toEqual(['proj-a', 'proj-b']);
    await Promise.all([a.runner.tick(), b.runner.tick(), a.runner.tick(), b.runner.tick()]);
    expect(a.store.maskState().projects).toEqual(aliasesBefore);
    expect(b.store.maskState().projects).toEqual(aliasesBefore);

    for (const day of listDays(dir, '2026-06-01', '2026-06-30')) {
      expect(day.v).toBe(1);
      expect(day.totals.totalMs).toBe(day.totals.youMs + day.totals.claudeMs + day.totals.bothMs);
    }
    expect(readDay(dir, '2026-06-10')!.projects.map((p) => p.key).sort()).toEqual(['proj-a', 'proj-b']);
    const leftovers = fs.readdirSync(path.join(dir, 'days')).filter((n) => !/^\d{4}-\d{2}-\d{2}\.json$/.test(n));
    expect(leftovers).toEqual([]);
  });

  it('a UI alias write from another window waits for the running merge, then lands without loss', async () => {
    seedSpool([hb('proj-a', sec(13, 0))]);
    let release!: () => void;
    const gate = new Promise<void>((res) => (release = res));
    const a = rig(111, {
      readPass: async () => {
        await gate;
        return emptyPass();
      },
    });
    const ui = new AliasStore(dir, { isNew: false, lockOptions: { pid: 333 }, sleep: () => new Promise((r) => setTimeout(r, 5)) });
    const tick = a.runner.tick();
    await new Promise((res) => setImmediate(res));
    const write = ui.setStreamerMode(true); // blocked by the lock the runner holds
    await new Promise((res) => setTimeout(res, 20));
    release();
    await tick;
    await write;
    const state = ui.maskState();
    expect(state.streamerMode).toBe(true);
    expect(Object.keys(state.projects)).toEqual(['proj-a']); // the runner's assignment survived the UI write
  });
});
