import Foundation

/// The one reader for a JSON object file on disk: a regular file of at most `maxBytes`, parsed
/// as strict JSON. Claude Code's own `JSON.parse` refuses a comment and a trailing comma, and
/// Foundation's parser lets the comma through, so the bytes are walked once more after parsing
/// (`strictProblem`). A folder, a FIFO, a file over its cap or a top-level array reads as
/// unreadable with a reason, never as empty. Seat scans, config packs, pack imports and the mod
/// inventory all read through here; JSONC (VS Code's settings) has its own lenient reader in
/// `SeatScanner`.
enum JSONFiles {
    /// Size caps: a file over its cap reads as unreadable ("is larger than …"), never as empty.
    enum Cap {
        static let claudeJSON = 32 << 20
        static let manifest = 4 << 20
        static let settings = 2 << 20
        static let small = 1 << 20
    }

    /// What a bounded read of a file found.
    enum Bytes {
        case missing
        /// Not a regular file (a folder, a FIFO, a socket, a device), or larger than the cap.
        case refused(String)
        case data(Data)

        var data: Data? {
            if case .data(let d) = self { return d }
            return nil
        }
    }

    /// What reading a JSON object file found.
    enum Read {
        case missing
        case unreadable(String)
        case object([String: Any])

        var object: [String: Any]? {
            if case .object(let o) = self { return o }
            return nil
        }
    }

    /// A regular file's bytes, at most `maxBytes` of them. A FIFO, socket or device is never opened.
    static func bytes(at path: String, maxBytes: Int = Cap.small, _ fm: FileManager = .default) -> Bytes {
        guard let a = try? fm.attributesOfItem(atPath: path) else { return .missing }
        guard (a[.type] as? FileAttributeType) == .typeRegular else { return .refused("isn't a regular file") }
        let size = (a[.size] as? NSNumber)?.intValue ?? 0
        guard size <= maxBytes else { return .refused("is larger than \(max(1, maxBytes >> 20)) MB, so Sanduhr doesn't read it") }
        guard let h = FileHandle(forReadingAtPath: path) else { return .missing }
        defer { try? h.close() }
        return .data((try? h.read(upToCount: maxBytes)) ?? Data())
    }

    /// A JSON object file: missing, unreadable (with where it stops parsing, never the text) or
    /// read. A file whose top level isn't an object, that holds a comment or a trailing comma
    /// (Claude Code's strict JSON refuses both), that isn't a regular file or that is over
    /// `maxBytes` is unreadable too.
    static func read(at path: String, maxBytes: Int = Cap.small, _ fm: FileManager = .default) -> Read {
        let data: Data
        switch bytes(at: path, maxBytes: maxBytes, fm) {
        case .missing: return .missing
        case .refused(let why): return .unreadable(why)
        case .data(let d): data = d
        }
        do {
            let value = try JSONSerialization.jsonObject(with: data, options: [.fragmentsAllowed])
            guard let o = value as? [String: Any] else { return .unreadable("has no JSON object at its top level") }
            if let problem = strictProblem(data) { return .unreadable(problem) }
            return .object(o)
        } catch {
            return .unreadable(strictProblem(data) ?? parseError(error))
        }
    }

    /// The object in a JSON object file, or nil when the file is missing or unreadable
    /// (`read(at:)` says why). For callers that treat both the same.
    static func object(at path: String, maxBytes: Int = Cap.small, _ fm: FileManager = .default) -> [String: Any]? {
        read(at: path, maxBytes: maxBytes, fm).object
    }

    /// What Foundation's parser lets through and Claude Code's strict JSON doesn't: a comment or
    /// a trailing comma outside strings, with where it is ("isn't valid JSON (a trailing comma
    /// around line 1, column 15)"), columns counted in characters. Nil for strict JSON.
    static func strictProblem(_ data: Data) -> String? {
        data.withUnsafeBytes { (raw: UnsafeRawBufferPointer) -> String? in
            let b = raw.bindMemory(to: UInt8.self)
            var i = 0, line = 1, column = 1
            var inString = false
            var lastComma: (line: Int, column: Int)?
            func at(_ l: Int, _ c: Int) -> String { "around line \(l), column \(c)" }
            while i < b.count {
                let c = b[i]
                if inString {
                    if c == UInt8(ascii: "\\") {
                        i += 2
                        column += 2
                        continue
                    }
                    if c == UInt8(ascii: "\"") { inString = false }
                } else {
                    switch c {
                    case UInt8(ascii: "\""):
                        inString = true
                        lastComma = nil
                    case UInt8(ascii: "/") where i + 1 < b.count && (b[i + 1] == UInt8(ascii: "/") || b[i + 1] == UInt8(ascii: "*")):
                        return "isn't valid JSON (a comment \(at(line, column)))"
                    case UInt8(ascii: ","):
                        lastComma = (line, column)
                    case UInt8(ascii: "}"), UInt8(ascii: "]"):
                        if let comma = lastComma { return "isn't valid JSON (a trailing comma \(at(comma.line, comma.column)))" }
                    case UInt8(ascii: " "), UInt8(ascii: "\t"), UInt8(ascii: "\r"), UInt8(ascii: "\n"):
                        break
                    default:
                        lastComma = nil
                    }
                }
                if c == UInt8(ascii: "\n") {
                    line += 1
                    column = 1
                } else if c & 0xC0 != 0x80 {
                    // A UTF-8 continuation byte isn't a new character.
                    column += 1
                }
                i += 1
            }
            return nil
        }
    }

    /// "isn't valid JSON (around line 3, column 5)": the position only. Foundation's message can
    /// quote a character of the file, so nothing else of it is kept.
    static func parseError(_ error: Error) -> String {
        let text = ((error as NSError).userInfo["NSDebugDescription"] as? String) ?? ""
        if let r = text.range(of: #"line \d+, column \d+"#, options: .regularExpression) {
            return "isn't valid JSON (around \(text[r]))"
        }
        if let r = text.range(of: #"character \d+"#, options: .regularExpression) {
            return "isn't valid JSON (around \(text[r]))"
        }
        return "isn't valid JSON"
    }
}
