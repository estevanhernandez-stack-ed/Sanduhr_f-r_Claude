import Foundation
import Testing
@testable import Sanduhr

/// A made-up agent source: lines `2026-11-10T10:00:00Z,100,50`, its state the count of events the
/// file produced so far (Windows `VaultAgentIngestTests.FakeSource`).
final class FakeBurnSource: AgentBurnSource {
    let agent: AgentSource
    var list: () throws -> [AgentBurnFile]
    private(set) var statesHandedOut: [String?] = []

    init(_ agent: AgentSource = .codex, _ list: @escaping () throws -> [AgentBurnFile]) {
        self.agent = agent
        self.list = list
    }

    func files() throws -> [AgentBurnFile] { try list() }

    func read(_ file: AgentBurnFile, from offset: Int64, state: String?) throws -> AgentBurnRead {
        statesHandedOut.append(state)
        var seen = state.flatMap { Int($0) } ?? 0
        var read = AgentBurnRead()
        read.endOffset = try AgentBurnFileReader.lines(plain: file.path, from: offset) { data in
            let p = String(decoding: data, as: UTF8.self).split(separator: ",")
            guard p.count == 3, let ts = VaultTime.parse(String(p[0])), let i = Int64(p[1]), let o = Int64(p[2]) else {
                read.skippedLines += 1
                return
            }
            seen += 1
            read.events.append(BurnEvent(timestamp: ts, input: i, output: o, model: "fake-model"))
        }
        read.state = String(seen)
        return read
    }

    /// One listed file, its size and time read from disk now.
    static func file(_ path: String, session: String, cwd: String? = nil) -> AgentBurnFile {
        let a = (try? FileManager.default.attributesOfItem(atPath: path)) ?? [:]
        let size = (a[.size] as? NSNumber)?.int64Value ?? 0
        let modified = a[.modificationDate] as? Date ?? Date()
        return AgentBurnFile(path: path, sessionID: session, cwd: cwd, compressed: false, length: size,
                             mtimeTicks: VaultTime.ticks(modified))
    }
}

struct FakeListingFailure: Error {}

/// Agent roots in the Vault (every-agents spec, sections 2, 3 and 5; Windows
/// `VaultAgentIngestTests`): `vault/codex` beside the Claude Code folders, tagged in its meta, the
/// same shards, rollups, checkpoints, quarantine and purge, each root in its own failure scope, and
/// old on-disk data reading and writing exactly as before. Temp folders and in-memory defaults only.
@Suite("Vault agent roots")
struct VaultAgentRootTests {
    typealias B = VaultBed
    static let now = CodexBurnReaderTests.now

    static func write(_ b: VaultBed, _ name: String, _ text: String, mtime: Date = now.addingTimeInterval(-600)) -> String {
        b.home.dir("in")
        let path = b.home.at("in/\(name)")
        b.write(path, text, mtime: mtime)
        return path
    }

    static func source(_ path: String, session: String = "sess-a", cwd: String? = nil,
                       agent: AgentSource = .codex) -> FakeBurnSource {
        FakeBurnSource(agent) { [FakeBurnSource.file(path, session: session, cwd: cwd)] }
    }

    static func json(_ value: some Encodable) throws -> [String: Any] {
        try #require(try JSONSerialization.jsonObject(with: VaultStore.encode(value)) as? [String: Any])
    }

    // MARK: Meta and checkpoints on disk

    @Test func anAgentMetaRoundTrips() throws {
        let meta = VaultRootMeta(since: "2026-11-01", covered: [VaultDateRange(from: "2026-10-18", to: "2026-11-12")],
                                 lastIngestTs: "2026-11-12T18:00:00.000000+00:00", walkVersion: 3, agent: "codex")
        let back = try JSONDecoder().decode(VaultRootMeta.self, from: VaultStore.encode(meta))
        #expect(back == meta)
        #expect(back.agentSource == .codex)
        #expect(try Self.json(meta)["agent"] as? String == "codex")
    }

    @Test func aMetaWithoutAgentReadsAsClaude() throws {
        let old = #"{"covered":[{"from":"2026-06-17","to":"2026-07-12"}],"last_ingest_ts":"2026-07-12T18:00:00.000000+00:00","since":"2026-07-01","walk_version":3}"#
        let meta = try JSONDecoder().decode(VaultRootMeta.self, from: Data(old.utf8))
        #expect(meta.agent == nil)
        #expect(meta.agentSource == .claude)
        #expect(meta.since == "2026-07-01")
        #expect(meta.walkVersion == 3)
        // Written back, it has the keys it came with and no others.
        #expect(Set(try Self.json(meta).keys) == ["covered", "last_ingest_ts", "since", "walk_version"])
    }

