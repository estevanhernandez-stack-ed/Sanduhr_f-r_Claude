import * as fs from 'node:fs';
import * as path from 'node:path';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import * as vscode from 'vscode';
import { webviewPanels, statusBarItems, type FakeWebviewPanel } from '../test/vscodeStub';
import type { MaskState } from '../store/aliases';
import type { DayRecord } from '../types';
import { day, project } from './fixtures';
import { TimePanel, buildCsp, buildHtml, type PanelController } from './panel';
import { createStatusBar } from './statusBar';

const H = 3_600_000;
const NOW = new Date(2026, 9, 6, 14, 0, 0).getTime();
const emitters: Array<(d: DayRecord) => void> = [];
const onDayUpdated = (l: (d: DayRecord) => void): { dispose(): void } => {
  emitters.push(l);
  return { dispose: () => void emitters.splice(emitters.indexOf(l), 1) };
};

let mask: MaskState;
let calls: string[];
let record: DayRecord;

function controller(): PanelController {
  return {
    setStreamerMode: async (on) => void calls.push(`streamer:${on}`),
    setMasked: async (k, m, n) => void calls.push(`mask:${k}:${m}:${n}`),
    reroll: async (k, n) => {
      calls.push(`reroll:${k}:${n}`);
      return 'x';
    },
    mergeNow: async () => void calls.push('merge'),
  };
}

function make(over: { controller?: PanelController | undefined; remote?: boolean } = {}): { panel: TimePanel; fake: FakeWebviewPanel } {
  const panel = new TimePanel({
    api: { getDays: async () => [record], maskState: () => mask, onDayUpdated },
    controller: 'controller' in over ? over.controller : controller(),
    extensionUri: vscode.Uri.file('/fixture/ext'),
    remote: over.remote ?? false,
    now: () => NOW,
  });
  panel.open();
  return { panel, fake: webviewPanels[webviewPanels.length - 1] };
}

beforeEach(() => {
  webviewPanels.length = 0;
  statusBarItems.length = 0;
  emitters.length = 0;
  calls = [];
  mask = { readable: true, streamerMode: false, projects: {} };
  record = day('2026-10-06', { youMs: H }, [project({ key: 'aaaa', realName: 'Demo App', youMs: H })]);
});

const lastModel = (f: FakeWebviewPanel): { projects: { name: string }[]; streamerMode: boolean } =>
  (f.webview.posted.filter((m) => (m as { type: string }).type === 'model').pop() as { model: never }).model;

