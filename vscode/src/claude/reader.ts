/**
 * Incremental Claude transcript reader. Walks every config home's transcripts, turns
 * new lines into ClaudeEvents, appends them to the Claude spool and only then advances
 * the file's offset, so a crash re-reads (the spool de-duplicates by id) and never drops.
 * The runner calls `readPass` under the merge lock.
 */
import { createHash } from 'node:crypto';
import * as fs from 'node:fs';
import * as path from 'node:path';
import { resolve as resolveProject } from '../project';
import { appendClaudeEvents, localDate } from '../store/claudeSpool';
import { hashPath } from '../store/hash';
import { stateDir } from '../store/paths';
import { readOffsets, writeOffsets } from '../store/state';
import type { ClaudeEvent, InstalledState, OffsetsState } from '../types';
import { discoverHomes, type HomeOptions } from './homes';

export const DEFAULT_MAX_BYTES = 8 * 1024 * 1024;
const EXTEND_CHUNK = 1024 * 1024;

export interface ReadPassOptions extends HomeOptions {
  /** Data directory (holds `state/`, `claude/`). */
  dir: string;
  installed: InstalledState;
  /** Explicit homes; default is `discoverHomes`. */
  homes?: string[];
  /** Byte budget across all files for this pass. Default 8 MB. */
  maxBytes?: number;
}

export interface ReadPassResult {
  /** Events appended to the spool this pass. */
  events: ClaudeEvent[];
  /** True when the byte budget stopped the pass before every file was caught up. */
  more: boolean;
  /** Unreadable transcript lines, by local date. */
  unreadableByDate: Record<string, number>;
  /** Local dates that gained events. */
  touchedDates: string[];
}

/** Short stable id for a transcript file: hash of its real path. */
export function streamId(realPath: string, platform: NodeJS.Platform = process.platform): string {
  const p = platform === 'win32' ? realPath.toLowerCase() : realPath;
  return createHash('sha256').update(p.split('\\').join('/')).digest('hex').slice(0, 16);
}

function listJsonl(dir: string, recursive: boolean, out: string[]): void {
  let entries: fs.Dirent[];
  try {
    entries = fs.readdirSync(dir, { withFileTypes: true });
  } catch {
    return;
  }
  for (const e of entries) {
    const full = path.join(dir, e.name);
    if (e.isDirectory()) {
      if (recursive) listJsonl(full, true, out);
    } else if (e.name.endsWith('.jsonl')) {
      out.push(full);
    }
  }
}

/** Main sessions and subagent transcripts under one home, sorted for a stable order. */
export function listTranscripts(home: string): string[] {
  const files: string[] = [];
  let projects: fs.Dirent[];
  try {
    projects = fs.readdirSync(path.join(home, 'projects'), { withFileTypes: true });
  } catch {
    return files;
  }
  for (const proj of projects) {
    if (!proj.isDirectory()) continue;
    const projDir = path.join(home, 'projects', proj.name);
    let entries: fs.Dirent[];
    try {
      entries = fs.readdirSync(projDir, { withFileTypes: true });
    } catch {
      continue;
    }
    for (const e of entries) {
      const full = path.join(projDir, e.name);
      if (e.isFile() && e.name.endsWith('.jsonl')) files.push(full);
      else if (e.isDirectory()) listJsonl(path.join(full, 'subagents'), true, files);
    }
  }
  return files.sort();
}

type Json = Record<string, unknown>;
const isObj = (v: unknown): v is Json => typeof v === 'object' && v !== null && !Array.isArray(v);

function hasToolResult(content: unknown): boolean {
  return Array.isArray(content) && content.some((p) => isObj(p) && p.type === 'tool_result');
}

/** A line is your prompt only when every condition of the spec's prompt rule holds. */
export function isHumanPrompt(o: Json): boolean {
  if (o.type !== 'user') return false;
  const content = isObj(o.message) ? o.message.content : undefined;
  if (!(typeof content === 'string' || (Array.isArray(content) && !hasToolResult(content)))) return false;
  if (o.isSidechain === true || o.isMeta === true || o.isCompactSummary === true) return false;
  if (o.origin !== undefined && o.origin !== null) {
    if (!isObj(o.origin) || o.origin.kind !== 'human') return false;
  }
  return true;
}

/** Claude activity: every assistant line and every user line carrying a tool_result. */
export function isClaudeActivity(o: Json): boolean {
  if (o.type === 'assistant') return true;
  if (o.type !== 'user') return false;
  return hasToolResult(isObj(o.message) ? o.message.content : undefined);
}

