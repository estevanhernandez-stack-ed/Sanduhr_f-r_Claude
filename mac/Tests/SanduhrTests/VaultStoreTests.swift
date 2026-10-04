import Foundation
import Testing
@testable import Sanduhr

/// The vault test bed (item 46): a made-up home with Claude Code folders under the temp
/// directory, a vault directory beside it, the Windows tests' clock (2026-07-12 18:00 UTC) and
/// zone (US Central), and lines built exactly as the Windows tests' `EventLine`. Nothing here
/// reads a real ~/.claude* folder or the real vault.
final class VaultBed: @unchecked Sendable {
    static let cst = TimeZone(identifier: "America/Chicago")!
    static let now = VaultTime.parse("2026-07-12T18:00:00+00:00")!
    static let winCwd = #"C:\Users\x\Projects\api"#
    static let parentUuid = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"

    let home = TempHome()
    let vaultDir: String
    let store: VaultStore
    private let logLock = NSLock()
    private var _logs: [String] = []

    init() {
        vaultDir = FileManager.default.temporaryDirectory
            .appendingPathComponent("sanduhr-vault-\(UUID().uuidString)", isDirectory: true).path
        try? FileManager.default.createDirectory(atPath: vaultDir, withIntermediateDirectories: true)
        store = VaultStore(vaultDir: vaultDir)
    }

    func cleanUp() {
        home.cleanUp()
        try? FileManager.default.removeItem(atPath: vaultDir)
    }

    var logs: [String] { logLock.lock(); defer { logLock.unlock() }; return _logs }

    var sink: VaultLogSink {
        { [weak self] m in
            guard let self else { return }
            self.logLock.lock()
            self._logs.append(m)
            self.logLock.unlock()
        }
    }

    /// Windows `EventLine`, byte for byte.
    static func line(_ ts: String, model: String = "claude-fable-5", input: Int64 = 100, output: Int64 = 50,
                     cwd: String? = VaultBed.winCwd, skill: String? = nil,
                     cacheRead: Int64 = 0, cacheCreation: Int64 = 0) -> String {
        var s = "{\"type\":\"assistant\",\"timestamp\":\"" + ts + "\""
        if let cwd { s += ",\"cwd\":" + json(cwd) }
        if let skill { s += ",\"attributionSkill\":\"" + skill + "\"" }
        s += ",\"message\":{\"model\":\"" + model + "\",\"usage\":{\"input_tokens\":" + String(input)
        s += ",\"output_tokens\":" + String(output)
        if cacheRead > 0 { s += ",\"cache_read_input_tokens\":" + String(cacheRead) }
        if cacheCreation > 0 { s += ",\"cache_creation_input_tokens\":" + String(cacheCreation) }
        return s + "}}}"
    }

    static func json(_ s: String) -> String {
        let data = (try? JSONSerialization.data(withJSONObject: s, options: [.fragmentsAllowed, .withoutEscapingSlashes])) ?? Data()
        return String(decoding: data, as: UTF8.self)
    }

    /// `<home>/<root>/projects/c--Users-x-Projects-api/<uuid>.jsonl`, mtime pinned (Now - 10 min
    /// unless told): a real-clock mtime would make the quiet-file seal engage at random.
    @discardableResult
    func session(_ uuid: String, root: String = ".claude", mtime: Date? = nil, _ lines: [String]) -> String {
        let rel = "\(root)/projects/c--Users-x-Projects-api"
        home.dir(rel)
        let path = home.at("\(rel)/\(uuid).jsonl")
        write(path, lines.joined(separator: "\n") + "\n", mtime: mtime ?? Self.now.addingTimeInterval(-600))
        return path
    }

    /// A nested transcript: `<projectDir>/<parent>/subagents/<name>.jsonl`.
    @discardableResult
    func nested(parent: String, _ name: String, root: String = ".claude", mtime: Date? = nil,
                _ lines: [String]) -> String {
        let rel = "\(root)/projects/c--Users-x-Projects-api/\(parent)/subagents"
        home.dir(rel)
        let path = home.at("\(rel)/\(name).jsonl")
        write(path, lines.joined(separator: "\n") + "\n", mtime: mtime ?? Self.now.addingTimeInterval(-600))
        return path
    }

