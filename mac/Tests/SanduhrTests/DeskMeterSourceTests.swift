import Foundation
import Testing
@testable import Sanduhr

/// Desk meter pieces pick a source (agent sources, item 8). The layout word's `src=` field: every word
/// written before 2.46 reads Combined and writes back unchanged, the new field parses and writes (after
/// the other fields, before the display), and Combined writes nothing. Made-up numbers only.

private let uuid = "6F1A2B3C-0000-4D5E-8F90-A1B2C3D4E5F6"

/// Every layout word shape a 2.45.0 build writes: bare, size, tilt, style, display, the long forms, the
/// standard layouts, and the shared fixtures of the older suites.
private let words245: [String] = DeskArrangementStringTests.older.prefix(5).map { $0 } + DeskFoldStyleTests.saved + [
    DeskLayout.standard,
    "claude:bl", "meters:tr", "claude:bl:1.2", "meters:tr:1:12", "claude:bl:1.2:-15", "meters:tr:1:0:ring",
    "claude:bl:1.2:8:slim", "meters:br:1.4:0:ticks", "telemetry:tr:1:0:ticks", "claude:bl:1:0:bar@all",
    "clock:tl:1:0:bar@all", "clock:tl:1.2:0:bar@all", "meters:tr:1:0:bar@\(uuid)", "claude:bl:1.2:-15:slim@\(uuid)",
    "meters:tr:1:0:ring@all claude:bl:1.2:8:slim@\(uuid) clock:tl",
    "clock:bl:1:-15 claude:bl:1.2:-15 meters:tr:1:0:ring telemetry:tr:1:0:ticks weather:mr",
]

@Suite("Desk layout word: source")
struct DeskMeterSourceWordTests {
    @Test(arguments: words245)
    func aWordFrom245ReadsCombinedAndWritesUnchanged(_ layout: String) {
        let a = DeskArrangement(layout)
        #expect(a.pieces.allSatisfy { $0.source == .combined })
        #expect(a.string == layout)
    }

    @Test func onlyTheUsagePiecesTakeASource() {
        #expect(DeskMeterSources.pieces == ["claude", "meters"])
        #expect(DeskMeterSource.allCases.map(DeskMeterSources.name) == ["Combined", "Claude", "Codex"])
        #expect(DeskMeterSources.for("clock", .codex) == .combined)
        #expect(DeskMeterSources.for("telemetry", .claude) == .combined)
        #expect(DeskMeterSources.for("meters", .codex) == .codex)
    }

    @Test(arguments: [
        ("claude:bl:src=codex", DeskMeterSource.codex),
        ("meters:tr:src=claude", DeskMeterSource.claude),
        ("meters:tr:1.2:src=codex", DeskMeterSource.codex),
        ("meters:tr:1:12:src=codex", DeskMeterSource.codex),
        ("meters:tr:1:0:ring:src=codex", DeskMeterSource.codex),
        ("claude:bl:1.2:-15:slim:src=claude", DeskMeterSource.claude),
        ("meters:tr:src=combined", DeskMeterSource.combined),
    ])
    func theSourceFieldParsesAndRoundTrips(_ word: String, _ source: DeskMeterSource) {
        let a = DeskArrangement(word)
        #expect(a.pieces.count == 1)
        #expect(a.pieces[0].source == source)
        // A combined word is written without the field; every other word is written back as it was.
        #expect(a.string == (source == .combined ? "meters:tr" : word))
    }

    @Test func theFieldKeepsTheOtherFieldsInTheirPlaces() {
        let a = DeskArrangement("meters:tr:src=codex:1.3:0:ring")
        #expect(a.placement("meters") == DeskPlacement(widget: "meters", anchor: .tr, scale: 1.3, tilt: 0, style: .ring, source: .codex))
        // Any place reads the same.
        #expect(DeskArrangement("meters:tr:1.3:0:ring:src=codex").placement("meters") == a.placement("meters"))
        #expect(DeskArrangement("src=codex:meters:tr:1.3:0:ring").placement("meters") == a.placement("meters"))
    }

