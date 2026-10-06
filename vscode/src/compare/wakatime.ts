/**
 * WakaTime side-by-side comparison. At most once an hour, triggered by a merge of today's record, it
 * runs `wakatime-cli --today`, and appends WakaTime's total next to our editor time to
 * `state/wakatime-compare.jsonl`. The panel reads the latest line. Everything that touches the
 * outside world (the CLI, the clock, the setting) is injected so tests need no real CLI.
 *
 * Shapes measured against wakatime-cli 2.26 (see docs/time-usage-spec.md 7.5):
 * - `--today`                    text: `2 hrs 32 mins`
 * - `--today --output json`      `{ "text": "2 hrs 32 mins", "has_team_features": false }`
 * - `--today --output raw-json`  `{ "cached_at", "data": { "grand_total": { "total_seconds": <number>, ... } } }`
 * `raw-json` is preferred because it carries exact seconds.
 */
import { execFile, type ExecFileException } from 'node:child_process';
import * as fs from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';
import { localDate } from '../store/claudeSpool';
import { stateDir } from '../store/paths';
import type { DayRecord } from '../types';

export const COMPARE_FILE = 'wakatime-compare.jsonl';
export const RUN_TIMEOUT_MS = 15_000;
/** Minimum gap between runs for the same day. */
export const MIN_INTERVAL_MS = 60 * 60 * 1000;

export interface CompareLine {
  date: string;
  wakatimeSeconds: number;
  oursEditorSeconds: number;
  at: string;
}

/** Same shape the panel's `compare` provider returns. */
export interface CompareValue {
  wakatimeSeconds: number;
}

export type RunResult = { seconds: number } | { error: string };

/** `child_process.execFile`, narrowed to what this module uses. */
export type ExecFn = (
  file: string,
  args: string[],
  options: { timeout: number; windowsHide: boolean; maxBuffer: number },
  callback: (error: ExecFileException | null, stdout: string, stderr: string) => void,
) => unknown;

const realExec: ExecFn = (file, args, options, cb) => execFile(file, args, { ...options, encoding: 'utf8' }, cb);

// ---------- locating the CLI ----------

function archName(arch: string): string {
  if (arch === 'x64') return 'amd64';
  if (arch === 'ia32') return '386';
  return arch; // arm64, arm
}

function osName(platform: string): string {
  if (platform === 'win32') return 'windows';
  return platform; // darwin, linux, freebsd
}

/** `wakatime-cli-<os>-<arch>[.exe]`, the file name WakaTime's own plugins install. */
export function cliFileName(platform: string, arch: string): string {
  return `wakatime-cli-${osName(platform)}-${archName(arch)}${platform === 'win32' ? '.exe' : ''}`;
}

export interface LocateOptions {
  platform?: string;
  arch?: string;
  /** Defaults to `fs.existsSync` on a regular file. */
  exists?: (p: string) => boolean;
  /** PATH value; defaults to `process.env.PATH`. */
  pathEnv?: string;
}

function isFile(p: string): boolean {
  try {
    return fs.statSync(p).isFile();
  } catch {
    return false;
  }
}

/** `~/.wakatime/wakatime-cli-<os>-<arch>[.exe]`, then `wakatime-cli` on PATH. Undefined when neither exists. */
export function locateCli(home: string, platform: string = process.platform, opts: LocateOptions = {}): string | undefined {
  const exists = opts.exists ?? isFile;
  const arch = opts.arch ?? process.arch;
  const direct = path.join(home, '.wakatime', cliFileName(platform, arch));
  if (exists(direct)) return direct;

  const sep = platform === 'win32' ? ';' : ':';
  const exe = platform === 'win32' ? 'wakatime-cli.exe' : 'wakatime-cli';
  for (const dir of (opts.pathEnv ?? process.env.PATH ?? '').split(sep)) {
    if (!dir) continue;
    const candidate = path.join(dir, exe);
    if (exists(candidate)) return candidate;
  }
  return undefined;
}

// ---------- parsing ----------

/** `data.grand_total.total_seconds` from `--output raw-json`. Undefined if the shape is anything else. */
export function parseRawJson(stdout: string): number | undefined {
  try {
    const j: unknown = JSON.parse(stdout);
    const data = (j as { data?: { grand_total?: { total_seconds?: unknown } } } | null)?.data;
    const s = data?.grand_total?.total_seconds;
    return typeof s === 'number' && Number.isFinite(s) && s >= 0 ? s : undefined;
  } catch {
    return undefined;
  }
}

