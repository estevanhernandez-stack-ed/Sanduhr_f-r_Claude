import Foundation

/// What a pasted drawers part set besides rules: a look, an opacity and a Tidy arrangement, each optional. `corner` and `flow` are
/// set only when the paste named them; the rest come from the user's own Tidy when this is applied.
struct PastedDrawerLook: Equatable {
    var look: DrawerLook?
    var opacity: Int?
    var corner: DrawerCorner?
    var flow: DrawerFlow?

    var hasArrange: Bool { corner != nil || flow != nil }
    var any: Bool { look != nil || opacity != nil || hasArrange }
}

/// One line of the paste window's list: the field, what is wrong with it, and whether it blocks (an error) or only advises.
struct PastedFinding: Equatable {
    let field: String
    let text: String
    let isError: Bool
}

/// What a pasted AI chat answer held (setup spec, section 12): its Desk part kept raw (item 7 runs the `propose_desk` validator
/// on it), its drawer rules and the names they gave their drawers, the look part, every problem by field, and how many Sanduhr
/// blocks (JSON objects holding `desk` or `drawers`) the paste held. `rules` is nil when the paste had no rules, and `drawerLook` is
/// nil when it had no look, opacity or arrange; neither holds anything that failed a check.
struct PastedSetupRead {
    var desk: PackValue?
    var rules: [DrawerRule]?
    var ruleDrawerNames: [String]
    var reasons: [DeskReason]
    var blocks: Int
    var drawerLook: PastedDrawerLook?

    /// No problem, and at least one part present.
    var ok: Bool { reasons.isEmpty && (desk != nil || rules != nil || drawerLook != nil) }

    /// The problems as the window lists them, in its neutral words.
    var findings: [PastedSetup.Finding] { PastedSetup.findings(errors: reasons, warnings: []) }
}

/// Reads a pasted AI chat answer (a port of Windows' `PastedSetup`). The answer is prose around one JSON block, often fenced; the
/// reader scans balanced JSON objects (a brace inside a string does not count), takes the first good one holding `desk` or
/// `drawers`, and says how many there were. The only parts are `desk` and `drawers`; the drawers part holds rules, look, opacity and
/// arrange and nothing else, so a chat can name no file and no path. One error rejects the whole block.
enum PastedSetup {
    typealias Finding = PastedFinding

    static let minRules = 1
    static let maxRules = 20
    static let maxDays = DrawerRules.daysRange.upperBound
    static let maxTextChars = 60
    /// The longest paste the reader scans, in characters (UTF-16 units): the scan is quadratic in unmatched braces.
    static let maxPasteChars = 200_000
    /// The scan gives up after this many opening braces that start nothing readable.
    private static let maxMisses = 500

    // MARK: - Words

    /// The rule kinds as a chat writes them, in Windows' words: the kind's name in snake_case. The Mac's `links` kind is
    /// Windows' `shortcuts`; the reader takes both and the prompt teaches `shortcuts`, so one answer reads on either platform.
    static let kindWords: [String] = kinds.map(\.word)

    private static let kinds: [(kind: DrawerRuleKind, word: String)] = [
        (.screenshots, "screenshots"), (.images, "images"), (.documents, "documents"), (.installers, "installers"),
        (.archives, "archives"), (.links, "shortcuts"), (.olderThan, "older_than"),
        (.nameContains, "name_contains"), (.nameStartsWith, "name_starts_with"),
    ]

    static func kindWord(_ kind: DrawerRuleKind) -> String { kinds.first { $0.kind == kind }!.word }

    private static func kind(_ word: String) -> DrawerRuleKind? {
        if word == "links" { return .links }
        return kinds.first { $0.word == word }?.kind
    }

    /// The tidy corners as Windows writes them.
    static let cornerWords = ["tl", "tr", "bl", "br"]
    private static let corners: [String: DrawerCorner] = ["tl": .topLeft, "tr": .topRight, "bl": .bottomLeft, "br": .bottomRight]

    static func cornerWord(_ c: DrawerCorner) -> String {
        switch c {
        case .topLeft: return "tl"
        case .topRight: return "tr"
        case .bottomLeft: return "bl"
        case .bottomRight: return "br"
        }
    }

    // MARK: - Reading

