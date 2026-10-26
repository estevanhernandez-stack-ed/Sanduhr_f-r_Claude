import Foundation
import Testing
@testable import Sanduhr

/// Item 74: per-seat meters. Which accounts get their own snapshot, seats.json, the per-account
/// writer, the files on Sign Out, Remove, unlink and rename, and SeatMeters' fetch rule with a fake
/// client, clock and wait. Temp folders and made-up labels only; never the real Application Support.
private func tempDir() throws -> URL {
    let dir = FileManager.default.temporaryDirectory
        .appendingPathComponent("sanduhr-seats-\(UUID().uuidString)", isDirectory: true)
    try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
    return dir
}

private func usage(_ five: Double) -> UsageResponse {
    UsageResponse(tiers: [.fiveHour: TierUsage(utilization: five, resetsAt: "2026-10-11T18:00:00Z")], extraUsage: nil)
}

private func json(_ url: URL) -> [String: Any]? {
    (try? Data(contentsOf: url)).flatMap { try? JSONSerialization.jsonObject(with: $0) as? [String: Any] }
}

private func five(_ url: URL) -> Int? {
    (json(url)?["tiers"] as? [[String: Any]])?.first?["utilization"] as? Int
}

private func ref(_ label: String) -> String { AccountRef.of(label)! }

@Suite("Per-seat meters: files")
struct SeatSnapshotsTests {
    @Test func everyListedAccountWithAFolderIsLinkedSignedInOrNot() {
        let c: [String: AccountDataChoices] = ["Work": .init(folder: "/tmp/a/.claude"), "Home": .init(folder: "/tmp/a/.claude-home"),
                                               "Spare": .init(), "Gone": .init(folder: "/tmp/a/.claude-gone")]
        #expect(SeatSnapshots.linked(choices: c, labels: ["Work", "Home", "Spare"]) == ["Work": "/tmp/a/.claude", "Home": "/tmp/a/.claude-home"])
        #expect(SeatSnapshots.seats(["Work": "/x"], realPath: { $0 }) == [.init(folder: "/x", accountRef: ref("Work"), label: "Work")])
        #expect(SeatSnapshots.seats([:]).isEmpty)
    }

    @Test func inactiveSignedInLinkedAccountsAreDueOnlyWhileInUse() {
        let linked = ["Work": "/x", "Home": "/y", "Lab": "/z"]
        let all: Set<String> = ["Work", "Home", "Lab"]
        #expect(SeatSnapshots.due(linked: linked, signedIn: all, active: "Work", inUse: ["Home"]) == ["Home"])
        #expect(SeatSnapshots.due(linked: linked, signedIn: all, active: "Work", inUse: []) == [])
        #expect(SeatSnapshots.due(linked: linked, signedIn: all, active: "Work", inUse: all) == ["Home", "Lab"])
        #expect(SeatSnapshots.due(linked: linked, signedIn: ["Work", "Home"], active: "Work", inUse: all) == ["Home"])
        #expect(SeatSnapshots.due(linked: linked, signedIn: all, active: "Work", inUse: ["Spare"]) == [])
    }

    @Test func foldersAreRealPathsWithoutATrailingSlash() throws {
        let dir = try tempDir()
        defer { try? FileManager.default.removeItem(at: dir) }
        let real = dir.appendingPathComponent(".claude-real")
        try FileManager.default.createDirectory(at: real, withIntermediateDirectories: true)
        let link = dir.appendingPathComponent(".claude-link")
        try FileManager.default.createSymbolicLink(at: link, withDestinationURL: real)
        let expected = SeatSnapshots.realPath(real.path)
        #expect(!expected.hasSuffix("/"))
        #expect(SeatSnapshots.realPath(link.path) == expected)
        #expect(SeatSnapshots.realPath(link.path + "/") == expected)
        #expect(SeatSnapshots.realPath("/no/such/folder/") == "/no/such/folder")
    }

