/**
 * Incremental Codex rollout reader. Walks `sessions/**\/rollout-*.jsonl` under every Codex
 * home, turns new lines into agent events (`agent: 'codex'`), appends them to the agent spool
 * and only then advances the file's offset, like the Claude reader. One rollout is one thread
 * and grows for days, so every record is dated by its own `timestamp`, never by the file.
 *
 * A rollout can hold several `session_meta` segments (a subagent or guardian thread inside the
 * file); the reader tracks the one in force, and its `session_id` keys the stream. The segment
 * and the working directory in force are saved with the offset, so a pass that resumes mid-file
 * still knows them. Only timestamps, kinds, hashed streams and paths, project keys and line
 * counts are extracted; message text, credentials and account ids are never read into an event.
 *
 * Codex later compresses an older rollout to `rollout-*.jsonl.zst`. The compressed file is read
 * under its uncompressed path, so its stream ids and its offset entry carry over from the plain
 * file; a VS Code whose Node has no zstd leaves it unread until one that has it.
 */
import { createHash } from 'node:crypto';
import * as fs from 'node:fs';
import * as path from 'node:path';
import * as zlib from 'node:zlib';
import { yieldTurn, type ReadPassResult } from '../claude/reader';
import type { HomeOptions } from '../claude/homes';
import { resolve as resolveProject } from '../project';
import { appendAgentEvents, localDate } from '../store/agentSpool';
import { hashPath } from '../store/hash';
import { readChunk, splitLines } from '../store/lines';
import { stateDir } from '../store/paths';
import { readOffsets, writeOffsets } from '../store/state';
import type { AgentEvent, CodexSegmentState, InstalledState, OffsetEntry, OffsetsState } from '../types';
import { discoverCodexHomes, listRollouts } from './homes';
import { findPatches } from './patch';

export const DEFAULT_MAX_BYTES = 8 * 1024 * 1024;
/** Context scans read this much at a time. */
const SCAN_CHUNK = 4 * 1024 * 1024;

export interface CodexPassOptions extends HomeOptions {
  /** Data directory (holds `state/`, `agents/`). */
  dir: string;
  installed: InstalledState;
  /** Explicit Codex homes; default is `discoverCodexHomes`. */
  homes?: string[];
  /** Byte budget across all rollouts for this pass. Default 8 MB. */
  maxBytes?: number;
  platform?: NodeJS.Platform;
  /** zstd decompressor for `.jsonl.zst` rollouts; default `nativeZstd()`, null for none. */
  zstd?: ZstdDecompress | null;
}

/** Decompress a whole zstd buffer. */
export type ZstdDecompress = (compressed: Buffer) => Buffer;

/** Node's own zstd (Node 22.15 and later; VS Code 1.146 runs Node 24). Undefined without it. */
export function nativeZstd(): ZstdDecompress | undefined {
  const f = (zlib as unknown as { zstdDecompressSync?: (b: Buffer) => Buffer }).zstdDecompressSync;
  return typeof f === 'function' ? (b) => f(b) : undefined;
}

const ZST = '.zst';

/** The thread sources whose `user` messages can be the human's prompts. */
const HUMAN_THREAD = 'user';

/**
 * Stream id for one segment of a rollout: the first 16 hex of sha256 of the rollout's real
 * path (forward slashes, lowercased on Windows), a newline, and the segment's `session_id`.
 * A record before any `session_meta` gets the path alone.
 */
export function codexStreamId(realPath: string, sessionId: string, platform: NodeJS.Platform = process.platform): string {
  const p = (platform === 'win32' ? realPath.toLowerCase() : realPath).split('\\').join('/');
  return createHash('sha256').update(`${p}\n${sessionId}`).digest('hex').slice(0, 16);
}

type Json = Record<string, unknown>;
const isObj = (v: unknown): v is Json => typeof v === 'object' && v !== null && !Array.isArray(v);
const str = (v: unknown): string | undefined => (typeof v === 'string' && v !== '' ? v : undefined);

/**
 * The Codex human-prompt rule: a `response_item` `message` with role `user`, carrying
 * `metadata.user_input_order`, inside a segment whose `thread_source` is `user`. Injected
 * context has no `user_input_order`; subagent and guardian_review prompts are machine-written.
 */
export function isCodexPrompt(o: Json, threadSource: string | undefined): boolean {
  if (o.type !== 'response_item' || !isObj(o.payload)) return false;
  if (o.payload.type !== 'message' || o.payload.role !== 'user') return false;
  if (!isObj(o.metadata) || o.metadata.user_input_order === undefined || o.metadata.user_input_order === null) return false;
  return threadSource === HUMAN_THREAD;
}

