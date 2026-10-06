import * as fs from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { clearProjectCache } from '../project';
import { readClaudeDay, localDate, appendClaudeEvents, pruneClaudeSpool } from '../store/claudeSpool';
import { hashPath } from '../store/hash';
import { readOffsets } from '../store/state';
import { stateDir } from '../store/paths';
import type { ClaudeEvent, InstalledState } from '../types';
import { discoverHomes } from './homes';
import { readPass, streamId } from './reader';

const SALT = 'a'.repeat(64);
const INSTALLED: InstalledState = { installedAt: '2026-03-01T00:00:00.000Z', salt: SALT };
const SESSION = 'sess-1';
const CWD = '/fixture/repos/alpha';

let root: string;
let data: string;
let home: string;
let projDir: string;

beforeEach(() => {
  clearProjectCache();
  root = fs.mkdtempSync(path.join(os.tmpdir(), 'sanduhr-reader-'));
  data = path.join(root, 'data');
  home = path.join(root, '.claude-a');
  projDir = path.join(home, 'projects', 'fixture-alpha');
  fs.mkdirSync(projDir, { recursive: true });
});
afterEach(() => {
  fs.rmSync(root, { recursive: true, force: true });
});

let n = 0;
const ts = (min: number): string => new Date(Date.UTC(2026, 5, 10, 12, min, 0)).toISOString();
const line = (o: Record<string, unknown>): string => JSON.stringify(o);
const user = (min: number, content: unknown, extra: Record<string, unknown> = {}): string =>
  line({ type: 'user', uuid: `u-${++n}`, timestamp: ts(min), cwd: CWD, sessionId: SESSION, message: { role: 'user', content }, ...extra });
const assistant = (min: number): string =>
  line({ type: 'assistant', uuid: `a-${++n}`, timestamp: ts(min), cwd: CWD, sessionId: SESSION, message: { role: 'assistant', content: [] } });
const toolResult = (min: number, toolUseResult?: unknown, extra: Record<string, unknown> = {}): string =>
  user(min, [{ type: 'tool_result', tool_use_id: 't', content: 'ok' }], { toolUseResult, ...extra });

const write = (file: string, lines: string[]): string => {
  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(file, lines.join('\n') + '\n');
  return file;
};
const main = (name = `${SESSION}.jsonl`): string => path.join(projDir, name);

const allEvents = (): ClaudeEvent[] => {
  const dates = fs.existsSync(path.join(data, 'claude'))
    ? fs.readdirSync(path.join(data, 'claude')).map((f) => f.replace('.jsonl', ''))
    : [];
  return dates.flatMap((d) => readClaudeDay(data, d));
};
const rawLineCount = (): number =>
  fs.existsSync(path.join(data, 'claude'))
    ? fs
        .readdirSync(path.join(data, 'claude'))
        .map((f) => fs.readFileSync(path.join(data, 'claude', f), 'utf8').split('\n').filter(Boolean).length)
        .reduce((a, b) => a + b, 0)
    : 0;

const pass = (extra: Record<string, unknown> = {}) => readPass({ dir: data, installed: INSTALLED, homes: [home], ...extra });
const oldInstall: InstalledState = { installedAt: '2020-01-01T00:00:00.000Z', salt: SALT };

describe('prompt rule', () => {
  it('counts human prompts and excludes every machine-written user entry', async () => {
    write(main(), [
      user(0, 'hello'),
      user(1, [{ type: 'text', text: 'array prompt' }]),
      user(2, 'with origin', { origin: { kind: 'human' } }),
      user(3, 'notif', { origin: { kind: 'task-notification' } }),
      user(4, 'peer', { origin: { kind: 'peer' } }),
      user(5, 'meta', { isMeta: true }),
      user(6, 'summary', { isCompactSummary: true }),
      user(7, 'sidechain task', { isSidechain: true }),
    ]);
    const r = await pass({ installed: oldInstall });
    const prompts = r.events.filter((e) => e.kind === 'prompt');
    expect(prompts).toHaveLength(3);
    expect(r.events).toHaveLength(3);
  });

  it('an assistant line and a tool_result line are activity, main and sidechain alike', async () => {
    write(main(), [assistant(0), toolResult(1), toolResult(2, undefined, { isSidechain: true }), assistant(3)]);
    const r = await pass({ installed: oldInstall });
    expect(r.events.map((e) => e.kind)).toEqual(['activity', 'activity', 'activity', 'activity']);
  });
});

