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

    @Test func companionStaysOnMain() {
        let screens = named([mainScreen, right])
        let layout = "companion:br@\(idB)"
        let plan = DeskWindowPlan.plan(DeskArrangement(layout), screens: screens)
        #expect(plan.windows.count == 1)
        let extra = DeskWindowPlan.Window(screen: screens[1], isMain: false)
        #expect(drawn(layout, on: plan.windows[0], screens)[.br] == ["companion"])
        #expect(drawn(layout, on: extra, screens).isEmpty)
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
