import Foundation

/// What Claude Code tells Sanduhr through its hooks (item 51): only that a session waits on you
/// or finished a turn. Nothing about the conversation ever comes with it.
enum ClaudeCodeEvent: String, CaseIterable, Sendable {
    /// `Notification`: a permission prompt, an input request, or the idle reminder.
    case waiting
    /// `Stop`: a turn finished.
    case done

    /// The Glow switch that governs it.
    var kind: NotchGlowEvent.Kind {
        switch self {
        case .waiting: .claudeWaiting
        case .done: .claudeDone
        }
    }
}

/// `sanduhr://claude-code?event=waiting|done`, the one public link that reaches the glow without
/// the debug gate. Strict on purpose: the host, no path, exactly one query item named `event`
/// with a known value. Anything else is no event, and the link is dropped.
enum ClaudeCodeLink {
    static let scheme = "sanduhr"
    static let host = "claude-code"

    /// The link the hooks open for `event`.
    static func url(_ event: ClaudeCodeEvent) -> String { "\(scheme)://\(host)?event=\(event.rawValue)" }

    /// Addressed to the Claude Code handler, whatever it carries (so a bad one is dropped there
    /// rather than handed on).
    static func isClaudeCode(_ url: URL) -> Bool {
        url.scheme?.lowercased() == scheme && url.host?.lowercased() == host
    }

    /// The event a link carries, nil for anything that isn't exactly one known event.
    static func event(_ url: URL) -> ClaudeCodeEvent? {
        guard isClaudeCode(url), url.path.isEmpty || url.path == "/",
              url.user == nil, url.password == nil, url.port == nil, url.fragment == nil,
              let c = URLComponents(url: url, resolvingAgainstBaseURL: false),
              let items = c.queryItems, items.count == 1, items[0].name == "event",
              let value = items[0].value else { return nil }
        return ClaudeCodeEvent(rawValue: value)
    }
}

/// At most one glow per kind of event every 20 seconds, and no "finished" glow within 5 seconds
/// of a "waiting" one (a turn that ends by asking does both). Only a glow that happens counts.
struct ClaudeCodeGlowLimiter: Equatable {
    static let sameKindGap: TimeInterval = 20
    static let doneAfterWaiting: TimeInterval = 5

    private(set) var last: [ClaudeCodeEvent: Date] = [:]

    /// Whether `event` may glow at `now`; remembers it when it may. A clock set back allows it.
    mutating func allow(_ event: ClaudeCodeEvent, now: Date) -> Bool {
        if let t = last[event], Self.within(Self.sameKindGap, from: t, to: now) { return false }
        if event == .done, let w = last[.waiting], Self.within(Self.doneAfterWaiting, from: w, to: now) { return false }
        last[event] = now
        return true
    }

    private static func within(_ gap: TimeInterval, from t: Date, to now: Date) -> Bool {
        let d = now.timeIntervalSince(t)
        return d >= 0 && d < gap
    }
}

/// Which Claude Code events glow. Pure: the controller hands it the frontmost app's bundle id.
enum ClaudeCodeGlowRules {
    /// Terminals and editors known to host Claude Code: with one in front, you are already
    /// looking at the session, so "Not while a terminal is in front" skips the glow.
    static let terminalBundleIDs: Set<String> = [
        "com.apple.Terminal",
        "com.googlecode.iterm2",
        "com.mitchellh.ghostty",
        "dev.warp.Warp-Stable", "dev.warp.Warp-Preview", "dev.warp.Warp-Beta",
        "com.github.wez.wezterm",
        "org.alacritty", "io.alacritty",
        "net.kovidgoyal.kitty",
        "com.microsoft.VSCode", "com.microsoft.VSCodeInsiders",
        "com.todesktop.230313mzl4w4u92",   // Cursor
        "dev.zed.Zed", "dev.zed.Zed-Preview",
    ]

    static func isTerminal(_ bundleID: String?) -> Bool {
        guard let bundleID else { return false }
        return terminalBundleIDs.contains(bundleID)
    }

    /// Whether `event` glows: its switch is on, no terminal is in front (when that option is
    /// on), and the limiter lets it through. Only a glow that happens is remembered.
    static func decide(_ event: ClaudeCodeEvent, switches: NotchGlowSwitches, frontmost: String?,
                       limiter: inout ClaudeCodeGlowLimiter, now: Date) -> Bool {
        guard switches.isOn(event.kind) else { return false }
        if switches.claudeSkipTerminal, isTerminal(frontmost) { return false }
        return limiter.allow(event, now: now)
    }
}
