import Foundation
import CoreGraphics

/// Where a Desk piece sits (item 59): the four corners, the top and bottom centers, and the
/// middles of the left and right edges. The raw values are the layout string's words.
enum DeskAnchor: String, CaseIterable, Hashable {
    case tl, tc, tr, ml, mr, bl, bc, br

    enum Column { case left, center, right }
    enum Row { case top, middle, bottom }

    var column: Column {
        switch self {
        case .tl, .ml, .bl: return .left
        case .tc, .bc: return .center
        case .tr, .mr, .br: return .right
        }
    }

    var row: Row {
        switch self {
        case .tl, .tc, .tr: return .top
        case .ml, .mr: return .middle
        case .bl, .bc, .br: return .bottom
        }
    }

    /// As Settings, Desk, Layout names it.
    var name: String {
        switch self {
        case .tl: return "Top left"
        case .tc: return "Top center"
        case .tr: return "Top right"
        case .ml: return "Middle left"
        case .mr: return "Middle right"
        case .bl: return "Bottom left"
        case .bc: return "Bottom center"
        case .br: return "Bottom right"
        }
    }

    /// The anchor in `column` at `row`, nil for the center's middle (there is none: that is the
    /// open middle of the screen).
    static func at(_ column: Column, _ row: Row) -> DeskAnchor? {
        allCases.first { $0.column == column && $0.row == row }
    }
}

/// One placed piece: its widget word, its anchor and its size (1 is the piece's own size).
struct DeskPlacement: Equatable, Hashable {
    var widget: String
    var anchor: DeskAnchor
    var scale: Double = 1
}

/// The Desk layout string read and written (item 59). Each word is `widget:anchor` or
/// `widget:anchor:scale`; within an anchor, pieces stack top to bottom in the string's order.
/// An older string (corners only, no sizes) reads exactly as before. A word for a widget this
/// build does not know is dropped; an anchor it does not know puts the piece at its default
/// anchor instead of hiding it; a size it cannot read is 1. A widget named twice keeps its last
/// word. A size of 1 is not written, so a string without sizes stays readable by older builds.
struct DeskArrangement: Equatable {
    /// Known widgets, each once, in order: the order within an anchor is the order here.
    var pieces: [DeskPlacement] = []

    /// A piece's size, as a share of its own size.
    static let scaleRange: ClosedRange<Double> = 0.6...1.6
    /// The size steps Settings offers (and item 60's handle snaps to).
    static let scaleStep = 0.1
    static var scaleSteps: [Double] {
        stride(from: 0.0, through: 10.0, by: 1.0).map { clampScale(scaleRange.lowerBound + $0 * scaleStep) }
    }

    /// Snaps to the step and keeps within the range; anything unreadable is 1.
    static func clampScale(_ s: Double) -> Double {
        guard s.isFinite else { return 1 }
        let snapped = (s / scaleStep).rounded() * scaleStep
        let clamped = min(scaleRange.upperBound, max(scaleRange.lowerBound, snapped))
        return (clamped * 100).rounded() / 100
    }

    /// Where a piece goes when its anchor is not one this build knows: where the standard
    /// layout puts it (the message top left, the rest bottom left with the clock).
    static func defaultAnchor(_ widget: String) -> DeskAnchor {
        widget == "message" ? .tl : .bl
    }

    /// The widget words this build knows, in Settings' order (also the order a newly placed
    /// piece takes among its neighbors).
    static var known: [String] { DeskLayout.widgets.map(\.key) }

    init(pieces: [DeskPlacement] = []) { self.pieces = pieces }

    init(_ layout: String) {
        let known = Set(Self.known)
        for word in layout.split(separator: " ") {
            let bits = word.split(separator: ":").map(String.init)
            guard bits.count >= 2, known.contains(bits[0]) else { continue }
            let anchor = DeskAnchor(rawValue: bits[1]) ?? Self.defaultAnchor(bits[0])
            let scale = bits.count >= 3 ? Double(bits[2]).map(Self.clampScale) ?? 1 : 1
            pieces.removeAll { $0.widget == bits[0] }
            pieces.append(DeskPlacement(widget: bits[0], anchor: anchor, scale: scale))
        }
    }

