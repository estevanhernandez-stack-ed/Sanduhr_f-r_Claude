import { expect, mock, test } from 'claude-code/testing'

// Sunday 2026-10-04 15:00 UTC.
const NOW = Date.parse('2026-10-04T15:00:00Z')
const MIN = 60_000
const HOUR = 60 * MIN
const HOME = '/Users/someone'
const PATH = `${HOME}/Library/Application Support/Sanduhr/snapshot.json`
const BAND = `${HOME}/Library/Application Support/Sanduhr/band.json`
const START = { cwd: '/', surface: 'terminal', isInteractive: true } as const

const band = (props: Record<string, unknown> = {}) =>
  ({
    plugin: 'sanduhr-meters',
    surface: 'terminal',
    component: 'AbovePrompt',
    props: { hasSurvey: false, isWorking: false, maxRows: 4, bodyColumns: 120, ...props },
  }) as never

function snapshot(tiers: unknown[], o: { at?: number; status?: string; kind?: string | null } = {}): string {
  return JSON.stringify({
    account_ref: '1a2b3c4d',
    captured_at: new Date(o.at ?? NOW - MIN).toISOString(),
    error_kind: o.kind ?? null,
    plan: null,
    schema_version: 1,
    status: o.status ?? 'ok',
    tiers,
    writer_version: '3.9.0',
  })
}

const NORMAL = snapshot([
  { key: 'five_hour', resets_at: new Date(NOW + 2 * HOUR + 14 * MIN).toISOString(), utilization: 61 },
  { key: 'seven_day', resets_at: '2026-10-06T12:00:00Z', utilization: 88 },
])
const HOT = snapshot([
  { key: 'five_hour', resets_at: new Date(NOW + 2 * HOUR).toISOString(), utilization: 30 },
  { key: 'seven_day', resets_at: '2026-10-06T12:00:00Z', utilization: 93 },
])

type World = { files: Record<string, string>; reads: string[]; toasts: string[]; store: Record<string, unknown>; redraws: number[] }

/** The engine beneath the mod: a clock, a store, HOME, a file system holding `files`. */
function beneath(on: any, files: Record<string, string>, env: Record<string, string> = { HOME }, store: Record<string, unknown> = {}, clock?: { now: () => number }): World {
  const world: World = { files, reads: [], toasts: [], store, redraws: [] }
  mock.store(on, world.store)
  mock.env(on, env)
  on('session.start', (_$: unknown, e: { cwd: string }) => ({ cwd: e.cwd }))
  on('fs.read', (_$: unknown, e: { path: string }) => {
    world.reads.push(e.path)
    // A Windows path is relative on this host, which resolves it under the working directory.
    const name = Object.keys(world.files).find(k => e.path === k || e.path.endsWith(`/${k}`))
    const text = name === undefined ? undefined : world.files[name]

    if (text === undefined) {
      throw new Error('ENOENT')
    }

    return { value: text } as never
  })
  on('ui.toast', (_$: unknown, e: { text: string }) => {
    world.toasts.push(e.text)

    return { value: undefined } as never
  })
  // Every draw passes through here: what counts the redraws.
  on('ui.render', (_$: any, e: any) => {
    world.redraws.push(clock?.now() ?? 0)
    const { Text } = _$.ui.resolve(e)

    return <Text key="below">beneath</Text>
  })

  return world
}

const textOf = async (ui: any) => (await ui.findAll({ type: 'Text' })).map((t: any) => t.text).join('|')

test('draws both meters above the band beneath, with the pace mark and countdowns', async ($, on) => {
  mock.clock(on, { now: NOW })
  beneath(on, { [PATH]: NORMAL })
  await $.session.start(START)

  const ui = await $.ui.mount(band())
  const session = await ui.find({ key: 'sm-five_hour' })
  const weekly = await ui.find({ key: 'sm-seven_day' })
  expect(session?.text).toContain('Session')
  expect(session?.text).toContain('61%')
  expect(session?.text).toContain('resets 2h 14m')
  expect(session?.text).toContain('│')
  expect(weekly?.text).toContain('88%')
  expect(weekly?.text).toContain('resets Tue')
  expect(await ui.find({ type: "Text", text: "beneath" })).toBeDefined()
})

