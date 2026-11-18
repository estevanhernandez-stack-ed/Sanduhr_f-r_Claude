import Foundation
import CoreGraphics
import Testing
@testable import Sanduhr

/// 2.45: the auto-hiding Dock's slide on every display. Which display the slide follows (the one
/// under the pointer, from the screens as values), the slide reaching another display's window
/// while the main one stays put and the reverse, separate Spaces off, and a one-display Mac as
/// before. Made-up displays: a 1512x982 main and a 1920x1080 to its left.

private let primaryHeight: CGFloat = 982
private let mainAppKit = CGRect(x: 0, y: 0, width: 1512, height: 982)
private let leftAppKit = CGRect(x: -1920, y: 0, width: 1920, height: 1080)
private let leftKey = DeskDisplay.id("3F2A6B1C-0D4E-4A5B-9C8D-7E6F5A4B3C2D")
private let mainCG = DockDisplay(key: .main, frame: DebugTree.topLeft(mainAppKit, primaryHeight: primaryHeight))
private let leftCG = DockDisplay(key: leftKey, frame: DebugTree.topLeft(leftAppKit, primaryHeight: primaryHeight))
/// The bottom edge of each display, where the pointer pins (one past the frame, as AppKit reads it).
private let mainEdge = CGPoint(x: 700, y: mainCG.frame.maxY)
private let leftEdge = CGPoint(x: -1000, y: leftCG.frame.maxY)
/// What an auto-hiding Dock of the default tile size is expected to be (DockGeometry.estimatedThickness).
private let thickness = DockGeometry.estimatedThickness(tilesize: DockPrefs.defaultTileSize)

@Suite("Dock target display")
struct DockTargetTests {
    @Test func thePointersDisplayIsTheTarget() {
        let both = [mainCG, leftCG]
        #expect(DockTarget.display(under: CGPoint(x: 100, y: 100), among: both)?.key == .main)
        #expect(DockTarget.display(under: CGPoint(x: -100, y: 500), among: both)?.key == leftKey)
        #expect(DockTarget.display(under: mainEdge, among: both)?.key == .main)
        #expect(DockTarget.display(under: leftEdge, among: both)?.key == leftKey)
        // Inside a frame wins over the next frame's slack at their shared edge.
        #expect(DockTarget.display(under: CGPoint(x: 0, y: 500), among: both)?.key == .main)
        #expect(DockTarget.display(under: CGPoint(x: -0.5, y: 500), among: both)?.key == leftKey)
        // Below the main display, beside the taller left one: on no Desk display.
        #expect(DockTarget.display(under: CGPoint(x: 700, y: 1060), among: both) == nil)
        #expect(DockTarget.display(under: CGPoint(x: -1000, y: 500), among: [mainCG]) == nil)
    }

    @Test func theLocalPointIsInThatDisplaysWindow() {
        let local = DockTarget.local(leftEdge, in: leftCG)
        #expect(local == CGPoint(x: 920, y: 1080))
        #expect(DockGeometry.atRevealEdge(local, size: leftCG.frame.size, side: .bottom))
        #expect(DockTarget.local(mainEdge, in: mainCG) == mainEdge)
        #expect(DockTarget.topLeft(CGPoint(x: 700, y: 0), primaryHeight: primaryHeight) == mainEdge)
    }

    @Test func onlyTheTargetSlides() {
        #expect(DockTarget.slideInsets(for: leftKey, target: leftKey, side: .bottom, inset: 70) == .on(.bottom, 70))
        #expect(DockTarget.slideInsets(for: .main, target: leftKey, side: .bottom, inset: 70) == DockInsets())
        #expect(DockTarget.slideInsets(for: .main, target: .main, side: .left, inset: 80) == .on(.left, 80))
    }

