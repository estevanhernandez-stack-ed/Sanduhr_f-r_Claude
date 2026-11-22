import Foundation

/// What the person chose on Settings, Notifications (design section 5), plus the Alerts quiet hours
/// the rules honor. The caller reads the defaults; this is the pure shape the rules take.
/// `threshold` is in seconds, 0 meaning every turn.
struct NotificationSettings {
    static let thresholds: [TimeInterval] = [0, 30, 60, 120, 300]
    static let defaultThreshold: TimeInterval = 60

    var banner = true
    var card = true
    var glowFinished = false
    var glowWaiting = false
    var skipInFront = true
    /// Claude Code's idle reminder, off by default.
    var idle = false
    var threshold: TimeInterval = defaultThreshold
    var quiet: (enabled: Bool, start: Int, end: Int) = (false, 0, 0)
}

/// Where one event shows. `glowColorHex` is the agent's Sources color for a finished or stopped glow,
/// nil for the notch ink (waiting, idle).
struct NotificationDecision: Equatable {
    var banner: Bool
    var glow: Bool
    var card: Bool
    var glowColorHex: String?

    static let none = NotificationDecision(banner: false, glow: false, card: false, glowColorHex: nil)
}

/// The limiter, kept per agent session: one kind at most every 20 seconds, and no finished within
/// 5 seconds of a waiting. It is the glow's limiter (`ClaudeCodeGlowLimiter`), one per session; only an
/// interruption that happens counts.
struct NotificationLimiter {
    static let maxSessions = 64

    private struct Key: Hashable { var agent: AgentSource; var session: String }
    private var bySession: [Key: ClaudeCodeGlowLimiter] = [:]

    var sessionCount: Int { bySession.count }

    mutating func allow(_ e: AgentTurnEvent, now: Date) -> Bool {
        let key = Key(agent: e.agent, session: e.session)
        var limiter = bySession[key] ?? ClaudeCodeGlowLimiter()
        let allowed = limiter.allow(e.kind == .waiting || e.kind == .idle ? .waiting : .done, now: now)
        if bySession.count >= Self.maxSessions, bySession[key] == nil { bySession.removeAll() }
        bySession[key] = limiter
        return allowed
    }
}

/// Section 3 of the design (and Windows section 11) in one place: which surfaces an event uses.
enum NotificationRules {
    /// Whether the front app means "you are looking at this agent". Claude Code and the Antigravity CLI:
    /// a terminal or editor. Codex adds its own app, which is also matched by name or an OpenAI bundle id
    /// prefix, as its real bundle id is not verified (decision D3).
    static func inFront(_ agent: AgentSource, bundleID: String?, name: String?) -> Bool {
        if ClaudeCodeGlowRules.isTerminal(bundleID) { return true }
        guard agent == .codex else { return false }
        if let bundleID, bundleID.hasPrefix("com.openai.") { return true }
        if let name, name.trimmingCharacters(in: .whitespacesAndNewlines).caseInsensitiveCompare("Codex") == .orderedSame { return true }
        return false
    }

    /// The surfaces for `e`. The front app is asked at most once, and only when an interrupt is
    /// otherwise due (the agent's event is not a start, the threshold lets it through, a banner or glow
    /// is on, it is not quiet hours) and "skip in front" is on. The limiter is asked last, so an event
    /// the quiet hours or the front app stops does not use up the limit. Quiet hours hold the banner and
    /// the glow and leave the card. The idle reminder never makes a card.
    static func decide(_ e: AgentTurnEvent, settings s: NotificationSettings,
                       frontBundleID: () -> String?, frontName: () -> String?,
                       sourceColorHex: String, limiter: inout NotificationLimiter, now: Date,
                       calendar: Calendar) -> NotificationDecision {
        let idle = e.kind == .idle
        if idle && !s.idle { return .none }
        let card = s.card && !idle
        var held = NotificationDecision(banner: false, glow: false, card: card, glowColorHex: nil)
        if e.kind == .started { return held }

        let waits = e.kind == .waiting || idle
        if !waits && !longEnough(e.duration, s.threshold) { return held }

        let banner = s.banner
        let glow = waits ? s.glowWaiting : s.glowFinished
        if !banner && !glow { return held }
        if s.quiet.enabled, AlertRules.isQuiet(now, start: s.quiet.start, end: s.quiet.end, calendar: calendar) { return held }
        if s.skipInFront, inFront(e.agent, bundleID: frontBundleID(), name: frontName()) { return held }
        if !limiter.allow(e, now: now) { return held }

        held.banner = banner
        held.glow = glow
        held.glowColorHex = glow && !waits ? sourceColorHex : nil
        return held
    }

    /// "Every turn" (0) lets anything through; otherwise the turn's length must be known and long enough.
    private static func longEnough(_ duration: TimeInterval?, _ threshold: TimeInterval) -> Bool {
        threshold <= 0 || (duration.map { $0 >= threshold } ?? false)
    }
}
