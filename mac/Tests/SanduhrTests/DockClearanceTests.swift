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
        #expect(p.timeModifier == nil)
        #expect(p.slideDuration == 0.5)
    }

    @Test func theSlideTakesTheDocksOwnTime() {
        func duration(_ scale: Any?) -> TimeInterval {
            DockPrefs.read { $0 == "autohide-time-modifier" ? scale : nil }.slideDuration
        }
        #expect(duration(nil) == DockPrefs.defaultSlide)
        #expect(duration(NSNumber(value: 1)) == 0.5)
        #expect(abs(duration(NSNumber(value: 0.3)) - 0.15) < 1e-9)
        #expect(duration(NSNumber(value: 0)) == 0)
        #expect(duration(NSNumber(value: -1)) == DockPrefs.defaultSlide)
        #expect(duration(NSNumber(value: 60)) == 2.5)
        #expect(DockFollower.animation(showing: true, duration: 0) == nil)
        #expect(DockFollower.animation(showing: true, duration: 0.15) != nil)
        #expect(DockFollower.animation(showing: false, duration: 0.15) != nil)
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
        #expect(DockGeometry.anchors(for: .bottom) == [.bl, .bc, .br])
        #expect(DockGeometry.anchors(for: .left) == [.tl, .ml, .bl])
        #expect(DockGeometry.anchors(for: .right) == [.tr, .mr, .br])
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

    @Test func theRevealEdgeIsTheVeryEdge() {
        #expect(DockGeometry.atRevealEdge(CGPoint(x: 900, y: 1169), size: size, side: .bottom))
        #expect(DockGeometry.atRevealEdge(CGPoint(x: 900, y: 1167.5), size: size, side: .bottom))
        // In the trigger zone, where the list is read, but short of where the Dock reacts.
        #expect(!DockGeometry.atRevealEdge(CGPoint(x: 900, y: 1164), size: size, side: .bottom))
        #expect(DockGeometry.inTriggerZone(CGPoint(x: 900, y: 1164), size: size, side: .bottom, shownExtent: 0))
        #expect(DockGeometry.atRevealEdge(CGPoint(x: 0, y: 500), size: size, side: .left))
        #expect(DockGeometry.atRevealEdge(CGPoint(x: 1800, y: 500), size: size, side: .right))
        #expect(!DockGeometry.atRevealEdge(CGPoint(x: 1800, y: 500), size: size, side: .left))
        #expect(!DockGeometry.atRevealEdge(CGPoint(x: 2400, y: 1169), size: size, side: .bottom))
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

    let t0 = Date(timeIntervalSinceReferenceDate: 1000)

    @Test func anticipatedSlideShowsBeforeTheListAndAReadingConfirmsIt() {
        var s = DockSlide()
        let slid = s.anticipate(73, now: t0)
        #expect(slid)
        #expect(s.phase == .showing && s.inset == 73 && s.unconfirmed)
        // The list has not caught up yet: nothing changes.
        let early = s.observe(nil, now: t0.addingTimeInterval(0.1))
        #expect(!early)
        #expect(s.phase == .showing)
        s.settle()
        let confirmed = s.observe(73, now: t0.addingTimeInterval(0.2))
        #expect(!confirmed)
        #expect(s.phase == .shown && !s.unconfirmed)
        // Confirmed: the next reading without the Dock slides back at once.
        let gone = s.observe(nil, now: t0.addingTimeInterval(0.3))
        #expect(gone)
        #expect(s.phase == .hiding && s.inset == 0)
    }

    @Test func anUnconfirmedSlideGoesBack() {
        var s = DockSlide()
        s.anticipate(73, now: t0)
        s.settle()
        let waiting = s.observe(nil, now: t0.addingTimeInterval(DockSlide.confirmWithin - 0.05))
        #expect(!waiting)
        #expect(s.phase == .shown)
        let back = s.observe(nil, now: t0.addingTimeInterval(DockSlide.confirmWithin))
        #expect(back)
        #expect(s.phase == .hiding && s.inset == 0 && !s.unconfirmed)
    }

    @Test func onlyARestingDockAnticipates() {
        var s = DockSlide()
        let none = s.anticipate(0, now: t0)
        #expect(!none)
        s.observe(72)
        let alreadyUp = s.anticipate(73, now: t0)
        #expect(!alreadyUp)
        #expect(s.inset == 72 && !s.unconfirmed)
        s.pointerLeft()
        let dropping = s.anticipate(73, now: t0)
        #expect(dropping)
        #expect(s.phase == .showing)
    }

    @Test func thePointerLeavingSlidesBack() {
        var s = DockSlide()
        s.anticipate(73, now: t0)
        let left = s.pointerLeft()
        #expect(left)
        #expect(s.phase == .hiding && !s.unconfirmed)
        let again = s.pointerLeft()
        #expect(!again)
        // The Dock is still on screen while it drops: a reading away from its zone does not
        // bring the Desk back up.
        let stillDropping = s.observe(73, canShow: false, now: t0.addingTimeInterval(0.1))
        #expect(!stillDropping)
        #expect(s.phase == .hiding)
        s.settle()
        let stillAway = s.observe(73, canShow: false)
        #expect(!stillAway)
        #expect(s.phase == .hidden)
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

@Suite("Dock dwell")
struct DockDwellTests {
    let t0 = Date(timeIntervalSinceReferenceDate: 1000)

    @Test func dueAfterTheDocksDelay() {
        var d = DockDwell()
        #expect(!d.due(delay: 0.5, now: t0))
        d.update(atEdge: true, now: t0)
        d.update(atEdge: true, now: t0.addingTimeInterval(0.3))
        #expect(!d.due(delay: 0.5, now: t0.addingTimeInterval(0.45)))
        #expect(d.due(delay: 0.5, now: t0.addingTimeInterval(0.5)))
    }

    @Test func aZeroDelayIsDueAtOnce() {
        var d = DockDwell()
        d.update(atEdge: true, now: t0)
        #expect(d.due(delay: 0, now: t0))
    }

    @Test func oneSlidePerVisit() {
        var d = DockDwell()
        d.update(atEdge: true, now: t0)
        d.spend()
        #expect(!d.due(delay: 0, now: t0.addingTimeInterval(5)))
        d.update(atEdge: false, now: t0.addingTimeInterval(5))
        #expect(d == DockDwell())
        d.update(atEdge: true, now: t0.addingTimeInterval(6))
        #expect(d.due(delay: 0.5, now: t0.addingTimeInterval(6.5)))
    }

    @Test func dwellThenSlideThenConfirmOrRetract() {
        // The tick's order: the pointer rests at the edge, the Desk starts with the Dock, the
        // list confirms it; a second visit the list never confirms slides back and stays down.
        let delay = 0.5
        var d = DockDwell()
        var s = DockSlide()
        for ms in stride(from: 0, through: 500, by: 50) {
            let now = t0.addingTimeInterval(Double(ms) / 1000)
            d.update(atEdge: true, now: now)
            if d.due(delay: delay, now: now) { d.spend(); s.anticipate(73, now: now) }
            if ms < 500 { #expect(s.phase == .hidden) }
        }
        #expect(s.phase == .showing && s.inset == 73)
        s.observe(73, now: t0.addingTimeInterval(0.55))
        #expect(!s.unconfirmed)

        s.pointerLeft(); s.settle()
        d.update(atEdge: false, now: t0.addingTimeInterval(2))
        let t1 = t0.addingTimeInterval(3)
        for ms in stride(from: 0, through: 1500, by: 50) {
            let now = t1.addingTimeInterval(Double(ms) / 1000)
            d.update(atEdge: true, now: now)
            if d.due(delay: delay, now: now) { d.spend(); s.anticipate(73, now: now) }
            s.observe(nil, now: now)
        }
        #expect(s.phase == .hiding && s.inset == 0)
    }
}

@Suite("Dock watch")
struct DockWatchTests {
    @Test func aRestingPointerReadsLongEnoughToConfirm() {
        for delay in [0.0, 0.5, 2] {
            #expect(DockWatch.quietAfter(delay: delay) > DockSlide.confirmWithin)
            #expect(DockWatch.shouldRead(inZone: true, phase: .shown, pointerMoved: false,
                                         sinceActivity: DockSlide.confirmWithin, delay: delay, tick: 1))
        }
    }

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

