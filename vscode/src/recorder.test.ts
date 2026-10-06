import * as fs from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import * as vscode from 'vscode';
import {
  EDIT_DEBOUNCE_MS,
  RELOAD_WINDOW_MS,
  THROTTLE_MS,
  Recorder,
  lineDelta,
  type RecChange,
  type RecDocument,
  type RecorderHost,
} from './recorder';
import type { ResolvedProject } from './project';
import { hashPath } from './store/hash';
import { Spool, machineLabel, pruneSpool, readSpoolDay, windowIdFor } from './store/spool';
import type { Heartbeat } from './types';

const SALT = 'fixture-salt';
const PLATFORM = 'linux' as const;
const T0 = Date.UTC(2026, 0, 15, 12, 0, 0);

/** Deterministic clock plus timers: `advance` runs due timers in order. */
class FakeTime {
  now = T0;
  private seq = 0;
  private pending = new Map<number, { at: number; fn: () => void }>();
  timers = {
    setTimeout: (fn: () => void, ms: number): unknown => {
      const id = ++this.seq;
      this.pending.set(id, { at: this.now + ms, fn });
      return id;
    },
    clearTimeout: (h: unknown): void => {
      this.pending.delete(h as number);
    },
  };
  advance(ms: number): void {
    const target = this.now + ms;
    for (;;) {
      let next: [number, { at: number; fn: () => void }] | undefined;
      for (const e of this.pending) if (e[1].at <= target && (!next || e[1].at < next[1].at)) next = e;
      if (!next) break;
      this.pending.delete(next[0]);
      this.now = next[1].at;
      next[1].fn();
    }
    this.now = target;
  }
}

const host: RecorderHost = {
  onDidChangeTextDocument: vscode.workspace.onDidChangeTextDocument as RecorderHost['onDidChangeTextDocument'],
  onDidSaveTextDocument: vscode.workspace.onDidSaveTextDocument as RecorderHost['onDidSaveTextDocument'],
  onDidChangeActiveTextEditor: vscode.window.onDidChangeActiveTextEditor as RecorderHost['onDidChangeActiveTextEditor'],
  onDidChangeTextEditorSelection:
    vscode.window.onDidChangeTextEditorSelection as RecorderHost['onDidChangeTextEditorSelection'],
  onDidChangeWindowState: vscode.window.onDidChangeWindowState as RecorderHost['onDidChangeWindowState'],
  isFocused: () => vscode.window.state.focused,
};

const doc = (p: string, over: Partial<RecDocument> = {}, scheme = 'file'): RecDocument => ({
  uri: { scheme, fsPath: p, toString: () => `${scheme}://${p}` },
  languageId: 'typescript',
  lineCount: 10,
  isDirty: false,
  version: 1,
  ...over,
});

const change = (startLine: number, endLine: number, text: string): RecChange => ({
  range: { start: { line: startLine }, end: { line: endLine } },
  text,
});

const ws = vscode.workspace as unknown as { fireChange(e: unknown): void; fireSave(e: unknown): void };
const win = vscode.window as unknown as {
  fireWindowState(f: boolean): void;
  fireActiveEditor(e: unknown): void;
  fireSelection(e: unknown): void;
};
const fire = (d: RecDocument, changes: RecChange[], reason?: number): void =>
  ws.fireChange({ document: d, contentChanges: changes, reason });
const fireSave = (d: RecDocument): void => ws.fireSave(d);

const resolver =
  (map: Record<string, string>) =>
  (fsPath: string): ResolvedProject => {
    const root = Object.keys(map).find((r) => fsPath.startsWith(r));
    return { key: root ? map[root] : 'f'.repeat(16), realName: 'fixture', root: root ?? '/fixture', branch: 'main' };
  };

let time: FakeTime;
let beats: Heartbeat[];
let rec: Recorder;

function start(resolve = resolver({ '/fixture/alpha': 'a'.repeat(16) })): void {
  rec = new Recorder({
    host,
    spool: { append: (hb) => beats.push(hb) },
    resolve,
    now: () => time.now,
    salt: SALT,
    timers: time.timers,
    platform: PLATFORM,
  });
}

beforeEach(() => {
  time = new FakeTime();
  beats = [];
  vscode.window.state.focused = true;
  start();
});
afterEach(() => rec.dispose());

