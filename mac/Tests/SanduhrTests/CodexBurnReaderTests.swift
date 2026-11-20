import Foundation
import Testing
@testable import Sanduhr

/// A zstd frame of raw (stored) blocks, written by hand: real zstd the app's decoder reads, with no
/// encoder in the test target. Single segment with a 4-byte content size; blocks up to 128 KB.
enum ZstdRawFrame {
    static func encode(_ data: Data) -> Data {
        var out = Data([0x28, 0xB5, 0x2F, 0xFD, 0xA0])
        withUnsafeBytes(of: UInt32(data.count).littleEndian) { out.append(contentsOf: $0) }
        let maxBlock = 128 * 1024
        var i = 0
        repeat {
            let n = min(maxBlock, data.count - i)
            let last: UInt32 = i + n >= data.count ? 1 : 0
            let header = UInt32(n) << 3 | last   // block type 0: raw
            out.append(contentsOf: [UInt8(header & 0xFF), UInt8((header >> 8) & 0xFF), UInt8((header >> 16) & 0xFF)])
            out.append(data.subdata(in: data.startIndex + i ..< data.startIndex + i + n))
            i += n
        } while i < data.count
        return out
    }
}

/// Codex's local burn (every-agents spec, section 3), the cases of Windows `CodexBurnSourceTests`
/// with the same inputs and numbers, run through `VaultIngester.ingestAgent`. Made-up rollouts in
/// temp folders only.
@Suite("Codex burn reader")
struct CodexBurnReaderTests {
    static let now = VaultTime.parse("2026-11-12T18:00:00+00:00")!
    static let api = "/work/Projects/api"

    // MARK: Lines, as Codex writes them

    static func meta(_ session: String, cwd: String = api) -> String {
        #"{"timestamp":"2026-11-10T16:00:00Z","type":"session_meta","payload":{"session_id":"\#(session)","cwd":"\#(cwd)","thread_source":"user"}}"#
    }

    static func turn(_ model: String, cwd: String? = nil) -> String {
        guard let cwd else {
            return #"{"timestamp":"2026-11-10T16:00:01Z","type":"turn_context","payload":{"model":"\#(model)"}}"#
        }
        return #"{"timestamp":"2026-11-10T16:00:01Z","type":"turn_context","payload":{"model":"\#(model)","cwd":"\#(cwd)"}}"#
    }

    static func tokens(_ ts: String, _ input: Int64, _ cached: Int64, _ cacheWrite: Int64, _ output: Int64,
                       lastInput: Int64 = 0, lastOutput: Int64 = 0) -> String {
        func usage(_ i: Int64, _ c: Int64, _ w: Int64, _ o: Int64) -> String {
            #"{"input_tokens":\#(i),"cached_input_tokens":\#(c),"cache_write_input_tokens":\#(w),"output_tokens":\#(o),"reasoning_output_tokens":0,"total_tokens":\#(i + o)}"#
        }
        return #"{"timestamp":"\#(ts)","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":\#(usage(input, cached, cacheWrite, output)),"last_token_usage":\#(usage(lastInput, 0, 0, lastOutput))},"rate_limits":null}}"#
    }

    static let nullInfo = #"{"timestamp":"2026-11-10T16:05:00Z","type":"event_msg","payload":{"type":"token_count","info":null,"rate_limits":null}}"#

    // MARK: Bed

    final class Bed {
        let vault = VaultBed()
        var home: URL { URL(fileURLWithPath: vault.home.at("codex-home"), isDirectory: true) }

        func cleanUp() { vault.cleanUp() }

        @discardableResult
        func rollout(_ name: String, _ lines: [String], compressed: Bool = false,
                     mtime: Date = CodexBurnReaderTests.now.addingTimeInterval(-600)) -> String {
            let dir = home.appendingPathComponent("sessions/2026/11/10", isDirectory: true).path
            try? FileManager.default.createDirectory(atPath: dir, withIntermediateDirectories: true)
            let path = (dir as NSString).appendingPathComponent(name + (compressed ? ".jsonl.zst" : ".jsonl"))
            let body = Data((lines.joined(separator: "\n") + "\n").utf8)
            FileManager.default.createFile(atPath: path, contents: compressed ? ZstdRawFrame.encode(body) : body)
            CCLogs.setModified(path, mtime)
            return path
        }

