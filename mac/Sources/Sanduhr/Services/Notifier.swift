import Foundation
import AppKit
import UserNotifications

/// Usage alerts, off until turned on in Settings ▸ Alerts. AlertRules decides what to say; this
/// shell remembers what already fired (each alert comes once per tier per reset window, so a
/// meter sitting on the line does not repeat itself every refresh), posts banners with the
/// chosen sound and asks Desk to pulse. macOS Focus and Do Not Disturb still decide whether a
/// banner shows.
final class Notifier: NSObject, UNUserNotificationCenterDelegate {
    static let shared = Notifier()

    enum Key {
        static let enabled = "alertsEnabled"
        static let sessionPct = "alertSessionPct"
        static let weeklyPct = "alertWeeklyPct"
        static let sessionFull = "remindSessionEnd"
        static let sessionReset = "alertSessionReset"
        static let fired = "alertsFired"
        static let weeklyReset = "alertWeeklyReset"
        static let pace = "alertPace"
        static let delivery = "alertDelivery"
        static let quietEnabled = "alertQuietEnabled"
        static let quietStart = "alertQuietStart"
        static let quietEnd = "alertQuietEnd"
        static let sound = "alertSound"
    }

    /// How many once-per-window keys are kept, newest last. Enough for a week of session windows
    /// beside the weekly ones, so a weekly key is not forgotten while its window still runs.
    static let firedLimit = 200

    private let defaults = UserDefaults.standard
    private var lastUsage: UsageResponse?

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
        let previous = lastUsage
        lastUsage = usage
        let settings = AlertSettings(defaults)
        var fired = defaults.stringArray(forKey: Key.fired) ?? []
        let outcome = AlertRules.evaluate(usage: usage, previous: previous, settings: settings,
                                          fired: Set(fired), deskRunning: DeskController.shared.running,
                                          now: Date())
        guard !outcome.alerts.isEmpty else { return }
        // Recorded even when quiet hours hold the banner back, so nothing bursts out at the end.
        fired.append(contentsOf: outcome.alerts.map(\.onceKey))
        defaults.set(Array(fired.suffix(Self.firedLimit)), forKey: Key.fired)
        deliver(outcome, sound: settings.sound)
    }

    /// A sample alert through the chosen delivery and sound. Quiet hours do not apply to it.
    func sendTest() {
        let settings = AlertSettings(defaults)
        let route = AlertRules.route(settings.delivery, deskRunning: DeskController.shared.running, quiet: false)
        let sample = AlertDecision(kind: .line, tier: .fiveHour, title: "Sanduhr alerts are on",
                                   body: "This is what a usage alert looks like.",
                                   onceKey: "test-\(Int(Date().timeIntervalSince1970))")
        deliver(AlertOutcome(alerts: [sample], banner: route.banner, pulse: route.pulse), sound: settings.sound)
    }

    /// Plays a sound the way an alert would. The default notification sound cannot be played
    /// outside a notification, so it and None play nothing.
    static func preview(sound: String) {
        guard sound != AlertSound.standard, sound != AlertSound.none else { return }
        NSSound(named: NSSound.Name(sound))?.play()
    }

    // MARK: - Private

    private func deliver(_ outcome: AlertOutcome, sound: String) {
        if outcome.pulse {
            let tiers = Set(outcome.alerts.map(\.tier))
            DispatchQueue.main.async { DeskController.shared.model.pulse(tiers) }
        }
        guard outcome.banner else { return }
        for alert in outcome.alerts {
            post(id: alert.onceKey, title: alert.title, body: alert.body, sound: sound)
        }
        playSystemSound(sound)
    }

    private func post(id: String, title: String, body: String, sound: String) {
        let content = UNMutableNotificationContent()
        content.title = title
        content.body = body
        // A named system sound is played with NSSound (playSystemSound): UNNotificationSound(named:)
        // only looks in the app's own bundle and Library/Sounds, not /System/Library/Sounds.
        content.sound = sound == AlertSound.standard ? .default : nil
        UNUserNotificationCenter.current().add(
            UNNotificationRequest(identifier: id, content: content, trigger: nil))
    }

    /// Once per delivery, not per banner, and only while Sanduhr's notification sounds are on
    /// in System Settings.
    private func playSystemSound(_ sound: String) {
        guard sound != AlertSound.standard, sound != AlertSound.none else { return }
        UNUserNotificationCenter.current().getNotificationSettings { settings in
            guard settings.soundSetting == .enabled, settings.authorizationStatus == .authorized else { return }
            DispatchQueue.main.async { Self.preview(sound: sound) }
        }
    }

    // Show banners even while the widget is the active app.
    func userNotificationCenter(_ center: UNUserNotificationCenter,
                                willPresent notification: UNNotification,
                                withCompletionHandler completionHandler: @escaping (UNNotificationPresentationOptions) -> Void) {
        completionHandler([.banner, .sound])
    }
}