describe('lineDelta convention', () => {
  it('counts a multi-line insert as the newlines in text', () => {
    expect(lineDelta([change(4, 4, 'a\nb\nc\n')])).toBe(3);
  });
  it('counts a deletion as the range line span', () => {
    expect(lineDelta([change(2, 5, '')])).toBe(3);
  });
  it('counts a replace as removed plus added', () => {
    expect(lineDelta([change(2, 4, 'x\ny\nz\nw')])).toBe(2 + 3);
  });
  it('counts typing inside one line as 0, and Enter as 1', () => {
    expect(lineDelta([change(3, 3, 'abc')])).toBe(0);
    expect(lineDelta([change(3, 3, '\n')])).toBe(1);
  });
  it('sums several changes in one event', () => {
    expect(lineDelta([change(0, 1, ''), change(5, 5, '\n\n')])).toBe(3);
  });
});

describe('focus gating', () => {
  it('records nothing while the window is unfocused', () => {
    win.fireWindowState(false);
    const d = doc('/fixture/alpha/a.ts', { isDirty: true });
    fire(d, [change(1, 1, '\n')]);
    fireSave(d);
    win.fireActiveEditor({ document: d });
    win.fireSelection({ textEditor: { document: d } });
    time.advance(EDIT_DEBOUNCE_MS * 2);
    expect(beats).toEqual([]);
  });

  it('resumes when focus returns', () => {
    win.fireWindowState(false);
    win.fireWindowState(true);
    win.fireActiveEditor({ document: doc('/fixture/alpha/a.ts') });
    expect(beats).toHaveLength(1);
  });

  it('does not record a candidate that arrives while unfocused', () => {
    win.fireWindowState(false);
    fire(doc('/fixture/alpha/a.ts'), [change(0, 0, 'x\n')]);
    time.advance(RELOAD_WINDOW_MS * 2);
    expect(beats).toEqual([]);
  });

  it('starts from the host focus state', () => {
    rec.dispose();
    vscode.window.state.focused = false;
    start();
    win.fireActiveEditor({ document: doc('/fixture/alpha/a.ts') });
    expect(beats).toEqual([]);
  });
});

describe('schemes and projects', () => {
  it('puts untitled documents in the none bucket with no branch', () => {
    win.fireActiveEditor({ document: doc('Untitled-1', {}, 'untitled') });
    expect(beats).toHaveLength(1);
    expect(beats[0].project).toBe('none');
    expect(beats[0].branch).toBeNull();
  });

  it('ignores output, git and vscode-userdata documents', () => {
    for (const scheme of ['output', 'git', 'vscode-userdata', 'vscode-remote']) {
      const d = doc('/fixture/alpha/x', { isDirty: true }, scheme);
      win.fireActiveEditor({ document: d });
      win.fireSelection({ textEditor: { document: d } });
      fireSave(d);
      fire(d, [change(0, 0, '\n')]);
    }
    time.advance(EDIT_DEBOUNCE_MS * 2);
    expect(beats).toEqual([]);
  });

  it('routes multi-root files to their own project keys', () => {
    rec.dispose();
    beats = [];
    start(resolver({ '/fixture/alpha': 'a'.repeat(16), '/fixture/beta': 'b'.repeat(16) }));
    win.fireActiveEditor({ document: doc('/fixture/alpha/src/a.ts') });
    win.fireActiveEditor({ document: doc('/fixture/beta/src/b.ts') });
    expect(beats.map((b) => b.project)).toEqual(['a'.repeat(16), 'b'.repeat(16)]);
  });
});

describe('heartbeat shape', () => {
  it('fills every spec field, hashes the entity, and never writes ai_line_changes', () => {
    const d = doc('/fixture/alpha/src/a.ts', { lineCount: 42, languageId: 'python' });
    fireSave(d);
    expect(beats[0]).toEqual({
      v: 1,
      time: T0 / 1000,
      entity: hashPath('/fixture/alpha/src/a.ts', SALT, PLATFORM),
      type: 'file',
      category: 'coding',
      kind: 'save',
      project: 'a'.repeat(16),
      branch: 'main',
      language: 'python',
      lines: 42,
      human_line_changes: 0,
      is_write: true,
      reload: false,
    });
    expect(beats[0]).not.toHaveProperty('ai_line_changes');
  });

  it('maps each event to its kind', () => {
    const d = doc('/fixture/alpha/a.ts');
    win.fireActiveEditor({ document: d });
    win.fireSelection({ textEditor: { document: d } });
    fireSave(d);
    expect(beats.map((b) => [b.kind, b.is_write])).toEqual([
      ['focus', false],
      ['nav', false],
      ['save', true],
    ]);
  });
});

