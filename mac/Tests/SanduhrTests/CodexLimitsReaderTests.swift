import Foundation
import Testing
@testable import Sanduhr

/// Codex limits from Codex's own rollouts (item 3), ported from Windows' CodexLimitsTests and
/// CodexLimitsReaderTests. Made-up rollouts in temp homes; nothing reads the real ~/.codex. The
/// `.zst` fixture is the checked-in plain fixture compressed once with the zstd tool.

/// One temp folder per test, removed at deinit.
final class CodexTempDir {
    let url: URL
    init() {
        url = FileManager.default.temporaryDirectory
            .appendingPathComponent("sanduhr-codex-\(UUID().uuidString)", isDirectory: true)
        try? FileManager.default.createDirectory(at: url, withIntermediateDirectories: true)
    }
    deinit { try? FileManager.default.removeItem(at: url) }
}

enum CodexFixture {
    static func event(_ ts: String, five: Double, week: Double,
                      fiveResets: Int64 = 1_792_721_664, weekResets: Int64 = 1_793_308_464) -> String {
        #"{"timestamp":"\#(ts)","type":"event_msg","payload":{"type":"token_count","rate_limits":{"limit_id":"codex","primary":{"used_percent":\#(five),"window_minutes":300,"resets_at":\#(fiveResets)},"secondary":{"used_percent":\#(week),"window_minutes":10080,"resets_at":\#(weekResets)},"plan_type":"plus"}}}"#
    }

    static let premium = #"{"timestamp":"2026-10-22T23:59:00Z","payload":{"type":"token_count","rate_limits":{"limit_id":"premium","primary":null,"secondary":null}}}"#

    /// Writes a rollout under `home/sessions/2026/10/<day>/`, with its mtime when given.
    @discardableResult
    static func rollout(_ home: URL, day: String, name: String, body: String, zst: Data? = nil,
                        mtime: Date? = nil) throws -> URL {
        let dir = home.appendingPathComponent("sessions/2026/10/\(day)", isDirectory: true)
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        let url = dir.appendingPathComponent(name + (zst != nil ? ".jsonl.zst" : ".jsonl"))
        try (zst ?? Data(body.utf8)).write(to: url)
        if let mtime { try FileManager.default.setAttributes([.modificationDate: mtime], ofItemAtPath: url.path) }
        return url
    }

    static func bundled(_ name: String) throws -> Data {
        let url = try #require(Bundle.module.url(forResource: "Fixtures/codex/\(name)", withExtension: nil))
        return try Data(contentsOf: url)
    }

    static let fixtureName = "rollout-2026-10-22T20-00-00-fixture"

    static func date(_ s: String) -> Date { ISO8601.parse(s)! }
}

/// The real disk with every call counted: which files were opened, how, and every folder asked about.
final class SpyCodexFS: CodexFileSystem {
    let inner = CodexDisk()
    var opened: [URL] = []
    var reads: [(name: String, offset: UInt64, length: Int)] = []
    var readAlls = 0
    var directoryChecks: [URL] = []
    var listings: [URL] = []
    /// File names whose open throws (a lock, a permission).
    var failing: Set<String> = []

    func isDirectory(_ url: URL) -> Bool { directoryChecks.append(url); return inner.isDirectory(url) }
    func rollouts(under sessions: URL) -> [CodexRolloutFile] { listings.append(sessions); return inner.rollouts(under: sessions) }
    func read(_ url: URL, offset: UInt64, length: Int) throws -> Data {
        opened.append(url)
        reads.append((url.lastPathComponent, offset, length))
        if failing.contains(url.lastPathComponent) { throw CocoaError(.fileReadNoPermission) }
        return try inner.read(url, offset: offset, length: length)
    }
    func readAll(_ url: URL) throws -> Data {
        opened.append(url)
        readAlls += 1
        if failing.contains(url.lastPathComponent) { throw CocoaError(.fileReadNoPermission) }
        return try inner.readAll(url)
    }
    var touchedAnything: Bool { !opened.isEmpty || !directoryChecks.isEmpty || !listings.isEmpty }
}