        func reader(cap: Int? = nil) -> CodexBurnReader {
            var r = CodexBurnReader(homes: [home])
            if let cap { r.capOf = { _ in cap } }
            return r
        }

        @discardableResult
        func ingest(cap: Int? = nil, now: Date = CodexBurnReaderTests.now) -> VaultIngestResult {
            vault.ingester().ingestAgent(reader(cap: cap), now: now)
        }

        /// The checkpoint key of a listed rollout (the listing's spelling of the path).
        func key(_ session: String = "rollout-a") throws -> String {
            VaultStore.pathKey(try #require(try reader().files().first { $0.sessionID == session }).path)
        }

        func shard() -> VaultSessionShard { vault.store.loadSessionShard("codex", "2026-11").1 }
        func row(_ session: String = "rollout-a") -> VaultSessionRow? { shard().sessions[session] }
    }

    // MARK: Session keys

    @Test(arguments: [("rollout-a.jsonl", "rollout-a"), ("rollout-a.jsonl.zst", "rollout-a"),
                      ("rollout-A.JSONL.ZST", "rollout-A")])
    func plainAndCompressedNamesShareASession(name: String, id: String) {
        #expect(CodexBurnReader.sessionID(of: name) == id)
    }

    // MARK: Counting

    @Test func tokensAreTotalDeltasMappedToFreshInputCacheAndOutput() throws {
        let b = Bed()
        defer { b.cleanUp() }
        b.rollout("rollout-a", [
            Self.meta("s1"), Self.turn("gpt-x"),
            Self.tokens("2026-11-10T16:01:00Z", 1000, 600, 50, 100),
            Self.tokens("2026-11-10T16:02:00Z", 1000, 600, 50, 100),   // a repeated reading counts zero
            Self.tokens("2026-11-10T16:03:00Z", 1500, 900, 50, 160),
        ])
        b.ingest()
        let row = try #require(b.row())
        // fresh input = 1500 - 900 = 600, output = 160: total 760
        #expect(row.total == 760)
        #expect(row.cacheTokens == VaultCacheTokens(read: 900, creation: 50))
        #expect(row.byModel == ["gpt-x": 760])
        #expect(row.eventCount == 2)
        #expect(row.projectName == "api")
        #expect(row.byDay["2026-11-10"]?.input == 600)
        #expect(row.byDay["2026-11-10"]?.output == 160)
    }

    @Test func aRepeatedReadingCountsOnce() throws {
        let b = Bed()
        defer { b.cleanUp() }
        let same = Self.tokens("2026-11-10T16:01:00Z", 1000, 0, 0, 100)
        b.rollout("rollout-a", [Self.meta("s1"), Self.turn("gpt-x"), same, same, same])
        b.ingest()
        let row = try #require(b.row())
        #expect(row.total == 1100)
        #expect(row.eventCount == 1)
    }

    @Test func aTotalThatDropsFallsBackToTheLastReading() throws {
        let b = Bed()
        defer { b.cleanUp() }
        b.rollout("rollout-a", [
            Self.meta("s1"), Self.turn("gpt-x"),
            Self.tokens("2026-11-10T16:01:00Z", 1000, 0, 0, 100),
            Self.tokens("2026-11-10T16:02:00Z", 200, 0, 0, 20, lastInput: 200, lastOutput: 20),
        ])
        b.ingest()
        #expect(b.row()?.total == Int64(1100 + 220))
    }

    @Test func aNullInfoCountsNothingAndIsNotABadLine() throws {
        let b = Bed()
        defer { b.cleanUp() }
        b.rollout("rollout-a", [Self.meta("s1"), Self.turn("gpt-x"), Self.nullInfo,
                                Self.tokens("2026-11-10T16:06:00Z", 10, 0, 0, 1)])
        b.ingest()
        let row = try #require(b.row())
        #expect(row.total == 11)
        #expect(row.skippedLines == 0)
    }

