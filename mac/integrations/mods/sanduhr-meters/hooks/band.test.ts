import { expect, test } from 'claude-code/testing'

import {
  FADE_MS,
  FAST_MS,
  PERIOD_MS,
  SLOW_MS,
  STALE_MS,
  SWEEP_MS,
  crossings,
  elapsedWords,
  glowLevel,
  gradient,
  isMoving,
  letters,
  lightAt,
  liveWatchers,
  mix,
  nextFrameIn,
  onceProgress,
  paint,
  parseBand,
  parseStyle,
  pulseProgress,
  watcherMovers,
  watcherRows,
} from './band'

const NOW = Date.parse('2026-10-07T15:00:00Z')
const MIN = 60_000

function file(o: Record<string, unknown> = {}): string {
  return JSON.stringify({ schema_version: 1, written_at: new Date(NOW - 5_000).toISOString(), reduce_motion: false, ...o })
}

test('band.json: looks, Reduce Motion and watchers; junk reads as nothing', () => {
  const band = parseBand(
    file({
      meters: { styles: { session: { ink: ['#ff2a6d', '05d9e8'], font: 'script', bold: true }, weekly: { ink: 'red' }, model: { bold: true } } },
      reduce_motion: true,
      watchers: [
        { source: 'agent', title: 'CI on main', short: 'CI', state: 'waiting', done: 4, total: 12, started_at: new Date(NOW - 4 * MIN).toISOString() },
        { source: 'automatic', kind: 'shell', state: 'running' },
      ],
    }),
  )
  expect(band?.reduceMotion).toBe(true)
  expect(band?.styles.session).toEqual({ ink: ['#ff2a6d', '#05d9e8'], font: 'script', bold: true, italic: false, dim: false, underline: false })
  // A look the band can't trust is dropped whole; only Sanduhr's three segments are read.
  expect(band?.styles.weekly).toBeUndefined()
  expect(Object.keys(band?.styles ?? {})).toEqual(['session'])
  expect(band?.watchers?.length).toBe(2)
  expect(band?.watchers?.[1]).toEqual({ source: 'automatic', kind: 'shell', state: 'running' })

  expect(parseBand('')).toBeNull()
  expect(parseBand('{not json')).toBeNull()
  expect(parseBand('[]')).toBeNull()
  expect(parseBand(file({ schema_version: 2 }))).toBeNull()
  expect(parseBand(file({ written_at: 'soon' }))).toBeNull()
  // The switch off: no watchers key at all.
  expect(parseBand(file())?.watchers).toBeNull()
})

test('malformed watcher rows are left out, the rest kept; long titles are clipped', () => {
  const band = parseBand(
    file({
      watchers: [
        { source: 'agent', title: 'ok', state: 'running' },
        { source: 'agent', title: '', state: 'running' },
        { source: 'agent', title: 'no state' },
        { source: 'agent', title: 'odd state', state: 'exploded' },
        { source: 'someone', title: 'x', state: 'running' },
        { source: 'automatic', state: 'running' },
        'row',
        null,
        { source: 'agent', title: `a\u0007b${'x'.repeat(200)}`, state: 'failed', total: -1, done: 3 },
      ],
    }),
  )
  const rows = band?.watchers ?? []
  expect(rows.length).toBe(2)
  expect(rows[0]).toMatchObject({ title: 'ok', state: 'running', total: null, done: null })
  const long = rows[1]
  expect(long?.source === 'agent' && Array.from(long.title).length).toBe(80)
  expect(long?.source === 'agent' && long.title.startsWith('a b')).toBe(true)
  expect(long?.source === 'agent' && long.total).toBeNull()
})

test('styles: the statusline grammar, nothing looser', () => {
  expect(parseStyle({})).toEqual({ ink: [], font: null, bold: false, italic: false, dim: false, underline: false })
  expect(parseStyle({ ink: ['#abc'] })?.ink).toEqual(['#aabbcc'])
  expect(parseStyle({ ink: [] })).toBeNull()
  expect(parseStyle({ ink: ['#000', '#111', '#222', '#333', '#444'] })).toBeNull()
  expect(parseStyle({ font: 'comic' })).toBeNull()
  expect(parseStyle({ bold: 'yes' })).toBeNull()
  expect(parseStyle({ shimmer: true })).toBeNull()
  expect(parseStyle('bold')).toBeNull()
})

