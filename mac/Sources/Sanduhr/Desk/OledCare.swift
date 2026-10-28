import Foundation
import CoreGraphics

/// "OLED care" (Settings, Desk; the Mac port of Windows' 3.7.0 `OledCare`). macOS can't reliably tell an
/// OLED panel from any other, so it is a switch: off by default, and when on the Desk drifts a few points
/// every few minutes (`OledDrift`), the Desk and the widget dim while you are away (`OledIdle`), and the
/// Desk's ink never holds pure white (`OledInk`). Pure rules here; OledCareController wires them.
enum OledCare {
    /// The desk suite key (`com.626labs.sanduhr.desk`).
    static let key = "deskOledCare"
    /// The switch is off when the key is absent.
    static let defaultOn = false

    static let title = "OLED care"
    static let caption = "For OLED screens: the Desk drifts a few pixels every few minutes, dims when you're away, and never holds pure white."

    /// Whether the switch is on; a missing or odd value is off.
    static func isOn(in store: DefaultsStore = UserDefaults.desk) -> Bool {
        store.object(forKey: key) as? Bool ?? defaultOn
    }
}

/// How far the Dock, the menu bar or the notch reaches into the Desk's area on each side, in points.
struct OledInsets: Equatable {
    var left: CGFloat = 0
    var top: CGFloat = 0
    var right: CGFloat = 0
    var bottom: CGFloat = 0

    static let none = OledInsets()
}

/// The slow orbit a Desk piece follows with OLED care on. Pure geometry (top-left origin, points): the host
/// moves the piece, never re-lays it out.
enum OledDrift {
    /// The farthest a piece strays from its anchor, per axis, in points.
    static let radius = 6
    /// Points in one loop of the orbit.
    static let steps = 16
    /// How often the orbit moves on to its next point.
    static let every: TimeInterval = 3 * 60

    /// A figure-eight (a 1:2 Lissajous curve) sampled at 16 points: x = 6 sin(k pi / 8), y = 6 sin(k pi / 4),
    /// rounded. Each side of the eight sums to nothing, so the loop's average is the anchor; successive points
    /// always differ; it takes 48 minutes to come round, and no row or column is held for long.
    private static let orbit: [(x: Int, y: Int)] = [
        (0, 0), (2, 4), (4, 6), (6, 4), (6, 0), (6, -4), (4, -6), (2, -4),
        (0, 0), (-2, 4), (-4, 6), (-6, 4), (-6, 0), (-6, -4), (-4, -6), (-2, -4),
    ]

    /// The offset from the anchor at `step` (any integer; the loop repeats every `steps`).
    static func offset(_ step: Int) -> (x: Int, y: Int) {
        orbit[((step % steps) + steps) % steps]
    }

    /// `rect` moved the least that puts it inside `area` less `clearance`. A rect larger than that region
    /// sits at its top left.
    static func clamp(_ rect: CGRect, area: CGRect, clearance: OledInsets) -> CGRect {
        let r = region(area, clearance)
        var out = rect
        out.origin.x = axis(rect.minX, rect.width, r.loX, r.hiX, relaxTo: nil)
        out.origin.y = axis(rect.minY, rect.height, r.loY, r.hiY, relaxTo: nil)
        return out
    }

    /// `home` (the piece's true place) moved to the orbit's point at `step`, then clamped as `clamp`. A piece
    /// the layout already put outside the region is never pulled in by drift: it may move along the orbit,
    /// but never farther out than it started.
    static func drifted(_ home: CGRect, step: Int, area: CGRect, clearance: OledInsets) -> CGRect {
        let d = offset(step)
        let r = region(area, clearance)
        var out = home
        out.origin.x = axis(home.minX + CGFloat(d.x), home.width, r.loX, r.hiX, relaxTo: home.minX)
        out.origin.y = axis(home.minY + CGFloat(d.y), home.height, r.loY, r.hiY, relaxTo: home.minY)
        return out
    }

    private static func region(_ area: CGRect, _ c: OledInsets) -> (loX: CGFloat, hiX: CGFloat, loY: CGFloat, hiY: CGFloat) {
        (area.minX + ceil0(c.left), area.maxX - ceil0(c.right), area.minY + ceil0(c.top), area.maxY - ceil0(c.bottom))
    }

    private static func ceil0(_ v: CGFloat) -> CGFloat { v > 0 ? v.rounded(.up) : 0 }

    /// One axis: where a span of `size` starting at `pos` lands between `lo` and `hi` (the region's edges).
    /// `relaxTo` widens the allowed range to include a start the layout chose.
    private static func axis(_ pos: CGFloat, _ size: CGFloat, _ lo: CGFloat, _ hi: CGFloat, relaxTo: CGFloat?) -> CGFloat {
        var lo = lo
        var high = max(lo, hi - size)
        if let home = relaxTo {
            lo = min(lo, home)
            high = max(high, home)
        }
        return min(max(pos, lo), high)
    }
}

/// What `OledIdle` asks for after a look at the last input.
enum OledIdleEdge: Equatable { case none, dim, wake }