    @Test func fileNamesCarryAnEightDigitRef() {
        #expect(SeatSnapshots.ref(inFileName: "snapshot.1a2b3c4d.json") == "1a2b3c4d")
        #expect(SeatSnapshots.ref(inFileName: "snapshot.json") == nil)
        #expect(SeatSnapshots.ref(inFileName: "snapshot.1A2B3C4D.json") == nil)
        #expect(SeatSnapshots.ref(inFileName: "snapshot.../x.json") == nil)
    }

    @Test func seatsJSONMapsFoldersToRefsWithTheAccountsLabel() throws {
        let dir = try tempDir()
        defer { try? FileManager.default.removeItem(at: dir) }
        let files = SeatSnapshots.Files(dir: dir)
        SeatSnapshots.sync(SeatSnapshots.seats(["Work Label": "/tmp/s/.claude"], realPath: { $0 }), in: files)
        let obj = try #require(json(files.seats))
        #expect(obj["schema_version"] as? Int == 1)
        // The label is for that folder's statusline; the snapshot files never carry one.
        #expect(obj["seats"] as? [[String: String]]
            == [["folder": "/tmp/s/.claude", "account_ref": ref("Work Label"), "label": "Work Label"]])
        for name in try FileManager.default.contentsOfDirectory(atPath: dir.path) where name.hasPrefix("snapshot") {
            #expect(!(try String(contentsOf: dir.appendingPathComponent(name), encoding: .utf8)).contains("Label"))
        }
        #expect(SeatSnapshots.counts(in: files).seats == 1)
    }

    @Test func seatsJSONIsOwnerOnlyEvenWhenUnchanged() throws {
        let dir = try tempDir()
        defer { try? FileManager.default.removeItem(at: dir) }
        let files = SeatSnapshots.Files(dir: dir)
        let seats = SeatSnapshots.seats(["Work": "/tmp/s/a"], realPath: { $0 })
        SeatSnapshots.sync(seats, in: files)
        let mode = { (try FileManager.default.attributesOfItem(atPath: files.seats.path)[.posixPermissions] as? Int) }
        #expect(try mode() == 0o600)
        let inode = try FileManager.default.attributesOfItem(atPath: files.seats.path)[.systemFileNumber] as? Int
        try FileManager.default.setAttributes([.posixPermissions: 0o644], ofItemAtPath: files.seats.path)
        SeatSnapshots.sync(seats, in: files)
        #expect(try mode() == 0o600)
        // Same bytes: not rewritten.
        #expect(try FileManager.default.attributesOfItem(atPath: files.seats.path)[.systemFileNumber] as? Int == inode)
    }

    @Test func eachSeatGetsItsOwnSnapshotInTheSameSchema() throws {
        let dir = try tempDir()
        defer { try? FileManager.default.removeItem(at: dir) }
        let files = SeatSnapshots.Files(dir: dir)
        SnapshotWriter.writeOk(usage(42), accountRef: ref("Work"), now: Date(timeIntervalSince1970: 0), to: files.snapshot(ref("Work")))
        let w = try #require(json(files.snapshot(ref("Work"))))
        #expect(Set(w.keys) == ["schema_version", "writer_version", "captured_at", "account_ref", "plan", "status", "error_kind", "tiers"])
        #expect(w["account_ref"] as? String == ref("Work"))
        #expect(five(files.snapshot(ref("Work"))) == 42)
        #expect(SeatSnapshots.counts(in: files).snapshots == 1)
    }

    @Test func aSignedOutSeatKeepsItsRowWithASignedOutFile() throws {
        let dir = try tempDir()
        defer { try? FileManager.default.removeItem(at: dir) }
        let files = SeatSnapshots.Files(dir: dir)
        let seats = SeatSnapshots.seats(["Work": "/tmp/s/a", "Home": "/tmp/s/b"], realPath: { $0 })
        SnapshotWriter.writeOk(usage(12), accountRef: ref("Home"), to: files.snapshot(ref("Home")))
        SeatSnapshots.sync(seats, signedOut: [ref("Home")], in: files)
        #expect((json(files.seats)?["seats"] as? [Any])?.count == 2)
        #expect(SeatSnapshots.isSignedOut(files.snapshot(ref("Home"))))
        #expect(json(files.snapshot(ref("Home")))?["account_ref"] as? String == ref("Home"))
        // Signed in again: the signed-out file goes until its next reading.
        SeatSnapshots.sync(seats, in: files)
        #expect(!FileManager.default.fileExists(atPath: files.snapshot(ref("Home")).path))
    }

    @Test func aSeatThatGoesLosesItsFileAndUnlinkingTheLastClearsEverything() throws {
        let dir = try tempDir()
        defer { try? FileManager.default.removeItem(at: dir) }
        let files = SeatSnapshots.Files(dir: dir)
        let both = ["Work": "/tmp/s/a", "Home": "/tmp/s/b"]
        SeatSnapshots.sync(SeatSnapshots.seats(both, realPath: { $0 }), in: files)
        for label in both.keys { SnapshotWriter.writeOk(usage(1), accountRef: ref(label), to: files.snapshot(ref(label))) }
        SnapshotWriter.writeOk(usage(1), accountRef: ref("Work"), to: dir.appendingPathComponent("snapshot.json"))
        // Home removed or unlinked: its row and file go.
        SeatSnapshots.sync(SeatSnapshots.seats(["Work": "/tmp/s/a"], realPath: { $0 }), in: files)
        #expect(files.snapshotRefs() == [ref("Work")])
        #expect(SeatSnapshots.counts(in: files) == (1, 1))
        // The last folder unlinked: back to today, snapshot.json untouched.
        SeatSnapshots.sync([], in: files)
        #expect(try FileManager.default.contentsOfDirectory(atPath: dir.path) == ["snapshot.json"])
    }

    @Test func aRenamedSeatFileMovesWithItsRef() throws {
        let dir = try tempDir()
        defer { try? FileManager.default.removeItem(at: dir) }
        let files = SeatSnapshots.Files(dir: dir)
        SnapshotWriter.writeOk(usage(55), accountRef: ref("Work"), to: files.snapshot(ref("Work")))
        SeatSnapshots.rename(ref("Work"), to: ref("Office"), in: files)
        #expect(files.snapshotRefs() == [ref("Office")])
        #expect(json(files.snapshot(ref("Office")))?["account_ref"] as? String == ref("Office"))
        #expect(five(files.snapshot(ref("Office"))) == 55)
    }

    @Test func folderBoxSaysWhoseMetersShow() {
        #expect(SeatSnapshots.metersHere(owner: "Work") == "Meters here show Work")
        #expect(SeatSnapshots.metersHere(owner: nil) == "Meters here show the active account")
    }

    @Test func errorKinds() {
        #expect(SnapshotWriter.errorKind(ClaudeAPI.APIError.unauthorized) == "session_expired")
        #expect(SnapshotWriter.errorKind(ClaudeAPI.APIError.cloudflareChallenge) == "cloudflare")
        #expect(SnapshotWriter.errorKind(ClaudeAPI.APIError.http(429)) == "network")
    }

    @Test func linkFolderIsANamedControlOnAccounts() {
        #expect(SettingsNames.table.contains { $0.name == SettingsNames.linkFolder && $0.page == .credentials })
        #expect(SettingsNames.anchors[SettingsNames.linkFolder] == SettingsAnchor.data)
    }
}

