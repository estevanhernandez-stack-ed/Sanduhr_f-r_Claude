import * as fs from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import * as vscode from 'vscode';
import { setConfig, shown, statusBarItems, webviewPanels } from './test/vscodeStub';
import { activate, deactivate } from './extension';
import { readOffsets } from './store/state';

let dir: string;
beforeEach(() => {
  dir = path.join(fs.mkdtempSync(path.join(os.tmpdir(), 'sanduhr-ext-')), 'data');
  setConfig('sanduhrTime.dataDir', dir);
  (vscode.env as { remoteName: string | undefined }).remoteName = undefined;
  shown.length = 0;
  statusBarItems.length = 0;
  webviewPanels.length = 0;
});
afterEach(() => {
  deactivate();
  (vscode.env as { remoteName: string | undefined }).remoteName = undefined;
  fs.rmSync(path.dirname(dir), { recursive: true, force: true });
});

const subs = (): { subscriptions: { dispose(): unknown }[] } => ({ subscriptions: [] });

describe('extension', () => {
  it('activate returns the public API at version 1 and sets up the data dir', () => {
    const api = activate(subs());
    expect(api.version).toBe(1);
    expect(typeof api.getToday).toBe('function');
    expect(typeof api.getDays).toBe('function');
    expect(typeof api.onDayUpdated).toBe('function');
    expect(api.maskState()).toEqual({ readable: true, streamerMode: false, projects: {} });
    expect(fs.existsSync(path.join(dir, 'state', 'installed.json'))).toBe(true);
    expect(readOffsets(path.join(dir, 'state'))).toEqual({}); // no reader pass has run inside activate
  });

  it('registers the four commands', () => {
    activate(subs());
    expect((vscode.commands as unknown as { registered(): string[] }).registered().sort()).toEqual([
      'sanduhrTime.mergeNow',
      'sanduhrTime.openPanel',
      'sanduhrTime.revealData',
      'sanduhrTime.toggleStreamerMode',
    ]);
  });

  it('records heartbeats from the window while focused, flushed on deactivate', () => {
    activate(subs());
    const doc = {
      uri: { scheme: 'untitled', fsPath: 'Untitled-1', toString: () => 'untitled:Untitled-1' },
      languageId: 'plaintext',
      lineCount: 1,
      isDirty: true,
      version: 1,
    };
    (vscode.window as unknown as { fireActiveEditor(e: unknown): void }).fireActiveEditor({ document: doc });
    deactivate();
    const spool = path.join(dir, 'spool');
    expect(fs.readdirSync(spool).some((n) => n.endsWith('.jsonl'))).toBe(true);
  });

  it('toggleStreamerMode flips the store, and revealData opens the data dir', async () => {
    activate(subs());
    const run = (vscode.commands as unknown as { run(id: string): Promise<unknown> }).run;
    await run('sanduhrTime.toggleStreamerMode');
    expect(JSON.parse(fs.readFileSync(path.join(dir, 'aliases.json'), 'utf8')).streamerMode).toBe(true);
    await run('sanduhrTime.toggleStreamerMode');
    expect(JSON.parse(fs.readFileSync(path.join(dir, 'aliases.json'), 'utf8')).streamerMode).toBe(false);
    await run('sanduhrTime.revealData');
    expect((vscode.env as unknown as { opened: { fsPath: string }[] }).opened.at(-1)?.fsPath).toBe(path.resolve(dir));
    await run('sanduhrTime.openPanel');
    expect(shown.at(-1)).toMatch(/location/); // no extensionUri in the unit context
  });

  it('openPanel opens the Time panel once and a status bar item is created', async () => {
    activate({ subscriptions: [], extensionUri: vscode.Uri.file('/fixture/ext') });
    await (vscode.commands as unknown as { run(id: string): Promise<unknown> }).run('sanduhrTime.openPanel');
    await (vscode.commands as unknown as { run(id: string): Promise<unknown> }).run('sanduhrTime.openPanel');
    expect(webviewPanels).toHaveLength(1);
    expect(webviewPanels[0].title).toBe('Sanduhr Time');
    expect(statusBarItems).toHaveLength(1);
    expect(statusBarItems[0].command).toBe('sanduhrTime.openPanel');
  });

  it('under a remote window it records nothing and returns the API with empty data', async () => {
    (vscode.env as { remoteName: string | undefined }).remoteName = 'ssh-remote';
    const api = activate(subs());
    expect(api.version).toBe(1);
    expect(await api.getToday()).toBeUndefined();
    expect(await api.getDays('2026-01-01', '2026-12-31')).toEqual([]);
    expect(fs.existsSync(dir)).toBe(false); // no data dir, no spool, no recorder
    expect(api.maskState().readable).toBe(false);
  });

  it('deactivate is safe to call without activate, and twice', () => {
    expect(deactivate()).toBeUndefined();
    activate(subs());
    deactivate();
    expect(deactivate()).toBeUndefined();
  });
});
