import Foundation

/// Minimal edits to someone else's JSON file (item 49): set or remove one member of an object
/// and leave every other byte where it was. Claude Code's `.claude.json` and `settings.json` are
/// its files, not Sanduhr's; re-serializing them would reorder keys and reformat the whole file,
/// so the installers splice the text instead.
///
/// The text is checked with `JSONSerialization` before it is scanned, so the scanner only ever
/// walks valid JSON. Offsets are UTF-8 byte offsets. An insert appends the member last in the
/// object, copying the indentation and the `": "` spacing of the member before it (or two spaces
/// more than the object's own line when it was empty), so removing that member again gives back
/// the original bytes.
enum JSONEdit {
    /// A value Sanduhr writes: strings, booleans, integers, arrays and objects with their keys
    /// in order.
    indirect enum Value: Equatable {
        case string(String)
        case bool(Bool)
        case int(Int)
        case array([Value])
        case object([Pair])

        /// The plain value `JSONSerialization` reads back, for comparisons.
        var plain: Any {
            switch self {
            case .string(let s): return s
            case .bool(let b): return b
            case .int(let n): return n
            case .array(let a): return a.map(\.plain)
            case .object(let pairs):
                var d: [String: Any] = [:]
                for p in pairs { d[p.key] = p.value.plain }
                return d
            }
        }
    }

    struct Pair: Equatable {
        let key: String
        let value: Value
        init(_ key: String, _ value: Value) {
            self.key = key
            self.value = value
        }
    }

    enum Failure: Error, Equatable {
        /// Not JSON, or not an object at the top.
        case malformed
        /// The member to edit inside holds something other than an object.
        case notAnObject
    }

    /// One `"key": value` member of an object.
    struct Member {
        let key: String
        /// The opening quote of the key.
        let keyStart: Int
        /// The byte after the key's closing quote.
        let keyEnd: Int
        let valueStart: Int
        let valueEnd: Int
    }

    /// An object: its braces and members in file order.
    struct Object {
        let open: Int
        let close: Int
        let members: [Member]

        /// The last member with `key` (the one a JSON reader keeps).
        func member(_ key: String) -> Member? { members.last { $0.key == key } }
        func index(_ key: String) -> Int? { members.lastIndex { $0.key == key } }
    }

    /// One element of an array: its value's bytes.
    struct Element {
        let start: Int
        let end: Int
    }

    /// An array: its brackets and elements in file order (item 51's hook lists).
    struct ArrayValue {
        let open: Int
        let close: Int
        let elements: [Element]
    }

    // MARK: Checking and reading

    /// The top-level object, parsed, or `.malformed`. Strict JSON, as Claude Code's own
    /// `JSON.parse` reads it: `JSONSerialization` lets a trailing comma through, so the scanner
    /// walks the whole text too, and nothing but whitespace may follow the object.
    static func root(_ data: Data) throws -> [String: Any] {
        guard let obj = try? JSONSerialization.jsonObject(with: data), let dict = obj as? [String: Any] else {
            throw Failure.malformed
        }
        var s = Scanner(b: Array(data), i: 0)
        s.skipSpace()
        guard s.peek == UInt8(ascii: "{") else { throw Failure.malformed }
        _ = try s.object()
        s.skipSpace()
        guard s.peek == nil else { throw Failure.malformed }
        return dict
    }

    /// The object whose `{` is the first non-space byte at or after `start`.
    static func object(_ b: [UInt8], at start: Int) throws -> Object {
        var s = Scanner(b: b, i: start)
        s.skipSpace()
        guard s.peek == UInt8(ascii: "{") else { throw Failure.notAnObject }
        return try s.object()
    }

    /// The array whose `[` is the first non-space byte at or after `start`.
    static func array(_ b: [UInt8], at start: Int) throws -> ArrayValue {
        var s = Scanner(b: b, i: start)
        s.skipSpace()
        guard s.peek == UInt8(ascii: "[") else { throw Failure.notAnObject }
        return try s.array()
    }

    /// One element's value, parsed (nil when it doesn't parse on its own, which valid JSON does).
    static func parsed(_ b: [UInt8], _ e: Element) -> Any? {
        try? JSONSerialization.jsonObject(with: Data(b[e.start..<e.end]), options: [.fragmentsAllowed])
    }

    /// The top-level object of a valid file.
    static func topObject(_ b: [UInt8]) throws -> Object { try object(b, at: 0) }

    /// The bytes of a member's value.
    static func text(_ b: [UInt8], _ m: Member) -> [UInt8] { Array(b[m.valueStart..<m.valueEnd]) }