@Suite("Codex limits: parsing a rollout line")
struct CodexLimitsParseTests {
    static let good = #"{"timestamp":"2026-10-22T23:48:09.060Z","type":"event_msg","payload":{"type":"token_count","info":null,"rate_limits":{"limit_id":"codex","limit_name":null,"primary":{"used_percent":99.0,"window_minutes":300,"resets_at":1792721664},"secondary":{"used_percent":16.0,"window_minutes":10080,"resets_at":1793308464},"credits":{"has_credits":false,"unlimited":false,"balance":"0"},"plan_type":"plus"}}}"#

    @Test func parsesBothWindowsAndThePlan() throws {
        let r = try #require(CodexLimits.parse(line: Self.good))
        #expect(r.asOf == CodexFixture.date("2026-10-22T23:48:09.060Z"))
        #expect(r.primary?.usedPercent == 99)
        #expect(r.primary?.windowMinutes == 300)
        #expect(r.primary?.resetsAt == Date(timeIntervalSince1970: 1_792_721_664))
        #expect(r.secondary?.windowMinutes == 10080)
        #expect(r.planType == "plus")
    }

    @Test func aPremiumEventWithNoWindowsIsNotAReading() {
        #expect(CodexLimits.parse(line: CodexFixture.premium) == nil)
    }

    @Test(arguments: [
        "",
        "{",
        #"{"timestamp":"2026-10-22T23:48:09Z","type":"response_item","payload":{"type":"message"}}"#,
        #"{"type":"event_msg","payload":{"type":"token_count","rate_limits":{"primary":{"used_percent":5,"window_minutes":300}}}}"#,
        #"{"timestamp":"not a date","payload":{"type":"token_count","rate_limits":{"primary":{"used_percent":5,"window_minutes":300}}}}"#,
    ])
    func anythingElseIsNotAReading(_ line: String) {
        #expect(CodexLimits.parse(line: line) == nil)
    }

    /// Codex changing its format: a field the reader needs is missing, so there are no limits, never a crash.
    @Test(arguments: [
        #"{"timestamp":"2026-10-22T23:48:09Z","payload":{"type":"token_count","rate_limits":{"primary":{"window_minutes":300}}}}"#,
        #"{"timestamp":"2026-10-22T23:48:09Z","payload":{"type":"token_count","rate_limits":{"primary":{"used_percent":5}}}}"#,
        #"{"timestamp":"2026-10-22T23:48:09Z","payload":{"type":"token_count"}}"#,
        #"{"timestamp":"2026-10-22T23:48:09Z","payload":{"type":"token_count","rate_limits":{"primary":{"used_percent":12.5,"window_minutes":"300"}}}}"#,
        #"{"timestamp":"2026-10-22T23:48:09Z","payload":{"type":"token_count","rate_limits":{"primary":{"used_percent":true,"window_minutes":300}}}}"#,
    ])
    func aMissingOrMistypedFieldMeansNoLimits(_ line: String) {
        #expect(CodexLimits.parse(line: line) == nil)
    }

    @Test func aWindowWithoutAResetStillParses() throws {
        let r = try #require(CodexLimits.parse(line: #"{"timestamp":"2026-10-22T23:48:09Z","payload":{"type":"token_count","rate_limits":{"primary":{"used_percent":12.5,"window_minutes":300}}}}"#))
        #expect(r.primary?.resetsAt == nil)
        #expect(r.secondary == nil)
    }

    @Test func aNullOrOutOfRangeResetParsesWithNoReset() throws {
        let null = try #require(CodexLimits.parse(line: #"{"timestamp":"2026-10-22T23:48:09Z","payload":{"type":"token_count","rate_limits":{"primary":{"used_percent":12.5,"window_minutes":300,"resets_at":null}}}}"#))
        #expect(null.primary?.resetsAt == nil)
        let far = try #require(CodexLimits.parse(line: #"{"timestamp":"2026-10-22T23:48:09Z","payload":{"type":"token_count","rate_limits":{"primary":{"used_percent":12.5,"window_minutes":300,"resets_at":99999999999999}}}}"#))
        #expect(far.primary?.resetsAt == nil)
    }

