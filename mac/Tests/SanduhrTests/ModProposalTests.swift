import Foundation
import Testing
@testable import Sanduhr

/// Item 75, slice G: propose_mod's requests, checked again, staged read-only for review, kept or
/// dismissed, with the server told each step. On made-up folders only.
@Suite("Mod Studio, proposed mods")
@MainActor
struct ModProposalTests {
    private static let at = Date(timeIntervalSince1970: 1_790_000_000)

    private func request(id: String = String(repeating: "a", count: 32), name: String = "ci-band",
                         files: [[String: String]]? = nil, extra: [String: Any] = [:]) -> Data {
        var root: [String: Any] = [
            "schema_version": 1, "id": id, "requested_at": HandoffFiles.stamp(Self.at), "name": name,
            "version": "0.1.0", "summary": "CI runs above the prompt.",
            "files": files ?? [
                ["path": ".claude-plugin/plugin.json", "text": #"{"name": "\#(name)", "version": "0.1.0"}"#],
                ["path": "hooks/hooks.json", "text": #"{"modules": ["./register.tsx"]}"#],
                ["path": "hooks/register.tsx", "text": "export const register = (on) => {}\n"],
            ],
        ]
        root.merge(extra) { $1 }
        return try! JSONSerialization.data(withJSONObject: root)
    }

    private func rig(allowed: Bool = true) -> (TempHome, ModProposalStore, UserDefaults) {
        let h = TempHome()
        let defaults = UserDefaults(suiteName: "proposals-\(UUID().uuidString)")!
        defaults.set(allowed, forKey: ModProposalStore.switchKey)
        let paths = ModProposalStore.Paths(support: URL(fileURLWithPath: h.at("Support")),
                                           kept: KeptMods(dir: URL(fileURLWithPath: h.at("Support/mods"))))
        return (h, ModProposalStore(paths: paths, defaults: defaults, now: { Self.at.addingTimeInterval(5) }), defaults)
    }

    private func result(_ h: TempHome) -> [String: Any] {
        let data = FileManager.default.contents(atPath: h.at("Support/mod-result.json")) ?? Data()
        return ((try? JSONSerialization.jsonObject(with: data)) as? [String: Any])?["result"] as? [String: Any] ?? [:]
    }

    private func index(_ h: TempHome) -> [[String: Any]] {
        let data = FileManager.default.contents(atPath: h.at("Support/mod-proposals.json")) ?? Data()
        return ((try? JSONSerialization.jsonObject(with: data)) as? [String: Any])?["proposals"] as? [[String: Any]] ?? []
    }

    @Test func namesAndLimitsMatchTheServer() {
        #expect([ModProposal.switchFile, ModProposal.requestFile, ModProposal.resultFile, ModProposal.statusFile]
            == ["mods-agents.json", "mod-request.json", "mod-result.json", "mod-proposals.json"])
        let limits: [Int] = [ModProposal.maxFiles, ModProposal.maxFileBytes, ModProposal.maxBytes, ModProposal.maxSummary]
        let server: [Int] = [24, 65_536, 262_144, 200]
        #expect(limits == server)
    }

    @Test func theAppChecksAgain() {
        let bad: [(Data, String)] = [
            (request(name: "CI"), "name must be"),
            (request(name: IntegrationScripts.modName), "Sanduhr's own mod"),
            (request(files: [["path": "../x.ts", "text": ""], ["path": ".claude-plugin/plugin.json", "text": #"{"name": "ci-band"}"#]]), "'..'"),
            (request(files: [["path": "run.sh", "text": ""]]), "files must end in"),
            (request(files: [["path": ".claude-plugin/plugin.json", "text": #"{"name": "other"}"#]]), "plugin.json's name"),
            (request(files: [["path": "hooks/hooks.json", "text": "{}"]]), "plugin.json is required"),
            (request(files: [["path": ".claude-plugin/plugin.json", "text": #"{"name": "ci-band"}"#],
                             ["path": "big.ts", "text": String(repeating: "x", count: 64 * 1024 + 1)]]), "larger than 64 KB"),
            (request(extra: ["summary": ""]), "summary"),
        ]
        for (data, needle) in bad {
            guard case .refused(_, let reasons) = ModProposal.decode(data, now: Self.at) else {
                Issue.record("not refused: \(needle)")
                continue
            }
            #expect(reasons.contains { $0.contains(needle) }, "\(needle): \(reasons)")
        }
        // Too old, or not a request: dropped without an answer.
        #expect(ModProposal.decode(request(), now: Self.at.addingTimeInterval(601)) == .discard)
        #expect(ModProposal.decode(request(id: "x"), now: Self.at) == .discard)
        guard case .proposal(let p) = ModProposal.decode(request(), now: Self.at) else { Issue.record("refused"); return }
        #expect(p.name == "ci-band" && p.files.count == 3 && p.summary == "CI runs above the prompt.")
    }

    @Test func switchedOffRefusesAndStagesNothing() {
        let (h, store, _) = rig(allowed: false)
        defer { h.cleanUp() }
        store.start(watch: HandoffWatch(dir: URL(fileURLWithPath: h.at("Support"))))
        let doc = (try? JSONSerialization.jsonObject(with: FileManager.default.contents(atPath: h.at("Support/mods-agents.json")) ?? Data())) as? [String: Any]
        let propose = doc?["propose"] as? Bool
        let schema = doc?["schema_version"] as? Int
        #expect(propose == false)
        #expect(schema == 1)
        store.receive(request())
        #expect(result(h)["reason"] as? String == "switched_off")
        #expect(store.records.isEmpty && !FileManager.default.fileExists(atPath: h.at("Support/mods/.proposed")))
    }

    @Test func aProposalIsStagedReadOnlyThenKept() throws {
        let (h, store, defaults) = rig()
        defer { h.cleanUp() }
        store.receive(request())
        #expect(result(h)["status"] as? String == "pending_review")
        let r = try #require(store.pending.first)
        let staged = store.stagedPath(r)
        let perms = try FileManager.default.attributesOfItem(atPath: staged + "/hooks/register.tsx")[.posixPermissions] as? Int
        #expect(perms == 0o444)
        #expect(store.files(r).map(\.path) == [".claude-plugin/plugin.json", "hooks/hooks.json", "hooks/register.tsx"])
        #expect(index(h).first?["status"] as? String == "pending_review")
        // Index carries names and statuses, never file text.
        let raw = String(decoding: FileManager.default.contents(atPath: h.at("Support/mod-proposals.json")) ?? Data(), as: UTF8.self)
        #expect(!raw.contains("register"))

        let kept = try store.approve(r.id)
        #expect(kept.name == "ci-band" && kept.proposed == true)
        #expect(store.paths.kept.list().map(\.id) == [kept.id])
        #expect(!FileManager.default.fileExists(atPath: staged))
        #expect(index(h).first?["status"] as? String == "approved")
        // A new store reads the decisions back.
        let again = ModProposalStore(paths: store.paths, defaults: defaults)
        #expect(again.records.first?.status == .approved && again.pending.isEmpty)
    }

    @Test func aNewerProposalReplacesOneWaitingAndDismissDeletes() throws {
        let (h, store, _) = rig()
        defer { h.cleanUp() }
        store.receive(request(id: String(repeating: "a", count: 32)))
        let first = try #require(store.pending.first)
        store.receive(request(id: String(repeating: "b", count: 32)))
        #expect(store.pending.map(\.id) == [String(repeating: "b", count: 32)])
        #expect(store.records.first { $0.id == first.id }?.status == .replaced)
        #expect(!FileManager.default.fileExists(atPath: store.stagedPath(first)))
        let second = try #require(store.pending.first)
        store.dismiss(second.id)
        #expect(store.pending.isEmpty && !FileManager.default.fileExists(atPath: store.stagedPath(second)))
        #expect(Set(index(h).compactMap { $0["status"] as? String }) == ["replaced", "dismissed"])
    }

    @Test func thePromptAsksForProposeModOnlyWhenOn() {
        #expect(!ModPrompt.text(idea: "x").contains("propose_mod"))
        #expect(ModPrompt.text(idea: "x", canPropose: true).contains("propose_mod"))
    }
}
