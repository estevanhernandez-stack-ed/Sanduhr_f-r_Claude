import Foundation
import SwiftUI

/// Progress bar: track, usage-colored fill with top sheen, a breathing-glass
/// accent-tinted shimmer over the fill, and an always-on 2px pace ghost tick
/// marking where pace says usage should be right now.
///
/// Parity with windows/src/sanduhr/tiers.py (TierCard paint loop).
struct ProgressBarView: View {
    let utilization: Double            // 0–100
    let paceFraction: Double?          // 0–1, position of the ghost tick
    let palette: Theme.Palette

    private let breathAmp: Double = 0.08  // subliminal — any higher reads as flicker

    var body: some View {
        GeometryReader { g in
            let fillWidth = max(utilization / 100, 0.008) * g.size.width
            ZStack(alignment: .leading) {
                // Track — subtle linear gradient instead of flat, for depth.
                LinearGradient(
                    colors: [palette.barBg.opacity(0.9), palette.barBg.opacity(0.6)],
                    startPoint: .top, endPoint: .bottom)

                // Fill with a top-to-bottom sheen in the tier color.
                let base = usageColor(utilization)
                LinearGradient(
                    colors: [base.opacity(0.95), base.opacity(0.72)],
                    startPoint: .top, endPoint: .bottom)
                    .frame(width: fillWidth)
                    .overlay(
                        Rectangle()
                            .fill(Color.white.opacity(0.18))
                            .frame(width: fillWidth, height: 1)
                            .frame(maxHeight: .infinity, alignment: .top)
                    )

                // Breathing glass — subliminal accent-tinted pulse over the
                // fill only, scoped inside the clipShape so empty tracks
                // (utilization=0) stay pure. Period driven by the theme so
                // Matrix pulses faster than the other themes.
                if utilization > 0 {
                    TimelineView(.animation(minimumInterval: 0.066)) { context in
                        breathOverlay(width: fillWidth, at: context.date)
                    }
                    .allowsHitTesting(false)
                }
            }
            .clipShape(RoundedRectangle(cornerRadius: 5, style: .continuous))
            .overlay(alignment: .leading) {
                // Always-on pace ghost — 2px tick in the theme's pace_marker
                // color, positioned at paceFraction * bar_width. Rendered in
                // the .overlay (outside clipShape) so it survives the rounded
                // corners on bars where the ghost is near the edge. Parity
                // with windows/src/sanduhr/tiers.py::_update_pace_tick.
                if let f = paceFraction {
                    let tickX = max(0, min(g.size.width - 2, f * g.size.width))
                    palette.paceMarker
                        .opacity(palette.ghostAlpha)
                        .frame(width: 2)
                        .offset(x: tickX)
                }
            }
        }
        .frame(height: 10)
    }

    /// One frame of the breathing-glass shimmer. Alpha oscillates via
    /// `amp + amp*sin(phase)`, so it never exceeds the rest state's
    /// brightness by more than 2×amp.
    @ViewBuilder
    private func breathOverlay(width w: CGFloat, at date: Date) -> some View {
        let periodMs = Double(palette.breathPeriodMs)
        let ms = date.timeIntervalSinceReferenceDate * 1000
        let phase = ms.truncatingRemainder(dividingBy: periodMs) / periodMs * 2.0 * .pi
        let alpha = breathAmp + breathAmp * sin(phase)
        palette.accent.opacity(alpha).frame(width: w)
    }
}
