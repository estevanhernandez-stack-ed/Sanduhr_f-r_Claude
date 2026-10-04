import Foundation
import Testing
@testable import Sanduhr

/// Item 48's stewardship: every record on this Mac listed, linked or not, with its size, oldest
/// day and account, and erase by vault id, which reaches records no account links any more and
/// waits for a recording account's tombstone. Temp vaults and in-memory defaults only.
@Suite("Records on this Mac")
struct VaultStewardshipTests {
    typealias B = VaultBed

    static func day(_ s: String) -> CCLocalDay { CCLocalDay(key: s)! }

    @Test func everyRecordIsListedLinkedOrNot() throws {
        let b = VaultBed()
        defer { b.cleanUp() }
        let d = MemoryDefaults()
        let work = b.home.at(".claude-work")
        let personal = b.home.at(".claude")
        AccountData.link(work, to: "Work", in: d)
        AccountData.setActivity(.record, for: "Work", in: d)
        AccountData.link(personal, to: "Personal", in: d)
        AccountData.setActivity(.live, for: "Personal", in: d)
        let workID = VaultFolderID.of(work)
        let personalID = VaultFolderID.of(personal)
        let orphan = VaultFolderID.of(b.home.at(".claude-old"))
        VaultReaderTests.saveRollup(b, workID, "2026-06", [("2026-06-03", 10, "api~1", ""), ("2026-06-20", 10, "api~1", "")])
        VaultReaderTests.saveShard(b, workID, "2026-06", [("u1", VaultReaderTests.row("api~1", "2026-06-03T10:00:00Z", "2026-06-03T11:00:00Z", continuation: false, [("2026-06-03", 10)]))])
        VaultReaderTests.saveShard(b, workID, "2026-07", [("u2", VaultReaderTests.row("api~1", "2026-07-03T10:00:00Z", "2026-07-03T11:00:00Z", continuation: false, [("2026-07-03", 10)]))])
        // The orphan has a session shard but no rollups: its oldest day comes from the shard.
        VaultReaderTests.saveShard(b, orphan, "2026-05", [("u3", VaultReaderTests.row("x~2", "2026-05-09T10:00:00Z", "2026-05-09T11:00:00Z", continuation: false, [("2026-05-09", 4)]))])
        // Kept from when Personal recorded: linked, not recording, meta only.
        b.store.saveMeta(personalID, VaultReaderTests.meta("2026-04-01", []))
        FileManager.default.createFile(atPath: (b.vaultDir as NSString).appendingPathComponent(".writer.lock"), contents: nil)

        let list = VaultStewardship.records(in: b.store, choices: AccountData.allChoices(in: d))
        #expect(list.map(\.id) == [personalID, workID, orphan])
        #expect(list.map(\.account) == ["Personal", "Work", nil])
        #expect(list.map(\.recording) == [false, true, false])
        #expect(list.map(\.folder) == [personal, work, nil])
        #expect(list.map(\.oldestDay) == [Self.day("2026-04-01"), Self.day("2026-06-03"), Self.day("2026-05-09")])
        #expect(list.allSatisfy { $0.bytes > 0 })
        let workDir = b.store.rootDir(workID)
        let onDisk = try FileManager.default.contentsOfDirectory(atPath: workDir).reduce(Int64(0)) { sum, name in
            let a = try FileManager.default.attributesOfItem(atPath: (workDir as NSString).appendingPathComponent(name))
            return sum + ((a[.size] as? NSNumber)?.int64Value ?? 0)
        }
        #expect(list[1].bytes == onDisk)
        #expect(list[2].isLinked == false)
        #expect(list[2].shortID == String(orphan.prefix(8)))
    }

    @Test func anUnlinkedRecordErasesAndARecordingOneWaitsForTheTombstone() {
        let b = VaultBed()
        defer { b.cleanUp() }
        let d = MemoryDefaults()
        let s = VaultServiceTests.service(b, d)
        let orphan = VaultFolderID.of(b.home.at(".claude-old"))
        b.store.saveMeta(orphan, VaultReaderTests.meta("2026-07-01", []))
        #expect(s.eraseNow(id: orphan, wait: 1))
        #expect(!b.store.rootExists(orphan))

        let folder = VaultServiceTests.recordingWork(b, d)
        s.ingestNow(now: B.now)
        let id = VaultFolderID.of(folder)
        #expect(b.store.rootExists(id))
        #expect(!s.eraseNow(id: id, wait: 1))              // still recorded: nothing goes
        #expect(b.store.rootExists(id))
        AccountData.setActivity(.live, for: "Work", in: d)  // the tombstone first (item 46)
        #expect(s.eraseNow(id: id, wait: 1))
        #expect(!b.store.rootExists(id))
        #expect(s.records(AccountData.allChoices(in: d)).isEmpty)
    }

    @Test func eraseByIdNeverLeavesTheVault() {
        #expect(VaultStewardship.isRecordID("0123456789abcdef"))
        for bad in ["", ".", "..", "../x", "a/b", ".writer.lock", ".claude"] {
            #expect(!VaultStewardship.isRecordID(bad), "\(bad)")
        }
        let b = VaultBed()
        defer { b.cleanUp() }
        let s = VaultServiceTests.service(b, MemoryDefaults())
        #expect(!s.eraseNow(id: "..", wait: 0.1))
        #expect(FileManager.default.fileExists(atPath: b.vaultDir))
    }
}
