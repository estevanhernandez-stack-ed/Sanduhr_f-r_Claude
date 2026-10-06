import * as path from 'node:path';
import * as vscode from 'vscode';
import { createApi, createEmptyApi, type SanduhrTimeApi } from './api';
import { knownProjectNames, resolve as resolveProject } from './project';
import { Recorder, type RecorderHost } from './recorder';
import { Runner } from './runner';
import { AliasStore } from './store/aliases';
import { dataDir, stateDir } from './store/paths';
import { Spool, machineLabel, windowIdFor } from './store/spool';
import { ensureInstalled } from './store/state';
import type { DayRecord } from './types';

export type { SanduhrTimeApi } from './api';

const READER_VERSION = '0.1.0';

interface Live {
  recorder: Recorder;
  spool: Spool;
  runner: Runner;
  emitter: vscode.EventEmitter<DayRecord>;
}
let live: Live | undefined;

interface Context {
  subscriptions: { dispose(): unknown }[];
}

/** The recorder's event sources, backed by the real `vscode` API. */
function vscodeHost(): RecorderHost {
  return {
    onDidChangeTextDocument: (l) => vscode.workspace.onDidChangeTextDocument((e) => l(e)),
    onDidSaveTextDocument: (l) => vscode.workspace.onDidSaveTextDocument((d) => l(d)),
    onDidChangeActiveTextEditor: (l) => vscode.window.onDidChangeActiveTextEditor((e) => l(e)),
    onDidChangeTextEditorSelection: (l) => vscode.window.onDidChangeTextEditorSelection((e) => l(e)),
    onDidChangeWindowState: (l) => vscode.window.onDidChangeWindowState((s) => l(s)),
    isFocused: () => vscode.window.state.focused,
  };
}

interface CommandDeps {
  dir: string;
  store: AliasStore;
  runner: Runner;
}

function registerCommands(deps: CommandDeps | undefined, context?: Context): void {
  const reg = (id: string, fn: () => Promise<void> | void): void => {
    const d = vscode.commands.registerCommand(id, async () => {
      try {
        await fn();
      } catch (err) {
        void vscode.window.showErrorMessage(`Sanduhr Time: ${err instanceof Error ? err.message : String(err)}`);
      }
    });
    context?.subscriptions.push(d);
  };
  const localOnly = (): void => {
    void vscode.window.showInformationMessage('Sanduhr Time: local workspaces only.');
  };

  reg('sanduhrTime.openPanel', () => {
    void vscode.window.showInformationMessage('Sanduhr Time: the panel arrives in the next update.');
  });
  reg('sanduhrTime.mergeNow', async () => {
    if (!deps) return localOnly();
    const r = await deps.runner.mergeNow();
    void vscode.window.showInformationMessage(
      r.ran
        ? `Sanduhr Time: merged ${r.merged.length} day(s).`
        : 'Sanduhr Time: another merge is running; try again shortly.',
    );
  });
  reg('sanduhrTime.revealData', async () => {
    if (!deps) return localOnly();
    await vscode.env.openExternal(vscode.Uri.file(path.resolve(deps.dir)));
  });
  reg('sanduhrTime.toggleStreamerMode', async () => {
    if (!deps) return localOnly();
    const next = !deps.store.maskState().streamerMode;
    await deps.store.setStreamerMode(next);
    void vscode.window.showInformationMessage(`Sanduhr Time: streamer mode ${next ? 'on' : 'off'}.`);
  });
}

export function activate(context?: Context): SanduhrTimeApi {
  // Local windows only: under a remote window the extension records nothing.
  if (vscode.env.remoteName) {
    registerCommands(undefined, context);
    return createEmptyApi();
  }

  const config = vscode.workspace.getConfiguration('sanduhrTime');
  const dir = dataDir(config.get<string>('dataDir'));
  const { installed, isNew } = ensureInstalled(stateDir(dir));
  const store = new AliasStore(dir, { isNew });
  const machine = machineLabel();

  const spool = new Spool(dir, { machine, windowId: windowIdFor(vscode.env.sessionId) });

  // The first time this window sees a project, record its real name in the alias store so
  // every window's merge can name it. Failures are not fatal: the merge falls back to the resolver.
  const registered = new Set<string>();
  const resolve = (fsPath: string): ReturnType<typeof resolveProject> => {
    const r = resolveProject(fsPath);
    if (!registered.has(r.key)) {
      registered.add(r.key);
      store.aliasFor(r.key, r.realName).catch(() => registered.delete(r.key));
    }
    return r;
  };
  const recorder = new Recorder({ host: vscodeHost(), spool, resolve, now: Date.now, salt: installed.salt });

  const emitter = new vscode.EventEmitter<DayRecord>();
  const runner = new Runner({
    dir,
    installed,
    machine,
    store,
    now: Date.now,
    idleMinutes: () => vscode.workspace.getConfiguration('sanduhrTime').get<number>('idleMinutes') ?? 15,
    knownNames: knownProjectNames,
    readerVersion: READER_VERSION,
    onDayUpdated: (d) => emitter.fire(d),
    onError: (err) => console.error('[sanduhr-time]', err instanceof Error ? err.message : err),
  });
  runner.start();

  live = { recorder, spool, runner, emitter };
  registerCommands({ dir, store, runner }, context);
  return createApi({ dir, store, onDayUpdated: emitter.event });
}

export function deactivate(): void {
  const l = live;
  live = undefined;
  if (!l) return;
  // Flush this window's buffered heartbeats first; no reader pass, no merge.
  l.recorder.dispose();
  l.spool.dispose();
  l.runner.dispose();
  l.emitter.dispose();
}
