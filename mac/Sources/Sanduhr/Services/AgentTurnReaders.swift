import Foundation

/// Codex's turn markers (agent notifications design, section 2; Windows `CodexTurnReader`). One reader
/// per rollout, fed every line in order. `task_started` opens a turn, `task_complete` finishes it and
/// `turn_aborted` stops it; the project is the `cwd` of the `turn_context` in force. A rollout whose
/// `session_meta` names a parent thread is a sub-agent's: it raises nothing, and that mark sticks,
/// because a forked rollout repeats its parent's meta without the keys. Reads timestamps, ids, folders
/// and the duration; never `last_agent_message` or any other text.
struct CodexTurnReader {
    /// Turns a folder into the name shown. Injected so tests don't probe the disk.
    private let projectName: (String) -> String
    private var cwd: String?
    private var started: Date?

    /// This rollout belongs to a sub-agent. Set once, never cleared.
    private(set) var isChild = false

    /// A turn has started and not ended.
    var turnOpen: Bool { started != nil }

    init(projectName: @escaping (String) -> String = CCProjectName.displayName) {
        self.projectName = projectName
    }

    private static let marks = ["\"session_meta\"", "\"turn_context\"", "\"task_started\"", "\"task_complete\"", "\"turn_aborted\""]
        .map { Data($0.utf8) }

    /// The events `lines` raise, in order. `file` is the rollout's file name or path; the session is its
    /// name without `.jsonl`. A line that cannot be read is skipped, never thrown.
    mutating func read(lines: [Data], file: String) -> [AgentTurnEvent] {
        let session = CodexBurnReader.sessionID(of: (file as NSString).lastPathComponent)
        var out: [AgentTurnEvent] = []
        for line in lines {
            if let e = readLine(line, session: session) { out.append(e) }
        }
        return out
    }

    private mutating func readLine(_ line: Data, session: String) -> AgentTurnEvent? {
        guard Self.marks.contains(where: { line.range(of: $0) != nil }),
              let d = (try? JSONSerialization.jsonObject(with: line)) as? [String: Any],
              let p = d["payload"] as? [String: Any] else { return nil }
        switch d["type"] as? String {
        case "session_meta":
            cwd = (p["cwd"] as? String) ?? cwd
            if hasText(p, "parent_thread_id") || hasText(p, "forked_from_id") { isChild = true }
            return nil
        case "turn_context":
            cwd = (p["cwd"] as? String) ?? cwd
            return nil
        case "event_msg":
            break
        default:
            return nil
        }
        guard let at = (d["timestamp"] as? String).flatMap(VaultTime.parse) else { return nil }
        switch p["type"] as? String {
        case "task_started":
            started = at
            return raise(.started, at, session: session, duration: nil)
        case "task_complete":
            let duration = Self.loggedDuration(p) ?? since(at)
            started = nil
            return raise(.finished, at, session: session, duration: duration)
        case "turn_aborted":
            let duration = since(at)
            started = nil
            return raise(.stopped, at, session: session, duration: duration)
        default:
            return nil
        }
    }

    private func raise(_ kind: AgentTurnEvent.Kind, _ at: Date, session: String, duration: TimeInterval?) -> AgentTurnEvent? {
        guard !isChild else { return nil }
        let project = cwd.map(projectName).flatMap { $0.isEmpty ? nil : $0 }
        return AgentTurnEvent(agent: .codex, kind: kind, project: project, session: session, at: at, duration: duration)
    }

    private func since(_ at: Date) -> TimeInterval? {
        guard let s = started, at >= s else { return nil }
        return at.timeIntervalSince(s)
    }

    /// .NET's `TimeSpan.MaxValue` in milliseconds: a logged duration past it is not believed (Windows).
    private static let maxLoggedMilliseconds = 922_337_203_685_477.5807

    private static func loggedDuration(_ p: [String: Any]) -> TimeInterval? {
        guard let n = p["duration_ms"] as? NSNumber, CFGetTypeID(n) != CFBooleanGetTypeID() else { return nil }
        let ms = n.doubleValue
        guard ms.isFinite, ms >= 0, ms <= maxLoggedMilliseconds else { return nil }
        return ms / 1000
    }

