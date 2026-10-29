import Foundation
import Testing
@testable import Sanduhr

/// The layout word's fourth and fifth fields (2.37.0 item 1), ported case for case from the
/// Windows suites (DeskRideAlongsTests: the fold's layout string; DeskMeterStylesTests: the style
/// word, the pieces that have one, the refusals). A tilt is a stack's slant in whole degrees; a
/// style is the piece's own look. Every 2.36 layout reads and writes byte for byte as before.

@Suite("Desk layout word: tilt and style")
struct DeskFoldStyleTests {
    // MARK: Byte-identical for every layout written before the fourth and fifth fields

    /// The standard layouts, the first five older shapes of DeskArrangementStringTests (the rest hold
    /// words that are dropped on read, as before), sizes, every anchor and
    /// widget, and the saved shapes a person's Settings and Arrange mode write.
    static let saved = Array(DeskArrangementStringTests.older.prefix(5)) + [
        DeskLayout.standard,
        "clock:bl claude:bl:1.4 meters:tr",
        "message:tl:0.6 clock:tc:1.2 meters:bc:1.6 meetings:ml:0.8 watchers:mr nowPlaying:br:1.1 companion:bc:1.3",
        "clock:tr claude:tr:1.5 meters:tr:0.7",
    ]

    @Test(arguments: saved)
    func aLayoutFrom236ReadsAndWritesUnchanged(_ layout: String) {
        let a = DeskArrangement(layout)
        #expect(a.string == layout)
        #expect(a.pieces.allSatisfy { $0.tilt == 0 && $0.style == .bar })
        #expect(a.tiltConflicts.isEmpty)
    }

    @Test func theRealDefaultLayoutsAreUnchanged() {
        for layout in [DeskLayout.standard, "message:tl clock:bl claude:bl meetings:bl"] {
            #expect(DeskArrangement(layout).string == layout)
        }
        // A fresh install's layout, as Settings writes it.
        #expect(DeskArrangement(DeskLayout.standard).pieces.map(\.widget) == ["message", "clock", "claude", "meetings"])
    }

    // MARK: The tilt field

    @Test func aTiltIsTheFourthFieldOfAWordAndRoundTrips() {
        let a = DeskArrangement("clock:bl:1:-18 claude:bl:1.2:-18 meters:tr")
        #expect(a.placement("clock") == DeskPlacement(widget: "clock", anchor: .bl, scale: 1, tilt: -18))
        #expect(a.placement("claude") == DeskPlacement(widget: "claude", anchor: .bl, scale: 1.2, tilt: -18))
        #expect(a.placement("meters")?.tilt == 0)
        #expect(a.string == "clock:bl:1:-18 claude:bl:1.2:-18 meters:tr")
    }

    @Test(arguments: [
        ("33", 20.0), ("-90", -20.0), ("7.6", 8.0), ("-0.2", 0.0), ("NaN", 0.0), ("tilted", 0.0),
        ("20", 20.0), ("-20", -20.0), ("0", 0.0), ("Infinity", 0.0),
    ])
    func anOddTiltIsClampedToWholeDegreesOrUpright(_ word: String, _ expected: Double) {
        let t = DeskArrangement("clock:bl:1:\(word)").placement("clock")?.tilt
        #expect(t == expected)
        // Never -0: it must not write as "-0".
        #expect(DeskArrangement("clock:bl:1:\(word)").string == (expected == 0 ? "clock:bl" : "clock:bl:1:\(Int(expected))"))
    }

    @Test func aTiltHoldsTheSizePlaceSoAnOlderBuildStillReadsTheSize() {
        let a = DeskArrangement("clock:tl:1.4:12")
        #expect(a.placement("clock") == DeskPlacement(widget: "clock", anchor: .tl, scale: 1.4, tilt: 12))
        #expect(a.string == "clock:tl:1.4:12")
        // What an older build does with the word: the third field is still the size, the rest unread.
        #expect(a.string.split(separator: ":")[2] == "1.4")
        // A slant on a size of 1 writes the 1 to hold its place.
        #expect(DeskArrangement("clock:bl:1:12").string == "clock:bl:1:12")
    }

    @Test func setTiltSlantsTheWholeStackAndOnlyIt() {
        var a = DeskArrangement("clock:bl claude:bl meters:tr")
        a.setTilt("claude", -18)
        #expect(a.placement("clock")?.tilt == -18)
        #expect(a.placement("claude")?.tilt == -18)
        #expect(a.placement("meters")?.tilt == 0)
        #expect(a.stackTilt(.bl) == -18)
        #expect(a.stackTilt(.tc) == nil)
        a.setTilt("watchers", 10)   // not placed: nothing changes
        #expect(a.string == "clock:bl:1:-18 claude:bl:1:-18 meters:tr")
        a.setTilt("clock", 99)      // kept within range
        #expect(a.stackTilt(.bl) == 20)
        a.setTilt("clock", 0)
        #expect(a.string == "clock:bl claude:bl meters:tr")
    }

