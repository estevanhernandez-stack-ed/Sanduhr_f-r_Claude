import Foundation
import Testing
@testable import Sanduhr

/// Item 76, slice K: propose_config's requests, checked again and scanned, held for review in the
/// Open sheet, decided by an import or Dismiss, with the server told each step. On made-up folders only.
@Suite("Packs, config proposals")
@MainActor
struct ConfigProposalTests {
    private static let at = Date(timeIntervalSince1970: 1_790_000_000)

    private func request(id: String = String(repeating: "c", count: 32), _ extra: [String: Any] = [:]) -> Data {
        var root: [String: Any] = [
            "schema_version": 1, "id": id, "requested_at": HandoffFiles.stamp(Self.at), "folder": ".claude-personal",
            "summary": "A reviewer agent and the docs server.",
            "files": [["path": "agents/reviewer.md", "text": "---\nname: reviewer\n---\nRead the diff.\n"],
                      ["path": "skills/notes/SKILL.md", "text": "---\nname: notes\n---\nWrite notes.\n"]],
            "settings": ["model": "opus"],
            "mcp_servers": ["docs": ["type": "http", "url": "https://docs.example.com/mcp", "headers": ["X-Api-Key": "${DOCS_KEY}"]]],
        ]
        root.merge(extra) { $1 }
        return try! JSONSerialization.data(withJSONObject: root)
    }

    private func rig(allowed: Bool = true) -> (TempHome, ModProposalStore, String) {
        let h = TempHome()
        let defaults = UserDefaults(suiteName: "sanduhr.tests.config-proposals.\(UUID().uuidString)")!
        defaults.set(allowed, forKey: ModProposalStore.switchKey)
        let personal = h.at(".claude-personal")
        h.dir(".claude-personal")
        let paths = ModProposalStore.Paths(support: URL(fileURLWithPath: h.at("Support")),
                                           kept: KeptMods(dir: URL(fileURLWithPath: h.at("Support/mods"))))
        return (h, ModProposalStore(paths: paths, defaults: defaults, now: { Self.at.addingTimeInterval(5) },
                                    folders: { [h.at(".claude"), personal] }), personal)
    }

    private func result(_ h: TempHome) -> [String: Any] {
        let data = FileManager.default.contents(atPath: h.at("Support/config-result.json")) ?? Data()
        return ((try? JSONSerialization.jsonObject(with: data)) as? [String: Any])?["result"] as? [String: Any] ?? [:]
    }

    @Test func fileNamesMatchTheServer() {
        #expect(ConfigProposal.requestFile == "config-request.json" && ConfigProposal.resultFile == "config-result.json")
    }

    @Test func aBadOrSecretProposalIsRefused() {
        let bad: [[String: Any]] = [
            ["folder": "/Users/x/.claude"],
            ["files": [["path": "hooks/run.sh", "text": "x"]]],
            ["files": [["path": "agents/leaky.md", "text": "Use sk-ant-api03-abcdefghijklmnopqrstuvwxyz0123456789 here.\n"]]],
            ["settings": ["env": ["TOKEN": "abc"]]],
            ["settings": ["permissions": ["allow": ["Bash(curl -H 'Authorization: Bearer abcdefghijklmnop0123456789' x)"]]]],
            ["mcp_servers": ["docs": ["url": "https://x", "headers": ["Authorization": "Bearer abc"]]]],
            ["mcp_servers": ["sanduhr": ["command": "python3"]]],
            ["files": [Any](), "settings": [String: Any](), "mcp_servers": [String: Any]()],
        ]
        for extra in bad {
            guard case .refused(_, let reasons) = ConfigProposal.decode(request(extra), now: Self.at) else {
                Issue.record("refused: \(extra)")
                continue
            }
            #expect(!reasons.isEmpty)
            #expect(!reasons.joined().contains("sk-ant") && !reasons.joined().contains("Bearer abc"))
        }
    }

    @Test func aProposalBecomesAPackTheOpenSheetPlans() throws {
        guard case .proposal(let p) = ConfigProposal.decode(request(), now: Self.at) else {
            Issue.record("a good proposal")
            return
        }
        let h = TempHome()
        defer { h.cleanUp() }
        h.dir(".claude-personal/agents")
        h.file(".claude-personal/agents/reviewer.md", "Its own.\n")
        let rows = ConfigImport.plan(p.pack, seat: h.at(".claude-personal"), home: h.at(""))
        #expect(rows.first { $0.kind == .agent && $0.name == "reviewer" }?.status == .differs)
        #expect(rows.first { $0.kind == .skill && $0.name == "notes" }?.status == .new)
        #expect(rows.first { $0.kind == .setting && $0.name == "model" }?.status == .new)
        guard case .command(let cmd)? = rows.first(where: { $0.kind == .mcpServer && $0.name == "docs" })?.status else {
            Issue.record("the server is a command to run")
            return
        }
        #expect(cmd.contains("${DOCS_KEY}"))
    }

    @Test func switchedOffOrAnUnknownFolderIsRefused() {
        let (h, store, _) = rig(allowed: false)
        defer { h.cleanUp() }
        store.receiveConfig(request())
        #expect(result(h)["reason"] as? String == "switched_off" && store.configs.isEmpty)
        store.allowed = true
        store.receiveConfig(request(["folder": ".claude-elsewhere"]))
        #expect(result(h)["reason"] as? String == "unknown_folder" && store.configs.isEmpty)
    }

    @Test func aProposalWaitsForReviewThenIsDecided() throws {
        let (h, store, personal) = rig()
        defer { h.cleanUp() }
        store.receiveConfig(request())
        #expect(result(h)["status"] as? String == "pending_review" && result(h)["folder"] as? String == ".claude-personal")
        let r = try #require(store.pendingConfigs.first)
        #expect(r.folder == AccountData.normalized(personal) && r.files == 2 && r.settings == 1 && r.servers == 1)
        // Staged owner-only, read back as the same pack.
        let staged = h.at("Support/mods/.proposed-config/\(r.id).json")
        #expect(((try? FileManager.default.attributesOfItem(atPath: staged))?[.posixPermissions] as? Int) == 0o600)
        let pack = try #require(store.configPack(r))
        #expect(pack.pack.files["files/agents/reviewer.md"] == Data("---\nname: reviewer\n---\nRead the diff.\n".utf8))
        // The server's index answers get_mod_status for it.
        let index = try JSONSerialization.jsonObject(with: Data(contentsOf: URL(fileURLWithPath: h.at("Support/mod-proposals.json")))) as? [String: Any]
        let entry = (index?["proposals"] as? [[String: Any]])?.first { $0["id"] as? String == r.id }
        #expect(entry?["kind"] as? String == "config" && entry?["status"] as? String == "pending_review")

        store.decideConfig(r.id, approved: false)
        #expect(store.pendingConfigs.isEmpty && store.configs.first?.status == .dismissed)
        #expect(!FileManager.default.fileExists(atPath: staged))
    }
}