    @Test(arguments: [(150, 100.0), (-5, 0.0)])
    func usedPercentClampsToZeroThroughOneHundred(_ raw: Int, _ expected: Double) throws {
        let r = try #require(CodexLimits.parse(line: #"{"timestamp":"2026-10-22T23:48:09Z","payload":{"type":"token_count","rate_limits":{"primary":{"used_percent":\#(raw),"window_minutes":300}}}}"#))
        #expect(r.primary?.usedPercent == expected)
    }
}

@Suite("Codex limits: tiers and freshness")
struct CodexLimitsTierTests {
    private let asOf = CodexFixture.date("2026-10-22T23:00:00Z")

    @Test func mapsByWindowLengthNotPosition() {
        let swapped = CodexLimitsReading(asOf: asOf, primary: CodexWindow(usedPercent: 16, windowMinutes: 10080),
                                         secondary: CodexWindow(usedPercent: 99, windowMinutes: 300))
        let usage = CodexLimits.toUsage(swapped, now: asOf)
        #expect(usage.tiers[.fiveHour]?.utilization == 99)
        #expect(usage.tiers[.sevenDay]?.utilization == 16)
        #expect(usage.tiers.count == 2)
        #expect(usage.extraUsage == nil)
    }

    @Test func anOddWindowIsDroppedAndReported() {
        let odd = CodexLimitsReading(asOf: asOf, primary: CodexWindow(usedPercent: 40, windowMinutes: 1440),
                                     secondary: CodexWindow(usedPercent: 10, windowMinutes: 10080))
        var seen: [Int] = []
        let usage = CodexLimits.toUsage(odd, now: asOf) { seen.append($0) }
        #expect(usage.tiers.keys.sorted { $0.rawValue < $1.rawValue } == [.sevenDay])
        #expect(seen == [1440])
        #expect(CodexLimits.freshTiers(odd, now: asOf) == [.sevenDay])
    }

    @Test func aPassedResetReadsZeroAndKeepsItsResetTime() {
        let reset = CodexFixture.date("2026-10-23T02:00:00Z")
        let r = CodexLimitsReading(asOf: asOf, primary: CodexWindow(usedPercent: 99, windowMinutes: 300, resetsAt: reset))
        let usage = CodexLimits.toUsage(r, now: reset.addingTimeInterval(60))
        #expect(usage.tiers[.fiveHour]?.utilization == 0)
        #expect(usage.tiers[.fiveHour]?.resetsAt == "2026-10-23T02:00:00Z")
        #expect(parseISO(usage.tiers[.fiveHour]?.resetsAt) == reset)
    }

    @Test func freshForThirtyMinutesOrAfterAnyPassedReset() {
        let r = CodexLimitsReading(asOf: asOf, primary: CodexWindow(usedPercent: 10, windowMinutes: 300, resetsAt: asOf.addingTimeInterval(3 * 3600)))
        #expect(CodexLimits.isFresh(r, now: asOf.addingTimeInterval(30 * 60)))
        #expect(!CodexLimits.isFresh(r, now: asOf.addingTimeInterval(31 * 60)))
        #expect(CodexLimits.isFresh(r, now: asOf.addingTimeInterval(3 * 3600 + 1)))
    }

    @Test func freshnessIsPerTier() {
        let now = asOf.addingTimeInterval(6 * 3600)
        let r = CodexLimitsReading(asOf: asOf,
                                   primary: CodexWindow(usedPercent: 99, windowMinutes: 300, resetsAt: asOf.addingTimeInterval(5 * 3600)),
                                   secondary: CodexWindow(usedPercent: 85, windowMinutes: 10080, resetsAt: now.addingTimeInterval(3 * 86400)))
        #expect(CodexLimits.freshTiers(r, now: now) == [.fiveHour])
        #expect(CodexLimits.freshTiers(r, now: asOf.addingTimeInterval(600)) == [.fiveHour, .sevenDay])
        let idle = CodexLimitsReading(asOf: asOf, primary: CodexWindow(usedPercent: 99, windowMinutes: 300, resetsAt: now.addingTimeInterval(3600)))
        #expect(CodexLimits.freshTiers(idle, now: now).isEmpty)
    }
}

