import Foundation
import Testing
@testable import Sanduhr

/// Claude proposes themes (item 55): the request checks, where a theme is saved, the result file
/// and the approval flow, on temp folders. Made-up themes only; never the real themes folder.

private let t0 = Date(timeIntervalSince1970: 1_791_133_000)

private func requestJSON(id: String = "req1", at: Date = t0, theme: Any = cleanTheme(), saveAs: Any? = nil,
                         apply: Any? = nil, extra: [String: Any] = [:]) -> Data {
    var root: [String: Any] = ["schema_version": 1, "id": id, "requested_at": HandoffFiles.stamp(at), "theme": theme]
    if let saveAs { root["save_as"] = saveAs }
    if let apply { root["apply"] = apply }
    root.merge(extra) { $1 }
    return try! JSONSerialization.data(withJSONObject: root)
}

private func proposal(_ data: Data) -> ThemeProposal? {
    if case .proposal(let p) = ThemeProposal.decode(data, now: t0) { return p }
    return nil
}

private func refusal(_ data: Data) -> ThemeProposal.Outcome? {
    if case .refused(_, let o) = ThemeProposal.decode(data, now: t0) { return o }
    return nil
}

@Suite("Theme proposal requests")
struct ThemeProposalRequestTests {
    @Test func decodesAGoodRequest() throws {
        var theme = cleanTheme()
        theme["description"] = "  Quiet graphite.  "
        let p = try #require(proposal(requestJSON(theme: theme)))
        #expect(p.id == "req1")
        #expect(p.name == "Test")
        #expect(p.key == "test")
        #expect(p.apply)
        #expect(p.summary == "Quiet graphite.")
        #expect(p.findings.isEmpty)
        let preview = try #require(p.preview())
        #expect(preview.displayName == "Test")
        #expect(preview.summary == "Quiet graphite.")
    }

    @Test func saveAsAndApplyFalse() throws {
        let p = try #require(proposal(requestJSON(saveAs: "graphite", apply: false)))
        #expect((p.key, p.apply) == ("graphite", false))
        #expect(try #require(proposal(requestJSON(apply: "nope"))).apply)   // anything but false applies
    }

    @Test func refusals() {
        var missing = cleanTheme()
        missing.removeValue(forKey: "accent")
        let bad = refusal(requestJSON(theme: missing))
        #expect(bad?.reason == "invalid_theme")
        #expect(bad?.findings.contains { $0.field == "accent" && $0.level == .error } == true)
        #expect(refusal(requestJSON(theme: "x"))?.reason == "invalid_params")
        #expect(refusal(requestJSON(saveAs: "Bad Key"))?.reason == "invalid_params")
        #expect(refusal(requestJSON(extra: ["schema_version": 2]))?.reason == "invalid_params")
        #expect(refusal(requestJSON(extra: ["requested_at": "yesterday"]))?.reason == "invalid_params")
        var obsidian = cleanTheme()
        obsidian["name"] = "Obsidian"
        #expect(refusal(requestJSON(theme: obsidian))?.reason == "reserved_name")
        #expect(refusal(requestJSON(saveAs: "match-desk"))?.reason == "reserved_name")
        #expect(proposal(requestJSON(theme: obsidian, saveAs: "my-obsidian")) != nil)
    }

    @Test func oldOrUnreadableRequestsAreDiscarded() {
        #expect(ThemeProposal.decode(requestJSON(at: t0.addingTimeInterval(-601)), now: t0) == .discard)
        #expect(ThemeProposal.decode(Data("nope".utf8), now: t0) == .discard)
        #expect(ThemeProposal.decode(requestJSON(id: ""), now: t0) == .discard)
    }

    /// Windows reads a partial accent_bloom or inner_highlight with defaults; the Mac's loader
    /// needs both numbers, and a whole breath period.
    @Test func normalizesWhatTheMacLoaderNeeds() throws {
        var theme = cleanTheme()
        theme["accent_bloom"] = ["blur": 5]
        theme["inner_highlight"] = ["color": "#6c63ff"]
        theme["breath_period_ms"] = 3000.6
        theme["description"] = "   "
        let p = try #require(proposal(requestJSON(theme: theme)))
        let saved = try #require(try JSONSerialization.jsonObject(with: p.file) as? [String: Any])
        #expect((saved["accent_bloom"] as? [String: Any])?["alpha"] as? Double == 0.45)
        #expect((saved["inner_highlight"] as? [String: Any])?["alpha"] as? Double == 0.2)
        #expect(saved["breath_period_ms"] as? Int == 3001)
        #expect(saved["description"] == nil)
        #expect(p.preview()?.palette.accentBloom.blur == 5)
    }

