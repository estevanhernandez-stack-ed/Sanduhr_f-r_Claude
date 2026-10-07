import Foundation

/// The statusline input for the Combine sheet's "Test with live data" (item 63b): the documented
/// sample's shape with Sanduhr's current numbers (snapshot.json: the active account's 5-hour and
/// weekly percentages and resets), the current time, and the model, context and working folder
/// of the folder's latest Claude Code session when its transcript says them. Only those fields
/// are read from the transcript, never the conversation.
enum StatuslineLiveInput {
    struct Result: Equatable, Sendable {
        let data: Data
        /// The limits came from Sanduhr's saved numbers (else the sample's).
        let liveLimits: Bool
        /// The model and context came from the folder's latest session (else the sample's).
        let liveSession: Bool
    }

    /// How much of the end of the latest transcript is read.
    static let tailBytes = 256 * 1024

    static func build(folder: String, snapshot: URL = SnapshotWriter.url, now: Date = Date()) -> Result {
        var input = StatuslinePreview.sample(now: now)
        let limits = rateLimits(snapshot: snapshot, now: now)
        if let limits { input["rate_limits"] = limits }
        let session = latestSession(folder: folder)
        if let session {
            input["model"] = ["id": session.model, "display_name": displayName(session.model)]
            var context = input["context_window"] as? [String: Any] ?? [:]
            context["used_percentage"] = session.contextPercent
            context["remaining_percentage"] = max(0, 100 - session.contextPercent)
            context["context_window_size"] = session.window
            input["context_window"] = context
            if let cwd = session.cwd {
                input["cwd"] = cwd
                input["workspace"] = ["current_dir": cwd, "project_dir": cwd, "added_dirs": [String]()]
            }
        }
        let data = (try? JSONSerialization.data(withJSONObject: input, options: [.sortedKeys])) ?? StatuslinePreview.sampleJSON(now: now)
        return Result(data: data, liveLimits: limits != nil, liveSession: session != nil)
    }

    /// The snapshot's 5-hour and weekly windows as Claude Code's `rate_limits`; nil when it has
    /// neither (no snapshot, signed out).
    static func rateLimits(snapshot: URL, now: Date) -> [String: Any]? {
        guard let data = try? Data(contentsOf: snapshot),
              let o = (try? JSONSerialization.jsonObject(with: data)) as? [String: Any],
              let tiers = o["tiers"] as? [[String: Any]] else { return nil }
        var out: [String: Any] = [:]
        for tier in tiers {
            guard let key = tier["key"] as? String, key == "five_hour" || key == "seven_day",
                  let util = (tier["utilization"] as? NSNumber)?.doubleValue else { continue }
            var window: [String: Any] = ["used_percentage": util]
            if let reset = (tier["resets_at"] as? String).flatMap(parseDate), reset > now {
                window["resets_at"] = Int(reset.timeIntervalSince1970)
            }
            out[key] = window
        }
        return out.isEmpty ? nil : out
    }

    private static func parseDate(_ s: String) -> Date? {
        let f = ISO8601DateFormatter()
        f.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        if let d = f.date(from: s) { return d }
        f.formatOptions = [.withInternetDateTime]
        return f.date(from: s)
    }

    struct Session: Equatable, Sendable {
        let model: String
        let contextPercent: Int
        let window: Int
        let cwd: String?
    }

    /// The model, context use and working folder of the last assistant turn in the folder's
    /// most recently written transcript (`projects/*/*.jsonl`), read from its last 256 KB.
    static func latestSession(folder: String) -> Session? {
        let projects = URL(fileURLWithPath: AccountData.normalized(folder)).appendingPathComponent("projects")
        let fm = FileManager.default
        guard let dirs = try? fm.contentsOfDirectory(at: projects, includingPropertiesForKeys: nil) else { return nil }
        var newest: (URL, Date)?
        for dir in dirs {
            guard let files = try? fm.contentsOfDirectory(at: dir, includingPropertiesForKeys: [.contentModificationDateKey]) else { continue }
            for file in files where file.pathExtension == "jsonl" {
                let date = (try? file.resourceValues(forKeys: [.contentModificationDateKey]))?.contentModificationDate ?? .distantPast
                if newest == nil || date > newest!.1 { newest = (file, date) }
            }
        }
        guard let file = newest?.0, let handle = try? FileHandle(forReadingFrom: file) else { return nil }
        defer { try? handle.close() }
        let size = (try? handle.seekToEnd()) ?? 0
        try? handle.seek(toOffset: size > UInt64(tailBytes) ? size - UInt64(tailBytes) : 0)
        let text = String(decoding: handle.readDataToEndOfFile(), as: UTF8.self)
        for line in text.split(separator: "\n").reversed() {
            guard let o = (try? JSONSerialization.jsonObject(with: Data(line.utf8))) as? [String: Any],
                  o["type"] as? String == "assistant", let message = o["message"] as? [String: Any],
                  let model = message["model"] as? String, model.hasPrefix("claude"),
                  let usage = message["usage"] as? [String: Any] else { continue }
            let tokens = ["input_tokens", "cache_read_input_tokens", "cache_creation_input_tokens"]
                .compactMap { (usage[$0] as? NSNumber)?.intValue }.reduce(0, +)
            let window = model.contains("[1m]") || tokens > 200_000 ? 1_000_000 : 200_000
            let cwd = (o["cwd"] as? String).flatMap { fm.fileExists(atPath: $0) ? $0 : nil }
            return Session(model: model, contextPercent: min(100, tokens * 100 / window), window: window, cwd: cwd)
        }
        return nil
    }

    /// `claude-opus-5-5` as "Opus 5.5", `claude-sonnet-4-5-20250929` as "Sonnet 4.5"; an id
    /// that isn't shaped so stays as it is.
    static func displayName(_ id: String) -> String {
        let parts = id.replacingOccurrences(of: "[1m]", with: "").split(separator: "-").map(String.init)
        guard parts.first == "claude", let i = parts.firstIndex(where: { $0.first?.isLetter == true && $0 != "claude" }) else { return id }
        let family = parts[i].prefix(1).uppercased() + parts[i].dropFirst()
        let version = parts[(i + 1)...].prefix { $0.count <= 2 && $0.allSatisfy(\.isNumber) }
        return version.isEmpty ? family : "\(family) \(version.joined(separator: "."))"
    }
}
