import Foundation
import SQLite3
import Testing
@testable import Sanduhr

/// A real SQLite file in a temp folder, written with the C API: the Antigravity CLI's
/// `conversation_summaries` with the columns the reader must never touch.
final class SummariesDB {
    private var db: OpaquePointer?

    static let schema = """
        CREATE TABLE conversation_summaries (conversation_id TEXT, title TEXT, preview TEXT,
            workspace_uris TEXT, raw_summary TEXT, last_modified_time TEXT);
        """

    init(_ path: String, _ sql: String = SummariesDB.schema) {
        #expect(sqlite3_open_v2(path, &db, SQLITE_OPEN_READWRITE | SQLITE_OPEN_CREATE, nil) == SQLITE_OK)
        exec(sql)
    }

    deinit { close() }

    func exec(_ sql: String) {
        #expect(sqlite3_exec(db, sql, nil, nil, nil) == SQLITE_OK)
    }

    func row(_ id: String, uris: String?) {
        let value = uris.map { "'" + $0.replacingOccurrences(of: "'", with: "''") + "'" } ?? "NULL"
        exec("INSERT INTO conversation_summaries VALUES ('\(id)', 'a title', 'a preview', \(value), 'a summary', '2026-11-10');")
    }

    func close() {
        if db != nil { sqlite3_close(db) }
        db = nil
    }
}

/// The Antigravity CLI's local burn (every-agents spec, section 3; Windows
/// `AntigravityBurnSourceTests` and `AntigravitySummariesTests`), run through
/// `VaultIngester.ingestAgent`. A generated database and brain folders in temp folders only.
@Suite("Antigravity burn reader")
struct AntigravityBurnReaderTests {
    static let now = CodexBurnReaderTests.now
    static let api = #"["file:///work/Projects/api"]"#

    // MARK: Lines, as the CLI writes them

    static func turn(_ ts: String, _ input: Int64, _ output: Int64, _ cacheRead: Int64) -> String {
        #"{"type":"PLANNER_RESPONSE","created_at":"\#(ts)","step_index":1,"input_tokens":\#(input),"output_tokens":\#(output),"cache_read_tokens":\#(cacheRead),"content":"never read"}"#
    }

    static let userLine = #"{"type":"USER_INPUT","created_at":"2026-11-10T16:00:00Z","step_index":0,"content":"never read"}"#

    // MARK: Bed

    final class Bed {
        let vault = VaultBed()
        var home: URL { URL(fileURLWithPath: vault.home.at("agy-home"), isDirectory: true) }
        var dbPath: String { home.appendingPathComponent("conversation_summaries.db").path }
        var opened: [(path: String, offset: Int64)] = []

        init() { vault.home.dir("agy-home") }

        func cleanUp() { vault.cleanUp() }

        /// The database with a row per `(conversation, workspace_uris)`.
        @discardableResult
        func database(_ rows: [(String, String?)] = []) -> SummariesDB {
            let db = SummariesDB(dbPath)
            for (id, uris) in rows { db.row(id, uris: uris) }
            return db
        }

        @discardableResult
        func transcript(_ conversation: String, _ lines: [String], name: String = "transcript.jsonl",
                        mtime: Date = AntigravityBurnReaderTests.now.addingTimeInterval(-600)) -> String {
            let dir = home.appendingPathComponent("brain/\(conversation)/.system_generated/logs", isDirectory: true).path
            try? FileManager.default.createDirectory(atPath: dir, withIntermediateDirectories: true)
            let path = (dir as NSString).appendingPathComponent(name)
            FileManager.default.createFile(atPath: path, contents: Data((lines.joined(separator: "\n") + "\n").utf8))
            CCLogs.setModified(path, mtime)
            return path
        }

        func append(_ path: String, _ lines: [String], mtime: Date = AntigravityBurnReaderTests.now.addingTimeInterval(-300)) {
            CCLogs.append(path, lines.joined(separator: "\n") + "\n")
            CCLogs.setModified(path, mtime)
        }

        func reader(busyMs: Int32 = 2000) -> AntigravityBurnReader {
            var r = AntigravityBurnReader(home: home)
            r.busyTimeoutMs = busyMs
            r.onOpen = { [unowned self] path, offset in opened.append((path, offset)) }
            return r
        }

        @discardableResult
        func ingest(busyMs: Int32 = 2000, now: Date = AntigravityBurnReaderTests.now) -> VaultIngestResult {
            vault.ingester().ingestAgent(reader(busyMs: busyMs), now: now)
        }

        func shard() -> VaultSessionShard { vault.store.loadSessionShard("antigravity", "2026-11").1 }
        func row(_ conversation: String = "conv-1") -> VaultSessionRow? { shard().sessions[conversation] }

