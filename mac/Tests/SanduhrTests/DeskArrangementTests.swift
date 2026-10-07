import Foundation
import CoreGraphics
import Testing
@testable import Sanduhr

/// Item 59: Desk layout, step 1. Eight anchors, your order within each, a size per piece, the
/// layout string read backward compatibly, and the geometry the Desk and the map share.

/// What DeskView drew from a layout string before item 59, copied as it was: corners only,
/// known widgets, the string's order, the older switches. The new reader must match it.
private func corners(before layout: String, showMeetings: Bool = true, showClaude: Bool = true) -> [String: [String]] {
    var out: [String: [String]] = [:]
    for item in layout.split(separator: " ") {
        let bits = item.split(separator: ":").map(String.init)
        guard bits.count == 2, let w = DeskView.Widget(rawValue: bits[0]),
              ["tl", "tr", "bl", "br"].contains(bits[1]) else { continue }
        if w == .meetings && !showMeetings { continue }
        if (w == .claude || w == .meters) && !showClaude { continue }
        out[bits[1], default: []].append(w.rawValue)
    }
    return out
}

private func corners(now layout: String, showMeetings: Bool = true, showClaude: Bool = true) -> [String: [String]] {
    var out: [String: [String]] = [:]
    for (anchor, stack) in DeskArrangement(layout).stacks(showMeetings: showMeetings, showClaude: showClaude) {
        out[anchor.rawValue] = stack.map(\.widget)
    }
    return out
}

@Suite("Desk layout string, item 59")
struct DeskArrangementStringTests {
    /// Layouts saved before item 59: the default, the fresh-install one, hand-edited orders (the
    /// 2.6.0 screenshots), every widget, stray spaces and malformed words.
    static let older = [
        DeskLayout.standard,
        "message:tl clock:bl meters:bl meetings:bl",
        "meters:bl clock:bl message:tr meetings:br nowPlaying:bl watchers:tr",
        "message:tl meetings:bl meters:bl clock:bl",
        "clock:tr claude:tr meters:tr nowPlaying:tr watchers:tl meetings:tl message:br",
        "  claude:br   clock  a:b:c :bl meters: message:tl ",
        "weather:tr clock:tl",
        "",
    ]

    @Test(arguments: older)
    func anOlderLayoutDrawsExactlyAsBefore(_ layout: String) {
        for (meetings, claude) in [(true, true), (false, true), (true, false), (false, false)] {
            #expect(corners(now: layout, showMeetings: meetings, showClaude: claude)
                    == corners(before: layout, showMeetings: meetings, showClaude: claude))
        }
        #expect(DeskArrangement(layout).pieces.allSatisfy { $0.scale == 1 })
    }

    @Test func anOlderLayoutIsWrittenBackUnchanged() {
        for layout in Self.older.prefix(5) {
            #expect(DeskArrangement(layout).string == layout)
        }
    }

    @Test func readsAnchorsOrderAndSizes() {
        let a = DeskArrangement("clock:tc:1.2 message:mr meters:bc:0.6 meetings:ml watchers:bc")
        #expect(a.pieces == [
            DeskPlacement(widget: "clock", anchor: .tc, scale: 1.2),
            DeskPlacement(widget: "message", anchor: .mr),
            DeskPlacement(widget: "meters", anchor: .bc, scale: 0.6),
            DeskPlacement(widget: "meetings", anchor: .ml),
            DeskPlacement(widget: "watchers", anchor: .bc),
        ])
        #expect(a.stack(.bc).map(\.widget) == ["meters", "watchers"])
        #expect(a.string == "clock:tc:1.2 message:mr meters:bc:0.6 meetings:ml watchers:bc")
    }

    @Test func everyPieceCanSitAtEveryAnchor() {
        for w in DeskLayout.widgets {
            for anchor in DeskAnchor.allCases {
                let a = DeskArrangement("\(w.key):\(anchor.rawValue)")
                #expect(a.stacks()[anchor]?.map(\.widget) == [w.key])
            }
        }
    }

    @Test func anUnknownAnchorFallsBackToThePiecesDefault() {
        let a = DeskArrangement("message:zz clock:center meters:tl:1.2:x meetings:BL")
        #expect(a.placement("message")?.anchor == .tl)
        #expect(a.placement("clock")?.anchor == .bl)
        #expect(a.placement("meetings")?.anchor == .bl)
        // Extra parts after the size are ignored; the piece is still drawn.
        #expect(a.placement("meters") == DeskPlacement(widget: "meters", anchor: .tl, scale: 1.2))
        #expect(a.shown().count == 4)
    }

    @Test func aRepeatedWidgetKeepsItsLastWord() {
        let a = DeskArrangement("clock:bl message:tl clock:tr:1.4")
        #expect(a.pieces.map(\.widget) == ["message", "clock"])
        #expect(a.placement("clock") == DeskPlacement(widget: "clock", anchor: .tr, scale: 1.4))
    }

    @Test func theOlderSwitchesStillHide() {
        let a = DeskArrangement("claude:tc meters:mr meetings:bc clock:bl")
        #expect(a.shown(showMeetings: false).map(\.widget) == ["claude", "meters", "clock"])
        #expect(a.shown(showClaude: false).map(\.widget) == ["meetings", "clock"])
    }
}

