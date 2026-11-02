import Foundation
import Testing
@testable import Sanduhr

/// Drawer rules, plans, the validator and free names (docs/mac-drawers-setup-spec.md, 1 to 3).

@Suite("Drawer rules")
struct DrawerRulesTests {
    // 2026-11-01 12:00:00 UTC
    let now = Date(timeIntervalSince1970: 1_793_534_400)
    var fresh: Date { now.addingTimeInterval(-3600) }
    func old(_ days: Double) -> Date { now.addingTimeInterval(-days * 86_400) }

    func file(_ name: String, capture: Bool = false, alias: Bool = false, modified: Date? = nil) -> DrawerItemFacts {
        DrawerItemFacts(name: name, isAlias: alias, isScreenCapture: capture, size: 10, modified: modified ?? fresh)
    }

    /// The id of the drawer the default rules send `f` to, or nil.
    func target(_ f: DrawerItemFacts, rules: [DrawerRule] = DrawerRules.defaults) -> String? {
        rules.first { DrawerRules.matches($0, f, now: now) }?.drawer
    }

    @Test("built-in rules against names and flags", arguments: [
        ("Report.pdf", false, false, "documents"),
        ("notes.TXT", false, false, "documents"),
        ("Budget.numbers", false, false, "documents"),
        ("Deck.key", false, false, "documents"),
        ("Essay.pages", false, false, "documents"),
        ("photo.HEIC", false, false, "images"),
        ("photo.heif", false, false, "images"),
        ("logo.svg", false, false, "images"),
        ("Screenshot 2026-11-01 at 9.41.02 AM.png", false, false, "screenshots"),
        ("Screen Shot 2019-04-02 at 3.10.11 PM.png", false, false, "screenshots"),
        ("screenshot.jpg", false, false, "screenshots"),
        ("Bildschirmfoto 2026-11-01.png", true, false, "screenshots"),
        ("Screen Recording 2026-11-01.mov", true, false, "screenshots"),
        ("Screenshot notes.txt", false, false, "documents"),
        ("Setup.dmg", false, false, "installers"),
        ("tool.pkg", false, false, "installers"),
        ("suite.mpkg", false, false, "installers"),
        ("backup.zip", false, false, "archives"),
        ("disk.iso", false, false, "archives"),
        ("bundle.tgz", false, false, "archives"),
        ("Docs.webloc", false, false, "links"),
        ("Site.inetloc", false, false, "links"),
        ("Project folder", false, true, "links"),
        ("movie.mov", false, false, nil),
        ("noext", false, false, nil),
    ] as [(String, Bool, Bool, String?)])
    func builtins(name: String, capture: Bool, alias: Bool, expected: String?) {
        #expect(target(file(name, capture: capture, alias: alias)) == expected)
    }

    @Test("an image named Screenshot goes to Screenshots, not Images (first match wins)")
    func firstMatch() {
        #expect(target(file("Screenshot 1.png")) == "screenshots")
        var rules = DrawerRules.defaults
        rules.swapAt(0, 1)
        #expect(target(file("Screenshot 1.png"), rules: rules) == "images")
    }

    @Test("a kind rule beats Older files; an old unmatched file goes to Older files")
    func olderLast() {
        #expect(target(file("a.pdf", modified: old(400))) == "documents")
        #expect(target(file("clip.mov", modified: old(400))) == "older-files")
        #expect(target(file("clip.mov", modified: old(5))) == nil)
    }

    @Test("a turned-off rule never matches")
    func off() {
        var rules = DrawerRules.defaults
        rules[2].on = false
        #expect(target(file("a.pdf"), rules: rules) == nil)
    }

