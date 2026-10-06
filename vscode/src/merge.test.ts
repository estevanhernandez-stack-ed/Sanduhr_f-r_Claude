import { readFileSync } from 'node:fs';
import * as path from 'node:path';
import Ajv from 'ajv';
import addFormats from 'ajv-formats';
import { afterAll, describe, expect, it } from 'vitest';
import { dayWindow, merge, type MergeInput } from './merge';
import type { ClaudeEvent, DayRecord, Heartbeat } from './types';

const MIN = 60_000;
const DATE = '2026-06-10';
const A = 'a'.repeat(16);
const B = 'b'.repeat(16);

/** Unix seconds for a local wall-clock time on `date` (default DATE). */
function at(hh: number, mm: number, ss = 0, date = DATE): number {
  const [y, mo, d] = date.split('-').map(Number);
  return new Date(y, mo - 1, d, hh, mm, ss).getTime() / 1000;
}

let seq = 0;
function hb(time: number, over: Partial<Heartbeat> = {}): Heartbeat {
  return {
    v: 1,
    time,
    entity: 'e'.repeat(24),
    type: 'file',
    category: 'coding',
    kind: 'edit',
    project: A,
    branch: null,
    language: 'typescript',
    lines: 100,
    human_line_changes: 0,
    is_write: false,
    reload: false,
    ...over,
  };
}
function ce(time: number, kind: ClaudeEvent['kind'], over: Partial<ClaudeEvent> = {}): ClaudeEvent {
  return { v: 1, id: `id-${seq++}`, time, project: A, stream: 's1', kind, ...over };
}

function run(over: Partial<MergeInput> = {}): DayRecord {
  return merge({
    date: DATE,
    machine: 'machine-1',
    heartbeats: [],
    claudeEvents: [],
    aliases: {},
    realNames: { [A]: 'alpha', [B]: 'beta' },
    settings: { idleMinutes: 15 },
    generatedAt: '2026-06-11T00:00:00.000Z',
    caveats: { unreadableTranscriptLines: 0, readerVersion: '0.1.0' },
    ...over,
  });
}

const proj = (d: DayRecord, key: string) => {
  const p = d.projects.find((x) => x.key === key);
  if (!p) throw new Error(`no project ${key}`);
  return p;
};

const schema = JSON.parse(readFileSync(path.join(__dirname, '..', 'docs', 'time-day.schema.json'), 'utf8'));
const ajv = new Ajv({ allErrors: true, strict: true });
addFormats(ajv);
const validate = ajv.compile(schema);
function expectValid(d: DayRecord): void {
  const ok = validate(d);
  expect(validate.errors ?? []).toEqual([]);
  expect(ok).toBe(true);
  // Invariant the schema cannot express: total is the sum of the three parts.
  for (const t of [d.totals, ...d.projects]) expect(t.totalMs).toBe(t.youMs + t.claudeMs + t.bothMs);
}

describe('overlap', () => {
  it('both is the intersection of you and Claude in one project', () => {
    const d = run({
      heartbeats: [hb(at(10, 0)), hb(at(10, 5)), hb(at(10, 10))],
      claudeEvents: [ce(at(10, 8), 'activity'), ce(at(10, 12), 'activity'), ce(at(10, 14), 'activity')],
    });
    const p = proj(d, A);
    expect(p.bothMs).toBe(2 * MIN); // 10:08 to 10:10
    expect(p.youMs).toBe(8 * MIN);
    expect(p.claudeMs).toBe(4 * MIN);
    expect(p.totalMs).toBe(14 * MIN);
    expect(d.totals.bothMs).toBe(2 * MIN);
    expectValid(d);
  });

  it('an edit event counts as Claude activity', () => {
    const d = run({
      claudeEvents: [ce(at(9, 0), 'activity'), ce(at(9, 4), 'edit', { entity: 'x'.repeat(24), linesAdded: 1, linesRemoved: 0 })],
    });
    expect(proj(d, A).claudeMs).toBe(4 * MIN);
  });
});