@Suite("Codex limits: reading the rollouts")
struct CodexLimitsReaderTests {
    private func reader(_ homes: URL..., fs: CodexFileSystem = CodexDisk(), cap: Int = Zstd.defaultCap) -> CodexLimitsReader {
        CodexLimitsReader(homes: homes, fileSystem: fs, zstdCap: cap)
    }

    @Test func noHomeIsNotInstalled() {
        #expect(CodexLimitsReader(homes: []).read().status == .notInstalled)
    }

    @Test func aHomeWithNoRolloutsHasNoLimits() {
        let t = CodexTempDir()
        #expect(reader(t.url).read().status == .noLimits)
    }

    @Test func plainAndZstGiveTheSameLimits() throws {
        let plainHome = CodexTempDir(), zstHome = CodexTempDir()
        let text = String(decoding: try CodexFixture.bundled(CodexFixture.fixtureName + ".jsonl"), as: UTF8.self)
        try CodexFixture.rollout(plainHome.url, day: "22", name: CodexFixture.fixtureName, body: text)
        try CodexFixture.rollout(zstHome.url, day: "22", name: CodexFixture.fixtureName, body: "",
                                 zst: try CodexFixture.bundled(CodexFixture.fixtureName + ".jsonl.zst"))
        let plain = reader(plainHome.url).read()
        let packed = reader(zstHome.url).read()
        #expect(plain.status == .ok)
        #expect(plain == packed)
        // The newest qualifying event, past the trailing premium event.
        #expect(plain.reading?.primary?.usedPercent == 64)
        #expect(plain.reading?.secondary?.usedPercent == 21.5)
        #expect(plain.reading?.asOf == CodexFixture.date("2026-10-22T23:48:09.060Z"))
    }

    @Test func malformedLinesAndATornLastLineAreSkipped() throws {
        let t = CodexTempDir()
        let body = CodexFixture.event("2026-10-22T23:48:00Z", five: 40, week: 10) + "\n"
            + "not json at all\n{\"broken\": \n" + CodexFixture.premium + "\n"
            + #"{"timestamp":"2026-10-22T23:59:30Z","payl"#
        try CodexFixture.rollout(t.url, day: "22", name: "rollout-a", body: body)
        let r = reader(t.url).read()
        #expect(r.status == .ok)
        #expect(r.reading?.primary?.usedPercent == 40)
    }

    @Test func crlfLinesParse() throws {
        let t = CodexTempDir()
        try CodexFixture.rollout(t.url, day: "22", name: "rollout-a",
                                 body: CodexFixture.event("2026-10-22T23:48:00Z", five: 41, week: 10) + "\r\n")
        #expect(reader(t.url).read().reading?.primary?.usedPercent == 41)
    }

    @Test func theNewestAcrossTwoHomesWins() throws {
        let a = CodexTempDir(), b = CodexTempDir()
        try CodexFixture.rollout(a.url, day: "21", name: "rollout-old", body: CodexFixture.event("2026-10-21T10:00:00Z", five: 10, week: 1) + "\n",
                                 mtime: CodexFixture.date("2026-10-21T10:00:00Z"))
        try CodexFixture.rollout(b.url, day: "22", name: "rollout-new", body: CodexFixture.event("2026-10-22T10:00:00Z", five: 70, week: 7) + "\n",
                                 mtime: CodexFixture.date("2026-10-22T10:00:00Z"))
        #expect(reader(a.url, b.url).read().reading?.primary?.usedPercent == 70)
        #expect(reader(b.url, a.url).read().reading?.primary?.usedPercent == 70)
    }

    @Test func aNewestFileWithNoLimitsFallsBackToTheNext() throws {
        let t = CodexTempDir()
        try CodexFixture.rollout(t.url, day: "21", name: "rollout-old", body: CodexFixture.event("2026-10-21T10:00:00Z", five: 33, week: 3) + "\n",
                                 mtime: CodexFixture.date("2026-10-21T10:00:00Z"))
        try CodexFixture.rollout(t.url, day: "22", name: "rollout-new", body: CodexFixture.premium + "\n",
                                 mtime: CodexFixture.date("2026-10-22T10:00:00Z"))
        #expect(reader(t.url).read().reading?.primary?.usedPercent == 33)
    }

