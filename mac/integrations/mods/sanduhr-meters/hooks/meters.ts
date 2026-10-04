import type { MetersSnapshot, MetersTier } from '../types'

// The band's pure logic: snapshot.json parsing, freshness, bars, countdowns, warnings and the
// toast decisions. No engine calls here, so every edge is a plain test.
//
// The snapshot contract is Sanduhr's (schema_version 1): the macOS widget writes
// ~/Library/Application Support/Sanduhr/snapshot.json, the Windows widget %APPDATA%\Sanduhr\snapshot.json.
// Freshness bands, the reset-crossed rule and the newer-schema refusal follow the statusline
// script; the pace math and the bar colors follow the widget, so the band shows its numbers.

export const SCHEMA_VERSION = 1
// Fresh below 1.5x the widget's 5-minute fetch, stale up to two missed polls, dead beyond.
export const FRESH_SECONDS = 450
export const DEAD_SECONDS = 900

export const SESSION = 'five_hour'
export const WEEKLY = 'seven_day'

const HOUR = 3600_000
const DAY = 24 * HOUR
const TOTAL_MS: Record<string, number> = { [SESSION]: 5 * HOUR, [WEEKLY]: 7 * DAY }
const NAMES: Record<string, string> = { [SESSION]: 'Session', [WEEKLY]: 'Weekly' }

// The widget's meter warning (MeterWarning): nearly full while the reset is still far off.
// Weekly: 90% with more than a day to go (the widget's default). Session: 90% with more than an
// hour to go (the widget's rule for the session limit when switched on).
export const WARN_PERCENT = 90
const WARN_MIN_RESET: Record<string, number> = { [SESSION]: HOUR, [WEEKLY]: DAY }

// A session reset older than this is old news: no toast.
export const RESET_TOAST_WINDOW_MS = 10 * 60_000

export type Bars = 'both' | 'session' | 'weekly'
export type Style = 'compact' | 'full'

export type SnapshotTier = MetersTier
export type Snapshot = MetersSnapshot

/** An ISO-8601 time in ms, or null. Fractions are cut to milliseconds (.NET writes seven digits). */
export function parseTime(value: unknown): number | null {
  if (typeof value !== 'string' || value.trim() === '') {
    return null
  }

  const s = value.trim().replace(/(\.\d{3})\d+/, '$1')
  const ms = Date.parse(s)

  return Number.isNaN(ms) ? null : ms
}

/** snapshot.json's text, or null for anything missing or malformed (the uninstalled look). */
export function parseSnapshot(text: string): Snapshot | null {
  let raw: unknown

  try {
    raw = JSON.parse(text)
  } catch {
    return null
  }

  if (typeof raw !== 'object' || raw === null || Array.isArray(raw)) {
    return null
  }

  const o = raw as Record<string, unknown>
  const version = Number(o.schema_version)

  if (!Number.isFinite(version)) {
    return null
  }

  // A newer major is refused, never read best effort: wrong numbers are the failure to avoid.
  if (version > SCHEMA_VERSION) {
    return { kind: 'newer' }
  }

  const capturedAt = parseTime(o.captured_at)

  if (capturedAt === null) {
    return null
  }

  const tiers: SnapshotTier[] = []

  for (const t of Array.isArray(o.tiers) ? o.tiers : []) {
    if (typeof t !== 'object' || t === null) {
      continue
    }

    const r = t as Record<string, unknown>
    const util = typeof r.utilization === 'number' && Number.isFinite(r.utilization) ? Math.trunc(r.utilization) : null
    tiers.push({ key: String(r.key ?? ''), utilization: util, resetsAt: parseTime(r.resets_at) })
  }

  return {
    kind: 'snapshot',
    capturedAt,
    status: o.status === 'error' ? 'error' : 'ok',
    errorKind: typeof o.error_kind === 'string' ? o.error_kind : null,
    tiers,
  }
}

export type Freshness = 'fresh' | 'stale' | 'dead'

/** The statusline's bands; a snapshot from the future is fresh. */
export function freshness(capturedAt: number, now: number): Freshness {
  const age = Math.max(0, (now - capturedAt) / 1000)

  if (age < FRESH_SECONDS) {
    return 'fresh'
  }

  return age <= DEAD_SECONDS ? 'stale' : 'dead'
}

/** The fraction of the period gone (0..1), or null without a reset time. */
export function paceFraction(key: string, resetsAt: number | null, now: number): number | null {
  const total = TOTAL_MS[key]

  if (resetsAt === null || total === undefined) {
    return null
  }

  const rem = Math.max(0, resetsAt - now)

  return Math.min(1, Math.max(0, (total - rem) / total))
}

/** The widget's pace words: on pace within 5 points, else how far ahead or under. */
export function paceWords(pct: number, fraction: number | null): string | null {
  if (fraction === null) {
    return null
  }

  const diff = pct - fraction * 100

  if (Math.abs(diff) < 5) {
    return 'on pace'
  }

  return `${Math.trunc(Math.abs(diff))}% ${diff > 0 ? 'ahead' : 'under'}`
}