    static func read(_ raw: String?) -> PastedSetupRead {
        let raw = raw ?? ""
        if raw.utf16.count > maxPasteChars {
            return PastedSetupRead(desk: nil, rules: nil, ruleDrawerNames: [],
                                   reasons: [DeskReason("design", "is longer than \(maxPasteChars) characters; paste only the answer")],
                                   blocks: 0, drawerLook: nil)
        }
        let (blocks, broken) = scanBlocks(raw)
        let candidates = blocks.compactMap { $0.object }.filter { $0.has("desk") || $0.has("drawers") }
        if candidates.isEmpty {
            let why = broken > 0
                ? "a block that looks like Sanduhr's did not parse as JSON; check for a missing quote or bracket, or ask the chat to send it again"
                : "no Sanduhr block found; ask the chat for one JSON block with a desk part, a drawers part or both"
            return PastedSetupRead(desk: nil, rules: nil, ruleDrawerNames: [], reasons: [DeskReason("design", why)], blocks: 0, drawerLook: nil)
        }
        // The first good block is the first whose parts pass this reader's checks; the Desk validator runs later.
        // When none is clean, the first block's problems are the ones to show.
        var first: PastedSetupRead?
        for c in candidates {
            let r = readBlock(c, count: candidates.count)
            if r.reasons.isEmpty { return r }
            if first == nil { first = r }
        }
        return first!
    }

    // MARK: - Window words

    /// The Desk validator's own phrasings that name a tool, and what the AI chat window says instead. Only these are replaced:
    /// text echoed from the paste stays as typed.
    private static let phrasings: [(from: String, to: String)] = [
        ("; send message lines with propose_desk_messages", "; message lines are set in Settings, Desk"),
        ("; send song looks with propose_now_playing_looks", "; song looks are set in Settings, Desk"),
        ("; send a widget theme with propose_theme", "; a theme is set in Settings, Themes"),
        ("get_desk", "this Mac's Desk settings"),
        ("propose_desk", "the Desk"),
        ("Claude cannot", "an AI chat cannot"),
        ("Claude's", "the AI chat's"),
    ]

    /// A reason's words as the AI chat window shows them: the Desk's own rules were written for a coding agent, and the chat
    /// window names no product and no tool.
    static func neutral(_ text: String) -> String {
        var t = text
        for (from, to) in phrasings { t = t.replacingOccurrences(of: from, with: to) }
        return t
    }

    /// The paste window's problem list: each reason by field in neutral words, errors first, then the warnings.
    static func findings(errors: [DeskReason], warnings: [DeskReason]) -> [PastedFinding] {
        var out: [PastedFinding] = []
        for (list, isError) in [(errors, true), (warnings, false)] {
            for r in list {
                let f = PastedFinding(field: neutral(r.field), text: neutral(r.message), isError: isError)
                if !out.contains(f) { out.append(f) }   // the reader and the validator can name the same field twice
            }
        }
        return out
    }

    // MARK: - One block

    private static func readBlock(_ block: PackObject, count: Int) -> PastedSetupRead {
        var reasons: [DeskReason] = []
        for key in block.keys where key != "desk" && key != "drawers" {
            reasons.append(DeskReason(clip(key, 24), "is not a part of the answer; the parts are desk and drawers"))
        }

        var desk: PackValue?
        if let d = block["desk"] {
            if d.object != nil { desk = d } else { reasons.append(DeskReason("desk", "must be an object")) }
        }

        var rules: [DrawerRule]?
        var drawerLook: PastedDrawerLook?
        var names: [String] = []
        if let dv = block["drawers"] {
            if let dobj = dv.object {
                if dobj.count == 0 {
                    reasons.append(DeskReason("drawers", "must hold rules, look, opacity or arrange; leave drawers out to change nothing"))
                } else {
                    for key in dobj.keys where !["rules", "look", "opacity", "arrange"].contains(key) {
                        reasons.append(DeskReason("drawers." + clip(key, 24), "is not part of drawers; it holds rules, look, opacity and arrange"))
                    }
                    drawerLook = readDrawerLook(dobj, &reasons)
                    if let rv = dobj["rules"] {
                        if let arr = rv.array, !arr.isEmpty {
                            if arr.count > maxRules {
                                reasons.append(DeskReason("drawers.rules", "has \(arr.count) rules; at most \(maxRules)"))
                            } else {
                                var list: [DrawerRule] = []
                                var ids: [String: String] = [:]   // lowercased name to id
                                func idFor(_ name: String) -> String {
                                    // One name is one drawer; two names that slug alike get ids of their own.
                                    if let id = ids[name.lowercased()] { return id }
                                    let taken = Set(ids.values)
                                    let base = DrawerIds.from(name)
                                    var id = base
                                    var i = 2
                                    while taken.contains(id) {
                                        let suffix = "-\(i)"
                                        id = String(base.prefix(DrawerIds.maxLength - suffix.count)) + suffix
                                        i += 1
                                    }
                                    ids[name.lowercased()] = id
                                    return id
                                }
                                for (i, item) in arr.enumerated() {
                                    if let r = readRule(item, at: "drawers.rules[\(i)]", &reasons, idFor) {
                                        list.append(r.rule)
                                        names.append(r.name)
                                    }
                                }
                                rules = list
                            }
                        } else {
                            reasons.append(DeskReason("drawers.rules", "must be a list of \(minRules) to \(maxRules) rules; leave rules out to keep them as they are"))
                        }
                    }
                }
            } else {
                reasons.append(DeskReason("drawers", "must be { \"rules\": [ ... ], \"look\": ..., \"opacity\": ..., \"arrange\": { ... } }"))
            }
        }

        // Nothing applies partly: a block that had a problem hands back nothing of its drawers part.
        if !reasons.isEmpty { rules = nil; drawerLook = nil; names = [] }
        return PastedSetupRead(desk: desk, rules: rules, ruleDrawerNames: names, reasons: Array(reasons.prefix(40)),
                               blocks: count, drawerLook: drawerLook)
    }

