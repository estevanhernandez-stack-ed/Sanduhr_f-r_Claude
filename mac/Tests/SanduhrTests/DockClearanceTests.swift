import Foundation
import Testing
@testable import Sanduhr

/// Item 56: Desk items move clear of the Dock. The Dock's settings, the always-shown Dock's
/// inset, which corners move, the auto-hiding Dock's extent from the window list, the slide and
/// when the watch reads.

@Suite("Dock settings")
struct DockPrefsTests {
    @Test func noKeysMeansBottomShownAndTheDefaults() {
        let p = DockPrefs.read { _ in nil }
        #expect(p == DockPrefs())
        #expect(p.side == .bottom)
        #expect(!p.autohide)
        #expect(p.tilesize == DockPrefs.defaultTileSize)
        #expect(p.delay == DockPrefs.defaultDelay)
    }

    @Test func readsSideAutohideTileSizeAndDelay() {
        let values: [String: Any] = ["orientation": "left", "autohide": NSNumber(value: 1),
                                     "tilesize": NSNumber(value: 51), "autohide-delay": NSNumber(value: 0)]
        let p = DockPrefs.read { values[$0] }
        #expect(p.side == .left)
        #expect(p.autohide)
        #expect(p.tilesize == 51)
        #expect(p.delay == 0)
    }

    @Test func orientationWords() {
        #expect(DockSide(orientation: "right") == .right)
        #expect(DockSide(orientation: "bottom") == .bottom)
        #expect(DockSide(orientation: "sideways") == .bottom)
        #expect(DockSide(orientation: nil) == .bottom)
    }

    @Test func nonsenseValuesFallBack() {
        let values: [String: Any] = ["tilesize": NSNumber(value: 0), "autohide-delay": NSNumber(value: 60), "autohide": "yes"]
        let p = DockPrefs.read { values[$0] }
        #expect(p.tilesize == DockPrefs.defaultTileSize)
        #expect(p.delay == 5)
        #expect(!p.autohide)
    }
}

@Suite("Dock inset")
struct DockInsetTests {
    let frame = CGRect(x: 0, y: 0, width: 1800, height: 1169)

    @Test func theMenuBarNeverCounts() {
        let visible = CGRect(x: 0, y: 0, width: 1800, height: 1130)
        #expect(DockGeometry.reserved(frame: frame, visible: visible) == DockInsets())
    }

    @Test func aBottomDockReservesItsHeight() {
        let visible = CGRect(x: 0, y: 70, width: 1800, height: 1060)
        let prefs = DockPrefs()
        #expect(DockGeometry.alwaysShown(prefs, frame: frame, visible: visible) == DockInsets(bottom: 70))
    }

    @Test func sideDocksReserveTheirWidth() {
        var prefs = DockPrefs()
        prefs.side = .left
        let left = CGRect(x: 64, y: 0, width: 1736, height: 1130)
        #expect(DockGeometry.alwaysShown(prefs, frame: frame, visible: left) == DockInsets(left: 64))
        prefs.side = .right
        let right = CGRect(x: 0, y: 0, width: 1736, height: 1130)
        #expect(DockGeometry.alwaysShown(prefs, frame: frame, visible: right) == DockInsets(right: 64))
    }

    @Test func aSecondScreenUsesItsOwnOrigin() {
        let second = CGRect(x: 1800, y: -200, width: 2560, height: 1440)
        let visible = CGRect(x: 1800, y: -120, width: 2560, height: 1335)
        #expect(DockGeometry.reserved(frame: second, visible: visible) == DockInsets(bottom: 80))
    }

    @Test func aDockOnAnotherScreenReservesNothingHere() {
        let visible = CGRect(x: 0, y: 0, width: 1800, height: 1130)
        #expect(DockGeometry.alwaysShown(DockPrefs(), frame: frame, visible: visible) == DockInsets())
    }

    @Test func onlyTheDocksSideCounts() {
        // A stray reservation on another side (another tool's, a rounding) moves nothing.
        let visible = CGRect(x: 3, y: 70, width: 1797, height: 1060)
        #expect(DockGeometry.alwaysShown(DockPrefs(), frame: frame, visible: visible) == DockInsets(bottom: 70))
    }

    @Test func anAutoHidingDockReservesNothingThatCounts() {
        var prefs = DockPrefs()
        prefs.autohide = true
        let visible = CGRect(x: 0, y: 4, width: 1800, height: 1126)
        #expect(DockGeometry.alwaysShown(prefs, frame: frame, visible: visible) == DockInsets())
    }

    @Test func cornersForEachSide() {
        #expect(DockGeometry.corners(for: .bottom) == [.bl, .br])
        #expect(DockGeometry.corners(for: .left) == [.tl, .bl])
        #expect(DockGeometry.corners(for: .right) == [.tr, .br])
    }

    @Test func insetsBySide() {
        for side in DockSide.allCases {
            let i = DockInsets.on(side, 42)
            #expect(i.amount(on: side) == 42)
            #expect(DockSide.allCases.filter { $0 != side }.allSatisfy { i.amount(on: $0) == 0 })
        }
    }