    // MARK: Editing

    /// What `set` did, for undoing it.
    struct SetResult {
        let bytes: [UInt8]
        /// The value that was replaced, byte for byte; nil when the member was added.
        let previous: [UInt8]?
        /// What sat between the braces when the object was empty and the member became its only
        /// one; nil otherwise.
        let emptyInner: [UInt8]?
    }

    /// Sets `key` to `value` in the object `obj` of `b`: replaces the value of an existing member
    /// in place, or appends a new member last.
    static func set(_ b: [UInt8], in obj: Object, key: String, value: Value) -> SetResult {
        if let m = obj.member(key) {
            let indent = lineIndent(b, at: m.keyStart)
            let rendered = render(value, indent: indent, unit: indentUnit(b), compact: isCompact(b, obj))
            var out = Array(b[..<m.valueStart])
            out += rendered
            out += b[m.valueEnd...]
            return SetResult(bytes: out, previous: Array(b[m.valueStart..<m.valueEnd]), emptyInner: nil)
        }
        let unit = indentUnit(b)
        if let last = obj.members.last {
            let compact = isCompact(b, obj)
            let colon = Array(b[last.keyEnd..<last.valueStart])
            var insert: [UInt8] = Array(",".utf8)
            if compact {
                insert += quoted(key) + colon + render(value, indent: [], unit: unit, compact: true)
            } else {
                let indent = lineIndent(b, at: last.keyStart)
                insert += [UInt8(ascii: "\n")] + indent + quoted(key) + colon
                insert += render(value, indent: indent, unit: unit, compact: false)
            }
            var out = Array(b[..<last.valueEnd])
            out += insert
            out += b[last.valueEnd...]
            return SetResult(bytes: out, previous: nil, emptyInner: nil)
        }
        // Empty object: one member on its own line, the closing brace back at the object's indent.
        let outer = lineIndent(b, at: obj.open)
        let indent = outer + unit
        var inner: [UInt8] = [UInt8(ascii: "\n")] + indent + quoted(key) + Array(": ".utf8)
        inner += render(value, indent: indent, unit: unit, compact: false)
        inner += [UInt8(ascii: "\n")] + outer
        var out = Array(b[...obj.open])
        out += inner
        out += b[obj.close...]
        return SetResult(bytes: out, previous: nil, emptyInner: Array(b[(obj.open + 1)..<obj.close]))
    }

    /// Puts `previous` back as the value of `key` (undoing a replacement).
    static func restore(_ b: [UInt8], in obj: Object, key: String, previous: [UInt8]) -> [UInt8] {
        guard let m = obj.member(key) else { return b }
        var out = Array(b[..<m.valueStart])
        out += previous
        out += b[m.valueEnd...]
        return out
    }

    /// Removes the member `key` and its separator. The only member leaves the object holding
    /// `emptyInner` (what was between the braces before it was added), or nothing.
    static func remove(_ b: [UInt8], in obj: Object, key: String, emptyInner: [UInt8]? = nil) -> [UInt8] {
        guard let i = obj.index(key) else { return b }
        let ms = obj.members
        let m = ms[i]
        let range: Range<Int>
        var replacement: [UInt8] = []
        if ms.count == 1 {
            range = (obj.open + 1)..<obj.close
            replacement = emptyInner ?? []
        } else if i == ms.count - 1 {
            range = ms[i - 1].valueEnd..<m.valueEnd
        } else {
            range = m.keyStart..<ms[i + 1].keyStart
        }
        var out = Array(b[..<range.lowerBound])
        out += replacement
        out += b[range.upperBound...]
        return out
    }

    // MARK: Editing arrays

