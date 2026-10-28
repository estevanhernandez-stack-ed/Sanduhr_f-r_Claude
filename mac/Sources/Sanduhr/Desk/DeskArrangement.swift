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

/// How a meter-like piece draws its numbers (the layout word's fifth field): `bar` is today's
/// full-width bar and the default, `slim` a one-line label over a thin bar, `ring` a small arc
/// gauge, `ticks` ten segmented cells. The raw values are the layout and design words.
enum DeskMeterStyle: String, CaseIterable, Hashable {
    case bar, slim, ring, ticks
}

/// Words, names and the pieces that take a `DeskMeterStyle`.
enum DeskMeterStyles {
    /// The pieces that have a style: the Claude line and the Claude limits. 2.38.0 adds `system`
    /// here (Windows also lists `telemetry`); everything below reads this one list.
    static let pieces = ["claude", "meters"]

    static func supports(_ widget: String) -> Bool { pieces.contains(widget) }

    /// The layout and design word: "bar", "slim", "ring", "ticks".
    static func word(_ s: DeskMeterStyle) -> String { s.rawValue }

    /// The style for an exact lowercase word, nil for anything else.
    static func parse(_ word: String?) -> DeskMeterStyle? { word.flatMap(DeskMeterStyle.init(rawValue:)) }

    /// As Settings, Desk names it.
    static func name(_ s: DeskMeterStyle) -> String {
        switch s {
        case .bar: return "Bars"
        case .slim: return "Slim"
        case .ring: return "Rings"
        case .ticks: return "Ticks"
        }
    }

    /// The style a piece can carry: `style` for a piece that has one, `.bar` for any other.
    static func `for`(_ widget: String, _ style: DeskMeterStyle) -> DeskMeterStyle {
        supports(widget) ? style : .bar
    }

    /// Why a design or layout may not give `widget` the style word `word`, nil when it may. Windows'
    /// messages, with this build's pieces: a style only on a piece that draws one, and only a known word.
    static func refusal(widget: String, word: String?) -> String? {
        guard supports(widget) else { return "only " + pieces.joined(separator: ", ") + " have a style" }
        guard parse(word?.trimmingCharacters(in: .whitespaces).lowercased()) != nil else {
            return "must be one of " + DeskMeterStyle.allCases.map(\.rawValue).joined(separator: ", ")
        }
        return nil
    }
}

/// One placed piece: its widget word, its anchor and its size (1 is the piece's own size).
/// `tilt` is the slant, in whole degrees, of the stack the piece sits in (the fold): the pieces of
/// one anchor share it (`DeskArrangement.setTilt`), and a stack whose words disagree slants by its
/// first piece's. `style` is how a meter-like piece draws (`.bar` for every other piece).
struct DeskPlacement: Equatable, Hashable {
    var widget: String
    var anchor: DeskAnchor
    var scale: Double = 1
    var tilt: Double = 0
    var style: DeskMeterStyle = .bar
}

/// Two pieces of one stack given different slants: the refusal a design or layout check reports.
/// `field` and `message` are Windows' (`layout.<first piece>.tilt`).
struct DeskTiltConflict: Equatable {
    var anchor: DeskAnchor
    var pieces: [String]
    var field: String { "layout.\(pieces[0]).tilt" }
    var message: String {
        "the pieces at \(anchor.rawValue) share one slant; give \(pieces.joined(separator: " and ")) the same tilt"
    }
}

