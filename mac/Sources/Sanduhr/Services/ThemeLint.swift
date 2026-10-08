import Foundation

/// One thing the lint has to say about a theme: how bad, which field, and a sentence that names
/// the fix. Errors block the theme; warnings ride along with one that goes through.
struct ThemeFinding: Equatable {
    enum Level: String { case error, warning }
    let level: Level
    let field: String
    let message: String
}

/// The theme validator behind Claude's theme proposals (item 55): a port of the Windows
/// ThemeLint (windows-dotnet/src/Sanduhr.Core/ThemeLint.cs), same rules, thresholds and wording,
/// so a theme an agent iterates against reads the same on either machine. The MCP server runs a
/// Python copy (`lint_theme` in mac/integrations/sanduhr_mcp.py) to refuse early; this one is the
/// authority. Pure: a JSON object in, findings out.
///
/// Errors enforce the schema: the name, the fourteen `#rrggbb` colors, the dials' ranges. The
/// Mac adds its own fields: `description` (one line, 200 characters), `ghost_alpha`,
/// `monospace_font`, and `opts_out_of_mica` must be a boolean (the Mac's decoder would refuse
/// anything else). Warnings measure the design rules: a dark base, text contrast over the card
/// as composited over a mid-gray desktop, a monochrome text ramp, a pace marker that shows on the
/// bar fills, one accent hue. The built-ins lint with no findings (the calibration test).
enum ThemeLint {
    static let maxNameLength = 24
    static let maxDescriptionLength = 200
    static let darkBaseMaxLuminance = 0.25
    static let textContrastMin = 4.5
    static let textSecondaryContrastMin = 3.0
    static let paceMarkerContrastMin = 1.4
    static let paceMarkerHueMin = 60.0
    static let hueToleranceDegrees = 30.0
    /// Hue is only compared between colors this saturated or more: a gray has no hue.
    static let hueSaturationFloor = 0.15

    /// The neutral mid-gray desktop the card is measured over.
    static let desktop = RGB(r: 0x80 / 255.0, g: 0x80 / 255.0, b: 0x80 / 255.0)
    /// The lowest usage-ramp bar fill (green); the pace marker must read on it.
    static let barFillGreen = "#4ade80"

    static let colorFields = [
        "bg", "glass", "glass_on_mica", "title_bg", "border",
        "text", "text_secondary", "text_dim", "text_muted",
        "accent", "bar_bg", "footer_bg", "pace_marker", "sparkline",
    ]

    struct RGB: Equatable {
        var r, g, b: Double
    }

    static func ok(_ findings: [ThemeFinding]) -> Bool { !findings.contains { $0.level == .error } }

    /// Lints raw JSON. A parse failure or a document that is not an object is one error on `json`.
    static func lint(json data: Data) -> [ThemeFinding] {
        guard let object = try? JSONSerialization.jsonObject(with: data) else {
            return [ThemeFinding(level: .error, field: "json", message: "Not valid JSON.")]
        }
        return lint(object)
    }

    static func lint(_ object: Any?) -> [ThemeFinding] {
        guard let data = object as? [String: Any] else {
            return [ThemeFinding(level: .error, field: "json", message: "Theme JSON must be an object.")]
        }
        var findings: [ThemeFinding] = []
        let colors = schema(data, into: &findings)
        let glassAlpha = dials(data, into: &findings)
        let optsOut = isBool(data["opts_out_of_mica"]) && (data["opts_out_of_mica"] as? Bool ?? false)
        guard ok(findings) else { return findings }
        designRules(data, colors: colors, glassAlpha: glassAlpha, optsOut: optsOut, into: &findings)
        return findings
    }

    /// Findings as `[{level, field, message}]` for the result file.
    static func json(_ findings: [ThemeFinding]) -> [[String: String]] {
        findings.map { ["level": $0.level.rawValue, "field": $0.field, "message": $0.message] }
    }

    // MARK: - Schema (errors)

