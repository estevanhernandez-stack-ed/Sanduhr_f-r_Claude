import * as fs from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { clearProjectCache, resolve as resolveProject } from '../project';
import { readAgentsPass } from '../readAgents';
import { localDate, readAgentDay } from '../store/agentSpool';
import { hashPath } from '../store/hash';
import { stateDir } from '../store/paths';
import { readOffsets, writeOffsets } from '../store/state';
import type { AgentEvent, InstalledState } from '../types';
import * as fx from './fixtures';
import { discoverCodexHomes } from './homes';
import { codexStreamId, readCodexPass } from './reader';

const SALT = 'b'.repeat(64);
const oldInstall: InstalledState = { installedAt: '2020-01-01T00:00:00.000Z', salt: SALT };
const CWD = '/fixture/repos/alpha';
const CWD_B = '/fixture/repos/beta';
const S_MAIN = '00000000-0000-4000-8000-00000000000a';
const S_SUB = '00000000-0000-4000-8000-00000000000b';
const S_GUARD = '00000000-0000-4000-8000-00000000000c';

let root: string;
let data: string;
let home: string;

beforeEach(() => {
  clearProjectCache();
  root = fs.mkdtempSync(path.join(os.tmpdir(), 'sanduhr-codex-'));
  data = path.join(root, 'data');
  home = path.join(root, '.codex');
  fs.mkdirSync(path.join(home, 'sessions'), { recursive: true });
});
afterEach(() => {
  fs.rmSync(root, { recursive: true, force: true });
});

/** ISO UTC for a local wall-clock time on 2026-06-10 (or `day`). */
const iso = (h: number, m: number, s = 0, day = 10): string => new Date(2026, 5, day, h, m, s).toISOString();
const rollout = (name = 'rollout-2026-06-10T09-00-00-fixture.jsonl'): string => path.join(home, 'sessions', '2026', '06', '10', name);
const write = (file: string, lines: fx.Line[]): string => {
  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(file, fx.toJsonl(lines));
  return file;
};
const append = (file: string, lines: fx.Line[]): void => fs.appendFileSync(file, fx.toJsonl(lines));
const pass = (extra: Record<string, unknown> = {}) => readCodexPass({ dir: data, installed: oldInstall, homes: [home], ...extra });

const spooled = (): AgentEvent[] => {
  const dir = path.join(data, 'agents');
  if (!fs.existsSync(dir)) return [];
  return fs.readdirSync(dir).flatMap((f) => readAgentDay(data, f.replace('.jsonl', '')));
};
const kinds = (evs: AgentEvent[]): string[] => evs.map((e) => e.kind);

const head = (sessionId = S_MAIN, threadSource = 'user', cwd = CWD, t = iso(9, 0)): fx.Line[] => [
  fx.sessionMeta(t, { sessionId, threadSource, cwd, subagent: threadSource === 'subagent' }),
  fx.turnContext(t, cwd),
];

describe('the Codex human-prompt rule', () => {
  it('counts only a user message with user_input_order in a user thread', async () => {
    write(rollout(), [
      ...head(),
      fx.injectedUser(iso(9, 0, 1)),
      fx.injectedUser(iso(9, 0, 2)),
      fx.attributedUser(iso(9, 0, 3)),
      fx.developer(iso(9, 0, 4)),
      fx.humanPrompt(iso(9, 1), 1),
      fx.itemCompleted(iso(9, 1, 1), 'UserMessage'), // repeats the prompt: neither prompt nor activity
      fx.taskStarted(iso(9, 1, 2)),
    ]);
    const r = await pass();
    expect(kinds(r.events)).toEqual(['prompt', 'activity']);
    expect(r.events.every((e) => e.agent === 'codex' && e.v === 2)).toBe(true);
  });

  it('guardian_review and subagent threads are activity, never prompts', async () => {
    write(rollout('rollout-guard.jsonl'), [...head(S_GUARD, 'guardian_review'), fx.humanPrompt(iso(9, 1), 1), fx.reasoning(iso(9, 2))]);
    write(rollout('rollout-sub.jsonl'), [...head(S_SUB, 'subagent'), fx.injectedUser(iso(9, 1)), fx.humanPrompt(iso(9, 2), 1), fx.assistant(iso(9, 3))]);
    const r = await pass();
    expect(kinds(r.events)).toEqual(['activity', 'activity']);
  });
});