    /// The layout string: a word per piece, in order.
    var string: String {
        pieces.map(Self.word).joined(separator: " ")
    }

    static func word(_ p: DeskPlacement) -> String {
        guard p.scale != 1 else { return "\(p.widget):\(p.anchor.rawValue)" }
        return "\(p.widget):\(p.anchor.rawValue):\(String(format: "%g", p.scale))"
    }

    func placement(_ widget: String) -> DeskPlacement? {
        pieces.first { $0.widget == widget }
    }

    /// The pieces DeskView draws, in order: all of them, less the meetings with the older
    /// showMeetings switch off and the claude line and meters with showClaude off.
    func shown(showMeetings: Bool = true, showClaude: Bool = true) -> [DeskPlacement] {
        pieces.filter { p in
            if p.widget == "meetings" && !showMeetings { return false }
            if (p.widget == "claude" || p.widget == "meters") && !showClaude { return false }
            return true
        }
    }

    /// Each anchor's stack, top to bottom, of the pieces DeskView draws.
    func stacks(showMeetings: Bool = true, showClaude: Bool = true) -> [DeskAnchor: [DeskPlacement]] {
        var out: [DeskAnchor: [DeskPlacement]] = [:]
        for p in shown(showMeetings: showMeetings, showClaude: showClaude) {
            out[p.anchor, default: []].append(p)
        }
        return out
    }

    /// One anchor's stack, every piece placed there (switches aside), top to bottom.
    func stack(_ anchor: DeskAnchor) -> [DeskPlacement] {
        pieces.filter { $0.anchor == anchor }
    }

    // MARK: Changes

    /// Puts `widget` at `anchor`, or hides it (nil). Already there: nothing changes. Into an
    /// anchor that has pieces, it takes its place among them by Settings' order (before the
    /// first one listed after it), so the pieces already there keep yours. Into an empty anchor,
    /// a placed piece keeps its place in the string and a new one goes where Settings' order
    /// puts it. A piece keeps its size when it moves.
    mutating func place(_ widget: String, at anchor: DeskAnchor?) {
        let old = placement(widget)
        guard let anchor else {
            pieces.removeAll { $0.widget == widget }
            return
        }
        if old?.anchor == anchor { return }
        let moved = DeskPlacement(widget: widget, anchor: anchor, scale: old?.scale ?? 1)
        let rank = Self.rank(widget)
        if stack(anchor).isEmpty {
            if let i = pieces.firstIndex(where: { $0.widget == widget }) {
                pieces[i] = moved
            } else {
                let at = pieces.firstIndex { Self.rank($0.widget) > rank } ?? pieces.endIndex
                pieces.insert(moved, at: at)
            }
            return
        }
        pieces.removeAll { $0.widget == widget }
        let there = pieces.indices.filter { pieces[$0].anchor == anchor }
        let at = there.first { Self.rank(pieces[$0].widget) > rank } ?? (there.last.map { $0 + 1 } ?? pieces.endIndex)
        pieces.insert(moved, at: at)
    }

    /// Sets a placed piece's size (snapped and kept within range). A hidden piece has none.
    mutating func setScale(_ widget: String, _ scale: Double) {
        guard let i = pieces.firstIndex(where: { $0.widget == widget }) else { return }
        pieces[i].scale = Self.clampScale(scale)
    }

    /// Drops `widget` onto `target`, Settings' drag: in the same anchor it takes the target's
    /// place in the stack (coming from above it lands after it, from below before it); from
    /// another anchor it joins the target's anchor just before it. Nothing changes when either
    /// is not placed or they are the same.
    mutating func move(_ widget: String, onto target: String) {
        guard widget != target, let from = pieces.firstIndex(where: { $0.widget == widget }),
              let to = pieces.firstIndex(where: { $0.widget == target }) else { return }
        var piece = pieces[from]
        let sameAnchor = piece.anchor == pieces[to].anchor
        piece.anchor = pieces[to].anchor
        pieces.remove(at: from)
        let target = pieces.firstIndex { $0.widget == target } ?? pieces.endIndex
        pieces.insert(piece, at: sameAnchor && from <= to ? target + 1 : target)
    }