describe('throttle and debounce', () => {
  it('throttles focus, nav and save to one per file per 2 minutes', () => {
    const d = doc('/fixture/alpha/a.ts');
    for (let i = 0; i < 5; i++) {
      win.fireSelection({ textEditor: { document: d } });
      time.advance(10_000);
    }
    expect(beats).toHaveLength(1);
    time.advance(THROTTLE_MS);
    win.fireSelection({ textEditor: { document: d } });
    expect(beats).toHaveLength(2);
  });

  it('throttles per file, not globally', () => {
    win.fireSelection({ textEditor: { document: doc('/fixture/alpha/a.ts') } });
    win.fireSelection({ textEditor: { document: doc('/fixture/alpha/b.ts') } });
    expect(beats).toHaveLength(2);
  });

  it('debounces edits per file into one heartbeat per 10 seconds with summed deltas', () => {
    const d = doc('/fixture/alpha/a.ts', { isDirty: true });
    fire(d, [change(1, 1, '\n')]);
    time.advance(3000);
    fire(d, [change(2, 4, '')]);
    time.advance(3000);
    fire(d, [change(5, 5, 'abc')]);
    expect(beats).toEqual([]);
    time.advance(EDIT_DEBOUNCE_MS);
    expect(beats).toHaveLength(1);
    expect(beats[0]).toMatchObject({ kind: 'edit', human_line_changes: 3, reload: false, is_write: false });
    // A new window starts afterwards.
    fire(d, [change(0, 0, '\n')]);
    time.advance(EDIT_DEBOUNCE_MS);
    expect(beats).toHaveLength(2);
    expect(beats[1].human_line_changes).toBe(1);
  });

  it('debounces each file separately', () => {
    fire(doc('/fixture/alpha/a.ts', { isDirty: true }), [change(0, 0, '\n')]);
    fire(doc('/fixture/alpha/b.ts', { isDirty: true }), [change(0, 0, '\n')]);
    time.advance(EDIT_DEBOUNCE_MS);
    expect(beats).toHaveLength(2);
  });

  it('flushes a pending edit on dispose', () => {
    fire(doc('/fixture/alpha/a.ts', { isDirty: true }), [change(0, 0, '\n')]);
    rec.dispose();
    expect(beats).toHaveLength(1);
  });
});