@Suite("Desk piece sizes, item 59")
struct DeskArrangementScaleTests {
    @Test func sizesStayWithinBoundsAndSnapToSteps() {
        #expect(DeskArrangement.clampScale(0.1) == 0.6)
        #expect(DeskArrangement.clampScale(5) == 1.6)
        #expect(DeskArrangement.clampScale(1.23) == 1.2)
        #expect(DeskArrangement.clampScale(0.66) == 0.7)
        #expect(DeskArrangement.clampScale(.nan) == 1)
        #expect(DeskArrangement.clampScale(.infinity) == 1)
        #expect(DeskArrangement.scaleSteps.count == 11)
        #expect(DeskArrangement.scaleSteps.first == 0.6)
        #expect(DeskArrangement.scaleSteps.last == 1.6)
        #expect(DeskArrangement.scaleSteps.contains(1))
    }

    @Test func aSizeInTheStringIsClampedAndOneIsNotWritten() {
        #expect(DeskArrangement("clock:bl:9").placement("clock")?.scale == 1.6)
        #expect(DeskArrangement("clock:bl:0").placement("clock")?.scale == 0.6)
        #expect(DeskArrangement("clock:bl:abc").placement("clock")?.scale == 1)
        #expect(DeskArrangement("clock:bl:1.0").string == "clock:bl")
        #expect(DeskArrangement("clock:bl:1.24").string == "clock:bl:1.2")
    }

    @Test func setScaleChangesOnePlacedPiece() {
        var a = DeskArrangement(DeskLayout.standard)
        a.setScale("clock", 1.3)
        a.setScale("meters", 1.3)   // not placed: nothing to size
        #expect(a.string == "message:tl clock:bl:1.3 claude:bl meetings:bl")
        a.setScale("clock", 1)
        #expect(a.string == DeskLayout.standard)
    }
}

@Suite("Desk order within an anchor, item 59")
struct DeskArrangementOrderTests {
    @Test func dragDownLandsAfterTheTarget() {
        var a = DeskArrangement("message:tl clock:bl claude:bl meetings:bl")
        a.move("clock", onto: "meetings")
        #expect(a.stack(.bl).map(\.widget) == ["claude", "meetings", "clock"])
    }

    @Test func dragUpLandsBeforeTheTarget() {
        var a = DeskArrangement("message:tl clock:bl claude:bl meetings:bl")
        a.move("meetings", onto: "clock")
        #expect(a.stack(.bl).map(\.widget) == ["meetings", "clock", "claude"])
        #expect(a.string == "message:tl meetings:bl clock:bl claude:bl")
    }

    @Test func dragOntoAnotherAnchorJoinsIt() {
        var a = DeskArrangement("message:tl clock:bl:1.2 claude:bl")
        a.move("clock", onto: "message")
        #expect(a.stack(.tl).map(\.widget) == ["clock", "message"])
        #expect(a.placement("clock")?.scale == 1.2)
        #expect(a.stack(.bl).map(\.widget) == ["claude"])
    }

    @Test func nothingMovesOntoItselfOrAHiddenPiece() {
        var a = DeskArrangement(DeskLayout.standard)
        a.move("clock", onto: "clock")
        a.move("clock", onto: "meters")
        a.move("meters", onto: "clock")
        #expect(a.string == DeskLayout.standard)
    }

