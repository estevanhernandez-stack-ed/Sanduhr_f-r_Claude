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

    /// What a key does in Arrange mode, wherever the key focus is: Return and keypad Enter end it
    /// as Done (keeping the edit), Escape as Cancel (dropping it); nil for any other key.
    static func endKey(_ keyCode: UInt16) -> DeskArrangeEnd? {
        switch keyCode {
        case 36, 76: return .done     // Return, keypad Enter
        case 53: return .cancel       // Escape
        default: return nil
        }
    }

    /// Where the bar's floating panel goes: its middle on the middle of `screen` (the visible
    /// frame of the screen the Desk is on, AppKit coordinates), on whole points.
    /// The bar panel's size: what its content fits, never smaller than the bar needs to show its
    /// words and buttons. 2.10.0 read the fitting size before the hosting view had laid out, got
    /// zero, and opened an invisible 0×0 panel.
    static let barMinimum = CGSize(width: 340, height: 170)
    static func barSize(fitting: CGSize?) -> CGSize {
        let f = fitting ?? .zero
        return CGSize(width: max(f.width, barMinimum.width), height: max(f.height, barMinimum.height))
    }

    static func barOrigin(size: CGSize, in screen: CGRect) -> CGPoint {
        CGPoint(x: (screen.midX - size.width / 2).rounded(), y: (screen.midY - size.height / 2).rounded())
    }

    /// state.yaml's `desk_arrange.click_through`: `whole` while the window takes clicks over its
    /// whole frame, `drawn` when only the drawn click areas take them.
    static func clickThrough(arranging: Bool, windowTakesMouse: Bool) -> String {
        arranging && windowTakesMouse ? "whole" : "drawn"
    }

    // MARK: Across displays (2.44)

    /// The window a point in the main window's top-left coordinates is over; nil off every window.
    static func window(at point: CGPoint, in windows: [DeskArrangeWindow]) -> DeskArrangeWindow? {
        windows.first { $0.rect.contains(point) }
    }

    /// Where a drop of `widget` at `point` (the window's own DeskArrange.space) lands on one window:
    /// the stack under the pointer (its own first, when it is drawn here), else the nearest anchor,
    /// in front of the first piece there whose middle is below the pointer. `frames` are that
    /// window's drawn pieces; `folds` its slanted stacks (the main window's only).
    static func landing(_ widget: String, in arrangement: DeskArrangement, at point: CGPoint,
                        geometry: DeskArrangeGeometry, frames: [String: CGRect],
                        folds: [DeskAnchor: DeskFold] = [:]) -> (anchor: DeskAnchor, before: String?) {
        let own = frames[widget] == nil ? nil : arrangement.placement(widget)?.anchor
        let anchor = target(point: point, content: geometry.content, centerDrop: geometry.centerDrop,
                            stacks: stackBounds(arrangement, frames: frames), own: own)
        let fold = folds[anchor].flatMap { DeskArrangement.clampTilt($0.tilt) != 0 ? $0 : nil }
        // The window's drawn stack at the anchor, in layout order, its frames back in the unslanted layout.
        let stack = arrangement.pieces.compactMap { p -> (widget: String, frame: CGRect)? in
            guard p.anchor == anchor, p.widget != widget, let f = frames[p.widget] else { return nil }
            return (p.widget, fold.map { $0.layoutFrame(fromBounds: f) } ?? f)
        }
        // A drop on a slanted stack is judged where the pointer is on the stack's own face.
        let face = folds[anchor].map { $0.unapply(point) } ?? point
        return (anchor, pieceAfter(y: face.y, in: stack))
    }

    /// The arrangement after a drop of `widget` dragged on window `source` and let go over window
    /// `target` (each named by the display This display writes for it), at `point` in the target's
    /// own coordinates. Within one window it is the drop from before 2.44: anchor and place only.
    /// On another window the piece moves to that window's display at the landing anchor.
    /// The All displays rule: a piece on `.all` keeps `.all` wherever it is dropped, within one
    /// window or onto another; only its anchor and place change, and they change on every display.
    /// A piece that stays on the main display (DeskWindowPlan.mainOnly) dropped elsewhere does not move.
    static func dropped(_ arrangement: DeskArrangement, widget: String, from source: DeskDisplay,
                        onto target: DeskDisplay, at point: CGPoint, geometry: DeskArrangeGeometry,
                        frames: [String: CGRect] = [:], folds: [DeskAnchor: DeskFold] = [:]) -> DeskArrangement {
        guard let piece = arrangement.placement(widget) else { return arrangement }
        let across = source != target
        if across && DeskWindowPlan.mainOnly.contains(widget) { return arrangement }
        let land = landing(widget, in: arrangement, at: point, geometry: geometry, frames: frames, folds: folds)
        let display = !across || piece.monitor == .all ? piece.monitor : target
        var out = arrangement
        out.put(widget, at: land.anchor, on: display, before: land.before)
        return out
    }

    /// Whether a piece's chrome offers Show on: with more than one Desk window, or with a piece
    /// already off the main display. A one-display Arrange looks exactly as before 2.44.
    static func offersShowOn(windows: Int, _ arrangement: DeskArrangement) -> Bool {
        windows > 1 || arrangement.pieces.contains { $0.monitor != .main }
    }

    /// Show on, for the piece's chrome on the window named `window`: All displays sets `.all`,
    /// This display sets the window's display (`.main` on the main window, its id on another).
    static func showOn(_ arrangement: DeskArrangement, widget: String, all: Bool, window: DeskDisplay) -> DeskArrangement {
        var out = arrangement
        out.setDisplay(widget, all ? .all : window)
        return out
    }

    /// The screen the floating bar goes on: the one under the pointer, else the one it is on.
    static func barScreen(pointer: CGPoint, screens: [CGRect], current: Int?) -> Int? {
        screens.firstIndex { $0.contains(pointer) } ?? current
    }
}

