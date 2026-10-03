import Foundation
import CoreGraphics
import Testing
@testable import Sanduhr

/// The notch glow: which events glow, once per meeting, and where its window sits. No windows,
/// timers or real defaults involved.

@Suite("Notch glow rules")
struct NotchGlowRulesTests {
    var calendar: Calendar {
        var c = Calendar(identifier: .gregorian)
        c.timeZone = TimeZone(identifier: "UTC")!
        return c
    }
    /// 2026-10-02 09:00 UTC.
    let t0 = Date(timeIntervalSince1970: 1_791_018_000)
    func at(_ s: Double) -> Date { t0.addingTimeInterval(s) }
    let allOn = NotchGlowSwitches(alerts: true, meetings: true, camera: true)

    func decide(_ e: NotchGlowEvent, _ s: NotchGlowSwitches, _ m: inout NotchGlowMemory, _ now: Date? = nil) -> Bool {
        NotchGlowRules.decide(e, switches: s, memory: &m, now: now ?? t0, calendar: calendar)
    }

    @Test func allOffByDefault() {
        let s = NotchGlowSwitches(MemoryDefaults())
        #expect(s == NotchGlowSwitches())
        var m = NotchGlowMemory()
        #expect(!decide(.alert, s, &m))
        #expect(!decide(.meetingSoon(id: "a"), s, &m))
        #expect(!decide(.cameraLightOn, s, &m))
    }

    @Test func switchesReadFromTheirKeys() {
        let d = MemoryDefaults()
        d.set(true, forKey: "notchGlowAlerts")
        d.set(true, forKey: "notchGlowCamera")
        #expect(NotchGlowSwitches(d) == NotchGlowSwitches(alerts: true, meetings: false, camera: true))
        d.set(true, forKey: "notchGlowMeetings")
        #expect(NotchGlowSwitches(d) == allOn)
    }

    @Test func eachSwitchGovernsOnlyItsEvent() {
        var m = NotchGlowMemory()
        let alerts = NotchGlowSwitches(alerts: true)
        #expect(decide(.alert, alerts, &m))
        #expect(!decide(.cameraLightOn, alerts, &m))
        #expect(!decide(.meetingSoon(id: "a"), alerts, &m))
        let camera = NotchGlowSwitches(camera: true)
        #expect(decide(.cameraLightOn, camera, &m))
        #expect(!decide(.alert, camera, &m))
        let meetings = NotchGlowSwitches(meetings: true)
        #expect(decide(.meetingSoon(id: "a"), meetings, &m))
        #expect(!decide(.alert, meetings, &m))
        #expect(!decide(.cameraLightOn, meetings, &m))
    }

    @Test func alertsAndTheCameraGlowEveryTime() {
        var m = NotchGlowMemory()
        #expect(decide(.alert, allOn, &m))
        #expect(decide(.alert, allOn, &m))
        #expect(decide(.cameraLightOn, allOn, &m))
        #expect(decide(.cameraLightOn, allOn, &m))
        #expect(m.meetings.isEmpty)
    }

    @Test func aMeetingGlowsOnce() {
        var m = NotchGlowMemory()
        #expect(decide(.meetingSoon(id: "a"), allOn, &m))
        #expect(!decide(.meetingSoon(id: "a"), allOn, &m, at(15)))
        #expect(decide(.meetingSoon(id: "b"), allOn, &m, at(15)))
        #expect(m.meetings == ["a", "b"])
    }

    @Test func aMeetingDueWhileSwitchedOffIsNotRemembered() {
        var m = NotchGlowMemory()
        #expect(!decide(.meetingSoon(id: "a"), NotchGlowSwitches(), &m))
        #expect(m.meetings.isEmpty)
        #expect(decide(.meetingSoon(id: "a"), allOn, &m, at(15)))
    }

    @Test func theMemoryClearsWithTheDay() {
        var m = NotchGlowMemory()
        #expect(decide(.meetingSoon(id: "a"), allOn, &m))
        #expect(!decide(.meetingSoon(id: "a"), allOn, &m, at(14 * 3600)))   // 23:00, same day
        #expect(decide(.meetingSoon(id: "a"), allOn, &m, at(16 * 3600)))    // 01:00 next day
        #expect(m.meetings == ["a"])
    }

    @Test func theMinuteBeforeWindow() {
        let start = at(600)
        #expect(!NotchGlowRules.isSoon(start: start, now: at(539)))   // 61 s out
        #expect(NotchGlowRules.isSoon(start: start, now: at(540)))    // 60 s out
        #expect(NotchGlowRules.isSoon(start: start, now: at(599)))
        #expect(NotchGlowRules.isSoon(start: start, now: at(600)))    // starting now
        #expect(NotchGlowRules.isSoon(start: start, now: at(630)))    // 30 s late
        #expect(!NotchGlowRules.isSoon(start: start, now: at(631)))
    }

