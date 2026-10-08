import { atom, read, update } from 'claude-code'
import type { EngineInterface, Register } from 'claude-code'

import type { BandStyle, MetersBand, MetersSnapshot } from '../types'
import {
  FAST_MS,
  MAX_ROWS,
  SHIMMER_BEFORE_MS,
  SLOW_MS,
  crossings,
  glowLevel,
  gradient,
  isMoving,
  letters,
  liveWatchers,
  mix,
  nextFrameIn,
  onceProgress,
  paint,
  parseBand,
  pulseProgress,
  watcherMovers,
  watcherRows,
} from './band'
import type { Movers, Run, WatcherRow } from './band'
import {
  PACE_COLOR,
  RESET_TOAST,
  SESSION,
  WEEKLY,
  bandFor,
  bar,
  parseSnapshot,
  remember,
  sessionReset,
  warningKeys,
  warningToast,
} from './meters'
import type { Band, Bars, Meter, Style } from './meters'

// Sanduhr's meters above the prompt, and its watchers (items 50, 65f, 66). Reads two files,
// Sanduhr's snapshot.json and band.json, and nothing else: no network, no model calls, no writes
// but the "already toasted" keys in $.store.

const snapshot = atom({ plugin: 'sanduhr-meters', key: 'snapshot' } as const, null as MetersSnapshot | null)
const bandFile = atom({ plugin: 'sanduhr-meters', key: 'band' } as const, null as MetersBand | null)

const POLL_MS = 30_000
// Watchers change by the second: band.json is a small local file, read every two.
const BAND_POLL_MS = 2_000
const TOASTED = 'toasted'
const WARN_RED = '#f87171'
const BAR_WIDTH: Record<Style, number> = { compact: 10, full: 20 }
const NARROW_BAR = 6
// Below this many columns a watcher shows its short title.
const WIDE_ROW = 60

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
    const appData = await $.env.get('APPDATA')

    return appData === undefined || appData === '' ? null : `${appData}\\Sanduhr\\${name}`
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

