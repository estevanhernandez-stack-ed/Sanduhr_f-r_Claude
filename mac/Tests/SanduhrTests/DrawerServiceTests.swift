import Foundation
import Testing
@testable import Sanduhr

/// The drawer service, its settings, the watcher and Reset (docs/mac-drawers-setup-spec.md,
/// sections 1, 3, 5, 7 and 8).
///
/// Everything works in a temp "home" from `DrawerFixture` (it refuses to start outside the temp
/// directory). The Desktop, root, home and journal are injected, and the preferences go to a
/// throwaway defaults suite removed after each test. Nothing here names the real Desktop, the real
/// journal or the `.desk` suite.

/// A defaults suite under a UUID name, removed on deinit.
final class DrawerScratchDefaults {
    let name = "sanduhr.tests.drawers.\(UUID().uuidString)"
    let defaults: UserDefaults
    init() { defaults = UserDefaults(suiteName: name)! }
    deinit { defaults.removePersistentDomain(forName: name) }
}

/// Counts how often the Desktop was listed or the Desk was turned on.
final class ServiceProbe: @unchecked Sendable {
    private let lock = NSLock()
    private var _desktopReads = 0, _deskOn = 0
    var desktopReads: Int { lock.lock(); defer { lock.unlock() }; return _desktopReads }
    var deskOn: Int { lock.lock(); defer { lock.unlock() }; return _deskOn }
    func read() { lock.lock(); _desktopReads += 1; lock.unlock() }
    func on() { lock.lock(); _deskOn += 1; lock.unlock() }
}

@MainActor
private struct Rig {
    let fx: DrawerFixture
    let scratch = DrawerScratchDefaults()
    let probe = ServiceProbe()
    let service: DrawerService

    init(enabled: Bool = false, mover: DrawerMover? = nil, fixture: DrawerFixture = DrawerFixture()) {
        fx = fixture
        let fx = self.fx, scratch = self.scratch, probe = self.probe
        if enabled { var s = DrawerSettings(); s.enabled = true; DrawerPrefs.save(s, to: scratch.defaults) }
        service = DrawerService(defaults: scratch.defaults, desktop: fx.desktop, home: fx.home, store: fx.store,
                                turnDeskOn: { probe.on() },
                                readFacts: { probe.read(); return DrawerFacts.list($0) }, mover: mover)
    }

    /// A second service over the same defaults and journal, as a relaunch would build.
    func relaunch() -> DrawerService {
        let fx = self.fx, probe = self.probe
        return DrawerService(defaults: scratch.defaults, desktop: fx.desktop, home: fx.home, store: fx.store,
                             turnDeskOn: { probe.on() }, readFacts: { probe.read(); return DrawerFacts.list($0) })
    }

    var realRoot: String { fx.realBase + "/Home/Desktop/Drawers" }
}

/// Polls until `check` holds, up to `timeout`; short naps, no fixed long sleep.
@MainActor
private func waitUntil(_ timeout: TimeInterval = 10, _ check: () -> Bool) async -> Bool {
    let end = Date().addingTimeInterval(timeout)
    while Date() < end {
        if check() { return true }
        try? await Task.sleep(nanoseconds: 25_000_000)
    }
    return check()
}

@Suite("Drawer settings")
struct DrawerSettingsTests {
    @Test("a fresh suite loads defaults")
    func defaultsLoad() {
        let s = DrawerScratchDefaults()
        let d = DrawerPrefs.load(from: s.defaults)
        #expect(d == DrawerSettings())
        #expect(!d.enabled && d.root == nil && d.drawers.isEmpty && d.rules == DrawerRules.defaults)
        #expect(d.opacity == 55 && d.look == .solid && d.tidyCorner == .topRight && d.tidyFlow == .column)
    }

