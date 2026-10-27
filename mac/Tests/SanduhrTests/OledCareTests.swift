import Foundation
import CoreGraphics
import Testing
@testable import Sanduhr

/// OLED care (port of Windows' OledCareTests): the switch, the drift orbit and its clamp, the idle state
/// machine and the white cap. Skipped from the Windows set: the JsonObject-based switch cases (the Mac
/// reads a defaults store, covered below), `DeskInk.Resolve`/`ResolvedInk` cases (the Mac has no resolved-ink
/// type; the cap works on the ink spec, so the same colors are pinned through `OledInk.capWhite`), the
/// white-shadow cap (the Mac's shadow is black), and `DeskInkSettings.Decode` (WPF-side settings JSON).
@Suite("OLED care")
struct OledCareTests {
    // ---- The switch ----

    @Test func theSwitchIsOffUntilTheKeyIsTrue() {
        #expect(OledCare.key == "deskOledCare")
        let d = MemoryDefaults()
        #expect(OledCare.isOn(in: d) == false)
        d.set("yes", forKey: OledCare.key)
        #expect(OledCare.isOn(in: d) == false)
        d.set(true, forKey: OledCare.key)
        #expect(OledCare.isOn(in: d))
        #expect(OledCare.defaultOn == false)
        #expect(OledCare.title == "OLED care")
        #expect(OledCare.caption == "For OLED screens: the Desk drifts a few pixels every few minutes, dims when you're away, and never holds pure white.")
    }

    // ---- Drift ----

    @Test func theOrbitIsSixteenPointsAFigureEightWithinSixPoints() {
        let points = (0..<OledDrift.steps).map(OledDrift.offset)
        #expect(points.count == 16)
        #expect(points.allSatisfy { (-6...6).contains($0.x) && (-6...6).contains($0.y) })
        #expect(points.contains { $0.x == 6 && $0.y == 4 })
        #expect(points.contains { $0.x == -6 && $0.y == -4 })
        for k in 0..<16 {
            #expect(points[k].x == Int((6 * sin(Double(k) * .pi / 8)).rounded(.toNearestOrAwayFromZero)))
            #expect(points[k].y == Int((6 * sin(Double(k) * .pi / 4)).rounded(.toNearestOrAwayFromZero)))
        }
    }

    @Test func successivePointsDifferAndTheLoopAveragesOnTheAnchor() {
        for k in 0..<40 {
            let a = OledDrift.offset(k), b = OledDrift.offset(k + 1)
            #expect(a.x != b.x || a.y != b.y)
        }
        #expect((0..<16).reduce(0) { $0 + OledDrift.offset($1).x } == 0)
        #expect((0..<16).reduce(0) { $0 + OledDrift.offset($1).y } == 0)
    }

    @Test func theLoopRepeatsAndNegativeStepsWrap() {
        for k in 0..<16 {
            #expect(OledDrift.offset(k) == OledDrift.offset(k + 16))
            #expect(OledDrift.offset(k) == OledDrift.offset(k - 16))
        }
        #expect(OledDrift.offset(15) == OledDrift.offset(-1))
        #expect(OledDrift.every == 180)
        #expect(OledDrift.radius == 6)
    }

    private let work = CGRect(x: 0, y: 0, width: 1920, height: 1032)

    @Test func aClampLeavesAPieceInsideItsAreaWhereItIs() {
        let r = CGRect(x: 100, y: 200, width: 300, height: 120)
        #expect(OledDrift.clamp(r, area: work, clearance: .none) == r)
    }

