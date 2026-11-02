import Foundation
import Testing
@testable import Sanduhr

/// Settings, Drawers (docs/mac-drawers-setup-spec.md, section 8): the page's view model over a
/// service built on a temp Desktop and a throwaway defaults suite, as in DrawerServiceTests.
/// Nothing here names the real Desktop, and no panel is raised.

@MainActor
private struct Page {
    let fx = DrawerFixture()
    let scratch = DrawerScratchDefaults()
    let service: DrawerService
    let model: DrawersSettingsModel
    let tidied: Counter

    final class Counter { var n = 0 }

    init(enabled: Bool = false) {
        let fx = self.fx
        if enabled { var s = DrawerSettings(); s.enabled = true; DrawerPrefs.save(s, to: scratch.defaults) }
        service = DrawerService(defaults: scratch.defaults, desktop: fx.desktop, home: fx.home, store: fx.store, turnDeskOn: {})
        let counter = Counter()
        tidied = counter
        model = DrawersSettingsModel(service: service, tidy: { counter.n += 1 })
    }

    /// Two files on the Desktop, sorted and applied: a run in the journal and two files in drawers.
    func sortAndApply() {
        fx.write("Home/Desktop/a.pdf")
        fx.write("Home/Desktop/b.png")
        model.setEnabled(true)
        model.sort()
        service.apply()
    }
}

