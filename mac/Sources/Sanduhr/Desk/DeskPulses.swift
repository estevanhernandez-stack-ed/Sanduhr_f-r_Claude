import Foundation

/// One alert pulse's address on the Desk: the source whose limit alerted and the limit (2.48.0, item 2).
/// A pulse used to be keyed by tier alone because only Claude's alerts pulsed; Codex's pulse the pieces
/// that show Codex, so a Claude-only piece never answers to a Codex alert.
struct DeskPulseKey: Hashable {
    var source: AgentSource
    var tier: Tier
}

/// Which pulses a Desk meter row answers to, and whether a piece has any row a source's alert can light.
/// Pure: `DeskModel` holds the counts, `DeskView` and `DeskSourceMeters` read the trigger.
enum DeskPulses {
    /// What a bar row's pulse trigger reads for a piece drawing `drawn`: a Claude piece counts Claude's
    /// pulses for the row's limit (as it always did), a Codex piece counts Codex's, and a combined piece
    /// counts Claude's plus, on a joined row (one that carries both sources' layout), Codex's. A row only
    /// pulses when its count goes up, so an unrelated source's pulse leaves it still.
    static func trigger(_ pulses: [DeskPulseKey: Int], drawn: DeskMeterSource, row: DeskMeterRow) -> Int {
        func count(_ source: AgentSource) -> Int { pulses[DeskPulseKey(source: source, tier: row.tier)] ?? 0 }
        switch drawn {
        case .claude: return count(.claude)
        case .codex: return count(.codex)
        case .combined: return count(.claude) + (row.layout != nil ? count(.codex) : 0)
        }
    }

    /// Whether a piece drawing `drawn` has a row `source`'s alert can light: a Codex piece with Codex rows,
    /// a combined piece with a joined row. `codexRows` and `combinedRows` are the model's source views.
    static func shows(_ source: AgentSource, drawn: DeskMeterSource, codexRows: [DeskMeterRow],
                      combinedRows: [DeskMeterRow]) -> Bool {
        switch (drawn, source) {
        case (.claude, .claude): return true
        case (.combined, .claude): return true
        case (.combined, .codex): return combinedRows.contains { $0.layout != nil }
        case (.codex, .codex): return !codexRows.isEmpty
        default: return false
        }
    }
}
