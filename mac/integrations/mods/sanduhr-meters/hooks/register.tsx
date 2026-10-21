import { atom, read, update } from 'claude-code'
import type { EngineInterface, Register } from 'claude-code'

import type { MetersBand, MetersSnapshot } from '../types'
import {
  FAST_MS,
  SLOW_MS,
  crossings,
  isMoving,
  liveWatchers,
  nextFrameIn,
  onceProgress,
  parseBand,
  watcherMovers,
  watcherRows,
} from './band'
import type { Movers } from './band'
import { draws, frameFor, isShimmering } from './draw'
import type { Cell, Frame } from './draw'
import {
  RESET_TOAST,
  SESSION,
  bandFor,
  SEATS_MAX_BYTES,
  configFolder,
  expandFolder,
  parseSeats,
  parseSnapshot,
  remember,
  seatKey,
  seatSnapshotName,
  sessionReset,
  warningKeys,
  warningToast,
} from './meters'
import type { Band, Bars, Style } from './meters'

// Sanduhr's meters above the prompt, and its watchers (items 50, 65f, 66). Reads Sanduhr's
// snapshot (snapshot.json, or on macOS this seat's own snapshot.<ref>.json that seats.json names,
// item 74) and band.json, and nothing else: no network, no model calls, no writes but the
// "already toasted" keys in $.store.

const snapshot = atom({ plugin: 'sanduhr-meters', key: 'snapshot' } as const, null as MetersSnapshot | null)
const bandFile = atom({ plugin: 'sanduhr-meters', key: 'band' } as const, null as MetersBand | null)

const POLL_MS = 30_000
// Watchers change by the second: band.json is a small local file, read every two.
const BAND_POLL_MS = 2_000
const TOASTED = 'toasted'

type Motion = 'on' | 'off'
type Options = { bars: Bars; style: Style; label: string; motion: Motion }

// The module's own memory (a reload starts it over; session.start reads the files again).
let opts: Options = { bars: 'both', style: 'compact', label: '', motion: 'on' }
let lastText: string | null = null
let current: MetersSnapshot | null = null
let lastBandText: string | null = null
let currentBand: MetersBand | null = null
let lastSessionReset: number | null = null
// Each limit's percent at the last reading, and when each one last crossed a warning line.
let levels: Record<string, number> | null = null
let sweeps: Record<string, number> = {}
// Whether the band drew anything last, and what: a frame redraws only when that would change.
let drawn = false
let frameKey: string | null = null
let timers: (() => void)[] = []
// This session's Claude Code folder as a seat key, resolved once per session.start (null on
// Windows or for a relative CLAUDE_CONFIG_DIR), and whether the snapshot read is a seat's own.
let seatFolder: string | null = null
let home = ''
let isSeat = false
let frameTimer: (() => void) | null = null

function pick<T extends string>(value: unknown, allowed: readonly T[], fallback: T): T {
  return allowed.includes(value as T) ? (value as T) : fallback
}

/** A file in Sanduhr's folder on this machine; `named` (an override variable's value) names another (testing). */
async function sanduhrFile($: EngineInterface, name: string, named: string | undefined): Promise<string | null> {

  if (named !== undefined && named !== '') {
    return named
  }

  if ((await $.env.get('OS')) === 'Windows_NT') {
    const profile = await $.env.get('USERPROFILE')

    return profile === undefined || profile === '' ? null : `${profile}\\.sanduhr\\${name}`
  }

  const home = await $.env.get('HOME')

  return home === undefined || home === '' ? null : `${home}/Library/Application Support/Sanduhr/${name}`
}

async function readFile($: EngineInterface, name: string, named: string | undefined): Promise<string | null> {
  const path = await sanduhrFile($, name, named)

  if (path === null) {
    return null
  }

  try {
    return await $.fs.read(path)
  } catch {
    // Missing (Sanduhr not installed, signed out, or nothing for the band): no band.
    return null
  }
}