describe('activity records', () => {
  it('reasoning, assistant and agent messages, tool calls and outputs, task_started and item_completed', async () => {
    write(rollout(), [
      ...head(),
      fx.reasoning(iso(9, 1)),
      fx.assistant(iso(9, 2)),
      fx.agentMessage(iso(9, 3)),
      fx.functionCall(iso(9, 4)),
      fx.functionCallOutput(iso(9, 5)),
      fx.customToolCall(iso(9, 6), 'exec', 'const x = 1;'),
      fx.customToolCallOutput(iso(9, 7)),
      fx.taskStarted(iso(9, 8)),
      fx.itemCompleted(iso(9, 9), 'Reasoning'),
      // not time: cost, state and settings records
      fx.tokenCount(iso(9, 10)),
      fx.worldState(iso(9, 11)),
    ]);
    const r = await pass();
    expect(kinds(r.events)).toEqual(Array(9).fill('activity'));
    expect(r.events.map((e) => e.time)).toEqual([1, 2, 3, 4, 5, 6, 7, 8, 9].map((m) => Date.parse(iso(9, m)) / 1000));
  });
});

describe('segments, streams and projects', () => {
  it('each session_meta segment in one rollout is its own stream, keyed by path plus session_id', async () => {
    const f = write(rollout(), [
      ...head(S_MAIN, 'user'),
      fx.humanPrompt(iso(9, 1), 1),
      fx.reasoning(iso(9, 2)),
      fx.sessionMeta(iso(9, 3), { sessionId: S_SUB, threadSource: 'subagent', cwd: CWD, subagent: true }),
      fx.humanPrompt(iso(9, 4), 1), // a subagent's task prompt: machine-written
      fx.reasoning(iso(9, 5)),
      fx.sessionMeta(iso(9, 6), { sessionId: S_GUARD, threadSource: 'guardian_review', cwd: CWD }),
      fx.humanPrompt(iso(9, 7), 2),
      fx.assistant(iso(9, 8)),
    ]);
    const r = await pass();
    const real = fs.realpathSync(f);
    expect(r.events.map((e) => [e.kind, e.stream])).toEqual([
      ['prompt', codexStreamId(real, S_MAIN)],
      ['activity', codexStreamId(real, S_MAIN)],
      ['activity', codexStreamId(real, S_SUB)],
      ['activity', codexStreamId(real, S_GUARD)],
    ]);
    expect(new Set(r.events.map((e) => e.stream)).size).toBe(3);
  });

  it('the project follows turn_context.cwd, falling back to the segment\'s session_meta.cwd', async () => {
    write(rollout(), [
      fx.sessionMeta(iso(9, 0), { sessionId: S_MAIN, threadSource: 'user', cwd: CWD }),
      fx.reasoning(iso(9, 1)), // no turn yet: session_meta.cwd
      fx.turnContext(iso(9, 2), CWD_B),
      fx.reasoning(iso(9, 3)),
      fx.sessionMeta(iso(9, 4), { sessionId: S_SUB, threadSource: 'subagent', cwd: CWD }),
      fx.reasoning(iso(9, 5)), // a new segment starts from its own cwd
    ]);
    const r = await pass();
    const a = resolveProject(CWD).key;
    const b = resolveProject(CWD_B).key;
    expect(r.events.map((e) => e.project)).toEqual([a, b, a]);
  });

  it('a record with no cwd in force is unreadable', async () => {
    write(rollout(), [fx.reasoning(iso(9, 1)), ...head(), fx.reasoning(iso(9, 2))]);
    const r = await pass();
    expect(r.events).toHaveLength(1);
    expect(r.unreadableByDate).toEqual({ [localDate(Date.parse(iso(9, 1)))]: 1 });
  });
});