const ACTIVITY_ITEMS = new Set([
  'reasoning',
  'agent_message',
  'function_call',
  'function_call_output',
  'custom_tool_call',
  'custom_tool_call_output',
]);

/**
 * Codex activity: `response_item` reasoning, assistant messages, agent messages, function and
 * custom tool calls and their outputs; `event_msg` `task_started` and `item_completed`. An
 * `item_completed` for a `UserMessage` only repeats the prompt, so it is not activity.
 */
export function isCodexActivity(o: Json): boolean {
  const p = o.payload;
  if (!isObj(p)) return false;
  if (o.type === 'response_item') {
    if (p.type === 'message') return p.role === 'assistant';
    return typeof p.type === 'string' && ACTIVITY_ITEMS.has(p.type);
  }
  if (o.type === 'event_msg') {
    if (p.type === 'task_started') return true;
    if (p.type === 'item_completed') return !(isObj(p.item) && p.item.type === 'UserMessage');
  }
  return false;
}

/** The tool-call text that may carry an `apply_patch` body: a custom call's input or a function call's arguments. */
function toolText(o: Json): string | undefined {
  if (o.type !== 'response_item' || !isObj(o.payload)) return undefined;
  const p = o.payload;
  if (p.type === 'custom_tool_call') return typeof p.input === 'string' ? p.input : undefined;
  if (p.type === 'function_call') return typeof p.arguments === 'string' ? p.arguments : undefined;
  return undefined;
}

interface FileCtx {
  real: string;
  salt: string;
  installedMs: number;
  platform: NodeJS.Platform;
  projectKeys: Map<string, string>;
}

/** Fold a context line (`session_meta`, `turn_context`) into the segment state. True when it was one. */
function applyContext(o: Json, seg: CodexSegmentState, ctx: Pick<FileCtx, 'real' | 'platform'>): boolean {
  const p = isObj(o.payload) ? o.payload : undefined;
  if (o.type === 'session_meta') {
    const sessionId = str(p?.session_id) ?? str(p?.id) ?? '';
    seg.stream = codexStreamId(ctx.real, sessionId, ctx.platform);
    seg.threadSource = str(p?.thread_source);
    seg.cwd = str(p?.cwd);
    return true;
  }
  if (o.type === 'turn_context') {
    const cwd = str(p?.cwd);
    if (cwd) seg.cwd = cwd;
    return true;
  }
  return false;
}

/**
 * Rebuild the segment state at `upTo` from the start of the file, parsing only context lines.
 * Needed once for a rollout first seen mid-way (older than the install, then resumed).
 */
function scanContext(body: Body, upTo: number, ctx: Pick<FileCtx, 'real' | 'platform'>): CodexSegmentState {
  const seg: CodexSegmentState = {};
  let offset = 0;
  while (offset < upTo) {
    const buf = body.chunk(offset, SCAN_CHUNK, upTo);
    const lines = splitLines(buf);
    if (lines.length === 0) break;
    for (const { start, end } of lines) {
      const raw = buf.subarray(start, end).toString('utf8');
      if (!raw.includes('"session_meta"') && !raw.includes('"turn_context"')) continue;
      try {
        const o: unknown = JSON.parse(raw);
        if (isObj(o)) applyContext(o, seg, ctx);
      } catch {
        /* counted when it was first read */
      }
    }
    offset += lines[lines.length - 1].end + 1;
  }
  return seg;
}

function resolveIn(cwd: string, file: string, platform: NodeJS.Platform): string {
  return (platform === 'win32' ? path.win32 : path.posix).resolve(cwd, file);
}

