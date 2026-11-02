import CoreGraphics
import Foundation

/// Where drawer panels sit (docs/mac-drawers-setup-spec.md, section 7). Pure: values in, values
/// out, no AppKit window and no file system, so the maths is tested without a screen. Every rect
/// here is in "work space": the origin is the top left of a screen's visible frame and y grows
/// downward, the same space a saved `DrawerPlace` offset uses. The window code converts to and
/// from AppKit's bottom-left screen coordinates with `frame(...)` and `offset(...)`.
enum DrawerPlacement {
    static let width: CGFloat = 360
    static let openHeight: CGFloat = 330
    static let foldedHeight: CGFloat = 42
    static let margin: CGFloat = 24
    static let gap: CGFloat = 12

    static func height(folded: Bool) -> CGFloat { folded ? foldedHeight : openHeight }
    static func size(folded: Bool) -> CGSize { CGSize(width: width, height: height(folded: folded)) }

    /// The key a saved place carries for a display. Nil (no display number) reads as "main".
    static func screenKey(displayID: UInt32?) -> String { displayID.map { "display-\($0)" } ?? "main" }

    /// A global AppKit frame as an offset from `visible`'s top left.
    static func offset(ofFrame frame: CGRect, in visible: CGRect) -> CGPoint {
        CGPoint(x: frame.minX - visible.minX, y: visible.maxY - frame.maxY)
    }

    /// The AppKit frame for an offset on `visible`, kept on the screen (`clamp`).
    static func frame(offset: CGPoint, size: CGSize, in visible: CGRect) -> CGRect {
        let o = clamp(offset, size: size, work: visible.size)
        return CGRect(x: visible.minX + o.x, y: visible.maxY - o.y - size.height, width: size.width, height: size.height)
    }

    /// A work-space rect as an AppKit frame, and back.
    static func globalFrame(_ r: CGRect, in visible: CGRect) -> CGRect {
        CGRect(x: visible.minX + r.minX, y: visible.maxY - r.minY - r.height, width: r.width, height: r.height)
    }

    static func workRect(ofFrame frame: CGRect, in visible: CGRect) -> CGRect {
        CGRect(origin: offset(ofFrame: frame, in: visible), size: frame.size)
    }

    /// A Desk piece's frame (top-left origin on the screen's full frame) as a work-space rect.
    static func workRect(ofDesk rect: CGRect, screenFrame: CGRect, visible: CGRect) -> CGRect {
        let dx = visible.minX - screenFrame.minX
        let dy = screenFrame.maxY - visible.maxY
        return rect.offsetBy(dx: -dx, dy: -dy)
    }

    /// Keeps a drawer on its screen: the whole width, and at least the title bar's height, inside
    /// the work area, so the title bar can always be grabbed again.
    static func clamp(_ p: CGPoint, size: CGSize, work: CGSize) -> CGPoint {
        CGPoint(x: min(max(p.x, 0), max(0, work.width - size.width)),
                y: min(max(p.y, 0), max(0, work.height - foldedHeight)))
    }

    /// Strict overlap: rects that only share an edge do not overlap.
    static func overlaps(_ a: CGRect, _ b: CGRect) -> Bool {
        a.minX < b.maxX && b.minX < a.maxX && a.minY < b.maxY && b.minY < a.maxY
    }

    /// Places for the drawers that have none: each takes the next free Tidy slot for the corner and
    /// direction, skipping the drawers already placed. A drawer with no room anywhere arrives near
    /// the top right, each one `margin` further along so they don't hide each other.
    static func arrive(_ fresh: [(id: String, folded: Bool)], placed: [CGRect], work: CGRect,
                       corner: DrawerCorner, flow: DrawerFlow) -> [String: CGPoint] {
        let ordered = fresh.map { (id: $0.id, h: height(folded: $0.folded)) }
        let slots = DrawerTidyPlan.slots(ordered, work: work, corner: corner, flow: flow, blocked: placed)
        var out: [String: CGPoint] = [:]
        var unfit = 0
        for (id, h) in ordered {
            if let r = slots[id] { out[id] = r.origin; continue }
            let step = margin * CGFloat(unfit)
            unfit += 1
            let p = CGPoint(x: max(0, work.maxX - margin - width) + step, y: margin + step)
            out[id] = clamp(p, size: CGSize(width: width, height: h), work: work.size)
        }
        return out
    }
}

