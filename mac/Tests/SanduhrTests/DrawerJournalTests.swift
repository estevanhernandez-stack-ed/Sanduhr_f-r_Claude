import Foundation
import Testing
@testable import Sanduhr

/// The journal, Undo and the return (docs/mac-drawers-setup-spec.md, section 5). Pure: paths are
/// made-up strings and the file system comes in through closures.

@Suite("Drawer journal, Undo and return")
struct DrawerJournalTests {
    // 2026-11-01 12:00:00 UTC
    let now = Date(timeIntervalSince1970: 1_793_534_400)
    let desk = "/Users/x/Desktop"
    let root = "/Users/x/Desktop/Drawers"

    func move(_ from: String, _ to: String, folder: Bool = false, modified: Date? = nil, pending: Bool = false) -> JournalMove {
        JournalMove(from: from, to: to, isFolder: folder,
                    written: pending ? .pending : .done(modified: folder ? nil : modified))
    }

    func entry(_ id: String, at: Date? = nil, source: DrawerPlanSource = .rules, _ moves: [JournalMove]) -> JournalEntry {
        JournalEntry(id: id, at: at ?? now, source: source, moves: moves)
    }

    // MARK: format

    @Test("encode then decode gives the same journal")
    func roundTrip() {
        let m1 = move("/Users/x/Desktop/a.pdf", "/Users/x/Desktop/Drawers/Documents/a.pdf", modified: now.addingTimeInterval(-60.25))
        let m2 = move("/Users/x/Desktop/Old", "/Users/x/Desktop/Drawers/Older/Old", folder: true)
        let m3 = move("/Users/x/Desktop/c.png", "/Users/x/Desktop/Drawers/Images/c.png", pending: true)
        let j = DrawerJournal(entries: [
            entry("r1", at: now.addingTimeInterval(0.5), [m1, m2, m3]),
            entry("u1", source: .user, [m1]),
        ])
        #expect(DrawerJournal.decode(j.encoded()) == j)
    }

    @Test("the file uses Windows' keys and time format")
    func shape() throws {
        let m = move("/a/b.txt", "/a/D/b.txt", modified: now)
        let json = DrawerJournal(entries: [entry("r1", [m])]).encoded()
        #expect(json.contains(#""at":"2026-11-01T12:00:00.0000000Z""#))
        #expect(json.contains(#""written":"2026-11-01T12:00:00.0000000Z""#))
        #expect(json.contains(#""source":"rules""#))
        #expect(json.contains(#""folder":false"#))
    }

    @Test("a journal written by Windows decodes")
    func windowsSample() {
        let sample = """
        {"entries":[{"id":"abc","at":"2026-10-30T08:15:30.1234567Z","source":"rules","agent":null,"moves":[
        {"from":"C:\\\\x\\\\a.pdf","to":"C:\\\\x\\\\D\\\\a.pdf","folder":false,"written":"2026-10-29T10:00:00.5000000Z"},
        {"from":"C:\\\\x\\\\F","to":"C:\\\\x\\\\D\\\\F","folder":true,"written":null}]},
        {"id":"bad","at":"nope","source":"rules","moves":[]},
        {"id":"ag","at":"2026-10-30T08:15:30Z","source":"agent","moves":[{"from":"/a","to":"/b","folder":false,"written":null}]}]}
        """
        let j = DrawerJournal.decode(sample)
        #expect(j.entries.count == 1)
        let e = j.entries[0]
        #expect(e.id == "abc" && e.source == .rules && e.agent == nil)
        #expect(abs(e.at.timeIntervalSince1970 - (1_793_348_130 + 0.1234567)) < 0.0001)
        #expect(e.moves.count == 2)
        #expect(e.moves[0].written == .done(modified: Date(timeIntervalSince1970: 1_793_268_000.5)))
        #expect(e.moves[1].isFolder && e.moves[1].written == .done(modified: nil))
        #expect(!e.isOpen)
    }