    @Test func aNewSegmentRestartsTheRunningTotal() throws {
        let b = Bed()
        defer { b.cleanUp() }
        b.rollout("rollout-a", [
            Self.meta("s1"), Self.turn("gpt-x"),
            Self.tokens("2026-11-10T16:01:00Z", 1000, 0, 0, 100),
            Self.nullInfo,
            Self.meta("s2"), Self.turn("gpt-y"),
            Self.tokens("2026-11-10T16:04:00Z", 300, 0, 0, 30),
        ])
        b.ingest()
        let row = try #require(b.row())
        #expect(row.total == 1100 + 330)
        #expect(row.byModel == ["gpt-x": 1100, "gpt-y": 330])
        #expect(b.shard().sessions.count == 1)   // segments fold into the file's row
    }

    @Test func modelNamesAreRecordedAsCodexReportsThem() throws {
        let b = Bed()
        defer { b.cleanUp() }
        b.rollout("rollout-a", [Self.meta("s1"), Self.turn("gpt-5.1-codex-MAX"),
                                Self.tokens("2026-11-10T16:01:00Z", 10, 0, 0, 1)])
        b.ingest()
        #expect(b.row()?.byModel == ["gpt-5.1-codex-MAX": 11])
    }

    @Test func eventsBeforeAnyTurnContextCountUnderNoModel() throws {
        let b = Bed()
        defer { b.cleanUp() }
        b.rollout("rollout-a", [Self.meta("s1"), Self.tokens("2026-11-10T16:01:00Z", 10, 0, 0, 1)])
        b.ingest()
        #expect(b.row()?.byModel == ["<none>": 11])
    }

    // MARK: Project

    @Test func theCwdInForceAtTheFirstCountedEventNamesTheProject() throws {
        let b = Bed()
        defer { b.cleanUp() }
        b.rollout("rollout-a", [Self.meta("s1"), Self.turn("gpt-x", cwd: "/work/Projects/web"),
                                Self.tokens("2026-11-10T16:01:00Z", 10, 0, 0, 1)])
        b.ingest()
        #expect(b.row()?.projectName == "web")
    }

    @Test func beforeAnyTurnContextTheSegmentsSessionMetaCwdIsTheProject() throws {
        let b = Bed()
        defer { b.cleanUp() }
        b.rollout("rollout-a", [Self.meta("s1", cwd: "/work/Projects/cli"), Self.turn("gpt-x"),
                                Self.tokens("2026-11-10T16:01:00Z", 10, 0, 0, 1)])
        b.ingest()
        #expect(b.row()?.projectName == "cli")
    }

    @Test func aCwdChangingMidSegmentLeavesTheFirstProject() throws {
        let b = Bed()
        defer { b.cleanUp() }
        b.rollout("rollout-a", [
            Self.meta("s1"), Self.turn("gpt-x", cwd: "/work/Projects/web"),
            Self.tokens("2026-11-10T16:01:00Z", 10, 0, 0, 1),
            Self.turn("gpt-x", cwd: "/work/Projects/docs"),
            Self.tokens("2026-11-10T16:02:00Z", 20, 0, 0, 2),
        ])
        b.ingest()
        let row = try #require(b.row())
        #expect(row.projectName == "web")
        #expect(row.total == 22)
        #expect(row.cwd == nil)   // names only: the path is never kept
    }

    // MARK: Days