    @Test func aClampPullsAPieceBackFromEveryEdgeAndFromTheDocksReach() {
        let dock = OledInsets(left: 0, top: 0, right: 0, bottom: 48)
        let low = OledDrift.clamp(CGRect(x: 100, y: 990, width: 300, height: 120), area: work, clearance: dock)
        #expect(low.minY == 864, "\(low)")
        let corner = OledDrift.clamp(CGRect(x: -5, y: -9, width: 300, height: 120), area: work, clearance: .none)
        #expect(corner.minX == 0 && corner.minY == 0)
        let right = OledDrift.clamp(CGRect(x: 1700, y: 10, width: 300, height: 120), area: work, clearance: .none)
        #expect(right.minX == 1620, "\(right)")
        #expect(OledDrift.clamp(CGRect(x: 40, y: 10, width: 300, height: 120), area: work,
                                clearance: OledInsets(left: 60, top: 0, right: 0, bottom: 0)).minX == 60)
        // The menu bar on top.
        #expect(OledDrift.clamp(CGRect(x: 100, y: 2, width: 300, height: 120), area: work,
                                clearance: OledInsets(left: 0, top: 38, right: 0, bottom: 0)).minY == 38)
    }

    @Test func aRectBiggerThanTheRegionSitsAtItsTopLeft() {
        let c = OledDrift.clamp(CGRect(x: 500, y: 500, width: 3000, height: 2000), area: work, clearance: .none)
        #expect(c.minX == 0 && c.minY == 0)
    }

    @Test func driftMovesTheRectByTheOrbitAndNeverChangesItsSize() {
        let home = CGRect(x: 800, y: 400, width: 300, height: 120)
        for k in 0..<16 {
            let d = OledDrift.drifted(home, step: k, area: work, clearance: .none)
            let o = OledDrift.offset(k)
            #expect(d == CGRect(x: home.minX + CGFloat(o.x), y: home.minY + CGFloat(o.y), width: 300, height: 120))
        }
    }

    @Test func driftNeverCrossesTheDockClearanceOrTheAreaWhateverTheStep() {
        let clearance = OledInsets(left: 0, top: 0, right: 0, bottom: 56)
        for home in [CGRect(x: 100, y: 1032 - 56 - 120, width: 300, height: 120), CGRect(x: 0, y: 0, width: 300, height: 120),
                     CGRect(x: 1920 - 300, y: 400, width: 300, height: 120)] {
            for k in 0..<16 {
                let d = OledDrift.drifted(home, step: k, area: work, clearance: clearance)
                #expect(d.minX >= 0 && d.minY >= 0 && d.maxX <= 1920 && d.maxY <= 1032 - 56, "\(home) step \(k) -> \(d)")
            }
        }
    }

    @Test func thePieceTheLayoutPutOutsideTheRegionIsNeverPulledIn() {
        let clearance = OledInsets(left: 0, top: 0, right: 0, bottom: 56)
        let home = CGRect(x: 100, y: 1032 - 56 - 120 + 3, width: 300, height: 120)
        for k in 0..<16 {
            #expect(OledDrift.drifted(home, step: k, area: work, clearance: clearance).minY <= home.minY, "step \(k)")
        }
        #expect(OledDrift.drifted(home, step: 5, area: work, clearance: clearance).minY == home.minY - 4)
    }

    // ---- Idle ----

    private final class Clock { var now = Date(timeIntervalSince1970: 1_800_000_000) }

    @Test func dimsOnceAfterFiveMinutesWithoutInputAndWakesOnceOnInput() {
        let clock = Clock()
        let idle = OledIdle(clock: { clock.now })
        var lastInput = clock.now

        #expect(idle.observe(lastInputAt: lastInput) == .none)
        clock.now += 4 * 60 + 45
        #expect(idle.observe(lastInputAt: lastInput) == .none)
        #expect(idle.dimmed == false)
        clock.now += 15
        #expect(idle.observe(lastInputAt: lastInput) == .dim)
        #expect(idle.dimmed)
        clock.now += 20 * 60
        #expect(idle.observe(lastInputAt: lastInput) == .none)

        lastInput = clock.now - 2
        #expect(idle.observe(lastInputAt: lastInput) == .wake)
        #expect(idle.dimmed == false)
        #expect(idle.observe(lastInputAt: lastInput) == .none)
    }

    @Test func anUnreadableLastInputChangesNothing() {
        let clock = Clock()
        let idle = OledIdle(clock: { clock.now })
        #expect(idle.observe(lastInputAt: nil) == .none)
        _ = idle.observe(lastInputAt: clock.now - 3600)
        #expect(idle.dimmed)
        #expect(idle.observe(lastInputAt: nil) == .none)
        #expect(idle.dimmed)
    }

    @Test func resetWakesADimmedStateAndIsQuietOtherwise() {
        let clock = Clock()
        let idle = OledIdle(clock: { clock.now })
        #expect(idle.reset() == .none)
        _ = idle.observe(lastInputAt: clock.now - 3600)
        #expect(idle.reset() == .wake)
        #expect(idle.dimmed == false)
        #expect(idle.reset() == .none)
    }

    @Test func theThresholdCanBeChangedAndTheNumbersAreTheSpecs() {
        let clock = Clock()
        let idle = OledIdle(clock: { clock.now }, threshold: 30)
        #expect(idle.observe(lastInputAt: clock.now - 30) == .dim)
        #expect(OledIdle.defaultThreshold == 300)
        #expect(OledIdle.pollEvery == 15)
        #expect(OledIdle.deskDimmedOpacity == 0.40)
        #expect(OledIdle.widgetDimmedOpacity == 0.70)
        #expect(OledIdle.easeFor(.dim, animate: true) == 2)
        #expect(OledIdle.easeFor(.wake, animate: true) == 0.3)
    }

    @Test func reduceMotionSnapsBothWays() {
        #expect(OledIdle.easeFor(.dim, animate: false) == 0)
        #expect(OledIdle.easeFor(.wake, animate: false) == 0)
    }

    // ---- The white cap ----

    @Test func pureWhiteIsDrawnAsE6E6E6() {
        #expect(OledInk.capLuminance("ffffff", OledInk.whiteCapLuminance) == "e6e6e6")
        #expect(abs(OledInk.luminance("e6e6e6") - OledInk.whiteCapLuminance) < 1e-9)
    }

    @Test func colorsUnderTheCapAreUntouchedAndAnInvalidOneReadsAsWhite() {
        for hex in ["9ad7ff", "ffd08a", "000000", "e6e6e6", "1b1f27", "33fdff"] {
            #expect(OledInk.capLuminance(hex, OledInk.whiteCapLuminance) == hex)
        }
        #expect(OledInk.capLuminance("nope", OledInk.whiteCapLuminance) == "e6e6e6")
        #expect(OledInk.capLuminance("#FFF", OledInk.whiteCapLuminance) == "e6e6e6")
    }

    @Test func aNearWhiteKeepsItsHueAndLandsAtTheCap() {
        let capped = OledInk.capLuminance("fff4e0", OledInk.whiteCapLuminance)
        let l = OledInk.luminance(capped)
        #expect(l >= OledInk.whiteCapLuminance - 0.01 && l <= OledInk.whiteCapLuminance + 0.01)
        let r = Int(capped.prefix(2), radix: 16)!, g = Int(capped.dropFirst(2).prefix(2), radix: 16)!, b = Int(capped.suffix(2), radix: 16)!
        #expect(r >= g && g >= b, "\(capped)")
        #expect(r < 0xff)
    }

    @Test func theCapScalesWhiteInkAndEveryGradientStopAboveItAndNothingElse() {
        #expect(OledInk.capWhite("ffffff") == "e6e6e6")
        #expect(OledInk.capWhite("") == "e6e6e6")
        let g = OledInk.capWhite("ffffff,3a63e0,fdfdfd").split(separator: ",").map(String.init)
        #expect(g.count == 3)
        #expect(g[0] == "e6e6e6")
        #expect(g[1] == "3a63e0")
        #expect(OledInk.luminance(g[2]) <= OledInk.whiteCapLuminance + 0.01)
        #expect(g[2] != "fdfdfd")
        #expect(OledInk.capWhite("531b93,012089,00fdff") == "531b93,012089,00fdff")
        #expect(OledInk.capWhite("fff") == "e6e6e6")
    }

    @Test func theCapIsARenderTimeRuleTheSavedInkIsUntouched() {
        // The cap returns a new spec; DeskLook/inkHexes still hand back pure white for the saved one.
        #expect(DeskThemeMapping.inkHexes("ffffff") == ["ffffff", "ffffff"])
        #expect(OledInk.capWhite("ffffff") != "ffffff")
    }
}