        func checkpoint(_ path: String) -> VaultCheckpointEntry? {
            vault.store.loadCheckpoints("antigravity").entries[VaultStore.pathKey(path)]
        }
    }

    // MARK: Project path

    @Test(arguments: [
        (#"["file:///work/Projects/api"]"#, "/work/Projects/api" as String?),
        (#"["file:///work/My%20App","file:///other"]"#, "/work/My App"),
        (#"["file://localhost/work/api"]"#, "/work/api"),
        (#"["file:///work/a%25b"]"#, "/work/a%b"),
        ("[]", nil),
        ("", nil),
        ("not json", nil),
        (#"["https://example.com/x"]"#, nil),
        ("[42]", nil),
        (#"{"a":"file:///x"}"#, nil),
    ] as [(String, String?)])
    func theProjectPathIsTheFirstFileURI(json: String, expected: String?) {
        #expect(AntigravityBurnReader.projectPath(ofWorkspaceURIs: json) == expected)
    }

    @Test func noWorkspaceURIsIsNoPath() {
        #expect(AntigravityBurnReader.projectPath(ofWorkspaceURIs: nil) == nil)
    }

    @Test func theDefaultHomeIsTheCLIFolder() {
        let home = URL(fileURLWithPath: "/home-of-test")
        #expect(AntigravityBurnReader.defaultHome(userHome: home).path == "/home-of-test/.gemini/antigravity-cli")
    }

    // MARK: Counting

    @Test func turnsAreDatedOneByOneAndLandInTheirProject() throws {
        let b = Bed()
        defer { b.cleanUp() }
        b.database([("conv-1", Self.api)])
        b.transcript("conv-1", [
            Self.userLine,
            Self.turn("2026-11-10T16:01:00Z", 1000, 100, 5000),
            Self.turn("2026-11-11T16:01:00Z", 500, 50, 2000),
        ])
        b.ingest()
        let row = try #require(b.row())
        #expect(row.total == 1650)
        #expect(row.cacheTokens?.read == 7000)
        #expect(row.byDay["2026-11-10"]?.total == 1100)
        #expect(row.byDay["2026-11-11"]?.total == 550)
        #expect(row.byDay["2026-11-10"]?.input == 1000)
        #expect(row.byDay["2026-11-10"]?.output == 100)
        #expect(row.projectName == "api")
        #expect(row.byModel == ["antigravity": 1650])
        #expect(row.eventCount == 2)
        #expect(row.skippedLines == 0)   // the user line is not bad
    }

    @Test func aLineWithAnyOneTokenFieldIsAnEvent() throws {
        let b = Bed()
        defer { b.cleanUp() }
        b.database([("conv-1", Self.api)])
        b.transcript("conv-1", [
            #"{"created_at":"2026-11-10T16:01:00Z","cache_read_tokens":300}"#,
            #"{"created_at":"2026-11-10T16:02:00Z","output_tokens":7}"#,
            #"{"created_at":"2026-11-10T16:03:00Z","input_tokens":5}"#,
        ])
        b.ingest()
        let row = try #require(b.row())
        #expect(row.total == 12)
        #expect(row.cacheTokens?.read == 300)
        // The cache-only line adds its cache and no event, as a Claude event with no tokens does.
        #expect(row.eventCount == 2)
    }

    @Test func nonTokenLinesAreSkippedWithoutCountingAsBad() throws {
        let b = Bed()
        defer { b.cleanUp() }
        b.database([("conv-1", Self.api)])
        b.transcript("conv-1", [
            Self.userLine,
            #"{"type":"TOOL_CALL","created_at":"2026-11-10T16:00:30Z","step_index":2,"content":"never read"}"#,
            "not even json",
            Self.turn("2026-11-10T16:01:00Z", 10, 1, 0),
            #"{"type":"PLANNER_RESPONSE","input_tokens":99,"output_tokens":9}"#,   // no created_at: nothing to date it by
        ])
        b.ingest()
        let row = try #require(b.row())
        #expect(row.total == 11)
        #expect(row.skippedLines == 0)
    }

    @Test func aTokenLineThatDoesNotParseCountsAsBad() {
        let (event, bad) = AntigravityLineParser.parse(Data(#"{"input_tokens": broken"#.utf8))
        #expect(event == nil)
        #expect(bad)
        let (e2, bad2) = AntigravityLineParser.parse(Data(#"{"input_tokens":1}"#.utf8))
        #expect(e2 == nil)
        #expect(bad2 == false)   // no created_at: nothing to date it by, not a bad line
        let (e3, bad3) = AntigravityLineParser.parse(Data(#"[{"output_tokens":1}]"#.utf8))
        #expect(e3 == nil)
        #expect(bad3)   // a token mark in something that is not an object
    }

    @Test func aBadTokenLineIsCountedInTheRow() throws {
        let b = Bed()
        defer { b.cleanUp() }
        b.database([("conv-1", Self.api)])
        b.transcript("conv-1", [#"{"input_tokens": broken"#, Self.turn("2026-11-10T16:01:00Z", 10, 1, 0)])
        b.ingest()
        #expect(b.row()?.skippedLines == 1)
        #expect(b.row()?.total == 11)
    }

    // MARK: Which files

    @Test func transcriptFullIsNeverListedOpenedOrRead() throws {
        let b = Bed()
        defer { b.cleanUp() }
        b.database([("conv-1", Self.api)])
        let main = b.transcript("conv-1", [Self.turn("2026-11-10T16:01:00Z", 10, 1, 0)])
        let full = b.transcript("conv-1", [Self.turn("2026-11-10T16:01:00Z", 1_000_000, 1, 0)],
                                name: "transcript_full.jsonl")
        let files = try b.reader().files()
        #expect(files.map(\.path) == [main])
        #expect(files[0].sessionID == "conv-1")
        b.ingest()
        #expect(b.opened.map(\.path) == [main])
        #expect(!b.opened.contains { $0.path == full })
        #expect(b.row()?.total == 11)
        // Nothing under the vault names it either.
        #expect(b.vault.snapshot().keys.allSatisfy { !$0.contains("transcript_full") })
    }

    @Test func aMissingHomeOrBrainFolderListsNothing() throws {
        let nowhere = URL(fileURLWithPath: NSTemporaryDirectory()).appendingPathComponent("no-such-\(UUID().uuidString)")
        #expect(try AntigravityBurnReader(home: nowhere).files().isEmpty)
        let b = Bed()
        defer { b.cleanUp() }
        b.database([("conv-1", Self.api)])
        #expect(try b.reader().files().isEmpty)   // a database, no brain folder
    }

    @Test func aConversationFolderWithoutATranscriptIsSkipped() throws {
        let b = Bed()
        defer { b.cleanUp() }
        b.database([("conv-1", Self.api), ("conv-2", Self.api)])
        try FileManager.default.createDirectory(atPath: b.home.path + "/brain/conv-2/.system_generated/logs",
                                                withIntermediateDirectories: true)
        b.transcript("conv-1", [Self.turn("2026-11-10T16:01:00Z", 10, 1, 0)])
        #expect(try b.reader().files().map(\.sessionID) == ["conv-1"])
    }

    @Test func aMissingDatabaseListsNothing() throws {
        let b = Bed()
        defer { b.cleanUp() }
        b.transcript("conv-1", [Self.turn("2026-11-10T16:01:00Z", 10, 1, 0)])
        #expect(try b.reader().files().isEmpty)
        let result = b.ingest()
        #expect(result.filesSeen == 0)
        #expect(b.row() == nil)
    }

    // MARK: Project, and the row that arrives later

    @Test func aConversationWithoutASummariesRowIsNoneAndMovesWhenTheRowAppears() throws {
        let b = Bed()
        defer { b.cleanUp() }
        let db = b.database([("other", Self.api)])
        // A transcript quiet for over the seal hour: unchanged and unsealed, it is parsed once more.
        let path = b.transcript("conv-1", [Self.turn("2026-11-10T16:01:00Z", 100, 10, 0)],
                                mtime: Self.now.addingTimeInterval(-7200))
        #expect(try b.reader().files().first?.cwd == nil)
        b.ingest()
        #expect(try #require(b.row()).projectName == "(none)")
        #expect(b.row()?.total == 110)

        db.row("conv-1", uris: #"["file:///work/Projects/billing"]"#)
        b.ingest()   // the next parse of the transcript moves the whole session
        let moved = try #require(b.row())
        #expect(moved.projectName == "billing")
        #expect(moved.total == 110)
        #expect(b.shard().sessions.count == 1)

        // Appended later, the whole session stays with its project.
        b.append(path, [Self.turn("2026-11-10T16:09:00Z", 5, 5, 0)])
        b.ingest()
        #expect(b.row()?.projectName == "billing")
        #expect(b.row()?.total == 120)
    }

    @Test func aRowWithNoWorkspaceIsNone() throws {
        let b = Bed()
        defer { b.cleanUp() }
        b.database([("conv-1", nil), ("conv-2", "[]")])
        b.transcript("conv-1", [Self.turn("2026-11-10T16:01:00Z", 10, 1, 0)])
        b.transcript("conv-2", [Self.turn("2026-11-10T16:01:00Z", 20, 2, 0)])
        b.ingest()
        #expect(b.row("conv-1")?.projectName == "(none)")
        #expect(b.row("conv-2")?.projectName == "(none)")
        #expect(b.row("conv-2")?.total == 22)
    }

    @Test func theProjectUsesTheSameNamingRulesAsClaudes() throws {
        let b = Bed()
        defer { b.cleanUp() }
        // A worktree folder names the repo, as a Claude Code cwd does.
        b.database([("conv-1", #"["file:///work/Projects/api/.worktrees/feature-x"]"#)])
        b.transcript("conv-1", [Self.turn("2026-11-10T16:01:00Z", 10, 1, 0)])
        b.ingest()
        #expect(b.row()?.projectName == "api")
    }

    @Test func rowsNeverCarryThePathOrTheTitle() throws {
        let b = Bed()
        defer { b.cleanUp() }
        b.database([("conv-1", Self.api)])
        b.transcript("conv-1", [Self.turn("2026-11-10T16:01:00Z", 10, 1, 0)])
        b.ingest()
        for (name, data) in b.vault.snapshot() {
            let text = String(decoding: data, as: UTF8.self)
            #expect(!text.contains("/work/Projects"), "\(name)")
            #expect(!text.contains("a title") && !text.contains("a preview") && !text.contains("a summary"), "\(name)")
            #expect(!text.contains("never read"), "\(name)")
        }
    }

    // MARK: A locked database, and a resumed pass

    @Test func aLockedDatabaseListsNothingAndKeepsTheOffset() throws {
        let b = Bed()
        defer { b.cleanUp() }
        let db = b.database([("conv-1", Self.api)])
        let path = b.transcript("conv-1", [Self.turn("2026-11-10T16:01:00Z", 100, 10, 0)])
        b.ingest()
        let before = try #require(b.checkpoint(path))
        #expect(before.offset > 0)
        #expect(b.row()?.total == 110)

        b.append(path, [Self.turn("2026-11-11T16:01:00Z", 200, 20, 0)])
        db.exec("BEGIN EXCLUSIVE;")
        let started = Date()
        #expect(try b.reader(busyMs: 50).files().isEmpty)
        #expect(Date().timeIntervalSince(started) < 5)
        let locked = b.ingest(busyMs: 50)
        #expect(locked.filesSeen == 0)
        #expect(b.checkpoint(path) == before)
        #expect(b.row()?.total == 110)

        // Unlocked, the next pass reads from the saved offset, project and all.
        db.exec("COMMIT;")
        b.opened.removeAll()
        b.ingest()
        #expect(b.opened.map(\.offset) == [before.offset])
        #expect(b.row()?.total == 330)
        #expect(b.row()?.projectName == "api")
        #expect(b.row()?.byDay.mapValues(\.total) == ["2026-11-10": 110, "2026-11-11": 220])
        #expect(try #require(b.checkpoint(path)).offset > before.offset)
    }

    @Test func aPassResumedFromAnOffsetReadsOnlyTheNewLines() throws {
        let b = Bed()
        defer { b.cleanUp() }
        b.database([("conv-1", Self.api)])
        let path = b.transcript("conv-1", [Self.turn("2026-11-10T16:01:00Z", 100, 10, 0)])
        b.ingest()
        let first = try #require(b.checkpoint(path)).offset

        // A torn last line waits for its newline.
        b.append(path, [Self.turn("2026-11-10T16:05:00Z", 50, 5, 0)])
        CCLogs.append(path, #"{"created_at":"2026-11-10T16:06:00Z","input_tok"#)
        b.opened.removeAll()
        b.ingest()
        #expect(b.opened.map(\.offset) == [first])
        #expect(b.row()?.total == 165)
        #expect(b.row()?.eventCount == 2)
        #expect(b.row()?.skippedLines == 0)

        // Finishing the line counts it once.
        CCLogs.append(path, #"ens":1000,"output_tokens":1}"# + "\n")
        CCLogs.setModified(path, Self.now.addingTimeInterval(-200))
        b.ingest()
        let total = b.row()?.total
        #expect(total == 1166)
        #expect(b.row()?.eventCount == 3)
    }

    @Test func aDamagedDatabaseListsNothing() throws {
        let b = Bed()
        defer { b.cleanUp() }
        FileManager.default.createFile(atPath: b.dbPath, contents: Data("this is not a database at all".utf8))
        b.transcript("conv-1", [Self.turn("2026-11-10T16:01:00Z", 10, 1, 0)])
        #expect(try b.reader().files().isEmpty)
    }

    @Test func aDatabaseWithoutTheTableListsNothing() throws {
        let b = Bed()
        defer { b.cleanUp() }
        _ = SummariesDB(b.dbPath, "CREATE TABLE other (a TEXT);")
        b.transcript("conv-1", [Self.turn("2026-11-10T16:01:00Z", 10, 1, 0)])
        #expect(try b.reader().files().isEmpty)
    }

    // MARK: Column pin

    @Test func theQuerySelectsOnlyTheIDAndTheWorkspace() {
        #expect(AntigravitySummaries.query == "SELECT conversation_id, workspace_uris FROM conversation_summaries")
        for banned in ["title", "preview", "raw_summary", "*"] {
            #expect(!AntigravitySummaries.query.contains(banned))
        }
    }

    @Test func theReaderWorksWithoutAnyOtherColumnPresent() throws {
        // Proof the statement names no other column: a table holding just these two reads.
        let b = Bed()
        defer { b.cleanUp() }
        let db = SummariesDB(b.dbPath, "CREATE TABLE conversation_summaries (conversation_id TEXT, workspace_uris TEXT);")
        db.exec("INSERT INTO conversation_summaries VALUES ('conv-1', '[\"file:///work/Projects/api\"]');")
        let map = AntigravitySummaries.read(dbPath: b.dbPath)
        #expect(map == ["conv-1": #"["file:///work/Projects/api"]"#])
    }

    @Test func theReaderReadsRowsAndLeavesNullWorkspacesOut() throws {
        let b = Bed()
        defer { b.cleanUp() }
        b.database([("c1", Self.api), ("c2", nil)])
        let map = try #require(AntigravitySummaries.read(dbPath: b.dbPath))
        #expect(map == ["c1": Self.api])
    }

    @Test func theDatabaseIsOpenedReadOnly() throws {
        let b = Bed()
        defer { b.cleanUp() }
        b.database([("c1", Self.api)])
        let before = try Data(contentsOf: URL(fileURLWithPath: b.dbPath))
        _ = AntigravitySummaries.read(dbPath: b.dbPath)
        #expect(try Data(contentsOf: URL(fileURLWithPath: b.dbPath)) == before)
        #expect(AntigravitySummaries.fileURI("/a b/c#d%41/e?f.db") == "file:///a%20b/c%23d%2541/e%3Ff.db")
    }

    @Test func aFolderWithSpaceHashAndPercentEscapesOpens() throws {
        let b = Bed()
        defer { b.cleanUp() }
        let dir = b.vault.home.at("a b#c%41")
        try FileManager.default.createDirectory(atPath: dir, withIntermediateDirectories: true)
        let path = (dir as NSString).appendingPathComponent("conversation_summaries.db")
        let db = SummariesDB(path)
        db.row("c1", uris: Self.api)
        #expect(AntigravitySummaries.read(dbPath: path) == ["c1": Self.api])
    }

    @Test func aMissingDatabaseFileReadsAsNil() {
        let missing = NSTemporaryDirectory() + "no-such-\(UUID().uuidString).db"
        #expect(AntigravitySummaries.read(dbPath: missing) == nil)
        #expect(!FileManager.default.fileExists(atPath: missing))   // read-only: nothing was created
    }

    // MARK: Which agents the Vault ingests

    @Test func antigravityIngestsOnlyWhileItsSwitchIsOnAndItsFolderExists() throws {
        let b = Bed()
        defer { b.cleanUp() }
        let off = AgentSourceSettings.default
        var on = AgentSourceSettings.default
        on.antigravityOn = true
        let home = b.home
        let nowhere = URL(fileURLWithPath: b.vault.home.at("none"), isDirectory: true)
        let setup = SetupSettings(desktop: false, usage: ["claude": true])
        #expect(VaultAgents.active(setup: setup, sources: off, codexHomes: { [] }, antigravityHome: { home }).isEmpty)
        #expect(VaultAgents.active(setup: setup, sources: on, codexHomes: { [] }, antigravityHome: { nowhere }).isEmpty)
        let active = VaultAgents.active(setup: setup, sources: on, codexHomes: { [] }, antigravityHome: { home })
        #expect(active.map(\.agent) == [.antigravity])
        #expect((active.first as? AntigravityBurnReader)?.home == home)
        // Codex and Antigravity both on: Codex first, each its own source.
        let both = VaultAgents.active(setup: setup.withCodex(true), sources: on,
                                      codexHomes: { [URL(fileURLWithPath: "/nowhere/.codex")] }, antigravityHome: { home })
        #expect(both.map(\.agent) == [.codex, .antigravity])
    }
}
