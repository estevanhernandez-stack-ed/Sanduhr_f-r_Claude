import Foundation
import Testing
@testable import Sanduhr

/// The bed's ingest helpers.
extension VaultBed {
    /// An ingester over this bed: US Central unless told, no `.git` anywhere.
    func ingester(tz: TimeZone = VaultBed.cst) -> VaultIngester {
        VaultIngester(store: VaultStore(vaultDir: vaultDir, log: sink), writerVersion: "test",
                      timeZone: tz, log: sink, dirHoldsGit: { _ in false })
    }

    /// A folder of this home as a recorded root whose vault directory is named like the folder,
    /// so the assertions read like the Windows ones.
    func root(_ name: String = ".claude", names: ProjectNamesChoice = .names) -> VaultRoot {
        VaultRoot(folder: home.at(name), id: name, names: names)
    }

    @discardableResult
    func ingest(_ roots: [VaultRoot]? = nil, now: Date = VaultBed.now, tz: TimeZone = VaultBed.cst,
                stillRecording: ((String) -> Bool)? = nil) -> VaultIngestResult {
        ingester(tz: tz).ingestOnce(roots ?? [root()], now: now, stillRecording: stillRecording)
    }
}

/// Windows `VaultIngesterTests`, `VaultIngesterHardeningTests`, `VaultSubagentCoverageTests`,
/// `VaultSplitTests` (ingest side) and the rollup case of `VaultLogicalSessionTests`, with the
/// same inputs and expected numbers, plus the Mac's project-name choices. Temp folders only.
@Suite("Vault ingest")
struct VaultIngesterTests {
    typealias B = VaultBed

    // MARK: VaultIngesterTests

    @Test func happyPathKeepsRawModelsCacheAndUnconditionalTotals() throws {
        let b = VaultBed()
        defer { b.cleanUp() }
        b.session("u1", [
            B.line("2026-07-10T15:00:00Z", model: "claude-fable-5", input: 1000, output: 100,
                   skill: "code-review", cacheRead: 5000, cacheCreation: 200),
            B.line("2026-07-10T16:00:00Z", model: "<synthetic>", input: 0, output: 4),
            B.line("2026-07-10T17:00:00Z", model: "claude-sonnet-5", input: 300, output: 30),
        ])
        let r = b.ingest()
        #expect(r.acquired)
        #expect(r.filesFullParsed == 1)
        let shard = b.shard()
        let row = try #require(shard.sessions["u1"])
        #expect(row.total == 1434)
        #expect(row.byModel["claude-fable-5"] == 1100)
        #expect(row.byModel["<synthetic>"] == 4)
        #expect(row.byModel["claude-sonnet-5"] == 330)
        #expect(row.byModel.values.reduce(0, +) == row.total)
        #expect(row.cacheTokens == VaultCacheTokens(read: 5000, creation: 200))
        #expect(row.bySkill?["code-review"] == 1100)
        #expect(row.eventCount == 3)
        #expect(!row.continuation)
        #expect(row.projectName == "api")
        #expect(row.projectKey.hasPrefix("api~") && row.projectKey.count == 12)
        #expect(row.cwd == nil)
        #expect(row.byDay.values.reduce(0) { $0 + $1.total } == row.total)
        #expect(Array(row.byDay.keys) == ["2026-07-10"])     // 15:00Z = 10:00 CDT
        #expect(row.firstTs == "2026-07-10T15:00:00.000000+00:00")
        #expect(row.utcOffsetMin == -300)
        #expect(shard.writerVersion == "test")
    }

    @Test func projectKeyHashesTheCwdLikeWindows() {
        let b = VaultBed()
        defer { b.cleanUp() }
        b.session("u1", [B.line("2026-07-10T15:00:00Z")])
        b.ingest()
        let expected = "api~" + VaultStore.pathKey(B.winCwd).prefix(8)
        #expect(b.shard().sessions["u1"]?.projectKey == expected)
    }

    @Test func fullPathsKeepTheCwd() {
        let b = VaultBed()
        defer { b.cleanUp() }
        b.session("u1", [B.line("2026-07-10T15:00:00Z")])
        b.ingest([b.root(names: .full)])
        #expect(b.shard().sessions["u1"]?.cwd == B.winCwd)
    }

    @Test func reingestWithTheSameNowIsByteIdentical() {
        let b = VaultBed()
        defer { b.cleanUp() }
        b.session("u1", [B.line("2026-07-10T15:00:00Z"), B.line("2026-07-10T16:00:00Z")])
        b.ingest()
        let before = b.snapshot()
        b.ingest()
        #expect(b.snapshot() == before)
    }

