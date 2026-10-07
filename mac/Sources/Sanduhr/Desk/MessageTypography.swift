import AppKit
import SwiftUI

/// How a `{font:…}` style draws in Sanduhr's own text (item 65): typographically in the line's
/// font where it can (bold is the family's Bold face, italic a slant, small caps the capitals at
/// x-height size), and as Unicode letters (LetterMap) for the styles no font has a face for
/// (sans, mono, double-struck, script, fraktur), drawn in the system font, which has the glyphs.
enum MessageTypography {
    /// Italic's slant, in degrees.
    static let slantDegrees: Double = 12
    /// Small caps' size against the capitals when the font gives no x-height.
    static let smallCapsFallback: CGFloat = 0.72

    enum Face: Equatable {
        /// A family name, or a PostScript name from BundledFonts.face (EsteFont's Bold face).
        case custom(String)
        case system
    }

    struct Plan: Equatable {
        /// The characters drawn: the text, or its Unicode letters.
        var text: String
        var face: Face
        /// A heavier weight on top of the face, for a family without a Bold face of its own.
        var bold = false
        /// Degrees of slant; 0 is upright.
        var slant: Double = 0
        var smallCaps = false
    }

    static func plan(_ text: String, style: LetterStyle?, family: String) -> Plan {
        switch style {
        case nil:
            return Plan(text: text, face: .custom(family))
        case .bold, .boldItalic:
            let slant = style == .boldItalic ? slantDegrees : 0
            return Plan(text: text, face: .custom(boldFace(family) ?? family), bold: boldFace(family) == nil, slant: slant)
        case .italic:
            return Plan(text: text, face: .custom(family), slant: slantDegrees)
        case .smallCaps:
            return Plan(text: text, face: .custom(family), smallCaps: true)
        case .sans, .mono, .doubleStruck, .script, .fraktur:
            return Plan(text: LetterMap.map(text, style), face: .system)
        }
    }

    /// The family's own Bold face by BundledFonts' lookup (EsteFont's), nil when the lookup has
    /// none and the weight is synthesized instead.
    static func boldFace(_ family: String) -> String? {
        let face = BundledFonts.face(family, bold: true)
        return face == family ? nil : face
    }

    /// The text in runs for small caps: lowercase letters drawn as capitals at the small size.
    static func smallCapsRuns(_ text: String) -> [(text: String, small: Bool)] {
        var runs: [(text: String, small: Bool)] = []
        for ch in text {
            let small = ch.isLowercase
            let piece = small ? ch.uppercased() : String(ch)
            if let last = runs.last, last.small == small {
                runs[runs.count - 1].text += piece
            } else {
                runs.append((piece, small))
            }
        }
        return runs
    }

    /// Small caps' size against the capitals: the font's x-height over its cap height, kept
    /// between 0.6 and 0.85 so a font with odd metrics still reads as small caps.
    static func smallCapsScale(xHeight: CGFloat, capHeight: CGFloat) -> CGFloat {
        guard xHeight > 0, capHeight > 0 else { return smallCapsFallback }
        return min(0.85, max(0.6, xHeight / capHeight))
    }

    /// `smallCapsScale` for a family (by family name, else as a face name).
    static func smallCapsScale(family: String) -> CGFloat {
        let font = NSFontManager.shared.font(withFamily: family, traits: [], weight: 5, size: 100)
            ?? NSFont(name: family, size: 100)
        guard let font else { return smallCapsFallback }
        return smallCapsScale(xHeight: font.xHeight, capHeight: font.capHeight)
    }

    static func font(_ plan: Plan, size: CGFloat) -> Font {
        switch plan.face {
        case .system:
            return .system(size: size, weight: plan.bold ? .bold : .regular)
        case .custom(let name):
            return plan.bold ? .custom(name, size: size).weight(.bold) : .custom(name, size: size)
        }
    }

    /// The plan as one Text. `color`, when given, colors each source character by its index
    /// (nil there is clear): the sweep's light drawn over the line.
    static func text(_ plan: Plan, size: CGFloat, color: ((Int) -> Color?)? = nil) -> Text {
        let scale = plan.smallCaps ? smallCapsScale(family: familyName(plan.face)) : 1
        var out = AttributedString()
        var index = 0
        for ch in plan.text {
            let small = plan.smallCaps && ch.isLowercase
            var piece = AttributedString(small ? ch.uppercased() : String(ch))
            piece.font = font(plan, size: small ? size * scale : size)
            if let color { piece.foregroundColor = color(index) ?? .clear }
            out.append(piece)
            index += 1
        }
        return Text(out)
    }

    /// The text as drawn and read: tags gone, its style applied. For places that draw only
    /// characters, typographic styles stay as written (the view draws them).
    static func characters(_ parsed: MessageMarkup.Parsed) -> String {
        plan(parsed.text, style: parsed.effects.font, family: "").text
    }

    private static func familyName(_ face: Face) -> String {
        if case .custom(let name) = face { return name }
        return ""
    }
}

/// Italic's slant: a shear about the middle of the line, so it leans without drifting sideways.
struct Slant: GeometryEffect {
    var degrees: Double

    func effectValue(size: CGSize) -> ProjectionTransform {
        guard degrees != 0 else { return ProjectionTransform() }
        let k = CGFloat(tan(degrees * .pi / 180))
        return ProjectionTransform(CGAffineTransform(a: 1, b: 0, c: -k, d: 1, tx: k * size.height / 2, ty: 0))
    }
}

/// A message line on the notch: tags gone, its letter style applied, in the notch's ink.
struct NotchMessageText: View {
    let raw: String
    let font: String
    let size: CGFloat

    var body: some View {
        let parsed = MessageMarkup.parse(raw)
        let plan = MessageTypography.plan(parsed.text, style: parsed.effects.font, family: font)
        MessageTypography.text(plan, size: size)
            .modifier(Slant(degrees: plan.slant))
            .accessibilityLabel(parsed.text)
    }

    /// A notch place's text: the message with its letter style, anything else as before.
    @ViewBuilder
    static func line(_ text: String, content: NotchContent, message: String?, font: String, size: CGFloat) -> some View {
        if content == .message, let message {
            NotchMessageText(raw: message, font: font, size: size)
        } else {
            Text(text).font(.custom(font, size: size))
        }
    }
}