    @Test func separateSpacesFromMissionControlsSetting() {
        #expect(DockTarget.separateSpaces { _ in nil })
        #expect(DockTarget.separateSpaces { $0 == "spans-displays" ? NSNumber(value: false) : nil })
        #expect(!DockTarget.separateSpaces { $0 == "spans-displays" ? NSNumber(value: true) : nil })
        #expect(DockTarget.anticipates(on: .main, separateSpaces: false))
        #expect(DockTarget.anticipates(on: leftKey, separateSpaces: true))
        #expect(!DockTarget.anticipates(on: leftKey, separateSpaces: false))
    }
}

/// What a follower handed each window last.
private final class Applied {
    var main: [DockInsets] = []
    var extras: [DeskDisplay: [DockInsets?]] = [:]
    var lastMain: DockInsets? { main.last }
    func lastExtra(_ key: DeskDisplay) -> DockInsets? { extras[key]?.last ?? nil }
}

private func follower(separateSpaces: Bool = true, twoDisplays: Bool = true,
                      windows: @escaping () -> [DockGeometry.DockWindow] = { [] }) -> (DockFollower, Applied) {
    let f = DockFollower()
    let applied = Applied()
    f.prefsSource = { DockPrefs(side: .bottom, autohide: true, delay: 0) }
    f.separateSpacesSource = { separateSpaces }
    f.windowSource = windows
    f.apply = { insets, _ in applied.main.append(insets) }
    f.applyExtra = { key, insets, _ in applied.extras[key, default: []].append(insets) }
    f.setMain(frame: mainAppKit, visible: mainAppKit, primaryHeight: primaryHeight)
    if twoDisplays { f.setExtras([leftCG]) }
    return (f, applied)
}

@Suite("Dock slide on every display")
struct DockEveryDisplayTests {
    private let t0 = Date(timeIntervalSinceReferenceDate: 800_000_000)

    @Test func theExtraWindowSlidesAndMainStaysPut() {
        let (f, applied) = follower()
        #expect(f.wantsWatch(pointer: leftEdge))
        f.tick(pointer: leftEdge, moved: true, now: t0)
        #expect(f.target == leftKey)
        #expect(applied.lastExtra(leftKey) == .on(.bottom, thickness))
        #expect(applied.lastMain == DockInsets())
        #expect(f.insets == DockInsets())
        // Leaving the Dock's zone there: back to rest.
        f.tick(pointer: CGPoint(x: -1000, y: 400), moved: true, now: t0 + 0.1)
        #expect(applied.lastExtra(leftKey) == DockInsets())
        #expect(applied.lastMain == DockInsets())
    }

    @Test func mainSlidesAndTheExtraStaysPut() {
        let (f, applied) = follower()
        f.tick(pointer: mainEdge, moved: true, now: t0)
        #expect(f.target == .main)
        #expect(applied.lastMain == .on(.bottom, thickness))
        #expect(applied.lastExtra(leftKey) == DockInsets())
    }

    @Test func crossingToTheOtherDisplayHandsTheSlideOver() {
        let (f, applied) = follower()
        f.tick(pointer: leftEdge, moved: true, now: t0)
        #expect(applied.lastExtra(leftKey) == .on(.bottom, thickness))
        f.tick(pointer: mainEdge, moved: true, now: t0 + 0.2)
        #expect(f.target == .main)
        #expect(applied.lastMain == .on(.bottom, thickness))
        #expect(applied.lastExtra(leftKey) == DockInsets())
        #expect(f.insets(for: leftKey) == DockInsets())
    }

    @Test func theWindowListConfirmsOverTheTargetDisplayOnly() {
        // A Dock strip at the bottom of the left display.
        let strip = DockGeometry.DockWindow(layer: DockFollower.dockLayer,
                                            bounds: CGRect(x: -1920, y: leftCG.frame.maxY - 70, width: 1920, height: 70),
                                            onScreen: true)
        let (f, applied) = follower()
        f.tick(pointer: leftEdge, moved: true, now: t0)
        f.observe([strip], now: t0 + 0.1)
        #expect(applied.lastExtra(leftKey) == .on(.bottom, 70))
        #expect(applied.lastMain == DockInsets())
        // The same strip seen while the main display is the target is not over it: no slide there.
        let (g, onMain) = follower()
        g.tick(pointer: mainEdge, moved: true, now: t0)
        g.observe([strip], now: t0 + 1)
        #expect(onMain.lastMain == DockInsets())
    }

