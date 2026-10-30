import Foundation

/// A font a design names: a family and, for a family the user may lack, an https link to get it. Sanduhr
/// never follows the link; the review only shows it.
struct DeskFontChoice: Equatable {
    var family: String
    var link: String?
}

/// One piece's part of a design's layout. Every member but `piece` is optional: absent leaves the current
/// value alone. A hidden piece has no anchor, size, tilt or style. `tilt` is the fold (whole degrees, -20 to
/// 20) and belongs to the piece's stack; `style` is the meter style, only on `DeskMeterStyles.pieces`.
struct DeskPieceLayout: Equatable {
    var piece: String
    var anchor: DeskAnchor?
    var size: Double?
    var hidden: Bool?
    var tilt: Double?
    var style: DeskMeterStyle?
}

/// A font a design names that this Mac lacks, with the link to get it when the design gave one.
struct MissingFont: Equatable {
    var family: String
    var link: String?

    func toJson() -> PackObject {
        WidgetJson.object([("family", .string(family)), ("link", link.map { .string($0) } ?? .null)])
    }
}

/// Where a design stands.
enum DeskDesignStatus: Equatable {
    case ok, rejected, needsFont

    /// The words of the status in tool results and files.
    var word: String {
        switch self {
        case .ok: return "ok"
        case .needsFont: return "needs_font"
        case .rejected: return "rejected"
        }
    }
}

/// What the machine adds to the rules. A nil `installedFonts` is unknown, and the font rule is skipped (the
/// app, which always knows, checks again).
struct DeskDesignContext {
    var installedFonts: [String]?

    static let unknown = DeskDesignContext(installedFonts: nil)
}

/// The verdict on a design. `design` is the normalized design (trimmed, canonical colors, snapped sizes)
/// whenever it parsed; nil when rejected.
struct DeskValidation {
    var status: DeskDesignStatus
    var reasons: [DeskReason]
    var fonts: [MissingFont]
    var design: DeskDesign?

    var ok: Bool { status == .ok }
}

/// A Desk design (spec 8): a PARTIAL set of changes to the Desk; a nil field leaves the current value alone.
/// The Mac subset of Windows' `DeskDesign`: `name`, `note`, `layout` ({piece: {anchor, size, hidden, tilt,
/// style}}), `font` (a family, or {family, link}), `ink` (one `#rrggbb`, or two to four for a gradient),
/// `plate`, `shadow` and `weather` ({nickname}). What only Windows has is refused with a reason, never
/// dropped: `monitor` in a layout piece, the launcher and glow pieces, the launcher, messages, looks and theme
/// fields, and the auto, light, dark and theme ink words.
///
/// Pure data and rules, no IO. `Tests/SanduhrTests/Fixtures/desk-designs/` holds the designs this and the
/// Python MCP's `design_validate` must judge the same way.
struct DeskDesign: Equatable {
    var name: String?
    var note: String?
    var layout: [DeskPieceLayout]?
    var font: DeskFontChoice?
    /// `#rrggbb` colors, lowercase: one, or two to four for a left-to-right gradient.
    var ink: [String]?
    var plate: Bool?
    var shadow: Bool?
    /// The weather piece's nickname (Windows' `weather.nickname`): nil leaves it alone, "" goes back to the
    /// place's own name.
    var weatherNickname: String?

    static let nameMax = 40
    static let noteMax = 200
    static let fontFamilyMax = 64
    static let maxPieces = 16
    /// The longest weather nickname, in characters.
    static let weatherNicknameMax = Weather.nicknameMax
    static let linkMax = 2048
    /// The whole design is far below this by construction; the cap is a belt against a runaway request.
    static let maxJsonBytes = 128 * 1024

    static let fields = ["name", "note", "layout", "weather", "font", "ink", "plate", "shadow"]
    static let layoutMembers = ["anchor", "size", "hidden", "tilt", "style"]
    static let inkWords = ["auto", "light", "dark", "theme"]

    /// Fields Windows has and the Mac Desk lacks: refused with this reason.
    static let windowsOnlyFields: [(field: String, reason: String)] = [
        ("launcher", "is Windows only: the Mac Desk has no Launcher piece"),
        ("messages", "is not part of a Mac Desk design; send message lines with propose_desk_messages"),
        ("looks", "is not part of a Mac Desk design; send song looks with propose_now_playing_looks"),
        ("theme", "is not part of a Mac Desk design; send a widget theme with propose_theme"),
    ]
    /// Pieces Windows has and the Mac Desk does not: refused with this reason.
    static let windowsOnlyPieces: [String: String] = [
        "launcher": "is a Windows piece; the Mac Desk has no Launcher",
        "glow": "is a Windows piece; the Mac Desk has no glow piece",
    ]