export function isWarning(key: string, pct: number, resetsAt: number | null, now: number): boolean {
  const minReset = WARN_MIN_RESET[key]

  if (minReset === undefined || pct < WARN_PERCENT) {
    return false
  }

  return resetsAt === null || resetsAt - now > minReset
}

/** The widget's usage colors: green, yellow, orange, then red from 90%. */
export function usageColor(pct: number): string {
  if (pct < 50) {
    return '#4ade80'
  }

  if (pct < 75) {
    return '#facc15'
  }

  return pct < 90 ? '#fb923c' : '#f87171'
}

export const PACE_COLOR = '#f472b6'

export type BarRun = { text: string; part: 'fill' | 'pace' | 'empty' }

const EIGHTHS = ['', '▏', '▎', '▍', '▌', '▋', '▊', '▉']

/** A bar `width` cells wide: full blocks, one partial eighth, the pace mark, and the rest empty. */
export function bar(pct: number, fraction: number | null, width: number): BarRun[] {
  const cells: { ch: string; part: BarRun['part'] }[] = []
  const filled = (Math.min(100, Math.max(0, pct)) / 100) * width
  const full = Math.floor(filled)
  const eighth = Math.round((filled - full) * 8)

  for (let i = 0; i < width; i += 1) {
    if (i < full) {
      cells.push({ ch: '█', part: 'fill' })
    } else if (i === full && eighth === 8) {
      cells.push({ ch: '█', part: 'fill' })
    } else if (i === full && eighth > 0) {
      cells.push({ ch: EIGHTHS[eighth] as string, part: 'fill' })
    } else {
      cells.push({ ch: '░', part: 'empty' })
    }
  }

  if (fraction !== null) {
    const at = Math.min(width - 1, Math.max(0, Math.floor(fraction * width)))
    cells[at] = { ch: '│', part: 'pace' }
  }

  const runs: BarRun[] = []

  for (const c of cells) {
    const last = runs[runs.length - 1]

    if (last !== undefined && last.part === c.part) {
      last.text += c.ch
    } else {
      runs.push({ text: c.ch, part: c.part })
    }
  }

  return runs
}

/** "2d 3h", "2h 14m", "14m", or "now". */
export function countdown(ms: number): string {
  const minutes = Math.floor(Math.max(0, ms) / 60_000)

  if (minutes <= 0) {
    return 'now'
  }

  const d = Math.floor(minutes / 1440)
  const h = Math.floor((minutes % 1440) / 60)
  const m = minutes % 60

  if (d > 0) {
    return h > 0 ? `${d}d ${h}h` : `${d}d`
  }

  return h > 0 ? `${h}h ${m}m` : `${m}m`
}

const DAYS = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat']

/** The local time of `at`, "Mon 9:00 AM", given the host's `Date#getTimezoneOffset()`. */
export function clockTime(at: number, tzOffsetMinutes: number, withDay: boolean): string {
  const local = new Date(at - tzOffsetMinutes * 60_000)
  const h24 = local.getUTCHours()
  const h12 = h24 % 12 === 0 ? 12 : h24 % 12
  const mm = String(local.getUTCMinutes()).padStart(2, '0')
  const time = `${h12}:${mm} ${h24 < 12 ? 'AM' : 'PM'}`

  return withDay ? `${DAYS[local.getUTCDay()]} ${time}` : time
}

/** When the limit resets, in the band's words: a countdown inside a day, else the day. */
export function resetWords(resetsAt: number | null, now: number, style: Style, tzOffsetMinutes: number): string | null {
  if (resetsAt === null) {
    return null
  }

  const left = resetsAt - now

  if (left > DAY) {
    return style === 'full'
      ? `resets ${clockTime(resetsAt, tzOffsetMinutes, true)}`
      : `resets ${DAYS[new Date(resetsAt - tzOffsetMinutes * 60_000).getUTCDay()]}`
  }

  return style === 'full' ? `resets in ${countdown(left)}` : `resets ${countdown(left)}`
}

export type Meter = {
  key: string
  name: string
  // The limit's reset has passed: the stored percent is wrong until Sanduhr fetches again.
  isCrossed: boolean
  pct: number
  color: string
  fraction: number | null
  pace: string | null
  isWarning: boolean
  reset: string | null
}

export type Band =
  | { kind: 'none' }
  | { kind: 'line'; text: string }
  | { kind: 'meters'; meters: Meter[]; isDim: boolean; note: string | null }

export type BandOptions = { bars: Bars; style: Style; tzOffsetMinutes: number }

const ERROR_WORDS: Record<string, string> = { session_expired: 'sign in again', cloudflare: 'blocked, sign in again' }

function wanted(key: string, bars: Bars): boolean {
  return key === SESSION ? bars !== 'weekly' : key === WEEKLY ? bars !== 'session' : false
}