    @Test("every field round-trips under drawers.* keys")
    func roundTrip() {
        let s = DrawerScratchDefaults()
        var v = DrawerSettings()
        v.enabled = true
        v.root = "/x/Drawers"
        v.drawers = [DrawerDef(id: "docs", name: "Docs", home: "/x/Drawers/Docs", owned: true),
                     DrawerDef(id: "pics", name: "Pics", home: "/y/Pics", owned: false)]
        v.rules = [DrawerRule(kind: .nameContains, drawer: "docs", on: false, text: "invoice", days: 30),
                   DrawerRule(kind: .olderThan, drawer: "older-files", days: 90)]
        v.look = .glass
        v.opacity = 70
        v.looks = ["docs": .bare]
        v.places = ["docs": DrawerPlace(screen: "main", x: 12.5, y: 40)]
        v.folded = ["docs"]
        v.hidden = ["pics"]
        v.tidyCorner = .bottomLeft
        v.tidyFlow = .row
        DrawerPrefs.save(v, to: s.defaults)
        #expect(DrawerPrefs.load(from: s.defaults) == v)
        let keys = s.defaults.dictionaryRepresentation().keys.filter { $0.hasPrefix("drawers.") }
        #expect(keys.count == 12)
        #expect(DrawerPrefs.clear(s.defaults))
        #expect(DrawerPrefs.load(from: s.defaults) == DrawerSettings())
    }

    @Test("bad stored values fall back to defaults")
    func badValues() {
        let s = DrawerScratchDefaults()
        s.defaults.set(5, forKey: DrawerPrefs.Key.opacity)
        s.defaults.set("sparkly", forKey: DrawerPrefs.Key.look)
        s.defaults.set("not json", forKey: DrawerPrefs.Key.drawers)
        s.defaults.set(["ok", "Bad Id", "ok"], forKey: DrawerPrefs.Key.folded)
        let d = DrawerPrefs.load(from: s.defaults)
        #expect(d.opacity == 55 && d.look == .solid && d.drawers.isEmpty && d.folded == ["ok"])
    }

    @Test("the desktop location honors the debug override and defaults to the system's")
    func desktopOverride() {
        #expect(DrawerDesktop.standard(environment: [DrawerDesktop.overrideVariable: "/tmp/fixture-desktop"]) == "/tmp/fixture-desktop")
        #expect(DrawerDesktop.standard(environment: [DrawerDesktop.overrideVariable: "relative"]) != "relative")
        #expect(DrawerDesktop.standard(environment: [:]).hasSuffix("/Desktop"))
    }

    @Test("repointing a journal moves the paths in and under a folder, and nothing else")
    func repoint() {
        let m1 = JournalMove(from: "/h/Desktop/a.pdf", to: "/h/Desktop/Drawers/Documents/a.pdf", isFolder: false, written: .done(modified: nil))
        let m2 = JournalMove(from: "/h/Desktop/b.png", to: "/h/Desktop/Drawers/Images/b.png", isFolder: false, written: .done(modified: nil))
        let m3 = JournalMove(from: "/h/Desktop/Drawers/Documents/a.pdf", to: "/h/Desktop/Drawers/Images/a.pdf", isFolder: false, written: .pending)
        let j = DrawerJournal(entries: [JournalEntry(id: "1", at: Date(), source: .rules, moves: [m1, m2, m3])])
        let r = j.repointing(from: "/h/Desktop/Drawers/Documents", to: "/h/Desktop/Drawers/Paperwork")
        let moves = r.entries[0].moves
        #expect(moves[0].to == "/h/Desktop/Drawers/Paperwork/a.pdf" && moves[0].from == m1.from)
        #expect(moves[1] == m2)
        #expect(moves[2].from == "/h/Desktop/Drawers/Paperwork/a.pdf" && moves[2].to == m3.to)
        #expect(j.repointing(from: "relative", to: "/x") == j)
        // A sibling that only shares a prefix is not under the folder.
        let near = JournalMove(from: "/a/b", to: "/h/Drawers/Documents2/z", isFolder: false, written: .done(modified: nil))
        let k = DrawerJournal(entries: [JournalEntry(id: "2", at: Date(), source: .user, moves: [near])])
        #expect(k.repointing(from: "/h/Drawers/Documents", to: "/h/Drawers/P") == k)
    }

