import Foundation

/// An invented config pack for the `mods-config import-sample` debug hook (issue #292), so the
/// hub page can show Compare on import without a real pack or a real folder. Names are made up,
/// the rows are built in code and never planned against a folder, and the state is marked
/// `sample` so Import writes nothing.
enum SampleConfigPack {
    /// Used only when Sanduhr has read no Claude Code folder at all.
    static let fallbackSeat = "/sample/.claude-demo"

    static let rows: [ConfigImport.Row] = [
        .init(kind: .agent, name: "standup-notes", detail: "1 file", status: .new),
        .init(kind: .skill, name: "focus-timer", detail: "3 files", status: .new),
        .init(kind: .command, name: "lint-on-save", detail: "1 file", status: .new),
        .init(kind: .skill, name: "release-helper", detail: "2 files", status: .same),
        .init(kind: .plugin, name: "focus-timer", detail: "sample-tools", status: .command("claude plugin install focus-timer@sample-tools")),
        .init(kind: .setting, name: "model", detail: "", status: .differs),
        .init(kind: .env, name: "STANDUP_CHANNEL", detail: "", status: .needsValue),
    ]

    static var pack: ModPackReader.Unpacked {
        ModPackReader.Unpacked(manifest: [
            "kind": "config",
            "label": "Sample setup",
            "created_by": "Sanduhr",
            "carry": [["kind": "Values", "name": "STANDUP_TOKEN", "why": "A secret never travels: set it by hand."]],
        ], files: [:])
    }

    static func state(seat: String) -> ConfigImportState {
        var state = ConfigImportState(pack: pack, packSHA256: "", fileName: "sample-setup.sanduhrpack", seat: seat)
        state.sample = true
        state.rows = rows
        state.chosen = Set(rows.filter { $0.choosable && $0.kind != .instructions }.map(\.id))
        return state
    }
}
