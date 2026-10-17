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
}