/// The Desk layout string read and written (item 59). Each word is `widget:anchor`,
/// `widget:anchor:scale`, `widget:anchor:scale:tilt` (the fold: whole degrees -20 to 20, written
/// only when not 0, with the size written as 1 to hold its place) or `widget:anchor:scale:tilt:style`
/// (`meters:tr:1:0:ring`: only on the pieces `DeskMeterStyles.pieces` names, written only when not
/// `bar`, the size and tilt holding their places; an older build reads the size and ignores the rest);
/// within an anchor, pieces stack top to bottom in the string's order.
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

    /// The fold's range: a stack slants at most this many degrees either way. Negative rises to
    /// the right, positive falls.
    static let tiltRange: ClosedRange<Double> = -20...20

    /// Whole degrees within `tiltRange`; anything unreadable is 0 (no slant), and never -0.
    static func clampTilt(_ t: Double) -> Double {
        guard t.isFinite else { return 0 }
        let whole = min(tiltRange.upperBound, max(tiltRange.lowerBound, t)).rounded()
        return whole == 0 ? 0 : whole
    }

    /// Snaps to the step and keeps within the range; anything unreadable is 1.
    static func clampScale(_ s: Double) -> Double {
        guard s.isFinite else { return 1 }
        let snapped = (s / scaleStep).rounded() * scaleStep
        let clamped = min(scaleRange.upperBound, max(scaleRange.lowerBound, snapped))
        return (clamped * 100).rounded() / 100
    }

    /// Where a piece goes when its anchor is not one this build knows: where the standard
    /// layout puts it (the message top left, the companion bottom center, the rest bottom left with the clock).
    static func defaultAnchor(_ widget: String) -> DeskAnchor {
        switch widget {
        case "message": return .tl
        case "companion": return .bc
        default: return .bl
        }
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
            let tilt = bits.count >= 4 ? Double(bits[3]).map(Self.clampTilt) ?? 0 : 0
            // The meter style is the fifth field; a word this build does not know, or a style on a
            // piece that has none, is a bar.
            let style = bits.count >= 5 ? DeskMeterStyles.parse(bits[4]).map { DeskMeterStyles.for(bits[0], $0) } ?? .bar : .bar
            pieces.removeAll { $0.widget == bits[0] }
            pieces.append(DeskPlacement(widget: bits[0], anchor: anchor, scale: scale, tilt: tilt, style: style))
        }
    }

    /// The layout string: a word per piece, in order.
    var string: String {
        pieces.map(Self.word).joined(separator: " ")
    }

    static func word(_ p: DeskPlacement) -> String {
        let head = "\(p.widget):\(p.anchor.rawValue)"
        let tilt = clampTilt(p.tilt)
        let style = DeskMeterStyles.for(p.widget, p.style)
        guard p.scale != 1 || tilt != 0 || style != .bar else { return head }
        var word = head + ":" + String(format: "%g", p.scale)
        // The fold rides after the size and the style after the fold, each only when not default.
        if tilt != 0 || style != .bar { word += ":" + String(format: "%.0f", tilt) }
        if style != .bar { word += ":" + DeskMeterStyles.word(style) }
        return word
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
        // Into a stack a piece takes its slant (one per stack); into an empty anchor it keeps its own.
        let moved = DeskPlacement(widget: widget, anchor: anchor, scale: old?.scale ?? 1,
                                  tilt: stackTilt(anchor) ?? old?.tilt ?? 0, style: old?.style ?? .bar)
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

    /// Takes `widget` off the Desk: the write Settings makes when a piece's place is set to
    /// Hidden (`place` at nil), so its word goes, size and all, and the rest keep theirs.
    mutating func hide(_ widget: String) {
        place(widget, at: nil)
    }

    /// Puts a hidden `widget` back at its standard anchor at size 1: the write Settings makes
    /// when a place is picked for a hidden piece (`place`, which also seats it by Settings'
    /// order). A placed piece stays where it is.
    mutating func show(_ widget: String) {
        guard placement(widget) == nil else { return }
        place(widget, at: Self.defaultAnchor(widget))
    }

    /// The known widgets not in the layout, in Settings' order (Arrange mode's Hidden strip).
    var hidden: [String] {
        Self.known.filter { placement($0) == nil }
    }

    /// Nothing is placed: Settings' Order list is empty and Arrange Desk… has nothing to drag.
    var placesNothing: Bool { pieces.isEmpty }

    /// Sets a placed piece's size (snapped and kept within range). A hidden piece has none.
    mutating func setScale(_ widget: String, _ scale: Double) {
        guard let i = pieces.firstIndex(where: { $0.widget == widget }) else { return }
        pieces[i].scale = Self.clampScale(scale)
    }

    /// Sets a placed piece's meter style; a piece that has none stays a bar, a hidden piece has none.
    mutating func setStyle(_ widget: String, _ style: DeskMeterStyle) {
        guard let i = pieces.firstIndex(where: { $0.widget == widget }) else { return }
        pieces[i].style = DeskMeterStyles.for(widget, style)
    }

    /// The slant of the stack at `anchor`: its first piece's; nil when the anchor has none.
    func stackTilt(_ anchor: DeskAnchor) -> Double? {
        pieces.first { $0.anchor == anchor }?.tilt
    }

    /// The slant of the stack `widget` sits in; nil when it is not placed. What the Desk draws by.
    func tilt(of widget: String) -> Double? {
        placement(widget).map { stackTilt($0.anchor) ?? $0.tilt }
    }

    /// The style `widget` draws in: its own, `.bar` for a piece without one or one not placed.
    func style(of widget: String) -> DeskMeterStyle {
        placement(widget).map { DeskMeterStyles.for(widget, $0.style) } ?? .bar
    }

    /// Slants the stack `widget` sits in: every piece at its anchor takes the tilt (whole degrees,
    /// kept within range), so a stack reads as one slanted face. A hidden piece changes nothing.
    mutating func setTilt(_ widget: String, _ tilt: Double) {
        guard let p = placement(widget) else { return }
        let t = Self.clampTilt(tilt)
        for i in pieces.indices where pieces[i].anchor == p.anchor { pieces[i].tilt = t }
    }

    /// Stacks whose pieces carry different tilts, in anchor order (what a layout string can say and
    /// the Desk resolves by the first piece's). A design or layout check refuses these.
    var tiltConflicts: [DeskTiltConflict] {
        Self.tiltConflicts(pieces.map { ($0.widget, $0.anchor, Optional($0.tilt)) })
    }

    /// The same check over words a design gives: pieces sent to one anchor with different tilts
    /// (a nil tilt says nothing and never conflicts), at most three, in the pieces' order.
    static func tiltConflicts(_ words: [(widget: String, anchor: DeskAnchor, tilt: Double?)]) -> [DeskTiltConflict] {
        var order: [DeskAnchor] = []
        var by: [DeskAnchor: [(String, Double)]] = [:]
        for w in words {
            guard let t = w.tilt else { continue }
            if by[w.anchor] == nil { order.append(w.anchor) }
            by[w.anchor, default: []].append((w.widget, t))
        }
        return order.compactMap { a -> DeskTiltConflict? in
            guard let g = by[a], Set(g.map(\.1)).count > 1 else { return nil }
            return DeskTiltConflict(anchor: a, pieces: g.map(\.0))
        }.prefix(3).map { $0 }
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
        if !sameAnchor { piece.tilt = pieces[to].tilt }
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

    /// Room kept between the center column and each side column, in points (the gap the sides
    /// keep between them without a center).
    static let columnGap: CGFloat = 24

    /// How wide each column may draw when the centers have pieces: the center column takes its
    /// own width (at most `colWidth`, the side cap), and each side gets what is left on its half,
    /// less `columnGap`, so a side's pieces wrap or shrink before they reach the center. The three
    /// never overlap horizontally: side + gap + center + gap + side <= total.
    static func columnWidths(total: CGFloat, colWidth: CGFloat, center: CGFloat) -> (side: CGFloat, center: CGFloat) {
        let mid = max(0, min(center, colWidth, total - 2 * columnGap))
        let side = max(0, min(colWidth, (total - mid) / 2 - columnGap))
        return (side, mid)
    }

    /// The point an anchor's stack hangs from: its outer corner or edge middle in `content`
    /// (top anchors hang down from the top, bottom ones rest on the bottom, middles are
    /// centered on the height), the top center moved down by `centerDrop`. A middle stack is
    /// drawn between its side's top and bottom stacks, so a tall corner pushes it off the exact
    /// middle rather than drawing over it; this is where it sits when its side has room.
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
