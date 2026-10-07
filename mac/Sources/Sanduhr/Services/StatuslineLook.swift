import Foundation

/// What one of the user's statusline segments shows (item 63b), as the runner's classifier names
/// it from the segment's text.
enum SegmentKind: String, CaseIterable, Sendable {
    case branch, cost, model, context, session, weekly, tokens, time, directory, segment

    var title: String {
        switch self {
        case .branch: "Branch"
        case .cost: "Cost"
        case .model: "Model"
        case .context: "Context"
        case .session: "Session"
        case .weekly: "Weekly"
        case .tokens: "Tokens"
        case .time: "Time"
        case .directory: "Directory"
        case .segment: "Your segment"
        }
    }

    /// The segment of Sanduhr's that shows the same thing, if any.
    var sanduhr: SanduhrSegment? {
        switch self {
        case .model: .model
        case .context: .context
        case .session: .session
        case .weekly: .weekly
        default: nil
        }
    }

    /// Kinds a mod likely shows, from words in its name and description.
    static func fromModWords(_ text: String) -> Set<SegmentKind> {
        let t = text.lowercased()
        var out: Set<SegmentKind> = []
        func has(_ words: [String]) -> Bool { words.contains { t.contains($0) } }
        if has(["git", "branch"]) { out.insert(.branch) }
        if has(["cost", "spend", "$"]) { out.insert(.cost) }
        if has(["model"]) { out.insert(.model) }
        if has(["context", "ctx"]) { out.insert(.context) }
        if has(["usage", "limit", "quota", "meter", "rate", "token"]) { out.formUnion([.session, .weekly]) }
        return out
    }
}

/// A Unicode letter style for a segment (item 63b), by the runner's name: the Mathematical
/// Alphanumeric styles and small caps.
enum LetterStyle: String, CaseIterable, Sendable {
    case bold
    case italic
    case boldItalic = "bold-italic"
    case sans, mono
    case doubleStruck = "double-struck"
    case script, fraktur
    case smallCaps = "small-caps"

    var title: String {
        switch self {
        case .bold: "Bold"
        case .italic: "Italic"
        case .boldItalic: "Bold italic"
        case .sans: "Sans"
        case .mono: "Monospace"
        case .doubleStruck: "Double-struck"
        case .script: "Script"
        case .fraktur: "Fraktur"
        case .smallCaps: "Small caps"
        }
    }
}

/// A segment's static look (item 63b), applied by the runner each refresh: `ink` (one hex color,
/// or 2 to 4 gradient stops, per character in truecolor; none keeps the segment's own colors),
/// a letter style (`font`) and the attributes. The names follow the Desk's effects grammar where
/// they overlap. Statuslines can't animate, so there's no shimmer here.
struct SegmentStyle: Equatable, Sendable {
    var ink: [String] = []
    var font: LetterStyle?
    var bold = false
    var italic = false
    var dim = false
    var underline = false

    static let maxInk = 4

    var isEmpty: Bool { self == SegmentStyle() }

    /// Keeps the segment's own colors (no ink).
    var keepsOwnColors: Bool { ink.isEmpty }

    var json: [String: Any] {
        var o: [String: Any] = [:]
        if !ink.isEmpty { o["ink"] = ink }
        if let font { o["font"] = font.rawValue }
        for (key, on) in [("bold", bold), ("italic", italic), ("dim", dim), ("underline", underline)] where on {
            o[key] = true
        }
        return o
    }

    init(ink: [String] = [], font: LetterStyle? = nil, bold: Bool = false, italic: Bool = false,
         dim: Bool = false, underline: Bool = false) {
        self.ink = ink
        self.font = font
        self.bold = bold
        self.italic = italic
        self.dim = dim
        self.underline = underline
    }

    /// A style as the runner accepts it, nil for anything else: only the known keys, `ink` 1 to 4
    /// hex colors (#rgb or #rrggbb, the # optional), a known `font`, booleans.
    init?(json: Any) {
        guard let o = json as? [String: Any],
              Set(o.keys).isSubset(of: ["ink", "font", "bold", "italic", "dim", "underline"]) else { return nil }
        if let v = o["ink"] {
            guard let list = v as? [Any], (1...Self.maxInk).contains(list.count) else { return nil }
            let colors = list.compactMap { $0 as? String }
            guard colors.count == list.count, colors.allSatisfy(MessageMarkup.isHex) else { return nil }
            ink = colors
        }
        if let v = o["font"] {
            guard let name = v as? String, let style = LetterStyle(rawValue: name) else { return nil }
            font = style
        }
        for key in ["bold", "italic", "dim", "underline"] {
            guard let v = o[key] else { continue }
            guard let n = v as? NSNumber, CFGetTypeID(n) == CFBooleanGetTypeID() else { return nil }
            switch key {
            case "bold": bold = n.boolValue
            case "italic": italic = n.boolValue
            case "dim": dim = n.boolValue
            default: underline = n.boolValue
            }
        }
    }

    // MARK: The popover's edits