/// One Desk window as Arrange sees it (2.44): the display Show on's This display writes for it
/// (`.main` on the main window, its screen's id on another), its rectangle in the main window's
/// top-left coordinates (DeskWindowPlan.offset and its size) and where its anchors are.
struct DeskArrangeWindow: Equatable {
    var display: DeskDisplay
    var rect: CGRect
    var geometry: DeskArrangeGeometry
}

extension DeskArrangement {
    /// Puts a placed piece at `anchor`, in front of `before` in that anchor's stack (nil: at its
    /// bottom). Into an empty anchor it keeps its place in the string, as `place` does. The piece
    /// keeps its size and style, and joins the stack's slant. Nothing changes for a piece that is not placed, or when `before` is the
    /// piece itself. The piece keeps its display.
    mutating func put(_ widget: String, at anchor: DeskAnchor, before: String?) {
        guard let display = placement(widget)?.monitor else { return }
        put(widget, at: anchor, on: display, before: before)
    }

    /// `put` onto `display` as well: the piece joins that display's stack at `anchor` (a stack is a
    /// display and an anchor), taking its slant; into an empty one it keeps its own. `before` is
    /// found at `anchor` on any display, since a window draws several displays' pieces as one stack
    /// (All displays, a display that is not connected) in layout order.
    mutating func put(_ widget: String, at anchor: DeskAnchor, on display: DeskDisplay, before: String?) {
        guard widget != before, let from = pieces.firstIndex(where: { $0.widget == widget }) else { return }
        var piece = pieces[from]
        // Into another stack the piece takes its slant; into an empty one it keeps its own.
        if piece.anchor != anchor || piece.monitor != display,
           let joined = pieces.first(where: { $0.anchor == anchor && $0.monitor == display && $0.widget != widget })?.tilt {
            piece.tilt = joined
        }
        piece.anchor = anchor
        piece.monitor = display
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

/// How Arrange mode ends: Done writes the edit, Cancel drops it.
enum DeskArrangeEnd: Equatable {
    case done, cancel

    var keep: Bool { self == .done }
}

/// Arrange mode's words, in one place for the menus, the bar, Settings and the tests.
enum DeskArrangeCopy {
    static let menuItem = "Arrange Desk…"
    static let settingsButton = "Arrange Desk…"
    static let settingsNote = "Move, reorder and resize the pieces on the desktop itself. Return or Done keeps the new layout; Escape or Cancel puts it back as it was."
    static let barTitle = "Arrange Desk"
    static let barHint = "Drag a piece to any of the eight places, or up and down its stack. Drag its round handle to resize it."
    static let barKeys = "Return or Done keeps the new layout. Escape or Cancel puts it back."
    static let settingsArranging = "Arranging on the desktop. Finish there with Done or Cancel."
    static let settingsDeskOff = "Turn on Desk to arrange it on the desktop."
    static let settingsNothingPlaced = "Place a piece first."
    static let hide = "Hide"
    static let show = "Show"
    static let hiddenStrip = "Hidden:"
    static let showOn = "Show on"
    static let showOnThis = "This display"
    static let showOnAll = "All displays"
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
    /// The window the drag started on (2.44): its display key and its top-left corner in the main
    /// window's coordinates, which the location and the frame are relative to.
    var window: DeskDisplay = .main
    var origin: CGPoint = .zero

    /// The pointer in the main window's top-left coordinates.
    var global: CGPoint { CGPoint(x: origin.x + location.x, y: origin.y + location.y) }

    /// The ghost's middle in the main window's top-left coordinates.
    var ghostCenter: CGPoint {
        CGPoint(x: origin.x + frame.midX + translation.width, y: origin.y + frame.midY + translation.height)
    }
}

/// Arrange mode's state, on DeskModel. Begin reads the saved layout; every change edits the
/// working copy only, which DeskView draws; Done writes it to the layout key once, Cancel drops
/// it. The store is UserDefaults.desk in the app and an in-memory one in tests.
@Observable
final class DeskArrangeMode {
    static let layoutKey = "layout"

    private(set) var session: DeskArrangeSession?
    var drag: DeskArrangeDrag?
    /// Each window's drawn pieces' frames in its own DeskArrange.space, by the window's display key
    /// (`.main`, or another screen's id), reported while arranging (reorder drops). Kept per window
    /// because a piece on All displays is drawn, and reports, on every window.
    @ObservationIgnored var framesByWindow: [DeskDisplay: [String: CGRect]] = [:]
    /// The main window's frames.
    var frames: [String: CGRect] {
        get { framesByWindow[.main] ?? [:] }
        set { framesByWindow[.main] = newValue }
    }
    /// The Desk windows, by display key, as each DeskView reports itself (2.44).
    private(set) var windows: [DeskDisplay: DeskArrangeWindow] = [:]

    func setFrame(_ frame: CGRect?, of widget: String, in window: DeskDisplay) {
        if framesByWindow[window]?[widget] != frame { framesByWindow[window, default: [:]][widget] = frame }
    }

    func setWindow(_ window: DeskArrangeWindow) {
        if windows[window.display] != window { windows[window.display] = window }
    }

    func removeWindow(_ display: DeskDisplay) {
        if windows[display] != nil { windows[display] = nil }
        framesByWindow[display] = nil
    }

    /// Whether the pieces' chrome offers Show on (DeskArrange.offersShowOn): never on one display.
    var offersShowOn: Bool {
        working.map { DeskArrange.offersShowOn(windows: max(1, windows.count), $0) } ?? false
    }

    /// The window a point in the main window's coordinates is over, else `fallback`.
    func window(at global: CGPoint, fallback: DeskArrangeWindow) -> DeskArrangeWindow {
        DeskArrange.window(at: global, in: Array(windows.values)) ?? fallback
    }

    /// Where a drag at `location` on window `source` would land: the window under the pointer and
    /// the anchor there. What the lit anchors show and the drop does.
    func landing(of widget: String, at location: CGPoint, from source: DeskArrangeWindow) -> (window: DeskDisplay, anchor: DeskAnchor)? {
        guard let working else { return nil }
        let global = CGPoint(x: source.rect.minX + location.x, y: source.rect.minY + location.y)
        let target = window(at: global, fallback: source)
        let local = CGPoint(x: global.x - target.rect.minX, y: global.y - target.rect.minY)
        let land = DeskArrange.landing(widget, in: working, at: local, geometry: target.geometry,
                                       frames: framesByWindow[target.display] ?? [:],
                                       folds: target.display == .main ? folds : [:])
        return (target.display, land.anchor)
    }

    /// The drop of a drag let go at `location` on window `source` (DeskArrange.dropped): onto the
    /// window under the pointer, or within `source` when it is over none.
    func drop(_ widget: String, at location: CGPoint, from source: DeskArrangeWindow) {
        guard working != nil else { return }
        let global = CGPoint(x: source.rect.minX + location.x, y: source.rect.minY + location.y)
        let target = window(at: global, fallback: source)
        let local = CGPoint(x: global.x - target.rect.minX, y: global.y - target.rect.minY)
        let frames = framesByWindow[target.display] ?? [:]
        let folds = target.display == .main ? self.folds : [:]
        edit { a in
            a = DeskArrange.dropped(a, widget: widget, from: source.display, onto: target.display, at: local,
                                    geometry: target.geometry, frames: frames, folds: folds)
        }
    }

    /// Show on, from the piece's chrome on window `window` (DeskArrange.showOn).
    func showOn(_ widget: String, all: Bool, window: DeskDisplay) {
        edit { a in a = DeskArrange.showOn(a, widget: widget, all: all, window: window) }
    }
    /// Each slanted stack's tilt and top left in DeskArrange.space (the fold), by anchor; the
    /// frames above are the boxes around what is drawn. A stack that stands upright has none.
    @ObservationIgnored private(set) var folds: [DeskAnchor: DeskFold] = [:]

    func setFold(_ fold: DeskFold?, at anchor: DeskAnchor) {
        if folds[anchor] != fold { folds[anchor] = fold }
    }

    /// The fold of the stack `widget` sits in, nil when it stands upright or is not placed.
    func fold(of widget: String) -> DeskFold? {
        guard let anchor = working?.placement(widget)?.anchor, let fold = folds[anchor],
              DeskArrangement.clampTilt(fold.tilt) != 0 else { return nil }
        return fold
    }

    /// `widget`'s frame in the unslanted layout: SwiftUI reports the box around what is drawn
    /// (`frames`), so a slanted piece's is taken back along its stack's shear. nil with no frame.
    func layoutFrame(of widget: String) -> CGRect? {
        layoutFrame(of: widget, in: .main)
    }

    /// The same on any window: another window's stacks report no fold, so its frames are as drawn.
    func layoutFrame(of widget: String, in window: DeskDisplay) -> CGRect? {
        guard window == .main else { return framesByWindow[window]?[widget] }
        guard let f = frames[widget] else { return nil }
        return fold(of: widget).map { $0.layoutFrame(fromBounds: f) } ?? f
    }

    /// `widget`'s outline as drawn: its layout frame sheared with its stack (the quad Arrange Desk
    /// outlines and drags); the frame's rectangle when upright; nil with no frame.
    func quad(of widget: String) -> DeskQuad? {
        guard let f = layoutFrame(of: widget) else { return nil }
        return fold(of: widget).map { $0.quad(of: f) } ?? DeskQuad.of(f)
    }

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
        framesByWindow = [:]
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

    /// `desk-arrange hide`: the working layout's first placed piece goes, the edit its Hide
    /// button makes. Nothing placed, or not arranging: nothing changes.
    func smokeHide() {
        edit { a in
            guard let first = a.pieces.first else { return }
            a.hide(first.widget)
        }
    }

    /// `desk-arrange show`: the Hidden strip's first piece comes back, the edit its Show button
    /// makes. Nothing hidden, or not arranging: nothing changes.
    func smokeShow() {
        edit { a in
            guard let first = a.hidden.first else { return }
            a.show(first)
        }
    }
}
