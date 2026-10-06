/**
 * Project resolver. Maps a folder or file path (editor) or a transcript `cwd` to a
 * stable project key, a display name, a root and a branch, reading git's own files
 * directly. No child process is ever spawned.
 */
import { createHash } from 'node:crypto';
import * as fs from 'node:fs';
import * as path from 'node:path';

export interface ResolvedProject {
  /** 16 hex chars: sha256 of the normalized remote, or of `path:<case-folded root>`. */
  key: string;
  /** Repo basename from the remote (case preserved, no `.git`), or the folder name. */
  realName: string;
  root: string;
  /** Current branch of this checkout, or null when detached or unknown. */
  branch: string | null;
}

const sha16 = (s: string): string => createHash('sha256').update(s).digest('hex').slice(0, 16);

const trimEnd = (s: string): string => s.replace(/\/+$/, '').replace(/\.git$/i, '').replace(/\/+$/, '');

/**
 * Canonical form of a git remote URL. Strips credentials, query and fragment, a trailing
 * `.git` and slashes; converts `git@host:owner/repo` and `ssh://git@host[:port]/owner/repo`
 * to `https://host/owner/repo`; bumps `http://` to `https://`; lowercases the whole URL.
 * Matches the 626 server's `normalizeRepoUrl` for GitHub URLs, so the publisher binds
 * by the identical key.
 */
