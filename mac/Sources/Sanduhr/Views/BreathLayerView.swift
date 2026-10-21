import AppKit
import SwiftUI

/// The breathing-glass pulse's numbers (ProgressBarView): an accent tint whose opacity rises and
/// falls as a sine around `amplitude` with the theme's period, so it never exceeds twice the
/// amplitude. Pure, so the animation's shape is pinned by a test.
struct BreathPulse: Equatable {
    let periodMs: Int
    let amplitude: Double

    /// The opacity at rest, and the fixed value under Reduce Motion: the sine's mean.
    var rest: Double { amplitude }
    /// The opacity swings between clear and this.
    var peak: Double { amplitude * 2 }
    /// Half the period: one rise or one fall of an autoreversing animation.
    var halfPeriod: TimeInterval { Double(periodMs) / 2000 }
    /// The opacity `seconds` into a cycle, the sine the frame-by-frame version drew.
    func opacity(at seconds: TimeInterval) -> Double {
        amplitude + amplitude * sin(seconds / (Double(periodMs) / 1000) * 2 * .pi)
    }
}

/// The pulse as a layer the render server animates: the accent breathing between clear and the
/// peak, autoreversing forever, with no frame work on the app's main thread (BACKLOG #7). The
/// frame-by-frame version redrew every filled bar fifteen times a second through a timeline view,
/// which was most of the app's idle CPU. Under Reduce Motion the layer sits at the rest opacity.
struct BreathLayerView: NSViewRepresentable {
    let color: NSColor
    let pulse: BreathPulse
    var reduceMotion = NSWorkspace.shared.accessibilityDisplayShouldReduceMotion

    func makeNSView(context: Context) -> BreathNSView { BreathNSView() }

    func updateNSView(_ view: BreathNSView, context: Context) {
        view.apply(color: color, pulse: pulse, reduceMotion: reduceMotion)
    }
}

final class BreathNSView: NSView {
    static let animationKey = "breath"
    private var applied: (NSColor, BreathPulse, Bool)?

    override init(frame: NSRect) {
        super.init(frame: frame)
        wantsLayer = true
        layerContentsRedrawPolicy = .never
    }

    required init?(coder: NSCoder) { nil }

    override var isOpaque: Bool { false }

    /// Sets the color and the animation once per change; a repeat with the same values is a no-op
    /// so SwiftUI's updates never restart the breath.
    func apply(color: NSColor, pulse: BreathPulse, reduceMotion: Bool) {
        guard let layer else { return }
        if let a = applied, a.0 == color, a.1 == pulse, a.2 == reduceMotion { return }
        applied = (color, pulse, reduceMotion)
        layer.backgroundColor = color.cgColor
        layer.removeAnimation(forKey: Self.animationKey)
        if reduceMotion {
            layer.opacity = Float(pulse.rest)
            return
        }
        layer.opacity = Float(pulse.peak)
        layer.add(Self.animation(pulse), forKey: Self.animationKey)
    }

    /// Clear to the peak and back, forever, eased so it reads as the sine did.
    static func animation(_ pulse: BreathPulse) -> CABasicAnimation {
        let a = CABasicAnimation(keyPath: "opacity")
        a.fromValue = 0
        a.toValue = pulse.peak
        a.duration = pulse.halfPeriod
        a.autoreverses = true
        a.repeatCount = .infinity
        a.timingFunction = CAMediaTimingFunction(name: .easeInEaseOut)
        a.isRemovedOnCompletion = false
        return a
    }
}
