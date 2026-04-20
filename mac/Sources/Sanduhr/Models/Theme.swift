import SwiftUI

/// A named palette + rendering dials. Built-ins live in `ThemeRegistry`;
/// users can add JSON files to ~/Library/Application Support/Sanduhr/themes/
/// which are merged at app bootstrap.
struct Theme: Identifiable, Hashable {
    let id: String           // "obsidian", "626-labs", "my-custom"
    let displayName: String  // shown in the theme strip
    let palette: Palette

    struct Palette: Hashable {
        // Base colors.
        let bg, glass, titleBg, border: Color
        let text, textSecondary, textDim, textMuted: Color
        let accent, barBg, footerBg, paceMarker, sparkline: Color
        /// Card background tuned for alpha-blending over the window's
        /// vibrancy layer. Most themes drop it slightly darker than `glass`
        /// so text contrast holds when the desktop shows through.
        let glassOnMica: Color

        // Window-level dials (apply once to the whole panel).
        /// How opaque the theme tint is over the window's vibrancy. Most
        /// themes let vibrancy dominate (~0.18); Matrix goes near-opaque so
        /// the desktop can't bleed through and wash the green out.
        let overlayOpacity: Double
        /// Font design for numeric readouts (percentages, countdowns).
        /// `.rounded` for most themes; `.monospaced` for Matrix so the
        /// digits feel like phosphor terminal output.
        let numericFontDesign: Font.Design

        // Per-card dials (ported from windows/src/sanduhr/themes.py).
        /// Card density over vibrancy. 0.78–0.90 is the readable range;
        /// Matrix pushes to 1.0 to kill translucency.
        let glassAlpha: Double
        /// Border opacity for each card.
        let borderAlpha: Double
        /// Optional accent-matched border color; nil falls back to `border`.
        let borderTint: Color?
        /// Soft glow around the percentage readout.
        let accentBloom: AccentBloom
        /// Optional 1-px top-edge light catch on cards (nil for restrained
        /// themes like Obsidian and Matrix).
        let innerHighlight: InnerHighlight?
        /// Corner radius in points. Default 10; Matrix drops to 2 for a
        /// sharper CRT/terminal feel.
        let cardCornerRadius: CGFloat

        // v2.0.4 dials (ported from windows/src/sanduhr/themes.py).
        /// Opacity of the always-on pace ghost tick. Windows settled on 1.0
        /// (full opacity, paceMarker color) after a round of UX tuning; set
        /// lower via user JSON for a more subliminal variant.
        let ghostAlpha: Double
        /// Breathing-glass shimmer period on the bar fill, in milliseconds.
        /// 2800 feels like a calm inhale/exhale; Matrix runs at 1800 so the
        /// CRT phosphor pulse feels more alive.
        let breathPeriodMs: Int

        struct AccentBloom: Hashable {
            let blur: CGFloat
            let alpha: Double
        }

        struct InnerHighlight: Hashable {
            let color: Color
            let alpha: Double
        }
    }
}

/// All themes available in this session — built-ins plus any loaded from
/// the user's App Support themes directory. Mutable so `load_user_themes()`
/// (B.3) can append at bootstrap.
enum ThemeRegistry {
    private static var registered: [Theme] = builtIn

    static var themes: [Theme] { registered }

    /// Fallback when the user's saved theme id no longer resolves.
    static var `default`: Theme {
        registered.first(where: { $0.id == "obsidian" }) ?? registered[0]
    }

    static func theme(id: String) -> Theme? {
        registered.first(where: { $0.id == id })
    }

    /// Add or replace a theme by id. Later calls win (user themes override
    /// built-ins that share a key).
    static func register(_ theme: Theme) {
        if let i = registered.firstIndex(where: { $0.id == theme.id }) {
            registered[i] = theme
        } else {
            registered.append(theme)
        }
    }

    // MARK: Built-ins (port of sanduhr.py:54-90 + windows themes.py dials)

