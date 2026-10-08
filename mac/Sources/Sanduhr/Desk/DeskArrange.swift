import Foundation
import CoreGraphics
import Observation
import SwiftUI

/// Arrange mode (item 60): the Desk's pieces moved, reordered and resized on the desktop itself.
/// The pure answers live here (which anchor a drop lands on, where in a stack, the size a handle
/// drag snaps to, when the window takes the mouse); DeskArrangeMode holds the edit, and
/// DeskArrangeViews draws the outlines, the handles, the lit anchors and the Done bar.
enum DeskArrange {
    /// The coordinate space the gestures, the reported piece frames and the anchors share: the
    /// Desk window's root view, top-left origin.
    static let space = "deskArrange"

    /// The anchor a drop at `point` lands on: the nearest of the eight anchor points, measured as
    /// a share of the content's width and height, so the zones are the same on any screen shape.
    /// A tie goes to the first anchor in DeskAnchor's order.
    static func nearest(to point: CGPoint, in content: CGRect, centerDrop: CGFloat = 0) -> DeskAnchor {
        let w = max(1, content.width)
        let h = max(1, content.height)
        var best = DeskAnchor.tl
        var bestDistance = CGFloat.infinity
        for anchor in DeskAnchor.allCases {
            let p = DeskAnchorGeometry.point(anchor, in: content, centerDrop: centerDrop)
            let dx = (point.x - p.x) / w
            let dy = (point.y - p.y) / h
            let d = dx * dx + dy * dy
            if d < bestDistance {
                best = anchor
                bestDistance = d
            }
        }
        return best
    }

    /// How far around a stack's pieces a drop still counts as on that stack, in points: the
    /// outline's pad and the name label that sits above it.
    static let stackReach: CGFloat = 26

    /// Each anchor's stack as one rectangle: the union of its drawn pieces' frames (the dragged
    /// piece's own included, it stays drawn while dragged), grown by `reach`. Pieces with no
    /// frame are skipped; an anchor with none drawn has no rectangle.
    static func stackBounds(_ arrangement: DeskArrangement, frames: [String: CGRect],
                            reach: CGFloat = stackReach) -> [DeskAnchor: CGRect] {
        var out: [DeskAnchor: CGRect] = [:]
        for piece in arrangement.pieces {
            guard let f = frames[piece.widget], !f.isEmpty else { continue }
            out[piece.anchor] = out[piece.anchor].map { $0.union(f) } ?? f
        }
        return out.mapValues { $0.insetBy(dx: -reach, dy: -reach) }
    }

    /// The anchor a drop at `point` lands on. Over a stack (`stackBounds`) it is that stack's
    /// anchor, the dragged piece's own stack (`own`) first, so a drop anywhere on a tall stack
    /// reorders within it even where its upper pieces reach past the halfway line to the next
    /// anchor. Elsewhere it is the nearest anchor point.
    static func target(point: CGPoint, content: CGRect, centerDrop: CGFloat = 0,
                       stacks: [DeskAnchor: CGRect], own: DeskAnchor?) -> DeskAnchor {
        if let own, stacks[own]?.contains(point) == true { return own }
        if let over = DeskAnchor.allCases.first(where: { stacks[$0]?.contains(point) == true }) {
            return over
        }
        return nearest(to: point, in: content, centerDrop: centerDrop)
    }

    /// The piece a drop at height `y` goes in front of, in a stack listed top to bottom (the
    /// dragged piece left out): the first drawn piece whose middle is at or below `y`. nil puts
    /// it at the bottom of the stack. Pieces with no frame (not drawn) are skipped.
    static func pieceAfter(y: CGFloat, in stack: [(widget: String, frame: CGRect)]) -> String? {
        stack.first { !$0.frame.isEmpty && $0.frame.midY >= y }?.widget
    }

    /// Which corner of a piece carries its resize handle: the one facing the middle of the
    /// screen, so the handle never sits against an edge, the menu bar or the Dock.
    static func handle(_ anchor: DeskAnchor) -> (trailing: Bool, bottom: Bool) {
        (anchor.column != .right, anchor.row != .bottom)
    }

    /// The size a handle drag asks for: the start size grown by the drag along the handle's
    /// outward direction, as a share of the piece's width plus height (a center piece grows both
    /// ways across, so its sideways drag counts twice), then snapped to the size steps and kept
    /// within 60% to 160% (DeskArrangement.clampScale).
    static func scale(from start: Double, size: CGSize, drag: CGSize, anchor: DeskAnchor) -> Double {
        let span = size.width + size.height
        guard span > 0 else { return DeskArrangement.clampScale(start) }
        let corner = handle(anchor)
        let across = (corner.trailing ? drag.width : -drag.width) * (anchor.column == .center ? 2 : 1)
        let down = corner.bottom ? drag.height : -drag.height
        return DeskArrangement.clampScale(start * (1 + Double((across + down) / span)))
    }

    /// The snap after a drop: a short spring, none with Reduce Motion (the piece is simply there).
    static func snapAnimation(reduceMotion: Bool) -> Animation? {
        reduceMotion ? nil : .spring(duration: 0.28, bounce: 0.15)
    }

