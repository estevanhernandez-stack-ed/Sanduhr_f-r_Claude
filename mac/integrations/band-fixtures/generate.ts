// The band's frames as fixtures (item 75, slice B). Each case is an input the meters mod could
// see (a snapshot, band.json, the time, the options, the terminal's size, the sweeps under way)
// and the lines draw.ts makes of it. Sanduhr's Swift port of the band (BandFrame) replays every
// case and must match cell for cell, so the Mod Studio's preview draws what a session draws.
//
//   node --import ./register.mjs generate.ts          writes the fixtures
//   node --import ./register.mjs generate.ts --check  fails when they are out of date (Mac CI)
//
// Node 24 or newer (type stripping, module.registerHooks). Run it after any change to the mod's
// meters.ts, band.ts or draw.ts, and port the change to BandFrame.swift.
//
// Run from mac/integrations/band-fixtures. Made-up numbers only.

import { readFileSync, writeFileSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'

import { parseBand } from '../mods/sanduhr-meters/hooks/band.ts'
import { frameFor, frameLines } from '../mods/sanduhr-meters/hooks/draw.ts'
import type { FrameInput } from '../mods/sanduhr-meters/hooks/draw.ts'
import { parseSnapshot } from '../mods/sanduhr-meters/hooks/meters.ts'

const OUT = join(dirname(fileURLToPath(import.meta.url)), '../../Tests/SanduhrTests/Fixtures/band-frames/frames.json')

// Sunday 2026-10-04 15:00 UTC.
const NOW = Date.parse('2026-10-04T15:00:00Z')
const MIN = 60_000
const HOUR = 60 * MIN
const DAY = 24 * HOUR
// Times inside the repeating pass (shimmer, glow, waiting pulse), and between passes.
const PULSE = NOW - (NOW % 4000) + 4000
const MID_PULSE = PULSE + 550
const EARLY_PULSE = PULSE + 180
const REST = PULSE + 2500

const iso = (ms: number) => new Date(ms).toISOString()

function snapshot(now: number, tiers: [string, number | null, number | null][], o: { age?: number; status?: string; kind?: string | null } = {}): string {
  return JSON.stringify({
    account_ref: '1a2b3c4d',
    captured_at: iso(now - (o.age ?? MIN)),
    error_kind: o.kind ?? null,
    plan: null,
    schema_version: 1,
    status: o.status ?? 'ok',
    tiers: tiers.map(([key, utilization, resetIn]) => ({ key, utilization, resets_at: resetIn === null ? null : iso(now + resetIn) })),
    writer_version: '3.9.0',
  })
}

function band(now: number, o: Record<string, unknown> = {}): string {
  return JSON.stringify({ schema_version: 1, written_at: iso(now - 5_000), reduce_motion: false, ...o })
}

type Case = { name: string; snapshot: string | null; band: string | null; input: Omit<FrameInput, 'snap' | 'look'> }

function input(now: number, o: Partial<Omit<FrameInput, 'snap' | 'look'>> = {}): Omit<FrameInput, 'snap' | 'look'> {
  return { now, bars: 'both', style: 'compact', label: '', moving: true, width: 120, maxRows: 4, tzOffsetMinutes: 0, seat: false, sweeps: {}, ...o }
}

const calm = (now: number) => snapshot(now, [['five_hour', 34, 3 * HOUR + 12 * MIN], ['seven_day', 21, 4 * DAY + 2 * HOUR]])
const near = (now: number) => snapshot(now, [['five_hour', 78, 2 * HOUR + 14 * MIN], ['seven_day', 52, 2 * DAY]])
const full = (now: number) => snapshot(now, [['five_hour', 93, 2 * HOUR], ['seven_day', 61, 3 * DAY]])
const resetting = (now: number) => snapshot(now, [['five_hour', 62, 6 * MIN], ['seven_day', 40, 5 * DAY]])

const LOOK = {
  meters: {
    styles: {
      session: { ink: ['#ff2a6d', '#05d9e8'], font: 'script', bold: true },
      weekly: { ink: ['#a78bfa'], font: 'small-caps', italic: true },
      resets: { ink: ['#fbbf24', '#f472b6', '#38bdf8'], font: 'double-struck', underline: true },
    },
  },
}
const WATCHERS = (now: number) => [
  { source: 'agent', title: 'Run the test suite', short: 'tests', state: 'running', done: 3, total: 12, started_at: iso(now - 4 * MIN - 12_000) },
  { source: 'agent', title: 'Approve the deploy to production', short: 'deploy', state: 'waiting', started_at: iso(now - 70 * MIN) },
  { source: 'agent', title: 'Lint', state: 'passed', started_at: iso(now - 2 * MIN), ended_at: iso(now - 2_500) },
  { source: 'agent', title: 'Nightly', state: 'failed', started_at: iso(now - 10 * MIN), ended_at: iso(now - MIN) },
  { source: 'automatic', kind: 'shell', state: 'running' },
  { source: 'agent', title: 'Old job', state: 'lost_touch', started_at: iso(now - 20 * MIN) },
  { source: 'agent', title: 'Build docs', state: 'finished', started_at: iso(now - 9 * MIN), ended_at: iso(now - 4_000) },
]

const cases: Case[] = [
  { name: 'calm, compact, both', snapshot: calm(REST), band: null, input: input(REST) },
  { name: 'calm, full, a label', snapshot: calm(REST), band: null, input: input(REST, { style: 'full', label: 'Personal' }) },
  { name: 'calm, full folds when short', snapshot: calm(REST), band: null, input: input(REST, { style: 'full', maxRows: 1 }) },
  { name: 'calm, full folds when narrow', snapshot: calm(REST), band: null, input: input(REST, { style: 'full', width: 63 }) },
  { name: 'calm, compact narrows its bars', snapshot: calm(REST), band: null, input: input(REST, { width: 70 }) },
  { name: 'calm, session only', snapshot: calm(REST), band: null, input: input(REST, { bars: 'session' }) },
  { name: 'calm, weekly only, full, a time zone', snapshot: calm(REST), band: null, input: input(REST, { bars: 'weekly', style: 'full', tzOffsetMinutes: 300 }) },
  { name: 'near a line', snapshot: near(REST), band: null, input: input(REST, { style: 'full' }) },
  { name: 'crossing: early in the sweep', snapshot: near(REST), band: null, input: input(REST, { sweeps: { five_hour: REST - 200 } }) },
  { name: 'crossing: mid sweep', snapshot: near(REST), band: null, input: input(REST, { sweeps: { five_hour: REST - 550 } }) },
  { name: 'crossing: sweep over', snapshot: near(REST), band: null, input: input(REST, { sweeps: { five_hour: REST - 1500 } }) },
  { name: 'crossing: still with motion off', snapshot: near(REST), band: null, input: input(REST, { moving: false, sweeps: { five_hour: REST - 550 } }) },
  { name: 'before reset: shimmer mid pass', snapshot: resetting(MID_PULSE), band: null, input: input(MID_PULSE, { style: 'full' }) },
  { name: 'before reset: between passes', snapshot: resetting(REST), band: null, input: input(REST, { style: 'full' }) },
  { name: 'nearly full: glow early', snapshot: full(EARLY_PULSE), band: null, input: input(EARLY_PULSE) },
  { name: 'nearly full: glow mid', snapshot: full(MID_PULSE), band: null, input: input(MID_PULSE, { style: 'full' }) },
  { name: 'nearly full: still with motion off', snapshot: full(MID_PULSE), band: null, input: input(MID_PULSE, { moving: false }) },
  { name: 'looks: gradients, letters, attributes', snapshot: calm(REST), band: band(REST, LOOK), input: input(REST, { style: 'full' }) },
  { name: 'looks: compact, nearly full, mid glow', snapshot: full(MID_PULSE), band: band(MID_PULSE, LOOK), input: input(MID_PULSE) },
  { name: 'looks: shimmer on styled resets', snapshot: resetting(MID_PULSE), band: band(MID_PULSE, LOOK), input: input(MID_PULSE) },
  { name: 'watchers: wide, all states', snapshot: calm(MID_PULSE), band: band(MID_PULSE, { watchers: WATCHERS(MID_PULSE) }), input: input(MID_PULSE, { maxRows: 8 }) },
  { name: 'watchers: narrow, short titles, folded', snapshot: calm(REST), band: band(REST, { watchers: WATCHERS(REST) }), input: input(REST, { width: 50, maxRows: 4 }) },
  { name: 'watchers: no snapshot', snapshot: null, band: band(REST, { watchers: WATCHERS(REST).slice(0, 2) }), input: input(REST) },
  { name: 'watchers: stale band.json shows none', snapshot: calm(REST), band: JSON.stringify({ schema_version: 1, written_at: iso(REST - 4 * MIN), watchers: WATCHERS(REST) }), input: input(REST) },
  { name: 'stale snapshot dims', snapshot: snapshot(REST, [['five_hour', 34, 3 * HOUR], ['seven_day', 21, 4 * DAY]], { age: 9 * MIN }), band: null, input: input(REST) },
  { name: 'dead snapshot is a line', snapshot: snapshot(REST, [['five_hour', 34, 3 * HOUR]], { age: 20 * MIN }), band: null, input: input(REST) },
  { name: 'dead seat reading dims with its age', snapshot: snapshot(REST, [['five_hour', 34, 3 * HOUR]], { age: 5 * HOUR }), band: null, input: input(REST, { seat: true }) },
  { name: 'signed out', snapshot: snapshot(REST, [], { status: 'error', kind: 'session_expired' }), band: null, input: input(REST) },
  { name: 'offline, last known', snapshot: snapshot(REST, [['five_hour', 34, 3 * HOUR], ['seven_day', 21, 4 * DAY]], { status: 'error', kind: 'network' }), band: null, input: input(REST) },
  { name: 'a reset passed', snapshot: snapshot(REST, [['five_hour', 88, -2 * MIN], ['seven_day', 21, 4 * DAY]]), band: null, input: input(REST) },
  { name: 'no reset time, nothing over', snapshot: snapshot(REST, [['five_hour', 0, null], ['seven_day', 100, null]]), band: null, input: input(REST, { style: 'full' }) },
  { name: 'nothing: no snapshot', snapshot: null, band: null, input: input(REST) },
]

const out = {
  note: 'Written by mac/integrations/band-fixtures/generate.ts from the meters mod (draw.ts). Do not edit by hand.',
  cases: cases.map(c => {
    const frame = frameFor({ ...c.input, snap: c.snapshot === null ? null : parseSnapshot(c.snapshot), look: c.band === null ? null : parseBand(c.band) })
    return { name: c.name, snapshot: c.snapshot, band: c.band, input: c.input, lines: frameLines(frame) }
  }),
}
const text = `${JSON.stringify(out, null, 1)}\n`

if (process.argv.includes('--check')) {
  let current = ''

  try {
    current = readFileSync(OUT, 'utf8')
  } catch {
    // Missing: out of date.
  }

  if (current !== text) {
    console.error('band frames are out of date: run node --import ./register.mjs generate.ts in mac/integrations/band-fixtures')
    process.exit(1)
  }

  console.log(`band frames up to date (${cases.length} cases)`)
} else {
  writeFileSync(OUT, text)
  console.log(`wrote ${cases.length} cases`)
}
