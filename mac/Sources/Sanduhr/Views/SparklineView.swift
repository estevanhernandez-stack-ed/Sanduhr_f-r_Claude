import SwiftUI

/// A tiny smoothed sparkline. Port of sanduhr.py:233-254, but using SwiftUI
/// `Canvas` + a quadratic-curved `Path` for a crisper line than Tk can draw.
struct SparklineView: View {
    let values: [Double]
    let color: Color

    var body: some View {
        Canvas { ctx, size in
            guard values.count >= 2, size.width > 10, size.height > 4 else { return }

            let mn = values.min() ?? 0
            let mx = values.max() ?? 1
            let rng = max(0.0001, mx - mn)

            let points: [CGPoint] = values.enumerated().map { i, v in
                let x = (CGFloat(i) / CGFloat(values.count - 1)) * size.width
                let y = size.height - ((v - mn) / rng) * (size.height - 2) - 1
                return CGPoint(x: x, y: y)
            }

            var path = Path()
            path.move(to: points[0])
            for i in 1..<points.count {
                let p = points[i]
                let prev = points[i - 1]
                let mid = CGPoint(x: (prev.x + p.x) / 2,
                                  y: (prev.y + p.y) / 2)
                path.addQuadCurve(to: mid, control: prev)
                if i == points.count - 1 {
                    path.addLine(to: p)
                }
            }
            ctx.stroke(path,
                       with: .color(color),
                       style: StrokeStyle(lineWidth: 1.5, lineCap: .round,
                                          lineJoin: .round))
        }
    }
}
