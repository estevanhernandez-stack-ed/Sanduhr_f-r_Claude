import Foundation

/// The settings Settings ▸ Notifications saves and `NotificationRules` reads (design section 5). The new keys live
/// in the app's standard defaults. The glow switches and "not while in front" are the existing keys in the Desk
/// defaults (`notchGlowClaudeDone`, `notchGlowClaudeWaiting`, `notchGlowClaudeSkipTerminal`): the page and
/// Notch ▸ Glow show the same switches, so nothing about them is migrated and the legacy glow keeps reading them.
enum AgentNotificationSettings {
    static let bannerKey = "agentNotifications.banner"
    static let cardKey = "agentNotifications.card"
    /// Seconds; 0 means every turn.
    static let thresholdKey = "agentNotifications.threshold"
    static let idleKey = "agentNotifications.idle"
    /// Set once the upgrade rule has run, so it never runs again.
    static let migratedKey = "agentNotifications.migrated"

    /// The rules' settings: the glow switches from the Desk defaults, the new keys from the standard ones at the
    /// plan's defaults, and Alerts' quiet hours. A threshold that is not one of the five choices reads as the default.
    static func load(standard: DefaultsStore, desk: DefaultsStore) -> NotificationSettings {
        var s = NotificationSettings()
        s.banner = standard.object(forKey: bannerKey) as? Bool ?? true
        s.card = standard.object(forKey: cardKey) as? Bool ?? true
        s.idle = standard.object(forKey: idleKey) as? Bool ?? false
        if let seconds = (standard.object(forKey: thresholdKey) as? NSNumber)?.doubleValue,
           NotificationSettings.thresholds.contains(seconds) {
            s.threshold = seconds
        }
        let glow = NotchGlowSwitches(desk)
        s.glowFinished = glow.claudeDone
        s.glowWaiting = glow.claudeWaiting
        s.skipInFront = glow.claudeSkipTerminal
        let alerts = AlertSettings(standard)
        s.quiet = (alerts.quietEnabled, alerts.quietStart, alerts.quietEnd)
        return s
    }

    /// The upgrade rule, once: the idle reminder is on if "glow when Claude Code is waiting" was on (the old glow
    /// also fired on Claude Code's idle reminder, so someone who had it keeps the reminder). Nothing else moves,
    /// and a choice already saved is kept. Call at launch.
    static func migrateOnce(standard: DefaultsStore, desk: DefaultsStore) {
        guard !standard.bool(forKey: migratedKey) else { return }
        if standard.object(forKey: idleKey) == nil, desk.bool(forKey: NotchGlowSwitches.claudeWaitingKey) {
            standard.set(true, forKey: idleKey)
        }
        standard.set(true, forKey: migratedKey)
    }

    /// The threshold's five choices, in order.
    static let thresholdChoices: [(seconds: TimeInterval, title: String)] = [
        (0, "Every turn"), (30, "30 seconds"), (60, "1 minute"), (120, "2 minutes"), (300, "5 minutes"),
    ]
}

/// Settings ▸ Notifications' words, in one place so the page and its tests agree. House voice: commas and
/// periods, no em-dashes.
enum NotificationsPageCopy {
    static let title = "Notifications"
    static let intro = "Know when an agent finishes a long turn or needs you. Sanduhr reads this from each agent's own files on this Mac, and from the Claude Code hook; nothing is sent anywhere."

    static let agentsHeader = "Agents"
    static let showHeader = "Where it shows"
    static let whenHeader = "When"

    static func supports(_ agent: AgentSource) -> String {
        switch agent {
        case .claude: "Working, finished and waiting"
        case .codex: "Working and finished"
        case .antigravity: "Working, finished about a minute and a half late"
        }
    }

    /// Codex's and Antigravity's rows while their source is off.
    static let sourceOff = "Switch it on in Usage ▸ Sources"
    static let sourceOn = "On"

    static let banner = "Show a macOS notification"
    static let glowFinished = "Glow when an agent finishes"
    static let glowWaiting = "Glow when an agent is waiting on you"
    static let glowCaption = "The notch glows in the agent's color when it finishes. Where it shows is set on Notch."
    static let card = "Show a card on the Desk"
    static let cardCaption = "Shows where the watcher cards piece is placed."
    static let threshold = "Only for turns longer than"
    static let thresholdCaption = "Applies to finished turns. Waiting on you always counts."
    static let skipInFront = "Not while the agent's app is in front"
    static let inFrontCaption = "A terminal or an editor in front counts for every agent. The Codex app also counts for Codex."
    static let idle = "Remind me when Claude Code has waited a minute for my next prompt"
    static let idleCaption = "Claude Code only. Off, only a permission prompt or a question counts as waiting on you."
    static let quietCaption = "Quiet hours on Alerts hold the notification and the glow."
    static let tryIt = "Try it"
    static let tryHelp = "Shows a sample, whatever the switches say."

    static let hookMissing = "Needs the Claude Code hook. Install it on the Claude Code page."
    static let hookOutdated = "The Claude Code hook here is an older one. Update it to get the notification and the Desk card."
    static let hookUnreadable = "A Claude Code folder's settings can't be read. See the Claude Code page."
    static let update = "Update"

    /// The lines under Claude Code's row: what is missing, if anything. A hook installed in at least one folder
    /// is all Claude Code's notifications need (decision D9); an older one or an unreadable file still gets a line.
    static func hookNotes(_ statuses: [IntegrationStatus]) -> [String] {
        var notes: [String] = []
        if !statuses.contains(where: { $0 == .installed || $0 == .outdated }) { notes.append(hookMissing) }
        if statuses.contains(.outdated) { notes.append(hookOutdated) }
        if statuses.contains(.unreadable) { notes.append(hookUnreadable) }
        return notes
    }

    /// A listed folder's status, in the Claude Code page's words.
    static func hookStatus(_ s: IntegrationStatus) -> String {
        switch s {
        case .installed: "Installed"
        case .outdated: "Outdated"
        case .notInstalled, .other: "Not installed"
        case .unreadable: "Can't be read"
        }
    }

    /// Folders with the hook, an older one or an unreadable settings file are listed; the rest would only be noise.
    static func listsFolder(_ s: IntegrationStatus) -> Bool {
        switch s {
        case .installed, .outdated, .unreadable: true
        case .notInstalled, .other: false
        }
    }
}
