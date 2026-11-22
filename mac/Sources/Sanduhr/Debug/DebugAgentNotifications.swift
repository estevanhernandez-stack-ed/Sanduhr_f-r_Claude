import Foundation

/// The `notify` debug action (agent notifications, 2.47.0). A `turn` is a made-up `AgentTurnEvent` fed to
/// `AgentNotifier.handle`, so the real rules (switches, threshold, quiet hours, front app, limiter) decide what
/// shows; it is never `tryIt`. A `post` runs the routing a Darwin post from Claude Code's hook takes
/// (`AgentNotificationsWiring.handlePost`) without posting a Darwin name, so a smoke can drop a signal file first and
/// then post.
enum DebugNotify: Equatable {
    case turn(agent: AgentSource, kind: AgentTurnEvent.Kind, session: String, project: String?, duration: TimeInterval?)
    case post(ClaudeCodeEvent)

    /// The session of a turn that named none.
    static let defaultSession = "smoke"

    /// The event a `turn` stands for, stamped `at`; nil for a post.
    func event(at: Date) -> AgentTurnEvent? {
        guard case let .turn(agent, kind, session, project, duration) = self else { return nil }
        return AgentTurnEvent(agent: agent, kind: kind, project: project, session: session, at: at, duration: duration)
    }
}

extension DebugLink {
    static let kindWords: [String: AgentTurnEvent.Kind] = [
        "started": .started, "finished": .finished, "stopped": .stopped, "waiting": .waiting, "idle": .idle,
    ]

    /// `notify agent=codex kind=finished session=s1 project=alpha duration=252`, or `notify kind=post event=done`:
    /// words of key=value (spaces or commas between), each key once. `agent` and `kind` are needed for a turn,
    /// `session` (default `smoke`), `project` and `duration` (seconds, any finite number: the card's clamp is the
    /// smoke's to check) are optional. A post takes `event=prompt|waiting|done` and nothing else.
    static func notifyAction(_ arg: String?) -> Result<DebugAction, Failure> {
        func bad(_ why: String) -> Result<DebugAction, Failure> { .failure(Failure(message: why)) }
        let usage = "notify needs agent=claude|codex|antigravity kind=started|finished|stopped|waiting|idle "
            + "[session=<s> project=<p> duration=<seconds>], or kind=post event=prompt|waiting|done"
        let words = (arg ?? "").split(whereSeparator: { $0 == " " || $0 == "," }).map(String.init)
        guard !words.isEmpty else { return bad(usage) }
        var values: [String: String] = [:]
        for word in words {
            let parts = word.split(separator: "=", maxSplits: 1, omittingEmptySubsequences: false).map(String.init)
            guard parts.count == 2, !parts[1].isEmpty else { return bad(usage) }
            let key = parts[0].lowercased()
            guard ["agent", "kind", "session", "project", "duration", "event"].contains(key), values[key] == nil else {
                return bad(usage)
            }
            values[key] = parts[1]
        }
        guard let kind = values["kind"]?.lowercased() else { return bad(usage) }
        if kind == "post" {
            guard values.count == 2, let event = values["event"].flatMap({ ClaudeCodeEvent(rawValue: $0.lowercased()) }) else {
                return bad(usage)
            }
            return .success(.notify(.post(event)))
        }
        guard values["event"] == nil, let k = kindWords[kind],
              let agent = values["agent"].flatMap({ AgentSource(rawValue: $0.lowercased()) }) else { return bad(usage) }
        var duration: TimeInterval?
        if let text = values["duration"] {
            guard let d = Double(text), d.isFinite else { return bad(usage) }
            duration = d
        }
        return .success(.notify(.turn(agent: agent, kind: k, session: values["session"] ?? DebugNotify.defaultSession,
                                      project: values["project"], duration: duration)))
    }
}

/// state.yaml's `agent_notifications:` (2.47.0): the settings the rules read and the last decision, with the words of
/// the banner it posted. Flags, a color, counts and the notification's own words (agent name, a project, a length):
/// never a path or a session.
struct AgentNotificationsDebug: Equatable {
    struct Last: Equatable {
        var agent: String
        var kind: String
        var banner: Bool
        var glow: Bool
        var card: Bool
        var glowColor: String?
        /// The notification's words; nil when no banner was posted.
        var title: String?
        var body: String?

        static func of(_ e: AgentTurnEvent, _ d: NotificationDecision) -> Last {
            let banner = d.banner ? AgentBanner(e) : nil
            return Last(agent: e.agent.rawValue, kind: kindName(e.kind), banner: d.banner, glow: d.glow, card: d.card,
                        glowColor: d.glow ? d.glowColorHex : nil, title: banner?.title, body: banner?.body)
        }

