import CoreGraphics
import Foundation
import Testing
@testable import Sanduhr

/// 2.44, item 5: Arrange Desk across displays. A drop on another display's window moves the piece
/// there at the anchor it lands on; within one window a drop is what it was; Show on writes All
/// displays or the window's display; a piece on All displays stays on All; Cancel leaves the saved
/// string byte for byte; one display never offers Show on.

private final class CrossArrangeDefaults: DefaultsStore {
    var values: [String: Any] = [:]
    var writes: [String] = []
    func object(forKey key: String) -> Any? { values[key] }
    func bool(forKey key: String) -> Bool { values[key] as? Bool ?? false }
    func set(_ value: Any?, forKey key: String) {
        writes.append(key)
        values[key] = value
    }
}

private let idB = "7A1A2B3C-0000-4D5E-8F90-A1B2C3D4E5F7"

/// The main window, 1512 x 982, and a 1920 x 1080 display to its right (its top 98 points above
/// the main one's, in the main window's top-left coordinates).
private let mainGeometry = DeskArrangeGeometry(content: CGRect(x: 52, y: 64, width: 1408, height: 858), centerDrop: 0)
private let rightGeometry = DeskArrangeGeometry(content: CGRect(x: 52, y: 64, width: 1816, height: 956), centerDrop: 0)
private let mainWindow = DeskArrangeWindow(display: .main, rect: CGRect(x: 0, y: 0, width: 1512, height: 982),
                                           geometry: mainGeometry)
private let rightWindow = DeskArrangeWindow(display: .id(idB), rect: CGRect(x: 1512, y: -98, width: 1920, height: 1080),
                                            geometry: rightGeometry)

@Suite("Arrange across displays: the drop")
struct DeskArrangeCrossDropTests {
    @Test("a drop on another display's window writes that display and the nearest anchor there")
    func crossDropWritesDisplayAndAnchor() {
        let a = DeskArrangement("message:tl clock:bl:1.3 meters:bl")
        // Near the right display's top right corner, in its own coordinates.
        let out = DeskArrange.dropped(a, widget: "clock", from: .main, onto: .id(idB),
                                      at: CGPoint(x: 1800, y: 90), geometry: rightGeometry)
        let clock = out.placement("clock")
        #expect(clock?.monitor == .id(idB))
        #expect(clock?.anchor == .tr)
        #expect(clock?.scale == 1.3)
        // The other pieces are untouched, and the string round-trips.
        #expect(out.placement("meters") == a.placement("meters"))
        #expect(DeskArrangement(out.string) == out)
    }

    @Test("from another display back onto the main window writes main")
    func backToMain() {
        let a = DeskArrangement("message:tl clock:bl@\(idB)")
        let out = DeskArrange.dropped(a, widget: "clock", from: .id(idB), onto: .main,
                                      at: CGPoint(x: 60, y: 900), geometry: mainGeometry)
        #expect(out.placement("clock")?.monitor == .main)
        #expect(out.placement("clock")?.anchor == .bl)
        #expect(out.string == "message:tl clock:bl")
    }

    @Test("a drop within one window is the drop from before 2.44: a reorder, the display kept")
    func sameWindowUnchanged() {
        let start = "message:tl clock:bl meters:bl meetings:bl"
        let frames: [String: CGRect] = [
            "message": CGRect(x: 60, y: 70, width: 400, height: 80),
            "clock": CGRect(x: 60, y: 700, width: 300, height: 60),
            "meters": CGRect(x: 60, y: 780, width: 300, height: 60),
            "meetings": CGRect(x: 60, y: 860, width: 300, height: 40),
        ]
        let out = DeskArrange.dropped(DeskArrangement(start), widget: "meetings", from: .main, onto: .main,
                                      at: CGPoint(x: 100, y: 720), geometry: mainGeometry, frames: frames)
        var expected = DeskArrangement(start)
        expected.put("meetings", at: .bl, before: "clock")
        #expect(out == expected)
        #expect(out.string == "message:tl meetings:bl clock:bl meters:bl")
        // On another display's own window, within it: the piece keeps that display.
        let there = DeskArrangement("clock:bl@\(idB)")
        let moved = DeskArrange.dropped(there, widget: "clock", from: .id(idB), onto: .id(idB),
                                        at: CGPoint(x: 1800, y: 90), geometry: rightGeometry)
        #expect(moved.placement("clock")?.monitor == .id(idB))
        #expect(moved.placement("clock")?.anchor == .tr)
    }

