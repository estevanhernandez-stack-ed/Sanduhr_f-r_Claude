// Minimal stand-in for the `vscode` module in unit tests. Grows with the extension.
// Modeled on the 626 Labs extension's stub: hand-written, extended here rather than mocked per file.

export class EventEmitter<T> {
  private listeners: Array<(e: T) => void> = [];
  event = (listener: (e: T) => void): { dispose: () => void } => {
    this.listeners.push(listener);
    return { dispose: () => (this.listeners = this.listeners.filter((l) => l !== listener)) };
  };
  fire(e: T): void {
    for (const l of [...this.listeners]) l(e);
  }
  dispose(): void {
    this.listeners = [];
  }
}

export enum TextDocumentChangeReason {
  Undo = 1,
  Redo = 2,
}

export class Position {
  constructor(
    public readonly line: number,
    public readonly character: number,
  ) {}
}

export class Range {
  readonly start: Position;
  readonly end: Position;
  constructor(startLine: number, startChar: number, endLine: number, endChar: number) {
    this.start = new Position(startLine, startChar);
    this.end = new Position(endLine, endChar);
  }
}

export const Uri = {
  file: (p: string) => ({ fsPath: p, path: p, scheme: 'file', toString: () => `file://${p}` }),
  parse: (value: string) => {
    const i = value.indexOf(':');
    const scheme = i === -1 ? 'file' : value.slice(0, i);
    const rest = i === -1 ? value : value.slice(i + 1);
    return { fsPath: rest, path: rest, scheme, toString: () => value };
  },
};

const windowState = new EventEmitter<{ focused: boolean }>();
const activeEditor = new EventEmitter<unknown>();
const selection = new EventEmitter<unknown>();
const changeDoc = new EventEmitter<unknown>();
const saveDoc = new EventEmitter<unknown>();

export const window = {
  state: { focused: true },
  onDidChangeWindowState: windowState.event,
  onDidChangeActiveTextEditor: activeEditor.event,
  onDidChangeTextEditorSelection: selection.event,
  /** Test hooks: fire the matching event. */
  fireWindowState(focused: boolean): void {
    window.state.focused = focused;
    windowState.fire({ focused });
  },
  fireActiveEditor: (e: unknown): void => activeEditor.fire(e),
  fireSelection: (e: unknown): void => selection.fire(e),
};

export const workspace = {
  onDidChangeTextDocument: changeDoc.event,
  onDidSaveTextDocument: saveDoc.event,
  fireChange: (e: unknown): void => changeDoc.fire(e),
  fireSave: (e: unknown): void => saveDoc.fire(e),
};

export const env: {
  sessionId: string;
  remoteName: string | undefined;
  openExternal: (uri: unknown) => Promise<boolean>;
  opened: unknown[];
} = {
  sessionId: 'fixture-session-id',
  remoteName: undefined,
  opened: [],
  openExternal: async (uri) => {
    env.opened.push(uri);
    return true;
  },
};

/** Registered command handlers, by id. Tests call them through `commands.run`. */
const handlers = new Map<string, (...args: unknown[]) => unknown>();
export const commands = {
  registerCommand: (id: string, fn: (...args: unknown[]) => unknown): { dispose: () => void } => {
    handlers.set(id, fn);
    return { dispose: () => void handlers.delete(id) };
  },
  /** Test hook. */
  registered: (): string[] => [...handlers.keys()],
  run: async (id: string, ...args: unknown[]): Promise<unknown> => handlers.get(id)?.(...args),
};

const configValues = new Map<string, unknown>();
/** Test hook: set `section.key` for `getConfiguration`. */
export function setConfig(key: string, value: unknown): void {
  configValues.set(key, value);
}
(workspace as Record<string, unknown>).getConfiguration = (section: string) => ({
  get: <T>(key: string, fallback?: T): T | undefined => (configValues.get(section + '.' + key) as T | undefined) ?? fallback,
});
export const shown: string[] = [];
(window as Record<string, unknown>).showInformationMessage = async (m: string): Promise<undefined> => {
  shown.push(m);
  return undefined;
};
(window as Record<string, unknown>).showErrorMessage = async (m: string): Promise<undefined> => {
  shown.push(m);
  return undefined;
};

