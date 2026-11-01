import CoreGraphics
import Foundation
import QuartzCore
import Testing
@testable import Sanduhr

/// The companion piece on the Desk (spec 4): its layout word, when it shows, the keyframes the render
/// server steps, the roam's y-flip, the demo and state.yaml. Every folder is a temp one.
@Suite("Companion piece")
struct CompanionPieceTests {
    private func store(installing ids: [(group: String, name: String)] = [("packs", "hourglass")]) -> (WidgetStore, WidgetTestRoot) {
        let root = WidgetTestRoot()
        let store = root.store()
        for id in ids {
            let (files, manifest) = StoreFixtures.valid(id.group, id.name)
            #expect(store.install(files, manifest, source: .catalog).ok)
        }
        return (store, root)
    }

    // MARK: Placement

    @Test func theLayoutWordRoundTripsWithTheCompanion() {
        let a = DeskArrangement("clock:bl companion:bc:1.4 message:tl")
        #expect(a.placement("companion") == DeskPlacement(widget: "companion", anchor: .bc, scale: 1.4))
        #expect(a.string == "clock:bl companion:bc:1.4 message:tl")
        #expect(DeskArrangement("companion:mr").string == "companion:mr")
        #expect(DeskLayout.placed("companion:bc").contains("companion"))
    }

    @Test func theCompanionDefaultsToBottomCenterAndIsHiddenInTheStandardLayout() {
        #expect(DeskArrangement.defaultAnchor("companion") == .bc)
        #expect(DeskArrangement("companion:zz").placement("companion")?.anchor == .bc)
        var a = DeskArrangement(DeskLayout.standard)
        #expect(a.hidden.contains("companion"))
        a.show("companion")
        #expect(a.placement("companion") == DeskPlacement(widget: "companion", anchor: .bc, scale: 1))
    }

    @Test func everyDeskWidgetHasAPieceAndAName() {
        #expect(DeskView.Widget.allCases.map(\.rawValue).sorted() == DeskLayout.widgets.map(\.key).sorted())
        #expect(DeskLayout.widgets.contains { $0.key == "companion" && $0.name == "Companion" })
    }

    // MARK: Shown only with a sprite

    @Test func nothingShowsWithNoSpritePackInstalled() {
        let (empty, root1) = store(installing: [])
        defer { withExtendedLifetime(root1) {} }
        #expect(CompanionPicker.pick(store: empty, settings: CompanionSettings(), demo: false) == nil)
        let (themes, root2) = store(installing: [("good", "good-theme")])
        defer { withExtendedLifetime(root2) {} }
        #expect(CompanionPicker.pick(store: themes, settings: CompanionSettings(pack: "good-theme"), demo: false) == nil)
    }

    @Test func aSpritePackShowsAndTheChoiceFallsBackToTheFirst() {
        let (s, root3) = store()
        defer { withExtendedLifetime(root3) {} }
        let first = CompanionPicker.pick(store: s, settings: CompanionSettings(), demo: false)
        #expect(first?.packId == "hourglass")
        #expect(CompanionPicker.pick(store: s, settings: CompanionSettings(pack: "not-installed"), demo: false)?.packId == "hourglass")
    }

    // MARK: Demo

    @Test func demoShowsTheHourglassOnlyAndNeverTheChoice() {
        let (none, root4) = store(installing: [])
        defer { withExtendedLifetime(root4) {} }
        #expect(CompanionPicker.pick(store: none, settings: CompanionSettings(), demo: true) == nil)
        let (s, root5) = store()
        defer { withExtendedLifetime(root5) {} }
        let pick = CompanionPicker.pick(store: s, settings: CompanionSettings(pack: "other-pack"), demo: true)
        #expect(pick?.packId == CompanionPicker.demoPackId)
        let stage = pick!.sprite.stage(at: CompanionDemo.growth)
        #expect(stage != pick!.sprite.stages.first)
    }

    @Test func demoMoodsTakeTurnsSlowly() {
        #expect(CompanionDemo.mood(secondsIn: 0) == .content)
        #expect(CompanionDemo.mood(secondsIn: CompanionDemo.moodSeconds - 0.1) == .content)
        #expect(CompanionDemo.mood(secondsIn: CompanionDemo.moodSeconds) == .focused)
        #expect(Set((0..<5).map { CompanionDemo.mood(secondsIn: Double($0) * CompanionDemo.moodSeconds) }) == Set(CompanionMood.allCases))
        #expect(CompanionDemo.mood(secondsIn: 5 * CompanionDemo.moodSeconds) == .content)
        #expect(CompanionDemo.mood(secondsIn: -3) == .content)
    }

    // MARK: state.yaml

    @Test func theDebugFieldIsIdsAndEnumsOnly() {
        #expect(CompanionDebug.yaml(nil) == .null)
        var s = DebugStateInput()
        #expect(YAMLEmitter.emit(DebugState.yaml(s)).contains("companion: null\n"))
        s.companion = CompanionDebug(pack: "hourglass", stage: "sand", mood: "worried", roaming: true)
        let yaml = YAMLEmitter.emit(DebugState.yaml(s))
        #expect(yaml.contains("companion:\n  pack: hourglass\n  stage: sand\n  mood: worried\n  roaming: true\n"))
    }

    // MARK: Keyframes