    @Test func vaultDayTotalMatchesTheLiveReaderEvenForUnmappedModels() {
        // The fable-5 invariant, against the Mac's live reader (item 45) on the same clock and zone.
        let b = VaultBed()
        defer { b.cleanUp() }
        b.session("u1", [
            B.line("2026-07-11T15:00:00Z", model: "claude-fable-5", input: 700, output: 70),
            B.line("2026-07-11T15:00:00Z", model: "claude-mystery-99", input: 20, output: 2),
        ])
        b.ingest()
        var cal = Calendar(identifier: .gregorian)
        cal.timeZone = B.cst
        let live = CCLogReader(root: b.home.at(".claude")).tokensByDay(daysBack: 30, now: B.now, calendar: cal)
        let day = CCLocalDay(year: 2026, month: 7, day: 11)
        let vaultDay = b.shard().sessions.values.compactMap { $0.byDay[day.key]?.total }.reduce(0, +)
        #expect(live[day] == vaultDay)
        #expect(vaultDay == 792)
    }

    @Test func monthSpanningSessionSlicesWithContinuationRows() throws {
        let b = VaultBed()
        defer { b.cleanUp() }
        b.session("u1", [
            B.line("2026-07-31T20:00:00Z", input: 100, output: 0),   // Jul 31 15:00 CDT
            B.line("2026-08-01T20:00:00Z", input: 200, output: 0),   // Aug 1 15:00 CDT
        ])
        b.ingest()
        let primary = try #require(b.shard("2026-07").sessions["u1"])
        let slice = try #require(b.shard("2026-08").sessions["u1"])
        #expect(!primary.continuation)
        #expect(slice.continuation)
        #expect(primary.total == 100)
        #expect(slice.total == 200)
        #expect(Array(primary.byDay.keys) == ["2026-07-31"])
        #expect(Array(slice.byDay.keys) == ["2026-08-01"])
        #expect(primary.cacheTokens != nil)
        #expect(slice.cacheTokens == nil)
        #expect(slice.eventCount == 0)
        #expect(primary.firstTs == slice.firstTs)
        #expect(primary.projectKey == slice.projectKey)
    }

    @Test func utcMonthBoundaryPlacesByLocalTime() {
        let b = VaultBed()
        defer { b.cleanUp() }
        b.session("u1", [B.line("2026-08-01T02:00:00Z")])    // 2026-07-31 21:00 CDT
        b.ingest()
        #expect(b.store.loadSessionShard(".claude", "2026-07").0 == .ok)
        #expect(b.shard("2026-07").sessions["u1"] != nil)
        #expect(b.store.loadSessionShard(".claude", "2026-08").0 == .missing)
    }

    @Test func dstFallBackTwentyFiveHourDayBucketsByLocalDate() {
        let b = VaultBed()
        defer { b.cleanUp() }
        // US DST ends 2026-11-01: 06:30Z = 01:30 CDT; 2026-11-02T05:30Z = 23:30 CST on the 1st.
        b.session("u1", [
            B.line("2026-11-01T06:30:00Z", input: 10, output: 0),
            B.line("2026-11-02T05:30:00Z", input: 20, output: 0),
        ])
        b.ingest(now: VaultTime.parse("2026-11-03T00:00:00Z")!)
        let row = b.shard("2026-11").sessions["u1"]
        #expect(row?.byDay["2026-11-01"]?.total == 30)
        #expect(row?.byDay["2026-11-02"] == nil)
    }

    @Test func secondWriterHoldingTheLockSkipsCleanly() {
        let b = VaultBed()
        defer { b.cleanUp() }
        b.session("u1", [B.line("2026-07-10T15:00:00Z")])
        let held = VaultWriterLock.tryAcquire(b.vaultDir + "/.writer.lock")
        #expect(held != nil)
        let r = b.ingest()
        #expect(!r.acquired)
        #expect(!b.store.rootExists(".claude"))
        held?.release()
        #expect(b.ingest().acquired)
        #expect(b.rowTotal() == 150)
    }

    @Test func onlyTheRootsAskedForAreTouched() {
        let b = VaultBed()
        defer { b.cleanUp() }
        b.session("u1", root: ".claude", [B.line("2026-07-10T15:00:00Z")])
        b.session("u2", root: ".claude-personal", [B.line("2026-07-10T15:00:00Z")])
        b.ingest([b.root(".claude-personal")])
        #expect(!b.store.rootExists(".claude"))
        #expect(FileManager.default.fileExists(atPath: b.rootPath(".claude-personal") + "/sessions-2026-07.json"))
    }

    @Test func zeroAssistantEventSessionWritesNoRowButCheckpoints() {
        let b = VaultBed()
        defer { b.cleanUp() }
        b.session("u1", ["{\"type\":\"user\",\"text\":\"hi\"}"])
        let r = b.ingest()
        #expect(b.store.loadSessionShard(".claude", "2026-07").0 == .missing)
        #expect(b.store.loadCheckpoints(".claude").entries.count == 1)
        #expect(r.filesFullParsed == 1)
    }