    // MARK: - Reading

    /// Reads a design from JSON, checking everything that needs no knowledge of the machine. A field that is
    /// wrong is a reason and the design is nil; an unknown field is a reason too, so a typo never silently
    /// does nothing.
    static func parse(_ node: PackValue?) -> (design: DeskDesign?, reasons: [DeskReason]) {
        guard let o = present(node)?.object else { return (nil, [DeskReason("design", "must be an object")]) }
        var r: [DeskReason] = []
        let windowsOnly = windowsOnlyFields.map(\.field)
        let unknown = WidgetProposal.ordinalSorted(o.keys.filter { !fields.contains($0) && !windowsOnly.contains($0) })
        if !unknown.isEmpty {
            r.append(DeskReason("design", "unknown field(s): " + unknown.prefix(5).map { clip($0, 24) }.joined(separator: ", ")
                + ". The fields are " + fields.joined(separator: ", ") + "."))
        }
        let name = text(o, "name", nameMax, &r)
        let note = text(o, "note", noteMax, &r)
        let layout = parseLayout(present(o["layout"]), &r)
        let weatherNickname = parseWeather(present(o["weather"]), &r)
        let font = parseFont(present(o["font"]), &r)
        let ink = parseInk(present(o["ink"]), &r)
        let plate = bool(o, "plate", &r)
        let shadow = bool(o, "shadow", &r)
        for (field, reason) in windowsOnlyFields where present(o[field]) != nil {
            r.append(DeskReason(field, reason))
        }
        if !r.isEmpty { return (nil, Array(r.prefix(40))) }
        return (DeskDesign(name: name, note: note, layout: layout, font: font, ink: ink, plate: plate, shadow: shadow,
                           weatherNickname: weatherNickname), [])
    }

    /// A key's value, nil when it is absent or a JSON null (System.Text.Json reads a null as no node).
    private static func present(_ v: PackValue?) -> PackValue? {
        if case .null? = v { return nil }
        return v
    }

    /// `s` cut to `n` UTF-16 units (C#'s `s[..n]`).
    private static func clip(_ s: String, _ n: Int) -> String {
        s.utf16.count <= n ? s : String(decoding: Array(s.utf16.prefix(n)), as: UTF16.self)
    }

    private static func hasControl(_ s: String) -> Bool { s.unicodeScalars.contains(where: MessageProposal.isControl) }

    private static func number(_ v: PackValue?) -> Double? {
        guard let n = v?.number, n.value.isFinite else { return nil }
        return n.value
    }

    private static func text(_ o: PackObject, _ key: String, _ limit: Int, _ r: inout [DeskReason]) -> String? {
        guard let n = present(o[key]) else { return nil }
        guard let raw = n.string else { r.append(DeskReason(key, "must be a string")); return nil }
        if raw.utf16.count > limit * 4 || raw.unicodeScalars.count > limit {
            r.append(DeskReason(key, "must be at most \(limit) characters"))
            return nil
        }
        if hasControl(raw) { r.append(DeskReason(key, "must be one line, with no control characters")); return nil }
        let t = raw.trimmingCharacters(in: .whitespacesAndNewlines)
        return t.isEmpty ? nil : t
    }

    private static func bool(_ o: PackObject, _ key: String, _ r: inout [DeskReason]) -> Bool? {
        guard let n = present(o[key]) else { return nil }
        if let b = n.bool { return b }
        r.append(DeskReason(key, "must be true or false"))
        return nil
    }

    /// Windows' `ParseWeather`: an object with one member, `nickname`, a one-line string of at most 24 characters;
    /// "" is kept (it goes back to the place's own name).
    private static func parseWeather(_ node: PackValue?, _ r: inout [DeskReason]) -> String? {
        guard let node else { return nil }
        guard let wo = node.object else {
            r.append(DeskReason("weather", "must be an object with nickname"))
            return nil
        }
        let before = r.count
        for bad in WidgetProposal.ordinalSorted(wo.keys.filter { $0 != "nickname" }).prefix(3) {
            r.append(DeskReason("weather", "unknown member '\(clip(bad, 20))'; use nickname"))
        }
        guard let nn = present(wo["nickname"]) else {
            if r.count == before {
                r.append(DeskReason("weather.nickname", "is missing; send a name, or an empty string for the place's own name"))
            }
            return nil
        }
        guard let raw = nn.string else { r.append(DeskReason("weather.nickname", "must be a string")); return nil }
        if hasControl(raw) { r.append(DeskReason("weather.nickname", "must be one line, with no control characters")); return nil }
        let nick = raw.trimmingCharacters(in: .whitespacesAndNewlines)
        if nick.utf16.count > weatherNicknameMax * 4 || nick.unicodeScalars.count > weatherNicknameMax {
            r.append(DeskReason("weather.nickname", "must be at most \(weatherNicknameMax) characters"))
            return nil
        }
        return r.count > before ? nil : nick
    }

