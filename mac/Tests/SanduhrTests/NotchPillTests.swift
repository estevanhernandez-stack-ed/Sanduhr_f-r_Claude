import CoreGraphics
import Foundation
import Testing
@testable import Sanduhr

/// "Top-center island on screens without a notch" (NotchPill, issue #119): the default, the
/// switch's round trip, the pill's frame against the menu bar, when it shows, the pointer rule,
/// the debug override and the words. In-memory defaults only.
@Suite("Notch pill")
struct NotchPillTests {
    /// A 16:10 external display, AppKit coordinates, a 24 pt menu bar.
    static let screen = CGRect(x: 0, y: 0, width: 1920, height: 1200)
    static let visible = CGRect(x: 0, y: 80, width: 1920, height: 1096)

    @Test func anAbsentKeyReadsOffSoANotchedMacKeepsItsIsland() {
        let desk = MemoryDefaults()
        #expect(NotchPill.isOn(in: desk) == false)
        #expect(NotchPill.defaultOn == false)
        #expect(NotchPill.key == "notchPill")
    }

    @Test func aFreshInstallLeavesItOff() {
        let s = ScratchDefaults(), widget = MemoryDefaults()
        #expect(s.firstRun(widget: widget) == .fresh)
        #expect(s.defaults.object(forKey: "notchPill") == nil)
        #expect(NotchPill.isOn(in: s.defaults) == false)
    }

    @Test func theSwitchRoundTrips() {
        let desk = MemoryDefaults()
        NotchPill.set(true, in: desk)
        #expect(NotchPill.isOn(in: desk))
        NotchPill.set(false, in: desk)
        #expect(NotchPill.isOn(in: desk) == false)
    }

    @Test func theFrameIsNilWhileTheMenuBarIsHidden() {
        let fullScreen = CGRect(x: 0, y: 0, width: 1920, height: 1200)
        #expect(NotchPill.frame(screen: fullScreen, visibleFrame: fullScreen, contentWidth: 300) == nil)
        // A visible frame that somehow overshoots the screen counts as no bar too.
        let over = CGRect(x: 0, y: 0, width: 1920, height: 1201)
        #expect(NotchPill.frame(screen: fullScreen, visibleFrame: over, contentWidth: 300) == nil)
    }

    @Test func theFrameIsCenteredBarTallAtTheTopAndContentWide() {
        let f = NotchPill.frame(screen: Self.screen, visibleFrame: Self.visible, contentWidth: 300)
        #expect(f != nil)
        guard let f else { return }
        #expect(f.height == 24)
        #expect(f.minY == Self.visible.maxY)
        #expect(f.maxY == Self.screen.maxY)
        #expect(f.width == 300)
        #expect(f.midX == Self.screen.midX)
    }

    @Test func theFrameFollowsAScreenThatDoesNotStartAtTheOrigin() {
        let screen = CGRect(x: -2560, y: 300, width: 2560, height: 1440)
        let visible = CGRect(x: -2560, y: 300, width: 2560, height: 1415)
        let f = NotchPill.frame(screen: screen, visibleFrame: visible, contentWidth: 200)
        #expect(f?.height == 25)
        #expect(f?.minY == 1715)
        #expect(f?.midX == -1280)
        #expect(f?.width == 200)
    }

    @Test func theWidthIsCappedAtTheFractionOfTheScreen() {
        #expect(NotchPill.maxWidthFraction == 0.36)
        let f = NotchPill.frame(screen: Self.screen, visibleFrame: Self.visible, contentWidth: 5000)
        #expect(f?.width == 1920 * 0.36)
        #expect(abs((f?.midX ?? 0) - Self.screen.midX) < 0.001)
        // Exactly at the cap is not capped.
        let at = NotchPill.frame(screen: Self.screen, visibleFrame: Self.visible, contentWidth: 1920 * 0.36)
        #expect(at?.width == 1920 * 0.36)
    }

    @Test func theWidthIsNeverUnderTheHeightSoTheLeastPillIsRound() {
        let f = NotchPill.frame(screen: Self.screen, visibleFrame: Self.visible, contentWidth: 0)
        #expect(f?.width == 24)
        #expect(f?.height == 24)
        #expect(f?.midX == Self.screen.midX)
        let narrow = NotchPill.frame(screen: Self.screen, visibleFrame: Self.visible, contentWidth: 10)
        #expect(narrow?.width == 24)
    }