function processLines(
  buf: Buffer,
  baseOffset: number,
  ctx: FileCtx,
  seg: CodexSegmentState,
  unreadable: Record<string, number>,
  lastDate: { value: string },
  afterMs?: number,
): { events: AgentEvent[]; consumed: number } {
  const events: AgentEvent[] = [];
  const lines = splitLines(buf);
  // Re-reading a compressed copy: lines up to the watermark were counted the first time.
  let quiet = afterMs !== undefined;
  const bump = (date: string): void => {
    if (quiet) return;
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
    const ms = typeof o.timestamp === 'string' ? Date.parse(o.timestamp) : NaN;
    if (Number.isNaN(ms)) {
      bump(lastDate.value);
      continue;
    }
    const date = localDate(ms);
    lastDate.value = date;
    if (seg.lastMs === undefined || ms > seg.lastMs) seg.lastMs = ms;
    if (applyContext(o, seg, ctx)) continue;
    if (afterMs !== undefined) {
      if (ms <= afterMs) continue;
      quiet = false;
    }

    const prompt = isCodexPrompt(o, seg.threadSource);
    if (!prompt && !isCodexActivity(o)) continue;
    if (ms < ctx.installedMs) continue;
    const cwd = seg.cwd;
    if (!cwd) {
      bump(date);
      continue;
    }
    let project = ctx.projectKeys.get(cwd);
    if (project === undefined) {
      project = resolveProject(cwd).key;
      ctx.projectKeys.set(cwd, project);
    }
    const stream = seg.stream ?? codexStreamId(ctx.real, '', ctx.platform);
    const id = `${stream}:${baseOffset + start}`;
    const base = { v: 2 as const, agent: 'codex' as const, time: ms / 1000, project, stream };

    const text = prompt ? undefined : toolText(o);
    const patched = text ? findPatches(text) : [];
    if (patched.length > 0) {
      // One edit per file the patch touches; each is also Codex activity.
      patched.forEach((f, i) => {
        events.push({
          ...base,
          id: `${id}:${i}`,
          kind: 'edit',
          linesAdded: f.added,
          linesRemoved: f.removed,
          entity: hashPath(resolveIn(cwd, f.path, ctx.platform), ctx.salt, ctx.platform),
        });
      });
      continue;
    }
    events.push({ ...base, id, kind: prompt ? 'prompt' : 'activity' });
  }
  const last = lines[lines.length - 1];
  return { events, consumed: last ? last.end + 1 : 0 };
}

/** Bytes of one rollout, plain on disk or decompressed in memory. */
interface Body {
  /** `[offset, offset+budget)` extended to the end of a line, never past `upTo`. */
  chunk(offset: number, budget: number, upTo?: number): Buffer;
  close(): void;
}

function fileBody(fd: number, size: number): Body {
  return {
    chunk: (offset, budget, upTo = size) => readChunk(fd, offset, upTo, budget),
    close: () => fs.closeSync(fd),
  };
}

/** A decompressed rollout is sealed, so its last line counts even without a trailing newline. */
function bufferBody(data: Buffer): Body {
  return {
    chunk: (offset, budget, upTo = data.length) => {
      let end = Math.min(offset + Math.max(budget, 1), upTo);
      const nl = data.indexOf(0x0a, end - 1);
      end = nl === -1 || nl >= upTo ? upTo : nl + 1;
      const out = data.subarray(offset, end);
      return end === data.length && out.length > 0 && out[out.length - 1] !== 0x0a ? Buffer.concat([out, Buffer.from('\n')]) : out;
    },
    close: () => undefined,
  };
}

/** The last decompressed rollout, kept while a pass works through it over several budgets. */
let unpacked: { key: string; data: Buffer } | undefined;

function decompress(zstd: ZstdDecompress, file: string, stat: fs.Stats): Buffer {
  const key = `${file}\n${stat.size}\n${stat.mtimeMs}`;
  if (unpacked?.key !== key) unpacked = { key, data: zstd(fs.readFileSync(file)) };
  return unpacked.data;
}

/**
 * One bounded pass over every rollout. For each file: append its events to the agent spool,
 * then advance and persist its offset and segment. Reads at most `maxBytes` (default 8 MB)
 * across files, yielding between files; `more` says whether work remains. A rollout first
 * seen with an mtime before the install starts at its current size, like a Claude transcript.
 *
 * A `.jsonl.zst` is read only when its plain `.jsonl` is gone, under the plain path. When the
 * entry it inherits came from the plain file, it is read from the start and only records newer
 * than the plain file's last one become events, whatever its bytes look like compressed.
 */