    @Test func malformedAssistantLinesAreCountedNeverFatal() {
        let b = VaultBed()
        defer { b.cleanUp() }
        b.session("u1", [
            B.line("2026-07-10T15:00:00Z"),
            "{\"type\":\"assistant\", TRUNCATED GARBAGE",
            B.line("2026-07-10T16:00:00Z"),
        ])
        b.ingest()
        let row = b.shard().sessions["u1"]
        #expect(row?.eventCount == 2)
        #expect(row?.skippedLines == 1)
        #expect(row?.total == 300)
    }

    @Test func rollupsFoldPerDayProjectSkillAndSessions() throws {
        let b = VaultBed()
        defer { b.cleanUp() }
        b.session("u1", [
            B.line("2026-07-10T15:00:00Z", input: 100, output: 0, skill: "code-review"),
            B.line("2026-07-11T15:00:00Z", input: 200, output: 0),
        ])
        b.session("u2", [B.line("2026-07-10T16:00:00Z", input: 40, output: 0)])
        b.ingest()
        #expect(b.store.loadRollupShard(".claude", "2026-07").0 == .ok)
        let d10 = try #require(b.rollups().days["2026-07-10"])
        #expect(d10.total == 140)
        #expect(d10.sessions == 2)
        #expect(d10.bySkill["code-review"] == 100)
        #expect(d10.byProject.values.reduce(0, +) == 140)
        #expect(d10.byProject.count == 1)
        let d11 = try #require(b.rollups().days["2026-07-11"])
        #expect(d11.total == 200)
        #expect(d11.sessions == 1)
    }

    @Test func crashBetweenShardAndCheckpointConvergesWithoutDoubleCount() {
        let b = VaultBed()
        defer { b.cleanUp() }
        let path = b.session("u1", [B.line("2026-07-10T15:00:00Z")])
        b.ingest()
        let cpPath = b.rootPath() + "/checkpoints.json"
        let stale = FileManager.default.contents(atPath: cpPath)
        b.append(path, mtime: B.now.addingTimeInterval(-300), [B.line("2026-07-10T16:00:00Z")])
        b.ingest()
        FileManager.default.createFile(atPath: cpPath, contents: stale)   // the "crash"
        b.ingest()                                                          // recovery
        #expect(b.rowTotal() == 300)
        #expect(b.shard().sessions["u1"]?.eventCount == 2)
        #expect(b.rollups().days["2026-07-10"]?.total == 300)
    }

    @Test func metaRecordsSinceCoverageAndLastIngest() throws {
        let b = VaultBed()
        defer { b.cleanUp() }
        b.session("u1", [B.line("2026-07-10T15:00:00Z")])
        b.ingest()
        var meta = try #require(b.store.loadMeta(".claude"))
        #expect(meta.since == "2026-07-12")
        #expect(meta.covered == [VaultDateRange(from: "2026-06-17", to: "2026-07-12")])
        #expect(meta.lastIngestTs == "2026-07-12T18:00:00.000000+00:00")
        #expect(meta.walkVersion == 2)

        b.ingest(now: B.now.addingTimeInterval(3 * 86_400))
        meta = try #require(b.store.loadMeta(".claude"))
        #expect(meta.since == "2026-07-12")
        #expect(meta.covered == [VaultDateRange(from: "2026-06-17", to: "2026-07-15")])
    }

    // MARK: VaultSplitTests (ingest side)

    @Test func newBucketsConserveInputPlusOutput() {
        let b = VaultBed()
        defer { b.cleanUp() }
        b.session("u1", [
            B.line("2026-07-10T15:00:00Z", input: 700, output: 70),
            B.line("2026-07-10T16:00:00Z", input: 20, output: 2),
        ])
        b.ingest()
        let bucket = b.shard().sessions["u1"]?.byDay["2026-07-10"]
        #expect(bucket?.input == 720)
        #expect(bucket?.output == 72)
        #expect(bucket.map { $0.input + $0.output == $0.total } == true)
        #expect(b.rollups().days["2026-07-10"]?.input == 720)
        #expect(b.rollups().days["2026-07-10"]?.output == 72)
    }

    @Test func tailParsePreservesTheSplit() {
        let b = VaultBed()
        defer { b.cleanUp() }
        let path = b.session("u1", [B.line("2026-07-10T15:00:00Z", input: 100, output: 50)])
        b.ingest()
        b.append(path, mtime: B.now.addingTimeInterval(-300), [B.line("2026-07-10T16:00:00Z", input: 30, output: 3)])
        let r2 = b.ingest()
        #expect(r2.filesTailParsed == 1)
        let bucket = b.shard().sessions["u1"]?.byDay["2026-07-10"]
        #expect(bucket?.input == 130)
        #expect(bucket?.output == 53)
        #expect(bucket.map { $0.input + $0.output == $0.total } == true)
    }

