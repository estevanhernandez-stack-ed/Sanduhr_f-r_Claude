import Foundation
import CoreGraphics
import SwiftUI

/// A piece's outline as drawn: its four corners, clockwise from the top left, after its stack's
/// slant (the fold, 2.37.0). An upright piece's quad is its rectangle. Windows' `DeskQuad`.
struct DeskQuad: Equatable {
    var topLeft: CGPoint
    var topRight: CGPoint
    var bottomRight: CGPoint
    var bottomLeft: CGPoint

    static func of(_ r: CGRect) -> DeskQuad {
        DeskQuad(topLeft: CGPoint(x: r.minX, y: r.minY), topRight: CGPoint(x: r.maxX, y: r.minY),
                 bottomRight: CGPoint(x: r.maxX, y: r.maxY), bottomLeft: CGPoint(x: r.minX, y: r.maxY))
    }

    var corners: [CGPoint] { [topLeft, topRight, bottomRight, bottomLeft] }

    /// The smallest upright rectangle around it.
    var bounds: CGRect {
        let xs = corners.map(\.x), ys = corners.map(\.y)
        guard let l = xs.min(), let r = xs.max(), let t = ys.min(), let b = ys.max() else { return .null }
        return CGRect(x: l, y: t, width: r - l, height: b - t)
    }

    /// True when the corners are those of an upright rectangle (no slant worth a polygon).
    var isUpright: Bool {
        abs(topLeft.y - topRight.y) < 0.5 && abs(bottomLeft.y - bottomRight.y) < 0.5
            && abs(topLeft.x - bottomLeft.x) < 0.5 && abs(topRight.x - bottomRight.x) < 0.5
    }

    /// The quad as a closed path, for the click layer's plates.
    var path: CGPath {
        let p = CGMutablePath()
        p.addLines(between: corners)
        p.closeSubpath()
        return p
    }
}

/// The fold's geometry (spec section 9, Windows' `DeskFold`): a stack slanted by its tilt is a
/// vertical shear, `y' = y + (x - origin.x) * tan(tilt)` about the stack's top left, so a piece's
/// frame becomes a parallelogram whose left and right edges stay upright and whose top and bottom
/// rise (negative tilt) or fall by `width * tan(tilt)`. Arrange Desk and the click areas hit-test
/// and outline that parallelogram, not the rectangle around it. Pure.
struct DeskFold: Equatable {
    /// Whole degrees, `DeskArrangement.tiltRange`. Negative rises to the right.
    var tilt: Double
    /// The stack's top left, in the space the frames are in: where the shear holds still.
    var origin: CGPoint

    /// `tan` of the (clamped) tilt: the rise per point of width.
    static func factor(_ tilt: Double) -> CGFloat {
        CGFloat(tan(DeskArrangement.clampTilt(tilt) * .pi / 180))
    }

    /// The transform a stack draws with: a shear about its own top left. Identity at 0.
    static func transform(_ tilt: Double) -> CGAffineTransform {
        CGAffineTransform(a: 1, b: factor(tilt), c: 0, d: 1, tx: 0, ty: 0)
    }

    /// How much taller a stack of this `width` is once slanted: the room the container reserves.
    static func extraHeight(width: CGFloat, tilt: Double) -> CGFloat {
        abs(width * factor(tilt))
    }

    /// How far the slanted content sits below the container's top: the rise, when it rises.
    static func lift(width: CGFloat, tilt: Double) -> CGFloat {
        max(0, -width * factor(tilt))
    }

    /// The quad `r` becomes under a vertical shear of `tilt` about its own top left (Windows'
    /// `DeskFold.Shear`): positive falls to the right, negative rises.
    static func shear(_ r: CGRect, tilt: Double) -> DeskQuad {
        DeskFold(tilt: tilt, origin: r.origin).quad(of: r)
    }

    /// Where a point in the unslanted layout is drawn.
    func apply(_ p: CGPoint) -> CGPoint {
        CGPoint(x: p.x, y: p.y + (p.x - origin.x) * Self.factor(tilt))
    }

