import CoreGraphics
import Foundation
import Testing
@testable import Sanduhr

private let idA = "6F1A2B3C-0000-4D5E-8F90-A1B2C3D4E5F6"
private let idB = "7A1A2B3C-0000-4D5E-8F90-A1B2C3D4E5F7"
private let idC = "8B1A2B3C-0000-4D5E-8F90-A1B2C3D4E5F8"

private let mainScreen = DeskScreenInfo(id: idA, frame: CGRect(x: 0, y: 0, width: 1512, height: 982), primary: true)
private let right = DeskScreenInfo(id: idB, frame: CGRect(x: 1512, y: 0, width: 1920, height: 1080), primary: false)
private let left = DeskScreenInfo(id: idC, frame: CGRect(x: -1280, y: 200, width: 1280, height: 720), primary: false)

private func named(_ s: [DeskScreenInfo]) -> [DeskScreenInfo] { DeskScreens.named(s) }

/// The widgets each window draws, by anchor.
private func drawn(_ layout: String, on window: DeskWindowPlan.Window, _ screens: [DeskScreenInfo]) -> [DeskAnchor: [String]] {
    DeskWindowPlan.stacks(DeskArrangement(layout), on: window, connected: screens).mapValues { $0.map(\.widget) }
}

@Suite("Desk window plan, 2.44")
struct DeskWindowPlanTests {
    @Test func oneScreenPlansOneWindowDrawingEverything() {
        let screens = named([mainScreen])
        let layout = "message:tl clock:bl@\(idB) claude:bl@all meetings:bl"
        let plan = DeskWindowPlan.plan(DeskArrangement(layout), screens: screens)
        #expect(plan.windows.count == 1)
        #expect(plan.windows[0].isMain)
        #expect(plan.extraScreens.isEmpty)
        // Exactly what one window drew before 2.44: every piece, by anchor, in layout order.
        let merged = DeskArrangement(layout).stacks().mapValues { $0.map(\.widget) }
        #expect(drawn(layout, on: plan.windows[0], screens) == merged)
    }

    @Test func twoScreensWithAPieceOnTheSecond() {
        let screens = named([mainScreen, right])
        let plan = DeskWindowPlan.plan(DeskArrangement("message:tl clock:tl@\(idB)"), screens: screens)
        #expect(plan.windows.map(\.screen.id) == [idA, idB])
        #expect(plan.windows.map(\.isMain) == [true, false])
        #expect(plan.extraScreens.map(\.id) == [idB])
    }

    @Test func secondScreenWithoutPiecesGetsNoWindow() {
        let screens = named([mainScreen, right])
        let plan = DeskWindowPlan.plan(DeskArrangement("message:tl clock:bl"), screens: screens)
        #expect(plan.windows.count == 1)
    }

    @Test func threeScreensOnlyThoseWithPieces() {
        let screens = named([mainScreen, right, left])
        let plan = DeskWindowPlan.plan(DeskArrangement("message:tl clock:bl@\(idC)"), screens: screens)
        #expect(plan.windows.map(\.screen.id) == [idA, idC])
        let all = DeskWindowPlan.plan(DeskArrangement("message:tl clock:bl@all"), screens: screens)
        #expect(all.windows.map(\.screen.id) == [idA, idB, idC])
    }

    @Test("2.45: the companion on another display gets that display's window, and main stays")
    func companionOnAnExtraDisplay() {
        let screens = named([mainScreen, right])
        let layout = "message:tl companion:br@\(idB)"
        let plan = DeskWindowPlan.plan(DeskArrangement(layout), screens: screens)
        #expect(plan.windows.count == 2)
        #expect(plan.main?.screen == screens[0])
        #expect(plan.extraScreens == [screens[1]])
        #expect(drawn(layout, on: plan.windows[0], screens) == [.tl: ["message"]])
        #expect(drawn(layout, on: plan.windows[1], screens) == [.br: ["companion"]])
        let p = DeskArrangement(layout).placement("companion")!
        #expect(DeskWindowPlan.window(drawing: p, connected: screens) == plan.windows[1])
    }

