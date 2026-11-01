import Foundation
import Testing
@testable import Sanduhr

/// Ports of Windows' AudioBarsTests and the AudioBarMotion cases of DeskRideAlongsTests. Pure: no audio,
/// no drawing, made-up levels only.
@Suite("Audio bars")
struct AudioBarsTests {
    let t0 = Date(timeIntervalSince1970: 1_792_000_000)

    func close(_ a: Float, _ b: Float, _ tol: Float = 1e-5) -> Bool { abs(a - b) <= tol }

    @Test func startsAsZerosOfTheRequestedLength() {
        #expect(AudioBars().levels.count == 16)
        let bars = AudioBars(count: 5)
        #expect(bars.levels.count == 5)
        #expect(bars.levels.allSatisfy { $0 == 0 })
    }

    @Test func pushKeepsLengthAndShiftsNewestToTheRight() {
        let bars = AudioBars(count: 4)
        bars.push(0.25, now: t0)
        #expect(bars.levels.count == 4)
        #expect(close(bars.levels[3], 0.5))
        #expect(bars.levels[2] == 0)
        bars.push(1, now: t0)
        #expect(close(bars.levels[2], 0.5))
        #expect(close(bars.levels[3], 1))
    }

    @Test func attackTakesTheHigherValueImmediately() {
        let bars = AudioBars(count: 3)
        bars.push(0.1, now: t0)
        bars.push(0.64, now: t0)
        #expect(close(bars.levels[2], 0.8))
    }

    @Test func decayIsPreviousTimesPoint82ThenSquareRooted() {
        let bars = AudioBars(count: 3)
        bars.push(1, now: t0)
        bars.push(0, now: t0)
        #expect(close(bars.levels[2], Float(0.82).squareRoot()))
        bars.push(0, now: t0)
        #expect(close(bars.levels[2], Float(0.82 * 0.82).squareRoot()))
    }

    @Test func decayWithALowerNonZeroPeakStillDecaysFromPrevious() {
        let bars = AudioBars(count: 3)
        bars.push(1, now: t0)
        bars.push(0.2, now: t0)
        #expect(close(bars.levels[2], Float(0.82).squareRoot()))
    }

    @Test func nanCountsAsZeroAndOutOfRangeIsClamped() {
        let bars = AudioBars(count: 3)
        bars.push(.nan, now: t0)
        #expect(bars.levels[2] == 0)
        bars.push(7, now: t0)
        #expect(close(bars.levels[2], 1))
        bars.push(-3, now: t0)
        #expect((0...1).contains(bars.levels[2]))
    }

    @Test func idleAfterFiveSecondsOfQuiet() {
        let bars = AudioBars()
        bars.push(0.001, now: t0)
        #expect(!bars.isIdle(now: t0.addingTimeInterval(4.9)))
        #expect(bars.isIdle(now: t0.addingTimeInterval(5)))
    }

    @Test func idleARealPeakResetsTheClockAndRecoveryIsImmediate() {
        let bars = AudioBars()
        bars.push(0, now: t0)
        #expect(bars.isIdle(now: t0.addingTimeInterval(6)))
        bars.push(0.2, now: t0.addingTimeInterval(6))
        #expect(!bars.isIdle(now: t0.addingTimeInterval(6)))
        bars.push(0, now: t0.addingTimeInterval(7))
        #expect(!bars.isIdle(now: t0.addingTimeInterval(11.9)))
        #expect(bars.isIdle(now: t0.addingTimeInterval(12)))
    }

    @Test func idlePeakAtTheThresholdCountsAsSound() {
        let bars = AudioBars()
        bars.push(0.004, now: t0)
        #expect(!bars.isIdle(now: t0.addingTimeInterval(60)))
    }

    @Test func idleBeforeAnyPushIsTrue() {
        #expect(AudioBars().isIdle(now: t0))
    }

    @Test func resetZeroesLevelsAndGoesIdle() {
        let bars = AudioBars(count: 3)
        bars.push(1, now: t0)
        bars.reset()
        #expect(bars.levels.allSatisfy { $0 == 0 })
        #expect(bars.isIdle(now: t0))
        bars.push(0, now: t0)
        #expect(bars.levels[2] == 0)
    }

    @Test func frameIntervalIsFiftyMilliseconds() {
        #expect(AudioBars.frameInterval == 0.05)
    }