    /// Where a point on screen is in the unslanted layout: the inverse of `apply`.
    func unapply(_ p: CGPoint) -> CGPoint {
        CGPoint(x: p.x, y: p.y - (p.x - origin.x) * Self.factor(tilt))
    }

    /// The unslanted layout frame of a view SwiftUI reported as `bounds`: the box around the view
    /// as drawn (a slanted view reports its bounds, not its frame). Undoes the shear of a
    /// rectangle: the width is the same, the height is less the slant's rise, and the top is the
    /// box's top less the rise (if it rises), moved back along the shear to the stack's origin.
    func layoutFrame(fromBounds b: CGRect) -> CGRect {
        guard !b.isEmpty else { return b }
        let f = Self.factor(tilt)
        let w = b.width
        let h = b.height - abs(w * f)
        // The left edge's top as drawn, then back along the shear.
        let drawnTop = b.minY - min(0, w * f)
        let top = drawnTop - (b.minX - origin.x) * f
        return CGRect(x: b.minX, y: top, width: w, height: max(0, h))
    }

    /// The stack's origin from the box SwiftUI reported around the whole slanted stack: the
    /// stack's own top left, which is the box's left and, when the stack rises, `lift` below its top.
    static func origin(ofStackBounds b: CGRect, tilt: Double) -> CGPoint {
        CGPoint(x: b.minX, y: b.minY + lift(width: b.width, tilt: tilt))
    }

    /// `r` (a frame in the unslanted layout) as drawn.
    func quad(of r: CGRect) -> DeskQuad {
        DeskQuad(topLeft: apply(CGPoint(x: r.minX, y: r.minY)), topRight: apply(CGPoint(x: r.maxX, y: r.minY)),
                 bottomRight: apply(CGPoint(x: r.maxX, y: r.maxY)), bottomLeft: apply(CGPoint(x: r.minX, y: r.maxY)))
    }

    /// The quad grown by `pad` on every side: each corner moves out along both axes, which keeps
    /// the slant of a sheared quad (its top and bottom move up and down together).
    static func grow(_ q: DeskQuad, by pad: CGFloat) -> DeskQuad {
        DeskQuad(topLeft: CGPoint(x: q.topLeft.x - pad, y: q.topLeft.y - pad),
                 topRight: CGPoint(x: q.topRight.x + pad, y: q.topRight.y - pad),
                 bottomRight: CGPoint(x: q.bottomRight.x + pad, y: q.bottomRight.y + pad),
                 bottomLeft: CGPoint(x: q.bottomLeft.x - pad, y: q.bottomLeft.y + pad))
    }

    /// True when `p` is inside the convex quad or on its edge. A degenerate quad (no area) holds
    /// nothing.
    static func contains(_ q: DeskQuad, _ p: CGPoint) -> Bool {
        let c = q.corners
        var sign = 0
        for i in 0..<4 {
            let a = c[i], b = c[(i + 1) % 4]
            let cross = Double((b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x))
            if abs(cross) < 1e-9 { continue }
            let s = cross > 0 ? 1 : -1
            if sign == 0 { sign = s } else if s != sign { return false }
        }
        return sign != 0 || area(q) > 0
    }

    /// The quad's area (shoelace), always positive.
    static func area(_ q: DeskQuad) -> Double {
        let c = q.corners
        var sum = 0.0
        for i in 0..<4 {
            let a = c[i], b = c[(i + 1) % 4]
            sum += Double(a.x * b.y - b.x * a.y)
        }
        return abs(sum) / 2
    }
}

extension DeskElement {
    /// The widget whose stack this element sits in; nil for the strip under the camera, which no
    /// stack holds.
    var stackWidget: String? {
        switch kind {
        case .meetingRow, .note, .meetings: return "meetings"
        case .account, .claudeLine: return "claude"
        case .meterRow, .meters: return "meters"
        case .nowPlaying: return key == "desk" ? "nowPlaying" : nil
        case .watcher: return key == "strip" ? nil : "watchers"
        case .clock: return "clock"
        case .message: return "message"
        case .nowPlayingNext, .avIndicators: return nil
        }
    }
}