    private static let builtIn: [Theme] = [
        Theme(
            id: "obsidian", displayName: "Obsidian",
            palette: .init(
                bg: .hex("0d0d0d"), glass: .hex("1c1c1c"),
                titleBg: .hex("161616"), border: .hex("333333"),
                text: .hex("e8e4dc"), textSecondary: .hex("b8b4ac"),
                textDim: .hex("777777"), textMuted: .hex("555555"),
                accent: .hex("6c63ff"), barBg: .hex("2a2a2a"),
                footerBg: .hex("111111"), paceMarker: .hex("ff6b6b"),
                sparkline: .hex("6c63ff"),
                glassOnMica: .hex("1a1a1c"),
                overlayOpacity: 0.18,
                numericFontDesign: .rounded,
                glassAlpha: 0.85,
                borderAlpha: 0.30,
                borderTint: nil,
                accentBloom: .init(blur: 4, alpha: 0.35),
                innerHighlight: nil,
                cardCornerRadius: 10,
                ghostAlpha: 1.0, breathPeriodMs: 2800)),
        Theme(
            id: "aurora", displayName: "Aurora",
            palette: .init(
                bg: .hex("0a0f1a"), glass: .hex("161e30"),
                titleBg: .hex("0f172a"), border: .hex("334155"),
                text: .hex("e2e8f0"), textSecondary: .hex("94a3b8"),
                textDim: .hex("64748b"), textMuted: .hex("475569"),
                accent: .hex("38bdf8"), barBg: .hex("1e293b"),
                footerBg: .hex("0c1220"), paceMarker: .hex("f472b6"),
                sparkline: .hex("38bdf8"),
                glassOnMica: .hex("141d2e"),
                overlayOpacity: 0.18,
                numericFontDesign: .rounded,
                glassAlpha: 0.80,
                borderAlpha: 0.50,
                borderTint: .hex("38bdf8"),
                accentBloom: .init(blur: 6, alpha: 0.55),
                innerHighlight: .init(color: .hex("38bdf8"), alpha: 0.20),
                cardCornerRadius: 10,
                ghostAlpha: 1.0, breathPeriodMs: 2800)),
        Theme(
            id: "ember", displayName: "Ember",
            palette: .init(
                bg: .hex("1a0a0a"), glass: .hex("261414"),
                titleBg: .hex("1f0e0e"), border: .hex("442222"),
                text: .hex("f5e6e0"), textSecondary: .hex("d4a89c"),
                textDim: .hex("8b6b60"), textMuted: .hex("6b4b40"),
                accent: .hex("f97316"), barBg: .hex("2d1a1a"),
                footerBg: .hex("150808"), paceMarker: .hex("fbbf24"),
                sparkline: .hex("f97316"),
                glassOnMica: .hex("211010"),
                overlayOpacity: 0.18,
                numericFontDesign: .rounded,
                glassAlpha: 0.82,
                borderAlpha: 0.40,
                borderTint: .hex("f97316"),
                accentBloom: .init(blur: 6, alpha: 0.55),
                innerHighlight: .init(color: .hex("f97316"), alpha: 0.18),
                cardCornerRadius: 10,
                ghostAlpha: 1.0, breathPeriodMs: 2800)),
        Theme(
            id: "mint", displayName: "Mint",
            palette: .init(
                bg: .hex("0a1a14"), glass: .hex("122a1e"),
                titleBg: .hex("0c1f14"), border: .hex("22543d"),
                text: .hex("e0f5ec"), textSecondary: .hex("9cd4b8"),
                textDim: .hex("5a9a78"), textMuted: .hex("3a7a58"),
                accent: .hex("34d399"), barBg: .hex("163020"),
                footerBg: .hex("081510"), paceMarker: .hex("f472b6"),
                sparkline: .hex("34d399"),
                glassOnMica: .hex("0e2419"),
                overlayOpacity: 0.18,
                numericFontDesign: .rounded,
                glassAlpha: 0.78,
                borderAlpha: 0.45,
                borderTint: .hex("34d399"),
                accentBloom: .init(blur: 4, alpha: 0.35),
                innerHighlight: .init(color: .hex("34d399"), alpha: 0.22),
                cardCornerRadius: 10,
                ghostAlpha: 1.0, breathPeriodMs: 2800)),
        Theme(
            id: "626-labs", displayName: "626 Labs",
            // 626 Labs brand — navy + cyan + magenta. Ported from
            // docs/themes/examples/626-labs.json on the windows-native branch.
            palette: .init(
                bg: .hex("0f182b"), glass: .hex("1a2540"),
                titleBg: .hex("131a2a"), border: .hex("2a3a5c"),
                text: .hex("e8f2ff"), textSecondary: .hex("a8c2d9"),
                textDim: .hex("6a849e"), textMuted: .hex("4a5f7a"),
                accent: .hex("3bb4d9"), barBg: .hex("1a2540"),
                footerBg: .hex("0a121f"), paceMarker: .hex("e13aa0"),
                sparkline: .hex("3bb4d9"),
                glassOnMica: .hex("131d31"),
                overlayOpacity: 0.18,
                numericFontDesign: .rounded,
                glassAlpha: 0.84,
                borderAlpha: 0.50,
                borderTint: .hex("3bb4d9"),
                accentBloom: .init(blur: 6, alpha: 0.60),
                innerHighlight: .init(color: .hex("7ae0f5"), alpha: 0.22),
                cardCornerRadius: 10,
                ghostAlpha: 1.0, breathPeriodMs: 2800)),
        Theme(
            id: "matrix", displayName: "Matrix",
            // Matrix opts out of vibrancy — overlayOpacity pushes near 1.0
            // and cards go fully opaque so the phosphor stays punchy. Sharp
            // 2-pt corners give the CRT/terminal feel.
            palette: .init(
                bg: .hex("020605"), glass: .hex("061a0c"),
                titleBg: .hex("020a05"), border: .hex("0f3a1a"),
                text: .hex("39ff7c"), textSecondary: .hex("20e060"),
                textDim: .hex("18a048"), textMuted: .hex("0f6030"),
                accent: .hex("39ff7c"), barBg: .hex("0a1a0f"),
                footerBg: .hex("010604"), paceMarker: .hex("ff0a5c"),
                sparkline: .hex("39ff7c"),
                glassOnMica: .hex("0a140a"),
                overlayOpacity: 0.82,
                numericFontDesign: .monospaced,
                glassAlpha: 1.0,
                borderAlpha: 0.50,
                borderTint: .hex("39ff7c"),
                accentBloom: .init(blur: 4, alpha: 0.60),
                innerHighlight: nil,
                cardCornerRadius: 2,
                ghostAlpha: 1.0, breathPeriodMs: 1800)),
        Theme(
            id: "blueprint", displayName: "Blueprint",
            palette: .init(
                bg: .hex("1a1625"), glass: .hex("2a2438"),
                titleBg: .hex("15121e"), border: .hex("3a324d"),
                text: .hex("e0e2f5"), textSecondary: .hex("a2a6cc"),
                textDim: .hex("6a6e99"), textMuted: .hex("484b70"),
                accent: .hex("4dffc4"), barBg: .hex("1f1a2e"),
                footerBg: .hex("100d16"), paceMarker: .hex("ff4d88"),
                sparkline: .hex("4dffc4"),
                glassOnMica: .hex("241f30"),
                overlayOpacity: 0.18,
                numericFontDesign: .rounded,
                glassAlpha: 0.85,
                borderAlpha: 0.70,
                borderTint: .hex("4dffc4"),
                accentBloom: .init(blur: 8, alpha: 0.65),
                innerHighlight: .init(color: .hex("4dffc4"), alpha: 0.15),
                cardCornerRadius: 10,
                ghostAlpha: 1.0, breathPeriodMs: 2800)),
    ]
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