    @Test("2.45: the companion on All displays draws once, on main, and adds no window")
    func companionOnAllStaysOnMain() {
        let screens = named([mainScreen, right, left])
        let layout = "companion:br@all"
        let plan = DeskWindowPlan.plan(DeskArrangement(layout), screens: screens)
        #expect(plan.windows.count == 1)
        #expect(drawn(layout, on: plan.windows[0], screens)[.br] == ["companion"])
        for s in screens.dropFirst() {
            #expect(drawn(layout, on: DeskWindowPlan.Window(screen: s, isMain: false), screens).isEmpty)
        }
        let p = DeskArrangement(layout).placement("companion")!
        #expect(DeskWindowPlan.window(drawing: p, connected: screens)?.isMain == true)
        // Another piece on All still has a window on every display.
        #expect(DeskWindowPlan.plan(DeskArrangement("companion:br@all clock:tl@all"), screens: screens).windows.count == 3)
        // While arranging every display has a window, and still only main draws it.
        let arranging = DeskWindowPlan.plan(DeskArrangement(layout), screens: screens, arranging: true)
        #expect(arranging.windows.filter { DeskWindowPlan.draws(p, on: $0, connected: screens) }.map(\.isMain) == [true])
    }

    @Test("2.45: the companion's display unplugged puts it on main; back, it returns")
    func companionFallsBackAndReturns() {
        let layout = "companion:br@\(idB)"
        let p = DeskArrangement(layout).placement("companion")!
        let one = named([mainScreen])
        let unplugged = DeskWindowPlan.plan(DeskArrangement(layout), screens: one)
        #expect(unplugged.windows.count == 1)
        #expect(drawn(layout, on: unplugged.windows[0], one)[.br] == ["companion"])
        #expect(DeskWindowPlan.window(drawing: p, connected: one)?.isMain == true)
        let two = named([mainScreen, right])
        let back = DeskWindowPlan.plan(DeskArrangement(layout), screens: two)
        #expect(back.extraScreens == [two[1]])
        #expect(DeskWindowPlan.window(drawing: p, connected: two)?.screen == two[1])
        #expect(drawn(layout, on: back.windows[0], two).isEmpty)
    }

    @Test("2.45: one display, the companion roams main as in 2.44.0, whatever its display says")
    func companionOnOneDisplay() {
        let one = named([mainScreen])
        for word in ["companion:br", "companion:br@all", "companion:br@\(idA)", "companion:br@\(idB)"] {
            let plan = DeskWindowPlan.plan(DeskArrangement(word), screens: one)
            #expect(plan.windows.count == 1)
            #expect(drawn(word, on: plan.windows[0], one)[.br] == ["companion"])
            #expect(DeskWindowPlan.window(drawing: DeskArrangement(word).placement("companion")!, connected: one)?.isMain == true)
        }
    }

    @Test func aDisplayThatGoesAwayFallsBackToMainWithoutARewrite() {
        let layout = "message:tl clock:tl@\(idB) meetings:bl@\(idB)"
        let arrangement = DeskArrangement(layout)
        let screens = named([mainScreen])
        let plan = DeskWindowPlan.plan(arrangement, screens: screens)
        #expect(plan.windows.count == 1)
        #expect(drawn(layout, on: plan.windows[0], screens) == [.tl: ["message", "clock"], .bl: ["meetings"]])
        // Resolution only: the stored layout keeps the display.
        #expect(arrangement.string == DeskArrangement(layout).string)
        #expect(arrangement.placement("clock")?.monitor == .id(idB))
    }

    @Test func aPieceDrawsOnOneWindowUnlessOnAll() {
        let screens = named([mainScreen, right, left])
        let layout = "message:tl clock:tl@\(idB) claude:bl@all meetings:bl@\(idC) weather:tr@9F000000-0000-0000-0000-000000000000"
        let plan = DeskWindowPlan.plan(DeskArrangement(layout), screens: screens)
        #expect(plan.windows.count == 3)
        for p in DeskArrangement(layout).pieces {
            let count = plan.windows.filter { DeskWindowPlan.draws(p, on: $0, connected: screens) }.count
            #expect(count == (p.monitor == .all ? 3 : 1), "\(p.widget)")
        }
    }

