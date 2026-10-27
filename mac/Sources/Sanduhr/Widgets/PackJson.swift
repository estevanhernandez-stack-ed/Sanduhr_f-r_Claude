import Foundation

/// A JSON number as the widget pack format sees it. Python's `json` module, which the reference
/// validator (`sanduhr-widgets/tools/validate.py`) uses, keeps `8` and `8.0` apart: an integer
/// field rejects `8.0`. `repr` is the value as Python prints it, so messages match the reference
/// byte for byte; `raw` is the token as written (the theme lint quotes it back).
struct PackNumber: Equatable {
    let value: Double
    let isInt: Bool
    let repr: String
    let raw: String

    var isFinite: Bool { value.isFinite }
}

/// A JSON object with its keys in document order (messages and the trademark scan walk them in
/// order). Keys compare by their UTF-8 bytes, never by Swift's canonical equivalence.
final class PackObject {
    private(set) var keys: [String] = []
    private var map: [[UInt8]: PackValue] = [:]

    var count: Int { keys.count }

    func has(_ key: String) -> Bool { map[Array(key.utf8)] != nil }

    /// The value, or nil when the key is absent (`.null` is a present JSON null).
    subscript(_ key: String) -> PackValue? { map[Array(key.utf8)] }

    /// False when the key is already there.
    @discardableResult
    func add(_ key: String, _ value: PackValue) -> Bool {
        let k = Array(key.utf8)
        guard map[k] == nil else { return false }
        map[k] = value
        keys.append(key)
        return true
    }
}

/// A parsed JSON value. Booleans are not numbers here, as in the reference.
enum PackValue {
    case null
    case bool(Bool)
    case number(PackNumber)
    case string(String)
    case array([PackValue])
    case object(PackObject)

    var string: String? { if case .string(let s) = self { return s }; return nil }
    var array: [PackValue]? { if case .array(let a) = self { return a }; return nil }
    var object: PackObject? { if case .object(let o) = self { return o }; return nil }
    var number: PackNumber? { if case .number(let n) = self { return n }; return nil }
    var bool: Bool? { if case .bool(let b) = self { return b }; return nil }
    var isNull: Bool { if case .null = self { return true }; return false }
}

/// The strict JSON reader behind `PackValidator`, plus the Python-style formatting the validator's
/// messages use (a port of Windows' PackJson.cs). Pure: bytes in, a tree out, no IO. A duplicate
/// key is a parse error, as in the reference (FORMAT section 2); NaN and Infinity are not JSON.
enum PackJson {
    /// Nesting limit. A real pack nests four deep; Python's own limit sits near 1,000.
    static let maxDepth = 256

    // MARK: - Parsing

    /// Strict UTF-8 decode (no overlongs, surrogates or values above U+10FFFF), nil when invalid.
    static func decodeUTF8(_ bytes: [UInt8]) -> [Unicode.Scalar]? {
        var out: [Unicode.Scalar] = []
        out.reserveCapacity(bytes.count)
        var i = 0
        let n = bytes.count
        while i < n {
            let b = bytes[i]
            if b < 0x80 {
                out.append(Unicode.Scalar(b)); i += 1; continue
            }
            var need = 0
            var cp: UInt32 = 0
            var lo: UInt32 = 0
            switch b {
            case 0xC2...0xDF: need = 1; cp = UInt32(b & 0x1F); lo = 0x80
            case 0xE0...0xEF: need = 2; cp = UInt32(b & 0x0F); lo = 0x800
            case 0xF0...0xF4: need = 3; cp = UInt32(b & 0x07); lo = 0x10000
            default: return nil
            }
            guard i + need < n else { return nil }
            for k in 1...need {
                let c = bytes[i + k]
                guard c & 0xC0 == 0x80 else { return nil }
                cp = (cp << 6) | UInt32(c & 0x3F)
            }
            guard cp >= lo, let s = Unicode.Scalar(cp) else { return nil }   // nil for surrogates and > 10FFFF
            out.append(s)
            i += need + 1
        }
        return out
    }

    /// Parse JSON text (the caller rejects a byte-order mark first). Returns the value, or the
    /// reason for anything that is not exactly one JSON value.
    static func parse(_ text: [Unicode.Scalar]) -> Result<PackValue, ParseError> {
        var p = Parser(chars: text)
        do {
            p.skipSpace()
            let v = try p.value(depth: 0)
            p.skipSpace()
            if p.pos != p.chars.count { throw p.fail("Extra data", at: p.pos) }
            return .success(v)
        } catch let e as ParseError {
            return .failure(e)
        } catch {
            return .failure(ParseError(message: "\(error)"))
        }
    }

