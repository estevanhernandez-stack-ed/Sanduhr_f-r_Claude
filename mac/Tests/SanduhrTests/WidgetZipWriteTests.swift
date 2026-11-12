import Foundation
import Testing
@testable import Sanduhr

/// Export a pack (spec 21): `WidgetZip.write` is the reader's inverse and deterministic.
@Suite("Widget zip writer (2.43.0)")
struct WidgetZipWriteTests {
    private static func sample() -> [String: Data] {
        var blob = [UInt8]()
        for i in 0..<4096 { blob.append(UInt8((i * 31 + 7) & 0xFF)) }
        return [
            "pack.json": Data("{\"id\":\"x\"}".utf8),
            "LICENSE": Data("MIT".utf8),
            "art/sprite.png": Data(blob),
            "art/deep/empty.bin": Data(),
            "fonts/a.ttf": Data((0..<300).map { UInt8($0 & 0xFF) }),
        ]
    }

    private static func written(_ files: [String: Data]) throws -> Data {
        try WidgetZip.write(files).get()
    }

    private static func refusal(_ files: [String: Data], sourceLocation: SourceLocation = #_sourceLocation) -> String {
        switch WidgetZip.write(files) {
        case .success:
            Issue.record("the write was accepted", sourceLocation: sourceLocation)
            return ""
        case .failure(let e):
            #expect(!e.message.isEmpty, sourceLocation: sourceLocation)
            return e.message
        }
    }

    @Test(arguments: [false, true])
    func writeThenReadReturnsTheSameFiles(fromUser: Bool) throws {
        let files = Self.sample()
        let back = try WidgetZip.read(try Self.written(files), fromUser: fromUser).get()
        #expect(back == files)
    }

    @Test func twoWritesAreByteIdenticalWhateverTheInsertionOrder() throws {
        let files = Self.sample()
        var reversed: [String: Data] = [:]
        for k in files.keys.sorted().reversed() { reversed[k] = files[k] }
        let a = try Self.written(files), b = try Self.written(files), c = try Self.written(reversed)
        #expect(a == b)
        #expect(a == c)
    }

    @Test func theSystemUnzipAcceptsTheOutput() throws {
        let url = FileManager.default.temporaryDirectory.appendingPathComponent("zipwrite-\(UUID().uuidString).zip")
        try Self.written(Self.sample()).write(to: url)
        defer { try? FileManager.default.removeItem(at: url) }
        let p = Process()
        p.executableURL = URL(fileURLWithPath: "/usr/bin/unzip")
        p.arguments = ["-t", url.path]
        p.standardOutput = Pipe()
        p.standardError = Pipe()
        try p.run()
        p.waitUntilExit()
        #expect(p.terminationStatus == 0)
    }

    @Test(arguments: ["../x", "/abs", "a//b", "a\\b", "C:x", "./x", "a/../b", "has space", "caf\u{e9}"])
    func anUnsafeNameIsRefused(name: String) {
        _ = Self.refusal([name: Data([1]), "pack.json": Data([2])])
    }

    @Test func namesThatDifferOnlyInCaseAreRefused() {
        _ = Self.refusal(["A.txt": Data([1]), "a.txt": Data([2])])
    }

    @Test func anEmptySetIsRefused() {
        _ = Self.refusal([:])
    }

    @Test func tooManyEntriesAreRefused() {
        var files: [String: Data] = [:]
        for i in 0...WidgetZip.maxEntries { files["f\(i).txt"] = Data([UInt8(i & 0xFF)]) }
        #expect(files.count == WidgetZip.maxEntries + 1)
        _ = Self.refusal(files)
        files.removeValue(forKey: "f0.txt")
        #expect((try? WidgetZip.write(files).get()) != nil)
    }

    @Test func aPackOverTheZipLimitIsRefused() {
        let chunk = Data(count: WidgetZip.maxEntryBytes)
        var files: [String: Data] = [:]
        var n = 0
        while files.count * WidgetZip.maxEntryBytes <= WidgetZip.maxZipBytes {
            files["f\(n).bin"] = chunk
            n += 1
        }
        _ = Self.refusal(files)
        _ = Self.refusal(["big.bin": Data(count: WidgetZip.maxEntryBytes + 1)])
    }

    @Test func aGoodPackRoundTripsAndStillValidatesStrict() throws {
        let pack = WidgetPackFixtures.load("good", "good-palette")
        let files = try WidgetZip.read(try Self.written(pack)).get()
        #expect(files == pack)
        let verdict = PackValidator.validate(files, folderName: "good-palette", strict: true)
        #expect(verdict.ok, "\(verdict.errors.joined(separator: " | "))")
    }
}