    // MARK: VaultIngesterHardeningTests

    @Test func growingFileTailParsesAndConvergesWithAFullParse() throws {
        let b = VaultBed()
        defer { b.cleanUp() }
        let path = b.session("u1", [B.line("2026-07-10T15:00:00Z")])
        b.ingest()
        b.append(path, mtime: B.now.addingTimeInterval(-300), [B.line("2026-07-10T16:00:00Z", input: 200, output: 0)])
        let r2 = b.ingest()
        #expect(r2.filesTailParsed == 1)
        #expect(r2.filesFullParsed == 0)
        #expect(b.rowTotal() == 350)
        #expect(b.shard().sessions["u1"]?.eventCount == 2)

        let fresh = VaultBed()
        defer { fresh.cleanUp() }
        let freshRoot = VaultRoot(folder: b.home.at(".claude"), id: ".claude", names: .names)
        fresh.ingester().ingestOnce([freshRoot], now: B.now)
        #expect(fresh.shard().sessions["u1"] == b.shard().sessions["u1"])
        #expect(b.foldMatchesRollups("2026-07"))
    }

    @Test func tailParseKeepsTheProjectWithoutAStoredCwd() {
        let b = VaultBed()
        defer { b.cleanUp() }
        let path = b.session("u1", [B.line("2026-07-10T15:00:00Z")])
        b.ingest()
        let keyBefore = b.shard().sessions["u1"]?.projectKey
        b.append(path, mtime: B.now.addingTimeInterval(-300), [B.line("2026-07-10T16:00:00Z", cwd: nil)])
        b.ingest()
        #expect(b.shard().sessions["u1"]?.projectKey == keyBefore)
        #expect(b.shard().sessions["u1"]?.projectName == "api")
    }

    @Test func tornLastLineCompletingAfterTheCheckpointConverges() {
        let b = VaultBed()
        defer { b.cleanUp() }
        let full = B.line("2026-07-10T16:00:00Z", input: 200, output: 0)
        b.home.dir(".claude/projects/c--Users-x-Projects-api")
        let path = b.home.at(".claude/projects/c--Users-x-Projects-api/u1.jsonl")
        b.write(path, B.line("2026-07-10T15:00:00Z") + "\n" + String(full.prefix(40)),
                mtime: B.now.addingTimeInterval(-600))
        b.ingest()
        #expect(b.rowTotal() == 150)

        CCLogs.append(path, String(full.dropFirst(40)) + "\n")
        CCLogs.setModified(path, B.now.addingTimeInterval(-300))
        let r2 = b.ingest()
        #expect(r2.filesTailParsed == 1)
        #expect(b.rowTotal() == 350)
        #expect(b.shard().sessions["u1"]?.skippedLines == 0)
    }

    @Test func guardMismatchForcesAFullReparse() {
        let b = VaultBed()
        defer { b.cleanUp() }
        let path = b.session("u1", [B.line("2026-07-10T15:00:00Z")])
        b.ingest()
        b.write(path, [
            B.line("2026-07-10T14:00:00Z", input: 999, output: 0),
            B.line("2026-07-10T15:30:00Z", input: 1, output: 0),
            B.line("2026-07-10T16:00:00Z", input: 2, output: 0),
        ].joined(separator: "\n") + "\n", mtime: B.now.addingTimeInterval(-240))
        let r2 = b.ingest()
        #expect(r2.filesFullParsed == 1)
        #expect(r2.filesTailParsed == 0)
        #expect(b.rowTotal() == 1002)
    }

    @Test func shrunkFileForcesAFullReparse() {
        let b = VaultBed()
        defer { b.cleanUp() }
        b.session("u1", [B.line("2026-07-10T15:00:00Z"), B.line("2026-07-10T16:00:00Z")])
        b.ingest()
        #expect(b.rowTotal() == 300)
        b.session("u1", mtime: B.now.addingTimeInterval(-180), [B.line("2026-07-10T15:00:00Z", input: 10, output: 0)])
        b.ingest()
        #expect(b.rowTotal() == 10)
    }

    @Test func quietFileIsVerifiedThenSealedThenSkipped() {
        let b = VaultBed()
        defer { b.cleanUp() }
        let path = b.session("u1", mtime: B.now.addingTimeInterval(-7200), [B.line("2026-07-10T15:00:00Z")])
        let r1 = b.ingest()
        #expect(r1.filesFullParsed == 1)
        #expect(b.store.loadCheckpoints(".claude").entries.values.first?.sealed == false)

        let r2 = b.ingest()
        #expect(r2.filesFullParsed == 1)
        #expect(b.store.loadCheckpoints(".claude").entries.values.first?.sealed == true)
        #expect(b.rowTotal() == 150)

        let r3 = b.ingest()
        #expect(r3.filesSkipped == 1)
        #expect(r3.filesFullParsed + r3.filesTailParsed == 0)

        b.append(path, mtime: B.now.addingTimeInterval(-5400), [B.line("2026-07-10T16:00:00Z", input: 7, output: 0)])
        let r4 = b.ingest()
        #expect(r4.filesFullParsed == 1)
        #expect(b.rowTotal() == 157)
        #expect(b.store.loadCheckpoints(".claude").entries.values.first?.sealed == false)
    }

