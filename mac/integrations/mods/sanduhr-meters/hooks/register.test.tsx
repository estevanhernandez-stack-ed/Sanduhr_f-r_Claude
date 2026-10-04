import { expect, mock, test } from 'claude-code/testing'

// Sunday 2026-10-04 15:00 UTC.
const NOW = Date.parse('2026-10-04T15:00:00Z')
const MIN = 60_000
const HOUR = 60 * MIN
const HOME = '/Users/someone'
const PATH = `${HOME}/Library/Application Support/Sanduhr/snapshot.json`
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

type World = { files: Record<string, string>; reads: string[]; toasts: string[]; store: Record<string, unknown> }

/** The engine beneath the mod: a clock, a store, HOME, a file system holding `files`. */
function beneath(on: any, files: Record<string, string>, env: Record<string, string> = { HOME }, store: Record<string, unknown> = {}): World {
  const world: World = { files, reads: [], toasts: [], store }
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
  on('ui.render', (_$: any, e: any) => {
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

test('nothing draws without a snapshot, and only the snapshot is read', async ($, on) => {
  mock.clock(on, { now: NOW })
  const world = beneath(on, {})
  await $.session.start(START)

  const ui = await $.ui.mount(band())
  expect(await ui.find({ key: 'sm-five_hour' })).toBeUndefined()
  expect(await ui.find({ type: "Text", text: /^Sanduhr:/ })).toBeUndefined()
  expect(await ui.find({ type: "Text", text: "beneath" })).toBeDefined()
  expect(new Set(world.reads)).toEqual(new Set([PATH]))
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
  const world = beneath(on, { [PATH]: HOT })
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
  expect(world.reads.every(p => p.endsWith(`${appData}\\Sanduhr\\snapshot.json`))).toBe(true)
  expect(await textOf(await $.ui.mount(band()))).toContain('61%')
})
