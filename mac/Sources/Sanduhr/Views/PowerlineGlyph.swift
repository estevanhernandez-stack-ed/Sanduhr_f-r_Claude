import AppKit
import SwiftUI

/// Powerline's arrows (item 63b) live in the Private Use Area: only Nerd Fonts have them, so the
/// app's fonts draw boxes. Sanduhr draws them itself wherever it shows them: menus, chips and the
/// preview. U+E0A0 (Powerline's branch mark) reads as ⎇.
enum PowerlineGlyph {
    enum Shape: Equatable, Sendable {
        /// U+E0B0: a filled right-pointing triangle.
        case arrow
        /// U+E0B1: a thin right chevron.
        case thin
        /// U+E0B2: a filled left-pointing triangle.
        case leftArrow
        /// U+E0B3: a thin left chevron.
        case leftThin
    }

    static let branch: Unicode.Scalar = "\u{E0A0}"

    static func shape(_ scalar: Unicode.Scalar) -> Shape? {
        switch scalar.value {
        case 0xE0B0: .arrow
        case 0xE0B1: .thin
        case 0xE0B2: .leftArrow
        case 0xE0B3: .leftThin
        default: nil
        }
    }

    /// The size a glyph takes next to caption-sized monospaced text: one cell.
    static let cell = CGSize(width: 7, height: 13)

    /// The glyph drawn in `fg` over `bg` (nil: clear). `template` images take the text's color.
    static func image(_ shape: Shape, fg: NSColor = .black, bg: NSColor? = nil, size: CGSize = cell,
                      template: Bool = false) -> NSImage {
        let image = NSImage(size: size, flipped: false) { rect in
            if let bg {
                bg.setFill()
                rect.fill()
            }
            let path = NSBezierPath()
            let w = rect.width, h = rect.height
            switch shape {
            case .arrow, .thin:
                path.move(to: NSPoint(x: 0, y: h))
                path.line(to: NSPoint(x: w, y: h / 2))
                path.line(to: NSPoint(x: 0, y: 0))
            case .leftArrow, .leftThin:
                path.move(to: NSPoint(x: w, y: h))
                path.line(to: NSPoint(x: 0, y: h / 2))
                path.line(to: NSPoint(x: w, y: 0))
            }
            if shape == .arrow || shape == .leftArrow {
                path.close()
                fg.setFill()
                path.fill()
            } else {
                path.lineWidth = 1.2
                fg.setStroke()
                path.stroke()
            }
            return true
        }
        image.isTemplate = template
        return image
    }

    /// `text` as one Text: powerline arrows drawn (in the text's color), the branch mark as ⎇.
    static func text(_ text: String) -> Text {
        var out = Text("")
        var buffer = ""
        for scalar in text.unicodeScalars {
            if let shape = shape(scalar) {
                out = out + Text(buffer) + Text(Image(nsImage: image(shape, template: true))).baselineOffset(-2)
                buffer = ""
            } else if scalar == branch {
                buffer += "\u{2387}"
            } else {
                buffer.unicodeScalars.append(scalar)
            }
        }
        return out + Text(buffer)
    }

    /// A menu item's icon for a separator or join glyph drawn here, nil for one fonts have.
    static func menuImage(_ shape: Shape?) -> NSImage? {
        shape.map { image($0, size: CGSize(width: 9, height: 14), template: true) }
    }

    /// In words, for tooltips.
    static let explanation = "Powerline arrow: a solid triangle pointing right, colored like the segment before it over the one after it. Powerline thin arrow: a thin chevron pointing right. Both need a Nerd Font in your terminal; without one they show as boxes."
}

extension StatuslineSeparator {
    var shape: PowerlineGlyph.Shape? {
        switch self {
        case .powerline: .arrow
        case .powerlineThin: .thin
        default: nil
        }
    }
}

extension StatuslineJoinGlyph {
    var shape: PowerlineGlyph.Shape? {
        switch self {
        case .powerline: .arrow
        case .powerlineThin: .thin
        default: nil
        }
    }
}

/// A menu item: the drawn glyph, when it is one only Nerd Fonts have, and the title.
struct GlyphMenuLabel: View {
    let title: String
    let shape: PowerlineGlyph.Shape?

    var body: some View {
        if let image = PowerlineGlyph.menuImage(shape) {
            Label { Text(title) } icon: { Image(nsImage: image) }
        } else {
            Text(title)
        }
    }
}

extension ANSIText {
    /// A piece of terminal text: styled text, or a powerline glyph drawn in its run's colors.
    enum Piece: Equatable {
        case text(String, Style)
        case glyph(PowerlineGlyph.Shape, Style)
    }

    /// `text`'s runs with each powerline glyph cut out as its own piece; the branch mark reads ⎇.
    static func pieces(_ text: String) -> [Piece] {
        var out: [Piece] = []
        for run in runs(text) {
            var buffer = ""
            for scalar in run.text.unicodeScalars {
                if let shape = PowerlineGlyph.shape(scalar) {
                    if !buffer.isEmpty { out.append(.text(buffer, run.style)) }
                    buffer = ""
                    out.append(.glyph(shape, run.style))
                } else if scalar == PowerlineGlyph.branch {
                    buffer += "\u{2387}"
                } else {
                    buffer.unicodeScalars.append(scalar)
                }
            }
            if !buffer.isEmpty { out.append(.text(buffer, run.style)) }
        }
        return out
    }

    /// Terminal text as one Text, powerline glyphs drawn: the arrow in the run's foreground over
    /// its background, as a terminal with a Nerd Font draws it.
    static func text(_ text: String) -> Text {
        var out = Text("")
        for piece in pieces(text) {
            switch piece {
            case .text(let s, let style):
                out = out + Text(attributed(s, style: style))
            case .glyph(let shape, let style):
                var fg = style.fg, bg = style.bg
                if style.inverse { swap(&fg, &bg) }
                let ns: (RGB) -> NSColor = { NSColor(srgbRed: $0.r, green: $0.g, blue: $0.b, alpha: 1) }
                out = out + Text(Image(nsImage: PowerlineGlyph.image(shape, fg: fg.map(ns) ?? NSColor(white: 0.9, alpha: 1),
                                                                     bg: bg.map(ns)))).baselineOffset(-2)
            }
        }
        return out
    }

    /// One run's text styled, as `attributed` styles a run.
    static func attributed(_ text: String, style: Style) -> AttributedString {
        attributed(rebuild(text, style))
    }

    /// `text` with `style`'s SGR in front, so `attributed` reads it back.
    private static func rebuild(_ text: String, _ s: Style) -> String {
        var codes: [String] = []
        if s.bold { codes.append("1") }
        if s.dim { codes.append("2") }
        if s.italic { codes.append("3") }
        if s.underline { codes.append("4") }
        if s.inverse { codes.append("7") }
        if s.strike { codes.append("9") }
        func rgb(_ c: RGB) -> String { "\(Int((c.r * 255).rounded()));\(Int((c.g * 255).rounded()));\(Int((c.b * 255).rounded()))" }
        if let fg = s.fg { codes.append("38;2;" + rgb(fg)) }
        if let bg = s.bg { codes.append("48;2;" + rgb(bg)) }
        return codes.isEmpty ? text : "\u{1B}[\(codes.joined(separator: ";"))m" + text
    }
}