    @Test("a drawer listing puts folders first, then Finder order, and hides dotfiles")
    func listing() throws {
        let fx = DrawerFixture()
        fx.write("Home/Desk/z.txt"); fx.write("Home/Desk/file10.txt"); fx.write("Home/Desk/file2.txt")
        fx.write("Home/Desk/.DS_Store"); fx.write("Home/Desk/.hidden")
        try FileManager.default.createDirectory(atPath: fx.base.path + "/Home/Desk/Zeta", withIntermediateDirectories: false)
        let names = DrawerListing.read(fx.home + "/Desk").map(\.name)
        #expect(names == ["Zeta", "file2.txt", "file10.txt", "z.txt"])
    }
}

@MainActor
@Suite("Drawer service")
struct DrawerServiceTests {
    @Test("turning on turns the Desk on, saves the switch, and reads nothing")
    func on() {
        let r = Rig()
        r.service.turnOn()
        #expect(r.service.settings.enabled && r.probe.deskOn == 1 && r.probe.desktopReads == 0)
        #expect(DrawerPrefs.load(from: r.scratch.defaults).enabled)
        #expect(!FileManager.default.fileExists(atPath: r.fx.root))
    }

    @Test("sorting holds a review and moves nothing; apply moves, creates the drawers and journals")
    func sortReviewApply() {
        let r = Rig()
        r.fx.write("Home/Desktop/report.pdf"); r.fx.write("Home/Desktop/photo.jpg"); r.fx.write("Home/Desktop/odd.xyz")
        let start = r.fx.snapshot()
        r.service.turnOn()
        let review = r.service.sort()
        #expect(review?.fileCount == 2 && review?.drawerCount == 2 && r.service.pending == review)
        #expect(r.probe.desktopReads == 1)
        r.fx.expectInvariant(start, drawersOK: false)
        #expect(!FileManager.default.fileExists(atPath: r.fx.root))

        let result = r.service.apply()
        #expect(result?.moved.count == 2 && result?.refusal == nil)
        #expect(r.service.pending == nil)
        #expect(FileManager.default.fileExists(atPath: r.fx.inDrawer("Documents", "report.pdf")))
        #expect(FileManager.default.fileExists(atPath: r.fx.desk("odd.xyz")))
        #expect(Set(r.service.settings.drawers.map(\.id)) == ["documents", "images"])
        #expect(r.service.settings.drawers.allSatisfy { $0.owned })
        #expect(r.service.runs.count == 1)
        r.fx.expectInvariant(start)
        // The drawers persist, so a relaunch sees them.
        #expect(Set(DrawerPrefs.load(from: r.scratch.defaults).drawers.map(\.id)) == ["documents", "images"])
    }

    @Test("dismissing a review leaves the Desktop alone; sort with Drawers off does nothing")
    func dismissAndOff() {
        let r = Rig()
        r.fx.write("Home/Desktop/a.pdf")
        #expect(r.service.sort() == nil && r.probe.desktopReads == 0)
        r.service.turnOn()
        #expect(r.service.sort() != nil)
        r.service.dismissReview()
        #expect(r.service.pending == nil && r.service.apply() == nil)
        #expect(FileManager.default.fileExists(atPath: r.fx.desk("a.pdf")))
    }

    @Test("Undo of a run puts its files back")
    func undoRun() throws {
        let r = Rig()
        r.fx.write("Home/Desktop/report.pdf"); r.fx.write("Home/Desktop/photo.jpg")
        let start = r.fx.snapshot()
        r.service.turnOn(); r.service.sort()
        r.service.apply()
        let id = try #require(r.service.runs.first?.id)
        let result = r.service.undo(id)
        #expect(result?.moved.count == 2)
        #expect(FileManager.default.fileExists(atPath: r.fx.desk("report.pdf")))
        #expect(r.service.runs.isEmpty)
        r.fx.expectInvariant(start)
    }

