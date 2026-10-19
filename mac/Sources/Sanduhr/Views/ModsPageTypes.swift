import Foundation

// The Mods page's value types: ModsPageRead, ModsTab, ModsFocus, ModSketch and FolderNotes.
// Split out of ModsSettings.swift on 2026-10-19.

/// What one read of every seat found, handed from the background to the page.
struct ModsPageRead: Sendable {
    var paths: [String]
    var scans: [SeatScan]
    var inventory: [ModFolderInventory]
    var claude: String?
    var own: [OwnModRow]
    var app: AppModVersion?
    var notes: [String: String]
    var configs: [String: [ClaudeCodeFolders.ConfigCandidate]]
    var skipped: Int
    var origins: [String: Seat.Origin] = [:]
    var reports: [SeatReport] = []
    var running: Set<String> = []
    var unreadableProcesses = 0
    var version: String?
    var environmentSeat: String?
    /// The mods Sanduhr kept, for More's first state (2026-10-17).
    var kept: [KeptMod] = []
}

/// Mods & Config's tabs (item 71, slice 1; Compare in slice 1d).
enum ModsTab: String, CaseIterable, Sendable {
    case catalog, bySeat, compare

    var title: String {
        switch self {
        case .catalog: "Catalog"
        case .bySeat: SettingsNames.byFolder
        case .compare: "Compare"
        }
    }

    /// state.yaml's word.
    var debugName: String {
        switch self {
        case .catalog: "catalog"
        case .bySeat: "seat"
        case .compare: "compare"
        }
    }
}

/// Where a chip's click lands on By Seat.
struct ModsFocus: Equatable, Sendable {
    let seat: String
    let card: String
    let name: String
}

/// A terminal-styled sketch of where a mod draws in Claude Code (item 64): blocks stand in for
/// its content, so nothing here is live text, and nothing of the mod runs.
enum ModSketch {
    static let dim = "\u{1B}[2m", reset = "\u{1B}[0m"
    static let cyan = "\u{1B}[36m", yellow = "\u{1B}[33m", magenta = "\u{1B}[35m", green = "\u{1B}[32m"

    /// The sketch for `touches`, top to bottom as Claude Code lays it out; nil when the mod
    /// draws nothing.
    static func text(name: String, touches: ModTouches) -> String? {
        let s = Set(touches.surfaces)
        guard !s.isEmpty else { return nil }
        var lines = [dim + "⏺ ░░░░░░░░░░░░░░░░░░░░ ░░░░░░░░░" + reset]
        if s.contains(.pane) {
            lines.append(magenta + "┌ \(name) ─────────────────────" + reset)
            lines.append(magenta + "│ " + reset + dim + "░░░░░░░░░░ ░░░░░░░░" + reset)
            lines.append(magenta + "└──────────────────────────────" + reset)
        }
        if s.contains(.band) { lines.append(cyan + "▌ \(name) " + reset + cyan + "██████████░░░░░░  ░░░░" + reset) }
        lines.append("> " + dim + "░░░░░░░" + reset)
        if s.contains(.status) { lines.append(yellow + "⚠ \(name): " + reset + dim + "░░░░░░░░" + reset) }
        if s.contains(.toast) { lines.append(green + "  ◆ \(name) " + reset + dim + "░░░░░░░░░░░░" + reset) }
        if s.contains(.commands) {
            let names = touches.commands.isEmpty ? ["░░░░"] : touches.commands.prefix(4).map { $0 }
            lines.append(dim + names.map { "/" + $0 }.joined(separator: "  ") + reset)
        }
        return lines.joined(separator: "\n")
    }

    static func lineCount(_ touches: ModTouches) -> Int {
        text(name: "", touches: touches)?.split(separator: "\n", omittingEmptySubsequences: false).count ?? 0
    }
}

/// One line per folder, keyed by the normalized path (polish 2026-10-17, #8), so a note from
/// one folder never shows under another. `nil` is the general slot: a note with no folder.
struct FolderNotes: Equatable, Sendable {
    private var lines: [String: String] = [:]

    func note(for folder: String?) -> String? { lines[Self.key(folder)] }

    mutating func setNote(_ note: String?, for folder: String?) { lines[Self.key(folder)] = note }

    mutating func clearNotes() { lines = [:] }

    private static func key(_ folder: String?) -> String { folder.map(AccountData.normalized) ?? "" }
}