    @Test func moveUpAndDownStopAtTheEnds() {
        var a = DeskArrangement(DeskLayout.standard)
        a.move("clock", by: -1)
        #expect(a.string == DeskLayout.standard)
        a.move("clock", by: 1)
        #expect(a.stack(.bl).map(\.widget) == ["claude", "clock", "meetings"])
        a.move("meetings", by: 1)
        a.move("clock", by: 1)
        #expect(a.stack(.bl).map(\.widget) == ["claude", "meetings", "clock"])
    }

    @Test func placingKeepsTheSizeAndTheOtherPiecesOrder() {
        var a = DeskArrangement("meetings:bl clock:bl:1.4 message:tl")
        a.place("clock", at: .tc)
        #expect(a.string == "meetings:bl clock:tc:1.4 message:tl")
        a.place("clock", at: .tc)
        #expect(a.string == "meetings:bl clock:tc:1.4 message:tl")
        a.place("clock", at: nil)
        #expect(a.string == "meetings:bl message:tl")
        a.place("clock", at: .bl)
        #expect(a.stack(.bl).map(\.widget) == ["clock", "meetings"])
        #expect(a.placement("clock")?.scale == 1)
    }
}

@Suite("Desk anchor geometry, item 59")
struct DeskAnchorGeometryTests {
    let window = CGSize(width: 1512, height: 982)
    let notch = CGRect(x: 656, y: 0, width: 200, height: 32)

    @Test func eachAnchorHasAColumnAndARow() {
        #expect(DeskAnchor.allCases.count == 8)
        #expect(Set(DeskAnchor.allCases.map(\.rawValue)) == ["tl", "tc", "tr", "ml", "mr", "bl", "bc", "br"])
        #expect(DeskAnchor.at(.center, .middle) == nil)
        for anchor in DeskAnchor.allCases {
            #expect(DeskAnchor.at(anchor.column, anchor.row) == anchor)
        }
    }

    @Test func theNotchOrTheIslandSetsTheTopCentersFloor() {
        #expect(DeskAnchorGeometry.notchBottom(notch: nil, island: true, chin: 26) == 0)
        #expect(DeskAnchorGeometry.notchBottom(notch: notch, island: false, chin: 26) == 32)
        #expect(DeskAnchorGeometry.notchBottom(notch: notch, island: true, chin: 26) == 58)
        #expect(DeskAnchorGeometry.notchBottom(notch: notch, island: true, chin: 0) == 32)
    }

    @Test func theTopCenterSitsBelowTheNotchOrTheIsland() {
        // The usual margins already clear an island: nothing moves.
        #expect(DeskAnchorGeometry.centerDrop(contentTop: 77, notchBottom: 58) == 0)
        // A small top margin: the top center drops below the island with the gap.
        #expect(DeskAnchorGeometry.centerDrop(contentTop: 37, notchBottom: 58) == 58 + DeskAnchorGeometry.notchGap - 37)
        // A plain screen: no drop at all.
        #expect(DeskAnchorGeometry.centerDrop(contentTop: 0, notchBottom: 0) == 0)
        let content = CGRect(x: 52, y: 37, width: 1408, height: 885)
        let drop = DeskAnchorGeometry.centerDrop(contentTop: content.minY, notchBottom: 58)
        let tc = DeskAnchorGeometry.point(.tc, in: content, centerDrop: drop)
        #expect(tc.y >= 58 + DeskAnchorGeometry.notchGap)
        #expect(tc.x == content.midX)
        // Only the top center drops; the corners stay on the margin.
        #expect(DeskAnchorGeometry.point(.tl, in: content, centerDrop: drop).y == content.minY)
        #expect(DeskAnchorGeometry.point(.tr, in: content, centerDrop: drop).y == content.minY)
    }

    @Test func middlesAreCenteredOnTheirSide() {
        let content = CGRect(x: 52, y: 77, width: 1408, height: 845)
        #expect(DeskAnchorGeometry.point(.ml, in: content) == CGPoint(x: content.minX, y: content.midY))
        #expect(DeskAnchorGeometry.point(.mr, in: content) == CGPoint(x: content.maxX, y: content.midY))
        #expect(DeskAnchorGeometry.point(.bc, in: content) == CGPoint(x: content.midX, y: content.maxY))
    }

