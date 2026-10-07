import SwiftUI

/// Terminal output (SGR colors and styles) as styled runs, for the Combine preview (item 63).
/// Handles what statuslines print: SGR 0-9 and their resets, the 16 colors (normal and bright),
/// 256-color and truecolor foregrounds and backgrounds. Other CSI sequences, OSC (OSC 8 links
/// keep their text) and stray control characters are dropped.
enum ANSIText {
    struct RGB: Equatable, Sendable {
        let r: Double, g: Double, b: Double
        init(_ r: Int, _ g: Int, _ b: Int) {
            self.r = Double(r) / 255
            self.g = Double(g) / 255
            self.b = Double(b) / 255
        }
    }

    struct Style: Equatable, Sendable {
        var fg: RGB?
        var bg: RGB?
        var bold = false
        var dim = false
        var italic = false
        var underline = false
        var inverse = false
        var strike = false
    }

    struct Run: Equatable, Sendable {
        var text: String
        var style: Style
    }

    /// The 16 basic colors (xterm's defaults).
    static let basic: [RGB] = [
        RGB(0, 0, 0), RGB(205, 0, 0), RGB(0, 205, 0), RGB(205, 205, 0),
        RGB(0, 0, 238), RGB(205, 0, 205), RGB(0, 205, 205), RGB(229, 229, 229),
        RGB(127, 127, 127), RGB(255, 0, 0), RGB(0, 255, 0), RGB(255, 255, 0),
        RGB(92, 92, 255), RGB(255, 0, 255), RGB(0, 255, 255), RGB(255, 255, 255),
    ]

    /// One of the 256 colors.
    static func palette(_ n: Int) -> RGB? {
        switch n {
        case 0..<16: return basic[n]
        case 16..<232:
            let i = n - 16
            let level = [0, 95, 135, 175, 215, 255]
            return RGB(level[i / 36], level[(i / 6) % 6], level[i % 6])
        case 232..<256:
            let v = 8 + (n - 232) * 10
            return RGB(v, v, v)
        default: return nil
        }
    }

    /// `text` as styled runs, escapes taken out.
    static func runs(_ text: String) -> [Run] {
        var out: [Run] = []
        var style = Style()
        var buffer = ""
        let s = Array(text.unicodeScalars)
        var i = 0
        func flush() {
            guard !buffer.isEmpty else { return }
            if let last = out.last, last.style == style {
                out[out.count - 1].text += buffer
            } else {
                out.append(Run(text: buffer, style: style))
            }
            buffer = ""
        }
        while i < s.count {
            let c = s[i]
            if c == "\u{1B}", i + 1 < s.count {
                let next = s[i + 1]
                if next == "[" {
                    // CSI: parameters, intermediates, one final byte.
                    var j = i + 2
                    var params = ""
                    while j < s.count, (0x30...0x3F).contains(s[j].value) { params.unicodeScalars.append(s[j]); j += 1 }
                    while j < s.count, (0x20...0x2F).contains(s[j].value) { j += 1 }
                    guard j < s.count else { break }
                    if s[j] == "m" {
                        flush()
                        apply(params, to: &style)
                    }
                    i = j + 1
                    continue
                }
                if next == "]" {
                    // OSC: up to BEL or ST.
                    var j = i + 2
                    while j < s.count {
                        if s[j] == "\u{07}" { j += 1; break }
                        if s[j] == "\u{1B}", j + 1 < s.count, s[j + 1] == "\\" { j += 2; break }
                        j += 1
                    }
                    i = j
                    continue
                }
                i += 2
                continue
            }
            if c == "\n" || c == "\t" || c.value >= 0x20 && c.value != 0x7F {
                buffer.unicodeScalars.append(c)
            }
            i += 1
        }
        flush()
        return out
    }

    /// Applies one SGR parameter list.
    static func apply(_ params: String, to style: inout Style) {
        let codes = params.isEmpty ? [0] : params.split(separator: ";", omittingEmptySubsequences: false).map { Int($0) ?? 0 }
        var k = 0
        func extended() -> RGB? {
            guard k + 1 < codes.count else { return nil }
            if codes[k + 1] == 5, k + 2 < codes.count {
                defer { k += 2 }
                return palette(codes[k + 2])
            }
            if codes[k + 1] == 2, k + 4 < codes.count {
                defer { k += 4 }
                return RGB(min(255, codes[k + 2]), min(255, codes[k + 3]), min(255, codes[k + 4]))
            }
            return nil
        }
        while k < codes.count {
            let c = codes[k]
            switch c {
            case 0: style = Style()
            case 1: style.bold = true
            case 2: style.dim = true
            case 3: style.italic = true
            case 4: style.underline = true
            case 7: style.inverse = true
            case 9: style.strike = true
            case 22: style.bold = false; style.dim = false
            case 23: style.italic = false
            case 24: style.underline = false
            case 27: style.inverse = false
            case 29: style.strike = false
            case 30...37: style.fg = basic[c - 30]
            case 38: style.fg = extended()
            case 39: style.fg = nil
            case 40...47: style.bg = basic[c - 40]
            case 48: style.bg = extended()
            case 49: style.bg = nil
            case 90...97: style.fg = basic[c - 90 + 8]
            case 100...107: style.bg = basic[c - 100 + 8]
            default: break
            }
            k += 1
        }
    }

    /// `text` styled for a SwiftUI `Text` in a monospaced font; uncolored text keeps the
    /// view's own color.
    static func attributed(_ text: String) -> AttributedString {
        var out = AttributedString()
        for run in runs(text) {
            var piece = AttributedString(run.text)
            let st = run.style
            var fg = st.fg, bg = st.bg
            if st.inverse { swap(&fg, &bg) }
            if st.inverse, fg == nil { fg = RGB(0, 0, 0) }
            if st.inverse, bg == nil { bg = RGB(229, 229, 229) }
            if let fg {
                piece.foregroundColor = Color(red: fg.r, green: fg.g, blue: fg.b).opacity(st.dim ? 0.6 : 1)
            } else if st.dim {
                piece.foregroundColor = Color.secondary
            }
            if let bg { piece.backgroundColor = Color(red: bg.r, green: bg.g, blue: bg.b) }
            var intent: InlinePresentationIntent = []
            if st.bold { intent.insert(.stronglyEmphasized) }
            if st.italic { intent.insert(.emphasized) }
            if st.strike { intent.insert(.strikethrough) }
            if !intent.isEmpty { piece.inlinePresentationIntent = intent }
            if st.underline { piece.underlineStyle = .single }
            out += piece
        }
        return out
    }
}
