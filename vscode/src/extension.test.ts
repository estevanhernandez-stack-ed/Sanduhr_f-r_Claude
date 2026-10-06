import { describe, expect, it } from 'vitest';
import { activate, deactivate } from './extension';

describe('extension', () => {
  it('activate returns the public API at version 1', () => {
    expect(activate()).toEqual({ version: 1 });
  });

  it('deactivate is a no-op', () => {
    expect(deactivate()).toBeUndefined();
  });
});