async function readSnapshot($: EngineInterface): Promise<string | null> {
  return readFile($, 'snapshot.json', await $.env.get('SANDUHR_SNAPSHOT'))
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
  return bandFor(snap, now, { bars: opts.bars, style: s, tzOffsetMinutes: new Date(now).getTimezoneOffset() })
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

function isShimmering(m: Meter): boolean {
  return m.resetLeft !== null && m.resetLeft <= SHIMMER_BEFORE_MS
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

/** The Text attributes a style turns on. */
type Attrs = { bold: boolean; italic: boolean; underline: boolean; dim: boolean }

function attrsOf(style: BandStyle | undefined, bold = false): Attrs {
  return { bold: bold || style?.bold === true, italic: style?.italic === true, underline: style?.underline === true, dim: style?.dim === true }
}

/** `text` in `style`'s letters and ink (else `fallback`), lit by `t` and `glow`. */
function styled(text: string, style: BandStyle | undefined, fallback: string | null, t: number | null, glow: number) {
  const chars = letters(text, style?.font ?? null)
  const base = style !== undefined && style.ink.length > 0 ? gradient(style.ink, chars.length) : chars.map(() => fallback)

  return paint(chars, base, t, glow)
}

function styleFor(band: MetersBand | null, key: string): BandStyle | undefined {
  return key === SESSION ? band?.styles.session : key === WEEKLY ? band?.styles.weekly : undefined
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
    const width = e.props.bodyColumns
    const moving = motionAllowed(look)
    // Full style takes a row per meter; it folds to one line where the rows or columns are short.
    const full = bandAt(snap, now, 'full')
    const fullFits = full.kind !== 'meters' || (full.meters.length <= e.props.maxRows && width >= 64)
    const shown = opts.style === 'full' && fullFits ? 'full' : 'compact'
    const band = shown === 'full' ? full : bandAt(snap, now, 'compact')
    const used = band.kind === 'none' ? 0 : band.kind === 'meters' && shown === 'full' ? band.meters.length : 1
    const room = Math.max(0, Math.min(MAX_ROWS, e.props.maxRows - used))
    const rows = watcherRows(liveWatchers(look, now), now, { wide: width >= WIDE_ROW, motion: moving })
    drawn = band.kind !== 'none' || (rows.length > 0 && room > 0)
    frameKey = signature(now)

    if (!drawn) {
      return below
    }

    const { Box, Text } = $.ui.resolve(e)
    // Runs as Text elements in a style's attributes; a run without a color is dim when `dimBare`.
    const texts = (prefix: string, runs: Run[], a: Attrs, dimBare: boolean) =>
      runs.map((r, i) => (
        <Text
          key={`${prefix}${i}`}
          color={r.color ?? undefined}
          bold={a.bold ? true : undefined}
          italic={a.italic ? true : undefined}
          underline={a.underline ? true : undefined}
          dimColor={a.dim || (r.color === null && dimBare) ? true : undefined}
        >
          {r.text}
        </Text>
      ))
    const barWidth = shown === 'compact' && width < 80 ? NARROW_BAR : BAR_WIDTH[shown]
    const plain = band.kind === 'meters' && band.isDim
    const lit = moving && !plain

    const meterRow = (m: Meter) => {
      const style = styleFor(look, m.key)
      const name = texts('n', styled(m.name, style, null, null, 0), attrsOf(style), plain)

      if (m.isCrossed) {
        return (
          <Box key={`sm-${m.key}`} gap={1}>
            <Box key="name">{texts('n', styled(m.name, style, null, null, 0), attrsOf(style), true)}</Box>
            <Text key="reset" dimColor>
              reset
            </Text>
          </Box>
        )
      }

      const tone = m.isWarning ? WARN_RED : m.color
      const cells = bar(m.pct, m.fraction, barWidth).flatMap(r =>
        Array.from(r.text).map(ch => ({ ch, color: plain ? null : r.part === 'fill' ? tone : r.part === 'pace' ? PACE_COLOR : null })),
      )
      const pctChars = letters(`${m.pct}%`, style?.font ?? null)
      const span = cells.length + pctChars.length
      const sweep = lit ? onceProgress(sweeps[m.key], now) : null
      const glow = lit && m.isWarning ? glowLevel(pulseProgress(now)) : 0
      const barRuns = paint(cells.map(c => c.ch), cells.map(c => c.color), sweep, 0, 0, span)
      const pctBase = plain ? pctChars.map(() => null) : style !== undefined && style.ink.length > 0 ? gradient(style.ink, pctChars.length) : pctChars.map(() => tone)
      const pctRuns = paint(pctChars, pctBase, sweep, glow, cells.length, span)
      const resets = look?.styles.resets
      const shimmer = lit && isShimmering(m) ? pulseProgress(now) : null

      return (
        <Box key={`sm-${m.key}`} gap={1}>
          <Box key="name">{name}</Box>
          <Box key="bar">
            <Text key="l" dimColor>
              ▕
            </Text>
            {barRuns.map((r, i) => (
              <Text key={`r${i}`} color={r.color ?? undefined} dimColor={plain || r.color === null ? true : undefined}>
                {r.text}
              </Text>
            ))}
            <Text key="r" dimColor>
              ▏
            </Text>
          </Box>
          <Box key="pct">{texts('p', pctRuns, attrsOf(style, m.isWarning), plain)}</Box>
          {m.isWarning && (
            <Text key="warn" color={mix(WARN_RED, '#ffffff', glow)}>
              ⚠
            </Text>
          )}
          {shown === 'full' && m.pace !== null && (
            <Text key="pace" dimColor>
              {m.pace}
            </Text>
          )}
          {m.reset !== null && <Box key="reset">{texts('s', styled(m.reset, resets, null, shimmer, 0), attrsOf(resets), true)}</Box>}
        </Box>
      )
    }

    const watcherRow = (r: WatcherRow, more: number) => (
      <Box key={`sw-${r.key}`} gap={1}>
        <Text key="mark" color={r.color}>
          {r.mark}
        </Text>
        <Text key="title" color={r.titleColor ?? undefined} dimColor={r.isDim ? true : undefined} wrap="truncate-end">
          {r.text}
        </Text>
        {r.elapsed !== null && (
          <Text key="elapsed" dimColor>
            {r.elapsed}
          </Text>
        )}
        {r.progress !== null && (
          <Text key="progress" dimColor={r.isDim ? true : undefined}>
            {r.progress}
          </Text>
        )}
        {more > 0 && (
          <Text key="more" dimColor>
            {`+${more} more`}
          </Text>
        )}
      </Box>
    )

    const visible = rows.slice(0, room)
    const watchers =
      visible.length === 0 ? null : (
        <Box key="sm-watchers" flexDirection="column">
          {visible.map((r, i) => watcherRow(r, i === visible.length - 1 ? rows.length - visible.length : 0))}
        </Box>
      )

    const head =
      opts.label === '' ? null : (
        <Text key="sm-label" dimColor>
          {opts.label}
        </Text>
      )
    const note =
      band.kind !== 'meters' || band.note === null ? null : (
        <Text key="sm-note" dimColor>
          {band.note}
        </Text>
      )
    const meters =
      band.kind === 'line' ? (
        <Text key="sm-line" dimColor wrap="truncate-end">
          {band.text}
        </Text>
      ) : band.kind !== 'meters' ? null : shown === 'full' ? (
          <Box key="sm-full" flexDirection="column">
            {band.meters.map((m, i) => (
              <Box key={`sm-row-${m.key}`} gap={1}>
                {i === 0 && head}
                {meterRow(m)}
                {i === band.meters.length - 1 && note}
              </Box>
            ))}
          </Box>
        ) : (
          <Box key="sm-band" gap={3}>
            {head}
            {band.meters.map(meterRow)}
            {note}
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
