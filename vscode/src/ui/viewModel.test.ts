import { describe, expect, it } from 'vitest';
import type { MaskState } from '../store/aliases';
import { day, project } from './fixtures';
import {
  EMPTY_MESSAGE,
  FOOTER,
  MASKED_FALLBACK,
  buildPanelModel,
  buildStatusBar,
  formatDuration,
  fractions,
} from './viewModel';

const MIN = 60_000;
const H = 60 * MIN;
const open: MaskState = { readable: true, streamerMode: false, projects: {} };

describe('formatDuration', () => {
  it('formats hours, minutes and sub-minute values', () => {
    expect(formatDuration(3 * H + 12 * MIN)).toBe('3h 12m');
    expect(formatDuration(45 * MIN)).toBe('45m');
    expect(formatDuration(30_000)).toBe('<1m');
    expect(formatDuration(1)).toBe('<1m');
    expect(formatDuration(0)).toBe('0m');
    expect(formatDuration(-5)).toBe('0m');
    expect(formatDuration(NaN)).toBe('0m');
  });
  it('pads minutes once hours show and keeps whole hours', () => {
    expect(formatDuration(2 * H + 5 * MIN)).toBe('2h 05m');
    expect(formatDuration(2 * H)).toBe('2h 00m');
    expect(formatDuration(H - 1)).toBe('59m');
    expect(formatDuration(100 * H)).toBe('100h 00m');
  });
});

describe('fractions', () => {
  it('sums to exactly 1 for awkward values', () => {
    for (const t of [
      { youMs: 1, claudeMs: 1, bothMs: 1 },
      { youMs: 7, claudeMs: 11, bothMs: 13 },
      { youMs: 100 * MIN, claudeMs: 125 * MIN, bothMs: 33 * MIN },
      { youMs: 1, claudeMs: 0, bothMs: 0 },
    ]) {
      const f = fractions(t);
      expect(f.you + f.claude + f.both).toBeCloseTo(1, 12);
    }
  });
  it('is all zero with no time and never negative', () => {
    expect(fractions({ youMs: 0, claudeMs: 0, bothMs: 0 })).toEqual({ you: 0, claude: 0, both: 0 });
    const f = fractions({ youMs: -5, claudeMs: 10, bothMs: 0 });
    expect(f.you).toBe(0);
    expect(f.claude).toBe(1);
  });
});

describe('buildStatusBar', () => {
  it('shows the total and the split tooltip', () => {
    const today = day('2026-10-06', { youMs: 100 * MIN, claudeMs: 125 * MIN, bothMs: 33 * MIN });
    expect(buildStatusBar(today)).toEqual({
      text: '$(watch) 4h 18m',
      tooltip: 'You 1h 40m · Claude 2h 05m · Both 33m',
    });
  });
  it('handles no day yet and the remote window', () => {
    expect(buildStatusBar(undefined).text).toBe('$(watch) 0m');
    expect(buildStatusBar(undefined, { remote: true })).toEqual({ text: '$(watch) off', tooltip: 'Local workspaces only' });
    expect(buildStatusBar(day('2026-10-06', { youMs: H }), { remote: true }).text).toBe('$(watch) off');
  });
});

describe('buildPanelModel headline and strip', () => {
  const today = '2026-10-06';
  const days = [
    day('2026-10-02', { youMs: 2 * H }),
    day('2026-10-05', { youMs: H, claudeMs: H, bothMs: 0 }),
    day(today, { youMs: 100 * MIN, claudeMs: 125 * MIN, bothMs: 33 * MIN }),
  ];

  it('headline carries total and the three splits for today', () => {
    const m = buildPanelModel(days, open, today);
    expect(m.headline).toEqual({ total: '4h 18m', you: '1h 40m', claude: '2h 05m', both: '33m' });
    const f = m.headlineFractions;
    expect(f.you + f.claude + f.both).toBeCloseTo(1, 12);
  });

  it('strip has seven days ending today, zero-filled, with relative heights', () => {
    const m = buildPanelModel(days, open, today);
    expect(m.strip.map((s) => s.date)).toEqual([
      '2026-09-30', '2026-10-01', '2026-10-02', '2026-10-03', '2026-10-04', '2026-10-05', '2026-10-06',
    ]);
    expect(m.strip[0].split.total).toBe('0m');
    expect(m.strip[0].height).toBe(0);
    expect(m.strip[6].isToday).toBe(true);
    expect(m.strip[6].height).toBe(1);
    expect(m.strip[2].height).toBeCloseTo(120 / 258, 6);
    expect(m.strip[5].split).toEqual({ total: '2h 00m', you: '1h 00m', claude: '1h 00m', both: '0m' });
    expect(m.strip[6].label).toBe('Tue 10-06');
  });

  it('crosses month and year boundaries', () => {
    expect(buildPanelModel([], open, '2027-01-03').strip[0].date).toBe('2026-12-28');
  });
});

