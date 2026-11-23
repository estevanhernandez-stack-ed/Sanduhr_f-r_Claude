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
        /// Codex's alerts switch (Settings, Alerts): off until turned on.
        static let codexAlerts = "alertsCodex"
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
    /// The usage before the latest one, per source, for the reset alerts. Claude's is the one an
    /// account switch clears; Codex's has its own, so neither is compared with the other's numbers.
    private var lastUsages: [AgentSource: UsageResponse] = [:]

    private override init() {
        super.init()
        UNUserNotificationCenter.current().delegate = self
    }

    func requestPermission(_ done: @escaping (Bool) -> Void) {
        UNUserNotificationCenter.current().requestAuthorization(options: [.alert, .sound]) { granted, _ in
            DispatchQueue.main.async { done(granted) }
        }
    }

    /// Called after every successful refresh. `source` and `freshTiers` are for another source's
    /// readings (Codex's): its alerts carry the source in their once-keys and speak only for the
    /// fresh tiers. Claude's call, with neither, is what it always was.
    func evaluate(_ usage: UsageResponse, source: AgentSource = .claude, freshTiers: Set<Tier>? = nil) {
        let previous = lastUsages[source]
        lastUsages[source] = usage
        let settings = AlertSettings(defaults)
        var fired = defaults.stringArray(forKey: Key.fired) ?? []
        let outcome = AlertRules.evaluate(usage: usage, previous: previous, settings: settings,
                                          fired: Set(fired), deskRunning: deskCanPulse(source),
                                          now: Date(),
                                          hidden: source == .claude ? MeterVisibility.hidden(in: UserDefaults.desk) : [],
                                          source: source, freshTiers: freshTiers)
        guard !outcome.alerts.isEmpty else { return }
        // Recorded even when quiet hours hold the banner back, so nothing bursts out at the end.
        fired.append(contentsOf: outcome.alerts.map(\.onceKey))
        defaults.set(Array(fired.suffix(Self.firedLimit)), forKey: Key.fired)
        deliver(outcome, sound: settings.sound, source: source)
    }

    /// An account switch: the next fetch is the new account's first, so nothing is compared with
    /// the old account's numbers (a reset alert would fire off another account's baseline, as
    /// Windows' AlertEngine.Reset prevents). The once-per-window memory stays: its keys name each
    /// tier's reset time, which belongs to one account, so switching back never repeats an alert.
    func resetForSwitch() {
        lastUsages[.claude] = nil
    }

    /// Codex was turned off: its next reading is a first one, compared with nothing.
    func forget(_ source: AgentSource) {
        lastUsages[source] = nil
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

    /// Whether the Desk can pulse for `source`'s alert. Claude's is the Desk running, as it always was;
    /// Codex's also needs a meters piece that shows Codex (DeskController.canPulse), so Codex alerts reach
    /// the Desk only where there is a Codex meter to light. Called on the main thread, as both sources' readings are.
    private func deskCanPulse(_ source: AgentSource) -> Bool {
        guard source != .claude else { return DeskController.shared.running }
        return MainActor.assumeIsolated { DeskController.shared.canPulse(source) }
    }

    private func deliver(_ outcome: AlertOutcome, sound: String, source: AgentSource = .claude) {
        // A Desk pulse always glows the notch (DeskController.pulse); a banner alone glows it
        // when Settings, Desk, Notch, Glow asks. One glow either way, never two.
        if outcome.pulse {
            let tiers = Set(outcome.alerts.map(\.tier))
            DispatchQueue.main.async { MainActor.assumeIsolated { DeskController.shared.pulse(tiers, source: source) } }
        } else if outcome.banner {
            DispatchQueue.main.async { MainActor.assumeIsolated { NotchGlowController.shared.event(.alert) } }
        }
        guard outcome.banner else { return }
        for alert in outcome.alerts {
            post(id: alert.onceKey, title: alert.title, body: alert.body, sound: sound)
        }
        playSystemSound(sound)
    }

    private func post(id: String, title: String, body: String, sound: String) {
        post(.alert(id: id, title: title, body: body, sound: sound))
    }

    private func post(_ spec: BannerSpec) {
        let content = UNMutableNotificationContent()
        content.title = spec.title
        content.body = spec.body
        if let thread = spec.thread { content.threadIdentifier = thread }
        content.sound = spec.defaultSound ? .default : nil
        UNUserNotificationCenter.current().add(
            UNNotificationRequest(identifier: spec.id, content: content, trigger: nil))
    }

    /// What one banner carries, before it becomes a notification request. Pure, so the words, the thread and
    /// the sound are tested without Notification Center.
    struct BannerSpec: Equatable {
        var id: String
        var title: String
        var body: String
        var thread: String?
        /// The default notification sound. A named system sound is played with NSSound instead
        /// (playSystemSound): UNNotificationSound(named:) only looks in the app's own bundle and
        /// Library/Sounds, not /System/Library/Sounds.
        var defaultSound: Bool

        /// A limit alert's banner, as it has always been: no thread.
        static func alert(id: String, title: String, body: String, sound: String) -> BannerSpec {
            BannerSpec(id: id, title: title, body: body, thread: nil, defaultSound: sound == AlertSound.standard)
        }

        /// An agent's banner: its own thread, and the alert sound setting.
        static func agent(_ b: AgentBanner, sound: String) -> BannerSpec {
            BannerSpec(id: b.id, title: b.title, body: b.body, thread: b.thread, defaultSound: sound == AlertSound.standard)
        }
    }

    // MARK: - Agent notifications (2.47.0)

    /// An agent's turn ended or waits on you (AgentNotifier decided it may interrupt). Its own path beside
    /// `deliver`: Alerts' master switch and delivery route don't apply, the alert sound does. A session's newer
    /// banner replaces its older one (the identifier). If macOS notification permission is off, Notification
    /// Center drops it; nothing retries.
    func postAgent(_ banner: AgentBanner) {
        let sound = AlertSettings(defaults).sound
        post(.agent(banner, sound: sound))
        playSystemSound(sound)
    }

    /// The lock screen line stopped updating (2.47.1, #356; LockLineController.checkWarning decided). A system
    /// notice on its own path: Alerts' master switch and delivery route don't apply, the alert sound does, and
    /// quiet hours were checked by the caller. Clicking it opens Settings, Lock Screen.
    func lockLineStopped() {
        let sound = AlertSettings(defaults).sound
        post(BannerSpec(id: LockLineDaemon.warningID, title: LockLineDaemon.warningTitle,
                        body: LockLineDaemon.warningBody, thread: nil, defaultSound: sound == AlertSound.standard))
        playSystemSound(sound)
    }

    /// Shows the widget: a click on an agent notification. AppDelegate's showPanel by default; injectable.
    var showWidget: @MainActor () -> Void = { (NSApp.delegate as? AppDelegate)?.showPanel() }

    /// What a click on a notification opens.
    enum ClickTarget: Equatable {
        case settings(SettingsSection)
        case widget
    }

    /// The click for a notification's identifier, by its prefix; nil for one a click does nothing for (a limit
    /// alert).
    static func clickTarget(for id: String) -> ClickTarget? {
        if id.hasPrefix(AgentBanner.idPrefix) { return .widget }
        if id == LockLineDaemon.warningID { return .settings(.lockScreen) }
        let section: SettingsSection? = id.hasPrefix(deskSuggestionPrefix) ? .message
            : id.hasPrefix(themeSuggestionPrefix) ? .themes
            : id.hasPrefix(widgetSuggestionPrefix) || id.hasPrefix(spriteMotionPrefix) ? .widgets
            : id.hasPrefix(deskDesignSuggestionPrefix) ? .deskLayout
            : id.hasPrefix(ExpiredAccountNotice.idPrefix) ? .credentials : nil
        return section.map(ClickTarget.settings)
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

    // MARK: - An account you aren't using expired (2.43.1)

    /// ExpiredAccountNotice's banner, as gentle as a suggestion's (alerts on, the delivery and quiet hours as an
    /// alert's, no sound). Clicking it opens Settings, Accounts, where the account shows Session expired.
    /// Whether an expired-account banner may go out now: the gate a Desk message suggestion uses (Alerts on,
    /// the delivery choice, quiet hours). The caller records an account as announced only when this is true.
    var accountBannerAllowed: Bool {
        MessageSuggestionNotice.banner(AlertSettings(defaults), deskRunning: DeskController.shared.running, now: Date())
    }

    /// Posts the banner; call it only when `accountBannerAllowed` (ExpiredAccountNotice.toNotify checks it).
    func accountExpired(_ label: String) {
        let content = UNMutableNotificationContent()
        content.title = ExpiredAccountNotice.title(label)
        content.body = ExpiredAccountNotice.body
        content.sound = nil
        UNUserNotificationCenter.current().add(
            UNNotificationRequest(identifier: ExpiredAccountNotice.id(label), content: content, trigger: nil))
    }

    /// The account answered again, got a new key, or was signed out or removed: its banner leaves Notification Center.
    func clearAccountExpired(_ label: String) {
        UNUserNotificationCenter.current().removeDeliveredNotifications(withIdentifiers: [ExpiredAccountNotice.id(label)])
    }

    // MARK: - Claude's Desk message suggestions (item 54)

    /// A gentle banner for a suggestion, only when MessageSuggestionNotice allows (alerts on, the
    /// delivery and quiet hours as an alert's), never with a sound. Clicking it opens Settings,
    /// Message. Without it, the badge on Message is the sign.
    func suggestDeskMessages(_ p: MessageProposal) {
        guard MessageSuggestionNotice.banner(AlertSettings(defaults), deskRunning: DeskController.shared.running,
                                             now: Date()) else { return }
        let content = UNMutableNotificationContent()
        content.title = MessageSuggestionNotice.title(p)
        content.body = p.note ?? "Add, review or dismiss them in Sanduhr Settings, Message."
        content.sound = nil
        UNUserNotificationCenter.current().add(
            UNNotificationRequest(identifier: Self.deskSuggestionPrefix + p.id, content: content, trigger: nil))
    }

    /// The suggestion was decided: its banner leaves Notification Center.
    func clearDeskSuggestion(_ p: MessageProposal) {
        UNUserNotificationCenter.current().removeDeliveredNotifications(withIdentifiers: [Self.deskSuggestionPrefix + p.id])
    }

    static let deskSuggestionPrefix = "desk-messages-"

    // MARK: - Claude's theme suggestions (item 55)

    /// The same gentle banner for a suggested theme (MessageSuggestionNotice's rule: alerts on,
    /// the delivery and quiet hours as an alert's, no sound). Clicking it opens Settings, Themes.
    func suggestTheme(_ p: ThemeProposal) {
        guard MessageSuggestionNotice.banner(AlertSettings(defaults), deskRunning: DeskController.shared.running,
                                             now: Date()) else { return }
        let content = UNMutableNotificationContent()
        content.title = "Claude suggested a theme: \(p.name)"
        content.body = p.summary ?? "Save, apply or dismiss it in Sanduhr Settings, Themes."
        content.sound = nil
        UNUserNotificationCenter.current().add(
            UNNotificationRequest(identifier: Self.themeSuggestionPrefix + p.id, content: content, trigger: nil))
    }

    /// The suggestion was decided: its banner leaves Notification Center.
    func clearThemeSuggestion(_ p: ThemeProposal) {
        UNUserNotificationCenter.current().removeDeliveredNotifications(withIdentifiers: [Self.themeSuggestionPrefix + p.id])
    }

    static let themeSuggestionPrefix = "theme-suggestion-"

    // MARK: - Claude's widget suggestions (2.36.0)

    /// The same gentle banner for a proposed widget. Clicking it opens Settings, Widgets.
    func suggestWidget(_ m: WidgetReviewModel) {
        guard MessageSuggestionNotice.banner(AlertSettings(defaults), deskRunning: DeskController.shared.running,
                                             now: Date()) else { return }
        let content = UNMutableNotificationContent()
        content.title = "Claude suggested a widget: \(m.name)"
        content.body = m.note ?? "Preview, apply or dismiss it in Sanduhr Settings, Widgets."
        content.sound = nil
        UNUserNotificationCenter.current().add(
            UNNotificationRequest(identifier: Self.widgetSuggestionPrefix + m.id, content: content, trigger: nil))
    }

    /// The suggestion was decided: its banner leaves Notification Center.
    func clearWidgetSuggestion(id: String) {
        UNUserNotificationCenter.current().removeDeliveredNotifications(withIdentifiers: [Self.widgetSuggestionPrefix + id])
    }

    static let widgetSuggestionPrefix = "widget-suggestion-"

    // MARK: - Claude's sprite motion suggestions (2.39.0)

    /// The same gentle banner for a proposed motion. Clicking it opens Settings, Widgets.
    func suggestSpriteMotion(_ m: SpriteMotionReviewModel) {
        guard MessageSuggestionNotice.banner(AlertSettings(defaults), deskRunning: DeskController.shared.running,
                                             now: Date()) else { return }
        let content = UNMutableNotificationContent()
        content.title = "Claude suggested a motion: \(m.name)"
        content.body = m.note ?? "Preview, apply or decline it in Sanduhr Settings, Widgets."
        content.sound = nil
        UNUserNotificationCenter.current().add(
            UNNotificationRequest(identifier: Self.spriteMotionPrefix + m.id, content: content, trigger: nil))
    }

    func clearSpriteMotion(id: String) {
        UNUserNotificationCenter.current().removeDeliveredNotifications(withIdentifiers: [Self.spriteMotionPrefix + id])
    }

    static let spriteMotionPrefix = "sprite-motion-suggestion-"

    // MARK: - Claude's Desk design suggestions (2.37.0)

    /// The same gentle banner for a proposed Desk design. Clicking it opens Settings, Desk.
    func suggestDeskDesign(_ m: DeskDesignReviewModel) {
        guard MessageSuggestionNotice.banner(AlertSettings(defaults), deskRunning: DeskController.shared.running,
                                             now: Date()) else { return }
        let content = UNMutableNotificationContent()
        content.title = "Claude suggested a Desk design: \(m.name)"
        content.body = m.note ?? "Preview, apply or dismiss it in Sanduhr Settings, Desk."
        content.sound = nil
        UNUserNotificationCenter.current().add(
            UNNotificationRequest(identifier: Self.deskDesignSuggestionPrefix + m.id, content: content, trigger: nil))
    }

    /// The suggestion was decided: its banner leaves Notification Center.
    func clearDeskDesignSuggestion(id: String) {
        UNUserNotificationCenter.current().removeDeliveredNotifications(withIdentifiers: [Self.deskDesignSuggestionPrefix + id])
    }

    static let deskDesignSuggestionPrefix = "desk-design-suggestion-"

    func userNotificationCenter(_ center: UNUserNotificationCenter,
                                didReceive response: UNNotificationResponse,
                                withCompletionHandler completionHandler: @escaping () -> Void) {
        switch Self.clickTarget(for: response.notification.request.identifier) {
        case .settings(let section):
            DispatchQueue.main.async { MainActor.assumeIsolated { SettingsWindowController.shared.show(section) } }
        case .widget:
            let show = showWidget
            DispatchQueue.main.async { MainActor.assumeIsolated { show() } }
        case nil:
            break
        }
        completionHandler()
    }

    // Show banners even while the widget is the active app.
    func userNotificationCenter(_ center: UNUserNotificationCenter,
                                willPresent notification: UNNotification,
                                withCompletionHandler completionHandler: @escaping (UNNotificationPresentationOptions) -> Void) {
        completionHandler([.banner, .sound])
    }
}