/** `{ "text": "2 hrs 32 mins" }` from `--output json`. */
export function parseJson(stdout: string): number | undefined {
  try {
    const j: unknown = JSON.parse(stdout);
    const text = (j as { text?: unknown } | null)?.text;
    return typeof text === 'string' ? parseDurationText(text) : undefined;
  } catch {
    return undefined;
  }
}

const UNIT_SECONDS: Record<string, number> = {
  h: 3600,
  hr: 3600,
  hrs: 3600,
  hour: 3600,
  hours: 3600,
  m: 60,
  min: 60,
  mins: 60,
  minute: 60,
  minutes: 60,
  s: 1,
  sec: 1,
  secs: 1,
  second: 1,
  seconds: 1,
};

const SEGMENT = /(\d+(?:\.\d+)?)\s*([a-z]+)/gi;

/**
 * Parses one total such as `2 hrs 32 mins`, `1 hr 4 min`, `45 mins`, `3h 12m`, `0 secs`.
 * With categories appended (`2 hrs 32 mins, 1 hr Coding`) only the text before the first
 * comma is the total. Returns undefined when no duration is found.
 */
export function parseDurationText(text: string): number | undefined {
  const first = text.split(',')[0].trim();
  let total = 0;
  let found = false;
  SEGMENT.lastIndex = 0;
  let rest = first;
  for (const m of first.matchAll(SEGMENT)) {
    const unit = UNIT_SECONDS[m[2].toLowerCase()];
    if (unit === undefined) return undefined;
    total += Number(m[1]) * unit;
    found = true;
    rest = rest.replace(m[0], '');
  }
  if (!found || rest.replace(/\s+/g, '') !== '') return undefined;
  return total;
}

/** Plain `--today` output: the first non-empty line is the total. */
export function parseText(stdout: string): number | undefined {
  const line = stdout.split(/\r?\n/).find((l) => l.trim() !== '');
  return line === undefined ? undefined : parseDurationText(line);
}

// ---------- running ----------

export interface RunOptions {
  timeoutMs?: number;
  exec?: ExecFn;
}

function exec1(
  cli: string,
  args: string[],
  timeoutMs: number,
  exec: ExecFn,
): Promise<{ stdout: string } | { error: string }> {
  return new Promise((resolve) => {
    try {
      exec(cli, args, { timeout: timeoutMs, windowsHide: true, maxBuffer: 4 * 1024 * 1024 }, (err, stdout) => {
        if (err) {
          const e = err as ExecFileException & { killed?: boolean };
          resolve({ error: e.killed ? `timed out after ${timeoutMs} ms` : `exited with an error (${e.code ?? 'unknown'})` });
        } else {
          resolve({ stdout: String(stdout) });
        }
      });
    } catch (e) {
      resolve({ error: e instanceof Error ? e.message : String(e) });
    }
  });
}

/**
 * Runs the CLI without a shell. Prefers `--output raw-json` (exact seconds), then plain
 * `--today` text. A timeout or a failed launch is an error; an unparseable raw-json output
 * falls through to the text form.
 */
export async function runToday(cliPath: string, opts: RunOptions = {}): Promise<RunResult> {
  const timeoutMs = opts.timeoutMs ?? RUN_TIMEOUT_MS;
  const exec = opts.exec ?? realExec;

  const raw = await exec1(cliPath, ['--today', '--output', 'raw-json'], timeoutMs, exec);
  if ('stdout' in raw) {
    const s = parseRawJson(raw.stdout);
    if (s !== undefined) return { seconds: s };
  } else if (raw.error.startsWith('timed out')) {
    return raw;
  }

  const text = await exec1(cliPath, ['--today'], timeoutMs, exec);
  if ('error' in text) return text;
  const s = parseText(text.stdout) ?? parseJson(text.stdout);
  return s === undefined ? { error: 'unrecognized wakatime-cli output' } : { seconds: s };
}

// ---------- the jsonl ----------

const compareFile = (dir: string): string => path.join(stateDir(dir), COMPARE_FILE);

export function readCompareLines(dir: string): CompareLine[] {
  let raw: string;
  try {
    raw = fs.readFileSync(compareFile(dir), 'utf8');
  } catch {
    return [];
  }
  const out: CompareLine[] = [];
  for (const line of raw.split(/\r?\n/)) {
    if (!line.trim()) continue;
    try {
      const j = JSON.parse(line) as Partial<CompareLine>;
      if (typeof j.date === 'string' && typeof j.wakatimeSeconds === 'number' && typeof j.oursEditorSeconds === 'number') {
        out.push({ date: j.date, wakatimeSeconds: j.wakatimeSeconds, oursEditorSeconds: j.oursEditorSeconds, at: String(j.at ?? '') });
      }
    } catch {
      /* skip an unreadable line */
    }
  }
  return out;
}

