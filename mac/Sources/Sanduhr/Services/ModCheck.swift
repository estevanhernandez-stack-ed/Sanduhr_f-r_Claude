import Foundation

/// What `claude plugin validate <dir> --json` reported for one plugin (item 64), read into a risk
/// card. The validator reads the plugin's files and lists what its hooks module hooks and calls;
/// it never runs the mod's code (`claude plugin test` does, so Sanduhr never calls that here).
struct ModCheckReport: Equatable, Sendable {
    enum Risk: Int, Comparable, Sendable {
        case low, medium, high

        static func < (a: Risk, b: Risk) -> Bool { a.rawValue < b.rawValue }

        var title: String {
            switch self {
            case .low: "Low risk"
            case .medium: "Medium risk"
            case .high: "High risk"
            }
        }
    }

    /// Claude Code would load it: no errors.
    var passed = true
    var errors: [String] = []
    var warnings: [String] = []
    /// Engine events it hooks (`session.start`, `ui.render{component=AbovePrompt}`).
    var hooks: [String] = []
    /// `$` calls it makes, helpers dropped (`$.fs.read`).
    var calls: [String] = []
    var envReads: [String] = []
    var envWrites: [String] = []
    /// Hooks that can stop or rewrite what Claude Code does (`tool.call`), with "without .catch".
    var gating: [String] = []
    /// The validator's other notes, as written.
    var notes: [String] = []
    var risk = Risk.low
    /// Why the risk is what it is, one line each, most serious first.
    var reasons: [String] = []

    /// Reads the validator's JSON; nil when it isn't a report. Absolute paths under `dir` are
    /// shortened to the plugin's own relative ones.
    static func parse(_ data: Data, dir: String) -> ModCheckReport? {
        guard let o = (try? JSONSerialization.jsonObject(with: data)) as? [String: Any],
              o["manifest"] != nil || o["contents"] != nil else { return nil }
        var r = ModCheckReport()
        r.passed = o["success"] as? Bool ?? false
        var sections: [[String: Any]] = []
        if let m = o["manifest"] as? [String: Any] { sections.append(m) }
        sections += (o["contents"] as? [[String: Any]]) ?? []
        let short = Shortener(dir: dir)
        for s in sections {
            r.errors += messages(s["errors"]).map(short.apply)
            r.warnings += messages(s["warnings"]).map(short.apply)
            for note in (s["notes"] as? [String]) ?? [] { r.read(note: short.apply(note)) }
            for g in (s["gatingHooks"] as? [[String: Any]]) ?? [] {
                guard let hook = (g["hook"] as? String) ?? (g["pattern"] as? String) else { continue }
                let line = (g["hasCatch"] as? Bool) == false ? "\(hook) (without .catch)" : hook
                if !r.gating.contains(line) { r.gating.append(line) }
            }
        }
        if !r.errors.isEmpty { r.passed = false }
        (r.risk, r.reasons) = assess(r)
        return r
    }

    /// Errors and warnings as "path: message" lines (or the message alone).
    private static func messages(_ value: Any?) -> [String] {
        ((value as? [Any]) ?? []).compactMap { item in
            if let s = item as? String { return s }
            guard let d = item as? [String: Any], let message = d["message"] as? String else { return nil }
            if let path = d["path"] as? String, !path.isEmpty { return "\(path): \(message)" }
            return message
        }
    }

    /// The notes name a module, a kind and a comma list: `./register.tsx calls: $.fs.read (via x)`.
    private mutating func read(note: String) {
        let kinds: [(String, WritableKeyPath<ModCheckReport, [String]>)] = [
            (" hooks: ", \.hooks), (" calls: ", \.calls), (" env reads: ", \.envReads), (" env writes: ", \.envWrites),
        ]
        for (marker, path) in kinds {
            guard let range = note.range(of: marker) else { continue }
            for value in Self.split(String(note[range.upperBound...])) where !self[keyPath: path].contains(value) {
                self[keyPath: path].append(value)
            }
            return
        }
        if note.contains(" gating hook without .catch: ") { return }  // in gatingHooks already
        notes.append(note)
    }