    @Test func meetingsDueGlowOncePerMeeting() {
        var m = NotchGlowMemory()
        let standup = (id: "standup", start: at(600))
        let review = (id: "review", start: at(3600))
        func due(_ now: Date, _ s: NotchGlowSwitches? = nil) -> Bool {
            NotchGlowRules.meetingsDue([standup, review], switches: s ?? allOn, memory: &m, now: now, calendar: calendar)
        }
        // Checked every 15 seconds, as the controller does.
        #expect(!due(at(525)))
        #expect(due(at(540)))
        #expect(!due(at(555)))
        #expect(!due(at(600)))
        #expect(!due(at(615)))
        #expect(due(at(3555)))
        #expect(!due(at(3570)))
        #expect(m.meetings.count == 2)
    }

    @Test func meetingsDueRespectTheSwitch() {
        var m = NotchGlowMemory()
        let meetings = [(id: "standup", start: at(30))]
        #expect(!NotchGlowRules.meetingsDue(meetings, switches: NotchGlowSwitches(alerts: true, camera: true),
                                            memory: &m, now: t0, calendar: calendar))
        #expect(NotchGlowRules.meetingsDue(meetings, switches: allOn, memory: &m, now: t0, calendar: calendar))
    }

    @Test func twoMeetingsAtOnceGlowOnceAndAreBothRemembered() {
        var m = NotchGlowMemory()
        let meetings = [(id: "a", start: at(45)), (id: "b", start: at(45))]
        #expect(NotchGlowRules.meetingsDue(meetings, switches: allOn, memory: &m, now: t0, calendar: calendar))
        #expect(m.meetings.count == 2)
        #expect(!NotchGlowRules.meetingsDue(meetings, switches: allOn, memory: &m, now: at(15), calendar: calendar))
    }

    @Test func aRecurringMeetingIsKeyedByItsStart() {
        let id = "weekly"
        #expect(NotchGlowRules.key(id: id, start: t0) != NotchGlowRules.key(id: id, start: at(7 * 86_400)))
        #expect(NotchGlowRules.key(id: id, start: t0) == NotchGlowRules.key(id: id, start: t0))
    }

    @Test func eventKinds() {
        #expect(NotchGlowEvent.alert.kind == .alert)
        #expect(NotchGlowEvent.meetingSoon(id: "x").kind == .meeting)
        #expect(NotchGlowEvent.cameraLightOn.kind == .camera)
        #expect(NotchGlowEvent.Kind.allCases.map(\.rawValue) == ["alert", "meeting", "camera"])
    }
}

@Suite("Notch glow layout")
struct NotchGlowLayoutTests {
    @Test func noGlowAlongTheTop() {
        // 39 pt menu bar, no strip: window 39 + 32 tall. Clear for the top 2 pt, full by 6 pt, so the
        // sides glow nearly the wings' full height.
        let fade = NotchGlowLayout.topFade(barHeight: 39, islandHeight: 39)
        #expect(fade.start > 0)
        #expect(abs(fade.start - 2.0 / 71) < 0.0001)
        #expect(abs(fade.end - 6.0 / 71) < 0.0001)
        #expect(fade.end * 71 < 39 * 0.25)
        #expect(fade.end > fade.start)
        let degenerate = NotchGlowLayout.topFade(barHeight: 0, islandHeight: 0)
        #expect(degenerate.start <= degenerate.end)
    }

    @Test func islandHeightFollowsTheStrip() {
        #expect(NotchGlowLayout.islandHeight(notchHeight: 32, barHeight: 37, chin: 26) == 58)
        #expect(NotchGlowLayout.islandHeight(notchHeight: 32, barHeight: 37, chin: 0) == 37)
        // A strip shorter than the menu bar still reaches the bar's bottom.
        #expect(NotchGlowLayout.islandHeight(notchHeight: 32, barHeight: 37, chin: 2) == 37)
    }

    @Test func frameGrowsTheWingsWindowByTheReach() {
        let wings = CGRect(x: 471.5, y: 945, width: 569, height: 37)   // 14-inch, maxY 982
        let f = NotchGlowLayout.frame(wings: wings, islandHeight: 58)
        let reach = NotchGlowLayout.reach
        #expect(f.maxY == wings.maxY)
        #expect(f.midX == wings.midX)
        #expect(f.width == wings.width + reach * 2)
        #expect(f.height == 58 + reach * 2)
    }

