import { randomBytes } from 'node:crypto';
import * as vscode from 'vscode';
import type { SanduhrTimeApi } from '../api';
import { localDate } from '../store/claudeSpool';
import type { DayRecord } from '../types';
import { buildPanelModel, type CompareInput, type PanelModel } from './viewModel';

export const PANEL_TYPE = 'sanduhrTime.panel';
export const PANEL_TITLE = 'Sanduhr Time';

/** The strict policy: nothing loads except our own css, our nonce'd script and images from the extension. */
export function buildCsp(cspSource: string, nonce: string): string {
  return [
    "default-src 'none'",
    `style-src ${cspSource}`,
    `script-src 'nonce-${nonce}'`,
    `img-src ${cspSource}`,
  ].join('; ');
}

export function buildHtml(csp: string, nonce: string, cssUri: string, jsUri: string): string {
  return `<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta http-equiv="Content-Security-Policy" content="${csp}">
<meta name="viewport" content="width=device-width, initial-scale=1">
<link rel="stylesheet" href="${cssUri}">
<title>${PANEL_TITLE}</title>
</head>
<body>
<div id="app" aria-live="polite"></div>
<script nonce="${nonce}" src="${jsUri}"></script>
</body>
</html>`;
}

/** Messages the webview may post. Anything else is ignored. */
export type PanelMessage =
  | { type: 'ready' }
  | { type: 'refresh' }
  | { type: 'toggleStreamer' }
  | { type: 'mask'; key: string; masked: boolean }
  | { type: 'reroll'; key: string };

/** The writes the panel needs. Absent in a remote window, where nothing is recorded. */
export interface PanelController {
  setStreamerMode(on: boolean): Promise<void>;
  setMasked(key: string, masked: boolean, realName?: string): Promise<void>;
  reroll(key: string, realName?: string): Promise<string>;
  mergeNow(): Promise<unknown>;
}

export interface PanelDeps {
  api: Pick<SanduhrTimeApi, 'getDays' | 'maskState' | 'onDayUpdated'>;
  controller: PanelController | undefined;
  extensionUri: vscode.Uri | undefined;
  remote: boolean;
  now?: () => number;
  /** WakaTime comparison input, supplied by the comparison feature when it is on. */
  compare?: () => Promise<CompareInput | undefined>;
}

function isMessage(m: unknown): m is PanelMessage {
  if (!m || typeof m !== 'object') return false;
  const o = m as Record<string, unknown>;
  switch (o.type) {
    case 'ready':
    case 'refresh':
    case 'toggleStreamer':
      return true;
    case 'mask':
      return typeof o.key === 'string' && typeof o.masked === 'boolean';
    case 'reroll':
      return typeof o.key === 'string';
    default:
      return false;
  }
}

export class TimePanel {
  private panel: vscode.WebviewPanel | undefined;
  private updateSub: vscode.Disposable | undefined;
  /** Real names by project key, kept in the extension host only; never sent to the webview. */
  private realNames = new Map<string, string>();

  constructor(private readonly deps: PanelDeps) {}

  /** Open the panel, or reveal it if already open. */
  open(): void {
    if (this.panel) {
      this.panel.reveal(vscode.ViewColumn.Active);
      void this.post();
      return;
    }
    const extUri = this.deps.extensionUri;
    if (!extUri) throw new Error('the extension location is unknown');
    const media = vscode.Uri.joinPath(extUri, 'media');
    const panel = vscode.window.createWebviewPanel(PANEL_TYPE, PANEL_TITLE, vscode.ViewColumn.Active, {
      enableScripts: true,
      retainContextWhenHidden: false,
      localResourceRoots: [media],
    });
    this.panel = panel;
    const nonce = randomBytes(16).toString('base64');
    const web = panel.webview;
    web.html = buildHtml(
      buildCsp(web.cspSource, nonce),
      nonce,
      web.asWebviewUri(vscode.Uri.joinPath(media, 'panel.css')).toString(),
      web.asWebviewUri(vscode.Uri.joinPath(media, 'panel.js')).toString(),
    );
    web.onDidReceiveMessage((m: unknown) => this.handle(m));
    this.updateSub = this.deps.api.onDayUpdated(() => {
      if (this.panel?.visible) void this.post();
    });
    panel.onDidChangeViewState((e) => {
      if (e.webviewPanel.visible) void this.post();
    });
    panel.onDidDispose(() => {
      this.updateSub?.dispose();
      this.updateSub = undefined;
      this.panel = undefined;
    });
  }

  isOpen(): boolean {
    return this.panel !== undefined;
  }

  dispose(): void {
    this.panel?.dispose();
  }

  async buildModel(): Promise<PanelModel> {
    const { api, now, remote } = this.deps;
    const today = localDate((now ?? Date.now)());
    if (remote) return buildPanelModel([], api.maskState(), today, undefined, { remote: true });
    const from = shiftBack(today, 6);
    const days = await api.getDays(from, today);
    this.rememberNames(days);
    let compare: CompareInput | undefined;
    try {
      compare = await this.deps.compare?.();
    } catch {
      compare = undefined;
    }
    return buildPanelModel(days, api.maskState(), today, compare);
  }

  private rememberNames(days: DayRecord[]): void {
    for (const d of days) for (const p of d.projects) if (p.realName) this.realNames.set(p.key, p.realName);
  }

  async post(): Promise<void> {
    const panel = this.panel;
    if (!panel) return;
    try {
      const model = await this.buildModel();
      await panel.webview.postMessage({ type: 'model', model });
    } catch (err) {
      await panel.webview.postMessage({ type: 'error', message: err instanceof Error ? err.message : String(err) });
    }
  }

  /** Handle one message from the webview: alias-store write (it takes the lock), merge, re-post. */
  async handle(raw: unknown): Promise<void> {
    if (!isMessage(raw)) return;
    if (raw.type === 'ready') return this.post();
    const c = this.deps.controller;
    try {
      if (c) {
        switch (raw.type) {
          case 'toggleStreamer':
            await c.setStreamerMode(!this.deps.api.maskState().streamerMode);
            break;
          case 'mask':
            await c.setMasked(raw.key, raw.masked, this.realNames.get(raw.key));
            break;
          case 'reroll':
            await c.reroll(raw.key, this.realNames.get(raw.key));
            break;
          case 'refresh':
            break;
        }
        await c.mergeNow();
      }
    } catch (err) {
      await this.panel?.webview.postMessage({
        type: 'error',
        message: err instanceof Error ? err.message : String(err),
      });
      return;
    }
    await this.post();
  }
}

function shiftBack(date: string, days: number): string {
  const [y, m, d] = date.split('-').map(Number);
  return localDate(new Date(y, m - 1, d - days).getTime());
}