describe('panel creation', () => {
  it('opens one titled panel with scripts on and nothing retained when hidden', () => {
    const { fake, panel } = make();
    expect(fake.title).toBe('Sanduhr Time');
    expect(fake.options.enableScripts).toBe(true);
    expect(fake.options.retainContextWhenHidden).toBe(false);
    panel.open();
    expect(webviewPanels).toHaveLength(1);
    expect(fake.revealed).toBe(1);
  });

  it('loads media through webview uris under a strict CSP', () => {
    const { fake } = make();
    const html = fake.webview.html;
    expect(html).toContain('panel.css');
    expect(html).toContain('panel.js');
    const csp = /Content-Security-Policy" content="([^"]+)"/.exec(html)![1];
    expect(csp).not.toMatch(/unsafe-inline|unsafe-eval/);
    expect(csp).toMatch(
      /^default-src 'none'; style-src https:\/\/fixture\.vscode-cdn\.net; script-src 'nonce-[^']+'; img-src https:\/\/fixture\.vscode-cdn\.net$/,
    );
    const nonce = /nonce-([^']+)'/.exec(csp)![1];
    expect(html).toContain(`<script nonce="${nonce}"`);
    expect(html).not.toMatch(/\son[a-z]+=/i);
    expect(html).not.toMatch(/<script(?![^>]*src=)/);
  });

  it('buildCsp never allows unsafe sources', () => {
    const csp = buildCsp('vscode-resource:', 'abc');
    expect(csp).not.toContain('unsafe-inline');
    expect(csp).not.toContain('unsafe-eval');
    expect(buildHtml(csp, 'abc', 'a.css', 'a.js')).toContain("script-src 'nonce-abc'");
  });

  it('uses a fresh nonce each time the panel is created', () => {
    const a = make();
    a.fake.dispose();
    const b = make();
    const n = (f: FakeWebviewPanel): string => /nonce-([^']+)'/.exec(f.webview.html)![1];
    expect(n(a.fake)).not.toBe(n(b.fake));
  });

  it('throws a clear error when the extension location is unknown', () => {
    const panel = new TimePanel({
      api: { getDays: async () => [], maskState: () => mask, onDayUpdated },
      controller: undefined,
      extensionUri: undefined,
      remote: false,
    });
    expect(() => panel.open()).toThrow(/location/);
  });
});

describe('message handling', () => {
  it('ready posts the model without writing or merging', async () => {
    const { fake } = make();
    await fake.webview.fireMessage({ type: 'ready' });
    expect(lastModel(fake).projects[0].name).toBe('Demo App');
    expect(calls).toEqual([]);
  });

  it('toggleStreamer flips the live state, then merges, then re-posts', async () => {
    const { fake } = make();
    await fake.webview.fireMessage({ type: 'toggleStreamer' });
    expect(calls).toEqual(['streamer:true', 'merge']);
    mask = { ...mask, streamerMode: true };
    await fake.webview.fireMessage({ type: 'toggleStreamer' });
    expect(calls.slice(2)).toEqual(['streamer:false', 'merge']);
    expect(lastModel(fake).streamerMode).toBe(true);
  });

  it('mask passes the key, the flag and the real name held in the host, then merges', async () => {
    const { fake } = make();
    await fake.webview.fireMessage({ type: 'ready' });
    await fake.webview.fireMessage({ type: 'mask', key: 'aaaa', masked: true });
    expect(calls).toEqual(['mask:aaaa:true:Demo App', 'merge']);
  });

  it('reroll calls the store then merges', async () => {
    const { fake } = make();
    await fake.webview.fireMessage({ type: 'ready' });
    await fake.webview.fireMessage({ type: 'reroll', key: 'aaaa' });
    expect(calls).toEqual(['reroll:aaaa:Demo App', 'merge']);
  });

  it('refresh merges and re-posts, writing nothing', async () => {
    const { fake } = make();
    const before = fake.webview.posted.length;
    await fake.webview.fireMessage({ type: 'refresh' });
    expect(calls).toEqual(['merge']);
    expect(fake.webview.posted.length).toBe(before + 1);
  });

  it('writes before the merge, and re-posts after both', async () => {
    const order: string[] = [];
    const c = controller();
    c.setMasked = async () => void order.push('write');
    c.mergeNow = async () => void order.push('merge');
    const { fake } = make({ controller: c });
    const orig = fake.webview.postMessage;
    fake.webview.postMessage = async (m) => {
      order.push('post');
      return orig(m);
    };
    await fake.webview.fireMessage({ type: 'mask', key: 'aaaa', masked: false });
    expect(order).toEqual(['write', 'merge', 'post']);
  });

  it('ignores malformed and unknown messages', async () => {
    const { fake } = make();
    for (const m of [null, 'x', {}, { type: 'nope' }, { type: 'mask', key: 5, masked: true }, { type: 'mask', key: 'a', masked: 'yes' }, { type: 'reroll' }]) {
      await fake.webview.fireMessage(m);
    }
    expect(calls).toEqual([]);
    expect(fake.webview.posted).toEqual([]);
  });

  it('ignores mask and reroll for anything that is not a project key', async () => {
    const { fake } = make();
    for (const key of ['constructor', '__proto__', 'toString', '', '../x', 'ABCDEF', '0123456789abcdef0']) {
      await fake.webview.fireMessage({ type: 'mask', key, masked: true });
      await fake.webview.fireMessage({ type: 'reroll', key });
    }
    expect(calls).toEqual([]);
    await fake.webview.fireMessage({ type: 'reroll', key: 'none' });
    await fake.webview.fireMessage({ type: 'mask', key: '0123456789abcdef', masked: true });
    expect(calls.filter((c) => c.startsWith('reroll') || c.startsWith('mask'))).toHaveLength(2);
  });

  it('a failing write posts an error and skips the merge', async () => {
    const c = controller();
    c.setMasked = async () => {
      throw new Error('alias store busy');
    };
    const { fake } = make({ controller: c });
    await fake.webview.fireMessage({ type: 'mask', key: 'aaaa', masked: true });
    expect(calls).toEqual([]);
    expect(fake.webview.posted).toEqual([{ type: 'error', message: 'alias store busy' }]);
  });

  it('never sends a real name to the webview for a masked project', async () => {
    mask = { readable: true, streamerMode: true, projects: { aaaa: { alias: 'Project Birch', masked: true } } };
    const { fake } = make();
    await fake.webview.fireMessage({ type: 'ready' });
    expect(JSON.stringify(fake.webview.posted)).not.toContain('Demo App');
  });
});

describe('live updates', () => {
  it('re-posts on onDayUpdated while visible, not while hidden', async () => {
    const { fake } = make();
    fake.webview.posted.length = 0;
    emitters.forEach((l) => l(record));
    await vi.waitFor(() => expect(fake.webview.posted).toHaveLength(1));
    fake.fireViewState(false);
    emitters.forEach((l) => l(record));
    await new Promise((r) => setTimeout(r, 10));
    expect(fake.webview.posted).toHaveLength(1);
    fake.fireViewState(true);
    await vi.waitFor(() => expect(fake.webview.posted).toHaveLength(2));
  });

  it('stops listening once disposed', () => {
    const { fake } = make();
    expect(emitters).toHaveLength(1);
    fake.dispose();
    expect(emitters).toHaveLength(0);
  });
});

describe('remote window', () => {
  it('posts the remote state and ignores writes', async () => {
    const { fake } = make({ controller: undefined, remote: true });
    await fake.webview.fireMessage({ type: 'toggleStreamer' });
    const m = lastModel(fake) as unknown as { remote: boolean; notice: string };
    expect(m.remote).toBe(true);
    expect(m.notice).toMatch(/Local workspaces only/);
  });
});

describe('media files', () => {
  const media = (f: string): string => fs.readFileSync(path.join(__dirname, '..', '..', 'media', f), 'utf8');

  it('panel.js builds no markup from data', () => {
    const js = media('panel.js');
    expect(js).not.toMatch(/innerHTML|outerHTML|insertAdjacentHTML|document\.write|eval\(|new Function/);
    expect(js).not.toMatch(/\bon(click|load|error)\s*=/);
  });

  it('panel.css takes colors only from theme variables', () => {
    const stripped = media('panel.css').replace(/var\([^;]*\)/g, '');
    expect(stripped).not.toMatch(/#[0-9a-fA-F]{3,8}\b/);
    expect(stripped).not.toMatch(/rgba?\(/);
  });
});

describe('status bar', () => {
  it('is right-aligned, opens the panel, and updates on merges and the 5-minute tick', async () => {
    vi.useFakeTimers();
    try {
      let today: DayRecord | undefined = day('2026-10-06', { youMs: 100 * 60_000, claudeMs: 125 * 60_000, bothMs: 33 * 60_000 });
      const sub = createStatusBar({ api: { getToday: async () => today, onDayUpdated }, remote: false });
      const item = statusBarItems[0];
      expect(item.alignment).toBe(vscode.StatusBarAlignment.Right);
      expect(item.command).toBe('sanduhrTime.openPanel');
      expect(item.shown).toBe(true);
      await vi.advanceTimersByTimeAsync(0);
      expect(item.text).toBe('$(watch) 4h 18m');
      expect(item.tooltip).toBe('You 1h 40m · Claude 2h 05m · Both 33m');

      today = day('2026-10-06', { youMs: H });
      emitters.forEach((l) => l(today!));
      await vi.advanceTimersByTimeAsync(0);
      expect(item.text).toBe('$(watch) 1h 00m');

      today = day('2026-10-06', { youMs: 2 * H });
      await vi.advanceTimersByTimeAsync(5 * 60_000);
      expect(item.text).toBe('$(watch) 2h 00m');

      sub.dispose();
      expect(item.disposed).toBe(true);
      expect(emitters).toHaveLength(0);
    } finally {
      vi.useRealTimers();
    }
  });

  it('shows off with the local-only tooltip in a remote window', async () => {
    createStatusBar({ api: { getToday: async () => undefined, onDayUpdated }, remote: true });
    await new Promise((r) => setTimeout(r, 0));
    expect(statusBarItems[0].text).toBe('$(watch) off');
    expect(statusBarItems[0].tooltip).toBe('Local workspaces only');
  });
});
