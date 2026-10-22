/**
 * Shared data shapes for Sanduhr Time.
 *
 * Unit conventions, kept strictly apart:
 * - Spool and Claude-spool `time` fields are unix SECONDS as a float (WakaTime's convention).
 * - Interval bounds and every duration in a day record (`*Ms`) are MILLISECONDS.
 *   `Interval` values are epoch milliseconds.
 */

/** A span of time in epoch milliseconds. `end >= start`. */
export interface Interval {
  /** Epoch milliseconds. */
  start: number;
  /** Epoch milliseconds. */
  end: number;
}

export type HeartbeatKind = 'edit' | 'save' | 'focus' | 'nav';

/** One editor spool line (`spool/<date>.<machine>.<window>.jsonl`). WakaTime-compatible names. */
export interface Heartbeat {
  v: 1;
  /** Unix seconds, float. */
  time: number;
  /** `hashPath(absolute path)`: 24 hex chars, never a real path. */
  entity: string;
  type: 'file';
  category: 'coding';
  kind: HeartbeatKind;
  /** Project key (16 hex chars). */
  project: string;
  /** Git branch, or null when detached or unknown. */
  branch: string | null;
  /** VS Code language id. */
  language: string;
  /** Document line count. */
  lines: number;
  /** Lines inserted plus removed by the human in this heartbeat; 0 for reloads. */
  human_line_changes: number;
  is_write: boolean;
  /** True when the change was a disk reload, never counted as the user's activity. */
  reload: boolean;
}

/** The agents whose transcripts Sanduhr Time reads. */
export type AgentName = 'claude' | 'codex';
/** Every agent, in display order. */
export const AGENT_NAMES: readonly AgentName[] = ['claude', 'codex'];

export type AgentEventKind = 'prompt' | 'activity' | 'edit';

/**
 * One agent spool line (`agents/<local date>.jsonl`, `v: 2`). Lines from the 0.1 and 0.2
 * Claude spool (`claude/<local date>.jsonl`, `v: 1`, no `agent`) are read as `agent: 'claude'`.
 */
export interface AgentEvent {
  v: 2;
  agent: AgentName;
  /** Transcript entry uuid, or stream plus byte offset; the merge de-duplicates on it. */
  id: string;
  /** Unix seconds, float. */
  time: number;
  /** Project key (16 hex chars). */
  project: string;
  /** Transcript identity: one Claude transcript file, or one Codex rollout segment. */
  stream: string;
  kind: AgentEventKind;
  linesAdded?: number;
  linesRemoved?: number;
  /** `hashPath(filePath)` for edit events. */
  entity?: string;
}

export interface LanguageTime {
  id: string;
  /** Milliseconds. */
  ms: number;
}

/** One agent's share of a day or project. All durations in milliseconds. */
export interface AgentTime {
  /** That agent's own union of intervals: the wall-clock time it was working. */
  wallMs: number;
  /** Sum over that agent's streams of each stream's own interval lengths (parallel streams add). */
  streamMs: number;
  /** Lines that agent added plus removed. */
  lines: number;
}

/** Per agent, only the agents that ran. */
export type ByAgent = Partial<Record<AgentName, AgentTime>>;

/** Totals shared by the day and each project. All durations in milliseconds. */
export interface DayTotals {
  /** You only (not overlapping any agent). */
  youMs: number;
  /** Agents only (not overlapping you): the union of every agent's intervals, minus yours. */
  agentsMs: number;
  /** Overlap of you and any agent. */
  bothMs: number;
  /** Union of all; equals youMs + agentsMs + bothMs. */
  totalMs: number;
  /** Sum over every agent's streams of each stream's own interval lengths. */
  agentMs: number;
  linesYou: number;
  /** Lines every agent added plus removed. */
  linesAgents: number;
  byAgent: ByAgent;
}

export interface ProjectDay extends DayTotals {
  key: string;
  realName: string;
  /** Null until the alias store has assigned one (item 7 fills aliases before merging). */
  alias: string | null;
  masked: boolean;
  languages: LanguageTime[];
}

export interface DayCaveats {
  unreadableTranscriptLines: number;
  readerVersion: string;
  claudeCodeVersions: string[];
}

/** `days/<date>.json`. A v1 file is read as v2 (`claudeMs` becomes `agentsMs` and `byAgent.claude`). */
export interface DayRecord {
  v: 2;
  /** Local date, YYYY-MM-DD. */
  date: string;
  machine: string;
  /** ISO timestamp. */
  generatedAt: string;
  totals: DayTotals;
  projects: ProjectDay[];
  caveats: DayCaveats;
}

/** `state/installed.json`. */
export interface InstalledState {
  /** ISO timestamp. */
  installedAt: string;
  /** Per-install random salt, hex. */
  salt: string;
}

/** One entry of `state/offsets.json`, keyed by real transcript path. */
export interface OffsetEntry {
  offset: number;
  size: number;
  mtimeMs: number;
  /** Codex rollouts only: the segment in force at `offset`, so a pass resuming mid-file knows it. */
  codex?: CodexSegmentState;
}

/**
 * Where a Codex rollout stands at a byte offset: the current `session_meta` segment's stream
 * (already hashed), its `thread_source`, and the working directory in force (the latest
 * `turn_context.cwd`, else the segment's `session_meta.cwd`). Local state only, like every
 * real path in `offsets.json`.
 */
export interface CodexSegmentState {
  stream?: string;
  threadSource?: string;
  cwd?: string;
}

export type OffsetsState = Record<string, OffsetEntry>;