test('letters: the statusline\'s Unicode styles, holes mapped, the rest as written', () => {
  expect(letters('Session', 'script').join('')).toBe('𝒮ℯ𝓈𝓈𝒾ℴ𝓃')
  expect(letters('Hi 42%', 'double-struck').join('')).toBe('ℍ𝕚 𝟜𝟚%')
  expect(letters('Weekly', 'small-caps').join('')).toBe('Wᴇᴇᴋʟʏ')
  expect(letters('h1', 'italic').join('')).toBe('ℎ1')
  expect(letters('Weekly', null).join('')).toBe('Weekly')
  expect(letters('𝒮x', 'bold').length).toBe(2)
})

test('ink: a gradient per character, ends exact', () => {
  expect(gradient(['#000000', '#ffffff'], 3)).toEqual(['#000000', '#808080', '#ffffff'])
  expect(gradient(['#ff0000'], 2)).toEqual(['#ff0000', '#ff0000'])
  expect(gradient(['#ff0000', '#00ff00', '#0000ff'], 5)).toEqual(['#ff0000', '#808000', '#00ff00', '#008080', '#0000ff'])
  expect(gradient([], 4)).toEqual([])
  expect(mix('#000000', '#ffffff', 0.5)).toBe('#808080')
})

test('the light: three characters wide, brightest at its center, across and gone', () => {
  // Halfway through ten characters, the center sits on character 4.5: 4 and 5 lit, 3 and 6 dimmer, 2 dark.
  const lit = Array.from({ length: 10 }, (_, i) => lightAt(i, 10, 0.5))
  expect(lit.filter(b => b > 0).length).toBe(4)
  expect(Math.abs((lit[4] as number) - 0.75) < 1e-9).toBe(true)
  expect(Math.abs((lit[3] as number) - 0.25) < 1e-9).toBe(true)
  expect(lit[2]).toBe(0)
  // Before and after its pass nothing is lit.
  expect(Array.from({ length: 10 }, (_, i) => lightAt(i, 10, 0)).every(b => b <= 0.5)).toBe(true)
  expect(Array.from({ length: 10 }, (_, i) => lightAt(i, 10, 1)).every(b => b <= 0.5)).toBe(true)
  // Painting merges equal colors and brightens toward white where the light is.
  const runs = paint(Array.from('abcdef'), Array(6).fill('#000000'), 0.5)
  expect(runs.map(r => r.text).join('')).toBe('abcdef')
  expect(runs[0]).toEqual({ text: 'a', color: '#000000' })
  expect(runs.some(r => r.color !== '#000000')).toBe(true)
  expect(paint(['a', 'b'], [null, null], null)).toEqual([{ text: 'ab', color: null }])
})

test('sweep timing: one pass of 1.1 s from the crossing; pulses every 4 s', () => {
  expect(onceProgress(NOW, NOW)).toBe(0)
  expect(onceProgress(NOW, NOW + SWEEP_MS / 2)).toBe(0.5)
  expect(onceProgress(NOW, NOW + SWEEP_MS)).toBeNull()
  expect(onceProgress(undefined, NOW)).toBeNull()
  const base = Math.ceil(NOW / PERIOD_MS) * PERIOD_MS
  expect(pulseProgress(base)).toBe(0)
  expect((pulseProgress(base + SWEEP_MS - 1) as number) > 0.99).toBe(true)
  expect(pulseProgress(base + SWEEP_MS)).toBeNull()
  expect(glowLevel(null)).toBe(0)
  expect(Math.abs(glowLevel(0.5) - 0.6) < 1e-9).toBe(true)
})