    @Test func unreadableFileAdvancesNothingAndRecoversNextCycle() {
        let b = VaultBed()
        defer { b.cleanUp() }
        let path = b.session("u1", [B.line("2026-07-10T15:00:00Z")])
        chmod(path, 0o000)
        let r1 = b.ingest()
        #expect(r1.filesFailed == 1)
        #expect(b.store.loadSessionShard(".claude", "2026-07").0 == .missing)
        #expect(b.store.loadCheckpoints(".claude").entries.isEmpty)
        // The log names the operation and the error type, never a path.
        #expect(!b.logs.isEmpty)
        #expect(b.logs.allSatisfy { !$0.contains("/") && !$0.contains("\\") && !$0.contains("api") })

        chmod(path, 0o644)
        let r2 = b.ingest()
        #expect(r2.filesFullParsed == 1)
        #expect(b.rowTotal() == 150)
    }

    @Test func quarantinedShardInvalidatesCheckpointsAndAFullReingestConverges() {
        let b = VaultBed()
        defer { b.cleanUp() }
        b.session("u1", [B.line("2026-07-10T15:00:00Z")])
        b.ingest()
        FileManager.default.createFile(atPath: b.rootPath() + "/sessions-2026-07.json", contents: Data("{corrupt".utf8))
        b.session("u1", mtime: B.now.addingTimeInterval(-300), [B.line("2026-07-10T15:00:00Z"), B.line("2026-07-10T16:00:00Z")])

        let r2 = b.ingest()
        #expect(r2.rootsAborted == 1)
        #expect(b.files(in: b.rootPath()).filter { $0.hasSuffix(".bad") }.count == 1)
        #expect(!FileManager.default.fileExists(atPath: b.rootPath() + "/checkpoints.json"))

        let r3 = b.ingest()
        #expect(r3.rootsAborted == 0)
        #expect(b.rowTotal() == 300)
        #expect(b.files(in: b.rootPath()).filter { $0.hasSuffix(".bad") }.count == 1)
        #expect(b.foldMatchesRollups("2026-07"))
    }

    @Test func checkpointPruneThenFileResurrectionIsHarmless() {
        let b = VaultBed()
        defer { b.cleanUp() }
        let path = b.session("u1", [B.line("2026-07-10T15:00:00Z")])
        b.ingest()
        try? FileManager.default.removeItem(atPath: path)
        let later = B.now.addingTimeInterval(8 * 86_400)
        b.ingest(now: later)
        #expect(b.store.loadCheckpoints(".claude").entries.isEmpty)
        #expect(b.rowTotal() == 150)                   // the record outlives its source

        b.session("u1", mtime: later.addingTimeInterval(-600), [B.line("2026-07-10T15:00:00Z")])
        b.ingest(now: later.addingTimeInterval(-300))
        #expect(b.shard().sessions.count == 1)
        #expect(b.rowTotal() == 150)
    }

    @Test func recordingEndedMidCycleLeavesTheVaultByteIdentical() {
        let b = VaultBed()
        defer { b.cleanUp() }
        let path = b.session("u1", [B.line("2026-07-10T15:00:00Z")])
        b.ingest()
        let before = b.snapshot()
        b.append(path, mtime: B.now.addingTimeInterval(-300), [B.line("2026-07-10T16:00:00Z", input: 200, output: 0)])
        let r = b.ingest(stillRecording: { _ in false })
        #expect(r.acquired)
        #expect(r.filesSeen == 1)
        #expect(r.filesTailParsed == 1)          // parsed as it would have been...
        #expect(r.rootsAborted == 0)             // ...but a clean skip
        #expect(r.rootsWithdrawn == 1)
        #expect(b.snapshot() == before)
    }

    @Test func recordingEndedFromTheStartKeepsAFreshVaultEmpty() {
        let b = VaultBed()
        defer { b.cleanUp() }
        b.session("u1", [B.line("2026-07-10T15:00:00Z")])
        let r = b.ingest(stillRecording: { _ in false })
        #expect(r.acquired)
        #expect(r.filesFullParsed == 1)
        #expect(r.rootsAborted == 0)
        #expect(!b.store.rootExists(".claude"))
    }

