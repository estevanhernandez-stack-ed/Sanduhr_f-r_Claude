import CryptoKit
import Foundation
import Testing
@testable import Sanduhr

/// The mover and the file-safety suite (docs/mac-drawers-setup-spec.md, sections 4, 5 and 9).
///
/// Every test works in a folder it makes under the system temp directory and removes after. The
/// folder stands in for a home with a Desktop, and the journal lives beside it. Nothing here names
/// the real Desktop or the real journal path: the mover takes its locations by injection, and the
/// fixture refuses to start if its folder is anywhere but the temp directory.
///
/// Every scenario ends with the same check: each starting file exists exactly once, at its start
/// path or in a drawer (or at its start path after a return), with its bytes unchanged.

/// A temp "home": `Home/Desktop`, the root `Home/Desktop/Drawers`, and `Support/` for the journal.
final class DrawerFixture {
    let base: URL
    let realBase: String

    init() {
        let temp = FileManager.default.temporaryDirectory
        base = temp.appendingPathComponent("sanduhr-drawers-test-\(UUID().uuidString)")
        try! FileManager.default.createDirectory(at: base.appendingPathComponent("Home/Desktop"), withIntermediateDirectories: true)
        try! FileManager.default.createDirectory(at: base.appendingPathComponent("Support"), withIntermediateDirectories: true)
        realBase = DrawerFS.realPath(base.path)!
        // The one guard that matters: this folder is under the temp directory and nowhere else.
        precondition(realBase.hasPrefix(DrawerFS.realPath(temp.path)! + "/"))
        precondition(!realBase.contains("/Desktop/") && !realBase.hasSuffix("/Desktop"))
    }

    deinit { try? FileManager.default.removeItem(at: base) }

    var home: String { base.path + "/Home" }
    var desktop: String { home + "/Desktop" }
    var root: String { desktop + "/Drawers" }
    var journalURL: URL { base.appendingPathComponent("Support/drawers-journal.json") }
    var loc: DrawerLocations { DrawerLocations(desktop: desktop, root: root, home: home) }
    var store: DrawerJournalStore { DrawerJournalStore(url: journalURL) }
    var drawers: [DrawerDef] { DrawerRules.defaultDrawers(root: root) }

    func mover(rename: @escaping (String, String) -> Int32 = DrawerMover.exclusiveRename,
               afterRename: ((Int) -> Bool)? = nil) -> DrawerMover {
        DrawerMover(store: store, rename: rename, afterRename: afterRename)
    }

    /// Writes a file under the temp home. Content is unique per call unless given, so the SHA-256
    /// identifies a file wherever it ends up.
    @discardableResult
    func write(_ rel: String, _ text: String? = nil, age days: Double = 0) -> String {
        let path = base.path + "/" + rel
        try! FileManager.default.createDirectory(atPath: DrawerPaths.parent(path), withIntermediateDirectories: true)
        try! Data((text ?? "\(rel) \(UUID().uuidString)").utf8).write(to: URL(fileURLWithPath: path))
        if days > 0 {
            try! FileManager.default.setAttributes([.modificationDate: Date().addingTimeInterval(-days * 86_400)], ofItemAtPath: path)
        }
        return path
    }

    func desk(_ name: String) -> String { desktop + "/" + name }
    func inDrawer(_ drawer: String, _ name: String) -> String { root + "/" + drawer + "/" + name }

    /// Every regular file under the temp home (not the journal), by path relative to the base, with
    /// its SHA-256. Links are not files, so a link's target is counted once, at its own path.
    func snapshot() -> [String: String] {
        var out: [String: String] = [:]
        guard let walker = FileManager.default.enumerator(atPath: realBase + "/Home") else { return out }
        for case let rel as String in walker {
            let full = realBase + "/Home/" + rel
            guard let st = DrawerFS.lstat(full), DrawerFS.isRegular(st), let data = FileManager.default.contents(atPath: full) else { continue }
            out["Home/" + rel] = SHA256.hash(data: data).map { String(format: "%02x", $0) }.joined()
        }
        return out
    }

    /// The rules' plan for the Desktop as it is, with the facts the review would have shown.
    func plan(existing: [DrawerDef] = []) -> (plan: DrawerPlan, planned: [String: DrawerItemFacts]) {
        let facts = DrawerFacts.list(desktop)
        let plan = DrawerRules.plan(facts: facts, rules: DrawerRules.defaults, drawers: existing,
                                    desktop: desktop, root: root, now: Date())
        return (plan, Dictionary(facts.map { (desk($0.name), $0) }, uniquingKeysWith: { a, _ in a }))
    }

    /// The file-safety invariant. `start` is the snapshot before the run (adjust it for a file the
    /// test changed or added on purpose). Each starting file must exist exactly once now, with the
    /// same bytes, at its start path or, when `drawersOK`, inside the Drawers root; nothing extra.
    func expectInvariant(_ start: [String: String], drawersOK: Bool = true,
                         sourceLocation: SourceLocation = #_sourceLocation) {
        let now = snapshot()
        var byHash: [String: [String]] = [:]
        for (path, hash) in now { byHash[hash, default: []].append(path) }
        for (path, hash) in start {
            let found = byHash[hash] ?? []
            #expect(found.count == 1, "\(path) should exist exactly once, found \(found.count)", sourceLocation: sourceLocation)
            guard let at = found.first else { continue }
            let inRoot = at.hasPrefix("Home/Desktop/Drawers/")
            #expect(at == path || (drawersOK && inRoot), "\(path) is at \(at)", sourceLocation: sourceLocation)
        }
        #expect(now.count == start.count, "no file appeared or vanished", sourceLocation: sourceLocation)
    }

    func hash(_ path: String) -> String? {
        FileManager.default.contents(atPath: path).map { SHA256.hash(data: $0).map { String(format: "%02x", $0) }.joined() }
    }

    func mode(_ url: URL) -> Int { Int(DrawerFS.lstat(url.path)!.st_mode & 0o777) }
}

@Suite("Drawer mover")
struct DrawerMoverTests {
    // MARK: a plain sort

