import SwiftUI

/// The five theme palettes from sanduhr.py:54-90. Hex values copied verbatim
/// so the Mac version matches the Python widget pixel-for-pixel.
enum Theme: String, CaseIterable, Codable, Identifiable {
    case obsidian, aurora, ember, mint, matrix

    var id: String { rawValue }

    var displayName: String {
        switch self {
        case .obsidian: return "Obsidian"
        case .aurora:   return "Aurora"
        case .ember:    return "Ember"
        case .mint:     return "Mint"
        case .matrix:   return "Matrix"
        }
    }

    struct Palette {
        let bg, glass, titleBg, border: Color
        let text, textSecondary, textDim, textMuted: Color
        let accent, barBg, footerBg, paceMarker, sparkline: Color
        /// How opaque the theme tint is over the window's vibrancy. Most
        /// themes let vibrancy dominate (~0.18); Matrix goes near-opaque so
        /// the desktop can't bleed through and wash the green out.
        let overlayOpacity: Double
        /// Same idea for the secondary glass tint inside each tier card.
        let cardTintOpacity: Double
        /// Font design for numeric readouts (percentages, countdowns).
        /// `.rounded` for most themes; `.monospaced` for Matrix so the
        /// digits feel like phosphor terminal output.
        let numericFontDesign: Font.Design
        /// Extra glow radius on the accent stripe. Matrix gets a halo so
        /// the green stripe reads as lit phosphor.
        let accentGlow: CGFloat
    }

    var palette: Palette {
        // Defaults for the translucent-glass themes.
        let standardOverlay: Double    = 0.18
        let standardCardTint: Double   = 0.14
        let standardDesign: Font.Design = .rounded

        switch self {
        case .obsidian:
            return .init(
                bg: .hex("0d0d0d"), glass: .hex("1c1c1c"),
                titleBg: .hex("161616"), border: .hex("333333"),
                text: .hex("e8e4dc"), textSecondary: .hex("b8b4ac"),
                textDim: .hex("777777"), textMuted: .hex("555555"),
                accent: .hex("6c63ff"), barBg: .hex("2a2a2a"),
                footerBg: .hex("111111"), paceMarker: .hex("ff6b6b"),
                sparkline: .hex("6c63ff"),
                overlayOpacity: standardOverlay,
                cardTintOpacity: standardCardTint,
                numericFontDesign: standardDesign,
                accentGlow: 0)
        case .aurora:
            return .init(
                bg: .hex("0a0f1a"), glass: .hex("161e30"),
                titleBg: .hex("0f172a"), border: .hex("334155"),
                text: .hex("e2e8f0"), textSecondary: .hex("94a3b8"),
                textDim: .hex("64748b"), textMuted: .hex("475569"),
                accent: .hex("38bdf8"), barBg: .hex("1e293b"),
                footerBg: .hex("0c1220"), paceMarker: .hex("f472b6"),
                sparkline: .hex("38bdf8"),
                overlayOpacity: standardOverlay,
                cardTintOpacity: standardCardTint,
                numericFontDesign: standardDesign,
                accentGlow: 0)
        case .ember:
            return .init(
                bg: .hex("1a0a0a"), glass: .hex("261414"),
                titleBg: .hex("1f0e0e"), border: .hex("442222"),
                text: .hex("f5e6e0"), textSecondary: .hex("d4a89c"),
                textDim: .hex("8b6b60"), textMuted: .hex("6b4b40"),
                accent: .hex("f97316"), barBg: .hex("2d1a1a"),
                footerBg: .hex("150808"), paceMarker: .hex("fbbf24"),
                sparkline: .hex("f97316"),
                overlayOpacity: standardOverlay,
                cardTintOpacity: standardCardTint,
                numericFontDesign: standardDesign,
                accentGlow: 0)
        case .mint:
            return .init(
                bg: .hex("0a1a14"), glass: .hex("122a1e"),
                titleBg: .hex("0c1f14"), border: .hex("22543d"),
                text: .hex("e0f5ec"), textSecondary: .hex("9cd4b8"),
                textDim: .hex("5a9a78"), textMuted: .hex("3a7a58"),
                accent: .hex("34d399"), barBg: .hex("163020"),
                footerBg: .hex("081510"), paceMarker: .hex("f472b6"),
                sparkline: .hex("34d399"),
                overlayOpacity: standardOverlay,
                cardTintOpacity: standardCardTint,
                numericFontDesign: standardDesign,
                accentGlow: 0)
        case .matrix:
            // Matrix is deliberately opaque + phosphor. Still glass — still
            // a Mac panel with vibrancy behind it — but the tint wins over
            // the desktop so the blacks read black and the greens punch.
            return .init(
                bg: .hex("020605"), glass: .hex("061a0c"),
                titleBg: .hex("020a05"), border: .hex("0f3a1a"),
                text: .hex("39ff7c"), textSecondary: .hex("20e060"),
                textDim: .hex("18a048"), textMuted: .hex("0f6030"),
                accent: .hex("39ff7c"), barBg: .hex("0a1a0f"),
                footerBg: .hex("010604"), paceMarker: .hex("ff0a5c"),
                sparkline: .hex("39ff7c"),
                overlayOpacity: 0.82,
                cardTintOpacity: 0.55,
                numericFontDesign: .monospaced,
                accentGlow: 6)
        }
    }
}

extension Color {
    /// Construct a Color from an RGB hex string ("6c63ff" or "#6c63ff").
    static func hex(_ s: String) -> Color {
        var h = s.hasPrefix("#") ? String(s.dropFirst()) : s
        if h.count == 3 { h = h.map { "\($0)\($0)" }.joined() }
        guard h.count == 6, let v = UInt32(h, radix: 16) else { return .gray }
        return Color(
            red:   Double((v >> 16) & 0xFF) / 255,
            green: Double((v >>  8) & 0xFF) / 255,
            blue:  Double( v        & 0xFF) / 255)
    }
}

/// Percentage-to-color rule from sanduhr.py:93-97.
func usageColor(_ pct: Double) -> Color {
    if pct < 50 { return .hex("4ade80") }
    if pct < 75 { return .hex("facc15") }
    if pct < 90 { return .hex("fb923c") }
    return .hex("f87171")
}