    func write(_ path: String, _ text: String, mtime: Date) {
        FileManager.default.createFile(atPath: path, contents: Data(text.utf8))
        CCLogs.setModified(path, mtime)
    }

    func append(_ path: String, mtime: Date, _ lines: [String]) {
        CCLogs.append(path, lines.joined(separator: "\n") + "\n")
        CCLogs.setModified(path, mtime)
    }

    func shard(_ month: String = "2026-07", root: String = ".claude") -> VaultSessionShard {
        store.loadSessionShard(root, month).1
    }

    func rollups(_ month: String = "2026-07", root: String = ".claude") -> VaultRollupShard {
        store.loadRollupShard(root, month).1
    }

    func rowTotal(_ month: String = "2026-07", uuid: String = "u1") -> Int64? {
        shard(month).sessions[uuid]?.total
    }

    func rootPath(_ root: String = ".claude") -> String { store.rootDir(root) }

    func files(in dir: String) -> [String] {
        ((try? FileManager.default.contentsOfDirectory(atPath: dir)) ?? []).sorted()
    }

    /// Every file under the vault, path to bytes.
    func snapshot() -> [String: Data] {
        var out: [String: Data] = [:]
        guard let walker = FileManager.default.enumerator(atPath: vaultDir) else { return out }
        while let rel = walker.nextObject() as? String {
            let full = (vaultDir as NSString).appendingPathComponent(rel)
            var isDir: ObjCBool = false
            if FileManager.default.fileExists(atPath: full, isDirectory: &isDir), !isDir.boolValue {
                out[rel] = FileManager.default.contents(atPath: full)
            }
        }
        return out
    }

    /// fold(session shard) == stored rollups, after any ingest.
    func foldMatchesRollups(_ month: String, root: String = ".claude") -> Bool {
        let (s, sessions) = store.loadSessionShard(root, month)
        let (r, rollups) = store.loadRollupShard(root, month)
        guard s == .ok, r == .ok else { return false }
        var expected: [String: Int64] = [:]
        for row in sessions.sessions.values {
            for (day, b) in row.byDay { expected[day, default: 0] += b.total }
        }
        return expected.count == rollups.days.count
            && expected.allSatisfy { rollups.days[$0.key]?.total == $0.value }
    }

    /// A vault fixture's bytes (`Fixtures/vault`).
    static func fixture(_ name: String) -> Data {
        guard let url = Bundle.module.url(forResource: name, withExtension: "json", subdirectory: "Fixtures/vault"),
              let data = try? Data(contentsOf: url) else { return Data() }
        return data
    }
}

/// Windows `VaultStoreTests` and `VaultSplitTests` (store side), the row algebra, and the Windows
/// file formats read as Windows writes them.
@Suite("Vault store")
struct VaultStoreTests {
    static func shard(_ rows: [(String, Int64)]) -> VaultSessionShard {
        var shard = VaultSessionShard(schemaVersion: 1, writerVersion: "test")
        for (uuid, total) in rows {
            var row = VaultSessionRow()
            row.projectKey = "api~00000000"
            row.projectName = "api"
            row.firstTs = "2026-07-01T00:00:00+00:00"
            row.lastTs = "2026-07-01T01:00:00+00:00"
            row.utcOffsetMin = -300
            row.eventCount = 1
            row.total = total
            row.byModel = ["claude-fable-5": total]
            row.byDay = ["2026-07-01": VaultDayBucket(total: total, byModel: ["claude-fable-5": total])]
            shard.sessions[uuid] = row
        }
        return shard
    }

    @Test func sessionShardRoundTripsInItsFolderDirectory() throws {
        let b = VaultBed()
        defer { b.cleanUp() }
        try b.store.saveSessionShard(".claude", "2026-07", Self.shard([("u1", 100)]))
        #expect(FileManager.default.fileExists(atPath: b.rootPath() + "/sessions-2026-07.json"))
        let (result, loaded) = b.store.loadSessionShard(".claude", "2026-07")
        #expect(result == .ok)
        #expect(loaded.sessions["u1"]?.total == 100)
        #expect(loaded.schemaVersion == 1)
    }