    @Test func theSourceRidesBeforeTheDisplay() {
        let a = DeskArrangement("meters:tr:1:0:ring:src=codex@all claude:bl:src=claude@\(uuid)")
        #expect(a.placement("meters")?.monitor == .all)
        #expect(a.placement("meters")?.source == .codex)
        #expect(a.placement("claude")?.monitor == .id(uuid))
        #expect(a.placement("claude")?.source == .claude)
        #expect(a.string == "meters:tr:1:0:ring:src=codex@all claude:bl:1:0:bar:src=claude@\(uuid)")
    }

    @Test func combinedWritesNoFieldSoAWordWritesExactlyAsToday() {
        var a = DeskArrangement("meters:tr:1.3:0:ring@all")
        a.setSource("meters", .codex)
        #expect(a.string == "meters:tr:1.3:0:ring:src=codex@all")
        a.setSource("meters", .combined)
        #expect(a.string == "meters:tr:1.3:0:ring@all")
        var b = DeskArrangement(DeskLayout.standard)
        b.setSource("claude", .combined)
        #expect(b.string == DeskLayout.standard)
    }

    @Test(arguments: ["meters:tr:src=gpt", "meters:tr:src=", "meters:tr:src=CODEX", "clock:bl:src=codex",
                      "telemetry:tr:src=claude", "weather:mr:src=codex"])
    func anUnknownSourceOrOneOnAPieceWithoutOneIsCombined(_ word: String) {
        let a = DeskArrangement(word)
        #expect(a.pieces.count == 1)
        #expect(a.pieces[0].source == .combined)
        #expect(!a.string.contains("src="))
    }

    @Test func aWordWithOnlyTheFieldIsNoWord() {
        #expect(DeskArrangement("src=codex").pieces.isEmpty)
        #expect(DeskArrangement("meters:src=codex").pieces.isEmpty)
    }

    @Test func anOlderBuildReadsTheSizeAndTiltAndIgnoresTheField() {
        // A 2.45 build splits on ":" and reads fields 1 to 3; a field it can't read is the default.
        func read245(_ word: String) -> (anchor: String, scale: Double, tilt: Double) {
            let bits = word.split(separator: ":").map(String.init)
            return (bits[1], bits.count >= 3 ? Double(bits[2]) ?? 1 : 1, bits.count >= 4 ? Double(bits[3]) ?? 0 : 0)
        }
        var a = DeskArrangement("meters:tr:1.3:12")
        a.setSource("meters", .codex)
        let read = read245(a.string.split(separator: "@").first.map(String.init) ?? a.string)
        #expect(read.anchor == "tr" && read.scale == 1.3 && read.tilt == 12)
        var b = DeskArrangement("meters:tr")
        b.setSource("meters", .codex)
        let bare = read245(b.string)
        #expect(bare.anchor == "tr" && bare.scale == 1 && bare.tilt == 0)
    }

    @Test func theSourceIsThePiecesOwnAndMovesWithIt() {
        var a = DeskArrangement("clock:bl claude:bl:src=codex meters:tr:1.2:0:slim:src=claude")
        a.place("claude", at: .tc)
        #expect(a.source(of: "claude") == .codex)
        a.setDisplay("meters", .all)
        #expect(a.source(of: "meters") == .claude)
        a.setScale("meters", 1.4)
        a.setStyle("meters", .ring)
        a.setTilt("meters", 5)
        #expect(a.source(of: "meters") == .claude)
        a.move("claude", onto: "meters")
        #expect(a.source(of: "claude") == .codex)
        a.put("claude", at: .mr, before: nil)
        #expect(a.source(of: "claude") == .codex)
        a.hide("claude")
        a.show("claude")
        #expect(a.source(of: "claude") == .combined)   // a hidden piece has none
        a.setSource("clock", .codex)
        #expect(a.source(of: "clock") == .combined)
        a.setSource("watchers", .codex)                  // not placed: nothing changes
        #expect(a.placement("watchers") == nil)
    }
}

@Suite("Desk meter source: the design, the state file and the Settings rule")
struct DeskMeterSourceDesignTests {
    @Test func theRefusalsNameThePiecesAndTheWords() {
        #expect(DeskMeterSources.refusal(widget: "meters", word: "codex") == nil)
        #expect(DeskMeterSources.refusal(widget: "claude", word: " Claude ") == nil)
        #expect(DeskMeterSources.refusal(widget: "clock", word: "codex") == "only claude, meters take a source")
        #expect(DeskMeterSources.refusal(widget: "meters", word: "gpt") == "must be one of combined, claude, codex")
        #expect(DeskMeterSources.refusal(widget: "meters", word: nil) == "must be one of combined, claude, codex")
    }