describe('Claude lines', () => {
  it('counts only + and - hunk lines, never context or hunk length fields', async () => {
    const edit = {
      filePath: '/fixture/repos/alpha/a.ts',
      structuredPatch: [
        { oldStart: 1, oldLines: 9, newStart: 1, newLines: 10, lines: [' ctx', ' ctx', '-old1', '+new1', '+new2', ' ctx'] },
        { oldStart: 20, oldLines: 3, newStart: 21, newLines: 3, lines: [' ctx', '-gone', '+here'] },
      ],
    };
    write(main(), [toolResult(0, edit)]);
    const [ev] = (await pass({ installed: oldInstall })).events;
    expect(ev.kind).toBe('edit');
    expect(ev.linesAdded).toBe(3);
    expect(ev.linesRemoved).toBe(2);
    expect(ev.entity).toBe(hashPath('/fixture/repos/alpha/a.ts', SALT));
  });

  it('a create counts the content lines, trailing newline adds none', async () => {
    write(main(), [
      toolResult(0, { type: 'create', filePath: '/fixture/repos/alpha/n.ts', content: 'a\nb\nc\n' }),
      toolResult(1, { type: 'create', filePath: '/fixture/repos/alpha/m.ts', content: 'a\nb' }),
      toolResult(2, { type: 'create', filePath: '/fixture/repos/alpha/e.ts', content: '' }),
    ]);
    const evs = (await pass({ installed: oldInstall })).events;
    expect(evs.map((e) => [e.linesAdded, e.linesRemoved])).toEqual([[3, 0], [2, 0], [0, 0]]);
  });

  it('an edit without filePath stays plain activity', async () => {
    write(main(), [toolResult(0, { stdout: 'x' })]);
    const [ev] = (await pass({ installed: oldInstall })).events;
    expect(ev.kind).toBe('activity');
    expect(ev.linesAdded).toBeUndefined();
  });
});

describe('streams and ids', () => {
  it('main and subagent files are different streams even with one sessionId', async () => {
    const sub = path.join(projDir, SESSION, 'subagents', 'agent-x.jsonl');
    const wf = path.join(projDir, SESSION, 'subagents', 'workflows', 'wf1', 'agent-y.jsonl');
    write(main(), [user(0, 'hi'), assistant(1)]);
    write(sub, [user(0, 'task prompt', { isSidechain: true }), assistant(1), toolResult(2, undefined, { isSidechain: true })]);
    write(wf, [assistant(3)]);
    const r = await pass({ installed: oldInstall });
    const streams = new Set(r.events.map((e) => e.stream));
    expect(streams.size).toBe(3);
    expect(r.events.filter((e) => e.kind === 'prompt')).toHaveLength(1);
    expect(new Set(r.events.map((e) => e.stream))).toContain(streamId(fs.realpathSync(sub)));
  });

  it('falls back to <stream>:<byteOffset> when a line has no uuid', async () => {
    const first = line({ type: 'assistant', timestamp: ts(0), cwd: CWD, message: {} });
    const second = line({ type: 'assistant', timestamp: ts(1), cwd: CWD, message: {} });
    const f = write(main(), [first, second]);
    const r = await pass({ installed: oldInstall });
    const s = streamId(fs.realpathSync(f));
    expect(r.events.map((e) => e.id)).toEqual([`${s}:0`, `${s}:${Buffer.byteLength(first) + 1}`]);
  });

  it('uses the last cwd seen for lines without one; none seen counts as unreadable', async () => {
    const noCwd = line({ type: 'assistant', uuid: 'n1', timestamp: ts(1), message: {} });
    write(main('a.jsonl'), [assistant(0), noCwd]);
    write(main('b.jsonl'), [noCwd.replace('n1', 'n2')]);
    const r = await pass({ installed: oldInstall });
    expect(r.events).toHaveLength(2);
    expect(new Set(r.events.map((e) => e.project)).size).toBe(1);
    expect(Object.values(r.unreadableByDate).reduce((a, b) => a + b, 0)).toBe(1);
  });

  it('event date is the local date of the timestamp', async () => {
    write(main(), [assistant(0)]);
    const r = await pass({ installed: oldInstall });
    expect(r.touchedDates).toEqual([localDate(Date.parse(ts(0)))]);
    expect(readClaudeDay(data, r.touchedDates[0])).toHaveLength(1);
  });
});

describe('unreadable lines', () => {
  it('counts malformed JSON per local date and never throws', async () => {
    write(main(), [assistant(0), '{not json', assistant(1), '"a string"']);
    const r = await pass({ installed: oldInstall });
    expect(r.events).toHaveLength(2);
    expect(r.unreadableByDate[localDate(Date.parse(ts(0)))]).toBe(2);
  });
});

