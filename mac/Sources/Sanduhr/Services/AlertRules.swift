import Foundation

/// Where an alert shows: a macOS banner, a pulse on the Desk meters and notch island, or both.
enum AlertDelivery: String, CaseIterable, Identifiable {
    case banner, desk, both

    var id: String { rawValue }

    var title: String {
        switch self {
        case .banner: "Banner"
        case .desk: "Desk pulse"
        case .both: "Banner and Desk pulse"
        }
    }
}

/// The alert settings as Settings ▸ Alerts saves them. The defaults are what an existing user
/// already had: banners with the default sound, and every new alert off.
struct AlertSettings: Equatable {
    var enabled = false
    var sessionLine = 80.0
    var weeklyLine = 80.0
    var sessionFull = false
    var sessionReset = false
    var weeklyReset = false
    var pace = false
    var delivery: AlertDelivery = .banner
    var quietEnabled = false
    /// Minutes since midnight.
    var quietStart = 22 * 60
    var quietEnd = 7 * 60
    /// A name from /System/Library/Sounds, `AlertSound.none` or `AlertSound.standard`.
    var sound = AlertSound.standard

    init() {}

    init(_ d: DefaultsStore) {
        enabled = d.bool(forKey: Notifier.Key.enabled)
        sessionLine = d.object(forKey: Notifier.Key.sessionPct) as? Double ?? sessionLine
        weeklyLine = d.object(forKey: Notifier.Key.weeklyPct) as? Double ?? weeklyLine
        sessionFull = d.bool(forKey: Notifier.Key.sessionFull)
        sessionReset = d.bool(forKey: Notifier.Key.sessionReset)
        weeklyReset = d.bool(forKey: Notifier.Key.weeklyReset)
        pace = d.bool(forKey: Notifier.Key.pace)
        delivery = (d.object(forKey: Notifier.Key.delivery) as? String).flatMap(AlertDelivery.init) ?? delivery
        quietEnabled = d.bool(forKey: Notifier.Key.quietEnabled)
        quietStart = d.object(forKey: Notifier.Key.quietStart) as? Int ?? quietStart
        quietEnd = d.object(forKey: Notifier.Key.quietEnd) as? Int ?? quietEnd
        sound = d.object(forKey: Notifier.Key.sound) as? String ?? sound
    }
}

/// The sound choices: macOS's default notification sound, silence, or a system sound by name.
enum AlertSound {
    static let standard = "default"
    static let none = "none"

    /// The system sounds by name, without the extension ("Glass", "Ping"), sorted.
    static func systemNames(in dir: URL = URL(fileURLWithPath: "/System/Library/Sounds")) -> [String] {
        let files = (try? FileManager.default.contentsOfDirectory(atPath: dir.path)) ?? []
        return Set(files.filter { !$0.hasPrefix(".") }.map { ($0 as NSString).deletingPathExtension })
            .sorted()
    }
}

/// One alert to show: what it says, which limit it is about, and the key that keeps it to once
/// per reset window ("<tier>|<resetsAt>|<kind>").
struct AlertDecision: Equatable {
    enum Kind: String { case line, full, pace, reset }
    let kind: Kind
    let tier: Tier
    let title: String
    let body: String
    let onceKey: String
}

/// What one refresh asks for: the alerts, and how they are delivered.
struct AlertOutcome: Equatable {
    var alerts: [AlertDecision] = []
    /// Post a banner for each alert.
    var banner = false
    /// Pulse the matching Desk meters and the notch island.
    var pulse = false
    /// Quiet hours held the banners back.
    var quiet = false
}

/// The alert decisions, kept apart from UserNotifications and AppKit so they can be tested.
/// Notifier hands in the latest usage, the one before it, the settings and the keys already
/// fired; it gets back what to show and where.
enum AlertRules {
    /// A pace warning needs the window at least this far along, so the first minutes of a fresh
    /// window do not cry wolf.
    static let paceMinElapsed = 0.10
    /// And the limit at least this full.
    static let paceMinUtil = 20.0

