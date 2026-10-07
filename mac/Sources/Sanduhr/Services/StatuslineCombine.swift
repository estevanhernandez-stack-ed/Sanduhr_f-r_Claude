import Foundation

/// How Sanduhr's statusline went into a folder that already had one (item 63).
enum StatuslineMode: String, Codable, Sendable {
    /// Sanduhr's line took the user's place; Remove puts theirs back.
    case replace
    /// Sanduhr's command runs the user's first and prints both.
    case combine
}

/// Where Sanduhr's segment goes in a combined statusline.
enum StatuslineJoin: String, Codable, CaseIterable, Sendable {
    /// The user's rows, then Sanduhr's on a row of its own.
    case line
    /// Appended to the user's last row, after an SGR reset (`line` when it would not fit).
    case same
}

/// What a statusline puts between its segments (item 63b), as the runner names it.
enum StatuslineSeparator: String, CaseIterable, Sendable {
    case powerline
    case powerlineThin = "powerline-thin"
    case bar, pipe, bullet, dot, spaces, none

    var title: String {
        switch self {
        case .powerline: "Powerline arrow (needs a Nerd Font)"
        case .powerlineThin: "Powerline thin arrow (needs a Nerd Font)"
        case .bar: "Bar │"
        case .pipe: "Pipe |"
        case .bullet: "Bullet •"
        case .dot: "Dot ·"
        case .spaces: "Two or more spaces"
        case .none: "None (one segment)"
        }
    }
}

/// What a combined statusline puts between segments when the user picks it (item 63b's Join
/// with), as the runner names it. Nil (not one of these) keeps their own separators.
enum StatuslineJoinGlyph: String, CaseIterable, Sendable {
    case bar, pipe, dot, bullet, powerline
    case powerlineThin = "powerline-thin"
    case spaces

    var title: String {
        switch self {
        case .bar: "Bar │"
        case .pipe: "Pipe |"
        case .dot: "Dot ·"
        case .bullet: "Bullet •"
        case .powerline: "Powerline arrow (needs a Nerd Font)"
        case .powerlineThin: "Powerline thin arrow (needs a Nerd Font)"
        case .spaces: "Two spaces"
        }
    }
}

/// Sanduhr's own segments in a combined statusline (item 63b), in the order they print.
enum SanduhrSegment: String, CaseIterable, Sendable {
    case session, weekly, resets, context, model

    /// What Sanduhr's line shows without `--mine`.
    static let defaults: [SanduhrSegment] = [.session, .weekly, .resets]

    var title: String {
        switch self {
        case .session: "Session"
        case .weekly: "Weekly"
        case .resets: "Weekly reset"
        case .context: "Context"
        case .model: "Model"
        }
    }

    /// `--mine`'s list: names in this order, each once, at least one. Nil for anything else.
    static func parseList(_ text: String) -> [SanduhrSegment]? {
        let names = text.components(separatedBy: ",")
        let segments = names.compactMap(SanduhrSegment.init(rawValue:))
        guard segments.count == names.count, !segments.isEmpty else { return nil }
        let order = segments.map { allCases.firstIndex(of: $0)! }
        guard zip(order, order.dropFirst()).allSatisfy({ $0 < $1 }) else { return nil }
        return segments
    }

    static func list(_ segments: [SanduhrSegment]) -> String {
        allCases.filter(segments.contains).map(\.rawValue).joined(separator: ",")
    }
}

/// Which of the user's segments a combined statusline keeps (item 63b), `--keep-theirs-b64`'s
/// payload: matchers (a segment's leading token) kept and dropped, whether segments never seen
/// stay, a separator per line where one was chosen (nil: detected), and the glyph to join with
/// (nil: their own).
struct StatuslinePicks: Equatable, Sendable {
    var keep: [String] = []
    var drop: [String] = []
    var keepNew = true
    var separators: [StatuslineSeparator?] = []
    var joinWith: StatuslineJoinGlyph?
    /// Looks for their segments, by matcher.
    var styles: [String: SegmentStyle] = [:]
    /// Looks for Sanduhr's segments.
    var ours: [SanduhrSegment: SegmentStyle] = [:]

    static let maxMatchers = 64
    static let maxMatcherLength = 64
    static let maxLines = 16

