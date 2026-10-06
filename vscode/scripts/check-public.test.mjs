import { describe, expect, it, beforeEach, afterEach } from 'vitest';
import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { scanText, scanTree } from './check-public.mjs';

const script = path.join(path.dirname(fileURLToPath(import.meta.url)), 'check-public.mjs');
// Built at runtime so this file never holds a profile path itself.
const winPath = 'C:' + '\\Users\\' + 'someone' + '\\proj';
const winSlash = 'D:' + '/Users/' + 'someone' + '/proj';
const macPath = '/Users' + '/someone/proj';

let dir;
beforeEach(() => {
  dir = fs.mkdtempSync(path.join(os.tmpdir(), 'check-public-'));
});
afterEach(() => {
  fs.rmSync(dir, { recursive: true, force: true });
});

describe('scanText', () => {
  it('flags windows paths in both slash styles and macOS paths with the line number', () => {
    expect(scanText(`ok\n${winPath}`)).toEqual([{ line: 2, reason: 'windows profile path' }]);
    expect(scanText(winSlash)).toHaveLength(1);
    expect(scanText(macPath + '/')).toHaveLength(1);
  });

  it('allows the documentation placeholder and fixture paths', () => {
    expect(scanText('C:\\Users\\<name>\\x and /Users/<name>/x and C:\\fixture\\a')).toEqual([]);
  });

  it('matches denylist terms case-insensitively', () => {
    expect(scanText('Some SecretCorp thing', ['secretcorp'])).toHaveLength(1);
    expect(scanText('nothing here', ['secretcorp'])).toEqual([]);
  });
});

describe('scanTree and CLI', () => {
  it('reports a planted hit with file:line and exits 1', () => {
    fs.mkdirSync(path.join(dir, 'src'));
    fs.writeFileSync(path.join(dir, 'src', 'a.ts'), `x\n${winPath}\n`);
    expect(scanTree(dir)).toEqual([{ file: 'src/a.ts', line: 2, reason: 'windows profile path' }]);
    const r = spawnSync('node', [script, '--stdin'], { input: `x\n${winPath}\n`, encoding: 'utf8' });
    expect(r.status).toBe(1);
    expect(r.stderr).toContain('<stdin>:2');
  });

  it('passes a clean tree and skips excluded folders', () => {
    fs.writeFileSync(path.join(dir, 'a.ts'), 'const a = 1;\n');
    fs.mkdirSync(path.join(dir, 'node_modules'));
    fs.writeFileSync(path.join(dir, 'node_modules', 'x.js'), winPath);
    expect(scanTree(dir)).toEqual([]);
    const r = spawnSync('node', [script, '--stdin'], { input: 'clean\n', encoding: 'utf8' });
    expect(r.status).toBe(0);
  });

  it('reads the denylist from SANDUHR_TIME_DENYLIST and skips it when unset', () => {
    const list = path.join(dir, 'deny.txt');
    fs.writeFileSync(list, 'Acme\n\n');
    const env = { ...process.env, SANDUHR_TIME_DENYLIST: list };
    const hit = spawnSync('node', [script, '--stdin'], { input: 'ACME repo', encoding: 'utf8', env });
    expect(hit.status).toBe(1);
    const unset = { ...process.env };
    delete unset.SANDUHR_TIME_DENYLIST;
    const skipped = spawnSync('node', [script, '--stdin'], { input: 'ACME repo', encoding: 'utf8', env: unset });
    expect(skipped.status).toBe(0);
  });
});