// MARK: - SeatMeters, the fetch rule

/// A client that answers from `result` and counts its calls; `during` runs while the request is out.
private final class FakeClient: UsageFetching, @unchecked Sendable {
    var calls = 0
    var result: Result<UsageResponse, Error>
    var during: (@MainActor () -> Void)?

    init(_ result: Result<UsageResponse, Error>) { self.result = result }

    func getUsage() async throws -> UsageResponse {
        calls += 1
        if let during { await MainActor.run { during() } }
        return try result.get()
    }
}

/// The world SeatMeters reads: the accounts, a clock, the waits and the clients made.
@MainActor
private final class World {
    let dir: URL
    let files: SeatSnapshots.Files
    let meters: SeatMeters
    var context = SeatMeters.Context()
    var clock = Date(timeIntervalSince1970: 1_000_000)
    var waits: [TimeInterval] = []
    var keys: [String: String] = [:]
    var clients: [String: FakeClient] = [:]
    var answers: [String: Result<UsageResponse, Error>] = [:]

    init() throws {
        dir = try tempDir()
        files = SeatSnapshots.Files(dir: dir)
        meters = SeatMeters(files: files, interval: 300)
        context = SeatMeters.Context(labels: ["Work", "Home", "Lab"],
                                     choices: ["Work": .init(folder: "/tmp/s/a"), "Home": .init(folder: "/tmp/s/b"), "Lab": .init(folder: "/tmp/s/c")],
                                     signedIn: ["Work", "Home", "Lab"], active: "Work", inUse: ["Home"])
        keys = ["Work": "kw", "Home": "kh", "Lab": "kl"]
        meters.context = { [unowned self] in self.context }
        meters.now = { [unowned self] in self.clock }
        meters.sleep = { [unowned self] s in self.waits.append(s) }
        meters.recentActivity = { _ in false }
        meters.credentials = { [unowned self] label in AccountCredentials(sessionKey: self.keys[label], cfClearance: nil) }
        meters.makeClient = { [unowned self] creds in
            let label = self.keys.first { $0.value == creds.sessionKey }?.key ?? "?"
            let c = FakeClient(self.answers[label] ?? .success(usage(10)))
            self.clients[label] = c
            return c
        }
    }