    private func hasText(_ p: [String: Any], _ key: String) -> Bool {
        guard let s = p[key] as? String else { return false }
        return !s.isEmpty
    }
}

/// The Antigravity CLI's turns (agent notifications design, section 2; Windows `AntigravityTurnReader`).
/// Its transcript has no turn-end marker, so the end is inferred: a `PLANNER_RESPONSE` with status
/// `DONE` and no `tool_calls` key, followed by `quiet` with no new line. On the 2026-11-17 sample a step
/// that looked final was followed by more work up to 83 seconds later, so 90 seconds is the shortest
/// wait that raised nothing falsely. Only whether `tool_calls` exists is read, never its value, and never
/// a step's `content` or `thinking`. One reader per transcript.
struct AntigravityTurnReader {
    static let quiet: TimeInterval = 90

    private let projectName: (String) -> String
    /// The turn's `USER_INPUT` `created_at`.
    private var turnStart: Date?
    private var open = false
    /// `created_at` of a step that looks final.
    private var finalAt: Date?
    /// When that line was read.
    private var finalSeen: Date?
    private var session = ""
    private var folder: String?

    /// A turn has started and not ended.
    var turnOpen: Bool { open }

    /// When `tick` could next raise `finished`; nil with nothing pending.
    var dueAt: Date? { finalSeen.map { $0.addingTimeInterval(Self.quiet) } }

    init(projectName: @escaping (String) -> String = CCProjectName.displayName) {
        self.projectName = projectName
    }

    /// A line arrived that was not read (an oversized one): more work, so a pending end is off.
    mutating func activity() {
        finalAt = nil
        finalSeen = nil
    }

    /// The events `lines` raise, in order. `conversation` is the session; `project` is the conversation's
    /// folder from the summaries lookup (kept for later ticks while a call passes none); `now` is when the
    /// lines were read, which starts the quiet wait for a final-looking step. Every line, readable or not,
    /// cancels a pending end first.
    mutating func read(lines: [Data], conversation: String, project: String?, now: Date = Date()) -> [AgentTurnEvent] {
        session = conversation
        if let project { folder = project }
        var out: [AgentTurnEvent] = []
        for line in lines {
            activity()
            if let e = readLine(line, now: now) { out.append(e) }
        }
        return out
    }

    private mutating func readLine(_ line: Data, now: Date) -> AgentTurnEvent? {
        guard !line.isEmpty,
              let d = (try? JSONSerialization.jsonObject(with: line)) as? [String: Any],
              let at = (d["created_at"] as? String).flatMap(VaultTime.parse) else { return nil }
        let type = d["type"] as? String
        if type == "USER_INPUT" {
            turnStart = at
            open = true
            return event(.started, at, duration: nil)
        }
        if type == "PLANNER_RESPONSE", d["status"] as? String == "DONE", d["tool_calls"] == nil {
            finalAt = at
            finalSeen = now
        }
        return nil
    }

    /// `finished`, once a step that looks final has had `quiet` with no new line. Timed by the step.
    mutating func tick(now: Date) -> [AgentTurnEvent] {
        guard let seen = finalSeen, let at = finalAt, now.timeIntervalSince(seen) >= Self.quiet else { return [] }
        var duration: TimeInterval?
        if let s = turnStart, at >= s { duration = at.timeIntervalSince(s) }
        close()
        return [event(.finished, at, duration: duration)]
    }

    /// After reading history at launch: a pending end closes its turn with no event, since nothing replays.
    mutating func settle() {
        if finalSeen != nil { close() }
    }

    private mutating func close() {
        turnStart = nil
        open = false
        activity()
    }

    private func event(_ kind: AgentTurnEvent.Kind, _ at: Date, duration: TimeInterval?) -> AgentTurnEvent {
        let project = folder.map(projectName).flatMap { $0.isEmpty ? nil : $0 }
        return AgentTurnEvent(agent: .antigravity, kind: kind, project: project, session: session, at: at, duration: duration)
    }
}
