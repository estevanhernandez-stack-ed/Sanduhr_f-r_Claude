import * as fs from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { acquireLock, defaultIsPidAlive, ensureInstalled, readOffsets, writeOffsets } from './state';

let dir: string;
beforeEach(() => {
  dir = fs.mkdtempSync(path.join(os.tmpdir(), 'sanduhr-state-'));
});
afterEach(() => {
  fs.rmSync(dir, { recursive: true, force: true });
});

describe('ensureInstalled', () => {
  it('creates installed.json with an ISO date and a 32-byte hex salt', () => {
    const { installed, isNew } = ensureInstalled(dir, () => new Date('2026-01-02T03:04:05.000Z'));
    expect(isNew).toBe(true);
    expect(installed.installedAt).toBe('2026-01-02T03:04:05.000Z');
    expect(installed.salt).toMatch(/^[0-9a-f]{64}$/);
    expect(JSON.parse(fs.readFileSync(path.join(dir, 'installed.json'), 'utf8'))).toEqual(installed);
  });

  it('never overwrites an existing file', () => {
    const first = ensureInstalled(dir);
    const second = ensureInstalled(dir, () => new Date('2030-01-01T00:00:00.000Z'));
    expect(second.isNew).toBe(false);
    expect(second.installed).toEqual(first.installed);
  });

  it('gives different installs different salts', () => {
    const other = fs.mkdtempSync(path.join(os.tmpdir(), 'sanduhr-state-'));
    try {
      expect(ensureInstalled(dir).installed.salt).not.toBe(ensureInstalled(other).installed.salt);
    } finally {
      fs.rmSync(other, { recursive: true, force: true });
    }
  });
});

describe('offsets', () => {
  it('reads a missing file as empty', () => {
    expect(readOffsets(dir)).toEqual({});
  });
  it('round-trips and leaves no tmp files behind', () => {
    const offsets = { '/fixture/a.jsonl': { offset: 10, size: 20, mtimeMs: 30 } };
    writeOffsets(dir, offsets);
    expect(readOffsets(dir)).toEqual(offsets);
    expect(fs.readdirSync(dir)).toEqual(['offsets.json']);
  });
  it('overwrites on a second write', () => {
    writeOffsets(dir, { a: { offset: 1, size: 1, mtimeMs: 1 } });
    writeOffsets(dir, { b: { offset: 2, size: 2, mtimeMs: 2 } });
    expect(readOffsets(dir)).toEqual({ b: { offset: 2, size: 2, mtimeMs: 2 } });
  });
});

describe('merge lock', () => {
  const T0 = 1_000_000_000_000;
  const lockFile = () => path.join(dir, 'merge.lock');

  it('is created exclusively with pid and at', () => {
    const lock = acquireLock(dir, { now: () => T0, pid: 111 });
    expect(lock).toBeDefined();
    expect(JSON.parse(fs.readFileSync(lockFile(), 'utf8'))).toEqual({ pid: 111, at: T0 });
  });

  it('a fresh live lock blocks a second holder', () => {
    acquireLock(dir, { now: () => T0, pid: 111 });
    expect(acquireLock(dir, { now: () => T0 + 1000, pid: 222, isPidAlive: () => true })).toBeUndefined();
  });

  it('an old lock whose pid is still running is live', () => {
    acquireLock(dir, { now: () => T0, pid: 111 });
    const second = acquireLock(dir, { now: () => T0 + 10 * 60_000, pid: 222, isPidAlive: () => true });
    expect(second).toBeUndefined();
    expect(JSON.parse(fs.readFileSync(lockFile(), 'utf8')).pid).toBe(111);
  });

  it('a recent lock whose pid is gone is still live (inside the 2 minute window)', () => {
    acquireLock(dir, { now: () => T0, pid: 111 });
    expect(acquireLock(dir, { now: () => T0 + 119_000, pid: 222, isPidAlive: () => false })).toBeUndefined();
  });

  it('is stale only when over 2 minutes old and the pid is not running', () => {
    acquireLock(dir, { now: () => T0, pid: 111 });
    // exactly 2 minutes is not "more than"
    expect(acquireLock(dir, { now: () => T0 + 120_000, pid: 222, isPidAlive: () => false })).toBeUndefined();
    const taken = acquireLock(dir, { now: () => T0 + 120_001, pid: 222, isPidAlive: () => false });
    expect(taken).toBeDefined();
    expect(JSON.parse(fs.readFileSync(lockFile(), 'utf8'))).toEqual({ pid: 222, at: T0 + 120_001 });
  });

  it('refresh rewrites at, which keeps a long holder live', () => {
    let now = T0;
    const lock = acquireLock(dir, { now: () => now, pid: 111 })!;
    now = T0 + 100_000;
    lock.refresh();
    expect(JSON.parse(fs.readFileSync(lockFile(), 'utf8')).at).toBe(T0 + 100_000);
    expect(acquireLock(dir, { now: () => T0 + 200_000, pid: 222, isPidAlive: () => false })).toBeUndefined();
  });

  it('release removes the lock so it can be taken again', () => {
    const lock = acquireLock(dir, { now: () => T0, pid: 111 })!;
    lock.release();
    expect(fs.existsSync(lockFile())).toBe(false);
    expect(acquireLock(dir, { now: () => T0, pid: 222 })).toBeDefined();
  });

  it('release does not remove a lock another holder took over', () => {
    const stale = acquireLock(dir, { now: () => T0, pid: 111 })!;
    acquireLock(dir, { now: () => T0 + 200_000, pid: 222, isPidAlive: () => false });
    stale.release();
    expect(JSON.parse(fs.readFileSync(lockFile(), 'utf8')).pid).toBe(222);
  });

  it('an unparseable fresh lock is treated as live', () => {
    fs.writeFileSync(lockFile(), '{not json');
    expect(acquireLock(dir, { isPidAlive: () => false })).toBeUndefined();
  });

  it('default pid check: own pid is alive, an unused pid is not', () => {
    expect(defaultIsPidAlive(process.pid)).toBe(true);
    // 2^22 is above the default pid ceiling on Linux and Windows pids are far smaller.
    expect(defaultIsPidAlive(4_194_304 + 12345)).toBe(false);
  });
});