    private static func readDrawerLook(_ d: PackObject, _ reasons: inout [DeskReason]) -> PastedDrawerLook? {
        var out = PastedDrawerLook()
        if let lv = d["look"] {
            if let w = lv.string, let l = DrawerLook(rawValue: w) { out.look = l }
            else { reasons.append(DeskReason("drawers.look", "must be one of " + DrawerLook.allCases.map(\.rawValue).joined(separator: ", "))) }
        }
        if let ov = d["opacity"] {
            let r = DrawerSettings.opacityRange
            if let n = wholeNumber(ov), r.contains(n) { out.opacity = n }
            else { reasons.append(DeskReason("drawers.opacity", "must be a whole number from \(r.lowerBound) to \(r.upperBound)")) }
        }
        if let av = d["arrange"] {
            if let a = av.object, a.count > 0 {
                var bad = false
                for key in a.keys where key != "corner" && key != "direction" {
                    reasons.append(DeskReason("drawers.arrange." + clip(key, 24), "is not part of arrange; it holds corner and direction"))
                    bad = true
                }
                var corner: DrawerCorner?
                var flow: DrawerFlow?
                if let cv = a["corner"] {
                    if let w = cv.string, let c = corners[w] { corner = c }
                    else { reasons.append(DeskReason("drawers.arrange.corner", "must be one of " + cornerWords.joined(separator: ", "))); bad = true }
                }
                if let fv = a["direction"] {
                    if let w = fv.string, let f = DrawerFlow(rawValue: w) { flow = f }
                    else { reasons.append(DeskReason("drawers.arrange.direction", "must be one of " + DrawerFlow.allCases.map(\.rawValue).joined(separator: ", "))); bad = true }
                }
                if !bad { out.corner = corner; out.flow = flow }
            } else {
                reasons.append(DeskReason("drawers.arrange", "must be { \"corner\": ..., \"direction\": ... } with at least one of them"))
            }
        }
        return out.any ? out : nil
    }

    private static func readRule(_ node: PackValue, at: String, _ reasons: inout [DeskReason],
                                 _ idFor: (String) -> String) -> (rule: DrawerRule, name: String)? {
        guard let o = node.object else {
            reasons.append(DeskReason(at, "must be an object with kind and drawer"))
            return nil
        }
        let before = reasons.count
        for key in o.keys where !["kind", "drawer", "text", "days"].contains(key) {
            reasons.append(DeskReason(at + "." + clip(key, 24), "is not part of a rule; a rule has kind, drawer, text and days"))
        }

        let kindWordRead = o["kind"]?.string
        var kind = DrawerRuleKind.screenshots
        if let w = kindWordRead, let k = Self.kind(w) { kind = k }
        else { reasons.append(DeskReason(at + ".kind", "must be one of " + kindWords.joined(separator: ", "))) }

        let name = o["drawer"]?.string?.trimmingCharacters(in: .whitespacesAndNewlines)
        if name == nil || !DrawerNames.isValid(name!) || name!.contains("\\") {
            reasons.append(DeskReason(at + ".drawer", "must be a short folder name of up to \(DrawerNames.maxLength) characters, not a path"))
        }

        var text: String?
        if kind == .nameContains || kind == .nameStartsWith {
            let t = o["text"]?.string?.trimmingCharacters(in: .whitespacesAndNewlines)
            if t == nil || t!.isEmpty {
                reasons.append(DeskReason(at + ".text", "is needed for \(kindWordRead ?? "this kind"): part of a file name"))
            } else if t!.unicodeScalars.count > maxTextChars {
                reasons.append(DeskReason(at + ".text", "must be at most \(maxTextChars) characters"))
            } else if t!.unicodeScalars.contains(where: { $0 == "/" || $0 == ":" || $0 == "\\" || MessageProposal.isControl($0) }) {
                reasons.append(DeskReason(at + ".text", "must be part of a file name, not a path"))
            } else { text = t }
        }

        var days = DrawerRules.defaultDays
        if kind == .olderThan {
            if let dv = o["days"], let n = wholeNumber(dv), DrawerRules.daysRange.contains(n) { days = n }
            else { reasons.append(DeskReason(at + ".days", "must be a whole number from \(DrawerRules.daysRange.lowerBound) to \(maxDays)")) }
        }

        if reasons.count > before { return nil }
        let id = idFor(name!)
        return (DrawerRule(kind: kind, drawer: id, on: true, text: text, days: days, drawerName: name!), name!)
    }