    @Test func saveReplacesAtomicallyLeavingNoTmp() throws {
        let b = VaultBed()
        defer { b.cleanUp() }
        try b.store.saveSessionShard(".claude", "2026-07", Self.shard([("u1", 100)]))
        try b.store.saveSessionShard(".claude", "2026-07", Self.shard([("u1", 200)]))
        #expect(b.files(in: b.rootPath()) == ["sessions-2026-07.json"])
        #expect(b.rowTotal() == 200)
    }

    @Test func missingShardIsMissingNotCorrupt() {
        let b = VaultBed()
        defer { b.cleanUp() }
        #expect(b.store.loadSessionShard(".claude", "2026-07").0 == .missing)
    }

    @Test func corruptShardReadsCorruptAndIsLeftAlone() {
        let b = VaultBed()
        defer { b.cleanUp() }
        try? FileManager.default.createDirectory(atPath: b.rootPath(), withIntermediateDirectories: true)
        FileManager.default.createFile(atPath: b.rootPath() + "/sessions-2026-07.json", contents: Data("{not json".utf8))
        #expect(b.store.loadSessionShard(".claude", "2026-07").0 == .corrupt)
        #expect(FileManager.default.fileExists(atPath: b.rootPath() + "/sessions-2026-07.json"))
    }

    @Test func quarantineRenamesTimestampedAndDeletesCheckpoints() {
        let b = VaultBed()
        defer { b.cleanUp() }
        let dir = b.rootPath()
        try? FileManager.default.createDirectory(atPath: dir, withIntermediateDirectories: true)
        FileManager.default.createFile(atPath: dir + "/sessions-2026-07.json", contents: Data("{not json".utf8))
        b.store.saveCheckpoints(".claude", VaultCheckpointFile())
        #expect(FileManager.default.fileExists(atPath: dir + "/checkpoints.json"))

        b.store.quarantineSessionShard(".claude", "2026-07", now: VaultTime.parse("2026-07-15T03:15:00+00:00")!)
        #expect(!FileManager.default.fileExists(atPath: dir + "/sessions-2026-07.json"))
        #expect(FileManager.default.fileExists(atPath: dir + "/sessions-2026-07.json.20260715T031500.bad"))
        #expect(!FileManager.default.fileExists(atPath: dir + "/checkpoints.json"))
    }

    @Test func quarantineNeverOverwritesABadFile() {
        let b = VaultBed()
        defer { b.cleanUp() }
        let dir = b.rootPath()
        try? FileManager.default.createDirectory(atPath: dir, withIntermediateDirectories: true)
        FileManager.default.createFile(atPath: dir + "/sessions-2026-07.json.20260715T031500.bad", contents: Data("earlier".utf8))
        FileManager.default.createFile(atPath: dir + "/sessions-2026-07.json", contents: Data("{not json".utf8))

        b.store.quarantineSessionShard(".claude", "2026-07", now: VaultTime.parse("2026-07-15T03:15:00+00:00")!)
        let earlier = FileManager.default.contents(atPath: dir + "/sessions-2026-07.json.20260715T031500.bad")
        #expect(earlier == Data("earlier".utf8))
        #expect(!FileManager.default.fileExists(atPath: dir + "/sessions-2026-07.json"))
        #expect(b.files(in: dir).filter { $0.hasSuffix(".bad") }.count == 2)
    }

    @Test func corruptCheckpointsLoadEmptyAndAreDeleted() {
        let b = VaultBed()
        defer { b.cleanUp() }
        try? FileManager.default.createDirectory(atPath: b.rootPath(), withIntermediateDirectories: true)
        FileManager.default.createFile(atPath: b.rootPath() + "/checkpoints.json", contents: Data("][".utf8))
        #expect(b.store.loadCheckpoints(".claude").entries.isEmpty)
        #expect(!FileManager.default.fileExists(atPath: b.rootPath() + "/checkpoints.json"))
    }

    @Test func monthsAreSortedAndIgnoreBadAndRollups() throws {
        let b = VaultBed()
        defer { b.cleanUp() }
        try b.store.saveSessionShard(".claude", "2026-07", Self.shard([("u1", 1)]))
        try b.store.saveSessionShard(".claude", "2026-06", Self.shard([("u2", 1)]))
        b.store.saveRollupShard(".claude", "2026-06", VaultRollupShard())
        FileManager.default.createFile(atPath: b.rootPath() + "/sessions-2026-05.json.20260715T031500.bad", contents: Data("x".utf8))
        #expect(b.store.listSessionShardMonths(".claude") == ["2026-06", "2026-07"])
    }

