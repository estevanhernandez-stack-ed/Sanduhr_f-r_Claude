import Foundation
import CoreGraphics

/// A YAML value for the debug hooks' tree.yaml and state.yaml. Maps keep their order, so the
/// files read the same way every time. Development tooling only.
indirect enum YAMLNode: Equatable {
    case string(String)
    case int(Int)
    case double(Double)
    case bool(Bool)
    case null
    case list([YAMLNode])
    case map([YAMLPair])

    /// A map from pairs, dropping the ones whose value is nil (an omitted field).
    static func object(_ pairs: [(String, YAMLNode?)]) -> YAMLNode {
        .map(pairs.compactMap { key, value in value.map { YAMLPair(key, $0) } })
    }

    /// `.string`, or nil for nil or an empty string: fields with nothing in them are left out.
    static func text(_ s: String?) -> YAMLNode? {
        guard let s, !s.isEmpty else { return nil }
        return .string(s)
    }

    /// `[x, y, w, h]`.
    static func rect(_ r: CGRect) -> YAMLNode {
        .list([.double(r.minX), .double(r.minY), .double(r.width), .double(r.height)])
    }
}

struct YAMLPair: Equatable {
    let key: String
    let value: YAMLNode
    init(_ key: String, _ value: YAMLNode) {
        self.key = key
        self.value = value
    }
}

/// A small block-style YAML writer, enough for nested maps and lists of scalars. Strings that a
/// YAML 1.1 reader (Ruby's Psych) could take for something else are double-quoted.
enum YAMLEmitter {
    /// The whole document, ending in a newline.
    static func emit(_ node: YAMLNode) -> String {
        lines(node, indent: 0).joined(separator: "\n") + "\n"
    }

    private static func lines(_ node: YAMLNode, indent: Int) -> [String] {
        let pad = String(repeating: " ", count: indent)
        if let inline = inline(node) { return [pad + inline] }
        switch node {
        case .map(let pairs):
            return pairs.flatMap { pair -> [String] in
                let key = pad + scalar(pair.key) + ":"
                if let inline = inline(pair.value) { return [key + " " + inline] }
                return [key] + lines(pair.value, indent: indent + 2)
            }
        case .list(let items):
            return items.flatMap { item -> [String] in
                if let inline = inline(item) { return [pad + "- " + inline] }
                if case .map = item {
                    // The first pair rides on the dash line; the rest line up under it.
                    var nested = lines(item, indent: indent + 2)
                    nested[0] = pad + "- " + nested[0].dropFirst(indent + 2)
                    return nested
                }
                return [pad + "-"] + lines(item, indent: indent + 2)
            }
        default:
            return [pad]   // unreachable: every scalar is inline
        }
    }

    /// The one-line form of scalars, empty collections and lists of numbers and bools.
    private static func inline(_ node: YAMLNode) -> String? {
        switch node {
        case .string(let s): return scalar(s)
        case .int(let i): return String(i)
        case .double(let d): return number(d)
        case .bool(let b): return b ? "true" : "false"
        case .null: return "null"
        case .list(let items):
            if items.isEmpty { return "[]" }
            let flat = items.allSatisfy {
                switch $0 {
                case .int, .double, .bool, .null: return true
                default: return false
                }
            }
            return flat ? "[" + items.compactMap(inline).joined(separator: ", ") + "]" : nil
        case .map(let pairs):
            return pairs.isEmpty ? "{}" : nil
        }
    }

    /// Whole numbers without a decimal point, others to four places, trailing zeros trimmed.
    static func number(_ d: Double) -> String {
        if d.isNaN { return ".nan" }
        if d.isInfinite { return d > 0 ? ".inf" : "-.inf" }
        if d == d.rounded(), abs(d) < 1e15 { return String(Int(d)) }
        var s = String(format: "%.4f", d)
        while s.hasSuffix("0") { s.removeLast() }
        if s.hasSuffix(".") { s.removeLast() }
        return s == "-0" ? "0" : s
    }

    /// A string as a plain scalar when that reads back as the same string, else double-quoted.
    static func scalar(_ s: String) -> String {
        needsQuotes(s) ? quoted(s) : s
    }

    /// Words YAML 1.1 reads as booleans or null.
    private static let reserved: Set<String> = [
        "true", "false", "yes", "no", "on", "off", "y", "n", "null", "~",
        ".inf", "-.inf", "+.inf", ".nan", "<<",
    ]

    static func needsQuotes(_ s: String) -> Bool {
        guard let first = s.unicodeScalars.first, let last = s.unicodeScalars.last else { return true }
        if reserved.contains(s.lowercased()) { return true }
        if first == " " || last == " " { return true }
        // Indicators that start something else: sequences, maps, anchors, tags, quotes, comments.
        if "-?:,[]{}#&*!|>'\"%@`".unicodeScalars.contains(first) { return true }
        // Anything that could be read as a number, date or time.
        let scalars = Array(s.unicodeScalars)
        if CharacterSet.decimalDigits.contains(first) { return true }
        if "+.".unicodeScalars.contains(first), scalars.count > 1,
           CharacterSet.decimalDigits.contains(scalars[1]) { return true }
        // Characters with a meaning inside a line: key separators, comments, flow punctuation.
        if s.unicodeScalars.contains(where: { ":#,[]{}".unicodeScalars.contains($0) }) { return true }
        // Line breaks, tabs and other control characters only survive quoted.
        if s.unicodeScalars.contains(where: { $0.value < 0x20 || $0.value == 0x7F
            || $0.value == 0x85 || $0.value == 0x2028 || $0.value == 0x2029 }) { return true }
        return false
    }

    static func quoted(_ s: String) -> String {
        var out = "\""
        for u in s.unicodeScalars {
            switch u {
            case "\\": out += "\\\\"
            case "\"": out += "\\\""
            case "\n": out += "\\n"
            case "\r": out += "\\r"
            case "\t": out += "\\t"
            default:
                if u.value < 0x20 || u.value == 0x7F || u.value == 0x85 || u.value == 0x2028 || u.value == 0x2029 {
                    out += String(format: "\\u%04X", u.value)
                } else {
                    out.unicodeScalars.append(u)
                }
            }
        }
        return out + "\""
    }
}