describe('streams and agent minutes', () => {
  it('claudeMs is the union of parallel streams, agentMs is the sum', () => {
    const d = run({
      claudeEvents: [
        ce(at(10, 0), 'activity', { stream: 's1' }),
        ce(at(10, 4), 'activity', { stream: 's1' }),
        ce(at(10, 2), 'activity', { stream: 's2' }),
        ce(at(10, 6), 'activity', { stream: 's2' }),
      ],
    });
    const p = proj(d, A);
    expect(p.claudeMs).toBe(6 * MIN); // [10:00, 10:06]
    expect(p.agentMs).toBe(8 * MIN); // 4 + 4
    expect(d.totals.claudeMs).toBe(6 * MIN);
    expect(d.totals.agentMs).toBe(8 * MIN);
    expectValid(d);
  });

  it('day agentMs sums across projects', () => {
    const d = run({
      claudeEvents: [
        ce(at(10, 0), 'activity', { stream: 's1', project: A }),
        ce(at(10, 3), 'activity', { stream: 's1', project: A }),
        ce(at(10, 0), 'activity', { stream: 's2', project: B }),
        ce(at(10, 4), 'activity', { stream: 's2', project: B }),
      ],
    });
    expect(d.totals.agentMs).toBe(7 * MIN);
    expect(d.totals.claudeMs).toBe(4 * MIN); // pooled union [10:00, 10:04]
  });

  it('duplicate Claude event ids count once', () => {
    const e = ce(at(10, 0), 'edit', { entity: 'x'.repeat(24), linesAdded: 2, linesRemoved: 1 });
    const d = run({ claudeEvents: [e, { ...e }] });
    expect(d.totals.linesClaude).toBe(3);
  });
});

describe('lone-event rule', () => {
  it('a single heartbeat lasts 2 minutes', () => {
    expect(proj(run({ heartbeats: [hb(at(10, 0))] }), A).youMs).toBe(2 * MIN);
  });
  it('a single Claude event lasts 2 minutes, two far apart last 2 + 2', () => {
    expect(proj(run({ claudeEvents: [ce(at(10, 0), 'activity')] }), A).claudeMs).toBe(2 * MIN);
    const d = run({ claudeEvents: [ce(at(10, 0), 'activity'), ce(at(10, 6), 'activity')] });
    expect(proj(d, A).claudeMs).toBe(4 * MIN);
  });
  it('a joined interval spans first to last event with no tail', () => {
    expect(proj(run({ heartbeats: [hb(at(10, 0)), hb(at(10, 14))] }), A).youMs).toBe(14 * MIN);
  });
  it('the join gap follows idleMinutes', () => {
    const hbs = [hb(at(10, 0)), hb(at(10, 10))];
    expect(proj(run({ heartbeats: hbs, settings: { idleMinutes: 5 } }), A).youMs).toBe(4 * MIN);
    expect(proj(run({ heartbeats: hbs, settings: { idleMinutes: 10 } }), A).youMs).toBe(10 * MIN);
  });
});

describe('prompt spans', () => {
  it('a lone prompt is the 2 minutes before it', () => {
    const d = run({ claudeEvents: [ce(at(10, 30), 'prompt')] });
    expect(proj(d, A).youMs).toBe(2 * MIN);
    expect(proj(d, A).claudeMs).toBe(0);
  });
  it('a prompt 1 minute after an edit joins it into one interval', () => {
    const d = run({
      heartbeats: [hb(at(10, 0)), hb(at(10, 10))],
      claudeEvents: [ce(at(10, 11), 'prompt')],
    });
    expect(proj(d, A).youMs).toBe(11 * MIN); // [10:00, 10:11]
  });
  it('the span start joins an edit up to idleMinutes before it', () => {
    const d = run({ heartbeats: [hb(at(10, 0))], claudeEvents: [ce(at(10, 16), 'prompt')] });
    expect(proj(d, A).youMs).toBe(16 * MIN); // span start 10:14 is 14 minutes after the edit
  });
  it('a prompt is not Claude activity', () => {
    const d = run({ claudeEvents: [ce(at(10, 0), 'prompt')] });
    expect(d.totals.claudeMs).toBe(0);
    expect(d.totals.agentMs).toBe(0);
  });
});

