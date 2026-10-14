import Foundation
import Testing
@testable import Sanduhr

/// Item 75, slice B: the Swift band draws every case the meters mod's own code drew
/// (`Fixtures/band-frames/frames.json`, written by `mac/integrations/band-fixtures/generate.ts`),
/// cell for cell: text, color and attributes.
@Suite("Mod Studio: the band's frames match the mod's")
struct BandFrameTests {
    struct Fixture: Decodable {
        struct Input: Decodable {
            var now: Double
            var bars: String
            var style: String
            var label: String
            var moving: Bool
            var width: Int
            var maxRows: Int
            var tzOffsetMinutes: Int
            var seat: Bool
            var sweeps: [String: Double]
        }
        struct Case: Decodable {
            var name: String
            var snapshot: String?
            var band: String?
            var input: Input
            var lines: [[BandFrame.Cell]]
        }
        var cases: [Case]
    }

    static func fixture() throws -> Fixture {
        let url = try #require(Bundle.module.url(forResource: "Fixtures/band-frames/frames", withExtension: "json"))
        return try JSONDecoder().decode(Fixture.self, from: Data(contentsOf: url))
    }

    @Test func everyCaseDrawsAsTheModDrawsIt() throws {
        let fixture = try Self.fixture()
        #expect(fixture.cases.count >= 30)
        for c in fixture.cases {
            let i = c.input
            let input = BandFrame.Input(
                now: i.now, bars: try #require(BandFrame.Bars(rawValue: i.bars)), style: try #require(BandFrame.Style(rawValue: i.style)),
                label: i.label, moving: i.moving, width: i.width, maxRows: i.maxRows,
                tzOffsetMinutes: i.tzOffsetMinutes, seat: i.seat, sweeps: i.sweeps)
            let snap = c.snapshot.flatMap { BandFrame.parseSnapshot(Data($0.utf8)) }
            let file = c.band.flatMap { BandFrame.parseBand(Data($0.utf8)) }
            let lines = BandFrame.lines(snap, file, input)
            #expect(lines == c.lines, "\(c.name)")
        }
    }

    @Test func aStylesJSONReadsAsTheBandReadsIt() {
        #expect(BandFrame.parseStyle(["ink": ["#F00", "00ff00"], "font": "script", "bold": true]) ==
            BandFrame.Look(ink: ["#ff0000", "#00ff00"], font: "script", bold: true))
        #expect(BandFrame.parseStyle(["ink": []]) == nil)
        #expect(BandFrame.parseStyle(["bold": 1]) == nil)
        #expect(BandFrame.parseStyle(["glow": true]) == nil)
        #expect(BandFrame.parseStyle(["font": "comic"]) == nil)
    }
}