function countLines(text: string): number {
  if (text === '') return 0;
  const n = text.split('\n').length;
  return text.endsWith('\n') ? n - 1 : n;
}

/** Added and removed line counts from a `toolUseResult` that has a `filePath`. */
export function countClaudeLines(r: Json): { added: number; removed: number } {
  if (r.type === 'create') {
    return { added: typeof r.content === 'string' ? countLines(r.content) : 0, removed: 0 };
  }
  let added = 0;
  let removed = 0;
  if (Array.isArray(r.structuredPatch)) {
    for (const hunk of r.structuredPatch) {
      if (!isObj(hunk) || !Array.isArray(hunk.lines)) continue;
      for (const l of hunk.lines) {
        if (typeof l !== 'string') continue;
        if (l.startsWith('+')) added++;
        else if (l.startsWith('-')) removed++;
      }
    }
  }
  return { added, removed };
}

/** Split a buffer into complete lines with their byte offsets (relative to the buffer). */
function splitLines(buf: Buffer): { start: number; end: number }[] {
  const out: { start: number; end: number }[] = [];
  let start = 0;
  for (;;) {
    const nl = buf.indexOf(0x0a, start);
    if (nl === -1) break;
    out.push({ start, end: nl });
    start = nl + 1;
  }
  return out;
}

interface FileCtx {
  stream: string;
  salt: string;
  installedMs: number;
  projectKeys: Map<string, string>;
  fallbackDate: string;
}

/** Read `[offset, offset+budget)` extended to a newline. Returns the bytes and whether any remain. */
function readChunk(fd: number, offset: number, size: number, budget: number): Buffer {
  const want = Math.min(Math.max(budget, 1), size - offset);
  let buf = Buffer.alloc(want);
  let got = fs.readSync(fd, buf, 0, want, offset);
  buf = buf.subarray(0, got);
  // A line longer than the budget must still be read whole, or the pass would never advance.
  while (buf.indexOf(0x0a) === -1 && offset + got < size) {
    const more = Math.min(EXTEND_CHUNK, size - offset - got);
    const extra = Buffer.alloc(more);
    const n = fs.readSync(fd, extra, 0, more, offset + got);
    if (n <= 0) break;
    buf = Buffer.concat([buf, extra.subarray(0, n)]);
    got += n;
  }
  if (buf.length > want) {
    // Extension overshoots in whole chunks; keep only through the end of the line that crossed the budget.
    const nl = buf.indexOf(0x0a, want - 1);
    if (nl !== -1) buf = buf.subarray(0, nl + 1);
  }
  return buf;
}

function processLines(
  buf: Buffer,
  baseOffset: number,
  ctx: FileCtx,
  lastCwd: { value?: string },
  unreadable: Record<string, number>,
  lastDate: { value: string },
): { events: ClaudeEvent[]; consumed: number } {
  const events: ClaudeEvent[] = [];
  const lines = splitLines(buf);
  const bump = (date: string): void => {
    unreadable[date] = (unreadable[date] ?? 0) + 1;
  };
  for (const { start, end } of lines) {
    const raw = buf.subarray(start, end).toString('utf8').trim();
    if (!raw) continue;
    let o: unknown;
    try {
      o = JSON.parse(raw);
    } catch {
      bump(lastDate.value);
      continue;
    }
    if (!isObj(o)) {
      bump(lastDate.value);
      continue;
    }
    const type = o.type;
    if (type !== 'user' && type !== 'assistant') {
      if (typeof o.cwd === 'string' && o.cwd) lastCwd.value = o.cwd;
      continue;
    }
    const ms = typeof o.timestamp === 'string' ? Date.parse(o.timestamp) : NaN;
    if (Number.isNaN(ms)) {
      bump(lastDate.value);
      continue;
    }
    const date = localDate(ms);
    lastDate.value = date;
    if (typeof o.cwd === 'string' && o.cwd) lastCwd.value = o.cwd;
    const cwd = lastCwd.value;
    if (!cwd) {
      bump(date);
      continue;
    }
    if (ms < ctx.installedMs) continue;

    const prompt = isHumanPrompt(o);
    if (!prompt && !isClaudeActivity(o)) continue;

    let project = ctx.projectKeys.get(cwd);
    if (project === undefined) {
      project = resolveProject(cwd).key;
      ctx.projectKeys.set(cwd, project);
    }
    const id = typeof o.uuid === 'string' && o.uuid ? o.uuid : `${ctx.stream}:${baseOffset + start}`;
    const ev: ClaudeEvent = { v: 1, id, time: ms / 1000, project, stream: ctx.stream, kind: prompt ? 'prompt' : 'activity' };

    const r = o.toolUseResult;
    if (!prompt && isObj(r) && typeof r.filePath === 'string' && r.filePath) {
      const { added, removed } = countClaudeLines(r);
      ev.kind = 'edit';
      ev.linesAdded = added;
      ev.linesRemoved = removed;
      ev.entity = hashPath(r.filePath, ctx.salt);
    }
    events.push(ev);
  }
  const last = lines[lines.length - 1];
  return { events, consumed: last ? last.end + 1 : 0 };
}