extension DeskElements {
    /// The elements with their stack's slant attached: `folds` is what each drawn stack reported
    /// (its tilt and top left), `arrangement` says which stack a widget sits in. The frames come
    /// in as SwiftUI reports them, the box around what is drawn; a slanted element's `frame` goes
    /// out as its layout frame (`DeskElement.drawnFrame` gives the box back). A stack that is not
    /// slanted, or has not reported, leaves its elements as they were.
    static func folded(_ elements: [DeskElement], arrangement: DeskArrangement,
                       folds: [DeskAnchor: DeskFold]) -> [DeskElement] {
        elements.map { e in
            var e = e
            if let w = e.stackWidget, let anchor = arrangement.placement(w)?.anchor,
               let fold = folds[anchor], DeskArrangement.clampTilt(fold.tilt) != 0 {
                e.fold = fold
                // SwiftUI reported the box around the drawn piece; the quad is built on its layout frame.
                e.frame = fold.layoutFrame(fromBounds: e.frame)
            }
            return e
        }
    }
}

// MARK: - Drawing the fold

/// A stack slanted as one face: its content sheared about its own top left, in a slot tall enough
/// for the slant (Windows draws it as a `LayoutTransform`, so neighbours and the island's window
/// are sized for the slanted stack). The shear is a `transformEffect`, a single compositing
/// transform on the container: nothing redraws per frame, and nothing is applied at 0 (DeskView
/// does not build this at 0 at all).
struct DeskFoldLayout: Layout {
    var tilt: Double

    func sizeThatFits(proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) -> CGSize {
        guard let content = subviews.first else { return .zero }
        let s = content.sizeThatFits(proposal)
        return CGSize(width: s.width, height: s.height + DeskFold.extraHeight(width: s.width, tilt: tilt))
    }

    func placeSubviews(in bounds: CGRect, proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) {
        guard let content = subviews.first else { return }
        let s = content.sizeThatFits(proposal)
        content.place(at: CGPoint(x: bounds.minX, y: bounds.minY + DeskFold.lift(width: s.width, tilt: tilt)),
                      anchor: .topLeading, proposal: ProposedViewSize(width: s.width, height: s.height))
    }
}

/// What a slanted stack tells the rest of the Desk: its tilt and the box SwiftUI reports around
/// it, in the Desk's global space (the click areas) and in the arrange space (the drags).
private struct DeskFoldProbe: Equatable {
    var global: CGRect
    var arrange: CGRect
    var tilt: Double
}

/// A stack drawn slanted: `content` (the stack's pieces) in a slot that has room for the slant,
/// sheared as one, plate and Arrange outlines with it. Reports its top left and tilt to the model
/// (click areas) and to Arrange mode (drags) on layout changes only, and removes them when it goes.
struct DeskFoldStack<Content: View>: View {
    let anchor: DeskAnchor
    let tilt: Double
    var model: DeskModel
    @ViewBuilder var content: () -> Content

    var body: some View {
        DeskFoldLayout(tilt: tilt) {
            content()
                .background(probe)
                .transformEffect(DeskFold.transform(tilt))
        }
    }

    private var probe: some View {
        GeometryReader { geo in
            let now = DeskFoldProbe(global: geo.frame(in: .global), arrange: geo.frame(in: .named(DeskArrange.space)), tilt: tilt)
            Color.clear
                .onAppear { report(now) }
                .onChange(of: now) { _, p in report(p) }
                .onDisappear { clear() }
        }
    }

    private func report(_ p: DeskFoldProbe) {
        model.setFold(DeskFold(tilt: p.tilt, origin: DeskFold.origin(ofStackBounds: p.global, tilt: p.tilt)), at: anchor)
        model.arrange.setFold(DeskFold(tilt: p.tilt, origin: DeskFold.origin(ofStackBounds: p.arrange, tilt: p.tilt)), at: anchor)
    }

    private func clear() {
        model.setFold(nil, at: anchor)
        model.arrange.setFold(nil, at: anchor)
    }
}
