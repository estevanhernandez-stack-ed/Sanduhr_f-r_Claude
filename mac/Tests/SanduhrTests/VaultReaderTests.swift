import Foundation
import Testing
@testable import Sanduhr

/// Windows `VaultReaderTests`, `VaultLedgerCsvTests` and the reader side of
/// `VaultLogicalSessionTests`: window merges, weeks with the "no record" flag, slice and
/// logical-session folds, scope math, meta facts and the ledger CSV, plus the Mac's by-model and
/// by-tier views. Shards are written through the store directly.
@Suite("Vault reader")
struct VaultReaderTests {
    static func day(_ s: String) -> CCLocalDay { CCLocalDay(key: s)! }

    static func row(_ projectKey: String, _ firstTs: String, _ lastTs: String, continuation: Bool,
                    parent: String? = nil, _ days: [(String, Int64)]) -> VaultSessionRow {
        var row = VaultSessionRow()
        row.projectKey = projectKey
        row.projectName = VaultReader.projectNameOf(projectKey)
        row.parentSession = parent
        row.firstTs = firstTs
        row.lastTs = lastTs
        row.utcOffsetMin = -300
        row.eventCount = continuation ? 0 : Int64(days.count)
        row.continuation = continuation
        row.cacheTokens = continuation ? nil : VaultCacheTokens(read: 1, creation: 1)
        for (d, total) in days {
            row.byDay[d] = VaultDayBucket(total: total, byModel: ["claude-fable-5": total])
        }
        VaultRowMath.recomputeRowAggregates(&row)
        return row
    }

    static func saveShard(_ b: VaultBed, _ root: String, _ month: String, _ rows: [(String, VaultSessionRow)]) {
        var shard = VaultSessionShard(schemaVersion: 1, writerVersion: "test")
        for (uuid, r) in rows { shard.sessions[uuid] = r }
        try? b.store.saveSessionShard(root, month, shard)
    }

    static func saveRollup(_ b: VaultBed, _ root: String, _ month: String,
                           _ days: [(day: String, total: Int64, project: String, skill: String)],
                           model: String = "claude-fable-5") {
        var shard = VaultRollupShard()
        for d in days {
            var r = shard.days[d.day] ?? VaultRollupDay()
            r.total += d.total
            r.sessions += 1
            r.byModel[model, default: 0] += d.total
            r.byProject[d.project, default: 0] += d.total
            if !d.skill.isEmpty { r.bySkill[d.skill, default: 0] += d.total }
            shard.days[d.day] = r
        }
        b.store.saveRollupShard(root, month, shard)
    }

    static func meta(_ since: String, _ ranges: [(String, String)], last: String = "2026-07-12T18:00:00.000000+00:00") -> VaultRootMeta {
        VaultRootMeta(since: since, covered: ranges.map { VaultDateRange(from: $0.0, to: $0.1) }, lastIngestTs: last)
    }

    @Test func windowMergesFoldersAndProjectsByDisplayName() {
        let b = VaultBed()
        defer { b.cleanUp() }
        Self.saveRollup(b, ".claude", "2026-07", [("2026-07-10", 100, "api~aaaaaaaa", "code-review"),
                                                  ("2026-07-11", 50, "api~aaaaaaaa", "")])
        Self.saveRollup(b, ".claude-personal", "2026-07", [("2026-07-10", 30, "api~bbbbbbbb", "")],
                        model: "claude-opus-4-7")
        let w = VaultReader(store: b.store).readWindow([".claude", ".claude-personal"],
                                                       from: Self.day("2026-07-01"), toExclusive: Self.day("2026-07-12"))
        #expect(w.byDay[Self.day("2026-07-10")] == 130)
        #expect(w.byDay[Self.day("2026-07-11")] == 50)
        #expect(w.byProjectName["api"] == 180)
        #expect(w.bySkill["code-review"] == 100)
        #expect(w.byModel == ["claude-fable-5": 150, "claude-opus-4-7": 30])
        #expect(w.byTier == ["seven_day_fable": 150, Tier.sevenDayOpus.rawValue: 30])
        #expect(w.sessionsByDay[Self.day("2026-07-10")] == 2)
        #expect(w.total == 180)
    }