    /// The JSON the runner reads: sorted keys, `new` only when false, `sep` only when a line has
    /// a choice (trailing detected lines left out).
    var json: String {
        var o: [String: Any] = [:]
        if !keep.isEmpty { o["keep"] = keep }
        if !drop.isEmpty { o["drop"] = drop }
        if !keepNew { o["new"] = false }
        var seps = separators
        while let last = seps.last, last == nil { seps.removeLast() }
        if !seps.isEmpty { o["sep"] = seps.map { $0.map { $0.rawValue as Any } ?? NSNull() } }
        if let joinWith { o["with"] = joinWith.rawValue }
        let theirStyles = styles.filter { !$0.value.isEmpty }
        if !theirStyles.isEmpty { o["style"] = theirStyles.mapValues(\.json) }
        let ourStyles = ours.filter { !$0.value.isEmpty }
        if !ourStyles.isEmpty { o["ours"] = Dictionary(uniqueKeysWithValues: ourStyles.map { ($0.key.rawValue, $0.value.json) }) }
        let data = (try? JSONSerialization.data(withJSONObject: o, options: [.sortedKeys, .withoutEscapingSlashes])) ?? Data("{}".utf8)
        return String(decoding: data, as: UTF8.self)
    }

    var base64: String { Data(json.utf8).base64EncodedString() }

    init(keep: [String] = [], drop: [String] = [], keepNew: Bool = true, separators: [StatuslineSeparator?] = [],
         joinWith: StatuslineJoinGlyph? = nil, styles: [String: SegmentStyle] = [:],
         ours: [SanduhrSegment: SegmentStyle] = [:]) {
        self.keep = keep
        self.drop = drop
        self.keepNew = keepNew
        self.separators = separators
        self.joinWith = joinWith
        self.styles = styles
        self.ours = ours
    }

    /// The payload as the runner accepts it, nil for anything it would refuse: canonical base64
    /// of a JSON object with only `keep`, `drop` (lists of 1 to 64 character matchers, at most
    /// 64), `new` (a boolean), `sep` (at most 16 separator names or nulls) and `with` (a
    /// join glyph's name).
    init?(base64 payload: String) {
        guard IntegrationInstaller.isCanonicalBase64(payload), let data = Data(base64Encoded: payload),
              let text = String(data: data, encoding: .utf8),
              let o = (try? JSONSerialization.jsonObject(with: Data(text.utf8))) as? [String: Any],
              Set(o.keys).isSubset(of: ["keep", "drop", "new", "sep", "with", "style", "ours"]) else { return nil }
        func matchers(_ key: String) -> [String]? {
            guard let v = o[key] else { return [] }
            guard let list = v as? [Any], list.count <= Self.maxMatchers else { return nil }
            let strings = list.compactMap { $0 as? String }
            guard strings.count == list.count,
                  strings.allSatisfy({ (1...Self.maxMatcherLength).contains($0.unicodeScalars.count) }) else { return nil }
            return strings
        }
        guard let keep = matchers("keep"), let drop = matchers("drop") else { return nil }
        self.keep = keep
        self.drop = drop
        if let v = o["new"] {
            guard let n = v as? NSNumber, CFGetTypeID(n) == CFBooleanGetTypeID() else { return nil }
            keepNew = n.boolValue
        }
        if let v = o["style"] {
            guard let map = v as? [String: Any], map.count <= Self.maxMatchers else { return nil }
            for (key, value) in map {
                guard (1...Self.maxMatcherLength).contains(key.unicodeScalars.count),
                      let style = SegmentStyle(json: value) else { return nil }
                styles[key] = style
            }
        }
        if let v = o["ours"] {
            guard let map = v as? [String: Any] else { return nil }
            for (key, value) in map {
                guard let segment = SanduhrSegment(rawValue: key), let style = SegmentStyle(json: value) else { return nil }
                ours[segment] = style
            }
        }
        if let v = o["with"] {
            guard let name = v as? String, let glyph = StatuslineJoinGlyph(rawValue: name) else { return nil }
            joinWith = glyph
        }
        if let v = o["sep"] {
            guard let list = v as? [Any], list.count <= Self.maxLines else { return nil }
            var seps: [StatuslineSeparator?] = []
            for item in list {
                if item is NSNull {
                    seps.append(nil)
                } else if let s = item as? String, let sep = StatuslineSeparator(rawValue: s) {
                    seps.append(sep)
                } else {
                    return nil
                }
            }
            separators = seps
        }
    }
}

/// The picks a combined statusline carries (item 63b): theirs (nil keeps every segment) and
/// Sanduhr's own (nil: the default three).
struct StatuslineSelection: Equatable, Sendable {
    var theirs: StatuslinePicks?
    var mine: [SanduhrSegment]?

    init(theirs: StatuslinePicks? = nil, mine: [SanduhrSegment]? = nil) {
        self.theirs = theirs
        self.mine = mine
    }
}