    @Test("never moved: folders, packages, hidden, symlinks, dataless, housekeeping")
    func neverMoved() {
        func f(_ name: String, dir: Bool = false, pkg: Bool = false, hidden: Bool = false, link: Bool = false, dataless: Bool = false) -> DrawerItemFacts {
            DrawerItemFacts(name: name, isDirectory: dir, isPackage: pkg, isHidden: hidden, isSymlink: link,
                            isDataless: dataless, isScreenCapture: true, modified: old(900))
        }
        let cases = [
            f("Stuff.pdf", dir: true), f("Thing.pages", dir: true, pkg: true), f("Tool.app", dir: true, pkg: true),
            f("a.pdf", hidden: true), f(".hidden.png"), f(".DS_Store"), f(".localized"), f("Icon\r"),
            f("a.pdf", link: true), f("a.pdf", dataless: true),
        ]
        for c in cases {
            #expect(DrawerRules.isNeverMoved(c), "\(c.name)")
            #expect(target(c) == nil, "\(c.name)")
        }
        #expect(!DrawerRules.isNeverMoved(file("Icon.png")))
    }

    @Test("custom rules: contains and starts with, case-insensitive")
    func custom() {
        let contains = DrawerRule(kind: .nameContains, drawer: "work", text: "invoice")
        let starts = DrawerRule(kind: .nameStartsWith, drawer: "trips", text: "TRIP")
        #expect(target(file("March INVOICE final.xyz"), rules: [contains]) == "work")
        #expect(target(file("receipt.xyz"), rules: [contains]) == nil)
        #expect(target(file("trip to oslo.xyz"), rules: [starts]) == "trips")
        #expect(target(file("my trip.xyz"), rules: [starts]) == nil)
        #expect(target(file("anything.xyz"), rules: [DrawerRule(kind: .nameContains, drawer: "work", text: "")]) == nil)
        #expect(target(file("anything.xyz"), rules: [DrawerRule(kind: .nameStartsWith, drawer: "work", text: nil)]) == nil)
        // A custom rule never takes a hidden file or a folder either.
        #expect(target(DrawerItemFacts(name: "invoice", isDirectory: true), rules: [contains]) == nil)
    }

    @Test("Older files: more than N days, days clamped to 1 to 3650")
    func days() {
        func older(_ d: Int) -> DrawerRule { DrawerRule(kind: .olderThan, drawer: "o", days: d) }
        #expect(DrawerRules.defaults.last?.days == 30)
        #expect(DrawerRules.matches(older(30), file("a.xyz", modified: old(31)), now: now))
        #expect(!DrawerRules.matches(older(30), file("a.xyz", modified: old(30)), now: now))
        #expect(DrawerRules.matches(older(1), file("a.xyz", modified: old(1.5)), now: now))
        #expect(DrawerRules.matches(older(0), file("a.xyz", modified: old(1.5)), now: now))      // 0 acts as 1
        #expect(!DrawerRules.matches(older(0), file("a.xyz", modified: old(0.5)), now: now))
        #expect(DrawerRules.matches(older(99_999), file("a.xyz", modified: old(3651)), now: now)) // capped at 3650
        #expect(!DrawerRules.matches(older(99_999), file("a.xyz", modified: old(3600)), now: now))
        #expect(DrawerRules.clampDays(-5) == 1)
        #expect(DrawerRules.clampDays(4000) == 3650)
        #expect(DrawerRules.clampDays(90) == 90)
    }

    @Test("default drawers: seven names in order, ids match the rules")
    func defaultDrawers() {
        let d = DrawerRules.defaultDrawers(root: "/Users/x/Desktop/Drawers")
        #expect(d.map(\.name) == ["Screenshots", "Images", "Documents", "Installers", "Archives", "Links", "Older files"])
        #expect(d.map(\.id) == DrawerRules.defaults.map(\.drawer))
        #expect(d.allSatisfy { $0.owned })
        #expect(d[6].home == "/Users/x/Desktop/Drawers/Older files")
    }

