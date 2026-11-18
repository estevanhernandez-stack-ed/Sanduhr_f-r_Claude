import CoreGraphics
import Foundation
import Testing
@testable import Sanduhr

private let idA = "6F1A2B3C-0000-4D5E-8F90-A1B2C3D4E5F6"
private let idB = "7A1A2B3C-0000-4D5E-8F90-A1B2C3D4E5F7"
private let idGone = "9C1A2B3C-0000-4D5E-8F90-A1B2C3D4E5F9"

private let mainScreen = DeskScreenInfo(id: idA, frame: CGRect(x: 0, y: 0, width: 1512, height: 982), primary: true)
private let right = DeskScreenInfo(id: idB, frame: CGRect(x: 1512, y: 0, width: 1920, height: 1080), primary: false)
private let two = DeskScreens.named([mainScreen, right])
private let one = DeskScreens.named([mainScreen])

private let mainWindow = DeskWindowPlan.Window(screen: two[0], isMain: true)
private let rightWindow = DeskWindowPlan.Window(screen: two[1], isMain: false)
/// The right display's window from the main window's top left (DeskWindowPlan.offset): 1512 across,
/// its top 98 points above main's.
private let rightOffset = DeskWindowPlan.offset(of: right.frame, from: mainScreen.frame)

/// What the model shares between windows for a layout: meters with two rows, two meetings (the first
/// with a link), a track, the strip under the camera showing now playing, clicks on the pieces.
private func shared(_ layout: String) -> DeskElements.Input {
    var i = DeskElements.Input()
    i.placed = DeskLayout.placed(layout)
    i.meterTiers = [.fiveHour, .sevenDay]
    i.rows = [.init(id: "m1", hasLink: true), .init(id: "m2", hasLink: false)]
    i.nowPlayingLine = i.placed.contains(NowPlayingPlacement.widget)
    i.nowPlayingStrip = true
    i.piecesTakeClicks = true
    return i
}

/// A window's frames as its views report them: drawn at `local` in its own window, moved by its
/// offset into the main window's coordinates (DeskFrameReporting.shifted).
private func frames(offset: CGPoint) -> DeskFrames {
    func at(_ r: CGRect) -> CGRect { r.offsetBy(dx: offset.x, dy: offset.y) }
    var f = DeskFrames()
    f.metersFrame = at(CGRect(x: 52, y: 820, width: 358, height: 60))
    f.meterRowFrames = [.fiveHour: at(CGRect(x: 52, y: 820, width: 358, height: 28)),
                        .sevenDay: at(CGRect(x: 52, y: 852, width: 358, height: 28))]
    f.meetingsFrame = at(CGRect(x: 1100, y: 800, width: 300, height: 60))
    f.rowFrames = ["m1": at(CGRect(x: 1100, y: 800, width: 300, height: 28)),
                   "m2": at(CGRect(x: 1100, y: 832, width: 300, height: 28))]
    f.nowPlayingFrame = at(CGRect(x: 52, y: 470, width: 360, height: 30))
    f.clockFrame = at(CGRect(x: 52, y: 60, width: 300, height: 150))
    f.stripFrame = at(CGRect(x: 700, y: 38, width: 200, height: 26))
    return f
}

private func elements(_ layout: String, on window: DeskWindowPlan.Window?, _ f: DeskFrames,
                      connected: [DeskScreenInfo] = two) -> [DeskElement] {
    DeskElements.build(DeskWindowElements.input(shared(layout), frames: f, arrangement: DeskArrangement(layout),
                                                window: window, connected: connected))
}

@Suite("Desk clicks on every window, 2.45")
struct DeskWindowFramesTests {
    // MARK: A piece on All displays

