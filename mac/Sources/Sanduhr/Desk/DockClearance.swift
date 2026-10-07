import Foundation
import CoreGraphics

/// Item 56: Desk items move clear of the Dock. The pure pieces: the Dock's preferences, the
/// inset an always-shown Dock reserves, which corners move for each side, the auto-hiding Dock's
/// extent from the window list, and the slide that follows it. DockFollower wires them to the
/// screen and the Desk's pointer watch.

/// The screen edge the Dock sits on (`com.apple.dock` `orientation`; no key means bottom).
enum DockSide: String, Equatable, CaseIterable {
    case bottom, left, right

    init(orientation: String?) {
        switch orientation {
        case "left": self = .left
        case "right": self = .right
        default: self = .bottom
        }
    }
}

/// The Dock's own settings, read only (`com.apple.dock`): never written.
struct DockPrefs: Equatable {
    static let domain = "com.apple.dock"
    /// When the tile size was never set: on the large side, so an estimate overshoots rather
    /// than leaves an item under the Dock.
    static let defaultTileSize: CGFloat = 64
    /// macOS's own pause before an auto-hidden Dock comes up, when `autohide-delay` is unset.
    static let defaultDelay: Double = 0.5
    /// How long the auto-hidden Dock takes to rise (and to drop) at the default animation speed,
    /// that is with `autohide-time-modifier` unset. Not documented by Apple: the commonly
    /// reported default, which `autohide-time-modifier` scales (1 is the default speed, 0 no
    /// animation).
    static let defaultSlide: TimeInterval = 0.5

    var side = DockSide.bottom
    var autohide = false
    var tilesize = defaultTileSize
    /// Seconds the pointer rests at the edge before the Dock comes up (`autohide-delay`).
    var delay = defaultDelay
    /// The Dock's animation time scale (`autohide-time-modifier`), nil when unset.
    var timeModifier: Double?

    /// How long the auto-hidden Dock takes to rise or drop: the Desk's slide matches it.
    var slideDuration: TimeInterval { Self.defaultSlide * (timeModifier ?? 1) }

    /// From a lookup over the Dock's domain (a dictionary in tests, the real domain in the app).
    static func read(_ value: (String) -> Any?) -> DockPrefs {
        var p = DockPrefs()
        p.side = DockSide(orientation: value("orientation") as? String)
        p.autohide = (value("autohide") as? NSNumber)?.boolValue ?? false
        if let size = (value("tilesize") as? NSNumber)?.doubleValue, size > 0 { p.tilesize = CGFloat(size) }
        if let delay = (value("autohide-delay") as? NSNumber)?.doubleValue, delay >= 0 { p.delay = min(delay, 5) }
        if let scale = (value("autohide-time-modifier") as? NSNumber)?.doubleValue, scale >= 0 {
            p.timeModifier = min(scale, 5)
        }
        return p
    }
}

/// How far the Dock reaches into the Desk's screen on each side it can sit on, in points.
struct DockInsets: Equatable {
    var left: CGFloat = 0
    var right: CGFloat = 0
    var bottom: CGFloat = 0

    static func on(_ side: DockSide, _ amount: CGFloat) -> DockInsets {
        var out = DockInsets()
        switch side {
        case .bottom: out.bottom = amount
        case .left: out.left = amount
        case .right: out.right = amount
        }
        return out
    }

    func amount(on side: DockSide) -> CGFloat {
        switch side {
        case .bottom: return bottom
        case .left: return left
        case .right: return right
        }
    }
}

enum DockGeometry {
    /// Depth of the auto-hiding Dock's trigger zone at its screen edge, in points: the Dock
    /// comes up when the pointer touches the edge, so a few points is plenty.
    static let triggerDepth: CGFloat = 6
    /// Depth of the strip at the very edge where the Dock reacts: a pointer resting here for the
    /// Dock's delay is when the Dock starts to rise, so the Desk starts with it.
    static let revealDepth: CGFloat = 2
    /// Extra room around a shown Dock's extent that still counts as over the Dock.
    static let shownSlack: CGFloat = 12
    /// A window-list extent larger than this share of the screen is not a strip (macOS 26's Dock
    /// window covers the whole screen); the thickness then comes from the estimate.
    static let stripShare: CGFloat = 0.4

    /// The insets the Dock reserves, from the screen's frame and visible frame (AppKit
    /// coordinates). The top is the menu bar and never counts.
    static func reserved(frame: CGRect, visible: CGRect) -> DockInsets {
        DockInsets(left: max(0, visible.minX - frame.minX),
                   right: max(0, frame.maxX - visible.maxX),
                   bottom: max(0, visible.minY - frame.minY))
    }

