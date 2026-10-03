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
}
