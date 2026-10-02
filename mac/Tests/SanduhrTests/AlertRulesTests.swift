import Foundation
import Testing
@testable import Sanduhr

/// Tests for AlertRules: what fires, once per window, and where it shows. Times run in UTC with
/// a fixed locale so the text is the same on every Mac.
@Suite("Alert rules")
struct AlertRulesTests {
    /// Friday 2 October 2026, 1:00 PM UTC.
    let now = parseISO("2026-10-02T13:00:00Z")!

    var calendar: Calendar {
        var c = Calendar(identifier: .gregorian)
        c.timeZone = TimeZone(identifier: "UTC")!
        c.locale = Locale(identifier: "en_US_POSIX")
        return c
    }

    func iso(_ offset: TimeInterval) -> String {
        let f = ISO8601DateFormatter()
        f.formatOptions = [.withInternetDateTime]
        return f.string(from: now.addingTimeInterval(offset))
    }

    // Typed, so fixture offsets are TimeInterval on every compiler: Swift 6.0 and 6.1 read
    // `2 * 3600` inside a tuple literal as Int.
    let minute: TimeInterval = 60, hour: TimeInterval = 3600, day: TimeInterval = 86400

    func usage(_ tiers: [Tier: (Double, TimeInterval?)]) -> UsageResponse {
        var u = UsageResponse()
        for (tier, (util, offset)) in tiers {
            u.tiers[tier] = TierUsage(utilization: util, resetsAt: offset.map(iso))
        }
        return u
    }

    func settings(_ change: (inout AlertSettings) -> Void = { _ in }) -> AlertSettings {
        var s = AlertSettings()
        s.enabled = true
        change(&s)
        return s
    }

    func run(_ u: UsageResponse, previous: UsageResponse? = nil, _ s: AlertSettings,
             fired: Set<String> = [], desk: Bool = false, at time: Date? = nil) -> AlertOutcome {
        AlertRules.evaluate(usage: u, previous: previous, settings: s, fired: fired,
                            deskRunning: desk, now: time ?? now, calendar: calendar)
    }

    // MARK: - Pace

    /// Session 88% with 2 hours of 5 left: 100% at 1:24 PM, well before the 3:00 PM reset.
    @Test func paceWarningNamesBothTimes() {
        let out = run(usage([.fiveHour: (88, 2 * hour)]), settings { $0.pace = true; $0.sessionLine = 95 })
        #expect(out.alerts.count == 1)
        let a = out.alerts[0]
        #expect(a.kind == .pace)
        #expect(a.title == "Session on pace to run out at 1:24 PM")
        #expect(a.body == "Resets 3:00 PM.")
        #expect(a.onceKey == "five_hour|\(iso(2 * 3600))|pace")
        #expect(out.banner)
    }

    @Test func paceWarningCoversWeeklyLimits() {
        let out = run(usage([.sevenDay: (90, 2 * day)]), settings { $0.pace = true; $0.weeklyLine = 95 })
        #expect(out.alerts.count == 1)
        #expect(out.alerts[0].title.hasPrefix("Weekly — All Models on pace to run out at Sat "))
        #expect(out.alerts[0].body == "Resets Sun 1:00 PM.")
    }

    @Test func paceWarningIsOffByDefault() {
        let out = run(usage([.fiveHour: (88, 2 * hour)]), settings { $0.sessionLine = 95 })
        #expect(out.alerts.isEmpty)
    }

    @Test func paceWarningOncePerWindow() {
        let s = settings { $0.pace = true; $0.sessionLine = 95 }
        let first = run(usage([.fiveHour: (88, 2 * hour)]), s)
        let again = run(usage([.fiveHour: (90, 2 * hour)]), s, fired: Set(first.alerts.map(\.onceKey)))
        #expect(first.alerts.count == 1)
        #expect(again.alerts.isEmpty)
        // A new window gets its own warning.
        let next = run(usage([.fiveHour: (88, 2 * hour + 5 * hour)]), s, fired: Set(first.alerts.map(\.onceKey)),
                       at: now.addingTimeInterval(5 * 3600))
        #expect(next.alerts.map(\.kind) == [.pace])
    }

    @Test func paceWarningWaitsForTenPercentOfTheWindow() {
        // 27 minutes into the 5 hours (9%), already projected to run out.
        let early = usage([.fiveHour: (25, 5 * hour - 27 * minute)])
        #expect(secondsUntilFull(util: 25, iso: iso(5 * 3600 - 27 * 60), tier: .fiveHour, now: now) != nil)
        #expect(run(early, settings { $0.pace = true }).alerts.isEmpty)
    }

    @Test func paceWarningNeedsTwentyPercentUsed() {
        // 45 minutes in (15%) at 19%: projected to run out, but too little used to say so.
        let light = usage([.fiveHour: (19, 5 * hour - 45 * minute)])
        #expect(secondsUntilFull(util: 19, iso: iso(5 * 3600 - 45 * 60), tier: .fiveHour, now: now) != nil)
        #expect(run(light, settings { $0.pace = true }).alerts.isEmpty)
    }