    @Test func eachEventIsDatedByItsOwnTimestampInTheLocalDay() throws {
        let b = Bed()
        defer { b.cleanUp() }
        // US Central: 05:00Z on the 11th is 23:00 on the 10th; 07:00Z is 01:00 on the 11th.
        b.rollout("rollout-a", [
            Self.meta("s1"), Self.turn("gpt-x"),
            Self.tokens("2026-11-10T16:00:00Z", 100, 0, 0, 10),
            Self.tokens("2026-11-11T05:00:00Z", 200, 0, 0, 20),
            Self.tokens("2026-11-11T07:00:00Z", 300, 0, 0, 30),
        ], mtime: Self.now.addingTimeInterval(-600))
        b.ingest()
        let row = try #require(b.row())
        #expect(row.byDay.mapValues(\.total) == ["2026-11-10": 220, "2026-11-11": 110])
        let rollups = b.vault.store.loadRollupShard("codex", "2026-11").1
        #expect(rollups.days["2026-11-11"]?.total == 110)
        #expect(rollups.days["2026-11-10"]?.byProject.values.reduce(0, +) == 220)
    }

    // MARK: Resume

    @Test func aGrownRolloutResumesMidSegmentFromItsCheckpoint() throws {
        let b = Bed()
        defer { b.cleanUp() }
        let path = b.rollout("rollout-a", [Self.meta("s1"), Self.turn("gpt-x"),
                                           Self.tokens("2026-11-10T16:01:00Z", 1000, 0, 0, 100)])
        b.ingest()
        let key = try b.key()
        let state = try #require(b.vault.store.loadCheckpoints("codex").entries[key]?.parserState)
        #expect(state.contains("gpt-x"))

        CCLogs.append(path, Self.tokens("2026-11-10T16:02:00Z", 1200, 0, 0, 120) + "\n")
        CCLogs.setModified(path, Self.now.addingTimeInterval(-300))
        let r = b.ingest()
        #expect(r.filesTailParsed == 1)
        #expect(r.filesFullParsed == 0)
        let row = try #require(b.row())
        #expect(row.total == 1320)   // 1100 + the delta 220, not 1100 + 1320
        #expect(row.byModel == ["gpt-x": 1320])   // the model came back from the saved state
        #expect(row.eventCount == 2)
    }

    @Test func aTornLastLineWaitsForTheNextPass() throws {
        let b = Bed()
        defer { b.cleanUp() }
        let path = b.rollout("rollout-a", [Self.meta("s1"), Self.turn("gpt-x"),
                                           Self.tokens("2026-11-10T16:01:00Z", 1000, 0, 0, 100)])
        let next = Self.tokens("2026-11-10T16:02:00Z", 1200, 0, 0, 120)
        CCLogs.append(path, String(next.prefix(40)))
        CCLogs.setModified(path, Self.now.addingTimeInterval(-600))
        b.ingest()
        #expect(b.row()?.total == 1100)
        #expect(b.row()?.skippedLines == 0)

        CCLogs.append(path, String(next.dropFirst(40)) + "\n")
        CCLogs.setModified(path, Self.now.addingTimeInterval(-300))
        b.ingest()
        #expect(b.row()?.total == 1320)
    }

    // MARK: Compressed rollouts

    @Test func thePlainAndCompressedCopiesGiveOneIdenticalRow() throws {
        let lines = [Self.meta("s1"), Self.turn("gpt-x"),
                     Self.tokens("2026-11-10T16:01:00Z", 1000, 400, 7, 100),
                     Self.tokens("2026-11-10T16:09:00Z", 1500, 500, 9, 150)]
        let plain = Bed()
        let packed = Bed()
        defer { plain.cleanUp(); packed.cleanUp() }
        plain.rollout("rollout-a", lines)
        packed.rollout("rollout-a", lines, compressed: true)
        plain.ingest()
        packed.ingest()
        let a = try #require(plain.row())
        #expect(a.total == 1500 - 500 + 150)
        #expect(packed.row() == a)
        #expect(packed.vault.store.loadRollupShard("codex", "2026-11").1 == plain.vault.store.loadRollupShard("codex", "2026-11").1)
    }

