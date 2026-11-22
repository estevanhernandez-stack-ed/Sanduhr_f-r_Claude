import AppKit
import Foundation

/// The app in front, read only when the rules ask (an interrupt is otherwise due and "not while the agent's
/// window is in front" is on). Injected, so tests never read the real front app.
struct FrontApp {
    var bundleID: () -> String?
    var name: () -> String?

    static let workspace = FrontApp(bundleID: { NSWorkspace.shared.frontmostApplication?.bundleIdentifier },
                                    name: { NSWorkspace.shared.frontmostApplication?.localizedName })
}

/// One agent notification's words (design section 4): "Claude Code finished", "alpha, 4m 12s". Its identifier
/// is made from the agent and the session, so a session's newer banner replaces its older one; the idle reminder
/// has its own, so it never replaces that turn's "finished". Never carries message text.
struct AgentBanner: Equatable {
    static let thread = "sanduhr-agents"
    static let idPrefix = "agent-"

    var id: String
    var title: String
    var body: String
    var thread = AgentBanner.thread

    init(id: String, title: String, body: String) {
        self.id = id
        self.title = title
        self.body = body
    }

    init(_ e: AgentTurnEvent) {
        self.init(id: Self.id(e), title: Self.title(e), body: Self.body(e))
    }

    /// The name a person knows the agent by: Claude Code, not Claude (the usage source).
    static func name(_ agent: AgentSource) -> String { agent == .claude ? "Claude Code" : agent.displayName }

    static func id(_ e: AgentTurnEvent) -> String {
        idPrefix + (e.kind == .idle ? "idle" : e.agent.rawValue) + "-" + e.session
    }

    static func title(_ e: AgentTurnEvent) -> String {
        switch e.kind {
        case .waiting, .idle: "\(name(e.agent)) is waiting on you"
        case .stopped: "\(name(e.agent)) stopped"
        case .started, .finished: "\(name(e.agent)) finished"
        }
    }

    /// "alpha, 4m 12s"; the project alone for waiting and the idle reminder; whichever part is known.
    static func body(_ e: AgentTurnEvent) -> String {
        var parts: [String] = []
        if let p = project(e.project) { parts.append(p) }
        if e.kind != .waiting, e.kind != .idle, let d = e.duration { parts.append(duration(d)) }
        return parts.joined(separator: ", ")
    }

    /// "45s", "4m 12s", "1h 02m". Negative or not a number reads as 0s.
    static func duration(_ d: TimeInterval) -> String {
        let s = d.isFinite ? Int(max(0, d.rounded(.down))) : 0
        if s < 60 { return "\(s)s" }
        if s < 3600 { return String(format: "%dm %02ds", s / 60, s % 60) }
        return String(format: "%dh %02dm", s / 3600, (s % 3600) / 60)
    }

    /// A turn card's title: "Codex · alpha", or the agent alone.
    static func cardTitle(_ agent: AgentSource, project: String?) -> String {
        guard let p = self.project(project) else { return name(agent) }
        return "\(name(agent)) · \(p)"
    }

    private static func project(_ p: String?) -> String? {
        guard let p, !p.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { return nil }
        return p
    }
}

/// One agent turn event in, the surfaces called (design sections 3 and 4). `NotificationRules.decide` picks the
/// surfaces; the app plugs them in as closures, so this stays testable without a window. A surface that throws is
/// logged by the surface and the error's type and never stops the others. Whether the agent's source is on is
/// checked here, before the rules (decision D5). The same end of a turn read twice (a file event, then the poll
/// after a wake) raises nothing the second time. Call `handle` on the main thread.
final class AgentNotifier {
    /// How many sessions' last ends are remembered for the repeat check.
    static let maxRemembered = 64

    private let settings: () -> NotificationSettings
    private let banner: (AgentBanner) throws -> Void
    private let glow: (String?) throws -> Void
    private let card: (AgentTurnEvent) throws -> Void
    private let front: FrontApp
    private let clock: () -> Date
    private let isOn: (AgentSource) -> Bool
    private let color: (AgentSource) -> String
    private let calendar: Calendar
    private let log: (String) -> Void
    private var limiter = NotificationLimiter()