/// Sanduhr's statusline command taken apart: `<python> <…/sanduhr_statusline.py>`, optionally
/// followed by exactly `--chain-b64 <base64> --join line|same [--padding <n>]
/// [--keep-theirs-b64 <base64>] [--mine <list>]`.
struct StatuslineCommand: Equatable, Sendable {
    /// `<python> <script>`, as written.
    var base: String
    /// The chained command (decoded), nil for Sanduhr's line alone.
    var chain: String?
    var join: StatuslineJoin?
    var padding: Int?
    /// The segments kept (item 63b); empty without picks.
    var selection = StatuslineSelection()
}

extension IntegrationInstaller {
    /// The keys of the user's `statusLine` object Sanduhr's value carries over (their padding,
    /// refresh and vim choices still apply to the line that shows).
    static let statuslineSiblingKeys = ["padding", "refreshInterval", "hideVimModeIndicator"]

    /// How many Sanduhr commands deep an unwrap goes before giving up.
    static let unwrapLimit = 16

    /// Sanduhr's statusline command, alone or combined with `chain` (and its picks).
    static func statuslineCommand(python: String, script: String, chain: String? = nil,
                                  join: StatuslineJoin = .line, padding: Int? = nil,
                                  selection: StatuslineSelection = StatuslineSelection()) -> String {
        let base = "\(shellQuoted(python)) \(shellQuoted(script))"
        guard let chain else { return base }
        var c = "\(base) --chain-b64 \(Data(chain.utf8).base64EncodedString()) --join \(join.rawValue)"
        if let padding, padding > 0, padding <= 999 { c += " --padding \(padding)" }
        c += pickFlags(selection).map { " " + $0 }.joined()
        return c
    }

    /// `--keep-theirs-b64 <b64> --mine <list>` for `selection`, each left out when it is the default.
    static func pickFlags(_ selection: StatuslineSelection) -> [String] {
        var out: [String] = []
        if let theirs = selection.theirs { out += ["--keep-theirs-b64", theirs.base64] }
        if let mine = selection.mine, !mine.isEmpty { out += ["--mine", SanduhrSegment.list(mine)] }
        return out
    }

    /// Base64 as the runner takes it: the standard alphabet, padded, `=` only at the end.
    static func isCanonicalBase64(_ payload: String) -> Bool {
        let alphabet = CharacterSet(charactersIn: "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/=")
        return !payload.isEmpty && payload.count % 4 == 0 && payload.unicodeScalars.allSatisfy(alphabet.contains)
            && !payload.dropLast(2).contains("=")
    }

    /// `command` taken apart when it is Sanduhr's, nil when it is anyone else's. Sanduhr's line
    /// alone is a command that is only `<python> <…/sanduhr_statusline.py>` (no `;`, `&`, a pipe,
    /// a substitution or a newline); combined, that is followed by exactly the runner's flags in
    /// order, the payload valid base64 of a non-blank UTF-8 command, the picks exactly what the
    /// runner accepts. Nothing looser: a command of the user's that merely calls the script stays
    /// theirs.
    static func parseStatusline(_ command: String) -> StatuslineCommand? {
        let c = command.trimmingCharacters(in: .whitespaces)
        if [";", "&", "|", "$(", "`", "\n", "\r"].contains(where: { c.contains($0) }) { return nil }
        if isPlainStatusline(c) { return StatuslineCommand(base: c) }
        var words = c.components(separatedBy: " ")
        var selection = StatuslineSelection()
        if words.count >= 2, words[words.count - 2] == "--mine" {
            guard let mine = SanduhrSegment.parseList(words[words.count - 1]) else { return nil }
            selection.mine = mine
            words.removeLast(2)
        }
        if words.count >= 2, words[words.count - 2] == "--keep-theirs-b64" {
            guard let picks = StatuslinePicks(base64: words[words.count - 1]) else { return nil }
            selection.theirs = picks
            words.removeLast(2)
        }
        var padding: Int?
        if words.count >= 2, words[words.count - 2] == "--padding" {
            let n = words[words.count - 1]
            guard (1...3).contains(n.count), n.allSatisfy({ $0.isASCII && $0.isNumber }) else { return nil }
            padding = Int(n)
            words.removeLast(2)
        }
        guard words.count >= 5, words[words.count - 4] == "--chain-b64", words[words.count - 2] == "--join",
              let join = StatuslineJoin(rawValue: words[words.count - 1]) else { return nil }
        let payload = words[words.count - 3]
        guard isCanonicalBase64(payload),
              let data = Data(base64Encoded: payload), let chain = String(data: data, encoding: .utf8),
              !chain.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { return nil }
        let base = words.dropLast(4).joined(separator: " ")
        guard isPlainStatusline(base) else { return nil }
        return StatuslineCommand(base: base, chain: chain, join: join, padding: padding, selection: selection)
    }

