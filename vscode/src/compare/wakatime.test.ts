import * as fs from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import type { DayRecord } from '../types';
import {
  appendCompareLine,
  Comparison,
  locateCli,
  parseDurationText,
  parseJson,
  parseRawJson,
  parseText,
  readCompareLines,
  runToday,
  type ExecFn,
  type RunResult,
} from './wakatime';

const HOME_WIN = 'C:\\fixture\\home';
const HOME_NIX = '/fixture/home';

describe('locateCli', () => {
  const only = (p: string) => (q: string) => q === p;

  it.each([
    ['win32', 'x64', path.join(HOME_WIN, '.wakatime', 'wakatime-cli-windows-amd64.exe')],
    ['win32', 'arm64', path.join(HOME_WIN, '.wakatime', 'wakatime-cli-windows-arm64.exe')],
    ['darwin', 'arm64', path.join(HOME_WIN, '.wakatime', 'wakatime-cli-darwin-arm64')],
    ['darwin', 'x64', path.join(HOME_WIN, '.wakatime', 'wakatime-cli-darwin-amd64')],
    ['linux', 'x64', path.join(HOME_WIN, '.wakatime', 'wakatime-cli-linux-amd64')],
    ['linux', 'arm64', path.join(HOME_WIN, '.wakatime', 'wakatime-cli-linux-arm64')],
  ])('finds the home binary on %s %s', (platform, arch, expected) => {
    expect(locateCli(HOME_WIN, platform, { arch, exists: only(expected), pathEnv: '' })).toBe(expected);
  });

  it('falls back to PATH', () => {
    const onPath = path.join('/fixture/bin', 'wakatime-cli');
    expect(locateCli(HOME_NIX, 'linux', { arch: 'x64', exists: only(onPath), pathEnv: '/fixture/other:/fixture/bin' })).toBe(onPath);
    const exe = path.join('C:\\fixture\\bin', 'wakatime-cli.exe');
    expect(locateCli(HOME_WIN, 'win32', { arch: 'x64', exists: only(exe), pathEnv: 'C:\\fixture\\bin;C:\\fixture\\x' })).toBe(exe);
  });

  it('prefers the home binary over PATH', () => {
    const home = path.join(HOME_NIX, '.wakatime', 'wakatime-cli-linux-amd64');
    expect(locateCli(HOME_NIX, 'linux', { arch: 'x64', exists: () => true, pathEnv: '/fixture/bin' })).toBe(home);
  });

  it('returns undefined when nothing exists', () => {
    expect(locateCli(HOME_NIX, 'linux', { arch: 'x64', exists: () => false, pathEnv: '/fixture/bin' })).toBeUndefined();
  });
});

describe('parseDurationText', () => {
  it.each([
    ['2 hrs 32 mins', 2 * 3600 + 32 * 60], // observed form
    ['1 hr 4 mins', 3600 + 4 * 60],
    ['1 hr 1 min', 3600 + 60],
    ['1 hrs 0 mins', 3600],
    ['45 mins', 45 * 60],
    ['1 min', 60],
    ['3h 12m', 3 * 3600 + 12 * 60],
    ['3 h 12 m', 3 * 3600 + 12 * 60],
    ['0 secs', 0],
    ['30 secs', 30],
    ['2 hrs', 7200],
    ['  2 hrs 5 mins  ', 7200 + 300],
    ['2 hrs 32 mins, 1 hr 10 mins Coding', 2 * 3600 + 32 * 60],
    ['45 mins, 45 mins Coding', 45 * 60],
    ['2 HRS 5 MINS', 7200 + 300],
  ])('parses %j', (text, seconds) => {
    expect(parseDurationText(text)).toBe(seconds);
  });

  it.each(['', 'no data', 'Coding: 2 hrs', '2 parsecs', 'about 2 hrs'])('rejects %j', (text) => {
    expect(parseDurationText(text)).toBeUndefined();
  });
});