    @Test func placementNeverOverwritesAUserTheme() throws {
        let p = try #require(proposal(requestJSON()))
        let other = Data(#"{"name":"Other"}"#.utf8)
        #expect(ThemeProposal.placement(for: p) { _ in nil } == .init(key: "test", renamedFrom: nil, sameAsExisting: false))
        #expect(ThemeProposal.placement(for: p) { $0 == "test" ? other : nil }
                == .init(key: "test-2", renamedFrom: "test", sameAsExisting: false))
        #expect(ThemeProposal.placement(for: p) { ["test", "test-2"].contains($0) ? other : nil }?.key == "test-3")
        // The same theme again reuses its file.
        #expect(ThemeProposal.placement(for: p) { $0 == "test" ? p.file : nil }
                == .init(key: "test", renamedFrom: nil, sameAsExisting: true))
        #expect(ThemeProposal.placement(for: p) { _ in other } == nil)
        #expect(ThemeProposal.suffixed(String(repeating: "a", count: 40), 12).count == 40)
        #expect(ThemeProposal.suffixed("ab-" + String(repeating: "c", count: 37), 2) == "ab-" + String(repeating: "c", count: 35) + "-2")
    }

    @Test func resultFileShape() throws {
        let o = ThemeProposal.Outcome(status: .applied, key: "test-2", name: "Test", previousKey: "obsidian",
                                      savedPath: "/tmp/x/test-2.json", renamedFrom: "test",
                                      findings: [ThemeFinding(level: .warning, field: "text", message: "m")])
        let root = try #require(try JSONSerialization.jsonObject(with: ThemeProposal.resultJSON(id: "r", o, now: t0)) as? [String: Any])
        #expect(root["id"] as? String == "r")
        #expect(root["completed_at"] as? String != nil)
        let r = try #require(root["result"] as? [String: Any])
        #expect(r["status"] as? String == "applied")
        #expect(r["previous_key"] as? String == "obsidian")
        #expect(r["renamed_from"] as? String == "test")
        #expect((r["findings"] as? [[String: String]])?.first?["field"] == "text")
        #expect(r["reason"] == nil)
        #expect(ThemeProposal.Status.pendingApproval.rawValue == "pending_approval")
    }

    /// The handoff's names and the built-in keys match the MCP server's, read from its source.
    @Test func serverAgrees() throws {
        let server = URL(fileURLWithPath: #filePath).deletingLastPathComponent().deletingLastPathComponent()
            .deletingLastPathComponent().appendingPathComponent("integrations/sanduhr_mcp.py")
        let source = try String(contentsOf: server, encoding: .utf8)
        #expect(source.contains(#"THEME_REQUEST_FILE = "\#(ThemeProposal.requestFile)""#))
        #expect(source.contains(#"THEME_RESULT_FILE = "\#(ThemeProposal.resultFile)""#))
        let ids = ThemeRegistry.builtIn.map { "\"\($0.id)\"" }.joined(separator: ", ")
        #expect(source.contains("BUILT_IN_THEME_IDS = [\(ids)]"))
        #expect(source.contains("THEME_MAX_NAME = \(ThemeLint.maxNameLength)"))
        #expect(source.contains("THEME_MAX_DESCRIPTION = \(ThemeLint.maxDescriptionLength)"))
    }
}

@MainActor
@Suite("Theme proposal handoff")
struct ThemeProposalHandoffTests {
    final class Folder {
        let root: URL
        let paths: ThemeProposalHandoff.Paths
        init() {
            root = FileManager.default.temporaryDirectory.appendingPathComponent("sanduhr-theme-\(UUID().uuidString)")
            paths = ThemeProposalHandoff.Paths(support: root.appendingPathComponent("Sanduhr"),
                                               themes: root.appendingPathComponent("Sanduhr/themes"))
            try? FileManager.default.createDirectory(at: paths.support, withIntermediateDirectories: true)
        }
        deinit { try? FileManager.default.removeItem(at: root) }

        func result() -> [String: Any]? {
            guard let data = try? Data(contentsOf: paths.result),
                  let root = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else { return nil }
            return root["result"] as? [String: Any]
        }
        func status() -> String? { result()?["status"] as? String }
        func themeFiles() -> [String] {
            ((try? FileManager.default.contentsOfDirectory(atPath: paths.themes.path)) ?? []).sorted()
        }
    }

    private func handoff(_ f: Folder, direct: Bool = false) -> (ThemeProposalHandoff, Log) {
        let h = ThemeProposalHandoff(paths: f.paths, direct: { direct }, now: { t0 })
        let log = Log()
        h.activeKey = { "obsidian" }
        h.onSaved = { log.saved.append($0) }
        h.onApply = { log.applied.append($0) }
        h.onSuggestion = { log.suggested.append($0.id) }
        h.onDecided = { log.decided.append($0.id) }
        return (h, log)
    }

    final class Log {
        var saved: [String] = [], applied: [String] = [], suggested: [String] = [], decided: [String] = []
    }

