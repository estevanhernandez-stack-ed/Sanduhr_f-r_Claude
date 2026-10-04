import Foundation
import SwiftUI
import Testing
@testable import Sanduhr

/// The theme lint (item 55): the Windows ThemeLintTests' cases against the Swift port, the Mac's
/// own fields, and the built-ins as the calibration set. Made-up themes only.

/// A clean theme (Obsidian's values), as the Windows tests' Clean().
func cleanTheme() -> [String: Any] {
    ["name": "Test",
     "bg": "#0d0d0d", "glass": "#1c1c1c", "glass_on_mica": "#1a1a1c",
     "title_bg": "#161616", "border": "#333333", "footer_bg": "#111111", "bar_bg": "#2a2a2a",
     "text": "#e8e4dc", "text_secondary": "#b8b4ac", "text_dim": "#777777", "text_muted": "#555555",
     "accent": "#6c63ff", "pace_marker": "#ff6b6b", "sparkline": "#6c63ff",
     "glass_alpha": 0.85, "border_alpha": 0.30]
}

private func fields(_ findings: [ThemeFinding], _ level: ThemeFinding.Level) -> [String] {
    findings.filter { $0.level == level }.map(\.field)
}

private func with(_ changes: [String: Any?]) -> [String: Any] {
    var t = cleanTheme()
    for (k, v) in changes { t[k] = v ?? NSNull() }
    return t
}

/// The built-ins as JSON (Fixtures/theme-builtins.json), shared with the Python tests.
func builtInThemesFixture() throws -> [String: [String: Any]] {
    let url = try #require(Bundle.module.url(forResource: "theme-builtins", withExtension: "json", subdirectory: "Fixtures"))
    let root = try #require(try JSONSerialization.jsonObject(with: Data(contentsOf: url)) as? [String: Any])
    return root.compactMapValues { $0 as? [String: Any] }
}

@Suite("Theme lint")
struct ThemeLintTests {
    // MARK: Calibration

    @Test func everyBuiltInLintsWithNoFindings() throws {
        let builtIns = try builtInThemesFixture()
        #expect(Set(builtIns.keys) == Set(ThemeRegistry.builtIn.map(\.id)).subtracting([DeskThemeMapping.id]))
        for (key, json) in builtIns {
            let findings = ThemeLint.lint(json)
            #expect(findings.isEmpty, "\(key): \(findings.map(\.message))")
        }
    }

    /// The fixture is the registry's built-ins, color for color.
    @Test func theFixtureMatchesTheRegistry() throws {
        for (key, json) in try builtInThemesFixture() {
            let theme = try #require(ThemeRegistry.builtIn.first { $0.id == key })
            let p = theme.palette
            let pairs: [(String, Color)] = [
                ("bg", p.bg), ("glass", p.glass), ("glass_on_mica", p.glassOnMica), ("title_bg", p.titleBg),
                ("border", p.border), ("footer_bg", p.footerBg), ("bar_bg", p.barBg), ("text", p.text),
                ("text_secondary", p.textSecondary), ("text_dim", p.textDim), ("text_muted", p.textMuted),
                ("accent", p.accent), ("pace_marker", p.paceMarker), ("sparkline", p.sparkline),
            ]
            for (field, color) in pairs {
                #expect(Color.hex(json[field] as! String) == color, "\(key).\(field)")
            }
            #expect(json["glass_alpha"] as? Double == p.glassAlpha, "\(key)")
            #expect(json["border_alpha"] as? Double == p.borderAlpha, "\(key)")
            #expect(json["name"] as? String == theme.displayName)
        }
    }

    @Test func cleanThemeIsOK() {
        #expect(ThemeLint.lint(cleanTheme()).isEmpty)
    }

    // MARK: Schema errors

    @Test func invalidJSONIsOneErrorOnJSON() {
        #expect(ThemeLint.lint(json: Data("{ not json".utf8)).map(\.field) == ["json"])
        #expect(!ThemeLint.ok(ThemeLint.lint(json: Data("[1,2]".utf8))))
    }

    @Test func missingRequiredColor() {
        var j = cleanTheme()
        j.removeValue(forKey: "pace_marker")
        let r = ThemeLint.lint(j)
        #expect(!ThemeLint.ok(r))
        #expect(fields(r, .error).contains("pace_marker"))
        #expect(fields(ThemeLint.lint(with(["bg": nil])), .error).contains("bg"))
    }