    /// The always-shown Dock's inset on the Desk's screen: the reservation on the Dock's side,
    /// nothing elsewhere. An auto-hiding Dock reserves nothing that counts (some macOS versions
    /// keep a few points), so it moves the Desk through the slide instead.
    static func alwaysShown(_ prefs: DockPrefs, frame: CGRect, visible: CGRect) -> DockInsets {
        guard !prefs.autohide else { return DockInsets() }
        return .on(prefs.side, reserved(frame: frame, visible: visible).amount(on: prefs.side))
    }

    /// The Desk anchors a Dock on `side` pushes: the three on that edge (item 59 added the
    /// centers and middles). A side Dock moves its whole column, every anchor sharing that margin.
    static func anchors(for side: DockSide) -> Set<DeskAnchor> {
        switch side {
        case .bottom: return [.bl, .bc, .br]
        case .left: return [.tl, .ml, .bl]
        case .right: return [.tr, .mr, .br]
        }
    }

    /// The Dock's thickness from its tile size, when the window list gives no strip: the tiles,
    /// the shelf around them and the gap to the edge, rounded up. `measured` is an always-shown
    /// Dock's reservation seen at the same tile size, which wins when there is one.
    static func estimatedThickness(tilesize: CGFloat, measured: CGFloat? = nil) -> CGFloat {
        if let measured, measured > 0 { return measured }
        return (tilesize + max(16, tilesize * 0.35) + 4).rounded(.up)
    }

    /// True when `point` (Desk window coordinates, top-left origin) is in the auto-hiding Dock's
    /// trigger zone: the edge on its side, a few points deep, or over the Dock while it shows.
    static func inTriggerZone(_ point: CGPoint, size: CGSize, side: DockSide, shownExtent: CGFloat) -> Bool {
        let depth = shownExtent > 0 ? max(triggerDepth, shownExtent + shownSlack) : triggerDepth
        return within(depth, of: side, point, size: size)
    }

    /// True when `point` (Desk window coordinates) is at the very edge on the Dock's side, where
    /// resting for the Dock's delay brings it up.
    static func atRevealEdge(_ point: CGPoint, size: CGSize, side: DockSide) -> Bool {
        within(revealDepth, of: side, point, size: size)
    }

    private static func within(_ depth: CGFloat, of side: DockSide, _ point: CGPoint, size: CGSize) -> Bool {
        guard point.x >= -1, point.y >= -1, point.x <= size.width + 1, point.y <= size.height + 1 else { return false }
        switch side {
        case .bottom: return point.y >= size.height - depth
        case .left: return point.x <= depth
        case .right: return point.x >= size.width - depth
        }
    }

    /// One Dock-owned window from `CGWindowListCopyWindowInfo`: layer, bounds (global
    /// CoreGraphics coordinates, top-left origin) and whether it is on screen. Metadata only.
    struct DockWindow: Equatable {
        var layer: Int
        var bounds: CGRect
        var onScreen: Bool
    }

    /// The Dock's extent into `screen` (CoreGraphics coordinates) on `side`, or nil when no
    /// Dock window at `layer` is on screen there. A strip-shaped window gives its own depth;
    /// a window covering the screen (the Dock draws in one full-screen window on macOS 26)
    /// says only that the Dock shows, and `fallback` gives the depth.
    static func shownExtent(_ windows: [DockWindow], layer: Int, screen: CGRect, side: DockSide,
                            fallback: CGFloat) -> CGFloat? {
        let mine = windows.filter { $0.layer == layer && $0.onScreen && !$0.bounds.intersection(screen).isNull }
        guard !mine.isEmpty else { return nil }
        var deepest: CGFloat = 0
        for w in mine {
            let b = w.bounds.intersection(screen)
            let span: CGFloat
            let limit: CGFloat
            switch side {
            case .bottom: span = screen.maxY - b.minY; limit = screen.height
            case .left: span = b.maxX - screen.minX; limit = screen.width
            case .right: span = screen.maxX - b.minX; limit = screen.width
            }
            if span > limit * stripShare { return fallback }
            deepest = max(deepest, span)
        }
        return deepest > 0 ? deepest : fallback
    }
}

/// The auto-hiding Dock's slide: Desk items rest at the edge while the Dock is hidden, slide
/// clear when it comes up and settle back when it goes. The slide starts ahead of the window list
/// when the pointer has rested at the edge for the Dock's delay (`anticipate`), the moment the
/// Dock itself starts to rise; a reading that sees the Dock confirms it, and one that still does
/// not after `confirmWithin` takes it back. The pointer leaving the Dock's zone, or a reading
/// that no longer sees the Dock, slides back. `settle()` (after the Dock's slide duration, or at
/// once with Reduce Motion) ends the slide.
struct DockSlide: Equatable {
    enum Phase: String { case hidden, showing, shown, hiding }