    @Test func anAllPieceIsAClickAreaOnTheMainAndTheExtraWindow() {
        let layout = "meters:bl@all meetings:br@all clock:tl"
        let onMain = elements(layout, on: mainWindow, frames(offset: .zero))
        let onRight = elements(layout, on: rightWindow, frames(offset: rightOffset))
        #expect(onMain.map(DeskFrameCheck.name).contains("meters"))
        #expect(onRight.map(DeskFrameCheck.name) == ["meters", "meter_row five_hour", "meter_row seven_day",
                                                     "meetings", "meeting_row 0", "meeting_row 1"])
        // Each copy has its own frame: the extra one sits where its window drew it, moved by its offset.
        let mainMeters = onMain.first { $0.kind == .meters }?.frame
        let rightMeters = onRight.first { $0.kind == .meters }?.frame
        #expect(mainMeters == CGRect(x: 52, y: 820, width: 358, height: 60))
        #expect(rightMeters == CGRect(x: 52 + 1512, y: 820 - 98, width: 358, height: 60))
        // The extra window's click layer gets plates in its own coordinates: where it drew them.
        let plates = DeskClickLayer.plates(in: onRight, offset: rightOffset)
        #expect(plates.areas == [DeskHitTest.area(DeskElement(kind: .meetingRow, key: "0",
                                                              frame: CGRect(x: 1100, y: 800, width: 300, height: 28)))])
        #expect(plates.slanted.isEmpty)
    }

    @Test func aClickOnTheExtraCopyFindsTheSameElementAsOneOnTheMainCopy() {
        let layout = "meters:bl@all meetings:br@all nowPlaying:ml@all"
        let onMain = elements(layout, on: mainWindow, frames(offset: .zero))
        let onRight = elements(layout, on: rightWindow, frames(offset: rightOffset))
        // The same spot in each window: the pointer reading is the main window's, so the extra
        // window's spot is moved by its offset.
        for local in [CGPoint(x: 1200, y: 812), CGPoint(x: 100, y: 860), CGPoint(x: 200, y: 485)] {
            let there = CGPoint(x: local.x + rightOffset.x, y: local.y + rightOffset.y)
            let a = DeskHitTest.element(at: local, in: onMain)
            let b = DeskHitTest.element(at: there, in: onRight)
            #expect(a != nil)
            #expect(a?.kind == b?.kind)
            #expect(a?.key == b?.key)
        }
        // Meeting row 0 on either copy is the first meeting, which has the link; row 1 takes no click.
        let joinThere = CGPoint(x: 1200 + rightOffset.x, y: 812 + rightOffset.y)
        #expect(DeskHitTest.element(at: joinThere, in: onRight)?.key == "0")
        #expect(DeskHitTest.element(at: CGPoint(x: 1200 + rightOffset.x, y: 846 + rightOffset.y), in: onRight) == nil)
        // The menus follow the element: a meter row's limit, now playing's, the shared menu.
        #expect(DeskHitTest.hasMenu(DeskHitTest.element(at: CGPoint(x: 100 + rightOffset.x, y: 860 + rightOffset.y), in: onRight)))
        // A point on the main display hits nothing among the extra window's elements, and back.
        #expect(DeskHitTest.element(at: CGPoint(x: 1200, y: 812), in: onRight) == nil)
        #expect(DeskHitTest.element(at: joinThere, in: onMain) == nil)
    }

    @Test func theClockAndMessageCopiesTakeTheirClicksToo() {
        let layout = "clock:tl@all"
        let onRight = elements(layout, on: rightWindow, frames(offset: rightOffset))
        let hit = DeskHitTest.element(at: CGPoint(x: 100 + rightOffset.x, y: 100 + rightOffset.y), in: onRight)
        #expect(hit?.kind == .clock)
        #expect(DeskHitTest.hasSharedMenu(hit))
    }

    // MARK: A piece on one display

    @Test func aPieceOnOneDisplayIsAClickAreaOnlyThere() {
        let layout = "meters:bl@\(idB) meetings:br clock:tl"
        // Stale frames on the other window (the piece used to draw there) never make it a click area.
        let onMain = elements(layout, on: mainWindow, frames(offset: .zero))
        let onRight = elements(layout, on: rightWindow, frames(offset: rightOffset))
        #expect(!onMain.contains { $0.kind == .meters || $0.kind == .meterRow })
        #expect(onMain.contains { $0.kind == .meetings })
        #expect(onRight.map(\.kind) == [.meters, .meterRow, .meterRow])
        #expect(DeskHitTest.element(at: CGPoint(x: 100, y: 860), in: onMain) == nil)
        #expect(DeskHitTest.element(at: CGPoint(x: 100 + rightOffset.x, y: 860 + rightOffset.y), in: onRight)?.kind
                == .meterRow)
    }

    @Test func theStripUnderTheCameraIsTheMainWindowsAlone() {
        let layout = "meters:bl@all"
        #expect(elements(layout, on: mainWindow, frames(offset: .zero)).contains { $0.key == "strip" })
        #expect(!elements(layout, on: rightWindow, frames(offset: rightOffset)).contains { $0.key == "strip" })
    }

    @Test func aPieceOnADisplayNotConnectedClicksOnTheMainWindow() {
        let layout = "meters:bl@\(idGone)"
        #expect(elements(layout, on: mainWindow, frames(offset: .zero)).first?.kind == .meters)
        #expect(elements(layout, on: rightWindow, frames(offset: rightOffset)).isEmpty)
    }

    // MARK: One display, as 2.44.0

    @Test func oneDisplayBuildsExactlyThe244Elements() {
        let layouts = ["message:tl clock:bl claude:bl meetings:bl",
                       "meters:bl@all meetings:br@\(idGone) clock:tl nowPlaying:ml watchers:mr",
                       "meters:tc meetings:mr claude:bc nowPlaying:ml toaster:bl"]
        let main = DeskWindowPlan.Window(screen: one[0], isMain: true)
        for layout in layouts {
            // 2.44.0: one Input with the frames in it, built and checked as it was.
            var before = shared(layout)
            before.take(frames(offset: .zero))
            before.watcherRows = before.placed.contains(WatcherPlacement.widget) ? ["w1"] : []
            var now = shared(layout)
            now.watcherRows = before.watcherRows
            let built = DeskElements.build(DeskWindowElements.input(now, frames: frames(offset: .zero),
                                                                    arrangement: DeskArrangement(layout),
                                                                    window: main, connected: one))
            #expect(built == DeskElements.build(before))
            // The screens not known yet: the same.
            let unknown = DeskElements.build(DeskWindowElements.input(now, frames: frames(offset: .zero),
                                                                      arrangement: DeskArrangement(layout),
                                                                      window: nil, connected: []))
            #expect(unknown == DeskElements.build(before))
            // The main click layer's plates are the 2.44.0 plates.
            let plates = DeskClickLayer.plates(in: built, offset: .zero)
            #expect(plates.areas == DeskClickLayer.areas(in: built))
            #expect(plates.slanted == DeskClickLayer.slantedAreas(in: built))
        }
    }

    @Test func oneDisplayHasOnlyTheMainWindowsFrames() {
        let model = DeskModel()
        #expect(model.windowFrames.keys.sorted { "\($0)" < "\($1)" } == [.main])
        model.metersFrame = CGRect(x: 1, y: 2, width: 3, height: 4)
        model.rowFrames["m1"] = CGRect(x: 5, y: 6, width: 7, height: 8)
        #expect(model.windowFrames.count == 1)
        #expect(model.frames.metersFrame == CGRect(x: 1, y: 2, width: 3, height: 4))
        #expect(model.frames(in: .main).rowFrames["m1"] == CGRect(x: 5, y: 6, width: 7, height: 8))
    }

    // MARK: The store

    @Test func eachWindowKeepsItsOwnFrames() {
        let model = DeskModel()
        var changes = 0
        model.onHitAreasChange = { changes += 1 }
        let key = DeskWindowElements.key(rightWindow)
        model.metersFrame = CGRect(x: 52, y: 820, width: 358, height: 60)
        model.setFrames(in: key) { $0.metersFrame = CGRect(x: 1564, y: 722, width: 358, height: 60) }
        #expect(changes == 2)
        // The same frame again moves nothing, so nothing is told.
        model.setFrames(in: key) { $0.metersFrame = CGRect(x: 1564, y: 722, width: 358, height: 60) }
        model.metersFrame = CGRect(x: 52, y: 820, width: 358, height: 60)
        #expect(changes == 2)
        #expect(model.metersFrame == CGRect(x: 52, y: 820, width: 358, height: 60))
        #expect(model.frames(in: key).metersFrame == CGRect(x: 1564, y: 722, width: 358, height: 60))
        // A window that goes takes its frames; the main window's stay.
        model.keepFrames(for: [])
        #expect(model.frames(in: key) == DeskFrames())
        #expect(model.metersFrame == CGRect(x: 52, y: 820, width: 358, height: 60))
    }

    @Test func windowsAreKeyedByTheirDisplay() {
        #expect(DeskWindowElements.key(mainWindow) == .main)
        #expect(DeskWindowElements.key(rightWindow) == .id(idB))
        #expect(DeskExtraWindow(geometry: DeskScreenGeometry.of(two[1], visibleFrame: right.frame, notch: nil),
                                offset: rightOffset).key == .id(idB))
        // Two screens without an id never share a window's frames.
        let a = DeskScreenInfo(id: nil, frame: CGRect(x: 1512, y: 0, width: 800, height: 600), primary: false)
        let b = DeskScreenInfo(id: nil, frame: CGRect(x: -800, y: 0, width: 800, height: 600), primary: false)
        #expect(DeskWindowElements.key(a, isMain: false) != DeskWindowElements.key(b, isMain: false))
        // A key finds its window among the connected screens, and none once unplugged.
        #expect(DeskWindowElements.window(.id(idB), connected: two) == rightWindow)
        #expect(DeskWindowElements.window(.main, connected: two) == mainWindow)
        #expect(DeskWindowElements.window(.id(idB), connected: one) == nil)
        #expect(DeskWindowElements.window(.main, connected: []) == nil)
    }

    @Test func showWidgetSitsBesideTheCopyItWasAskedFrom() {
        let key = DeskDisplay.id(idB)
        let m = CGRect(x: 52, y: 820, width: 358, height: 60)
        let r = CGRect(x: 1564, y: 722, width: 358, height: 60)
        var main = DeskFrames(); main.metersFrame = m
        var extra = DeskFrames(); extra.metersFrame = r
        #expect(DeskWindowElements.metersFrame([.main: main, key: extra], preferring: key) == r)
        #expect(DeskWindowElements.metersFrame([.main: main, key: extra], preferring: .main) == m)
        // The meters only on the other display: their frame there, wherever the menu was opened.
        #expect(DeskWindowElements.metersFrame([.main: DeskFrames(), key: extra], preferring: .main) == r)
        #expect(DeskWindowElements.metersFrame([.main: DeskFrames()], preferring: .main) == .zero)
    }

    @Test func anExtraWindowsSlantedPlatesMoveBackByItsOffset() {
        // Item 3 gives other windows folds; the plates already move with the window.
        let fold = DeskFold(tilt: 8, origin: CGPoint(x: 1600, y: 700))
        let e = DeskElement(kind: .meetingRow, key: "0", frame: CGRect(x: 1600, y: 700, width: 300, height: 28), fold: fold)
        let plates = DeskClickLayer.plates(in: [e], offset: rightOffset)
        let quad = DeskHitTest.quad(e)
        #expect(plates.slanted.count == 1)
        #expect(plates.slanted.first?.topLeft == quad.map { CGPoint(x: $0.topLeft.x - 1512, y: $0.topLeft.y + 98) })
    }
}