/// How drawers a plan creates arrive: the first two open and the rest folded, so a six-drawer
/// plan fits a 1080-point screen.
enum DrawerArrival {
    static let openOnArrival = 2
    static func folded(created ids: [String]) -> [String] { Array(ids.dropFirst(openOnArrival)) }
}

/// Where a dragged drawer settles: each axis on its own, the first rule with a candidate within
/// `threshold` wins (other drawers, then Desk pieces, then the screen's margin), nearest within
/// it. Option-drag turns snapping off and only keeps the title bar on the screen.
enum DrawerSnap {
    static let threshold: CGFloat = 12

    static func snap(_ moving: CGRect, work: CGRect, drawers: [CGRect], pieces: [CGRect], snapping: Bool = true) -> CGRect {
        guard snapping else { return keep(moving, work: work) }
        let x = axis(moving.minX, moving.width, work.minX, work.maxX,
                     groups: [drawers.map { ($0.minX, $0.maxX) }, pieces.map { ($0.minX, $0.maxX) }])
        let y = axis(moving.minY, moving.height, work.minY, work.maxY,
                     groups: [drawers.map { ($0.minY, $0.maxY) }, pieces.map { ($0.minY, $0.maxY) }])
        return keep(CGRect(x: x, y: y, width: moving.width, height: moving.height), work: work)
    }

    /// The title bar always stays on the work area.
    static func keep(_ r: CGRect, work: CGRect) -> CGRect {
        CGRect(x: min(max(r.minX, work.minX), max(work.minX, work.maxX - r.width)),
               y: min(max(r.minY, work.minY), max(work.minY, work.maxY - DrawerPlacement.foldedHeight)),
               width: r.width, height: r.height)
    }

    private static func axis(_ start: CGFloat, _ size: CGFloat, _ lo: CGFloat, _ hi: CGFloat,
                             groups: [[(CGFloat, CGFloat)]]) -> CGFloat {
        for group in groups {
            if let hit = nearest(start, candidates(size, group)) { return hit }
        }
        if let edge = nearest(start, [lo + DrawerPlacement.margin, hi - DrawerPlacement.margin - size]) { return edge }
        return start
    }

    private static func candidates(_ size: CGFloat, _ others: [(CGFloat, CGFloat)]) -> [CGFloat] {
        others.flatMap { s, e in
            [s, e - size, e + DrawerPlacement.gap, s - DrawerPlacement.gap - size]
        }
    }

    private static func nearest(_ at: CGFloat, _ candidates: [CGFloat]) -> CGFloat? {
        var best: CGFloat?
        for c in candidates where abs(c - at) <= threshold {
            if best == nil || abs(c - at) < abs(best! - at) { best = c }
        }
        return best
    }
}

/// Tidy drawers: slots from a corner, a column or a row at a time, wrapping toward the centre. A
/// drawer that fits nowhere is left out and keeps its place.
enum DrawerTidyPlan {
    /// The ids ordered by how near each drawer's corner-side corner is to the work area's corner; ties by id.
    static func order(_ current: [String: CGRect], work: CGRect, corner: DrawerCorner) -> [String] {
        let c = point(work, corner)
        func dist(_ r: CGRect) -> CGFloat {
            let n = near(r, corner)
            return abs(n.x - c.x) + abs(n.y - c.y)
        }
        return current.sorted { a, b in
            let da = dist(a.value), db = dist(b.value)
            return da != db ? da < db : a.key < b.key
        }.map(\.key)
    }

