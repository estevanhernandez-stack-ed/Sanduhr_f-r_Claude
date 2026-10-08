import type { BandStyle, BandWatcher, LetterStyle, MetersBand, WatcherState } from '../types'

// The animated band's pure logic (items 65f and 66): band.json parsing, the letter styles and
// ink gradients of the statusline's Style popover, the moving light, when the band needs fast
// frames, and the watcher rows. No engine calls here, so every edge is a plain test.
//
// band.json is Sanduhr's (schema_version 1, next to snapshot.json): `meters.styles` (session,
// weekly, resets; the statusline's style grammar), `reduce_motion` (macOS's Reduce Motion), and
// `watchers` only while "Show watchers above the prompt" is on. Malformed parts are dropped.
//
// The motion follows the owner's now-playing mod: per-character colors, a three-character light
// brightening toward white, fast frames only while a light moves, else once a second.

export const BAND_SCHEMA_VERSION = 1
/** A frame while something moves: 12.5 a second, under the engine's 30 redraws a second. */
export const FAST_MS = 80
/** At rest: once a second (the watchers' clocks). */
export const SLOW_MS = 1000
/** One pass of the light. */
export const SWEEP_MS = 1100
/** Shimmer, glow and waiting pulses repeat this often. */
export const PERIOD_MS = 4000
/** A limit this close to its reset shimmers. */
export const SHIMMER_BEFORE_MS = 10 * 60_000
/** A passed or finished watcher fades over this long (the app drops it after the same six seconds). */
export const FADE_MS = 6000
/** Sanduhr rewrites band.json each minute while watchers show: older than this, it quit. */
export const STALE_MS = 3 * 60_000
/** Most watchers band.json may carry (the app keeps 24). */
export const MAX_WATCHERS = 24
/** Most watcher rows drawn; more fold into "+N more". */
export const MAX_ROWS = 6

const WHITE = '#ffffff'
/** What a light brightens when the text has no color of its own (dim text). */
export const PLAIN = '#9ca3af'

// -- band.json ---------------------------------------------------------------------------------

const HEX = /^#?([0-9a-fA-F]{3}|[0-9a-fA-F]{6})$/
const FONTS: readonly LetterStyle[] = ['bold', 'italic', 'bold-italic', 'script', 'fraktur', 'double-struck', 'sans', 'mono', 'small-caps']
const STYLE_KEYS = new Set(['ink', 'font', 'bold', 'italic', 'dim', 'underline'])
const STATES: readonly WatcherState[] = ['running', 'waiting', 'passed', 'failed', 'finished', 'lost_touch']
const TITLE_CAP = 80
const SHORT_CAP = 12

function isObject(v: unknown): v is Record<string, unknown> {
  return typeof v === 'object' && v !== null && !Array.isArray(v)
}

/** "#abc" and "abc" as "#aabbcc"; null for anything else. */
export function normalizeHex(v: unknown): string | null {
  if (typeof v !== 'string') {
    return null
  }

  const m = HEX.exec(v.trim())

  if (m === null) {
    return null
  }

  const h = (m[1] as string).toLowerCase()

  return `#${h.length === 3 ? h.split('').map(c => c + c).join('') : h}`
}

/** A style as the statusline's picks carry it, or null when it is anything else. */
export function parseStyle(v: unknown): BandStyle | null {
  if (!isObject(v) || Object.keys(v).some(k => !STYLE_KEYS.has(k))) {
    return null
  }

  let ink: string[] = []

  if (v.ink !== undefined) {
    if (!Array.isArray(v.ink) || v.ink.length < 1 || v.ink.length > 4) {
      return null
    }

    const colors = v.ink.map(normalizeHex)

    if (colors.some(c => c === null)) {
      return null
    }

    ink = colors as string[]
  }

  if (v.font !== undefined && !FONTS.includes(v.font as LetterStyle)) {
    return null
  }

  for (const k of ['bold', 'italic', 'dim', 'underline']) {
    if (v[k] !== undefined && typeof v[k] !== 'boolean') {
      return null
    }
  }

  return {
    ink,
    font: (v.font as LetterStyle | undefined) ?? null,
    bold: v.bold === true,
    italic: v.italic === true,
    dim: v.dim === true,
    underline: v.underline === true,
  }
}

function parseTime(v: unknown): number | null {
  if (typeof v !== 'string' || v.trim() === '') {
    return null
  }

  const ms = Date.parse(v.trim().replace(/(\.\d{3})\d+/, '$1'))

  return Number.isNaN(ms) ? null : ms
}

/** One line of text, no control characters, at most `cap` characters; null when empty. */
function line(v: unknown, cap: number): string | null {
  if (typeof v !== 'string') {
    return null
  }

  const flat = Array.from(v.replace(/[\u0000-\u001f\u007f-\u009f]/g, ' ').trim())

  if (flat.length === 0) {
    return null
  }

  return flat.length > cap ? `${flat.slice(0, cap - 1).join('').trim()}…` : flat.join('')
}

