import Foundation
import AppKit
import UserNotifications

/// Usage alerts, off until turned on in Settings ▸ Alerts. Each alert fires once per tier per
/// reset window, so a meter sitting on the line does not repeat itself every refresh.
/// macOS Focus and Do Not Disturb still decide whether a banner shows.
final class Notifier: NSObject, UNUserNotificationCenterDelegate {
    static let shared = Notifier()

    enum Key {
        static let enabled = "alertsEnabled"
        static let sessionPct = "alertSessionPct"
        static let weeklyPct = "alertWeeklyPct"
        static let sessionFull = "remindSessionEnd"
        static let sessionReset = "alertSessionReset"
        static let fired = "alertsFired"
    }

    private let defaults = UserDefaults.standard
    private var lastSession: Double?

    private override init() {
        super.init()
        UNUserNotificationCenter.current().delegate = self
    }

    func requestPermission(_ done: @escaping (Bool) -> Void) {
        UNUserNotificationCenter.current().requestAuthorization(options: [.alert, .sound]) { granted, _ in
            DispatchQueue.main.async { done(granted) }
        }
    }

    /// Called after every successful refresh.
    func evaluate(_ usage: UsageResponse) {
        let session = usage.tiers[.fiveHour]?.utilization
        defer { lastSession = session }
        guard defaults.bool(forKey: Key.enabled) else { return }

        let sessionLine = defaults.object(forKey: Key.sessionPct) as? Double ?? 80
        let weeklyLine = defaults.object(forKey: Key.weeklyPct) as? Double ?? 80

        for tier in Tier.allCases {
            guard let t = usage.tiers[tier], let util = t.utilization else { continue }
            let window = t.resetsAt ?? "no-reset"
            if tier == .fiveHour {
                if util >= sessionLine {
                    once("\(tier.rawValue)|\(window)|line",
                         title: "Session at \(Int(util))%", body: resetText(t))
                }
                if defaults.bool(forKey: Key.sessionFull), util >= 100 {
                    once("\(tier.rawValue)|\(window)|full",
                         title: "Session limit reached", body: resetText(t))
                }
            } else if util >= weeklyLine {
                once("\(tier.rawValue)|\(window)|line",
                     title: "\(tier.label) at \(Int(util))%", body: resetText(t))
            }
        }

        // A session that was busy and is now nearly empty has reset.
        if defaults.bool(forKey: Key.sessionReset), let before = lastSession, let now = session,
           before >= 50, now <= before - 40 {
            post(id: "reset-\(Int(Date().timeIntervalSince1970))",
                 title: "Session reset", body: "Your 5-hour window is fresh.")
        }
    }

    func sendTest() {
        post(id: "test-\(Int(Date().timeIntervalSince1970))",
             title: "Sanduhr alerts are on", body: "This is what a usage alert looks like.")
    }

    // MARK: - Private

    private func once(_ key: String, title: String, body: String) {
        var fired = defaults.stringArray(forKey: Key.fired) ?? []
        guard !fired.contains(key) else { return }
        fired.append(key)
        defaults.set(Array(fired.suffix(60)), forKey: Key.fired)
        post(id: key, title: title, body: body)
    }

    private func post(id: String, title: String, body: String) {
        let content = UNMutableNotificationContent()
        content.title = title
        content.body = body
        content.sound = .default
        UNUserNotificationCenter.current().add(
            UNNotificationRequest(identifier: id, content: content, trigger: nil))
    }

    private func resetText(_ t: TierUsage) -> String {
        guard let s = t.resetsAt else { return "" }
        let iso = ISO8601DateFormatter()
        iso.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        let date = iso.date(from: s) ?? ISO8601DateFormatter().date(from: s)
        guard let date else { return "" }
        let f = DateFormatter()
        f.dateFormat = Calendar.current.isDateInToday(date) ? "h:mm a" : "EEE h:mm a"
        return "Resets \(f.string(from: date))."
    }

    // Show banners even while the widget is the active app.
    func userNotificationCenter(_ center: UNUserNotificationCenter,
                                willPresent notification: UNNotification,
                                withCompletionHandler completionHandler: @escaping (UNNotificationPresentationOptions) -> Void) {
        completionHandler([.banner, .sound])
    }
}
