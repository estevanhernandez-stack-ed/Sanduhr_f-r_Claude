import { expect, test } from 'claude-code/testing'

import {
  RESET_TOAST_WINDOW_MS,
  bandFor,
  bar,
  countdown,
  freshness,
  isWarning,
  meterText,
  paceWords,
  parseSnapshot,
  parseTime,
  remember,
  resetWords,
  sessionReset,
  usageColor,
  warningKeys,
  warningToast,
} from './meters'
import type { BandOptions, Snapshot } from './meters'

// Sunday 2026-10-04 15:00 UTC; times below are read in UTC (offset 0).
const NOW = Date.parse('2026-10-04T15:00:00Z')
const MIN = 60_000
const HOUR = 60 * MIN
const COMPACT: BandOptions = { bars: 'both', style: 'compact', tzOffsetMinutes: 0 }
const FULL: BandOptions = { ...COMPACT, style: 'full' }

/** A snapshot as the macOS widget writes it (sorted keys, fractional seconds, Z). */
function file(o: { at?: number; status?: string; kind?: string | null; tiers?: unknown[]; version?: number } = {}): string {
  return JSON.stringify({
    account_ref: '1a2b3c4d',
    captured_at: new Date(o.at ?? NOW - 2 * MIN).toISOString(),
    error_kind: o.kind ?? null,
    plan: null,
    schema_version: o.version ?? 1,
    status: o.status ?? 'ok',
    tiers: o.tiers ?? [
      { key: 'five_hour', limit: null, resets_at: new Date(NOW + 2 * HOUR + 14 * MIN).toISOString(), used: null, utilization: 61 },
      { key: 'seven_day', limit: null, resets_at: '2026-10-05T16:00:00.000Z', used: null, utilization: 88 },
      { key: 'seven_day_opus', limit: null, resets_at: '2026-10-05T16:00:00.000Z', used: null, utilization: 40 },
    ],
    writer_version: '3.9.0',
  })
}

const snap = (o: Parameters<typeof file>[0] = {}) => parseSnapshot(file(o)) as Snapshot

test('parses the widget snapshot; missing, junk and partial files read as no snapshot', () => {
  const s = snap()
  expect(s.kind).toBe('snapshot')
  expect(s.kind === 'snapshot' && s.tiers.map(t => t.utilization)).toEqual([61, 88, 40])
  expect(parseSnapshot('')).toBeNull()
  expect(parseSnapshot('not json')).toBeNull()
  expect(parseSnapshot('[]')).toBeNull()
  expect(parseSnapshot('{"schema_version":1}')).toBeNull()
  expect(parseSnapshot(file({ version: 2 }))).toEqual({ kind: 'newer' })
})

test('times: .NET seven-digit fractions and offsets parse', () => {
  expect(parseTime('2026-10-04T15:00:00.1234567+00:00')).toBe(NOW + 123)
  expect(parseTime('2026-10-04T15:00:00Z')).toBe(NOW)
  expect(parseTime('soon')).toBeNull()
  expect(parseTime(null)).toBeNull()
})

test('freshness bands match the statusline: fresh under 7.5m, stale to 15m, dead after', () => {
  expect(freshness(NOW - 449_000, NOW)).toBe('fresh')
  expect(freshness(NOW - 450_000, NOW)).toBe('stale')
  expect(freshness(NOW - 900_000, NOW)).toBe('stale')
  expect(freshness(NOW - 901_000, NOW)).toBe('dead')
  expect(freshness(NOW + 60_000, NOW)).toBe('fresh')
})

test('bars: eighths, the pace mark, and clamping', () => {
  expect(bar(0, null, 10)).toEqual([{ text: '░░░░░░░░░░', part: 'empty' }])
  expect(bar(100, null, 10)).toEqual([{ text: '██████████', part: 'fill' }])
  expect(bar(88, null, 10).map(r => r.text).join('')).toBe('████████▊░')
  expect(bar(61, 0.55, 10)).toEqual([
    { text: '█████', part: 'fill' },
    { text: '│', part: 'pace' },
    { text: '▏', part: 'fill' },
    { text: '░░░', part: 'empty' },
  ])
  expect(bar(150, 1, 4).map(r => r.text).join('')).toBe('███│')
})