    @Test func aCompressedCopyReplacesItsPlainRowAndNothingCountsTwice() throws {
        let b = Bed()
        defer { b.cleanUp() }
        let first = [Self.meta("s1"), Self.turn("gpt-x"), Self.tokens("2026-11-10T16:01:00Z", 1000, 0, 0, 100)]
        b.rollout("rollout-a", first)
        b.ingest()
        #expect(b.row()?.total == 1100)

        // The compressed copy carries one more reading: 1320 means it replaced the row, 2420 added to it.
        b.rollout("rollout-a", first + [Self.tokens("2026-11-10T16:02:00Z", 1200, 0, 0, 120)], compressed: true,
                  mtime: Self.now.addingTimeInterval(-500))
        let r = b.ingest()
        #expect(r.filesFailed == 0)
        #expect(r.filesFullParsed == 1)
        #expect(b.shard().sessions.count == 1)
        #expect(b.row()?.total == 1320)
        #expect(b.vault.store.loadRollupShard("codex", "2026-11").1.days["2026-11-10"]?.total == 1320)
    }

    @Test func anUnchangedCompressedRolloutIsSkippedAndSealedOnceQuiet() throws {
        let b = Bed()
        defer { b.cleanUp() }
        b.rollout("rollout-a", [Self.meta("s1"), Self.turn("gpt-x"),
                                Self.tokens("2026-11-10T16:01:00Z", 10, 0, 0, 1)], compressed: true)
        #expect(b.ingest().filesFullParsed == 1)
        #expect(b.ingest().filesSkipped == 1)
        let key = try b.key()
        #expect(b.vault.store.loadCheckpoints("codex").entries[key]?.tailGuard == "")
        #expect(b.vault.store.loadCheckpoints("codex").entries[key]?.parserState != nil)
        // An hour quiet: one last whole read seals it, then it is skipped for good.
        let later = Self.now.addingTimeInterval(3 * 3600)
        #expect(b.ingest(now: later).filesFullParsed == 1)
        #expect(b.vault.store.loadCheckpoints("codex").entries[key]?.sealed == true)
        #expect(b.ingest(now: later).filesSkipped == 1)
        #expect(b.row()?.total == 11)
    }

    @Test(arguments: [(Int64(0), Int64(64 << 20)), (Int64(1 << 20), Int64(64 << 20)), (Int64(2 << 20), Int64(128 << 20))])
    func theDecompressedCapIs64MBOr64TimesTheFile(compressed: Int64, cap: Int64) {
        #expect(CodexBurnReader.decompressedCap(compressed) == Int(cap))
    }

    @Test func aLongRealSessionFitsUnderTheCap() {
        // Windows' live smoke, 2026-11-17: a 70.5 MB rollout expanded to 174.4 MB.
        #expect(CodexBurnReader.decompressedCap(Int64(70.5 * 1024 * 1024)) > Int(174.4 * 1024 * 1024))
        #expect(CodexBurnReader.decompressedCap(.max) == Int.max)
    }

    @Test func aRolloutOverTheCapFailsAlone() throws {
        let b = Bed()
        defer { b.cleanUp() }
        b.rollout("rollout-big", [Self.meta("s1"), Self.tokens("2026-11-10T16:01:00Z", 1, 0, 0, 1)], compressed: true)
        b.rollout("rollout-ok", [Self.meta("s2"), Self.tokens("2026-11-10T16:01:00Z", 1, 0, 0, 1)])
        let r = b.ingest(cap: 10)
        #expect(r.filesFailed == 1)
        #expect(r.rootsAborted == 0)
        #expect(b.row("rollout-ok")?.total == 2)
        #expect(b.row("rollout-big") == nil)
    }

    @Test func aBadCompressedFrameFailsAloneAndTheOthersLand() throws {
        let b = Bed()
        defer { b.cleanUp() }
        let bad = b.rollout("rollout-bad", ["x"], compressed: true)
        FileManager.default.createFile(atPath: bad, contents: Data("not zstd at all".utf8))
        CCLogs.setModified(bad, Self.now.addingTimeInterval(-600))
        b.rollout("rollout-good", [Self.meta("s1"), Self.tokens("2026-11-10T16:01:00Z", 1, 0, 0, 1)])
        let r = b.ingest()
        #expect(r.filesFailed == 1)
        #expect(b.row("rollout-good") != nil)
        #expect(b.row("rollout-bad") == nil)
    }