    @Test func shouldStopAbandonsTheFolderBeforeAnyWrite() {
        let b = VaultBed()
        defer { b.cleanUp() }
        b.session("u1", [B.line("2026-07-10T15:00:00Z")])
        b.session("u2", [B.line("2026-07-10T16:00:00Z")])
        var asked = 0
        let r = b.ingester().ingestOnce([b.root()], now: B.now, shouldStop: { _ in
            asked += 1
            return asked > 1                     // stop before the second file
        })
        #expect(r.filesSeen == 1)
        #expect(r.rootsWithdrawn == 1)
        #expect(!b.store.rootExists(".claude"))
    }

    @Test func firstTsMonthMoveOnReingestLeavesOneLogicalSession() {
        let b = VaultBed()
        defer { b.cleanUp() }
        let now = VaultTime.parse("2026-08-06T00:00:00Z")!
        b.session("u1", mtime: now.addingTimeInterval(-600), [B.line("2026-08-05T20:00:00Z", input: 200, output: 0)])
        b.ingest(now: now)
        #expect(b.shard("2026-08").sessions["u1"]?.continuation == false)

        b.session("u1", mtime: now.addingTimeInterval(-300), [
            B.line("2026-07-31T20:00:00Z", input: 100, output: 0),
            B.line("2026-08-05T20:00:00Z", input: 200, output: 0),
        ])
        b.ingest(now: now)
        #expect(b.shard("2026-07").sessions["u1"]?.continuation == false)
        #expect(b.shard("2026-08").sessions["u1"]?.continuation == true)
        #expect(b.rowTotal("2026-07") == 100)
        #expect(b.rowTotal("2026-08") == 200)

        b.session("u1", mtime: now.addingTimeInterval(-180), [B.line("2026-08-05T20:00:00Z", input: 200, output: 0)])
        b.ingest(now: now)
        #expect(b.shard("2026-07").sessions["u1"] == nil)
        #expect(b.shard("2026-08").sessions["u1"]?.continuation == false)
        #expect(b.foldMatchesRollups("2026-07"))
        #expect(b.foldMatchesRollups("2026-08"))
    }

    // MARK: VaultSubagentCoverageTests

    @Test func nestedFileGetsItsParentAndItsOwnRow() {
        let b = VaultBed()
        defer { b.cleanUp() }
        b.session(B.parentUuid, [B.line("2026-07-10T15:00:00Z")])
        b.nested(parent: B.parentUuid, "agent-x", [B.line("2026-07-10T16:00:00Z", input: 200, output: 0)])
        b.ingest()
        let shard = b.shard()
        #expect(shard.sessions.count == 2)
        #expect(shard.sessions[B.parentUuid]?.parentSession == nil)
        #expect(shard.sessions["agent-x"]?.parentSession == B.parentUuid)
        #expect(shard.sessions["agent-x"]?.total == 200)
    }

    @Test func nonUuidSubfolderYieldsNoParent() {
        let b = VaultBed()
        defer { b.cleanUp() }
        b.home.dir(".claude/projects/c--Users-x-Projects-api/scratch")
        b.write(b.home.at(".claude/projects/c--Users-x-Projects-api/scratch/stray.jsonl"),
                B.line("2026-07-10T15:00:00Z") + "\n", mtime: B.now.addingTimeInterval(-600))
        b.ingest()
        #expect(b.shard().sessions["stray"] != nil)
        #expect(b.shard().sessions["stray"]?.parentSession == nil)
    }

    @Test func walkVersionUpgradeReingestsOnceAndMatchesAFreshVault() throws {
        let b = VaultBed()
        defer { b.cleanUp() }
        b.session(B.parentUuid, [B.line("2026-07-10T15:00:00Z")])
        b.nested(parent: B.parentUuid, "agent-x", [B.line("2026-07-10T16:00:00Z", input: 200, output: 0)])
        b.ingest()

        // Regress to a v1 vault: no walk_version, and the nested row a one-level walk never saw.
        let metaPath = b.rootPath() + "/meta.json"
        let meta = String(decoding: FileManager.default.contents(atPath: metaPath) ?? Data(), as: UTF8.self)
        let stripped = meta.replacingOccurrences(of: ",\"walk_version\":2", with: "")
            .replacingOccurrences(of: "\"walk_version\":2,", with: "")
        #expect(stripped != meta)
        FileManager.default.createFile(atPath: metaPath, contents: Data(stripped.utf8))
        var s1 = b.shard()
        s1.sessions.removeValue(forKey: "agent-x")
        try b.store.saveSessionShard(".claude", "2026-07", s1)

        let r2 = b.ingest()
        #expect(r2.filesFullParsed >= 2)

        let fresh = VaultBed()
        defer { fresh.cleanUp() }
        fresh.ingester().ingestOnce([VaultRoot(folder: b.home.at(".claude"), id: ".claude", names: .names)], now: B.now)
        #expect(fresh.shard().sessions == b.shard().sessions)

        let r3 = b.ingest()
        #expect(r3.filesFullParsed + r3.filesTailParsed == 0)
        #expect(r3.filesSkipped == 2)
    }