    /// Moves `widget` one place up (-1) or down (+1) in its stack; the stack's ends stay put.
    mutating func move(_ widget: String, by step: Int) {
        guard let p = placement(widget) else { return }
        let stack = stack(p.anchor).map(\.widget)
        guard let i = stack.firstIndex(of: widget), stack.indices.contains(i + step) else { return }
        move(widget, onto: stack[i + step])
    }

    /// The widget's place in Settings' order; an unknown word sorts last.
    static func rank(_ widget: String) -> Int {
        known.firstIndex(of: widget) ?? known.count
    }
}

/// The geometry the anchors share between the Desk and the Layout pane's map (item 59): how far
/// in the pieces sit, and how far the top center moves down to clear the notch or the island.
enum DeskAnchorGeometry {
    /// Room between the notch (or the island) and a top-center piece, in points.
    static let notchGap: CGFloat = 10

    /// The Desk's outer margins as DeskView pads its content (before the inner `inset` that
    /// gives handwritten glyphs room): the saved margins, the menu bar at the top and the
    /// Dock's reach on its side (item 56). Every anchor sits inside these, so the Dock
    /// clearance covers the centers and middles as it covers the corners.
    struct Margins: Equatable {
        var top: CGFloat
        var leading: CGFloat
        var bottom: CGFloat
        var trailing: CGFloat
    }

    static func margins(left: Double, right: Double, top: Double, bottom: Double,
                        topInset: CGFloat, dock: DockInsets, inset: CGFloat) -> Margins {
        Margins(top: max(0, topInset + CGFloat(top) - inset),
                leading: max(0, CGFloat(left) + dock.left - inset),
                bottom: max(0, CGFloat(bottom) + dock.bottom - inset),
                trailing: max(0, CGFloat(right) + dock.right - inset))
    }

    /// The rectangle the stacks hang in, in the Desk window's coordinates (top-left origin).
    static func content(window: CGSize, margins m: Margins, inset: CGFloat) -> CGRect {
        CGRect(x: m.leading + inset, y: m.top + inset,
               width: max(0, window.width - m.leading - m.trailing - inset * 2),
               height: max(0, window.height - m.top - m.bottom - inset * 2))
    }

    /// The bottom of what the camera takes at the top center, from the window's top: the notch,
    /// and the island's strip under it while the island draws (Desk's notch switch on and some
    /// strip height). 0 on a screen without a notch.
    static func notchBottom(notch: CGRect?, island: Bool, chin: Double) -> CGFloat {
        guard let notch else { return 0 }
        return notch.maxY + (island && chin > 0 ? CGFloat(chin) : 0)
    }

    /// How much further down than the other top anchors the top center starts, so it sits below
    /// the notch or the island with `notchGap` to spare. 0 when the margin already clears it.
    static func centerDrop(contentTop: CGFloat, notchBottom: CGFloat) -> CGFloat {
        guard notchBottom > 0 else { return 0 }
        return max(0, notchBottom + notchGap - contentTop)
    }

    /// The point an anchor's stack hangs from: its outer corner or edge middle in `content`
    /// (top anchors hang down from the top, bottom ones rest on the bottom, middles are
    /// centered on the height), the top center moved down by `centerDrop`.
    static func point(_ anchor: DeskAnchor, in content: CGRect, centerDrop: CGFloat = 0) -> CGPoint {
        let x: CGFloat
        switch anchor.column {
        case .left: x = content.minX
        case .center: x = content.midX
        case .right: x = content.maxX
        }
        switch anchor.row {
        case .top: return CGPoint(x: x, y: content.minY + (anchor == .tc ? centerDrop : 0))
        case .middle: return CGPoint(x: x, y: content.midY)
        case .bottom: return CGPoint(x: x, y: content.maxY)
        }
    }
}
