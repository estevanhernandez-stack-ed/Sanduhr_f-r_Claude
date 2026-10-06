// Dev script, not shipped and not run by the tests: renders the Time panel in an isolated
// VS Code (from @vscode/test-electron) against a synthetic data dir and saves a screenshot of
// that window only.
//
// Usage: node scripts/panel-screenshot.mjs --theme "Default Dark Modern" --out <file.png>
//        [--cache <dir with a downloaded VS Code>] [--version 1.140.0]
//
// Isolation: fresh --user-data-dir and --extensions-dir under the OS temp dir, a throwaway
// home (so the Claude transcript reader finds nothing), VSCODE_* and ELECTRON_RUN_AS_NODE
// stripped from the environment. Run `npm run compile` first.
import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { runTests } from '@vscode/test-electron';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const args = Object.fromEntries(
  process.argv.slice(2).reduce((acc, a, i, all) => (a.startsWith('--') ? [...acc, [a.slice(2), all[i + 1]]] : acc), []),
);
const theme = args.theme ?? 'Default Dark Modern';
const out = args.out;
if (!out) throw new Error('--out is required');
const cachePath = args.cache ?? path.join(os.tmpdir(), 'sanduhr-time-vscode-test');
const version = args.version ?? '1.140.0';

const tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'sanduhr-shot-'));
const marker = path.basename(tmp);
const dirs = Object.fromEntries(['data', 'user', 'ext', 'home', 'work', 'sig'].map((d) => [d, path.join(tmp, d)]));
for (const d of Object.values(dirs)) fs.mkdirSync(d, { recursive: true });

// ---- synthetic data (invented names only) ----
const KEYS = { demo: 'a1b2c3d4e5f60718', widget: '1122334455667788', notes: 'ffeeddccbbaa9988' };
const pad = (n) => String(n).padStart(2, '0');
const dateOf = (d) => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
const at = (base, h, m) => new Date(base.getFullYear(), base.getMonth(), base.getDate(), h, m).getTime() / 1000;
const now = new Date();
const dayAt = (back) => new Date(now.getFullYear(), now.getMonth(), now.getDate() - back);

const machine = os.hostname().toLowerCase().replace(/[^a-z0-9-]/g, '-');
const hex = (s, n) => (s + '0'.repeat(n)).slice(0, n);
const ent = (name) => hex(Buffer.from(name).toString('hex'), 24);

function seedDay(base, scale) {
  const beats = [];
  const events = [];
  const beat = (t, project, entity, language) =>
    beats.push({ v: 1, time: t, entity, type: 'file', category: 'coding', kind: 'edit', project, branch: 'main', language, lines: 120, human_line_changes: 3, is_write: true, reload: false });
  let n = 0;
  const ev = (t, project, stream, kind, extra = {}) => events.push({ v: 1, id: `${dateOf(base)}-${n++}`, time: t, project, stream, kind, ...extra });
  for (let m = 0; m <= 100 * scale; m += 3) beat(at(base, 9, 0) + m * 60, KEYS.demo, ent('demo-a'), m % 15 === 0 ? 'css' : 'typescript');
  for (let m = 0; m <= 40 * scale; m += 4) beat(at(base, 13, 0) + m * 60, KEYS.widget, ent('widget-a'), 'python');
  for (let m = 0; m <= 20; m += 5) beat(at(base, 15, 0) + m * 60, KEYS.notes, ent('notes-a'), 'markdown');
  ev(at(base, 9, 50), KEYS.demo, 's-demo', 'prompt');
  ev(at(base, 10, 20), KEYS.demo, 's-demo', 'prompt');
  for (let m = 0; m <= 125 * scale; m += 2) ev(at(base, 10, 0) + m * 60, KEYS.demo, 's-demo', 'activity');
  for (let m = 0; m <= 120 * scale; m += 10) ev(at(base, 10, 0) + m * 60 + 5, KEYS.demo, 's-demo', 'edit', { linesAdded: 24, linesRemoved: 3, entity: ent('demo-claude') });
  for (let m = 0; m <= 30; m += 2) ev(at(base, 13, 20) + m * 60, KEYS.widget, 's-widget', 'activity');
  ev(at(base, 13, 25), KEYS.widget, 's-widget', 'edit', { linesAdded: 40, linesRemoved: 0, entity: ent('widget-claude') });
  return { beats, events };
}