    @Test func waitsForApprovalThenSavesAndApplies() throws {
        let f = Folder()
        let (h, log) = handoff(f)
        try requestJSON().write(to: f.paths.request)
        h.check()
        #expect(!FileManager.default.fileExists(atPath: f.paths.request.path))   // read once, removed
        #expect(h.pending?.id == "req1")
        #expect(log.suggested == ["req1"])
        #expect(f.status() == "pending_approval")
        #expect(f.result()?["key"] as? String == "test")
        #expect(f.themeFiles().isEmpty)   // nothing is saved before the user says so
        h.approve(apply: true)
        #expect(h.pending == nil)
        #expect(log.decided == ["req1"])
        #expect(f.themeFiles() == ["test.json"])
        #expect(log.saved == ["test"])
        #expect(log.applied == ["test"])
        #expect(f.status() == "applied")
        #expect(f.result()?["previous_key"] as? String == "obsidian")
        #expect(f.result()?["saved_path"] as? String == f.paths.file("test").path)
        let perms = try FileManager.default.attributesOfItem(atPath: f.paths.result.path)[.posixPermissions] as? Int
        #expect(perms == 0o600)
        let written = try Data(contentsOf: f.paths.file("test"))
        #expect(UserThemes.decodeTheme(written, id: "test")?.displayName == "Test")
    }

    @Test func saveWithoutApplying() {
        let f = Folder()
        let (h, log) = handoff(f)
        h.receive(requestJSON())
        h.approve(apply: false)
        #expect(f.status() == "saved")
        #expect(log.saved == ["test"])
        #expect(log.applied.isEmpty)
    }

    @Test func dismissSavesNothing() {
        let f = Folder()
        let (h, log) = handoff(f)
        h.receive(requestJSON())
        h.dismiss()
        #expect(h.pending == nil)
        #expect(f.themeFiles().isEmpty)
        #expect(f.status() == "rejected")
        #expect(f.result()?["reason"] as? String == "dismissed")
        #expect(log.decided == ["req1"])
    }

    @Test func directSavesAndAppliesAsAsked() {
        let f = Folder()
        let (h, log) = handoff(f, direct: true)
        h.receive(requestJSON())
        #expect(h.pending == nil)
        #expect(log.suggested.isEmpty)
        #expect(f.status() == "applied")
        #expect(log.applied == ["test"])
        h.receive(requestJSON(id: "req2", saveAs: "quiet", apply: false))
        #expect(f.status() == "saved")
        #expect(log.applied == ["test"])
        #expect(f.themeFiles() == ["quiet.json", "test.json"])
    }

    @Test func aUserThemeIsNeverOverwritten() throws {
        let f = Folder()
        try FileManager.default.createDirectory(at: f.paths.themes, withIntermediateDirectories: true)
        let mine = Data(#"{"name": "Mine"}"#.utf8)
        try mine.write(to: f.paths.file("test"))
        let (h, log) = handoff(f, direct: true)
        h.receive(requestJSON())
        #expect(try Data(contentsOf: f.paths.file("test")) == mine)
        #expect(f.themeFiles() == ["test-2.json", "test.json"])
        #expect(f.result()?["key"] as? String == "test-2")
        #expect(f.result()?["renamed_from"] as? String == "test")
        #expect(log.applied == ["test-2"])
        // The same theme again lands on its own file, not test-3.
        h.receive(requestJSON(id: "req2"))
        #expect(f.themeFiles() == ["test-2.json", "test.json"])
        #expect(f.result()?["key"] as? String == "test-2")
    }

    @Test func refusedAndExpiredRequests() {
        let f = Folder()
        let (h, log) = handoff(f, direct: true)
        var bad = cleanTheme()
        bad["text"] = "white"
        h.receive(requestJSON(theme: bad))
        #expect(f.status() == "rejected")
        #expect(f.result()?["reason"] as? String == "invalid_theme")
        #expect(((f.result()?["findings"] as? [[String: String]])?.first?["field"]) == "text")
        #expect(f.themeFiles().isEmpty)
        try? FileManager.default.removeItem(at: f.paths.result)
        h.receive(requestJSON(at: t0.addingTimeInterval(-3600)))
        #expect(f.result() == nil)
        #expect(log.applied.isEmpty)
    }

    @Test func aNewerSuggestionReplacesTheWaitingOne() {
        let f = Folder()
        let (h, log) = handoff(f)
        h.receive(requestJSON(id: "a"))
        h.receive(requestJSON(id: "b"))
        #expect(h.pending?.id == "b")
        #expect(log.decided == ["a"])
    }

    @Test func anOversizedRequestIsDropped() throws {
        let f = Folder()
        let (h, _) = handoff(f)
        try Data(count: ThemeProposal.maxRequestBytes + 1).write(to: f.paths.request)
        h.check()
        #expect(!FileManager.default.fileExists(atPath: f.paths.request.path))
        #expect(h.pending == nil)
        #expect(f.result() == nil)
    }

    @Test func bannerPlacementLines() throws {
        let p = try #require(proposal(requestJSON()))
        #expect(ThemeSuggestionBanner.placementLine(p, .init(key: "test", renamedFrom: nil, sameAsExisting: false)) == nil)
        #expect(ThemeSuggestionBanner.placementLine(p, .init(key: "test-2", renamedFrom: "test", sameAsExisting: false))?
            .contains("test-2.json") == true)
        #expect(ThemeSuggestionBanner.placementLine(p, nil) != nil)
    }
}