describe('apply_patch edits', () => {
  const entity = (rel: string): string => hashPath(path.posix.join(CWD, rel), SALT);

  it('an apply_patch custom tool call is one edit per file with its line counts', async () => {
    write(rollout(), [...head(), fx.customToolCall(iso(9, 1), 'apply_patch', fx.TWO_FILE_PATCH)]);
    const r = await pass();
    expect(r.events.map((e) => [e.kind, e.linesAdded, e.linesRemoved, e.entity])).toEqual([
      ['edit', 2, 1, entity('src/a.ts')],
      ['edit', 3, 0, entity('src/new.ts')],
    ]);
    expect(new Set(r.events.map((e) => e.id)).size).toBe(2);
  });

  it('a patch quoted inside an exec call counts the same', async () => {
    write(rollout(), [...head(), fx.customToolCall(iso(9, 1), 'exec', fx.execWithPatch(fx.TWO_FILE_PATCH))]);
    const r = await pass();
    expect(r.events.map((e) => [e.kind, e.linesAdded, e.linesRemoved, e.entity])).toEqual([
      ['edit', 2, 1, entity('src/a.ts')],
      ['edit', 3, 0, entity('src/new.ts')],
    ]);
  });

  it('an absolute patch path is hashed as is', async () => {
    const patch = '*** Begin Patch\n*** Add File: /fixture/repos/alpha/abs.ts\n+x\n*** End Patch';
    write(rollout(), [...head(), fx.customToolCall(iso(9, 1), 'apply_patch', patch)]);
    const [ev] = (await pass()).events;
    expect(ev.entity).toBe(hashPath('/fixture/repos/alpha/abs.ts', SALT));
  });
});

describe('day bucketing', () => {
  it('a thread running across midnight lands each record on its own local day', async () => {
    write(rollout(), [
      ...head(S_MAIN, 'user', CWD, iso(23, 50)),
      fx.humanPrompt(iso(23, 55), 1),
      fx.reasoning(iso(23, 58)),
      fx.assistant(iso(0, 3, 0, 11)),
      fx.humanPrompt(iso(0, 9, 0, 11), 2),
    ]);
    const r = await pass();
    const d10 = localDate(new Date(2026, 5, 10, 23, 55).getTime());
    const d11 = localDate(new Date(2026, 5, 11, 0, 3).getTime());
    expect(r.touchedDates).toEqual([d10, d11]);
    expect(kinds(readAgentDay(data, d10))).toEqual(['prompt', 'activity']);
    expect(kinds(readAgentDay(data, d11))).toEqual(['activity', 'prompt']);
  });
});