        static func kindName(_ k: AgentTurnEvent.Kind) -> String {
            switch k {
            case .started: "started"
            case .finished: "finished"
            case .stopped: "stopped"
            case .waiting: "waiting"
            case .idle: "idle"
            }
        }
    }

    var banner = true
    var card = true
    var glowFinished = false
    var glowWaiting = false
    var skipInFront = true
    var idle = false
    var threshold = 60
    var quietHours = false
    /// Events the notifier has been handed since launch, dropped ones included.
    var events = 0
    var last: Last?

    static func of(settings s: NotificationSettings, events: Int, last: Last?) -> AgentNotificationsDebug {
        AgentNotificationsDebug(banner: s.banner, card: s.card, glowFinished: s.glowFinished, glowWaiting: s.glowWaiting,
                                skipInFront: s.skipInFront, idle: s.idle, threshold: Int(s.threshold),
                                quietHours: s.quiet.enabled, events: events, last: last)
    }
}

/// state.yaml's `turn_cards:` (2.47.0): each agent turn card on the board, in board order.
struct TurnCardDebug: Equatable {
    var agent: String
    var project: String?
    var state: String
    var seconds: Int

    static func rows(_ watchers: [Watcher], now: Date) -> [TurnCardDebug] {
        watchers.filter { $0.source == .turn }.map { w in
            let agent = w.kind.flatMap(AgentSource.init(rawValue:))
            let prefix = agent.map { AgentBanner.name($0) + " · " }
            let project = prefix.flatMap { w.title.hasPrefix($0) ? String(w.title.dropFirst($0.count)) : nil }
            return TurnCardDebug(agent: w.kind ?? "", project: project?.isEmpty == false ? project : nil,
                                 state: w.state.rawValue, seconds: Int(w.elapsed(now: now).rounded(.down)))
        }
    }
}

/// state.yaml's `agent_watch:` (2.47.0): what the activity watcher follows, by agent and a label for the folder
/// (`codex home`, `antigravity cli`, `signals`), never its path, and whether it is on FSEvents, polling, or read on
/// Claude Code's Darwin post.
struct AgentWatchDebug: Equatable {
    var agent: String
    var folder: String
    var mode: String

    static func rows(followed: [AgentActivityWatcher.FollowedFolder], claudeSignals: Bool) -> [AgentWatchDebug] {
        var codexSeen = 0
        var rows = followed.map { f -> AgentWatchDebug in
            let label: String
            switch f.agent {
            case .codex:
                codexSeen += 1
                label = codexSeen == 1 ? "codex home" : "codex home \(codexSeen)"
            case .antigravity: label = "antigravity cli"
            case .claude: label = "claude folder"
            }
            return AgentWatchDebug(agent: f.agent.rawValue, folder: label, mode: f.polling ? "polling" : "fsevents")
        }
        if claudeSignals { rows.append(AgentWatchDebug(agent: "claude", folder: "signals", mode: "post")) }
        return rows
    }
}

extension DebugState {
    static func agentNotificationsYAML(_ a: AgentNotificationsDebug) -> YAMLNode {
        var last = YAMLNode.null
        if let l = a.last {
            last = .map([
                YAMLPair("agent", .string(l.agent)), YAMLPair("kind", .string(l.kind)),
                YAMLPair("banner", .bool(l.banner)), YAMLPair("glow", .bool(l.glow)), YAMLPair("card", .bool(l.card)),
                YAMLPair("glow_color", l.glowColor.map(YAMLNode.string) ?? .null),
                YAMLPair("title", l.title.map(YAMLNode.string) ?? .null),
                YAMLPair("body", l.body.map(YAMLNode.string) ?? .null),
            ])
        }
        return .map([
            YAMLPair("banner", .bool(a.banner)), YAMLPair("card", .bool(a.card)),
            YAMLPair("glow_finished", .bool(a.glowFinished)), YAMLPair("glow_waiting", .bool(a.glowWaiting)),
            YAMLPair("skip_in_front", .bool(a.skipInFront)), YAMLPair("idle", .bool(a.idle)),
            YAMLPair("threshold", .int(a.threshold)), YAMLPair("quiet_hours", .bool(a.quietHours)),
            YAMLPair("events", .int(a.events)), YAMLPair("last", last),
        ])
    }

    static func turnCardsYAML(_ rows: [TurnCardDebug]) -> YAMLNode {
        .list(rows.map {
            .map([YAMLPair("agent", .string($0.agent)), YAMLPair("project", $0.project.map(YAMLNode.string) ?? .null),
                  YAMLPair("state", .string($0.state)), YAMLPair("seconds", .int($0.seconds))])
        })
    }

    static func agentWatchYAML(_ rows: [AgentWatchDebug]) -> YAMLNode {
        .list(rows.map {
            .map([YAMLPair("agent", .string($0.agent)), YAMLPair("folder", .string($0.folder)),
                  YAMLPair("mode", .string($0.mode))])
        })
    }
}