    static func evaluate(usage: UsageResponse, previous: UsageResponse?, settings s: AlertSettings,
                         fired: Set<String>, deskRunning: Bool, now: Date,
                         hidden: Set<Tier> = [],
                         calendar: Calendar = .current) -> AlertOutcome {
        guard s.enabled else { return AlertOutcome() }
        var alerts: [AlertDecision] = []
        var seen = fired
        func add(_ kind: AlertDecision.Kind, _ tier: Tier, _ window: String, _ title: String, _ body: String) {
            let key = "\(tier.rawValue)|\(window)|\(kind.rawValue)"
            guard seen.insert(key).inserted else { return }
            alerts.append(AlertDecision(kind: kind, tier: tier, title: title, body: body, onceKey: key))
        }

        // A limit hidden in Settings, Alerts, Each limit never alerts (MeterVisibility).
        let shown = MeterVisibility.visible(usage, hidden: hidden) ?? usage
        for tier in Tier.allCases {
            guard let t = shown.tiers[tier], let util = t.utilization else { continue }
            let window = t.resetsAt ?? "no-reset"
            let name = tier == .fiveHour ? "Session" : tier.label
            let resets = resetText(t.resetsAt, now: now, calendar: calendar)
            if util >= (tier == .fiveHour ? s.sessionLine : s.weeklyLine) {
                add(.line, tier, window, "\(name) at \(Int(util))%", resets)
            }
            if tier == .fiveHour, s.sessionFull, util >= 100 {
                add(.full, tier, window, "Session limit reached", resets)
            }
            if s.pace, util >= paceMinUtil,
               let elapsed = paceFrac(t.resetsAt, tier: tier, now: now), elapsed >= paceMinElapsed,
               let secs = secondsUntilFull(util: util, iso: t.resetsAt, tier: tier, now: now), secs > 0 {
                let at = clock(now.addingTimeInterval(secs), now: now, calendar: calendar)
                add(.pace, tier, window, "\(name) on pace to run out at \(at)", resets)
            }
            if tier == .fiveHour ? s.sessionReset : s.weeklyReset,
               let before = previous?.tiers[tier], didReset(before: before, after: t, now: now) {
                // Keyed by the window that ended, which is known even when the new one has no
                // reset time yet.
                let ended = before.resetsAt ?? "ended-\(Int(now.timeIntervalSince1970))"
                if tier == .fiveHour {
                    add(.reset, tier, ended, "Session reset", "Your 5-hour window is fresh.")
                } else {
                    add(.reset, tier, ended, "\(tier.label) reset", "A fresh week starts now.")
                }
            }
        }

        guard !alerts.isEmpty else { return AlertOutcome() }
        let quiet = s.quietEnabled && isQuiet(now, start: s.quietStart, end: s.quietEnd, calendar: calendar)
        let route = route(s.delivery, deskRunning: deskRunning, quiet: quiet)
        return AlertOutcome(alerts: alerts, banner: route.banner, pulse: route.pulse, quiet: quiet)
    }

    /// Banner, pulse or both. Desk-only delivery falls back to a banner while Desk is off, so an
    /// alert never goes nowhere; quiet hours hold banners back and leave pulses alone.
    static func route(_ delivery: AlertDelivery, deskRunning: Bool, quiet: Bool) -> (banner: Bool, pulse: Bool) {
        let banner = (delivery != .desk || !deskRunning) && !quiet
        let pulse = delivery != .banner && deskRunning
        return (banner, pulse)
    }

    /// A limit reset when a busy meter (50% or more) has either dropped by 40 points or more, or
    /// its reset time has passed and it now reads lower. The first catches a reset however the
    /// server reports the new window; the second catches one where the new window filled
    /// quickly, or the meter was not busy enough for a 40-point drop to show.
    static func didReset(before: TierUsage, after: TierUsage, now: Date) -> Bool {
        guard let was = before.utilization, let isNow = after.utilization, was >= 50 else { return false }
        if isNow <= was - 40 { return true }
        if let ended = parseISO(before.resetsAt), ended <= now, isNow < was { return true }
        return false
    }

    /// Whether `now` falls in quiet hours (minutes since midnight). The range may cross
    /// midnight (22:00 to 07:00); equal start and end means no quiet hours.
    static func isQuiet(_ now: Date, start: Int, end: Int, calendar: Calendar = .current) -> Bool {
        guard start != end else { return false }
        let c = calendar.dateComponents([.hour, .minute], from: now)
        let m = (c.hour ?? 0) * 60 + (c.minute ?? 0)
        return start < end ? (m >= start && m < end) : (m >= start || m < end)
    }

    /// "Resets 5:00 PM." today, "Resets Thu 9:00 AM." on another day, empty without a time.
    static func resetText(_ iso: String?, now: Date, calendar: Calendar = .current) -> String {
        guard let date = parseISO(iso) else { return "" }
        return "Resets \(clock(date, now: now, calendar: calendar))."
    }

    /// "3:40 PM" today, "Thu 3:40 PM" on another day.
    static func clock(_ date: Date, now: Date, calendar: Calendar) -> String {
        let f = DateFormatter()
        f.calendar = calendar
        f.timeZone = calendar.timeZone
        f.locale = calendar.locale ?? .current
        f.dateFormat = calendar.isDate(date, inSameDayAs: now) ? "h:mm a" : "EEE h:mm a"
        return f.string(from: date)
    }
}