/// OLED care in Settings: found by what a person would type, on the Desk page (port of OledCareSettingsTests).
@Suite("OLED care settings")
struct OledCareSettingsTests {
    @Test(arguments: ["oled", "burn-in", "burn in", "drift", "dim"])
    func theSwitchIsSearchableByWhatAPersonWouldType(_ query: String) {
        #expect(SettingsSearch.hits(query).contains { $0.entry.page == .deskLayout && $0.entry.anchor == SettingsAnchor.oled })
        #expect(SettingsNames.table.first { $0.name == SettingsNames.oledCareSwitch }?.page == .deskLayout)
    }

    @Test func theKeyIsTheFrozenOne() {
        #expect(OledCare.key == "deskOledCare")
        #expect(OledCare.defaultOn == false)
    }
}

/// IdleClock: one shared reader and timer, none with no subscriber.
@Suite("Idle clock")
@MainActor
struct IdleClockTests {
    private final class Source { var seconds: TimeInterval? = 3; var now = Date(timeIntervalSince1970: 1_800_000_000) }

    private func clock(_ s: Source) -> IdleClock { IdleClock(reader: { s.seconds }, now: { s.now }) }

    @Test func noSubscriberMeansNoTimer() {
        let c = clock(Source())
        #expect(c.isPolling == false)
        #expect(c.subscriberCount == 0)
        #expect(c.pollInterval == 0)
    }