describe('empty state', () => {
  it('flags a missing today and a zero today with no projects', () => {
    const none = buildPanelModel([], open, '2026-10-06');
    expect(none.empty).toBe(true);
    expect(none.emptyMessage).toBe(EMPTY_MESSAGE);
    expect(none.emptyMessage).toBe('Tracking started. Your first numbers appear after a few minutes of work.');
    expect(buildPanelModel([day('2026-10-06', {})], open, '2026-10-06').empty).toBe(true);
  });
  it('is not empty once there is time', () => {
    expect(buildPanelModel([day('2026-10-06', { youMs: MIN })], open, '2026-10-06').empty).toBe(false);
  });
  it('a remote window is never the empty state', () => {
    const m = buildPanelModel([], open, '2026-10-06', undefined, { remote: true });
    expect(m.empty).toBe(false);
    expect(m.remote).toBe(true);
    expect(m.notice).toMatch(/Local workspaces only/);
  });
});

describe('project rows', () => {
  const today = '2026-10-06';
  const a = project({
    key: 'aaaa', realName: 'Demo App', alias: 'Project Aster',
    youMs: 30 * MIN, claudeMs: 10 * MIN, bothMs: 5 * MIN, totalMs: 45 * MIN, agentMs: 12 * MIN, linesYou: 40, linesClaude: 220,
    languages: [{ id: 'ts', ms: 20 * MIN }, { id: 'css', ms: 2 * MIN }, { id: 'json', ms: 9 * MIN }, { id: 'md', ms: 1 * MIN }],
  });
  const b = project({ key: 'bbbb', realName: 'Widget Lab', alias: 'Project Birch', youMs: 2 * H });
  const c = project({ key: 'cccc', realName: 'Tiny Tool', alias: 'Project Cedar', youMs: 20_000 });
  const days = [day(today, {}, [a, c, b])];

  it('sorts by total, descending', () => {
    expect(buildPanelModel(days, open, today).projects.map((p) => p.name)).toEqual(['Widget Lab', 'Demo App', 'Tiny Tool']);
  });

  it('carries splits, fractions, agent time, lines and the top three languages', () => {
    const m = buildPanelModel(days, open, today);
    const row = m.projects.find((p) => p.key === 'aaaa')!;
    expect(row.split).toEqual({ you: '30m', claude: '10m', both: '5m', total: '45m' });
    expect(row.fractions.you + row.fractions.claude + row.fractions.both).toBeCloseTo(1, 12);
    expect(row.agent).toBe('12m');
    expect(row.linesYou).toBe(40);
    expect(row.linesClaude).toBe(220);
    expect(row.languages).toEqual([
      { id: 'ts', time: '20m' },
      { id: 'json', time: '9m' },
      { id: 'css', time: '2m' },
    ]);
    expect(m.projects.find((p) => p.key === 'cccc')!.split.total).toBe('<1m');
  });

  it('breaks ties by key so the order is stable', () => {
    const x = project({ key: 'zzzz', youMs: MIN });
    const y = project({ key: 'yyyy', youMs: MIN });
    expect(buildPanelModel([day(today, {}, [x, y])], open, today).projects.map((p) => p.key)).toEqual(['yyyy', 'zzzz']);
  });
});