describe('midnight and the previous-day tail', () => {
  it('joins across midnight, then clips to the day', () => {
    const prev = '2026-06-09';
    const tail = { heartbeats: [hb(at(23, 50, 0, prev)), hb(at(23, 55, 0, prev))], claudeEvents: [ce(at(23, 58, 0, prev), 'activity')] };
    const d = run({
      heartbeats: [hb(at(0, 5))],
      claudeEvents: [ce(at(0, 1), 'activity')],
      previousDayTail: tail,
    });
    expect(proj(d, A).youMs + proj(d, A).bothMs).toBe(5 * MIN); // [23:50, 00:05] clipped to [00:00, 00:05]
    expect(d.totals.claudeMs + d.totals.bothMs).toBe(1 * MIN); // [23:58, 00:01] clipped to [00:00, 00:01]
    expectValid(d);
  });

  it('the previous day does not count the next day and keeps its own end', () => {
    const d = run({
      date: '2026-06-09',
      heartbeats: [hb(at(23, 50, 0, '2026-06-09')), hb(at(23, 55, 0, '2026-06-09')), hb(at(0, 5))],
    });
    expect(proj(d, A).youMs).toBe(5 * MIN);
  });

  it('tail events never add lines to the day', () => {
    const prev = '2026-06-09';
    const d = run({
      heartbeats: [hb(at(0, 2), { human_line_changes: 2 })],
      previousDayTail: {
        heartbeats: [hb(at(23, 59, 0, prev), { human_line_changes: 50 })],
        claudeEvents: [ce(at(23, 59, 0, prev), 'edit', { entity: 'z'.repeat(24), linesAdded: 9, linesRemoved: 9 })],
      },
    });
    expect(d.totals.linesYou).toBe(2);
    expect(d.totals.linesClaude).toBe(0);
  });

  it('a project active only before midnight gets no row', () => {
    const d = run({ previousDayTail: { heartbeats: [hb(at(23, 30, 0, '2026-06-09'), { project: B })], claudeEvents: [] } });
    expect(d.projects).toEqual([]);
    expect(d.totals.totalMs).toBe(0);
    expectValid(d);
  });
});

describe('project switching', () => {
  it('keeps each project on its own clock and sorts rows by total', () => {
    const d = run({
      heartbeats: [
        hb(at(10, 0)), hb(at(10, 5)),
        hb(at(10, 8), { project: B }), hb(at(10, 12), { project: B }), hb(at(10, 20), { project: B }),
      ],
    });
    expect(d.projects.map((p) => p.key)).toEqual([B, A]);
    expect(proj(d, A).youMs).toBe(5 * MIN);
    expect(proj(d, B).youMs).toBe(12 * MIN);
    expect(d.totals.youMs).toBe(17 * MIN);
  });
});

describe('pooled day totals', () => {
  it('per-project sums exceed the day total when you edit A while Claude works B', () => {
    const d = run({
      heartbeats: [hb(at(10, 0)), hb(at(10, 10))],
      claudeEvents: [
        ce(at(10, 0), 'activity', { project: B }),
        ce(at(10, 3), 'activity', { project: B }),
        ce(at(10, 4), 'activity', { project: B }),
      ],
    });
    const a = proj(d, A);
    const b = proj(d, B);
    expect(a).toMatchObject({ youMs: 10 * MIN, bothMs: 0, claudeMs: 0 });
    expect(b).toMatchObject({ youMs: 0, bothMs: 0, claudeMs: 4 * MIN });
    expect(d.totals).toMatchObject({ youMs: 6 * MIN, bothMs: 4 * MIN, claudeMs: 0, totalMs: 10 * MIN });
    expect(a.totalMs + b.totalMs).toBe(14 * MIN);
    expectValid(d);
  });
});

