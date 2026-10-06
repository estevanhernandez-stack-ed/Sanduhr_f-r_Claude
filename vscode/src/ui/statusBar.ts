import * as vscode from 'vscode';
import type { SanduhrTimeApi } from '../api';
import { buildStatusBar } from './viewModel';

export const STATUS_REFRESH_MS = 5 * 60 * 1000;

export interface StatusBarDeps {
  api: Pick<SanduhrTimeApi, 'getToday' | 'onDayUpdated'>;
  remote: boolean;
}

/** A right-aligned item showing today's total. Updates on each merge and every 5 minutes. */
export function createStatusBar(deps: StatusBarDeps): vscode.Disposable {
  const item = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Right, 100);
  item.command = 'sanduhrTime.openPanel';
  let disposed = false;

  const render = async (): Promise<void> => {
    let today;
    try {
      today = deps.remote ? undefined : await deps.api.getToday();
    } catch {
      return; // keep the last text; the next tick retries
    }
    if (disposed) return;
    const m = buildStatusBar(today, { remote: deps.remote });
    item.text = m.text;
    item.tooltip = m.tooltip;
  };

  void render();
  item.show();
  const sub = deps.api.onDayUpdated(() => void render());
  const timer = setInterval(() => void render(), STATUS_REFRESH_MS);

  return {
    dispose: () => {
      disposed = true;
      clearInterval(timer);
      sub.dispose();
      item.dispose();
    },
  };
}
