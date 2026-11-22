import Foundation

/// What an agent's turn did (agent notifications design, section 3). The same shape for every agent;
/// the readers (Claude Code's signal files, Codex's rollouts, Antigravity's steps) all produce it.
/// Never carries message text.
struct AgentTurnEvent: Equatable, Sendable {
    enum Kind: Equatable, Sendable {
        case started
        case finished
        /// The turn ended without finishing: aborted, or an error.
        case stopped
        /// The agent needs the person before it can go on.
        case waiting
        /// Claude Code's reminder that the prompt has sat idle after a turn ended (its idle
        /// `Notification`). Not a wait on the person: it has its own switch and never makes a card.
        case idle
    }

    var agent: AgentSource
    var kind: Kind
    /// The project's display name, when the agent's files name a folder.
    var project: String?
    /// Tells one run from another: the key for turn state, never the folder.
    var session: String
    var at: Date
    /// How long the turn ran; nil when that is not known.
    var duration: TimeInterval?
}

/// The file Claude Code's hook writes for each event: `cc-signal-<unix ms>-<random>.json` in Sanduhr's
/// folder, holding the event, the session's folder and its id. One file per event, so two sessions
/// never overwrite each other. Sanduhr deletes each as it reads it. Nothing of the conversation is in it.
/// Pure: bytes in, a signal or nil out.
enum ClaudeCodeSignalFile {
    static let maxBytes = 4096
    static let maxAge: TimeInterval = 600
    /// A stamp this far ahead of the clock is not believed.
    static let maxAhead: TimeInterval = 60
    static let maxSessionLength = 128
    static let maxCwdLength = 1024
    /// The session of a signal that named none.
    static let noSession = "-"

    static let events: Set<String> = ["prompt", "waiting", "done"]

    struct Signal: Equatable, Sendable {
        var event: String
        var cwd: String?
        var session: String
        var at: Date
    }

    /// The signal in `data`, nil for anything that is not one: over the size cap, not version 1, an
    /// unknown event, older than `maxAge` or stamped in the future. A blank session reads as
    /// `noSession`; a blank or over-long `cwd` as no folder; an over-long session drops the file.
    static func parse(_ data: Data, now: Date) -> Signal? {
        guard !data.isEmpty, data.count <= maxBytes,
              let o = (try? JSONSerialization.jsonObject(with: data)) as? [String: Any],
              let version = (o["v"] as? NSNumber), isInteger(version), version.intValue == 1,
              let event = o["event"] as? String, events.contains(event),
              let ms = o["at"] as? NSNumber, !isBool(ms), ms.doubleValue.isFinite else { return nil }
        let at = Date(timeIntervalSince1970: ms.doubleValue / 1000)
        let age = now.timeIntervalSince(at)
        if age > maxAge || -age > maxAhead { return nil }

        var session = (o["session"] as? String) ?? ""
        if session.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty { session = noSession }
        if session.count > maxSessionLength { return nil }
        var cwd = o["cwd"] as? String
        if let c = cwd, c.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty || c.count > maxCwdLength { cwd = nil }
        return Signal(event: event, cwd: cwd, session: session, at: at)
    }

    private static func isBool(_ n: NSNumber) -> Bool { CFGetTypeID(n) == CFBooleanGetTypeID() }
    private static func isInteger(_ n: NSNumber) -> Bool { !isBool(n) && n.doubleValue == Double(n.intValue) }
}

/// Turns Claude Code's signals into turn events: a prompt starts a turn, done finishes it with the
/// time between the two, and waiting inside an open turn is `.waiting`. A waiting with no open turn is
/// `.idle`: the hook's `Notification` matcher also catches Claude Code's idle reminder, raised a minute
/// after a turn ends, and that is not a wait on you (Windows spec, section 11). Start times are kept per
/// session, in memory, for at most `maxSessions`.
struct ClaudeCodeTurns {
    static let maxSessions = 64

    /// Turns a folder into the name shown. Injected so tests don't probe the disk.
    var projectName: (String) -> String = CCProjectName.displayName

    private var started: [String: Date] = [:]

    init(projectName: @escaping (String) -> String = CCProjectName.displayName) {
        self.projectName = projectName
    }

    /// The event for `s`, or nil on a `replay`: the first pass over files that were waiting when
    /// Sanduhr launched records each turn's start and end and raises nothing.
    mutating func apply(_ s: ClaudeCodeSignalFile.Signal, replay: Bool) -> AgentTurnEvent? {
        let project = s.cwd.map(projectName).flatMap { $0.isEmpty ? nil : $0 }
        func make(_ kind: AgentTurnEvent.Kind, _ duration: TimeInterval? = nil) -> AgentTurnEvent {
            AgentTurnEvent(agent: .claude, kind: kind, project: project, session: s.session, at: s.at, duration: duration)
        }
        switch s.event {
        case "prompt":
            if started.count >= Self.maxSessions, started[s.session] == nil,
               let oldest = started.min(by: { $0.value < $1.value })?.key {
                started.removeValue(forKey: oldest)
            }
            started[s.session] = s.at
            return replay ? nil : make(.started)
        case "waiting":
            return replay ? nil : make(started[s.session] != nil ? .waiting : .idle)
        default:
            var duration: TimeInterval?
            if let start = started.removeValue(forKey: s.session), s.at >= start { duration = s.at.timeIntervalSince(start) }
            return replay ? nil : make(.finished, duration)
        }
    }
}