    @Test func theEstimateCoversTheTilesAndAMeasureWins() {
        for size: CGFloat in [16, 32, 48, 51, 64, 128] {
            let t = DockGeometry.estimatedThickness(tilesize: size)
            #expect(t >= size + 20)
            #expect(t == t.rounded())
        }
        #expect(DockGeometry.estimatedThickness(tilesize: 64) > DockGeometry.estimatedThickness(tilesize: 48))
        #expect(DockGeometry.estimatedThickness(tilesize: 51, measured: 66) == 66)
        #expect(DockGeometry.estimatedThickness(tilesize: 51, measured: 0) == DockGeometry.estimatedThickness(tilesize: 51))
    }
}

@Suite("Dock window list")
struct DockExtentTests {
    let size = CGSize(width: 1800, height: 1169)
    let screen = CGRect(x: 0, y: 0, width: 1800, height: 1169)
    let layer = 20

    @Test func triggerZoneIsTheEdgeAFewPointsDeep() {
        #expect(DockGeometry.inTriggerZone(CGPoint(x: 900, y: 1168), size: size, side: .bottom, shownExtent: 0))
        #expect(DockGeometry.inTriggerZone(CGPoint(x: 900, y: 1163), size: size, side: .bottom, shownExtent: 0))
        #expect(!DockGeometry.inTriggerZone(CGPoint(x: 900, y: 1160), size: size, side: .bottom, shownExtent: 0))
        #expect(DockGeometry.inTriggerZone(CGPoint(x: 2, y: 500), size: size, side: .left, shownExtent: 0))
        #expect(!DockGeometry.inTriggerZone(CGPoint(x: 2, y: 500), size: size, side: .right, shownExtent: 0))
        #expect(DockGeometry.inTriggerZone(CGPoint(x: 1799, y: 500), size: size, side: .right, shownExtent: 0))
    }

    @Test func aShownDockWidensTheZoneToItsExtent() {
        let over = CGPoint(x: 900, y: 1169 - 70)
        #expect(!DockGeometry.inTriggerZone(over, size: size, side: .bottom, shownExtent: 0))
        #expect(DockGeometry.inTriggerZone(over, size: size, side: .bottom, shownExtent: 72))
        #expect(!DockGeometry.inTriggerZone(CGPoint(x: 900, y: 1169 - 72 - 13), size: size, side: .bottom, shownExtent: 72))
    }

    @Test func aPointerOnAnotherScreenIsNeverInTheZone() {
        #expect(!DockGeometry.inTriggerZone(CGPoint(x: 2400, y: 1168), size: size, side: .bottom, shownExtent: 0))
        #expect(!DockGeometry.inTriggerZone(CGPoint(x: 900, y: 1300), size: size, side: .bottom, shownExtent: 0))
    }

    @Test func aStripGivesItsOwnDepth() {
        let strip = DockGeometry.DockWindow(layer: layer, bounds: CGRect(x: 300, y: 1169 - 66, width: 1200, height: 66), onScreen: true)
        #expect(DockGeometry.shownExtent([strip], layer: layer, screen: screen, side: .bottom, fallback: 99) == 66)
        let left = DockGeometry.DockWindow(layer: layer, bounds: CGRect(x: 0, y: 200, width: 70, height: 700), onScreen: true)
        #expect(DockGeometry.shownExtent([left], layer: layer, screen: screen, side: .left, fallback: 99) == 70)
        let right = DockGeometry.DockWindow(layer: layer, bounds: CGRect(x: 1730, y: 200, width: 70, height: 700), onScreen: true)
        #expect(DockGeometry.shownExtent([right], layer: layer, screen: screen, side: .right, fallback: 99) == 70)
    }

    @Test func aFullScreenDockWindowSaysShownAndTheFallbackGivesTheDepth() {
        let full = DockGeometry.DockWindow(layer: layer, bounds: screen, onScreen: true)
        #expect(DockGeometry.shownExtent([full], layer: layer, screen: screen, side: .bottom, fallback: 73) == 73)
    }

    @Test func noDockHereIsNil() {
        let off = DockGeometry.DockWindow(layer: layer, bounds: screen, onScreen: false)
        let wallpaper = DockGeometry.DockWindow(layer: -2147483624, bounds: screen, onScreen: true)
        let elsewhere = DockGeometry.DockWindow(layer: layer, bounds: CGRect(x: 1800, y: 1300, width: 2560, height: 80), onScreen: true)
        #expect(DockGeometry.shownExtent([], layer: layer, screen: screen, side: .bottom, fallback: 73) == nil)
        #expect(DockGeometry.shownExtent([off, wallpaper, elsewhere], layer: layer, screen: screen, side: .bottom, fallback: 73) == nil)
    }