    @Test func radiusMatchesTheIsland() {
        #expect(NotchGlowLayout.radius(barHeight: 37, chin: 26) == 16)
        #expect(abs(NotchGlowLayout.radius(barHeight: 37, chin: 10) - 7) < 0.0001)
        #expect(NotchGlowLayout.radius(barHeight: 37, chin: 0) == 10)
        #expect(abs(NotchGlowLayout.radius(barHeight: 20, chin: 0) - 6) < 0.0001)
    }

    // MARK: The plain notch (item 27)

    @Test func plainFrameHugsTheHardwareNotch() {
        // 14-inch MacBook Pro: 1512 x 982 screen, a 185 x 32 notch centered at the top.
        let screen = CGRect(x: 0, y: 0, width: 1512, height: 982)
        let notch = CGRect(x: 663.5, y: 0, width: 185, height: 32)
        let f = NotchGlowLayout.plainFrame(screen: screen, notch: notch)
        let reach = NotchGlowLayout.reach
        #expect(f.maxY == screen.maxY)
        #expect(f.midX == screen.minX + notch.midX)
        #expect(f.width == 185 + reach * 2)
        #expect(f.height == 32 + reach * 2)
        // No wings: far narrower than the island's window.
        #expect(f.width < 185 + NotchWingsView.maxWings)
    }

    @Test func plainFrameFollowsAScreenThatIsNotPrimary() {
        let screen = CGRect(x: -1512, y: 200, width: 1512, height: 982)
        let notch = CGRect(x: 663.5, y: 0, width: 185, height: 32)
        let f = NotchGlowLayout.plainFrame(screen: screen, notch: notch)
        #expect(f.minX == -1512 + 663.5 - NotchGlowLayout.reach)
        #expect(f.maxY == 1182)
    }

    @Test func plainCutoutIsTheNotch() {
        let notch = CGRect(x: 663.5, y: 0, width: 185, height: 32)
        #expect(NotchGlowLayout.plainCutout(notch: notch) == CGSize(width: 185, height: 32))
    }

    @Test func plainRadiusRoundsTheBottomCorners() {
        #expect(NotchGlowLayout.plainRadius(notchHeight: 32) == 8)
        #expect(NotchGlowLayout.plainRadius(notchHeight: 38) == 8)
        #expect(abs(NotchGlowLayout.plainRadius(notchHeight: 20) - 5) < 0.0001)
        #expect(NotchGlowLayout.plainRadius(notchHeight: 32) < NotchGlowLayout.radius(barHeight: 37, chin: 26))
    }

    @Test func plainTopFadeLeavesTheScreenEdgeClear() {
        // 32 pt notch: window 64 tall, clear for the top 2 pt, full by 6 pt.
        let fade = NotchGlowLayout.topFade(barHeight: 32, islandHeight: 32)
        #expect(abs(fade.start - 2.0 / 64) < 0.0001)
        #expect(abs(fade.end - 6.0 / 64) < 0.0001)
    }

    @Test func shapePicksIslandOrPlainNotch() {
        #expect(NotchGlowLayout.shape(deskRunning: true, notchOn: true, hasIsland: true, hasNotch: true) == .island)
        // The island off, or Desk not running: the hardware notch still glows.
        #expect(NotchGlowLayout.shape(deskRunning: true, notchOn: false, hasIsland: true, hasNotch: true) == .plain)
        #expect(NotchGlowLayout.shape(deskRunning: false, notchOn: true, hasIsland: false, hasNotch: true) == .plain)
        #expect(NotchGlowLayout.shape(deskRunning: false, notchOn: false, hasIsland: false, hasNotch: true) == .plain)
        // No notched screen: nothing.
        #expect(NotchGlowLayout.shape(deskRunning: true, notchOn: true, hasIsland: false, hasNotch: false) == .none)
        #expect(NotchGlowLayout.shape(deskRunning: false, notchOn: false, hasIsland: false, hasNotch: false) == .none)
        #expect(NotchGlowShape.island.rawValue == "island" && NotchGlowShape.plain.rawValue == "plain")
    }
}

/// Item 34 (a): the glow traces the strip under the camera only while it shows.
@Suite("Notch glow strip cover")
struct NotchGlowStripTests {
    /// A 14-inch MacBook Pro: 1512 x 982, notch 185 x 32 centered, menu bar 37 tall.
    let notch = CGRect(x: 663.5, y: 0, width: 185, height: 32)

    var strip: CGRect {
        NotchGlowLayout.stripRect(notch: notch, barHeight: 37, chin: 26, left: 36, right: 50)!
    }

    @Test func stripSpansTheIslandBelowTheWings() {
        // Island-wide (notch plus both wings), from the wings' bottom (37) to the strip's (32 + 26).
        #expect(strip == CGRect(x: 627.5, y: 37, width: 271, height: 21))
    }