describe('output parsers', () => {
  it('parseText takes the first non-empty line', () => {
    expect(parseText('\n2 hrs 32 mins\n')).toBe(9120);
    expect(parseText('2 hrs 32 mins\r\n')).toBe(9120);
    expect(parseText('')).toBeUndefined();
  });

  it('parseJson reads the text field', () => {
    expect(parseJson('{"text":"2 hrs 32 mins","has_team_features":false}')).toBe(9120);
    expect(parseJson('{"text":"nope"}')).toBeUndefined();
    expect(parseJson('not json')).toBeUndefined();
  });

  it('parseRawJson reads data.grand_total.total_seconds', () => {
    const raw = { cached_at: 'x', data: { grand_total: { total_seconds: 1234.5, text: '20 mins' }, categories: [] } };
    expect(parseRawJson(JSON.stringify(raw))).toBe(1234.5);
    expect(parseRawJson('{"data":{}}')).toBeUndefined();
    expect(parseRawJson('{"data":{"grand_total":{"total_seconds":"5"}}}')).toBeUndefined();
    expect(parseRawJson('{"data":{"grand_total":{"total_seconds":-1}}}')).toBeUndefined();
    expect(parseRawJson('garbage')).toBeUndefined();
  });
});

type Reply = { stdout?: string; error?: { code?: number | string; killed?: boolean } };

function fakeExec(replies: Record<string, Reply>): { exec: ExecFn; calls: string[][] } {
  const calls: string[][] = [];
  const exec: ExecFn = (_file, args, _opts, cb) => {
    calls.push(args);
    const r = replies[args.join(' ')] ?? { error: { code: 1 } };
    if (r.error) cb(Object.assign(new Error('fail'), r.error) as never, '', '');
    else cb(null, r.stdout ?? '', '');
    return undefined;
  };
  return { exec, calls };
}

describe('runToday', () => {
  const RAW = JSON.stringify({ data: { grand_total: { total_seconds: 600.4 } } });

  it('prefers raw-json and does not shell out twice', async () => {
    const { exec, calls } = fakeExec({ '--today --output raw-json': { stdout: RAW } });
    expect(await runToday('/fixture/cli', { exec })).toEqual({ seconds: 600.4 });
    expect(calls).toEqual([['--today', '--output', 'raw-json']]);
  });

  it('falls back to the text form when raw-json fails', async () => {
    const { exec, calls } = fakeExec({ '--today': { stdout: '1 hr 5 mins\n' } });
    expect(await runToday('/fixture/cli', { exec })).toEqual({ seconds: 3900 });
    expect(calls.length).toBe(2);
  });

  it('falls back to the text form when raw-json is unrecognizable', async () => {
    const { exec } = fakeExec({ '--today --output raw-json': { stdout: '{"data":{}}' }, '--today': { stdout: '5 mins' } });
    expect(await runToday('/fixture/cli', { exec })).toEqual({ seconds: 300 });
  });

  it('reports an error for unrecognized text', async () => {
    const { exec } = fakeExec({ '--today --output raw-json': { error: { code: 1 } }, '--today': { stdout: 'hello' } });
    expect(await runToday('/fixture/cli', { exec })).toEqual({ error: 'unrecognized wakatime-cli output' });
  });

  it('a timeout is an error and is not retried in text form', async () => {
    const { exec, calls } = fakeExec({ '--today --output raw-json': { error: { killed: true } } });
    const r = await runToday('/fixture/cli', { exec, timeoutMs: 15000 });
    expect(r).toEqual({ error: 'timed out after 15000 ms' });
    expect(calls.length).toBe(1);
  });

  it('passes the timeout and never uses a shell', async () => {
    let seen: Record<string, unknown> | undefined;
    const exec: ExecFn = (_f, _a, opts, cb) => {
      seen = opts as unknown as Record<string, unknown>;
      cb(null, RAW, '');
      return undefined;
    };
    await runToday('/fixture/cli', { exec });
    expect(seen?.timeout).toBe(15000);
    expect(seen).not.toHaveProperty('shell');
  });

  it('a launch that throws is an error', async () => {
    const exec: ExecFn = () => {
      throw new Error('spawn failed');
    };
    expect(await runToday('/fixture/cli', { exec })).toEqual({ error: 'spawn failed' });
  });
});