    func calls(_ label: String) -> Int { clients[label]?.calls ?? 0 }
    func file(_ label: String) -> URL { files.snapshot(ref(label)) }

    /// The active account answers and the round it starts runs to the end.
    func activeAnswers(_ result: Result<UsageResponse, Error> = .success(usage(50))) async {
        meters.activeAnswered(context.active, result)
        await meters.round?.value
    }

    deinit { try? FileManager.default.removeItem(at: dir) }
}

@Suite("Per-seat meters: fetch rule")
@MainActor
struct SeatMetersTests {
    @Test func theActiveFetchWritesItsSeatAndFetchesSeatsInUse() async throws {
        let w = try World()
        w.answers["Home"] = .success(usage(12))
        await w.activeAnswers()
        #expect(five(w.file("Work")) == 50)
        #expect(five(w.file("Home")) == 12)
        #expect(w.calls("Home") == 1)
        #expect(w.calls("Lab") == 0)                            // not in use: keeps its last reading
        #expect(!FileManager.default.fileExists(atPath: w.file("Lab").path))
    }

    @Test func repeatedRefreshesDoNotFetchASeatAgainWithinTheInterval() async throws {
        let w = try World()
        await w.activeAnswers()
        w.clock += 20
        await w.activeAnswers()
        w.clock += 60
        await w.activeAnswers()
        #expect(w.calls("Home") == 1)
        w.clock += 300
        await w.activeAnswers()
        #expect(w.calls("Home") == 2)
    }

    @Test func noRoundAfterTheActiveFetchFailedOnTheNetwork() async throws {
        let w = try World()
        await w.activeAnswers(.failure(ClaudeAPI.APIError.http(429)))
        #expect(w.meters.round == nil)
        #expect(w.calls("Home") == 0)
        #expect(json(w.file("Work"))?["error_kind"] as? String == "network")
        // An auth failure of the active account is its own: the seats still run.
        await w.activeAnswers(.failure(ClaudeAPI.APIError.unauthorized))
        #expect(w.calls("Home") == 1)
    }

    @Test func oneRoundAtATimeStaggered() async throws {
        let w = try World()
        w.context.inUse = ["Home", "Lab"]
        let round = try #require(w.meters.startRound())
        #expect(w.meters.startRound() == nil)
        await round.value
        #expect(w.waits == [SeatSnapshots.stagger, SeatSnapshots.stagger])
        #expect((w.calls("Home"), w.calls("Lab")) == (1, 1))
        #expect(w.meters.round == nil)
    }

