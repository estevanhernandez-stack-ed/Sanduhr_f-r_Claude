import CoreGraphics
import Foundation
import Testing
@testable import Sanduhr

private let idA = "3D2E1F00-0000-4A5B-8C9D-0E1F2A3B4C5D"
private let idB = "4E2E1F00-0000-4A5B-8C9D-0E1F2A3B4C5E"

private let mainScreen = DeskScreenInfo(id: idA, frame: CGRect(x: 0, y: 0, width: 1512, height: 982), primary: true)
private let right = DeskScreenInfo(id: idB, frame: CGRect(x: 1512, y: 0, width: 1920, height: 1080), primary: false)
private let two = DeskScreens.named([mainScreen, right])
private let one = DeskScreens.named([mainScreen])

private let mainWindow = DeskWindowPlan.Window(screen: two[0], isMain: true)
private let rightWindow = DeskWindowPlan.Window(screen: two[1], isMain: false)
private let rightKey = DeskWindowElements.key(rightWindow)
/// The right display's window from the main window's top left: 1512 across, its top 98 points above.
private let rightOffset = DeskWindowPlan.offset(of: right.frame, from: mainScreen.frame)
private let rightReporting = DeskFrameReporting(offset: rightOffset, window: rightKey)

/// What the model shares between windows: the clock and the message drawn and taking clicks, one
/// meeting with a link.
private func shared(_ layout: String) -> DeskElements.Input {
    var i = DeskElements.Input()
    i.placed = DeskLayout.placed(layout)
    i.rows = [.init(id: "m1", hasLink: true)]
    i.piecesTakeClicks = true
    i.messageDrawn = true
    return i
}

/// The clock's layout frame where a window draws it, in that window's own coordinates.
private let clockLayout = CGRect(x: 52, y: 60, width: 300, height: 150)

/// A window's frames as its views report them for one slanted piece alone in its stack: the box
/// around the drawn piece and the stack's fold, both moved by the window's offset (DeskFrameReporting).
private func slanted(_ set: WritableKeyPath<DeskFrames, CGRect>, tilt: Double, at anchor: DeskAnchor,
                     reporting: DeskFrameReporting) -> DeskFrames {
    let box = DeskFold.shear(clockLayout, tilt: tilt).bounds
    var f = DeskFrames()
    f[keyPath: set] = reporting.shifted(box)
    f.folds[anchor] = DeskFoldReport.fold(stackBounds: box, tilt: tilt, reporting: reporting)
    return f
}

private func elements(_ layout: String, on window: DeskWindowPlan.Window?, _ f: DeskFrames,
                      connected: [DeskScreenInfo] = two) -> [DeskElement] {
    DeskWindowElements.elements(shared(layout), frames: f, arrangement: DeskArrangement(layout),
                                window: window, connected: connected)
}

private func close(_ a: CGPoint, _ b: CGPoint) -> Bool { abs(a.x - b.x) < 1e-6 && abs(a.y - b.y) < 1e-6 }

@Suite("Slanted Desk stacks on every window, 2.45")
struct DeskWindowFoldsTests {
    // MARK: A slanted stack on an extra window