    /// A comma list split outside braces and parentheses, " (via helper)" dropped, "nothing" empty.
    static func split(_ list: String) -> [String] {
        var parts: [String] = []
        var current = ""
        var depth = 0
        for ch in list {
            if ch == "{" || ch == "(" { depth += 1 }
            if ch == "}" || ch == ")" { depth = max(0, depth - 1) }
            if ch == ",", depth == 0 {
                parts.append(current)
                current = ""
            } else {
                current.append(ch)
            }
        }
        parts.append(current)
        return parts.compactMap { raw in
            var p = raw.trimmingCharacters(in: .whitespaces)
            if let via = p.range(of: " (via ") { p = String(p[..<via.lowerBound]) }
            return p.isEmpty || p == "nothing" ? nil : p
        }
    }

    /// The research doc's colors: red for programs, the network, writing files or settings,
    /// gating or rewriting what Claude does, and secret-looking env names; amber for reading
    /// files and the environment; green for drawing, timers and state.
    static func assess(_ r: ModCheckReport) -> (Risk, [String]) {
        var high: [String] = []
        var medium: [String] = []
        func calls(_ prefixes: [String]) -> [String] { r.calls.filter { c in prefixes.contains { c.hasPrefix($0) } } }
        let process = calls(["$.process."])
        if !process.isEmpty { high.append("Runs programs: " + process.joined(separator: ", ")) }
        let http = calls(["$.http."])
        if !http.isEmpty { high.append("Reaches the network: " + http.joined(separator: ", ")) }
        let writes = calls(["$.fs.write", "$.fs.remove", "$.fs.rename", "$.fs.mkdir", "$.config.set"])
        if !writes.isEmpty { high.append("Writes files or settings: " + writes.joined(separator: ", ")) }
        let steering = calls(["$.prompt.", "$.session.append", "$.agent.", "$.tool.", "$.telemetry."])
            + r.hooks.filter { h in ["tool.", "prompt.", "session.append", "telemetry."].contains { h.hasPrefix($0) } }
        if !steering.isEmpty { high.append("Changes what Claude does: " + steering.joined(separator: ", ")) }
        if !r.gating.isEmpty { high.append("Can stop or rewrite tool calls: " + r.gating.joined(separator: ", ")) }
        let secrets = r.envReads.filter { $0.range(of: "KEY|TOKEN|SECRET|PASSWORD", options: [.regularExpression, .caseInsensitive]) != nil }
        if !secrets.isEmpty { high.append("Reads secret-looking environment variables: " + secrets.joined(separator: ", ")) }
        let reads = calls(["$.fs."]).filter { !writes.contains($0) }
        if !reads.isEmpty { medium.append("Reads files: " + reads.joined(separator: ", ")) }
        let env = r.envReads.filter { !secrets.contains($0) }
        if !env.isEmpty { medium.append("Reads environment variables: " + env.joined(separator: ", ")) }
        if !r.envWrites.isEmpty { medium.append("Sets environment variables: " + r.envWrites.joined(separator: ", ")) }
        let model = calls(["$.model."])
        if !model.isEmpty { medium.append("Asks a model: " + model.joined(separator: ", ")) }
        let risk: Risk = !high.isEmpty ? .high : (!medium.isEmpty ? .medium : .low)
        let reasons = high + medium
        return (risk, reasons.isEmpty ? ["Draws and keeps its own state only."] : reasons)
    }

    /// Turns `<dir>/hooks/r.ts` into `hooks/r.ts` and `<dir>` into `.` (the real path too, as
    /// the validator resolves `/tmp` to `/private/tmp`).
    private struct Shortener {
        let prefixes: [String]

        init(dir: String) {
            let a = (dir as NSString).standardizingPath
            let b = URL(fileURLWithPath: a).resolvingSymlinksInPath().path
            prefixes = Array(Set([a, b])).sorted { $0.count > $1.count }
        }

        func apply(_ s: String) -> String {
            var out = s
            for p in prefixes where !p.isEmpty && p != "/" {
                out = out.replacingOccurrences(of: p + "/", with: "").replacingOccurrences(of: p, with: ".")
            }
            return out
        }
    }
}

