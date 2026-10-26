import Foundation

/// An invented config pack for the `mods-config import-sample` debug hook (issue #292), so the
/// hub page can show Compare on import without a real pack or a real folder. Names are made up,
/// the rows are built in code and never planned against a folder, and the state is marked
/// `sample` so Import writes nothing.
enum SampleConfigPack {
    /// The one folder the import sample's Into list offers: invented, shown as typed.
    static let seat = "~/.claude-sample"
    static let seatLabel = "Sample"

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

    // MARK: Export sample

    private static func file(_ path: String, _ text: String) -> ConfigPack.File { .init(path: path, text: text) }

    private static func json(_ object: Any) -> Data? { try? JSONSerialization.data(withJSONObject: object) }

    /// Invented rows for the `mods-config export-sample` hook, built from the Export Config
    /// sheet's own row types: nothing is read from a folder, and two rows can't travel.
    static var exportItems: [ConfigPack.Item] {
        var items: [ConfigPack.Item] = []
        for (name, text) in [("standup-notes", "Writes the daily standup from yesterday's commits."),
                             ("release-reviewer", "Reads a diff and lists what to check before a release.")] {
            var item = ConfigPack.Item(kind: .agent, name: name, detail: "1 file, 1 KB")
            item.files = [file("agents/\(name).md", "---\nname: \(name)\n---\n\(text)\n")]
            items.append(item)
        }
        for (name, count) in [("focus-timer", 3), ("release-helper", 2)] {
            var item = ConfigPack.Item(kind: .skill, name: name, detail: "\(count) files, 2 KB")
            item.files = [file("skills/\(name)/SKILL.md", "---\nname: \(name)\n---\nAn invented skill for the sample.\n")]
            items.append(item)
        }
        var blockedSkill = ConfigPack.Item(kind: .skill, name: "deploy-notes", detail: "2 files, 1 KB")
        blockedSkill.blocked = "A line looks like a secret: skills/deploy-notes/README.md line 12."
        items.append(blockedSkill)
        var plugin = ConfigPack.Item(kind: .plugin, name: "sample-tools", detail: "from sample-marketplace")
        plugin.json = json(["name": "sample-tools", "marketplace": ["name": "sample-marketplace", "source": ["source": "github", "repo": "example/sample-marketplace"]],
                            "scope": "user", "enabled": true] as [String: Any])
        items.append(plugin)
        items.append(ConfigPack.Item(kind: .plugin, name: "local-helper", detail: "from a folder",
                                     blocked: "Its folder couldn't be read, or holds a link."))
        var server = ConfigPack.Item(kind: .mcpServer, name: "status-board", detail: "http, 1 headers (Authorization: ${STATUS_BOARD_TOKEN})")
        server.json = json(["name": "status-board", "config": ["type": "http", "url": "https://status.example.com/mcp",
                                                              "headers": ["Authorization": "${STATUS_BOARD_TOKEN}"]]] as [String: Any])
        items.append(server)
        var env = ConfigPack.Item(kind: .env, name: "env", detail: "1 name, 1 typed in on import: STANDUP_CHANNEL")
        env.json = json([["name": "STANDUP_CHANNEL"]])
        items.append(env)
        return items
    }

    static func exportState() -> ConfigExportState {
        let items = exportItems
        var state = ConfigExportState(seat: "~/.claude-sample", label: "Sample", items: items,
                                      chosen: Set(items.filter { $0.canTravel && $0.onByDefault }.map(\.id)))
        state.sample = true
        return state
    }
}
