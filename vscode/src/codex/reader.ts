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
 */
import { createHash } from 'node:crypto';
import * as fs from 'node:fs';
import * as path from 'node:path';
import { yieldTurn, type ReadPassResult } from '../claude/reader';
import type { HomeOptions } from '../claude/homes';
import { resolve as resolveProject } from '../project';
import { appendAgentEvents, localDate } from '../store/agentSpool';
import { hashPath } from '../store/hash';
import { readChunk, splitLines } from '../store/lines';
import { stateDir } from '../store/paths';
import { readOffsets, writeOffsets } from '../store/state';
import type { AgentEvent, CodexSegmentState, InstalledState, OffsetsState } from '../types';
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
}

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
function scanContext(fd: number, upTo: number, ctx: Pick<FileCtx, 'real' | 'platform'>): CodexSegmentState {
  const seg: CodexSegmentState = {};
  let offset = 0;
  while (offset < upTo) {
    const buf = readChunk(fd, offset, upTo, SCAN_CHUNK);
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
): { events: AgentEvent[]; consumed: number } {
  const events: AgentEvent[] = [];
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
    const ms = typeof o.timestamp === 'string' ? Date.parse(o.timestamp) : NaN;
    if (Number.isNaN(ms)) {
      bump(lastDate.value);
      continue;
    }
    const date = localDate(ms);
    lastDate.value = date;
    if (applyContext(o, seg, ctx)) continue;

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

/**
 * One bounded pass over every rollout. For each file: append its events to the agent spool,
 * then advance and persist its offset and segment. Reads at most `maxBytes` (default 8 MB)
 * across files, yielding between files; `more` says whether work remains. A rollout first
 * seen with an mtime before the install starts at its current size, like a Claude transcript.
 */
export async function readCodexPass(opts: CodexPassOptions): Promise<ReadPassResult> {
  const homes = opts.homes ?? discoverCodexHomes(opts);
  const maxBytes = opts.maxBytes ?? DEFAULT_MAX_BYTES;
  const platform = opts.platform ?? process.platform;
  const sdir = stateDir(opts.dir);
  const installedMs = Date.parse(opts.installed.installedAt);
  const offsets: OffsetsState = readOffsets(sdir);

  const events: AgentEvent[] = [];
  const unreadableByDate: Record<string, number> = {};
  const touched = new Set<string>();
  let budget = maxBytes;
  let more = false;
  let dirty = false;

  const seen = new Set<string>();
  const files: string[] = [];
  for (const home of homes) {
    for (const f of listRollouts(home)) {
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
    let seg: CodexSegmentState | undefined;
    if (!entry) offset = stat.mtimeMs < installedMs ? stat.size : 0;
    else if (stat.size < entry.size || stat.size < entry.offset) offset = 0;
    else {
      offset = entry.offset;
      seg = entry.codex;
    }

    if (offset >= stat.size) {
      if (!entry || entry.offset !== offset || entry.size !== stat.size || entry.mtimeMs !== stat.mtimeMs) {
        offsets[real] = { offset, size: stat.size, mtimeMs: stat.mtimeMs, ...(seg ? { codex: seg } : {}) };
        dirty = true;
      }
      continue;
    }

    const ctx: FileCtx = { real, salt: opts.installed.salt, installedMs, platform, projectKeys: new Map() };
    let fd: number;
    try {
      fd = fs.openSync(real, 'r');
    } catch {
      continue;
    }
    let fileEvents: AgentEvent[];
    let consumed: number;
    try {
      if (offset === 0) seg = {};
      else if (!seg) seg = scanContext(fd, offset, ctx);
      const buf = readChunk(fd, offset, stat.size, budget);
      budget -= buf.length;
      ({ events: fileEvents, consumed } = processLines(buf, offset, ctx, seg, unreadableByDate, {
        value: localDate(stat.mtimeMs),
      }));
    } finally {
      fs.closeSync(fd);
    }

    // Spool first, offsets second.
    appendAgentEvents(opts.dir, fileEvents);
    offsets[real] = { offset: offset + consumed, size: stat.size, mtimeMs: stat.mtimeMs, codex: seg };
    writeOffsets(sdir, offsets);
    dirty = false;

    for (const ev of fileEvents) {
      events.push(ev);
      touched.add(localDate(ev.time * 1000));
    }
    if (offset + consumed < stat.size && budget <= 0 && consumed > 0) more = true;
  }

  if (dirty) writeOffsets(sdir, offsets);
  return { events, more, unreadableByDate, touchedDates: [...touched].sort(), versionsByDate: {} };
}