export function normalizeRemote(url: string): string {
  let s = (url ?? '').trim().replace(/[?#].*$/, '');
  if (!s) return '';

  const scheme = /^([a-z][a-z0-9+.-]*):\/\/(.*)$/i.exec(s);
  if (scheme) {
    const proto = scheme[1].toLowerCase();
    const rest = scheme[2];
    const slash = rest.indexOf('/');
    const authority = slash === -1 ? rest : rest.slice(0, slash);
    const tail = slash === -1 ? '' : rest.slice(slash);
    let host = authority.slice(authority.lastIndexOf('@') + 1);
    if (proto === 'ssh' || proto === 'git' || proto === 'ssh+git') {
      host = host.replace(/:\d+$/, '');
      s = `https://${host}${tail}`;
    } else if (proto === 'http' || proto === 'https') {
      s = `https://${host}${tail}`;
    } else {
      s = `${proto}://${host}${tail}`;
    }
  } else {
    // scp-like `user@host:owner/repo`. A one-letter host is a Windows drive, not scp.
    const scp = /^(?:[^@/:\\]+@)?([^:/\\]{2,}):(?!\/\/)(.+)$/.exec(s);
    if (scp) s = `https://${scp[1]}/${scp[2].replace(/^\/+/, '')}`;
  }

  s = s.replace(/^https:\/\/www\.github\.com\//i, 'https://github.com/');
  return trimEnd(s).toLowerCase();
}

/**
 * Project key: first 16 hex chars of sha256 of the normalized remote, or, for a root
 * path with no remote, of `path:<case-folded root>`. Pass a remote URL or a root path;
 * a value that looks like a URL or scp remote is treated as a remote.
 */
export function projectKey(remoteOrRoot: string): string {
  if (isRemote(remoteOrRoot)) return sha16(normalizeRemote(remoteOrRoot));
  return sha16('path:' + foldRoot(remoteOrRoot));
}

function isRemote(s: string): boolean {
  return /^[a-z][a-z0-9+.-]*:\/\//i.test(s) || /^(?:[^@/:\\]+@)?[^:/\\]{2,}:(?!\/\/)(?!\\).+$/.test(s);
}

function foldRoot(root: string): string {
  return root.split('\\').join('/').replace(/\/+$/, '').toLowerCase();
}

/** The nearest ancestor (or the path itself) containing `.git`, as a file or a directory. */
export function findRoot(fsPath: string): string | undefined {
  let dir = path.resolve(fsPath);
  for (;;) {
    if (fs.existsSync(path.join(dir, '.git'))) return dir;
    const parent = path.dirname(dir);
    if (parent === dir) return undefined;
    dir = parent;
  }
}

interface GitDirs {
  /** This checkout's git dir (the worktree's own dir for a worktree). */
  gitDir: string;
  /** The shared git dir holding `config`. */
  commonDir: string;
}

function readTrim(file: string): string | undefined {
  try {
    return fs.readFileSync(file, 'utf8').trim();
  } catch {
    return undefined;
  }
}

function gitDirs(root: string): GitDirs | undefined {
  const dotGit = path.join(root, '.git');
  let stat: fs.Stats;
  try {
    stat = fs.statSync(dotGit);
  } catch {
    return undefined;
  }
  if (stat.isDirectory()) return { gitDir: dotGit, commonDir: dotGit };
  const body = readTrim(dotGit);
  const m = body && /^gitdir:\s*(.+)$/m.exec(body);
  if (!m) return undefined;
  const gitDir = path.resolve(root, m[1].trim());
  const common = readTrim(path.join(gitDir, 'commondir'));
  return { gitDir, commonDir: common ? path.resolve(gitDir, common) : gitDir };
}

/** `[remote "origin"] url` from a git config file's text, or undefined. */
export function parseOriginUrl(config: string): string | undefined {
  let inOrigin = false;
  for (const raw of config.split(/\r?\n/)) {
    const line = raw.trim();
    if (line.startsWith('[')) {
      inOrigin = /^\[\s*remote\s+"origin"\s*\]/i.test(line);
      continue;
    }
    if (!inOrigin) continue;
    const kv = /^url\s*=\s*(.*)$/i.exec(line);
    if (kv) return kv[1].trim().replace(/^"(.*)"$/, '$1');
  }
  return undefined;
}

function readBranch(gitDir: string): string | null {
  const head = readTrim(path.join(gitDir, 'HEAD'));
  const m = head && /^ref:\s*refs\/heads\/(.+)$/.exec(head);
  return m ? m[1].trim() : null;
}

function nameFromRemote(url: string): string {
  const cleaned = url.trim().replace(/[?#].*$/, '');
  const base = trimEnd(cleaned).split(/[/:\\]/).pop();
  return base ?? '';
}

interface CachedRoot {
  key: string;
  realName: string;
}

const cache = new Map<string, CachedRoot>();

/** Drop the per-root cache (tests, or after a settings change). */
export function clearProjectCache(): void {
  cache.clear();
}

/** Real names of every project resolved so far, by key (the runner's name source for the merge). */
export function knownProjectNames(): Map<string, string> {
  const out = new Map<string, string>();
  for (const e of cache.values()) out.set(e.key, e.realName);
  return out;
}

/**
 * Resolve a file or folder to its project. Root: the nearest `.git` ancestor, else the
 * folder itself. Key and name are cached per root; the branch is read fresh each call
 * because it changes without the root changing.
 */
export function resolve(fsPath: string): ResolvedProject {
  const abs = path.resolve(fsPath);
  let root = findRoot(abs);
  if (!root) {
    let isFile = false;
    try {
      isFile = fs.statSync(abs).isFile();
    } catch {
      /* nonexistent: treat as a folder */
    }
    root = isFile ? path.dirname(abs) : abs;
  }

  const dirs = gitDirs(root);
  let entry = cache.get(root);
  if (!entry) {
    const config = dirs ? readTrim(path.join(dirs.commonDir, 'config')) : undefined;
    const remote = config ? parseOriginUrl(config) : undefined;
    const name = remote ? nameFromRemote(remote) : '';
    entry = remote
      ? { key: sha16(normalizeRemote(remote)), realName: name || path.basename(root) }
      : { key: sha16('path:' + foldRoot(root)), realName: path.basename(root) || root };
    cache.set(root, entry);
  }
  return { key: entry.key, realName: entry.realName, root, branch: dirs ? readBranch(dirs.gitDir) : null };
}
