import AppKit
import Foundation
import Testing
@testable import Sanduhr

/// The click layer under the desktop icons (DeskClickLayer): its plates are exactly the click
/// areas DeskHitTest answers for among the small pieces, the big pieces stay with the files, it
/// sits where the Desk did, and it stands aside for Arrange.
@Suite("Desk click layer")
struct DeskClickLayerTests {
    let meters = DeskElement(kind: .meters, frame: CGRect(x: 50, y: 800, width: 360, height: 120))
    let session = DeskElement(kind: .meterRow, key: "five_hour", frame: CGRect(x: 50, y: 800, width: 360, height: 50))
    let account = DeskElement(kind: .account, frame: CGRect(x: 50, y: 770, width: 60, height: 20))
    let meetings = DeskElement(kind: .meetings, frame: CGRect(x: 50, y: 930, width: 300, height: 60), clickable: false)
    let joinable = DeskElement(kind: .meetingRow, key: "0", frame: CGRect(x: 50, y: 940, width: 300, height: 25))
    let noLink = DeskElement(kind: .meetingRow, key: "1", frame: CGRect(x: 50, y: 965, width: 300, height: 25), clickable: false)
    let watcher = DeskElement(kind: .watcher, key: "0", frame: CGRect(x: 600, y: 100, width: 200, height: 20))
    let notLaidOut = DeskElement(kind: .nowPlaying, key: "desk", frame: .zero)
    let clock = DeskElement(kind: .clock, frame: CGRect(x: 600, y: 800, width: 360, height: 186))
    let message = DeskElement(kind: .message, frame: CGRect(x: 50, y: 500, width: 565, height: 107))
    let line = DeskElement(kind: .claudeLine, frame: CGRect(x: 50, y: 770, width: 254, height: 27))

    var elements: [DeskElement] {
        [meters, session, account, meetings, joinable, noLink, watcher, notLaidOut, clock, message, line]
    }

    @Test func thePlatesAreTheSmallPiecesClickAreasAndNothingElse() {
        let areas = DeskClickLayer.areas(in: elements)
        let expected = [account, joinable, watcher].map(DeskHitTest.area)
        #expect(areas == expected)
        // The meetings block, a row without a link and a piece without a frame take no click.
        #expect(!areas.contains(DeskHitTest.area(meetings)))
        #expect(!areas.contains(DeskHitTest.area(noLink)))
        #expect(areas.allSatisfy { !$0.isEmpty })
        // The big pieces (a click there only opens a menu) stay with the files under them.
        for big in [meters, session, clock, message, line] {
            #expect(!areas.contains(DeskHitTest.area(big)), "\(big.kind)")
        }
    }

    @Test func theKindsAreTheSmallPiecesAClickActsOn() {
        #expect(DeskClickLayer.kinds == [.meetingRow, .note, .account, .watcher, .nowPlaying, .nowPlayingNext, .avIndicators])
        // The not-yet-laid-out now playing is served (it will have a frame) but has no plate yet.
        #expect(DeskClickLayer.elements(in: elements).map(\.kind) == [.account, .meetingRow, .watcher, .nowPlaying])
    }

    /// Every point the hit test answers for among the layer's elements is on a plate, and every
    /// point on a plate gets an answer: the layer can never take a click it would not act on, or
    /// the reverse. The controller runs the hit test on the same list (clickableElements).
    @Test func thePlatesAndTheHitTestAgreeOnEveryPoint() {
        let areas = DeskClickLayer.areas(in: elements)
        let served = DeskClickLayer.elements(in: elements)
        for x in stride(from: 0, through: 1000, by: 10) {
            for y in stride(from: 0, through: 1100, by: 10) {
                let p = CGPoint(x: x, y: y)
                let hit = DeskHitTest.element(at: p, in: served) != nil
                let onPlate = areas.contains { $0.contains(p) }
                #expect(hit == onPlate, "at \(p)")
            }
        }
    }

    @Test func theLayerSitsWhereTheDeskDidAndStepsAsideForArrange() {
        #expect(DeskClickLayer.level == DeskLayer.level(underIcons: false))
        #expect(DeskClickLayer.level.rawValue == NSWindow.Level.normal.rawValue - 1)
        #expect(DeskClickLayer.active(underIcons: true, arranging: false))
        #expect(!DeskClickLayer.active(underIcons: true, arranging: true))
        #expect(!DeskClickLayer.active(underIcons: false, arranging: false))
    }

    @Test @MainActor func theViewDrawsTopDownAndTakesTheFirstClick() {
        let view = DeskClickLayerView(frame: CGRect(x: 0, y: 0, width: 100, height: 100))
        #expect(view.isFlipped)
        #expect(view.acceptsFirstMouse(for: nil))
        view.areas = [CGRect(x: 1, y: 2, width: 3, height: 4)]
        #expect(view.areas.count == 1)
    }
}
