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