function day(date: string, youMs: number, bothMs: number): DayRecord {
  return {
    v: 1,
    date,
    machine: 'fixture',
    generatedAt: '2026-01-01T00:00:00.000Z',
    totals: { youMs, claudeMs: 0, bothMs, totalMs: youMs + bothMs, agentMs: 0, linesYou: 0, linesClaude: 0 },
    projects: [],
    caveats: { unreadableTranscriptLines: 0, readerVersion: '0.1.0', claudeCodeVersions: [] },
  };
}

describe('Comparison', () => {
  let dir: string;
  beforeEach(() => {
    dir = fs.mkdtempSync(path.join(os.tmpdir(), 'sanduhr-cmp-'));
  });
  afterEach(() => {
    fs.rmSync(dir, { recursive: true, force: true });
  });

  const NOW = new Date(2026, 5, 10, 14, 0, 0).getTime();
  const TODAY = '2026-06-10';

  function make(over: Partial<ConstructorParameters<typeof Comparison>[0]> = {}) {
    const runs: string[] = [];
    const errors: string[] = [];
    let now = NOW;
    const c = new Comparison({
      dir,
      enabled: () => true,
      now: () => now,
      locate: () => '/fixture/cli',
      run: async (p): Promise<RunResult> => {
        runs.push(p);
        return { seconds: 3600 };
      },
      getToday: async () => day(TODAY, 30 * 60_000, 15 * 60_000),
      onError: (m) => errors.push(m),
      ...over,
    });
    return { c, runs, errors, setNow: (n: number) => (now = n) };
  }

  it('appends one line with ours = (you + both) seconds and exposes it as latest', async () => {
    const { c, runs } = make();
    await c.onDayUpdated(day(TODAY, 1, 1));
    expect(runs).toEqual(['/fixture/cli']);
    const lines = readCompareLines(dir);
    expect(lines).toHaveLength(1);
    expect(lines[0]).toMatchObject({ date: TODAY, wakatimeSeconds: 3600, oursEditorSeconds: 2700 });
    expect(new Date(lines[0].at).getTime()).toBe(NOW);
    expect(c.latest(TODAY)?.wakatimeSeconds).toBe(3600);
    expect(await c.provider()).toEqual({ wakatimeSeconds: 3600 });
  });

  it('is gated hourly: 30 minutes apart runs once, 61 minutes apart runs twice', async () => {
    const { c, runs, setNow } = make();
    await c.onDayUpdated(day(TODAY, 1, 1));
    setNow(NOW + 30 * 60_000);
    await c.onDayUpdated(day(TODAY, 2, 2));
    expect(runs).toHaveLength(1);
    setNow(NOW + 61 * 60_000);
    await c.onDayUpdated(day(TODAY, 3, 3));
    expect(runs).toHaveLength(2);
    expect(readCompareLines(dir)).toHaveLength(2);
  });

  it('latest picks the newest line for the date', async () => {
    let secs = 100;
    const { c, setNow } = make({ run: async () => ({ seconds: secs }) });
    await c.onDayUpdated(day(TODAY, 1, 1));
    secs = 200;
    setNow(NOW + 61 * 60_000);
    await c.onDayUpdated(day(TODAY, 1, 1));
    expect(c.latest(TODAY)?.wakatimeSeconds).toBe(200);
    expect(await c.provider()).toEqual({ wakatimeSeconds: 200 });
  });

  it('survives a restart: a fresh service sees a line under an hour old', async () => {
    await make().c.onDayUpdated(day(TODAY, 1, 1));
    const second = make();
    await second.c.onDayUpdated(day(TODAY, 1, 1));
    expect(second.runs).toHaveLength(0);
    second.setNow(NOW + 61 * 60_000);
    await second.c.onDayUpdated(day(TODAY, 1, 1));
    expect(second.runs).toHaveLength(1);
  });

  it('runs again on the next local day', async () => {
    const { c, runs, setNow } = make();
    await c.onDayUpdated(day(TODAY, 1, 1));
    setNow(new Date(2026, 5, 11, 9, 0, 0).getTime());
    await c.onDayUpdated(day('2026-06-11', 1, 1));
    expect(runs).toHaveLength(2);
    expect(readCompareLines(dir).map((l) => l.date)).toEqual([TODAY, '2026-06-11']);
  });

  it('ignores updates for other dates', async () => {
    const { c, runs } = make();
    await c.onDayUpdated(day('2026-06-09', 1, 1));
    expect(runs).toHaveLength(0);
  });

  it('does nothing and shows nothing when the setting is off', async () => {
    const { c, runs } = make({ enabled: () => false });
    await c.onDayUpdated(day(TODAY, 1, 1));
    expect(await c.compareNow()).toBeUndefined();
    expect(runs).toHaveLength(0);
    expect(readCompareLines(dir)).toHaveLength(0);
    appendCompareLine(dir, { date: TODAY, wakatimeSeconds: 5, oursEditorSeconds: 5, at: 'x' });
    expect(c.latest(TODAY)).toBeUndefined();
    expect(await c.provider()).toBeUndefined();
  });

  it('does nothing when there is no CLI', async () => {
    const { c, runs, errors } = make({ locate: () => undefined });
    await c.onDayUpdated(day(TODAY, 1, 1));
    expect(runs).toHaveLength(0);
    expect(errors).toEqual([]);
    expect(c.available()).toBe(false);
    expect(await c.provider()).toBeUndefined();
  });

  it('logs a failed run once and retries at the next hourly window', async () => {
    let n = 0;
    const { c, errors, setNow } = make({
      run: async () => {
        n++;
        return { error: 'timed out after 15000 ms' };
      },
    });
    await c.onDayUpdated(day(TODAY, 1, 1));
    setNow(NOW + 30 * 60_000);
    await c.onDayUpdated(day(TODAY, 2, 2));
    expect(n).toBe(1);
    setNow(NOW + 61 * 60_000);
    await c.onDayUpdated(day(TODAY, 2, 2));
    expect(n).toBe(2);
    expect(errors).toEqual(['timed out after 15000 ms']);
    expect(readCompareLines(dir)).toHaveLength(0);
    expect(await c.provider()).toBeUndefined();
  });

  it('compareNow bypasses the hourly gate', async () => {
    const { c, runs } = make();
    await c.onDayUpdated(day(TODAY, 1, 1));
    expect(await c.compareNow()).toEqual({ seconds: 3600 });
    expect(runs).toHaveLength(2);
    expect(readCompareLines(dir)).toHaveLength(2);
  });

  it('records ours as 0 when there is no day record yet', async () => {
    const { c } = make({ getToday: async () => undefined });
    await c.onDayUpdated(day(TODAY, 1, 1));
    expect(readCompareLines(dir)[0].oursEditorSeconds).toBe(0);
  });
});

