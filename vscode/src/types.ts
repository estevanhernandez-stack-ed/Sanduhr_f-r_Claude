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

export type ClaudeEventKind = 'prompt' | 'activity' | 'edit';

/** One Claude spool line (`claude/<local date>.jsonl`). */
export interface ClaudeEvent {
  v: 1;
  /** Transcript entry uuid, or file hash plus byte offset; the merge de-duplicates on it. */
  id: string;
  /** Unix seconds, float. */
  time: number;
  /** Project key (16 hex chars). */
  project: string;
  /** Transcript file identity (main session or one subagent file). */
  stream: string;
  kind: ClaudeEventKind;
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

/** Totals shared by the day and each project. All durations in milliseconds. */
export interface DayTotals {
  /** You only (not overlapping Claude). */
  youMs: number;
  /** Claude only (not overlapping you). */
  claudeMs: number;
  /** Overlap of you and Claude. */
  bothMs: number;
  /** Union of both; equals youMs + claudeMs + bothMs. */
  totalMs: number;
  /** Sum over Claude streams of each stream's own interval lengths. */
  agentMs: number;
  linesYou: number;
  linesClaude: number;
}

export interface ProjectDay extends DayTotals {
  key: string;
  realName: string;
  alias: string;
  masked: boolean;
  languages: LanguageTime[];
}

export interface DayCaveats {
  unreadableTranscriptLines: number;
  readerVersion: string;
  claudeCodeVersions: string[];
}

/** `days/<date>.json`. */
export interface DayRecord {
  v: 1;
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
}

export type OffsetsState = Record<string, OffsetEntry>;