    @Test("a piece on All displays keeps All, dragged within one window or onto another; only its anchor changes")
    func allStaysAll() {
        let a = DeskArrangement("message:tl clock:bl@all")
        let within = DeskArrange.dropped(a, widget: "clock", from: .main, onto: .main,
                                         at: CGPoint(x: 1450, y: 70), geometry: mainGeometry)
        #expect(within.placement("clock")?.monitor == .all)
        #expect(within.placement("clock")?.anchor == .tr)
        let across = DeskArrange.dropped(a, widget: "clock", from: .main, onto: .id(idB),
                                         at: CGPoint(x: 1800, y: 1000), geometry: rightGeometry)
        #expect(across.placement("clock")?.monitor == .all)
        #expect(across.placement("clock")?.anchor == .br)
    }

    @Test("the companion stays on the main display: a drop on another window changes nothing")
    func companionStays() {
        let a = DeskArrangement("message:tl companion:br")
        let out = DeskArrange.dropped(a, widget: "companion", from: .main, onto: .id(idB),
                                      at: CGPoint(x: 60, y: 90), geometry: rightGeometry)
        #expect(out == a)
    }

    @Test("the window under a point, in the main window's coordinates")
    func windowAtPoint() {
        let windows = [mainWindow, rightWindow]
        #expect(DeskArrange.window(at: CGPoint(x: 100, y: 100), in: windows)?.display == .main)
        #expect(DeskArrange.window(at: CGPoint(x: 1600, y: -50), in: windows)?.display == .id(idB))
        #expect(DeskArrange.window(at: CGPoint(x: -10, y: 100), in: windows) == nil)
    }
}

@Suite("Arrange across displays: the session")
struct DeskArrangeCrossSessionTests {
    private func arranging(_ saved: String) -> (DeskArrangeMode, CrossArrangeDefaults) {
        let d = CrossArrangeDefaults()
        d.values["layout"] = saved
        let mode = DeskArrangeMode(store: d)
        mode.begin()
        mode.setWindow(mainWindow)
        mode.setWindow(rightWindow)
        return (mode, d)
    }

    @Test("a drag let go past the main window's edge lands on the window there")
    func dropAcrossFromTheMainWindow() {
        let (mode, _) = arranging("message:tl clock:bl meters:bl")
        // The pointer at 1512 + 1800 - 1512 across, in the main window's own coordinates: on the right display.
        mode.drop("clock", at: CGPoint(x: 1512 + 1800, y: -98 + 90), from: mainWindow)
        #expect(mode.working?.placement("clock")?.monitor == .id(idB))
        #expect(mode.working?.placement("clock")?.anchor == .tr)
        #expect(mode.landing(of: "meters", at: CGPoint(x: 1512 + 60, y: -98 + 1000), from: mainWindow)?.window == .id(idB))
    }

    @Test("Cancel after moves across displays leaves the saved string byte for byte; Done writes once")
    func cancelRestoresAfterCrossMoves() {
        let saved = "message:tl clock:zz meters:bl  meetings:bl"
        let (mode, d) = arranging(saved)
        mode.drop("clock", at: CGPoint(x: 1512 + 1800, y: -98 + 90), from: mainWindow)
        mode.showOn("meters", all: true, window: .main)
        mode.drop("meetings", at: CGPoint(x: 1800, y: 1000), from: rightWindow)
        #expect(mode.working?.string != DeskArrangement(saved).string)
        mode.cancel()
        #expect(d.writes.isEmpty)
        #expect(d.values["layout"] as? String == saved)

        let (again, d2) = arranging(saved)
        again.drop("clock", at: CGPoint(x: 1512 + 1800, y: -98 + 90), from: mainWindow)
        #expect(again.done())
        #expect(d2.writes == ["layout"])
        #expect(DeskArrangement(d2.values["layout"] as? String ?? "").placement("clock")?.monitor == .id(idB))
    }
}