    /// Appends `value` as the last element of `arr`: on its own line at the last element's
    /// indent (or the array's own line plus one step when it was empty), or inline in an array
    /// written on one line. `emptyInner` is what sat between the brackets of an empty array.
    static func append(_ b: [UInt8], in arr: ArrayValue, value: Value) -> SetResult {
        let unit = indentUnit(b)
        if let last = arr.elements.last {
            let compact = !b[arr.open...arr.close].contains(UInt8(ascii: "\n"))
            var insert: [UInt8]
            if arr.elements.count >= 2 {
                // The separator the file already uses between elements.
                let prev = arr.elements[arr.elements.count - 2]
                insert = Array(b[prev.end..<last.start])
            } else {
                insert = compact ? Array(",".utf8) : Array(",\n".utf8) + lineIndent(b, at: last.start)
            }
            let indent = compact ? [] : lineIndent(b, at: last.start)
            insert += render(value, indent: indent, unit: unit, compact: compact)
            var out = Array(b[..<last.end])
            out += insert
            out += b[last.end...]
            return SetResult(bytes: out, previous: nil, emptyInner: nil)
        }
        let outer = lineIndent(b, at: arr.open)
        let indent = outer + unit
        var inner: [UInt8] = [UInt8(ascii: "\n")] + indent
        inner += render(value, indent: indent, unit: unit, compact: false)
        inner += [UInt8(ascii: "\n")] + outer
        var out = Array(b[...arr.open])
        out += inner
        out += b[arr.close...]
        return SetResult(bytes: out, previous: nil, emptyInner: Array(b[(arr.open + 1)..<arr.close]))
    }

    /// Replaces element `index` of `arr` with `value`, rendered at that element's indent.
    static func replaceElement(_ b: [UInt8], in arr: ArrayValue, index: Int, value: Value) -> [UInt8] {
        let e = arr.elements[index]
        let compact = !b[arr.open...arr.close].contains(UInt8(ascii: "\n"))
        var out = Array(b[..<e.start])
        out += render(value, indent: compact ? [] : lineIndent(b, at: e.start), unit: indentUnit(b), compact: compact)
        out += b[e.end...]
        return out
    }

    /// Removes element `index` and its separator. The only element leaves the array holding
    /// `emptyInner` (what was between the brackets before it was added), or nothing.
    static func removeElement(_ b: [UInt8], in arr: ArrayValue, index: Int, emptyInner: [UInt8]? = nil) -> [UInt8] {
        let es = arr.elements
        guard es.indices.contains(index) else { return b }
        let range: Range<Int>
        var replacement: [UInt8] = []
        if es.count == 1 {
            range = (arr.open + 1)..<arr.close
            replacement = emptyInner ?? []
        } else if index == es.count - 1 {
            range = es[index - 1].end..<es[index].end
        } else {
            range = es[index].start..<es[index + 1].start
        }
        var out = Array(b[..<range.lowerBound])
        out += replacement
        out += b[range.upperBound...]
        return out
    }

    // MARK: Rendering

    /// The JSON string literal for `s`, slashes left alone.
    static func quoted(_ s: String) -> [UInt8] {
        let data = (try? JSONSerialization.data(withJSONObject: [s], options: [.withoutEscapingSlashes])) ?? Data("[\"\"]".utf8)
        let bytes = Array(data)
        return Array(bytes[1..<(bytes.count - 1)])
    }

    /// `value` as text. Multi-line values put each member on its own line, one `unit` deeper
    /// than `indent` (the line the value starts on), the closing bracket back at `indent`.
    static func render(_ value: Value, indent: [UInt8], unit: [UInt8], compact: Bool) -> [UInt8] {
        switch value {
        case .string(let s):
            return quoted(s)
        case .bool(let b):
            return Array((b ? "true" : "false").utf8)
        case .int(let n):
            return Array(String(n).utf8)
        case .array(let items):
            if items.isEmpty { return Array("[]".utf8) }
            let parts = items.map { render($0, indent: indent + unit, unit: unit, compact: compact) }
            return wrap(parts, open: "[", close: "]", indent: indent, unit: unit, compact: compact)
        case .object(let pairs):
            if pairs.isEmpty { return Array("{}".utf8) }
            let colon = Array((compact ? ":" : ": ").utf8)
            let parts = pairs.map { quoted($0.key) + colon + render($0.value, indent: indent + unit, unit: unit, compact: compact) }
            return wrap(parts, open: "{", close: "}", indent: indent, unit: unit, compact: compact)
        }
    }

    private static func wrap(_ parts: [[UInt8]], open: String, close: String,
                             indent: [UInt8], unit: [UInt8], compact: Bool) -> [UInt8] {
        var out = Array(open.utf8)
        for (n, p) in parts.enumerated() {
            if n > 0 { out.append(UInt8(ascii: ",")) }
            if !compact { out += [UInt8(ascii: "\n")] + indent + unit }
            out += p
        }
        if !compact { out += [UInt8(ascii: "\n")] + indent }
        out += Array(close.utf8)
        return out
    }

    /// The spaces and tabs that start the line holding offset `i`.
    static func lineIndent(_ b: [UInt8], at i: Int) -> [UInt8] {
        var start = i
        while start > 0, b[start - 1] != UInt8(ascii: "\n") { start -= 1 }
        var end = start
        while end < b.count, b[end] == UInt8(ascii: " ") || b[end] == UInt8(ascii: "\t") { end += 1 }
        return Array(b[start..<end])
    }

