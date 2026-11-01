import Foundation

/// State for the Now playing audio visualizer (spec 14): a short history of peak levels, newest at the
/// right, with fast attack and slow decay. It only ever sees peak values (0 to 1) from the playing app's
/// output; no audio is kept. Pure port of Windows' AudioBars: no Core Audio, no drawing, no timers.
final class AudioBars {
    /// 20 frames a second.
    static let frameInterval: TimeInterval = 0.05
    /// Knighthawk's silence threshold on the raw peak.
    static let idleThreshold: Float = 0.004
    /// How long every raw peak must stay under `idleThreshold` before the bars are idle.
    static let idleAfter: TimeInterval = 5

    private static let decay: Float = 0.82

    private var storedLevels: [Float]
    private var smoothed: Float = 0
    private var hasPushed = false
    private var quietSince: Date?

    /// Creates the bars with `count` zeroed slots (at least 1).
    init(count: Int = 16) {
        storedLevels = [Float](repeating: 0, count: max(1, count))
    }

    /// The stored levels, oldest first, newest last, always `count` long.
    var levels: [Float] { storedLevels }

    /// Appends one peak. A higher value is taken as is (attack); a lower one decays the previous
    /// smoothed level by 0.82. The stored value is the square root of the smoothed level, a perceptual
    /// curve. NaN counts as 0; the peak is clamped to 0..1.
    func push(_ peak: Float, now: Date) {
        let raw: Float = peak.isNaN ? 0 : min(max(peak, 0), 1)
        smoothed = raw > smoothed ? raw : smoothed * Self.decay
        storedLevels.removeFirst()
        storedLevels.append(min(max(smoothed.squareRoot(), 0), 1))

        if raw >= Self.idleThreshold { quietSince = nil }
        else if quietSince == nil { quietSince = now }
        hasPushed = true
    }

    /// True with nothing pushed yet, or once every raw peak has stayed under 0.004 for 5 seconds.
    func isIdle(now: Date) -> Bool {
        guard hasPushed else { return true }
        guard let since = quietSince else { return false }
        return now.timeIntervalSince(since) >= Self.idleAfter
    }

    /// Zeroes the levels and forgets the quiet run, as when the meter is released.
    func reset() {
        storedLevels = [Float](repeating: 0, count: storedLevels.count)
        smoothed = 0
        hasPushed = false
        quietSince = nil
    }
}

/// How the drawn bars move. Each bar eases toward its level in `AudioBars.levels`: up by a share of the
/// gap each frame (`attack`), down by a share of it (`decay`) but never faster than `falloff`, so a drop
/// reads as a fall rather than a cut. Over each bar a peak cap rides its highest point, holds there for
/// `capHoldFrames` frames, then drops with growing speed until it lands on the bar. `calm` (Reduce Motion)
/// keeps the bars calm: slower easing, no caps, and the source ticks at `calmFrameInterval`.
/// Pure: the Desk steps it once per frame.
final class AudioBarMotion {
    /// Share of a rise closed per frame.
    static let attack: Float = 0.55
    /// Share of a drop closed per frame, before the `falloff` limit.
    static let decay: Float = 0.35
    /// The most a bar drops in one frame (a full bar falls in a little over a second at 20 frames a second).
    static let falloff: Float = 0.045
    /// How long a cap holds at its peak before it drops: 9 frames, about 0.45 s.
    static let capHoldFrames = 9
    /// What a dropping cap's speed grows by each frame.
    static let capGravity: Float = 0.004
    /// The calm bars' easing: gentler both ways.
    static let calmAttack: Float = 0.3
    static let calmFalloff: Float = 0.02
    /// The calm bars update five times a second instead of twenty.
    static let calmFrameInterval: TimeInterval = 0.2

    private var storedHeights: [Float]
    private var storedCaps: [Float]
    private var capSpeed: [Float]
    private var hold: [Int]

    init(count: Int = 16) {
        let n = max(1, count)
        storedHeights = [Float](repeating: 0, count: n)
        storedCaps = [Float](repeating: 0, count: n)
        capSpeed = [Float](repeating: 0, count: n)
        hold = [Int](repeating: 0, count: n)
    }

    /// Reduce Motion is on: calm easing and no caps.
    var calm = false

    /// True when the caps are drawn (not `calm`).
    var showsCaps: Bool { !calm }

    /// Each bar's drawn height, 0 to 1.
    var heights: [Float] { storedHeights }

    /// Each bar's cap, 0 to 1, never below its bar.
    var caps: [Float] { storedCaps }

    /// One frame toward `levels` (one per bar; a missing one counts as 0, NaN as 0, the rest clamped to 0..1).
    func step(_ levels: [Float]) {
        let attack = calm ? Self.calmAttack : Self.attack
        let falloff = calm ? Self.calmFalloff : Self.falloff
        for i in 0..<storedHeights.count {
            let t: Float = i < levels.count && !levels[i].isNaN ? min(max(levels[i], 0), 1) : 0
            var h = storedHeights[i]
            if t > h { h += (t - h) * attack }
            else { h -= min((h - t) * Self.decay, falloff) }
            if h < 0.0005 { h = 0 }
            storedHeights[i] = h

            if calm {
                // No peak animation: the cap sits on the bar and is not drawn.
                storedCaps[i] = h
                capSpeed[i] = 0
                hold[i] = 0
                continue
            }
            if h >= storedCaps[i] {
                storedCaps[i] = h
                capSpeed[i] = 0
                hold[i] = Self.capHoldFrames
            } else if hold[i] > 0 {
                hold[i] -= 1
            } else {
                capSpeed[i] += Self.capGravity
                storedCaps[i] = max(h, storedCaps[i] - capSpeed[i])
            }
        }
    }

    /// Every bar and cap back to 0, as when the source stops.
    func reset() {
        let n = storedHeights.count
        storedHeights = [Float](repeating: 0, count: n)
        storedCaps = [Float](repeating: 0, count: n)
        capSpeed = [Float](repeating: 0, count: n)
        hold = [Int](repeating: 0, count: n)
    }
}