    @Test func windowExcludesTheEndDay() {
        let b = VaultBed()
        defer { b.cleanUp() }
        Self.saveRollup(b, ".claude", "2026-07", [("2026-07-11", 50, "api~aaaaaaaa", ""),
                                                  ("2026-07-12", 999, "api~aaaaaaaa", "")])
        let w = VaultReader(store: b.store).readWindow([".claude"], from: Self.day("2026-07-01"),
                                                       toExclusive: Self.day("2026-07-12"))
        #expect(w.byDay[Self.day("2026-07-12")] == nil)
        #expect(w.total == 50)
        #expect(w.byProjectName["api"] == 50)
    }

    @Test func windowSpansMonths() {
        let b = VaultBed()
        defer { b.cleanUp() }
        Self.saveRollup(b, ".claude", "2026-06", [("2026-06-30", 10, "api~aaaaaaaa", "")])
        Self.saveRollup(b, ".claude", "2026-07", [("2026-07-01", 20, "api~aaaaaaaa", "")])
        let w = VaultReader(store: b.store).readWindow([".claude"], from: Self.day("2026-06-15"),
                                                       toExclusive: Self.day("2026-07-12"))
        #expect(w.total == 30)
        #expect(VaultReader.months(from: Self.day("2026-11-20"), toExclusive: Self.day("2027-02-01"))
                == ["2026-11", "2026-12", "2027-01"])
    }

    @Test func weeksStartMondayAndFlagTheCurrentOne() {
        let b = VaultBed()
        defer { b.cleanUp() }
        Self.saveRollup(b, ".claude", "2026-07", [("2026-07-06", 100, "api~aaaaaaaa", ""),
                                                  ("2026-07-01", 40, "api~aaaaaaaa", "")])
        b.store.saveMeta(".claude", Self.meta("2026-06-01", [("2026-06-01", "2026-07-12")]))
        let weeks = VaultReader(store: b.store).readWeeks([".claude"], weeks: 4, today: Self.day("2026-07-12"))
        #expect(weeks.count == 4)
        #expect(weeks[3].weekStart == Self.day("2026-07-06"))
        #expect(weeks[3].isCurrent)
        #expect(!weeks[2].isCurrent)
        #expect(weeks[3].total == 100)
        #expect(weeks[2].total == 40)
        #expect(weeks.allSatisfy { !$0.hasNoRecordGap })
    }

    @Test func weeksFlagNoRecordForUncoveredAndPreVaultDays() {
        let b = VaultBed()
        defer { b.cleanUp() }
        b.store.saveMeta(".claude", Self.meta("2026-07-01", [("2026-06-25", "2026-07-01"), ("2026-07-08", "2026-07-12")]))
        let weeks = VaultReader(store: b.store).readWeeks([".claude"], weeks: 4, today: Self.day("2026-07-12"))
        #expect(weeks[0].weekStart == Self.day("2026-06-15"))
        #expect(weeks[0].hasNoRecordGap)
        #expect(weeks[1].hasNoRecordGap)
        #expect(weeks[2].hasNoRecordGap)
        #expect(weeks[3].isCurrent)
        #expect(weeks[3].hasNoRecordGap)
    }

    @Test func coverageIsTheIntersectionOfFolders() {
        let b = VaultBed()
        defer { b.cleanUp() }
        b.store.saveMeta(".claude", Self.meta("2026-07-01", [("2026-07-01", "2026-07-12")]))
        b.store.saveMeta(".claude-personal", Self.meta("2026-07-05", [("2026-07-05", "2026-07-12")]))
        let r = VaultReader(store: b.store)
        let roots = [".claude", ".claude-personal"]
        #expect(r.isDayCovered(roots, Self.day("2026-07-06")))
        #expect(!r.isDayCovered(roots, Self.day("2026-07-03")))
        #expect(!r.isDayCovered(roots, Self.day("2026-06-20")))
        #expect(r.isDayCovered([".claude"], Self.day("2026-07-03")))
    }

    @Test func sessionsMergeSlicesByUuidWithinAFolder() throws {
        let b = VaultBed()
        defer { b.cleanUp() }
        Self.saveShard(b, ".claude", "2026-07", [
            ("u1", Self.row("api~aaaaaaaa", "2026-07-31T20:00:00+00:00", "2026-08-01T22:00:00+00:00",
                            continuation: false, [("2026-07-31", 100)])),
        ])
        Self.saveShard(b, ".claude", "2026-08", [
            ("u1", Self.row("api~aaaaaaaa", "2026-07-31T20:00:00+00:00", "2026-08-01T22:00:00+00:00",
                            continuation: true, [("2026-08-01", 200)])),
            ("u2", Self.row("web~cccccccc", "2026-08-02T10:00:00+00:00", "2026-08-02T11:00:00+00:00",
                            continuation: false, [("2026-08-02", 50)])),
        ])
        let sessions = VaultReader(store: b.store).readSessions([".claude"])
        #expect(sessions.count == 2)
        let u1 = try #require(sessions.first { $0.uuid == "u1" })
        #expect(u1.total == 300)
        #expect(u1.byDay.count == 2)
        #expect(u1.root == ".claude")
        #expect(u1.cache != nil)
        #expect(u1.firstTs == VaultTime.parse("2026-07-31T20:00:00Z"))
    }

    @Test func sameUuidInTwoFoldersStaysTwoSessions() {
        let b = VaultBed()
        defer { b.cleanUp() }
        let r = Self.row("api~aaaaaaaa", "2026-07-10T15:00:00+00:00", "2026-07-10T16:00:00+00:00",
                         continuation: false, [("2026-07-10", 100)])
        Self.saveShard(b, ".claude", "2026-07", [("u1", r)])
        Self.saveShard(b, ".claude-personal", "2026-07", [("u1", r)])
        let sessions = VaultReader(store: b.store).readSessions([".claude", ".claude-personal"])
        #expect(sessions.count == 2)
        #expect(Set(sessions.map(\.root)).count == 2)
    }

    @Test func tokensInScopeSumsOnlyDaysInside() {
        let info = VaultSessionInfo(
            uuid: "u1", root: ".claude", projectKey: "api~aaaaaaaa", projectName: "api", cwd: nil,
            firstTs: VaultTime.parse("2026-07-08T10:00:00Z")!, lastTs: VaultTime.parse("2026-07-11T10:00:00Z")!,
            total: 600, byModel: ["claude-fable-5": 600], bySkill: nil,
            byDay: ["2026-07-08": VaultDayBucket(total: 100), "2026-07-10": VaultDayBucket(total: 200),
                    "2026-07-11": VaultDayBucket(total: 300)],
            cache: nil, agentCount: 0, agentTokens: 0)
        #expect(VaultReader.tokensInScope(info, from: Self.day("2026-07-10"), through: Self.day("2026-07-11")) == 500)
        #expect(VaultReader.tokensInScope(info, from: Self.day("2026-07-11"), through: Self.day("2026-07-11")) == 300)
        #expect(VaultReader.tokensInScope(info, from: Self.day("0001-01-01"), through: Self.day("9999-12-31")) == 600)
        #expect(VaultReader.tokensInScope(info, from: Self.day("2026-07-12"), through: Self.day("2026-07-12")) == 0)
    }

    @Test func metaFactsBirthOldestIngestAndUnstartedFolders() {
        let b = VaultBed()
        defer { b.cleanUp() }
        let r = VaultReader(store: b.store)
        let roots = [".claude", ".claude-personal"]
        #expect(r.birthDate(roots) == nil)
        #expect(r.lastSuccessfulIngest(roots) == nil)
        b.store.saveMeta(".claude", VaultRootMeta(since: "2026-07-01", lastIngestTs: "2026-07-12T18:00:00.000000+00:00"))
        #expect(r.birthDate(roots) == Self.day("2026-07-01"))
        #expect(r.lastSuccessfulIngest(roots) == nil)
        b.store.saveMeta(".claude-personal", VaultRootMeta(since: "2026-06-20", lastIngestTs: "2026-07-12T17:00:00.000000+00:00"))
        #expect(r.birthDate(roots) == Self.day("2026-06-20"))
        #expect(r.lastSuccessfulIngest(roots) == VaultTime.parse("2026-07-12T17:00:00Z"))
    }

    @Test func corruptShardsReadEmptyAndAreNeverTouched() {
        let b = VaultBed()
        defer { b.cleanUp() }
        let dir = b.rootPath()
        try? FileManager.default.createDirectory(atPath: dir, withIntermediateDirectories: true)
        FileManager.default.createFile(atPath: dir + "/sessions-2026-07.json", contents: Data("{corrupt".utf8))
        FileManager.default.createFile(atPath: dir + "/rollups-2026-07.json", contents: Data("{corrupt".utf8))
        let r = VaultReader(store: b.store)
        #expect(r.readSessions([".claude"]).isEmpty)
        #expect(r.readWindow([".claude"], from: Self.day("2026-07-01"), toExclusive: Self.day("2026-07-12")).byDay.isEmpty)
        #expect(FileManager.default.fileExists(atPath: dir + "/sessions-2026-07.json"))
        #expect(!b.files(in: dir).contains { $0.hasSuffix(".bad") })
    }

    @Test func projectNameOfStripsTheHashSuffix() {
        #expect(VaultReader.projectNameOf("api~3f2a91cc") == "api")
        #expect(VaultReader.projectNameOf("(none)") == "(none)")
        #expect(VaultReader.projectNameOf("odd~name~12345678") == "odd~name")
    }

    @Test func topProjectsAndModelsRankLargestFirst() {
        let b = VaultBed()
        defer { b.cleanUp() }
        Self.saveRollup(b, ".claude", "2026-07", [("2026-07-10", 100, "api~a", ""), ("2026-07-10", 300, "web~b", ""),
                                                  ("2026-07-11", 50, "cli~c", "")])
        let r = VaultReader(store: b.store)
        let top = r.topProjects([".claude"], from: Self.day("2026-07-01"), toExclusive: Self.day("2026-07-12"), top: 2)
        #expect(top.map(\.name) == ["web", "api"])
        #expect(top.map(\.total) == [300, 100])
        let models = r.topModels([".claude"], from: Self.day("2026-07-01"), toExclusive: Self.day("2026-07-12"), top: 5)
        #expect(models.map(\.name) == ["claude-fable-5"])
    }

    // MARK: Logical sessions (VaultLogicalSessionTests)

    @Test func membersFoldIntoOneLogicalSessionWithAgentStats() throws {
        let b = VaultBed()
        defer { b.cleanUp() }
        Self.saveShard(b, ".claude", "2026-07", [
            ("parent-1", Self.row("api~aaaaaaaa", "2026-07-10T10:00:00+00:00", "2026-07-10T18:00:00+00:00",
                                  continuation: false, [("2026-07-10", 100)])),
            ("agent-x", Self.row("api~aaaaaaaa", "2026-07-10T11:00:00+00:00", "2026-07-10T12:00:00+00:00",
                                 continuation: false, parent: "parent-1", [("2026-07-10", 200)])),
            ("agent-y", Self.row("api~aaaaaaaa", "2026-07-10T13:00:00+00:00", "2026-07-10T19:00:00+00:00",
                                 continuation: false, parent: "parent-1", [("2026-07-10", 50)])),
        ])
        let sessions = VaultReader(store: b.store).readSessions([".claude"])
        #expect(sessions.count == 1)
        let s = try #require(sessions.first)
        #expect(s.uuid == "parent-1")
        #expect(s.total == 350)
        #expect(s.byDay["2026-07-10"]?.total == 350)
        #expect(s.agentCount == 2)
        #expect(s.agentTokens == 250)
        #expect(s.firstTs == VaultTime.parse("2026-07-10T10:00:00Z"))
        #expect(s.lastTs == VaultTime.parse("2026-07-10T19:00:00Z"))
    }

    @Test func agentOnlySessionSurvivesWhenTheMainTranscriptAgedOut() throws {
        let b = VaultBed()
        defer { b.cleanUp() }
        Self.saveShard(b, ".claude", "2026-07", [
            ("agent-x", Self.row("api~aaaaaaaa", "2026-07-10T11:00:00+00:00", "2026-07-10T12:00:00+00:00",
                                 continuation: false, parent: "parent-gone", [("2026-07-10", 200)])),
        ])
        let s = try #require(VaultReader(store: b.store).readSessions([".claude"]).first)
        #expect(s.uuid == "parent-gone")
        #expect(s.projectName == "api")
        #expect(s.agentCount == 1)
        #expect(s.agentTokens == 200)
    }

    @Test func sessionsWithoutParentsAreUnchanged() throws {
        let b = VaultBed()
        defer { b.cleanUp() }
        Self.saveShard(b, ".claude", "2026-07", [
            ("u1", Self.row("api~aaaaaaaa", "2026-07-10T10:00:00+00:00", "2026-07-10T11:00:00+00:00",
                            continuation: false, [("2026-07-10", 100)])),
        ])
        let s = try #require(VaultReader(store: b.store).readSessions([".claude"]).first)
        #expect(s.uuid == "u1")
        #expect(s.agentCount == 0)
        #expect(s.agentTokens == 0)
    }

    @Test func windowCarriesTheSplitAndCoveredSetBatchesCoverage() {
        let b = VaultBed()
        defer { b.cleanUp() }
        var day = VaultRollupDay()
        day.total = 130
        day.input = 100
        day.output = 30
        day.sessions = 1
        day.byModel = ["claude-fable-5": 130]
        day.byProject = ["api~aaaaaaaa": 130]
        b.store.saveRollupShard(".claude", "2026-07", VaultRollupShard(days: ["2026-07-10": day]))
        b.store.saveMeta(".claude", VaultRootMeta(since: "2026-07-01",
                                                  covered: [VaultDateRange(from: "2026-07-05", to: "2026-07-12")],
                                                  lastIngestTs: "2026-07-12T18:00:00.000000+00:00", walkVersion: 2))
        let r = VaultReader(store: b.store)
        let w = r.readWindow([".claude"], from: Self.day("2026-07-01"), toExclusive: Self.day("2026-07-12"))
        #expect(w.byDayInput[Self.day("2026-07-10")] == 100)
        #expect(w.byDayOutput[Self.day("2026-07-10")] == 30)
        let covered = r.coveredSet([".claude"], from: Self.day("2026-07-01"), through: Self.day("2026-07-12"))
        #expect(covered.contains(Self.day("2026-07-05")))
        #expect(!covered.contains(Self.day("2026-07-04")))
    }

    // MARK: Ledger CSV (Windows VaultLedgerCsvTests)

    @Test func ledgerHeaderAlwaysAndRowsInGivenOrder() {
        let empty = VaultLedgerCsv.build([])
        #expect(empty.text == "session,root,project,first_seen_utc,last_seen_utc,tokens_in_scope,tokens_total,models\r\n")
        #expect(empty.rowCount == 0)

        let built = VaultLedgerCsv.build([
            .init(uuid: "u2", root: ".claude", project: "api", firstTs: "2026-07-11T00:00:00+00:00",
                  lastTs: "2026-07-11T01:00:00+00:00", tokensInScope: 200, tokensTotal: 200, models: "claude-fable-5:200"),
            .init(uuid: "u1", root: ".claude", project: "web", firstTs: "2026-07-10T00:00:00+00:00",
                  lastTs: "2026-07-10T01:00:00+00:00", tokensInScope: 100, tokensTotal: 600,
                  models: "claude-fable-5:400;claude-sonnet-5:200"),
        ])
        #expect(built.rowCount == 2)
        let lines = built.text.components(separatedBy: "\r\n")
        #expect(lines[1].hasPrefix("u2,"))
        #expect(lines[2].hasPrefix("u1,"))
        #expect(lines[2].contains("claude-fable-5:400;claude-sonnet-5:200"))
    }

    @Test func ledgerQuotesCommasAndQuotes() {
        let built = VaultLedgerCsv.build([
            .init(uuid: "u1", root: ".claude", project: "odd,\"proj\"", firstTs: "t", lastTs: "t",
                  tokensInScope: 1, tokensTotal: 1, models: "m"),
        ])
        #expect(built.text.contains("\"odd,\"\"proj\"\"\""))
    }

    @Test func ledgerRowFromASessionMatchesWindows() {
        let s = VaultSessionInfo(uuid: "u1", root: "r", projectKey: "api~1", projectName: "api", cwd: nil,
                                 firstTs: VaultTime.parse("2026-07-10T15:00:00.700Z")!,
                                 lastTs: VaultTime.parse("2026-07-10T16:00:00Z")!, total: 600,
                                 byModel: ["claude-sonnet-5": 200, "claude-fable-5": 400], bySkill: nil,
                                 byDay: [:], cache: nil, agentCount: 0, agentTokens: 0)
        let row = VaultLedgerCsv.row(s, root: "Work", project: "api", tokensInScope: 100)
        #expect(row.firstTs == "2026-07-10T15:00:00Z")
        #expect(row.lastTs == "2026-07-10T16:00:00Z")
        #expect(row.models == "claude-fable-5:400;claude-sonnet-5:200")
    }
}