    @Test("garbage reads as an empty journal")
    func garbage() {
        for s in [nil, "", "   ", "[]", "42", "{", #"{"entries":5}"#] { #expect(DrawerJournal.decode(s).entries.isEmpty) }
    }

    @Test("a time with an offset or a short fraction parses")
    func timeParsing() {
        #expect(JournalTime.parse("2026-11-01T12:00:00Z") == now)
        #expect(JournalTime.parse("2026-11-01T07:00:00-05:00") == now)
        #expect(JournalTime.parse("2026-11-01T12:00:00.5Z") == now.addingTimeInterval(0.5))
        #expect(JournalTime.parse("yesterday") == nil)
    }

    // MARK: trim

    @Test("51 runs keep the newest 50")
    func trimRuns() {
        var j = DrawerJournal()
        for i in 1...51 { j = j.adding(entry("r\(i)", at: now.addingTimeInterval(Double(i)), [move("/a/\(i)", "/b/\(i)")]), now: now) }
        #expect(j.entries.count == 50)
        #expect(j.entries.first?.id == "r2" && j.entries.last?.id == "r51")
    }

    @Test("user moves older than 30 days drop, newer stay, and they don't count toward 50")
    func trimUser() {
        var j = DrawerJournal(entries: [
            entry("old", at: now.addingTimeInterval(-31 * 86_400), source: .user, [move("/a/1", "/b/1")]),
            entry("edge", at: now.addingTimeInterval(-29 * 86_400), source: .user, [move("/a/2", "/b/2")]),
            entry("new", at: now.addingTimeInterval(-3600), source: .user, [move("/a/3", "/b/3")]),
        ])
        for i in 1...50 { j = j.adding(entry("r\(i)", [move("/a/r\(i)", "/b/r\(i)")]), now: now) }
        let ids = j.entries.map(\.id)
        #expect(!ids.contains("old"))
        #expect(ids.contains("edge") && ids.contains("new"))
        #expect(j.entries.filter { $0.source == .rules }.count == 50)
    }

    @Test("without drops one entry and an unknown id changes nothing")
    func without() {
        let j = DrawerJournal(entries: [entry("a", [move("/a/1", "/b/1")]), entry("b", [move("/a/2", "/b/2")])])
        #expect(j.without("a").entries.map(\.id) == ["b"])
        #expect(j.without("zzz") == j)
    }

    // MARK: crash recovery

    @Test("an open entry is found and its Undo covers only the written moves")
    func openEntry() {
        let a = move("/Users/x/Desktop/a.pdf", "/Users/x/Desktop/Drawers/Documents/a.pdf", modified: now)
        let b = move("/Users/x/Desktop/b.pdf", "/Users/x/Desktop/Drawers/Documents/b.pdf", modified: now)
        let c = move("/Users/x/Desktop/c.pdf", "/Users/x/Desktop/Drawers/Documents/c.pdf", pending: true)
        let closed = entry("done", [a])
        let open = entry("open", [a, b, c])
        let j = DrawerJournal.decode(DrawerJournal(entries: [closed, open]).encoded())
        #expect(j.openEntries.map(\.id) == ["open"])
        let plan = DrawerUndo.plan(DrawerJournal.recovered(j.openEntries[0]), modified: { _ in self.now }, exists: { _ in false })
        #expect(plan.steps.map(\.from) == [b.to, a.to])
        #expect(!plan.steps.contains { $0.from == c.to })
        // Even the raw entry never touches the pending move.
        #expect(DrawerUndo.plan(open, modified: { _ in self.now }, exists: { _ in false }).steps.count == 2)
        #expect(j.summary().last?.files == 2)
    }

    // MARK: Undo

    @Test("Undo runs newest first back to the old places")
    func undoOrder() {
        let a = move("/Users/x/Desktop/a.pdf", "/Users/x/Desktop/Drawers/Documents/a.pdf", modified: now)
        let b = move("/Users/x/Desktop/b.png", "/Users/x/Desktop/Drawers/Images/b.png", modified: now)
        let plan = DrawerUndo.plan(entry("r", [a, b]), modified: { _ in self.now }, exists: { _ in false })
        #expect(plan.steps == [UndoStep(from: b.to, to: b.from), UndoStep(from: a.to, to: a.from)])
        #expect(plan.skipped.isEmpty)
    }

    @Test("a file that is gone is skipped as moved since")
    func undoGone() {
        let a = move("/Users/x/Desktop/a.pdf", "/Users/x/Desktop/Drawers/Documents/a.pdf", modified: now)
        let plan = DrawerUndo.plan(entry("r", [a]), modified: { _ in nil }, exists: { _ in false })
        #expect(plan.steps.isEmpty)
        #expect(plan.skipped == [UndoSkip(path: a.to, reason: "was moved since")])
    }

    @Test("a file with a new modification date is skipped as changed since")
    func undoChanged() {
        let a = move("/Users/x/Desktop/a.pdf", "/Users/x/Desktop/Drawers/Documents/a.pdf", modified: now)
        let plan = DrawerUndo.plan(entry("r", [a]), modified: { _ in self.now.addingTimeInterval(5) }, exists: { _ in false })
        #expect(plan.steps.isEmpty)
        #expect(plan.skipped == [UndoSkip(path: a.to, reason: "changed since")])
        // A date within the journal's own precision is the same date.
        let same = DrawerUndo.plan(entry("r", [a]), modified: { _ in self.now.addingTimeInterval(0.0000001) }, exists: { _ in false })
        #expect(same.steps.count == 1)
    }

    @Test("a folder skips the date check")
    func undoFolder() {
        let f = move("/Users/x/Desktop/Proj", "/Users/x/Desktop/Drawers/Older/Proj", folder: true)
        let plan = DrawerUndo.plan(entry("r", [f]), modified: { _ in self.now.addingTimeInterval(9999) }, exists: { _ in false })
        #expect(plan.steps == [UndoStep(from: f.to, to: f.from)])
    }

    @Test("a taken old place gets the next free name, in both file and folder forms")
    func undoClash() {
        let a = move("/Users/x/Desktop/Report.pdf", "/Users/x/Desktop/Drawers/Documents/Report.pdf", modified: now)
        let f = move("/Users/x/Desktop/Proj", "/Users/x/Desktop/Drawers/Older/Proj", folder: true)
        let taken: Set<String> = ["/Users/x/Desktop/Report.pdf", "/Users/x/Desktop/Report 2.pdf", "/Users/x/Desktop/Proj"]
        let plan = DrawerUndo.plan(entry("r", [a, f]), modified: { _ in self.now }, exists: { taken.contains($0) })
        #expect(plan.steps.map(\.to) == ["/Users/x/Desktop/Proj 2", "/Users/x/Desktop/Report 3.pdf"])
    }

    @Test("two moves from the same old name don't take the same place")
    func undoClaimed() {
        let a = move("/Users/x/Desktop/R.pdf", "/Users/x/Desktop/Drawers/Documents/R.pdf", modified: now)
        let b = move("/Users/x/Desktop/R.pdf", "/Users/x/Desktop/Drawers/Older/R.pdf", modified: now)
        let plan = DrawerUndo.plan(entry("r", [a, b]), modified: { _ in self.now }, exists: { _ in false })
        #expect(plan.steps.map(\.to) == ["/Users/x/Desktop/R.pdf", "/Users/x/Desktop/R 2.pdf"])
    }

    // MARK: return

    func drawers() -> [DrawerDef] {
        [DrawerDef(id: "documents", name: "Documents", home: "\(root)/Documents", owned: true),
         DrawerDef(id: "images", name: "Images", home: "\(root)/Images", owned: true),
         DrawerDef(id: "downloads", name: "Downloads", home: "/Users/x/Downloads", owned: false),
         DrawerDef(id: "elsewhere", name: "Elsewhere", home: "/Users/x/Other/Elsewhere", owned: true)]
    }

    func kids(_ table: [String: [DrawerChild]]) -> (String) -> [DrawerChild] { { table[$0] ?? [] } }

    @Test("the return empties owned drawers, hand-made and rules-made files alike, then lists folders to remove")
    func returnMixed() {
        let table = [
            "\(root)/Documents": [DrawerChild(path: "\(root)/Documents/b.pdf", isFolder: false),
                                  DrawerChild(path: "\(root)/Documents/a.pdf", isFolder: false)],
            "\(root)/Images": [DrawerChild(path: "\(root)/Images/Trip", isFolder: true)],
            "/Users/x/Downloads": [DrawerChild(path: "/Users/x/Downloads/keep.zip", isFolder: false)],
            "/Users/x/Other/Elsewhere": [DrawerChild(path: "/Users/x/Other/Elsewhere/z.txt", isFolder: false)],
        ]
        let plan = DrawerReturn.plan(drawers: drawers(), root: root, desktop: desk, childrenOf: kids(table),
                                     exists: { _ in false }, removeRoot: true)
        #expect(plan.moves == [
            UndoStep(from: "\(root)/Documents/a.pdf", to: "\(desk)/a.pdf"),
            UndoStep(from: "\(root)/Documents/b.pdf", to: "\(desk)/b.pdf"),
            UndoStep(from: "\(root)/Images/Trip", to: "\(desk)/Trip"),
        ])
        // The view drawer and the owned drawer outside the root are untouched; the root is last.
        #expect(plan.foldersToRemove == ["\(root)/Documents", "\(root)/Images", root])
    }

    @Test("a non-default root is kept")
    func returnKeepsRoot() {
        let plan = DrawerReturn.plan(drawers: drawers(), root: root, desktop: desk, childrenOf: { _ in [] },
                                     exists: { _ in false }, removeRoot: false)
        #expect(plan.foldersToRemove == ["\(root)/Documents", "\(root)/Images"])
    }

    @Test("return names clash with the Desktop and with each other")
    func returnClash() {
        let table = [
            "\(root)/Documents": [DrawerChild(path: "\(root)/Documents/Report.pdf", isFolder: false)],
            "\(root)/Images": [DrawerChild(path: "\(root)/Images/Report.pdf", isFolder: false),
                               DrawerChild(path: "\(root)/Images/Trip", isFolder: true)],
        ]
        let onDesk: Set<String> = ["\(desk)/Trip"]
        let plan = DrawerReturn.plan(drawers: drawers(), root: root, desktop: desk, childrenOf: kids(table),
                                     exists: { onDesk.contains($0) }, removeRoot: true)
        #expect(plan.moves.map(\.to) == ["\(desk)/Report.pdf", "\(desk)/Report 2.pdf", "\(desk)/Trip 2"])
    }

    @Test("a name already on the Desktop takes the next number")
    func returnDeskName() {
        let table = ["\(root)/Documents": [DrawerChild(path: "\(root)/Documents/Report.pdf", isFolder: false)]]
        let onDesk: Set<String> = ["\(desk)/Report.pdf", "\(desk)/Report 2.pdf"]
        let plan = DrawerReturn.plan(drawers: drawers(), root: root, desktop: desk, childrenOf: kids(table),
                                     exists: { onDesk.contains($0) }, removeRoot: true)
        #expect(plan.moves.map(\.to) == ["\(desk)/Report 3.pdf"])
    }

    @Test("a root that is the Desktop or holds it plans nothing")
    func returnUnsafeRoot() {
        let table = ["/Users/x/Desktop/Documents": [DrawerChild(path: "/Users/x/Desktop/Documents/a.pdf", isFolder: false)]]
        for bad in [desk, "/Users/x", "/Users", "/"] {
            let ds = [DrawerDef(id: "documents", name: "Documents", home: "\(bad == "/" ? "" : bad)/Documents", owned: true)]
            let plan = DrawerReturn.plan(drawers: ds, root: bad, desktop: desk, childrenOf: kids(table),
                                         exists: { _ in false }, removeRoot: true)
            #expect(plan.moves.isEmpty && plan.foldersToRemove.isEmpty)
        }
    }

    @Test("nothing owned in the root plans nothing, not even the root")
    func returnNothing() {
        let ds = [DrawerDef(id: "downloads", name: "Downloads", home: "/Users/x/Downloads", owned: false)]
        let plan = DrawerReturn.plan(drawers: ds, root: root, desktop: desk, childrenOf: { _ in [] },
                                     exists: { _ in false }, removeRoot: true)
        #expect(plan.moves.isEmpty && plan.foldersToRemove.isEmpty)
    }

    @Test("the prompt counts")
    func prompt() {
        #expect(DrawerReturn.prompt(1) == "Put 1 file back on the Desktop?")
        #expect(DrawerReturn.prompt(0) == "Put 0 files back on the Desktop?")
        #expect(DrawerReturn.prompt(12) == "Put 12 files back on the Desktop?")
    }
}