/// The Check button's answer.
enum ModCheckOutcome: Equatable, Sendable {
    case report(ModCheckReport)
    /// No `claude` found.
    case unavailable
    /// It didn't answer within the time allowed and was stopped.
    case timedOut
    /// It ran but gave no report: the reason, short.
    case failed(String)
}

/// Runs `claude plugin validate <dir> --json` for the Mods page's Check button (item 64): found on
/// PATH or in the usual install folders, stopped after 10 seconds. Never `claude plugin test`,
/// which runs the mod's code.
enum ModCheck {
    static let timeout: TimeInterval = 10

    /// Where Claude Code's installers put `claude`, after PATH (an app started from the Dock has
    /// a short PATH).
    static func commonPaths(home: String) -> [String] {
        [
            (home as NSString).appendingPathComponent(".local/bin/claude"),
            (home as NSString).appendingPathComponent(".claude/local/claude"),
            "/opt/homebrew/bin/claude",
            "/usr/local/bin/claude",
            (home as NSString).appendingPathComponent(".npm-global/bin/claude"),
            (home as NSString).appendingPathComponent(".bun/bin/claude"),
        ]
    }

    /// The first executable `claude` on `PATH`, then in `commonPaths`; nil when there is none.
    static func findClaude(environment: [String: String] = ProcessInfo.processInfo.environment,
                           home: String = NSHomeDirectory(),
                           isExecutable: (String) -> Bool = { FileManager.default.isExecutableFile(atPath: $0) }) -> String? {
        let onPath = (environment["PATH"] ?? "").split(separator: ":").map { ($0 as NSString).appendingPathComponent("claude") }
        return (onPath + commonPaths(home: home)).first(where: isExecutable)
    }

    /// The arguments Check passes: validate only, as JSON.
    static func arguments(dir: String) -> [String] { ["plugin", "validate", dir, "--json"] }

    /// Runs the validator on `dir`. Its folder and the usual bin folders go first on PATH, so a
    /// `claude` that is a node script finds node.
    static func run(claude: String?, dir: String, timeout: TimeInterval = timeout) -> ModCheckOutcome {
        guard let claude else { return .unavailable }
        let p = Process()
        p.executableURL = URL(fileURLWithPath: claude)
        p.arguments = arguments(dir: dir)
        var env = ProcessInfo.processInfo.environment
        let bins = [(claude as NSString).deletingLastPathComponent, "/opt/homebrew/bin", "/usr/local/bin", "/usr/bin", "/bin"]
        env["PATH"] = (bins + [env["PATH"] ?? ""]).filter { !$0.isEmpty }.joined(separator: ":")
        env["NO_COLOR"] = "1"
        p.environment = env
        p.currentDirectoryURL = URL(fileURLWithPath: NSTemporaryDirectory())
        let output = Pipe(), errors = Pipe()
        p.standardInput = FileHandle.nullDevice
        p.standardOutput = output
        p.standardError = errors
        let box = Box()
        let done = DispatchGroup()
        do { try p.run() } catch { return .failed("Couldn't start \(claude).") }
        done.enter()
        DispatchQueue.global(qos: .userInitiated).async {
            box.err = errors.fileHandleForReading.readDataToEndOfFile()
            done.leave()
        }
        done.enter()
        DispatchQueue.global(qos: .userInitiated).async {
            box.out = output.fileHandleForReading.readDataToEndOfFile()
            done.leave()
        }
        if done.wait(timeout: .now() + timeout) == .timedOut {
            p.terminate()
            if done.wait(timeout: .now() + 1) == .timedOut { kill(p.processIdentifier, SIGKILL) }
            p.waitUntilExit()
            return .timedOut
        }
        p.waitUntilExit()
        if let report = ModCheckReport.parse(box.out, dir: dir) { return .report(report) }
        let reason = String(decoding: box.err.prefix(300), as: UTF8.self)
            .split(separator: "\n").first.map(String.init) ?? ""
        return .failed(reason.isEmpty ? "It gave no report (exit \(p.terminationStatus))." : reason)
    }

    private final class Box: @unchecked Sendable {
        var out = Data()
        var err = Data()
    }
}