    struct ParseError: Error {
        let message: String
    }

    private struct Parser {
        let chars: [Unicode.Scalar]
        var pos = 0

        init(chars: [Unicode.Scalar]) { self.chars = chars }

        /// Python's message shape: `<what>: line L column C (char N)`.
        func fail(_ what: String, at: Int) -> ParseError {
            var line = 1
            var lastNewline = -1
            for i in 0..<min(at, chars.count) where chars[i] == "\n" {
                line += 1
                lastNewline = i
            }
            let col = at - lastNewline
            return ParseError(message: "\(what): line \(line) column \(col) (char \(at))")
        }

        mutating func skipSpace() {
            while pos < chars.count, chars[pos] == " " || chars[pos] == "\t" || chars[pos] == "\n" || chars[pos] == "\r" {
                pos += 1
            }
        }

        func peek() -> Unicode.Scalar? { pos < chars.count ? chars[pos] : nil }

        func matches(_ word: String) -> Bool {
            let w = Array(word.unicodeScalars)
            guard pos + w.count <= chars.count else { return false }
            return Array(chars[pos..<pos + w.count]) == w
        }

        mutating func value(depth: Int) throws -> PackValue {
            guard let c = peek() else { throw fail("Expecting value", at: pos) }
            switch c {
            case "{":
                guard depth < PackJson.maxDepth else { throw ParseError(message: "maximum nesting depth exceeded") }
                return try object(depth: depth + 1)
            case "[":
                guard depth < PackJson.maxDepth else { throw ParseError(message: "maximum nesting depth exceeded") }
                return try array(depth: depth + 1)
            case "\"":
                return .string(try string())
            case "t" where matches("true"):
                pos += 4; return .bool(true)
            case "f" where matches("false"):
                pos += 5; return .bool(false)
            case "n" where matches("null"):
                pos += 4; return .null
            default:
                if let n = number() { return .number(n) }
                throw fail("Expecting value", at: pos)
            }
        }

        mutating func object(depth: Int) throws -> PackValue {
            pos += 1   // {
            let obj = PackObject()
            skipSpace()
            if peek() == "}" { pos += 1; return .object(obj) }
            while true {
                guard peek() == "\"" else { throw fail("Expecting property name enclosed in double quotes", at: pos) }
                let key = try string()
                skipSpace()
                guard peek() == ":" else { throw fail("Expecting ':' delimiter", at: pos) }
                pos += 1
                skipSpace()
                let v = try value(depth: depth)
                if !obj.add(key, v) { throw ParseError(message: "duplicate key " + PackJson.pyStr(key)) }
                skipSpace()
                if peek() == "}" { pos += 1; return .object(obj) }
                guard peek() == "," else { throw fail("Expecting ',' delimiter", at: pos) }
                pos += 1
                skipSpace()
            }
        }

        mutating func array(depth: Int) throws -> PackValue {
            pos += 1   // [
            var items: [PackValue] = []
            skipSpace()
            if peek() == "]" { pos += 1; return .array(items) }
            while true {
                items.append(try value(depth: depth))
                skipSpace()
                if peek() == "]" { pos += 1; return .array(items) }
                guard peek() == "," else { throw fail("Expecting ',' delimiter", at: pos) }
                pos += 1
                skipSpace()
            }
        }

        mutating func string() throws -> String {
            let start = pos
            pos += 1   // opening quote
            var out = String.UnicodeScalarView()
            while true {
                guard let c = peek() else { throw fail("Unterminated string starting at", at: start) }
                if c == "\"" { pos += 1; return String(out) }
                if c.value < 0x20 { throw fail("Invalid control character at", at: pos) }
                if c != "\\" { out.append(c); pos += 1; continue }
                pos += 1
                guard let e = peek() else { throw fail("Unterminated string starting at", at: start) }
                switch e {
                case "\"": out.append("\""); pos += 1
                case "\\": out.append("\\"); pos += 1
                case "/": out.append("/"); pos += 1
                case "b": out.append("\u{08}"); pos += 1
                case "f": out.append("\u{0C}"); pos += 1
                case "n": out.append("\n"); pos += 1
                case "r": out.append("\r"); pos += 1
                case "t": out.append("\t"); pos += 1
                case "u":
                    let escStart = pos - 1
                    guard var cp = hex4(at: pos + 1) else { throw fail("Invalid \\uXXXX escape", at: escStart + 1) }
                    pos += 5
                    if (0xD800...0xDBFF).contains(cp), peek() == "\\", pos + 1 < chars.count, chars[pos + 1] == "u",
                       let lo = hex4(at: pos + 2), (0xDC00...0xDFFF).contains(lo) {
                        cp = 0x10000 + ((cp - 0xD800) << 10) + (lo - 0xDC00)
                        pos += 6
                    }
                    guard let s = Unicode.Scalar(cp) else {
                        // A lone surrogate: Python accepts it, no pack needs one.
                        throw fail("Unpaired surrogate escape at", at: escStart)
                    }
                    out.append(s)
                default:
                    throw fail("Invalid \\escape: " + PackJson.pyStr(String(e)), at: pos - 1)
                }
            }
        }