    @Test("only the last 10 runs are kept for Undo")
    func tenRuns() {
        let r = Rig()
        r.service.turnOn()
        for i in 0..<12 {
            r.fx.write("Home/Desktop/f\(i).pdf")
            r.service.sort(); r.service.apply()
        }
        #expect(r.service.runs.count == 10)
        #expect(r.service.runs.first!.at >= r.service.runs.last!.at)
    }

    @Test("a hand move into an existing drawer lands, is journaled user, and Undo returns it")
    func handMoveExisting() throws {
        let r = Rig()
        r.fx.write("Home/Desktop/report.pdf"); r.fx.write("Home/Desktop/note.xyz")
        let start = r.fx.snapshot()
        r.service.turnOn(); r.service.sort(); r.service.apply()
        let hand = r.service.moveByHand(r.fx.desk("note.xyz"), to: "documents")
        #expect(hand.moved.count == 1)
        #expect(FileManager.default.fileExists(atPath: r.fx.inDrawer("Documents", "note.xyz")))
        let entry = try #require(r.service.runs.first)
        #expect(entry.source == .user)
        r.service.undo(entry.id)
        #expect(FileManager.default.fileExists(atPath: r.fx.desk("note.xyz")))
        r.fx.expectInvariant(start)
    }

    @Test("rename renames the folder, repoints the journal, and Undo still works")
    func renameThenUndo() throws {
        let r = Rig()
        r.fx.write("Home/Desktop/report.pdf")
        let start = r.fx.snapshot()
        r.service.turnOn(); r.service.sort(); r.service.apply()
        let runId = try #require(r.service.runs.first?.id)
        #expect(r.service.rename("documents", to: "Paperwork") == nil)
        #expect(FileManager.default.fileExists(atPath: r.fx.inDrawer("Paperwork", "report.pdf")))
        #expect(!FileManager.default.fileExists(atPath: r.fx.root + "/Documents"))
        let d = try #require(r.service.settings.drawers.first { $0.id == "documents" })
        #expect(d.name == "Paperwork" && d.home.hasSuffix("/Drawers/Paperwork"))
        let journal = r.fx.store.load()
        #expect(journal.entries[0].moves[0].to.hasSuffix("/Drawers/Paperwork/report.pdf"))
        #expect(r.service.undo(runId)?.moved.count == 1)
        #expect(FileManager.default.fileExists(atPath: r.fx.desk("report.pdf")))
        r.fx.expectInvariant(start)
    }

    @Test("rename refuses a taken name, a bad name, and a folder that already exists")
    func renameRefusals() throws {
        let r = Rig()
        r.fx.write("Home/Desktop/a.pdf"); r.fx.write("Home/Desktop/b.png")
        r.service.turnOn(); r.service.sort(); r.service.apply()
        #expect(r.service.rename("documents", to: "Images") != nil)
        #expect(r.service.rename("documents", to: "a/b") != nil)
        #expect(r.service.rename("nope", to: "X") != nil)
        try FileManager.default.createDirectory(atPath: r.fx.root + "/Taken", withIntermediateDirectories: false)
        let was = r.service.settings.drawers
        #expect(r.service.rename("documents", to: "Taken") != nil)
        #expect(r.service.settings.drawers == was)
        #expect(FileManager.default.fileExists(atPath: r.fx.inDrawer("Documents", "a.pdf")))
    }