    /// Whether the Desk window takes the mouse at the pointer: over its whole frame while
    /// arranging, else only over a click area (DeskHitTest), exactly as before.
    static func takesMouse(arranging: Bool, overElement: Bool) -> Bool {
        arranging || overElement
    }

    /// What a key does in Arrange mode: Escape and Return end it keeping the changes (as Done);
    /// nil for any other key.
    static func endKey(_ keyCode: UInt16) -> Bool? {
        switch keyCode {
        case 53, 36, 76: return true   // Escape, Return, keypad Enter
        default: return nil
        }
    }

    /// state.yaml's `desk_arrange.click_through`: `whole` while the window takes clicks over its
    /// whole frame, `drawn` when only the drawn click areas take them.
    static func clickThrough(arranging: Bool, windowTakesMouse: Bool) -> String {
        arranging && windowTakesMouse ? "whole" : "drawn"
    }
}

extension DeskArrangement {
    /// Puts a placed piece at `anchor`, in front of `before` in that anchor's stack (nil: at its
    /// bottom). Into an empty anchor it keeps its place in the string, as `place` does. The piece
    /// keeps its size. Nothing changes for a piece that is not placed, or when `before` is the
    /// piece itself.
    mutating func put(_ widget: String, at anchor: DeskAnchor, before: String?) {
        guard widget != before, let from = pieces.firstIndex(where: { $0.widget == widget }) else { return }
        var piece = pieces[from]
        piece.anchor = anchor
        pieces.remove(at: from)
        if let before, let i = pieces.firstIndex(where: { $0.widget == before && $0.anchor == anchor }) {
            pieces.insert(piece, at: i)
        } else if let last = pieces.lastIndex(where: { $0.anchor == anchor }) {
            pieces.insert(piece, at: last + 1)
        } else {
            pieces.insert(piece, at: min(from, pieces.endIndex))
        }
    }
}

/// Arrange mode's words, in one place for the menus, Settings and the tests.
enum DeskArrangeCopy {
    static let menuItem = "Arrange Desk…"
    static let settingsButton = "Arrange Desk…"
    static let settingsNote = "Move, reorder and resize the pieces on the desktop itself. Escape or Done keeps the new layout; Cancel puts it back as it was."
    static let settingsArranging = "Arranging on the desktop. Finish there with Done or Cancel."
    static let settingsDeskOff = "Turn on Desk to arrange it on the desktop."
}

/// One Arrange mode edit: the layout string as it was saved, kept verbatim so Cancel leaves it
/// exactly so, and the arrangement being changed. Done writes the working layout, once, and only
/// when it differs from the saved one.
struct DeskArrangeSession: Equatable {
    let saved: String
    var working: DeskArrangement

    init(saved: String) {
        self.saved = saved
        working = DeskArrangement(saved)
    }

    var changed: Bool { working != DeskArrangement(saved) }

    /// The string Done writes, nil when nothing changed (an older string stays as it was).
    var toWrite: String? { changed ? working.string : nil }
}

/// The piece being dragged: where the pointer is and how far it moved, and the piece's frame
/// when the drag started (the ghost's size).
struct DeskArrangeDrag: Equatable {
    var widget: String
    var location: CGPoint
    var translation: CGSize
    var frame: CGRect
}

/// Arrange mode's state, on DeskModel. Begin reads the saved layout; every change edits the
/// working copy only, which DeskView draws; Done writes it to the layout key once, Cancel drops
/// it. The store is UserDefaults.desk in the app and an in-memory one in tests.
@Observable
final class DeskArrangeMode {
    static let layoutKey = "layout"

    private(set) var session: DeskArrangeSession?
    var drag: DeskArrangeDrag?
    /// Each drawn piece's frame in DeskArrange.space, reported while arranging (reorder drops).
    @ObservationIgnored var frames: [String: CGRect] = [:]
    @ObservationIgnored private let store: DefaultsStore

    init(store: DefaultsStore = UserDefaults.desk) {
        self.store = store
    }

    var active: Bool { session != nil }
    var working: DeskArrangement? { session?.working }

    /// Starts an edit of the saved layout (the standard one when none is saved). Already
    /// arranging: nothing changes.
    func begin() {
        guard session == nil else { return }
        let saved = store.object(forKey: Self.layoutKey) as? String ?? DeskLayout.standard
        session = DeskArrangeSession(saved: saved)
        drag = nil
        frames = [:]
    }

    /// Changes the working layout; nothing is saved.
    func edit(_ change: (inout DeskArrangement) -> Void) {
        guard var s = session else { return }
        change(&s.working)
        if s != session { session = s }
    }

    /// Ends the edit keeping it: writes the layout once when it changed. True when it wrote.
    @discardableResult
    func done() -> Bool {
        guard let s = session else { return false }
        session = nil
        drag = nil
        guard let layout = s.toWrite else { return false }
        store.set(layout, forKey: Self.layoutKey)
        return true
    }

    /// Ends the edit dropping it: the saved layout was never touched.
    func cancel() {
        session = nil
        drag = nil
    }

    /// The smoke's own edit (`desk-arrange test`): the clock to Top right at 120%, when placed.
    func smokeEdit() {
        edit { a in
            guard a.placement("clock") != nil else { return }
            a.put("clock", at: .tr, before: nil)
            a.setScale("clock", 1.2)
        }
    }
}