    /// Only `<python> <…/sanduhr_statusline.py>` (the rule before Combine).
    private static func isPlainStatusline(_ c: String) -> Bool {
        let name = IntegrationScripts.statuslineScript
        return c.hasSuffix(name) || c.hasSuffix(name + "'") || c.hasSuffix(name + "\"")
    }

    /// The user's own command inside `command`: itself when it isn't Sanduhr's, the innermost
    /// foreign command when it is Sanduhr's combined with something (of any seat or version),
    /// nil when it is only Sanduhr's line (there is nothing of theirs to keep).
    static func innermostForeign(_ command: String) -> String? {
        var c = command
        for _ in 0..<unwrapLimit {
            guard let parsed = parseStatusline(c) else { return c }
            guard let chain = parsed.chain else { return nil }
            c = chain
        }
        return nil
    }

    /// The members of the user's `statusLine` Sanduhr's value keeps: `padding` and
    /// `refreshInterval` when they are whole numbers, `hideVimModeIndicator` when a boolean.
    static func carriedSiblings(_ existing: Any?) -> [JSONEdit.Pair] {
        guard let o = existing as? [String: Any] else { return [] }
        var out: [JSONEdit.Pair] = []
        for key in statuslineSiblingKeys {
            guard let n = o[key] as? NSNumber else { continue }
            let isBool = CFGetTypeID(n) == CFBooleanGetTypeID()
            if key == "hideVimModeIndicator" {
                if isBool { out.append(JSONEdit.Pair(key, .bool(n.boolValue))) }
            } else if !isBool, let i = Int(exactly: n.doubleValue) {
                out.append(JSONEdit.Pair(key, .int(i)))
            }
        }
        return out
    }

    /// Sanduhr's `statusLine` value: its command (combined with `chain` when given, with its
    /// picks) and the carried members.
    func statuslineEntry(python: String, chain: String?, join: StatuslineJoin, siblings: [JSONEdit.Pair],
                         selection: StatuslineSelection = StatuslineSelection()) -> JSONEdit.Value {
        let padding: Int? = siblings.first { $0.key == "padding" }.flatMap {
            if case .int(let n) = $0.value { return n }
            return nil
        }
        let command = Self.statuslineCommand(python: python, script: scripts.installedPath(IntegrationScripts.statuslineScript),
                                             chain: chain, join: join, padding: padding, selection: selection)
        return .object([JSONEdit.Pair("type", .string("command")), JSONEdit.Pair("command", .string(command))] + siblings)
    }

    /// The folder's statusline is Sanduhr's combined with the user's own.
    func isCombined(folder: String) -> Bool {
        let file = configFile(.statusline, folder: folder)
        guard let data = FileManager.default.contents(atPath: file), let root = try? JSONEdit.root(data),
              let o = root[Self.statusLineKey] as? [String: Any], let command = o["command"] as? String else { return false }
        return Self.parseStatusline(command)?.chain != nil
    }

    /// The app's own copy of the runner (the installed one may not be there yet), nil without it.
    private var previewScript: String? {
        guard let source = scripts.source?.appendingPathComponent(IntegrationScripts.statuslineScript).path,
              FileManager.default.fileExists(atPath: source) else { return nil }
        return source
    }

    /// The arguments that run the user's command once and list its segments and Sanduhr's for
    /// the sheet's chips (item 63b). Nil without the script.
    func inspectArguments(chain: String) -> [String]? {
        guard let script = previewScript else { return nil }
        return [script, "--inspect-b64", Data(chain.utf8).base64EncodedString()]
    }

    /// The arguments that print Sanduhr's own statusline, alone, as Claude Code runs it, for
    /// Settings, Integrations' preview (item 68). Nil without the script.
    func sampleArguments() -> [String]? {
        previewScript.map { [$0] }
    }

    /// The arguments that print what the combined line would, from the user's output `theirs`
    /// (their command doesn't run again), for the sheet's live preview. Nil without the script.
    func composeArguments(theirs: String, join: StatuslineJoin, selection: StatuslineSelection) -> [String]? {
        guard let script = previewScript else { return nil }
        return [script, "--compose-b64", Data(theirs.utf8).base64EncodedString(), "--join", join.rawValue]
            + Self.pickFlags(selection)
    }
}