/** What the band shows for a snapshot at `now`. Nothing at all without a snapshot. */
export function bandFor(snap: Snapshot | null, now: number, opts: BandOptions): Band {
  if (snap === null) {
    return { kind: 'none' }
  }

  if (snap.kind === 'newer') {
    return { kind: 'line', text: 'Sanduhr: this snapshot is newer than the meters mod. Update the mod from Sanduhr.' }
  }

  const age = freshness(snap.capturedAt, now)

  if (age === 'dead') {
    const minutes = Math.floor((now - snap.capturedAt) / 60_000)

    return { kind: 'line', text: `Sanduhr: no update for ${minutes}m. Is the widget running?` }
  }

  const meters: Meter[] = []

  for (const t of snap.tiers) {
    if (!wanted(t.key, opts.bars) || t.utilization === null) {
      continue
    }

    const isCrossed = t.resetsAt !== null && t.resetsAt <= now
    const fraction = isCrossed ? null : paceFraction(t.key, t.resetsAt, now)
    meters.push({
      key: t.key,
      name: NAMES[t.key] ?? t.key,
      isCrossed,
      pct: t.utilization,
      color: usageColor(t.utilization),
      fraction,
      pace: isCrossed ? null : paceWords(t.utilization, fraction),
      isWarning: !isCrossed && snap.status === 'ok' && isWarning(t.key, t.utilization, t.resetsAt, now),
      reset: isCrossed ? null : resetWords(t.resetsAt, now, opts.style, opts.tzOffsetMinutes),
    })
  }

  if (snap.status === 'error') {
    const words = ERROR_WORDS[snap.errorKind ?? ''] ?? 'offline'

    // Signed out (or the session expired) with nothing kept: one line, no old numbers.
    if (snap.tiers.length === 0) {
      return { kind: 'line', text: snap.errorKind === 'session_expired' ? 'Sanduhr: sign in to see your meters.' : `Sanduhr: ${words}.` }
    }

    return meters.length === 0 ? { kind: 'line', text: `Sanduhr: ${words}.` } : { kind: 'meters', meters, isDim: true, note: `last known, ${words}` }
  }

  if (meters.length === 0) {
    return { kind: 'none' }
  }

  if (age === 'stale') {
    return { kind: 'meters', meters, isDim: true, note: `(${Math.floor((now - snap.capturedAt) / 60_000)}m ago)` }
  }

  return { kind: 'meters', meters, isDim: false, note: null }
}

/** A meter as one plain line, the words the band draws (tests and the compact row read this). */
export function meterText(m: Meter, style: Style, width: number): string {
  if (m.isCrossed) {
    return `${m.name} reset`
  }

  const cells = bar(m.pct, m.fraction, width).map(r => r.text).join('')
  const parts = [`${m.name} ▕${cells}▏ ${m.pct}%${m.isWarning ? ' ⚠' : ''}`]

  if (style === 'full' && m.pace !== null) {
    parts.push(m.pace)
  }

  if (m.reset !== null) {
    parts.push(m.reset)
  }

  return parts.join('  ')
}

// -- Toasts -----------------------------------------------------------------------------------

/** One key per limit and reset window that warns now: a toast is due once per key. */
export function warningKeys(snap: Snapshot | null, now: number): string[] {
  if (snap === null || snap.kind !== 'snapshot' || snap.status !== 'ok' || freshness(snap.capturedAt, now) === 'dead') {
    return []
  }

  return snap.tiers
    .filter(t => t.utilization !== null && !(t.resetsAt !== null && t.resetsAt <= now))
    .filter(t => isWarning(t.key, t.utilization as number, t.resetsAt, now))
    .map(t => `warn:${t.key}@${t.resetsAt ?? 'none'}`)
}

export function warningToast(snap: Snapshot, key: string, now: number, tzOffsetMinutes: number): string | null {
  if (snap.kind !== 'snapshot') {
    return null
  }

  const t = snap.tiers.find(x => `warn:${x.key}@${x.resetsAt ?? 'none'}` === key)

  if (t === undefined || t.utilization === null) {
    return null
  }

  const reset = resetWords(t.resetsAt, now, 'full', tzOffsetMinutes)
  const name = (NAMES[t.key] ?? t.key).toLowerCase()

  return `Sanduhr: the ${name} limit is at ${t.utilization}%${reset === null ? '' : `, ${reset}`}.`
}

/**
 * The session reset that just passed, or null: the snapshot's own reset time crossed, or the
 * last one seen crossed and the snapshot already moved on to the next window. Older than ten
 * minutes is old news.
 */
export function sessionReset(lastSeen: number | null, snap: Snapshot | null, now: number): number | null {
  const current = snap !== null && snap.kind === 'snapshot' ? snap.tiers.find(t => t.key === SESSION)?.resetsAt ?? null : null
  const candidates = [current, lastSeen].filter((x): x is number => x !== null && x <= now && now - x <= RESET_TOAST_WINDOW_MS)

  return candidates.length === 0 ? null : Math.max(...candidates)
}

export const RESET_TOAST = 'Sanduhr: the session limit has reset.'

/** The keys remembered as toasted, newest last, at most `cap`. */
export function remember(seen: readonly string[], key: string, cap = 24): string[] {
  return [...seen.filter(k => k !== key), key].slice(-cap)
}