    @Test func theKeyframesAreTheSheetsCellsFromTheBottom() throws {
        let sprite = CompanionTestKit.fixture("packs", "hourglass")
        let stage = sprite.stages[0]
        let frames = sprite.frames(CompanionSprite.walk)
        #expect(!frames.isEmpty)
        // A sheet with two rows of cells, enough for every frame.
        let columns = (sprite.maxFrame + 2) / 2
        let w = stage.frameWidth * columns, h = stage.frameHeight * 2
        let rects = CompanionFrames.rects(frames, sheetWidth: w, sheetHeight: h, frameWidth: stage.frameWidth, frameHeight: stage.frameHeight)
        #expect(rects.count == frames.count)
        for (rect, i) in zip(rects, frames) {
            let expected = CompanionSheet.contentsRect(i, sheetWidth: w, sheetHeight: h, frameWidth: stage.frameWidth,
                                                       frameHeight: stage.frameHeight, originAtBottom: true)
            #expect(rect == expected)
        }
        // Frame 0 is the top-left cell, so counted from the bottom its y is the upper half.
        let first = CompanionFrames.rects([0], sheetWidth: w, sheetHeight: h, frameWidth: stage.frameWidth, frameHeight: stage.frameHeight)[0]
        #expect(first.minY == 0.5 && first.minX == 0)

        let loop = CompanionFrames.animation(rects, fps: 8, repeatForever: true)
        #expect(loop.calculationMode == .discrete && loop.keyPath == "contentsRect")
        #expect(loop.values?.count == rects.count)
        #expect(loop.repeatCount == .infinity)
        #expect(abs(loop.duration - Double(rects.count) / 8) < 1e-9)
        let shot = CompanionFrames.animation(rects, fps: 8, repeatForever: false)
        #expect(shot.repeatCount == 1)
    }

    @Test func framesPastTheSheetAreDropped() {
        let rects = CompanionFrames.rects([0, 1, 99], sheetWidth: 64, sheetHeight: 32, frameWidth: 32, frameHeight: 32)
        #expect(rects.count == 2)
    }

    // MARK: Roam

    @Test func theRoamFlipsFromYDownWindowPointsToTheLayersYUp() {
        let spot = CompanionSpot(x: 400, y: 700, facingLeft: true, edge: .bottom)
        // A view 96 wide whose bottom-left sits at (352, 40) in the 800-high window (y-up).
        let frame = CGRect(x: 352, y: 40, width: 96, height: 96)
        let p = CompanionRoamMath.layerPoint(spot, windowHeight: 800, viewFrame: frame)
        #expect(p == CGPoint(x: 48, y: 60))
        // The view's own bottom-centre maps back to window points y-down, and onto the same spot.
        let home = CompanionRoamMath.windowPoint(viewFrame: frame, windowHeight: 800)
        #expect(home == CGPoint(x: 400, y: 760))
        let atHome = CompanionRoamMath.layerPoint(CompanionSpot(x: home.x, y: home.y, facingLeft: false, edge: .bottom),
                                                  windowHeight: 800, viewFrame: frame)
        #expect(atHome == CGPoint(x: 48, y: 0))
        #expect(CompanionRoamMath.xScale(facingLeft: true) == -1 && CompanionRoamMath.xScale(facingLeft: false) == 1)
    }

    @Test func aWalkIsSampledAlongTheRouteAndMirroredWhereItTurns() throws {
        let area = CompanionRoamMath.area(window: CGSize(width: 1000, height: 700), topInset: 30, dock: DockInsets.on(.bottom, 70))
        #expect(area == CGRect(x: 0, y: 30, width: 1000, height: 600))
        let path = try #require(CompanionPath.build(edges: [.bottom], area: area, spriteWidth: 100, spriteHeight: 100))
        // Start 40 points from the far end and walk 100: out to the end, then back.
        let from = path.length - 40
        let spots = CompanionRoamMath.samples(path, from: from, to: from + 100, count: 10)
        #expect(spots.count == 11)
        #expect(spots.allSatisfy { $0.y == area.maxY })
        #expect(spots.first?.facingLeft == false && spots.last?.facingLeft == true)
        let move = CompanionRoamMath.positionAnimation(spots, windowHeight: 700, viewFrame: CGRect(x: 0, y: 0, width: 100, height: 100),
                                                       duration: 12)
        #expect(move.keyPath == "position" && move.values?.count == 11 && move.duration == 12)
        let flip = CompanionRoamMath.flipAnimation(spots, duration: 12)
        #expect(flip.calculationMode == .discrete && flip.values?.count == 11)
    }

    @Test func theRoamerReportsTheSecondsLeftInItsCycle() {
        var r = CompanionRoamer()
        #expect(r.secondsLeftInPhase == 12)
        r.advance(1, allowed: true)
        #expect(r.secondsLeftInPhase == 11)
        for _ in 0..<11 { r.advance(1, allowed: true) }
        #expect(!r.walking && r.secondsLeftInPhase == 8)
    }

    @Test func theDriverStopsOnlyWhenTheLastPieceViewGoes() {
        // Moving the piece: the new view appears before the old one disappears.
        var views = CompanionPieceViews()
        views.appeared()
        views.appeared()
        let first = views.disappeared()
        #expect(!first && views.count == 1)
        let last = views.disappeared()
        #expect(last)
        // An extra disappear (the Desk stopping first) never goes below zero.
        let extra = views.disappeared()
        #expect(extra && views.count == 0)
    }
}