describe('masking', () => {
  const today = '2026-10-06';
  const p = project({ key: 'aaaa', realName: 'Demo App', alias: 'Project Aster', youMs: H });
  const days = [day(today, {}, [p])];
  const names = (mask: MaskState, list = days): string[] => buildPanelModel(list, mask, today).projects.map((r) => r.name);

  it('shows the real name when unmasked', () => {
    expect(names(open)).toEqual(['Demo App']);
  });
  it('uses the alias when the live mask state masks the project', () => {
    const mask: MaskState = { readable: true, streamerMode: false, projects: { aaaa: { alias: 'Project Birch', masked: true } } };
    const row = buildPanelModel(days, mask, today).projects[0];
    expect(row.name).toBe('Project Birch');
    expect(row.masked).toBe(true);
    expect(row.maskFlag).toBe(true);
  });
  it('live mask state wins over the day record flag', () => {
    const maskedRecord = [day(today, {}, [project({ key: 'aaaa', masked: true, alias: 'Project Aster', youMs: H })])];
    const live: MaskState = { readable: true, streamerMode: false, projects: { aaaa: { alias: 'Project Aster', masked: false } } };
    expect(names(live, maskedRecord)).toEqual(['Demo App']);
  });
  it('streamer mode aliases everything but leaves each project flag alone', () => {
    const mask: MaskState = { readable: true, streamerMode: true, projects: { aaaa: { alias: 'Project Birch', masked: false } } };
    const m = buildPanelModel(days, mask, today);
    expect(m.projects[0].name).toBe('Project Birch');
    expect(m.projects[0].masked).toBe(true);
    expect(m.projects[0].maskFlag).toBe(false);
    expect(m.streamerMode).toBe(true);
  });
  it('falls back to the day record alias when the live store has none for the key', () => {
    expect(names({ readable: true, streamerMode: true, projects: {} })).toEqual(['Project Aster']);
  });
  it('shows "Masked project" when masked with no alias anywhere, never the real name', () => {
    const bare = [day(today, {}, [project({ key: 'aaaa', alias: null, youMs: H })])];
    expect(names({ readable: true, streamerMode: true, projects: {} }, bare)).toEqual([MASKED_FALLBACK]);
    expect(names({ readable: true, streamerMode: false, projects: { aaaa: { alias: '  ', masked: true } } }, bare)).toEqual([MASKED_FALLBACK]);
  });
  it('fails closed when the alias store is unreadable', () => {
    const closed: MaskState = { readable: false, streamerMode: false, projects: {} };
    const m = buildPanelModel(days, closed, today);
    expect(m.projects[0].name).toBe('Project Aster');
    expect(m.maskReadable).toBe(false);
    expect(m.notice).toMatch(/unreadable/);
    const bare = [day(today, {}, [project({ key: 'aaaa', alias: null, youMs: H })])];
    expect(names(closed, bare)).toEqual([MASKED_FALLBACK]);
  });
  it('never puts a real name anywhere in a masked model', () => {
    const mask: MaskState = { readable: true, streamerMode: true, projects: { aaaa: { alias: 'Project Birch', masked: true } } };
    expect(JSON.stringify(buildPanelModel(days, mask, today))).not.toContain('Demo App');
  });
});

describe('caveats, compare and footer', () => {
  const today = '2026-10-06';
  it('shows the caveats note only when unreadable lines exist', () => {
    expect(buildPanelModel([day(today, { youMs: MIN })], open, today).caveats).toBeNull();
    expect(buildPanelModel([day(today, { youMs: MIN }, [], 3)], open, today).caveats).toBe(
      '3 transcript lines could not be read, so Claude time may be slightly low.',
    );
    expect(buildPanelModel([day(today, { youMs: MIN }, [], 1)], open, today).caveats).toMatch(/^1 transcript line could/);
  });
  it('builds the WakaTime line from you plus both, with a signed difference', () => {
    const d = [day(today, { youMs: H, claudeMs: H, bothMs: 10 * MIN })];
    expect(buildPanelModel(d, open, today, { wakatimeSeconds: 70 * 60 }).compare).toBe(
      'WakaTime 1h 10m · Sanduhr editor time 1h 10m · difference 0m',
    );
    expect(buildPanelModel(d, open, today, { wakatimeSeconds: 60 * 60 }).compare).toMatch(/difference \+10m$/);
    expect(buildPanelModel(d, open, today, { wakatimeSeconds: 80 * 60 }).compare).toMatch(/difference -10m$/);
    expect(buildPanelModel(d, open, today).compare).toBeNull();
  });
  it('always carries the trademark footer', () => {
    expect(buildPanelModel([], open, today).footer).toBe(FOOTER);
    expect(FOOTER).toBe('Claude is a trademark of Anthropic. Sanduhr Time is not affiliated with or endorsed by Anthropic.');
  });
});