test('nothing draws without a snapshot, and only the snapshot and band.json are read', async ($, on) => {
  mock.clock(on, { now: NOW })
  const world = beneath(on, {})
  await $.session.start(START)

  const ui = await $.ui.mount(band())
  expect(await ui.find({ key: 'sm-five_hour' })).toBeUndefined()
  expect(await ui.find({ type: "Text", text: /^Sanduhr:/ })).toBeUndefined()
  expect(await ui.find({ type: "Text", text: "beneath" })).toBeDefined()
  expect(new Set(world.reads)).toEqual(new Set([PATH, BAND]))
})

test('yields to a survey', async ($, on) => {
  mock.clock(on, { now: NOW })
  beneath(on, { [PATH]: NORMAL })
  await $.session.start(START)

  const ui = await $.ui.mount(band({ hasSurvey: true }))
  expect(await ui.find({ key: 'sm-five_hour' })).toBeUndefined()
  expect(await ui.find({ type: "Text", text: "beneath" })).toBeDefined()
})

test('signed out is one line', async ($, on) => {
  mock.clock(on, { now: NOW })
  beneath(on, { [PATH]: snapshot([], { status: 'error', kind: 'session_expired' }) })
  await $.session.start(START)

  const ui = await $.ui.mount(band())
  expect((await ui.find({ type: "Text", text: /^Sanduhr:/ }))?.text).toBe('Sanduhr: sign in to see your meters.')
})

test('a stale snapshot dims with its age, and the minute tick moves it on', async ($, on) => {
  const clock = mock.clock(on, { now: NOW })
  beneath(on, { [PATH]: snapshot([{ key: 'seven_day', resets_at: '2026-10-06T12:00:00Z', utilization: 50 }], { at: NOW - 8 * MIN }) })
  await $.session.start(START)

  expect((await (await $.ui.mount(band())).find({ type: "Text", text: /ago\)$/ }))?.text).toBe('(8m ago)')
  await clock.advance(2 * MIN)
  expect((await (await $.ui.mount(band())).find({ type: "Text", text: /ago\)$/ }))?.text).toBe('(10m ago)')
  await clock.advance(6 * MIN)
  expect((await (await $.ui.mount(band())).find({ type: "Text", text: /^Sanduhr:/ }))?.text).toBe('Sanduhr: no update for 16m. Is the widget running?')
})

test('picks up a new snapshot on the 30-second poll', async ($, on) => {
  const clock = mock.clock(on, { now: NOW })
  const world = beneath(on, {})
  await $.session.start(START)
  expect(await (await $.ui.mount(band())).find({ key: 'sm-five_hour' })).toBeUndefined()

  world.files[PATH] = NORMAL
  await clock.advance(30_000)
  expect((await (await $.ui.mount(band())).find({ key: 'sm-five_hour' }))?.text).toContain('61%')
})

test('a limit entering warning toasts once per reset window, across sessions', async ($, on) => {
  const clock = mock.clock(on, { now: NOW })
  const world = beneath(on, { [PATH]: HOT }, { HOME }, {}, clock)
  await $.session.start(START)

  expect(world.toasts).toEqual([expect.stringMatching(/^Sanduhr: the weekly limit is at 93%, resets Tue \d{1,2}:00 [AP]M\.$/)])
  const weekly = await (await $.ui.mount(band())).find({ key: 'sm-seven_day' })
  expect(weekly?.text).toContain('⚠')

  await clock.advance(5 * MIN)
  expect(world.toasts).toHaveLength(1)
})

test('a warning toasted in another session stays quiet', async ($, on) => {
  mock.clock(on, { now: NOW })
  const key = `warn:seven_day@${Date.parse('2026-10-06T12:00:00Z')}`
  const world = beneath(on, { [PATH]: HOT }, { HOME }, { toasted: [key] })
  await $.session.start(START)

  expect(world.toasts).toEqual([])
})

test('the session reset toasts once', async ($, on) => {
  const clock = mock.clock(on, { now: NOW })
  const world = beneath(on, {
    [PATH]: snapshot([{ key: 'five_hour', resets_at: new Date(NOW + 30_000).toISOString(), utilization: 70 }]),
  })
  await $.session.start(START)
  expect(world.toasts).toEqual([])

  await clock.advance(60_000)
  expect(world.toasts).toEqual(['Sanduhr: the session limit has reset.'])
  expect((await (await $.ui.mount(band())).find({ key: 'sm-five_hour' }))?.text).toMatch(/^Session\s*reset$/)

  // Sanduhr fetches the new window: no second toast.
  world.files[PATH] = snapshot([{ key: 'five_hour', resets_at: new Date(NOW + 5 * HOUR).toISOString(), utilization: 0 }])
  await clock.advance(60_000)
  expect(world.toasts).toEqual(['Sanduhr: the session limit has reset.'])
})

