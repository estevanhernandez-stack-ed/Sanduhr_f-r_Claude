import Foundation

/// Codex's local burn for the Vault (every-agents spec, section 3; parity with Windows
/// `CodexBurnSource.cs`). Walks `sessions/**/rollout-*.jsonl` and `.jsonl.zst` under every Codex
/// home. One rollout is one Vault session, keyed by its name without the extension, so a rollout
/// Codex compresses later writes the same row and replaces it. Reads only timestamps, segment
/// boundaries, cwd, model names and token counts; never message text or account ids.
struct CodexBurnReader: AgentBurnSource {
    static let plainSuffix = ".jsonl"
    static let zstdSuffix = ".jsonl.zst"
    /// How far a compressed rollout may expand, as a multiple of its size on disk, above the 64 MB
    /// floor. A fixed 64 MB dropped real long sessions whole on Windows (a 70 MB rollout expands to
    /// 174 MB); a bomb expands thousands of times, far past 64.
    static let maxExpansion: Int64 = 64

    var homes: [URL]
    var fileSystem: CodexFileSystem = CodexDisk()
    /// The decompression cap for a compressed rollout of this size on disk (tests lower it).
    var capOf: (Int64) -> Int = { CodexBurnReader.decompressedCap($0) }

    var agent: AgentSource { .codex }

    /// The session a rollout writes: its file name without `.jsonl` or `.jsonl.zst`.
    static func sessionID(of fileName: String) -> String {
        let lower = fileName.lowercased()
        if lower.hasSuffix(zstdSuffix) { return String(fileName.dropLast(zstdSuffix.count)) }
        if lower.hasSuffix(plainSuffix) { return String(fileName.dropLast(plainSuffix.count)) }
        return fileName
    }

    /// 64 MB, or 64 times the file on disk when that is larger. Output is handed on in pieces, so
    /// the cap bounds a bomb's work, not memory.
    static func decompressedCap(_ compressedLength: Int64) -> Int {
        let (scaled, overflow) = compressedLength.multipliedReportingOverflow(by: maxExpansion)
        return Int(clamping: max(Int64(Zstd.defaultCap), overflow ? Int64.max : scaled))
    }

    func files() throws -> [AgentBurnFile] {
        var out: [AgentBurnFile] = []
        for home in homes {
            let sessions = home.appendingPathComponent("sessions", isDirectory: true)
            guard fileSystem.isDirectory(sessions) else { continue }
            for f in fileSystem.rollouts(under: sessions) where CodexDisk.isRolloutName(f.url.lastPathComponent) {
                let name = f.url.lastPathComponent
                out.append(AgentBurnFile(path: f.url.path, sessionID: Self.sessionID(of: name), cwd: nil,
                                         compressed: name.lowercased().hasSuffix(Self.zstdSuffix),
                                         length: Int64(clamping: f.size), mtimeTicks: VaultTime.ticks(f.modified)))
            }
        }
        return out
    }

    func read(_ file: AgentBurnFile, from offset: Int64, state: String?) throws -> AgentBurnRead {
        var parser = CodexLineParser(state: file.compressed ? nil : state)
        var read = AgentBurnRead()
        let handle: (Data) -> Void = { line in
            let (event, bad) = parser.parse(line)
            if bad { read.skippedLines += 1 }
            if let event { read.events.append(event) }
        }
        if file.compressed {
            read.endOffset = try AgentBurnFileReader.lines(
                zstd: file.path, cap: capOf(file.length), handle)
        } else {
            read.endOffset = try AgentBurnFileReader.lines(plain: file.path, from: offset, handle)
        }
        read.state = parser.savedState()
        return read
    }
}

/// Codex rollout lines (Windows `CodexLineParser`). `session_meta` opens a segment and restarts the
/// running total; `turn_context` sets the model and cwd in force; a `token_count` event counts the
/// change in `info.total_token_usage` since the segment's previous reading, so a repeated reading
/// counts zero and a total that drops counts `last_token_usage`. Fresh input is
/// `input_tokens - cached_input_tokens`; output already includes reasoning.
///
/// The saved state is the model and the segment's last total, never a path or a session id: a
/// resumed parser learns the cwd again from the next `session_meta` or `turn_context`.
struct CodexLineParser {
    struct Usage: Codable, Equatable {
        var input: Int64 = 0
        var cached: Int64 = 0
        var cacheWrite: Int64 = 0
        var output: Int64 = 0
        var total: Int64 = 0

        enum CodingKeys: String, CodingKey {
            case input = "in", cached
            case cacheWrite = "cache_write"
            case output = "out", total
        }

        init() {}