    @Test func aCheckpointWithoutParserStateDecodesAndEncodesUnchanged() throws {
        let old = #"{"last_seen":"2026-07-12T18:00:00.000000+00:00","length":120,"months":["2026-07"],"mtime_ticks":638877000000000000,"offset":120,"row_cache_creation":0,"row_cache_read":0,"row_events":1,"row_total":150,"sealed":false,"tail_guard":"ab"}"#
        let entry = try JSONDecoder().decode(VaultCheckpointEntry.self, from: Data(old.utf8))
        #expect(entry.parserState == nil)
        #expect(entry.rowTotal == 150)
        #expect(String(decoding: try VaultStore.encode(entry), as: UTF8.self) == old)
    }

    @Test func aClaudeRootsFilesGetNoNewKeys() throws {
        let b = VaultBed()
        defer { b.cleanUp() }
        b.session("u1", [B.line("2026-07-10T15:00:00Z")])
        b.ingest()
        let meta = try #require(try JSONSerialization.jsonObject(
            with: Data(contentsOf: URL(fileURLWithPath: b.store.metaPath(".claude")))) as? [String: Any])
        #expect(meta["agent"] == nil)
        let cps = String(decoding: try Data(contentsOf: URL(fileURLWithPath: b.store.checkpointsPath(".claude"))), as: UTF8.self)
        #expect(!cps.contains("parser_state"))
        #expect(!cps.contains("agent"))
    }

    // MARK: Ingest

    @Test func anAgentSourceWritesItsOwnRootTaggedWithTheAgent() throws {
        let b = VaultBed()
        defer { b.cleanUp() }
        let path = Self.write(b, "a.jsonl", "2026-11-10T16:00:00Z,100,50\n2026-11-11T16:00:00Z,10,5\n")
        let r = b.ingester().ingestAgent(Self.source(path, cwd: "/work/Projects/api"), now: Self.now)
        #expect(r.acquired)
        #expect(r.filesFullParsed == 1)
        #expect(b.store.loadMeta("codex")?.agent == "codex")
        #expect(b.store.loadMeta("codex")?.walkVersion == VaultIngester.currentWalkVersion)
        let row = try #require(b.shard("2026-11", root: "codex").sessions["sess-a"])
        #expect(row.total == 165)
        #expect(row.projectName == "api")
        #expect(row.byDay.keys.sorted() == ["2026-11-10", "2026-11-11"])
        #expect(b.rollups("2026-11", root: "codex").days["2026-11-11"]?.sessions == 1)
    }

    @Test func aGrownPlainFileResumesWithTheSavedParserState() throws {
        let b = VaultBed()
        defer { b.cleanUp() }
        let path = Self.write(b, "a.jsonl", "2026-11-10T16:00:00Z,100,50\n")
        let src = Self.source(path)
        b.ingester().ingestAgent(src, now: Self.now)
        b.append(path, mtime: Self.now.addingTimeInterval(-300), ["2026-11-10T17:00:00Z,1,1"])
        let r = b.ingester().ingestAgent(src, now: Self.now)
        #expect(r.filesTailParsed == 1)
        #expect(src.statesHandedOut == [nil, "1"])
        #expect(b.shard("2026-11", root: "codex").sessions["sess-a"]?.total == 152)
    }

    @Test func aProjectThatAppearsLaterMovesTheWholeSession() throws {
        let b = VaultBed()
        defer { b.cleanUp() }
        let path = Self.write(b, "a.jsonl", "2026-11-10T16:00:00Z,100,50\n")
        var cwd: String?
        let src = FakeBurnSource(.antigravity) { [FakeBurnSource.file(path, session: "conv", cwd: cwd)] }
        b.ingester().ingestAgent(src, now: Self.now)
        #expect(b.shard("2026-11", root: "antigravity").sessions["conv"]?.projectName == "(none)")

        cwd = "/work/Projects/web"
        b.append(path, mtime: Self.now.addingTimeInterval(-300), ["2026-11-10T17:00:00Z,1,1"])
        b.ingester().ingestAgent(src, now: Self.now)
        let row = try #require(b.shard("2026-11", root: "antigravity").sessions["conv"])
        #expect(row.projectName == "web")
        #expect(row.total == 152)
    }

    @Test func aSourceThatThrowsWhileListingSkipsOnlyItsOwnRoot() {
        let b = VaultBed()
        defer { b.cleanUp() }
        let path = Self.write(b, "a.jsonl", "2026-11-10T16:00:00Z,1,1\n")
        let broken = FakeBurnSource(.antigravity) { throw FakeListingFailure() }
        let r1 = b.ingester().ingestAgent(broken, now: Self.now)
        #expect(r1.rootsAborted == 1)
        #expect(!b.store.rootExists("antigravity"))
        #expect(b.logs.contains("agent-root failed (FakeListingFailure)"))
        let r2 = b.ingester().ingestAgent(Self.source(path), now: Self.now)
        #expect(r2.rootsAborted == 0)
        #expect(b.store.loadSessionShard("codex", "2026-11").0 == .ok)
    }

    @Test func anAgentRootStoppedMidCycleWritesNothing() {
        let b = VaultBed()
        defer { b.cleanUp() }
        let path = Self.write(b, "a.jsonl", "2026-11-10T16:00:00Z,1,1\n")
        let r = b.ingester().ingestAgent(Self.source(path), now: Self.now, stillRecording: { $0 != "codex" })
        #expect(r.rootsWithdrawn == 1)
        #expect(!b.store.rootExists("codex"))
    }

    @Test func aCorruptAgentShardIsQuarantinedAndTheNextCycleConverges() throws {
        let b = VaultBed()
        defer { b.cleanUp() }
        let path = Self.write(b, "a.jsonl", "2026-11-10T16:00:00Z,100,50\n")
        let src = Self.source(path)
        b.ingester().ingestAgent(src, now: Self.now)
        try Data("{ not json".utf8).write(to: URL(fileURLWithPath: b.store.sessionShardPath("codex", "2026-11")))
        b.append(path, mtime: Self.now.addingTimeInterval(-300), ["2026-11-10T17:00:00Z,1,1"])

        let r = b.ingester().ingestAgent(src, now: Self.now)
        #expect(r.rootsAborted == 1)
        #expect(b.files(in: b.rootPath("codex")).contains { $0.hasSuffix(".bad") })
        b.ingester().ingestAgent(src, now: Self.now)
        #expect(b.shard("2026-11", root: "codex").sessions["sess-a"]?.total == 152)
    }

    // MARK: The service

    static func codexOnly(_ b: VaultBed) -> String {
        Self.write(b, "a.jsonl", "2026-11-10T16:00:00Z,100,50\n")
    }

    @Test func aFailingAgentRootNeverStopsClaudesIngest() throws {
        let b = VaultBed()
        defer { b.cleanUp() }
        let d = MemoryDefaults()
        let folder = VaultServiceTests.recordingWork(b, d)
        let path = Self.codexOnly(b)
        let broken = FakeBurnSource(.antigravity) { throw FakeListingFailure() }
        let s = VaultService(vaultDir: b.vaultDir, defaults: { d }, writerVersion: "test", timeZone: B.cst,
                             log: b.sink, dirHoldsGit: { _ in false },
                             agentSources: { [broken, Self.source(path)] })
        let r = s.ingestNow(now: B.now)
        #expect(r.acquired)
        #expect(r.rootsAborted == 1)
        #expect(b.store.loadSessionShard(VaultFolderID.of(folder), "2026-07").1.sessions["u1"]?.total == 150)
        #expect(b.store.loadSessionShard("codex", "2026-11").1.sessions["sess-a"]?.total == 150)
        #expect(!b.store.rootExists("antigravity"))
    }

    @Test func agentRootsIngestWithNoAccountKeepingARecord() {
        let b = VaultBed()
        defer { b.cleanUp() }
        let d = MemoryDefaults()
        let path = Self.codexOnly(b)
        let s = VaultService(vaultDir: b.vaultDir, defaults: { d }, writerVersion: "test", timeZone: B.cst,
                             log: b.sink, agentSources: { [Self.source(path)] })
        #expect(VaultService.roots(in: d).isEmpty)
        s.ingestNow(now: Self.now)
        #expect(b.store.loadSessionShard("codex", "2026-11").0 == .ok)
    }

    @Test func demoModeIngestsNoAgentRoot() {
        let b = VaultBed()
        defer { b.cleanUp() }
        let d = MemoryDefaults()
        let path = Self.codexOnly(b)
        let s = VaultService(vaultDir: b.vaultDir, defaults: { d }, writerVersion: "test", timeZone: B.cst,
                             log: b.sink, agentSources: { [Self.source(path)] })
        s.agentsPaused = true
        #expect(s.activeAgents(in: d).isEmpty)
        s.ingestNow(now: Self.now)
        #expect(!b.store.rootExists("codex"))
        s.agentsPaused = false
        #expect(s.activeAgents(in: d).count == 1)
    }

    @Test func erasingAnAgentRecordStopsItUntilTheSourceIsSwitchedOnAgain() {
        let b = VaultBed()
        defer { b.cleanUp() }
        let d = MemoryDefaults()
        let path = Self.codexOnly(b)
        let s = VaultService(vaultDir: b.vaultDir, defaults: { d }, writerVersion: "test", timeZone: B.cst,
                             log: b.sink, agentSources: { [Self.source(path)] })
        s.ingestNow(now: Self.now)
        #expect(b.store.rootExists("codex"))

        #expect(s.eraseNow(id: "codex", wait: 1))
        #expect(!b.store.rootExists("codex"))
        #expect(VaultAgentStop.isStopped(.codex, in: d))
        #expect(s.activeAgents(in: d).isEmpty)
        s.ingestNow(now: Self.now)
        #expect(!b.store.rootExists("codex"))
        // Even a cycle handed the source before the erase writes nothing.
        s.ingestNow(agents: [Self.source(path)], now: Self.now)
        #expect(!b.store.rootExists("codex"))

        VaultAgentStop.clear(.codex, in: d)
        s.ingestNow(now: Self.now)
        #expect(b.store.loadSessionShard("codex", "2026-11").1.sessions["sess-a"]?.total == 150)
    }

    @Test func switchingCodexOnInSetupClearsItsStop() throws {
        let setup = MemoryDefaults()
        let desk = MemoryDefaults()
        let storage = SetupStorage.defaults(setup: setup, desk: desk)
        let claudeOnly = SetupSettings(desktop: false, usage: ["claude": true])
        try storage.save(claudeOnly, false)
        VaultAgentStop.stop(.codex, in: setup)
        try storage.save(claudeOnly.withCodex(true), false)
        #expect(!VaultAgentStop.isStopped(.codex, in: setup))
        // Stopped while on, a save that keeps Codex on (Claude switched, say) leaves the stop.
        VaultAgentStop.stop(.codex, in: setup)
        try storage.save(claudeOnly.withCodex(true).withClaude(false), false)
        #expect(VaultAgentStop.isStopped(.codex, in: setup))
    }

    @Test func theActiveAgentsFollowTheCodexSwitchAndAHome() {
        let homes = [URL(fileURLWithPath: "/nowhere/.codex")]
        let off = SetupSettings(desktop: false, usage: ["claude": true])
        let on = off.withCodex(true)
        #expect(VaultAgents.active(setup: off, sources: .default, codexHomes: { homes }).isEmpty)
        #expect(VaultAgents.active(setup: nil, sources: .default, codexHomes: { homes }).isEmpty)
        #expect(VaultAgents.active(setup: on, sources: .default, codexHomes: { [] }).isEmpty)
        let active = VaultAgents.active(setup: on, sources: .default, codexHomes: { homes })
        #expect(active.map(\.agent) == [.codex])
        #expect((active.first as? CodexBurnReader)?.homes == homes)
    }

    @Test func aClaudeOnlyMacIngestsExactlyAsBefore() {
        let b = VaultBed()
        defer { b.cleanUp() }
        let d = MemoryDefaults()
        let folder = VaultServiceTests.recordingWork(b, d)
        let s = VaultServiceTests.service(b, d)
        let r = s.ingestNow(now: B.now)
        #expect(r == VaultIngestResult(filesSeen: 1, filesFullParsed: 1))
        #expect(b.files(in: b.vaultDir).filter { !$0.hasPrefix(".") } == [VaultFolderID.of(folder)])
    }

    // MARK: Stewardship

    @Test func agentRecordsAreListedAsAgentsNeverAsUnlinked() throws {
        let b = VaultBed()
        defer { b.cleanUp() }
        let d = MemoryDefaults()
        let folder = VaultServiceTests.recordingWork(b, d)
        let path = Self.codexOnly(b)
        let s = VaultService(vaultDir: b.vaultDir, defaults: { d }, writerVersion: "test", timeZone: B.cst,
                             log: b.sink, dirHoldsGit: { _ in false }, agentSources: { [Self.source(path)] })
        s.ingestNow(now: Self.now)
        let orphan = VaultFolderID.of(b.home.at(".claude-old"))
        b.store.saveMeta(orphan, VaultReaderTests.meta("2026-07-01", []))

        let list = s.records(AccountData.allChoices(in: d))
        #expect(list.map(\.id) == [VaultFolderID.of(folder), "codex", orphan])
        let codex = try #require(list.first { $0.id == "codex" })
        #expect(codex.agent == .codex)
        #expect(codex.isAgent)
        #expect(!codex.isLinked)
        #expect(codex.account == nil)
        #expect(codex.recording)
        #expect(codex.oldestDay == CCLocalDay(key: "2026-11-10"))
        #expect(list.first { $0.id == orphan }?.agent == nil)
        #expect(UsageRecordCopy.title(.codex) == "Codex · local activity")

        VaultAgentStop.stop(.codex, in: d)
        #expect(s.records(AccountData.allChoices(in: d)).first { $0.id == "codex" }?.recording == false)
    }
}