/** A toast once per limit and reset window, and once per session reset, across sessions. */
async function toasts($: EngineInterface, snap: MetersSnapshot | null, now: number) {
  const tz = new Date(now).getTimezoneOffset()
  const stored = await $.store.get(TOASTED)
  const seen = Array.isArray(stored) ? stored.filter((k): k is string => typeof k === 'string') : []
  let next = seen

  for (const key of warningKeys(snap, now)) {
    if (next.includes(key) || snap === null) {
      continue
    }

    const text = warningToast(snap, key, now, tz)

    if (text !== null) {
      $.ui.toast(text, { timeoutMs: 8000 })
    }

    next = remember(next, key)
  }

  const reset = sessionReset(lastSessionReset, snap, now)

  if (reset !== null && !next.includes(`reset@${reset}`)) {
    $.ui.toast(RESET_TOAST, { timeoutMs: 6000 })
    next = remember(next, `reset@${reset}`)
  }

  const session = snap?.kind === 'snapshot' ? snap.tiers.find(t => t.key === SESSION)?.resetsAt ?? null : null

  if (session !== null) {
    lastSessionReset = session
  }

  if (next !== seen) {
    await $.store.set(TOASTED, next)
  }
}

/** Each limit's percent in a fresh reading, for the warning-line sweeps. */
function percents(snap: MetersSnapshot | null): Record<string, number> | null {
  if (snap === null || snap.kind !== 'snapshot' || snap.status !== 'ok') {
    return null
  }

  const out: Record<string, number> = {}

  for (const t of snap.tiers) {
    if (t.utilization !== null) {
      out[t.key] = t.utilization
    }
  }

  return out
}

/** Reads the snapshot; the drawing hears of it only when the numbers changed. */
async function poll($: EngineInterface) {
  const text = await readSnapshot($)
  const now = await $.clock.now()

  if (text !== lastText) {
    lastText = text
    current = text === null ? null : parseSnapshot(text)
    const next = percents(current)

    // A limit that crossed a warning line since the last reading sweeps its bar once.
    if (next !== null) {
      if (levels !== null) {
        for (const key of crossings(levels, next)) {
          sweeps[key] = now
        }
      }

      levels = next
    }

    await update($, snapshot, () => current)
    kick($)
  }

  await toasts($, current, now)
}

/**
 * The snapshot this seat reads (item 74): SANDUHR_SNAPSHOT when set; else the account seats.json
 * links this session's folder to, else snapshot.json (Sanduhr's active account) as before. A
 * linked seat whose account has no file yet draws nothing rather than another account's numbers.
 */
async function readSnapshot($: EngineInterface): Promise<string | null> {
  const named = await $.env.get('SANDUHR_SNAPSHOT')

  if (named !== undefined && named !== '') {
    isSeat = false

    return readFile($, 'snapshot.json', named)
  }

  const rows = seatFolder === null ? null : await seatRows($)
  const name = seatSnapshotName(rows, seatFolder)
  isSeat = name !== 'snapshot.json'

  return readFile($, name, undefined)
}

/** seats.json's rows with each folder resolved to its key; null without a readable file of at most 64 KB. */
async function seatRows($: EngineInterface): Promise<{ key: string; ref: string }[] | null> {
  const path = await sanduhrFile($, 'seats.json', undefined)

  if (path === null) {
    return null
  }

  try {
    if ((await $.fs.stat(path, { resolve: false })).size > SEATS_MAX_BYTES) {
      return null
    }
  } catch {
    return null
  }

  const seats = parseSeats(await readFile($, 'seats.json', undefined))

  if (seats === null) {
    return null
  }

  const rows: { key: string; ref: string }[] = []

  for (const s of seats) {
    const folder = expandFolder(s.folder, home)

    if (folder !== null) {
      rows.push({ key: await resolvedKey($, folder), ref: s.ref })
    }
  }

  return rows
}

/** A folder's seat key: links followed where the folder exists, its spelling otherwise. */
async function resolvedKey($: EngineInterface, folder: string): Promise<string> {
  try {
    return seatKey((await $.fs.stat(folder, { resolve: true })).realPath ?? folder)
  } catch {
    return seatKey(folder)
  }
}