describe('reload rule (docs/spike-reload.md)', () => {
  const a = '/fixture/alpha/a.ts';

  it('(a) first edit after clean: candidate plus same-version dirty flip is your edit, with lines', () => {
    fire(doc(a, { isDirty: false, version: 2 }), [change(1, 1, 'x\ny\n')]);
    fire(doc(a, { isDirty: true, version: 2 }), []);
    time.advance(RELOAD_WINDOW_MS * 2 + EDIT_DEBOUNCE_MS);
    expect(beats).toHaveLength(1);
    expect(beats[0]).toMatchObject({ kind: 'edit', reload: false, human_line_changes: 2 });
  });

  it('(a) later keystrokes with isDirty true are edits at once', () => {
    fire(doc(a, { isDirty: true, version: 3 }), [change(1, 1, '\n')]);
    fire(doc(a, { isDirty: true, version: 4 }), [change(2, 2, '\n')]);
    time.advance(EDIT_DEBOUNCE_MS);
    expect(beats).toHaveLength(1);
    expect(beats[0]).toMatchObject({ reload: false, human_line_changes: 2 });
  });

  it('(b) a reload: isDirty false with no follow-up in 500 ms is reload true with 0 lines', () => {
    fire(doc(a, { isDirty: false, version: 8 }), [change(3, 3, 'appended\nline\n')]);
    expect(beats).toEqual([]);
    time.advance(RELOAD_WINDOW_MS - 1);
    expect(beats).toEqual([]);
    time.advance(1);
    expect(beats).toHaveLength(1);
    expect(beats[0]).toMatchObject({ kind: 'edit', reload: true, human_line_changes: 0, is_write: false });
    time.advance(EDIT_DEBOUNCE_MS * 2);
    expect(beats).toHaveLength(1);
  });

  it('a pure dirty flip with no pending candidate writes nothing', () => {
    fire(doc(a, { isDirty: false }), []);
    fire(doc(a, { isDirty: true }), []);
    time.advance(EDIT_DEBOUNCE_MS * 2);
    expect(beats).toEqual([]);
  });

  it('(d) undo to saved: reason Undo with isDirty true is your edit', () => {
    fire(doc(a, { isDirty: true, version: 20 }), [change(0, 1, '')], 1);
    fire(doc(a, { isDirty: false, version: 20 }), []);
    time.advance(RELOAD_WINDOW_MS * 2 + EDIT_DEBOUNCE_MS);
    expect(beats).toHaveLength(1);
    expect(beats[0]).toMatchObject({ reload: false, human_line_changes: 1 });
  });

  it('(d) redo from saved: reason Redo with isDirty false is never a reload', () => {
    fire(doc(a, { isDirty: false, version: 21 }), [change(0, 0, 'x\n')], 2);
    fire(doc(a, { isDirty: true, version: 21 }), []);
    time.advance(RELOAD_WINDOW_MS * 2 + EDIT_DEBOUNCE_MS);
    expect(beats).toHaveLength(1);
    expect(beats[0]).toMatchObject({ reload: false, human_line_changes: 1 });
  });

  it('(h) typing 90 ms after a reload: both classified correctly', () => {
    fire(doc(a, { isDirty: false, version: 41 }), [change(0, 0, 'ten chars\n')]);
    time.advance(90);
    // First keystroke after the reload: clean state, then the flip at the same version.
    fire(doc(a, { isDirty: false, version: 42 }), [change(0, 0, 'z')]);
    fire(doc(a, { isDirty: true, version: 42 }), []);
    fire(doc(a, { isDirty: true, version: 43 }), [change(0, 0, '\n')]);
    time.advance(EDIT_DEBOUNCE_MS + RELOAD_WINDOW_MS);
    expect(beats).toHaveLength(2);
    expect(beats[0]).toMatchObject({ reload: true, human_line_changes: 0 });
    expect(beats[1]).toMatchObject({ reload: false, human_line_changes: 1 });
  });

  it('a candidate followed by a different-version event is a reload', () => {
    fire(doc(a, { isDirty: false, version: 5 }), [change(0, 0, 'x\n')]);
    fire(doc(a, { isDirty: true, version: 6 }), []);
    time.advance(EDIT_DEBOUNCE_MS);
    expect(beats).toHaveLength(1);
    expect(beats[0].reload).toBe(true);
  });

  it('keeps candidates per document', () => {
    const b = '/fixture/alpha/b.ts';
    fire(doc(a, { isDirty: false, version: 1 }), [change(0, 0, 'x\n')]);
    fire(doc(b, { isDirty: false, version: 1 }), [change(0, 0, 'y\n')]);
    fire(doc(a, { isDirty: true, version: 1 }), []);
    time.advance(RELOAD_WINDOW_MS);
    expect(beats.filter((h) => h.reload)).toHaveLength(1);
    expect(beats.find((h) => h.reload)?.entity).toBe(hashPath(b, SALT, PLATFORM));
  });

  it('writes a reload tag with zero lines even for a big change', () => {
    fire(doc(a, { isDirty: false }), [change(0, 50, 'q\n'.repeat(80))]);
    time.advance(RELOAD_WINDOW_MS);
    expect(beats[0]).toMatchObject({ reload: true, human_line_changes: 0 });
  });
});

