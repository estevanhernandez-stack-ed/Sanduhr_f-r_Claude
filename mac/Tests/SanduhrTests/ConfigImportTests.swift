import Foundation
import Testing
@testable import Sanduhr

/// Item 76, slice D: a config pack opened into a seat, written by receipts and undone byte for
/// byte. On made-up homes only.
@Suite("Packs, config import")
struct ConfigImportTests {
    private static let at = Date(timeIntervalSince1970: 1_790_000_000)

    /// Exports a made-up source seat and reads the pack back, as Open would.
    private func pack(_ h: TempHome) throws -> (ModPackReader.Unpacked, String) {
        let home = AccountData.normalized(h.at(""))
        let s = "source/.claude-personal"
        h.dir("\(s)/agents"); h.dir("\(s)/skills/notes"); h.dir("\(s)/plugins")
        h.file("\(s)/agents/reviewer.md", "Read the diff.\n")
        h.file("\(s)/agents/mine.md", "The source's version.\n")
        h.file("\(s)/skills/notes/SKILL.md", "---\nname: notes\n---\nWrite notes.\n")
        h.file("\(s)/settings.json", """
        {"permissions": {"allow": ["Read(\(home)/source/notes/**)"]}, "model": "opus", "cleanupPeriodDays": 90,
         "env": {"API_TOKEN": "never-travels", "MY_FLAG": "1", "DISABLE_TELEMETRY": "1"}}
        """)
        h.file("\(s)/plugins/known_marketplaces.json", #"{"claude-plugins-official": {"source": {"source": "github", "repo": "anthropics/claude-plugins-official"}}}"#)
        h.file("\(s)/.claude.json", #"{"mcpServers": {"docs": {"type": "http", "url": "https://docs.example.com/mcp", "headers": {"X-Api-Key": "secret"}}}}"#)
        let root = h.at(s)
        var scan = SeatScan(seat: root)
        scan.agents = [SeatEntry(name: "reviewer", description: "", scope: .seat, path: root + "/agents/reviewer.md"),
                       SeatEntry(name: "mine", description: "", scope: .seat, path: root + "/agents/mine.md")]
        scan.skills = [SeatEntry(name: "notes", description: "", scope: .seat, path: root + "/skills/notes/SKILL.md")]
        scan.plugins = [ModItem(folder: root, name: "swift-lsp", path: nil, origin: .installed(marketplace: "claude-plugins-official"))]
        let items = ConfigPack.plan(seat: root, scan: scan, keptOn: [], kept: KeptMods(dir: URL(fileURLWithPath: h.at("k"))), home: home)
        let url = URL(fileURLWithPath: h.at("p.sanduhrpack"))
        try ConfigPack.write(items, label: "Personal", to: url, version: "2.21.0", now: Self.at)
        return (try ModPackReader.read(url), home)
    }

    private func row(_ rows: [ConfigImport.Row], _ kind: ConfigPack.Item.Kind, _ name: String) throws -> ConfigImport.Row {
        try #require(rows.first { $0.kind == kind && $0.name == name }, "\(kind) \(name)")
    }

    @Test func rowsCompareThePackWithTheSeat() throws {
        let h = TempHome()
        defer { h.cleanUp() }
        let (p, home) = try pack(h)
        let target = "target/.claude-personal"
        h.dir("\(target)/agents")
        h.file("\(target)/agents/reviewer.md", "Read the diff.\n")
        h.file("\(target)/agents/mine.md", "The target's own.\n")
        h.file("\(target)/settings.json", "{\n  \"model\": \"sonnet\",\n  \"env\": {\"MY_FLAG\": \"0\"}\n}\n")
        let rows = ConfigImport.plan(p, seat: h.at(target), home: home)

        #expect(try row(rows, .agent, "reviewer").status == .same)
        #expect(try row(rows, .agent, "mine").status == .differs)
        #expect(try row(rows, .skill, "notes").status == .new)
        #expect(try row(rows, .setting, "model").status == .differs)
        let perms = try row(rows, .setting, "permissions")
        #expect(perms.status == .new)
        // The home folder is put back for this Mac.
        #expect((perms.value as? [String: Any])?["allow"] as? [String] == ["Read(\(home)/source/notes/**)"])
        #expect(try row(rows, .env, "API_TOKEN").status == .needsValue)
        #expect(try row(rows, .env, "MY_FLAG").status == .same)
        let telemetry = try row(rows, .env, "DISABLE_TELEMETRY")
        #expect(telemetry.status == .needsValue && telemetry.value as? String == "1")
        #expect(try row(rows, .env, "API_TOKEN").value == nil)
        guard case .command(let plugin) = try row(rows, .plugin, "swift-lsp").status,
              case .command(let mcp) = try row(rows, .mcpServer, "docs").status else {
            Issue.record("plugins and servers are commands")
            return
        }
        #expect(plugin.contains("claude plugin marketplace add 'anthropics/claude-plugins-official'"))
        #expect(plugin.contains("claude plugin install 'swift-lsp@claude-plugins-official' --scope user"))
        #expect(plugin.hasPrefix("CLAUDE_CONFIG_DIR=\"$HOME\"'/target/.claude-personal' "))
        #expect(mcp.contains("claude mcp add-json -s user 'docs'") && mcp.contains("${X_API_KEY}") && !mcp.contains("secret"))
    }

    @Test func anImportWritesOnlyWhatIsNewAndUndoesByteForByte() throws {
        let h = TempHome()
        defer { h.cleanUp() }
        let (p, home) = try pack(h)
        let target = "target/.claude-personal"
        h.dir("\(target)/agents")
        h.file("\(target)/agents/mine.md", "The target's own.\n")
        let settings = "{\n    \"model\": \"sonnet\",\n    \"theme\": \"dark\"\n}\n"
        h.file("\(target)/settings.json", settings)
        let seat = h.at(target)
        let rows = ConfigImport.plan(p, seat: seat, home: home)
        let chosen = rows.filter(\.choosable)

        let (record, failure) = ConfigImport.apply(chosen, env: ["API_TOKEN": "typed-here"], seat: seat, label: "Personal",
                                                   packSHA256: "x", now: Self.at)
        #expect(failure == nil)
        #expect(FileManager.default.contents(atPath: h.at("\(target)/agents/reviewer.md")) == Data("Read the diff.\n".utf8))
        #expect(FileManager.default.fileExists(atPath: h.at("\(target)/skills/notes/SKILL.md")))
        #expect(FileManager.default.contents(atPath: h.at("\(target)/agents/mine.md")) == Data("The target's own.\n".utf8))
        let written = try String(contentsOf: URL(fileURLWithPath: h.at("\(target)/settings.json")), encoding: .utf8)
        #expect(written.hasPrefix("{\n    \"model\": \"sonnet\",\n    \"theme\": \"dark\""))   // the seat's own, as they were
        let parsed = try JSONEdit.root(Data(written.utf8))
        #expect(parsed["model"] as? String == "sonnet" && parsed["cleanupPeriodDays"] as? Int == 90)
        // The typed value, and the switch with the value it came with.
        #expect((parsed["env"] as? [String: String]) == ["API_TOKEN": "typed-here", "DISABLE_TELEMETRY": "1"])
        // The record names what it wrote, never an env value.
        let encoded = try JSONEncoder().encode(record)
        #expect(!String(decoding: encoded, as: UTF8.self).contains("typed-here"))

        #expect(ConfigImport.undo(record).isEmpty)
        #expect(try String(contentsOf: URL(fileURLWithPath: h.at("\(target)/settings.json")), encoding: .utf8) == settings)
        #expect(!FileManager.default.fileExists(atPath: h.at("\(target)/agents/reviewer.md")))
        #expect(!FileManager.default.fileExists(atPath: h.at("\(target)/skills")))
        #expect(FileManager.default.fileExists(atPath: h.at("\(target)/agents/mine.md")))
    }

    @Test func theCarryListComesWithThePackAndGrowsWithWhatTheImportLeft() throws {
        let h = TempHome()
        defer { h.cleanUp() }
        let (p, home) = try pack(h)
        let target = "target/.claude-personal"
        h.dir(target)
        let rows = ConfigImport.plan(p, seat: h.at(target), home: home)
        let fromPack = ConfigImport.packCarry(p, rows: rows)
        // The header's variable is still to bring; API_TOKEN is a row here, so not said twice.
        #expect(fromPack.contains { $0.kind == "Values" && $0.name == "X_API_KEY" })
        #expect(!fromPack.contains { $0.name == "API_TOKEN" })
        #expect(fromPack.contains { $0.kind == "Never in a pack" && $0.name == "Sign-in" })

        let skill = try row(rows, .skill, "notes"), token = try row(rows, .env, "API_TOKEN"), telemetry = try row(rows, .env, "DISABLE_TELEMETRY")
        let left = ConfigImport.leftOver(rows, chosen: [token.id, telemetry.id], env: [:])
        #expect(left.contains(ConfigPack.Carry(kind: "Skills", name: "notes", why: "Not brought over.")))
        #expect(left.contains(ConfigPack.Carry(kind: "Environment variables", name: "API_TOKEN", why: "No value was typed: set it in settings.json by hand.")))
        #expect(!left.contains { $0.name == "DISABLE_TELEMETRY" })   // its value came with the pack
        #expect(left.contains { $0.kind == "MCP servers" && $0.name == "docs" && $0.why.hasPrefix("Run it yourself: ") })
        #expect(!ConfigImport.leftOver(rows, chosen: [skill.id], env: [:]).contains { $0.name == "notes" })
    }

    @Test func aPluginFromAFolderIsWrittenAsAMarketplaceItsScriptRunnable() throws {
        let h = TempHome()
        defer { h.cleanUp() }
        let home = AccountData.normalized(h.at(""))
        h.dir("source/.claude-personal"); h.dir("tools/stopper/.claude-plugin"); h.dir("tools/stopper/hooks")
        h.file("tools/stopper/.claude-plugin/plugin.json", #"{"name": "stopper", "hooks": {"Stop": [{"hooks": [{"type": "command", "command": "python3 ${CLAUDE_PLUGIN_ROOT}/hooks/note.py"}]}]}}"#)
        h.file("tools/stopper/hooks/hooks.json", #"{"hooks": {"Stop": [{"hooks": [{"type": "command", "command": "${CLAUDE_PLUGIN_ROOT}/hooks/stop.sh"}]}]}}"#)
        h.file("tools/stopper/hooks/stop.sh", "#!/bin/sh\necho done\n")
        h.file("tools/stopper/hooks/note.py", "print('noted')\n")
        try FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: h.at("tools/stopper/hooks/stop.sh"))
        let source = h.at("source/.claude-personal")
        var scan = SeatScan(seat: source)
        scan.plugins = [ModItem(folder: source, name: "stopper", path: h.at("tools/stopper"), origin: .pluginDirs)]
        let items = ConfigPack.plan(seat: source, scan: scan, keptOn: [], kept: KeptMods(dir: URL(fileURLWithPath: h.at("k"))), home: home)
        let url = URL(fileURLWithPath: h.at("p.sanduhrpack"))
        try ConfigPack.write(items, label: "Personal", to: url, version: "2.26.0", now: Self.at)
        let p = try ModPackReader.read(url)

        let target = "target/.claude-personal"
        h.dir(target)
        let seat = h.at(target)
        let row = try row(ConfigImport.plan(p, seat: seat, home: home, cli: .ready), .plugin, "stopper")
        guard case .install(let cmd) = row.status else { Issue.record("a carried plugin installs through claude"); return }
        #expect(cmd.contains("claude plugin marketplace add '\(seat)/carried-plugins/stopper'") && cmd.contains("claude plugin install 'stopper@carried-stopper'"))
        #expect(row.detail.contains("runs hooks/stop.sh, python3 hooks/note.py") || row.detail.contains("runs python3 hooks/note.py, hooks/stop.sh"))
        #expect(row.job == .plugin(id: "stopper@carried-stopper", marketplace: "carried-stopper", source: seat + "/carried-plugins/stopper"))

        let (record, failure) = ConfigImport.apply([row], env: [:], seat: seat, label: "Personal", packSHA256: "x", now: Self.at)
        #expect(failure == nil)
        let base = h.at("\(target)/carried-plugins/stopper")
        let market = try JSONSerialization.jsonObject(with: Data(contentsOf: URL(fileURLWithPath: base + "/.claude-plugin/marketplace.json"))) as? [String: Any]
        #expect(market?["name"] as? String == "carried-stopper")
        #expect((market?["plugins"] as? [[String: Any]])?.first?["source"] as? String == "./plugin")
        #expect(FileManager.default.isExecutableFile(atPath: base + "/plugin/hooks/stop.sh"))
        #expect(!FileManager.default.isExecutableFile(atPath: base + "/plugin/hooks/note.py"))
        #expect(ConfigImport.undo(record).isEmpty)
        #expect(!FileManager.default.fileExists(atPath: h.at("\(target)/carried-plugins")))

        // Without claude, the files are written and the command is on the carry list.
        let manual = try self.row(ConfigImport.plan(p, seat: seat, home: home, cli: .missing), .plugin, "stopper")
        #expect(manual.status == .new && manual.command?.contains("claude plugin install 'stopper@carried-stopper'") == true)
        #expect(ConfigImport.leftOver([manual], chosen: [manual.id], env: [:]).first?.why.hasPrefix("Its files are written; install it yourself: ") == true)
    }

    @Test func undoLeavesWhatChangedSinceAndASeatWithoutSettingsGetsItsFileBackToNone() throws {
        let h = TempHome()
        defer { h.cleanUp() }
        let (p, home) = try pack(h)
        let target = "fresh/.claude-personal"
        h.dir(target)
        let seat = h.at(target)
        let rows = ConfigImport.plan(p, seat: seat, home: home).filter(\.choosable)
        let (record, failure) = ConfigImport.apply(rows, env: [:], seat: seat, label: "Personal", packSHA256: "x", now: Self.at)
        #expect(failure == nil)
        #expect(FileManager.default.fileExists(atPath: h.at("\(target)/settings.json")))
        // Edited after the import: left, and said so.
        try "Read the diff, twice.\n".write(toFile: h.at("\(target)/agents/reviewer.md"), atomically: true, encoding: .utf8)
        let left = ConfigImport.undo(record)
        #expect(left.count == 1 && left[0].hasSuffix("reviewer.md, changed since"))
        #expect(FileManager.default.fileExists(atPath: h.at("\(target)/agents/reviewer.md")))
        #expect(!FileManager.default.fileExists(atPath: h.at("\(target)/settings.json")))
        #expect(!FileManager.default.fileExists(atPath: h.at("\(target)/skills")))
    }

    @Test func noFolderChosenPlansNothing() throws {
        let h = TempHome()
        defer { h.cleanUp() }
        let (p, home) = try pack(h)
        var state = ConfigImportState(pack: p, packSHA256: "x", fileName: "p.sanduhrpack", seat: "")
        state.plan(home: home)
        #expect(!state.hasSeat && state.rows.isEmpty && state.chosen.isEmpty)
        h.dir("fresh/.claude-new")
        state.seat = h.at("fresh/.claude-new")
        state.plan(home: home)
        #expect(state.hasSeat && !state.chosen.isEmpty)
    }

    @Test func theLogKeepsImportsUntilUndone() {
        let h = TempHome()
        defer { h.cleanUp() }
        let log = ConfigImport.Log(url: URL(fileURLWithPath: h.at("imports.json")))
        let r = ConfigImport.Record(id: "a", seat: "/s", label: "Personal", packSHA256: "x", at: Self.at,
                                    steps: [.init(kind: .file, name: "/s/agents/a.md", sha256: "y")])
        log.add(r)
        #expect(log.list() == [r])
        log.remove("a")
        #expect(log.list().isEmpty)
    }
}
