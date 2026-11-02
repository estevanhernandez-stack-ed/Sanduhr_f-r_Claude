import CoreGraphics
import Foundation
import Testing
@testable import Sanduhr

/// The drawer panels' pure parts (docs/mac-drawers-setup-spec.md, sections 3 and 7): placement,
/// snap, Tidy, the four looks, the review model, display names, keys and the trash guard. Nothing
/// here draws a window or reads a real folder; the trash tests inject the trasher, so no file is
/// ever moved to the real Trash.

private let work = CGRect(x: 0, y: 0, width: 1440, height: 860)
private func rect(_ x: CGFloat, _ y: CGFloat, folded: Bool = false) -> CGRect {
    CGRect(origin: CGPoint(x: x, y: y), size: DrawerPlacement.size(folded: folded))
}

@Suite("Drawer placement")
struct DrawerPlacementTests {
    @Test func sizesAreTheSpecs() {
        #expect(DrawerPlacement.width == 360)
        #expect(DrawerPlacement.height(folded: false) == 330)
        #expect(DrawerPlacement.height(folded: true) == 42)
    }

    @Test func offsetRoundTripsPerScreen() {
        // A second screen to the right and a menu bar on top: the offset is from the visible frame.
        let visible = CGRect(x: 1440, y: 25, width: 1920, height: 1055)
        let size = DrawerPlacement.size(folded: false)
        let frame = DrawerPlacement.frame(offset: CGPoint(x: 100, y: 60), size: size, in: visible)
        #expect(frame.minX == 1540)
        #expect(frame.maxY == visible.maxY - 60)
        #expect(DrawerPlacement.offset(ofFrame: frame, in: visible) == CGPoint(x: 100, y: 60))
    }

    @Test func sameOffsetOnADifferentResolutionKeepsItsDistanceFromTheTopLeft() {
        let small = CGRect(x: 0, y: 0, width: 1280, height: 720)
        let big = CGRect(x: 0, y: 0, width: 2560, height: 1440)
        let size = DrawerPlacement.size(folded: true)
        let a = DrawerPlacement.offset(ofFrame: DrawerPlacement.frame(offset: CGPoint(x: 50, y: 40), size: size, in: small), in: small)
        let b = DrawerPlacement.offset(ofFrame: DrawerPlacement.frame(offset: CGPoint(x: 50, y: 40), size: size, in: big), in: big)
        #expect(a == b)
    }

    @Test func clampKeepsTheWholeWidthAndTheTitleBarOnScreen() {
        let size = DrawerPlacement.size(folded: false)
        let w = CGSize(width: 1000, height: 700)
        #expect(DrawerPlacement.clamp(CGPoint(x: -50, y: -9), size: size, work: w) == CGPoint(x: 0, y: 0))
        #expect(DrawerPlacement.clamp(CGPoint(x: 5000, y: 5000), size: size, work: w) == CGPoint(x: 640, y: 658))
        // A bottom drawer may hang below the edge, but its 42 pt title bar never does.
        #expect(DrawerPlacement.clamp(CGPoint(x: 10, y: 690), size: size, work: w).y == 658)
        // A work area narrower than a drawer cannot go negative.
        #expect(DrawerPlacement.clamp(CGPoint(x: 30, y: 0), size: size, work: CGSize(width: 300, height: 700)).x == 0)
    }

    @Test func deskPiecesAreConvertedIntoWorkSpace() {
        let screen = CGRect(x: 0, y: 0, width: 1440, height: 900)
        let visible = CGRect(x: 0, y: 0, width: 1440, height: 870)   // 30 pt menu bar on top
        let r = DrawerPlacement.workRect(ofDesk: CGRect(x: 100, y: 130, width: 200, height: 50), screenFrame: screen, visible: visible)
        #expect(r == CGRect(x: 100, y: 100, width: 200, height: 50))
    }

    @Test func newDrawersArriveInTheTopRightColumn() {
        let out = DrawerPlacement.arrive([("a", false), ("b", true)], placed: [], work: work, corner: .topRight, flow: .column)
        #expect(out["a"] == CGPoint(x: 1440 - 24 - 360, y: 24))
        #expect(out["b"] == CGPoint(x: 1440 - 24 - 360, y: 24 + 330 + 12))
    }

