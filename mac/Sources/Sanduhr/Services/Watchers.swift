import Foundation

/// Watchers (item 66): live cards for work in flight. An agent puts one up through the MCP
/// server's `watch_start` and moves it with `watch_update` and `watch_end`; Claude Code's own
/// background work (a background shell, a monitor, a subagent, a workflow) shows as one when the
/// Stop hook hands Sanduhr the session's `background_tasks`. Everything here is pure and in
/// memory: the board never touches a file, a log or the network.

/// Where a watcher is in its life.
enum WatcherState: String, CaseIterable, Sendable {
    case running
    /// Waiting on you: it pulses and the notch glows once.
    case waiting
    case passed
    case failed
    /// An automatic watcher whose task left the session's list: it ended, the result unknown.
    case finished
    /// No update within its window (WatcherBoard.window): greyed until it hears again or is
    /// dismissed.
    case lostTouch = "lost_touch"

    /// Lower is more urgent: waiting, then failed, then running, then the rest.
    var urgency: Int {
        switch self {
        case .waiting: 0
        case .failed: 1
        case .running: 2
        case .lostTouch: 3
        case .passed, .finished: 4
        }
    }

    var isEnded: Bool { self == .passed || self == .failed || self == .finished }

    /// Passed and finished fade out on their own; failed stays until dismissed.
    var fades: Bool { self == .passed || self == .finished }

    /// The words VoiceOver and the Desk's note line use.
    var label: String {
        switch self {
        case .running: "running"
        case .waiting: "waiting on you"
        case .passed: "passed"
        case .failed: "failed"
        case .finished: "finished"
        case .lostTouch: "lost touch"
        }
    }
}

/// Who put the watcher up.
enum WatcherSource: String, Sendable {
    /// An agent, through the MCP tools.
    case agent
    /// Claude Code's background work, through the Stop hook.
    case automatic
}

struct Watcher: Equatable, Identifiable, Sendable {
    /// `a:<id>` for an agent's, `b:<session>:<task id>` for an automatic one.
    var id: String
    var source: WatcherSource
    var title: String
    /// The notch's resting form: the agent's `short`, else derived (WatcherText.shortTitle).
    var short: String = ""
    /// Opened on a click; https only (WatcherLimits.link).
    var link: URL?
    var total: Int?
    var done: Int?
    /// One line under the title.
    var note: String?
    var state: WatcherState
    var started: Date
    /// The last time it heard anything: what lost touch is measured from.
    var touched: Date
    var ended: Date?
    /// From a Claude Code folder linked to a work account, or flagged by the agent: hidden while
    /// demo mode is on.
    var work = false
    /// The Claude Code session an automatic watcher belongs to.
    var session: String?
    /// An automatic watcher's kind of task (shell, subagent, monitor, workflow): all the band file
    /// (BandFile) says of it besides its state.
    var kind: String?

    /// How long it has run: until now, or until it ended.
    func elapsed(now: Date) -> TimeInterval { max(0, (ended ?? now).timeIntervalSince(started)) }

    /// "3/10" when there is a total.
    var progress: String? {
        guard let total else { return nil }
        return "\(min(done ?? 0, total))/\(total)"
    }
}

/// The caps both sides apply (the MCP server first, the app again): one short line each.
enum WatcherLimits {
    static let title = 80
    /// An agent's short title, for the notch's resting line.
    static let short = 12
    static let note = 140
    static let link = 2048
    static let total = 1_000_000

    /// One line of text: trimmed, no control characters, at most `cap` characters (longer is
    /// clipped with an ellipsis). Nil when nothing is left.
    static func line(_ s: String?, cap: Int) -> String? {
        guard let s else { return nil }
        let flat = String(String.UnicodeScalarView(s.unicodeScalars.map {
            CharacterSet.controlCharacters.contains($0) ? " " : $0
        })).trimmingCharacters(in: .whitespaces)
        guard !flat.isEmpty else { return nil }
        guard flat.count > cap else { return flat }
        return String(flat.prefix(cap - 1)).trimmingCharacters(in: .whitespaces) + "…"
    }

    /// An https link with a host and nothing else (no user, password), nil for anything else.
    static func link(_ s: String?) -> URL? {
        guard let s, s.count <= link, let url = URL(string: s), url.scheme?.lowercased() == "https",
              let host = url.host, !host.isEmpty, url.user == nil, url.password == nil else { return nil }
        return url
    }
}

/// What an agent asks for, checked (WatcherRequest decodes it from the request file).
enum WatcherCommand: Equatable, Sendable {
    /// `short` is the notch's resting title; nil derives one from the title.
    case start(id: String, title: String, link: URL?, total: Int?, work: Bool, short: String? = nil)
    /// `state` is running or waiting.
    case update(id: String, done: Int?, note: String?, state: WatcherState?)
    /// `result` is passed or failed.
    case end(id: String, result: WatcherState, note: String?)
}

