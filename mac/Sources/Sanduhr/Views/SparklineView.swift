import SwiftUI

/// Sparkline widget. Two modes:
///
/// - `.line` — the classic quadratic-smoothed curve.
/// - `.horizon` — 4-band Heer/Tufte horizon chart (new v2.0.4 default).
///
/// Parity with `windows/src/sanduhr/sparkline.py`. The math is identical;
/// Canvas + Path replaces QPainter + QPainterPath.
struct SparklineView: View {
    let values: [Double]
    let color: Color
    var mode: Mode = .horizon

    enum Mode { case line, horizon }

    var body: some View {
        Canvas { ctx, size in
            guard values.count >= 2, size.width > 10, size.height > 4 else { return }
            switch mode {
            case .line:    paintLine(ctx: ctx, size: size)
            case .horizon: paintHorizon(ctx: ctx, size: size)
            }
        }
    }

    // MARK: - Line mode (quadratic-smoothed curve)

    private func paintLine(ctx: GraphicsContext, size: CGSize) {
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
            let mid = CGPoint(x: (prev.x + p.x) / 2, y: (prev.y + p.y) / 2)
            path.addQuadCurve(to: mid, control: prev)
            if i == points.count - 1 {
                path.addLine(to: p)
            }
        }
        ctx.stroke(path,
                   with: .color(color),
                   style: StrokeStyle(lineWidth: 1.5, lineCap: .round, lineJoin: .round))
    }

    // MARK: - Horizon mode (4-band Heer/Tufte chart)

    /// All 4 bands anchor to the bottom; each band's bar height is the value
    /// clamped to that band's ceiling, mapped onto the *full* widget height
    /// (not scaled into the band's vertical slice). That mapping is what
    /// lets tall values reach high while still having every band anchored at
    /// the bottom. Alpha compositing turns the overlap into dense dark
    /// regions at peaks and single-band soft washes at lulls.
    private func paintHorizon(ctx: GraphicsContext, size: CGSize) {
        // Horizon bands use absolute percentage space (0–100), not
        // normalized values. Matches windows/src/sanduhr/sparkline.py.
        let maxPct: Double = 100
        let n = values.count
        let colW = max(1, size.width / CGFloat(n))
        let bands = 4
        let bandStep = maxPct / Double(bands)  // 25 per band

        for band in 0..<bands {
            let bandFloor = Double(band) * bandStep
            let bandCeil  = Double(band + 1) * bandStep
            // Alpha rises with band index — lowest band softest, top band
            // densest. A tall value crosses every band so its column gets
            // all four alphas composited.
            let alpha = 0.18 + 0.20 * Double(band)  // 0.18 / 0.38 / 0.58 / 0.78
            let bandColor = color.opacity(alpha)

            for (i, v) in values.enumerated() where v > bandFloor {
                let effective = min(v, bandCeil)
                let barH = max(1, (effective / maxPct) * size.height)
                let rect = CGRect(
                    x: CGFloat(i) * colW,
                    y: size.height - barH,
                    width: colW,
                    height: barH)
                ctx.fill(Path(rect), with: .color(bandColor))
            }
        }
    }
}