    @Test("plan builder groups moves per drawer and creates default drawers")
    func plan() {
        let desktop = "/Users/x/Desktop", root = "/Users/x/Desktop/Drawers"
        let facts = [
            file("b.pdf"), file("a.png"), file("Screenshot 1.png"), file("clip.mov"),
            DrawerItemFacts(name: "Drawers", isDirectory: true),
            DrawerItemFacts(name: "Folder", isDirectory: true),
            file("old.mov", modified: old(90)),
        ]
        let p = DrawerRules.plan(facts: facts, rules: DrawerRules.defaults, drawers: [], desktop: desktop, root: root, now: now)
        #expect(p.source == .rules)
        #expect(p.moves.map(\.source) == ["\(desktop)/a.png", "\(desktop)/b.pdf", "\(desktop)/old.mov", "\(desktop)/Screenshot 1.png"])
        #expect(Set(p.drawers.map(\.name)) == ["Images", "Documents", "Older files", "Screenshots"])
        let g = p.grouped.first { $0.drawer.id == "documents" }
        #expect(g?.moves.count == 1)
        #expect(g?.drawer.home == "\(root)/Documents")
    }

    @Test("plan builder prefers an existing drawer's name and home, and skips rules naming no drawer")
    func planExisting() {
        let renamed = DrawerDef(id: "documents", name: "Paperwork", home: "/Users/x/Desktop/Drawers/Paperwork", owned: true)
        let rules = DrawerRules.defaults + [DrawerRule(kind: .nameContains, drawer: "ghost", text: "zzz")]
        let p = DrawerRules.plan(facts: [file("a.pdf"), file("zzz.xyz")], rules: rules, drawers: [renamed],
                                 desktop: "/Users/x/Desktop", root: "/Users/x/Desktop/Drawers", now: now)
        #expect(p.drawers == [renamed])
        #expect(p.moves.count == 1)
    }

    @Test("plan builder never takes the root or a drawer folder, even if a file-like name")
    func planSkipsOwn() {
        let view = DrawerDef(id: "v", name: "Pics", home: "/Users/x/Desktop/Pics.png", owned: false)
        let facts = [file("Drawers.pdf"), file("Pics.png"), file("ok.pdf")]
        let p = DrawerRules.plan(facts: facts, rules: DrawerRules.defaults, drawers: [view],
                                 desktop: "/Users/x/Desktop", root: "/Users/x/Desktop/Drawers.pdf", now: now)
        #expect(p.moves.map(\.source) == ["/Users/x/Desktop/ok.pdf"])
    }
}

@Suite("Drawer plan validator")
struct DrawerPlanValidatorTests {
    let desktop = "/Users/x/Desktop"
    let root = "/Users/x/Desktop/Drawers"
    func roots(root: String? = nil, desktop: String? = nil, rootVol: String = "v1") -> DrawerRoots {
        DrawerRoots(desktop: desktop ?? self.desktop, drawersRoot: root ?? self.root, home: "/Users/x",
                    desktopVolume: "v1", rootVolume: rootVol)
    }
    func drawer(_ name: String, at r: String? = nil) -> DrawerDef {
        DrawerDef(id: DrawerIds.from(name), name: name, home: (r ?? root) + "/" + name, owned: true)
    }
    func plan(_ drawers: [DrawerDef], _ moves: [(String, String)]) -> DrawerPlan {
        DrawerPlan(source: .rules, drawers: drawers, moves: moves.map { DrawerMove(source: $0.0, drawerId: $0.1) })
    }

    @Test("a good rules plan passes and creates the new drawer")
    func good() {
        let docs = drawer("Documents")
        let c = DrawerPlanValidator.check(plan([docs], [("\(desktop)/a.pdf", "documents")]), roots: roots(), existing: [])
        #expect(c.ok)
        #expect(c.drawers == [docs])
        #expect(c.moves == [DrawerMove(source: "\(desktop)/a.pdf", drawerId: "documents")])
    }

    @Test("root refusals", arguments: [
        ("/Users/x/Desktop", "the Desktop itself"),
        ("/Users/x", "the home folder"),
        ("/Users", "a folder that holds the Desktop"),
        ("/", "the disk root"),
        ("/Volumes/Backup", "a volume root"),
        ("/Users/x/Desktop/../Desktop", "the Desktop by another spelling"),
        ("/users/X/DESKTOP", "the Desktop in other case"),
        ("Drawers", "a relative path"),
    ])
    func rootRefused(root: String, why: String) {
        let c = DrawerPlanValidator.check(plan([], []), roots: roots(root: root), existing: [])
        #expect(!c.ok, "\(why)")
    }

