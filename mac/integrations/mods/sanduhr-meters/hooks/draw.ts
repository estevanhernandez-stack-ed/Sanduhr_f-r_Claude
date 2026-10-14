import type { BandStyle, MetersBand, MetersSnapshot } from '../types'
import {
  MAX_ROWS,
  SHIMMER_BEFORE_MS,
  glowLevel,
  gradient,
  letters,
  liveWatchers,
  mix,
  onceProgress,
  paint,
  pulseProgress,
  watcherRows,
} from './band'
import type { Run, WatcherRow } from './band'
import { PACE_COLOR, SESSION, WEEKLY, bandFor, bar } from './meters'
import type { Band, Bars, Meter, Style } from './meters'

// What the band draws in one frame, as plain data (item 75, slice B): every decision the drawing
// makes (full or compact, bar width, colors, letters, the light, the glow, the shimmer, watcher
// rows) lives here, and register.tsx only turns it into Box and Text. Sanduhr's Mod Studio draws
// its preview from a Swift port of this file; fixtures/generate.ts writes cases from it that the
// Swift tests replay, so the two can't drift.

export const WARN_RED = '#f87171'
export const BAR_WIDTH: Record<Style, number> = { compact: 10, full: 20 }
export const NARROW_BAR = 6
/** Below this many columns a watcher shows its short title. */
export const WIDE_ROW = 60
/** Full style needs this many columns, else it folds to one line. */
export const FULL_COLUMNS = 64
/** Below this many columns a compact bar narrows. */
export const NARROW_COLUMNS = 80

/** One run of characters as drawn: its color (null: the terminal's own) and attributes. */
export type Cell = { text: string; color: string | null; bold: boolean; italic: boolean; underline: boolean; dim: boolean }

/** The Text attributes a style turns on. */
export type Attrs = { bold: boolean; italic: boolean; underline: boolean; dim: boolean }

export function attrsOf(style: BandStyle | undefined, bold = false): Attrs {
  return { bold: bold || style?.bold === true, italic: style?.italic === true, underline: style?.underline === true, dim: style?.dim === true }
}

/** Runs as cells in a style's attributes; a run without a color is dim when `dimBare`. */
export function cells(runs: Run[], a: Attrs, dimBare: boolean): Cell[] {
  return runs.map(r => ({ text: r.text, color: r.color, bold: a.bold, italic: a.italic, underline: a.underline, dim: a.dim || (r.color === null && dimBare) }))
}

/** Plain text, dim or not, in the terminal's own color. */
export function plainCell(text: string, dim: boolean, color: string | null = null): Cell {
  return { text, color, bold: false, italic: false, underline: false, dim }
}

/** `text` in `style`'s letters and ink (else `fallback`), lit by `t` and `glow`. */
export function styled(text: string, style: BandStyle | undefined, fallback: string | null, t: number | null, glow: number): Run[] {
  const chars = letters(text, style?.font ?? null)
  const base = style !== undefined && style.ink.length > 0 ? gradient(style.ink, chars.length) : chars.map(() => fallback)

  return paint(chars, base, t, glow)
}

export function styleFor(look: MetersBand | null, key: string): BandStyle | undefined {
  return key === SESSION ? look?.styles.session : key === WEEKLY ? look?.styles.weekly : undefined
}

export function isShimmering(m: Meter): boolean {
  return m.resetLeft !== null && m.resetLeft <= SHIMMER_BEFORE_MS
}

/** One meter's pieces, left to right, drawn one column apart. The bar piece carries its brackets. */
export type MeterRow = { key: string; pieces: Cell[][] }

export type MeterDraw = { barWidth: number; plain: boolean; lit: boolean; full: boolean; now: number; sweepStart: number | undefined }

export function meterRow(m: Meter, look: MetersBand | null, d: MeterDraw): MeterRow {
  const style = styleFor(look, m.key)

  if (m.isCrossed) {
    return { key: m.key, pieces: [cells(styled(m.name, style, null, null, 0), attrsOf(style), true), [plainCell('reset', true)]] }
  }

  const name = cells(styled(m.name, style, null, null, 0), attrsOf(style), d.plain)
  const tone = m.isWarning ? WARN_RED : m.color
  const barCells = bar(m.pct, m.fraction, d.barWidth).flatMap(r =>
    Array.from(r.text).map(ch => ({ ch, color: d.plain ? null : r.part === 'fill' ? tone : r.part === 'pace' ? PACE_COLOR : null })),
  )
  const pctChars = letters(`${m.pct}%`, style?.font ?? null)
  const span = barCells.length + pctChars.length
  const sweep = d.lit ? onceProgress(d.sweepStart, d.now) : null
  const glow = d.lit && m.isWarning ? glowLevel(pulseProgress(d.now)) : 0
  const barRuns = paint(barCells.map(c => c.ch), barCells.map(c => c.color), sweep, 0, 0, span)
  const pctBase = d.plain ? pctChars.map(() => null) : style !== undefined && style.ink.length > 0 ? gradient(style.ink, pctChars.length) : pctChars.map(() => tone)
  const pctRuns = paint(pctChars, pctBase, sweep, glow, barCells.length, span)
  const resets = look?.styles.resets
  const shimmer = d.lit && isShimmering(m) ? pulseProgress(d.now) : null

  const pieces: Cell[][] = [
    name,
    [plainCell('▕', true), ...barRuns.map(r => plainCell(r.text, d.plain || r.color === null, r.color)), plainCell('▏', true)],
    cells(pctRuns, attrsOf(style, m.isWarning), d.plain),
  ]

  if (m.isWarning) {
    pieces.push([plainCell('⚠', false, mix(WARN_RED, '#ffffff', glow))])
  }

  if (d.full && m.pace !== null) {
    pieces.push([plainCell(m.pace, true)])
  }

  if (m.reset !== null) {
    pieces.push(cells(styled(m.reset, resets, null, shimmer, 0), attrsOf(resets), true))
  }

  return { key: m.key, pieces }
}