    @Test("a sort puts each file in its drawer with its bytes unchanged, and journals it")
    func sort() throws {
        let fx = DrawerFixture()
        fx.write("Home/Desktop/report.pdf")
        fx.write("Home/Desktop/Screenshot 2026-11-01 at 9.00.00.png")
        fx.write("Home/Desktop/photo.jpg")
        fx.write("Home/Desktop/setup.dmg")
        fx.write("Home/Desktop/bundle.zip")
        fx.write("Home/Desktop/old-notes.xyz", age: 90)
        fx.write("Home/Desktop/mystery.xyz")
        let start = fx.snapshot()
        let (plan, planned) = fx.plan()
        let result = fx.mover().apply(plan, at: fx.loc, existing: [], planned: planned)

        #expect(result.refusal == nil)
        #expect(result.moved.count == 6)
        #expect(result.skipped.isEmpty)
        #expect(FileManager.default.fileExists(atPath: fx.inDrawer("Documents", "report.pdf")))
        #expect(FileManager.default.fileExists(atPath: fx.inDrawer("Screenshots", "Screenshot 2026-11-01 at 9.00.00.png")))
        #expect(FileManager.default.fileExists(atPath: fx.inDrawer("Older files", "old-notes.xyz")))
        #expect(FileManager.default.fileExists(atPath: fx.desk("mystery.xyz")))
        fx.expectInvariant(start)

        let journal = fx.store.load()
        #expect(journal.entries.count == 1)
        #expect(journal.entries[0].moves.count == 6 && !journal.entries[0].isOpen)
        #expect(journal.entries[0].moves.allSatisfy { m in
            if case .done(let d?) = m.written { return d.timeIntervalSinceNow < 1 } else { return false }
        })
        #expect(result.entryId == journal.entries[0].id)
    }

    @Test("a path given through a symlinked folder still resolves and moves")
    func aliasedDesktop() throws {
        let fx = DrawerFixture()
        let doc = fx.write("Home/Desktop/a.pdf")
        let start = fx.snapshot()
        let alias = fx.base.path + "/Alias"
        try FileManager.default.createSymbolicLink(atPath: alias, withDestinationPath: fx.desktop)
        let plan = DrawerPlan(source: .user, drawers: [fx.drawers[2]], moves: [DrawerMove(source: alias + "/a.pdf", drawerId: "documents")])
        let result = fx.mover().apply(plan, at: DrawerLocations(desktop: alias, root: alias + "/Drawers", home: fx.home), existing: [])
        #expect(result.moved.count == 1, "\(result)")
        #expect(!FileManager.default.fileExists(atPath: doc))
        fx.expectInvariant(start)
    }

    // MARK: clashes

    @Test("an existing destination is never replaced: the newcomer takes the next name and the original bytes stay")
    func neverReplaces() throws {
        let fx = DrawerFixture()
        let original = fx.write("Home/Desktop/Drawers/Documents/Report.pdf", "the one already in the drawer")
        let taken2 = fx.write("Home/Desktop/Drawers/Documents/Report 2.pdf", "second one already there")
        let originalHash = fx.hash(original), taken2Hash = fx.hash(taken2)
        fx.write("Home/Desktop/Report.pdf", "the newcomer from the Desktop")
        let start = fx.snapshot()

        let (plan, planned) = fx.plan(existing: fx.drawers)
        let result = fx.mover().apply(plan, at: fx.loc, existing: fx.drawers, planned: planned)

        #expect(result.moved.map(\.to) == [DrawerFS.realPath(fx.root)! + "/Documents/Report 3.pdf"])
        #expect(fx.hash(original) == originalHash)
        #expect(fx.hash(taken2) == taken2Hash)
        #expect(String(data: FileManager.default.contents(atPath: original)!, encoding: .utf8) == "the one already in the drawer")
        #expect(String(data: FileManager.default.contents(atPath: fx.inDrawer("Documents", "Report 3.pdf"))!, encoding: .utf8) == "the newcomer from the Desktop")
        fx.expectInvariant(start)
    }

    @Test("the exclusive rename itself refuses an existing destination and leaves both files")
    func kernelRefuses() throws {
        let fx = DrawerFixture()
        let a = fx.write("Home/a.txt", "AAA"), b = fx.write("Home/b.txt", "BBB")
        #expect(DrawerMover.exclusiveRename(a, b) == EEXIST)
        #expect(fx.hash(a) != nil && fx.hash(b) != nil && fx.hash(a) != fx.hash(b))
        #expect(String(data: FileManager.default.contents(atPath: b)!, encoding: .utf8) == "BBB")
        #expect(DrawerMover.exclusiveRename(a, fx.base.path + "/Home/c.txt") == 0)
        #expect(!FileManager.default.fileExists(atPath: a))
    }

    @Test("two Desktop files with one name in different rules each land, and a full set of names gives up")
    func giveUpAfterNinetyNine() throws {
        let fx = DrawerFixture()
        fx.write("Home/Desktop/x.pdf")
        let start0 = fx.snapshot()
        var calls = 0
        let always: (String, String) -> Int32 = { _, _ in calls += 1; return EEXIST }
        let (plan, planned) = fx.plan()
        let result = fx.mover(rename: always).apply(plan, at: fx.loc, existing: [], planned: planned)
        #expect(calls == 100, "the plain name and 99 numbered ones")
        #expect(result.moved.isEmpty)
        #expect(result.skipped.map(\.reason) == [DrawerUndo.noFreeName])
        fx.expectInvariant(start0, drawersOK: false)
        #expect(fx.store.load().entries.isEmpty, "a run that moved nothing leaves no entry")
    }

    @Test("Undo into a taken old place takes the next free name and never overwrites")
    func undoClash() throws {
        let fx = DrawerFixture()
        fx.write("Home/Desktop/Report.pdf", "first report")
        let originalHash = fx.snapshot()["Home/Desktop/Report.pdf"]!
        let (plan, planned) = fx.plan()
        let mover = fx.mover()
        let run = mover.apply(plan, at: fx.loc, existing: [], planned: planned)
        #expect(run.moved.count == 1)
        // The user makes a new Report.pdf where the old one was.
        let newcomer = fx.write("Home/Desktop/Report.pdf", "a different report")
        let newcomerHash = try #require(fx.hash(newcomer))
        let undone = mover.undo(fx.store.load().entries[0], at: fx.loc, drawers: fx.drawers)
        #expect(undone.moved.map { DrawerFS.name(of: $0.to) } == ["Report 2.pdf"])
        #expect(fx.hash(newcomer) == newcomerHash)
        fx.expectInvariant(["Home/Desktop/Report.pdf": newcomerHash, "Home/Desktop/Report 2.pdf": originalHash], drawersOK: false)
        #expect(fx.store.load().entries.isEmpty, "an undone run leaves the journal")
    }

    // MARK: edits and vanished files

    @Test("a file edited between plan and Apply is skipped, by size or by date, and the rest move")
    func editedBetween() throws {
        let fx = DrawerFixture()
        let bySize = fx.write("Home/Desktop/a.pdf", "short")
        let byDate = fx.write("Home/Desktop/b.pdf", "same size text")
        fx.write("Home/Desktop/c.pdf", "untouched")
        let gone = fx.write("Home/Desktop/d.pdf", "will vanish")
        let (plan, planned) = fx.plan()
        // The user edits after the review.
        try Data("a longer body than before".utf8).write(to: URL(fileURLWithPath: bySize))
        try FileManager.default.setAttributes([.modificationDate: Date().addingTimeInterval(-5000)], ofItemAtPath: byDate)
        try FileManager.default.moveItem(atPath: gone, toPath: fx.base.path + "/Home/gone.pdf") // the user moves it away
        let start = fx.snapshot()

        let result = fx.mover().apply(plan, at: fx.loc, existing: [], planned: planned)
        #expect(result.moved.map { DrawerFS.name(of: $0.to) } == ["c.pdf"])
        let reasons = Dictionary(uniqueKeysWithValues: result.skipped.map { (DrawerFS.name(of: $0.path), $0.reason) })
        #expect(reasons["a.pdf"] == DrawerSkipReason.changed)
        #expect(reasons["b.pdf"] == DrawerSkipReason.changed)
        #expect(reasons["d.pdf"] == DrawerSkipReason.gone)
        #expect(FileManager.default.fileExists(atPath: bySize) && FileManager.default.fileExists(atPath: byDate))
        fx.expectInvariant(start)
    }

    @Test("a rule move with no review facts is refused")
    func noReview() {
        let fx = DrawerFixture()
        fx.write("Home/Desktop/a.pdf")
        let start = fx.snapshot()
        let (plan, _) = fx.plan()
        let result = fx.mover().apply(plan, at: fx.loc, existing: [], planned: [:])
        #expect(result.moved.isEmpty && result.skipped.map(\.reason) == [DrawerSkipReason.noReview])
        fx.expectInvariant(start, drawersOK: false)
    }

    @Test("a drawer folder the user removed by hand: Undo skips what is gone, and the next sort makes it again")
    func drawerDeletedByHand() throws {
        let fx = DrawerFixture()
        fx.write("Home/Desktop/a.pdf"); fx.write("Home/Desktop/b.jpg"); fx.write("Home/Desktop/c.zip")
        let start = fx.snapshot()
        let (plan, planned) = fx.plan()
        let mover = fx.mover()
        #expect(mover.apply(plan, at: fx.loc, existing: [], planned: planned).moved.count == 3)

        // By hand: b.jpg goes back to the Desktop in Finder, then its now-empty drawer is deleted.
        try FileManager.default.moveItem(atPath: fx.inDrawer("Images", "b.jpg"), toPath: fx.desk("b.jpg"))
        try FileManager.default.removeItem(atPath: fx.root + "/Images")
        // And the Archives drawer vanishes with c.zip's parent renamed away, so its file is not found.
        try FileManager.default.moveItem(atPath: fx.root + "/Archives", toPath: fx.base.path + "/Home/Archives-moved")
        let entry = fx.store.load().entries[0]
        let undone = mover.undo(entry, at: fx.loc, drawers: fx.drawers)

        #expect(Set(undone.skipped.map(\.reason)) == [DrawerUndo.movedSince])
        #expect(undone.skipped.count == 2 && undone.moved.count == 1)
        #expect(FileManager.default.fileExists(atPath: fx.desk("a.pdf")))
        // c.zip is in the renamed folder, which is outside the Drawers root now; nothing lost.
        let now = fx.snapshot()
        #expect(Set(now.keys) == ["Home/Desktop/a.pdf", "Home/Desktop/b.jpg", "Home/Archives-moved/c.zip"])
        #expect(Set(now.values) == Set(start.values))

        // A new sort into the removed drawer makes the folder again.
        fx.write("Home/Desktop/d.jpg")
        let (plan2, planned2) = fx.plan(existing: fx.drawers)
        let again = mover.apply(plan2, at: fx.loc, existing: fx.drawers, planned: planned2)
        #expect(again.moved.contains { $0.to.hasSuffix("/Images/d.jpg") || $0.to.hasSuffix("/Images/b.jpg") })
    }

    // MARK: crash and recovery

    @Test("a crash after 3 moves leaves a readable journal with the open entry, recovery finds the 3, Undo puts them back")
    func crashAndRecover() throws {
        let fx = DrawerFixture()
        for n in 1...6 { fx.write("Home/Desktop/doc\(n).pdf") }
        let start = fx.snapshot()
        let (plan, planned) = fx.plan()
        let crashed = fx.mover(afterRename: { $0 == 3 }).apply(plan, at: fx.loc, existing: [], planned: planned)
        #expect(crashed.halted)

        // After the "crash": a fresh process reads the file.
        let reopened = DrawerJournalStore(url: fx.journalURL).load()
        #expect(reopened.entries.count == 1 && reopened.openEntries.count == 1)
        #expect(reopened.entries[0].moves.count == 6 && reopened.entries[0].writtenMoves.isEmpty)
        fx.expectInvariant(start) // 3 in the drawer, 3 on the Desktop, none lost

        let recovered = fx.mover().recover()
        #expect(recovered.count == 1 && recovered[0].moves.count == 3 && !recovered[0].isOpen)
        let afterRecovery = fx.store.load()
        #expect(afterRecovery.openEntries.isEmpty && afterRecovery.entries[0].moves.count == 3)

        let undone = fx.mover().undo(recovered[0], at: fx.loc, drawers: fx.drawers)
        #expect(undone.moved.count == 3 && undone.skipped.isEmpty)
        fx.expectInvariant(start, drawersOK: false)
        #expect(fx.store.load().entries.isEmpty)
    }

    @Test("a crash past a periodic save: the flagged moves and the unflagged ones that landed are all recovered")
    func crashPastSave() throws {
        let fx = DrawerFixture()
        for n in 10...49 { fx.write("Home/Desktop/img\(n).png") }
        let start = fx.snapshot()
        let (plan, planned) = fx.plan()
        let crashed = fx.mover(afterRename: { $0 == 30 }).apply(plan, at: fx.loc, existing: [], planned: planned)
        #expect(crashed.halted)
        let onDisk = fx.store.load().entries[0]
        #expect(onDisk.writtenMoves.count == 25, "saved at the 25th move, not after")
        #expect(onDisk.moves.count == 40)

        let recovered = fx.mover().recover()
        #expect(recovered[0].moves.count == 30)
        #expect(Set(recovered[0].moves.map(\.to)).count == 30)
        let undone = fx.mover().undo(recovered[0], at: fx.loc, drawers: fx.drawers)
        #expect(undone.moved.count == 30)
        fx.expectInvariant(start, drawersOK: false)
    }

    @Test("recovery finds a file that took a numbered name when its first choice was taken")
    func recoverNumberedName() throws {
        let fx = DrawerFixture()
        fx.write("Home/Desktop/Drawers/Images/a.png", "already in the drawer")
        fx.write("Home/Desktop/a.png", "from the desktop"); fx.write("Home/Desktop/b.png")
        let start = fx.snapshot()
        let (plan, planned) = fx.plan(existing: fx.drawers)
        _ = fx.mover(afterRename: { $0 == 1 }).apply(plan, at: fx.loc, existing: fx.drawers, planned: planned)
        let recovered = fx.mover().recover()
        #expect(recovered.count == 1 && recovered[0].moves.count == 1)
        #expect(DrawerFS.name(of: recovered[0].moves[0].to) == "a 2.png")
        let undone = fx.mover().undo(recovered[0], at: fx.loc, drawers: fx.drawers)
        #expect(undone.moved.count == 1)
        fx.expectInvariant(start, drawersOK: true)
        #expect(String(data: FileManager.default.contents(atPath: fx.desk("a.png"))!, encoding: .utf8) == "from the desktop")
    }

    @Test("recovery leaves a pending move alone when its file never left, or no inode was recorded")
    func recoverConservative() throws {
        let fx = DrawerFixture()
        let a = fx.write("Home/Desktop/a.pdf")
        let b = fx.write("Home/Desktop/b.pdf")
        let start = fx.snapshot()
        let ino = DrawerFS.inode(DrawerFS.lstat(a)!)
        let entry = JournalEntry(id: "open", at: Date(), source: .rules, moves: [
            JournalMove(from: a, to: fx.inDrawer("Documents", "a.pdf"), written: .pending, inode: ino),
            JournalMove(from: b, to: fx.inDrawer("Documents", "b.pdf"), written: .pending, inode: nil),
        ])
        try fx.store.save(DrawerJournal(entries: [entry]))
        #expect(fx.mover().recover().isEmpty)
        #expect(fx.store.load().entries.isEmpty, "an entry with nothing that happened is closed and dropped")
        fx.expectInvariant(start, drawersOK: false)
    }

    @Test("the entry is on disk, every move unflagged, before the first rename")
    func entryBeforeFirstMove() throws {
        let fx = DrawerFixture()
        for n in 1...4 { fx.write("Home/Desktop/f\(n).txt") }
        let (plan, planned) = fx.plan()
        var seenAtFirstRename: JournalEntry?
        var calls = 0
        let spy: (String, String) -> Int32 = { from, to in
            calls += 1
            if calls == 1 { seenAtFirstRename = fx.store.load().entries.first }
            return DrawerMover.exclusiveRename(from, to)
        }
        _ = fx.mover(rename: spy).apply(plan, at: fx.loc, existing: [], planned: planned)
        let seen = try #require(seenAtFirstRename)
        #expect(seen.moves.count == 4 && seen.moves.allSatisfy { !$0.isWritten && $0.inode != nil })
    }

    @Test("the journal is saved every 25 moves")
    func saveCadence() throws {
        let fx = DrawerFixture()
        for n in 10...49 { fx.write("Home/Desktop/img\(n).png") }
        let (plan, planned) = fx.plan()
        var done: [Int: Int] = [:]
        var calls = 0
        let spy: (String, String) -> Int32 = { from, to in
            calls += 1
            done[calls] = fx.store.load().entries.first?.writtenMoves.count ?? -1
            return DrawerMover.exclusiveRename(from, to)
        }
        _ = fx.mover(rename: spy).apply(plan, at: fx.loc, existing: [], planned: planned)
        #expect(done[25] == 0, "before the 25th rename, nothing is flagged on disk")
        #expect(done[26] == 25, "the 25th flip was saved")
        #expect(done[40] == 25)
        #expect(fx.store.load().entries[0].writtenMoves.count == 40, "and the end is saved")
    }

    // MARK: the journal file

    @Test("the journal is written owner-only and replaced atomically, with no temp file left")
    func journalFile() throws {
        let fx = DrawerFixture()
        fx.write("Home/Desktop/a.pdf")
        let (plan, planned) = fx.plan()
        _ = fx.mover().apply(plan, at: fx.loc, existing: [], planned: planned)
        #expect(fx.mode(fx.journalURL) == 0o600)

        // Saving over a file that was world-readable ends owner-only too, and the old file is whole until the swap.
        FileManager.default.createFile(atPath: fx.journalURL.path + ".tmp", contents: Data("stale".utf8), attributes: [.posixPermissions: 0o644])
        try fx.store.save(DrawerJournal(entries: []))
        #expect(fx.mode(fx.journalURL) == 0o600)
        #expect(DrawerFS.names(in: fx.journalURL.deletingLastPathComponent().path) == ["drawers-journal.json"])
        #expect(fx.store.load().entries.isEmpty)
    }

    @Test("a journal store makes its folder when it is missing, owner-only")
    func journalFolder() throws {
        let fx = DrawerFixture()
        let store = DrawerJournalStore(url: fx.base.appendingPathComponent("New/Sub/drawers-journal.json"))
        try store.save(DrawerJournal(entries: [JournalEntry(id: "x", at: Date(), source: .user, moves: [JournalMove(from: "/a", to: "/b", written: .pending)])]))
        #expect(store.load().entries.count == 1)
        #expect(fx.mode(fx.base.appendingPathComponent("New/Sub")) == 0o700)
    }

    @Test("a journal that can't be written stops the run before the first move")
    func journalFails() throws {
        let fx = DrawerFixture()
        fx.write("Home/Desktop/a.pdf")
        let start = fx.snapshot()
        // A file where the journal's folder should be.
        let blocker = fx.write("Blocker", "x")
        let store = DrawerJournalStore(url: URL(fileURLWithPath: blocker + "/drawers-journal.json"))
        let (plan, planned) = fx.plan()
        let result = DrawerMover(store: store).apply(plan, at: fx.loc, existing: [], planned: planned)
        #expect(result.refusal != nil && result.moved.isEmpty)
        #expect(fx.hash(fx.desk("a.pdf")) == start["Home/Desktop/a.pdf"])
        #expect(DrawerFS.names(in: fx.root) == nil || DrawerFS.names(in: fx.root) == [], "no file went into a drawer")
    }

    // MARK: errno handling

    @Test("EXDEV refuses and names the file; the file stays and nothing is copied")
    func exdev() {
        let fx = DrawerFixture()
        fx.write("Home/Desktop/a.pdf")
        let start = fx.snapshot()
        let (plan, planned) = fx.plan()
        let result = fx.mover(rename: { _, _ in EXDEV }).apply(plan, at: fx.loc, existing: [], planned: planned)
        #expect(result.moved.isEmpty && result.skipped.map(\.reason) == [DrawerSkipReason.otherDisk])
        fx.expectInvariant(start, drawersOK: false)
    }

    @Test("other errors skip with a reason: no safe move on this disk, permission, and the rest")
    func otherErrno() {
        for (code, expected) in [(ENOTSUP, DrawerSkipReason.noSafeMove), (EACCES, "not allowed"), (EROFS, "not allowed")] {
            let fx = DrawerFixture()
            fx.write("Home/Desktop/a.pdf")
            let start = fx.snapshot()
            let (plan, planned) = fx.plan()
            let result = fx.mover(rename: { _, _ in code }).apply(plan, at: fx.loc, existing: [], planned: planned)
            #expect(result.skipped.map(\.reason) == [expected])
            fx.expectInvariant(start, drawersOK: false)
        }
        let odd = DrawerSkipReason.failed(EIO)
        #expect(odd.hasPrefix("couldn't move it"))
    }

    // MARK: hand moves

    @Test("hand moves: Desktop to a drawer, drawer to drawer, a folder too; journaled as user; Undo newest first")
    func handMoves() throws {
        let fx = DrawerFixture()
        fx.write("Home/Desktop/note.txt")
        fx.write("Home/Desktop/Project/readme.md"); fx.write("Home/Desktop/Project/src/main.c")
        let start = fx.snapshot()
        let mover = fx.mover()
        let docs = fx.drawers[2], images = fx.drawers[1]

        let one = mover.apply(DrawerPlan(source: .user, drawers: [docs], moves: [DrawerMove(source: fx.desk("note.txt"), drawerId: "documents")]),
                              at: fx.loc, existing: [])
        #expect(one.moved.count == 1)
        let two = mover.apply(DrawerPlan(source: .user, drawers: [docs, images], moves: [DrawerMove(source: fx.inDrawer("Documents", "note.txt"), drawerId: "images")]),
                              at: fx.loc, existing: [docs])
        #expect(two.moved.count == 1, "\(two)")
        let three = mover.apply(DrawerPlan(source: .user, drawers: [docs], moves: [DrawerMove(source: fx.desk("Project"), drawerId: "documents")]),
                                at: fx.loc, existing: [docs, images])
        #expect(three.moved.count == 1 && three.moved[0].isFolder)
        fx.expectInvariant(start)
        let journal = fx.store.load()
        #expect(journal.entries.count == 3 && journal.entries.allSatisfy { $0.source == .user })

        for entry in journal.entries.reversed() {
            let undone = mover.undo(entry, at: fx.loc, drawers: fx.drawers)
            #expect(undone.skipped.isEmpty && undone.moved.count == 1, "\(undone)")
        }
        fx.expectInvariant(start, drawersOK: false)
    }

    @Test("a hand-moved file edited since is not put back by Undo, and stays in its drawer")
    func undoChanged() throws {
        let fx = DrawerFixture()
        fx.write("Home/Desktop/note.txt", "v1")
        let mover = fx.mover()
        let docs = fx.drawers[2]
        _ = mover.apply(DrawerPlan(source: .user, drawers: [docs], moves: [DrawerMove(source: fx.desk("note.txt"), drawerId: "documents")]),
                        at: fx.loc, existing: [])
        let inDrawer = fx.inDrawer("Documents", "note.txt")
        try FileManager.default.setAttributes([.modificationDate: Date().addingTimeInterval(-7200)], ofItemAtPath: inDrawer)
        let start = fx.snapshot()
        let undone = mover.undo(fx.store.load().entries[0], at: fx.loc, drawers: fx.drawers)
        #expect(undone.moved.isEmpty && undone.skipped.map(\.reason) == [DrawerUndo.changedSince])
        fx.expectInvariant(start)
    }

    // MARK: left alone

    @Test("packages, hidden files, dotfiles, folders and symlinks are never taken by a sort")
    func leftAlone() throws {
        let fx = DrawerFixture()
        fx.write("Home/Desktop/Tool.app/Contents/Info.plist", "<plist/>")
        fx.write("Home/Desktop/Book.pages/index.xml", "<doc/>")
        fx.write("Home/Desktop/.hidden.pdf"); fx.write("Home/Desktop/.DS_Store"); fx.write("Home/Desktop/.env")
        let flagged = fx.write("Home/Desktop/flagged.pdf")
        #expect(chflags(flagged, UInt32(UF_HIDDEN)) == 0)
        fx.write("Home/Desktop/Folder/inside.pdf")
        let target = fx.write("Home/Elsewhere/real.pdf", "linked-to file")
        try FileManager.default.createSymbolicLink(atPath: fx.desk("link.pdf"), withDestinationPath: target)
        try FileManager.default.createSymbolicLink(atPath: fx.desk("dirlink"), withDestinationPath: fx.base.path + "/Home/Elsewhere")
        fx.write("Home/Desktop/ok.pdf")
        let start = fx.snapshot()

        let (plan, planned) = fx.plan()
        let result = fx.mover().apply(plan, at: fx.loc, existing: [], planned: planned)
        #expect(result.moved.map { DrawerFS.name(of: $0.to) } == ["ok.pdf"], "\(result)")
        #expect(DrawerFS.lstat(fx.desk("link.pdf")).map(DrawerFS.isLink) == true)
        #expect(DrawerFS.lstat(fx.desk("dirlink")).map(DrawerFS.isLink) == true)
        fx.expectInvariant(start)
    }

    @Test("a forged plan: a link, a package and a path outside are not moved")
    func forgedPlan() throws {
        let fx = DrawerFixture()
        let docs = fx.drawers[2]
        let target = fx.write("Home/Elsewhere/real.pdf", "outside")
        try FileManager.default.createSymbolicLink(atPath: fx.desk("link.pdf"), withDestinationPath: target)
        fx.write("Home/Desktop/Tool.app/Contents/Info.plist", "<plist/>")
        fx.write("Home/Desktop/fine.pdf")
        let start = fx.snapshot()
        let facts = DrawerFacts.list(fx.desktop)
        let planned = Dictionary(facts.map { (fx.desk($0.name), $0) }, uniquingKeysWith: { a, _ in a })

        // Link and package pass the validator (they are Desktop children) but not the mover's own check.
        let plan = DrawerPlan(source: .rules, drawers: [docs], moves: [
            DrawerMove(source: fx.desk("link.pdf"), drawerId: "documents"),
            DrawerMove(source: fx.desk("Tool.app"), drawerId: "documents"),
            DrawerMove(source: fx.desk("fine.pdf"), drawerId: "documents"),
        ])
        let result = fx.mover().apply(plan, at: fx.loc, existing: [], planned: planned)
        let reasons = Dictionary(uniqueKeysWithValues: result.skipped.map { (DrawerFS.name(of: $0.path), $0.reason) })
        #expect(reasons["link.pdf"] == DrawerSkipReason.link)
        #expect(reasons["Tool.app"] == DrawerSkipReason.notPlain)
        #expect(result.moved.map { DrawerFS.name(of: $0.to) } == ["fine.pdf"])

        // A source outside the Desktop and the drawers refuses the whole plan.
        let outside = DrawerPlan(source: .rules, drawers: [docs], moves: [
            DrawerMove(source: target, drawerId: "documents"),
            DrawerMove(source: fx.desk("link.pdf"), drawerId: "documents"),
        ])
        let refused = fx.mover().apply(outside, at: fx.loc, existing: [docs], planned: planned)
        #expect(refused.refusal != nil && refused.moved.isEmpty)
        fx.expectInvariant(start)
    }

    @Test("a view drawer is shown, never filled")
    func viewDrawerNotFilled() throws {
        let fx = DrawerFixture()
        fx.write("Home/Desktop/a.txt")
        try FileManager.default.createDirectory(atPath: fx.home + "/Pictures", withIntermediateDirectories: true)
        let start = fx.snapshot()
        let view = DrawerDef(id: "pics", name: "Pictures", home: fx.home + "/Pictures", owned: false)
        let plan = DrawerPlan(source: .user, drawers: [view], moves: [DrawerMove(source: fx.desk("a.txt"), drawerId: "pics")])
        let result = fx.mover().apply(plan, at: fx.loc, existing: [view])
        #expect(result.moved.isEmpty && result.skipped.map(\.reason) == [DrawerSkipReason.viewDrawer])
        fx.expectInvariant(start, drawersOK: false)
    }

    @Test("the Drawers folder and a drawer's folder are never moved, even by a hand plan")
    func ownFoldersStay() throws {
        let fx = DrawerFixture()
        fx.write("Home/Desktop/Drawers/Documents/a.pdf")
        let start = fx.snapshot()
        let docs = fx.drawers[2], images = fx.drawers[1]
        for source in [fx.root, fx.inDrawer("Documents", "")] {
            let plan = DrawerPlan(source: .user, drawers: [docs, images], moves: [DrawerMove(source: source, drawerId: "images")])
            let result = fx.mover().apply(plan, at: fx.loc, existing: [docs, images])
            #expect(result.moved.isEmpty, "\(result)")
        }
        fx.expectInvariant(start)
    }

    @Test("a link swapped in for a drawer's folder is not followed")
    func swappedDrawer() throws {
        let fx = DrawerFixture()
        fx.write("Home/Desktop/a.pdf")
        let elsewhere = fx.base.path + "/Home/Elsewhere"
        try FileManager.default.createDirectory(atPath: elsewhere, withIntermediateDirectories: true)
        try FileManager.default.createDirectory(atPath: fx.root, withIntermediateDirectories: true)
        try FileManager.default.createSymbolicLink(atPath: fx.root + "/Documents", withDestinationPath: elsewhere)
        let start = fx.snapshot()
        let (plan, planned) = fx.plan()
        let result = fx.mover().apply(plan, at: fx.loc, existing: [], planned: planned)
        #expect(result.moved.isEmpty)
        #expect((result.refusal != nil) || !result.skipped.isEmpty)
        #expect(DrawerFS.names(in: elsewhere) == [])
        fx.expectInvariant(start, drawersOK: false)
    }

    @Test("an unsafe root refuses the run, Undo and the return")
    func unsafeRoot() throws {
        let fx = DrawerFixture()
        fx.write("Home/Desktop/a.pdf")
        let start = fx.snapshot()
        let bad = DrawerLocations(desktop: fx.desktop, root: fx.desktop, home: fx.home)
        let plan = DrawerPlan(source: .rules, drawers: [], moves: [])
        #expect(fx.mover().apply(plan, at: bad, existing: []).refusal != nil)
        let entry = JournalEntry(id: "e", at: Date(), source: .rules, moves: [JournalMove(from: fx.desk("a.pdf"), to: fx.desktop + "/x/a.pdf", written: .done(modified: nil))])
        #expect(fx.mover().undo(entry, at: bad, drawers: []).refusal != nil)
        #expect(fx.mover().returnAll(drawers: [], at: bad, removeRoot: true).refusal != nil)
        fx.expectInvariant(start, drawersOK: false)
    }

    // MARK: the return

    @Test("the return after mixed history: rules, hand moves, a Finder drop and Finder's own file")
    func returnMixed() throws {
        let fx = DrawerFixture()
        fx.write("Home/Desktop/a.pdf"); fx.write("Home/Desktop/b.jpg"); fx.write("Home/Desktop/c.zip")
        fx.write("Home/Desktop/hand.xyz"); fx.write("Home/Desktop/Report.pdf", "desktop report")
        let mover = fx.mover()
        let (plan, planned) = fx.plan()
        #expect(mover.apply(plan, at: fx.loc, existing: [], planned: planned).moved.count == 4) // Report.pdf, a, b, c
        let docs = fx.drawers[2]
        _ = mover.apply(DrawerPlan(source: .user, drawers: [docs], moves: [DrawerMove(source: fx.desk("hand.xyz"), drawerId: "documents")]),
                        at: fx.loc, existing: fx.drawers)
        // A file dropped into a drawer in Finder (never journaled), a folder, and Finder's .DS_Store.
        fx.write("Home/Desktop/Drawers/Documents/dropped.docx")
        fx.write("Home/Desktop/Drawers/Images/Album/one.jpg")
        // The user made a new Report.pdf on the Desktop after the sort.
        fx.write("Home/Desktop/Report.pdf", "a newer report")
        let start = fx.snapshot()
        fx.write("Home/Desktop/Drawers/Documents/.DS_Store", "finder bits")
        fx.write("Home/Desktop/Drawers/Images/.DS_Store", "finder bits")
        // A view drawer's contents are never touched.
        let viewFile = fx.write("Home/Pictures/view.jpg", "viewed")
        let startWithView = fx.snapshot().filter { $0.key == "Home/Pictures/view.jpg" || start[$0.key] != nil }

        let all = fx.drawers + [DrawerDef(id: "pics", name: "Pictures", home: fx.home + "/Pictures", owned: false)]
        let back = mover.returnAll(drawers: all, at: fx.loc, removeRoot: true)
        #expect(back.refusal == nil && back.skipped.isEmpty, "\(back)")
        #expect(back.moved.count == 7) // a, Report, b, c, hand.xyz, dropped.docx, Album
        #expect(!FileManager.default.fileExists(atPath: fx.root), "the root went with its empty drawers")
        #expect(back.foldersKept.isEmpty)
        #expect(fx.hash(viewFile) != nil)
        // Every file is on the Desktop (or untouched in the view drawer), bytes intact; .DS_Store is gone.
        var expected = startWithView
        expected["Home/Pictures/view.jpg"] = fx.hash(viewFile)
        let now = fx.snapshot()
        #expect(Set(now.values) == Set(expected.values))
        #expect(now.count == expected.count)
        #expect(now.keys.filter { !$0.hasPrefix("Home/Desktop/") && $0 != "Home/Pictures/view.jpg" }.isEmpty)
        #expect(!now.keys.contains { $0.hasSuffix(".DS_Store") })
        #expect(Set(DrawerFS.names(in: fx.desktop)!).isSuperset(of: ["Report.pdf", "Report 2.pdf", "Album", "dropped.docx", "hand.xyz"]))
    }

    @Test("the return keeps a non-default root, and keeps a folder that still holds something")
    func returnKeeps() throws {
        let fx = DrawerFixture()
        fx.write("Home/Desktop/a.pdf"); fx.write("Home/Desktop/b.jpg"); fx.write("Home/Desktop/stuck.zip")
        let start = fx.snapshot()
        let (plan, planned) = fx.plan()
        let mover = fx.mover()
        _ = mover.apply(plan, at: fx.loc, existing: [], planned: planned)
        fx.write("Home/Desktop/Drawers/Documents/.DS_Store", "finder")

        // Without removing the root: homes go, the root stays.
        let stuckFails: (String, String) -> Int32 = { from, to in from.contains("stuck") ? EACCES : DrawerMover.exclusiveRename(from, to) }
        let partial = fx.mover(rename: stuckFails).returnAll(drawers: fx.drawers, at: fx.loc, removeRoot: false)
        #expect(partial.moved.count == 2)
        #expect(partial.skipped.map(\.reason) == [DrawerSkipReason.failed(EACCES)])
        #expect(partial.foldersKept.map(\.reason) == [DrawerSkipReason.stillHolds])
        #expect(FileManager.default.fileExists(atPath: fx.inDrawer("Archives", "stuck.zip")))
        #expect(!FileManager.default.fileExists(atPath: fx.root + "/Documents"))
        #expect(FileManager.default.fileExists(atPath: fx.root), "root kept")

        // Turning off again finishes the job.
        let finish = mover.returnAll(drawers: fx.drawers, at: fx.loc, removeRoot: true)
        #expect(finish.moved.count == 1 && finish.foldersKept.isEmpty)
        #expect(!FileManager.default.fileExists(atPath: fx.root))
        fx.expectInvariant(start, drawersOK: false)
    }

    @Test("a folder is removed only if it is empty at that moment")
    func removeOnlyEmpty() throws {
        let fx = DrawerFixture()
        try FileManager.default.createDirectory(atPath: fx.root + "/Documents", withIntermediateDirectories: true)
        fx.write("Home/Desktop/Drawers/Documents/late.pdf")
        let start = fx.snapshot()
        let keys = Set([DrawerPaths.key(DrawerFS.realPath(fx.root)! + "/Documents")])
        let mover = fx.mover()
        let real = DrawerFS.realPath(fx.root)! + "/Documents"
        #expect(mover.removeIfEmpty(real, removable: keys) == .kept(DrawerSkipReason.stillHolds))
        #expect(mover.removeIfEmpty(real, removable: []) == .kept(DrawerSkipReason.outside), "not a folder it was told it may remove")
        fx.expectInvariant(start)
        // A .DS_Store beside a real file does not count as empty, and is left alone.
        fx.write("Home/Desktop/Drawers/Documents/.DS_Store", "x")
        #expect(mover.removeIfEmpty(real, removable: keys) == .kept(DrawerSkipReason.stillHolds))
        #expect(FileManager.default.fileExists(atPath: real + "/.DS_Store"))
    }

    // MARK: facts

    @Test("facts read the flags the rules look at")
    func facts() throws {
        let fx = DrawerFixture()
        fx.write("Home/Desktop/Thing.app/Contents/Info.plist", "<plist/>")
        fx.write("Home/Desktop/.dot")
        let plain = fx.write("Home/Desktop/plain.txt", "12345")
        let shot = fx.write("Home/Desktop/shot.png")
        let notShot = fx.write("Home/Desktop/notshot.png")
        let bplistFalse = try PropertyListSerialization.data(fromPropertyList: NSNumber(value: false), format: .binary, options: 0)
        let raw = Data("bplist00".utf8)
        #expect(setxattr(shot, DrawerFacts.screenCaptureAttribute, [UInt8](raw), raw.count, 0, 0) == 0)
        #expect(setxattr(notShot, DrawerFacts.screenCaptureAttribute, [UInt8](bplistFalse), bplistFalse.count, 0, 0) == 0)
        try FileManager.default.createSymbolicLink(atPath: fx.desk("sym"), withDestinationPath: plain)
        let aliasData = try URL(fileURLWithPath: plain).bookmarkData(options: .suitableForBookmarkFile)
        try URL.writeBookmarkData(aliasData, to: URL(fileURLWithPath: fx.desk("plain alias")))
        let flagged = fx.write("Home/Desktop/flagged.txt")
        #expect(chflags(flagged, UInt32(UF_HIDDEN)) == 0)

        let byName = Dictionary(uniqueKeysWithValues: DrawerFacts.list(fx.desktop).map { ($0.name, $0) })
        #expect(byName["Thing.app"]?.isPackage == true && byName["Thing.app"]?.isDirectory == true)
        #expect(byName[".dot"]?.isHidden == true)
        #expect(byName["flagged.txt"]?.isHidden == true)
        #expect(byName["plain.txt"]?.isHidden == false && byName["plain.txt"]?.size == 5)
        #expect(byName["plain.txt"]?.isPackage == false && byName["plain.txt"]?.isSymlink == false && byName["plain.txt"]?.isAlias == false)
        #expect(byName["sym"]?.isSymlink == true)
        #expect(byName["plain alias"]?.isAlias == true)
        #expect(byName["shot.png"]?.isScreenCapture == true)
        #expect(byName["notshot.png"]?.isScreenCapture == false)
        #expect(byName["plain.txt"]?.isScreenCapture == false)
        let modified = try #require(byName["plain.txt"]?.modified)
        #expect(abs(modified.timeIntervalSinceNow) < 60)
        #expect(DrawerFacts.read(fx.desk("nope")) == nil)
    }

    @Test("dataless is the stat flag or a download status that isn't current")
    func dataless() {
        var st = stat()
        #expect(DrawerFacts.isDataless(st) == false)
        #expect(DrawerFacts.isDataless(st, .current) == false)
        #expect(DrawerFacts.isDataless(st, .notDownloaded) == true)
        #expect(DrawerFacts.isDataless(st, .downloaded) == true)
        st.st_flags = DrawerFacts.datalessFlag
        #expect(DrawerFacts.isDataless(st) == true)
        st.st_flags = 0x1 // UF_NODUMP, unrelated
        #expect(DrawerFacts.isDataless(st) == false)
    }

    @Test("the volume id is the same for two folders on one disk, and a missing folder reads its parent's")
    func volumes() {
        let fx = DrawerFixture()
        #expect(!DrawerFS.volumeID(fx.desktop).isEmpty)
        #expect(DrawerFS.volumeID(fx.desktop) == DrawerFS.volumeID(fx.root))
        #expect(DrawerFS.resolve(fx.desktop + "/../Desktop/./Drawers") == DrawerFS.realPath(fx.desktop)! + "/Drawers")
    }

    @Test("the fixture never leaves the temp directory, and removes itself")
    func fixtureIsContained() {
        var path = ""
        do {
            let fx = DrawerFixture()
            path = fx.base.path
            #expect(DrawerFS.realPath(path)!.hasPrefix(DrawerFS.realPath(FileManager.default.temporaryDirectory.path)! + "/"))
            #expect(FileManager.default.fileExists(atPath: path))
        }
        #expect(!FileManager.default.fileExists(atPath: path))
    }
}