    private static func parseLayout(_ node: PackValue?, _ r: inout [DeskReason]) -> [DeskPieceLayout]? {
        guard let node else { return nil }
        guard let lo = node.object else {
            r.append(DeskReason("layout", "must be an object of piece name to {anchor, size, hidden, tilt, style}"))
            return nil
        }
        if lo.count > maxPieces {
            r.append(DeskReason("layout", "names \(lo.count) pieces; the Desk has \(DeskArrangement.known.count)"))
            return nil
        }
        var pieces: [DeskPieceLayout] = []
        let before = r.count
        for key in lo.keys {
            let field = "layout." + clip(key, 24)
            guard DeskArrangement.known.contains(key) else {
                if let why = windowsOnlyPieces[key] {
                    r.append(DeskReason(field, why))
                } else {
                    r.append(DeskReason(field, "is not a Desk piece; the pieces are " + DeskArrangement.known.joined(separator: ", ")))
                }
                continue
            }
            guard let po = lo[key]?.object else {
                r.append(DeskReason(field, "must be an object with anchor, size, hidden, tilt and style"))
                continue
            }
            for bad in WidgetProposal.ordinalSorted(po.keys.filter { !layoutMembers.contains($0) }).prefix(3) {
                if bad == "monitor" {
                    r.append(DeskReason(field, "monitor is Windows only: the Mac Desk has no per-monitor placement; use anchor, size, hidden, tilt and style"))
                } else {
                    r.append(DeskReason(field, "unknown member '\(clip(bad, 20))'; use anchor, size, hidden, tilt and style"))
                }
            }
            var anchor: DeskAnchor?
            if let an = present(po["anchor"]) {
                if let w = an.string, let a = DeskAnchor(rawValue: w.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()) {
                    anchor = a
                } else {
                    r.append(DeskReason(field + ".anchor", "must be one of " + DeskAnchor.allCases.map(\.rawValue).joined(separator: ", ")))
                }
            }
            var size: Double?
            if let sn = present(po["size"]) {
                if let s = number(sn), s >= DeskArrangement.scaleRange.lowerBound - 1e-9, s <= DeskArrangement.scaleRange.upperBound + 1e-9 {
                    size = DeskArrangement.clampScale(s)
                } else {
                    r.append(DeskReason(field + ".size", "must be a number from 0.6 to 1.6"))
                }
            }
            var hidden: Bool?
            if let hn = present(po["hidden"]) {
                if let h = hn.bool { hidden = h } else { r.append(DeskReason(field + ".hidden", "must be true or false")) }
            }
            var tilt: Double?
            if let tn = present(po["tilt"]) {
                if let t = number(tn), t >= DeskArrangement.tiltRange.lowerBound - 1e-9, t <= DeskArrangement.tiltRange.upperBound + 1e-9 {
                    tilt = DeskArrangement.clampTilt(t)
                } else {
                    r.append(DeskReason(field + ".tilt", "must be a number of degrees from -20 to 20 (0 is upright)"))
                }
            }
            var style: DeskMeterStyle?
            if let stn = present(po["style"]) {
                // A style only on a piece that draws one: a clock with a style is a mistake, not a no-op.
                if let why = DeskMeterStyles.refusal(widget: key, word: stn.string) {
                    r.append(DeskReason(field + ".style", why))
                } else {
                    style = DeskMeterStyles.parse(stn.string?.trimmingCharacters(in: .whitespaces).lowercased())
                }
            }
            if hidden == true { anchor = nil; size = nil; tilt = nil; style = nil }
            pieces.append(DeskPieceLayout(piece: key, anchor: anchor, size: size, hidden: hidden, tilt: tilt, style: style))
        }
        // One slant per stack: two pieces sent to the same anchor with different tilts cannot both be honored.
        let words: [(widget: String, anchor: DeskAnchor, tilt: Double?)] = pieces.compactMap { p in
            guard let a = p.anchor, p.tilt != nil else { return nil }
            return (p.piece, a, p.tilt)
        }
        for clash in DeskArrangement.tiltConflicts(words) { r.append(DeskReason(clash.field, clash.message)) }
        return r.count > before ? nil : pieces
    }