/** One watcher row's pieces, drawn one column apart; the last row carries "+N more". */
export function watcherPieces(r: WatcherRow, more: number): Cell[][] {
  const pieces: Cell[][] = [[plainCell(r.mark, false, r.color)], [plainCell(r.text, r.isDim, r.titleColor)]]

  if (r.elapsed !== null) {
    pieces.push([plainCell(r.elapsed, true)])
  }

  if (r.progress !== null) {
    pieces.push([plainCell(r.progress, r.isDim)])
  }

  if (more > 0) {
    pieces.push([plainCell(`+${more} more`, true)])
  }

  return pieces
}

export type FrameInput = {
  snap: MetersSnapshot | null
  look: MetersBand | null
  now: number
  bars: Bars
  style: Style
  label: string
  /** The motion option and band.json's Reduce Motion together. */
  moving: boolean
  width: number
  maxRows: number
  tzOffsetMinutes: number
  seat: boolean
  /** When each limit's one-shot sweep started. */
  sweeps: Record<string, number>
}

/** The band's meters part: nothing, one line of words, or meters in one line (compact) or a row each (full). */
export type MetersPart =
  | { kind: 'none' }
  | { kind: 'line'; text: string }
  | { kind: 'compact' | 'full'; head: string | null; note: string | null; rows: MeterRow[] }

export type Frame = { band: Band; shown: Style; meters: MetersPart; watchers: Cell[][][] }

/** Everything one frame draws, as data. */
export function frameFor(i: FrameInput): Frame {
  const opts = (s: Style) => ({ bars: i.bars, style: s, tzOffsetMinutes: i.tzOffsetMinutes, seat: i.seat })
  // Full style takes a row per meter; it folds to one line where the rows or columns are short.
  const full = bandFor(i.snap, i.now, opts('full'))
  const fullFits = full.kind !== 'meters' || (full.meters.length <= i.maxRows && i.width >= FULL_COLUMNS)
  const shown: Style = i.style === 'full' && fullFits ? 'full' : 'compact'
  const band = shown === 'full' ? full : bandFor(i.snap, i.now, opts('compact'))
  const used = band.kind === 'none' ? 0 : band.kind === 'meters' && shown === 'full' ? band.meters.length : 1
  const room = Math.max(0, Math.min(MAX_ROWS, i.maxRows - used))
  const rows = watcherRows(liveWatchers(i.look, i.now), i.now, { wide: i.width >= WIDE_ROW, motion: i.moving })
  const visible = rows.slice(0, room)
  const watchers = visible.map((r, n) => watcherPieces(r, n === visible.length - 1 ? rows.length - visible.length : 0))

  let meters: MetersPart = { kind: 'none' }

  if (band.kind === 'line') {
    meters = { kind: 'line', text: band.text }
  } else if (band.kind === 'meters') {
    const plain = band.isDim
    const d = {
      barWidth: shown === 'compact' && i.width < NARROW_COLUMNS ? NARROW_BAR : BAR_WIDTH[shown],
      plain,
      lit: i.moving && !plain,
      full: shown === 'full',
      now: i.now,
    }
    meters = {
      kind: shown,
      head: i.label === '' ? null : i.label,
      note: band.note,
      rows: band.meters.map(m => meterRow(m, i.look, { ...d, sweepStart: i.sweeps[m.key] })),
    }
  }

  return { band, shown, meters, watchers }
}

/** Whether the frame draws anything at all. */
export function draws(f: Frame): boolean {
  return f.band.kind !== 'none' || f.watchers.length > 0
}

const gap = (n: number): Cell => plainCell(' '.repeat(n), false)

function joined(groups: Cell[][], n: number): Cell[] {
  return groups.flatMap((g, k) => (k === 0 ? g : [gap(n), ...g]))
}

/** The frame as lines of cells, with the gaps Box puts between its children: the preview's form. */
export function frameLines(f: Frame): Cell[][] {
  const lines: Cell[][] = []
  const m = f.meters
  const row = (r: MeterRow) => joined(r.pieces, 1)

  if (m.kind === 'line') {
    lines.push([plainCell(m.text, true)])
  } else if (m.kind === 'compact') {
    const groups: Cell[][] = []

    if (m.head !== null) {
      groups.push([plainCell(m.head, true)])
    }

    groups.push(...m.rows.map(row))

    if (m.note !== null) {
      groups.push([plainCell(m.note, true)])
    }

    lines.push(joined(groups, 3))
  } else if (m.kind === 'full') {
    m.rows.forEach((r, k) => {
      const groups: Cell[][] = []

      if (k === 0 && m.head !== null) {
        groups.push([plainCell(m.head, true)])
      }

      groups.push(row(r))

      if (k === m.rows.length - 1 && m.note !== null) {
        groups.push([plainCell(m.note, true)])
      }

      lines.push(joined(groups, 1))
    })
  }

  for (const w of f.watchers) {
    lines.push(joined(w, 1))
  }

  return lines
}