    @Test("a view drawer is shown, never filled, and untouched by the return")
    func viewDrawer() throws {
        let r = Rig()
        let view = r.fx.write("Home/Elsewhere/Photos/keep.jpg")
        r.fx.write("Home/Desktop/report.pdf"); r.fx.write("Home/Desktop/x.xyz")
        let start = r.fx.snapshot()
        r.service.turnOn()
        #expect(r.service.addViewDrawer(folder: r.fx.home + "/Elsewhere/Photos") == nil)
        #expect(r.service.settings.drawers.contains { !$0.owned && $0.name == "Photos" })
        r.service.sort(); r.service.apply()
        // Hand moves into a view drawer are refused: it is only shown.
        #expect(r.service.moveByHand(r.fx.desk("x.xyz"), to: "photos").moved.isEmpty)
        #expect(FileManager.default.fileExists(atPath: r.fx.desk("x.xyz")))
        #expect(r.service.addViewDrawer(folder: r.fx.home + "/Elsewhere/Photos") != nil)
        #expect(r.service.addViewDrawer(folder: r.fx.desktop) != nil)
        #expect(r.service.addViewDrawer(folder: r.fx.root) != nil)
        #expect(r.service.addViewDrawer(folder: r.fx.home + "/nothing") != nil)
        r.service.confirmTurnOff()
        #expect(FileManager.default.fileExists(atPath: view))
        #expect(r.service.settings.drawers.contains { !$0.owned })
        r.fx.expectInvariant(start)
        #expect(r.service.removeViewDrawer("photos"))
        #expect(r.service.settings.drawers.isEmpty)
    }

    @Test("off asks for the count, then puts everything back, keeps the rules and clears emptied drawers")
    func off() throws {
        let r = Rig()
        r.fx.write("Home/Desktop/a.pdf"); r.fx.write("Home/Desktop/b.png"); r.fx.write("Home/Desktop/c.zip")
        let start = r.fx.snapshot()
        r.service.turnOn(); r.service.sort(); r.service.apply()
        // A file the user dropped in by hand counts too.
        r.fx.write("Home/Desktop/Drawers/Documents/by-hand.txt", "by hand")
        #expect(r.service.offCount() == 4)
        #expect(r.service.offPrompt() == "Put 4 files back on the Desktop?")
        var custom = r.service.settings.rules
        custom.append(DrawerRule(kind: .nameContains, drawer: "documents", text: "tax"))
        r.service.updateSettings { $0.rules = custom }

        let result = r.service.confirmTurnOff()
        #expect(result.moved.count == 4 && result.foldersKept.isEmpty)
        #expect(!r.service.settings.enabled && r.service.settings.drawers.isEmpty)
        #expect(r.service.settings.rules == custom)
        #expect(!FileManager.default.fileExists(atPath: r.fx.root))
        #expect(FileManager.default.fileExists(atPath: r.fx.desk("by-hand.txt")))
        var expected = start
        expected["Home/Desktop/by-hand.txt"] = r.fx.snapshot()["Home/Desktop/by-hand.txt"]
        r.fx.expectInvariant(expected, drawersOK: false)
        #expect(DrawerPrefs.load(from: r.scratch.defaults).rules == custom)
    }

    @Test("a drawer that keeps something stays listed")
    func offKeeps() throws {
        let r = Rig()
        r.fx.write("Home/Desktop/a.pdf")
        r.service.turnOn(); r.service.sort(); r.service.apply()
        // The Desktop already holds a.pdf's 99 numbered names, so the return can't place it.
        r.fx.write("Home/Desktop/a.pdf")
        for n in 2...100 { r.fx.write("Home/Desktop/a \(n).pdf") }
        let result = r.service.confirmTurnOff()
        #expect(result.foldersKept.count >= 1)
        #expect(r.service.settings.drawers.contains { $0.id == "documents" })
        #expect(FileManager.default.fileExists(atPath: r.fx.inDrawer("Documents", "a.pdf")))
    }

    @Test("root choice is allowed with no owned drawer and refused for unsafe roots")
    func rootChoice() throws {
        let r = Rig()
        let safe = r.fx.home + "/Elsewhere/Sorted"
        #expect(r.service.chooseRoot(safe) == nil)
        #expect(r.service.settings.root == DrawerFS.resolve(safe))
        #expect(r.service.chooseRoot(r.fx.desktop) != nil)
        #expect(r.service.chooseRoot(r.fx.home) != nil)
        #expect(r.service.chooseRoot("/") != nil)
        #expect(r.service.chooseRoot("relative/path") != nil)
        #expect(r.service.chooseRoot(r.fx.base.path) != nil)
        #expect(r.service.settings.root == DrawerFS.resolve(safe))
        #expect(r.service.chooseRoot(nil) == nil && r.service.settings.root == nil)

        r.fx.write("Home/Desktop/a.pdf")
        r.service.turnOn(); r.service.sort(); r.service.apply()
        #expect(r.service.chooseRoot(safe) != nil)
        #expect(r.service.settings.root == nil)
    }

