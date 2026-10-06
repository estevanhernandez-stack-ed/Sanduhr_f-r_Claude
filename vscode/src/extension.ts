export interface SanduhrTimeApi {
  version: 1;
}

export function activate(): SanduhrTimeApi {
  return { version: 1 };
}

export function deactivate(): void {
  // Nothing to release yet.
}