test('countdowns and reset words', () => {
  expect(countdown(2 * HOUR + 14 * MIN + 30_000)).toBe('2h 14m')
  expect(countdown(14 * MIN)).toBe('14m')
  expect(countdown(26 * HOUR)).toBe('1d 2h')
  expect(countdown(30_000)).toBe('now')
  expect(resetWords(NOW + 2 * HOUR + 14 * MIN, NOW, 'compact', 0)).toBe('resets 2h 14m')
  expect(resetWords(NOW + 2 * HOUR + 14 * MIN, NOW, 'full', 0)).toBe('resets in 2h 14m')
  expect(resetWords(Date.parse('2026-10-05T16:00:00Z'), NOW, 'compact', 0)).toBe('resets Mon')
  expect(resetWords(Date.parse('2026-10-05T16:00:00Z'), NOW, 'full', 0)).toBe('resets Mon 4:00 PM')
  // Chicago in summer: UTC-5, offset +300.
  expect(resetWords(Date.parse('2026-10-05T16:00:00Z'), NOW, 'full', 300)).toBe('resets Mon 11:00 AM')
  expect(resetWords(null, NOW, 'full', 0)).toBeNull()
})

test('pace words and colors are the widget\'s', () => {
  expect(paceWords(50, 0.48)).toBe('on pace')
  expect(paceWords(61, 0.553)).toBe('5% ahead')
  expect(paceWords(20, 0.5)).toBe('30% under')
  expect(paceWords(20, null)).toBeNull()
  expect([usageColor(10), usageColor(50), usageColor(75), usageColor(90)]).toEqual(['#4ade80', '#facc15', '#fb923c', '#f87171'])
})

test('warning: 90% with the reset still far off (a day weekly, an hour session)', () => {
  expect(isWarning('seven_day', 90, NOW + 25 * HOUR, NOW)).toBe(true)
  expect(isWarning('seven_day', 90, NOW + 23 * HOUR, NOW)).toBe(false)
  expect(isWarning('seven_day', 89, NOW + 25 * HOUR, NOW)).toBe(false)
  expect(isWarning('five_hour', 95, NOW + 2 * HOUR, NOW)).toBe(true)
  expect(isWarning('five_hour', 95, NOW + 30 * MIN, NOW)).toBe(false)
  expect(isWarning('seven_day', 95, null, NOW)).toBe(true)
  expect(isWarning('seven_day_opus', 99, NOW + 25 * HOUR, NOW)).toBe(false)
})

test('the band: compact line with both meters, the pace mark and countdowns', () => {
  const band = bandFor(snap(), NOW, COMPACT)
  expect(band.kind).toBe('meters')

  if (band.kind !== 'meters') {
    return
  }

  expect(band.isDim).toBe(false)
  expect(band.meters.map(m => m.name)).toEqual(['Session', 'Weekly'])
  expect(band.meters.map(m => meterText(m, 'compact', 10)).join('   ')).toBe(
    'Session ▕█████│▏░░░▏ 61%  resets 2h 14m   Weekly ▕████████│░▏ 88%  resets Mon',
  )
  expect(bandFor(snap(), NOW, { ...COMPACT, bars: 'weekly' }).kind === 'meters' && bandFor(snap(), NOW, { ...COMPACT, bars: 'weekly' })).toMatchObject({
    meters: [{ name: 'Weekly' }],
  })
})

test('the band: full style adds pace words and the reset time', () => {
  const band = bandFor(snap(), NOW, FULL)
  const lines = band.kind === 'meters' ? band.meters.map(m => meterText(m, 'full', 20)) : []
  expect(lines).toEqual([
    'Session ▕███████████│▎░░░░░░░▏ 61%  5% ahead  resets in 2h 14m',
    'Weekly ▕█████████████████│░░▏ 88%  on pace  resets Mon 4:00 PM',
  ])
})