        func hex4(at: Int) -> UInt32? {
            guard at + 4 <= chars.count else { return nil }
            var v: UInt32 = 0
            for i in at..<at + 4 {
                guard let d = hexValue(chars[i]) else { return nil }
                v = v << 4 | d
            }
            return v
        }

        func hexValue(_ s: Unicode.Scalar) -> UInt32? {
            switch s.value {
            case 0x30...0x39: return s.value - 0x30
            case 0x61...0x66: return s.value - 0x61 + 10
            case 0x41...0x46: return s.value - 0x41 + 10
            default: return nil
            }
        }

        /// Python's number grammar: `-?(0|[1-9]\d*)(\.\d+)?([eE][-+]?\d+)?`, taking the longest match.
        mutating func number() -> PackNumber? {
            var i = pos
            func digit(_ k: Int) -> Bool { k < chars.count && ("0"..."9").contains(chars[k]) }
            if i < chars.count, chars[i] == "-" { i += 1 }
            guard digit(i) else { return nil }
            if chars[i] == "0" { i += 1 } else { while digit(i) { i += 1 } }
            var isInt = true
            if i + 1 < chars.count, chars[i] == ".", digit(i + 1) {
                isInt = false
                i += 1
                while digit(i) { i += 1 }
            }
            if i < chars.count, chars[i] == "e" || chars[i] == "E" {
                var j = i + 1
                if j < chars.count, chars[j] == "+" || chars[j] == "-" { j += 1 }
                if digit(j) {
                    isInt = false
                    while digit(j) { j += 1 }
                    i = j
                }
            }
            var raw = String.UnicodeScalarView()
            raw.append(contentsOf: chars[pos..<i])
            let text = String(raw)
            pos = i
            // Out-of-range text parses to an infinity, which no rule accepts (Python's float does the same).
            let v = Double(text) ?? .infinity
            let repr: String
            if isInt {
                let digits = text.drop(while: { $0 == "-" })
                repr = digits.allSatisfy({ $0 == "0" }) ? "0" : text   // "-0" is 0 in Python
            } else {
                repr = PackJson.pyFloat(v)
            }
            return PackNumber(value: v, isInt: isInt, repr: repr, raw: text)
        }
    }

    // MARK: - Python semantics the reference relies on

    /// Python's `isinstance(v, int)` (booleans are not numbers here).
    static func isInt(_ v: PackValue?) -> Bool {
        if case .number(let n)? = v { return n.isInt && n.isFinite }
        return false
    }

    /// The reference's `is_num`: an int or a finite float, never a boolean.
    static func isNum(_ v: PackValue?) -> Bool {
        if case .number(let n)? = v { return n.isFinite }
        return false
    }

    /// Python's `v == 1` for a JSON value (1 and 1.0 both count; true does not, by the reference's guard).
    static func isOne(_ v: PackValue?) -> Bool {
        if case .number(let n)? = v { return n.value == 1 }
        return false
    }

    /// Python's `len(str)`: code points.
    static func pyLen(_ s: String) -> Int { s.unicodeScalars.count }

    /// Python's `s[:count]`, by code points.
    static func pySlice(_ s: String, _ count: Int) -> String {
        var out = String.UnicodeScalarView()
        out.append(contentsOf: s.unicodeScalars.prefix(count))
        return String(out)
    }

    /// Python's `repr()` of a JSON value: `None`, `True`, `'text'`, `[1, 2]`, `{'k': 1}`.
    static func pyRepr(_ v: PackValue?) -> String {
        switch v {
        case nil, .null?: return "None"
        case .bool(let b)?: return b ? "True" : "False"
        case .number(let n)?: return n.repr
        case .string(let s)?: return pyStr(s)
        case .array(let a)?: return "[" + a.map { pyRepr($0) }.joined(separator: ", ") + "]"
        case .object(let o)?:
            return "{" + o.keys.map { pyStr($0) + ": " + pyRepr(o[$0]) }.joined(separator: ", ") + "}"
        }
    }