    /// A slot for each drawer in `ordered` (width 360, the given height), from `corner` at the Desk
    /// margin and the drawer gap, never overlapping `blocked` or each other. A drawer with no room
    /// anywhere is absent from the result.
    static func slots(_ ordered: [(id: String, h: CGFloat)], work: CGRect, corner: DrawerCorner,
                      flow: DrawerFlow, blocked: [CGRect]) -> [String: CGRect] {
        let left = corner == .topLeft || corner == .bottomLeft
        let top = corner == .topLeft || corner == .topRight
        let column = flow == .column
        let m = DrawerPlacement.margin, g = DrawerPlacement.gap, w = DrawerPlacement.width
        let eps: CGFloat = 0.5
        let minX = work.minX + m - eps, maxX = work.maxX - m + eps
        let minY = work.minY + m - eps, maxY = work.maxY - m + eps

        var lineX = left ? work.minX + m : work.maxX - m - w
        var lineY = top ? work.minY + m : work.maxY - m
        let rowStart = left ? work.minX + m : work.maxX - m
        var cursor = column ? lineY : rowStart
        var lane: CGFloat = 0
        var edge: CGFloat? = nil
        var placed = blocked
        var slots: [String: CGRect] = [:]

        for (id, h) in ordered {
            // Each pass steps past one obstacle, wraps one line or places the drawer. The bound is a
            // backstop that leaves the drawer out rather than ever spinning.
            let lineSpan = column ? work.width / (w + g) : work.height / g
            let saved = (lineX, lineY, cursor, lane, edge)
            var guardCount = (placed.count + 2) * (Int(min(lineSpan, 100_000)) + 2)
            while guardCount > 0 {
                guardCount -= 1
                let r: CGRect = column
                    ? CGRect(x: lineX, y: top ? cursor : cursor - h, width: w, height: h)
                    : CGRect(x: left ? cursor : cursor - w, y: top ? lineY : lineY - h, width: w, height: h)
                let inside = r.minX >= minX && r.maxX <= maxX && r.minY >= minY && r.maxY <= maxY
                if !inside {
                    if column {
                        lineX += left ? w + g : -(w + g)
                        cursor = lineY
                        if lineX < minX || lineX + w > maxX { break }
                    } else {
                        if lane > 0 { lineY += top ? lane + g : -(lane + g) }
                        else if let e = edge { lineY = top ? e + g : e - g }
                        else { lineY += top ? h + g : -(h + g) }
                        cursor = rowStart
                        lane = 0
                        edge = nil
                        if lineY < minY || lineY > maxY { break }
                    }
                    continue
                }
                if let o = placed.first(where: { DrawerPlacement.overlaps(r, $0) }) {
                    cursor = column ? (top ? o.maxY + g : o.minY - g) : (left ? o.maxX + g : o.minX - g)
                    if !column {
                        let bound = top ? o.maxY : o.minY
                        edge = edge.map { top ? min($0, bound) : max($0, bound) } ?? bound
                    }
                    continue
                }
                slots[id] = r
                placed.append(r)
                if column { cursor = top ? r.maxY + g : r.minY - g }
                else { cursor = left ? r.maxX + g : r.minX - g; lane = max(lane, h) }
                break
            }
            // Left out: the next drawer starts where this one did.
            if slots[id] == nil { (lineX, lineY, cursor, lane, edge) = saved }
        }
        return slots
    }

    /// What Tidy would do: the new origin of each drawer that moves, the origins it had, and how many move.
    struct Change: Equatable {
        var previous: [String: CGPoint]
        var next: [String: CGPoint]
        var count: Int { next.count }
        var isEmpty: Bool { next.isEmpty }
    }

    /// Lines the drawers up. `current` holds each shown drawer's rect (its real height). A drawer
    /// already on its slot is not in the change; one with no room keeps its place.
    static func tidy(_ current: [String: CGRect], work: CGRect, corner: DrawerCorner, flow: DrawerFlow) -> Change {
        let ids = order(current, work: work, corner: corner)
        let slots = slots(ids.map { (id: $0, h: current[$0]!.height) }, work: work, corner: corner, flow: flow, blocked: [])
        var change = Change(previous: [:], next: [:])
        for id in ids {
            guard let slot = slots[id], let cur = current[id] else { continue }
            if abs(slot.minX - cur.minX) < 0.5 && abs(slot.minY - cur.minY) < 0.5 { continue }
            change.previous[id] = cur.origin
            change.next[id] = slot.origin
        }
        return change
    }

    private static func point(_ w: CGRect, _ c: DrawerCorner) -> CGPoint {
        switch c {
        case .topLeft: return CGPoint(x: w.minX, y: w.minY)
        case .topRight: return CGPoint(x: w.maxX, y: w.minY)
        case .bottomLeft: return CGPoint(x: w.minX, y: w.maxY)
        case .bottomRight: return CGPoint(x: w.maxX, y: w.maxY)
        }
    }

    private static func near(_ r: CGRect, _ c: DrawerCorner) -> CGPoint {
        switch c {
        case .topLeft: return CGPoint(x: r.minX, y: r.minY)
        case .topRight: return CGPoint(x: r.maxX, y: r.minY)
        case .bottomLeft: return CGPoint(x: r.minX, y: r.maxY)
        case .bottomRight: return CGPoint(x: r.maxX, y: r.maxY)
        }
    }
}
