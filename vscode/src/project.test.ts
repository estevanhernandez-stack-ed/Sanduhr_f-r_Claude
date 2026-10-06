import { createHash } from 'node:crypto';
import * as fs from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { clearProjectCache, findRoot, normalizeRemote, parseOriginUrl, projectKey, resolve } from './project';

const sha16 = (s: string) => createHash('sha256').update(s).digest('hex').slice(0, 16);
const CANON = 'https://github.com/example/demo';

describe('normalizeRemote', () => {
  it.each([
    ['https://github.com/example/demo.git', CANON],
    ['https://github.com/example/demo', CANON],
    ['https://github.com/example/demo/', CANON],
    ['https://github.com/example/demo.git/', CANON],
    ['git@github.com:example/demo.git', CANON],
    ['git@github.com:example/demo', CANON],
    ['ssh://git@github.com/example/demo.git', CANON],
    ['ssh://git@github.com:2222/example/demo.git', CANON],
    ['http://github.com/example/demo', CANON],
    ['https://www.github.com/example/demo', CANON],
    ['HTTPS://GitHub.com/Example/Demo.GIT', CANON],
    ['https://user:fake-token@github.com/example/demo.git', CANON],
    ['https://fake-token@github.com/example/demo', CANON],
    ['  https://github.com/example/demo.git\n', CANON],
    ['https://github.com/example/demo?ref=main#readme', CANON],
  ])('%s', (input, expected) => {
    expect(normalizeRemote(input)).toBe(expected);
  });

  it('converts scp-like remotes on other hosts', () => {
    expect(normalizeRemote('git@git.example.org:team/tool.git')).toBe('https://git.example.org/team/tool');
    expect(normalizeRemote('ssh://git@git.example.org/team/tool')).toBe('https://git.example.org/team/tool');
  });

  it('keeps a port on https remotes', () => {
    expect(normalizeRemote('https://git.example.org:8443/team/tool.git')).toBe('https://git.example.org:8443/team/tool');
  });

  it('returns empty for empty input', () => {
    expect(normalizeRemote('')).toBe('');
    expect(normalizeRemote('   ')).toBe('');
  });

  it('SSH, HTTPS and credentialed forms all give the same project key', () => {
    const keys = [
      'git@github.com:example/demo.git',
      'https://github.com/example/demo.git',
      'https://user:fake-token@github.com/example/demo',
    ].map(projectKey);
    expect(new Set(keys).size).toBe(1);
    expect(keys[0]).toBe(sha16(CANON));
  });
});

describe('projectKey', () => {
  it('is 16 hex chars', () => {
    expect(projectKey('https://github.com/example/demo')).toMatch(/^[0-9a-f]{16}$/);
  });
  it('hashes a root path with the path: prefix, case-folded', () => {
    expect(projectKey('C:\\fixture\\Demo')).toBe(sha16('path:c:/fixture/demo'));
    expect(projectKey('/fixture/Demo')).toBe(projectKey('/fixture/demo'));
    expect(projectKey('C:\\fixture\\Demo')).toBe(projectKey('c:/fixture/demo/'));
  });
  it('does not mistake a Windows drive path for a remote', () => {
    expect(projectKey('C:/fixture/demo')).toBe(sha16('path:c:/fixture/demo'));
  });
});

describe('parseOriginUrl', () => {
  it('reads the origin url and ignores other remotes and sections', () => {
    const cfg = [
      '[core]',
      '\turl = nope',
      '[remote "upstream"]',
      '\turl = https://github.com/example/other.git',
      '[remote "origin"]',
      '\tfetch = +refs/heads/*:refs/remotes/origin/*',
      '\turl = https://github.com/example/demo.git',
      '[branch "main"]',
      '\turl = nope',
    ].join('\r\n');
    expect(parseOriginUrl(cfg)).toBe('https://github.com/example/demo.git');
  });
  it('returns undefined with no origin', () => {
    expect(parseOriginUrl('[core]\n\tbare = false\n')).toBeUndefined();
  });
});