    @Test func aRefusedKeyStopsUntilANewKeyIsSaved() async throws {
        let w = try World()
        w.answers["Home"] = .failure(ClaudeAPI.APIError.unauthorized)
        await w.activeAnswers()
        #expect(json(w.file("Home"))?["error_kind"] as? String == "session_expired")
        w.clock += 600
        await w.activeAnswers()
        #expect(w.calls("Home") == 1)
        w.keys["Home"] = "kh2"
        w.answers["Home"] = .success(usage(8))
        await w.activeAnswers()
        #expect(five(w.file("Home")) == 8)
    }

    @Test func aSwitchMidRoundDropsTheAnswer() async throws {
        let w = try World()
        w.meters.makeClient = { _ in
            let c = FakeClient(.success(usage(77)))
            c.during = { w.context.active = "Home" }
            w.clients["Home"] = c
            return c
        }
        w.meters.startRound()
        await w.meters.round?.value
        #expect(w.calls("Home") == 1)
        #expect(!FileManager.default.fileExists(atPath: w.file("Home").path))
    }

    @Test func aSignOutMidRoundDropsTheAnswer() async throws {
        let w = try World()
        w.meters.makeClient = { _ in
            let c = FakeClient(.success(usage(77)))
            c.during = { _ = w.context.signedIn.remove("Home") }
            w.clients["Home"] = c
            return c
        }
        w.meters.startRound()
        await w.meters.round?.value
        #expect(!FileManager.default.fileExists(atPath: w.file("Home").path))
    }

    @Test func followingsCheckFeedsTheFileAndCountsAsARead() async throws {
        let w = try World()
        w.meters.followerAnswered("Home", .success(usage(33)))
        #expect(five(w.file("Home")) == 33)
        await w.activeAnswers()
        #expect(w.calls("Home") == 0)
        // An account with no folder, or the active one, is never written from a check.
        w.meters.followerAnswered("Spare", .success(usage(1)))
        w.meters.followerAnswered("Work", .success(usage(1)))
        #expect(five(w.file("Work")) == 50)
    }

    @Test func inactiveSeatsCarryTheirOwnKeptPlanElseNull() async throws {
        let w = try World()
        w.meters.planFor = { ["Home": "Pro"][$0] }
        w.meters.followerAnswered("Home", .success(usage(33)))
        #expect(json(w.file("Home"))?["plan"] as? String == "Pro")
        w.answers["Lab"] = .success(usage(5))
        w.context.inUse = ["Lab"]
        await w.activeAnswers()
        #expect(json(w.file("Lab"))?["plan"] is NSNull)
        #expect(json(w.file("Work"))?["plan"] is NSNull)
    }

    @Test func syncKeepsSignedOutSeatsAndRenameMovesTheFile() async throws {
        let w = try World()
        w.context.signedIn = ["Work", "Home"]
        w.meters.sync()
        #expect((json(w.files.seats)?["seats"] as? [Any])?.count == 3)
        #expect(SeatSnapshots.isSignedOut(w.file("Lab")))
        await w.activeAnswers()
        w.meters.rename("Work", to: "Office")
        w.context.labels = ["Office", "Home", "Lab"]
        w.context.choices["Office"] = w.context.choices.removeValue(forKey: "Work")
        w.context.active = "Office"
        w.context.signedIn = ["Office", "Home"]
        w.meters.sync()
        #expect(five(w.file("Office")) == 50)
        #expect(!FileManager.default.fileExists(atPath: w.file("Work").path))
        // Every folder unlinked: nothing of per-seat meters is left.
        w.context.choices = [:]
        w.meters.sync()
        #expect(try FileManager.default.contentsOfDirectory(atPath: w.dir.path).isEmpty)
    }

