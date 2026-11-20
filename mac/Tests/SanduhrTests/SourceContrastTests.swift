import Foundation
import Testing
@testable import Sanduhr

/// The source colors read on every built-in theme's card and bar track, and the values are pinned.
/// Windows' AgentSourceColorTests is the parity reference: each default against the theme's glass and
/// its bar track at the non-text floor (3:1). Match Desk has no card, and its track is the Desk ink at
/// 22 percent over the desktop, so it is checked over a dark desktop, the one the default white ink is
/// made for; a light desktop is why each color is changeable.
@Suite("Source colors")
struct SourceContrastTests {
    /// WCAG's floor for graphical objects (Windows `ThemeContrast.NonTextMin`).
    static let nonTextMin = 3.0
    /// `DeskThemeMapping.palette`: `barBg` is the middle ink stop at this opacity.
    static let matchDeskTrackOpacity = 0.22

    private func ratio(_ hex: String, _ under: ThemeLint.RGB) throws -> Double {
        let c = try #require(ThemeLint.parseHex(hex))
        return ThemeLint.contrast(ThemeLint.luminance(c), ThemeLint.luminance(under))
    }

    private func rgb(_ hex: String) throws -> ThemeLint.RGB { try #require(ThemeLint.parseHex(hex)) }

    @Test func valuesArePinned() {
        #expect(AgentSource.claude.defaultColor == "#E0875A")
        #expect(AgentSource.codex.defaultColor == "#2FB5A3")
        #expect(AgentSource.antigravity.defaultColor == "#7B8CF0")
    }

    @Test func defaultsAreNotTheBrandsExactHexes() {
        #expect(AgentSource.claude.defaultColor.lowercased() != "#d97757")
        #expect(AgentSource.codex.defaultColor.lowercased() != "#10a37f")
    }

    @Test func eachDefaultReadsOnEveryBuiltInGlass() throws {
        var failures: [String] = []
        for (key, json) in try builtInThemesFixture() {
            let glass = try rgb(try #require(json["glass"] as? String))
            for source in AgentSource.allCases {
                let r = try ratio(source.defaultColor, glass)
                if r < Self.nonTextMin { failures.append("\(source) on \(key) glass: \(r)") }
            }
        }
        #expect(failures.isEmpty, "\(failures)")
    }

    @Test func eachDefaultReadsOnEveryBuiltInBarTrack() throws {
        var failures: [String] = []
        for (key, json) in try builtInThemesFixture() {
            let track = try rgb(try #require(json["bar_bg"] as? String))
            for source in AgentSource.allCases {
                let r = try ratio(source.defaultColor, track)
                if r < Self.nonTextMin { failures.append("\(source) on \(key) track: \(r)") }
            }
        }
        #expect(failures.isEmpty, "\(failures)")
    }

    @Test func theFixtureIsTheSevenBuiltIns() throws {
        #expect(try builtInThemesFixture().count == 7)
    }

    @Test func eachDefaultReadsOnMatchDesksTrack() throws {
        let ink = try rgb("#" + (DeskThemeMapping.inkHexes(DeskThemeMapping.fallbackInk).first ?? ""))
        let darkDesktop = ThemeLint.RGB(r: 0, g: 0, b: 0)
        let track = ThemeLint.composite(ink, Self.matchDeskTrackOpacity, darkDesktop)
        for source in AgentSource.allCases {
            let r = try ratio(source.defaultColor, track)
            #expect(r >= Self.nonTextMin, "\(source) on Match Desk's track: \(r)")
        }
    }

    @Test func theThreeDefaultsAreToldApart() throws {
        // Two sources on one bar: the colors must not blur into one another on the dark track.
        let colors = try AgentSource.allCases.map { try rgb($0.defaultColor) }
        for i in colors.indices {
            for j in colors.indices where j > i {
                let a = colors[i], b = colors[j]
                let dist = abs(a.r - b.r) + abs(a.g - b.g) + abs(a.b - b.b)
                #expect(dist > 0.5, "\(AgentSource.allCases[i]) and \(AgentSource.allCases[j]) are too alike")
            }
        }
    }
}