test('frames: fast only while something moves, else a second; never faster than 30 a second', () => {
  const base = Math.ceil(NOW / PERIOD_MS) * PERIOD_MS + SWEEP_MS + 10
  const still = { sweeps: [], pulses: false, fades: [] }
  expect(isMoving(still, base)).toBe(false)
  expect(nextFrameIn(still, base, true)).toBe(SLOW_MS)
  const sweeping = { sweeps: [base - 100], pulses: false, fades: [] }
  expect(nextFrameIn(sweeping, base, true)).toBe(FAST_MS)
  expect(nextFrameIn(sweeping, base + SWEEP_MS, true)).toBe(SLOW_MS)
  // A pulse due in less than a second wakes the band in time for it.
  const pulsing = { sweeps: [], pulses: true, fades: [] }
  const due = Math.ceil(base / PERIOD_MS) * PERIOD_MS
  expect(nextFrameIn(pulsing, due - 300, true)).toBe(300)
  expect(nextFrameIn(pulsing, due + 10, true)).toBe(FAST_MS)
  // A passed watcher fades for six seconds.
  expect(isMoving({ sweeps: [], pulses: false, fades: [base - FADE_MS + 100] }, base)).toBe(true)
  expect(isMoving({ sweeps: [], pulses: false, fades: [base - FADE_MS] }, base)).toBe(false)
  // No motion: a second, whatever moves.
  expect(nextFrameIn(sweeping, base, false)).toBe(SLOW_MS)
  expect(1000 / FAST_MS).toBeLessThanOrEqual(30)
})

test('warning lines: a percent crossing 50, 75 or 90 upward sweeps; down or new does not', () => {
  expect(crossings({ five_hour: 70, seven_day: 89 }, { five_hour: 76, seven_day: 90 })).toEqual(['five_hour', 'seven_day'])
  expect(crossings({ five_hour: 76 }, { five_hour: 80 })).toEqual([])
  expect(crossings({ five_hour: 80 }, { five_hour: 40 })).toEqual([])
  expect(crossings({}, { five_hour: 95 })).toEqual([])
})

test('watcher rows: state marks, title or short, elapsed and progress; background work by kind', () => {
  const band = parseBand(
    file({
      watchers: [
        { source: 'agent', title: 'Release 2.9.0', short: 'v2.9.0', state: 'waiting', done: 4, total: 12, started_at: new Date(NOW - 4 * MIN).toISOString() },
        { source: 'agent', title: 'Nightly', state: 'failed', started_at: new Date(NOW - 65 * MIN).toISOString(), ended_at: new Date(NOW - MIN).toISOString() },
        { source: 'agent', title: 'Deploy', state: 'passed', started_at: new Date(NOW - 30_000).toISOString(), ended_at: new Date(NOW).toISOString() },
        { source: 'agent', title: 'Old', state: 'lost_touch', started_at: new Date(NOW - 20 * MIN).toISOString() },
        { source: 'automatic', kind: 'subagent', state: 'running' },
      ],
    }),
  )
  const live = liveWatchers(band, NOW)
  const wide = watcherRows(live, NOW, { wide: true, motion: true })
  expect(wide.map(r => r.mark)).toEqual(['◉', '⚠', '✓', '○', '●'])
  expect(wide.map(r => r.text)).toEqual(['Release 2.9.0', 'Nightly', 'Deploy', 'Old', 'background subagent'])
  expect(wide.map(r => r.elapsed)).toEqual(['4m', '1h 04m', '30s', '20m', null])
  expect(wide[0]?.progress).toBe('4/12')
  expect(wide[1]?.titleColor).toBe('#f87171')
  expect(wide[3]?.isDim).toBe(true)
  expect(watcherRows(live, NOW, { wide: false, motion: true })[0]?.text).toBe('v2.9.0')
  // A passed watcher's check fades toward grey before Sanduhr drops it.
  const later = watcherRows(live, NOW + FADE_MS - 1, { wide: true, motion: true })
  expect(later[2]?.color).not.toBe(wide[2]?.color)
  expect(later[2]?.isDim).toBe(true)
  expect(watcherMovers(live)).toEqual({ pulses: true, fades: [NOW] })
  // Without motion nothing pulses or fades.
  const still = watcherRows(live, NOW + FADE_MS - 1, { wide: true, motion: false })
  expect(still[2]?.color).toBe('#4ade80')
  expect(elapsedWords(12_500)).toBe('12s')
})

test('a band file Sanduhr stopped writing (quit or crashed) shows no watchers', () => {
  const band = parseBand(file({ watchers: [{ source: 'automatic', kind: 'shell', state: 'running' }] }))
  expect(liveWatchers(band, NOW).length).toBe(1)
  expect(liveWatchers(band, NOW + STALE_MS)).toEqual([])
  expect(liveWatchers(null, NOW)).toEqual([])
})