describe('line counts and the backstop rule', () => {
  it('Claude lines are added plus removed on edit events', () => {
    const d = run({
      claudeEvents: [
        ce(at(10, 0), 'edit', { entity: 'x'.repeat(24), linesAdded: 3, linesRemoved: 1 }),
        ce(at(10, 1), 'edit', { entity: 'y'.repeat(24), linesAdded: 2, linesRemoved: 0 }),
        ce(at(10, 2), 'activity'),
      ],
    });
    expect(d.totals.linesClaude).toBe(6);
  });

  it('drops an edit heartbeat on the same entity within 2 seconds of a Claude edit', () => {
    const E = 'c'.repeat(24);
    const F = 'd'.repeat(24);
    const d = run({
      heartbeats: [
        hb(at(10, 0, 1), { entity: E, human_line_changes: 4 }), // backstopped
        hb(at(10, 0, 5), { entity: E, human_line_changes: 2 }), // 5 s away: yours
        hb(at(10, 0, 1), { entity: F, human_line_changes: 5 }), // other file: yours
      ],
      claudeEvents: [ce(at(10, 0, 0), 'edit', { entity: E, linesAdded: 3, linesRemoved: 1 })],
    });
    expect(d.totals.linesYou).toBe(7);
    expect(d.totals.linesClaude).toBe(4);
  });

  it('the backstopped heartbeat still counts as your time', () => {
    const E = 'c'.repeat(24);
    const d = run({
      heartbeats: [hb(at(10, 0, 1), { entity: E, human_line_changes: 4 })],
      claudeEvents: [ce(at(10, 0, 0), 'edit', { entity: E, linesAdded: 1, linesRemoved: 0 })],
    });
    expect(d.totals.linesYou).toBe(0);
    expect(proj(d, A).youMs + proj(d, A).bothMs).toBe(2 * MIN);
  });

  it('day line totals sum the projects', () => {
    const d = run({
      heartbeats: [hb(at(10, 0), { human_line_changes: 3 }), hb(at(10, 1), { project: B, human_line_changes: 4 })],
    });
    expect(d.totals.linesYou).toBe(7);
  });
});

describe('reload heartbeats', () => {
  it('are not activity and add no lines', () => {
    const d = run({
      heartbeats: [hb(at(10, 0)), hb(at(10, 10), { reload: true, human_line_changes: 7 })],
    });
    expect(proj(d, A).youMs).toBe(2 * MIN); // the reload at 10:10 does not extend the interval
    expect(d.totals.linesYou).toBe(0);
  });
  it('alone create no project row', () => {
    const d = run({ heartbeats: [hb(at(10, 0), { reload: true })] });
    expect(d.projects).toEqual([]);
  });
});

describe('languages', () => {
  it('attribute each stretch to the heartbeat that extended it and leave prompts out', () => {
    const d = run({
      heartbeats: [
        hb(at(10, 0), { language: 'typescript' }),
        hb(at(10, 5), { language: 'typescript' }),
        hb(at(10, 7), { language: 'python' }),
      ],
      claudeEvents: [ce(at(10, 20), 'prompt')], // span [10:18, 10:20] joins the editor interval
    });
    const p = proj(d, A);
    expect(p.youMs).toBe(20 * MIN);
    expect(p.languages).toEqual([
      { id: 'typescript', ms: 5 * MIN },
      { id: 'python', ms: 2 * MIN },
    ]);
  });
  it('a lone heartbeat gets 2 minutes; ties sort by id; output is capped at 10', () => {
    const langs = Array.from({ length: 12 }, (_, i) => `lang${String(i).padStart(2, '0')}`);
    const d = run({ heartbeats: langs.map((language, i) => hb(at(1 + i, 0), { language })) });
    const p = proj(d, A);
    expect(p.languages).toHaveLength(10);
    expect(p.languages[0]).toEqual({ id: 'lang00', ms: 2 * MIN });
  });
  it('a prompt-only project has no languages', () => {
    expect(proj(run({ claudeEvents: [ce(at(10, 0), 'prompt')] }), A).languages).toEqual([]);
  });
});

