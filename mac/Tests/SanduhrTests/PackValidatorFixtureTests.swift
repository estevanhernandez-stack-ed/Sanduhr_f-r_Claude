import Foundation
import Testing
@testable import Sanduhr

/// The fixtures and first-party packs of `sanduhr-widgets` (copied at 61b4247, see
/// `Fixtures/widget-packs/README.md`), read into the in-memory form `PackValidator` takes.
enum WidgetPackFixtures {
    static var root: String {
        Bundle.module.resourceURL!.appendingPathComponent("Fixtures/widget-packs").path
    }

    static func names(_ group: String) -> [String] {
        let dir = root + "/" + group
        let items = (try? FileManager.default.contentsOfDirectory(atPath: dir)) ?? []
        return items.filter { !$0.hasPrefix(".") }.sorted { Array($0.utf8).lexicographicallyPrecedes(Array($1.utf8)) }
    }

    /// Every file in the pack folder, keyed by its relative POSIX path.
    static func load(_ group: String, _ name: String) -> [String: Data] {
        let dir = root + "/" + group + "/" + name
        var files: [String: Data] = [:]
        for rel in (try? FileManager.default.subpathsOfDirectory(atPath: dir)) ?? [] {
            var isDir: ObjCBool = false
            guard FileManager.default.fileExists(atPath: dir + "/" + rel, isDirectory: &isDir), !isDir.boolValue else { continue }
            files[rel] = FileManager.default.contents(atPath: dir + "/" + rel)
        }
        return files
    }

    struct Finding {
        let level: String
        let message: String
    }

    /// The reference validator's findings (python-expected.tsv): pack key "group/name" to its findings, in order.
    static func expected() -> [String: [Finding]] {
        let text = (try? String(contentsOfFile: root + "/python-expected.tsv", encoding: .utf8)) ?? ""
        var out: [String: [Finding]] = [:]
        for line in text.split(separator: "\n", omittingEmptySubsequences: true) {
            let parts = line.split(separator: "\t", maxSplits: 2, omittingEmptySubsequences: false)
            guard parts.count == 3 else { continue }
            out[String(parts[0]), default: []].append(Finding(level: String(parts[1]), message: String(parts[2])))
        }
        return out
    }
}

/// Fixture parity with the reference validator (sanduhr-widgets `tools/validate.py` and
/// `test_validate.py` at 61b4247): the same packs pass, the same packs fail, with the same messages.
/// Theme schema and design messages extend the reference's sentence with the fix, so those compare
/// by prefix and everything else byte for byte. Ported case for case from PackValidatorFixtureTests.cs.
@Suite("Widget packs, fixture parity")
struct PackValidatorFixtureTests {
    static let python = WidgetPackFixtures.expected()

    @Test func theFixtureSetsAreAllThere() {
        #expect(WidgetPackFixtures.names("good").count == 8)
        #expect(WidgetPackFixtures.names("bad").count == 42)
        #expect(WidgetPackFixtures.names("packs") == ["hourglass", "oled-neon", "oled-neon-effects", "oled-neon-palettes"])
    }

    @Test(arguments: WidgetPackFixtures.names("good"))
    func everyGoodFixturePassesStrictWithNoFindings(name: String) {
        let r = PackValidator.validate(WidgetPackFixtures.load("good", name), folderName: name, strict: true)
        #expect(r.errors.isEmpty)
        #expect(r.warnings.isEmpty)
        #expect(r.ok)
        #expect(r.manifest?.id == name)
    }

    @Test(arguments: WidgetPackFixtures.names("packs"))
    func everyFirstPartyPackIsStrictClean(name: String) {
        let r = PackValidator.validate(WidgetPackFixtures.load("packs", name), folderName: name, strict: true)
        #expect(r.errors.isEmpty)
        #expect(r.warnings.isEmpty)
        #expect(r.manifest?.id == name)
    }

    @Test func everyBadFixtureHasAnExpectationFromTheReference() {
        for name in WidgetPackFixtures.names("bad") {
            #expect(Self.python["bad/" + name]?.isEmpty == false, "python-expected.tsv has no line for bad/\(name)")
        }
    }

    /// Every row of python-expected.tsv, in the reference's order, grouped by pack: the bad packs'
    /// errors and the light theme's warnings (which fail only under strict).
    @Test(arguments: WidgetPackFixtures.names("bad"))
    func everyBadFixtureFailsWithExactlyTheReferenceErrors(name: String) {
        let expected = (Self.python["bad/" + name] ?? []).filter { $0.level == "ERROR" }.map(\.message)
        if expected.isEmpty { return }   // bad-theme-light-bg: warnings only, covered below
        let r = PackValidator.validate(WidgetPackFixtures.load("bad", name), folderName: name)
        #expect(!r.ok)
        #expect(r.errors.count == expected.count, "\(name): \(r.errors)")
        for (want, got) in zip(expected, r.errors) { Self.assertSameMessage(want, got) }
    }