    /// Why a font family name is refused, or nil. A family is a name as the font picker shows it: letters,
    /// digits, spaces and a few marks. Anything that could be a path, a URI, a font list or markup is not one.
    static func fontFamilyProblem(_ family: String) -> String? {
        if family.isEmpty { return nil }   // the system font
        if family != family.trimmingCharacters(in: .whitespacesAndNewlines) { return "must not start or end with a space" }
        if family.unicodeScalars.count > fontFamilyMax { return "must be at most \(fontFamilyMax) characters" }
        for s in family.unicodeScalars {
            if MessageProposal.isControl(s) { return "must not contain control characters" }
            if s.value < 0x80 {
                let c = Character(s)
                if !((c.isASCII && (c.isLetter || c.isNumber)) || " .-_()+&!".contains(c)) {
                    return "must be a font family name, not a path, link or list"
                }
            } else {
                switch s.properties.generalCategory {
                case .uppercaseLetter, .lowercaseLetter, .titlecaseLetter, .modifierLetter, .otherLetter, .decimalNumber,
                     .connectorPunctuation, .dashPunctuation, .openPunctuation, .closePunctuation, .initialPunctuation,
                     .finalPunctuation, .otherPunctuation, .mathSymbol, .currencySymbol, .modifierSymbol, .otherSymbol:
                    break
                default:
                    if !s.properties.isWhitespace { return "must be a font family name" }
                }
            }
        }
        return nil
    }

    /// Why an https link is refused, or nil (Windows' `LauncherTarget.Validate` for a link, with the address
    /// read by hand: the part between `https://` and the first `/`, `?` or `#`).
    static func linkProblem(_ link: String?) -> String? {
        guard let link, !link.isEmpty else { return "Enter a link." }
        if link.utf16.count > linkMax { return "That link is too long." }
        let scalars = link.unicodeScalars
        if link != link.trimmingCharacters(in: .whitespacesAndNewlines)
            || scalars.contains(where: { $0.properties.isWhitespace || $0.properties.generalCategory == .control }) {
            return "A link has no spaces."
        }
        if link.contains(where: { "\"'\\`".contains($0) }) { return "A link has no quotes or backslashes." }
        guard link.lowercased().hasPrefix("https://") else { return "A link must start with https://." }
        let rest = link.dropFirst(8)
        let authority = rest.prefix { !"/?#".contains($0) }
        var host = Substring(authority)
        var hasUserInfo = false
        if let at = authority.lastIndex(of: "@") {
            hasUserInfo = true
            host = authority[authority.index(after: at)...]
        }
        var port = Substring("")
        if host.hasPrefix("[") {
            guard let close = host.firstIndex(of: "]") else { return "That is not a link." }
            let tail = host[host.index(after: close)...]
            host = host[...close]
            if !tail.isEmpty {
                guard tail.hasPrefix(":") else { return "That is not a link." }
                port = tail.dropFirst()
            }
        } else if let colon = host.lastIndex(of: ":") {
            port = host[host.index(after: colon)...]
            host = host[..<colon]
        }
        if !port.isEmpty {
            guard port.allSatisfy({ $0.isASCII && $0.isNumber }), let n = Int(port), n <= 65535 else { return "That is not a link." }
        }
        if host.isEmpty { return "That link has no site name." }
        if hasUserInfo { return "A link has no sign-in part." }
        return nil
    }

    private static func parseFont(_ node: PackValue?, _ r: inout [DeskReason]) -> DeskFontChoice? {
        guard let node else { return nil }
        var family: String
        var link: String?
        if let fo = node.object {
            for bad in WidgetProposal.ordinalSorted(fo.keys.filter { $0 != "family" && $0 != "link" }).prefix(3) {
                r.append(DeskReason("font", "unknown member '\(clip(bad, 20))'; use family and link"))
            }
            guard let f = fo["family"]?.string else {
                r.append(DeskReason("font.family", "must be a font family name"))
                return nil
            }
            family = f
            if let ln = present(fo["link"]) {
                if let l = ln.string {
                    if let why = linkProblem(l) { r.append(DeskReason("font.link", why)) } else { link = l }
                } else {
                    r.append(DeskReason("font.link", "must be an https link"))
                }
            }
        } else if let f = node.string {
            family = f
        } else {
            r.append(DeskReason("font", "must be a font family name (or {family, link})"))
            return nil
        }
        if let problem = fontFamilyProblem(family) {
            r.append(DeskReason(node.object != nil ? "font.family" : "font", problem))
            return nil
        }
        return DeskFontChoice(family: family, link: link)
    }