    /// One color (keeping the first stop) or a gradient (two stops at least).
    var isGradient: Bool { ink.count > 1 }

    mutating func keepOwnColors() { ink = [] }

    mutating func setSolid(_ hex: String) { ink = [hex] }

    mutating func makeGradient() {
        let first = ink.first ?? "#ff2a6d"
        ink = [first, ink.count > 1 ? ink[1] : "#05d9e8"]
    }

    mutating func makeSolid() { ink = [ink.first ?? "#ff2a6d"] }

    mutating func addStop() {
        guard ink.count >= 2, ink.count < Self.maxInk else { return }
        ink.append(ink.last ?? "#ffffff")
    }

    mutating func removeStop(at i: Int) {
        guard ink.count > 2, ink.indices.contains(i) else { return }
        ink.remove(at: i)
    }

    mutating func setStop(_ i: Int, hex: String) {
        guard ink.indices.contains(i), MessageMarkup.isHex(hex) else { return }
        ink[i] = hex.hasPrefix("#") ? hex : "#" + hex
    }

    /// `#rrggbb` for components 0...1.
    static func hex(red: Double, green: Double, blue: Double) -> String {
        func c(_ v: Double) -> String { String(format: "%02x", Int((min(1, max(0, v)) * 255).rounded())) }
        return "#" + c(red) + c(green) + c(blue)
    }

    /// Components 0...1 of `#rgb` or `#rrggbb`.
    static func components(_ hex: String) -> (Double, Double, Double)? {
        var h = hex.hasPrefix("#") ? String(hex.dropFirst()) : hex
        if h.count == 3 { h = h.map { "\($0)\($0)" }.joined() }
        guard h.count == 6, let v = Int(h, radix: 16) else { return nil }
        return (Double((v >> 16) & 0xFF) / 255, Double((v >> 8) & 0xFF) / 255, Double(v & 0xFF) / 255)
    }
}

/// Two sources in a combined statusline showing the same thing (item 63b).
struct StatuslineDuplicate: Identifiable, Equatable, Sendable {
    enum Side: Equatable, Hashable, Sendable {
        /// One of the user's segments, by matcher.
        case theirs(String)
        case sanduhr(SanduhrSegment)
        /// A mod's status entry or band, by its path.
        case mod(String)
    }

    let what: String
    let a: Side
    let b: Side
    /// Keep yours / Keep Sanduhr's apply (theirs against Sanduhr's); a mod's can only be explained.
    var resolvable: Bool {
        if case .theirs = a, case .sanduhr = b { return true }
        return false
    }

    var id: String { "\(what)|\(a)|\(b)" }

    /// The duplicates among what is kept now: theirs against Sanduhr's by kind, theirs against a
    /// mod by the mod's words, a usage-like mod (or Sanduhr's own meters mod) against Sanduhr's
    /// session and weekly.
    static func find(_ chips: StatuslineChips, mods: [ModStatusEntry]) -> [StatuslineDuplicate] {
        var out: [StatuslineDuplicate] = []
        let mine = Set(chips.mineChips.filter(\.kept).compactMap { SanduhrSegment(rawValue: $0.key) })
        var seen = Set<String>()
        let theirs = chips.lineIndices.flatMap { chips.chips(line: $0) }.filter { $0.kept && $0.enabled }
            .filter { seen.insert($0.key).inserted }
        for chip in theirs {
            guard let kind = chip.kind else { continue }
            if let s = kind.sanduhr, mine.contains(s) {
                out.append(StatuslineDuplicate(what: kind.title, a: .theirs(chip.key), b: .sanduhr(s)))
            }
            for mod in mods where mod.kinds.contains(kind) {
                out.append(StatuslineDuplicate(what: kind.title, a: .theirs(chip.key), b: .mod(mod.path)))
            }
        }
        for mod in mods {
            for s in [SanduhrSegment.session, .weekly] where mine.contains(s) && mod.showsUsage {
                out.append(StatuslineDuplicate(what: s.title, a: .mod(mod.path), b: .sanduhr(s)))
            }
        }
        return out
    }

    /// "2 duplicates: Context, Model", nil without any.
    static func summary(_ list: [StatuslineDuplicate]) -> String? {
        guard !list.isEmpty else { return nil }
        var names: [String] = []
        for d in list where !names.contains(d.what) { names.append(d.what) }
        return "\(list.count) duplicate\(list.count == 1 ? "" : "s"): \(names.joined(separator: ", "))"
    }
}

extension ModStatusEntry {
    /// Kinds of information the mod likely shows, from its name and description.
    var kinds: Set<SegmentKind> {
        showsUsage ? SegmentKind.fromModWords(name + " " + description).union([.session, .weekly])
                   : SegmentKind.fromModWords(name + " " + description)
    }

    /// The mod likely shows usage: Sanduhr's own meters mod, or words like usage, limits, quota,
    /// meters, rate or tokens in its name or description.
    var showsUsage: Bool {
        name == "sanduhr-meters" || SegmentKind.fromModWords(name + " " + description).contains(.session)
    }
}