    @Test func zstdHandsItsOutputOnInPiecesUnderTheCap() throws {
        let text = Data(String(repeating: "a line of a rollout\n", count: 20_000).utf8)
        let packed = ZstdRawFrame.encode(text)
        #expect(try Zstd.decompress(packed) == text)
        var pieces = 0
        var joined = Data()
        try Zstd.decompress(packed, cap: text.count) { pieces += 1; joined.append(contentsOf: $0) }
        #expect(joined == text)
        #expect(pieces > 1)
        #expect(throws: Zstd.Failure.capExceeded(cap: text.count - 1)) {
            try Zstd.decompress(packed, cap: text.count - 1) { _ in }
        }
    }

    // MARK: Listing

    @Test func everyRolloutUnderEachHomesSessionsIsListedAndNothingElse() throws {
        let b = Bed()
        defer { b.cleanUp() }
        b.rollout("rollout-a", [Self.meta("s1")])
        b.rollout("rollout-b", [Self.meta("s2")], compressed: true)
        let other = URL(fileURLWithPath: b.vault.home.at("codex-two"), isDirectory: true)
        let deep = other.appendingPathComponent("sessions/2025/01/02", isDirectory: true)
        try FileManager.default.createDirectory(at: deep, withIntermediateDirectories: true)
        FileManager.default.createFile(atPath: deep.appendingPathComponent("rollout-c.jsonl").path, contents: Data())
        FileManager.default.createFile(atPath: deep.appendingPathComponent("notes.jsonl").path, contents: Data())
        FileManager.default.createFile(atPath: other.appendingPathComponent("rollout-outside.jsonl").path, contents: Data())

        let files = try CodexBurnReader(homes: [b.home, other]).files()
        #expect(Set(files.map(\.sessionID)) == ["rollout-a", "rollout-b", "rollout-c"])
        #expect(files.first { $0.sessionID == "rollout-b" }?.compressed == true)
        #expect(files.first { $0.sessionID == "rollout-a" }?.compressed == false)
        #expect(files.allSatisfy { $0.cwd == nil })
        #expect(try CodexBurnReader(homes: []).files().isEmpty)
    }

    // MARK: Parser privacy and damage

    @Test func theParserNeverReadsMessageText() {
        var p = CodexLineParser()
        let (event, bad) = p.parse(#"{"timestamp":"2026-11-10T16:00:00Z","type":"response_item","payload":{"type":"message","role":"user","content":[{"type":"input_text","text":"secret token_count"}]}}"#)
        #expect(event == nil)
        #expect(!bad)
        #expect(!(p.savedState() ?? "").contains("secret"))
    }

    @Test func savedStateCarriesNoPathAndNoSessionId() {
        var p = CodexLineParser()
        _ = p.parse(Self.meta("sess-secret-id", cwd: "/work/Projects/api"))
        _ = p.parse(Self.tokens("2026-11-10T16:01:00Z", 10, 0, 0, 1))
        let state = p.savedState() ?? ""
        #expect(!state.contains("Projects"))
        #expect(!state.contains("sess-secret-id"))
        #expect(CodexLineParser(state: #"{"last":null}"#).savedState() != nil)
    }

    @Test func anUnreadableStateRestartsTheSegmentTotal() {
        var p = CodexLineParser(state: "not json")
        let (event, _) = p.parse(Self.tokens("2026-11-10T16:01:00Z", 10, 0, 0, 1))
        #expect(event?.input == 10)
        #expect(event?.output == 1)
    }

    @Test func aMalformedRecordIsABadLine() {
        var p = CodexLineParser()
        #expect(p.parse(#"{"type":"token_count", broken"#).bad)
        #expect(p.parse(#"{"type":"session_meta","payload":"x"}"#).bad)
        #expect(p.parse(#"["token_count"]"#).bad)
    }
}
