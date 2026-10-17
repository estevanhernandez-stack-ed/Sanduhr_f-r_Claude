import Foundation
import Testing
@testable import Sanduhr

/// The one owner-only atomic write every seat file goes through. Temp folders only.
@Suite("Handoff files")
struct HandoffFilesTests {
    private func scratch() throws -> URL {
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent("sanduhr-handoff-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        return dir
    }

    private func mode(_ url: URL) throws -> Int? {
        try FileManager.default.attributesOfItem(atPath: url.path)[.posixPermissions] as? Int
    }

    @Test func writesTheBytesOwnerOnlyAndMakesTheFolder() throws {
        let dir = try scratch()
        defer { try? FileManager.default.removeItem(at: dir) }
        let url = dir.appendingPathComponent("nested/deeper/result.json")
        let bytes = Data("{\"ok\":true,\"n\":1}".utf8)
        try HandoffFiles.writeOwnerOnlyOrThrow(bytes, to: url)
        #expect(try Data(contentsOf: url) == bytes)
        #expect(try mode(url) == 0o600)
        // No temporary file is left beside it.
        #expect(try FileManager.default.contentsOfDirectory(atPath: url.deletingLastPathComponent().path) == ["result.json"])
    }

    @Test func replacesAnExistingFileWhole() throws {
        let dir = try scratch()
        defer { try? FileManager.default.removeItem(at: dir) }
        let url = dir.appendingPathComponent("state.json")
        try HandoffFiles.writeOwnerOnlyOrThrow(Data(String(repeating: "a", count: 4096).utf8), to: url)
        // Widened by hand: the second write is a fresh 0600 file, not the old one patched.
        try FileManager.default.setAttributes([.posixPermissions: 0o644], ofItemAtPath: url.path)
        let second = Data("b".utf8)
        try HandoffFiles.writeOwnerOnlyOrThrow(second, to: url)
        #expect(try Data(contentsOf: url) == second)
        #expect(try mode(url) == 0o600)
        #expect(try FileManager.default.contentsOfDirectory(atPath: dir.path) == ["state.json"])
    }

    @Test func throwsWhenTheParentIsAFile() throws {
        let dir = try scratch()
        defer { try? FileManager.default.removeItem(at: dir) }
        let blocker = dir.appendingPathComponent("blocker")
        try Data("x".utf8).write(to: blocker)
        let url = blocker.appendingPathComponent("child.json")
        #expect(throws: (any Error).self) {
            try HandoffFiles.writeOwnerOnlyOrThrow(Data("{}".utf8), to: url)
        }
        #expect(try Data(contentsOf: blocker) == Data("x".utf8))
        #expect(try FileManager.default.contentsOfDirectory(atPath: dir.path) == ["blocker"])
    }

    @Test func writesNothingAndDoesNotThrowWhileResetting() throws {
        let dir = try scratch()
        defer { try? FileManager.default.removeItem(at: dir) }
        let url = dir.appendingPathComponent("gone/kept.json")
        try ResetGate.$testOverride.withValue(true) {
            try HandoffFiles.writeOwnerOnlyOrThrow(Data("{}".utf8), to: url)
        }
        #expect(try FileManager.default.contentsOfDirectory(atPath: dir.path).isEmpty)
    }

    // MARK: - The result file

    private func root(_ data: Data) throws -> [String: Any] {
        try #require(try JSONSerialization.jsonObject(with: data) as? [String: Any])
    }

    @Test func resultCarriesTheFieldsThatAreThereAndSkipsNil() throws {
        let t0 = Date(timeIntervalSince1970: 1_760_000_000.25)
        let name: String? = "sanduhr-tip", version: String? = nil, saved: Int? = 3
        let data = HandoffFiles.resultJSON(id: "r1", status: "pending_review",
                                           fields: [("name", name), ("version", version), ("looks_saved", saved), ("style_on", false)],
                                           now: t0)
        let doc = try root(data)
        #expect(doc["id"] as? String == "r1")
        #expect(doc["completed_at"] as? String == "2025-10-09T08:53:20.250Z")
        let result = try #require(doc["result"] as? [String: Any])
        #expect(result["status"] as? String == "pending_review")
        #expect(result["name"] as? String == "sanduhr-tip")
        #expect(result["looks_saved"] as? Int == 3)
        #expect(result["style_on"] as? Bool == false)
        #expect(result["version"] == nil && result["reasons"] == nil)
        // Keys sorted, so two writes of the same answer are the same bytes.
        #expect(String(decoding: data, as: UTF8.self).hasPrefix(#"{"completed_at":"2025-10-09T08:53:20.250Z","id":"r1","result":{"looks_saved":3,"name":"sanduhr-tip","status":"pending_review","style_on":false}}"#))
    }

    @Test func resultWritesReasonsOnlyWhenThereAreAny() throws {
        let t0 = Date(timeIntervalSince1970: 1_760_000_000)
        let refused = try root(HandoffFiles.resultJSON(id: "r2", status: "rejected", fields: [("reason", "invalid_mod")],
                                                       reasons: ["a", "b"], now: t0))
        let result = try #require(refused["result"] as? [String: Any])
        #expect(result["reason"] as? String == "invalid_mod")
        #expect(result["reasons"] as? [String] == ["a", "b"])
        let clean = try root(HandoffFiles.resultJSON(id: "r3", status: "applied", now: t0))
        #expect((clean["result"] as? [String: Any])?.keys.sorted() == ["status"])
    }
}
