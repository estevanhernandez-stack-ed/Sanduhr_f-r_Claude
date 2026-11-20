import Foundation

/// One token reading from an agent's log (every-agents spec, section 3; Windows `BurnEvent`).
/// `input` is fresh input only, with the cache apart, so `input + output` is the total every Vault
/// row sums.
struct BurnEvent: Equatable, Sendable {
    var timestamp: Date
    var input: Int64
    var output: Int64
    var cacheRead: Int64 = 0
    var cacheCreation: Int64 = 0
    /// As the agent reports it.
    var model: String?
    /// The project in force at the event, when the log names one.
    var cwd: String?
}

/// One log file an agent source hands the Vault, with its size and modification time taken when
/// it was listed (before any open), as a Claude Code walk does. `sessionID` keys the Vault row:
/// two files with the same id write the same row, the later one replacing it. `cwd` is the
/// project when the source knows it up front (Antigravity); nil lets the events name it (Codex).
/// A compressed file is always read whole, never resumed mid-file.
struct AgentBurnFile: Equatable, Sendable {
    var path: String
    var sessionID: String
    var cwd: String?
    var compressed: Bool
    var length: Int64
    var mtimeTicks: Int64
}

/// What one read of a file produced: its events in order, the lines that looked like events and
/// failed to parse, the offset after the last whole line consumed (a torn last line is left for
/// the next read), and the parser's state at that offset (nil for a stateless reader).
struct AgentBurnRead: Equatable, Sendable {
    var events: [BurnEvent] = []
    var skippedLines: Int64 = 0
    var endOffset: Int64 = 0
    var state: String?
}

/// An agent whose local burn the Vault holds (Windows `IAgentBurnSource`). Its root in the Vault
/// is `vault/<agent.rawValue>`.
protocol AgentBurnSource {
    var agent: AgentSource { get }
    /// The files to read now. May throw; the ingester then skips this agent's root for the cycle.
    func files() throws -> [AgentBurnFile]
    /// Reads `file` from `offset` with the parser resumed from `state` (nil and 0 for a fresh read;
    /// a compressed file is only ever read from 0). Throws when the file can't be opened, is not
    /// valid, or would decompress past the cap; that file then fails alone.
    func read(_ file: AgentBurnFile, from offset: Int64, state: String?) throws -> AgentBurnRead
}

/// Splits bytes fed in pieces into whole lines, each trimmed of surrounding white space and
/// skipped when empty, and tracks the offset after the last newline consumed. Bytes after the last
/// newline wait for more input and are never handed out on their own.
struct BurnLineSplitter {
    private var pending = Data()
    private var position: Int64
    /// The offset after the last newline consumed.
    private(set) var consumed: Int64

    init(start: Int64) {
        position = start
        consumed = start
    }

    mutating func feed(_ chunk: UnsafeRawBufferPointer, _ line: (Data) -> Void) {
        guard let base = chunk.baseAddress else { return }
        let n = chunk.count
        var lineStart = 0
        while lineStart < n, let hit = memchr(base + lineStart, 0x0A, n - lineStart) {
            let i = base.distance(to: UnsafeRawPointer(hit))
            pending.append(base.advanced(by: lineStart).assumingMemoryBound(to: UInt8.self), count: i - lineStart)
            if let trimmed = Self.trimmed(pending) { line(trimmed) }
            pending.removeAll(keepingCapacity: true)
            consumed = position + Int64(i) + 1
            lineStart = i + 1
        }
        if lineStart < n {
            pending.append(base.advanced(by: lineStart).assumingMemoryBound(to: UInt8.self), count: n - lineStart)
        }
        position += Int64(n)
    }

    mutating func feed(_ data: Data, _ line: (Data) -> Void) {
        data.withUnsafeBytes { feed($0, line) }
    }

    private static func isSpace(_ b: UInt8) -> Bool {
        b == 0x20 || b == 0x09 || b == 0x0A || b == 0x0D || b == 0x0B || b == 0x0C
    }

    private static func trimmed(_ d: Data) -> Data? {
        guard let lo = d.firstIndex(where: { !isSpace($0) }), let hi = d.lastIndex(where: { !isSpace($0) }) else { return nil }
        return d[lo...hi]
    }
}

/// Reading an agent's file line by line: a plain file from an offset in 1 MB pieces, a
/// compressed one decoded whole through `Zstd` under `cap`, with memory at one piece either way.
enum AgentBurnFileReader {
    static let chunkSize = 1 << 20

    /// Calls `line` for every whole line from `offset`; returns the offset after the last newline.
    static func lines(plain path: String, from offset: Int64, _ line: (Data) -> Void) throws -> Int64 {
        guard let h = FileHandle(forReadingAtPath: path) else { throw CocoaError(.fileReadNoPermission) }
        defer { try? h.close() }
        try h.seek(toOffset: UInt64(offset))
        var splitter = BurnLineSplitter(start: offset)
        while let chunk = try h.read(upToCount: chunkSize), !chunk.isEmpty {
            splitter.feed(chunk, line)
        }
        return splitter.consumed
    }

    /// Calls `line` for every whole line of the decompressed file; returns the decompressed offset
    /// after the last newline.
    static func lines(zstd path: String, cap: Int, _ line: (Data) -> Void) throws -> Int64 {
        let packed = try Data(contentsOf: URL(fileURLWithPath: path), options: .mappedIfSafe)
        var splitter = BurnLineSplitter(start: 0)
        try Zstd.decompress(packed, cap: cap) { splitter.feed($0, line) }
        return splitter.consumed
    }
}
