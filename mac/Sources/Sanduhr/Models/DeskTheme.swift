import SwiftUI

/// Desk's look as the Match Desk theme reads it: the desk suite's `font`, `inkColor` (one hex,
/// or a comma list for a gradient) and `inkShadow`, with Desk's own defaults when unset.
struct DeskLook: Equatable {
    static let fontKey = "font"
    static let inkKey = "inkColor"
    static let shadowKey = "inkShadow"
    /// Every key Match Desk follows.
    static let keys = [fontKey, inkKey, shadowKey]

    var font: String = ""
    var ink: String = "ffffff"
    var shadow: Bool = true

    /// The font as the Desk draws it (DeskFont: EsteFont Pro unless one was picked and is installed).
    static func read(_ defaults: DefaultsStore, available: (String) -> Bool = DeskFont.isAvailable) -> DeskLook {
        DeskLook(font: DeskFont.resolve(defaults, available: available),
                 ink: defaults.object(forKey: inkKey) as? String ?? "ffffff",
                 shadow: defaults.object(forKey: shadowKey) as? Bool ?? true)
    }
}

/// The built-in "Match Desk" theme: the widget drawn in Desk's ink over the desktop. Text takes
/// the first ink stop (dimmer for secondary lines), accents and the pace tick the middle stop,
/// the bars the whole ink run left to right as Desk's meters do. Backgrounds, cards and borders
/// are clear. A limit at 75% or more keeps the standard usage colors (orange, then red) on its
/// bar and readout, so a warning never disappears into the ink.
enum DeskThemeMapping {
    static let id = "match-desk"
    static let name = "Match Desk"
    /// The ink when the spec has no valid color, Desk's default.
    static let fallbackInk = "ffffff"
    /// From here up a limit is drawn in the usage colors instead of the ink.
    static let warningLine = 75.0

    /// The theme for a Desk ink spec and shadow switch.
    static func theme(ink: String, shadow: Bool) -> Theme {
        Theme(id: id, displayName: name, palette: palette(ink: ink, shadow: shadow))
    }

    /// The registry's copy, drawn in Desk's default ink. The view model swaps in the live one.
    static let builtIn = theme(ink: fallbackInk, shadow: true)

    static func palette(ink: String, shadow: Bool) -> Theme.Palette {
        let stops = inkHexes(ink).map { Color.hex($0) }
        let first = stops[0]
        let middle = stops[stops.count / 2]
        return Theme.Palette(
            bg: .clear, glass: .clear, titleBg: .clear, border: .clear,
            text: first, textSecondary: first.opacity(0.85),
            textDim: first.opacity(0.7), textMuted: first.opacity(0.55),
            accent: middle, barBg: middle.opacity(0.22),
            footerBg: .clear, paceMarker: middle, sparkline: middle,
            glassOnMica: .clear,
            overlayOpacity: 0,
            numericFontDesign: .rounded,
            glassAlpha: 0,
            borderAlpha: 0,
            borderTint: nil,
            accentBloom: .init(blur: 0, alpha: 0),
            innerHighlight: nil,
            cardCornerRadius: 10,
            ghostAlpha: 1.0, breathPeriodMs: 2800,
            ink: .init(stops: stops, shadow: shadow))
    }

    /// The ink spec's colors as six-digit lowercase hex, always two or more: one color is
    /// doubled; entries that are not hex colors are skipped, and with none left the fallback
    /// (white) is used. Spaces around the commas and a leading # are fine.
    static func inkHexes(_ spec: String) -> [String] {
        var hexes = spec.split(separator: ",").compactMap { normalized(String($0)) }
        if hexes.isEmpty { hexes = [fallbackInk] }
        if hexes.count == 1 { hexes.append(hexes[0]) }
        return hexes
    }

    /// "#9AD7FF" is "9ad7ff", "fff" is "ffffff"; nil when it is not a 3 or 6 digit hex color.
    static func normalized(_ raw: String) -> String? {
        var h = raw.trimmingCharacters(in: .whitespaces).lowercased()
        if h.hasPrefix("#") { h.removeFirst() }
        guard h.count == 3 || h.count == 6, h.allSatisfy(\.isHexDigit) else { return nil }
        if h.count == 3 { h = h.map { "\($0)\($0)" }.joined() }
        return h
    }

    /// Whether a limit at this utilization is drawn in the ink (below the warning line).
    static func inkCarries(_ utilization: Double) -> Bool { utilization < warningLine }
}

/// The widget's drop shadow: the floating-panel shadow normally, a tight one in subtle mode,
/// and under Match Desk Desk's dark drop shadow (sized for the widget's smaller text), or none
/// when Desk's shadow switch is off.
struct WidgetShadow: Equatable {
    let opacity: Double
    let radius: CGFloat
    let y: CGFloat

    static func resolve(subtle: Bool, ink: Theme.Palette.Ink?) -> WidgetShadow {
        if let ink {
            return ink.shadow ? WidgetShadow(opacity: 0.45, radius: 4, y: 2)
                              : WidgetShadow(opacity: 0, radius: 0, y: 0)
        }
        return subtle ? WidgetShadow(opacity: 0.6, radius: 3, y: 1)
                      : WidgetShadow(opacity: 0.45, radius: 24, y: 10)
    }
}
