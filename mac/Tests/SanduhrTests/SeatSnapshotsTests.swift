import Foundation
import Testing
@testable import Sanduhr

/// Item 74: per-seat meters. Which accounts get their own snapshot, the fetch rule for inactive
/// ones, seats.json, the per-account writer and the cleanup on Sign Out, Remove, unlink and
/// rename. Temp folders and made-up labels only; never the real Application Support folder.
@Suite("Per-seat meters")
struct SeatSnapshotsTests {
    private func tempDir() throws -> URL {
        let dir = FileManager.default.temporaryDirectory
            .appendingPathComponent("sanduhr-seats-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        return dir
    }

    private func choices(_ folders: [String: String?]) -> [String: AccountDataChoices] {
        folders.mapValues { AccountDataChoices(folder: $0) }
    }

    private func usage(_ five: Double) -> UsageResponse {
        UsageResponse(tiers: [.fiveHour: TierUsage(utilization: five, resetsAt: "2026-10-11T18:00:00Z")], extraUsage: nil)
    }

    private func json(_ url: URL) -> [String: Any]? {
        (try? Data(contentsOf: url)).flatMap { try? JSONSerialization.jsonObject(with: $0) as? [String: Any] }
    }

    // MARK: Which accounts

    @Test func onlySignedInAccountsWithAFolderAreLinked() {
        let c = choices(["Work": "/tmp/a/.claude", "Home": "/tmp/a/.claude-home", "Spare": nil, "Gone": "/tmp/a/.claude-gone"])
        let linked = SeatSnapshots.linked(choices: c, signedIn: ["Work", "Home", "Spare"])
        #expect(linked == ["Work": "/tmp/a/.claude", "Home": "/tmp/a/.claude-home"])
    }

    @Test func offUntilTheSecondLinkedAccount() {
        #expect(!SeatSnapshots.isOn([:]))
        #expect(!SeatSnapshots.isOn(["Work": "/x"]))
        #expect(SeatSnapshots.seats(["Work": "/x"], realPath: { $0 }).isEmpty)
        #expect(SeatSnapshots.seatAccounts(["Work": "/x"]).isEmpty)
        let two = ["Work": "/x", "Home": "/y"]
        #expect(SeatSnapshots.isOn(two))
        #expect(SeatSnapshots.seatAccounts(two) == ["Work", "Home"])
        #expect(SeatSnapshots.seats(two, realPath: { $0 }) == [
            .init(folder: "/x", accountRef: AccountRef.of("Work")!),
            .init(folder: "/y", accountRef: AccountRef.of("Home")!),
        ])
    }

    // MARK: The fetch rule

    @Test func inactiveLinkedAccountsAreFetchedOnlyWhileInUse() {
        let linked = ["Work": "/x", "Home": "/y", "Lab": "/z"]
        // The active account has its own fetch; Home is in use, Lab is not.
        #expect(SeatSnapshots.due(linked: linked, active: "Work", inUse: ["Home"]) == ["Home"])
        #expect(SeatSnapshots.due(linked: linked, active: "Work", inUse: []) == [])
        #expect(SeatSnapshots.due(linked: linked, active: "Work", inUse: ["Work", "Home", "Lab"]) == ["Home", "Lab"])
        // In use but with no folder (not in `linked`): never fetched for a seat.
        #expect(SeatSnapshots.due(linked: linked, active: "Work", inUse: ["Spare"]) == [])
        // One linked account: per-seat is off, nothing extra is fetched.
        #expect(SeatSnapshots.due(linked: ["Home": "/y"], active: "Work", inUse: ["Home"]) == [])
    }

    // MARK: Paths

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
        #expect(SeatSnapshots.ref(inFileName: "snapshot.1a2b3c4.json") == nil)
        #expect(SeatSnapshots.ref(inFileName: "snapshot.../x.json") == nil)
    }

    // MARK: seats.json and the per-account files

    @Test func seatsJSONMapsFoldersToRefsAndNeverALabel() throws {
        let dir = try tempDir()
        defer { try? FileManager.default.removeItem(at: dir) }
        let files = SeatSnapshots.Files(dir: dir)
        let seats = SeatSnapshots.seats(["Work Label": "/tmp/s/.claude", "Home Label": "/tmp/s/.claude-home"], realPath: { $0 })
        SeatSnapshots.sync(seats, in: files)
        let text = try String(contentsOf: files.seats, encoding: .utf8)
        #expect(!text.contains("Label"))
        let obj = try #require(json(files.seats))
        #expect(obj["schema_version"] as? Int == 1)
        let rows = try #require(obj["seats"] as? [[String: String]])
        #expect(rows == [
            ["folder": "/tmp/s/.claude", "account_ref": AccountRef.of("Work Label")!],
            ["folder": "/tmp/s/.claude-home", "account_ref": AccountRef.of("Home Label")!],
        ])
        #expect(SeatSnapshots.counts(in: files).seats == 2)
    }

    @Test func eachSeatGetsItsOwnSnapshotInTheSameSchema() throws {
        let dir = try tempDir()
        defer { try? FileManager.default.removeItem(at: dir) }
        let files = SeatSnapshots.Files(dir: dir)
        let work = AccountRef.of("Work")!, home = AccountRef.of("Home")!
        SnapshotWriter.writeOk(usage(42), accountRef: work, now: Date(timeIntervalSince1970: 0), to: files.snapshot(work))
        SnapshotWriter.writeOk(usage(7), accountRef: home, now: Date(timeIntervalSince1970: 0), to: files.snapshot(home))
        let w = try #require(json(files.snapshot(work)))
        #expect(w["schema_version"] as? Int == SnapshotWriter.schemaVersion)
        #expect(w["writer_version"] is String)
        #expect(w["account_ref"] as? String == work)
        #expect(w["status"] as? String == "ok")
        #expect(((w["tiers"] as? [[String: Any]])?.first?["utilization"] as? Int) == 42)
        #expect(((json(files.snapshot(home))?["tiers"] as? [[String: Any]])?.first?["utilization"] as? Int) == 7)
        #expect(Set(w.keys) == ["schema_version", "writer_version", "captured_at", "account_ref", "plan", "status", "error_kind", "tiers"])
        #expect(SeatSnapshots.counts(in: files).snapshots == 2)
    }

    @Test func aFailedSeatFetchKeepsThatSeatsLastTiers() throws {
        let dir = try tempDir()
        defer { try? FileManager.default.removeItem(at: dir) }
        let files = SeatSnapshots.Files(dir: dir)
        let home = AccountRef.of("Home")!
        SnapshotWriter.writeOk(usage(33), accountRef: home, to: files.snapshot(home))
        SnapshotWriter.writeError(UsageViewModel.errorKind(ClaudeAPI.APIError.unauthorized), accountRef: home, to: files.snapshot(home))
        let h = try #require(json(files.snapshot(home)))
        #expect(h["status"] as? String == "error")
        #expect(h["error_kind"] as? String == "session_expired")
        #expect(((h["tiers"] as? [[String: Any]])?.first?["utilization"] as? Int) == 33)
        #expect(UsageViewModel.errorKind(ClaudeAPI.APIError.cloudflareChallenge) == "cloudflare")
        #expect(UsageViewModel.errorKind(ClaudeAPI.APIError.http(500)) == "network")
    }

    @Test func aSeatThatGoesLosesItsFileAndItsRow() throws {
        let dir = try tempDir()
        defer { try? FileManager.default.removeItem(at: dir) }
        let files = SeatSnapshots.Files(dir: dir)
        let both = ["Work": "/tmp/s/a", "Home": "/tmp/s/b", "Lab": "/tmp/s/c"]
        SeatSnapshots.sync(SeatSnapshots.seats(both, realPath: { $0 }), in: files)
        for label in both.keys {
            let ref = AccountRef.of(label)!
            SnapshotWriter.writeOk(usage(1), accountRef: ref, to: files.snapshot(ref))
        }
        // The active account's snapshot.json is never touched.
        SnapshotWriter.writeOk(usage(1), accountRef: AccountRef.of("Work"), to: dir.appendingPathComponent("snapshot.json"))
        // Lab signed out (or removed, or unlinked): its row and its file go.
        SeatSnapshots.sync(SeatSnapshots.seats(["Work": "/tmp/s/a", "Home": "/tmp/s/b"], realPath: { $0 }), in: files)
        #expect(files.snapshotRefs() == [AccountRef.of("Work")!, AccountRef.of("Home")!])
        #expect(SeatSnapshots.counts(in: files) == (2, 2))
        // Renamed: the old ref's file goes; the new one is written at its next fetch.
        SeatSnapshots.sync(SeatSnapshots.seats(["Office": "/tmp/s/a", "Home": "/tmp/s/b"], realPath: { $0 }), in: files)
        #expect(files.snapshotRefs() == [AccountRef.of("Home")!])
        let rows = json(files.seats)?["seats"] as? [[String: String]]
        #expect(rows?.first?["account_ref"] == AccountRef.of("Office"))
        // Down to one linked account: per-seat is off, every seat file and seats.json go.
        SeatSnapshots.sync(SeatSnapshots.seats(["Home": "/tmp/s/b"], realPath: { $0 }), in: files)
        #expect(files.snapshotRefs().isEmpty)
        #expect(!FileManager.default.fileExists(atPath: files.seats.path))
        #expect(FileManager.default.fileExists(atPath: dir.appendingPathComponent("snapshot.json").path))
        #expect(SeatSnapshots.counts(in: files) == (0, 0))
    }

    @Test func anExistingInstallSeesNoNewFiles() throws {
        let dir = try tempDir()
        defer { try? FileManager.default.removeItem(at: dir) }
        let files = SeatSnapshots.Files(dir: dir)
        SeatSnapshots.sync(SeatSnapshots.seats(["Work": "/tmp/s/a"], realPath: { $0 }), in: files)
        SeatSnapshots.sync([], in: files)
        #expect(try FileManager.default.contentsOfDirectory(atPath: dir.path).isEmpty)
    }

    @Test func seatsJSONIsRewrittenOnlyWhenItChanges() throws {
        let dir = try tempDir()
        defer { try? FileManager.default.removeItem(at: dir) }
        let files = SeatSnapshots.Files(dir: dir)
        let seats = SeatSnapshots.seats(["Work": "/tmp/s/a", "Home": "/tmp/s/b"], realPath: { $0 })
        SeatSnapshots.sync(seats, in: files)
        let before = try FileManager.default.attributesOfItem(atPath: files.seats.path)[.modificationDate] as? Date
        let inode = try FileManager.default.attributesOfItem(atPath: files.seats.path)[.systemFileNumber] as? Int
        SeatSnapshots.sync(seats, in: files)
        #expect(try FileManager.default.attributesOfItem(atPath: files.seats.path)[.systemFileNumber] as? Int == inode)
        #expect(try FileManager.default.attributesOfItem(atPath: files.seats.path)[.modificationDate] as? Date == before)
        // A link moved to another folder: rewritten.
        SeatSnapshots.sync(SeatSnapshots.seats(["Work": "/tmp/s/c", "Home": "/tmp/s/b"], realPath: { $0 }), in: files)
        #expect((json(files.seats)?["seats"] as? [[String: String]])?.contains { $0["folder"] == "/tmp/s/c" } == true)
    }

    // MARK: Settings

    @Test func folderBoxSaysWhoseMetersShow() {
        #expect(SeatSnapshots.metersHere(owner: "Work", seatAccounts: ["Work", "Home"]) == "Meters here show Work")
        #expect(SeatSnapshots.metersHere(owner: nil, seatAccounts: ["Work", "Home"]) == "Meters here show the active account")
        // Linked, but the only linked account: the folder still shows the active account.
        #expect(SeatSnapshots.metersHere(owner: "Work", seatAccounts: []) == "Meters here show the active account")
    }

    @Test func linkFolderIsANamedControlOnAccounts() {
        #expect(SettingsNames.table.contains { $0.name == SettingsNames.linkFolder && $0.page == .credentials })
        #expect(SettingsNames.anchors[SettingsNames.linkFolder] == SettingsAnchor.data)
    }
}
