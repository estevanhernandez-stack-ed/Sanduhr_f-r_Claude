import Foundation
import Testing
@testable import Sanduhr

/// Defaults that run a hook on reads, to change a choice at an exact moment of a cycle.
final class HookDefaults: DefaultsStore, @unchecked Sendable {
    private let lock = NSLock()
    private var values: [String: Any] = [:]
    private var reads = 0
    private var trigger = 0
    private var action: (() -> Void)?

    /// Runs `action` once, on the `n`th read from now (before that read answers).
    func arm(onRead n: Int, _ action: @escaping () -> Void) {
        lock.lock()
        reads = 0
        trigger = n
        self.action = action
        lock.unlock()
    }

    func object(forKey key: String) -> Any? {
        lock.lock()
        reads += 1
        var fire: (() -> Void)?
        if reads == trigger {
            fire = action
            action = nil
        }
        lock.unlock()
        fire?()
        lock.lock()
        defer { lock.unlock() }
        return values[key]
    }

    func bool(forKey key: String) -> Bool { object(forKey: key) as? Bool ?? false }
    func string(forKey key: String) -> String? { object(forKey: key) as? String }

    func set(_ value: Any?, forKey key: String) {
        lock.lock()
        values[key] = value
        lock.unlock()
    }
}

/// Item 46 per account: who is recorded, the folder id, Live only and Not tracked never
/// writing, erase completeness and the erase race. In-memory defaults and temp folders only.
@Suite("Vault per account")
struct VaultServiceTests {
    typealias B = VaultBed

    static func service(_ b: VaultBed, _ d: DefaultsStore) -> VaultService {
        VaultService(vaultDir: b.vaultDir, defaults: { d }, writerVersion: "test", timeZone: B.cst,
                     log: b.sink, dirHoldsGit: { _ in false })
    }

    /// Work keeps a record of `~/.claude-work`, which holds one session of 150 tokens.
    static func recordingWork(_ b: VaultBed, _ d: DefaultsStore, names: ProjectNamesChoice = .names) -> String {
        b.session("u1", root: ".claude-work", [B.line("2026-07-10T15:00:00Z")])
        let folder = b.home.at(".claude-work")
        AccountData.link(folder, to: "Work", in: d)
        AccountData.setActivity(.record, for: "Work", in: d)
        AccountData.setNames(names, for: "Work", in: d)
        return folder
    }

    @Test func folderIdIsStableShortAndNotThePath() {
        let id = VaultFolderID.of("/Users/x/.claude-work")
        #expect(id.count == 16)
        #expect(id.allSatisfy { $0.isHexDigit && !$0.isUppercase })
        #expect(VaultFolderID.of("/Users/x/.claude-work/") == id)
        #expect(VaultFolderID.of("/Users/x/../x/.claude-work") == id)
        #expect(VaultFolderID.of("/Users/X/.Claude-Work") == id)          // case-insensitive volumes
        #expect(VaultFolderID.of("/Users/x/.claude") != id)
        #expect(id == String(VaultStore.sha256Hex("/users/x/.claude-work").prefix(16)))
    }

    @Test func everyRecordingAccountWithAFolderIsRecorded() {
        let d = MemoryDefaults()
        AccountData.link("/h/.claude-work", to: "Work", in: d)
        AccountData.setActivity(.record, for: "Work", in: d)
        AccountData.link("/h/.claude", to: "Home", in: d)
        AccountData.setActivity(.live, for: "Home", in: d)
        AccountData.link("/h/.claude-old", to: "Old", in: d)
        AccountData.setActivity(.record, for: "Solo", in: d)             // no folder
        AccountData.link("/h/.claude-client", to: "Client", in: d)
        AccountData.setActivity(.record, for: "Client", in: d)
        AccountData.setNames(.hidden, for: "Client", in: d)

        let roots = VaultService.roots(in: d)
        #expect(roots.map(\.folder) == ["/h/.claude-client", "/h/.claude-work"])
        #expect(roots.map(\.names) == [.hidden, .names])
        #expect(roots.map(\.id) == [VaultFolderID.of("/h/.claude-client"), VaultFolderID.of("/h/.claude-work")])
        #expect(VaultService.isRecording(VaultFolderID.of("/h/.claude-work"), in: d))
        #expect(!VaultService.isRecording(VaultFolderID.of("/h/.claude"), in: d))
    }