    @Test func noStripWhenNothingHangsBelowTheWings() {
        #expect(NotchGlowLayout.stripRect(notch: notch, barHeight: 37, chin: 0, left: 36, right: 36) == nil)
        // 32 + 5 = 37: level with the wings, nothing below them.
        #expect(NotchGlowLayout.stripRect(notch: notch, barHeight: 37, chin: 5, left: 36, right: 36) == nil)
    }

    @Test func glowChinDropsTheHiddenStrip() {
        #expect(NotchGlowLayout.glowChin(26, stripVisible: true) == 26)
        #expect(NotchGlowLayout.glowChin(26, stripVisible: false) == 0)
        // Hidden: the island is the wings alone, with their radius.
        let hidden = NotchGlowLayout.glowChin(26, stripVisible: false)
        #expect(NotchGlowLayout.islandHeight(notchHeight: 32, barHeight: 37, chin: hidden) == 37)
        #expect(NotchGlowLayout.radius(barHeight: 37, chin: hidden) == NotchGlowLayout.radius(barHeight: 37, chin: 0))
        let shown = NotchGlowLayout.glowChin(26, stripVisible: true)
        #expect(NotchGlowLayout.islandHeight(notchHeight: 32, barHeight: 37, chin: shown) == 58)
    }

    @Test func globalFlipsToTheTopLeftOrigin() {
        let r = CGRect(x: 10, y: 37, width: 100, height: 21)
        // The primary display: the same rect.
        #expect(NotchGlowLayout.global(r, screen: CGRect(x: 0, y: 0, width: 1512, height: 982),
                                       primaryHeight: 982) == r)
        // A screen above the primary one (AppKit y 982...1964): 982 pt higher in CG terms.
        #expect(NotchGlowLayout.global(r, screen: CGRect(x: 200, y: 982, width: 1512, height: 982),
                                       primaryHeight: 982) == CGRect(x: 210, y: -945, width: 100, height: 21))
    }

    @Test func aWindowOverTheStripCoversIt() {
        // A maximized window starting at the menu bar's bottom.
        let app = NotchCoverWindow(layer: 0, bounds: CGRect(x: 0, y: 37, width: 1512, height: 945))
        #expect(NotchStripCover.isCovered(strip: strip, by: [app]))
        // A floating panel over part of it.
        let panel = NotchCoverWindow(layer: 3, bounds: CGRect(x: 850, y: 40, width: 300, height: 200))
        #expect(NotchStripCover.isCovered(strip: strip, by: [panel]))
    }

    @Test func windowsBesideOrBelowLeaveItVisible() {
        let beside = NotchCoverWindow(layer: 0, bounds: CGRect(x: 0, y: 37, width: 600, height: 500))
        let below = NotchCoverWindow(layer: 0, bounds: CGRect(x: 600, y: 100, width: 400, height: 400))
        // Touching the strip's bottom edge only.
        let touching = NotchCoverWindow(layer: 0, bounds: CGRect(x: 600, y: 58, width: 400, height: 400))
        #expect(!NotchStripCover.isCovered(strip: strip, by: [beside, below, touching]))
        #expect(!NotchStripCover.isCovered(strip: strip, by: []))
    }

    @Test func overlaysAndInvisibleWindowsDoNotCount() {
        let full = CGRect(x: 0, y: 0, width: 1512, height: 982)
        // The menu bar (24, 25), the wings (popUpMenu, 101) and the glow above them, a screen-wide
        // overlay (1000): never app windows hiding the strip.
        for layer in [24, 25, 101, 103, 1000] {
            #expect(!NotchStripCover.isCovered(strip: strip, by: [NotchCoverWindow(layer: layer, bounds: full)]))
        }
        // Desk's own layer, and a fully transparent window.
        #expect(!NotchStripCover.isCovered(strip: strip, by: [NotchCoverWindow(layer: -1, bounds: full)]))
        #expect(!NotchStripCover.isCovered(strip: strip, by: [NotchCoverWindow(layer: 0, alpha: 0, bounds: full)]))
    }

    @Test func windowInfoReadsLayerAlphaAndBounds() {
        let info: [String: Any] = [
            kCGWindowLayer as String: 0,
            kCGWindowAlpha as String: 1.0,
            kCGWindowBounds as String: ["X": 10.0, "Y": 37.0, "Width": 300.0, "Height": 200.0],
        ]
        #expect(NotchCoverWindow(info: info) == NotchCoverWindow(layer: 0, bounds: CGRect(x: 10, y: 37, width: 300, height: 200)))
        // No bounds, or no layer: skipped.
        #expect(NotchCoverWindow(info: [kCGWindowLayer as String: 0]) == nil)
        #expect(NotchCoverWindow(info: [kCGWindowBounds as String: ["X": 0.0, "Y": 0.0, "Width": 1.0, "Height": 1.0]]) == nil)
    }
}