    @Test func aSecondScreenInCoreGraphicsCoordinates() {
        // A screen to the right of the primary, its top 200 points lower.
        let second = CGRect(x: 1800, y: 200, width: 2560, height: 1440)
        let strip = DockGeometry.DockWindow(layer: layer, bounds: CGRect(x: 2500, y: 200 + 1440 - 80, width: 1000, height: 80), onScreen: true)
        #expect(DockGeometry.shownExtent([strip], layer: layer, screen: second, side: .bottom, fallback: 99) == 80)
        #expect(DockGeometry.shownExtent([strip], layer: layer, screen: screen, side: .bottom, fallback: 99) == nil)
    }
}

@Suite("Dock slide")
struct DockSlideTests {
    @Test func comesUpSettlesGoesAndSettlesBack() {
        var s = DockSlide()
        #expect(s.phase == .hidden && s.inset == 0)
        let slid1 = s.observe(nil)
        #expect(!slid1)
        let slid2 = s.observe(72)
        #expect(slid2)
        #expect(s.phase == .showing && s.inset == 72)
        s.settle()
        #expect(s.phase == .shown && s.inset == 72)
        let slid3 = s.observe(72)
        #expect(!slid3)
        let slid4 = s.observe(nil)
        #expect(slid4)
        #expect(s.phase == .hiding && s.inset == 0)
        s.settle()
        #expect(s.phase == .hidden && s.inset == 0)
        s.settle()
        #expect(s.phase == .hidden)
    }

    @Test func turnsAroundMidSlide() {
        var s = DockSlide()
        s.observe(72)
        let slid5 = s.observe(nil)
        #expect(slid5)
        #expect(s.phase == .hiding)
        let slid6 = s.observe(72)
        #expect(slid6)
        #expect(s.phase == .showing && s.inset == 72)
    }

    @Test func aResizedDockMovesTheShownItems() {
        var s = DockSlide()
        s.observe(72)
        s.settle()
        let slid7 = s.observe(90)
        #expect(slid7)
        #expect(s.phase == .shown && s.inset == 90)
    }

    @Test func aZeroExtentCountsAsGone() {
        var s = DockSlide()
        s.observe(72)
        let slid8 = s.observe(0)
        #expect(slid8)
        #expect(s.phase == .hiding)
    }

    @Test func resetRests() {
        var s = DockSlide()
        s.observe(72)
        s.reset()
        #expect(s == DockSlide())
    }

    @Test func framesStayValidThroughTheSlide() {
        // A bottom-left stack (meters over the meeting list) moved up by the slide, at rest,
        // midway and clear of the Dock: DeskFrameCheck holds at every step.
        let window = CGSize(width: 1800, height: 1169)
        func elements(_ lift: CGFloat) -> [DeskElement] {
            [DeskElement(kind: .meters, frame: CGRect(x: 52, y: 742 - lift, width: 358, height: 136)),
             DeskElement(kind: .meterRow, key: "five_hour", frame: CGRect(x: 52, y: 742 - lift, width: 358, height: 58)),
             DeskElement(kind: .meterRow, key: "seven_day", frame: CGRect(x: 52, y: 820 - lift, width: 358, height: 58)),
             DeskElement(kind: .meetings, frame: CGRect(x: 52, y: 898 - lift, width: 260, height: 22), clickable: false)]
        }
        let extent = DockGeometry.estimatedThickness(tilesize: 51)
        for step in stride(from: 0.0, through: 1.0, by: 0.25) {
            #expect(DeskFrameCheck.problem(elements(extent * step), window: window) == nil)
        }
    }
}

@Suite("Dock watch")
struct DockWatchTests {
    @Test func nothingReadsAwayFromAHiddenDock() {
        for tick in 0..<8 {
            #expect(!DockWatch.shouldRead(inZone: false, phase: .hidden, pointerMoved: true,
                                          sinceActivity: 0, delay: 0.5, tick: tick))
        }
    }

    @Test func inTheZoneAMovingOrRecentPointerReads() {
        #expect(DockWatch.shouldRead(inZone: true, phase: .hidden, pointerMoved: true, sinceActivity: 10, delay: 0.5, tick: 1))
        // Resting at the edge: reads through the Dock's delay and its slide, then rests.
        #expect(DockWatch.shouldRead(inZone: true, phase: .hidden, pointerMoved: false, sinceActivity: 1.4, delay: 0.5, tick: 1))
        #expect(!DockWatch.shouldRead(inZone: true, phase: .shown, pointerMoved: false, sinceActivity: 1.6, delay: 0.5, tick: 1))
        #expect(DockWatch.quietAfter(delay: 2) == 3)
    }

    @Test func awayFromAShownDockReadsNowAndThenUntilItHides() {
        let reads = (0..<8).filter {
            DockWatch.shouldRead(inZone: false, phase: .shown, pointerMoved: false, sinceActivity: 0, delay: 0.5, tick: $0)
        }
        #expect(reads == [0, 4])
        #expect(DockWatch.shouldRead(inZone: false, phase: .hiding, pointerMoved: false, sinceActivity: 0, delay: 0.5, tick: 8))
    }
}

