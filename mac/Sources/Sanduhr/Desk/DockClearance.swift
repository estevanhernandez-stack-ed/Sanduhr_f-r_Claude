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

    var side = DockSide.bottom
    var autohide = false
    var tilesize = defaultTileSize
    /// Seconds the pointer rests at the edge before the Dock comes up (`autohide-delay`).
    var delay = defaultDelay

    /// From a lookup over the Dock's domain (a dictionary in tests, the real domain in the app).
    static func read(_ value: (String) -> Any?) -> DockPrefs {
        var p = DockPrefs()
        p.side = DockSide(orientation: value("orientation") as? String)
        p.autohide = (value("autohide") as? NSNumber)?.boolValue ?? false
        if let size = (value("tilesize") as? NSNumber)?.doubleValue, size > 0 { p.tilesize = CGFloat(size) }
        if let delay = (value("autohide-delay") as? NSNumber)?.doubleValue, delay >= 0 { p.delay = min(delay, 5) }
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

    /// The Desk corners a Dock on `side` pushes: the two on that edge. A side Dock moves its
    /// whole column, the two corners sharing that margin.
    static func corners(for side: DockSide) -> Set<DeskView.Slot> {
        switch side {
        case .bottom: return [.bl, .br]
        case .left: return [.tl, .bl]
        case .right: return [.tr, .br]
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
        guard point.x >= -1, point.y >= -1, point.x <= size.width + 1, point.y <= size.height + 1 else { return false }
        let depth = shownExtent > 0 ? max(triggerDepth, shownExtent + shownSlack) : triggerDepth
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
/// clear when it comes up and settle back when it goes. Readings move it to showing or hiding;
/// `settle()` (after `duration`, or at once with Reduce Motion) ends the slide.
struct DockSlide: Equatable {
    enum Phase: String { case hidden, showing, shown, hiding }

    /// The Dock's own slide is about this long; the Desk's matches it.
    static let duration: TimeInterval = 0.25

    private(set) var phase = Phase.hidden
    /// The Dock's extent at the last reading that saw it.
    private(set) var extent: CGFloat = 0

    /// The inset the Desk lays out against: the extent while the Dock shows or comes up, zero
    /// while it goes or is gone.
    var inset: CGFloat { phase == .showing || phase == .shown ? extent : 0 }

    /// A window-list reading: the Dock's extent here, or nil when it is not showing. True when
    /// the inset changed, so the Desk slides.
    @discardableResult
    mutating func observe(_ seen: CGFloat?) -> Bool {
        let before = inset
        if let seen, seen > 0 {
            extent = seen
            if phase == .hidden || phase == .hiding { phase = .showing }
        } else if phase == .showing || phase == .shown {
            phase = .hiding
        }
        return inset != before
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