describe('first run and installedAt', () => {
  it('a file older than installedAt starts at its current size; a newer one drops events before installedAt', async () => {
    const oldFile = write(main('old.jsonl'), [assistant(0), assistant(1)]);
    const past = new Date('2026-02-01T00:00:00Z');
    fs.utimesSync(oldFile, past, past);
    // newer file, with one event before installedAt and one after
    const early = line({ type: 'assistant', uuid: 'early', timestamp: '2026-02-20T00:00:00.000Z', cwd: CWD, message: {} });
    write(main('new.jsonl'), [early, assistant(5)]);
    const r = await pass({ installed: { installedAt: '2026-03-01T00:00:00.000Z', salt: SALT } });
    expect(r.events).toHaveLength(1);
    expect(r.events[0].id).not.toBe('early');
    const offs = readOffsets(stateDir(data));
    expect(offs[fs.realpathSync(oldFile)].offset).toBe(fs.statSync(oldFile).size);
    // later appends to the old file are read
    fs.appendFileSync(oldFile, assistant(9) + '\n');
    const r2 = await pass({ installed: INSTALLED });
    expect(r2.events).toHaveLength(1);
  });
});

describe('incremental, restart and crash safety', () => {
  const eight = (): string[] => Array.from({ length: 8 }, (_, i) => (i % 2 ? toolResult(i, { type: 'create', filePath: `/fixture/repos/alpha/f${i}.ts`, content: 'x\n' }) : user(i, `p${i}`)));

  it('reads only new lines on the next pass', async () => {
    const f = write(main(), [assistant(0)]);
    expect((await pass({ installed: oldInstall })).events).toHaveLength(1);
    expect((await pass({ installed: oldInstall })).events).toHaveLength(0);
    fs.appendFileSync(f, assistant(1) + '\n');
    expect((await pass({ installed: oldInstall })).events).toHaveLength(1);
    expect(rawLineCount()).toBe(2);
  });

  it('a restart over the same state dir after a partial pass loses and duplicates nothing', async () => {
    write(main('a.jsonl'), eight());
    write(main('b.jsonl'), eight());
    const first = await pass({ installed: oldInstall, maxBytes: 300 });
    expect(first.more).toBe(true);
    expect(first.events.length).toBeGreaterThan(0);
    expect(first.events.length).toBeLessThan(16);
    // "restart": every pass is a fresh reader over the same data dir
    let more = true;
    for (let i = 0; i < 50 && more; i++) more = (await pass({ installed: oldInstall, maxBytes: 300 })).more;
    expect(more).toBe(false);
    expect(allEvents()).toHaveLength(16);
    expect(rawLineCount()).toBe(16);
    expect(new Set(allEvents().map((e) => e.id)).size).toBe(16);
  });

  it('a crash between spool append and offset write re-reads without duplicates', async () => {
    write(main(), eight());
    const offsetFile = path.join(stateDir(data), 'offsets.json');
    await pass({ installed: oldInstall, maxBytes: 1 }); // a first line is spooled and committed
    const before = fs.readFileSync(offsetFile, 'utf8');
    await pass({ installed: oldInstall, maxBytes: 400 });
    // simulate the crash: the spool has the second batch, the offsets never advanced
    fs.writeFileSync(offsetFile, before);
    let more = true;
    for (let i = 0; i < 50 && more; i++) more = (await pass({ installed: oldInstall, maxBytes: 400 })).more;
    expect(rawLineCount()).toBeGreaterThan(8); // re-read wrote the batch twice
    expect(allEvents()).toHaveLength(8);
    expect(new Set(allEvents().map((e) => e.id)).size).toBe(8);
  });

  it('the spool is written before the offset: offsets never run ahead of the spool', async () => {
    write(main(), eight());
    await pass({ installed: oldInstall });
    const offs = readOffsets(stateDir(data));
    expect(Object.values(offs)[0].offset).toBe(fs.statSync(main()).size);
    expect(allEvents()).toHaveLength(8);
  });

  it('a bounded pass reports remaining work, then finishes', async () => {
    write(main('a.jsonl'), eight());
    write(main('b.jsonl'), eight());
    const r = await pass({ installed: oldInstall, maxBytes: 100 });
    expect(r.more).toBe(true);
    const done = await pass({ installed: oldInstall });
    expect(done.more).toBe(false);
  });

  it('progresses even when one line is larger than the budget', async () => {
    write(main(), [toolResult(0, { type: 'create', filePath: '/fixture/repos/alpha/big.ts', content: 'y\n'.repeat(5000) }), assistant(1)]);
    let total = 0;
    for (let i = 0; i < 10; i++) {
      const r = await pass({ installed: oldInstall, maxBytes: 50 });
      total += r.events.length;
      if (!r.more) break;
    }
    expect(total).toBe(2);
  });

  it('a file that shrank restarts at zero', async () => {
    const f = write(main(), [assistant(0), assistant(1), assistant(2)]);
    expect((await pass({ installed: oldInstall })).events).toHaveLength(3);
    write(f, [assistant(4)]);
    const r = await pass({ installed: oldInstall });
    expect(r.events).toHaveLength(1);
    expect(allEvents()).toHaveLength(4);
  });

  it('keeps a trailing partial line for the next pass', async () => {
    const f = main();
    const whole = assistant(0);
    const partialLine = assistant(1);
    const cut = Math.floor(partialLine.length / 2);
    fs.writeFileSync(f, whole + '\n' + partialLine.slice(0, cut));
    const r1 = await pass({ installed: oldInstall });
    expect(r1.events).toHaveLength(1);
    expect(readOffsets(stateDir(data))[fs.realpathSync(f)].offset).toBe(Buffer.byteLength(whole) + 1);
    fs.appendFileSync(f, partialLine.slice(cut) + '\n');
    const r2 = await pass({ installed: oldInstall });
    expect(r2.events).toHaveLength(1);
    expect(r1.unreadableByDate).toEqual({});
    expect(allEvents()).toHaveLength(2);
  });
});