function count(v: unknown): number | null {
  return typeof v === 'number' && Number.isInteger(v) && v >= 0 && v <= 1_000_000 ? v : null
}

/** One watcher row, or null when it is malformed (a row the band can't trust is left out). */
export function parseWatcher(v: unknown): BandWatcher | null {
  if (!isObject(v) || !STATES.includes(v.state as WatcherState)) {
    return null
  }

  const state = v.state as WatcherState

  if (v.source === 'automatic') {
    const kind = line(v.kind, 20)

    return kind === null ? null : { source: 'automatic', kind, state }
  }

  if (v.source !== 'agent') {
    return null
  }

  const title = line(v.title, TITLE_CAP)

  if (title === null) {
    return null
  }

  const total = count(v.total)
  const done = total === null ? null : Math.min(count(v.done) ?? 0, total)

  return {
    source: 'agent',
    title,
    short: line(v.short, SHORT_CAP),
    state,
    done,
    total,
    startedAt: parseTime(v.started_at),
    endedAt: parseTime(v.ended_at),
  }
}

/** band.json's text, or null for anything missing, malformed or of a newer schema. */
export function parseBand(text: string): MetersBand | null {
  let raw: unknown

  try {
    raw = JSON.parse(text)
  } catch {
    return null
  }

  if (!isObject(raw) || raw.schema_version !== BAND_SCHEMA_VERSION) {
    return null
  }

  const writtenAt = parseTime(raw.written_at)

  if (writtenAt === null) {
    return null
  }

  const styles: MetersBand['styles'] = {}
  const all = isObject(raw.meters) && isObject(raw.meters.styles) ? raw.meters.styles : {}

  for (const key of ['session', 'weekly', 'resets'] as const) {
    const s = parseStyle(all[key])

    if (s !== null) {
      styles[key] = s
    }
  }

  let watchers: BandWatcher[] | null = null

  if (Array.isArray(raw.watchers)) {
    watchers = raw.watchers.slice(0, MAX_WATCHERS).map(parseWatcher).filter((w): w is BandWatcher => w !== null)
  }

  return { writtenAt, reduceMotion: raw.reduce_motion === true, styles, watchers }
}

// -- Letters and colors -----------------------------------------------------------------------

// (capital A, small a, digit 0 or null, the letters the block leaves out), as the statusline.
const BLOCKS: Record<Exclude<LetterStyle, 'small-caps'>, [number, number, number | null, Record<string, number>]> = {
  bold: [0x1d400, 0x1d41a, 0x1d7ce, {}],
  italic: [0x1d434, 0x1d44e, null, { h: 0x210e }],
  'bold-italic': [0x1d468, 0x1d482, null, {}],
  script: [0x1d49c, 0x1d4b6, null, { B: 0x212c, E: 0x2130, F: 0x2131, H: 0x210b, I: 0x2110, L: 0x2112, M: 0x2133, R: 0x211b, e: 0x212f, g: 0x210a, o: 0x2134 }],
  fraktur: [0x1d504, 0x1d51e, null, { C: 0x212d, H: 0x210c, I: 0x2111, R: 0x211c, Z: 0x2128 }],
  'double-struck': [0x1d538, 0x1d552, 0x1d7d8, { C: 0x2102, H: 0x210d, N: 0x2115, P: 0x2119, Q: 0x211a, R: 0x211d, Z: 0x2124 }],
  sans: [0x1d5a0, 0x1d5ba, 0x1d7e2, {}],
  mono: [0x1d670, 0x1d68a, 0x1d7f6, {}],
}
const SMALL_CAPS = 'ᴀʙᴄᴅᴇꜰɢʜɪᴊᴋʟᴍɴᴏᴘꞯʀꜱᴛᴜᴠᴡxʏᴢ'

/** `ch` in the letter style `font`; anything the style has no form for stays as it is. */
export function letter(ch: string, font: LetterStyle | null): string {
  if (font === null || !/^[A-Za-z0-9]$/.test(ch)) {
    return ch
  }

  if (font === 'small-caps') {
    return /[a-z]/.test(ch) ? (Array.from(SMALL_CAPS)[ch.charCodeAt(0) - 97] as string) : ch
  }

  const [upper, lower, digit, holes] = BLOCKS[font]
  const hole = holes[ch]

  if (hole !== undefined) {
    return String.fromCodePoint(hole)
  }

  const code = ch.charCodeAt(0)

  if (ch >= 'A' && ch <= 'Z') {
    return String.fromCodePoint(upper + code - 65)
  }

  if (ch >= 'a' && ch <= 'z') {
    return String.fromCodePoint(lower + code - 97)
  }

  return digit === null ? ch : String.fromCodePoint(digit + code - 48)
}