fs.mkdirSync(path.join(dirs.data, 'spool'), { recursive: true });
fs.mkdirSync(path.join(dirs.data, 'claude'), { recursive: true });
fs.mkdirSync(path.join(dirs.data, 'days'), { recursive: true });
fs.mkdirSync(path.join(dirs.data, 'state'), { recursive: true });
for (const [back, scale] of [[0, 1], [1, 0.7]]) {
  const base = dayAt(back);
  const { beats, events } = seedDay(base, scale);
  fs.writeFileSync(path.join(dirs.data, 'spool', `${dateOf(base)}.${machine}.0a1b2c3d.jsonl`), beats.map((b) => JSON.stringify(b)).join('\n') + '\n');
  fs.writeFileSync(path.join(dirs.data, 'claude', `${dateOf(base)}.jsonl`), events.map((e) => JSON.stringify(e)).join('\n') + '\n');
}
// Older days are not re-merged, so write their records directly.
const MIN = 60000;
[[2, 150, 90, 20], [3, 60, 0, 0], [4, 0, 0, 0], [5, 200, 140, 40], [6, 95, 45, 10]].forEach(([back, you, claude, both]) => {
  const d = dayAt(back);
  const totals = { youMs: you * MIN, claudeMs: claude * MIN, bothMs: both * MIN, totalMs: (you + claude + both) * MIN, agentMs: claude * MIN, linesYou: 0, linesClaude: 0 };
  fs.writeFileSync(
    path.join(dirs.data, 'days', `${dateOf(d)}.json`),
    JSON.stringify({ v: 1, date: dateOf(d), machine, generatedAt: d.toISOString(), totals, projects: [], caveats: { unreadableTranscriptLines: 0, readerVersion: '0.1.0', claudeCodeVersions: [] } }),
  );
});
fs.writeFileSync(path.join(dirs.data, 'state', 'installed.json'), JSON.stringify({ installedAt: new Date().toISOString(), salt: 'ab'.repeat(16) }));
const entry = (realName, alias, masked) => ({ realName, alias, masked, setAt: new Date().toISOString() });
fs.writeFileSync(
  path.join(dirs.data, 'aliases.json'),
  JSON.stringify({ v: 1, streamerMode: false, projects: { [KEYS.demo]: entry('Demo App', 'Project Aster', false), [KEYS.widget]: entry('Widget Lab', 'Project Birch', true), [KEYS.notes]: entry('Notes Garden', 'Project Cedar', false) } }),
);

// ---- test profile ----
fs.mkdirSync(path.join(dirs.user, 'User'), { recursive: true });
fs.writeFileSync(
  path.join(dirs.user, 'User', 'settings.json'),
  JSON.stringify({
    'sanduhrTime.dataDir': dirs.data,
    'workbench.colorTheme': theme,
    'workbench.startupEditor': 'none',
    'workbench.tips.enabled': false,
    'telemetry.telemetryLevel': 'off',
    'update.mode': 'none',
    'extensions.autoUpdate': false,
    'security.workspace.trust.enabled': false,
    'files.autoSave': 'off',
    'git.enabled': false,
    'chat.disableAIFeatures': true,
    'window.commandCenter': false,
  }),
);

const suite = path.join(root, 'scripts', 'panel-screenshot-suite.cjs');
const env = { ...process.env, USERPROFILE: dirs.home, HOME: dirs.home, HOMEDRIVE: path.parse(dirs.home).root.replace(/[\\/]$/, ''), HOMEPATH: dirs.home.slice(2), SANDUHR_SIGNAL_DIR: dirs.sig };
for (const k of Object.keys(env)) if (k.startsWith('VSCODE_') || k === 'ELECTRON_RUN_AS_NODE' || k === 'CLAUDE_CONFIG_DIR') delete env[k];
Object.assign(process.env, env);
for (const k of Object.keys(process.env)) if (k.startsWith('VSCODE_') || k === 'ELECTRON_RUN_AS_NODE' || k === 'CLAUDE_CONFIG_DIR') delete process.env[k];

const running = runTests({
  version,
  cachePath,
  extensionDevelopmentPath: root,
  extensionTestsPath: suite,
  launchArgs: [dirs.work, `--user-data-dir=${dirs.user}`, `--extensions-dir=${dirs.ext}`, '--disable-workspace-trust', '--new-window', '--skip-welcome', '--skip-release-notes'],
}).then(
  () => 0,
  (err) => {
    console.error('test host failed:', err?.message ?? err);
    return 1;
  },
);

// Wait for the suite to say the panel is rendered, capture that window, then release it.
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const ready = path.join(dirs.sig, 'ready');
const deadline = Date.now() + 120_000;
while (!fs.existsSync(ready) && Date.now() < deadline) await sleep(500);
let code = 1;
if (fs.existsSync(ready)) {
  fs.mkdirSync(path.dirname(out), { recursive: true });
  const r = spawnSync('powershell', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', path.join(root, 'scripts', 'capture-window.ps1'), '-Marker', dirs.user, '-Out', out], { encoding: 'utf8' });
  process.stdout.write(r.stdout ?? '');
  process.stderr.write(r.stderr ?? '');
  code = r.status ?? 1;
} else {
  console.error('the panel never reported ready');
}
fs.writeFileSync(path.join(dirs.sig, 'done'), '1');
const hostCode = await running;
fs.rmSync(tmp, { recursive: true, force: true });
console.log(`theme=${theme} capture=${code === 0 ? 'ok' : 'failed'} host=${hostCode} marker=${marker}`);
process.exit(code || hostCode);
