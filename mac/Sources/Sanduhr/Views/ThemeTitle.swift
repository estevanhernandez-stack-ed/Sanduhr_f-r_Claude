import SwiftUI

/// A theme's title style (item 65d): the widget title drawn in the theme's `title_ink` gradient and
/// `title_style` letters. Themes without them draw the title as before.
enum ThemeTitle {
    static let text = "Sanduhr"

    /// `title_ink` as gradient stops: 2 to 4 `#rrggbb` colors, else nil (drawn in `text`).
    static func stops(_ hexes: [String]?) -> [Color]? {
        guard let hexes, (2...4).contains(hexes.count),
              hexes.allSatisfy({ ThemeLint.parseHex($0) != nil }) else { return nil }
        return hexes.map { Color.hex($0) }
    }

    /// The characters drawn: the Unicode styles' letters, else the title as written (the
    /// typographic styles are drawn by the font).
    static func characters(_ style: LetterStyle?) -> String {
        switch style {
        case .sans, .mono, .doubleStruck, .script, .fraktur: LetterMap.map(text, style)
        default: text
        }
    }

    /// The weight: semibold as always, bold for the bold styles.
    static func weight(_ style: LetterStyle?) -> Font.Weight {
        style == .bold || style == .boldItalic ? .bold : .semibold
    }

    static func italic(_ style: LetterStyle?) -> Bool { style == .italic || style == .boldItalic }

    /// The Unicode styles draw in the system font, which has the letters.
    static func usesSystemFont(_ style: LetterStyle?) -> Bool { characters(style) != text }
}

/// The widget's title in the theme's ink and letters.
struct ThemeTitleText: View {
    let palette: Theme.Palette
    let size: CGFloat

    var body: some View {
        let style = palette.titleStyle
        Text(ThemeTitle.characters(style))
            .font(font(style))
            .italic(ThemeTitle.italic(style))
            .foregroundStyle(ink)
            .accessibilityLabel(ThemeTitle.text)
    }

    private func font(_ style: LetterStyle?) -> Font {
        let weight = ThemeTitle.weight(style)
        let base: Font = ThemeTitle.usesSystemFont(style)
            ? .system(size: size, weight: weight)
            : .app(size: size, weight: weight, design: .rounded)
        return style == .smallCaps ? base.smallCaps() : base
    }

    private var ink: AnyShapeStyle {
        if let stops = palette.titleInk {
            return AnyShapeStyle(LinearGradient(colors: stops, startPoint: .leading, endPoint: .trailing))
        }
        return AnyShapeStyle(palette.text)
    }
}