    @Test func itShowsOnlyWhenEveryConditionHolds() {
        #expect(NotchPill.shows(on: true, hasNotch: false, isMainScreen: true, menuBarVisible: true, pointerInMenuBar: false))
    }

    @Test func theSwitchOffHidesIt() {
        #expect(NotchPill.shows(on: false, hasNotch: false, isMainScreen: true, menuBarVisible: true, pointerInMenuBar: false) == false)
    }

    @Test func aNotchHidesItBecauseTheIslandDrawsThere() {
        #expect(NotchPill.shows(on: true, hasNotch: true, isMainScreen: true, menuBarVisible: true, pointerInMenuBar: false) == false)
    }

    @Test func anotherScreenHidesIt() {
        #expect(NotchPill.shows(on: true, hasNotch: false, isMainScreen: false, menuBarVisible: true, pointerInMenuBar: false) == false)
    }

    @Test func aHiddenMenuBarHidesIt() {
        #expect(NotchPill.shows(on: true, hasNotch: false, isMainScreen: true, menuBarVisible: false, pointerInMenuBar: false) == false)
    }

    @Test func thePointerInTheMenuBarHidesIt() {
        #expect(NotchPill.shows(on: true, hasNotch: false, isMainScreen: true, menuBarVisible: true, pointerInMenuBar: true) == false)
    }

    @Test func thePointerIsInTheBarAtTheTopEdgeAndNotJustUnderIt() {
        let screen = Self.screen, bar: CGFloat = 24
        #expect(NotchPill.pointerInMenuBar(CGPoint(x: 960, y: 1200), screen: screen, barHeight: bar))
        #expect(NotchPill.pointerInMenuBar(CGPoint(x: 960, y: 1190), screen: screen, barHeight: bar))
        // The bar's bottom edge belongs to the desktop below, not the bar.
        #expect(NotchPill.pointerInMenuBar(CGPoint(x: 960, y: 1176), screen: screen, barHeight: bar) == false)
        #expect(NotchPill.pointerInMenuBar(CGPoint(x: 960, y: 1100), screen: screen, barHeight: bar) == false)
        // Above the screen (another screen stacked on top) is not this bar.
        #expect(NotchPill.pointerInMenuBar(CGPoint(x: 960, y: 1201), screen: screen, barHeight: bar) == false)
    }

    @Test func thePointerOutsideTheScreensWidthIsNotInItsBar() {
        let screen = Self.screen, bar: CGFloat = 24
        #expect(NotchPill.pointerInMenuBar(CGPoint(x: -1, y: 1195), screen: screen, barHeight: bar) == false)
        #expect(NotchPill.pointerInMenuBar(CGPoint(x: 1920, y: 1195), screen: screen, barHeight: bar) == false)
        #expect(NotchPill.pointerInMenuBar(CGPoint(x: 0, y: 1195), screen: screen, barHeight: bar))
        #expect(NotchPill.pointerInMenuBar(CGPoint(x: 1919, y: 1195), screen: screen, barHeight: bar))
    }

    @Test func theDebugOverrideReadsFalseUntilTheHookWritesIt() {
        let desk = MemoryDefaults()
        #expect(NotchPill.debugForceKey == "notchPillForce")
        #expect(NotchPill.forced(in: desk) == false)
        desk.set(true, forKey: "notchPillForce")
        #expect(NotchPill.forced(in: desk))
        desk.set(false, forKey: "notchPillForce")
        #expect(NotchPill.forced(in: desk) == false)
    }

    @Test func theWordsSayWhereItDrawsAndWhenItHidesAndSearchFindsIt() {
        #expect(NotchPill.title == "Top-center island on screens without a notch")
        #expect(NotchPill.caption.contains("menu bar"))
        #expect(NotchPill.caption.contains("full screen"))
        #expect(NotchPill.caption.contains("pill"))
        #expect(!NotchPill.caption.contains("\u{2014}"))
        #expect(SettingsSearch.hits("no notch").contains { $0.entry.page == .notch })
        #expect(SettingsSearch.hits("pill").contains { $0.entry.page == .notch })
        #expect(SettingsSearch.hits("external display").contains { $0.entry.page == .notch })
    }
}
