// Public-repo gate: fails when vscode/** carries a user-profile path or a denylisted term.
// Usage: node scripts/check-public.mjs [--stdin]
// Env:   SANDUHR_TIME_DENYLIST = path to a file with one term per line (case-insensitive), optional.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const SKIP_DIRS = new Set(['node_modules', 'dist', 'out', '.vscode-test', 'coverage', '.git']);
const WIN_PROFILE = /[A-Za-z]:[\\/]+Users[\\/]+\w/;
const MAC_PROFILE = /\/Users\/\w[^/\s]*\//;

export function loadDenylist(file) {
  if (!file) return [];
  return fs
    .readFileSync(file, 'utf8')
    .split(/\r?\n/)
    .map((t) => t.trim().toLowerCase())
    .filter(Boolean);
}

export function scanText(text, denylist = []) {
  const hits = [];
  text.split(/\r?\n/).forEach((line, i) => {
    const lower = line.toLowerCase();
    if (WIN_PROFILE.test(line)) hits.push({ line: i + 1, reason: 'windows profile path' });
    else if (MAC_PROFILE.test(line)) hits.push({ line: i + 1, reason: 'macOS profile path' });
    for (const term of denylist) {
      if (lower.includes(term)) hits.push({ line: i + 1, reason: 'denylisted term' });
    }
  });
  return hits;
}

export function* walk(dir) {
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    if (entry.isDirectory()) {
      if (!SKIP_DIRS.has(entry.name)) yield* walk(path.join(dir, entry.name));
    } else if (entry.isFile() && !entry.name.endsWith('.vsix') && entry.name !== '.build-brief.md') {
      yield path.join(dir, entry.name);
    }
  }
}

export function scanTree(root, denylist = []) {
  const results = [];
  for (const file of walk(root)) {
    let text;
    try {
      text = fs.readFileSync(file, 'utf8');
    } catch {
      continue;
    }
    if (text.includes('\u0000')) continue; // binary
    for (const hit of scanText(text, denylist)) {
      results.push({ file: path.relative(root, file).split(path.sep).join('/'), ...hit });
    }
  }
  return results;
}

function main() {
  const denylist = loadDenylist(process.env.SANDUHR_TIME_DENYLIST);
  let hits;
  if (process.argv.includes('--stdin')) {
    hits = scanText(fs.readFileSync(0, 'utf8'), denylist).map((h) => ({ file: '<stdin>', ...h }));
  } else {
    const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
    hits = scanTree(root, denylist);
  }
  for (const h of hits) console.error(`${h.file}:${h.line}: ${h.reason}`);
  if (hits.length) {
    console.error(`check-public: ${hits.length} hit(s)`);
    process.exit(1);
  }
  console.log('check-public: clean');
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) main();
