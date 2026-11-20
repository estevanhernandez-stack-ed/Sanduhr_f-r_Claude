import Foundation
import CoreGraphics

/// The two-tone combined bar's pure layout (agent layer design section 3), ported from Windows'
/// `SourceBar`. On a meter that shows more than one source: the primary fills from 0 to its percent
/// in its color; the furthest other source past it continues the bar in its own color; every other
/// source is a tick. Nothing here draws: `ProgressBarView` reads the layout, and each mark carries
/// the name a screen reader and a tooltip say.

/// The window a combined bar covers.
enum SourceBarWindow: Equatable {
    case fiveHour, weekly

    /// The bar's own label: the cards' wording ("Session (5hr)"), as Windows' combined bars say it.
    var label: String {
        switch self {
        case .fiveHour: Tier.fiveHour.label
        case .weekly: "Weekly"
        }
    }

    /// The word a mark's name uses: "Codex session, 40%." and "Codex weekly, 62%."
    var markWord: String {
        switch self {
        case .fiveHour: "session"
        case .weekly: "weekly"
        }
    }
}

/// One source's reading for the window: its percent, whether it can be trusted now, and the color to
/// draw it in. `asOf` is the time a stale reading was taken ("3:42 PM"), already formatted by the
/// caller; without it a stale mark says "As of earlier."
struct SourceBarPoint: Equatable {
    var source: AgentSource
    var percent: Double
    var stale: Bool
    var colorHex: String
    var asOf: String?

    init(source: AgentSource, percent: Double, stale: Bool = false, colorHex: String? = nil, asOf: String? = nil) {
        self.source = source
        self.percent = percent
        self.stale = stale
        self.colorHex = colorHex ?? source.defaultColor
        self.asOf = asOf
    }
}

/// A filled stretch of the bar, `from` to `to` as 0 to 1 of its width.
struct SourceBarSegment: Equatable {
    var source: AgentSource
    var from: Double
    var to: Double
    /// The source's percent, clamped, 0 to 100.
    var percent: Double
    var stale: Bool
    var colorHex: String
    /// "Codex weekly, 62%." and, when stale, " As of 3:42 PM."
    var name: String

    var opacity: Double { stale ? SourceBar.staleOpacity : 1 }
}

/// A source's mark on the bar, `at` as 0 to 1 of its width.
struct SourceBarTick: Equatable {
    var source: AgentSource
    var at: Double
    var percent: Double
    var stale: Bool
    var colorHex: String
    var name: String

    var opacity: Double { stale ? SourceBar.staleOpacity : 1 }
}

struct SourceBarLayout: Equatable {
    var primary: SourceBarSegment?
    var beyond: SourceBarSegment?
    var ticks: [SourceBarTick]
    /// "Weekly. Claude 40%. Codex 70%, as of earlier." The whole bar's sentence.
    var accessibleName: String

    /// One fill and nothing else: no continuation, no ticks. A bar like this draws exactly as a
    /// one-source meter always has (usage colors, no source tone), so a Mac with only Claude on
    /// reads as it did before there were sources.
    var isSingleFill: Bool { beyond == nil && ticks.isEmpty }

    var segments: [SourceBarSegment] { [primary, beyond].compactMap { $0 } }

    /// Every mark's name: the segments, then the ticks.
    var markNames: [String] { segments.map(\.name) + ticks.map(\.name) }
}

enum SourceBar {
    /// A stale source draws at this opacity (Windows' `CombinedBarViewModel.StaleOpacity`).
    static let staleOpacity = 0.55
    /// A tick is this wide, points.
    static let tickWidth: CGFloat = 2
    /// A fill at 0 still shows this sliver of the bar, as `ProgressBarView` always has.
    static let minimumFill = 0.008

    private static func order(_ source: AgentSource) -> Int {
        AgentSource.allCases.firstIndex(of: source) ?? Int.max
    }