test('a warning limit carries the mark', () => {
  const tiers = [{ key: 'seven_day', resets_at: '2026-10-06T16:00:00Z', utilization: 93 }]
  const band = bandFor(snap({ tiers }), NOW, COMPACT)
  expect(band.kind === 'meters' && band.meters[0]?.isWarning).toBe(true)
  expect(band.kind === 'meters' && meterText(band.meters[0]!, 'compact', 10)).toBe('Weekly ▕███████│█▎▏ 93% ⚠  resets Tue')
})

test('stale dims with the age; dead, signed out, offline and newer are one line; nothing without a file', () => {
  expect(bandFor(snap({ at: NOW - 9 * MIN }), NOW, COMPACT)).toMatchObject({ kind: 'meters', isDim: true, note: '(9m ago)' })
  expect(bandFor(snap({ at: NOW - 22 * MIN }), NOW, COMPACT)).toEqual({ kind: 'line', text: 'Sanduhr: no update for 22m. Is the widget running?' })
  expect(bandFor(snap({ status: 'error', kind: 'session_expired', tiers: [] }), NOW, COMPACT)).toEqual({
    kind: 'line',
    text: 'Sanduhr: sign in to see your meters.',
  })
  expect(bandFor(snap({ status: 'error', kind: 'network', tiers: [] }), NOW, COMPACT)).toEqual({ kind: 'line', text: 'Sanduhr: offline.' })
  expect(bandFor(snap({ status: 'error', kind: 'session_expired' }), NOW, COMPACT)).toMatchObject({
    kind: 'meters',
    isDim: true,
    note: 'last known, sign in again',
  })
  expect(bandFor(parseSnapshot(file({ version: 2 })), NOW, COMPACT).kind).toBe('line')
  expect(bandFor(null, NOW, COMPACT)).toEqual({ kind: 'none' })
  expect(bandFor(snap({ tiers: [] }), NOW, COMPACT)).toEqual({ kind: 'none' })
})

test('a limit whose reset passed shows reset, never its stale percent', () => {
  const tiers = [{ key: 'five_hour', resets_at: new Date(NOW - MIN).toISOString(), utilization: 97 }]
  const band = bandFor(snap({ tiers }), NOW, COMPACT)
  expect(band.kind === 'meters' && meterText(band.meters[0]!, 'compact', 10)).toBe('Session reset')
})

test('warning toasts: one key per limit and reset window, none for an old or failed snapshot', () => {
  const tiers = [
    { key: 'seven_day', resets_at: '2026-10-06T16:00:00Z', utilization: 93 },
    { key: 'five_hour', resets_at: new Date(NOW + 3 * HOUR).toISOString(), utilization: 40 },
  ]
  const s = snap({ tiers })
  const keys = warningKeys(s, NOW)
  expect(keys).toEqual([`warn:seven_day@${Date.parse('2026-10-06T16:00:00Z')}`])
  expect(warningToast(s, keys[0]!, NOW, 0)).toBe('Sanduhr: the weekly limit is at 93%, resets Tue 4:00 PM.')
  expect(warningKeys(snap({ tiers, at: NOW - 30 * MIN }), NOW)).toEqual([])
  expect(warningKeys(snap({ tiers, status: 'error', kind: 'network' }), NOW)).toEqual([])
  expect(warningKeys(null, NOW)).toEqual([])
})

test('session reset: the window passed recently, seen in the snapshot or remembered', () => {
  const passed = NOW - 2 * MIN
  const crossed = snap({ tiers: [{ key: 'five_hour', resets_at: new Date(passed).toISOString(), utilization: 80 }] })
  const moved = snap({ tiers: [{ key: 'five_hour', resets_at: new Date(NOW + 5 * HOUR).toISOString(), utilization: 0 }] })
  expect(sessionReset(null, crossed, NOW)).toBe(passed)
  expect(sessionReset(passed, moved, NOW)).toBe(passed)
  expect(sessionReset(null, moved, NOW)).toBeNull()
  expect(sessionReset(NOW - RESET_TOAST_WINDOW_MS - 1, moved, NOW)).toBeNull()
})

test('remembered keys stay unique and capped', () => {
  expect(remember(['a', 'b'], 'a')).toEqual(['b', 'a'])
  expect(remember(['a', 'b', 'c'], 'd', 3)).toEqual(['b', 'c', 'd'])
})