describe('names, caveats and the none project', () => {
  it('carries realName, aliases, and null/false for a missing alias', () => {
    const d = run({
      heartbeats: [hb(at(10, 0)), hb(at(11, 0), { project: B })],
      aliases: { [A]: { alias: 'Project Kestrel', masked: true } },
    });
    expect(proj(d, A)).toMatchObject({ realName: 'alpha', alias: 'Project Kestrel', masked: true });
    expect(proj(d, B)).toMatchObject({ realName: 'beta', alias: null, masked: false });
    expectValid(d);
  });
  it('the none project is a normal row named (no project)', () => {
    const d = run({ heartbeats: [hb(at(10, 0), { project: 'none' })] });
    expect(proj(d, 'none')).toMatchObject({ realName: '(no project)', youMs: 2 * MIN });
    expectValid(d);
  });
  it('passes caveats through', () => {
    const d = run({ caveats: { unreadableTranscriptLines: 3, readerVersion: '0.1.0', claudeCodeVersions: ['9.9.9'] } });
    expect(d.caveats).toEqual({ unreadableTranscriptLines: 3, readerVersion: '0.1.0', claudeCodeVersions: ['9.9.9'] });
    expect(d.v).toBe(1);
    expect(d.date).toBe(DATE);
    expectValid(d);
  });
  it('an empty day validates', () => {
    const d = run();
    expect(d.projects).toEqual([]);
    expect(d.totals).toEqual({ youMs: 0, claudeMs: 0, bothMs: 0, totalMs: 0, agentMs: 0, linesYou: 0, linesClaude: 0 });
    expectValid(d);
  });
});

describe('schema', () => {
  it('rejects non-integer milliseconds, a wrong version and unknown fields', () => {
    const good = run({ heartbeats: [hb(at(10, 0))] });
    expect(validate({ ...good, v: 2 })).toBe(false);
    expect(validate({ ...good, totals: { ...good.totals, youMs: 1.5 } })).toBe(false);
    expect(validate({ ...good, extra: true })).toBe(false);
    const { caveats: _omit, ...noCaveats } = good;
    void _omit;
    expect(validate(noCaveats)).toBe(false);
  });
});

describe('time zones and DST', () => {
  const savedTz = process.env.TZ;
  afterAll(() => {
    if (savedTz === undefined) delete process.env.TZ;
    else process.env.TZ = savedTz;
  });

  it('day windows follow local calendar days: 23 hours on spring-forward, 25 on fall-back', (ctx) => {
    process.env.TZ = 'America/New_York';
    // Windows may ignore a runtime TZ change; probe before asserting.
    if (new Date(2026, 2, 8, 12).getTimezoneOffset() !== 240 || new Date(2026, 0, 8, 12).getTimezoneOffset() !== 300) {
      ctx.skip('process.env.TZ is not honored at runtime on this platform');
    }
    const spring = dayWindow('2026-03-08');
    expect((spring.end - spring.start) / 3_600_000).toBe(23);
    const fall = dayWindow('2026-11-01');
    expect((fall.end - fall.start) / 3_600_000).toBe(25);
    expect((dayWindow('2026-03-09').start - spring.end)).toBe(0);

    // Epoch math across the missing hour: 01:58 and 03:01 local are 3 minutes apart.
    const d = merge({
      date: '2026-03-08',
      machine: 'm',
      heartbeats: [
        hb(new Date(2026, 2, 8, 1, 58).getTime() / 1000),
        hb(new Date(2026, 2, 8, 3, 1).getTime() / 1000),
        hb(new Date(2026, 2, 9, 0, 0).getTime() / 1000), // next local day: excluded
      ],
      claudeEvents: [],
      aliases: {},
      settings: { idleMinutes: 15 },
      generatedAt: '2026-03-09T00:00:00.000Z',
      caveats: { unreadableTranscriptLines: 0, readerVersion: '0.1.0' },
    });
    expect(d.totals.youMs).toBe(3 * MIN);
  });
});