    @Test(arguments: ["#fff", "#ff00ff80", "ff00ff", "#gg0000", "red", "#ｆｆ００ｆｆ"])
    func malformedHexNamesTheAcceptedForm(_ bad: String) {
        let errors = ThemeLint.lint(with(["accent": bad])).filter { $0.level == .error }
        #expect(errors.map(\.field) == ["accent"])
        #expect(errors.first?.message.contains("#rrggbb") == true)
    }

    @Test func nonStringColorIsAnError() {
        #expect(fields(ThemeLint.lint(with(["bg": 12])), .error).contains("bg"))
        #expect(ThemeLint.lint(with(["bg": 12])).first?.message.contains("got 12") == true)
    }

    @Test func dialsOutOfRange() {
        let cases: [(String, Any)] = [("glass_alpha", 1.5), ("border_alpha", -0.1), ("card_corner_radius", 99),
                                      ("breath_period_ms", 10), ("ghost_alpha", 2), ("glass_alpha", true),
                                      ("glass_alpha", "0.8")]
        for (field, value) in cases {
            #expect(fields(ThemeLint.lint(with([field: value])), .error).contains(field), "\(field)=\(value)")
        }
        let msg = ThemeLint.lint(with(["glass_alpha": 1.5])).first?.message
        #expect(msg == "glass_alpha must be between 0 and 1, got 1.5.")
    }

    @Test func nestedDialsUseDottedNames() {
        var r = ThemeLint.lint(with(["accent_bloom": ["blur": 40, "alpha": 0.5],
                                     "inner_highlight": ["color": "nope", "alpha": 0.2]]))
        #expect(fields(r, .error).contains("accent_bloom.blur"))
        #expect(fields(r, .error).contains("inner_highlight.color"))
        r = ThemeLint.lint(with(["accent_bloom": 3, "inner_highlight": ["alpha": 0.2]]))
        #expect(fields(r, .error).contains("accent_bloom"))
        #expect(fields(r, .error).contains("inner_highlight.color"))
    }

    @Test func nameRules() {
        for name: Any? in ["", "   ", String(repeating: "x", count: 25), nil, 7, "two\nlines"] {
            #expect(fields(ThemeLint.lint(with(["name": name])), .error).contains("name"), "\(String(describing: name))")
        }
        var j = cleanTheme()
        j.removeValue(forKey: "name")
        #expect(fields(ThemeLint.lint(j), .error).contains("name"))
        #expect(ThemeLint.lint(with(["name": String(repeating: "x", count: 24)])).isEmpty)
    }

