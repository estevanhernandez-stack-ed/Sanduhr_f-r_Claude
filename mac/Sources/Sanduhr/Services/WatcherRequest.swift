import Foundation

/// The files that bring watchers in (item 66), decoded and checked again. Pure: bytes in, a
/// command or a report out, nothing kept.
///
/// An agent's request, `watch-request-<ms>-<seq>-<id>.json`, written by the MCP server's
/// `watch_start`, `watch_update` and `watch_end`:
///
///     { "schema_version": 1, "op": "start", "id": "w0123456789ab", "requested_at": "…",
///       "title": "CI on main", "link": "https://…", "total": 12, "work": false,
///       "folder": "/Users/x/.claude" }
///     { "schema_version": 1, "op": "update", "id": "…", "requested_at": "…", "done": 3,
///       "note": "lint passed", "state": "waiting" }
///     { "schema_version": 1, "op": "end", "id": "…", "requested_at": "…", "result": "passed" }
///
/// A Stop's report, `watch-stop-<ms>-<uuid>.json`, written by the installed Stop hook
/// (IntegrationInstaller.stopTasksScript) when "Show Claude Code's background work" is on:
///
///     { "schema_version": 1, "session": "…", "folder": "/Users/x/.claude-work",
///       "tasks": [ { "id": "…", "type": "shell", "status": "running",
///                    "description": "Run the test suite", "name": null } ] }
///
/// The report decoder also reads a Stop hook's own input (`session_id`, `background_tasks`), the
/// shape the fixtures hold, and keeps the same few fields: never `command`, never
/// `last_assistant_message`.
enum WatcherRequest {
    static let schemaVersion = 1
    /// A request older than this is dropped (Sanduhr was not running when it was made).
    static let maxAge: TimeInterval = 10 * 60
    /// Largest file either kind may be.
    static let maxBytes = 64 * 1024

    struct Decoded: Equatable {
        var command: WatcherCommand
        /// The Claude Code folder the agent runs with, for the work tag; nil for the default.
        var folder: String?
    }

    /// An id as the server makes it (`w` and 12 hex digits); the app takes any short plain token.
    static func validID(_ s: String?) -> String? {
        guard let s, (1...64).contains(s.count),
              s.allSatisfy(\.isASCII),
              s.unicodeScalars.allSatisfy({ CharacterSet.alphanumerics.contains($0) || "-_.:".unicodeScalars.contains($0) })
        else { return nil }
        return s
    }

    /// One agent request, or nil for anything malformed or stale.
    static func decode(_ data: Data, now: Date) -> Decoded? {
        guard data.count <= maxBytes,
              let o = (try? JSONSerialization.jsonObject(with: data)) as? [String: Any],
              (o["schema_version"] as? NSNumber)?.intValue == schemaVersion,
              let id = validID(o["id"] as? String),
              let at = (o["requested_at"] as? String).flatMap(parseDate),
              now.timeIntervalSince(at) <= maxAge, at.timeIntervalSince(now) <= 60 else { return nil }
        switch o["op"] as? String {
        case "start":
            guard let title = WatcherLimits.line(o["title"] as? String, cap: WatcherLimits.title) else { return nil }
            let total = int(o["total"]).flatMap { (1...WatcherLimits.total).contains($0) ? $0 : nil }
            let folder = (o["folder"] as? String).flatMap { $0.hasPrefix("/") ? $0 : nil }
            return Decoded(command: .start(id: id, title: title, link: WatcherLimits.link(o["link"] as? String),
                                           total: total, work: o["work"] as? Bool ?? false),
                           folder: folder)
        case "update":
            let state: WatcherState?
            switch o["state"] as? String {
            case nil: state = nil
            case "running": state = .running
            case "waiting": state = .waiting
            default: return nil
            }
            let done = int(o["done"]).map { min(max(0, $0), WatcherLimits.total) }
            return Decoded(command: .update(id: id, done: done,
                                            note: WatcherLimits.line(o["note"] as? String, cap: WatcherLimits.note),
                                            state: state))
        case "end":
            let result: WatcherState
            switch o["result"] as? String {
            case "passed": result = .passed
            case "failed": result = .failed
            default: return nil
            }
            return Decoded(command: .end(id: id, result: result,
                                         note: WatcherLimits.line(o["note"] as? String, cap: WatcherLimits.note)))
        default:
            return nil
        }
    }

    /// One Stop's report (the hook's file, or a Stop hook input), or nil when it names no session.
    /// Only id, type, status, description (clipped) and a workflow's name are kept.
    static func decodeStop(_ data: Data) -> StopReport? {
        guard data.count <= maxBytes,
              let o = (try? JSONSerialization.jsonObject(with: data)) as? [String: Any] else { return nil }
        if let v = o["schema_version"], (v as? NSNumber)?.intValue != schemaVersion { return nil }
        guard let session = validID((o["session"] ?? o["session_id"]) as? String) else { return nil }
        let raw = (o["tasks"] ?? o["background_tasks"]) as? [Any] ?? []
        let tasks: [BackgroundTask] = raw.prefix(WatcherBoard.tasksPerStop).compactMap { item in
            guard let t = item as? [String: Any], let id = validID(t["id"] as? String) else { return nil }
            return BackgroundTask(
                id: id,
                type: WatcherLimits.line(t["type"] as? String, cap: 20) ?? "task",
                status: WatcherLimits.line(t["status"] as? String, cap: 20) ?? "running",
                description: WatcherLimits.line(t["description"] as? String, cap: WatcherLimits.title) ?? "",
                name: WatcherLimits.line(t["name"] as? String, cap: 60))
        }
        let folder = (o["folder"] as? String).flatMap { $0.hasPrefix("/") ? $0 : nil }
        return StopReport(session: session, folder: folder, tasks: tasks)
    }

    private static func int(_ v: Any?) -> Int? {
        guard let n = v as? NSNumber, CFGetTypeID(n) != CFBooleanGetTypeID() else { return nil }
        let d = n.doubleValue
        guard d.isFinite, d == d.rounded(), abs(d) <= 1e9 else { return nil }
        return Int(d)
    }

    static func parseDate(_ s: String) -> Date? {
        let f = ISO8601DateFormatter()
        f.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        if let d = f.date(from: s) { return d }
        f.formatOptions = [.withInternetDateTime]
        return f.date(from: s)
    }

    /// An agent request's JSON, as the server writes it (the debug actions and the tests).
    static func json(_ command: WatcherCommand, folder: String? = nil, at: Date) -> Data {
        var o: [String: Any] = ["schema_version": schemaVersion, "requested_at": HandoffFiles.stamp(at)]
        switch command {
        case let .start(id, title, link, total, work):
            o["op"] = "start"; o["id"] = id; o["title"] = title; o["work"] = work
            if let link { o["link"] = link.absoluteString }
            if let total { o["total"] = total }
            if let folder { o["folder"] = folder }
        case let .update(id, done, note, state):
            o["op"] = "update"; o["id"] = id
            if let done { o["done"] = done }
            if let note { o["note"] = note }
            if let state { o["state"] = state.rawValue }
        case let .end(id, result, note):
            o["op"] = "end"; o["id"] = id; o["result"] = result.rawValue
            if let note { o["note"] = note }
        }
        return (try? JSONSerialization.data(withJSONObject: o, options: [.sortedKeys])) ?? Data()
    }
}