/// What `--inspect-b64` prints (item 63b): the user's output against the sample JSON, each line
/// split under every separator (the detected one named), and Sanduhr's segments one by one.
struct StatuslineInspection: Decodable, Equatable, Sendable {
    struct Segment: Decodable, Equatable, Sendable {
        let matcher: String
        let text: String
        /// What it shows, as the runner's classifier names it (nil from an older script).
        var kind: String?
    }

    struct Split: Decodable, Equatable, Sendable {
        /// The runner keeps this line whole under this separator.
        let doubt: Bool
        let segments: [Segment]
    }

    struct Line: Decodable, Equatable, Sendable {
        /// The line as printed, escapes included.
        let text: String
        /// The separator detected, by the runner's name.
        let auto: String
        let splits: [String: Split]
    }

    struct Mine: Decodable, Equatable, Sendable {
        let name: String
        /// What the segment shows now; empty when it has nothing to show.
        let text: String
    }

    let theirs: String
    let lines: [Line]
    let mine: [Mine]
    /// A line of Sanduhr's that shows whole whatever is picked (stale, update), else empty.
    let notice: String

    static func decode(_ text: String) -> StatuslineInspection? {
        try? JSONDecoder().decode(StatuslineInspection.self, from: Data(text.utf8))
    }
}

/// Runs the combined statusline once against sample statusline JSON for the "other statusline"
/// sheet (item 63). The JSON is the shape Claude Code documents, with made-up numbers: no real
/// session's data goes in, except when the user presses Test with live data (`StatuslineLiveInput`). The user's command runs as Claude Code would run it.
enum StatuslinePreview {
    /// The docs' sample statusline input, trimmed, with resets an hour and three days out.
    static func sampleJSON(now: Date = Date()) -> Data {
        (try? JSONSerialization.data(withJSONObject: sample(now: now), options: [.sortedKeys])) ?? Data("{}".utf8)
    }

    /// The sample as a dictionary, for the live input to fill in.
    static func sample(now: Date = Date()) -> [String: Any] {
        let t = Int(now.timeIntervalSince1970)
        return [
            "cwd": "/home/user/project",
            "session_id": "abc123",
            "session_name": "my-session",
            "transcript_path": "/path/to/transcript.jsonl",
            "model": ["id": "claude-opus-5-5", "display_name": "Opus"],
            "workspace": ["current_dir": "/home/user/project", "project_dir": "/home/user/project", "added_dirs": [String]()],
            "version": "2.1.292",
            "output_style": ["name": "default"],
            "cost": ["total_cost_usd": 0.01234, "total_duration_ms": 45000, "total_api_duration_ms": 2300,
                     "total_lines_added": 156, "total_lines_removed": 23],
            "context_window": ["total_input_tokens": 15500, "total_output_tokens": 1200, "context_window_size": 200000,
                               "used_percentage": 8, "remaining_percentage": 92],
            "exceeds_200k_tokens": false,
            "rate_limits": ["five_hour": ["used_percentage": 23.5, "resets_at": t + 3600],
                            "seven_day": ["used_percentage": 41.2, "resets_at": t + 3 * 86400]],
            "vim": ["mode": "NORMAL"],
        ]
    }

    /// The width the preview pretends the terminal has.
    static let columns = 100

    /// Runs `python args` with the sample on stdin (or `input`); its stdout, or nil when it couldn't run.
    /// The runner keeps its own 1.5 s budget; this stops it after 4 s whatever happens.
    static func run(python: String, arguments: [String], input stdin: Data? = nil) -> String? {
        let p = Process()
        p.executableURL = URL(fileURLWithPath: python)
        p.arguments = arguments
        var env = ProcessInfo.processInfo.environment
        env["COLUMNS"] = String(columns)
        env.removeValue(forKey: "SANDUHR_CHAIN_DEPTH")
        p.environment = env
        let input = Pipe(), output = Pipe()
        p.standardInput = input
        p.standardOutput = output
        p.standardError = FileHandle.nullDevice
        do { try p.run() } catch { return nil }
        input.fileHandleForWriting.write(stdin ?? sampleJSON())
        try? input.fileHandleForWriting.close()
        let done = DispatchSemaphore(value: 0)
        let box = OutputBox()
        DispatchQueue.global(qos: .userInitiated).async {
            box.data = output.fileHandleForReading.readDataToEndOfFile()
            done.signal()
        }
        if done.wait(timeout: .now() + 4) == .timedOut {
            p.terminate()
            _ = done.wait(timeout: .now() + 1)
        }
        p.waitUntilExit()
        return String(decoding: box.data.prefix(64 * 1024), as: UTF8.self)
    }

    private final class OutputBox: @unchecked Sendable {
        var data = Data()
    }
}