    @Test func aPieceThatJoinsAStackTakesItsSlantAndOneAloneKeepsItsOwn() {
        var a = DeskArrangement("clock:bl:1:-18 claude:bl:1:-18 meters:tr:1:12")
        a.place("meters", at: .bl)
        #expect(a.placement("meters")?.tilt == -18)
        a.place("meters", at: .tc)   // an empty anchor: it keeps the slant it had
        #expect(a.placement("meters")?.tilt == -18)

        var b = DeskArrangement("clock:bl claude:tr:1:15")
        b.put("clock", at: .tr, before: "claude")
        #expect(b.placement("clock")?.tilt == 15)

        var c = DeskArrangement("clock:bl claude:tr:1:15")
        c.move("clock", onto: "claude")
        #expect(c.placement("clock")?.tilt == 15)

        var d = DeskArrangement("clock:bl:1:9")
        d.show("meters")
        #expect(d.placement("meters")?.tilt == 9)   // joins the bottom left stack, the standard anchor
        var e = DeskArrangement("clock:tl:1:9")
        e.show("meters")
        #expect(e.placement("meters")?.tilt == 0)   // alone at its standard anchor: no slant of its own

        // Within one stack a move keeps the stack's slant; putting into an empty anchor keeps its own.
        var f = DeskArrangement("clock:bl:1:7 claude:bl:1:7")
        f.move("clock", onto: "claude")
        #expect(f.stackTilt(.bl) == 7)
        f.put("claude", at: .mr, before: nil)
        #expect(f.placement("claude")?.tilt == 7)
    }

    @Test func aStackWhoseWordsDisagreeSlantsByItsFirstPiece() {
        let a = DeskArrangement("clock:bl:1:5 claude:bl:1:6 meters:tr")
        #expect(a.stackTilt(.bl) == 5)
        #expect(a.tilt(of: "claude") == 5)
        #expect(a.tilt(of: "meters") == 0)
        #expect(a.tilt(of: "watchers") == nil)
        #expect(a.tiltConflicts == [DeskTiltConflict(anchor: .bl, pieces: ["clock", "claude"])])
    }

    // MARK: Two slants for one stack are refused

    @Test func twoSlantsForOneStackAreRefusedWithWindowsMessage() {
        let c = DeskArrangement.tiltConflicts([("clock", .bl, 5), ("claude", .bl, 6), ("meters", .tr, 5)])
        #expect(c.count == 1)
        #expect(c[0].field == "layout.clock.tilt")
        #expect(c[0].message == "the pieces at bl share one slant; give clock and claude the same tilt")
    }

    @Test func theSameSlantTwiceIsFineAndASilentPieceNeverConflicts() {
        #expect(DeskArrangement.tiltConflicts([("clock", .bl, 10), ("claude", .bl, 10)]).isEmpty)
        // A design that leaves a piece's tilt out says nothing about it.
        #expect(DeskArrangement.tiltConflicts([("clock", .bl, 10), ("claude", .bl, nil)]).isEmpty)
        #expect(DeskArrangement.tiltConflicts([("clock", .bl, 0), ("claude", .bl, 3)]).count == 1)
    }

    @Test func conflictsReportAtMostThreeStacksInPieceOrder() {
        let words: [(widget: String, anchor: DeskAnchor, tilt: Double?)] = [
            ("message", .tl, 1), ("clock", .tl, 2), ("claude", .tr, 1), ("meters", .tr, 2),
            ("meetings", .bl, 1), ("watchers", .bl, 2), ("nowPlaying", .br, 1), ("companion", .br, 2),
        ]
        let c = DeskArrangement.tiltConflicts(words)
        #expect(c.map(\.anchor) == [.tl, .tr, .bl])
    }

    // MARK: The style field

    @Test(arguments: [
        ("meters:tr:1:0:ring", DeskMeterStyle.ring),
        ("claude:bl:1:-18:ticks", DeskMeterStyle.ticks),
        ("claude:bl:1.2:18:slim", DeskMeterStyle.slim),
        ("meters:tr", DeskMeterStyle.bar),
        ("meters:tr:1.4", DeskMeterStyle.bar),
        ("meters:tr:1:12", DeskMeterStyle.bar),
    ])
    func aStyleIsTheFifthFieldOfAWordAndRoundTrips(_ word: String, _ style: DeskMeterStyle) {
        let a = DeskArrangement(word)
        #expect(a.pieces.count == 1)
        #expect(a.pieces[0].style == style)
        #expect(a.string == word)
    }

