/**
 * Pure interval math. All values are epoch milliseconds. Inputs may be unsorted or
 * overlapping; every function normalizes first and returns sorted, non-overlapping,
 * non-touching intervals.
 */
import type { Interval } from './types';

/**
 * Join event times into intervals. Consecutive events whose gap is <= gapMs (a gap of
 * exactly the limit joins) form one interval spanning first to last event. Only a
 * single-event interval gets `loneMs` of duration. Identical timestamps collapse to
 * one event first, so duplicates never fake a multi-event stretch.
 */
export function joinEvents(timesMs: number[], gapMs: number, loneMs: number): Interval[] {
  const times = [...new Set(timesMs)].sort((a, b) => a - b);
  const out: Interval[] = [];
  let first = 0;
  for (let i = 0; i < times.length; i++) {
    const last = i === times.length - 1 || times[i + 1] - times[i] > gapMs;
    if (!last) continue;
    if (i === first) out.push({ start: times[i], end: times[i] + loneMs });
    else out.push({ start: times[first], end: times[i] });
    first = i + 1;
  }
  return out;
}

/** Sort, drop empty (end <= start) intervals, and merge overlapping or touching ones. */
export function normalize(intervals: Interval[]): Interval[] {
  const sorted = intervals
    .filter((iv) => iv.end > iv.start)
    .map((iv) => ({ start: iv.start, end: iv.end }))
    .sort((a, b) => a.start - b.start || a.end - b.end);
  const out: Interval[] = [];
  for (const iv of sorted) {
    const prev = out[out.length - 1];
    if (prev && iv.start <= prev.end) prev.end = Math.max(prev.end, iv.end);
    else out.push(iv);
  }
  return out;
}

export function union(a: Interval[], b: Interval[] = []): Interval[] {
  return normalize([...a, ...b]);
}

export function intersect(a: Interval[], b: Interval[]): Interval[] {
  const x = normalize(a);
  const y = normalize(b);
  const out: Interval[] = [];
  let i = 0;
  let j = 0;
  while (i < x.length && j < y.length) {
    const start = Math.max(x[i].start, y[j].start);
    const end = Math.min(x[i].end, y[j].end);
    if (end > start) out.push({ start, end });
    if (x[i].end < y[j].end) i++;
    else j++;
  }
  return out;
}

/** The parts of `a` not covered by `b`. */
export function subtract(a: Interval[], b: Interval[]): Interval[] {
  const x = normalize(a);
  const y = normalize(b);
  const out: Interval[] = [];
  let j = 0;
  for (const iv of x) {
    let cursor = iv.start;
    while (j < y.length && y[j].end <= cursor) j++;
    let k = j;
    while (k < y.length && y[k].start < iv.end) {
      if (y[k].start > cursor) out.push({ start: cursor, end: y[k].start });
      cursor = Math.max(cursor, y[k].end);
      k++;
    }
    if (cursor < iv.end) out.push({ start: cursor, end: iv.end });
  }
  return out;
}

/** Restrict intervals to [startMs, endMs]. */
export function clip(intervals: Interval[], startMs: number, endMs: number): Interval[] {
  return intersect(intervals, [{ start: startMs, end: endMs }]);
}

/** Total milliseconds covered (overlaps counted once). */
export function totalLength(intervals: Interval[]): number {
  return normalize(intervals).reduce((sum, iv) => sum + (iv.end - iv.start), 0);
}