    /// "#9AD7FF" is "9ad7ff", "fff" is "ffffff"; nil when it is not a 3 or 6 digit hex color.
    static func hex(_ raw: String?) -> String? {
        guard let raw else { return nil }
        var h = raw.trimmingCharacters(in: CharacterSet(charactersIn: " \t")).lowercased()
        if h.hasPrefix("#") { h.removeFirst() }
        guard h.count == 3 || h.count == 6, h.allSatisfy({ "0123456789abcdef".contains($0) }) else { return nil }
        return h.count == 3 ? h.map { "\($0)\($0)" }.joined() : h
    }

    private static func parseInk(_ node: PackValue?, _ r: inout [DeskReason]) -> [String]? {
        guard let node else { return nil }
        let help = "must be one #rrggbb color, or two to four colors for a gradient"
        if let s = node.string {
            let word = s.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
            if inkWords.contains(word) {
                r.append(DeskReason("ink", "\(word) is a Windows ink word; the Mac Desk's ink is one #rrggbb color, or two to four for a gradient"))
                return nil
            }
            if !s.contains(","), let h = hex(s) { return ["#" + h] }
            r.append(DeskReason("ink", help))
            return nil
        }
        if let arr = node.array {
            if arr.count < 1 || arr.count > 4 {
                r.append(DeskReason("ink", "a gradient is two to four colors"))
                return nil
            }
            var colors: [String] = []
            for item in arr {
                guard let h = hex(item.string) else {
                    r.append(DeskReason("ink", "every color must be a #rrggbb hex color"))
                    return nil
                }
                colors.append("#" + h)
            }
            return colors
        }
        r.append(DeskReason("ink", help))
        return nil
    }

    // MARK: - Writing

    private static func num(_ d: Double) -> PackValue {
        if d == d.rounded(), abs(d) < 1e15 { return WidgetJson.int(Int(d)) }
        return .number(PackNumber(value: d, isInt: false, repr: "\(d)", raw: "\(d)"))
    }

    /// The design as JSON: only the fields that are set, in the contract's shape.
    func toJson() -> PackObject {
        var f: [(String, PackValue)] = []
        if let name { f.append(("name", .string(name))) }
        if let note { f.append(("note", .string(note))) }
        if let layout {
            let lo = PackObject()
            for p in layout {
                var pf: [(String, PackValue)] = []
                if let a = p.anchor { pf.append(("anchor", .string(a.rawValue))) }
                if let s = p.size { pf.append(("size", Self.num(s))) }
                if let h = p.hidden { pf.append(("hidden", .bool(h))) }
                if let t = p.tilt { pf.append(("tilt", Self.num(t))) }
                if let st = p.style { pf.append(("style", .string(DeskMeterStyles.word(st)))) }
                lo.add(p.piece, .object(WidgetJson.object(pf)))
            }
            f.append(("layout", .object(lo)))
        }
        if let weatherNickname { f.append(("weather", .object(WidgetJson.object([("nickname", .string(weatherNickname))])))) }
        if let font {
            if let link = font.link {
                f.append(("font", .object(WidgetJson.object([("family", .string(font.family)), ("link", .string(link))]))))
            } else {
                f.append(("font", .string(font.family)))
            }
        }
        if let ink {
            f.append(("ink", ink.count == 1 ? .string(ink[0]) : .array(ink.map { .string($0) })))
        }
        if let plate { f.append(("plate", .bool(plate))) }
        if let shadow { f.append(("shadow", .bool(shadow))) }
        return WidgetJson.object(f)
    }

    // MARK: - Validating

    /// The one gate for a design: reads it with `parse`, then applies what needs the machine. A font that is
    /// neither bundled nor installed makes the design `needsFont` (valid, but it waits for the font); any other
    /// reason makes it rejected.
    static func validate(_ node: PackValue?, context: DeskDesignContext = .unknown) -> DeskValidation {
        if let node, WidgetJson.compact(node).utf8.count > maxJsonBytes {
            return DeskValidation(status: .rejected, reasons: [DeskReason("design", "is larger than \(maxJsonBytes / 1024) KB")], fonts: [], design: nil)
        }
        let parsed = parse(node)
        guard let d = parsed.design else {
            return DeskValidation(status: .rejected, reasons: parsed.reasons, fonts: [], design: nil)
        }
        var missing: [MissingFont] = []
        if let font = d.font, !font.family.isEmpty, let installed = context.installedFonts,
           BundledFonts.bundled(font.family) == nil,
           !installed.contains(where: { $0.lowercased() == font.family.lowercased() }) {
            missing.append(MissingFont(family: font.family, link: font.link))
        }
        return DeskValidation(status: missing.isEmpty ? .ok : .needsFont, reasons: [], fonts: missing, design: d)
    }
}