/** `text` as characters (code points) in `font`. */
export function letters(text: string, font: LetterStyle | null): string[] {
  return Array.from(text).map(ch => letter(ch, font))
}

function rgb(hex: string): [number, number, number] {
  const h = (normalizeHex(hex) ?? '#ffffff').slice(1)

  return [0, 2, 4].map(i => parseInt(h.slice(i, i + 2), 16)) as [number, number, number]
}

function toHex(c: readonly number[]): string {
  return `#${c.map(v => Math.max(0, Math.min(255, Math.round(v))).toString(16).padStart(2, '0')).join('')}`
}

/** `a` moved toward `b` by `t` (0..1). */
export function mix(a: string, b: string, t: number): string {
  const x = rgb(a)
  const y = rgb(b)
  const k = Math.max(0, Math.min(1, t))

  return toHex(x.map((v, i) => v + ((y[i] as number) - v) * k))
}

/** `n` colors along the stops of `ink` (one color: all the same), as the statusline draws them. */
export function gradient(ink: readonly string[], n: number): string[] {
  if (ink.length === 0 || n <= 0) {
    return []
  }

  if (ink.length === 1 || n === 1) {
    return Array.from({ length: n }, () => normalizeHex(ink[0]) ?? WHITE)
  }

  return Array.from({ length: n }, (_, i) => {
    const t = (i / (n - 1)) * (ink.length - 1)
    const k = Math.min(Math.floor(t), ink.length - 2)

    return mix(ink[k] as string, ink[k + 1] as string, t - k)
  })
}

// -- The light ---------------------------------------------------------------------------------

/**
 * How bright character `i` of `n` is while a light `t` (0..1) of the way through its pass: a
 * three-character window, brightest at its center, travelling from before the first character
 * to past the last.
 */
export function lightAt(i: number, n: number, t: number): number {
  const center = -1 + t * (n + 1)

  return Math.max(0, 1 - Math.abs(i - center) / 2)
}

/** How far through a one-shot pass that started at `start`, or null when it isn't moving. */
export function onceProgress(start: number | undefined, now: number): number | null {
  if (start === undefined || now < start || now - start >= SWEEP_MS) {
    return null
  }

  return (now - start) / SWEEP_MS
}

/** How far through the repeating pass (every PERIOD_MS, SWEEP_MS long), or null between passes. */
export function pulseProgress(now: number): number | null {
  const phase = ((now % PERIOD_MS) + PERIOD_MS) % PERIOD_MS

  return phase < SWEEP_MS ? phase / SWEEP_MS : null
}

/** A glow's strength through a pass: up and back down. */
export function glowLevel(t: number | null): number {
  return t === null ? 0 : Math.sin(Math.PI * t) * 0.6
}

export type Run = { text: string; color: string | null }

/**
 * Characters with their base colors (null: the terminal's own), brightened toward white by a
 * light at `t` across them and a glow `glow` over all; neighbours of one color merge. A light
 * that crosses several pieces (a bar, then its percent) names where this piece starts in it
 * (`offset`) and its whole length (`span`).
 */
export function paint(
  chars: readonly string[],
  base: readonly (string | null)[],
  t: number | null,
  glow = 0,
  offset = 0,
  span = chars.length,
): Run[] {
  const runs: Run[] = []

  chars.forEach((ch, i) => {
    const own = base[i] ?? null
    const light = Math.max(t === null ? 0 : lightAt(i + offset, span, t) * 0.85, glow)
    const color = light > 0.01 ? mix(own ?? PLAIN, WHITE, light) : own
    const last = runs[runs.length - 1]

    if (last !== undefined && last.color === color) {
      last.text += ch
    } else {
      runs.push({ text: ch, color })
    }
  })

  return runs
}

// -- Warning lines and when the band moves ----------------------------------------------------

/** Which side of the warning lines a percent is on (the widget's colors: 50, 75, 90). */
export function level(pct: number): number {
  return pct >= 90 ? 3 : pct >= 75 ? 2 : pct >= 50 ? 1 : 0
}

/** The limits whose percent crossed a warning line upward between two readings. */
export function crossings(before: Record<string, number>, after: Record<string, number>): string[] {
  return Object.keys(after).filter(k => before[k] !== undefined && level(after[k] as number) > level(before[k] as number))
}

/** What may move in the band now. */
export type Movers = {
  /** When each one-shot sweep started (a meter crossed a warning line). */
  sweeps: number[]
  /** Something pulses on the period: a limit about to reset, a nearly full one, a watcher waiting on you. */
  pulses: boolean
  /** When each passed or finished watcher ended (it fades). */
  fades: number[]
}