    @Test func upgradeKeepsRowsOfAgedOutFiles() {
        let b = VaultBed()
        defer { b.cleanUp() }
        let path = b.session("u-old", [B.line("2026-07-01T15:00:00Z")])
        b.ingest()
        try? FileManager.default.removeItem(atPath: path)
        let metaPath = b.rootPath() + "/meta.json"
        let meta = String(decoding: FileManager.default.contents(atPath: metaPath) ?? Data(), as: UTF8.self)
        FileManager.default.createFile(atPath: metaPath, contents: Data(meta
            .replacingOccurrences(of: ",\"walk_version\":2", with: "")
            .replacingOccurrences(of: "\"walk_version\":2,", with: "").utf8))
        b.ingest()
        #expect(b.rowTotal(uuid: "u-old") == 150)
    }

    @Test func vaultDayTotalMatchesTheLiveReaderWithNestedFiles() {
        let b = VaultBed()
        defer { b.cleanUp() }
        b.session(B.parentUuid, [B.line("2026-07-11T15:00:00Z", input: 700, output: 70)])
        b.nested(parent: B.parentUuid, "agent-x", [B.line("2026-07-11T15:00:00Z", input: 20, output: 2)])
        b.ingest()
        var cal = Calendar(identifier: .gregorian)
        cal.timeZone = B.cst
        let live = CCLogReader(root: b.home.at(".claude")).tokensByDay(daysBack: 30, now: B.now, calendar: cal)
        let day = CCLocalDay(year: 2026, month: 7, day: 11)
        let vaultDay = b.shard().sessions.values.compactMap { $0.byDay[day.key]?.total }.reduce(0, +)
        #expect(live[day] == vaultDay)
        #expect(vaultDay == 792)                 // 770 main + 22 agent
    }

    // MARK: VaultLogicalSessionTests (ingest side)

    @Test func rollupSessionsCountDistinctLogicalSessions() {
        let b = VaultBed()
        defer { b.cleanUp() }
        b.session(B.parentUuid, [B.line("2026-07-10T15:00:00Z")])
        b.nested(parent: B.parentUuid, "agent-x", [B.line("2026-07-10T16:00:00Z", input: 200, output: 0)])
        b.nested(parent: B.parentUuid, "agent-y", [B.line("2026-07-10T17:00:00Z", input: 50, output: 0)])
        b.ingest()
        #expect(b.rollups().days["2026-07-10"]?.sessions == 1)
        #expect(b.rollups().days["2026-07-10"]?.total == 400)
    }

    @Test func worktreeAndSubfolderSessionsFoldIntoTheRepo() {
        let b = VaultBed()
        defer { b.cleanUp() }
        b.session("u1", [B.line("2026-07-10T15:00:00Z", cwd: "/Users/x/Projects/api/.claude/worktrees/fix-1")])
        b.session("u2", [B.line("2026-07-10T16:00:00Z", cwd: "/Users/x/Projects/api/.worktrees/spike")])
        b.session("u3", [B.line("2026-07-10T17:00:00Z", cwd: "/Users/x/Projects/api/src/lib")])
        let ing = VaultIngester(store: b.store, writerVersion: "test", timeZone: B.cst,
                                dirHoldsGit: { $0 == "/Users/x/Projects/api" })
        ing.ingestOnce([b.root()], now: B.now)
        let names = Set(b.shard().sessions.values.map(\.projectName))
        #expect(names == ["api"])
        let window = VaultReader(store: b.store).readWindow([".claude"], from: CCLocalDay(key: "2026-07-01")!,
                                                            toExclusive: CCLocalDay(key: "2026-07-12")!)
        #expect(window.byProjectName == ["api": 450])
    }

    @Test func readsWhatTheIngesterWrote() {
        let b = VaultBed()
        defer { b.cleanUp() }
        b.session(VaultBed.parentUuid, [VaultBed.line("2026-07-10T15:00:00Z", skill: "code-review")])
        b.nested(parent: VaultBed.parentUuid, "agent-x", [VaultBed.line("2026-07-10T16:00:00Z", input: 200, output: 0)])
        b.ingest()
        let r = VaultReader(store: b.store)
        let sessions = r.readSessions([".claude"])
        #expect(sessions.count == 1)
        #expect(sessions.first?.total == 350)
        #expect(sessions.first?.agentCount == 1)
        let w = r.readWindow([".claude"], from: CCLocalDay(key: "2026-07-01")!, toExclusive: CCLocalDay(key: "2026-07-12")!)
        #expect(w.byDay[CCLocalDay(key: "2026-07-10")!] == 350)
        #expect(w.bySkill == ["code-review": 150])
        #expect(w.sessionsByDay[CCLocalDay(key: "2026-07-10")!] == 1)
        #expect(r.birthDate([".claude"]) == CCLocalDay(key: "2026-07-12")!)
    }