@Suite("Arrange across displays: Show on")
struct DeskArrangeShowOnTests {
    @Test("All displays sets all; This display goes back to the window's display, main or an id")
    func toggles() {
        let a = DeskArrangement("message:tl clock:bl")
        let all = DeskArrange.showOn(a, widget: "clock", all: true, window: .main)
        #expect(all.placement("clock")?.monitor == .all)
        let back = DeskArrange.showOn(all, widget: "clock", all: false, window: .main)
        #expect(back.placement("clock")?.monitor == .main)
        #expect(back.string == "message:tl clock:bl")
        let onB = DeskArrange.showOn(all, widget: "clock", all: false, window: .id(idB))
        #expect(onB.placement("clock")?.monitor == .id(idB))
        #expect(DeskArrange.showOn(onB, widget: "clock", all: true, window: .id(idB)).placement("clock")?.monitor == .all)
    }

    @Test("one display: Show on is not offered; two windows, or a piece already elsewhere: it is")
    func offeredOnlyWithDisplays() {
        let plain = DeskArrangement("message:tl clock:bl")
        #expect(!DeskArrange.offersShowOn(windows: 1, plain))
        #expect(DeskArrange.offersShowOn(windows: 2, plain))
        #expect(DeskArrange.offersShowOn(windows: 1, DeskArrangement("message:tl clock:bl@\(idB)")))
        #expect(DeskArrange.offersShowOn(windows: 1, DeskArrangement("clock:bl@all")))

        let d = CrossArrangeDefaults()
        d.values["layout"] = "message:tl clock:bl"
        let mode = DeskArrangeMode(store: d)
        #expect(!mode.offersShowOn)
        mode.begin()
        mode.setWindow(mainWindow)
        #expect(!mode.offersShowOn)
        mode.setWindow(rightWindow)
        #expect(mode.offersShowOn)
        mode.removeWindow(.id(idB))
        #expect(!mode.offersShowOn)
    }
}

@Suite("Arrange across displays: windows and the bar")
struct DeskArrangeDisplayWindowsTests {
    @Test("while arranging every display with an id has a window; after, only those with pieces")
    func everyDisplayWhileArranging() {
        let screens = DeskScreens.named([
            DeskScreenInfo(id: "6F1A2B3C-0000-4D5E-8F90-A1B2C3D4E5F6", frame: CGRect(x: 0, y: 0, width: 1512, height: 982), primary: true),
            DeskScreenInfo(id: idB, frame: CGRect(x: 1512, y: 0, width: 1920, height: 1080), primary: false),
            DeskScreenInfo(id: nil, frame: CGRect(x: -1280, y: 0, width: 1280, height: 720), primary: false),
        ])
        let a = DeskArrangement("message:tl clock:bl")
        #expect(DeskWindowPlan.plan(a, screens: screens).extraScreens.isEmpty)
        let arranging = DeskWindowPlan.plan(a, screens: screens, arranging: true)
        #expect(arranging.windows.map(\.isMain) == [true, false])
        #expect(arranging.extraScreens.map(\.id) == [idB])
    }

    @Test("the bar goes to the display under the pointer, and stays where it is off every display")
    func barFollowsPointer() {
        let screens = [CGRect(x: 0, y: 0, width: 1512, height: 982), CGRect(x: 1512, y: 0, width: 1920, height: 1080)]
        #expect(DeskArrange.barScreen(pointer: CGPoint(x: 100, y: 100), screens: screens, current: nil) == 0)
        #expect(DeskArrange.barScreen(pointer: CGPoint(x: 2000, y: 500), screens: screens, current: 0) == 1)
        #expect(DeskArrange.barScreen(pointer: CGPoint(x: -5, y: 5000), screens: screens, current: 1) == 1)
    }
}
