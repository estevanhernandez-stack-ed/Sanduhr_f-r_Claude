import Foundation
import Testing
@testable import Sanduhr

/// The fold's geometry (2.37.0 item 2), ported case for case from the Windows suite
/// (DeskRideAlongsTests, F: the fold, the geometry Arrange Desk hit-tests), plus the Mac's own
/// pieces: the shear a stack draws with, the slot it reserves, the click areas that follow the
/// slant and the drop that is judged on the stack's own face.

private final class FoldDefaults: DefaultsStore {
    var values: [String: Any] = [:]
    func object(forKey key: String) -> Any? { values[key] }
    func bool(forKey key: String) -> Bool { values[key] as? Bool ?? false }
    func set(_ value: Any?, forKey key: String) { values[key] = value }
}

@Suite("Desk fold")
struct DeskFoldTests {
    // MARK: Ported from Windows

    @Test func aShearLiftsTheRightEdgeAndKeepsTheSidesUpright() {
        let q = DeskFold.shear(CGRect(x: 100, y: 100, width: 200, height: 50), tilt: -20)
        let dy = 200 * tan(-20 * Double.pi / 180)
        #expect(q.topLeft == CGPoint(x: 100, y: 100))
        #expect(q.topRight.x == 300)
        #expect(abs(Double(q.topRight.y) - (100 + dy)) < 1e-6)
        #expect(q.topRight.x == q.bottomRight.x)
        #expect(!q.isUpright)
        #expect(DeskFold.shear(CGRect(x: 0, y: 0, width: 10, height: 10), tilt: 0).isUpright)
    }

    @Test func aPressFollowsTheSlantNotTheRectangleAroundIt() {
        let q = DeskFold.shear(CGRect(x: 0, y: 100, width: 200, height: 40), tilt: -20)   // the right end rises about 73 points
        let b = q.bounds
        // Inside the box around it, but under the slanted bottom on the right: the bottom right corner of the box is empty.
        #expect(195 >= b.minX && 195 < b.maxX && 135 >= b.minY && 135 < b.maxY)
        #expect(!DeskFold.contains(q, CGPoint(x: 195, y: 135)))
        // On the slanted face itself.
        #expect(DeskFold.contains(q, CGPoint(x: 195, y: 50)))
        #expect(DeskFold.contains(q, CGPoint(x: 5, y: 120)))
        #expect(!DeskFold.contains(q, CGPoint(x: 5, y: 60)))
        // Growing keeps the slant and takes the pad on every side.
        let g = DeskFold.grow(q, by: 6)
        #expect(DeskFold.contains(g, CGPoint(x: -4, y: 120)))
        #expect(abs(Double((q.topRight.y - q.topLeft.y) - (g.topRight.y - g.topLeft.y))) < 1e-6)
        #expect(DeskFold.area(g) > DeskFold.area(q))
    }

    @Test func anUprightQuadIsItsRectangle() {
        let q = DeskQuad.of(CGRect(x: 10, y: 20, width: 30, height: 40))
        #expect(q.isUpright)
        #expect(q.bounds == CGRect(x: 10, y: 20, width: 30, height: 40))
        #expect(DeskFold.contains(q, CGPoint(x: 10, y: 20)))
        #expect(DeskFold.contains(q, CGPoint(x: 39.9, y: 59.9)))
        #expect(!DeskFold.contains(q, CGPoint(x: 41, y: 30)))
        #expect(abs(DeskFold.area(q) - 1200) < 1e-6)
    }

    @Test func aDegenerateQuadHoldsNothing() {
        let flat = DeskQuad.of(CGRect(x: 5, y: 5, width: 0, height: 0))
        #expect(!DeskFold.contains(flat, CGPoint(x: 5, y: 5)))
    }

    // MARK: The shear a stack draws with