export function appendCompareLine(dir: string, line: CompareLine): void {
  fs.mkdirSync(stateDir(dir), { recursive: true });
  fs.appendFileSync(compareFile(dir), `${JSON.stringify(line)}\n`);
}

// ---------- the service ----------

export interface ComparisonDeps {
  dir: string;
  /** `sanduhrTime.compareWakaTime`, read fresh each time. */
  enabled: () => boolean;
  /** Epoch milliseconds. */
  now: () => number;
  /** Where the CLI is, or undefined; default looks in `~/.wakatime` then PATH. */
  locate?: () => string | undefined;
  run?: (cliPath: string) => Promise<RunResult>;
  /** Today's merged record, for "ours". */
  getToday: () => Promise<DayRecord | undefined>;
  /** Logged quietly by the host; called at most once per distinct message. */
  onError?: (message: string) => void;
}

export class Comparison {
  /** Epoch ms of the last attempt (success or failure) and the date it was for. */
  private lastAttempt: { date: string; at: number } | undefined;
  private inFlight = false;
  private lastError: string | undefined;

  constructor(private readonly deps: ComparisonDeps) {}

  /** Hook for `api.onDayUpdated`: runs the comparison for today's record when the last run for today is over an hour old. */
  async onDayUpdated(record: DayRecord): Promise<void> {
    if (record.date !== localDate(this.deps.now())) return;
    await this.run(false);
  }

  /** The `sanduhrTime.compareNow` command: ignore the hourly gate (still honors the setting and the CLI). */
  async compareNow(): Promise<RunResult | undefined> {
    return this.run(true);
  }

  /** Whether the comparison can run at all: the setting is on and a CLI exists. */
  available(): boolean {
    return this.deps.enabled() && this.cliPath() !== undefined;
  }

  /** Newest comparison line for a date, or undefined. Hidden entirely when the setting is off. */
  latest(date: string): CompareLine | undefined {
    if (!this.deps.enabled()) return undefined;
    const lines = readCompareLines(this.deps.dir).filter((l) => l.date === date);
    return lines[lines.length - 1];
  }

  /** The panel's `compare` provider. */
  provider = async (): Promise<CompareValue | undefined> => {
    const l = this.latest(localDate(this.deps.now()));
    return l ? { wakatimeSeconds: l.wakatimeSeconds } : undefined;
  };

  private cliPath(): string | undefined {
    return this.deps.locate ? this.deps.locate() : locateCli(os.homedir());
  }

  private async run(force: boolean): Promise<RunResult | undefined> {
    const d = this.deps;
    if (!d.enabled() || this.inFlight) return undefined;
    const date = localDate(d.now());
    if (!force && !this.due(date)) return undefined;
    const cli = this.cliPath();
    if (!cli) return undefined;

    this.inFlight = true;
    this.lastAttempt = { date, at: d.now() };
    try {
      const result = await (d.run ?? ((p) => runToday(p)))(cli);
      if ('error' in result) {
        this.fail(result.error);
        // The attempt time is kept, so a broken CLI is retried at the next hourly window, not every tick.
        return result;
      }
      const today = await d.getToday();
      appendCompareLine(d.dir, {
        date,
        wakatimeSeconds: Math.round(result.seconds),
        oursEditorSeconds: today ? Math.round((today.totals.youMs + today.totals.bothMs) / 1000) : 0,
        at: new Date(d.now()).toISOString(),
      });
      return result;
    } catch (err) {
      this.fail(err instanceof Error ? err.message : String(err));
      return undefined;
    } finally {
      this.inFlight = false;
    }
  }

  /** True when no attempt for `date` (this process) or recorded line (any window) is under an hour old. */
  private due(date: string): boolean {
    const now = this.deps.now();
    if (this.lastAttempt?.date === date && now - this.lastAttempt.at < MIN_INTERVAL_MS) return false;
    const last = this.latest(date);
    const at = last ? Date.parse(last.at) : NaN;
    return !(Number.isFinite(at) && now - at < MIN_INTERVAL_MS);
  }

  private fail(message: string): void {
    if (message === this.lastError) return;
    this.lastError = message;
    this.deps.onError?.(message);
  }
}