    /// Python's `repr()` of a list of strings, as printed for a sorted set of keys.
    static func pyList(_ items: [String]) -> String { "[" + items.map(pyStr).joined(separator: ", ") + "]" }

    /// Python's `repr()` of a string: single quotes unless the text has a single quote and no
    /// double one, with the usual escapes for backslashes, the quote and characters that do not print.
    static func pyStr(_ s: String) -> String {
        let scalars = s.unicodeScalars
        let q: Unicode.Scalar = scalars.contains("'") && !scalars.contains("\"") ? "\"" : "'"
        var out = String.UnicodeScalarView()
        func put(_ t: String) { out.append(contentsOf: t.unicodeScalars) }
        func hex(_ v: UInt32, _ width: Int) -> String {
            let h = String(v, radix: 16)
            return String(repeating: "0", count: max(0, width - h.count)) + h
        }
        out.append(q)
        for r in scalars {
            let c = r.value
            if r == q || r == "\\" { out.append("\\"); out.append(r) }
            else if c == 0x09 { put("\\t") }
            else if c == 0x0A { put("\\n") }
            else if c == 0x0D { put("\\r") }
            else if c < 0x20 || c == 0x7F { put("\\x" + hex(c, 2)) }
            else if !printable(r) {
                if c <= 0xFF { put("\\x" + hex(c, 2)) }
                else if c <= 0xFFFF { put("\\u" + hex(c, 4)) }
                else { put("\\U" + hex(c, 8)) }
            } else { out.append(r) }
        }
        out.append(q)
        return String(out)
    }

    private static func printable(_ r: Unicode.Scalar) -> Bool {
        if r == " " { return true }
        switch r.properties.generalCategory {
        case .control, .format, .surrogate, .privateUse, .unassigned, .lineSeparator, .paragraphSeparator, .spaceSeparator:
            return false
        default:
            return true
        }
    }

    /// Python's `repr(float)`: the shortest round-trip digits, fixed between 1e-4 and 1e16, else
    /// scientific with a two-digit exponent (`1.0`, `0.35`, `1e+16`, `1.5e-05`).
    static func pyFloat(_ d: Double) -> String {
        if d.isNaN { return "nan" }
        if d.isInfinite { return d < 0 ? "-inf" : "inf" }
        if d == 0 { return d.sign == .minus ? "-0.0" : "0.0" }
        // Swift's description is the shortest round-trip too; read its digits and exponent back out.
        var r = d.magnitude.description
        let neg = d < 0
        var exp = 0
        if let e = r.firstIndex(where: { $0 == "e" || $0 == "E" }) {
            exp = Int(r[r.index(after: e)...]) ?? 0
            r = String(r[..<e])
        }
        var digits: String
        var pointPos: Int
        if let point = r.firstIndex(of: ".") {
            pointPos = r.distance(from: r.startIndex, to: point)
            digits = String(r[..<point]) + String(r[r.index(after: point)...])
        } else {
            digits = r
            pointPos = r.count
        }
        while digits.count > 1, digits.first == "0" { digits.removeFirst(); pointPos -= 1 }
        while digits.last == "0" { digits.removeLast() }
        if digits.isEmpty { digits = "0" }
        let dexp = pointPos + exp - 1   // d.ddd x 10^dexp
        var body: String
        if dexp >= -4 && dexp < 16 {
            if dexp >= 0 {
                let whole = digits.count > dexp + 1
                    ? String(digits.prefix(dexp + 1))
                    : digits + String(repeating: "0", count: dexp + 1 - digits.count)
                let frac = digits.count > dexp + 1 ? String(digits.dropFirst(dexp + 1)) : ""
                body = whole + "." + (frac.isEmpty ? "0" : frac)
            } else {
                body = "0." + String(repeating: "0", count: -dexp - 1) + digits
            }
        } else {
            let e = abs(dexp) < 10 ? "0\(abs(dexp))" : "\(abs(dexp))"
            body = String(digits.prefix(1)) + (digits.count > 1 ? "." + digits.dropFirst() : "")
                + "e" + (dexp < 0 ? "-" : "+") + e
        }
        return neg ? "-" + body : body
    }

    /// Python's `"%g" % x` (six significant digits, trailing zeros dropped): C's %g.
    static func pyG(_ x: Double) -> String { String(format: "%g", x) }

    /// Python's `"%.Nf" % x`.
    static func pyF(_ x: Double, _ decimals: Int) -> String { String(format: "%.\(decimals)f", x) }
}