    @Test func arrivalSkipsDrawersAlreadyPlaced() {
        let held = rect(1440 - 24 - 360, 24)
        let out = DrawerPlacement.arrive([("a", false)], placed: [held], work: work, corner: .topRight, flow: .column)
        #expect(out["a"]!.y == CGFloat(366))
    }

    @Test func firstTwoOpenTheRestFolded() {
        #expect(DrawerArrival.folded(created: ["a", "b", "c", "d"]) == ["c", "d"])
        #expect(DrawerArrival.folded(created: ["a"]).isEmpty)
    }
}

@Suite("Drawer snap")
struct DrawerSnapTests {
    let drawer = rect(500, 100)

    @Test func snapsToAnotherDrawerWithinTwelve() {
        // Its left edge 8 pt off the drawer's right edge plus the gap (500 + 360 + 12 = 872).
        let r = DrawerSnap.snap(rect(880, 400), work: work, drawers: [drawer], pieces: [])
        #expect(r.minX == 872)
    }

    @Test func doesNotSnapPastTheThreshold() {
        let r = DrawerSnap.snap(rect(890, 400), work: work, drawers: [drawer], pieces: [])
        #expect(r.minX == 890)
    }

    @Test func drawersBeatPiecesBeatTheMargin() {
        let piece = CGRect(x: 878, y: 600, width: 100, height: 40)
        // Within 12 of a drawer edge (872) and of a piece edge (878): the drawer wins.
        #expect(DrawerSnap.snap(rect(876, 400), work: work, drawers: [drawer], pieces: [piece]).minX == 872)
        // With no drawer in range the piece wins over the margin candidate.
        #expect(DrawerSnap.snap(rect(880, 400), work: work, drawers: [], pieces: [piece]).minX == 878)
        // With neither, the margin (24) takes it.
        #expect(DrawerSnap.snap(rect(30, 400), work: work, drawers: [], pieces: []).minX == 24)
        #expect(DrawerSnap.snap(rect(1440 - 360 - 20, 400), work: work, drawers: [], pieces: []).minX == CGFloat(1056))
    }

    @Test func eachAxisSnapsOnItsOwn() {
        let r = DrawerSnap.snap(rect(880, 108), work: work, drawers: [drawer], pieces: [])
        #expect(r.minX == 872)
        #expect(r.minY == 100)   // aligned with the drawer's top edge
    }

    @Test func nearestCandidateWins() {
        let a = rect(500, 100), b = rect(500, 112)
        let r = DrawerSnap.snap(rect(500, 109), work: work, drawers: [a, b], pieces: [])
        #expect(r.minY == 112)
    }

    @Test func optionTurnsSnappingOffButKeepsTheTitleBarOnScreen() {
        let r = DrawerSnap.snap(rect(880, 400), work: work, drawers: [drawer], pieces: [], snapping: false)
        #expect(r.minX == 880)
        let off = DrawerSnap.snap(rect(-40, 5000), work: work, drawers: [], pieces: [], snapping: false)
        #expect(off.minX == 0)
        #expect(off.minY == CGFloat(818))
    }
}

@Suite("Drawer Tidy")
struct DrawerTidyTests {
    private func slots(_ n: Int, corner: DrawerCorner, flow: DrawerFlow, folded: Bool = false, in w: CGRect = work) -> [String: CGRect] {
        let ordered = (0..<n).map { (id: "d\($0)", h: DrawerPlacement.height(folded: folded)) }
        return DrawerTidyPlan.slots(ordered, work: w, corner: corner, flow: flow, blocked: [])
    }

    @Test func columnFromTheTopRight() {
        let s = slots(2, corner: .topRight, flow: .column)
        #expect(s["d0"] == CGRect(x: 1056, y: 24, width: 360, height: 330))
        #expect(s["d1"] == CGRect(x: 1056, y: 366, width: 360, height: 330))
    }

    @Test func columnWrapsTowardTheCentre() {
        // 860 pt of height fits two open drawers; the third starts a new column to the left.
        let s = slots(3, corner: .topRight, flow: .column)
        #expect(s["d2"] == CGRect(x: 1056 - 372, y: 24, width: 360, height: 330))
    }

