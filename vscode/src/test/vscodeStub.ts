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

export const env = { sessionId: 'fixture-session-id' };
export const commands = {};