    @Test func separateSpacesOffWaitsForTheWindowList() {
        var shown: [DockGeometry.DockWindow] = []
        let (f, applied) = follower(separateSpaces: false, windows: { shown })
        f.tick(pointer: leftEdge, moved: true, now: t0)
        #expect(applied.lastExtra(leftKey) != .on(.bottom, thickness))
        #expect(f.slide.phase == .hidden)
        // Where the window list does show the Dock, it is followed there.
        shown = [DockGeometry.DockWindow(layer: DockFollower.dockLayer,
                                         bounds: CGRect(x: -1920, y: leftCG.frame.maxY - 70, width: 1920, height: 70),
                                         onScreen: true)]
        f.tick(pointer: leftEdge, moved: true, now: t0 + 0.1)
        #expect(applied.lastExtra(leftKey) == .on(.bottom, 70))
        // The main display still slides ahead of the list.
        let (g, onMain) = follower(separateSpaces: false)
        g.tick(pointer: mainEdge, moved: true, now: t0)
        #expect(onMain.lastMain == .on(.bottom, thickness))
    }

    @Test func aDisplayThatLosesItsWindowEndsItsSlide() {
        let (f, applied) = follower()
        f.tick(pointer: leftEdge, moved: true, now: t0)
        f.setExtras([])
        #expect(f.target == .main)
        #expect(f.slide.phase == .hidden)
        #expect(applied.lastMain == DockInsets())
    }

    @Test func anAlwaysShownDockLeavesTheExtraToItsGeometry() {
        let f = DockFollower()
        var extra: [DockInsets?] = []
        f.prefsSource = { DockPrefs(side: .bottom, autohide: false) }
        f.separateSpacesSource = { true }
        f.windowSource = { [] }
        f.applyExtra = { _, insets, _ in extra.append(insets) }
        f.setMain(frame: mainAppKit, visible: CGRect(x: 0, y: 70, width: 1512, height: 887), primaryHeight: primaryHeight)
        f.setExtras([leftCG])
        #expect(extra == [nil])
        #expect(f.insets == .on(.bottom, 70))
        #expect(!f.wantsWatch(pointer: leftEdge))
    }
}

@Suite("Dock slide on one display")
struct DockOneDisplayTests {
    private let t0 = Date(timeIntervalSinceReferenceDate: 800_000_000)

    @Test func theMainDisplayBehavesAsBefore() {
        let (f, applied) = follower(twoDisplays: false)
        #expect(f.extras.isEmpty)
        #expect(!f.wantsWatch(pointer: CGPoint(x: 700, y: 400)))
        #expect(f.wantsWatch(pointer: mainEdge))
        f.tick(pointer: mainEdge, moved: true, now: t0)
        #expect(f.target == .main)
        #expect(applied.lastMain == .on(.bottom, thickness))
        #expect(f.slide.phase != .hidden)
        f.tick(pointer: CGPoint(x: 700, y: 400), moved: true, now: t0 + 0.1)
        #expect(applied.lastMain == DockInsets())
        #expect(f.slide.phase == .hiding || f.slide.phase == .hidden)
        #expect(applied.extras.isEmpty)
    }

    @Test func aPointerOffTheOnlyDisplayStartsNothing() {
        let (f, applied) = follower(twoDisplays: false)
        f.tick(pointer: leftEdge, moved: true, now: t0)
        #expect(f.target == .main)
        #expect(f.slide.phase == .hidden)
        #expect(applied.lastMain == DockInsets())
        #expect(!f.wantsWatch(pointer: leftEdge))
        f.tick(pointer: nil, moved: false, now: t0 + 0.1)
        #expect(f.slide.phase == .hidden)
    }
}