@MainActor
@Suite("Settings, Drawers: the view model")
struct DrawersSettingsModelTests {
    @Test("the switch turns on without reading the Desktop, and off asks first, then puts the files back")
    func switchOnAndOff() {
        let p = Page()
        p.fx.write("Home/Desktop/a.pdf")
        p.fx.write("Home/Desktop/b.png")
        p.fx.write("Home/Desktop/c.txt")
        p.model.setEnabled(true)
        #expect(p.model.enabled && p.model.offQuestion == nil)
        p.model.sort()
        p.service.apply()
        #expect(!FileManager.default.fileExists(atPath: p.fx.desk("a.pdf")))

        p.model.setEnabled(false)
        #expect(p.model.enabled, "off waits for the yes")
        #expect(p.model.offQuestion == "Put 3 files back on the Desktop?")
        p.model.cancelOff()
        #expect(p.model.enabled && p.model.offQuestion == nil)

        p.model.setEnabled(false)
        p.model.confirmOff()
        #expect(!p.model.enabled && p.model.offQuestion == nil)
        #expect(p.model.message == "3 files put back on the Desktop.")
        for n in ["a.pdf", "b.png", "c.txt"] { #expect(FileManager.default.fileExists(atPath: p.fx.desk(n))) }
        #expect(p.model.rules.count == DrawerRules.defaults.count, "rules are kept")
    }

    @Test("off with nothing sorted asks about 0 files, and the return line says counts only")
    func offWithNothing() {
        let p = Page(enabled: true)
        p.model.setEnabled(false)
        #expect(p.model.offQuestion == "Put 0 files back on the Desktop?")
        p.model.confirmOff()
        #expect(p.model.message == "0 files put back on the Desktop.")
        var r = DrawerRunResult()
        r.refusal = "Nope."
        #expect(DrawersSettingsModel.returnLine(r) == "Nope.")
        r.refusal = nil
        r.moved = [DrawerMoved(from: "/a", to: "/b", isFolder: false)]
        r.foldersKept = [UndoSkip(path: "/x", reason: "kept")]
        #expect(DrawersSettingsModel.returnLine(r) == "1 file put back on the Desktop. 1 folder kept something.")
    }

    @Test("Sort needs the switch, then holds the review for the Desk")
    func sortOpensTheReview() {
        let p = Page()
        p.fx.write("Home/Desktop/a.pdf")
        p.model.sort()
        #expect(p.model.message == "Turn Drawers on first." && p.service.pending == nil)
        p.model.setEnabled(true)
        p.model.sort()
        #expect(p.service.pending?.fileCount == 1 && p.model.message == "The review is on your Desk.")
        #expect(FileManager.default.fileExists(atPath: p.fx.desk("a.pdf")), "nothing moves before Apply")
        p.service.dismissReview()
        try? FileManager.default.removeItem(atPath: p.fx.desk("a.pdf"))
        p.model.sort()
        #expect(p.model.message == "Nothing on the Desktop matches a rule.")
    }

    @Test("the root can be chosen while no drawer exists, and a refusal reads as the service's sentence")
    func rootChoice() {
        let p = Page()
        #expect(p.model.canChooseRoot && p.model.rootPath == p.fx.root)
        try? FileManager.default.createDirectory(atPath: p.fx.realBase + "/Home/Elsewhere", withIntermediateDirectories: true)
        p.model.chooseRoot(p.fx.realBase + "/Home/Elsewhere/Mine")
        #expect(p.model.message == nil && p.model.rootPath == p.fx.realBase + "/Home/Elsewhere/Mine")
        p.model.useDefaultRoot()
        #expect(p.model.rootPath == p.fx.root)
        p.model.chooseRoot(p.fx.desktop)
        #expect(p.model.message != nil && p.model.message == p.service.chooseRoot(p.fx.desktop))
        #expect(p.model.rootPath == p.fx.root)
        p.sortAndApply()
        #expect(!p.model.canChooseRoot)
        p.model.chooseRoot(p.fx.realBase + "/Home/Elsewhere/Other")
        #expect(p.model.message == "The Drawers folder can't change while drawers exist. Turn Drawers off first.")
    }

    @Test("rules turn on and off, reorder, and only custom ones are removed")
    func ruleEdits() {
        let p = Page()
        #expect(p.model.rules.map(\.title).prefix(3) == ["Screenshots", "Images", "Documents"])
        #expect(p.model.rules.first?.canMoveUp == false && p.model.rules.last?.canMoveDown == false)
        p.model.setRule(1, on: false)
        #expect(p.model.rules[1].on == false && p.service.settings.rules[1].on == false)
        p.model.moveRule(1, by: -1)
        #expect(p.model.rules.map(\.title).prefix(2) == ["Images", "Screenshots"])
        p.model.moveRule(0, by: -1)
        p.model.moveRule(p.model.rules.count - 1, by: 1)
        #expect(p.model.rules.count == DrawerRules.defaults.count)
        p.model.removeRule(0)
        #expect(p.model.rules.count == DrawerRules.defaults.count, "a built-in rule stays")
        // The change is stored.
        #expect(DrawerPrefs.load(from: p.scratch.defaults).rules.first?.kind == .images)
    }

    @Test("a custom rule is added with a validated drawer name, goes first, and can be removed")
    func customRules() {
        let p = Page()
        #expect(p.model.addCustomRule(startsWith: false, text: "  ", drawer: "Bills") != nil)
        #expect(p.model.addCustomRule(startsWith: false, text: "invoice", drawer: "a/b") != nil)
        #expect(p.model.addCustomRule(startsWith: false, text: "invoice", drawer: ".hidden") != nil)
        #expect(p.model.addCustomRule(startsWith: false, text: "invoice", drawer: String(repeating: "x", count: 41)) != nil)
        #expect(p.model.rules.count == DrawerRules.defaults.count)

        #expect(p.model.addCustomRule(startsWith: false, text: "invoice", drawer: "Bills") == nil)
        #expect(p.model.addCustomRule(startsWith: true, text: "IMG_", drawer: "Documents") == nil)
        let rules = p.service.settings.rules
        #expect(rules.count == DrawerRules.defaults.count + 2)
        #expect(rules[0].kind == .nameStartsWith && rules[0].drawer == "documents", "an existing drawer's name reuses its id")
        #expect(rules[1].kind == .nameContains && rules[1].drawer == "bills" && rules[1].isCustom)
        #expect(p.model.rules[1].title == "Name contains \"invoice\" into Bills")

        // A second rule into the same new name shares its drawer.
        #expect(p.model.addCustomRule(startsWith: false, text: "receipt", drawer: "bills") == nil)
        #expect(p.service.settings.rules[0].drawer == "bills")

        p.model.removeRule(0)
        #expect(p.model.rules.count == DrawerRules.defaults.count + 2)
        #expect(p.service.settings.rules.allSatisfy { $0.text != "receipt" })
    }

    @Test("a custom rule fills its new drawer when sorting")
    func customRuleSorts() {
        let p = Page()
        p.fx.write("Home/Desktop/invoice-march.txt")
        p.model.setEnabled(true)
        _ = p.model.addCustomRule(startsWith: false, text: "invoice", drawer: "Bills")
        p.model.sort()
        #expect(p.service.pending?.plan.drawers.map(\.name) == ["Bills"])
        p.service.apply()
        #expect(FileManager.default.fileExists(atPath: p.fx.inDrawer("Bills", "invoice-march.txt")))
    }

    @Test("the Older files days clamp to 1 through 3650 and reach every Older files rule")
    func daysClamp() {
        let p = Page()
        #expect(p.model.olderDays == 30)
        p.model.setOlderDays(0)
        #expect(p.model.olderDays == 1)
        p.model.setOlderDays(99_999)
        #expect(p.model.olderDays == 3650)
        p.model.setOlderDays(-5)
        #expect(p.model.olderDays == 1)
        p.model.setOlderDays(90)
        #expect(p.model.olderDays == 90 && DrawerPrefs.load(from: p.scratch.defaults).rules.last?.days == 90)
    }

    @Test("view drawers are added and removed, refusals are shown, and a drawer of Sanduhr's own is not removed here")
    func viewDrawers() {
        let p = Page()
        p.fx.write("Home/Elsewhere/Photos/x.jpg")
        let folder = p.fx.realBase + "/Home/Elsewhere/Photos"
        p.model.addViewDrawer(folder)
        #expect(p.model.message == nil && p.model.viewDrawers.map(\.name) == ["Photos"])
        p.model.addViewDrawer(folder)
        #expect(p.model.message == "That folder is already a drawer.")
        p.model.addViewDrawer(p.fx.desktop)
        #expect(p.model.message == "That folder holds the Desktop.")
        p.model.removeViewDrawer("photos")
        #expect(p.model.viewDrawers.isEmpty)
        #expect(FileManager.default.fileExists(atPath: folder + "/x.jpg"), "its files are never touched")

        p.sortAndApply()
        p.model.removeViewDrawer("documents")
        #expect(p.model.message?.contains("turn Drawers off") == true || p.model.message?.contains("Turn Drawers off") == true)
        #expect(p.service.settings.drawers.contains { $0.id == "documents" })
    }

    @Test("a hidden drawer is listed with Show, and Show brings it back")
    func unhide() {
        let p = Page()
        p.fx.write("Home/Elsewhere/Photos/x.jpg")
        p.model.addViewDrawer(p.fx.realBase + "/Home/Elsewhere/Photos")
        #expect(p.model.hiddenDrawers.isEmpty)
        p.service.updateSettings { $0.hidden = ["photos", "gone"] }
        #expect(p.model.hiddenDrawers.map(\.id) == ["photos"], "an id with no drawer is not listed")
        p.model.show("photos")
        #expect(p.model.hiddenDrawers.isEmpty && p.service.settings.hidden == ["gone"])
        #expect(DrawerPrefs.load(from: p.scratch.defaults).hidden == ["gone"])
    }

    @Test("look, opacity, corner and direction update and persist, and Tidy runs the panels' tidy")
    func lookAndTidy() {
        let p = Page(enabled: true)
        p.model.setLook(.glass)
        p.model.setOpacity(5)
        #expect(p.model.settings.look == .glass && p.model.settings.opacity == 20)
        p.model.setOpacity(500)
        #expect(p.model.settings.opacity == 100)
        p.model.setOpacity(70)
        p.model.setCorner(.bottomLeft)
        p.model.setFlow(.row)
        let saved = DrawerPrefs.load(from: p.scratch.defaults)
        #expect(saved.look == .glass && saved.opacity == 70 && saved.tidyCorner == .bottomLeft && saved.tidyFlow == .row)
        for look in DrawerLook.allCases { p.model.setLook(look); #expect(p.model.settings.look == look) }
        p.model.tidy()
        #expect(p.tidied.n == 1)
    }

    @Test("recent runs list counts, not names, and Undo puts the files back")
    func runsAndUndo() {
        let p = Page()
        #expect(p.model.runs.isEmpty)
        p.sortAndApply()
        let runs = p.model.runs
        #expect(runs.count == 1 && runs[0].files == 2 && !runs[0].byHand && !runs[0].recovered)
        #expect(runs[0].title == "2 files")
        p.model.undo(runs[0].id)
        #expect(p.model.message == "2 moved.")
        #expect(FileManager.default.fileExists(atPath: p.fx.desk("a.pdf")) && FileManager.default.fileExists(atPath: p.fx.desk("b.png")))
        p.model.undo("no-such-run")
        #expect(p.model.message == "That run is no longer in the journal.")
    }

    @Test("a run a crash left open is offered, marked recovered")
    func recoveredRuns() {
        let move = JournalMove(from: "/h/Desktop/a.pdf", to: "/h/Desktop/Drawers/Documents/a.pdf", isFolder: false, written: .done(modified: nil))
        let entry = JournalEntry(id: "open-1", at: Date(), source: .user, moves: [move])
        let row = DrawerRunRow(id: entry.id, at: entry.at, files: 1, byHand: true, recovered: true)
        #expect(row.title == "1 file, by hand, recovered")
    }

    @Test("the page's wording: the uninstall note is the spec's, and the panel's item opens this page")
    func wording() {
        #expect(DrawersSettings.uninstallNote == "Before you remove Sanduhr, turn Drawers off to put your files back. Otherwise they stay in the Drawers folder on your Desktop.")
        #expect(DrawerPanelView.settingsItem == SettingsSection.drawers.linkTitle)
        #expect(SettingsSection.drawers.linkTitle == "Drawers Settings…")
        #expect(SettingsSection.groups.first { $0.header == "Desktop" }?.sections.contains(.drawers) == true)
        #expect(SettingsAnchor.entries(on: .drawers).count == 5)
    }
}