    @Test func metaRoundTripsAndMissingIsNil() {
        let b = VaultBed()
        defer { b.cleanUp() }
        #expect(b.store.loadMeta(".claude") == nil)
        b.store.saveMeta(".claude", VaultRootMeta(since: "2026-07-12",
                                                  covered: [VaultDateRange(from: "2026-06-17", to: "2026-07-12")],
                                                  lastIngestTs: "2026-07-12T09:00:00+00:00"))
        let meta = b.store.loadMeta(".claude")
        #expect(meta?.since == "2026-07-12")
        #expect(meta?.covered.count == 1)
    }

    @Test func purgeRootRemovesOnlyThatFolder() throws {
        let b = VaultBed()
        defer { b.cleanUp() }
        try b.store.saveSessionShard(".claude", "2026-07", Self.shard([("u1", 1)]))
        try b.store.saveSessionShard(".claude-personal", "2026-07", Self.shard([("u2", 1)]))
        b.store.purgeRoot(".claude")
        #expect(!b.store.rootExists(".claude"))
        #expect(b.store.rootExists(".claude-personal"))
    }

    @Test func pathKeyIsCaseFoldedHex() {
        let a = VaultStore.pathKey(#"C:\Users\X\.claude\projects\p\u.jsonl"#)
        let c = VaultStore.pathKey(#"c:\users\x\.claude\projects\p\u.jsonl"#)
        #expect(a == c)
        #expect(a.count == 64)
        #expect(a.allSatisfy { $0.isHexDigit && !$0.isUppercase })
        // The same key Windows computes for the same string (SHA-256 of the lowercased UTF-8).
        #expect(VaultStore.pathKey("ABC") == "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")
    }

    @Test func shardUsesSnakeCaseNamesAndOmitsNullCwd() throws {
        let b = VaultBed()
        defer { b.cleanUp() }
        try b.store.saveSessionShard(".claude", "2026-07", Self.shard([("u1", 100)]))
        let raw = String(decoding: FileManager.default.contents(atPath: b.rootPath() + "/sessions-2026-07.json") ?? Data(),
                         as: UTF8.self)
        for key in ["\"schema_version\"", "\"by_model\"", "\"by_day\"", "\"project_key\""] {
            #expect(raw.contains(key))
        }
        #expect(!raw.contains("\"projectKey\""))
        #expect(!raw.contains("\"cwd\""))
        #expect(!raw.contains("\"parent_session\""))
    }

    // MARK: Windows formats

    @Test func legacyBucketsWithoutSplitReadZero() throws {
        let b = VaultBed()
        defer { b.cleanUp() }
        try FileManager.default.createDirectory(atPath: b.rootPath(), withIntermediateDirectories: true)
        FileManager.default.createFile(atPath: b.rootPath() + "/sessions-2026-07.json",
                                       contents: VaultBed.fixture("wsc-era-sessions-2026-07"))
        let (result, shard) = b.store.loadSessionShard(".claude", "2026-07")
        #expect(result == .ok)
        let bucket = shard.sessions["u1"]?.byDay["2026-07-01"]
        #expect(bucket?.total == 150)
        #expect(bucket?.input == 0)
        #expect(bucket?.output == 0)
    }

    @Test func readsAWindowsWrittenShardCheckpointsAndMeta() throws {
        let b = VaultBed()
        defer { b.cleanUp() }
        let dir = b.rootPath()
        try FileManager.default.createDirectory(atPath: dir, withIntermediateDirectories: true)
        FileManager.default.createFile(atPath: dir + "/sessions-2026-07.json", contents: VaultBed.fixture("windows-sessions-2026-07"))
        FileManager.default.createFile(atPath: dir + "/checkpoints.json", contents: VaultBed.fixture("windows-checkpoints"))
        FileManager.default.createFile(atPath: dir + "/meta.json", contents: VaultBed.fixture("windows-meta"))

        let (result, shard) = b.store.loadSessionShard(".claude", "2026-07")
        #expect(result == .ok)
        #expect(shard.writerVersion == "3.4.0")
        let u1 = try #require(shard.sessions["u1"])
        #expect(u1.byModel["<synthetic>"] == 4)          // .NET's \u003C escapes decode
        #expect(u1.cacheTokens == VaultCacheTokens(read: 5000, creation: 200))
        #expect(shard.sessions["agent-x"]?.parentSession == "u1")
        let cp = b.store.loadCheckpoints(".claude")
        #expect(cp.entries.values.first?.sealed == true)
        #expect(cp.entries.values.first?.months == ["2026-07"])
        #expect(b.store.loadMeta(".claude")?.walkVersion == 2)

        // A round trip through the Mac's encoder keeps every value.
        try b.store.saveSessionShard(".claude", "2026-07", shard)
        #expect(b.shard() == shard)
    }

    @Test func timestampsAreWrittenTheWindowsWay() {
        let t = VaultTime.parse("2026-07-10T15:00:00.123Z")!
        #expect(VaultTime.iso(t) == "2026-07-10T15:00:00.123000+00:00")
        #expect(VaultTime.iso(VaultBed.now) == "2026-07-12T18:00:00.000000+00:00")
        #expect(VaultTime.parse("2026-07-12T18:00:00.000000+00:00") == VaultBed.now)
        // .NET ticks: 2026-07-12T18:00:00Z.
        let ticks = VaultTime.ticks(seconds: Int64(VaultBed.now.timeIntervalSince1970), nanoseconds: 0)
        #expect(ticks == 639_194_760_000_000_000)
        #expect(VaultTime.date(ticks: ticks) == VaultBed.now)
    }

    @Test func dayArithmetic() {
        let d = CCLocalDay(key: "2026-07-12")!
        #expect(d.key == "2026-07-12")
        #expect(d.monthKey == "2026-07")
        #expect(d.mondayIndex == 6)                       // a Sunday
        #expect(d.weekStart.key == "2026-07-06")
        #expect(d.adding(days: -25).key == "2026-06-17")
        #expect(CCLocalDay(key: "2026-12-31")!.adding(days: 1).key == "2027-01-01")
        #expect(CCLocalDay(key: "2028-02-28")!.adding(days: 1).key == "2028-02-29")
        #expect(CCLocalDay(key: "2026-13-01") == nil)
        #expect(CCLocalDay(key: "nope") == nil)
    }

    // MARK: Row algebra (Windows RowMath_split_then_merge_round_trips)

    @Test func splitThenMergeRoundTrips() {
        var merged = VaultSessionRow()
        merged.projectKey = "api~12345678"
        merged.projectName = "api"
        merged.firstTs = "2026-07-31T20:00:00+00:00"
        merged.lastTs = "2026-08-02T10:00:00+00:00"
        merged.utcOffsetMin = -300
        merged.eventCount = 3
        merged.skippedLines = 1
        merged.total = 600
        merged.byModel = ["claude-fable-5": 600]
        merged.cacheTokens = VaultCacheTokens(read: 42, creation: 7)
        merged.bySkill = ["code-review": 100]
        merged.byDay = [
            "2026-07-31": VaultDayBucket(total: 100, byModel: ["claude-fable-5": 100], bySkill: ["code-review": 100]),
            "2026-08-01": VaultDayBucket(total: 200, byModel: ["claude-fable-5": 200]),
            "2026-08-02": VaultDayBucket(total: 300, byModel: ["claude-fable-5": 300]),
        ]
        let byMonth = VaultRowMath.splitByMonth(merged)
        #expect(byMonth.count == 2)
        #expect(byMonth["2026-07"]?.continuation == false)
        #expect(byMonth["2026-08"]?.continuation == true)
        #expect(byMonth["2026-07"]?.total == 100)
        #expect(byMonth["2026-08"]?.total == 500)
        #expect(byMonth["2026-08"]?.cacheTokens == nil)

        let back = VaultRowMath.merge(Array(byMonth.values))
        #expect(back.total == 600)
        #expect(back.eventCount == 3)
        #expect(back.cacheTokens?.read == 42)
        #expect(back.byModel["claude-fable-5"] == 600)
        #expect(back.bySkill?["code-review"] == 100)
        #expect(back.byDay.count == 3)
        #expect(back.firstTs == "2026-07-31T20:00:00+00:00")
    }
}