export enum ViewColumn {
  Active = -1,
  One = 1,
}
export enum StatusBarAlignment {
  Left = 1,
  Right = 2,
}
(Uri as Record<string, unknown>).joinPath = (base: { fsPath: string }, ...parts: string[]) => Uri.file([base.fsPath, ...parts].join('/'));

export interface FakeStatusBarItem {
  alignment: number;
  priority: number | undefined;
  text: string;
  tooltip: string;
  command: string | undefined;
  shown: boolean;
  disposed: boolean;
  show(): void;
  hide(): void;
  dispose(): void;
}
export const statusBarItems: FakeStatusBarItem[] = [];
(window as Record<string, unknown>).createStatusBarItem = (alignment: number, priority?: number): FakeStatusBarItem => {
  const item: FakeStatusBarItem = {
    alignment,
    priority,
    text: '',
    tooltip: '',
    command: undefined,
    shown: false,
    disposed: false,
    show: () => void (item.shown = true),
    hide: () => void (item.shown = false),
    dispose: () => void (item.disposed = true),
  };
  statusBarItems.push(item);
  return item;
};

export interface FakeWebviewPanel {
  viewType: string;
  title: string;
  options: Record<string, unknown>;
  webview: {
    html: string;
    cspSource: string;
    options: Record<string, unknown>;
    posted: unknown[];
    postMessage(m: unknown): Promise<boolean>;
    asWebviewUri(u: { fsPath: string }): { toString(): string };
    onDidReceiveMessage(l: (m: unknown) => unknown): { dispose(): void };
    /** Test hook: deliver a message from the "webview". */
    fireMessage(m: unknown): Promise<void>;
  };
  visible: boolean;
  disposed: boolean;
  revealed: number;
  reveal(): void;
  onDidDispose(l: () => void): { dispose(): void };
  onDidChangeViewState(l: (e: unknown) => void): { dispose(): void };
  dispose(): void;
  /** Test hooks. */
  fireViewState(visible: boolean): void;
}
export const webviewPanels: FakeWebviewPanel[] = [];
(window as Record<string, unknown>).createWebviewPanel = (
  viewType: string,
  title: string,
  _column: number,
  options: Record<string, unknown>,
): FakeWebviewPanel => {
  const msg = new EventEmitter<unknown>();
  const disp = new EventEmitter<void>();
  const view = new EventEmitter<unknown>();
  const listeners: Array<(m: unknown) => unknown> = [];
  const panel: FakeWebviewPanel = {
    viewType,
    title,
    options,
    visible: true,
    disposed: false,
    revealed: 0,
    webview: {
      html: '',
      cspSource: 'https://fixture.vscode-cdn.net',
      options: {},
      posted: [],
      postMessage: async (m) => {
        panel.webview.posted.push(m);
        return true;
      },
      asWebviewUri: (u) => ({ toString: () => `https://fixture.vscode-cdn.net/${u.fsPath}` }),
      onDidReceiveMessage: (l) => {
        listeners.push(l);
        return msg.event(l as (e: unknown) => void);
      },
      fireMessage: async (m) => {
        for (const l of [...listeners]) await l(m);
      },
    },
    reveal: () => void panel.revealed++,
    onDidDispose: disp.event as never,
    onDidChangeViewState: view.event,
    dispose: () => {
      panel.disposed = true;
      disp.fire();
    },
    fireViewState: (visible) => {
      panel.visible = visible;
      view.fire({ webviewPanel: panel });
    },
  };
  webviewPanels.push(panel);
  return panel;
};