    @Test func theTransformIsTheWindowsSkewAboutTheTopLeft() {
        // WPF SkewTransform(0, tilt): y' = y + x * tan(tilt). A negative tilt rises to the right.
        let t = DeskFold.transform(-20)
        #expect(t.a == 1 && t.d == 1 && t.c == 0 && t.tx == 0 && t.ty == 0)
        #expect(abs(Double(t.b) - tan(-20 * Double.pi / 180)) < 1e-12)
        let p = CGPoint(x: 200, y: 50).applying(t)
        #expect(p.x == 200)
        #expect(abs(Double(p.y) - (50 - 200 * tan(20 * Double.pi / 180))) < 1e-9)
        #expect(DeskFold.transform(0).isIdentity)
    }

    @Test func aTiltBeyondTheRangeDrawsAtTheEdgeOfIt() {
        #expect(DeskFold.factor(45) == DeskFold.factor(20))
        #expect(DeskFold.factor(-45) == DeskFold.factor(-20))
        #expect(DeskFold.factor(.nan) == 0)
    }

    @Test func theSlotHoldsTheSlantWhicheverWayItLeans() {
        // A stack 200 wide: it grows by 200 * tan(tilt) either way; a rise sits that far down in the slot.
        let rise = DeskFold.extraHeight(width: 200, tilt: -20)
        #expect(abs(Double(rise) - 200 * tan(20 * Double.pi / 180)) < 1e-9)
        #expect(DeskFold.extraHeight(width: 200, tilt: 20) == rise)
        #expect(DeskFold.lift(width: 200, tilt: -20) == rise)
        #expect(DeskFold.lift(width: 200, tilt: 20) == 0)
        #expect(DeskFold.extraHeight(width: 200, tilt: 0) == 0)
    }

    // MARK: A stack's shear (about its top left, not the piece's)

    @Test func aPieceInAStackShearsAboutTheStacksTopLeft() {
        let fold = DeskFold(tilt: -20, origin: CGPoint(x: 100, y: 400))
        // A piece 60 across the stack's left edge sits 60 * tan(20) higher at its left than at the stack's.
        let q = fold.quad(of: CGRect(x: 160, y: 500, width: 100, height: 30))
        let f = Double(DeskFold.factor(-20))
        #expect(abs(Double(q.topLeft.y) - (500 + 60 * f)) < 1e-9)
        #expect(abs(Double(q.topRight.y) - (500 + 160 * f)) < 1e-9)
        #expect(q.topLeft.x == 160 && q.bottomRight.x == 260)
    }

    @Test func unapplyIsTheInverseOfApply() {
        let fold = DeskFold(tilt: 13, origin: CGPoint(x: 40, y: 10))
        let p = CGPoint(x: 333, y: 271)
        let back = fold.unapply(fold.apply(p))
        #expect(abs(back.x - p.x) < 1e-9 && abs(back.y - p.y) < 1e-9)
    }

    // MARK: Click areas follow the slant

    @Test func aClickLandsOnTheSlantedFaceNotTheBoxAroundIt() {
        var meters = DeskElement(kind: .meters, frame: CGRect(x: 0, y: 400, width: 300, height: 100))
        meters.fold = DeskFold(tilt: -20, origin: CGPoint(x: 0, y: 400))
        let rise = Double(300 * DeskFold.factor(20))   // about 109
        // The right end has risen: the old place at the bottom right is empty now, the risen place is the meters.
        #expect(DeskHitTest.element(at: CGPoint(x: 290, y: 480), in: [meters]) == nil)
        #expect(DeskHitTest.element(at: CGPoint(x: 290, y: 400 - rise + 40), in: [meters]) == meters)
        // The left end has not moved.
        #expect(DeskHitTest.element(at: CGPoint(x: 10, y: 450), in: [meters]) == meters)
        // The slack goes round the slanted face: just above its risen top still counts.
        #expect(DeskHitTest.element(at: CGPoint(x: 290, y: 400 - 290 * Double(DeskFold.factor(20)) - 4), in: [meters]) == meters)
    }