    @Test func noPaceWarningWhenTheResetComesFirst() {
        let out = run(usage([.fiveHour: (40, 2 * hour)]), settings { $0.pace = true })
        #expect(out.alerts.isEmpty)
    }

    // MARK: - Thresholds, as before

    @Test func thresholdsFireAsBefore() {
        let u = usage([.fiveHour: (85, 2 * hour), .sevenDay: (81, 2 * day), .sevenDaySonnet: (40, 2 * day)])
        let out = run(u, settings())
        #expect(out.alerts.map(\.title) == ["Session at 85%", "Weekly — All Models at 81%"])
        #expect(out.alerts[0].body == "Resets 3:00 PM.")
        #expect(out.alerts[0].onceKey == "five_hour|\(iso(2 * 3600))|line")
        #expect(out.alerts[1].onceKey == "seven_day|\(iso(2 * 86400))|line")
    }

    @Test func fullSessionOnlyWhenAsked() {
        let u = usage([.fiveHour: (100, hour)])
        #expect(run(u, settings()).alerts.map(\.kind) == [.line])
        let out = run(u, settings { $0.sessionFull = true })
        #expect(out.alerts.map(\.kind) == [.line, .full])
        #expect(out.alerts[1].title == "Session limit reached")
    }

    @Test func nothingWhenAlertsAreOff() {
        var s = settings { $0.pace = true; $0.sessionReset = true }
        s.enabled = false
        #expect(run(usage([.fiveHour: (99, hour)]), s) == AlertOutcome())
    }

    @Test func eachAlertOncePerWindow() {
        let u = usage([.fiveHour: (100, hour), .sevenDay: (90, day)])
        let s = settings { $0.sessionFull = true }
        let first = run(u, s)
        #expect(first.alerts.count == 3)
        #expect(run(u, s, fired: Set(first.alerts.map(\.onceKey))).alerts.isEmpty)
    }

    // MARK: - Resets

    @Test func sessionResetOnASharpDrop() {
        let before = usage([.fiveHour: (70, 10 * minute)])
        let after = usage([.fiveHour: (5, 5 * hour)])
        let out = run(after, previous: before, settings { $0.sessionReset = true })
        #expect(out.alerts.map(\.title) == ["Session reset"])
        #expect(out.alerts[0].onceKey == "five_hour|\(iso(600))|reset")
        // Off unless asked.
        #expect(run(after, previous: before, settings()).alerts.isEmpty)
    }

    @Test func sessionResetWhenTheOldWindowEnded() {
        // 60% to 30% is not a 40-point drop, but the old window's reset time has passed.
        let before = usage([.fiveHour: (60, -minute)])
        let after = usage([.fiveHour: (30, 5 * hour)])
        #expect(run(after, previous: before, settings { $0.sessionReset = true }).alerts.map(\.kind) == [.reset])
        // The same numbers inside one window are just a correction, not a reset.
        let early = usage([.fiveHour: (60, hour)])
        #expect(run(after, previous: early, settings { $0.sessionReset = true }).alerts.isEmpty)
    }

    @Test func quietMetersDoNotAnnounceAReset() {
        let before = usage([.fiveHour: (40, -minute)])
        let after = usage([.fiveHour: (0, nil)])
        #expect(run(after, previous: before, settings { $0.sessionReset = true }).alerts.isEmpty)
    }

    @Test func weeklyResetPerTier() {
        let before = usage([.sevenDay: (75, -minute), .sevenDaySonnet: (55, 3 * day)])
        let after = usage([.sevenDay: (1, 7 * day), .sevenDaySonnet: (10, 3 * day)])
        let out = run(after, previous: before, settings { $0.weeklyReset = true })
        #expect(out.alerts.map(\.title) == ["Weekly — All Models reset", "Weekly — Sonnet reset"])
        #expect(out.alerts[0].onceKey == "seven_day|\(iso(-60))|reset")
        // The session switch does not cover weekly limits, and the weekly one not the session.
        #expect(run(after, previous: before, settings { $0.sessionReset = true }).alerts.isEmpty)
        let session = run(usage([.fiveHour: (2, 5 * hour)]), previous: usage([.fiveHour: (80, -minute)]),
                          settings { $0.weeklyReset = true })
        #expect(session.alerts.isEmpty)
    }

    @Test func resetOncePerWindow() {
        let before = usage([.sevenDay: (75, -minute)])
        let after = usage([.sevenDay: (1, 7 * day)])
        let s = settings { $0.weeklyReset = true }
        let first = run(after, previous: before, s)
        #expect(run(after, previous: before, s, fired: Set(first.alerts.map(\.onceKey))).alerts.isEmpty)
    }

    // MARK: - Quiet hours and delivery