describe('homes', () => {
  const mk = (dir: string, withProjects = true): string => {
    fs.mkdirSync(withProjects ? path.join(dir, 'projects') : dir, { recursive: true });
    return dir;
  };

  it('finds .claude* homes with projects/, skips backups and homes without projects', () => {
    const h = path.join(root, 'userhome');
    mk(path.join(h, '.claude'));
    mk(path.join(h, '.claude-work'));
    mk(path.join(h, '.claude.config-backup-x'));
    mk(path.join(h, '.claude-empty'), false);
    mk(path.join(h, 'other'));
    const found = discoverHomes({ homeDir: h, env: {} }).map((p) => path.basename(p));
    expect(found.sort()).toEqual(['.claude', '.claude-work']);
  });

  it('adds CLAUDE_CONFIG_DIR and de-duplicates by real path', () => {
    const h = path.join(root, 'userhome');
    mk(path.join(h, '.claude-a'));
    const elsewhere = mk(path.join(root, 'elsewhere'));
    const found = discoverHomes({ homeDir: h, env: { CLAUDE_CONFIG_DIR: elsewhere } });
    expect(found).toHaveLength(2);
    const dup = discoverHomes({ homeDir: h, env: { CLAUDE_CONFIG_DIR: path.join(h, '.claude-a') } });
    expect(dup).toHaveLength(1);
    expect(discoverHomes({ homeDir: h, env: { CLAUDE_CONFIG_DIR: path.join(h, '.claude.config-backup-x') } })).toHaveLength(1);
  });

  it('readPass reads two homes and never the backup home', async () => {
    const h = path.join(root, 'userhome');
    const a = mk(path.join(h, '.claude'));
    const b = mk(path.join(h, '.claude-two'));
    const bak = mk(path.join(h, '.claude.config-backup-x'));
    for (const hh of [a, b, bak]) write(path.join(hh, 'projects', 'p', 's.jsonl'), [assistant(0)]);
    const r = await readPass({ dir: data, installed: oldInstall, homeDir: h, env: {} });
    expect(r.events).toHaveLength(2);
  });
});

describe('claude spool', () => {
  it('de-duplicates by id and tolerates a torn last line', () => {
    const ev = (id: string, t: number): ClaudeEvent => ({ v: 1, id, time: t, project: 'p', stream: 's', kind: 'activity' });
    const t = Date.parse(ts(0)) / 1000;
    appendClaudeEvents(data, [ev('a', t), ev('a', t), ev('b', t)]);
    const file = path.join(data, 'claude', `${localDate(t * 1000)}.jsonl`);
    fs.appendFileSync(file, '{"v":1,"id":"torn'); // crash mid-line
    appendClaudeEvents(data, [ev('c', t)]);
    expect(readClaudeDay(data, localDate(t * 1000)).map((e) => e.id)).toEqual(['a', 'b', 'c']);
    expect(readClaudeDay(data, '1999-01-01')).toEqual([]);
  });

  it('prunes files older than 90 days', () => {
    fs.mkdirSync(path.join(data, 'claude'), { recursive: true });
    for (const d of ['2026-01-01', '2026-05-01', '2026-06-09']) fs.writeFileSync(path.join(data, 'claude', `${d}.jsonl`), '');
    const removed = pruneClaudeSpool(data, new Date(2026, 5, 10, 12));
    expect(removed).toEqual(['2026-01-01']);
    expect(fs.readdirSync(path.join(data, 'claude')).sort()).toEqual(['2026-05-01.jsonl', '2026-06-09.jsonl']);
  });
});
