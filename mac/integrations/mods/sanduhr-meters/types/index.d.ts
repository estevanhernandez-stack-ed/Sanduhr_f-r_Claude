// The band's state contract: what the hooks module keeps in $.state for its drawing.

/** One limit as snapshot.json carries it; times in ms since the epoch. */
export type MetersTier = { key: string; utilization: number | null; resetsAt: number | null }

/** Sanduhr's snapshot.json, read; `newer` is a schema this mod must not read. */
export type MetersSnapshot =
  | {
      kind: 'snapshot'
      capturedAt: number
      status: 'ok' | 'error'
      errorKind: string | null
      tiers: MetersTier[]
    }
  | { kind: 'newer' }

declare module 'claude-code' {
  interface PluginState {
    'sanduhr-meters': {
      snapshot: MetersSnapshot | null
    }
  }
}