test('options: weekly only, full style, a label of the owner\'s own', { options: { bars: 'weekly', style: 'full', label: 'Work' } }, async ($, on) => {
  mock.clock(on, { now: NOW })
  beneath(on, { [PATH]: NORMAL })
  await $.session.start(START)

  const ui = await $.ui.mount(band())
  expect(await ui.find({ key: 'sm-five_hour' })).toBeUndefined()
  const weekly = await ui.find({ key: 'sm-seven_day' })
  expect(weekly?.text).toContain("14% ahead")
  expect(weekly?.text).toMatch(/resets Tue \d{1,2}:00 [AP]M/)
  expect((await ui.find({ type: "Text", text: "Work" }))?.text).toBe('Work')
})

test('full style folds to one line when the band is short', { options: { style: 'full' } }, async ($, on) => {
  mock.clock(on, { now: NOW })
  beneath(on, { [PATH]: NORMAL })
  await $.session.start(START)

  const ui = await $.ui.mount(band({ maxRows: 1 }))
  expect(await ui.find({ key: 'sm-band' })).toBeDefined()
  expect((await ui.find({ key: 'sm-seven_day' }))?.text).toContain('resets Tue')
})

test('Windows reads the snapshot under APPDATA', async ($, on) => {
  mock.clock(on, { now: NOW })
  const appData = 'C:\\Users\\someone\\AppData\\Roaming'
  const world = beneath(on, { [`${appData}\\Sanduhr\\snapshot.json`]: NORMAL }, { OS: 'Windows_NT', APPDATA: appData })
  await $.session.start(START)

  expect(world.reads.length).toBeGreaterThan(0)
  expect(world.reads.every(p => p.endsWith(`${appData}\\Sanduhr\\snapshot.json`) || p.endsWith(`${appData}\\Sanduhr\\band.json`))).toBe(true)
  expect(await textOf(await $.ui.mount(band()))).toContain('61%')
})

// -- The animated band (item 65f) and the watcher band (item 66) ------------------------------

function bandFile(o: Record<string, unknown> = {}, at = NOW - 5_000): string {
  return JSON.stringify({ schema_version: 1, written_at: new Date(at).toISOString(), reduce_motion: false, ...o })
}

const WATCHERS = [
  { source: 'agent', title: 'Release 2.9.0', short: 'v2.9.0', state: 'waiting', done: 4, total: 12, started_at: new Date(NOW - 4 * MIN).toISOString() },
  { source: 'agent', title: 'Nightly', state: 'failed', started_at: new Date(NOW - 10 * MIN).toISOString(), ended_at: new Date(NOW - MIN).toISOString() },
  { source: 'automatic', kind: 'shell', state: 'running' },
  { source: 'agent', title: 'Old job', state: 'lost_touch', started_at: new Date(NOW - 20 * MIN).toISOString() },
]

const watcherBoxes = async (ui: any) => (await ui.findAll({ type: 'Box' })).filter((b: any) => typeof b.key === 'string' && b.key.startsWith('sw-'))

const colorsOf = async (ui: any, text: RegExp) =>
  (await ui.findAll({ type: 'Text' })).filter((t: any) => text.test(t.text)).map((t: any) => t.props.color)

test('watchers from band.json: a row each, state mark, title, elapsed and progress', async ($, on) => {
  mock.clock(on, { now: NOW })
  beneath(on, { [PATH]: NORMAL, [BAND]: bandFile({ watchers: WATCHERS }) })
  await $.session.start(START)

  const ui = await $.ui.mount(band({ maxRows: 8 }))
  const rows = await watcherBoxes(ui)
  expect(rows.map((r: any) => r.text)).toEqual([
    '◉Release 2.9.04m4/12',
    '⚠Nightly9m',
    '●background shell',
    '○Old job20m',
  ])
  // The meters still draw above them, and the band beneath below.
  expect((await ui.find({ key: 'sm-five_hour' }))?.text).toContain('61%')
  expect(await ui.find({ type: 'Text', text: 'beneath' })).toBeDefined()
  // A narrow band writes the short title.
  const narrow = await $.ui.mount(band({ maxRows: 8, bodyColumns: 50 }))
  expect((await narrow.find({ key: 'sw-w0' }))?.text).toContain('v2.9.0')
})