/** CLAUDE_CONFIG_DIR (unset: ~/.claude) as a seat key. macOS only; null for a relative path. */
async function resolveSeatFolder($: EngineInterface): Promise<string | null> {
  home = (await $.env.get('HOME')) ?? ''

  if ((await $.env.get('OS')) === 'Windows_NT' || home === '') {
    return null
  }

  const folder = configFolder(await $.env.get('CLAUDE_CONFIG_DIR'), home)

  return folder === null ? null : resolvedKey($, folder)
}

/** Reads band.json: the looks, Reduce Motion and the watchers. */
async function pollBand($: EngineInterface) {
  const text = await readFile($, 'band.json', await $.env.get('SANDUHR_BAND'))

  if (text !== lastBandText) {
    lastBandText = text
    currentBand = text === null ? null : parseBand(text)
    await update($, bandFile, () => currentBand)
    kick($)
  }
}

function motionAllowed(band: MetersBand | null): boolean {
  return opts.motion === 'on' && band?.reduceMotion !== true
}

function bandAt(snap: MetersSnapshot | null, now: number, s: Style): Band {
  return bandFor(snap, now, { bars: opts.bars, style: s, tzOffsetMinutes: new Date(now).getTimezoneOffset(), seat: isSeat })
}

/** What may move now: sweeps, the pulses (a nearly full limit glows, one about to reset shimmers, waiting watchers), fades. */
function movers(now: number): Movers {
  const band = bandAt(current, now, opts.style)
  const meters = band.kind === 'meters' && !band.isDim ? band.meters : []
  const w = watcherMovers(liveWatchers(currentBand, now))
  const shown = new Set(meters.map(m => m.key))

  return {
    sweeps: Object.entries(sweeps)
      .filter(([k]) => shown.has(k))
      .map(([, at]) => at),
    pulses: w.pulses || meters.some(m => m.isWarning || isShimmering(m)),
    fades: w.fades,
  }
}

/** What the band would draw at `now`, as a key: a frame redraws only when it changed. */
function signature(now: number): string {
  const moving = motionAllowed(currentBand)
  const rows = watcherRows(liveWatchers(currentBand, now), now, { wide: true, motion: moving })
  const frame = moving && isMoving(movers(now), now) ? Math.floor(now / FAST_MS) : null

  // The pace fraction moves every millisecond; the mark moves by whole cells (at most 20), so hundredths do.
  return JSON.stringify([bandAt(current, now, opts.style), rows, frame, currentBand?.styles ?? null], (k, v) =>
    k === 'resetLeft' ? undefined : k === 'fraction' && typeof v === 'number' ? Math.floor(v * 100) : v,
  )
}

/** One frame: redraw when something changed, then wait fast while a light moves, else a second. */
async function frame($: EngineInterface) {
  frameTimer = null
  const now = await $.clock.now()

  for (const [k, at] of Object.entries(sweeps)) {
    if (onceProgress(at, now) === null && at <= now) {
      delete sweeps[k]
    }
  }

  if (drawn) {
    const key = signature(now)

    if (key !== frameKey) {
      frameKey = key
      $.ui.invalidate('ui.render')
    }
  }

  schedule($, drawn ? nextFrameIn(movers(now), now, motionAllowed(currentBand)) : SLOW_MS)
}

function schedule($: EngineInterface, ms: number) {
  frameTimer?.()
  const t = $.clock.after(ms, () => {
    void frame($).catch(() => undefined)
  })
  frameTimer = () => t.cancel()
}

/** Something new arrived: the next frame comes at once, not at the end of a resting second. */
function kick($: EngineInterface) {
  if (frameTimer !== null) {
    schedule($, 0)
  }
}

// -- Drawing ----------------------------------------------------------------------------------

/** One frame of the band at `now` (draw.ts decides everything; this only renders it). */
function frameAt(snap: MetersSnapshot | null, look: MetersBand | null, now: number, width: number, maxRows: number): Frame {
  return frameFor({
    snap,
    look,
    now,
    bars: opts.bars,
    style: opts.style,
    label: opts.label,
    moving: motionAllowed(look),
    width,
    maxRows,
    tzOffsetMinutes: new Date(now).getTimezoneOffset(),
    seat: isSeat,
    sweeps,
  })
}