    /// Codex rewrote old rollouts when it compressed them: old name, fresh mtime, old event.
    @Test func newestByContentBeatsNewestByMtime() throws {
        let t = CodexTempDir()
        let oct25 = CodexFixture.date("2026-10-25T00:00:00Z")
        try CodexFixture.rollout(t.url, day: "08", name: "rollout-2026-10-08T10-00-00-old",
                                 body: CodexFixture.event("2026-10-08T10:00:00Z", five: 25, week: 4) + "\n",
                                 mtime: CodexFixture.date("2026-10-22T18:47:00Z"))
        try CodexFixture.rollout(t.url, day: "22", name: "rollout-2026-10-22T20-00-00-new",
                                 body: CodexFixture.event("2026-10-22T23:48:09Z", five: 99, week: 16) + "\n",
                                 mtime: CodexFixture.date("2026-10-20T00:00:00Z"))
        // More than maxFiles rewritten rollouts with newer mtimes: the new one is reachable only by its name.
        for i in 0..<(CodexLimitsReader.maxFiles + 5) {
            try CodexFixture.rollout(t.url, day: "01", name: String(format: "rollout-2026-10-01T00-00-%02d-f", i),
                                     body: CodexFixture.premium + "\n", mtime: oct25.addingTimeInterval(Double(i) * 60))
        }
        let r = reader(t.url).read()
        #expect(r.status == .ok)
        #expect(r.reading?.primary?.usedPercent == 99)
    }

    @Test func aLiveOldThreadWithTheNewestEventWins() throws {
        let t = CodexTempDir()
        try CodexFixture.rollout(t.url, day: "08", name: "rollout-2026-10-08T10-00-00-live",
                                 body: CodexFixture.event("2026-10-23T09:00:00Z", five: 61, week: 9) + "\n",
                                 mtime: CodexFixture.date("2026-10-25T00:00:00Z"))
        try CodexFixture.rollout(t.url, day: "22", name: "rollout-2026-10-22T10-00-00-newer-name",
                                 body: CodexFixture.event("2026-10-22T10:00:00Z", five: 20, week: 2) + "\n",
                                 mtime: CodexFixture.date("2026-10-22T10:00:00Z"))
        #expect(reader(t.url).read().reading?.primary?.usedPercent == 61)
    }

    @Test func aCorruptZstIsUnreadableNotACrash() throws {
        let t = CodexTempDir()
        try CodexFixture.rollout(t.url, day: "22", name: "rollout-z", body: "", zst: Data("garbage".utf8))
        #expect(reader(t.url).read().status == .unreadable)
    }

    @Test func aZstPastTheCapIsUnreadable() throws {
        let t = CodexTempDir()
        try CodexFixture.rollout(t.url, day: "22", name: CodexFixture.fixtureName, body: "",
                                 zst: try CodexFixture.bundled(CodexFixture.fixtureName + ".jsonl.zst"))
        #expect(reader(t.url, cap: 100).read().status == .unreadable)
        #expect(reader(t.url).read().status == .ok)
    }

    @Test func oneFileFailingDoesNotHideAnothersReading() throws {
        let t = CodexTempDir()
        try CodexFixture.rollout(t.url, day: "21", name: "rollout-bad", body: "", zst: Data("garbage".utf8))
        try CodexFixture.rollout(t.url, day: "22", name: "rollout-good", body: CodexFixture.event("2026-10-22T10:00:00Z", five: 52, week: 5) + "\n")
        let r = reader(t.url).read()
        #expect(r.status == .ok)
        #expect(r.reading?.primary?.usedPercent == 52)
    }

