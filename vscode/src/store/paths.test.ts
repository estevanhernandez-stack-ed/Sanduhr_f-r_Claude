import * as os from 'node:os';
import * as path from 'node:path';
import { describe, expect, it } from 'vitest';
import { claudeDir, dataDir, daysDir, spoolDir, stateDir } from './paths';

describe('dataDir', () => {
  it('prefers a non-empty setting', () => {
    expect(dataDir('/fixture/custom', { SANDUHR_TIME_DIR: '/fixture/env' })).toBe('/fixture/custom');
  });
  it('ignores an empty or blank setting and uses the env var', () => {
    expect(dataDir('', { SANDUHR_TIME_DIR: '/fixture/env' })).toBe('/fixture/env');
    expect(dataDir('   ', { SANDUHR_TIME_DIR: '/fixture/env' })).toBe('/fixture/env');
    expect(dataDir(undefined, { SANDUHR_TIME_DIR: '/fixture/env' })).toBe('/fixture/env');
  });
  it('falls back to ~/.sanduhr/time', () => {
    expect(dataDir(undefined, {})).toBe(path.join(os.homedir(), '.sanduhr', 'time'));
    expect(dataDir('', { SANDUHR_TIME_DIR: '' })).toBe(path.join(os.homedir(), '.sanduhr', 'time'));
  });
  it('has subdir helpers', () => {
    const d = path.join('/fixture', 'time');
    expect(spoolDir(d)).toBe(path.join(d, 'spool'));
    expect(claudeDir(d)).toBe(path.join(d, 'claude'));
    expect(daysDir(d)).toBe(path.join(d, 'days'));
    expect(stateDir(d)).toBe(path.join(d, 'state'));
  });
});