    /// The file's indent step: the top object's first member's indent when it starts a line,
    /// else two spaces (what Claude Code writes).
    static func indentUnit(_ b: [UInt8]) -> [UInt8] {
        if let top = try? topObject(b), let first = top.members.first,
           b[(top.open + 1)..<first.keyStart].contains(UInt8(ascii: "\n")) {
            let indent = lineIndent(b, at: first.keyStart)
            if !indent.isEmpty { return indent }
        }
        return Array("  ".utf8)
    }

    /// An object written on one line with members: new members go in the same compact style.
    static func isCompact(_ b: [UInt8], _ obj: Object) -> Bool {
        guard !obj.members.isEmpty else { return false }
        return !b[obj.open...obj.close].contains(UInt8(ascii: "\n"))
    }

    // MARK: Scanner

    /// Walks valid JSON (checked beforehand), recording object members and value spans.
    private struct Scanner {
        let b: [UInt8]
        var i: Int

        var peek: UInt8? { i < b.count ? b[i] : nil }

        mutating func skipSpace() {
            while i < b.count, b[i] == 0x20 || b[i] == 0x0A || b[i] == 0x0D || b[i] == 0x09 { i += 1 }
        }

        mutating func object() throws -> Object {
            let open = i
            i += 1
            var members: [Member] = []
            skipSpace()
            if peek == UInt8(ascii: "}") {
                i += 1
                return Object(open: open, close: i - 1, members: [])
            }
            while true {
                skipSpace()
                let keyStart = i
                try string()
                let keyEnd = i
                let key = Self.decode(Array(b[keyStart..<keyEnd]))
                skipSpace()
                guard peek == UInt8(ascii: ":") else { throw Failure.malformed }
                i += 1
                skipSpace()
                let valueStart = i
                try value()
                members.append(Member(key: key, keyStart: keyStart, keyEnd: keyEnd,
                                      valueStart: valueStart, valueEnd: i))
                skipSpace()
                guard let c = peek else { throw Failure.malformed }
                i += 1
                if c == UInt8(ascii: "}") { return Object(open: open, close: i - 1, members: members) }
                guard c == UInt8(ascii: ",") else { throw Failure.malformed }
            }
        }

        mutating func value() throws {
            guard let c = peek else { throw Failure.malformed }
            switch c {
            case UInt8(ascii: "{"):
                _ = try object()
            case UInt8(ascii: "["):
                _ = try array()
            case UInt8(ascii: "\""):
                try string()
            default:
                // A number, true, false or null: up to the next delimiter.
                let start = i
                while i < b.count {
                    let d = b[i]
                    if d == UInt8(ascii: ",") || d == UInt8(ascii: "}") || d == UInt8(ascii: "]")
                        || d == 0x20 || d == 0x0A || d == 0x0D || d == 0x09 { break }
                    i += 1
                }
                if i == start { throw Failure.malformed }
            }
        }

        mutating func array() throws -> ArrayValue {
            let open = i
            i += 1
            var elements: [Element] = []
            skipSpace()
            if peek == UInt8(ascii: "]") {
                i += 1
                return ArrayValue(open: open, close: i - 1, elements: [])
            }
            while true {
                skipSpace()
                let start = i
                try value()
                elements.append(Element(start: start, end: i))
                skipSpace()
                guard let d = peek else { throw Failure.malformed }
                i += 1
                if d == UInt8(ascii: "]") { return ArrayValue(open: open, close: i - 1, elements: elements) }
                guard d == UInt8(ascii: ",") else { throw Failure.malformed }
            }
        }

        mutating func string() throws {
            guard peek == UInt8(ascii: "\"") else { throw Failure.malformed }
            i += 1
            while i < b.count {
                let c = b[i]
                if c == UInt8(ascii: "\\") { i += 2; continue }
                i += 1
                if c == UInt8(ascii: "\"") { return }
            }
            throw Failure.malformed
        }

        /// A key's text with its escapes resolved.
        static func decode(_ quoted: [UInt8]) -> String {
            let wrapped = Data([UInt8(ascii: "[")] + quoted + [UInt8(ascii: "]")])
            if let a = try? JSONSerialization.jsonObject(with: wrapped) as? [String], let s = a.first { return s }
            return String(decoding: quoted.dropFirst().dropLast(), as: UTF8.self)
        }
    }
}