    private struct Key: Hashable { var agent: AgentSource; var session: String }
    private struct End: Equatable { var kind: AgentTurnEvent.Kind; var at: Date }
    private var lastEnd: [Key: End] = [:]

    /// - Parameters:
    ///   - banner: posts the notification (`Notifier.postAgent`).
    ///   - glow: glows the notch, in the given `#RRGGBB` or the notch ink for nil.
    ///   - card: moves the agent's turn card (`WatcherStore.applyTurn`).
    ///   - isOn: whether the agent's source is on (Claude Code: with the updated hook); off raises nothing.
    ///   - color: the agent's Sources color.
    init(settings: @escaping () -> NotificationSettings,
         banner: @escaping (AgentBanner) throws -> Void,
         glow: @escaping (String?) throws -> Void,
         card: @escaping (AgentTurnEvent) throws -> Void,
         front: FrontApp,
         clock: @escaping () -> Date,
         isOn: @escaping (AgentSource) -> Bool = { _ in true },
         color: @escaping (AgentSource) -> String = { AgentSourceSettings.load().color(of: $0) },
         calendar: Calendar = .current,
         log: @escaping (String) -> Void = { NSLog("%@", $0) }) {
        self.settings = settings
        self.banner = banner
        self.glow = glow
        self.card = card
        self.front = front
        self.clock = clock
        self.isOn = isOn
        self.color = color
        self.calendar = calendar
        self.log = log
    }

    /// The surfaces `e` used.
    @discardableResult
    func handle(_ e: AgentTurnEvent) -> NotificationDecision {
        guard isOn(e.agent), !isRepeat(e) else { return .none }
        let d = NotificationRules.decide(e, settings: settings(), frontBundleID: front.bundleID, frontName: front.name,
                                         sourceColorHex: color(e.agent), limiter: &limiter, now: clock(),
                                         calendar: calendar)
        if d.card { attempt("card") { try card(e) } }
        if d.banner { attempt("banner") { try banner(AgentBanner(e)) } }
        if d.glow { attempt("glow") { try glow(d.glowColorHex) } }
        return d
    }

    /// A surface of Settings ▸ Notifications' Try it.
    enum TrySurface: Equatable {
        case notification
        case glowFinished
        case glowWaiting
        case card
    }

    /// The made-up session every sample carries.
    static let trySession = "try-it"

    /// A sample through one real surface, whatever the switches, the source, the quiet hours, the front app and the
    /// limiter say: Try it shows what the switch would do. The sample is a made-up Claude Code turn ("your project",
    /// 4m 12s); it posts no Darwin name and reads no file. A surface that throws is logged, as in `handle`.
    func tryIt(_ surface: TrySurface) {
        let waiting = surface == .glowWaiting
        let e = AgentTurnEvent(agent: .claude, kind: waiting ? .waiting : .finished, project: "your project",
                               session: Self.trySession, at: clock(), duration: waiting ? nil : 252)
        switch surface {
        case .notification: attempt("banner") { try banner(AgentBanner(e)) }
        case .glowFinished: attempt("glow") { try glow(color(e.agent)) }
        case .glowWaiting: attempt("glow") { try glow(nil) }
        case .card: attempt("card") { try card(e) }
        }
    }

    /// The agent was switched off: its sessions start over.
    func forget(_ agent: AgentSource) {
        lastEnd = lastEnd.filter { $0.key.agent != agent }
    }

    /// True for a finish or stop already handled: same agent, session, kind and time. Remembers the new one.
    private func isRepeat(_ e: AgentTurnEvent) -> Bool {
        guard e.kind == .finished || e.kind == .stopped else { return false }
        let key = Key(agent: e.agent, session: e.session)
        let end = End(kind: e.kind, at: e.at)
        if lastEnd[key] == end { return true }
        if lastEnd.count >= Self.maxRemembered, lastEnd[key] == nil { lastEnd.removeAll() }
        lastEnd[key] = end
        return false
    }

    private func attempt(_ surface: String, _ call: () throws -> Void) {
        do { try call() } catch {
            log("agent notifications: \(surface) failed (\(type(of: error)))")
        }
    }
}