    @Test func anUprightElementHitsExactlyAsBefore() {
        let upright = DeskElement(kind: .meters, frame: CGRect(x: 50, y: 800, width: 360, height: 120))
        #expect(upright.fold == nil)
        #expect(DeskHitTest.quad(upright)?.isUpright == true)
        #expect(DeskHitTest.quad(upright)?.bounds == DeskHitTest.area(upright))
        #expect(upright.drawnFrame == upright.frame)
        #expect(DeskHitTest.element(at: CGPoint(x: 100, y: 794.5), in: [upright]) == upright)
        #expect(DeskHitTest.element(at: CGPoint(x: 100, y: 793), in: [upright]) == nil)
    }

    @Test func aTiltOfZeroIsNoFold() {
        let layout = DeskArrangement("clock:bl")
        let clock = DeskElement(kind: .clock, frame: CGRect(x: 10, y: 10, width: 100, height: 40))
        let out = DeskElements.folded([clock], arrangement: layout,
                                      folds: [.bl: DeskFold(tilt: 0, origin: .zero)])
        #expect(out[0].fold == nil)
    }

    @Test(arguments: [-20.0, -7.0, 5.0, 20.0])
    func theLayoutFrameComesBackFromTheBoxAroundTheDrawnPiece(_ tilt: Double) {
        // SwiftUI reports the box around a slanted view; the layout frame is recovered exactly.
        let fold = DeskFold(tilt: tilt, origin: CGPoint(x: 52, y: 700))
        let layout = CGRect(x: 90, y: 760, width: 240, height: 36)
        let box = fold.quad(of: layout).bounds
        let back = fold.layoutFrame(fromBounds: box)
        #expect(abs(back.minX - layout.minX) < 1e-9 && abs(back.minY - layout.minY) < 1e-9)
        #expect(abs(back.width - layout.width) < 1e-9 && abs(back.height - layout.height) < 1e-9)
    }

    @Test func theStacksOriginComesBackFromItsBox() {
        // A 300 by 120 stack at (52, 700), rising at -20: its box starts 300 * tan(20) above the stack's own top.
        let fold = DeskFold(tilt: -20, origin: CGPoint(x: 52, y: 700))
        let box = fold.quad(of: CGRect(x: 52, y: 700, width: 300, height: 120)).bounds
        let o = DeskFold.origin(ofStackBounds: box, tilt: -20)
        #expect(abs(o.x - 52) < 1e-9 && abs(o.y - 700) < 1e-9)
        // Falling, the box starts at the stack's top.
        let down = DeskFold(tilt: 20, origin: CGPoint(x: 52, y: 700))
        let o2 = DeskFold.origin(ofStackBounds: down.quad(of: CGRect(x: 52, y: 700, width: 300, height: 120)).bounds, tilt: 20)
        #expect(abs(o2.y - 700) < 1e-9)
    }

    @Test func aStacksSlantReachesItsElementsAndOnlyThem() {
        let layout = DeskArrangement("clock:bl:1:-12 claude:bl:1:-12 meters:tr")
        let fold = DeskFold(tilt: -12, origin: CGPoint(x: 52, y: 700))
        let elements = [
            DeskElement(kind: .clock, frame: CGRect(x: 52, y: 700, width: 300, height: 100)),
            DeskElement(kind: .claudeLine, frame: CGRect(x: 52, y: 810, width: 300, height: 30)),
            DeskElement(kind: .account, frame: CGRect(x: 52, y: 810, width: 60, height: 30)),
            DeskElement(kind: .meters, frame: CGRect(x: 900, y: 40, width: 300, height: 100)),
            DeskElement(kind: .nowPlaying, key: "strip", frame: CGRect(x: 600, y: 0, width: 100, height: 20)),
        ]
        let out = DeskElements.folded(elements, arrangement: layout, folds: [.bl: fold])
        #expect(out.map { $0.fold != nil } == [true, true, true, false, false])
        // The slanted ones went in as boxes and come out as layout frames; the others are untouched.
        #expect(out[3].frame == elements[3].frame && out[4].frame == elements[4].frame)
    }

    @Test func theFrameCheckJudgesTheSlantAsDrawn() {
        // Upright it fits the 1000-wide window; slanted by -20 the risen corner leaves the top.
        var e = DeskElement(kind: .clock, frame: CGRect(x: 0, y: 50, width: 400, height: 40))
        #expect(DeskFrameCheck.problem([e], window: CGSize(width: 1000, height: 800)) == nil)
        e.fold = DeskFold(tilt: -20, origin: CGPoint(x: 0, y: 50))
        #expect(DeskFrameCheck.problem([e], window: CGSize(width: 1000, height: 800)) == "clock off the Desk window")
    }