export const register: Register = (on, options) => {
  opts = {
    bars: pick<Bars>(options.bars, ['both', 'session', 'weekly'], 'both'),
    style: pick<Style>(options.style, ['compact', 'full'], 'compact'),
    label: typeof options.label === 'string' ? options.label.trim() : '',
    motion: pick<Motion>(options.motion, ['on', 'off'], 'on'),
  }

  on('session.start', async ($, e, next) => {
    const started = await next(e)

    for (const cancel of timers) {
      cancel()
    }

    frameTimer?.()
    frameTimer = null
    seatFolder = await resolveSeatFolder($).catch(() => null)
    await poll($).catch(() => undefined)
    await pollBand($).catch(() => undefined)

    const pollTimer = $.clock.every(POLL_MS, () => {
      void poll($).catch(() => undefined)
    })
    const bandTimer = $.clock.every(BAND_POLL_MS, () => {
      void pollBand($).catch(() => undefined)
    })
    timers = [() => pollTimer.cancel(), () => bandTimer.cancel()]
    schedule($, SLOW_MS)

    return started
  })

  // Shares the band: draws its lines above whatever the plugins beneath draw.
  on('ui.render', { component: 'AbovePrompt' }, async ($, e, next) => {
    const below = await next(e)
    const snap = await read($, snapshot)
    const look = await read($, bandFile)

    if (e.props.hasSurvey || e.props.maxRows < 1) {
      drawn = false

      return below
    }

    const now = await $.clock.now()
    const f = frameAt(snap, look, now, e.props.bodyColumns, e.props.maxRows)
    drawn = draws(f)
    frameKey = signature(now)

    if (!drawn) {
      return below
    }

    const { Box, Text } = $.ui.resolve(e)
    // A watcher's title gives way at the edge rather than wrapping the row (`truncate`).
    const texts = (prefix: string, cs: Cell[], truncate = false) =>
      cs.map((c, i) => (
        <Text
          key={`${prefix}${i}`}
          wrap={truncate ? 'truncate-end' : undefined}
          color={c.color ?? undefined}
          bold={c.bold ? true : undefined}
          italic={c.italic ? true : undefined}
          underline={c.underline ? true : undefined}
          dimColor={c.dim ? true : undefined}
        >
          {c.text}
        </Text>
      ))
    const PIECES = ['name', 'bar', 'pct', 'warn', 'pace', 'reset']
    const m = f.meters
    const meterRow = (r: { key: string; pieces: Cell[][] }) => (
      <Box key={`sm-${r.key}`} gap={1}>
        {r.pieces.map((p, i) => (
          <Box key={PIECES[i] ?? `p${i}`}>{texts(`${i}-`, p)}</Box>
        ))}
      </Box>
    )
    const head = m.kind !== 'compact' && m.kind !== 'full' ? null : m.head === null ? null : (
      <Text key="sm-label" dimColor>
        {m.head}
      </Text>
    )
    const note = m.kind !== 'compact' && m.kind !== 'full' ? null : m.note === null ? null : (
      <Text key="sm-note" dimColor>
        {m.note}
      </Text>
    )
    const meters =
      m.kind === 'line' ? (
        <Text key="sm-line" dimColor wrap="truncate-end">
          {m.text}
        </Text>
      ) : m.kind === 'full' ? (
        <Box key="sm-full" flexDirection="column">
          {m.rows.map((r, i) => (
            <Box key={`sm-row-${r.key}`} gap={1}>
              {i === 0 && head}
              {meterRow(r)}
              {i === m.rows.length - 1 && note}
            </Box>
          ))}
        </Box>
      ) : m.kind === 'compact' ? (
        <Box key="sm-band" gap={3}>
          {head}
          {m.rows.map(meterRow)}
          {note}
        </Box>
      ) : null
    const watchers =
      f.watchers.length === 0 ? null : (
        <Box key="sm-watchers" flexDirection="column">
          {f.watchers.map((w, i) => (
            <Box key={`sw-w${i}`} gap={1}>
              {w.map((p, k) => (
                <Box key={`p${k}`}>{texts(`${k}-`, p, k === 1)}</Box>
              ))}
            </Box>
          ))}
        </Box>
      )

    return (
      <Box flexDirection="column">
        {meters}
        {watchers}
        {below}
      </Box>
    )
  })
}