    private static func wholeNumber(_ v: PackValue) -> Int? {
        guard let n = v.number, n.value.isFinite, n.value == n.value.rounded(.down), abs(n.value) < Double(Int32.max) else { return nil }
        return Int(n.value)
    }

    private static func clip(_ s: String, _ n: Int) -> String {
        s.utf16.count <= n ? s : String(decoding: Array(s.utf16.prefix(n)), as: UTF16.self)
    }

    // MARK: - The scan

    /// Every top-level JSON object in the paste, in order: a scan for balanced braces outside strings, each parsed alone. A span
    /// that does not parse is skipped and the scan goes on from the next character, so a prose brace before the real block is
    /// harmless. `broken` counts the skipped spans that mention desk or drawers. Trailing commas and comments are forgiven, as on
    /// Windows; a repeated key is not.
    private static func scanBlocks(_ raw: String) -> (blocks: [PackValue], broken: Int) {
        let chars = Array(raw.unicodeScalars)
        var found: [PackValue] = []
        var broken = 0
        var misses = 0
        var i = 0
        while i < chars.count {
            defer { i += 1 }
            guard chars[i] == "{" else { continue }
            if misses > maxMisses { break }
            var depth = 0
            var end = -1
            var inString = false
            var escaped = false
            var j = i
            while j < chars.count {
                let c = chars[j]
                if inString {
                    if escaped { escaped = false }
                    else if c == "\\" { escaped = true }
                    else if c == "\"" { inString = false }
                } else if c == "\"" { inString = true }
                else if c == "{" { depth += 1 }
                else if c == "}" { depth -= 1; if depth == 0 { end = j; break } }
                j += 1
            }
            if end < 0 { misses += 1; continue }
            let span = Array(chars[i...end])
            if case .success(let node) = PackJson.parse(forgive(span)) {
                found.append(node)
                i = end
            } else {
                misses += 1
                var s = String.UnicodeScalarView(); s.append(contentsOf: span)
                let text = String(s)
                if text.contains("\"desk\"") || text.contains("\"drawers\"") { broken += 1 }
            }
        }
        return (found, broken)
    }

    /// The span with comments and trailing commas outside strings taken out.
    private static func forgive(_ s: [Unicode.Scalar]) -> [Unicode.Scalar] {
        var out: [Unicode.Scalar] = []
        out.reserveCapacity(s.count)
        var inString = false
        var escaped = false
        var i = 0
        while i < s.count {
            let c = s[i]
            if inString {
                out.append(c)
                if escaped { escaped = false } else if c == "\\" { escaped = true } else if c == "\"" { inString = false }
                i += 1
                continue
            }
            if c == "\"" { inString = true; out.append(c); i += 1; continue }
            if c == "/", i + 1 < s.count, s[i + 1] == "/" {
                while i < s.count, s[i] != "\n" { i += 1 }
                continue
            }
            if c == "/", i + 1 < s.count, s[i + 1] == "*" {
                i += 2
                while i + 1 < s.count, !(s[i] == "*" && s[i + 1] == "/") { i += 1 }
                i = min(s.count, i + 2)
                continue
            }
            if c == "," {
                var k = i + 1
                while k < s.count, s[k] == " " || s[k] == "\t" || s[k] == "\n" || s[k] == "\r" { k += 1 }
                if k < s.count, s[k] == "}" || s[k] == "]" { i += 1; continue }
            }
            out.append(c)
            i += 1
        }
        return out
    }
}