    @Test("a chosen root is used, and off keeps a root the user picked")
    func customRoot() throws {
        let r = Rig()
        let root = r.fx.home + "/Elsewhere/Sorted"
        try FileManager.default.createDirectory(atPath: r.fx.home + "/Elsewhere", withIntermediateDirectories: true)
        #expect(r.service.chooseRoot(root) == nil)
        r.fx.write("Home/Desktop/a.pdf")
        let start = r.fx.snapshot()
        r.service.turnOn(); r.service.sort(); r.service.apply()
        #expect(FileManager.default.fileExists(atPath: DrawerFS.resolve(root)! + "/Documents/a.pdf"))
        r.service.confirmTurnOff()
        #expect(FileManager.default.fileExists(atPath: DrawerFS.resolve(root)!))
        #expect(FileManager.default.fileExists(atPath: r.fx.desk("a.pdf")))
        _ = start
    }

    @Test("Reset returns the files, then removes the preferences and the journal")
    func reset() throws {
        let r = Rig()
        r.fx.write("Home/Desktop/a.pdf"); r.fx.write("Home/Desktop/b.png")
        let start = r.fx.snapshot()
        r.service.turnOn(); r.service.sort(); r.service.apply()
        #expect(FileManager.default.fileExists(atPath: r.fx.journalURL.path))
        let report = r.service.resetAll()
        #expect(report.returned == 2 && report.stayedBehind == 0 && report.line == nil)
        #expect(report.prefsRemoved && report.journalRemoved)
        #expect(!FileManager.default.fileExists(atPath: r.fx.journalURL.path))
        #expect(!r.scratch.defaults.dictionaryRepresentation().keys.contains { $0.hasPrefix("drawers.") })
        #expect(!FileManager.default.fileExists(atPath: r.fx.root))
        r.fx.expectInvariant(start, drawersOK: false)
        // A late save does not put anything back.
        r.service.updateSettings { $0.opacity = 90 }
        #expect(!r.scratch.defaults.dictionaryRepresentation().keys.contains { $0.hasPrefix("drawers.") })
    }

    @Test("Reset says so when something stayed behind")
    func resetStays() {
        let r = Rig()
        r.fx.write("Home/Desktop/a.pdf")
        r.service.turnOn(); r.service.sort(); r.service.apply()
        r.fx.write("Home/Desktop/a.pdf")
        for n in 2...100 { r.fx.write("Home/Desktop/a \(n).pdf") }
        let report = r.service.resetAll()
        #expect(report.stayedBehind == 1)
        #expect(report.line == "1 file stayed in the Drawers folder on your Desktop.")
        #expect(report.prefsRemoved && report.journalRemoved)
    }

    @Test("Reset with drawers never set up removes nothing it shouldn't and reads no Desktop")
    func resetEmpty() {
        let r = Rig()
        let report = r.service.resetAll()
        #expect(report.line == nil && report.returned == 0 && r.probe.desktopReads == 0)
    }

    @Test("a crash mid-run is recovered at launch and offered as Undo")
    func recovery() throws {
        let fx = DrawerFixture()
        // A run that stops dead after its first rename, as a crash would.
        let r = Rig(enabled: true, mover: DrawerMover(store: fx.store, afterRename: { $0 == 1 }), fixture: fx)
        r.fx.write("Home/Desktop/a.pdf"); r.fx.write("Home/Desktop/b.pdf"); r.fx.write("Home/Desktop/c.pdf")
        let start = r.fx.snapshot()
        r.service.sort()
        #expect(r.service.apply()?.halted == true)
        #expect(r.fx.store.load().openEntries.count == 1)

        let readsBefore = r.probe.desktopReads
        let fresh = r.relaunch()
        fresh.launch()
        #expect(fresh.recovered.count == 1 && fresh.recovered[0].moves.count == 1)
        #expect(fresh.runs.count == 1)
        // The sort above listed the Desktop once; the relaunch adds no read.
        #expect(r.probe.desktopReads == readsBefore)
        let undone = fresh.undo(fresh.recovered[0].id)
        #expect(undone?.moved.count == 1, "\(String(describing: undone))")
        r.fx.expectInvariant(start, drawersOK: false)
        #expect(fresh.recovered.isEmpty)
    }

