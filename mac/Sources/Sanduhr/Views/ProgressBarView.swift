import SwiftUI

/// Fill bar plus the bright pace marker. sanduhr.py:527-536.
struct ProgressBarView: View {
    let utilization: Double            // 0–100
    let paceFraction: Double?          // 0–1, position of the marker
    let palette: Theme.Palette

    var body: some View {
        GeometryReader { g in
            ZStack(alignment: .leading) {
                // Track — subtle linear gradient instead of flat, for depth.
                LinearGradient(
                    colors: [palette.barBg.opacity(0.9), palette.barBg.opacity(0.6)],
                    startPoint: .top, endPoint: .bottom)

                // Fill with a top-to-bottom sheen in the tier color.
                let w = max(utilization / 100, 0.008) * g.size.width
                let base = usageColor(utilization)
                LinearGradient(
                    colors: [base.opacity(0.95), base.opacity(0.72)],
                    startPoint: .top, endPoint: .bottom)
                    .frame(width: w)
                    .overlay(
                        // Thin bright band along the top edge of the fill.
                        Rectangle()
                            .fill(Color.white.opacity(0.18))
                            .frame(width: w, height: 1)
                            .frame(maxHeight: .infinity, alignment: .top)
                    )

                // Pace marker — thin bright tick with a subtle glow.
                if let f = paceFraction {
                    palette.paceMarker
                        .frame(width: 2)
                        .shadow(color: palette.paceMarker.opacity(0.6),
                                radius: 2, x: 0, y: 0)
                        .offset(x: f * g.size.width - 1)
                }
            }
        }
        .frame(height: 10)
        .clipShape(RoundedRectangle(cornerRadius: 5, style: .continuous))
    }
}