export async function readCodexPass(opts: CodexPassOptions): Promise<ReadPassResult> {
  const homes = opts.homes ?? discoverCodexHomes(opts);
  const maxBytes = opts.maxBytes ?? DEFAULT_MAX_BYTES;
  const platform = opts.platform ?? process.platform;
  const zstd = opts.zstd === undefined ? nativeZstd() : (opts.zstd ?? undefined);
  const sdir = stateDir(opts.dir);
  const installedMs = Date.parse(opts.installed.installedAt);
  const offsets: OffsetsState = readOffsets(sdir);

  const events: AgentEvent[] = [];
  const unreadableByDate: Record<string, number> = {};
  const touched = new Set<string>();
  let budget = maxBytes;
  let more = false;
  let dirty = false;

  // Keyed by the uncompressed real path; the plain file wins while both exist.
  const sources = new Map<string, { plain?: string; zst?: string }>();
  for (const home of homes) {
    for (const f of listRollouts(home)) {
      let real: string;
      try {
        real = fs.realpathSync(f);
      } catch {
        continue;
      }
      const compressed = real.endsWith(ZST);
      const logical = compressed ? real.slice(0, -ZST.length) : real;
      let src = sources.get(logical);
      if (!src) sources.set(logical, (src = {}));
      if (compressed) src.zst ??= real;
      else src.plain ??= real;
    }
  }

  for (const [real, src] of sources) {
    await yieldTurn();
    if (budget <= 0) {
      more = true;
      break;
    }
    const file = src.plain ?? src.zst;
    if (!file) continue;
    let stat: fs.Stats;
    try {
      stat = fs.statSync(file);
    } catch {
      continue;
    }
    const entry = offsets[real];
    let offset: number;
    let size: number;
    let seg: CodexSegmentState | undefined;
    let afterMs: number | undefined;
    let zst: OffsetEntry['zst'];
    let data: Buffer | undefined;

    if (src.plain) {
      size = stat.size;
      if (!entry || entry.zst) offset = stat.mtimeMs < installedMs ? stat.size : 0;
      else if (stat.size < entry.size || stat.size < entry.offset) offset = 0;
      else {
        offset = entry.offset;
        seg = entry.codex;
      }
    } else {
      const same = entry?.zst !== undefined && entry.zst.size === stat.size && entry.zst.mtimeMs === stat.mtimeMs;
      if (same && entry.offset >= entry.size) continue;
      if (!zstd) continue; // no zstd in this Node: leave it for one that has it
      try {
        data = decompress(zstd, file, stat);
      } catch {
        continue; // not a whole zstd frame yet, or damaged
      }
      size = data.length;
      if (same) {
        offset = entry.offset;
        seg = entry.codex;
        afterMs = entry.zst?.afterMs;
      } else {
        offset = 0;
        // The plain file's mtime stands in for an older entry with no lastMs; a compressed
        // entry's mtime is when Codex compressed it, not its newest record, so never that.
        const fromPlain = entry !== undefined && entry.zst === undefined && entry.offset >= entry.size;
        afterMs = entry?.codex?.lastMs ?? (fromPlain ? entry.mtimeMs : undefined);
      }
      zst = { size: stat.size, mtimeMs: stat.mtimeMs, ...(afterMs !== undefined ? { afterMs } : {}) };
    }
    const stamp = { size, mtimeMs: stat.mtimeMs, ...(zst ? { zst } : {}) };

    if (offset >= size) {
      if (!entry || entry.offset !== offset || entry.size !== size || entry.mtimeMs !== stat.mtimeMs || !entry.zst !== !zst) {
        offsets[real] = { offset, ...stamp, ...(seg ? { codex: seg } : {}) };
        dirty = true;
      }
      continue;
    }

    const ctx: FileCtx = { real, salt: opts.installed.salt, installedMs, platform, projectKeys: new Map() };
    let body: Body;
    if (data) body = bufferBody(data);
    else {
      try {
        body = fileBody(fs.openSync(file, 'r'), size);
      } catch {
        continue;
      }
    }
    let fileEvents: AgentEvent[];
    let consumed: number;
    try {
      if (offset === 0) seg = {};
      else if (!seg) seg = scanContext(body, offset, ctx);
      const buf = body.chunk(offset, budget);
      budget -= buf.length;
      ({ events: fileEvents, consumed } = processLines(
        buf,
        offset,
        ctx,
        seg,
        unreadableByDate,
        { value: localDate(stat.mtimeMs) },
        afterMs,
      ));
    } finally {
      body.close();
    }
    const next = Math.min(offset + consumed, size);
    if (data && next >= size) unpacked = undefined;

    // Spool first, offsets second.
    appendAgentEvents(opts.dir, fileEvents);
    offsets[real] = { offset: next, ...stamp, codex: seg };
    writeOffsets(sdir, offsets);
    dirty = false;

    for (const ev of fileEvents) {
      events.push(ev);
      touched.add(localDate(ev.time * 1000));
    }
    if (next < size && budget <= 0 && consumed > 0) more = true;
  }

  if (dirty) writeOffsets(sdir, offsets);
  return { events, more, unreadableByDate, touchedDates: [...touched].sort(), versionsByDate: {} };
}