test('watchers fold into "+N more" when the band is short, and show without a snapshot', async ($, on) => {
  mock.clock(on, { now: NOW })
  beneath(on, { [BAND]: bandFile({ watchers: WATCHERS }) })
  await $.session.start(START)

  const ui = await $.ui.mount(band({ maxRows: 2 }))
  const rows = await watcherBoxes(ui)
  expect(rows.length).toBe(2)
  expect(rows[1].text).toContain('+2 more')
  expect(await ui.find({ key: 'sm-five_hour' })).toBeUndefined()
})

test('a malformed or stale band.json is ignored; the meters draw as before', async ($, on) => {
  const clock = mock.clock(on, { now: NOW })
  const world = beneath(on, { [PATH]: NORMAL, [BAND]: '{"schema_version":1,"watchers":[' })
  await $.session.start(START)

  let ui = await $.ui.mount(band({ maxRows: 8 }))
  expect(await watcherBoxes(ui)).toEqual([])
  expect((await ui.find({ key: 'sm-five_hour' }))?.text).toContain('61%')

  // Junk rows go, good ones stay.
  world.files[BAND] = bandFile({ watchers: ['x', { source: 'agent', state: 'running' }, WATCHERS[2]] })
  await clock.advance(2_000)
  ui = await $.ui.mount(band({ maxRows: 8 }))
  expect((await watcherBoxes(ui)).length).toBe(1)

  // Sanduhr stopped writing it (quit or crashed): no watchers after three minutes.
  await clock.advance(3 * MIN)
  ui = await $.ui.mount(band({ maxRows: 8 }))
  expect(await watcherBoxes(ui)).toEqual([])
})

test('the Style popover\'s looks: letters, ink per character, attributes', async ($, on) => {
  mock.clock(on, { now: NOW })
  const look = { meters: { styles: { session: { ink: ['#ff0000', '#0000ff'], font: 'script', bold: true }, resets: { italic: true } } } }
  beneath(on, { [PATH]: NORMAL, [BAND]: bandFile(look) })
  await $.session.start(START)

  const ui = await $.ui.mount(band())
  const session = await ui.find({ key: 'sm-five_hour' })
  expect(session?.text).toContain('𝒮ℯ𝓈𝓈𝒾ℴ𝓃')
  const name = (await ui.findAll({ type: 'Text' })).filter((t: any) => /^[𝒮ℯ𝓈𝒾ℴ𝓃]+$/u.test(t.text))
  // One color per character from red to blue, bold.
  expect(name.length).toBe(7)
  expect(name[0]?.props.color).toBe('#ff0000')
  expect(name[6]?.props.color).toBe('#0000ff')
  expect(name.every((t: any) => t.props.bold === true)).toBe(true)
  const reset = (await ui.findAll({ type: 'Text' })).find((t: any) => t.text === 'resets 2h 14m')
  expect(reset?.props.italic).toBe(true)
  // The weekly meter has no look of its own: as before.
  expect((await ui.find({ key: 'sm-seven_day' }))?.text).toContain('Weekly')
})

const SESSION_ONLY = { options: { bars: 'session' } }
const at = (pct: number, resetIn = 3 * HOUR + 7 * MIN + 30_000, captured = NOW - MIN) =>
  snapshot([{ key: 'five_hour', resets_at: new Date(NOW + resetIn).toISOString(), utilization: pct }], { at: captured })

test('a bar sweeps once when it crosses a warning line: fast frames for 1.1 s, then rest', SESSION_ONLY, async ($, on) => {
  const clock = mock.clock(on, { now: NOW })
  const world = beneath(on, { [PATH]: at(70) }, { HOME }, {}, clock)
  await $.session.start(START)
  await $.ui.mount(band())

  // At rest: no redraw at all over ten seconds (nothing on the band changes by the second).
  world.redraws.length = 0
  await clock.advance(10_000)
  expect(world.redraws).toEqual([])

  // 70% to 80% crosses the 75% line: the next poll sweeps the bar.
  world.files[PATH] = at(80, 3 * HOUR + 7 * MIN + 30_000, NOW + 10_000)
  await clock.advance(20_000)
  await clock.advance(3_000)
  const swept = world.redraws.filter(t => t >= NOW + 30_000)
  expect(swept.length).toBeGreaterThan(8)
  // Never faster than 30 a second, and done within the pass (plus the resting frame).
  for (let i = 1; i < swept.length; i += 1) {
    expect((swept[i] as number) - (swept[i - 1] as number)).toBeGreaterThanOrEqual(1000 / 30)
  }
  expect((swept[swept.length - 1] as number) - (swept[0] as number)).toBeLessThanOrEqual(1100 + 200)

  // Mid-sweep some bar cells are lit toward white.
  world.files[PATH] = at(90, 3 * HOUR + 7 * MIN + 30_000, NOW + 40_000)
  const before = world.redraws.length
  await clock.advance(30_000)
  expect(world.redraws.length).toBeGreaterThan(before)
})