    /// A triggered cycle calls onCycleEnd and lets the next trigger run. Reading onCycleEnd while
    /// holding the service's lock deadlocked the vault queue on the first finished cycle, and the
    /// main thread with it on the next trigger (2026-10-04). Waits with a timeout so a regression
    /// fails instead of hanging the run.
    @Test func cyclesEndAndTheNextTriggerRuns() {
        let b = VaultBed()
        defer { b.cleanUp() }
        let d = MemoryDefaults()
        _ = Self.recordingWork(b, d)
        let s = Self.service(b, d)
        let ended = DispatchSemaphore(value: 0)
        s.onCycleEnd = { ended.signal() }
        s.trigger()
        #expect(ended.wait(timeout: .now() + 10) == .success)
        s.trigger()
        #expect(ended.wait(timeout: .now() + 10) == .success)
    }

    @Test func liveOnlyAndNotTrackedNeverWrite() {
        let b = VaultBed()
        defer { b.cleanUp() }
        let d = MemoryDefaults()
        b.session("u1", root: ".claude-work", [B.line("2026-07-10T15:00:00Z")])
        b.session("u2", root: ".claude", [B.line("2026-07-10T15:00:00Z")])
        AccountData.link(b.home.at(".claude-work"), to: "Work", in: d)
        AccountData.setActivity(.live, for: "Work", in: d)
        AccountData.link(b.home.at(".claude"), to: "Home", in: d)          // Not tracked
        let s = Self.service(b, d)
        let r = s.ingestNow(now: B.now)
        s.trigger()
        #expect(r.filesSeen == 0)
        #expect(b.snapshot().isEmpty)
        #expect(b.files(in: b.vaultDir).isEmpty)
        #expect(s.state(for: AccountData.choices(for: "Work", in: d)) == VaultState(recording: false, months: 0, lastIngestOK: false))
    }

    @Test func aRecordLivesUnderTheFolderIdAndNamesNothing() {
        let b = VaultBed()
        defer { b.cleanUp() }
        let d = MemoryDefaults()
        let folder = Self.recordingWork(b, d)
        let s = Self.service(b, d)
        let r = s.ingestNow(now: B.now)
        #expect(r.filesFullParsed == 1)
        let id = VaultFolderID.of(folder)
        #expect(b.files(in: b.vaultDir) == [".writer.lock", id])
        #expect(b.files(in: b.rootPath(id)) == ["checkpoints.json", "meta.json", "rollups-2026-07.json", "sessions-2026-07.json"])
        #expect(b.shard(root: id).sessions["u1"]?.total == 150)
        for (path, bytes) in b.snapshot() {
            #expect(!path.contains("claude-work") && !path.contains("Work"))
            #expect(!String(decoding: bytes, as: UTF8.self).contains("claude-work"))
        }
        #expect(s.state(for: AccountData.choices(for: "Work", in: d)) == VaultState(recording: true, months: 1, lastIngestOK: true))
        #expect(s.months(folder: folder) == 1)
        #expect(b.logs.isEmpty)
    }

    @Test func aSecondSanduhrSkipsTheCycle() {
        let b = VaultBed()
        defer { b.cleanUp() }
        let d = MemoryDefaults()
        let folder = Self.recordingWork(b, d)
        let s = Self.service(b, d)
        let other = VaultWriterLock.tryAcquire(b.vaultDir + "/.writer.lock")
        let r = s.ingestNow(now: B.now)
        #expect(!r.acquired)
        #expect(!s.hasRecord(folder: folder))
        #expect(s.state(for: AccountData.choices(for: "Work", in: d)).lastIngestOK == false)
        other?.release()
    }

    @Test func hiddenFromTheAccountsChoice() {
        let b = VaultBed()
        defer { b.cleanUp() }
        let d = MemoryDefaults()
        let folder = Self.recordingWork(b, d, names: .hidden)
        Self.service(b, d).ingestNow(now: B.now)
        let row = b.shard(root: VaultFolderID.of(folder)).sessions["u1"]
        #expect(row?.projectName == VaultHiddenName.of("api"))
        #expect(row?.cwd == nil)
    }

    // MARK: Erase

    @Test func eraseIsCompleteAndStaysErased() {
        let b = VaultBed()
        defer { b.cleanUp() }
        let d = MemoryDefaults()
        let folder = Self.recordingWork(b, d)
        let s = Self.service(b, d)
        s.ingestNow(now: B.now)
        let id = VaultFolderID.of(folder)
        FileManager.default.createFile(atPath: b.rootPath(id) + "/sessions-2026-05.json.20260601T000000.bad",
                                       contents: Data("x".utf8))

        #expect(!s.eraseNow(folder: folder))                 // still recording: refused
        #expect(s.hasRecord(folder: folder))

        AccountData.setActivity(.live, for: "Work", in: d)   // the tombstone
        #expect(s.eraseNow(folder: folder))
        #expect(!s.hasRecord(folder: folder))
        #expect(b.files(in: b.vaultDir) == [".writer.lock"])

        let again = s.ingestNow(now: B.now.addingTimeInterval(300))
        #expect(again.filesSeen == 0)
        #expect(!s.hasRecord(folder: folder))
        #expect(s.state(for: AccountData.choices(for: "Work", in: d)) == VaultState(recording: false, months: 0, lastIngestOK: false))
    }