    @Test func lightThemeOnlyWarnsAndFailsOnlyUnderStrict() {
        let name = "bad-theme-light-bg"
        let expected = (Self.python["bad/" + name] ?? []).map(\.message)
        #expect(expected.count == 5)

        let lenient = PackValidator.validate(WidgetPackFixtures.load("bad", name), folderName: name)
        #expect(lenient.ok)
        #expect(lenient.errors.isEmpty)
        #expect(lenient.warnings.count == expected.count)
        for (want, got) in zip(expected, lenient.warnings) { Self.assertSameMessage(want, got) }
        #expect(lenient.warnings.contains { $0.contains("bg is light") })

        let strict = PackValidator.validate(WidgetPackFixtures.load("bad", name), folderName: name, strict: true)
        #expect(!strict.ok)
        #expect(strict.errors.count == 5)
        #expect(strict.warnings.isEmpty)
        #expect(strict.manifest == nil)
    }

    /// test_validate.py's BAD table: the substring the one expected error must carry.
    static let badNeedles: [(String, String)] = [
        ("bad-trademark-name", "trademark: pack.json name contains 'claude'"),
        ("bad-trademark-model-word", "contains 'sonnet'"),
        ("bad-script-in-json", "contains '<script'"),
        ("bad-banned-extension", ".exe files are banned"),
        ("bad-unlisted-file", "not listed in pack.json files"),
        ("bad-preview-too-wide", "limit is 4096 pixels"),
        ("bad-fake-png", "not a PNG"),
        ("bad-png-crc", "bad CRC"),
        ("bad-font-license", "font packs must use OFL-1.1 or Apache-2.0"),
        ("bad-font-magic", "not a TrueType/OpenType font"),
        ("bad-sprite-missing-anim", "required animation 'celebrate' is missing"),
        ("bad-sprite-grid", "not a whole number of 8x8 frames"),
        ("bad-sprite-index", "frame index 3 but the sheet has only 2 frames"),
        ("bad-sprite-stage-order", "must be greater than the previous stage"),
        ("bad-sprite-mood", "moods.exhausted names animation 'snore'"),
        ("bad-theme-missing-field", "accent is required"),
        ("bad-id-mismatch", "does not match the folder name"),
        ("bad-no-license", "LICENSE is missing"),
        ("bad-license-text", "LICENSE text does not look like MIT"),
        ("bad-effects-range", "period_ms must be an integer from 400 to 10000"),
        ("bad-palette-stops", "colors must be 1 to 4"),
        ("bad-pixel-char", "sheet.json: frames[0][3][3] is 'g'; a pixel is '.' (transparent) or 0-9, a-f"),
        ("bad-pixel-index", "frames[0][2][3] is palette index '3' (3) but the palette has only 3 colors"),
        ("bad-pixel-row-length", "frames[0][5] is 7 characters, expected 8 (frame_w)"),
        ("bad-pixel-rows", "frames[0] has 7 rows, expected 8 (frame_h)"),
        ("bad-pixel-frames", "animations use frame index 3 but the pixel sheet has only 3 frames"),
        ("bad-pixel-white", "palette[1] is pure white (#FFFFFF)"),
        ("bad-pixel-colors", "palette must be 1 to 16 #rrggbb colors (got 17)"),
        ("bad-pixel-frame-size", "frame_w and frame_h must be integers from 8 to 32, got 40 x 40"),
        ("bad-sprite-v1-json-sheet", "'sheet.json' is a .json pixel sheet, which needs sprite.json schema_version 2"),
    ]

    @Test(arguments: badNeedles)
    func badFixtureFailsForItsNamedReason(name: String, needle: String) {
        let r = PackValidator.validate(WidgetPackFixtures.load("bad", name), folderName: name)
        #expect(r.errors.filter { $0.contains(needle) }.count == 1, "\(name): \(r.errors)")
        // One mistake, one error; the .exe fixture also breaks the palette listing rule, as in the reference.
        #expect(r.errors.count == (name == "bad-banned-extension" ? 2 : 1))
    }

    @Test func trademarkErrorSaysWhy() {
        let r = PackValidator.validate(WidgetPackFixtures.load("bad", "bad-trademark-name"), folderName: "bad-trademark-name")
        #expect(r.errors[0].contains("no Anthropic or Claude marks"))
        #expect(r.errors[0].contains("model names as branding"))
    }

    /// Theme files go through the ported theme lint, whose sentences extend the reference's (same
    /// facts, then the fix); every other message is the reference's exactly.
    static func assertSameMessage(_ python: String, _ swift: String, sourceLocation: SourceLocation = #_sourceLocation) {
        if python.hasPrefix("themes/") {
            #expect(swift.hasPrefix(python), "\(swift) does not start with \(python)", sourceLocation: sourceLocation)
        } else {
            #expect(swift == python, sourceLocation: sourceLocation)
        }
    }
}