    @Test func constantsMatchWindows() {
        #expect(AudioBars.idleThreshold == 0.004)
        #expect(AudioBars.idleAfter == 5)
        #expect(AudioBarMotion.attack == 0.55)
        #expect(AudioBarMotion.decay == 0.35)
        #expect(AudioBarMotion.falloff == 0.045)
        #expect(AudioBarMotion.capHoldFrames == 9)
        #expect(AudioBarMotion.capGravity == 0.004)
        #expect(AudioBarMotion.calmAttack == 0.3)
        #expect(AudioBarMotion.calmFalloff == 0.02)
        #expect(AudioBarMotion.calmFrameInterval == 0.2)
    }

    // MARK: Motion

    @Test func aBarRisesByAShareOfTheGapAndFallsNoFasterThanTheFalloff() {
        let m = AudioBarMotion(count: 2)
        m.step([1, 0])
        #expect(close(m.heights[0], AudioBarMotion.attack))
        for _ in 0..<30 { m.step([1, 0]) }
        #expect(close(m.heights[0], 1, 1e-3))
        m.step([0, 0])
        // A share of 1 is 0.35; the falloff limits it.
        #expect(close(m.heights[0], 1 - AudioBarMotion.falloff, 1e-4))
        for _ in 0..<60 { m.step([0, 0]) }
        #expect(m.heights[0] == 0)
        // A small drop decays by its share, under the falloff.
        let s = AudioBarMotion(count: 1)
        for _ in 0..<40 { s.step([0.5]) }
        s.step([0.46])
        #expect(close(s.heights[0], 0.5 - (0.5 - 0.46) * AudioBarMotion.decay, 1e-3))
    }

    @Test func aCapHoldsAtThePeakThenDropsFasterAndFasterOntoTheBar() {
        let m = AudioBarMotion(count: 1)
        for _ in 0..<30 { m.step([0.9]) }
        let peak = m.caps[0]
        #expect(close(m.heights[0], peak))
        for _ in 0..<AudioBarMotion.capHoldFrames {
            m.step([0])
            #expect(close(m.caps[0], peak))
            #expect(m.heights[0] < peak)
        }
        m.step([0])
        let first = peak - m.caps[0]
        m.step([0])
        let second = peak - first - m.caps[0]
        #expect(first > 0 && second > first)
        for _ in 0..<200 { m.step([0]) }
        #expect(m.caps[0] == 0)
        #expect(m.caps[0] >= m.heights[0])
    }

    @Test func calmBarsEaseGentlyAndDrawNoCaps() {
        let calm = AudioBarMotion(count: 1)
        calm.calm = true
        let lively = AudioBarMotion(count: 1)
        calm.step([1])
        lively.step([1])
        #expect(calm.heights[0] < lively.heights[0])
        #expect(!calm.showsCaps)
        #expect(lively.showsCaps)
        for _ in 0..<40 { calm.step([1]) }
        calm.step([0])
        // The cap sits on the bar: no hold, no drop.
        #expect(calm.caps[0] == calm.heights[0])
        #expect(1 - calm.heights[0] <= AudioBarMotion.calmFalloff + 1e-6)
        #expect(AudioBarMotion.calmFrameInterval > AudioBars.frameInterval)
    }

    @Test func theMotionTakesOddLevelsAndResets() {
        let m = AudioBarMotion(count: 3)
        m.step([.nan, 7])   // NaN is 0, 7 is 1, the missing third is 0
        #expect(m.heights[0] == 0)
        #expect(close(m.heights[1], AudioBarMotion.attack))
        #expect(m.heights[2] == 0)
        m.reset()
        #expect((m.heights + m.caps).allSatisfy { $0 == 0 })
        #expect(AudioBarMotion(count: 0).heights.count == 1)
    }

    @Test func gradientBarsKeyIsTheSpecNameAndOffByDefault() {
        #expect(NowPlayingPrefs.barsGradientKey == "nowPlaying.barsGradient")
        let name = "sanduhr.tests.audiobars.gradient"
        let d = UserDefaults(suiteName: name)!
        defer { d.removePersistentDomain(forName: name) }
        #expect(d.bool(forKey: NowPlayingPrefs.barsGradientKey) == false)
    }
}
