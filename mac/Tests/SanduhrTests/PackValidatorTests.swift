import Foundation
import Testing
@testable import Sanduhr

/// The reference's mutation tests (test_validate.py Mutations, PixelSheets, TrademarkRule), each
/// one change away from a good fixture, plus the places the port follows FORMAT where the Python
/// does not, the never-crash contract and the inflater. Ported case for case from
/// PackValidatorTests.cs.
@Suite("Widget packs, validator")
struct PackValidatorTests {
    // MARK: - Helpers

    typealias Pack = [String: Data]

    private func good(_ name: String) -> Pack { WidgetPackFixtures.load("good", name) }

    private func errors(_ pack: Pack, _ folder: String) -> [String] {
        PackValidator.validate(pack, folderName: folder).errors
    }

    /// Edit a pack's JSON file through Foundation's tree. Sorted keys keep the output stable and
    /// slashes stay as they are, so the snippet scan reads what Python's `json.dumps` would write.
    private func editJson(_ pack: inout Pack, _ rel: String, _ edit: (inout [String: Any]) -> Void) throws {
        var obj = try #require(try JSONSerialization.jsonObject(with: pack[rel]!) as? [String: Any])
        edit(&obj)
        pack[rel] = try JSONSerialization.data(withJSONObject: obj, options: [.prettyPrinted, .sortedKeys, .withoutEscapingSlashes])
    }

    private func editElement(_ obj: inout [String: Any], _ key: String, _ index: Int, _ edit: (inout [String: Any]) -> Void) {
        var items = obj[key] as! [[String: Any]]
        edit(&items[index])
        obj[key] = items
    }

    private func setText(_ pack: inout Pack, _ rel: String, _ text: String) { pack[rel] = Data(text.utf8) }