/// The away-from-keyboard rule for OLED care: after `threshold` without input the Desk and the widget dim,
/// and any input wakes them. The clock is injected; the caller reads when the last input happened
/// (IdleClock, polled every `pollEvery`) and this keeps the state. It touches no window.
final class OledIdle {
    /// No input for this long dims the Desk.
    static let defaultThreshold: TimeInterval = 5 * 60
    /// How often the last input is looked at: cheap, and the dim lands within this of the threshold.
    static let pollEvery: TimeInterval = 15
    /// The Desk's opacity while dimmed.
    static let deskDimmedOpacity = 0.40
    /// The widget window's opacity while dimmed.
    static let widgetDimmedOpacity = 0.70
    /// How long the dim eases in.
    static let dimEase: TimeInterval = 2
    /// How long the wake eases back.
    static let wakeEase: TimeInterval = 0.3

    private let clock: () -> Date
    private let threshold: TimeInterval

    init(clock: @escaping () -> Date = Date.init, threshold: TimeInterval = OledIdle.defaultThreshold) {
        self.clock = clock
        self.threshold = threshold
    }

    /// True while dimmed.
    private(set) var dimmed = false

    /// Looks at when the last input happened (nil: it could not be read, which changes nothing). Dim once, when
    /// the time since then reaches the threshold; wake once, when it falls back under it while dimmed;
    /// otherwise none.
    func observe(lastInputAt: Date?) -> OledIdleEdge {
        guard let at = lastInputAt else { return .none }
        let away = clock().timeIntervalSince(at) >= threshold
        if away == dimmed { return .none }
        dimmed = away
        return away ? .dim : .wake
    }

    /// OLED care was switched off: wake when it had dimmed, else none. `dimmed` is clear afterwards.
    func reset() -> OledIdleEdge {
        guard dimmed else { return .none }
        dimmed = false
        return .wake
    }

    /// The ease for an edge, snapped to nothing when Reduce Motion is on (`animate` false).
    static func easeFor(_ edge: OledIdleEdge, animate: Bool) -> TimeInterval {
        !animate ? 0 : edge == .dim ? dimEase : wakeEase
    }
}

/// OLED care's render-time white cap, on ink specs ("ffffff", "531b93,012089,00fdff"): no held pure white.
/// Ports Windows' `InkHex.CapLuminance`, `DeskInk.WhiteCapLuminance` and `DeskInk.CapWhite`. The saved ink is
/// never touched: the Desk just draws a notch dimmer.
enum OledInk {
    /// The sRGB-to-linear table.
    private static let lin: [Double] = (0..<256).map { i in
        let v = Double(i) / 255
        return v <= 0.04045 ? v / 12.92 : pow((v + 0.055) / 1.055, 2.4)
    }

    /// WCAG relative luminance (0 black to 1 white) of a hex color; an invalid color reads as white (1).
    static func luminance(_ hex: String?) -> Double {
        guard let h = hex.flatMap(DeskThemeMapping.normalized), let (r, g, b) = bytes(h) else { return 1 }
        return 0.2126 * lin[r] + 0.7152 * lin[g] + 0.0722 * lin[b]
    }

    /// The luminance OLED care holds the Desk's ink to: that of #e6e6e6, so pure white is drawn as #e6e6e6.
    static let whiteCapLuminance = luminance("e6e6e6")

    /// `hex` with its luminance held to `maxLuminance`: a color above it is scaled toward black in linear
    /// light, which keeps its hue, and one at or under it comes back as it was. An invalid color reads as
    /// white. Six-digit lowercase hex out.
    static func capLuminance(_ hex: String?, _ maxLuminance: Double) -> String {
        let h = hex.flatMap(DeskThemeMapping.normalized) ?? "ffffff"
        let l = luminance(h)
        guard l > maxLuminance, l > 0, let (r, g, b) = bytes(h) else { return h }
        let f = maxLuminance / l
        return [r, g, b].map { String(format: "%02x", encode(lin[$0] * f)) }.joined()
    }

    /// An ink spec with every stop above the white cap scaled down to it, hue kept; stops under it, and the
    /// separators, are as they were. A part that is not a hex color is left for `Color.hex` to handle; an empty
    /// spec (which the Desk draws as white) becomes the capped white.
    static func capWhite(_ spec: String) -> String {
        let parts = spec.split(separator: ",", omittingEmptySubsequences: false).map(String.init)
        var any = false
        let capped = parts.map { part -> String in
            guard DeskThemeMapping.normalized(part) != nil else { return part }
            any = true
            return capLuminance(part, whiteCapLuminance)
        }
        return any ? capped.joined(separator: ",") : capLuminance("ffffff", whiteCapLuminance)
    }

    private static func bytes(_ h: String) -> (Int, Int, Int)? {
        guard h.count == 6, let v = Int(h, radix: 16) else { return nil }
        return ((v >> 16) & 0xff, (v >> 8) & 0xff, v & 0xff)
    }

    private static func encode(_ linear: Double) -> Int {
        let v = linear <= 0.0031308 ? linear * 12.92 : 1.055 * pow(linear, 1 / 2.4) - 0.055
        return min(max(Int((v * 255).rounded()), 0), 255)
    }
}