    @Test func rowFromTheTopLeftRunsRight() {
        let s = slots(2, corner: .topLeft, flow: .row)
        #expect(s["d0"]!.origin == CGPoint(x: 24, y: 24))
        #expect(s["d1"]!.origin == CGPoint(x: 24 + 360 + 12, y: 24))
    }

    @Test func columnFromABottomCornerGrowsUp() {
        let s = slots(2, corner: .bottomLeft, flow: .column, folded: true)
        #expect(s["d0"]!.maxY == CGFloat(836))
        #expect(s["d1"]!.maxY == CGFloat(782))
        #expect(s["d0"]!.minX == 24)
    }

    @Test func rowFromTheBottomRightGrowsLeft() {
        let s = slots(2, corner: .bottomRight, flow: .row)
        #expect(s["d0"]!.maxX == CGFloat(1416))
        #expect(s["d1"]!.maxX == CGFloat(1044))
    }

    @Test func slotsNeverOverlapTheBlockedAreaOrEachOther() {
        let blocked = [CGRect(x: 1000, y: 0, width: 440, height: 400)]
        let ordered = (0..<3).map { (id: "d\($0)", h: CGFloat(330)) }
        let s = DrawerTidyPlan.slots(ordered, work: work, corner: .topRight, flow: .column, blocked: blocked)
        let all = blocked + Array(s.values)
        for (i, a) in all.enumerated() { for b in all[(i + 1)...] { #expect(!DrawerPlacement.overlaps(a, b)) } }
    }

    @Test func aDrawerWithNoRoomIsLeftOutAndKeepsItsPlace() {
        let tiny = CGRect(x: 0, y: 0, width: 420, height: 400)
        let s = slots(2, corner: .topLeft, flow: .column, in: tiny)
        #expect(s["d0"] != nil)
        #expect(s["d1"] == nil)
    }

    @Test func orderIsNearestTheCornerThenId() {
        let cur = ["far": rect(100, 400), "near": rect(1000, 30), "alsoNear": rect(1000, 30)]
        #expect(DrawerTidyPlan.order(cur, work: work, corner: .topRight) == ["alsoNear", "near", "far"])
    }

    @Test func tidyReportsOnlyWhatMovesAndRemembersWhereItWas() {
        let cur = ["a": rect(200, 300), "b": rect(1056, 24)]
        let change = DrawerTidyPlan.tidy(cur, work: work, corner: .topRight, flow: .column)
        // b already sits on the first slot, so only a moves.
        #expect(change.next.keys.sorted() == ["a"])
        #expect(change.previous["a"] == CGPoint(x: 200, y: 300))
        #expect(change.next["a"] == CGPoint(x: 1056, y: 366))
        #expect(change.count == 1)
    }

    @Test func tidyOfAnAlignedSetIsEmpty() {
        let change = DrawerTidyPlan.tidy(["a": rect(1056, 24)], work: work, corner: .topRight, flow: .column)
        #expect(change.isEmpty)
    }

    @Test func undoRestoresPreviousPlaces() {
        // The undo is the change's `previous`: applying it puts every moved drawer back exactly.
        var places = ["a": CGPoint(x: 200, y: 300), "b": CGPoint(x: 700, y: 120)]
        let before = places
        let cur = places.mapValues { CGRect(origin: $0, size: DrawerPlacement.size(folded: false)) }
        let change = DrawerTidyPlan.tidy(cur, work: work, corner: .topLeft, flow: .row)
        for (id, p) in change.next { places[id] = p }
        #expect(places != before)
        for (id, p) in change.previous { places[id] = p }
        #expect(places == before)
    }

    @Test func tidyOutputStaysOnTheScreen() {
        let cur = Dictionary(uniqueKeysWithValues: (0..<6).map { ("d\($0)", rect(CGFloat($0) * 50, 400, folded: true)) })
        let change = DrawerTidyPlan.tidy(cur, work: work, corner: .bottomRight, flow: .column)
        for (_, p) in change.next {
            #expect(DrawerPlacement.clamp(p, size: DrawerPlacement.size(folded: true), work: work.size) == p)
        }
    }
}

@Suite("Drawer look values")
struct DrawerLookValuesTests {
    @Test func solidIsTheSurfaceAtTheOpacity() {
        let v = DrawerLookValues.of(.solid, opacity: 55)
        #expect(v.fillAlpha == 0.55)
        #expect(!v.usesBlur)
        #expect(v.borderAlpha == DrawerLookValues.edge)
    }

    @Test func glassIsABlurAtTheOpacity() {
        let v = DrawerLookValues.of(.glass, opacity: 80)
        #expect(v.usesBlur)
        #expect(v.blurAlpha == 0.8)
        #expect(v.fillAlpha == 0)
    }

    @Test func outlineHasASeventyPercentBorderAndAFaintFill() {
        let v = DrawerLookValues.of(.outline, opacity: 100)
        #expect(v.borderAlpha == 0.7)
        #expect(v.fillAlpha == 0.02)
        #expect(!v.usesBlur)
    }

    @Test func bareShowsItsBorderOnlyOnHover() {
        #expect(DrawerLookValues.of(.bare, opacity: 55, hovering: false).borderAlpha == 0)
        #expect(DrawerLookValues.of(.bare, opacity: 55, hovering: true).borderAlpha == 0.7)
        #expect(DrawerLookValues.of(.bare, opacity: 55).fillAlpha == 0.02)
    }

    @Test func opacityIsClampedTo20Through100() {
        #expect(DrawerLookValues.clampOpacity(5) == 0.2)
        #expect(DrawerLookValues.clampOpacity(250) == 1.0)
        #expect(DrawerLookValues.of(.solid, opacity: 0).fillAlpha == 0.2)
        #expect(DrawerSettings.defaultOpacity == 55)
    }

    @Test func onlySolidAndGlassUseTheOpacity() {
        #expect(DrawerLook.allCases.filter(DrawerLookValues.usesOpacity) == [.solid, .glass])
    }

    @Test func aDrawersOwnLookOverridesTheShared() {
        var s = DrawerSettings()
        s.look = .glass
        s.looks["shots"] = .bare
        #expect(s.lookFor("shots") == .bare)
        #expect(s.lookFor("docs") == .glass)
    }
}

@Suite("Drawer panel words and keys")
struct DrawerPanelWordsTests {
    @Test func titleIsNameAndCount() {
        #expect(DrawerTitle.text(name: "Screenshots", count: 14) == "Screenshots · 14")
    }

    @Test func extensionsFollowTheFinderSetting() {
        #expect(DrawerDisplay.name("Shot.png", isFolder: false, showAllExtensions: true, hiddenExtension: true) == "Shot.png")
        #expect(DrawerDisplay.name("Shot.png", isFolder: false, showAllExtensions: false, hiddenExtension: true) == "Shot")
        #expect(DrawerDisplay.name("Shot.png", isFolder: false, showAllExtensions: false, hiddenExtension: false) == "Shot.png")
        #expect(DrawerDisplay.name("v1.2", isFolder: true, showAllExtensions: false, hiddenExtension: true) == "v1.2")
    }

    @Test func keysMapToTheirActions() {
        #expect(DrawerKeyAction.of(keyCode: 36, command: false, characters: "\r") == .open)
        #expect(DrawerKeyAction.of(keyCode: 76, command: false, characters: nil) == .open)
        #expect(DrawerKeyAction.of(keyCode: 49, command: false, characters: " ") == .quickLook)
        #expect(DrawerKeyAction.of(keyCode: 15, command: true, characters: "r") == .reveal)
        #expect(DrawerKeyAction.of(keyCode: 51, command: true, characters: nil) == .trash)
        // A bare Delete never trashes; only the command does.
        #expect(DrawerKeyAction.of(keyCode: 51, command: false, characters: nil) == nil)
        #expect(DrawerKeyAction.of(keyCode: 0, command: false, characters: "a") == nil)
    }

    @Test func thumbnailsAreKeyedByPathAndDate() {
        let d1 = Date(timeIntervalSince1970: 100), d2 = Date(timeIntervalSince1970: 200)
        #expect(DrawerThumbnails.key(path: "/x/a.png", modified: d1) == DrawerThumbnails.key(path: "/x/a.png", modified: d1))
        #expect(DrawerThumbnails.key(path: "/x/a.png", modified: d1) != DrawerThumbnails.key(path: "/x/a.png", modified: d2))
        #expect(DrawerThumbnails.key(path: "/x/a.png", modified: d1) != DrawerThumbnails.key(path: "/x/b.png", modified: d1))
    }

    @Test func foldedTitleIs42Tall() {
        #expect(DrawerPlacement.foldedHeight == 42)
    }
}

@Suite("Drawer review model")
struct DrawerReviewModelTests {
    private func review() -> DrawerReview {
        let shots = DrawerDef(id: "screenshots", name: "Screenshots", home: "/r/Screenshots", owned: true)
        let docs = DrawerDef(id: "documents", name: "Documents", home: "/r/Documents", owned: true)
        let moves = ["a", "b", "c", "d", "e"].map { DrawerMove(source: "/d/\($0).png", drawerId: "screenshots") }
            + [DrawerMove(source: "/d/r.pdf", drawerId: "documents")]
        return DrawerReview(plan: DrawerPlan(source: .rules, drawers: [shots, docs], moves: moves), planned: [:])
    }

    @Test func titleCountsFilesAndDrawers() {
        let m = DrawerReviewModel(review: review(), existing: [])
        #expect(m.title == "6 files into 2 drawers")
        #expect(DrawerReviewModel.title(files: 1, drawers: 1) == "1 file into 1 drawer")
    }

    @Test func aLineNamesUpToThreeFiles() {
        let m = DrawerReviewModel(review: review(), existing: [])
        #expect(m.lines[0].names == ["a.png", "b.png", "c.png"])
        #expect(m.lines[0].text == "Screenshots (new): 5 files, including a.png, b.png and c.png")
        #expect(m.lines[1].text == "Documents (new): 1 file, including r.pdf")
        #expect(DrawerReviewModel.including(["x", "y"]) == ", including x and y")
    }

    @Test func existingDrawersAreNotNew() {
        let have = [DrawerDef(id: "documents", name: "Documents", home: "/r/Documents", owned: true)]
        let m = DrawerReviewModel(review: review(), existing: have)
        #expect(m.lines[0].isNew)
        #expect(!m.lines[1].isNew)
        #expect(m.lines[1].text == "Documents: 1 file, including r.pdf")
    }

    @Test func untickingDropsFilesFromTheCountsAndTheNames() {
        var edit = DrawerReviewEdit()
        edit.toggle("/d/a.png")
        let m = DrawerReviewModel(review: review(), existing: [], edit: edit)
        #expect(m.fileCount == 5)
        #expect(m.lines[0].count == 4)
        #expect(m.lines[0].names == ["b.png", "c.png", "d.png"])
        // The edit list still shows the unticked file, so it can be ticked again.
        #expect(m.lines[0].files.count == 5)
        #expect(m.lines[0].files[0].ticked == false)
        edit.toggle("/d/a.png")
        #expect(DrawerReviewModel(review: review(), existing: [], edit: edit).fileCount == 6)
    }

    @Test func aDrawerWithNothingTickedLeavesTheSummary() {
        var edit = DrawerReviewEdit()
        edit.toggle("/d/r.pdf")
        let m = DrawerReviewModel(review: review(), existing: [], edit: edit)
        #expect(m.drawerCount == 1)
        #expect(m.summary.map(\.id) == ["screenshots"])
        #expect(m.title == "5 files into 1 drawer")
    }

    @Test func nothingTickedCannotApply() {
        var edit = DrawerReviewEdit()
        for f in ["a", "b", "c", "d", "e"] { edit.toggle("/d/\(f).png") }
        edit.toggle("/d/r.pdf")
        #expect(!DrawerReviewModel(review: review(), existing: [], edit: edit).canApply)
        #expect(DrawerReviewModel(review: review(), existing: []).canApply)
    }

    @Test func renameValidationUsesDrawerNames() {
        let r = review()
        var edit = DrawerReviewEdit()
        edit.renames["screenshots"] = "Shots"
        #expect(DrawerReviewModel(review: r, existing: [], edit: edit).lines[0].name == "Shots")
        #expect(DrawerReviewModel(review: r, existing: [], edit: edit).canApply)
        for bad in ["", "a/b", "a:b", ".hidden", String(repeating: "x", count: 41)] {
            edit.renames["screenshots"] = bad
            let m = DrawerReviewModel(review: r, existing: [], edit: edit)
            #expect(m.lines[0].nameError != nil, "\(bad)")
            #expect(!m.canApply)
        }
    }

    @Test func renameCannotTakeAnotherDrawersNameIgnoringCase() {
        let r = review()
        var edit = DrawerReviewEdit()
        edit.renames["screenshots"] = "documents"
        #expect(DrawerReviewModel(review: r, existing: [], edit: edit).lines[0].nameError != nil)
        // Nor an existing drawer's.
        let have = [DrawerDef(id: "images", name: "Pictures", home: "/r/Pictures", owned: true)]
        edit.renames["screenshots"] = "pictures"
        #expect(DrawerReviewModel(review: r, existing: have, edit: edit).lines[0].nameError != nil)
        // Its own current name is fine.
        edit.renames["screenshots"] = "Screenshots"
        #expect(DrawerReviewModel(review: r, existing: [], edit: edit).lines[0].nameError == nil)
    }

    @Test func existingDrawersCannotBeRenamedHere() {
        let have = [DrawerDef(id: "documents", name: "Documents", home: "/r/Documents", owned: true)]
        var edit = DrawerReviewEdit()
        edit.renames["documents"] = "Papers"
        let m = DrawerReviewModel(review: review(), existing: have, edit: edit)
        #expect(m.lines[1].name == "Documents")
        let plan = DrawerReviewModel.editedPlan(review(), existing: have, edit: edit, root: "/r")
        #expect(plan.drawers.first { $0.id == "documents" }?.name == "Documents")
    }

    @Test func theEditedPlanDropsUntickedMovesAndEmptyDrawersAndRenamesNewOnes() {
        var edit = DrawerReviewEdit()
        edit.toggle("/d/r.pdf")
        edit.toggle("/d/a.png")
        edit.renames["screenshots"] = "Shots"
        let plan = DrawerReviewModel.editedPlan(review(), existing: [], edit: edit, root: "/r")
        #expect(plan.moves.count == 4)
        #expect(plan.drawers.map(\.id) == ["screenshots"])
        #expect(plan.drawers[0].name == "Shots")
        #expect(plan.drawers[0].home == "/r/Shots")
    }

    @Test func anInvalidRenameIsIgnoredByTheEditedPlan() {
        var edit = DrawerReviewEdit()
        edit.renames["screenshots"] = "a/b"
        let plan = DrawerReviewModel.editedPlan(review(), existing: [], edit: edit, root: "/r")
        #expect(plan.drawers[0].name == "Screenshots")
    }
}

@Suite("Drawer result model")
struct DrawerResultModelTests {
    @Test func plainResultHasUndoWhenSomethingMoved() {
        var r = DrawerRunResult()
        r.entryId = "run-1"
        r.moved = [DrawerMoved(from: "/d/a", to: "/r/a", isFolder: false)]
        let m = DrawerResultModel(r)
        #expect(m.line == "1 moved.")
        #expect(m.undo == .run("run-1"))
    }

    @Test func skipsNameThreeWithTheirReasonsThenCountTheRest() {
        var r = DrawerRunResult()
        r.entryId = "run-1"
        r.moved = [DrawerMoved(from: "/d/a", to: "/r/a", isFolder: false)]
        r.skipped = (1...5).map { UndoSkip(path: "/d/f\($0).txt", reason: DrawerSkipReason.changed) }
        let m = DrawerResultModel(r)
        #expect(m.line.hasPrefix("1 moved, 5 skipped: f1.txt changed since the review. f2.txt"))
        #expect(m.line.hasSuffix("And 2 more."))
    }

    @Test func aRefusalSaysWhyAndHasNoUndo() {
        var r = DrawerRunResult()
        r.refusal = "The plan was refused."
        let m = DrawerResultModel(r)
        #expect(m.line == "The plan was refused.")
        #expect(!m.canUndo)
    }

    @Test func tidyResultAndItsUndo() {
        #expect(DrawerResultModel(tidied: 3).line == "3 drawers lined up.")
        #expect(DrawerResultModel(tidied: 1).line == "1 drawer lined up.")
        #expect(DrawerResultModel(tidied: 3).undo == .tidy)
        #expect(DrawerResultModel(tidied: 0).line == "The drawers are already lined up.")
        #expect(!DrawerResultModel(tidied: 0).canUndo)
    }

    @Test func anUndoneResultOffersNothingFurther() {
        var r = DrawerRunResult()
        r.entryId = "x"
        r.moved = [DrawerMoved(from: "/r/a", to: "/d/a", isFolder: false)]
        #expect(!DrawerResultModel(undone: r).canUndo)
    }

    @Test func aHandMoveShowsABarOnlyWhenSomethingWasLeft() {
        var ok = DrawerRunResult()
        ok.moved = [DrawerMoved(from: "/d/a", to: "/r/a", isFolder: false)]
        #expect(!DrawerResultModel.worthShowing(ok))
        var skip = ok
        skip.skipped = [UndoSkip(path: "/d/b", reason: DrawerSkipReason.link)]
        #expect(DrawerResultModel.worthShowing(skip))
    }
}

@Suite("Drawer item guard and trash")
struct DrawerTrashTests {
    private func setUp() -> (DrawerFixture, [DrawerDef]) {
        let fx = DrawerFixture()
        let fm = FileManager.default
        for n in ["Screenshots", "Documents"] { try! fm.createDirectory(atPath: "\(fx.root)/\(n)", withIntermediateDirectories: true) }
        try! Data("x".utf8).write(to: URL(fileURLWithPath: "\(fx.root)/Screenshots/a.png"))
        try! Data("y".utf8).write(to: URL(fileURLWithPath: "\(fx.desktop)/loose.txt"))
        try! fm.createDirectory(atPath: "\(fx.root)/Screenshots/sub", withIntermediateDirectories: true)
        try! Data("z".utf8).write(to: URL(fileURLWithPath: "\(fx.root)/Screenshots/sub/deep.txt"))
        let drawers = [DrawerDef(id: "screenshots", name: "Screenshots", home: "\(fx.root)/Screenshots", owned: true),
                       DrawerDef(id: "documents", name: "Documents", home: "\(fx.root)/Documents", owned: true)]
        return (fx, drawers)
    }

    @Test func anItemInsideADrawerIsTrashedThroughTheTrasher() {
        let (fx, drawers) = setUp()
        var got: [URL] = []
        let why = DrawerTrash.trash("\(fx.root)/Screenshots/a.png", drawers: drawers) { got.append($0) }
        #expect(why == nil)
        #expect(got.map(\.lastPathComponent) == ["a.png"])
    }

    @Test func aViewDrawersItemCountsToo() {
        let (fx, _) = setUp()
        let view = [DrawerDef(id: "elsewhere", name: "Elsewhere", home: "\(fx.root)/Documents", owned: false)]
        try! Data("v".utf8).write(to: URL(fileURLWithPath: "\(fx.root)/Documents/v.txt"))
        var got = 0
        #expect(DrawerTrash.trash("\(fx.root)/Documents/v.txt", drawers: view) { _ in got += 1 } == nil)
        #expect(got == 1)
    }

    @Test func nothingOutsideADrawerIsEverTrashed() {
        let (fx, drawers) = setUp()
        var got = 0
        let t: (URL) throws -> Void = { _ in got += 1 }
        // A Desktop file, the drawer's own folder, the root, a path two levels down, a missing item, a relative path.
        for p in ["\(fx.desktop)/loose.txt", "\(fx.root)/Screenshots", fx.root, "\(fx.root)/Screenshots/sub/deep.txt",
                  "\(fx.root)/Screenshots/missing.png", "a.png", "/"] {
            #expect(DrawerTrash.trash(p, drawers: drawers, using: t) != nil, "\(p)")
        }
        #expect(got == 0)
    }

    @Test func dotDotCannotReachOutOfADrawer() {
        let (fx, drawers) = setUp()
        var got = 0
        #expect(DrawerTrash.trash("\(fx.root)/Screenshots/../../loose.txt", drawers: drawers) { _ in got += 1 } != nil)
        #expect(got == 0)
    }

    @Test func aSymlinkInADrawerIsTheLinkNotItsTarget() throws {
        let (fx, drawers) = setUp()
        try FileManager.default.createSymbolicLink(atPath: "\(fx.root)/Screenshots/link", withDestinationPath: "\(fx.desktop)/loose.txt")
        var got: [URL] = []
        #expect(DrawerTrash.trash("\(fx.root)/Screenshots/link", drawers: drawers) { got.append($0) } == nil)
        #expect(got.map(\.lastPathComponent) == ["link"])
        #expect(FileManager.default.fileExists(atPath: "\(fx.desktop)/loose.txt"))
    }

    @Test func aFailingTrashSaysSoAndLeavesTheFile() {
        let (fx, drawers) = setUp()
        struct Boom: Error {}
        #expect(DrawerTrash.trash("\(fx.root)/Screenshots/a.png", drawers: drawers) { _ in throw Boom() } != nil)
        #expect(FileManager.default.fileExists(atPath: "\(fx.root)/Screenshots/a.png"))
    }
}

@MainActor
@Suite("Drawer item rename and review edit through the service")
struct DrawerServiceItemTests {
    private func rig() -> (DrawerFixture, DrawerService, DrawerScratchDefaults) {
        let fx = DrawerFixture()
        let scratch = DrawerScratchDefaults()
        var s = DrawerSettings()
        s.enabled = true
        let fm = FileManager.default
        try! fm.createDirectory(atPath: "\(fx.root)/Screenshots", withIntermediateDirectories: true)
        s.drawers = [DrawerDef(id: "screenshots", name: "Screenshots", home: "\(fx.root)/Screenshots", owned: true)]
        DrawerPrefs.save(s, to: scratch.defaults)
        let service = DrawerService(defaults: scratch.defaults, desktop: fx.desktop, home: fx.home, store: fx.store, turnDeskOn: {})
        return (fx, service, scratch)
    }

    @Test func renamingAnItemNeverReplacesAnotherAndStaysInItsFolder() throws {
        let (fx, service, scratch) = rig()
        _ = scratch
        let a = "\(fx.root)/Screenshots/a.png", b = "\(fx.root)/Screenshots/b.png"
        try Data("a".utf8).write(to: URL(fileURLWithPath: a))
        try Data("b".utf8).write(to: URL(fileURLWithPath: b))
        #expect(service.renameItem(a, to: "b.png") != nil)
        #expect(try String(contentsOfFile: b, encoding: .utf8) == "b")
        #expect(service.renameItem(a, to: "../x.png") != nil)
        #expect(service.renameItem(a, to: "") != nil)
        #expect(service.renameItem(a, to: "c.png") == nil)
        #expect(FileManager.default.fileExists(atPath: "\(fx.root)/Screenshots/c.png"))
        // Outside a drawer: refused.
        try Data("l".utf8).write(to: URL(fileURLWithPath: "\(fx.desktop)/loose.txt"))
        #expect(service.renameItem("\(fx.desktop)/loose.txt", to: "m.txt") != nil)
    }

    @Test func serviceTrashRefusesAnythingOutsideADrawer() throws {
        let (fx, service, scratch) = rig()
        _ = scratch
        try Data("l".utf8).write(to: URL(fileURLWithPath: "\(fx.desktop)/loose.txt"))
        #expect(service.trashItem("\(fx.desktop)/loose.txt") != nil)
        #expect(FileManager.default.fileExists(atPath: "\(fx.desktop)/loose.txt"))
    }

    @Test func replacingThePendingReviewKeepsWhatTheReviewShowed() {
        let (_, service, scratch) = rig()
        _ = scratch
        #expect(service.pending == nil)
        service.replacePending(DrawerPlan(source: .rules, drawers: [], moves: []))
        #expect(service.pending == nil)
    }
}