    @Test func anExtraWindowsFoldIsMovedByItsOffsetLikeItsFrames() {
        let box = DeskFold.shear(clockLayout, tilt: -20).bounds
        let fold = DeskFoldReport.fold(stackBounds: box, tilt: -20, reporting: rightReporting)
        #expect(fold.tilt == -20)
        #expect(close(fold.origin, CGPoint(x: 52 + 1512, y: 60 - 98)))
        // The main window's offset is zero: the fold it reported before 2.45, exactly.
        #expect(DeskFoldReport.fold(stackBounds: box, tilt: -20, reporting: .main)
                == DeskFold(tilt: -20, origin: DeskFold.origin(ofStackBounds: box, tilt: -20)))
    }

    @Test func aSlantedStackOnAnExtraWindowTakesClicksByItsShape() {
        let layout = "clock:tl:1:-20@\(idB) meters:bl"
        let f = slanted(\.clockFrame, tilt: -20, at: .tl, reporting: rightReporting)
        let onRight = elements(layout, on: rightWindow, f)
        let clock = onRight.first { $0.kind == .clock }
        #expect(clock?.fold?.tilt == -20)
        // Rising to the right, the box's bottom right corner is empty; the face is where it was drawn.
        let corner = CGPoint(x: 340 + rightOffset.x, y: 200 + rightOffset.y)
        let fold = DeskFold(tilt: -20, origin: CGPoint(x: 52 + 1512, y: 60 - 98))
        let face = fold.apply(CGPoint(x: clockLayout.midX + rightOffset.x, y: clockLayout.midY + rightOffset.y))
        #expect(DeskHitTest.element(at: corner, in: onRight) == nil)
        #expect(DeskHitTest.element(at: face, in: onRight)?.kind == .clock)
        // Without its fold (before 2.45) the box took the corner.
        var unfolded = f
        unfolded.folds = [:]
        #expect(DeskHitTest.element(at: corner, in: elements(layout, on: rightWindow, unfolded))?.kind == .clock)
    }

    @Test func anExtraWindowsClickLayerGetsItsSlantedAreasInItsOwnCoordinates() {
        // A meeting row with a link (the layer serves the small pieces), alone in its slanted stack.
        let layout = "meetings:tl:1:-20@\(idB)"
        let row = CGRect(x: 52, y: 60, width: 300, height: 28)
        let box = DeskFold.shear(row, tilt: -20).bounds
        var f = DeskFrames()
        f.meetingsFrame = rightReporting.shifted(box)
        f.rowFrames = ["m1": rightReporting.shifted(box)]
        f.folds[.tl] = DeskFoldReport.fold(stackBounds: box, tilt: -20, reporting: rightReporting)
        let onRight = elements(layout, on: rightWindow, f)
        let plates = DeskClickLayer.plates(in: onRight, offset: rightOffset)
        #expect(plates.areas.isEmpty)
        #expect(plates.slanted.count == 1)
        // Where the window drew it: the row's click area slanted about its own top left.
        let local = DeskElement(kind: .meetingRow, key: "0", frame: row, fold: DeskFold(tilt: -20, origin: row.origin))
        let want = DeskHitTest.quad(local)
        #expect(want != nil)
        if let got = plates.slanted.first, let want {
            #expect(!got.isUpright)
            #expect(zip(got.corners, want.corners).allSatisfy { close($0, $1) })
        }
    }

    // MARK: Two windows, one anchor

    @Test func twoWindowsTopLeftFoldsDoNotMix() {
        let layout = "clock:tl:1:-20 message:tl:1:12@\(idB)"
        let onMainFrames = slanted(\.clockFrame, tilt: -20, at: .tl, reporting: .main)
        let onRightFrames = slanted(\.messageFrame, tilt: 12, at: .tl, reporting: rightReporting)
        let onMain = elements(layout, on: mainWindow, onMainFrames)
        let onRight = elements(layout, on: rightWindow, onRightFrames)
        #expect(onMain.map(\.kind) == [.clock])
        #expect(onRight.map(\.kind) == [.message])
        #expect(onMain.first?.fold == onMainFrames.folds[.tl])
        #expect(onRight.first?.fold == onRightFrames.folds[.tl])
        #expect(onMain.first?.fold?.tilt == -20 && onRight.first?.fold?.tilt == 12)
        // Each layout frame comes back where its own window drew it.
        let r = onRight.first?.frame ?? .null
        #expect(abs(r.minX - (clockLayout.minX + rightOffset.x)) < 1e-6 && abs(r.minY - (clockLayout.minY + rightOffset.y)) < 1e-6)
        #expect(abs(r.height - clockLayout.height) < 1e-6)
    }

    @Test func theModelKeepsEachWindowsFoldsApart() {
        let model = DeskModel()
        var changes = 0
        model.onHitAreasChange = { changes += 1 }
        let a = DeskFold(tilt: -20, origin: CGPoint(x: 52, y: 60))
        let b = DeskFold(tilt: 12, origin: CGPoint(x: 1564, y: -38))
        model.setFold(a, at: .tl)
        model.setFold(b, at: .tl, in: rightKey)
        #expect(changes == 2)
        #expect(model.folds == [.tl: a])
        #expect(model.frames(in: rightKey).folds == [.tl: b])
        // The same fold again changes nothing; clearing the extra window's leaves the main one's.
        model.setFold(b, at: .tl, in: rightKey)
        #expect(changes == 2)
        model.setFold(nil, at: .tl, in: rightKey)
        #expect(changes == 3)
        #expect(model.folds == [.tl: a])
        // A window that goes takes its folds, and a stack going with it after leaves no frames behind.
        model.setFold(b, at: .tl, in: rightKey)
        model.keepFrames(for: [])
        model.setFold(nil, at: .tl, in: rightKey)
        #expect(model.windowFrames.keys.map { "\($0)" } == ["\(DeskDisplay.main)"])
        #expect(model.folds == [.tl: a])
    }

    // MARK: The main window, as before

    @Test func theMainWindowFoldsExactlyAsBefore() {
        let layouts = ["clock:tl:1:-20 meters:bl",
                       "clock:tl:1:-20@all message:tl:1:-20@all",
                       "clock:tl:1:-20 message:tl:1:12@\(idB)"]
        let f = slanted(\.clockFrame, tilt: -20, at: .tl, reporting: .main)
        for layout in layouts {
            let arrangement = DeskArrangement(layout)
            for (window, screens) in [(mainWindow, two), (DeskWindowPlan.Window(screen: one[0], isMain: true), one)] {
                // Item 2's path: the main window's input, folded over its pieces with the main folds.
                let input = DeskWindowElements.input(shared(layout), frames: f, arrangement: arrangement,
                                                     window: window, connected: screens)
                let before = DeskElements.folded(DeskElements.build(input),
                                                 arrangement: DeskModel.mainWindowPieces(arrangement, screens: screens),
                                                 folds: f.folds)
                #expect(elements(layout, on: window, f, connected: screens) == before)
            }
            // One display, and the screens not known yet: 2.44.0's one Input, folded over every piece.
            var old = shared(layout)
            old.take(f)
            let v244 = DeskElements.folded(DeskElements.build(old), arrangement: arrangement, folds: f.folds)
            #expect(elements(layout, on: DeskWindowPlan.Window(screen: one[0], isMain: true), f, connected: one) == v244)
            #expect(elements(layout, on: nil, f, connected: []) == v244)
        }
    }
}