describe('offsets', () => {
  const long = (): fx.Line[] => [
    ...head(S_MAIN, 'user'),
    ...Array.from({ length: 6 }, (_, i) => [fx.humanPrompt(iso(10, i * 2), i + 1), fx.reasoning(iso(10, i * 2 + 1))]).flat(),
    fx.sessionMeta(iso(11, 0), { sessionId: S_GUARD, threadSource: 'guardian_review', cwd: CWD }),
    fx.humanPrompt(iso(11, 1), 7),
    fx.reasoning(iso(11, 2)),
  ];

  it('resumes mid-file in small passes with the segment it left off in, nothing lost or doubled', async () => {
    const f = write(rollout(), long());
    let more = true;
    let passes = 0;
    for (; passes < 100 && more; passes++) more = (await pass({ maxBytes: 200 })).more;
    expect(passes).toBeGreaterThan(3);
    const all = spooled();
    expect(kinds(all)).toEqual([...Array(6).fill(['prompt', 'activity']).flat(), 'activity']);
    expect(new Set(all.map((e) => e.id)).size).toBe(13);
    const off = readOffsets(stateDir(data))[fs.realpathSync(f)];
    expect(off.offset).toBe(fs.statSync(f).size);
    expect(off.codex).toEqual({ stream: codexStreamId(fs.realpathSync(f), S_GUARD), threadSource: 'guardian_review', cwd: CWD });
  });

  it('reads only what was appended, in the segment already in force', async () => {
    const f = write(rollout(), [...head(), fx.humanPrompt(iso(9, 1), 1)]);
    expect(kinds((await pass()).events)).toEqual(['prompt']);
    expect((await pass()).events).toEqual([]);
    append(f, [fx.reasoning(iso(9, 2)), fx.humanPrompt(iso(9, 3), 2)]);
    const r = await pass();
    expect(kinds(r.events)).toEqual(['activity', 'prompt']);
    expect(r.events[1].stream).toBe(codexStreamId(fs.realpathSync(f), S_MAIN));
  });

  it('an offset saved without the segment (or a rollout older than the install) rebuilds it from the file', async () => {
    const f = write(rollout(), [...head(S_MAIN, 'user', CWD_B), fx.humanPrompt(iso(9, 1), 1)]);
    const past = new Date('2019-06-01T00:00:00Z');
    fs.utimesSync(f, past, past);
    expect((await pass()).events).toEqual([]); // older than the install: starts at its size
    const real = fs.realpathSync(f);
    expect(readOffsets(stateDir(data))[real].codex).toBeUndefined();
    append(f, [fx.humanPrompt(iso(9, 5), 2)]);
    const r = await pass();
    expect(r.events.map((e) => [e.kind, e.stream, e.project])).toEqual([
      ['prompt', codexStreamId(real, S_MAIN), resolveProject(CWD_B).key],
    ]);
    // and an entry that lost its segment the same way
    const offs = readOffsets(stateDir(data));
    delete offs[real].codex;
    writeOffsets(stateDir(data), offs);
    append(f, [fx.humanPrompt(iso(9, 9), 3)]);
    expect(kinds((await pass()).events)).toEqual(['prompt']);
  });

  it('keeps a partial trailing line for the next pass and counts malformed lines as unreadable', async () => {
    const f = rollout();
    fs.mkdirSync(path.dirname(f), { recursive: true });
    const tail = JSON.stringify(fx.reasoning(iso(9, 4)));
    fs.writeFileSync(f, fx.toJsonl([...head(), fx.reasoning(iso(9, 1))]) + '{not json\n' + tail.slice(0, 20));
    const r1 = await pass();
    expect(r1.events).toHaveLength(1);
    expect(Object.values(r1.unreadableByDate)).toEqual([1]);
    fs.appendFileSync(f, tail.slice(20) + '\n');
    const r2 = await pass();
    expect(r2.events).toHaveLength(1);
    expect(r2.unreadableByDate).toEqual({});
  });

  it('drops records from before the install', async () => {
    write(rollout(), [...head(), fx.reasoning('2026-02-01T00:00:00.000Z'), fx.reasoning(iso(9, 1))]);
    const r = await pass({ installed: { installedAt: '2026-03-01T00:00:00.000Z', salt: SALT } });
    expect(r.events).toHaveLength(1);
  });
});