    @Test func theClickLayerPaintsASlantedPieceAsItsQuad() {
        var row = DeskElement(kind: .meetingRow, key: "0", frame: CGRect(x: 52, y: 600, width: 300, height: 25))
        let upright = DeskElement(kind: .note, frame: CGRect(x: 900, y: 100, width: 200, height: 25))
        row.fold = DeskFold(tilt: 10, origin: CGPoint(x: 52, y: 600))
        let all = [row, upright]
        #expect(DeskClickLayer.areas(in: all) == [DeskHitTest.area(upright)])
        let quads = DeskClickLayer.slantedAreas(in: all)
        #expect(quads.count == 1)
        #expect(!quads[0].isUpright)
    }

    // MARK: Arrange mode

    @Test func arrangeOutlinesAndDropsOnTheSlantedQuad() {
        let mode = DeskArrangeMode(store: FoldDefaults())
        mode.begin()
        mode.edit { $0 = DeskArrangement("clock:bl:1:-15 claude:bl:1:-15") }
        let fold = DeskFold(tilt: -15, origin: CGPoint(x: 52, y: 600))
        let layout = CGRect(x: 52, y: 600, width: 300, height: 100)
        // SwiftUI reports the box around the drawn piece.
        mode.frames["clock"] = fold.quad(of: layout).bounds
        // Upright until the stack reports its slant (the box is then taken as the frame).
        #expect(mode.quad(of: "clock")?.isUpright == true)
        mode.setFold(fold, at: .bl)
        let q = mode.quad(of: "clock")
        #expect(q?.isUpright == false)
        #expect(q == fold.quad(of: layout))
        #expect(mode.layoutFrame(of: "clock") != nil)
        if let f = mode.layoutFrame(of: "clock") {
            #expect(abs(f.minY - layout.minY) < 1e-9 && abs(f.height - layout.height) < 1e-9)
        }
        // A piece with no frame has no quad; a hidden one is never folded.
        #expect(mode.quad(of: "meters") == nil)
        #expect(mode.fold(of: "meters") == nil)
        // A pointer on screen maps back onto the unslanted layout the frames are judged in.
        let onScreen = fold.apply(CGPoint(x: 352, y: 650))
        #expect(abs(mode.folds[.bl]!.unapply(onScreen).y - 650) < 1e-9)
        // Taking the stack upright drops its fold.
        mode.setFold(nil, at: .bl)
        #expect(mode.fold(of: "clock") == nil)
    }
}

@Suite("Desk slant setting")
struct DeskSlantSettingTests {
    @Test func theLabelsAreWholeDegrees() {
        #expect(DeskSlant.label(0) == "0°")
        #expect(DeskSlant.label(-12) == "-12°")
        #expect(DeskSlant.label(8) == "8°")
        #expect(DeskSlant.label(99) == "20°")
        #expect(DeskSlant.label(-0.2) == "0°")
        #expect(DeskSlant.rowTitle(.tl) == "Top left slant")
    }

    @Test func theStepperWritesTheLayoutWordForTheWholeStack() {
        var a = DeskArrangement("message:tl clock:bl claude:bl meetings:bl")
        a.setTilt("clock", -12)
        #expect(a.string == "message:tl clock:bl:1:-12 claude:bl:1:-12 meetings:bl:1:-12")
        a.setTilt("clock", 0)
        #expect(a.string == "message:tl clock:bl claude:bl meetings:bl")
    }

    @Test func searchFindsTheSlantUnderDesk() {
        for word in ["slant", "tilt", "fold", "lean"] {
            let hit = SettingsSearch.hits(word).first { $0.entry.anchor == SettingsAnchor.slant }
            #expect(hit != nil, "\(word)")
            #expect(hit?.entry.page == .deskLayout)
        }
    }
}