    @Test func oneFailingAndAnotherEmptyIsNoLimits() throws {
        let t = CodexTempDir()
        try CodexFixture.rollout(t.url, day: "21", name: "rollout-bad", body: "", zst: Data("garbage".utf8))
        try CodexFixture.rollout(t.url, day: "22", name: "rollout-empty", body: CodexFixture.premium + "\n")
        #expect(reader(t.url).read().status == .noLimits)
    }

    @Test func aFileThatWontOpenIsSkippedForTheNext() throws {
        let t = CodexTempDir()
        try CodexFixture.rollout(t.url, day: "22", name: "rollout-locked", body: CodexFixture.event("2026-10-22T12:00:00Z", five: 90, week: 9) + "\n")
        try CodexFixture.rollout(t.url, day: "21", name: "rollout-open", body: CodexFixture.event("2026-10-21T12:00:00Z", five: 30, week: 3) + "\n")
        let spy = SpyCodexFS()
        spy.failing = ["rollout-locked.jsonl"]
        #expect(reader(t.url, fs: spy).read().reading?.primary?.usedPercent == 30)
        spy.failing = ["rollout-locked.jsonl", "rollout-open.jsonl"]
        #expect(reader(t.url, fs: spy).read().status == .unreadable)
    }

    @Test func aBigPlainFileIsReadFromItsEndAndPastTheTailCapStillYieldsItsLastEvent() throws {
        let t = CodexTempDir()
        let filler = String(repeating: "x", count: 999) + "\n"
        var body = String(repeating: filler, count: CodexLimitsReader.maxTailBytes / 1000 + 500)
        body += CodexFixture.event("2026-10-22T10:00:00Z", five: 77, week: 7) + "\n"
        let url = try CodexFixture.rollout(t.url, day: "22", name: "rollout-big", body: body)
        let size = try #require(try FileManager.default.attributesOfItem(atPath: url.path)[.size] as? Int)
        #expect(size > CodexLimitsReader.maxTailBytes)
        let spy = SpyCodexFS()
        #expect(reader(t.url, fs: spy).read().reading?.primary?.usedPercent == 77)
        // One small read at the end: the event is in the first chunk.
        #expect(spy.reads.count == 1)
        #expect(spy.reads[0].offset == UInt64(size - CodexLimitsReader.firstChunk))
    }

    @Test func aPlainFileWithNoEventInTheTailCapReadsNoMoreThanTheCap() throws {
        let t = CodexTempDir()
        let filler = String(repeating: "y", count: 999) + "\n"
        let body = CodexFixture.event("2026-10-22T10:00:00Z", five: 5, week: 1) + "\n"
            + String(repeating: filler, count: CodexLimitsReader.maxTailBytes / 1000 + 500)
        try CodexFixture.rollout(t.url, day: "22", name: "rollout-old-event", body: body)
        let spy = SpyCodexFS()
        #expect(reader(t.url, fs: spy).read().status == .noLimits)
        #expect(spy.reads.map(\.length).reduce(0, +) == CodexLimitsReader.maxTailBytes)
    }

    @Test func unchangedFilesAreNotDecompressedAgainAndAnAppendIsPickedUp() throws {
        let t = CodexTempDir()
        try CodexFixture.rollout(t.url, day: "21", name: CodexFixture.fixtureName, body: "",
                                 zst: try CodexFixture.bundled(CodexFixture.fixtureName + ".jsonl.zst"))
        let plain = try CodexFixture.rollout(t.url, day: "23", name: "rollout-2026-10-23T10-00-00-p",
                                             body: CodexFixture.event("2026-10-23T10:00:00Z", five: 30, week: 3) + "\n")
        let spy = SpyCodexFS()
        let r = reader(t.url, fs: spy)
        var cache = CodexReadCache()
        #expect(r.read(cache: &cache).reading?.primary?.usedPercent == 30)
        #expect(spy.readAlls == 1)
        #expect(r.read(cache: &cache).reading?.primary?.usedPercent == 30)
        #expect(spy.readAlls == 1)
        let h = try FileHandle(forWritingTo: plain)
        try h.seekToEnd()
        try h.write(contentsOf: Data((CodexFixture.event("2026-10-23T11:00:00Z", five: 45, week: 4) + "\n").utf8))
        try h.close()
        #expect(r.read(cache: &cache).reading?.primary?.usedPercent == 45)
        #expect(spy.readAlls == 1)
    }

