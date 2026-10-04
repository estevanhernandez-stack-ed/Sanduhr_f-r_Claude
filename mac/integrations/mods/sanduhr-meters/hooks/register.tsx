import { atom, read, update } from 'claude-code'
import type { EngineInterface, Register } from 'claude-code'

import type { MetersSnapshot } from '../types'
import {
  PACE_COLOR,
  RESET_TOAST,
  SESSION,
  bandFor,
  bar,
  parseSnapshot,
  remember,
  sessionReset,
  warningKeys,
  warningToast,
} from './meters'
import type { Band, Bars, Meter, Style } from './meters'

// Sanduhr's meters above the prompt. Reads one file, Sanduhr's snapshot.json, and nothing else:
// no network, no model calls, no writes but the "already toasted" keys in $.store.

const snapshot = atom({ plugin: 'sanduhr-meters', key: 'snapshot' } as const, null as MetersSnapshot | null)

const POLL_MS = 30_000
const TICK_MS = 60_000
const TOASTED = 'toasted'
const WARN_RED = '#f87171'
const BAR_WIDTH: Record<Style, number> = { compact: 10, full: 20 }
const NARROW_BAR = 6

// The module's own memory (a reload starts it over; session.start reads the file again).
let lastText: string | null = null
let current: MetersSnapshot | null = null
let lastSessionReset: number | null = null
// What the band drew last, so the minute tick redraws only when a countdown or age changed.
let drawn: string | null = null
let timers: (() => void)[] = []

function pick<T extends string>(value: unknown, allowed: readonly T[], fallback: T): T {
  return allowed.includes(value as T) ? (value as T) : fallback
}

/** Where Sanduhr writes snapshot.json on this machine; SANDUHR_SNAPSHOT names another (testing). */
async function snapshotPath($: EngineInterface): Promise<string | null> {
  const override = await $.env.get('SANDUHR_SNAPSHOT')

  if (override !== undefined && override !== '') {
    return override
  }

  if ((await $.env.get('OS')) === 'Windows_NT') {
    const appData = await $.env.get('APPDATA')

    return appData === undefined || appData === '' ? null : `${appData}\\Sanduhr\\snapshot.json`
  }

  const home = await $.env.get('HOME')

  return home === undefined || home === '' ? null : `${home}/Library/Application Support/Sanduhr/snapshot.json`
}