    @Test func twoDisplaysTopLeftStaySeparate() {
        let screens = named([mainScreen, right])
        let layout = "message:tl clock:tl@\(idB) meetings:tl claude:tl@\(idB) weather:bl@all"
        let plan = DeskWindowPlan.plan(DeskArrangement(layout), screens: screens)
        #expect(drawn(layout, on: plan.windows[0], screens) == [.tl: ["message", "meetings"], .bl: ["weather"]])
        #expect(drawn(layout, on: plan.windows[1], screens) == [.tl: ["clock", "claude"], .bl: ["weather"]])
    }

    @Test func geometryComesFromEachWindowsOwnScreen() {
        let screens = named([mainScreen, right])
        let notch = CGRect(x: 662, y: 0, width: 188, height: 32)
        let mainVisible = CGRect(x: 0, y: 0, width: 1512, height: 945)
        let rightVisible = CGRect(x: 1512, y: 0, width: 1920, height: 1055)
        let g0 = DeskScreenGeometry.of(screens[0], visibleFrame: mainVisible, notch: notch)
        let g1 = DeskScreenGeometry.of(screens[1], visibleFrame: rightVisible, notch: nil)
        #expect(g0.deskSize == CGSize(width: 1512, height: 982))
        #expect(g0.topInset == 37)
        #expect(g0.notchRect == notch)
        #expect(g1.deskSize == CGSize(width: 1920, height: 1080))
        #expect(g1.topInset == 25)
        #expect(g1.notchRect == nil)
        #expect(g1.dockInsets == DockInsets())
        #expect(g1.screen == screens[1])
    }

    @Test func oneScreenGeometryMatchesTheOldBuild() {
        // buildWindow set deskSize = frame.size, topInset = frame.maxY - visibleFrame.maxY, notchRect = cameraNotch.
        let frame = CGRect(x: 0, y: 0, width: 1512, height: 982)
        let visible = CGRect(x: 0, y: 70, width: 1512, height: 873)
        let notch = CGRect(x: 662, y: 0, width: 188, height: 32)
        let info = named([DeskScreenInfo(id: idA, frame: frame, primary: true)])[0]
        let plan = DeskWindowPlan.plan(DeskArrangement("message:tl"), screens: [info])
        let g = DeskScreenGeometry.of(plan.windows[0].screen, visibleFrame: visible, notch: notch)
        #expect(g.deskSize == frame.size)
        #expect(g.topInset == frame.maxY - visible.maxY)
        #expect(g.notchRect == notch)
    }

    @Test func extraWindowsGetTheDockOnItsSideAndNothingElsewhere() {
        // The right display, 1920x1080, with the visible frame reduced on the Dock's side only.
        let frame = right.frame
        let bottom = CGRect(x: 1512, y: 70, width: 1920, height: 985)
        let leftSide = CGRect(x: 1512 + 80, y: 0, width: 1840, height: 1055)
        let rightSide = CGRect(x: 1512, y: 0, width: 1840, height: 1055)
        let none = CGRect(x: 1512, y: 0, width: 1920, height: 1055)
        func geo(_ side: DockSide, _ visible: CGRect, autohide: Bool = false) -> DeskScreenGeometry {
            DeskScreenGeometry.extra(right, visibleFrame: visible, notch: nil, dockPrefs: DockPrefs(side: side, autohide: autohide))
        }
        #expect(frame.size == CGSize(width: 1920, height: 1080))
        #expect(geo(.bottom, bottom).dockInsets == .on(.bottom, 70))
        #expect(geo(.left, leftSide).dockInsets == .on(.left, 80))
        #expect(geo(.right, rightSide).dockInsets == .on(.right, 80))
        // The Dock is on another display, or hides: nothing reserved here.
        #expect(geo(.bottom, none).dockInsets == DockInsets())
        #expect(geo(.bottom, bottom, autohide: true).dockInsets == DockInsets())
    }

