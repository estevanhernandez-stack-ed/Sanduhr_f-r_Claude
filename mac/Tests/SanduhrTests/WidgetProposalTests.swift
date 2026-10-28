import Foundation
import Testing
@testable import Sanduhr

/// One step into a JSON tree: an object key or an array index.
enum JStep: ExpressibleByStringLiteral, ExpressibleByIntegerLiteral {
    case key(String)
    case index(Int)
    init(stringLiteral value: String) { self = .key(value) }
    init(integerLiteral value: Int) { self = .index(value) }
}

/// Reading and editing JSON trees in tests (the Windows tests edit `JsonObject`s in place; a `PackObject`
/// is immutable here, so each edit returns a new tree).
enum TJ {
    static func parse(_ text: String) -> PackValue {
        guard let scalars = PackJson.decodeUTF8(Array(text.utf8)), case .success(let v) = PackJson.parse(scalars) else {
            preconditionFailure("not JSON: \(text)")
        }
        return v
    }

    static func obj(_ text: String) -> PackObject { parse(text).object! }

    static func set(_ root: PackObject, _ path: [JStep], _ value: PackValue) -> PackObject {
        guard case .object(let o) = set(.object(root), path[...], value) else { preconditionFailure() }
        return o
    }

    private static func set(_ node: PackValue, _ path: ArraySlice<JStep>, _ value: PackValue) -> PackValue {
        guard let first = path.first else { return value }
        switch (first, node) {
        case (.key(let k), .object(let o)):
            return .object(WidgetJson.setting(o, k, set(o[k] ?? .null, path.dropFirst(), value)))
        case (.index(let i), .array(var a)):
            a[i] = set(a[i], path.dropFirst(), value)
            return .array(a)
        default:
            preconditionFailure("bad path")
        }
    }

    static func get(_ node: PackValue?, _ path: [JStep]) -> PackValue? {
        var cur = node
        for step in path {
            switch (step, cur) {
            case (.key(let k), .object(let o)?): cur = o[k]
            case (.index(let i), .array(let a)?): cur = i < a.count ? a[i] : nil
            default: return nil
            }
        }
        return cur
    }

    static func text(_ data: Data) -> PackValue { parse(String(decoding: data, as: UTF8.self)) }
}

/// propose_widget's shared rules (spec 3.8 section 5): the pack built from the tool's arguments, the refusals,
/// the request file and the widget's own check of it, and the handoff's result file. Ported case for case from
/// WidgetProposalTests.cs, less the handoff and review-bar cases (those types come with their own items).
/// Pure: no folder is touched.
@Suite("Widget proposals")
struct WidgetProposalTests {
    static let now = Date(timeIntervalSince1970: 1_792_692_000)   // 2026-10-22T18:00:00Z

    private static func fixture(_ pack: String, _ file: String) -> String {
        let data = WidgetPackFixtures.load("good", pack)[file]!
        return String(decoding: data, as: UTF8.self)
    }

    /// A sprite proposal as an agent would send it: the fixture's pixel sprite (8x8, four frames).
    static func spriteArgs(id: String = "neon-blob", name: String = "Neon blob") -> PackObject {
        WidgetJson.object([
            ("kind", .string("sprite")),
            ("pack", .object(WidgetJson.object([
                ("id", .string(id)), ("name", .string(name)),
                ("description", .string("A small neon blob that walks the taskbar.")),
            ]))),
            ("sprite", TJ.parse(fixture("good-sprite-pixel", "sprite.json"))),
            ("sheets", .object(WidgetJson.object([("sheet.json", TJ.parse(fixture("good-sprite-pixel", "sheet.json")))]))),
            ("note", .string("A tiny friend in your Desk's colors.")),
        ])
    }

    static func paletteArgs() -> PackObject {
        TJ.obj("""
        {"kind":"palette","pack":{"id":"dusk","name":"Dusk","description":"Two evening gradients."},
         "palettes":[{"name":"dusk","colors":["#ff2a6d","#d16ba5","#05d9e8"],"mood":"late drive"},{"name":"ember","colors":["#ff8a3d","#c2410c"]}]}
        """)
    }