    @Test func quietHoursAcrossMidnight() {
        func at(_ h: Int, _ m: Int) -> Date { calendar.date(bySettingHour: h, minute: m, second: 0, of: now)! }
        let start = 22 * 60, end = 7 * 60
        #expect(AlertRules.isQuiet(at(22, 0), start: start, end: end, calendar: calendar))
        #expect(AlertRules.isQuiet(at(23, 30), start: start, end: end, calendar: calendar))
        #expect(AlertRules.isQuiet(at(3, 0), start: start, end: end, calendar: calendar))
        #expect(!AlertRules.isQuiet(at(7, 0), start: start, end: end, calendar: calendar))
        #expect(!AlertRules.isQuiet(at(12, 0), start: start, end: end, calendar: calendar))
        #expect(!AlertRules.isQuiet(at(21, 59), start: start, end: end, calendar: calendar))
    }

    @Test func quietHoursWithinADayAndEqualEnds() {
        func at(_ h: Int) -> Date { calendar.date(bySettingHour: h, minute: 0, second: 0, of: now)! }
        #expect(AlertRules.isQuiet(at(12), start: 9 * 60, end: 17 * 60, calendar: calendar))
        #expect(!AlertRules.isQuiet(at(18), start: 9 * 60, end: 17 * 60, calendar: calendar))
        #expect(!AlertRules.isQuiet(at(12), start: 600, end: 600, calendar: calendar))
    }

    @Test func quietHoursHoldBannersBackButKeepTheAlerts() {
        let s = settings { $0.quietEnabled = true; $0.quietStart = 12 * 60; $0.quietEnd = 14 * 60 }
        let out = run(usage([.fiveHour: (85, 2 * hour)]), s)
        #expect(out.alerts.count == 1)   // still recorded, so nothing bursts out at the end
        #expect(out.quiet)
        #expect(!out.banner)
        // Desk pulses still show.
        var both = s
        both.delivery = .both
        let pulsed = run(usage([.fiveHour: (85, 2 * hour)]), both, desk: true)
        #expect(!pulsed.banner && pulsed.pulse)
        // Off, or outside the hours: banners as usual.
        #expect(run(usage([.fiveHour: (85, 2 * hour)]), settings { $0.quietStart = 12 * 60; $0.quietEnd = 14 * 60 }).banner)
    }

    @Test func deliveryRoutes() {
        let u = usage([.fiveHour: (85, 2 * hour)])
        let desk = run(u, settings { $0.delivery = .desk }, desk: true)
        #expect(!desk.banner && desk.pulse)
        let fallback = run(u, settings { $0.delivery = .desk }, desk: false)
        #expect(fallback.banner && !fallback.pulse)
        let both = run(u, settings { $0.delivery = .both }, desk: true)
        #expect(both.banner && both.pulse)
        let banner = run(u, settings(), desk: true)
        #expect(banner.banner && !banner.pulse)
    }

    // MARK: - Settings and the shared pace math

    @Test func existingUsersKeepWhatTheyHad() {
        let d = MemoryDefaults()
        d.set(true, forKey: Notifier.Key.enabled)
        d.set(70.0, forKey: Notifier.Key.sessionPct)
        let s = AlertSettings(d)
        #expect(s.enabled && s.sessionLine == 70 && s.weeklyLine == 80)
        #expect(!s.pace && !s.weeklyReset && !s.quietEnabled)
        #expect(s.delivery == .banner && s.sound == AlertSound.standard)
        #expect(s.quietStart == 22 * 60 && s.quietEnd == 7 * 60)
    }

    @Test func settingsReadBack() {
        let d = MemoryDefaults()
        d.set("both", forKey: Notifier.Key.delivery)
        d.set("Glass", forKey: Notifier.Key.sound)
        d.set(true, forKey: Notifier.Key.quietEnabled)
        d.set(23 * 60, forKey: Notifier.Key.quietStart)
        d.set(6 * 60, forKey: Notifier.Key.quietEnd)
        let s = AlertSettings(d)
        #expect(s.delivery == .both && s.sound == "Glass" && s.quietEnabled)
        #expect(s.quietStart == 23 * 60 && s.quietEnd == 6 * 60)
    }

    @Test func burnProjectionReadsTheSharedCore() {
        #expect(burnProjection(util: 88, iso: iso(2 * 3600), tier: .fiveHour, now: now)?.text
                == "At current pace, expires in 24m")
        #expect(burnProjection(util: 100, iso: iso(3600), tier: .fiveHour, now: now)?.text == "Limit reached")
        #expect(secondsUntilFull(util: 100, iso: iso(3600), tier: .fiveHour, now: now) == 0)
        #expect(burnProjection(util: 40, iso: iso(2 * 3600), tier: .fiveHour, now: now) == nil)
        #expect(secondsUntilFull(util: 40, iso: iso(2 * 3600), tier: .fiveHour, now: now) == nil)
    }

    @Test func systemSoundNamesDropTheExtension() {
        let names = AlertSound.systemNames()
        #expect(names.contains("Glass"))
        #expect(!names.contains { $0.hasSuffix(".aiff") })
    }
}