describe('compare jsonl', () => {
  let dir: string;
  beforeEach(() => {
    dir = fs.mkdtempSync(path.join(os.tmpdir(), 'sanduhr-cmp-'));
  });
  afterEach(() => {
    fs.rmSync(dir, { recursive: true, force: true });
  });

  it('appends in order, skips unreadable lines, returns the newest per date', () => {
    expect(readCompareLines(dir)).toEqual([]);
    appendCompareLine(dir, { date: '2026-06-10', wakatimeSeconds: 1, oursEditorSeconds: 2, at: 'a' });
    fs.appendFileSync(path.join(dir, 'state', 'wakatime-compare.jsonl'), 'not json\n{"date":1}\n');
    appendCompareLine(dir, { date: '2026-06-10', wakatimeSeconds: 3, oursEditorSeconds: 4, at: 'b' });
    const lines = readCompareLines(dir);
    expect(lines.map((l) => l.wakatimeSeconds)).toEqual([1, 3]);
    const c = new Comparison({ dir, enabled: () => true, now: () => Date.now(), getToday: async () => undefined });
    expect(c.latest('2026-06-10')?.wakatimeSeconds).toBe(3);
    expect(c.latest('2026-06-11')).toBeUndefined();
  });
});

const REAL = process.env.SANDUHR_TIME_REAL_WAKATIME === '1';
describe.skipIf(!REAL)('real wakatime-cli (SANDUHR_TIME_REAL_WAKATIME=1)', () => {
  it('runToday parses a number from the installed CLI', async () => {
    const cli = locateCli(os.homedir());
    expect(cli).toBeDefined();
    const r = await runToday(cli as string);
    expect('seconds' in r).toBe(true);
    if ('seconds' in r) expect(Number.isFinite(r.seconds) && r.seconds >= 0).toBe(true);
  });
});