describe('resolver on fake repos', () => {
  let tmp: string;
  const w = (p: string, body = '') => {
    fs.mkdirSync(path.dirname(p), { recursive: true });
    fs.writeFileSync(p, body);
  };
  const config = (url?: string) =>
    '[core]\n\tbare = false\n' + (url ? `[remote "origin"]\n\turl = ${url}\n` : '');

  /** A plain repo at <tmp>/<name> with an optional origin and a branch. */
  const plainRepo = (name: string, url?: string, head = 'ref: refs/heads/main\n') => {
    const root = path.join(tmp, name);
    w(path.join(root, '.git', 'config'), config(url));
    w(path.join(root, '.git', 'HEAD'), head);
    return root;
  };

  beforeEach(() => {
    tmp = fs.realpathSync(fs.mkdtempSync(path.join(os.tmpdir(), 'sanduhr-proj-')));
    clearProjectCache();
  });
  afterEach(() => {
    fs.rmSync(tmp, { recursive: true, force: true });
  });

  it('resolves a plain repo with an HTTPS remote', () => {
    const root = plainRepo('checkout', 'https://github.com/example/Demo.git');
    expect(resolve(root)).toEqual({
      key: sha16(CANON),
      realName: 'Demo',
      root,
      branch: 'main',
    });
  });

  it('resolves an SSH remote to the same key as HTTPS', () => {
    const a = plainRepo('a', 'git@github.com:example/demo.git');
    const b = plainRepo('b', 'https://github.com/example/demo.git');
    expect(resolve(a).key).toBe(resolve(b).key);
    expect(resolve(a).realName).toBe('demo');
  });

  it('strips credentials from the remote before keying', () => {
    const root = plainRepo('c', 'https://user:fake-token@github.com/example/demo.git');
    expect(resolve(root).key).toBe(sha16(CANON));
  });

  it('falls back to the folder name and a path key with no remote', () => {
    const root = plainRepo('No-Remote');
    const r = resolve(root);
    expect(r.realName).toBe('No-Remote');
    expect(r.key).toBe(sha16('path:' + root.split('\\').join('/').toLowerCase()));
  });

  it('resolves a nested folder and a file inside it to the repo root', () => {
    const root = plainRepo('nest', 'https://github.com/example/demo.git');
    const deep = path.join(root, 'src', 'deep');
    fs.mkdirSync(deep, { recursive: true });
    const file = path.join(deep, 'a.ts');
    fs.writeFileSync(file, '');
    expect(findRoot(deep)).toBe(root);
    expect(resolve(deep).root).toBe(root);
    expect(resolve(file).root).toBe(root);
    expect(resolve(file).key).toBe(sha16(CANON));
  });

  it('uses the folder itself as root when there is no git ancestor', () => {
    const dir = path.join(tmp, 'scratch');
    fs.mkdirSync(dir);
    const r = resolve(dir);
    expect(r.root).toBe(dir);
    expect(r.branch).toBeNull();
    expect(r.realName).toBe('scratch');
    const file = path.join(dir, 'x.txt');
    fs.writeFileSync(file, '');
    expect(resolve(file).root).toBe(dir);
  });

  it('folds a worktree into the main repo through gitdir and commondir', () => {
    const main = plainRepo('main-repo', 'git@github.com:example/demo.git');
    const wtGit = path.join(main, '.git', 'worktrees', 'feature');
    w(path.join(wtGit, 'HEAD'), 'ref: refs/heads/feat/thing\n');
    w(path.join(wtGit, 'commondir'), '../..\n');
    const wtRoot = path.join(tmp, 'wt-feature');
    w(path.join(wtRoot, '.git'), `gitdir: ${wtGit}\n`);

    const r = resolve(wtRoot);
    expect(r.key).toBe(resolve(main).key);
    expect(r.realName).toBe('demo');
    expect(r.root).toBe(wtRoot);
    expect(r.branch).toBe('feat/thing');
    expect(resolve(main).branch).toBe('main');
  });

  it('accepts a relative gitdir in a worktree .git file', () => {
    const main = plainRepo('rel-main', 'https://github.com/example/demo.git');
    const wtGit = path.join(main, '.git', 'worktrees', 'rel');
    w(path.join(wtGit, 'HEAD'), 'ref: refs/heads/rel\n');
    w(path.join(wtGit, 'commondir'), '../..');
    const wtRoot = path.join(main, 'inside-wt');
    w(path.join(wtRoot, '.git'), 'gitdir: ../.git/worktrees/rel\n');
    expect(resolve(wtRoot).key).toBe(sha16(CANON));
    expect(resolve(wtRoot).branch).toBe('rel');
  });

  it('reports a detached HEAD as null branch', () => {
    const root = plainRepo('detached', 'https://github.com/example/demo.git', '0123456789abcdef0123456789abcdef01234567\n');
    expect(resolve(root).branch).toBeNull();
  });

  it('caches key and name per root but reads the branch fresh', () => {
    const root = plainRepo('cached', 'https://github.com/example/demo.git');
    expect(resolve(root).branch).toBe('main');
    fs.writeFileSync(path.join(root, '.git', 'config'), config('https://github.com/example/changed.git'));
    fs.writeFileSync(path.join(root, '.git', 'HEAD'), 'ref: refs/heads/other\n');
    const r = resolve(root);
    expect(r.key).toBe(sha16(CANON));
    expect(r.branch).toBe('other');
    clearProjectCache();
    expect(resolve(root).realName).toBe('changed');
  });
});