/// One of Claude Code's background tasks as the Stop hook hands it over: never the shell
/// command, never the conversation.
struct BackgroundTask: Equatable, Sendable {
    var id: String
    /// shell, subagent, monitor, workflow (Claude Code's label; unknown ones pass through).
    var type: String
    var status: String
    /// Claude Code's own description, clipped.
    var description: String
    /// A workflow's name.
    var name: String?

    /// running and pending are in flight; failed and error ended badly; anything else that
    /// is not in flight (completed, killed, stopped) ended with the result unknown.
    var state: WatcherState {
        switch status.lowercased() {
        case "failed", "error": .failed
        case "completed", "killed", "stopped", "cancelled", "canceled", "done": .finished
        default: .running
        }
    }

    /// The card's title: the description, else the workflow's name, else the kind of task.
    var title: String {
        WatcherLimits.line(description, cap: WatcherLimits.title)
            ?? WatcherLimits.line(name, cap: WatcherLimits.title)
            ?? "Background \(kind)"
    }

    /// The note line: what kind of work it is ("background shell", "workflow deploy").
    var note: String {
        if type == "workflow", let name = WatcherLimits.line(name, cap: 60) { return "workflow \(name)" }
        return "background \(kind)"
    }

    /// The notch's resting title: the workflow's name, else the description's first word.
    var short: String {
        if type == "workflow", let name = WatcherLimits.line(name, cap: 60) { return WatcherText.clip(name, 10) }
        return WatcherText.firstWord(description) ?? WatcherText.clip(kind, 10)
    }

    var kind: String { WatcherLimits.line(type.lowercased(), cap: 20) ?? "task" }
}

/// One Stop's report: the session and its in-flight background work.
struct StopReport: Equatable, Sendable {
    var session: String
    /// The Claude Code folder the session runs with (CLAUDE_CONFIG_DIR), nil for the default.
    var folder: String?
    var tasks: [BackgroundTask]
}

/// Every watcher, and how they change. Pure: the time comes in.
struct WatcherBoard: Equatable, Sendable {
    /// An agent's watcher with no update for this long has lost touch.
    static let agentWindow: TimeInterval = 10 * 60
    /// An automatic watcher is confirmed or ended at each Stop of its session. One that hears no
    /// Stop for this long (the session was closed mid-task) has lost touch.
    static let automaticWindow: TimeInterval = 60 * 60
    /// Passed and finished fade after this long.
    static let fadeAfter: TimeInterval = 6
    /// Most watchers kept; past it the least urgent, oldest go.
    static let capacity = 24
    /// Most a session's Stop may bring.
    static let tasksPerStop = 20

    private(set) var watchers: [Watcher] = []

    static func window(_ source: WatcherSource) -> TimeInterval {
        source == .agent ? agentWindow : automaticWindow
    }

    static func agentID(_ id: String) -> String { "a:\(id)" }
    static func automaticID(session: String, task: String) -> String { "b:\(session):\(task)" }

    func watcher(_ id: String) -> Watcher? { watchers.first { $0.id == id } }

    /// Applies an agent's command. True when a watcher started waiting on you (the notch glows
    /// once). Updating or ending a watcher that isn't there does nothing.
    @discardableResult
    mutating func apply(_ command: WatcherCommand, now: Date) -> Bool {
        switch command {
        case let .start(id, title, link, total, work, short):
            let key = Self.agentID(id)
            watchers.removeAll { $0.id == key }
            watchers.append(Watcher(id: key, source: .agent, title: title,
                                    short: short ?? WatcherText.shortTitle(title), link: link, total: total,
                                    done: total == nil ? nil : 0, note: nil, state: .running,
                                    started: now, touched: now, ended: nil, work: work))
            trim()
            return false
        case let .update(id, done, note, state):
            guard let i = watchers.firstIndex(where: { $0.id == Self.agentID(id) }),
                  !watchers[i].state.isEnded else { return false }
            let was = watchers[i].state
            if let done { watchers[i].done = max(0, done) }
            if let note { watchers[i].note = note }
            watchers[i].state = state ?? (was == .lostTouch ? .running : was)
            watchers[i].touched = now
            return watchers[i].state == .waiting && was != .waiting
        case let .end(id, result, note):
            guard let i = watchers.firstIndex(where: { $0.id == Self.agentID(id) }),
                  !watchers[i].state.isEnded else { return false }
            if let note { watchers[i].note = note }
            if result == .passed, let total = watchers[i].total { watchers[i].done = total }
            watchers[i].state = result
            watchers[i].touched = now
            watchers[i].ended = now
            return false
        }
    }