    // MARK: Project names (Mac)

    @Test func hiddenNeverStoresANameOrACwd() throws {
        let b = VaultBed()
        defer { b.cleanUp() }
        let secret = "/Users/someone/Projects/secret-merger"
        let path = b.session("u1", [B.line("2026-07-10T15:00:00Z", cwd: secret)])
        b.nested(parent: "u1", "agent-x", [B.line("2026-07-10T16:00:00Z", cwd: secret)])
        b.ingest([b.root(names: .hidden)])
        b.append(path, mtime: B.now.addingTimeInterval(-300), [B.line("2026-07-10T17:00:00Z", cwd: secret)])
        b.ingest([b.root(names: .hidden)])

        let row = try #require(b.shard().sessions["u1"])
        #expect(row.projectName == VaultHiddenName.of("secret-merger"))
        #expect(row.projectKey == row.projectName)
        #expect(VaultHiddenName.isHidden(row.projectName))
        #expect(row.cwd == nil)
        for (_, bytes) in b.snapshot() {
            let text = String(decoding: bytes, as: UTF8.self)
            #expect(!text.contains("secret"))
            #expect(!text.contains("someone"))
            #expect(!text.contains("Projects"))
        }
        // Still grouped by project.
        #expect(b.rollups().days["2026-07-10"]?.byProject == [row.projectKey: row.total + 150])
    }

    @Test func hiddenNameIsStableShortAndNotTheName() {
        let h = VaultHiddenName.of("api")
        #expect(h == VaultHiddenName.of("api"))
        #expect(h != VaultHiddenName.of("web"))
        #expect(h.count == 12 && h.hasPrefix("p-"))
        #expect(h == "p-" + VaultStore.sha256Hex("api").prefix(10))
        #expect(VaultReader.projectNameOf(h) == h)
        #expect(!VaultHiddenName.isHidden("api"))
    }

    @Test func switchingToHiddenAppliesToNewRowsAndToRowsBeingRewritten() {
        let b = VaultBed()
        defer { b.cleanUp() }
        b.session("old", mtime: B.now.addingTimeInterval(-7200), [B.line("2026-07-09T15:00:00Z")])
        let live = b.session("live", [B.line("2026-07-10T15:00:00Z")])
        b.ingest([b.root(names: .names)])
        b.ingest([b.root(names: .names)])                         // seals the quiet one
        b.append(live, mtime: B.now.addingTimeInterval(-300), [B.line("2026-07-10T16:00:00Z", cwd: nil)])
        b.session("new", [B.line("2026-07-10T17:00:00Z")])
        b.ingest([b.root(names: .hidden)])

        let s = b.shard()
        #expect(s.sessions["old"]?.projectName == "api")         // kept as it was recorded
        #expect(s.sessions["live"]?.projectName == VaultHiddenName.of("api"))   // its tail had no cwd
        #expect(s.sessions["new"]?.projectName == VaultHiddenName.of("api"))
    }

    // MARK: Cost

    @Test func largeSyntheticFolderCost() throws {
        // 200 sessions x 500 lines (100k lines, about half assistant events), the shape of a busy
        // month. Reports the times; asserts only generous bounds so a slow CI box doesn't flake.
        let b = VaultBed()
        defer { b.cleanUp() }
        let filler = "{\"type\":\"user\",\"message\":{\"content\":\"" + String(repeating: "x", count: 600) + "\"}}"
        var bytes = 0
        for s in 0..<200 {
            var lines: [String] = []
            lines.reserveCapacity(500)
            for i in 0..<250 {
                let ts = VaultTime.iso(B.now.addingTimeInterval(-Double(s * 3600 + i * 10)))
                lines.append(B.line(ts, model: i % 3 == 0 ? "claude-sonnet-5" : "claude-fable-5",
                                    input: 120, output: 30, cacheRead: 4000))
                lines.append(filler)
            }
            let path = b.session(String(format: "s%03d", s), lines)
            bytes += (try? FileManager.default.attributesOfItem(atPath: path)[.size] as? Int) ?? 0
        }
        let t0 = Date()
        let first = b.ingest()
        let full = Date().timeIntervalSince(t0)
        let t1 = Date()
        let second = b.ingest()
        let steady = Date().timeIntervalSince(t1)
        print("vault cost: \(first.filesFullParsed) files, \(bytes / 1_048_576) MB, first ingest \(String(format: "%.2f", full)) s, unchanged re-ingest \(String(format: "%.3f", steady)) s")
        #expect(first.filesFullParsed == 200)
        #expect(second.filesSkipped == 200)
        #expect(b.shard().sessions.count + b.shard("2026-06").sessions.count >= 200)
        #expect(full < 60)
        #expect(steady < 10)
    }
}
