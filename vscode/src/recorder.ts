/**
 * Heartbeat recorder. Turns editor events into spool heartbeats.
 *
 * Takes its event sources, spool, project resolver, clock, timers and salt by injection,
 * so it never imports `vscode` and tests drive it with the stub's emitters.
 *
 * Line-delta convention (pinned by tests): for each content change,
 *   removed = range.end.line - range.start.line
 *   added   = number of '\n' in text
 *   human_line_changes = sum(removed + added) over the changes in the heartbeat.
 * So typing inside one line is 0 lines (still an `edit` heartbeat: activity), pressing
 * Enter is 1, deleting three whole lines is 3, and replacing two lines with four is 2 + 3.
 *
 * Reload rule: see `docs/spike-reload.md`. Measured, not guessed.
 */
import type { ResolvedProject } from './project';
import { hashPath } from './store/hash';
import type { Heartbeat, HeartbeatKind } from './types';

interface Disposable {
  dispose(): void;
}
type Subscribe<T> = (listener: (e: T) => void) => Disposable;

export interface RecUri {
  scheme: string;
  fsPath: string;
  toString(): string;
}
export interface RecDocument {
  uri: RecUri;
  languageId: string;
  lineCount: number;
  isDirty: boolean;
  version: number;
}
export interface RecChange {
  range: { start: { line: number }; end: { line: number } };
  text: string;
}
export interface RecChangeEvent {
  document: RecDocument;
  contentChanges: readonly RecChange[];
  reason?: number;
}

/** Mirrors `vscode.TextDocumentChangeReason`. */
export const REASON_UNDO = 1;
export const REASON_REDO = 2;

export interface RecorderHost {
  onDidChangeTextDocument: Subscribe<RecChangeEvent>;
  onDidSaveTextDocument: Subscribe<RecDocument>;
  onDidChangeActiveTextEditor: Subscribe<{ document: RecDocument } | undefined>;
  onDidChangeTextEditorSelection: Subscribe<{ textEditor: { document: RecDocument } }>;
  onDidChangeWindowState: Subscribe<{ focused: boolean }>;
  /** Current `window.state.focused`. */
  isFocused(): boolean;
}

export interface RecorderTimers {
  setTimeout(fn: () => void, ms: number): unknown;
  clearTimeout(handle: unknown): void;
}

export interface RecorderDeps {
  host: RecorderHost;
  spool: { append(hb: Heartbeat): void };
  resolve: (fsPath: string) => ResolvedProject;
  /** Epoch milliseconds. */
  now: () => number;
  salt: string;
  timers?: RecorderTimers;
  /** Passed to `hashPath`; defaults to the real platform. */
  platform?: NodeJS.Platform;
}

export const THROTTLE_MS = 2 * 60 * 1000;
export const EDIT_DEBOUNCE_MS = 10_000;
export const RELOAD_WINDOW_MS = 500;
export const NO_PROJECT_KEY = 'none';

/** Lines removed plus lines added across a set of content changes (see the convention above). */
export function lineDelta(changes: readonly RecChange[]): number {
  let n = 0;
  for (const c of changes) {
    n += Math.max(0, c.range.end.line - c.range.start.line);
    for (let i = 0; i < c.text.length; i++) if (c.text.charCodeAt(i) === 10) n++;
  }
  return n;
}

interface Candidate {
  version: number;
  time: number;
  doc: RecDocument;
  lines: number;
  timer: unknown;
}

interface PendingEdit {
  doc: RecDocument;
  lines: number;
  lastTime: number;
  timer: unknown;
}

export class Recorder {
  private readonly deps: RecorderDeps;
  private readonly timers: RecorderTimers;
  private focused: boolean;
  private readonly subs: Disposable[] = [];
  private readonly lastBeat = new Map<string, number>();
  private readonly candidates = new Map<string, Candidate>();
  private readonly edits = new Map<string, PendingEdit>();

  constructor(deps: RecorderDeps) {
    this.deps = deps;
    this.timers = deps.timers ?? {
      setTimeout: (fn, ms) => {
        const h = setTimeout(fn, ms);
        h.unref?.();
        return h;
      },
      clearTimeout: (h) => clearTimeout(h as ReturnType<typeof setTimeout>),
    };
    const host = deps.host;
    this.focused = host.isFocused();
    this.subs.push(
      host.onDidChangeWindowState((s) => {
        this.focused = s.focused;
      }),
      host.onDidChangeActiveTextEditor((ed) => {
        if (ed) this.onThrottled(ed.document, 'focus');
      }),
      host.onDidChangeTextEditorSelection((e) => this.onThrottled(e.textEditor.document, 'nav')),
      host.onDidSaveTextDocument((doc) => this.onThrottled(doc, 'save')),
      host.onDidChangeTextDocument((e) => this.onChange(e)),
    );
  }