    @Test func aDesignMergesTheSourceAndKeepsItWhenSilent() {
        let current = DeskDesignLook(arrangement: DeskArrangement("clock:bl meters:tr:1:0:ring:src=codex claude:bl"),
                                     font: nil, ink: "ffffff", plate: false, shadow: true)
        // A design that names no source leaves each piece's own.
        let quiet = DeskDesignApply.mergeLayout(current.arrangement, [DeskPieceLayout(piece: "meters", anchor: .tl, size: 1.2)])
        #expect(quiet.source(of: "meters") == .codex)
        #expect(quiet.placement("meters")?.anchor == .tl)
        let loud = DeskDesignApply.mergeLayout(current.arrangement, [DeskPieceLayout(piece: "meters", source: .claude),
                                                                    DeskPieceLayout(piece: "claude", source: .codex)])
        #expect(loud.source(of: "meters") == .claude)
        #expect(loud.source(of: "claude") == .codex)
    }

    @Test func theWhatChangedLinesSayWhatItShows() {
        let a = DeskDesignLook(arrangement: DeskArrangement("meters:tr claude:bl"), font: nil, ink: "ffffff", plate: false, shadow: true)
        var b = a
        b.arrangement.setSource("meters", .codex)
        #expect(DeskDesignApply.changes(from: a, to: b).contains { $0.hasSuffix(": shows Codex") })
        var c = a
        c.arrangement.hide("meters")
        c.arrangement.setSource("claude", .claude)
        #expect(DeskDesignApply.changes(from: a, to: c).contains { $0.hasSuffix(": shows Claude") })
        // A piece shown again with a source says so; a combined one adds nothing.
        let d = DeskDesignLook(arrangement: DeskArrangement("claude:bl"), font: nil, ink: "ffffff", plate: false, shadow: true)
        var e = d
        e.arrangement.place("meters", at: .tr)
        #expect(DeskDesignApply.changes(from: d, to: e).allSatisfy { !$0.contains("shows") })
        e.arrangement.setSource("meters", .codex)
        #expect(DeskDesignApply.changes(from: d, to: e).contains { $0.hasSuffix(", shows Codex") })
    }

    @Test func theSettingsRuleOffersShowsOnlyWithCodexOn() {
        #expect(DeskMeterSources.offers("meters", codexOn: true))
        #expect(DeskMeterSources.offers("claude", codexOn: true))
        #expect(!DeskMeterSources.offers("meters", codexOn: false))
        #expect(!DeskMeterSources.offers("clock", codexOn: true))
        #expect(!DeskMeterSources.offers("telemetry", codexOn: true))
    }

    @Test func theStateFileReportsTheSourceOnThePiecesThatTakeOne() throws {
        let suite = "sanduhr.tests.desk-source.\(UUID().uuidString)"
        let d = UserDefaults(suiteName: suite)!
        defer { d.removePersistentDomain(forName: suite) }
        d.set("clock:bl meters:tr:1:0:ring:src=codex claude:bl telemetry:tr", forKey: "layout")
        let data = DeskStateExport.stateJson(defaults: d, installedFonts: [], writerVersion: "2.46.0", fontAvailable: { _ in true })
        let root = try #require(JSONSerialization.jsonObject(with: data) as? [String: Any])
        let layout = try #require((root["design"] as? [String: Any])?["layout"] as? [String: [String: Any]])
        #expect(layout["meters"]?["source"] as? String == "codex")
        #expect(layout["claude"]?["source"] as? String == "combined")
        #expect(layout["clock"]?["source"] == nil)
        #expect(layout["telemetry"]?["source"] == nil)
        let limits = try #require(root["limits"] as? [String: Any])
        let piece = try #require(limits["piece_source"] as? [String: Any])
        #expect(piece["pieces"] as? [String] == ["claude", "meters"])
        #expect(piece["values"] as? [String] == ["combined", "claude", "codex"])
        #expect(piece["default"] as? String == "combined")
    }
}
