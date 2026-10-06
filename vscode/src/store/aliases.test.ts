import * as fs from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { ALIAS_POOL } from './alias-pool';
import { AliasStore, AliasStoreBusy, AliasStoreUnavailable, type AliasStoreOptions, type AliasesFile } from './aliases';
import { acquireLock } from './state';

let dir: string;
beforeEach(() => {
  dir = fs.mkdtempSync(path.join(os.tmpdir(), 'sanduhr-alias-'));
});
afterEach(() => {
  fs.rmSync(dir, { recursive: true, force: true });
});

const file = (): string => path.join(dir, 'aliases.json');
const onDisk = (): AliasesFile => JSON.parse(fs.readFileSync(file(), 'utf8')) as AliasesFile;
const fresh = (o: Partial<AliasStoreOptions> = {}): AliasStore => new AliasStore(dir, { isNew: true, ...o });
const existing = (o: Partial<AliasStoreOptions> = {}): AliasStore => new AliasStore(dir, { isNew: false, ...o });

describe('alias assignment', () => {
  it('assigns lazily, persists, and returns the same alias next time', async () => {
    const s = fresh();
    expect(fs.existsSync(file())).toBe(false);
    const a = await s.aliasFor('k1', 'widget');
    expect(ALIAS_POOL).toContain(a);
    expect(onDisk()).toMatchObject({ v: 1, streamerMode: false, projects: { k1: { realName: 'widget', alias: a, masked: false } } });
    expect(await s.aliasFor('k1', 'widget')).toBe(a);
    expect(await existing().aliasFor('k1')).toBe(a);
  });

  it('gives different projects different aliases and updates a renamed project in place', async () => {
    const s = fresh();
    const a = await s.aliasFor('k1', 'widget');
    const b = await s.aliasFor('k2', 'gadget');
    expect(a).not.toBe(b);
    expect(await s.aliasFor('k1', 'widget-renamed')).toBe(a);
    expect(onDisk().projects.k1.realName).toBe('widget-renamed');
  });

  it('reroll picks a different unused alias', async () => {
    const s = fresh();
    const a = await s.aliasFor('k1', 'widget');
    const b = await s.reroll('k1');
    expect(b).not.toBe(a);
    expect(onDisk().projects.k1.alias).toBe(b);
    const c = await s.aliasFor('k2', 'gadget');
    expect(new Set([b, c]).size).toBe(2);
  });

  it('mask and streamer mode round-trip through maskState', async () => {
    const s = fresh();
    const a = await s.aliasFor('k1', 'widget');
    await s.setMasked('k1', true);
    await s.setStreamerMode(true);
    expect(s.maskState()).toEqual({ readable: true, streamerMode: true, projects: { k1: { alias: a, masked: true } } });
    await s.setMasked('k1', false);
    await s.setStreamerMode(false);
    expect(existing().maskState()).toEqual({ readable: true, streamerMode: false, projects: { k1: { alias: a, masked: false } } });
  });

  it('ensureHeld assigns several keys in one write', () => {
    const s = fresh();
    const out = s.ensureHeld(
      new Map<string, string | undefined>([
        ['b', 'bee'],
        ['a', undefined],
      ]),
    );
    expect(Object.keys(out).sort()).toEqual(['a', 'b']);
    expect(out.a.alias).not.toBe(out.b.alias);
    expect(onDisk().projects.b.realName).toBe('bee');
  });
});

describe('fail closed', () => {
  it('a fresh install (this activation created installed.json) treats a missing file as empty', () => {
    expect(fresh().maskState()).toEqual({ readable: true, streamerMode: false, projects: {} });
  });

  it('a missing file on an existing install is unavailable, and maskState is unreadable', async () => {
    const s = existing();
    expect(() => s.read()).toThrow(AliasStoreUnavailable);
    expect(s.maskState()).toEqual({ readable: false, streamerMode: false, projects: {} });
    await expect(s.aliasFor('k1', 'widget')).rejects.toBeInstanceOf(AliasStoreUnavailable);
    await expect(s.setStreamerMode(true)).rejects.toBeInstanceOf(AliasStoreUnavailable);
    expect(fs.existsSync(file())).toBe(false); // never recreates an empty store behind a publisher
  });

  it('a file deleted later in a fresh activation is an error, not a silent reset', async () => {
    const s = fresh();
    await s.aliasFor('k1', 'widget');
    fs.rmSync(file());
    expect(() => s.read()).toThrow(AliasStoreUnavailable);
    expect(s.maskState().readable).toBe(false);
  });

  it('an unparseable or wrongly shaped file is unavailable, even on a fresh install', () => {
    fs.writeFileSync(file(), '{ not json');
    expect(fresh().maskState().readable).toBe(false);
    expect(() => fresh().read()).toThrow(AliasStoreUnavailable);
    fs.writeFileSync(file(), JSON.stringify({ v: 2, projects: {} }));
    expect(existing().maskState().readable).toBe(false);
    fs.writeFileSync(file(), JSON.stringify({ v: 1 }));
    expect(existing().maskState().readable).toBe(false);
  });
});

describe('the merge lock', () => {
  it('a write waits for a held lock and goes through once it is released', async () => {
    const holder = acquireLock(path.join(dir, 'state'), { pid: 1 })!;
    let slept = 0;
    const s = fresh({
      sleep: async () => {
        if (++slept === 3) holder.release();
      },
    });
    const a = await s.aliasFor('k1', 'widget');
    expect(slept).toBe(3);
    expect(onDisk().projects.k1.alias).toBe(a);
    expect(fs.existsSync(path.join(dir, 'state', 'merge.lock'))).toBe(false); // released after the write
  });

  it('gives up with AliasStoreBusy after the wait budget', async () => {
    acquireLock(path.join(dir, 'state'), { pid: 1 });
    const s = fresh({ lockWaitMs: 30, sleep: (ms: number) => new Promise((r) => setTimeout(r, ms)) });
    await expect(s.setStreamerMode(true)).rejects.toBeInstanceOf(AliasStoreBusy);
  });

  it('two stores on one file never lose each other assignments', async () => {
    const a = fresh();
    const b = existing();
    await a.aliasFor('seed', 'seed');
    const keys = Array.from({ length: 12 }, (_, i) => `k${i}`);
    await Promise.all(keys.map((k, i) => (i % 2 ? a : b).aliasFor(k, k)));
    const projects = onDisk().projects;
    expect(Object.keys(projects).sort()).toEqual(['seed', ...keys].sort());
    expect(new Set(Object.values(projects).map((p) => p.alias)).size).toBe(keys.length + 1);
  });
});
