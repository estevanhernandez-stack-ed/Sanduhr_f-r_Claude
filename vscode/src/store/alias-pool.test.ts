import { describe, expect, it } from 'vitest';
import { ALIAS_POOL, nextAlias } from './alias-pool';

describe('alias pool', () => {
  it('has about 100 unique "Project <Word>" names', () => {
    expect(ALIAS_POOL.length).toBeGreaterThanOrEqual(100);
    expect(new Set(ALIAS_POOL).size).toBe(ALIAS_POOL.length);
    for (const name of ALIAS_POOL) expect(name).toMatch(/^Project [A-Z][a-z]+$/);
  });

  it('picks an unused name and is deterministic', () => {
    expect(nextAlias(new Set())).toBe(nextAlias(new Set()));
    const used = new Set<string>();
    for (let i = 0; i < ALIAS_POOL.length; i++) {
      const a = nextAlias(used);
      expect(used.has(a)).toBe(false);
      used.add(a);
    }
    expect(used.size).toBe(ALIAS_POOL.length);
  });

  it('appends a number once the pool is exhausted, and keeps them unique', () => {
    const used = new Set<string>(ALIAS_POOL);
    const next = nextAlias(used);
    expect(next).toMatch(/^Project [A-Z][a-z]+ 2$/);
    for (let i = 0; i < ALIAS_POOL.length * 2; i++) {
      const a = nextAlias(used);
      expect(used.has(a)).toBe(false);
      used.add(a);
    }
    expect([...used].some((a) => a.endsWith(' 3'))).toBe(true);
  });
});