    /// A Stop of `report.session`: each task in flight is a watcher (added, or touched and
    /// updated), and the session's automatic watchers whose task left the list end as finished.
    /// A task the list reports as ended ends with it. True when anything changed.
    @discardableResult
    mutating func applyStop(_ report: StopReport, work: Bool, now: Date) -> Bool {
        let before = watchers
        var seen: Set<String> = []
        for task in report.tasks.prefix(Self.tasksPerStop) {
            let key = Self.automaticID(session: report.session, task: task.id)
            guard seen.insert(key).inserted else { continue }
            let state = task.state
            if let i = watchers.firstIndex(where: { $0.id == key }) {
                guard !watchers[i].state.isEnded else { continue }
                watchers[i].title = task.title
                watchers[i].short = task.short
                watchers[i].note = task.note
                watchers[i].kind = task.kind
                watchers[i].touched = now
                watchers[i].state = state
                if state.isEnded { watchers[i].ended = now }
            } else if !state.isEnded {
                watchers.append(Watcher(id: key, source: .automatic, title: task.title, short: task.short,
                                        link: nil, total: nil,
                                        done: nil, note: task.note, state: .running, started: now,
                                        touched: now, ended: nil, work: work, session: report.session,
                                        kind: task.kind))
            }
        }
        for i in watchers.indices where watchers[i].source == .automatic && watchers[i].session == report.session
            && !seen.contains(watchers[i].id) && !watchers[i].state.isEnded {
            watchers[i].state = .finished
            watchers[i].touched = now
            watchers[i].ended = now
        }
        trim()
        return watchers != before
    }

    /// The clock moved: running and waiting watchers past their window lose touch, passed and
    /// finished ones past the fade go. True when anything changed.
    @discardableResult
    mutating func tick(now: Date) -> Bool {
        let before = watchers
        watchers.removeAll { w in
            guard w.state.fades, let ended = w.ended else { return false }
            return now.timeIntervalSince(ended) >= Self.fadeAfter
        }
        for i in watchers.indices where watchers[i].state == .running || watchers[i].state == .waiting {
            if now.timeIntervalSince(watchers[i].touched) > Self.window(watchers[i].source) {
                watchers[i].state = .lostTouch
            }
        }
        return watchers != before
    }

    /// The next moment `tick` would change something, nil when nothing waits on the clock.
    func nextChange() -> Date? {
        watchers.compactMap { w -> Date? in
            if w.state.fades, let ended = w.ended { return ended.addingTimeInterval(Self.fadeAfter) }
            if w.state == .running || w.state == .waiting {
                return w.touched.addingTimeInterval(Self.window(w.source) + 1)
            }
            return nil
        }.min()
    }

    mutating func dismiss(_ id: String) { watchers.removeAll { $0.id == id } }

    mutating func dismissAll() { watchers.removeAll() }

    /// Drops every watcher from `source` (its switch went off).
    mutating func clear(_ source: WatcherSource) { watchers.removeAll { $0.source == source } }

    /// Most urgent first (waiting, failed, running, lost touch, then passed and finished), then
    /// the most recently heard from.
    func ordered() -> [Watcher] {
        watchers.sorted { a, b in
            if a.state.urgency != b.state.urgency { return a.state.urgency < b.state.urgency }
            if a.touched != b.touched { return a.touched > b.touched }
            return a.id < b.id
        }
    }

    /// What the notch and the Desk show: the ordered watchers, less the work ones in demo mode.
    static func shown(_ ordered: [Watcher], demo: Bool) -> [Watcher] {
        demo ? ordered.filter { !$0.work } : ordered
    }

    /// Past capacity, the least urgent and oldest go first.
    private mutating func trim() {
        guard watchers.count > Self.capacity else { return }
        let keep = Set(ordered().prefix(Self.capacity).map(\.id))
        watchers.removeAll { !keep.contains($0.id) }
    }
}

/// The words the notch and the Desk draw.
enum WatcherText {
    /// "12s", "4m", "1h 05m".
    static func elapsed(_ seconds: TimeInterval) -> String {
        let s = Int(seconds.rounded(.down))
        if s < 60 { return "\(s)s" }
        if s < 3600 { return "\(s / 60)m" }
        return String(format: "%dh %02dm", s / 3600, (s % 3600) / 60)
    }

    /// The notch line for the most urgent watcher: the full line during the intro (WatcherIntro),
    /// the short resting line after it. Nil with no watcher.
    static func notchLine(_ shown: [Watcher], intro: Bool, now: Date) -> String? {
        intro ? fullLine(shown, now: now) : restLine(shown, now: now)
    }

