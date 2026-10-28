import CoreGraphics
import Foundation

/// Where the roaming companion stands: its bottom-centre point, which way it faces, and the edge it is on.
struct CompanionSpot: Equatable, Sendable {
    let x: Double
    let y: Double
    let facingLeft: Bool
    let edge: CompanionEdge
}

/// The route a roaming companion walks (pure geometry, in points, y growing downward: the bottom edge
/// is `area.maxY`; a caller in AppKit's y-up space flips y). The edges a pack names make one route: up
/// the left edge's foot, along the bottom, up the right edge, and it walks there and back. The sprite
/// stays upright and hugs the edge it is on (its bottom-centre point is half a sprite in from a side
/// edge). Without the bottom, the two side edges are walked in turn and the companion hops from the
/// foot of one to the foot of the other. A port of Windows' CompanionPath.
struct CompanionPath {
    private struct Segment {
        let x0, y0, x1, y1: Double
        let edge: CompanionEdge
        let length: Double
    }

    private var segments: [Segment] = []

    /// The route's length one way, in points.
    private(set) var length = 0.0

    private init() {}

    /// The route along `edges` of `area` (the screen less the Dock and menu bar) for a sprite
    /// `spriteWidth` by `spriteHeight`. Nil when the area cannot hold the sprite or no edge is named.
    static func build(edges: [CompanionEdge], area: CGRect, spriteWidth: Double, spriteHeight: Double) -> CompanionPath? {
        guard !edges.isEmpty, spriteWidth > 0, spriteHeight > 0 else { return nil }
        let xl = area.minX + spriteWidth / 2, xr = area.maxX - spriteWidth / 2
        let yb = area.maxY, yt = area.minY + spriteHeight
        guard xr - xl >= 1, yb - yt >= 1 else { return nil }
        var path = CompanionPath()
        if edges.contains(.left) { path.add(xl, yt, xl, yb, .left) }
        if edges.contains(.bottom) { path.add(xl, yb, xr, yb, .bottom) }
        if edges.contains(.right) { path.add(xr, yb, xr, yt, .right) }
        return path.length > 0 ? path : nil
    }

    private mutating func add(_ x0: Double, _ y0: Double, _ x1: Double, _ y1: Double, _ edge: CompanionEdge) {
        let len = ((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0)).squareRoot()
        segments.append(Segment(x0: x0, y0: y0, x1: x1, y1: y1, edge: edge, length: len))
        length += len
    }

    /// The spot after walking `distance` points from the route's start: out to the far end, then
    /// back, and so on. Facing is the way it walks along the bottom; on a side edge it faces into the screen.
    func at(_ distance: Double) -> CompanionSpot {
        var distance = distance
        if !distance.isFinite || distance < 0 { distance = 0 }
        let lap = 2 * length
        let d = distance.truncatingRemainder(dividingBy: lap)
        let back = d > length
        var along = back ? lap - d : d
        for (i, s) in segments.enumerated() {
            if along <= s.length || i == segments.count - 1 {
                let f = s.length <= 0 ? 0 : min(max(along / s.length, 0), 1)
                let left: Bool
                switch s.edge {
                case .left: left = false
                case .right: left = true
                case .bottom: left = back
                }
                return CompanionSpot(x: s.x0 + (s.x1 - s.x0) * f, y: s.y0 + (s.y1 - s.y0) * f, facingLeft: left, edge: s.edge)
            }
            along -= s.length
        }
        preconditionFailure("a path has at least one segment")
    }

    /// How far along the route (going out) the point nearest `x`, `y` lies: where a companion
    /// placed at an anchor starts walking from.
    func nearest(_ x: Double, _ y: Double) -> Double {
        var best = Double.greatestFiniteMagnitude, at = 0.0, walked = 0.0
        for s in segments {
            let dx = s.x1 - s.x0, dy = s.y1 - s.y0
            let t = s.length <= 0 ? 0 : min(max(((x - s.x0) * dx + (y - s.y0) * dy) / (s.length * s.length), 0), 1)
            let px = s.x0 + dx * t, py = s.y0 + dy * t
            let dist = (px - x) * (px - x) + (py - y) * (py - y)
            if dist < best {
                best = dist
                at = walked + t * s.length
            }
            walked += s.length
        }
        return at
    }
}

/// The roaming clock (pure: time comes in as steps). The companion walks for `walkFor`, then stops
/// for `restFor` and shows its mood, and so on. The clock only moves while walking is allowed:
/// asleep (exhausted), while a trigger plays, covered or paused, it holds still, and walks on from
/// the same spot afterwards.
struct CompanionRoamer {
    static let walkFor: TimeInterval = 12
    static let restFor: TimeInterval = 8

    private var phase = 0.0

    /// Seconds spent walking so far.
    private(set) var walkedSeconds = 0.0

    /// Where along the route it started (`CompanionPath.nearest` of its anchor).
    var start = 0.0

    init(start: Double = 0) { self.start = start }

    /// The distance walked at `speed` points a second, plus where it started.
    func distance(speed: Double) -> Double { start + walkedSeconds * speed }

    /// True in the walking part of the cycle.
    var walking: Bool { phase < Self.walkFor }

    /// Seconds left in the current part of the cycle (the walk's or the rest's): the length of the
    /// segment the Desk hands the render server.
    var secondsLeftInPhase: TimeInterval {
        walking ? Self.walkFor - phase : Self.walkFor + Self.restFor - phase
    }

    /// Whether the companion may roam right now. Reduce Motion means no roaming; so do sleeping
    /// (exhausted) and a trigger playing.
    static func allowed(reduceMotion: Bool, mood: CompanionMood, triggerPlaying: Bool) -> Bool {
        !reduceMotion && mood != .exhausted && !triggerPlaying
    }

    /// Moves the clock on by `step` seconds when `allowed`; holds it otherwise.
    mutating func advance(_ step: TimeInterval, allowed: Bool) {
        guard allowed, step > 0 else { return }
        // A long gap (the machine slept, the Desk was covered) is not a sprint across the screen.
        let dt = min(step, 1.0)
        let cycle = Self.walkFor + Self.restFor
        if walking { walkedSeconds += min(dt, Self.walkFor - phase) }
        phase = (phase + dt).truncatingRemainder(dividingBy: cycle)
    }
}