    @Test func aBarIsNeverWrittenSoEveryOlderLayoutReadsAndWritesAsItDid() {
        for layout in [DeskLayout.standard, "clock:bl:1:-18 claude:bl:1:-18 meters:tr:1.2", "clock:tl meters:tr:0.8"] {
            let a = DeskArrangement(layout)
            #expect(a.pieces.allSatisfy { $0.style == .bar })
            #expect(a.string == layout)
        }
        #expect(DeskArrangement("meters:tr:1:0:bar").string == "meters:tr")
    }

    @Test func aStyleHoldsTheSizeAndTiltPlacesSoAnOlderBuildReadsBothAndDrawsBars() {
        var a = DeskArrangement()
        a.place("meters", at: .tr)
        a.setStyle("meters", .ring)
        a.setScale("meters", 1.3)
        let word = a.string
        #expect(word == "meters:tr:1.3:0:ring")
        let bits = word.split(separator: ":")
        #expect(Double(bits[2]) == 1.3)
        #expect(Double(bits[3]) == 0)
    }

    @Test(arguments: ["meters:tr:1:0:donut", "meters:tr:1:0:RING", "clock:bl:1:0:ring", "launcher:tr:1:0:slim",
                      "watchers:tr:1:0:ticks", "claude:bl:1:0:"])
    func anUnknownStyleOrAStyleOnAPieceWithoutOneIsABar(_ word: String) {
        let a = DeskArrangement(word)
        #expect(a.pieces.count <= 1)
        #expect(a.pieces.allSatisfy { $0.style == .bar })
    }

    @Test func aStyleIsThePiecesOwnAndMovesWithIt() {
        var a = DeskArrangement("clock:bl claude:bl:1:0:slim meters:tr:1:0:ring")
        a.setStyle("clock", .ring)       // no style: stays a bar
        #expect(a.placement("clock")?.style == .bar)
        a.setStyle("watchers", .ring)    // not placed: nothing
        #expect(a.placement("watchers") == nil)
        a.place("meters", at: .bl)       // into a stack: keeps its style, takes the stack's slant
        #expect(a.placement("meters")?.style == .ring)
        a.move("claude", onto: "clock")
        #expect(a.placement("claude")?.style == .slim)
        a.put("meters", at: .tr, before: nil)
        #expect(a.placement("meters")?.style == .ring)
        a.setTilt("meters", 18)          // one slant per stack, a style per piece
        #expect(a.string.split(separator: " ").first { $0.hasPrefix("meters") } == "meters:tr:1:18:ring")
        #expect(a.style(of: "meters") == .ring)
        #expect(a.style(of: "clock") == .bar)
        #expect(a.style(of: "watchers") == .bar)
    }

    @Test func aWrittenStyleOnAPieceWithoutOneIsNotWritten() {
        let p = DeskPlacement(widget: "clock", anchor: .bl, scale: 1, tilt: 0, style: .ring)
        #expect(DeskArrangement.word(p) == "clock:bl")
    }

    @Test func tiltAndStyleTogetherRoundTrip() {
        let layout = "clock:bl:1:-18 claude:bl:1:-18:slim meters:tr:1.2:0:ticks"
        #expect(DeskArrangement(layout).string == layout)
    }

    // MARK: Which pieces have a style

    @Test func onlyTheMeterLikePiecesHaveAStyle() {
        #expect(DeskMeterStyles.pieces == ["claude", "meters"])
        for w in DeskArrangement.known where !DeskMeterStyles.pieces.contains(w) {
            #expect(!DeskMeterStyles.supports(w))
        }
        for s in DeskMeterStyle.allCases { #expect(DeskMeterStyles.parse(DeskMeterStyles.word(s)) == s) }
        #expect(DeskMeterStyle.allCases.map(DeskMeterStyles.name) == ["Bars", "Slim", "Rings", "Ticks"])
        #expect(DeskMeterStyle.allCases.map(\.rawValue) == ["bar", "slim", "ring", "ticks"])
        #expect(DeskMeterStyles.parse("RING") == nil)
        #expect(DeskMeterStyles.parse(nil) == nil)
        #expect(DeskMeterStyles.for("clock", .ring) == .bar)
        #expect(DeskMeterStyles.for("meters", .ring) == .ring)
    }

    @Test func theRefusalNamesThePiecesAndTheWords() {
        let noStyle = DeskMeterStyles.refusal(widget: "clock", word: "ring")
        #expect(noStyle?.contains("claude, meters") == true)
        let unknown = DeskMeterStyles.refusal(widget: "meters", word: "donut")
        #expect(unknown?.contains("bar, slim, ring, ticks") == true)
        #expect(DeskMeterStyles.refusal(widget: "meters", word: " Ticks ") == nil)
        #expect(DeskMeterStyles.refusal(widget: "claude", word: nil) != nil)
    }
}