const yieldTurn = (): Promise<void> => new Promise((res) => setImmediate(res));

/**
 * One bounded pass over every transcript. For each file: append its events to the Claude
 * spool, then advance and persist its offset. Reads at most `maxBytes` (default 8 MB)
 * across files, yielding between files; `more` says whether work remains.
 */
export async function readPass(opts: ReadPassOptions): Promise<ReadPassResult> {
  const homes = opts.homes ?? discoverHomes(opts);
  const maxBytes = opts.maxBytes ?? DEFAULT_MAX_BYTES;
  const sdir = stateDir(opts.dir);
  const installedMs = Date.parse(opts.installed.installedAt);
  const offsets: OffsetsState = readOffsets(sdir);

  const events: ClaudeEvent[] = [];
  const unreadableByDate: Record<string, number> = {};
  const touched = new Set<string>();
  let budget = maxBytes;
  let more = false;
  let dirty = false;

  const seen = new Set<string>();
  const files: string[] = [];
  for (const home of homes) {
    for (const f of listTranscripts(home)) {
      let real: string;
      try {
        real = fs.realpathSync(f);
      } catch {
        continue;
      }
      if (!seen.has(real)) {
        seen.add(real);
        files.push(real);
      }
    }
  }

  for (const real of files) {
    await yieldTurn();
    if (budget <= 0) {
      more = true;
      break;
    }
    let stat: fs.Stats;
    try {
      stat = fs.statSync(real);
    } catch {
      continue;
    }
    const entry = offsets[real];
    let offset: number;
    if (!entry) offset = stat.mtimeMs < installedMs ? stat.size : 0;
    else if (stat.size < entry.size || stat.size < entry.offset) offset = 0;
    else offset = entry.offset;

    if (offset >= stat.size) {
      if (!entry || entry.offset !== offset || entry.size !== stat.size || entry.mtimeMs !== stat.mtimeMs) {
        offsets[real] = { offset, size: stat.size, mtimeMs: stat.mtimeMs };
        dirty = true;
      }
      continue;
    }

    const ctx: FileCtx = {
      stream: streamId(real),
      salt: opts.installed.salt,
      installedMs,
      projectKeys: new Map(),
      fallbackDate: localDate(stat.mtimeMs),
    };
    let fd: number;
    try {
      fd = fs.openSync(real, 'r');
    } catch {
      continue;
    }
    let fileEvents: ClaudeEvent[];
    let consumed: number;
    try {
      const buf = readChunk(fd, offset, stat.size, budget);
      budget -= buf.length;
      // Lines without a cwd inherit the last one seen; a pass resuming mid-file has none until one appears.
      ({ events: fileEvents, consumed } = processLines(
        buf,
        offset,
        ctx,
        {},
        unreadableByDate,
        { value: ctx.fallbackDate },
      ));
    } finally {
      fs.closeSync(fd);
    }

    // Spool first, offsets second.
    appendClaudeEvents(opts.dir, fileEvents);
    offsets[real] = { offset: offset + consumed, size: stat.size, mtimeMs: stat.mtimeMs };
    writeOffsets(sdir, offsets);
    dirty = false;

    for (const ev of fileEvents) {
      events.push(ev);
      touched.add(localDate(ev.time * 1000));
    }
    if (offset + consumed < stat.size) {
      // Either the budget stopped us or a partial trailing line remains; only the former is pending work.
      if (budget <= 0 && consumed > 0) more = true;
    }
  }

  if (dirty) writeOffsets(sdir, offsets);
  return { events, more, unreadableByDate, touchedDates: [...touched].sort() };
}