    static func effectsArgs() -> PackObject {
        TJ.obj("""
        {"kind":"effects","pack":{"id":"calm","name":"Calm","description":"A slow breath."},
         "effects":{"effects":[{"name":"breathe","type":"pulse","intensity":0.35,"period_ms":4000,"color":"ink"}]}}
        """)
    }

    private func messages(_ b: WidgetBuild) -> String { b.reasons.map(\.description).joined(separator: "\n") }

    private func has(_ b: WidgetBuild, _ field: String?, _ needle: String? = nil, sourceLocation: SourceLocation = #_sourceLocation) {
        #expect(b.reasons.contains { r in
            (field == nil || r.field == field) && (needle == nil || r.message.contains(needle!))
        }, "no reason (\(field ?? "any"), \(needle ?? "any")) in: \(messages(b))", sourceLocation: sourceLocation)
    }

    private func text(_ pack: WidgetProposalPack, _ rel: String) -> String { String(decoding: pack.files[rel]!, as: UTF8.self) }

    // MARK: - The format constants

    @Test func theFormatLevelIsThePackFormatsNotTheAppVersion() {
        #expect(WidgetProposal.sanduhrMin == "3.8.0")
        #expect(WidgetProposal.requestFile == "widget-request.json")
        #expect(WidgetProposal.resultFile == "widget-result.json")
        #expect(WidgetProposal.schemaVersion == 1)
        #expect(WidgetProposal.maxAge == 600)
        #expect(WidgetProposal.maxRequestBytes == 4 * 1024 * 1024)
        #expect(WidgetProposal.idPrefix == "my-")
        #expect(WidgetProposal.kinds == ["sprite", "palette", "effects"])
        #expect(WidgetProposal.licenses == ["CC0-1.0", "CC-BY-4.0", "MIT", "Apache-2.0", "BSD-3-Clause", "ISC"])
        #expect(WidgetAnswer.statuses == ["previewing", "applied", "rejected"])
    }

    // MARK: - The server's half

    @Test func aSpriteIsBuiltAsAWholePackWithItsIdNamespacedAndPassesTheStrictValidator() throws {
        let build = WidgetProposal.build(args: Self.spriteArgs(), now: Self.now)
        #expect(build.ok, "\(messages(build))")
        let pack = try #require(build.pack)
        #expect(pack.id == "my-neon-blob")
        #expect(pack.kind == "sprite")
        #expect(WidgetProposal.ordinalSorted(Array(pack.files.keys)) == ["LICENSE", "pack.json", "sheet.json", "sprite.json"])
        let manifest = TJ.text(pack.files["pack.json"]!)
        #expect(TJ.get(manifest, ["id"])?.string == "my-neon-blob")
        #expect(TJ.get(manifest, ["license"])?.string == "CC0-1.0")
        #expect(TJ.get(manifest, ["sanduhr_min"])?.string == "3.8.0")
        #expect(TJ.get(manifest, ["author"])?.string == "Made here")
        #expect(TJ.get(manifest, ["files"])?.array?.compactMap(\.string) == ["sheet.json", "sprite.json"])
        #expect(text(pack, "LICENSE").contains("CC0 1.0 Universal"))
        #expect(PackValidator.validate(pack.files, folderName: "my-neon-blob", strict: true).ok)
    }

    @Test func packJsonIsWrittenTheWayWindowsWritesIt() throws {
        let pack = try #require(WidgetProposal.build(args: Self.spriteArgs(), now: Self.now).pack)
        let expected = """
        {
          "schema_version": 1,
          "id": "my-neon-blob",
          "name": "Neon blob",
          "version": "1.0.0",
          "author": "Made here",
          "license": "CC0-1.0",
          "kind": "sprite",
          "sanduhr_min": "3.8.0",
          "description": "A small neon blob that walks the taskbar.",
          "files": [
            "sheet.json",
            "sprite.json"
          ]
        }

        """
        #expect(text(pack, "pack.json") == expected)
        #expect(text(pack, "LICENSE").hasPrefix("CC0 1.0 Universal\n\nCopyright (c) 2026 Made here\n\n"))
        let sprite = text(pack, "sprite.json")
        #expect(sprite.hasPrefix("{\n  \"schema_version\": 2,") || sprite.contains("\n  \"schema_version\": 2"))
        #expect(sprite.hasSuffix("}\n"))
    }

    @Test func anIdAlreadyNamespacedIsNotPrefixedTwice() {
        #expect(WidgetProposal.namespacedId("my-blob") == "my-blob")
        #expect(WidgetProposal.namespacedId("blob") == "my-blob")
        #expect(WidgetProposal.build(args: Self.spriteArgs(id: "my-neon"), now: Self.now).pack?.id == "my-neon")
    }

    @Test(arguments: ["CC0-1.0", "CC-BY-4.0", "MIT", "Apache-2.0", "BSD-3-Clause", "ISC"])
    func everyLicenseOnOfferWritesALicenseTheValidatorAccepts(license: String) {
        let args = TJ.set(Self.paletteArgs(), ["pack", "license"], .string(license))
        let build = WidgetProposal.build(args: args, now: Self.now)
        #expect(build.ok, "\(messages(build))")
        #expect(build.pack?.manifest.license == license)
    }

    @Test func palettesAndEffectsBuildFromTheListAloneOrTheWholeFile() {
        let palette = WidgetProposal.build(args: Self.paletteArgs(), now: Self.now)
        #expect(palette.ok, "\(messages(palette))")
        #expect(WidgetProposal.ordinalSorted(Array(palette.pack!.files.keys)) == ["LICENSE", "pack.json", "palettes.json"])
        let effects = WidgetProposal.build(args: Self.effectsArgs(), now: Self.now)
        #expect(effects.ok, "\(messages(effects))")
        #expect(effects.pack?.id == "my-calm")
    }

    @Test(arguments: [("theme", "propose_theme"), ("font", "fonts are binaries"), ("music", "must be one of sprite, palette, effects")])
    func themesFontsAndUnknownKindsAreRefused(kind: String, expected: String) {
        let build = WidgetProposal.build(args: TJ.set(Self.paletteArgs(), ["kind"], .string(kind)), now: Self.now)
        #expect(!build.ok)
        has(build, "kind", expected)
    }

    @Test func aPngSheetIsRefusedWhereverItIsNamed() {
        let named = TJ.set(Self.spriteArgs(), ["sprite", "sheet"], .string("sprite.png"))
        has(WidgetProposal.build(args: named, now: Self.now), "sprite", "never supplies an image")

        let keyed = TJ.set(Self.spriteArgs(), ["sheets"], .object(WidgetJson.object([("sheet.png", .object(PackObject()))])))
        has(WidgetProposal.build(args: keyed, now: Self.now), "sheets", "no PNG")
    }

    @Test func filesOrBinariesCannotBeSmuggledInAsArguments() {
        var args = Self.paletteArgs()
        args = WidgetJson.setting(args, "files", .object(WidgetJson.object([("art.png", .string("iVBORw0KGgo="))])))
        args = WidgetJson.setting(args, "font", .string("x.ttf"))
        let build = WidgetProposal.build(args: args, now: Self.now)
        #expect(!build.ok)
        has(build, "arguments", "files")

        let pack = TJ.set(Self.paletteArgs(), ["pack", "files"], TJ.parse("[\"palettes.json\", \"evil.png\"]"))
        has(WidgetProposal.build(args: pack, now: Self.now), "pack", "files")
    }

    @Test func anotherKindsFilesDoNotBelong() {
        let args = WidgetJson.setting(Self.paletteArgs(), "effects", .array([]))
        has(WidgetProposal.build(args: args, now: Self.now), "effects")
    }

    @Test(arguments: [("name", "Claude's buddy"), ("description", "An Anthropic mascot"), ("author", "Opus"), ("id", "haiku-cat")])
    func theTrademarkRuleRefusesThePack(field: String, value: String) {
        let args = TJ.set(Self.spriteArgs(), ["pack", .key(field)], .string(value))
        let build = WidgetProposal.build(args: args, now: Self.now)
        #expect(!build.ok)
        has(build, nil, "trademark")
    }

    @Test func aTrademarkInsideTheArtsJsonIsRefusedToo() {
        let args = TJ.set(Self.spriteArgs(), ["sprite", "animations", "claude_wave"], TJ.parse("[0]"))
        has(WidgetProposal.build(args: args, now: Self.now), nil, "trademark")
    }

    @Test func aPixelSheetThatBreaksTheGrammarIsRefusedWithTheValidatorsSentence() {
        let white = TJ.set(Self.spriteArgs(), ["sheets", "sheet.json", "palette", 0], .string("#FFFFFF"))
        has(WidgetProposal.build(args: white, now: Self.now), nil, "pure white")

        let shortRow = TJ.set(Self.spriteArgs(), ["sheets", "sheet.json", "frames", 0, 0], .string("..00"))
        has(WidgetProposal.build(args: shortRow, now: Self.now), nil, "expected 8 (frame_w)")

        let badIndex = TJ.set(Self.spriteArgs(), ["sheets", "sheet.json", "frames", 0, 0], .string("..9999.."))
        has(WidgetProposal.build(args: badIndex, now: Self.now), nil, "palette has only 3 colors")
    }

    @Test func anUnknownLicenseAndAMissingPackAreRefused() {
        let args = TJ.set(Self.paletteArgs(), ["pack", "license"], .string("GPL-3.0"))
        has(WidgetProposal.build(args: args, now: Self.now), "pack.license")
        has(WidgetProposal.build(args: TJ.obj("{\"kind\":\"palette\"}"), now: Self.now), "pack")
        #expect(!WidgetProposal.build(args: nil, now: Self.now).ok)
    }

    @Test func theRefusalSentencesAreWindowsWordsForWord() {
        let none = WidgetProposal.build(args: nil, now: Self.now)
        #expect(none.reasons.map(\.description) == [
            "kind: must be one of sprite, palette, effects",
            "pack: must be an object: {id, name, description, version?, author?, license?}",
        ])
        let odd = WidgetProposal.build(args: TJ.obj("{\"zeta\":1,\"alpha\":2,\"kind\":\"palette\",\"pack\":{\"id\":\"a\",\"name\":\"A\",\"description\":\"d\",\"license\":\"GPL\",\"x\":1}}"), now: Self.now)
        #expect(odd.reasons.map(\.description) == [
            "arguments: unknown argument(s): alpha, zeta. The arguments are kind, pack, sprite, sheets, palettes, effects and note. Claude never supplies a PNG or a font.",
            "pack: unknown key(s): x. pack.json's files, kind, schema_version and sanduhr_min are written by Sanduhr.",
            "pack.license: must be one of CC0-1.0, CC-BY-4.0, MIT, Apache-2.0, BSD-3-Clause, ISC (default CC0-1.0)",
            "palettes: must be palettes.json's object {schema_version: 1, palettes: [...]} or the list alone",
        ])
        let note = WidgetProposal.build(args: TJ.set(Self.paletteArgs(), ["note"], .string("two\nlines")), now: Self.now)
        #expect(note.reasons.map(\.description) == ["note: must be one line of at most 200 characters"])
        let sheets = WidgetProposal.build(args: WidgetJson.setting(Self.spriteArgs(), "sheets", .object(PackObject())), now: Self.now)
        #expect(sheets.reasons.map(\.description) == ["sheets: must be an object of pixel sheets: path (ending .json) to {palette, frame_w, frame_h, frames}"])
    }

    @Test func everyLicenseTextHoldsItsHolderAndTheValidatorsPhrase() {
        for (spdx, phrase) in PackValidator.licenses where WidgetProposal.licenses.contains(spdx) {
            let text = WidgetProposal.licenseText(spdx, "Ana", 2031).lowercased()
            #expect(text.contains("copyright (c) 2031 ana"), "\(spdx)")
            #expect(text.contains(phrase), "\(spdx)")
        }
        #expect(WidgetProposal.licenseText("MIT", "A", 2026).hasPrefix("MIT License\n\nCopyright (c) 2026 A\n\nPermission is hereby granted"))
        #expect(WidgetProposal.licenseText("MIT", "A", 2026).hasSuffix("DEALINGS IN THE SOFTWARE.\n"))
        #expect(WidgetProposal.licenseText("Apache-2.0", "A", 2026).contains("\n\n    http://www.apache.org/licenses/LICENSE-2.0\n\n"))
    }

    // MARK: - The request file and the widget's own check

    @Test func theRequestRoundTripsAndTheWidgetChecksThePackAgain() throws {
        let pack = try #require(WidgetProposal.build(args: Self.spriteArgs(), now: Self.now).pack)
        let json = WidgetJson.compact(.object(WidgetProposal.requestJson(requestId: "r1", pack: pack, note: "a note", now: Self.now)))
        let request = WidgetProposal.decode(Data(json.utf8), now: Self.now.addingTimeInterval(60))
        #expect(request.kind == .proposal)
        #expect(request.id == "r1")
        #expect(request.note == "a note")
        #expect(request.pack?.id == pack.id)
        for (rel, bytes) in pack.files { #expect(request.pack?.files[rel] == bytes, "\(rel)") }
    }

    @Test func theRequestFileKeepsItsKeysInOrder() throws {
        let pack = try #require(WidgetProposal.build(args: Self.paletteArgs(), now: Self.now).pack)
        let json = WidgetJson.compact(.object(WidgetProposal.requestJson(requestId: "r0", pack: pack, note: "  ", now: Self.now)))
        #expect(json.hasPrefix("{\"schema_version\":1,\"id\":\"r0\",\"requested_at\":\"2026-10-22T18:00:00.000Z\",\"pack_id\":\"my-dusk\",\"files\":{\"LICENSE\":"))
    }

    @Test func theWidgetRefusesARequestTheServerShouldNeverHaveWritten() throws {
        let pack = try #require(WidgetProposal.build(args: Self.spriteArgs(), now: Self.now).pack)
        func decode(_ o: PackObject) -> WidgetRequest {
            WidgetProposal.decode(Data(WidgetJson.compact(.object(o)).utf8), now: Self.now)
        }
        func request(_ id: String) -> PackObject { WidgetProposal.requestJson(requestId: id, pack: pack, note: nil, now: Self.now) }

        // A PNG slipped into the files.
        let a = decode(TJ.set(request("r2"), ["files", "preview.png"], .string("\u{0089}PNG")))
        #expect(a.kind == .refused)
        #expect(a.reasons.contains { $0.message.contains("JSON only") })

        // A trademark edited in after the server's check.
        let packText = text(pack, "pack.json").replacingOccurrences(of: "Neon blob", with: "Claude blob")
        let b = decode(TJ.set(request("r3"), ["files", "pack.json"], .string(packText)))
        #expect(b.kind == .refused)
        #expect(b.reasons.contains { $0.message.contains("trademark") })

        // An id outside the my- namespace (a catalog pack's).
        var hijack = TJ.set(request("r4"), ["pack_id"], .string("hourglass"))
        hijack = TJ.set(hijack, ["files", "pack.json"], .string(text(pack, "pack.json").replacingOccurrences(of: "my-neon-blob", with: "hourglass")))
        let c = decode(hijack)
        #expect(c.kind == .refused)
        #expect(c.reasons.contains { $0.field == "pack.id" })

        // A file that is not text.
        #expect(decode(TJ.set(request("r5"), ["files", "sheet.json"], TJ.parse("[1, 2, 3]"))).kind == .refused)
    }

    @Test func staleUnreadableOrIdlessRequestsAreDroppedUnanswered() throws {
        let pack = try #require(WidgetProposal.build(args: Self.paletteArgs(), now: Self.now).pack)
        let json = Data(WidgetJson.compact(.object(WidgetProposal.requestJson(requestId: "r6", pack: pack, note: nil, now: Self.now))).utf8)
        #expect(WidgetProposal.decode(json, now: Self.now.addingTimeInterval(11 * 60)).kind == .discard)
        #expect(WidgetProposal.decode(json, now: Self.now.addingTimeInterval(10 * 60)).kind == .proposal)
        #expect(WidgetProposal.decode(Data("not json".utf8), now: Self.now).kind == .discard)
        #expect(WidgetProposal.decode(Data("{\"schema_version\":1}".utf8), now: Self.now).kind == .discard)
        let v2 = TJ.set(WidgetProposal.requestJson(requestId: "r7", pack: pack, note: nil, now: Self.now), ["schema_version"], WidgetJson.int(2))
        let refused = WidgetProposal.decode(Data(WidgetJson.compact(.object(v2)).utf8), now: Self.now)
        #expect(refused.kind == .refused)
        #expect(refused.reasons.map(\.description) == ["schema_version: this Sanduhr reads widget requests of schema 1"])
        let noStamp = WidgetProposal.decode(Data("{\"id\":\"x\"}".utf8), now: Self.now)
        #expect(noStamp.reasons.map(\.description) == ["requested_at: is missing or not a date"])
        let big = Data(repeating: 0x20, count: WidgetProposal.maxRequestBytes + 1)
        #expect(WidgetProposal.decode(big, now: Self.now).kind == .discard)
    }

    @Test func theResultFileCarriesTheStatusReasonsPackAndCompanion() throws {
        let applied = TJ.text(WidgetProposal.resultJson(id: "r8", answer: WidgetAnswer(WidgetAnswer.applied, packId: "my-blob", companion: true), now: Self.now))
        #expect(TJ.get(applied, ["id"])?.string == "r8")
        #expect(TJ.get(applied, ["result", "status"])?.string == "applied")
        #expect(TJ.get(applied, ["result", "pack_id"])?.string == "my-blob")
        #expect(TJ.get(applied, ["result", "companion"])?.bool == true)
        let refused = TJ.text(WidgetProposal.resultJson(id: "r9", answer: .refused([DeskReason("widget", "dismissed by the user")]), now: Self.now))
        #expect(TJ.get(refused, ["result", "reasons", 0, "message"])?.string == "dismissed by the user")
        #expect(TJ.get(refused, ["result", "reasons", 0, "field"])?.string == "widget")
    }

    @Test func theResultFileIsCompactAndInWindowsKeyOrder() {
        let raw = String(decoding: WidgetProposal.resultJson(id: "r1", answer: .notReady, now: Self.now), as: UTF8.self)
        #expect(raw == "{\"completed_at\":\"2026-10-22T18:00:00.000Z\",\"id\":\"r1\",\"result\":{\"status\":\"rejected\",\"reason\":\"not_ready\","
            + "\"reasons\":[{\"field\":\"widget\",\"message\":\"not_ready: this version of Sanduhr checks widgets but cannot preview them here\"}]}}")
    }

    @Test func theJsonWriterEscapesAsTheDefaultEncoderDoes() {
        let tree = PackValue.string("a<b>&'+`\"\\/\u{7F}é😀\n")
        #expect(WidgetJson.compact(tree) == "\"a\\u003Cb\\u003E\\u0026\\u0027\\u002B\\u0060\\u0022\\\\/\\u007F\\u00E9\\uD83D\\uDE00\\n\"")
        #expect(WidgetJson.indented(.array([])) == "[]")
        #expect(WidgetJson.indented(.object(PackObject())) == "{}")
    }
}
