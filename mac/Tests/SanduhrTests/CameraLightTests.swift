import Foundation
import CoreGraphics
import Testing
@testable import Sanduhr

/// The camera light: the in-use decision fed by CoreMediaIO, where the light sits, and when it
/// shows. No cameras, windows or defaults involved.

@Suite("Camera activity")
struct CameraActivityTests {
    let t0 = Date(timeIntervalSince1970: 1_000)
    func at(_ s: Double) -> Date { t0.addingTimeInterval(s) }

    @Test func idleUntilACameraRuns() {
        var a = CameraActivity()
        #expect(a.reduce(.devices([1: false, 2: false]), now: t0) == nil)
        #expect(!a.inUse)
        #expect(a.reduce(.device(2, running: true), now: at(1)) == nil)
        #expect(a.inUse)
    }

    @Test func offOnlyAfterTheDelay() {
        var a = CameraActivity()
        a.reduce(.device(1, running: true), now: t0)
        let wait = a.reduce(.device(1, running: false), now: at(2))
        #expect(wait == CameraActivity.offDelay)
        #expect(a.inUse)
        // A settle that arrives early asks for the rest of the wait.
        let rest = a.reduce(.settle, now: at(2.2))
        #expect(rest.map { abs($0 - 0.3) < 0.0001 } == true)
        #expect(a.inUse)
        #expect(a.reduce(.settle, now: at(2.5)) == nil)
        #expect(!a.inUse)
        #expect(a.stoppedAt == nil)
    }

    @Test func flappingStaysOn() {
        var a = CameraActivity()
        a.reduce(.device(1, running: true), now: t0)
        for i in 0..<10 {
            let s = Double(i) * 0.2
            a.reduce(.device(1, running: false), now: at(s))
            #expect(a.inUse)
            a.reduce(.device(1, running: true), now: at(s + 0.1))
            #expect(a.inUse)
            #expect(a.stoppedAt == nil)
        }
        // The off timer starts again from the last stop, not the first.
        a.reduce(.device(1, running: false), now: at(5))
        #expect(a.reduce(.settle, now: at(5.4)) != nil)
        #expect(a.inUse)
        a.reduce(.settle, now: at(5.5))
        #expect(!a.inUse)
    }

    @Test func severalDevicesAnyOneKeepsItOn() {
        var a = CameraActivity()
        a.reduce(.devices([1: false, 2: false, 3: false]), now: t0)
        a.reduce(.device(1, running: true), now: at(1))
        a.reduce(.device(3, running: true), now: at(2))
        #expect(a.reduce(.device(1, running: false), now: at(3)) == nil)
        #expect(a.inUse)
        a.reduce(.settle, now: at(10))
        #expect(a.inUse)
        #expect(a.reduce(.device(3, running: false), now: at(11)) == CameraActivity.offDelay)
        a.reduce(.settle, now: at(11.5))
        #expect(!a.inUse)
    }

    @Test func unpluggingTheRunningCameraCountsAsStopping() {
        var a = CameraActivity()
        a.reduce(.devices([1: false, 2: true]), now: t0)
        #expect(a.inUse)
        #expect(a.reduce(.devices([1: false]), now: at(1)) == CameraActivity.offDelay)
        a.reduce(.settle, now: at(1.5))
        #expect(!a.inUse)
        #expect(a.running == [1: false])
    }

    @Test func settleWithNothingPendingDoesNothing() {
        var a = CameraActivity()
        #expect(a.reduce(.settle, now: t0) == nil)
        #expect(a == CameraActivity())
    }
}

@Suite("Camera light layout")
struct CameraLightLayoutTests {
    // A 14-inch MacBook Pro: 1512 x 982 points, notch 185 x 32 centered, menu bar 37 tall.
    let screen = CGRect(x: 0, y: 0, width: 1512, height: 982)
    let notch = CGRect(x: 663.5, y: 0, width: 185, height: 32)

    @Test func hugsTheNotchAndReachesBelowTheMenuBar() {
        let s = CameraLightLayout.shape(notch: notch, barHeight: 37, size: 60)
        #expect(s.width == 305)   // 60 each side
        #expect(s.height == 97)
        #expect(s.radius == 48.5)
        let f = CameraLightLayout.frame(screen: screen, notch: notch, barHeight: 37, size: 60)
        let feather = CameraLightLayout.feather
        #expect(f.midX == notch.midX)
        let overhang = CameraLightLayout.overhang
        #expect(f.maxY == screen.maxY + overhang)   // the blur fades out above the screen
        #expect(f.minY == screen.maxY - s.height - feather)
        #expect(f.width == s.width + feather * 2)
        #expect(f.height == s.height + feather + overhang)
        #expect(overhang >= feather / 2 * 3)   // covers the blur's reach
    }

    @Test func topCenterWithoutANotch() {
        let external = CGRect(x: 1512, y: -200, width: 2560, height: 1440)
        let f = CameraLightLayout.frame(screen: external, notch: nil, barHeight: 25, size: 40)
        #expect(f.midX == external.midX)
        #expect(f.maxY == external.maxY + CameraLightLayout.overhang)
        let s = CameraLightLayout.shape(notch: nil, barHeight: 25, size: 40)
        #expect(s.width == 280)
        #expect(s.height == 65)
    }

    @Test func notchOnASecondScreenUsesThatScreensOrigin() {
        let right = CGRect(x: 1920, y: 0, width: 1512, height: 982)
        let f = CameraLightLayout.frame(screen: right, notch: notch, barHeight: 37, size: 60)
        #expect(f.midX == 1920 + notch.midX)
    }

    @Test func lightShapeIsOneOutlineFromAboveTheEdge() {
        // 36 pt above the edge, 14 pt flares, 100 x 80 light: a 128 x 116 rect.
        let r = CGRect(x: 0, y: 0, width: 128, height: 116)
        let path = CameraLightShape(overhang: 36, flare: 14, radius: 20).path(in: r)
        #expect(path.boundingRect == r)
        #expect(path.contains(CGPoint(x: 64, y: 1)))        // the off-screen band, full width
        #expect(path.contains(CGPoint(x: 2, y: 30)))
        #expect(path.contains(CGPoint(x: 64, y: 36)))       // the screen edge, no seam
        #expect(path.contains(CGPoint(x: 64, y: 110)))      // the body
        #expect(!path.contains(CGPoint(x: 2, y: 60)))       // beside the light, under the flare
        #expect(!path.contains(CGPoint(x: 15, y: 115)))     // outside the rounded bottom corner
    }

    @Test func sizeIsClampedAndSidesKeepAMinimum() {
        let small = CameraLightLayout.shape(notch: notch, barHeight: 37, size: 0)
        #expect(small.height == 57)   // clamped to 20 below
        #expect(small.width == 257)   // 36 each side at least
        let big = CameraLightLayout.shape(notch: notch, barHeight: 37, size: 1_000)
        #expect(big.height == 237)   // clamped to 200 below
    }

    @Test func showsByHandOrForTheCameraWhenSwitchedOn() {
        #expect(!CameraLightLayout.showing(enabled: false, cameraInUse: false, manual: false))
        #expect(!CameraLightLayout.showing(enabled: false, cameraInUse: true, manual: false))
        #expect(!CameraLightLayout.showing(enabled: true, cameraInUse: false, manual: false))
        #expect(CameraLightLayout.showing(enabled: true, cameraInUse: true, manual: false))
        #expect(CameraLightLayout.showing(enabled: false, cameraInUse: false, manual: true))
    }
}