    @Test func aSubscriberStartsOneTimerAndHearsAtOnce() {
        let s = Source()
        let c = clock(s)
        var heard: [TimeInterval] = []
        let t = c.subscribe(every: 15) { heard.append($0) }
        #expect(c.isPolling)
        #expect(c.pollInterval == 15)
        #expect(heard == [3])
        c.unsubscribe(t)
        #expect(c.isPolling == false)
        #expect(c.subscriberCount == 0)
    }

    @Test func aSecondSubscriberSharesTheTimerAndKeepsItsOwnPace() {
        let s = Source()
        let c = clock(s)
        var slow = 0, fast = 0
        let a = c.subscribe(every: 60) { _ in slow += 1 }
        #expect(c.pollInterval == 60)
        let b = c.subscribe(every: 15) { _ in fast += 1 }
        #expect(c.subscriberCount == 2)
        #expect(c.pollInterval == 15)
        #expect((slow, fast) == (1, 1))

        s.now += 15; c.tick()
        #expect((slow, fast) == (1, 2))
        s.now += 45; c.tick()
        #expect((slow, fast) == (2, 3))

        c.unsubscribe(b)
        #expect(c.isPolling)
        #expect(c.pollInterval == 60)
        c.unsubscribe(a)
        #expect(c.isPolling == false)
    }

    @Test func anUnreadableClockTellsNobodyAndDoesNotCrash() {
        let s = Source()
        s.seconds = nil
        let c = clock(s)
        var heard = 0
        let t = c.subscribe(every: 15) { _ in heard += 1 }
        s.now += 20; c.tick()
        #expect(heard == 0)
        c.unsubscribe(t)
    }

    @Test func theSystemReaderGivesAFiniteNonNegativeAnswer() {
        let v = IdleClock.systemSeconds()
        #expect(v == nil || v! >= 0)
    }
}

/// OLED care's controller starts and stops with the switch and the Desk: no timer, no subscription, no
/// offset while it is off.
@Suite("OLED care controller")
@MainActor
struct OledCareControllerTests {
    @Test func itRunsOnlyWhileTheSwitchAndTheDeskAreBothOn() {
        let clock = IdleClock(reader: { 1 }, now: Date.init)
        let c = OledCareController(idleClock: clock, reduceMotion: { true })
        let model = DeskController.shared.model

        c.apply(deskRunning: true, switchOn: false)
        #expect(c.active == false && clock.isPolling == false && model.oledStep == nil)
        c.apply(deskRunning: false, switchOn: true)
        #expect(c.active == false && clock.isPolling == false)

        c.apply(deskRunning: true, switchOn: true)
        #expect(c.active && clock.isPolling && clock.pollInterval == OledIdle.pollEvery && model.oledStep == 0)

        c.apply(deskRunning: false, switchOn: true)
        #expect(c.active == false && clock.isPolling == false && model.oledStep == nil)
    }
}
