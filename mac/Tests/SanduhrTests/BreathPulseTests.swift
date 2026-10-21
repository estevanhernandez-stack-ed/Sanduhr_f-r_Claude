import Foundation
import Testing
@testable import Sanduhr

/// The breathing glass as a layer animation (BACKLOG #7): the same swing the frame-by-frame
/// sine drew, now described once for Core Animation.
@Suite("Breathing glass pulse")
struct BreathPulseTests {
    let pulse = BreathPulse(periodMs: 2800, amplitude: 0.08)

    @Test func restIsTheMeanAndThePeakTwiceTheAmplitude() {
        #expect(pulse.rest == 0.08)
        #expect(pulse.peak == 0.16)
        #expect(pulse.halfPeriod == 1.4)
    }

    @Test func theSineRunsFromRestToPeakToClear() {
        #expect(abs(pulse.opacity(at: 0) - 0.08) < 1e-9)
        #expect(abs(pulse.opacity(at: 0.7) - 0.16) < 1e-9)
        #expect(abs(pulse.opacity(at: 2.1) - 0) < 1e-9)
        #expect(abs(pulse.opacity(at: 2.8) - 0.08) < 1e-9)
        // Never above twice the amplitude, never below clear.
        for t in stride(from: 0.0, through: 2.8, by: 0.05) {
            let o = pulse.opacity(at: t)
            #expect(o >= -1e-9 && o <= 0.16 + 1e-9)
        }
    }

    @Test func theLayerAnimationSwingsClearToPeakAndBackForever() {
        let a = BreathNSView.animation(pulse)
        #expect(a.keyPath == "opacity")
        #expect((a.fromValue as? Double) == 0)
        #expect((a.toValue as? Double) == 0.16)
        #expect(a.duration == 1.4)
        #expect(a.autoreverses)
        #expect(a.repeatCount == .infinity)
        #expect(!a.isRemovedOnCompletion)
    }

    @MainActor @Test func reduceMotionHoldsTheRestOpacityWithNoAnimation() {
        let v = BreathNSView(frame: NSRect(x: 0, y: 0, width: 10, height: 4))
        v.apply(color: .red, pulse: pulse, reduceMotion: true)
        #expect(v.layer?.animation(forKey: BreathNSView.animationKey) == nil)
        #expect(abs(Double(v.layer?.opacity ?? 0) - 0.08) < 1e-6)
        v.apply(color: .red, pulse: pulse, reduceMotion: false)
        #expect(v.layer?.animation(forKey: BreathNSView.animationKey) != nil)
        #expect(abs(Double(v.layer?.opacity ?? 0) - 0.16) < 1e-6)
    }
}