    private static func schema(_ data: [String: Any], into findings: inout [ThemeFinding]) -> [String: RGB] {
        let name = data["name"] as? String
        if name == nil {
            findings.append(error("name", "name is required (1 to 12 characters, Title Case)."))
        } else if let name, name.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
            findings.append(error("name", "name must not be empty."))
        } else if let name, name.unicodeScalars.count > maxNameLength {
            findings.append(error("name", "name is \(name.unicodeScalars.count) characters; the strip shows 12, \(maxNameLength) is the limit."))
        } else if let name, name.unicodeScalars.contains(where: MessageProposal.isControl) {
            findings.append(error("name", "name must be one line with no control characters."))
        }

        var colors: [String: RGB] = [:]
        for field in colorFields {
            guard let raw = present(data[field]) else {
                findings.append(error(field, "\(field) is required (a #rrggbb color)."))
                continue
            }
            guard let rgb = parseHex(raw as? String) else {
                findings.append(error(field, "\(field) must be a #rrggbb color (six hex digits), got \(describe(raw))."))
                continue
            }
            colors[field] = rgb
        }
        return colors
    }

    /// The dials' checks; returns glass_alpha (or its default) for the contrast rule.
    private static func dials(_ data: [String: Any], into findings: inout [ThemeFinding]) -> Double {
        let glassAlpha = checkRange(data, "glass_alpha", 0, 1, 0.80, &findings)
        _ = checkRange(data, "border_alpha", 0, 1, 0.40, &findings)
        checkOptionalHex(data, "border_tint", &findings)
        if let ab = present(data["accent_bloom"]) {
            if let abo = ab as? [String: Any] {
                _ = checkRange(abo, "blur", 0, 20, 4, &findings, label: "accent_bloom.blur")
                _ = checkRange(abo, "alpha", 0, 1, 0.45, &findings, label: "accent_bloom.alpha")
            } else {
                findings.append(error("accent_bloom", "accent_bloom must be an object {\"blur\": 3-8, \"alpha\": 0.25-0.65}."))
            }
        }
        if let ih = present(data["inner_highlight"]) {
            if let iho = ih as? [String: Any] {
                if present(iho["color"]) == nil {
                    findings.append(error("inner_highlight.color", "inner_highlight needs a color (#rrggbb) or the whole field set to null."))
                } else {
                    checkOptionalHex(iho, "color", &findings, label: "inner_highlight.color")
                }
                _ = checkRange(iho, "alpha", 0, 1, 0.20, &findings, label: "inner_highlight.alpha")
            } else {
                findings.append(error("inner_highlight", "inner_highlight must be {\"color\": \"#rrggbb\", \"alpha\": 0.15-0.30} or null."))
            }
        }
        _ = checkRange(data, "card_corner_radius", 0, 24, 10, &findings)
        _ = checkRange(data, "breath_period_ms", 500, 20000, 4000, &findings)
        macFields(data, into: &findings)
        return glassAlpha
    }

    /// The fields the Mac reads beyond the Windows schema.
    private static func macFields(_ data: [String: Any], into findings: inout [ThemeFinding]) {
        _ = checkRange(data, "ghost_alpha", 0, 1, 1.0, &findings)
        if let d = present(data["description"]) {
            let s = d as? String
            if s == nil || s!.unicodeScalars.count > maxDescriptionLength
                || s!.unicodeScalars.contains(where: MessageProposal.isControl) {
                findings.append(error("description", "description must be one line of at most \(maxDescriptionLength) characters, or null."))
            }
        }
        if let m = present(data["monospace_font"]), !(m is String) {
            findings.append(error("monospace_font", "monospace_font must be a string (any value turns on monospaced numbers) or null."))
        }
        if let o = present(data["opts_out_of_mica"]), !isBool(o) {
            findings.append(error("opts_out_of_mica", "opts_out_of_mica must be true or false, got \(describe(o))."))
        }
        // Text style (item 65d).
        if let ink = present(data["title_ink"]), titleInk(ink) == nil {
            findings.append(error("title_ink", "title_ink must be 2 to 4 #rrggbb colors (the title's gradient, left to right), or null."))
        }
        if let s = present(data["title_style"]), (s as? String).flatMap(LetterStyle.init(tag:)) == nil {
            findings.append(error("title_style", "title_style must be one of \(LetterStyle.tagNames), or null."))
        }
    }

    /// `title_ink`'s stops, nil unless it is 2 to 4 `#rrggbb` strings.
    static func titleInk(_ v: Any?) -> [RGB]? {
        guard let list = v as? [Any], (2...4).contains(list.count) else { return nil }
        let stops = list.compactMap { parseHex($0 as? String) }
        return stops.count == list.count ? stops : nil
    }

    // MARK: - Design rules (warnings)

    private static func designRules(_ data: [String: Any], colors c: [String: RGB], glassAlpha: Double,
                                    optsOut: Bool, into findings: inout [ThemeFinding]) {
        for field in ["bg", "glass", "glass_on_mica"] {
            let l = luminance(c[field]!)
            if l > darkBaseMaxLuminance {
                findings.append(warning(field, "\(field) is light (luminance \(fmt(l, 2)), keep it under \(fmt(darkBaseMaxLuminance, 2))); translucent glass layering does not work on light surfaces."))
            }
        }

        let card = composite(c["glass_on_mica"]!, optsOut ? 1.0 : glassAlpha, desktop)
        let cardL = luminance(card)
        let textRatio = contrast(luminance(c["text"]!), cardL)
        if textRatio < textContrastMin {
            findings.append(warning("text", "text reads at \(fmt(textRatio, 1)):1 on the card (needs \(fmt(textContrastMin, 1)):1); pick a brighter text or a darker glass_on_mica."))
        }
        let secondaryRatio = contrast(luminance(c["text_secondary"]!), cardL)
        if secondaryRatio < textSecondaryContrastMin {
            findings.append(warning("text_secondary", "text_secondary reads at \(fmt(secondaryRatio, 1)):1 on the card (needs \(fmt(textSecondaryContrastMin, 1)):1)."))
        }
        // Each stop of the title's gradient reads like text (item 65d).
        for (i, stop) in (titleInk(data["title_ink"]) ?? []).enumerated() {
            let ratio = contrast(luminance(stop), cardL)
            if ratio < textContrastMin {
                findings.append(warning("title_ink", "title_ink stop \(i + 1) reads at \(fmt(ratio, 1)):1 on the card (needs \(fmt(textContrastMin, 1)):1); brighten it or darken glass_on_mica."))
            }
        }

        let ramp = ["text", "text_secondary", "text_dim", "text_muted"]
        for i in 1..<ramp.count where luminance(c[ramp[i]]!) >= luminance(c[ramp[i - 1]]!) {
            findings.append(warning(ramp[i], "\(ramp[i]) is not darker than \(ramp[i - 1]); the text ramp is one hue at decreasing luminance."))
        }
        for i in 1..<ramp.count {
            if let d = hueDistance(c["text"]!, c[ramp[i]]!), d > hueToleranceDegrees {
                findings.append(warning(ramp[i], "\(ramp[i]) is a different hue from text (\(fmt(d, 0)) degrees apart); keep the ramp monochrome."))
            }
        }

        paceMarkerRule(c, into: &findings)

        if let d = hueDistance(c["accent"]!, c["sparkline"]!), d > hueToleranceDegrees {
            findings.append(warning("sparkline", "sparkline is a different hue from accent (\(fmt(d, 0)) degrees apart); one accent hue reads as one brand."))
        }
        if let tint = parseHex(data["border_tint"] as? String), let d = hueDistance(c["accent"]!, tint), d > hueToleranceDegrees {
            findings.append(warning("border_tint", "border_tint is a different hue from accent (\(fmt(d, 0)) degrees apart); tint borders with the accent or leave it null."))
        }
        if optsOut && glassAlpha < 1.0 {
            findings.append(warning("glass_alpha", "opts_out_of_mica is true, so set glass_alpha to 1.0; cards render translucent on non-Mica fallbacks otherwise."))
        }
    }

    /// The marker stands out by luminance or by hue (Ember's amber tick on the green fill is a hue
    /// win, not a luminance one).
    private static func paceMarkerRule(_ c: [String: RGB], into findings: inout [ThemeFinding]) {
        let green = parseHex(barFillGreen)!
        let marker = c["pace_marker"]!
        func hidden(on fill: RGB) -> Bool {
            contrast(luminance(marker), luminance(fill)) < paceMarkerContrastMin
                && (hueDistance(marker, fill) ?? 0) < paceMarkerHueMin
        }
        let onGreen = hidden(on: green)
        if onGreen || hidden(on: c["bar_bg"]!) {
            findings.append(warning("pace_marker", "pace_marker disappears on the \(onGreen ? "green bar fill" : "empty bar"); it should stand out on every fill by brightness or by a complementary hue."))
        }
    }

    // MARK: - Color math

    /// `#rrggbb` (exactly: a hash and six hex digits) to 0…1 channels.
    static func parseHex(_ s: String?) -> RGB? {
        guard let s, s.utf8.count == 7, s.hasPrefix("#") else { return nil }
        let digits = s.dropFirst()
        guard digits.allSatisfy(\.isHexDigit), digits.allSatisfy(\.isASCII),
              let v = UInt32(digits, radix: 16) else { return nil }
        return RGB(r: Double((v >> 16) & 0xFF) / 255, g: Double((v >> 8) & 0xFF) / 255, b: Double(v & 0xFF) / 255)
    }

    /// WCAG relative luminance, 0 (black) to 1 (white).
    static func luminance(_ c: RGB) -> Double {
        func linear(_ v: Double) -> Double { v <= 0.03928 ? v / 12.92 : pow((v + 0.055) / 1.055, 2.4) }
        return 0.2126 * linear(c.r) + 0.7152 * linear(c.g) + 0.0722 * linear(c.b)
    }

    /// WCAG contrast ratio between two luminances, 1 to 21.
    static func contrast(_ l1: Double, _ l2: Double) -> Double {
        (max(l1, l2) + 0.05) / (min(l1, l2) + 0.05)
    }

    /// `top` at `alpha` over `under`.
    static func composite(_ top: RGB, _ alpha: Double, _ under: RGB) -> RGB {
        let a = min(1, max(0, alpha))
        return RGB(r: top.r * a + under.r * (1 - a), g: top.g * a + under.g * (1 - a), b: top.b * a + under.b * (1 - a))
    }

    /// HSV saturation, 0 to 1.
    static func saturation(_ c: RGB) -> Double {
        let mx = max(c.r, c.g, c.b), mn = min(c.r, c.g, c.b)
        return mx <= 0 ? 0 : (mx - mn) / mx
    }

    /// HSV hue in degrees, 0 to 360.
    static func hue(_ c: RGB) -> Double {
        let mx = max(c.r, c.g, c.b), mn = min(c.r, c.g, c.b)
        let d = mx - mn
        guard d > 0 else { return 0 }
        var h: Double
        if mx == c.r { h = fmod((c.g - c.b) / d, 6) } else if mx == c.g { h = (c.b - c.r) / d + 2 } else { h = (c.r - c.g) / d + 4 }
        h *= 60
        return h < 0 ? h + 360 : h
    }

    /// Degrees between two hues, or nil when either color is too gray to have one.
    static func hueDistance(_ a: RGB, _ b: RGB) -> Double? {
        guard saturation(a) >= hueSaturationFloor, saturation(b) >= hueSaturationFloor else { return nil }
        let d = fmod(abs(hue(a) - hue(b)), 360)
        return d > 180 ? 360 - d : d
    }

    // MARK: - Keys

    /// The file-key rule (Windows ThemeHandoff.IsValidKey): lowercase ASCII letters, digits and
    /// hyphens, 1 to 40, not starting with a hyphen.
    static func isValidKey(_ key: String?) -> Bool {
        guard let key, !key.isEmpty, key.utf8.count <= 40, let first = key.unicodeScalars.first else { return false }
        func lowerOrDigit(_ s: Unicode.Scalar) -> Bool { ("a"..."z").contains(s) || ("0"..."9").contains(s) }
        return lowerOrDigit(first) && key.unicodeScalars.allSatisfy { lowerOrDigit($0) || $0 == "-" }
    }

    /// Display name to file key (Windows ThemeHandoff.Slugify): lowercase ASCII letters and
    /// digits, runs of anything else one hyphen, at most 40; "theme" when nothing survives.
    static func slug(_ name: String) -> String {
        var out = ""
        var pendingHyphen = false
        for s in name.trimmingCharacters(in: .whitespaces).lowercased().unicodeScalars {
            if ("a"..."z").contains(s) || ("0"..."9").contains(s) {
                if pendingHyphen && !out.isEmpty { out.append("-") }
                pendingHyphen = false
                out.unicodeScalars.append(s)
            } else {
                pendingHyphen = true
            }
        }
        if out.count > 40 {
            out = String(out.prefix(40))
            while out.hasSuffix("-") { out.removeLast() }
        }
        return out.isEmpty ? "theme" : out
    }

    // MARK: - Helpers

    private static func error(_ field: String, _ message: String) -> ThemeFinding {
        ThemeFinding(level: .error, field: field, message: message)
    }

    private static func warning(_ field: String, _ message: String) -> ThemeFinding {
        ThemeFinding(level: .warning, field: field, message: message)
    }

    /// The value unless it is absent or JSON null.
    static func present(_ v: Any?) -> Any? {
        guard let v, !(v is NSNull) else { return nil }
        return v
    }

    /// JSON true or false (an NSNumber holding a CFBoolean), not a number.
    static func isBool(_ v: Any?) -> Bool {
        guard let n = v as? NSNumber else { return false }
        return CFGetTypeID(n) == CFBooleanGetTypeID()
    }

    /// Any JSON number as a Double; booleans, strings and nulls are not numbers.
    static func number(_ v: Any?) -> Double? {
        guard let n = v as? NSNumber, !isBool(n) else { return nil }
        return n.doubleValue
    }

    /// .NET's "0.0" / "0.00": fixed places, half away from zero.
    static func fmt(_ v: Double, _ places: Int) -> String {
        let q = pow(10, Double(places))
        let r = (abs(v) * q + 0.5).rounded(.down) / q
        let s = String(format: "%.\(places)f", r)
        return v < 0 && r != 0 ? "-" + s : s
    }

    /// .NET's "0.##": up to `places`, trailing zeros dropped.
    static func fmtTrim(_ v: Double, _ places: Int) -> String {
        var t = fmt(v, places)
        if t.contains(".") {
            while t.hasSuffix("0") { t.removeLast() }
            if t.hasSuffix(".") { t.removeLast() }
        }
        return t
    }

    static func describe(_ v: Any?) -> String {
        guard let v = present(v) else { return "null" }
        if let s = v as? String { return "\"\(s)\"" }
        if isBool(v) { return (v as? Bool ?? false) ? "true" : "false" }
        if let n = number(v) { return n == n.rounded() && abs(n) < 1e15 ? String(Int(n)) : String(n) }
        if v is [String: Any] { return "an object" }
        if v is [Any] { return "an array" }
        return "a value"
    }

    private static func checkRange(_ o: [String: Any], _ key: String, _ lo: Double, _ hi: Double, _ fallback: Double,
                                   _ findings: inout [ThemeFinding], label: String? = nil) -> Double {
        let label = label ?? key
        guard let raw = present(o[key]) else { return fallback }
        guard let value = number(raw) else {
            findings.append(error(label, "\(label) must be a number between \(fmtTrim(lo, 2)) and \(fmtTrim(hi, 2)), got \(describe(raw))."))
            return fallback
        }
        if value.isNaN || value < lo || value > hi {
            findings.append(error(label, "\(label) must be between \(fmtTrim(lo, 2)) and \(fmtTrim(hi, 2)), got \(fmtTrim(value, 3))."))
            return fallback
        }
        return value
    }

    private static func checkOptionalHex(_ o: [String: Any], _ key: String, _ findings: inout [ThemeFinding],
                                         label: String? = nil) {
        let label = label ?? key
        guard let raw = present(o[key]) else { return }
        if parseHex(raw as? String) == nil {
            findings.append(error(label, "\(label) must be a #rrggbb color or null, got \(describe(raw))."))
        }
    }
}