    @Test("constructing, launching and resetting with Drawers off never reads the Desktop")
    func offReadsNothing() {
        let fx = DrawerFixture()
        let scratch = DrawerScratchDefaults()
        let probe = ServiceProbe()
        // A Desktop path that does not exist: touching it would be visible, and the facts reader would fail the test.
        let service = DrawerService(defaults: scratch.defaults, desktop: fx.base.path + "/Nowhere/Desktop", home: fx.home,
                                    store: fx.store, turnDeskOn: { probe.on() },
                                    readFacts: { _ in probe.read(); Issue.record("the Desktop was read"); return [] })
        service.launch()
        _ = service.sort()
        #expect(service.offCount() == 0)
        #expect(probe.desktopReads == 0 && probe.deskOn == 0)
        #expect(!FileManager.default.fileExists(atPath: fx.base.path + "/Nowhere"))
    }

    @Test("the service's watcher publishes a change made in a drawer folder")
    func watcherPublishes() async throws {
        let r = Rig()
        r.fx.write("Home/Desktop/report.pdf")
        r.service.turnOn(); r.service.sort(); r.service.apply()
        #expect(r.service.contents["documents"]?.map(\.name) == ["report.pdf"])
        // Something outside the app adds a file; the stream reports it. Retried now and then so a stream
        // that was still starting can't make this flaky.
        var n = 0
        let seen = await waitUntil(15) {
            n += 1
            if n % 20 == 1 { r.fx.write("Home/Desktop/Drawers/Documents/added-\(n).txt") }
            return r.service.contents["documents"]?.contains { $0.name.hasPrefix("added-") } == true
        }
        #expect(seen)
        // Off stops the stream: the published contents are dropped.
        r.service.confirmTurnOff()
        #expect(r.service.contents.isEmpty)
    }
}

@Suite("Drawer watcher")
struct DrawerWatcherTests {
    @Test("a change in a watched folder is reported, and the sibling Desktop is not watched")
    func reports() async throws {
        let fx = DrawerFixture()
        let drawer = fx.realBase + "/Home/Desktop/Drawers/Docs"
        try FileManager.default.createDirectory(atPath: drawer, withIntermediateDirectories: true)
        final class Box: @unchecked Sendable {
            let lock = NSLock(); var paths: [String] = []
            func add(_ p: [String]) { lock.lock(); paths += p; lock.unlock() }
            var all: [String] { lock.lock(); defer { lock.unlock() }; return paths }
        }
        let box = Box()
        let w = DrawerWatcher(paths: [drawer]) { box.add($0) }
        #expect(w.start())
        defer { w.stop() }
        let end = Date().addingTimeInterval(15)
        var n = 0
        while Date() < end, !box.all.contains(where: { $0.contains("/Docs/") }) {
            if n % 20 == 0 { fx.write("Home/Desktop/Drawers/Docs/new-\(n).txt") }
            n += 1
            try await Task.sleep(nanoseconds: 25_000_000)
        }
        #expect(box.all.contains { $0.contains("/Docs/new-") })
        // A file made on the Desktop itself, outside the drawer, is not reported.
        fx.write("Home/Desktop/outside.txt")
        try await Task.sleep(nanoseconds: 600_000_000)
        #expect(!box.all.contains { $0.hasSuffix("/outside.txt") })
        w.stop()
        #expect(!w.isRunning)
    }

    @Test("nothing to watch does not start")
    func empty() {
        #expect(DrawerWatcher(paths: []) { _ in }.start() == false)
    }
}
