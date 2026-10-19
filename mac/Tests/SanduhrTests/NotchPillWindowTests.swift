import AppKit
import CoreGraphics
import Foundation
import Testing
@testable import Sanduhr

/// The pill's window and what gathers around it (issue #119): when the Desk builds it, the
/// width its wings ask for, the frame as the glow and the camera light read it, which shape a
/// glow takes with the pill up, and the Notch preview's line. No controller is made; every rule
/// here is a pure function the controller calls.
@Suite("Notch pill window")
struct NotchPillWindowTests {
    @Test func aNotchedMacWithoutTheOverrideBuildsNoPill() {
        #expect(NotchPill.builds(on: true, hasNotch: true, forced: false) == false)
        // The override is the smoke tools' way onto a notched Mac.
        #expect(NotchPill.builds(on: true, hasNotch: true, forced: true))
    }

    @Test func theSwitchOffBuildsNoPillAnywhere() {
        #expect(NotchPill.builds(on: false, hasNotch: false, forced: false) == false)
        #expect(NotchPill.builds(on: false, hasNotch: true, forced: true) == false)
        #expect(NotchPill.builds(on: true, hasNotch: false, forced: false))
    }

    @Test func thePillIsBothWingsAndTheGapAndNothingWhenBothAreEmpty() {
        let both = NotchWingsView.Layout(left: 80, right: 120, leftText: "a", rightText: "b", size: 10)
        #expect(NotchWingsView.pillWidth(both) == 200 + NotchWingsView.pillGap)
        var beside = both
        beside.besideLeft = 24
        #expect(NotchWingsView.pillWidth(beside) == 224 + NotchWingsView.pillGap)
        let one = NotchWingsView.Layout(left: 0, right: 120, leftText: nil, rightText: "b", size: 10)
        #expect(NotchWingsView.pillWidth(one) == 120 + NotchWingsView.pillGap)
        // Both wings collapsed: 0, so the window hides instead of drawing a round dot.
        let none = NotchWingsView.Layout(left: 0, right: 0, leftText: nil, rightText: nil, size: 10)
        #expect(NotchWingsView.pillWidth(none) == 0)
        #expect(NotchWingsView.pillGap > 0)
    }

    @Test func thePillFrameReadsAsANotchHangingFromTheScreensTopLeft() {
        // A 1920 by 1200 screen at the origin, a 24-point bar, the pill 400 wide at the center.
        let screen = CGRect(x: 0, y: 0, width: 1920, height: 1200)
        let pill = CGRect(x: 760, y: 1176, width: 400, height: 24)
        #expect(DeskController.pillLocalRect(pill, screen: screen) == CGRect(x: 760, y: 0, width: 400, height: 24))
        // A screen left of and above the primary: still points from its own top-left corner.
        let other = CGRect(x: -2560, y: 300, width: 2560, height: 1440)
        let there = CGRect(x: -1480, y: 1715, width: 400, height: 25)
        #expect(DeskController.pillLocalRect(there, screen: other) == CGRect(x: 1080, y: 0, width: 400, height: 25))
        // The same rect the camera light hugs as a notch: its core is the pill's width.
        let shape = CameraLightLayout.shape(notch: DeskController.pillLocalRect(pill, screen: screen), barHeight: 24, size: 60)
        let wanted: CGFloat = 520   // the pill's 400 plus the light's 60-point side on each edge
        #expect(shape.width == wanted)
    }

    @Test func aGlowOutlinesThePillWhileDeskDrawsIt() {
        #expect(NotchGlowLayout.shape(deskRunning: true, notchOn: true, hasIsland: false, hasNotch: false, hasPill: true) == .top)
        // The island wins on a Mac that has both (the override on a notched Mac).
        #expect(NotchGlowLayout.shape(deskRunning: true, notchOn: true, hasIsland: true, hasNotch: true, hasPill: true) == .island)
        // The pill needs the island switch and the Desk, as the island does.
        #expect(NotchGlowLayout.shape(deskRunning: true, notchOn: false, hasIsland: false, hasNotch: false, hasPill: true) == .none)
        #expect(NotchGlowLayout.shape(deskRunning: false, notchOn: true, hasIsland: false, hasNotch: false, hasPill: true) == .none)
        // Without the pill, nothing changes: the plain notch, or the top fallback for Claude Code.
        #expect(NotchGlowLayout.shape(deskRunning: true, notchOn: true, hasIsland: false, hasNotch: true, hasPill: false) == .plain)
        #expect(NotchGlowLayout.shape(deskRunning: true, notchOn: true, hasIsland: false, hasNotch: false, hasPill: false, topFallback: true) == .top)
    }

    @Test func thePreviewSaysWhereThePillDraws() {
        let on = NotchPreview.spoken(enabled: true, hasNotch: false, pill: true)
        #expect(on.contains("pill") && on.contains("top center"))
        let islandOff = NotchPreview.spoken(enabled: false, hasNotch: false, pill: true)
        #expect(islandOff.contains("With the island on"))
        // The switch off reads as before; a notched Mac never mentions the pill.
        #expect(NotchPreview.spoken(enabled: true, hasNotch: false, pill: false).contains("small tab"))
        #expect(NotchPreview.spoken(enabled: true, hasNotch: true, pill: true).contains("pill") == false)
    }
}