describe('what is stored', () => {
  it('no message text, instructions, credentials, account ids, session ids or paths reach the spool', async () => {
    write(rollout(), [
      ...head(),
      fx.injectedUser(iso(9, 0, 30)),
      fx.humanPrompt(iso(9, 1), 1),
      fx.reasoning(iso(9, 2)),
      fx.assistant(iso(9, 3)),
      fx.agentMessage(iso(9, 4)),
      fx.customToolCall(iso(9, 5), 'apply_patch', fx.TWO_FILE_PATCH),
      fx.customToolCallOutput(iso(9, 6)),
      fx.itemCompleted(iso(9, 7), 'AgentMessage'),
    ]);
    await pass();
    const dir = path.join(data, 'agents');
    const raw = fs.readdirSync(dir).map((f) => fs.readFileSync(path.join(dir, f), 'utf8')).join('');
    for (const secret of ['FIXTURE', 'fixture-user', 'fixture-account', S_MAIN, 'src/a.ts', 'src/new.ts', CWD, 'export const', 'Codex Desktop']) {
      expect(raw).not.toContain(secret);
    }
    const keys = new Set(raw.split('\n').filter(Boolean).flatMap((l) => Object.keys(JSON.parse(l))));
    expect([...keys].sort()).toEqual(['agent', 'entity', 'id', 'kind', 'linesAdded', 'linesRemoved', 'project', 'stream', 'time', 'v']);
  });
});

describe('Codex homes', () => {
  const mk = (dir: string, sessions = true): string => {
    fs.mkdirSync(sessions ? path.join(dir, 'sessions') : dir, { recursive: true });
    return dir;
  };

  it('uses CODEX_HOME and ~/.codex, needs sessions/, and de-duplicates by real path', () => {
    const h = path.join(root, 'userhome');
    mk(path.join(h, '.codex'));
    const other = mk(path.join(root, 'codex-elsewhere'));
    expect(discoverCodexHomes({ homeDir: h, env: {} })).toHaveLength(1);
    expect(discoverCodexHomes({ homeDir: h, env: { CODEX_HOME: other } })).toHaveLength(2);
    expect(discoverCodexHomes({ homeDir: h, env: { CODEX_HOME: path.join(h, '.codex') } })).toHaveLength(1);
    const bare = path.join(root, 'bare');
    mk(path.join(bare, '.codex'), false);
    expect(discoverCodexHomes({ homeDir: bare, env: {} })).toEqual([]);
  });

  it('reads sessions/** rollouts only, never archived_sessions or other files', async () => {
    write(rollout(), [...head(), fx.reasoning(iso(9, 1))]);
    write(path.join(home, 'archived_sessions', 'rollout-archived.jsonl'), [...head(), fx.reasoning(iso(9, 2))]);
    write(path.join(home, 'sessions', '2026', '06', '10', 'notes.jsonl'), [...head(), fx.reasoning(iso(9, 3))]);
    const r = await readCodexPass({ dir: data, installed: oldInstall, homeDir: root, env: {} });
    expect(r.events).toHaveLength(1);
  });
});

describe('readAgentsPass', () => {
  it('reads Claude transcripts and Codex rollouts in one pass, sharing offsets.json', async () => {
    const claudeHome = path.join(root, '.claude-x');
    const t = iso(9, 1);
    const claudeFile = path.join(claudeHome, 'projects', 'p', 's.jsonl');
    fs.mkdirSync(path.dirname(claudeFile), { recursive: true });
    fs.writeFileSync(claudeFile, JSON.stringify({ type: 'assistant', uuid: 'c-1', timestamp: t, cwd: CWD, message: {} }) + '\n');
    const codexFile = write(rollout(), [...head(), fx.reasoning(iso(9, 2)), '{bad' as unknown as fx.Line]);
    const r = await readAgentsPass({ dir: data, installed: oldInstall, homes: [claudeHome], codexHomes: [home] });
    expect(r.events.map((e) => e.agent).sort()).toEqual(['claude', 'codex']);
    expect(r.touchedDates).toEqual([localDate(Date.parse(t))]);
    const offs = readOffsets(stateDir(data));
    expect(Object.keys(offs).sort()).toEqual([fs.realpathSync(claudeFile), fs.realpathSync(codexFile)].sort());
    expect(spooled().map((e) => e.agent).sort()).toEqual(['claude', 'codex']);
  });
});