    /// How long an anticipated slide waits for the window list to see the Dock before it slides
    /// back (the pointer left early, a full-screen app, the Dock suppressed).
    static let confirmWithin: TimeInterval = 0.6

    private(set) var phase = Phase.hidden
    /// The Dock's extent at the last reading that saw it, or the estimate an anticipated slide
    /// used.
    private(set) var extent: CGFloat = 0
    /// When the slide started ahead of the window list, until a reading sees the Dock.
    private(set) var anticipatedAt: Date?

    /// The inset the Desk lays out against: the extent while the Dock shows or comes up, zero
    /// while it goes or is gone.
    var inset: CGFloat { phase == .showing || phase == .shown ? extent : 0 }

    /// True while the slide started ahead of the Dock and no reading has seen it yet.
    var unconfirmed: Bool { anticipatedAt != nil }

    /// The pointer rested at the edge for the Dock's delay: slide clear by `estimate` now,
    /// before the window list sees the Dock. True when the inset changed.
    @discardableResult
    mutating func anticipate(_ estimate: CGFloat, now: Date) -> Bool {
        guard estimate > 0, phase == .hidden || phase == .hiding else { return false }
        let before = inset
        extent = estimate
        phase = .showing
        anticipatedAt = now
        return inset != before
    }

    /// A window-list reading: the Dock's extent here, or nil when it is not showing. A reading
    /// starts a slide only when `canShow` (the pointer is in the Dock's zone); one that does not
    /// see the Dock leaves an anticipated slide alone until `confirmWithin` has passed. True
    /// when the inset changed, so the Desk slides.
    @discardableResult
    mutating func observe(_ seen: CGFloat?, canShow: Bool = true, now: Date = Date()) -> Bool {
        let before = inset
        if let seen, seen > 0 {
            if phase == .hidden || phase == .hiding {
                guard canShow else { return false }
                phase = .showing
            }
            extent = seen
            anticipatedAt = nil
        } else if phase == .showing || phase == .shown {
            if let t = anticipatedAt, now.timeIntervalSince(t) < Self.confirmWithin { return false }
            phase = .hiding
            anticipatedAt = nil
        }
        return inset != before
    }

    /// The pointer left the Dock's zone: the Dock drops, so the Desk slides back with it. True
    /// when the inset changed.
    @discardableResult
    mutating func pointerLeft() -> Bool {
        guard phase == .showing || phase == .shown else { return false }
        phase = .hiding
        anticipatedAt = nil
        return true
    }

    /// The slide finished: showing becomes shown, hiding becomes hidden.
    mutating func settle() {
        switch phase {
        case .showing: phase = .shown
        case .hiding: phase = .hidden
        case .hidden, .shown: break
        }
    }

    /// The Dock stopped hiding (or Desk stopped): back to rest, nothing applied.
    mutating func reset() { self = DockSlide() }
}

/// The pointer's rest at the Dock's edge: when it arrived, and whether this visit already
/// started a slide, so a slide taken back is not tried again until the pointer leaves the edge.
struct DockDwell: Equatable {
    private(set) var since: Date?
    private(set) var spent = false

    /// One tick: the pointer at the edge or not. Leaving the edge starts over.
    mutating func update(atEdge: Bool, now: Date) {
        if atEdge {
            if since == nil { since = now }
        } else {
            self = DockDwell()
        }
    }

    /// True once the pointer has rested at the edge for `delay` this visit (at once for 0).
    func due(delay: Double, now: Date) -> Bool {
        guard let since, !spent else { return false }
        return now.timeIntervalSince(since) >= delay
    }

    /// This visit started its slide.
    mutating func spend() { spent = true }
}

/// When the Desk's pointer watch reads the window list for the Dock (item 56). Nothing runs
/// while the pointer is away from the Dock's edge and the Dock is hidden.
enum DockWatch {
    /// Outside the trigger zone with the Dock not yet hidden, every this many ticks.
    static let awayEvery = 4
    /// After the last movement or slide, how long a resting pointer in the zone keeps reading:
    /// the Dock's own delay plus room for its slide.
    static func quietAfter(delay: Double) -> TimeInterval { delay + 1 }

    static func shouldRead(inZone: Bool, phase: DockSlide.Phase, pointerMoved: Bool,
                           sinceActivity: TimeInterval, delay: Double, tick: Int) -> Bool {
        if inZone { return pointerMoved || sinceActivity < quietAfter(delay: delay) }
        if phase != .hidden { return tick % awayEvery == 0 }
        return false
    }
}