  /** Flush pending edits and unresolved candidates (as reloads), then stop listening. */
  dispose(): void {
    for (const s of this.subs) s.dispose();
    this.subs.length = 0;
    for (const key of [...this.candidates.keys()]) this.settleAsReload(key);
    for (const key of [...this.edits.keys()]) this.flushEdit(key);
  }

  private static counts(doc: RecDocument): boolean {
    return doc.uri.scheme === 'file' || doc.uri.scheme === 'untitled';
  }

  private emit(
    doc: RecDocument,
    kind: HeartbeatKind,
    timeMs: number,
    lines: number,
    isWrite: boolean,
    reload: boolean,
  ): void {
    const fsPath = doc.uri.fsPath;
    let project: string = NO_PROJECT_KEY;
    let branch: string | null = null;
    if (doc.uri.scheme === 'file') {
      const r = this.deps.resolve(fsPath);
      project = r.key;
      branch = r.branch;
    }
    this.deps.spool.append({
      v: 1,
      time: timeMs / 1000,
      entity: hashPath(fsPath, this.deps.salt, this.deps.platform),
      type: 'file',
      category: 'coding',
      kind,
      project,
      branch,
      language: doc.languageId,
      lines: doc.lineCount,
      human_line_changes: reload ? 0 : lines,
      is_write: isWrite,
      reload,
    });
  }

  /** focus, nav and save: one per file per kind per 2 minutes. */
  private onThrottled(doc: RecDocument, kind: 'focus' | 'nav' | 'save'): void {
    if (!this.focused || !Recorder.counts(doc)) return;
    const now = this.deps.now();
    const key = `${kind}\u0000${doc.uri.toString()}`;
    const last = this.lastBeat.get(key);
    if (last !== undefined && now - last < THROTTLE_MS) return;
    this.lastBeat.set(key, now);
    this.emit(doc, kind, now, 0, kind === 'save', false);
  }

  private onChange(e: RecChangeEvent): void {
    const doc = e.document;
    if (!Recorder.counts(doc)) return;
    const key = doc.uri.toString();
    const empty = e.contentChanges.length === 0;

    // Step 4's second event settles any pending candidate, even while unfocused.
    const cand = this.candidates.get(key);
    if (cand) {
      if (empty && doc.isDirty && doc.version === cand.version) {
        this.dropCandidate(key);
        this.addEdit(cand.doc, cand.lines, cand.time);
        return;
      }
      this.settleAsReload(key);
    }

    if (empty || !this.focused) return;

    const lines = lineDelta(e.contentChanges);
    const now = this.deps.now();
    if (e.reason === REASON_UNDO || e.reason === REASON_REDO || doc.isDirty) {
      this.addEdit(doc, lines, now);
      return;
    }
    const c: Candidate = {
      version: doc.version,
      time: now,
      doc,
      lines,
      timer: this.timers.setTimeout(() => this.settleAsReload(key), RELOAD_WINDOW_MS),
    };
    this.candidates.set(key, c);
  }

  private dropCandidate(key: string): Candidate | undefined {
    const c = this.candidates.get(key);
    if (!c) return undefined;
    this.timers.clearTimeout(c.timer);
    this.candidates.delete(key);
    return c;
  }

  private settleAsReload(key: string): void {
    const c = this.dropCandidate(key);
    if (c) this.emit(c.doc, 'edit', c.time, 0, false, true);
  }

  /** Accumulate into the per-file 10 second window; one heartbeat per window. */
  private addEdit(doc: RecDocument, lines: number, timeMs: number): void {
    const key = doc.uri.toString();
    const cur = this.edits.get(key);
    if (cur) {
      cur.doc = doc;
      cur.lines += lines;
      cur.lastTime = timeMs;
      return;
    }
    this.edits.set(key, {
      doc,
      lines,
      lastTime: timeMs,
      timer: this.timers.setTimeout(() => this.flushEdit(key), EDIT_DEBOUNCE_MS),
    });
  }

  private flushEdit(key: string): void {
    const p = this.edits.get(key);
    if (!p) return;
    this.timers.clearTimeout(p.timer);
    this.edits.delete(key);
    this.emit(p.doc, 'edit', p.lastTime, p.lines, false, false);
  }
}
