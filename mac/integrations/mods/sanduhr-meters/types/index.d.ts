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

/** A Unicode letter style, the statusline's names (item 63b). */
export type LetterStyle =
  | 'bold'
  | 'italic'
  | 'bold-italic'
  | 'script'
  | 'fraktur'
  | 'double-struck'
  | 'sans'
  | 'mono'
  | 'small-caps'

/** A segment's look from the Combine sheet's Style popover: ink (one color or 2 to 4 stops). */
export type BandStyle = {
  ink: string[]
  font: LetterStyle | null
  bold: boolean
  italic: boolean
  dim: boolean
  underline: boolean
}

export type WatcherState = 'running' | 'waiting' | 'passed' | 'failed' | 'finished' | 'lost_touch'

/** One watcher as band.json carries it: an agent's with its title and times, background work as its kind. */
export type BandWatcher =
  | {
      source: 'agent'
      title: string
      short: string | null
      state: WatcherState
      done: number | null
      total: number | null
      startedAt: number | null
      endedAt: number | null
    }
  | { source: 'automatic'; kind: string; state: WatcherState }

/** Sanduhr's band.json, read: the looks for its segments, Reduce Motion, the watchers (null: switch off). */
export type MetersBand = {
  writtenAt: number
  reduceMotion: boolean
  styles: { session?: BandStyle; weekly?: BandStyle; resets?: BandStyle }
  watchers: BandWatcher[] | null
}

declare module 'claude-code' {
  interface PluginState {
    'sanduhr-meters': {
      snapshot: MetersSnapshot | null
      band: MetersBand | null
    }
  }
}