    @Test(arguments: DockSide.allCases)
    func theDockClearanceCoversEveryAnchorOnItsSide(_ side: DockSide) {
        let reach: CGFloat = 70
        let inset: CGFloat = 112 * 0.18
        let m = DeskAnchorGeometry.margins(left: 52, right: 52, top: 40, bottom: 60, topInset: 37,
                                          dock: DockInsets.on(side, reach), inset: inset)
        let content = DeskAnchorGeometry.content(window: window, margins: m, inset: inset)
        for anchor in DockGeometry.anchors(for: side) {
            let p = DeskAnchorGeometry.point(anchor, in: content)
            switch side {
            case .bottom: #expect(window.height - p.y >= reach + 60 - 0.5)
            case .left: #expect(p.x >= reach + 52 - 0.5)
            case .right: #expect(window.width - p.x >= reach + 52 - 0.5)
            }
        }
        // Without the Dock the same anchors sit on the plain margin.
        let none = DeskAnchorGeometry.margins(left: 52, right: 52, top: 40, bottom: 60, topInset: 37,
                                              dock: DockInsets(), inset: inset)
        let plain = DeskAnchorGeometry.content(window: window, margins: none, inset: inset)
        #expect(plain.minX == 52)
        #expect(abs(window.height - plain.maxY - 60) < 0.001)
    }

    @Test func theMarginsMatchTheDesksPaddingFromBefore() {
        // DeskView padded max(0, margin + dock - inset) on each side and max(0, topInset + top -
        // inset) at the top, then the inset inside: the same numbers.
        let m = DeskAnchorGeometry.margins(left: 10, right: 52, top: 40, bottom: 60, topInset: 24,
                                          dock: DockInsets(bottom: 70), inset: 20)
        #expect(m == DeskAnchorGeometry.Margins(top: 44, leading: 0, bottom: 110, trailing: 32))
    }
}

@Suite("Desk click areas at every anchor, item 59")
struct DeskArrangementFrameTests {
    @Test func piecesAtTheNewAnchorsAreClickAreas() {
        var i = DeskElements.Input()
        i.placed = DeskLayout.placed("meters:tc meetings:mr claude:bc nowPlaying:ml")
        #expect(i.placed == ["meters", "meetings", "claude", "nowPlaying"])
        i.meterTiers = [.fiveHour]
        i.rows = [DeskElements.Row(id: "a", hasLink: true)]
        i.hasAccount = true
        i.nowPlayingLine = true
        // Where the Desk puts them on a 1512 by 982 window: meters top center, meetings middle
        // right, the account at the bottom center, now playing middle left.
        i.metersFrame = CGRect(x: 577, y: 77, width: 358, height: 60)
        i.meterRowFrames = [.fiveHour: CGRect(x: 577, y: 77, width: 358, height: 58)]
        i.meetingsFrame = CGRect(x: 1200, y: 480, width: 260, height: 22)
        i.rowFrames = ["a": CGRect(x: 1200, y: 480, width: 260, height: 22)]
        i.accountFrame = CGRect(x: 700, y: 900, width: 80, height: 20)
        i.nowPlayingFrame = CGRect(x: 52, y: 470, width: 360, height: 30)
        let elements = DeskElements.build(i)
        #expect(Set(elements.map(\.kind)) == [.meters, .meterRow, .meetings, .meetingRow, .account, .nowPlaying])
        #expect(DeskFrameCheck.problem(elements, window: CGSize(width: 1512, height: 982)) == nil)
        #expect(DeskHitTest.element(at: CGPoint(x: 1300, y: 490), in: elements)?.kind == .meetingRow)
        #expect(DeskHitTest.element(at: CGPoint(x: 600, y: 100), in: elements)?.kind == .meterRow)
    }

    @Test func smokeStateListsAnchorsOrderAndSizes() {
        var s = DebugStateInput()
        s.deskPieces = DeskArrangement("message:tl meters:bl:1.2 clock:bl meetings:mr").shown()
        let yaml = YAMLEmitter.emit(DebugState.yaml(s))
        #expect(yaml.contains("""
        desk_pieces:
          - piece: message
            anchor: tl
            order: 0
            scale: 1
          - piece: meters
            anchor: bl
            order: 0
            scale: 1.2
          - piece: clock
            anchor: bl
            order: 1
            scale: 1
          - piece: meetings
            anchor: mr
            order: 0
            scale: 1
        notch:
        """))
        #expect(YAMLEmitter.emit(DebugState.yaml(DebugStateInput())).contains("desk_pieces: []\n"))
    }
}
