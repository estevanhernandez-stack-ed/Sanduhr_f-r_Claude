// Synthetic day records for UI tests. Invented names only.
import type { DayRecord, DayTotals, ProjectDay } from '../types';

export function totals(over: Partial<DayTotals> = {}): DayTotals {
  const t = { youMs: 0, agentsMs: 0, bothMs: 0, totalMs: 0, agentMs: 0, linesYou: 0, linesAgents: 0, byAgent: {}, ...over };
  if (over.totalMs === undefined) t.totalMs = t.youMs + t.agentsMs + t.bothMs;
  return t;
}

export function project(over: Partial<ProjectDay> & { key: string }): ProjectDay {
  return {
    realName: 'Demo App',
    alias: 'Project Aster',
    masked: false,
    languages: [],
    ...totals(over),
    ...over,
  } as ProjectDay;
}

export function day(date: string, tot: Partial<DayTotals>, projects: ProjectDay[] = [], unreadable = 0): DayRecord {
  return {
    v: 2,
    date,
    machine: 'fixture-machine',
    generatedAt: `${date}T12:00:00.000Z`,
    totals: totals(tot),
    projects,
    caveats: { unreadableTranscriptLines: unreadable, readerVersion: '0.1.0', claudeCodeVersions: [] },
  };
}