    /// The notch's resting line: "PR 140 · 4/12", else "deploy · 4m" without a total, plus " +N"
    /// for the other watchers. Nil with no watcher.
    static func restLine(_ shown: [Watcher], now: Date) -> String? {
        guard let top = shown.first else { return nil }
        let short = top.short.isEmpty ? shortTitle(top.title) : top.short
        var line = "\(short) · \(top.progress ?? elapsed(top.elapsed(now: now)))"
        if shown.count > 1 { line += " +\(shown.count - 1)" }
        return line
    }

    /// The full line the notch shows once (and scrolls when it doesn't fit) before it rests: the
    /// whole title, the time so far, the progress and the count.
    static func fullLine(_ shown: [Watcher], now: Date) -> String? {
        guard let top = shown.first else { return nil }
        var parts = [top.title, elapsed(top.elapsed(now: now))]
        if let p = top.progress { parts.append(p) }
        var line = parts.joined(separator: " · ")
        if shown.count > 1 { line += " +\(shown.count - 1)" }
        return line
    }

    /// A short title from a long one: "PR 140" (from "PR 140", "PR #140", "pull 140"), else "#140"
    /// (a number anywhere), else "v2.8.0" ("Release 2.8.0", "v2.8.0"), else the first word,
    /// clipped to 10 characters.
    static func shortTitle(_ title: String) -> String {
        let ns = title as NSString
        func first(_ pattern: String) -> String? {
            guard let re = try? NSRegularExpression(pattern: pattern, options: [.caseInsensitive]),
                  let m = re.firstMatch(in: title, range: NSRange(location: 0, length: ns.length)),
                  m.numberOfRanges > 1 else { return nil }
            return ns.substring(with: m.range(at: 1))
        }
        if let n = first(#"\b(?:PR|pull)\s*#?\s*(\d+)"#) { return "PR \(n)" }
        if let n = first(#"#(\d+)"#) { return "#\(n)" }
        if let v = first(#"\brelease\s+v?(\d+(?:\.\d+)+)"#) ?? first(#"(?:^|[^\w])v(\d+(?:\.\d+)+)"#) { return "v\(v)" }
        return firstWord(title) ?? clip(title, 10)
    }

    /// The first word, without trailing punctuation, clipped to 10 characters.
    static func firstWord(_ s: String) -> String? {
        guard let word = s.split(whereSeparator: { $0.isWhitespace }).first else { return nil }
        let trimmed = String(word).trimmingCharacters(in: CharacterSet(charactersIn: ":;,.!?-–—"))
        return trimmed.isEmpty ? nil : clip(trimmed, 10)
    }

    static func clip(_ s: String, _ n: Int) -> String { String(s.prefix(n)) }

    /// The Desk row's second line: the note, else the state when it says something the dot
    /// doesn't ("waiting on you", "lost touch").
    static func deskNote(_ w: Watcher) -> String? {
        if let note = w.note { return note }
        switch w.state {
        case .waiting, .lostTouch, .failed, .finished: return w.state.label
        default: return nil
        }
    }

    /// What VoiceOver reads for a watcher.
    static func spoken(_ w: Watcher, now: Date) -> String {
        var parts = [w.title, w.state.label, elapsed(w.elapsed(now: now))]
        if let p = w.progress { parts.append(p) }
        if let note = w.note { parts.append(note) }
        return parts.joined(separator: ", ")
    }
}

/// The notch's intro (item 66): when the top watcher changes (its id) or its state changes
/// (waiting on you, passed, failed…), a notch place on Watchers shows the full line once,
/// scrolling through it when it doesn't fit (NowPlayingScroll's timing and fade), then rests on
/// the short line (WatcherText.restLine). Never with Reduce Motion: straight to rest. Pure;
/// DeskModel keeps the phase, so the wings' width follows it.
enum WatcherIntro {
    /// The full line stays this long when it fits.
    static let hold: TimeInterval = 4

    /// What restarts the intro: the top watcher's id and state. Nil with no watcher.
    static func key(_ shown: [Watcher]) -> String? {
        shown.first.map { "\($0.id)\u{1F}\($0.state.rawValue)" }
    }

    /// Whether an intro starts going from `old` to `new`.
    static func starts(from old: String?, to new: String?, reduceMotion: Bool) -> Bool {
        guard let new, !reduceMotion else { return false }
        return new != old
    }

    /// How long the intro lasts for a line `textWidth` wide in `room`: the hold when it fits,
    /// else the scroll's pauses, travel and way back.
    static func duration(textWidth: CGFloat, room: CGFloat) -> TimeInterval {
        NowPlayingScroll.plan(textWidth: textWidth, room: room, reduceMotion: false)?.total ?? hold
    }
}
