import { describe, expect, it } from 'vitest';
import { clip, intersect, joinEvents, normalize, subtract, totalLength, union } from './intervals';

const iv = (start: number, end: number) => ({ start, end });
const MIN = 60_000;

describe('joinEvents', () => {
  it('returns nothing for no events', () => {
    expect(joinEvents([], 15 * MIN, 2 * MIN)).toEqual([]);
  });

  it('gives a lone event the lone duration', () => {
    expect(joinEvents([1000], 15 * MIN, 2 * MIN)).toEqual([iv(1000, 1000 + 2 * MIN)]);
  });

  it('spans first to last for joined events, with no lone padding', () => {
    expect(joinEvents([0, 5 * MIN, 9 * MIN], 15 * MIN, 2 * MIN)).toEqual([iv(0, 9 * MIN)]);
  });

  it('joins a gap of exactly the limit', () => {
    expect(joinEvents([0, 15 * MIN], 15 * MIN, 2 * MIN)).toEqual([iv(0, 15 * MIN)]);
  });

  it('splits when the gap is one millisecond over the limit, each side lone', () => {
    expect(joinEvents([0, 15 * MIN + 1], 15 * MIN, 2 * MIN)).toEqual([
      iv(0, 2 * MIN),
      iv(15 * MIN + 1, 15 * MIN + 1 + 2 * MIN),
    ]);
  });

  it('handles unsorted input', () => {
    expect(joinEvents([9 * MIN, 0, 5 * MIN, 100 * MIN], 15 * MIN, 2 * MIN)).toEqual([
      iv(0, 9 * MIN),
      iv(100 * MIN, 102 * MIN),
    ]);
  });

  it('collapses duplicate timestamps to one lone event', () => {
    expect(joinEvents([500, 500, 500], 15 * MIN, 2 * MIN)).toEqual([iv(500, 500 + 2 * MIN)]);
  });
});

describe('normalize', () => {
  it('sorts, merges overlaps and touching intervals, drops empties', () => {
    expect(normalize([iv(10, 20), iv(0, 5), iv(5, 8), iv(15, 30), iv(40, 40), iv(50, 45)])).toEqual([
      iv(0, 8),
      iv(10, 30),
    ]);
  });

  it('does not mutate its input', () => {
    const input = [iv(5, 10), iv(0, 6)];
    normalize(input);
    expect(input).toEqual([iv(5, 10), iv(0, 6)]);
  });
});

describe('union', () => {
  it('handles empty inputs', () => {
    expect(union([], [])).toEqual([]);
    expect(union([iv(1, 2)], [])).toEqual([iv(1, 2)]);
  });
  it('merges touching intervals', () => {
    expect(union([iv(0, 5)], [iv(5, 9)])).toEqual([iv(0, 9)]);
  });
  it('absorbs containment', () => {
    expect(union([iv(0, 100)], [iv(10, 20), iv(30, 40)])).toEqual([iv(0, 100)]);
  });
  it('normalizes unsorted overlapping input', () => {
    expect(union([iv(20, 30), iv(0, 10)], [iv(5, 22)])).toEqual([iv(0, 30)]);
  });
});

describe('intersect', () => {
  it('is empty when either side is empty', () => {
    expect(intersect([], [iv(0, 10)])).toEqual([]);
    expect(intersect([iv(0, 10)], [])).toEqual([]);
  });
  it('treats touching intervals as no overlap', () => {
    expect(intersect([iv(0, 5)], [iv(5, 10)])).toEqual([]);
  });
  it('returns the contained interval', () => {
    expect(intersect([iv(0, 100)], [iv(10, 20)])).toEqual([iv(10, 20)]);
  });
  it('handles partial overlaps across many pieces, unsorted', () => {
    expect(intersect([iv(20, 40), iv(0, 10)], [iv(5, 25), iv(35, 50)])).toEqual([
      iv(5, 10),
      iv(20, 25),
      iv(35, 40),
    ]);
  });
  it('normalizes overlapping input before intersecting', () => {
    expect(intersect([iv(0, 10), iv(5, 15)], [iv(0, 20)])).toEqual([iv(0, 15)]);
  });
});

describe('subtract', () => {
  it('returns a unchanged for an empty b', () => {
    expect(subtract([iv(0, 10)], [])).toEqual([iv(0, 10)]);
  });
  it('returns empty for an empty a', () => {
    expect(subtract([], [iv(0, 10)])).toEqual([]);
  });
  it('removes a fully covered interval', () => {
    expect(subtract([iv(5, 10)], [iv(0, 20)])).toEqual([]);
  });
  it('splits around a contained hole', () => {
    expect(subtract([iv(0, 100)], [iv(10, 20), iv(30, 40)])).toEqual([
      iv(0, 10),
      iv(20, 30),
      iv(40, 100),
    ]);
  });
  it('leaves touching intervals intact', () => {
    expect(subtract([iv(0, 5)], [iv(5, 10)])).toEqual([iv(0, 5)]);
    expect(subtract([iv(5, 10)], [iv(0, 5)])).toEqual([iv(5, 10)]);
  });
  it('trims the edges', () => {
    expect(subtract([iv(0, 10), iv(20, 30)], [iv(5, 25)])).toEqual([iv(0, 5), iv(25, 30)]);
  });
  it('handles one b interval spanning several a intervals, unsorted', () => {
    expect(subtract([iv(40, 50), iv(0, 10), iv(20, 30)], [iv(8, 45)])).toEqual([
      iv(0, 8),
      iv(45, 50),
    ]);
  });
});

describe('clip', () => {
  it('cuts intervals at the bounds', () => {
    expect(clip([iv(-10, 5), iv(8, 20), iv(30, 40)], 0, 10)).toEqual([iv(0, 5), iv(8, 10)]);
  });
  it('returns empty when nothing falls inside', () => {
    expect(clip([iv(0, 5)], 10, 20)).toEqual([]);
    expect(clip([], 0, 10)).toEqual([]);
  });
});

describe('totalLength', () => {
  it('is zero for empty', () => {
    expect(totalLength([])).toBe(0);
  });
  it('counts overlaps once', () => {
    expect(totalLength([iv(0, 10), iv(5, 15), iv(100, 110)])).toBe(25);
  });
});

describe('set identities', () => {
  it('|A ∪ B| = |A| + |B| - |A ∩ B| and |A − B| = |A| - |A ∩ B|', () => {
    const a = [iv(0, 40), iv(60, 90), iv(30, 50)];
    const b = [iv(20, 70), iv(85, 120)];
    expect(totalLength(union(a, b))).toBe(totalLength(a) + totalLength(b) - totalLength(intersect(a, b)));
    expect(totalLength(subtract(a, b))).toBe(totalLength(a) - totalLength(intersect(a, b)));
  });
});