describe('spool', () => {
  let dir: string;
  beforeEach(() => {
    dir = fs.mkdtempSync(path.join(os.tmpdir(), 'sanduhr-spool-'));
  });
  afterEach(() => fs.rmSync(dir, { recursive: true, force: true }));

  const hb = (timeMs: number, entity = 'e'.repeat(24)): Heartbeat => ({
    v: 1,
    time: timeMs / 1000,
    entity,
    type: 'file',
    category: 'coding',
    kind: 'edit',
    project: 'a'.repeat(16),
    branch: null,
    language: 'ts',
    lines: 1,
    human_line_changes: 1,
    is_write: false,
    reload: false,
  });

  it('normalizes the machine label and window id', () => {
    expect(machineLabel('Fixture_PC.local')).toBe('fixture-pc-local');
    expect(windowIdFor('s')).toMatch(/^[0-9a-f]{8}$/);
    expect(windowIdFor('s')).not.toBe(windowIdFor('t'));
  });

  it('buffers appends until dispose', () => {
    const s = new Spool(dir, { machine: 'pc-one', windowId: 'aaaaaaaa', flushMs: 60_000 });
    const t = new Date(2026, 0, 15, 12).getTime();
    s.append(hb(t));
    expect(readSpoolDay(dir, '2026-01-15', 'pc-one')).toEqual([]);
    s.append(hb(t + 1000));
    s.dispose();
    expect(readSpoolDay(dir, '2026-01-15', 'pc-one')).toHaveLength(2);
  });

  it('flushes on its own every flushMs', async () => {
    const s = new Spool(dir, { machine: 'pc-one', windowId: 'aaaaaaaa', flushMs: 10 });
    s.append(hb(new Date(2026, 0, 15, 12).getTime()));
    await new Promise((r) => setTimeout(r, 100));
    expect(readSpoolDay(dir, '2026-01-15', 'pc-one')).toHaveLength(1);
    s.dispose();
  });

  it('reads only this machine, across all windows', () => {
    const t = new Date(2026, 0, 15, 12).getTime();
    const w1 = new Spool(dir, { machine: 'pc-one', windowId: 'aaaaaaaa' });
    const w2 = new Spool(dir, { machine: 'pc-one', windowId: 'bbbbbbbb' });
    const other = new Spool(dir, { machine: 'pc-two', windowId: 'cccccccc' });
    w1.append(hb(t, '1'.repeat(24)));
    w2.append(hb(t, '2'.repeat(24)));
    other.append(hb(t, '3'.repeat(24)));
    w1.dispose();
    w2.dispose();
    other.dispose();
    const got = readSpoolDay(dir, '2026-01-15', 'pc-one').map((h) => h.entity[0]);
    expect(got.sort()).toEqual(['1', '2']);
    expect(readSpoolDay(dir, '2026-01-15', 'pc-two').map((h) => h.entity[0])).toEqual(['3']);
    expect(readSpoolDay(dir, '2026-01-16', 'pc-one')).toEqual([]);
  });

  it('names files by the local date of each heartbeat around midnight', () => {
    const s = new Spool(dir, { machine: 'pc-one', windowId: 'aaaaaaaa' });
    s.append(hb(new Date(2026, 0, 15, 23, 59, 59).getTime()));
    s.append(hb(new Date(2026, 0, 16, 0, 0, 1).getTime()));
    s.dispose();
    expect(fs.readdirSync(path.join(dir, 'spool')).sort()).toEqual([
      '2026-01-15.pc-one.aaaaaaaa.jsonl',
      '2026-01-16.pc-one.aaaaaaaa.jsonl',
    ]);
  });

  it('files a recorder heartbeat under the local date of the edit, not of the flush', () => {
    rec.dispose();
    time.now = new Date(2026, 0, 15, 23, 59, 58).getTime();
    const s = new Spool(dir, { machine: 'pc-one', windowId: 'aaaaaaaa' });
    rec = new Recorder({
      host,
      spool: s,
      resolve: resolver({}),
      now: () => time.now,
      salt: SALT,
      timers: time.timers,
      platform: PLATFORM,
    });
    fire(doc('/fixture/alpha/a.ts', { isDirty: true }), [change(0, 0, '\n')]);
    time.advance(EDIT_DEBOUNCE_MS - 1); // still 23:59:58 + 9.999s, past midnight, edit time was 23:59:58
    time.advance(1);
    s.dispose();
    expect(readSpoolDay(dir, '2026-01-15', 'pc-one')).toHaveLength(1);
    expect(readSpoolDay(dir, '2026-01-16', 'pc-one')).toHaveLength(0);
  });

  it('terminates a torn last line before appending, and skips bad lines on read', () => {
    fs.mkdirSync(path.join(dir, 'spool'));
    const file = path.join(dir, 'spool', '2026-01-15.pc-one.aaaaaaaa.jsonl');
    fs.writeFileSync(file, '{"time": 1, "entity": "x');
    const s = new Spool(dir, { machine: 'pc-one', windowId: 'aaaaaaaa' });
    s.append(hb(new Date(2026, 0, 15, 12).getTime()));
    s.dispose();
    expect(readSpoolDay(dir, '2026-01-15', 'pc-one')).toHaveLength(1);
  });

  it('prunes files older than 90 days, from any machine', () => {
    fs.mkdirSync(path.join(dir, 'spool'));
    const now = new Date(2026, 5, 1, 12);
    const names = [
      '2026-01-01.pc-one.aaaaaaaa.jsonl',
      '2026-02-01.pc-two.bbbbbbbb.jsonl',
      '2026-03-05.pc-one.aaaaaaaa.jsonl',
      '2026-05-31.pc-one.aaaaaaaa.jsonl',
      'notes.txt',
    ];
    for (const n of names) fs.writeFileSync(path.join(dir, 'spool', n), '');
    const removed = pruneSpool(dir, now);
    expect(removed.sort()).toEqual(['2026-01-01.pc-one.aaaaaaaa.jsonl', '2026-02-01.pc-two.bbbbbbbb.jsonl']);
    expect(fs.readdirSync(path.join(dir, 'spool')).sort()).toEqual(names.slice(2).sort());
  });

  it('prune on a missing spool folder is a no-op', () => {
    expect(pruneSpool(dir)).toEqual([]);
  });
});