    private func has(_ errors: [String], _ needle: String, sourceLocation: SourceLocation = #_sourceLocation) {
        #expect(errors.contains { $0.contains(needle) }, "no error containing \"\(needle)\" in: \(errors.joined(separator: " | "))",
                sourceLocation: sourceLocation)
    }

    /// A minimal RGBA PNG (filter 0 rows, stored deflate blocks), as tools/pngutil.py writes them.
    static func png(_ w: Int, _ h: Int, _ rgba: [UInt8]) -> Data {
        var raw: [UInt8] = []
        for y in 0..<h {
            raw.append(0)
            raw.append(contentsOf: rgba[(y * w * 4)..<((y + 1) * w * 4)])
        }
        return pngData(w, h, zlibStored(raw))
    }

    static func zlibStored(_ raw: [UInt8]) -> [UInt8] {
        var z: [UInt8] = [0x78, 0x01]
        var i = 0
        repeat {
            let n = min(65535, raw.count - i)
            let final = i + n >= raw.count
            z += [final ? 1 : 0, UInt8(n & 0xFF), UInt8(n >> 8), UInt8(~n & 0xFF), UInt8((~n >> 8) & 0xFF)]
            z += raw[i..<i + n]
            i += n
        } while i < raw.count
        var s1: UInt32 = 1, s2: UInt32 = 0
        for b in raw { s1 = (s1 + UInt32(b)) % 65521; s2 = (s2 + s1) % 65521 }
        let adler = s2 << 16 | s1
        return z + [UInt8(adler >> 24), UInt8((adler >> 16) & 0xFF), UInt8((adler >> 8) & 0xFF), UInt8(adler & 0xFF)]
    }

    static func pngData(_ w: Int, _ h: Int, _ idat: [UInt8]) -> Data {
        var png: [UInt8] = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]
        var ihdr = [UInt8](repeating: 0, count: 13)
        put32(&ihdr, 0, w)
        put32(&ihdr, 4, h)
        ihdr[8] = 8
        ihdr[9] = 6
        chunk(&png, "IHDR", ihdr)
        chunk(&png, "IDAT", idat)
        chunk(&png, "IEND", [])
        return Data(png)
    }

    private static func chunk(_ out: inout [UInt8], _ tag: String, _ body: [UInt8]) {
        var len = [UInt8](repeating: 0, count: 4)
        put32(&len, 0, body.count)
        let tb = Array(tag.utf8) + body
        var crc = [UInt8](repeating: 0, count: 4)
        put32(&crc, 0, Int(PackValidator.crc32(tb, 0, tb.count)))
        out += len + tb + crc
    }

    private static func put32(_ b: inout [UInt8], _ at: Int, _ v: Int) {
        b[at] = UInt8((v >> 24) & 0xFF); b[at + 1] = UInt8((v >> 16) & 0xFF); b[at + 2] = UInt8((v >> 8) & 0xFF); b[at + 3] = UInt8(v & 0xFF)
    }

    // MARK: - The trademark rule

    @Test(arguments: [("Claude", "claude"), ("my-ANTHROPIC-pack", "anthropic"), ("Opus Neon", "opus"), ("the haiku pack", "haiku"),
                      ("sonnet-theme", "sonnet"), ("Fable Gold", "fable"), ("claudette", "claude")])
    func trademarksAreFound(text: String, hit: String) {
        #expect(PackValidator.findTrademark(text) == hit)
    }

    @Test(arguments: ["Hourglass", "OLED Neon", "magnopus-free", "Unfableish", "palette", "sonnets"])
    func ordinaryWordsPass(text: String) {
        #expect(PackValidator.findTrademark(text) == nil)
    }

    @Test func trademarksAreScannedInFileNamesAndEveryJsonString() throws {
        var p = good("good-palette")
        p["haiku.json"] = p["palettes.json"]
        try editJson(&p, "pack.json") { m in m["files"] = (m["files"] as! [Any]) + ["haiku.json"] }
        has(errors(p, "good-palette"), "trademark: file name 'haiku.json' contains 'haiku'")

        p = good("good-palette")
        try editJson(&p, "palettes.json") { d in editElement(&d, "palettes", 0) { $0["mood"] = "Anthropic" } }
        has(errors(p, "good-palette"), "trademark: palettes.json value 'Anthropic' contains 'anthropic'")
    }

    // MARK: - pack.json and files

    @Test func duplicateJsonKeyIsAnError() {
        var p = good("good-palette")
        setText(&p, "palettes.json", "{\"schema_version\": 1, \"schema_version\": 1, \"palettes\": []}")
        has(errors(p, "good-palette"), "palettes.json: invalid JSON (duplicate key 'schema_version')")
    }

    @Test func byteOrderMarkIsRejected() {
        var p = good("good-palette")
        p["palettes.json"] = Data([0xEF, 0xBB, 0xBF]) + p["palettes.json"]!
        has(errors(p, "good-palette"), "byte-order mark")
    }

    @Test func invalidUtf8IsRejected() {
        var p = good("good-palette")
        p["palettes.json"] = Data([UInt8(ascii: "{"), 0xC3, 0x28, UInt8(ascii: "}")])
        has(errors(p, "good-palette"), "palettes.json: not valid UTF-8")
    }

    @Test func unknownManifestKey() throws {
        var p = good("good-palette")
        try editJson(&p, "pack.json") { $0["homepage"] = "https://example.com" }
        has(errors(p, "good-palette"), "unknown key 'homepage'")
    }

    @Test func badVersionAndId() throws {
        var p = good("good-palette")
        try editJson(&p, "pack.json") { $0["version"] = "1.0"; $0["id"] = "Bad_Id" }
        let errs = errors(p, "good-palette")
        has(errs, "version '1.0'")
        has(errs, "kebab-case")
    }

    @Test func descriptionTooLong() throws {
        var p = good("good-palette")
        try editJson(&p, "pack.json") { $0["description"] = String(repeating: "x", count: 201) }
        has(errors(p, "good-palette"), "description must be a string of 1 to 200")
    }

    @Test func controlCharactersInAManifestString() throws {
        var p = good("good-palette")
        try editJson(&p, "pack.json") { $0["name"] = "Two\nLines" }
        has(errors(p, "good-palette"), "pack.json: name contains control characters")
    }

    @Test func pathTraversalInFiles() throws {
        var p = good("good-palette")
        try editJson(&p, "pack.json") { $0["files"] = ($0["files"] as! [Any]) + ["../escape.json"] }
        has(errors(p, "good-palette"), "not a safe relative path")
    }

    @Test(arguments: ["../x.json", "/abs.json", "C:/x.json", "a\\b.json", "has space.json", ".hidden.json"])
    func unsafeFileNamesInThePackAreRejected(rel: String) {
        var p = good("good-palette")
        p[rel] = Data("{}".utf8)
        has(errors(p, "good-palette"), rel + ": unsafe file name")
    }

    @Test func listedFileMissing() {
        var p = good("good-palette")
        p["palettes.json"] = nil
        has(errors(p, "good-palette"), "missing from the pack")
    }

    @Test func missingPackJsonStopsEarly() {
        var p = good("good-palette")
        p["pack.json"] = nil
        #expect(errors(p, "good-palette") == ["pack.json is missing"])
    }

    @Test(arguments: [".svg", ".js", ".html", ".ps1"])
    func scriptAndMarkupExtensionsAreNamed(ext: String) {
        var p = good("good-palette")
        p["extra" + ext] = Data("x".utf8)
        has(errors(p, "good-palette"), ext + " files are banned")
    }

    @Test func unknownExtensionIsRejected() {
        var p = good("good-palette")
        p["notes.txt"] = Data("hello".utf8)
        has(errors(p, "good-palette"), "notes.txt: file type '.txt' is not allowed")
    }

    @Test func uppercaseExtensionIsRejected() throws {
        var p = good("good-sprite")
        p["preview.PNG"] = p["preview.png"]
        p["preview.png"] = nil
        try editJson(&p, "pack.json") { $0["preview"] = "preview.PNG" }
        has(errors(p, "good-sprite"), "preview.PNG: file extension must be lowercase")
    }

    @Test(arguments: [([UInt8(ascii: "M"), UInt8(ascii: "Z"), 0, 0], "Windows executable"),
                      ([0x7F, UInt8(ascii: "E"), UInt8(ascii: "L"), UInt8(ascii: "F")], "ELF executable"),
                      ([UInt8(ascii: "#"), UInt8(ascii: "!"), UInt8(ascii: "/"), UInt8(ascii: "b")], "a script (shebang)")])
    func executableSignatureInADataFile(head: [UInt8], what: String) {
        var p = good("good-palette")
        p["palettes.json"] = Data(head + [UInt8](repeating: 0, count: 10))
        has(errors(p, "good-palette"), what)
    }

    @Test(arguments: [("javascript:alert(1)", "javascript:"), ("<IFRAME src=x>", "<iframe"), ("<!DOCTYPE HTML>", "<!doctype html")])
    func activeContentSnippetsAreFoundInAnyCase(text: String, snippet: String) throws {
        var p = good("good-palette")
        try editJson(&p, "palettes.json") { d in editElement(&d, "palettes", 0) { $0["mood"] = String(text.prefix(24)) } }
        has(errors(p, "good-palette"), "contains '\(snippet)'; active content is banned")
    }

    @Test func pngOverTwoMegabytes() throws {
        var p = good("good-palette")
        try editJson(&p, "pack.json") { $0["preview"] = "preview.png" }
        p["preview.png"] = Data(PackValidator.pngSignature + [UInt8](repeating: 0, count: 2 * 1024 * 1024 + 10))
        has(errors(p, "good-palette"), "preview.png: image is 2.0 MB, limit is 2 MB")
    }

    @Test func pngWithTrailingData() throws {
        var p = good("good-palette")
        try editJson(&p, "pack.json") { $0["preview"] = "preview.png" }
        p["preview.png"] = Self.png(2, 2, [UInt8](repeating: 0, count: 16)) + Data("junk".utf8)
        has(errors(p, "good-palette"), "trailing data after IEND")
    }

    @Test func pngWithShortImageData() throws {
        var p = good("good-palette")
        try editJson(&p, "pack.json") { $0["preview"] = "preview.png" }
        var png = [UInt8](Self.png(4, 4, [UInt8](repeating: 0, count: 64)))
        // Claim 8x8 in IHDR (and fix its CRC): the data inflates to the 4x4 size.
        png[16 + 3] = 8; png[20 + 3] = 8
        let crc = PackValidator.crc32(png, 12, 17)
        png[29] = UInt8(crc >> 24); png[30] = UInt8((crc >> 16) & 0xFF); png[31] = UInt8((crc >> 8) & 0xFF); png[32] = UInt8(crc & 0xFF)
        p["preview.png"] = Data(png)
        has(errors(p, "good-palette"), "preview.png: image data is 68 bytes, expected 264 for 8x8")
    }

    @Test func aGoodPngPasses() throws {
        var p = good("good-palette")
        try editJson(&p, "pack.json") { $0["preview"] = "preview.png" }
        p["preview.png"] = Self.png(3, 2, [UInt8](repeating: 90, count: 24))
        #expect(errors(p, "good-palette").isEmpty)
    }

    @Test func packOverTenMegabytes() throws {
        var p = good("good-font")
        for i in 0..<3 {
            p["big\(i).ttf"] = Data([0, 1, 0, 0] + [UInt8](repeating: 0, count: 4 * 1024 * 1024))
            try editJson(&p, "pack.json") { $0["files"] = ($0["files"] as! [Any]) + ["big\(i).ttf"] }
        }
        has(errors(p, "good-font"), "limit is 10 MB")
    }

    @Test func fontCollectionsAndOversizedFonts() {
        var p = good("good-font")
        p["fixture.ttf"] = Data(Array("ttcf".utf8) + [UInt8](repeating: 0, count: 16))
        has(errors(p, "good-font"), "fixture.ttf: font collections (.ttc) are not allowed")
        p["fixture.ttf"] = Data([0, 1, 0, 0] + [UInt8](repeating: 0, count: 5 * 1024 * 1024))
        has(errors(p, "good-font"), "fixture.ttf: font is 5.0 MB, limit is 5 MB")
    }

    @Test func folderNameIsCheckedOnlyWhenGiven() {
        let p = good("good-palette")
        #expect(PackValidator.validate(p, folderName: nil).ok)
        has(PackValidator.validate(p, folderName: "somewhere-else").errors, "does not match the folder name 'somewhere-else'")
    }

    @Test func packJsonCanBeGivenApartFromTheOtherFiles() {
        var p = good("good-palette")
        let manifest = p["pack.json"]!
        p["pack.json"] = nil
        let r = PackValidator.validate(packJson: manifest, otherFiles: p, folderName: "good-palette")
        #expect(r.ok)
        #expect(r.manifest?.kind == "palette")
        #expect(r.manifest?.files == ["palettes.json"])

        p["pack.json"] = manifest
        #expect(PackValidator.validate(packJson: manifest, otherFiles: p).errors == ["pack.json is given twice"])
    }

    // MARK: - Themes

    @Test func themeTextColorMustBeAHex() throws {
        var p = good("good-theme")
        try editJson(&p, "themes/fixture-dark.json") { $0["text"] = "white" }
        has(errors(p, "good-theme"), "text must be a #rrggbb color")
    }

    @Test func themeUnknownKeyIsAnError() throws {
        var p = good("good-theme")
        try editJson(&p, "themes/fixture-dark.json") { $0["script"] = "x" }
        has(errors(p, "good-theme"), "unknown key 'script'")
    }

    @Test func themeDialOutOfRange() throws {
        var p = good("good-theme")
        try editJson(&p, "themes/fixture-dark.json") { $0["glass_alpha"] = 1.5 }
        has(errors(p, "good-theme"), "glass_alpha must be between")
    }

    @Test func themeFileNamePattern() throws {
        var p = good("good-theme")
        p["themes/Bad-Name.json"] = p["themes/fixture-dark.json"]
        p["themes/fixture-dark.json"] = nil
        try editJson(&p, "pack.json") { $0["files"] = ["themes/Bad-Name.json"] }
        has(errors(p, "good-theme"), "themes/Bad-Name.json: theme files must be themes/<kebab-name>.json")
    }

    /// FORMAT 5.1 rules the app's own theme lint lets through; the catalog does not.
    @Test(arguments: [("accent_bloom", "null", "accent_bloom must be an object"),
                      ("opts_out_of_mica", "\"yes\"", "opts_out_of_mica must be true or false"),
                      ("bg_grid", "1", "bg_grid must be true or false"),
                      ("monospace_font", "3", "monospace_font must be a string")])
    func catalogThemeRulesBeyondTheAppLint(key: String, json: String, needle: String) throws {
        var p = good("good-theme")
        let value = try JSONSerialization.jsonObject(with: Data(json.utf8), options: [.fragmentsAllowed])
        try editJson(&p, "themes/fixture-dark.json") { $0[key] = value }
        has(errors(p, "good-theme"), needle)
    }

    // MARK: - Palettes and effects

    @Test func effectsColorMustBeHexOrInk() throws {
        var p = good("good-effects")
        try editJson(&p, "effects.json") { d in editElement(&d, "effects", 0) { $0["color"] = "red" } }
        has(errors(p, "good-effects"), "color must be a #rrggbb color or \"ink\"")
    }

    @Test func effectsTypeEnum() throws {
        var p = good("good-effects")
        try editJson(&p, "effects.json") { d in editElement(&d, "effects", 0) { $0["type"] = "explode" } }
        has(errors(p, "good-effects"), "type must be one of glow, sweep, shimmer, pulse")
    }

    @Test func booleansAreNotNumbers() throws {
        var p = good("good-effects")
        setText(&p, "effects.json", "{\"schema_version\": 1, \"effects\": [{\"name\": \"breathe\", \"type\": \"pulse\", "
            + "\"intensity\": true, \"period_ms\": 4000, \"color\": \"ink\"}]}")
        has(errors(p, "good-effects"), "intensity must be a number")
    }

    @Test func anIntegerFieldRejectsAFloatWithTheSameValue() {
        var p = good("good-effects")
        setText(&p, "effects.json", "{\"schema_version\": 1, \"effects\": [{\"name\": \"breathe\", \"type\": \"pulse\", "
            + "\"intensity\": 0.3, \"period_ms\": 4000.0, \"color\": \"ink\"}]}")
        has(errors(p, "good-effects"), "period_ms must be an integer from 400 to 10000")
    }

    @Test func paletteUnknownKeysAndDuplicateNames() throws {
        var p = good("good-palette")
        try editJson(&p, "palettes.json") { d in
            d["extra"] = 1
            d["palettes"] = (d["palettes"] as! [Any]) + [["name": "plain", "colors": ["#000000"], "tag": 1] as [String: Any]]
        }
        let errs = errors(p, "good-palette")
        has(errs, "palettes.json: unknown keys ['extra']")
        has(errs, "palettes.json: palettes[1] has unknown keys ['tag']")
        has(errs, "palettes.json: palettes[1]: duplicate palette name 'plain'")
    }

    @Test func paletteColorMessagesReprTheValueLikeTheReference() {
        var p = good("good-palette")
        setText(&p, "palettes.json", "{\"schema_version\": 1, \"palettes\": [{\"name\": \"plain\", \"colors\": "
            + "[\"red\", 5, null, {\"a\": true}]}, {\"name\": \"other\", \"colors\": [1.5, 1e-05, 1e+16, -0]}]}")
        let errs = errors(p, "good-palette")
        has(errs, "palettes[0]: 'red' is not a #rrggbb color")
        has(errs, "palettes[0]: 5 is not a #rrggbb color")
        has(errs, "palettes[0]: None is not a #rrggbb color")
        has(errs, "palettes[0]: {'a': True} is not a #rrggbb color")
        has(errs, "palettes[1]: 1.5 is not a #rrggbb color")
        has(errs, "palettes[1]: 1e-05 is not a #rrggbb color")
        has(errs, "palettes[1]: 1e+16 is not a #rrggbb color")
        has(errs, "palettes[1]: 0 is not a #rrggbb color")
    }

    // MARK: - Sprites

    @Test func spriteRequiresExactlySheetOrStages() throws {
        var p = good("good-sprite")
        try editJson(&p, "sprite.json") {
            $0["stages"] = [["id": "aa", "name": "A", "min_growth": 0, "sheet": "sprite.png", "frame_w": 8, "frame_h": 8] as [String: Any]]
        }
        has(errors(p, "good-sprite"), "exactly one of")
    }

    @Test func spriteFirstStageMustStartAtZero() throws {
        var p = good("good-sprite-stages")
        try editJson(&p, "sprite.json") { d in editElement(&d, "stages", 0) { $0["min_growth"] = 0.2 } }
        has(errors(p, "good-sprite-stages"), "first stage must have min_growth 0")
    }

    @Test func spriteStageOrderMessageFormatsLikeTheReference() throws {
        var p = good("good-sprite-stages")
        try editJson(&p, "sprite.json") { d in editElement(&d, "stages", 1) { $0["min_growth"] = 0 } }
        has(errors(p, "good-sprite-stages"), "sprite.json: stages[1]: min_growth 0 must be greater than the previous stage's 0")
    }

    @Test func spriteStageZeroMatchesBaseFrame() throws {
        var p = good("good-sprite-stages")
        try editJson(&p, "sprite.json") { $0["frame_w"] = 16; $0["frame_h"] = 16 }
        has(errors(p, "good-sprite-stages"), "must equal the top-level")
    }

    @Test func spriteStageSheetMustBeListed() throws {
        var p = good("good-sprite-stages")
        try editJson(&p, "pack.json") { $0["files"] = ["sprite.json", "sprites/one.png"] }
        has(errors(p, "good-sprite-stages"), "pack.json: files must list sprites/two.png")
    }

    @Test func spriteTriggerNamesMustExist() throws {
        var p = good("good-sprite")
        try editJson(&p, "sprite.json") { d in
            var t = d["triggers"] as! [String: Any]
            t["idle"] = "missing"
            d["triggers"] = t
        }
        has(errors(p, "good-sprite"), "triggers.idle names animation 'missing'")
    }

    @Test func spriteRoamSpeedRangeAndEdges() throws {
        var p = good("good-sprite")
        try editJson(&p, "sprite.json") { d in
            var r = d["roam"] as! [String: Any]
            r["speed_px_s"] = 500
            r["edges"] = ["bottom", "bottom"]
            d["roam"] = r
        }
        let errs = errors(p, "good-sprite")
        has(errs, "roam.speed_px_s")
        has(errs, "roam.edges must be a non-empty unique subset of bottom, left, right")
    }

    @Test func spriteSixStageLimit() throws {
        var p = good("good-sprite-stages")
        try editJson(&p, "sprite.json") { d in
            let first = (d["stages"] as! [[String: Any]])[0]
            d["stages"] = (0..<7).map { i -> [String: Any] in
                var st = first
                st["id"] = "st\(i)"
                st["min_growth"] = Double(i) / 10.0
                return st
            }
        }
        has(errors(p, "good-sprite-stages"), "1 to 6 entries")
    }

    @Test func spriteIntegerFieldsRejectFloats() throws {
        var p = good("good-sprite")
        try editJson(&p, "sprite.json") { $0["scale"] = 2.5; $0["fps"] = 0 }
        let errs = errors(p, "good-sprite")
        has(errs, "scale must be an integer from 1 to 4")
        has(errs, "fps must be an integer from 1 to 24")
    }

    @Test func spriteAnimationNameAndFrameRules() throws {
        var p = good("good-sprite")
        try editJson(&p, "sprite.json") { d in
            var a = d["animations"] as! [String: Any]
            a["Bad Name"] = [0]
            a["walk"] = [-1]
            d["animations"] = a
        }
        let errs = errors(p, "good-sprite")
        has(errs, "animation name 'Bad Name' must match [a-z][a-z0-9_]{1,23}")
        has(errs, "animation 'walk' must be 1 to 64 non-negative integer frame indexes")
    }

    // MARK: - Sprite v2 pixel sheets

    @Test func v2WithPngSheetsOnlyIsValid() throws {
        var p = good("good-sprite")
        try editJson(&p, "sprite.json") { $0["schema_version"] = 2 }
        #expect(errors(p, "good-sprite").isEmpty)
    }

    @Test func schemaVersionThreeIsRejected() throws {
        var p = good("good-sprite-pixel")
        try editJson(&p, "sprite.json") { $0["schema_version"] = 3 }
        has(errors(p, "good-sprite-pixel"), "schema_version must be 1 or 2")
    }

    @Test func pixelFrameMustMatchTheStage() throws {
        var p = good("good-sprite-pixel-stages")
        try editJson(&p, "sprites/one.json") { s in
            s["frame_w"] = 16
            s["frame_h"] = 16
            let rows = [String](repeating: String(repeating: "0", count: 16), count: 16)
            s["frames"] = [[String]](repeating: rows, count: 4)
        }
        has(errors(p, "good-sprite-pixel-stages"), "frame size 16x16 does not match the stage 'one' frame size 8x8")
    }

    @Test func uppercaseIndexIsRejected() throws {
        var p = good("good-sprite-pixel")
        try editJson(&p, "sheet.json") { s in
            var frames = s["frames"] as! [[String]]
            frames[1][0] = "..0A00.."
            s["frames"] = frames
        }
        has(errors(p, "good-sprite-pixel"), "frames[1][0][3] is 'A'")
    }

    @Test func pixelSheetMustBeListed() throws {
        var p = good("good-sprite-pixel")
        try editJson(&p, "pack.json") { $0["files"] = ["sprite.json"] }
        has(errors(p, "good-sprite-pixel"), "must list sheet.json")
    }

    @Test func pixelSheetUnknownKeyAndTrademarkScan() throws {
        var p = good("good-sprite-pixel")
        try editJson(&p, "sheet.json") { $0["title"] = "Claude pet" }
        let errs = errors(p, "good-sprite-pixel")
        has(errs, "unknown keys ['title']")
        has(errs, "trademark: sheet.json value")
    }

    @Test func pixelSheetMissingKeysAndWrongTypes() {
        var p = good("good-sprite-pixel")
        setText(&p, "sheet.json", "{\"palette\": \"#22d3ee\", \"frames\": [[\"........\"], 7]}")
        let errs = errors(p, "good-sprite-pixel")
        has(errs, "missing required key 'frame_w'")
        has(errs, "palette must be 1 to 16")
        has(errs, "frames[1] must be an array of strings")
    }

    @Test func sheetMustBePngOrJson() throws {
        var p = good("good-sprite-pixel")
        try editJson(&p, "sprite.json") { $0["sheet"] = "sheet.txt" }
        has(errors(p, "good-sprite-pixel"), "must be a .png sheet or a .json pixel sheet")
    }

    @Test func spriteJsonCannotBeItsOwnSheet() throws {
        var p = good("good-sprite-pixel")
        try editJson(&p, "sprite.json") { $0["sheet"] = "sprite.json" }
        has(errors(p, "good-sprite-pixel"), "must not be sprite.json itself")
    }

    @Test func aBrokenSheetDoesNotFloodTheReport() throws {
        var p = good("good-sprite-pixel")
        try editJson(&p, "sheet.json") { s in
            s["frames"] = [[String]](repeating: [String](repeating: "zzzzzzzz", count: 8), count: 40)
        }
        let errs = errors(p, "good-sprite-pixel")
        #expect(errs.count <= PackValidator.pixelMaxErrors + 1)
        #expect(errs.last?.contains("more errors in this pixel sheet are not shown") == true)
    }

    @Test func frameLimit() throws {
        var p = good("good-sprite-pixel")
        try editJson(&p, "sheet.json") { s in
            let frames = s["frames"] as! [[String]]
            s["frames"] = (0..<65).flatMap { _ in frames }
        }
        has(errors(p, "good-sprite-pixel"), "frames must be an array of 1 to 256")
    }

    // MARK: - Null documents and trailing newlines (validate.py rejects both too since 61b4247)

    /// A file whose JSON is the literal null is an error, worded as validate.py's load_json words it.
    @Test(arguments: [("good-palette", "palettes.json"), ("good-effects", "effects.json"), ("good-sprite", "sprite.json"),
                      ("good-theme", "themes/fixture-dark.json"), ("good-sprite-pixel", "sheet.json"), ("good-palette", "pack.json")])
    func aNullDocumentIsNotAnObject(pack: String, rel: String) {
        var p = good(pack)
        setText(&p, rel, "null")
        has(errors(p, pack), "\(rel): must be a JSON object, got null")
    }

    /// A color with a trailing newline is not a color (validate.py uses fullmatch).
    @Test func aTrailingNewlineDoesNotSatisfyAPattern() throws {
        var p = good("good-palette")
        try editJson(&p, "palettes.json") { d in
            editElement(&d, "palettes", 0) { $0["colors"] = ["#ff2a6d\n"] }
        }
        has(errors(p, "good-palette"), "palettes[0]: '#ff2a6d\\n' is not a #rrggbb color")
    }

    /// validate.py raises TypeError (unhashable list) here; the port reports it.
    @Test func aLicenseOfTheWrongTypeIsReported() throws {
        var p = good("good-palette")
        try editJson(&p, "pack.json") { $0["license"] = [Any]() }
        has(errors(p, "good-palette"), "pack.json: license [] is not allowed; use one of MIT, CC-BY-4.0, CC0-1.0, Apache-2.0, OFL-1.1, BSD-3-Clause, ISC")
    }

    /// validate.py raises TypeError when a sprite pack's files list holds an object; the port reports it.
    @Test func aSpriteFilesEntryOfTheWrongTypeIsReported() throws {
        var p = good("good-sprite")
        try editJson(&p, "pack.json") { $0["files"] = ($0["files"] as! [Any]) + [[String: Any]()] }
        has(errors(p, "good-sprite"), "pack.json: files entries must be strings")
    }

    // MARK: - Never crashes

    private struct SplitMix {
        var state: UInt64
        mutating func next() -> UInt64 {
            state &+= 0x9E37_79B9_7F4A_7C15
            var z = state
            z = (z ^ (z >> 30)) &* 0xBF58_476D_1CE4_E5B9
            z = (z ^ (z >> 27)) &* 0x94D0_49BB_1331_11EB
            return z ^ (z >> 31)
        }
        mutating func below(_ n: Int) -> Int { Int(next() % UInt64(max(1, n))) }
    }

    @Test func garbageNeverCrashes() {
        var rng = SplitMix(state: 20_261_022)
        let base = good("good-sprite-stages")
        let names = base.keys.sorted()
        for round in 0..<400 {
            var p = base
            let victim = names[rng.below(names.count)]
            var bytes = [UInt8](p[victim]!)
            switch round % 4 {
            case 0: bytes = Array(bytes[..<rng.below(bytes.count)])   // truncate
            case 1:
                for _ in 0..<4 where !bytes.isEmpty { bytes[rng.below(bytes.count)] ^= UInt8(1 + rng.below(255)) }
            case 2: bytes = (0..<(1 + rng.below(63))).map { _ in UInt8(truncatingIfNeeded: rng.next()) }
            default: bytes.append(0)
            }
            p[victim] = Data(bytes)
            let r = PackValidator.validate(p, folderName: "good-sprite-stages")   // must not crash
            for e in r.errors { #expect(!e.trimmingCharacters(in: .whitespaces).isEmpty) }
        }
        #expect(PackValidator.validate(nil as [String: Data]?).errors == ["no files were given"])
    }

    @Test func deepNestingIsAnErrorNotACrash() {
        var p = good("good-palette")
        setText(&p, "palettes.json", String(repeating: "[", count: 5000) + String(repeating: "]", count: 5000))
        has(errors(p, "good-palette"), "palettes.json: invalid JSON")
    }

    // MARK: - The JSON reader and Python's formatting

    @Test(arguments: [(0.35, "0.35"), (1.0, "1.0"), (1e16, "1e+16"), (1.5e-5, "1.5e-05"), (0.0001, "0.0001"), (123456.789, "123456.789"),
                      (1e22, "1e+22"), (-2.5, "-2.5"), (0.1 + 0.2, "0.30000000000000004"), (9999999999999998.0, "9999999999999998.0")])
    func pyFloatReprsLikePython(value: Double, text: String) {
        #expect(PackJson.pyFloat(value) == text)
    }

    @Test(arguments: [("plain", "'plain'"), ("it's", "\"it's\""), ("both ' and \"", "'both \\' and \"'"), ("tab\there", "'tab\\there'"),
                      ("nl\n", "'nl\\n'"), ("\u{01}", "'\\x01'"), ("back\\slash", "'back\\\\slash'"), ("caf\u{e9}", "'caf\u{e9}'"),
                      ("\u{200B}", "'\\u200b'")])
    func pyStrReprsLikePython(value: String, text: String) {
        #expect(PackJson.pyStr(value) == text)
    }

    @Test func pyGMatchesPercentG() {
        #expect(PackJson.pyG(0) == "0")
        #expect(PackJson.pyG(0.25) == "0.25")
        #expect(PackJson.pyG(1.5) == "1.5")
        #expect(PackJson.pyG(1234567) == "1.23457e+06")
        #expect(PackJson.pyG(0.0001) == "0.0001")
    }

    @Test(arguments: [("", "Expecting value: line 1 column 1 (char 0)"),
                      ("{\"a\": 1,}", "Expecting property name enclosed in double quotes: line 1 column 9 (char 8)"),
                      ("{\"a\" 1}", "Expecting ':' delimiter: line 1 column 6 (char 5)"),
                      ("[1 2]", "Expecting ',' delimiter: line 1 column 4 (char 3)"),
                      ("[1,\n 2] x", "Extra data: line 2 column 5 (char 8)"),
                      ("\"abc", "Unterminated string starting at: line 1 column 1 (char 0)"),
                      ("{\"a\": NaN}", "Expecting value: line 1 column 7 (char 6)"),
                      ("01", "Extra data: line 1 column 2 (char 1)")])
    func jsonErrorsReadLikePythons(text: String, message: String) {
        switch PackJson.parse(Array(text.unicodeScalars)) {
        case .success: Issue.record("parsed \(text)")
        case .failure(let e): #expect(e.message == message)
        }
    }

    @Test func jsonNumbersKeepTheirKind() throws {
        let scalars = Array("[8, 8.0, 8e0, -0, 1E2]".unicodeScalars)
        let value = try #require(try? PackJson.parse(scalars).get())
        let items = try #require(value.array)
        #expect(items.compactMap { $0.number?.isInt } == [true, false, false, true, false])
        #expect(items.map { PackJson.pyRepr($0) } == ["8", "8.0", "8.0", "0", "100.0"])
    }

    // MARK: - The inflater

    private func hex(_ s: String) -> [UInt8] {
        stride(from: 0, to: s.count, by: 2).map { i in
            let a = s.index(s.startIndex, offsetBy: i)
            return UInt8(s[a..<s.index(a, offsetBy: 2)], radix: 16)!
        }
    }

    /// zlib.compress(b"hello hello hello hello hello", 9): a fixed-Huffman block with back references.
    private var fixedStream: [UInt8] { hex("78dacb48cdc9c957c8c04e0200a3100ae5") }

    @Test func inflatesAFixedHuffmanStream() throws {
        #expect(try PackInflate.inflatedLength(fixedStream, limit: 1000) == 29)
    }

    @Test func inflatesADynamicHuffmanStream() throws {
        let stream = hex("78da636064e11490543174084ca99cb0f2c8435635cfbca9fb5e4a7b55af7b2c1f31f98248e88cbb9a457bf9e3d6b3c76c16cc39a9dbf731fa90c16cde9ab7a9b7c2cefb9f09b818792feb73b3e812abb3e9cc736c6ed62a1f2b90395aaa7eadc7e5e7d662e30f5baa9c782e2f2c74167f716056598889d8d7db87d7ce6c2fcf8c0df67675b4b77774f50e8ecd2c6f9fb9f6f0edaf62262165b30ebc10772e5c7899c7a96acb07e3e2ad3f5d7aaea9971e952938a65c7bd3660e73fa59ab25a2cd9fb3ee455e0c38e37f3eec56eadb1aded90687a23ff6e99ecc11dc1cc3be3e8e7f6f91e6dd19a122172647c83f5e57ed25fd72dfd43c4f35d68747564ea84c097430549114e064616418f5faa8d747bd3eeaf551af8f7a7dd4eba35e1ff5faa8d7878ad713939247d1281a45a368140d330400d17fa870")
        #expect(try PackInflate.inflatedLength(stream, limit: 100_000) == 4500)
    }

    @Test func theLimitCapsTheOutput() throws {
        #expect(try PackInflate.inflatedLength(fixedStream, limit: 10) == 10)
    }

    @Test func aTruncatedStreamAnswersWithWhatItDecoded() throws {
        let cut = Array(fixedStream.prefix(8))
        let n = try PackInflate.inflatedLength(cut, limit: 1000)
        #expect(n > 0 && n < 29)
        #expect(try PackInflate.inflatedLength([0x78], limit: 1000) == 0)
    }

    @Test func aCorruptStreamIsNamedTheWayZlibNamesIt() {
        func message(_ bytes: [UInt8]) -> String? {
            do { _ = try PackInflate.inflatedLength(bytes, limit: 1000); return nil } catch let e as PackInflate.Corrupt { return e.message } catch { return "\(error)" }
        }
        #expect(message([0x78, 0x00, 0x03, 0x00]) == "Error -3 while decompressing data: incorrect header check")
        #expect(message([0x77, 0x09, 0x03, 0x00]) == "Error -3 while decompressing data: unknown compression method")
        #expect(message([0x78, 0x01, 0x07]) == "Error -3 while decompressing data: invalid block type")
        #expect(message([0x78, 0x01, 0x01, 0x05, 0x00, 0x00, 0x00]) == "Error -3 while decompressing data: invalid stored block lengths")
        var bad = fixedStream
        bad[bad.count - 1] ^= 1
        #expect(message(bad) == "Error -3 while decompressing data: incorrect data check")
    }

    @Test func aCorruptPngStreamIsReportedWithZlibsReason() throws {
        var p = good("good-palette")
        try editJson(&p, "pack.json") { $0["preview"] = "preview.png" }
        p["preview.png"] = Self.pngData(2, 2, [0x78, 0x00, 0x03, 0x00])
        has(errors(p, "good-palette"), "preview.png: corrupt image data (Error -3 while decompressing data: incorrect header check)")
    }
}