async function readSnapshot($: EngineInterface): Promise<string | null> {
  const path = await snapshotPath($)

  if (path === null) {
    return null
  }

  try {
    return await $.fs.read(path)
  } catch {
    // Missing (Sanduhr not installed, or signed out of every account and deleted): no band.
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

/** Reads the file; the drawing hears of it only when the numbers changed. */
async function poll($: EngineInterface) {
  const text = await readSnapshot($)

  if (text !== lastText) {
    lastText = text
    current = text === null ? null : parseSnapshot(text)
    await update($, snapshot, () => current)
  }

  await toasts($, current, await $.clock.now())
}

export const register: Register = (on, options) => {
  const bars = pick<Bars>(options.bars, ['both', 'session', 'weekly'], 'both')
  const style = pick<Style>(options.style, ['compact', 'full'], 'compact')
  const label = typeof options.label === 'string' ? options.label.trim() : ''

  const bandAt = (snap: MetersSnapshot | null, now: number, s: Style): Band =>
    bandFor(snap, now, { bars, style: s, tzOffsetMinutes: new Date(now).getTimezoneOffset() })

  on('session.start', async ($, e, next) => {
    const started = await next(e)

    for (const cancel of timers) {
      cancel()
    }

    await poll($).catch(() => undefined)

    const pollTimer = $.clock.every(POLL_MS, () => {
      void poll($).catch(() => undefined)
    })
    // Countdowns and the stale age move by the minute: redraw only while the band is drawn
    // and only when what it would draw changed.
    const tickTimer = $.clock.every(TICK_MS, () => {
      if (drawn === null) {
        return
      }

      void $.clock.now().then(now => {
        if (JSON.stringify(bandAt(current, now, style)) !== drawn) {
          $.ui.invalidate('ui.render')
        }
      })
    })
    timers = [() => pollTimer.cancel(), () => tickTimer.cancel()]

    return started
  })

  // Shares the band: draws its lines above whatever the plugins beneath draw.
  on('ui.render', { component: 'AbovePrompt' }, async ($, e, next) => {
    const below = await next(e)
    const snap = await read($, snapshot)

    if (e.props.hasSurvey || e.props.maxRows < 1) {
      drawn = null

      return below
    }

    const now = await $.clock.now()
    const width = e.props.bodyColumns
    // Full style takes a row per meter; it folds to one line where the rows or columns are short.
    const full = bandAt(snap, now, 'full')
    const fullFits = full.kind !== 'meters' || (full.meters.length <= e.props.maxRows && width >= 64)
    const shown = style === 'full' && fullFits ? 'full' : 'compact'
    const band = shown === 'full' ? full : bandAt(snap, now, 'compact')
    drawn = band.kind === 'none' ? null : JSON.stringify(bandAt(snap, now, style))

    if (band.kind === 'none') {
      return below
    }

    const { Box, Text } = $.ui.resolve(e)

    if (band.kind === 'line') {
      return (
        <Box flexDirection="column">
          <Text key="sm-line" dimColor wrap="truncate-end">
            {band.text}
          </Text>
          {below}
        </Box>
      )
    }

    const barWidth = shown === 'compact' && width < 80 ? NARROW_BAR : BAR_WIDTH[shown]
    const plain = band.isDim

    const meterRow = (m: Meter) => {
      if (m.isCrossed) {
        return (
          <Box key={`sm-${m.key}`} gap={1}>
            <Text dimColor>{m.name}</Text>
            <Text dimColor>reset</Text>
          </Box>
        )
      }

      const runs = bar(m.pct, m.fraction, barWidth)
      const tone = m.isWarning ? WARN_RED : m.color

      return (
        <Box key={`sm-${m.key}`} gap={1}>
          <Text dimColor={plain}>{m.name}</Text>
          <Box>
            <Text dimColor>▕</Text>
            {runs.map((r, i) => (
              <Text
                key={`r${i}`}
                color={plain ? undefined : r.part === 'fill' ? tone : r.part === 'pace' ? PACE_COLOR : undefined}
                dimColor={plain || r.part === 'empty'}
              >
                {r.text}
              </Text>
            ))}
            <Text dimColor>▏</Text>
          </Box>
          <Text key="pct" color={plain ? undefined : tone} bold={m.isWarning} dimColor={plain}>
            {`${m.pct}%`}
          </Text>
          {m.isWarning && (
            <Text key="warn" color={WARN_RED}>
              ⚠
            </Text>
          )}
          {shown === 'full' && m.pace !== null && <Text dimColor>{m.pace}</Text>}
          {m.reset !== null && <Text dimColor>{m.reset}</Text>}
        </Box>
      )
    }

    const head = label === '' ? null : (
      <Text key="sm-label" dimColor>
        {label}
      </Text>
    )
    const note = band.note === null ? null : (
      <Text key="sm-note" dimColor>
        {band.note}
      </Text>
    )
    if (shown === 'full') {
      return (
        <Box flexDirection="column">
          {band.meters.map((m, i) => (
            <Box key={`sm-row-${m.key}`} gap={1}>
              {i === 0 && head}
              {meterRow(m)}
              {i === band.meters.length - 1 && note}
            </Box>
          ))}
          {below}
        </Box>
      )
    }

    return (
      <Box flexDirection="column">
        <Box key="sm-band" gap={3}>
          {head}
          {band.meters.map(meterRow)}
          {note}
        </Box>
        {below}
      </Box>
    )
  })
}