test('mid-sweep the light brightens the bar toward white', SESSION_ONLY, async ($, on) => {
  const clock = mock.clock(on, { now: NOW })
  const world = beneath(on, { [PATH]: at(70) }, { HOME }, {}, clock)
  await $.session.start(START)
  world.files[PATH] = at(80, 3 * HOUR + 7 * MIN + 30_000, NOW + 1_000)
  await clock.advance(30_000)
  // The poll at 30 s read the crossing; half a pass later the light is over the bar.
  await clock.advance(450)
  const lit = await colorsOf(await $.ui.mount(band()), /^[█▏▎▍▌▋▊▉░│]+$/)
  expect(lit.some((c: string | undefined) => c !== undefined && c !== '#fb923c' && c !== '#f472b6')).toBe(true)
  await clock.advance(2_000)
  const rest = await colorsOf(await $.ui.mount(band()), /^[█▏▎▍▌▋▊▉░│]+$/)
  expect(rest.every((c: string | undefined) => c === undefined || c === '#fb923c' || c === '#f472b6')).toBe(true)
})

test('a limit about to reset shimmers on the period; Reduce Motion keeps it still', SESSION_ONLY, async ($, on) => {
  const clock = mock.clock(on, { now: NOW })
  const world = beneath(on, { [PATH]: at(40, 8 * MIN) }, { HOME }, {}, clock)
  await $.session.start(START)
  await $.ui.mount(band())
  world.redraws.length = 0
  await clock.advance(8_000)
  // Two passes in eight seconds, each a run of fast frames.
  expect(world.redraws.length).toBeGreaterThan(10)

  world.files[BAND] = bandFile({ reduce_motion: true }, NOW + 8_000)
  await clock.advance(2_000)
  world.redraws.length = 0
  await clock.advance(8_000)
  // Still: at most the minute-by-minute words change (none here).
  expect(world.redraws.length).toBeLessThanOrEqual(1)
})

test('the motion option off keeps a nearly full limit still', { options: { bars: 'weekly', motion: 'off' } }, async ($, on) => {
  const clock = mock.clock(on, { now: NOW })
  const world = beneath(on, { [PATH]: HOT })
  await $.session.start(START)
  await $.ui.mount(band())
  world.redraws.length = 0
  await clock.advance(8_000)
  expect(world.redraws.length).toBe(0)
  const ui = await $.ui.mount(band())
  expect((await ui.findAll({ type: 'Text', text: '⚠' }))[0]?.props.color).toBe('#f87171')
})

test('a watcher waiting on you pulses; a passed one fades, then Sanduhr drops it', async ($, on) => {
  const clock = mock.clock(on, { now: NOW })
  const world = beneath(on, {
    [BAND]: bandFile({ watchers: [{ source: 'agent', title: 'Review', state: 'waiting', started_at: new Date(NOW - MIN).toISOString() }] }),
  })
  await $.session.start(START)
  const marks = new Set<string>()

  for (let i = 0; i < 50; i += 1) {
    await clock.advance(80)
    marks.add((await (await $.ui.mount(band())).find({ type: 'Text', text: '◉' }))?.props.color as string)
  }

  expect(marks.size).toBeGreaterThan(3)

  const ended = NOW + 10_000
  world.files[BAND] = bandFile(
    { watchers: [{ source: 'agent', title: 'Review', state: 'passed', started_at: new Date(NOW - MIN).toISOString(), ended_at: new Date(ended).toISOString() }] },
    ended,
  )
  await clock.set(ended)
  await clock.advance(2_000)
  const early = (await (await $.ui.mount(band())).find({ type: 'Text', text: '✓' }))?.props.color
  await clock.advance(3_500)
  const late = (await (await $.ui.mount(band())).find({ type: 'Text', text: '✓' }))?.props.color
  expect(early).toBeDefined()
  expect(late).not.toBe(early)

  world.files[BAND] = bandFile({ watchers: [] }, ended + 6_000)
  await clock.advance(2_000)
  expect(await (await $.ui.mount(band())).find({ type: 'Text', text: '✓' })).toBeUndefined()
})