    @Test func withFollowOffANewSeatIsFetchedOnceAndAnIdleOneNotAgain() async throws {
        let w = try World()
        w.context.inUse = []
        w.answers["Home"] = .success(usage(12))
        w.meters.sync()                                         // launch: Home and Lab have no reading
        await w.meters.round?.value
        #expect((w.calls("Home"), w.calls("Lab"), w.calls("Work")) == (1, 1, 0))
        #expect(five(w.file("Home")) == 12)
        // Idle and Follow off: not read again, at any interval.
        w.clock += 900
        await w.activeAnswers()
        w.meters.sync()
        await w.meters.round?.value
        #expect((w.calls("Home"), w.calls("Lab")) == (1, 1))
        // A newly linked account is read once too.
        w.context.labels.append("Spare")
        w.context.choices["Spare"] = .init(folder: "/tmp/s/d")
        w.context.signedIn.insert("Spare")
        w.keys["Spare"] = "ks"
        w.meters.sync()
        await w.meters.round?.value
        #expect(w.calls("Spare") == 1)
        w.meters.sync()
        await w.meters.round?.value
        #expect(w.calls("Spare") == 1)
    }

    @Test func aSeatWithAFreshReadingAtLaunchIsNotFetched() async throws {
        let w = try World()
        w.context.inUse = []
        SnapshotWriter.writeOk(usage(5), accountRef: ref("Home"), now: w.clock.addingTimeInterval(-60), to: w.file("Home"))
        SnapshotWriter.writeOk(usage(5), accountRef: ref("Lab"), now: w.clock.addingTimeInterval(-3600), to: w.file("Lab"))
        w.meters.sync()
        await w.meters.round?.value
        #expect((w.calls("Home"), w.calls("Lab")) == (0, 1))
    }

    @Test func claudeCodeActivityInASeatFolderKeepsItReadEveryInterval() async throws {
        let w = try World()
        w.context.inUse = []
        w.meters.recentActivity = { $0 == "/tmp/s/b" }          // Home's folder
        await w.activeAnswers()
        #expect((w.calls("Home"), w.calls("Lab")) == (1, 0))
        w.clock += 20
        await w.activeAnswers()
        #expect(w.calls("Home") == 1)
        w.clock += 300
        await w.activeAnswers()
        #expect(w.calls("Home") == 2)
        // Following never hears of it: the context's in-use set is untouched.
        #expect(w.context.inUse.isEmpty)
    }
}

/// A made-up disk for SeatActivity: file times only, and a read is a failure.
private final class StatOnlyFS: CCLogFileSystem, @unchecked Sendable {
    var files: [String: Date] = [:]
    var reads = 0
    func isDirectory(_ path: String) -> Bool { files.keys.contains { $0.hasPrefix(path + "/") } }
    func subdirectories(of path: String) -> [String] { [] }
    func jsonlFiles(under path: String) -> [String] { files.keys.filter { $0.hasPrefix(path + "/") && $0.hasSuffix(".jsonl") }.sorted() }
    func stat(_ path: String) -> CCLogFileStat? { files[path].map { CCLogFileStat(size: 1, modified: $0) } }
    func read(_ path: String, from offset: UInt64) -> Data? { reads += 1; return nil }
}

@Suite("Per-seat meters: activity")
struct SeatActivityTests {
    @Test func theNewestLogWithinTenMinutesCountsByItsTimeOnly() {
        let now = Date(timeIntervalSince1970: 2_000_000)
        let fs = StatOnlyFS()
        #expect(!SeatActivity.recent(folder: "/h/.claude", fs: fs, now: now))       // no projects folder
        fs.files["/h/.claude/projects/p/a.jsonl"] = now.addingTimeInterval(-20 * 60)
        fs.files["/h/.claude/projects/p/notes.txt"] = now
        #expect(!SeatActivity.recent(folder: "/h/.claude", fs: fs, now: now))
        fs.files["/h/.claude/projects/q/sub/b.jsonl"] = now.addingTimeInterval(-5 * 60)
        #expect(SeatActivity.recent(folder: "/h/.claude", fs: fs, now: now))
        #expect(!SeatActivity.recent(folder: "/h/.claude-other", fs: fs, now: now))
        #expect(fs.reads == 0)
    }
}