/** Whether a light, a pulse or a fade is under way at `now`. */
export function isMoving(m: Movers, now: number): boolean {
  return (
    m.sweeps.some(s => onceProgress(s, now) !== null) ||
    (m.pulses && pulseProgress(now) !== null) ||
    m.fades.some(e => now >= e && now - e < FADE_MS)
  )
}

/** How long until the next frame: fast while something moves, else once a second or sooner when a pulse is due. */
export function nextFrameIn(m: Movers, now: number, motion: boolean): number {
  if (!motion) {
    return SLOW_MS
  }

  if (isMoving(m, now)) {
    return FAST_MS
  }

  let wait = SLOW_MS

  if (m.pulses) {
    const phase = ((now % PERIOD_MS) + PERIOD_MS) % PERIOD_MS
    wait = Math.min(wait, PERIOD_MS - phase)
  }

  for (const s of [...m.sweeps, ...m.fades]) {
    if (s > now) {
      wait = Math.min(wait, s - now)
    }
  }

  return Math.max(FAST_MS, wait)
}

// -- Watcher rows ------------------------------------------------------------------------------

export const MARKS: Record<WatcherState, string> = {
  running: '●',
  waiting: '◉',
  passed: '✓',
  failed: '⚠',
  finished: '✓',
  lost_touch: '○',
}

export const MARK_COLORS: Record<WatcherState, string> = {
  running: '#38bdf8',
  waiting: '#fbbf24',
  passed: '#4ade80',
  failed: '#f87171',
  finished: '#9ca3af',
  lost_touch: '#6b7280',
}

export type WatcherRow = {
  key: string
  state: WatcherState
  mark: string
  /** The mark's color this frame (pulsing, fading). */
  color: string
  /** The title's color this frame, null for the terminal's own. */
  titleColor: string | null
  isDim: boolean
  text: string
  elapsed: string | null
  progress: string | null
}

/** "12s", "4m", "1h 05m", as the notch and the Desk write it. */
export function elapsedWords(ms: number): string {
  const s = Math.floor(Math.max(0, ms) / 1000)

  if (s < 60) {
    return `${s}s`
  }

  if (s < 3600) {
    return `${Math.floor(s / 60)}m`
  }

  return `${Math.floor(s / 3600)}h ${String(Math.floor((s % 3600) / 60)).padStart(2, '0')}m`
}

/** The watchers the band shows now: none from an old file (Sanduhr quit) or with the switch off. */
export function liveWatchers(band: MetersBand | null, now: number): BandWatcher[] {
  if (band === null || band.watchers === null || now - band.writtenAt > STALE_MS) {
    return []
  }

  return band.watchers
}

/** One row per watcher, as drawn at `now`; `wide` writes the whole title, else the short one. */
export function watcherRows(watchers: readonly BandWatcher[], now: number, opts: { wide: boolean; motion: boolean }): WatcherRow[] {
  return watchers.map((w, i) => {
    const state = w.state
    let color = MARK_COLORS[state]
    let titleColor: string | null = null
    let isDim = state === 'lost_touch' || state === 'finished'
    const ended = w.source === 'agent' ? w.endedAt : null

    if (state === 'waiting') {
      const glow = opts.motion ? glowLevel(pulseProgress(now)) : 0
      color = mix(MARK_COLORS.waiting, WHITE, glow)
      titleColor = glow > 0.01 ? mix(MARK_COLORS.waiting, WHITE, glow) : MARK_COLORS.waiting
    } else if (state === 'failed') {
      titleColor = MARK_COLORS.failed
    } else if ((state === 'passed' || state === 'finished') && ended !== null && opts.motion) {
      // The check holds a moment, then fades toward grey before Sanduhr drops it.
      const t = Math.max(0, Math.min(1, (now - ended - 1000) / (FADE_MS - 1000)))
      color = mix(MARK_COLORS[state], '#4b5563', t)
      isDim = isDim || t >= 0.5
    }

    if (w.source === 'automatic') {
      return { key: `w${i}`, state, mark: MARKS[state], color, titleColor, isDim, text: `background ${w.kind}`, elapsed: null, progress: null }
    }

    const text = opts.wide || w.short === null ? w.title : w.short
    const elapsed = w.startedAt === null ? null : elapsedWords((w.endedAt ?? now) - w.startedAt)
    const progress = w.total === null ? null : `${w.done ?? 0}/${w.total}`

    return { key: `w${i}`, state, mark: MARKS[state], color, titleColor, isDim, text, elapsed, progress }
  })
}

/** What moves among the watchers: waiting pulses, passed and finished fade. */
export function watcherMovers(watchers: readonly BandWatcher[]): { pulses: boolean; fades: number[] } {
  return {
    pulses: watchers.some(w => w.state === 'waiting'),
    fades: watchers.flatMap(w => (w.source === 'agent' && (w.state === 'passed' || w.state === 'finished') && w.endedAt !== null ? [w.endedAt] : [])),
  }
}