    @Test("root on another volume is refused; the default root and a sibling folder pass")
    func rootVolume() {
        #expect(!DrawerPlanValidator.check(plan([], []), roots: roots(rootVol: "v2"), existing: []).ok)
        #expect(DrawerPlanValidator.check(plan([], []), roots: roots(), existing: []).ok)
        #expect(DrawerPlanValidator.check(plan([], []), roots: roots(root: "/Users/x/Documents/Sorted"), existing: []).ok)
        #expect(DrawerPlanValidator.check(plan([], []), roots: roots(root: "/Volumes/Backup/Sorted", rootVol: "v1"), existing: []).ok)
    }

    @Test("sources: Desktop children and drawer children pass; the rest are refused")
    func sources() {
        let docs = drawer("Documents"), imgs = drawer("Images")
        func ok(_ s: String, drawers: [DrawerDef]? = nil) -> DrawerPlanCheck {
            DrawerPlanValidator.check(plan(drawers ?? [docs, imgs], [(s, "documents")]), roots: roots(), existing: [])
        }
        #expect(ok("\(desktop)/a.pdf").ok)
        #expect(ok("\(root)/Images/a.pdf").ok)                    // a hand move between drawers
        #expect(!ok("\(desktop)/Sub/a.pdf").ok)                   // a grandchild
        #expect(!ok("/Users/x/Documents/a.pdf").ok)               // elsewhere
        #expect(!ok("relative.pdf").ok)
        #expect(!ok(desktop).ok)                                  // the Desktop itself
        #expect(!ok(root).ok)                                     // the root
        #expect(!ok("/Users/x").ok)                               // holds the root
        #expect(!ok("\(root)/Images").ok)                         // a drawer's own folder
        #expect(!ok("/Users/x/Desktop/../Documents/a.pdf").ok)    // dot-dot out of the Desktop
    }

    @Test("a move already in its drawer is dropped; a duplicate source refuses")
    func noopAndDup() {
        let docs = drawer("Documents")
        let c = DrawerPlanValidator.check(plan([docs], [("\(root)/Documents/a.pdf", "documents"), ("\(desktop)/b.pdf", "documents")]),
                                          roots: roots(), existing: [])
        #expect(c.ok && c.moves.count == 1)
        let d = DrawerPlanValidator.check(plan([docs], [("\(desktop)/b.pdf", "documents"), ("\(desktop)/B.pdf", "documents")]),
                                          roots: roots(), existing: [])
        #expect(!d.ok)
    }

    @Test("a move into an unknown drawer refuses; an existing drawer needs no entry in the plan")
    func unknownDrawer() {
        #expect(!DrawerPlanValidator.check(plan([], [("\(desktop)/a.pdf", "nope")]), roots: roots(), existing: []).ok)
        let c = DrawerPlanValidator.check(plan([], [("\(desktop)/a.pdf", "documents")]), roots: roots(), existing: [drawer("Documents")])
        #expect(c.ok && c.drawers.isEmpty && c.moves.count == 1)
    }

    @Test("new drawers must live in the root under their own name")
    func drawerHomes() {
        var elsewhere = drawer("Documents"); elsewhere.home = "/Users/x/Documents/Documents"
        #expect(!DrawerPlanValidator.check(plan([elsewhere], []), roots: roots(), existing: []).ok)
        var view = drawer("Documents"); view.owned = false
        #expect(!DrawerPlanValidator.check(plan([view], []), roots: roots(), existing: []).ok)
        var badName = drawer("a:b")
        badName.id = "ab"
        #expect(!DrawerPlanValidator.check(plan([badName], []), roots: roots(), existing: []).ok)
        var badId = drawer("Docs"); badId.id = "Bad Id"
        #expect(!DrawerPlanValidator.check(plan([badId], []), roots: roots(), existing: []).ok)
        // Same id as an existing drawer, different home.
        var moved = drawer("Documents"); moved.home = root + "/Other"
        #expect(!DrawerPlanValidator.check(plan([moved], []), roots: roots(), existing: [drawer("Documents")]).ok)
    }