        init(from decoder: Decoder) throws {
            let c = try decoder.container(keyedBy: CodingKeys.self)
            input = try c.decodeIfPresent(Int64.self, forKey: .input) ?? 0
            cached = try c.decodeIfPresent(Int64.self, forKey: .cached) ?? 0
            cacheWrite = try c.decodeIfPresent(Int64.self, forKey: .cacheWrite) ?? 0
            output = try c.decodeIfPresent(Int64.self, forKey: .output) ?? 0
            total = try c.decodeIfPresent(Int64.self, forKey: .total) ?? 0
        }

        static func of(_ o: [String: Any]) -> Usage {
            var u = Usage()
            u.input = CCLogReader.number(o["input_tokens"])
            u.cached = CCLogReader.number(o["cached_input_tokens"])
            u.cacheWrite = CCLogReader.number(o["cache_write_input_tokens"])
            u.output = CCLogReader.number(o["output_tokens"])
            u.total = CCLogReader.number(o["total_tokens"])
            return u
        }

        func minus(_ b: Usage) -> Usage {
            var u = Usage()
            u.input = max(0, input - b.input)
            u.cached = max(0, cached - b.cached)
            u.cacheWrite = max(0, cacheWrite - b.cacheWrite)
            u.output = max(0, output - b.output)
            u.total = max(0, total - b.total)
            return u
        }
    }

    struct State: Codable, Equatable {
        var model: String?
        var hasTotal = false
        var last = Usage()

        enum CodingKeys: String, CodingKey {
            case model
            case hasTotal = "has_total"
            case last
        }

        init() {}

        init(from decoder: Decoder) throws {
            let c = try decoder.container(keyedBy: CodingKeys.self)
            model = try c.decodeIfPresent(String.self, forKey: .model)
            hasTotal = try c.decodeIfPresent(Bool.self, forKey: .hasTotal) ?? false
            last = try c.decodeIfPresent(Usage.self, forKey: .last) ?? Usage()
        }
    }

    private(set) var state = State()
    /// This pass only, never saved.
    private var cwd: String?

    /// Resumed from a saved state; one that can't be read restarts the segment total, never fails.
    init(state saved: String? = nil) {
        guard let saved, let data = saved.data(using: .utf8),
              let s = try? JSONDecoder().decode(State.self, from: data) else { return }
        state = s
    }

    func savedState() -> String? {
        let e = JSONEncoder()
        e.outputFormatting = [.sortedKeys]
        guard let data = try? e.encode(state) else { return nil }
        return String(decoding: data, as: UTF8.self)
    }

    private static let marks = ["\"session_meta\"", "\"turn_context\"", "\"token_count\""].map { Data($0.utf8) }

    /// One trimmed, non-empty line: the event it carries, if any, and whether it was a bad line
    /// (it looked like one of Codex's records and did not parse).
    mutating func parse(_ line: Data) -> (BurnEvent?, bad: Bool) {
        guard Self.marks.contains(where: { line.range(of: $0) != nil }) else { return (nil, false) }
        guard let any = try? JSONSerialization.jsonObject(with: line, options: [.fragmentsAllowed]),
              let d = any as? [String: Any], let p = d["payload"] as? [String: Any] else { return (nil, true) }
        switch d["type"] as? String {
        case "session_meta":
            cwd = (p["cwd"] as? String) ?? cwd
            state.hasTotal = false
            state.last = Usage()
            return (nil, false)
        case "turn_context":
            state.model = (p["model"] as? String) ?? state.model
            cwd = (p["cwd"] as? String) ?? cwd
            return (nil, false)
        case "event_msg" where p["type"] as? String == "token_count":
            return (count(d, p), false)
        default:
            return (nil, false)
        }
    }

    mutating func parse(_ line: String) -> (BurnEvent?, bad: Bool) { parse(Data(line.utf8)) }

    private mutating func count(_ d: [String: Any], _ p: [String: Any]) -> BurnEvent? {
        guard let info = p["info"] as? [String: Any],
              let ts = (d["timestamp"] as? String).flatMap(VaultTime.parse) else { return nil }
        let total = info["total_token_usage"] as? [String: Any]
        let last = info["last_token_usage"] as? [String: Any]

        let u: Usage
        if let total {
            let t = Usage.of(total)
            if !state.hasTotal {
                u = t
            } else if t.total >= state.last.total {
                u = t.minus(state.last)
            } else {
                u = last.map(Usage.of) ?? t
            }
            state.last = t
            state.hasTotal = true
        } else if let last {
            u = Usage.of(last)
        } else {
            return nil
        }

        if u.input == 0 && u.output == 0 && u.cached == 0 && u.cacheWrite == 0 { return nil }
        return BurnEvent(timestamp: ts, input: max(0, u.input - u.cached), output: u.output,
                         cacheRead: u.cached, cacheCreation: u.cacheWrite, model: state.model, cwd: cwd)
    }
}