    /// The percent a bar can say: finite, 0 to 100 (not a number reads as 0).
    private static func clamped(_ percent: Double) -> Double {
        percent.isFinite ? min(max(percent, 0), 100) : 0
    }

    /// The whole number a percent is spoken as: truncated like the visible numbers, with a little slack
    /// so float noise (28.999999999999996) never costs a point.
    private static func spoken(_ percent: Double) -> Int {
        Int((clamped(percent) + 1e-9).rounded(.down))
    }

    /// The layout for one window. `primary` fills the bar; with it absent from `points`, the highest
    /// source does (the earlier source on a tie).
    static func layout(primary: AgentSource, window: SourceBarWindow, points: [SourceBarPoint]) -> SourceBarLayout {
        let ordered = points.map { point -> SourceBarPoint in
            var p = point
            p.percent = clamped(point.percent)
            return p
        }.sorted { order($0.source) < order($1.source) }

        guard let main = ordered.first(where: { $0.source == primary })
            ?? ordered.sorted(by: furtherOut).first else {
            return SourceBarLayout(primary: nil, beyond: nil, ticks: [],
                                   accessibleName: wholeName(window: window, points: ordered))
        }
        let others = ordered.filter { $0.source != main.source }
        let fill = main.percent / 100
        let beyond = others.filter { $0.percent / 100 > fill }.sorted(by: furtherOut).first

        return SourceBarLayout(
            primary: segment(main, from: 0, window: window),
            beyond: beyond.map { segment($0, from: fill, window: window) },
            ticks: others.map { tick($0, window: window) },
            accessibleName: wholeName(window: window, points: ordered))
    }

    // MARK: Geometry

    /// Where a segment sits in a bar `width` points wide. The primary keeps a sliver at 0.
    static func span(_ segment: SourceBarSegment, in width: CGFloat) -> (x: CGFloat, width: CGFloat) {
        let length = segment.from == 0 ? max(segment.to, minimumFill) : segment.to - segment.from
        return (CGFloat(segment.from) * width, max(0, CGFloat(length) * width))
    }

    /// A tick's left edge: centered on its percent, kept inside the bar.
    static func tickX(at fraction: Double, in width: CGFloat) -> CGFloat {
        max(0, min(width - tickWidth, CGFloat(fraction) * width - tickWidth / 2))
    }

    // MARK: Pieces

    /// Higher percent first, the earlier source on a tie.
    private static func furtherOut(_ a: SourceBarPoint, _ b: SourceBarPoint) -> Bool {
        a.percent != b.percent ? a.percent > b.percent : order(a.source) < order(b.source)
    }

    private static func segment(_ p: SourceBarPoint, from: Double, window: SourceBarWindow) -> SourceBarSegment {
        SourceBarSegment(source: p.source, from: from, to: p.percent / 100, percent: p.percent,
                         stale: p.stale, colorHex: p.colorHex, name: markName(p, window: window))
    }

    private static func tick(_ p: SourceBarPoint, window: SourceBarWindow) -> SourceBarTick {
        SourceBarTick(source: p.source, at: p.percent / 100, percent: p.percent,
                      stale: p.stale, colorHex: p.colorHex, name: markName(p, window: window))
    }

    /// "Codex weekly, 62%." or "Claude session, 40%."; a stale source adds " As of 3:42 PM." (Windows'
    /// `SourceTooltip`), or " As of earlier." when the caller has no time to give.
    private static func markName(_ p: SourceBarPoint, window: SourceBarWindow) -> String {
        var text = "\(p.source.displayName) \(window.markWord), \(spoken(p.percent))%."
        if p.stale { text += " As of \(p.asOf ?? "earlier")." }
        return text
    }

    /// "Weekly. Claude 40%. Codex 70%, as of earlier."
    private static func wholeName(window: SourceBarWindow, points: [SourceBarPoint]) -> String {
        let parts = points.map { "\($0.source.displayName) \(spoken($0.percent))%\($0.stale ? ", as of earlier" : "")." }
        return ([window.label + "."] + parts).joined(separator: " ")
    }
}