    @Test("limits: 24 drawers and 2000 moves pass, one more refuses")
    func limits() {
        func drawers(_ n: Int) -> [DrawerDef] { (0..<n).map { drawer("D\($0)") } }
        func moves(_ n: Int) -> [(String, String)] { (0..<n).map { ("\(desktop)/f\($0).txt", "d0") } }
        #expect(DrawerPlanValidator.check(plan(drawers(24), []), roots: roots(), existing: []).ok)
        #expect(!DrawerPlanValidator.check(plan(drawers(25), []), roots: roots(), existing: []).ok)
        #expect(DrawerPlanValidator.check(plan(drawers(1), moves(2000)), roots: roots(), existing: []).ok)
        #expect(!DrawerPlanValidator.check(plan(drawers(1), moves(2001)), roots: roots(), existing: []).ok)
    }
}

@Suite("Drawer names")
struct DrawerNamesTests {
    @Test("folder name checks", arguments: [
        ("Screenshots", true), ("Older files", true), ("a", true), (String(repeating: "a", count: 40), true),
        ("", false), ("   ", false), (String(repeating: "a", count: 41), false),
        ("a/b", false), ("a:b", false), (".", false), ("..", false), (".hidden", false), ("tab\there", false), ("nul\0", false),
    ])
    func validity(name: String, ok: Bool) {
        #expect(DrawerNames.isValid(name) == ok)
    }

    @Test("ids come from names")
    func ids() {
        #expect(DrawerIds.from("Older files") == "older-files")
        #expect(DrawerIds.from("  !! ") == "drawer")
        #expect(DrawerIds.from("Tom's  Stuff") == "tom-s-stuff")
        #expect(DrawerIds.isValid("older-files"))
        #expect(!DrawerIds.isValid("Older"))
        #expect(!DrawerIds.isValid("-x"))
        #expect(!DrawerIds.isValid(""))
    }

    func free(_ name: String, folder: Bool = false, taken: Set<String>) -> String? {
        DrawerNames.free(name, isFolder: folder) { taken.contains($0) }
    }

    @Test("free names: itself when free, then 2, 3 before the extension")
    func freeFiles() {
        #expect(free("Report.pdf", taken: []) == "Report.pdf")
        #expect(free("Report.pdf", taken: ["Report.pdf"]) == "Report 2.pdf")
        #expect(free("Report.pdf", taken: ["Report.pdf", "Report 2.pdf"]) == "Report 3.pdf")
        #expect(free("Notes", taken: ["Notes"]) == "Notes 2")
        #expect(free("archive.tar.gz", taken: ["archive.tar.gz"]) == "archive.tar 2.gz")
        #expect(free(".gitignore", taken: [".gitignore"]) == ".gitignore 2")
        #expect(free("trail.", taken: ["trail."]) == "trail. 2")
    }

    @Test("a folder is numbered after its whole name")
    func freeFolders() {
        #expect(free("Name", folder: true, taken: ["Name"]) == "Name 2")
        #expect(free("v1.2", folder: true, taken: ["v1.2"]) == "v1.2 2")
    }

    @Test("99 numbered tries, then nil")
    func cap() {
        #expect(free("a.txt", taken: Set(["a.txt"] + (2...99).map { "a \($0).txt" })) == "a 100.txt")
        #expect(free("a.txt", taken: Set(["a.txt"] + (2...100).map { "a \($0).txt" })) == nil)
        var calls = 0
        #expect(DrawerNames.free("x") { _ in calls += 1; return true } == nil)
        #expect(calls == 100)
    }
}