    @Test func removingTheAccountFreesItsRecordForErase() {
        let b = VaultBed()
        defer { b.cleanUp() }
        let d = MemoryDefaults()
        let folder = Self.recordingWork(b, d)
        let s = Self.service(b, d)
        s.ingestNow(now: B.now)
        AccountData.forget("Work", in: d)                     // what Remove Account does
        #expect(s.eraseNow(folder: folder))
        #expect(!s.hasRecord(folder: folder))
        s.ingestNow(now: B.now)
        #expect(!s.hasRecord(folder: folder))
    }

    @Test func anInFlightCycleStopsWhenRecordingEndsAndWritesNothing() {
        let b = VaultBed()
        defer { b.cleanUp() }
        let d = HookDefaults()
        let folder = Self.recordingWork(b, d)
        for i in 0..<20 { b.session("s\(i)", root: ".claude-work", [B.line("2026-07-10T15:00:00Z")]) }
        let s = Self.service(b, d)
        // Choosing the folders takes two reads (the keys, then Work's choices), and so does each
        // check before a file. On the fifth read, one file in, Work turns Keep a record off.
        d.arm(onRead: 5) { AccountData.setActivity(.live, for: "Work", in: d) }
        let r = s.ingestNow(now: B.now)
        #expect(r.rootsWithdrawn == 1)
        #expect(r.filesSeen == 1)
        #expect(!s.hasRecord(folder: folder))
        #expect(s.eraseNow(folder: folder))
    }

    @Test func eraseWaitsForTheWriterThenDeletes() async {
        let b = VaultBed()
        defer { b.cleanUp() }
        let d = MemoryDefaults()
        let folder = Self.recordingWork(b, d)
        let s = Self.service(b, d)
        s.ingestNow(now: B.now)
        AccountData.setActivity(.off, for: "Work", in: d)

        // Another process's cycle holds the lock; it lets go after 0.3 s.
        let held = VaultWriterLock.tryAcquire(b.vaultDir + "/.writer.lock")
        #expect(held != nil)
        let start = Date()
        let erased = Task.detached { s.eraseNow(folder: folder, wait: 5) }
        try? await Task.sleep(nanoseconds: 300_000_000)
        #expect(s.hasRecord(folder: folder))                 // still waiting
        held?.release()
        #expect(await erased.value)
        #expect(Date().timeIntervalSince(start) >= 0.3)
        #expect(!s.hasRecord(folder: folder))
    }

    @Test func aWriterThatNeverLetsGoDoesNotBlockErase() {
        let b = VaultBed()
        defer { b.cleanUp() }
        let d = MemoryDefaults()
        let folder = Self.recordingWork(b, d)
        let s = Self.service(b, d)
        s.ingestNow(now: B.now)
        AccountData.setActivity(.live, for: "Work", in: d)
        let held = VaultWriterLock.tryAcquire(b.vaultDir + "/.writer.lock")
        #expect(s.eraseNow(folder: folder, wait: 0.2))
        #expect(!s.hasRecord(folder: folder))
        held?.release()
        // The late pass runs once the lock is free; the record stays gone either way.
        usleep(200_000)
        #expect(!s.hasRecord(folder: folder))
    }

    @Test func eraseLeavesOtherFoldersAlone() {
        let b = VaultBed()
        defer { b.cleanUp() }
        let d = MemoryDefaults()
        let work = Self.recordingWork(b, d)
        b.session("u9", root: ".claude", [B.line("2026-07-10T15:00:00Z")])
        let home = b.home.at(".claude")
        AccountData.link(home, to: "Home", in: d)
        AccountData.setActivity(.record, for: "Home", in: d)
        let s = Self.service(b, d)
        s.ingestNow(now: B.now)
        #expect(s.hasRecord(folder: work) && s.hasRecord(folder: home))
        AccountData.setActivity(.live, for: "Work", in: d)
        #expect(s.eraseNow(folder: work))
        #expect(!s.hasRecord(folder: work))
        #expect(s.hasRecord(folder: home))
    }

    @Test func stateYamlCarriesFlagsAndACountOnly() {
        let yaml = YAMLEmitter.emit(.object([("vault", DebugState.vaultYAML(
            VaultState(recording: true, months: 3, lastIngestOK: true)))]))
        #expect(yaml == "vault:\n  recording: true\n  months: 3\n  last_ingest_ok: true\n")
    }
}