    @Test func aCorruptFileIsNotDecompressedAgainWhileUnchanged() throws {
        let t = CodexTempDir()
        try CodexFixture.rollout(t.url, day: "21", name: "rollout-bad", body: "", zst: Data("garbage".utf8))
        let spy = SpyCodexFS()
        let r = reader(t.url, fs: spy)
        var cache = CodexReadCache()
        #expect(r.read(cache: &cache).status == .unreadable)
        #expect(r.read(cache: &cache).status == .unreadable)
        #expect(spy.readAlls == 1)
    }

    /// The `auth.json` proof: newer limits planted in `auth.json` beside and inside `sessions/` are
    /// never seen, and every file the reader opens is a rollout under a `sessions` folder.
    @Test func neverOpensAnythingOutsideSessionsOrAuthJson() throws {
        let t = CodexTempDir()
        let newer = CodexFixture.event("2026-10-30T10:00:00Z", five: 88, week: 88) + "\n"
        try Data(newer.utf8).write(to: t.url.appendingPathComponent("auth.json"))
        try Data(newer.utf8).write(to: t.url.appendingPathComponent("rollout-outside.jsonl"))
        let sessions = t.url.appendingPathComponent("sessions", isDirectory: true)
        try FileManager.default.createDirectory(at: sessions, withIntermediateDirectories: true)
        try Data(newer.utf8).write(to: sessions.appendingPathComponent("auth.json"))
        try Data(newer.utf8).write(to: sessions.appendingPathComponent("rollout-notes.txt"))
        try CodexFixture.rollout(t.url, day: "22", name: "rollout-2026-10-22T10-00-00-a",
                                 body: CodexFixture.event("2026-10-22T10:00:00Z", five: 40, week: 10) + "\n")
        try CodexFixture.rollout(t.url, day: "21", name: CodexFixture.fixtureName, body: "",
                                 zst: try CodexFixture.bundled(CodexFixture.fixtureName + ".jsonl.zst"))
        let spy = SpyCodexFS()
        #expect(reader(t.url, fs: spy).read().reading?.primary?.usedPercent == 64)
        #expect(spy.opened.count == 2)
        let sessionsPath = sessions.resolvingSymlinksInPath().path + "/"
        for url in spy.opened {
            #expect(url.resolvingSymlinksInPath().path.hasPrefix(sessionsPath))
            #expect(url.lastPathComponent.hasPrefix("rollout-"))
            #expect(url.lastPathComponent != "auth.json")
        }
        #expect(spy.listings.map { $0.lastPathComponent } == ["sessions"])

        let source = URL(fileURLWithPath: #filePath).deletingLastPathComponent()
            .appendingPathComponent("../../Sources/Sanduhr/Services/CodexLimitsReader.swift").standardizedFileURL
        #expect(!(try String(contentsOf: source, encoding: .utf8)).lowercased().contains("auth.json"))
    }

    @Test func homesKeepExistingFoldersOnlyWithCodexHomeFirst() throws {
        let profile = CodexTempDir(), custom = CodexTempDir()
        let codex = profile.url.appendingPathComponent(".codex", isDirectory: true)
        try FileManager.default.createDirectory(at: codex, withIntermediateDirectories: true)
        let both = CodexHomes.discover(home: profile.url, environment: ["CODEX_HOME": custom.url.path])
        #expect(both.map(\.standardizedFileURL.path) == [custom.url.standardizedFileURL.path, codex.standardizedFileURL.path])
        let missing = CodexHomes.discover(home: profile.url, environment: ["CODEX_HOME": custom.url.appendingPathComponent("missing").path])
        #expect(missing.map(\.standardizedFileURL.path) == [codex.standardizedFileURL.path])
        #expect(CodexHomes.discover(home: profile.url, environment: ["CODEX_HOME": codex.path]).count == 1)
        #expect(CodexHomes.discover(home: custom.url, environment: [:]).isEmpty)
    }
}