    @Test func aNotchedExtraScreenKeepsItsNotchAndTheMainGeometryIsUnchanged() {
        let notch = CGRect(x: 640, y: 0, width: 188, height: 32)
        let visible = CGRect(x: 1512, y: 0, width: 1920, height: 1055)
        let g = DeskScreenGeometry.extra(right, visibleFrame: visible, notch: notch, dockPrefs: DockPrefs())
        #expect(g.notchRect == notch)
        #expect(g.topInset == 25)
        // The main window's geometry still comes from `of`, with the Dock left to DockFollower.
        let main = DeskScreenGeometry.of(mainScreen, visibleFrame: CGRect(x: 0, y: 0, width: 1512, height: 945), notch: nil)
        #expect(main.dockInsets == DockInsets())
        #expect(main.topInset == 37)
    }

    @Test func offsetsAreFromTheMainWindowsTopLeft() {
        let main = mainScreen.frame
        #expect(DeskWindowPlan.offset(of: main, from: main) == .zero)
        // Right of main, bottoms aligned: its top is 98 points higher than main's.
        #expect(DeskWindowPlan.offset(of: right.frame, from: main) == CGPoint(x: 1512, y: -98))
        // Left and raised: x negative, top 982 - 920 = 62 above main's top.
        #expect(DeskWindowPlan.offset(of: left.frame, from: main) == CGPoint(x: -1280, y: 62))
    }

    @Test func mainWindowPiecesLeaveOtherDisplaysOut() {
        let screens = named([mainScreen, right])
        let a = DeskArrangement("message:tl clock:tl@\(idB) weather:bl@all")
        #expect(DeskModel.mainWindowPieces(a, screens: screens).pieces.map(\.widget) == ["message", "weather"])
        #expect(DeskModel.mainWindowPieces(a, screens: []).pieces.count == 3)
    }

    @Test func noScreensPlansNothing() {
        #expect(DeskWindowPlan.plan(DeskArrangement("message:tl"), screens: []).windows.isEmpty)
    }
}

@Suite("The companion's room, 2.45")
struct CompanionRoomTests {
    /// The display right of main with a 70-point Dock at the bottom and a 25-point menu bar.
    private let extra = DeskScreenGeometry.extra(right, visibleFrame: CGRect(x: 1512, y: 70, width: 1920, height: 985),
                                                 notch: nil, dockPrefs: DockPrefs(side: .bottom))
    private let mainSize = CGSize(width: 1512, height: 982)
    private let mainDock = DockInsets.on(.left, 60)

    @Test func onAnExtraDisplayItRoamsThatWindowLessItsMenuBarAndDock() {
        let area = CompanionRoom.area(extra, mainSize: mainSize, mainTopInset: 37, mainDock: mainDock)
        #expect(area == CGRect(x: 0, y: 25, width: 1920, height: 1080 - 25 - 70))
        #expect(CompanionRoom.key(extra) == .id(idB))
    }

    @Test func onMainItRoamsTheModelsGeometryAsIn2440() {
        let area = CompanionRoom.area(nil, mainSize: mainSize, mainTopInset: 37, mainDock: mainDock)
        #expect(area == CompanionRoamMath.area(window: mainSize, topInset: 37, dock: mainDock))
        #expect(area == CGRect(x: 60, y: 37, width: 1452, height: 945))
        #expect(CompanionRoom.key(nil) == .main)
    }

    @Test func itReseatsWhenItChangesWindowsNotWhenItsWindowChanges() {
        #expect(!CompanionRoom.reseats(from: nil, to: nil))
        #expect(CompanionRoom.reseats(from: nil, to: extra))      // placed on the other display
        #expect(CompanionRoom.reseats(from: extra, to: nil))      // unplugged, or placed back on main
        var docked = extra
        docked.dockInsets = DockInsets()
        #expect(!CompanionRoom.reseats(from: extra, to: docked))  // the Dock moved off that display
        let other = DeskScreenGeometry.extra(left, visibleFrame: left.frame, notch: nil, dockPrefs: DockPrefs())
        #expect(CompanionRoom.reseats(from: extra, to: other))
    }
}