    @Test func presentNullsAreFine() {
        #expect(ThemeLint.lint(with(["border_tint": nil, "inner_highlight": nil, "accent_bloom": nil,
                                     "description": nil, "monospace_font": nil])).isEmpty)
    }

    @Test func macFields() {
        #expect(ThemeLint.lint(with(["description": "Night sea glass.", "ghost_alpha": 0.6, "monospace_font": "SF Mono",
                                     "opts_out_of_mica": false])).isEmpty)
        let cases: [(String, Any)] = [("description", String(repeating: "x", count: 201)), ("description", "a\nb"),
                                      ("description", 3), ("monospace_font", true), ("opts_out_of_mica", "yes"),
                                      ("opts_out_of_mica", 1)]
        for (field, value) in cases {
            #expect(fields(ThemeLint.lint(with([field: value])), .error).contains(field), "\(field)=\(value)")
        }
    }

    // MARK: Design-rule warnings

    @Test func lightBaseWarnsAndStillPasses() {
        let r = ThemeLint.lint(with(["glass_on_mica": "#f0f0f0", "glass": "#f0f0f0"]))
        #expect(ThemeLint.ok(r))
        #expect(fields(r, .warning).contains("glass_on_mica"))
        #expect(fields(r, .warning).contains("glass"))
        #expect(!fields(r, .warning).contains("bg"))
    }

    @Test func lowTextContrastNamesTheRatio() {
        let text = ThemeLint.lint(with(["text": "#5a5a5a"])).filter { $0.field == "text" }
        #expect(text.count == 1)
        #expect(text.first?.message.contains(":1") == true)
        #expect(text.first?.message.contains("4.5") == true)
    }

    @Test func textRampDescendsAndSharesAHue() {
        #expect(fields(ThemeLint.lint(with(["text_dim": "#ffffff"])), .warning).contains("text_dim"))
        let pink = with(["text": "#ffb0b0", "text_secondary": "#b0ffb0", "text_dim": "#802020", "text_muted": "#501010"])
        #expect(fields(ThemeLint.lint(pink), .warning).contains("text_secondary"))
        let gray = with(["text": "#eeeeee", "text_secondary": "#bbbbbb", "text_dim": "#777777", "text_muted": "#555555"])
        #expect(!fields(ThemeLint.lint(gray), .warning).contains("text_secondary"))
    }

    @Test func paceMarkerOnTheGreenFill() {
        #expect(fields(ThemeLint.lint(with(["pace_marker": "#4ade80"])), .warning).contains("pace_marker"))
        #expect(!fields(ThemeLint.lint(with(["pace_marker": "#fbbf24"])), .warning).contains("pace_marker"))
    }

    @Test func sparklineAndBorderTintShareTheAccentHue() {
        let r = fields(ThemeLint.lint(with(["sparkline": "#ff8800", "border_tint": "#00ff88"])), .warning)
        #expect(r.contains("sparkline"))
        #expect(r.contains("border_tint"))
    }

    @Test func micaOptOutWithTranslucentGlass() {
        #expect(fields(ThemeLint.lint(with(["opts_out_of_mica": true])), .warning).contains("glass_alpha"))
        #expect(!fields(ThemeLint.lint(with(["opts_out_of_mica": true, "glass_alpha": 1.0])), .warning).contains("glass_alpha"))
    }

    // MARK: Output and color math

    @Test func jsonShapeIsLevelFieldMessage() {
        let one = ThemeLint.json(ThemeLint.lint(with(["bg": nil])))[0]
        #expect(one["level"] == "error")
        #expect(one["field"] == "bg")
        #expect(one["message"]?.isEmpty == false)
    }

    @Test func colorMathMatchesTheReferenceValues() throws {
        let white = try #require(ThemeLint.parseHex("#ffffff")), black = try #require(ThemeLint.parseHex("#000000"))
        let red = try #require(ThemeLint.parseHex("#ff0000")), green = try #require(ThemeLint.parseHex("#00ff00"))
        #expect(abs(ThemeLint.luminance(white) - 1) < 0.001)
        #expect(abs(ThemeLint.contrast(ThemeLint.luminance(white), ThemeLint.luminance(black)) - 21) < 0.1)
        #expect(abs(ThemeLint.hue(red)) < 0.1)
        #expect(abs(ThemeLint.hue(green) - 120) < 0.1)
        #expect(abs((ThemeLint.hueDistance(red, green) ?? 0) - 120) < 0.1)
        #expect(ThemeLint.hueDistance(red, black) == nil)
        #expect(abs(ThemeLint.composite(black, 0.5, white).r - 0.5) < 0.001)
    }

    @Test func numberFormatsMatchDotNet() {
        #expect(ThemeLint.fmt(0.25, 2) == "0.25")
        #expect(ThemeLint.fmt(4.45, 1) == "4.5")
        #expect(ThemeLint.fmt(120.4, 0) == "120")
        #expect(ThemeLint.fmtTrim(1, 2) == "1")
        #expect(ThemeLint.fmtTrim(0.7, 2) == "0.7")
        #expect(ThemeLint.fmtTrim(20000, 2) == "20000")
    }

    // MARK: Keys

    @Test func keysAndSlugs() {
        #expect(ThemeLint.isValidKey("ocean-2"))
        for bad in ["", "-x", "Ocean", "a b", "über", String(repeating: "a", count: 41)] {
            #expect(!ThemeLint.isValidKey(bad), "\(bad)")
        }
        #expect(ThemeLint.slug("  Sunset Neon! ") == "sunset-neon")
        #expect(ThemeLint.slug("Café Noir") == "caf-noir")
        #expect(ThemeLint.slug("!!!") == "theme")
        #expect(ThemeLint.slug(String(repeating: "a", count: 50)).count == 40)
    }
}
