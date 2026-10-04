import SwiftUI
import AppKit

/// A now playing title that doesn't fit its place (item 53b): it scrolls through once when a new
/// track starts or when the place first shows it, then goes back to the beginning and rests there.
/// Never loops; with Reduce Motion it never moves. The decision is pure; ScrollOnceText runs it.
enum NowPlayingScroll {
    /// Points per second while the text travels.
    static let speed: CGFloat = 30
    /// The beginning stays readable this long before the text moves.
    static let startPause: TimeInterval = 1.2
    /// The end stays readable this long before the text goes back.
    static let endPause: TimeInterval = 1.0
    /// The way back to the beginning.
    static let returnDuration: TimeInterval = 0.6
    /// The soft fade at a clipped edge, in points.
    static let fade: CGFloat = 12
    /// Measuring and drawing can differ by a fraction of a point; that much is still a fit.
    static let tolerance: CGFloat = 1

    struct Plan: Equatable {
        /// How far the text moves to the left.
        let distance: CGFloat
        /// How long that takes (ease in and out).
        let travel: TimeInterval
        /// Start pause, travel, end pause and the way back.
        var total: TimeInterval { startPause + travel + endPause + returnDuration }
    }

    static func fits(textWidth: CGFloat, room: CGFloat) -> Bool {
        textWidth <= room + tolerance
    }

    /// The scroll for a text `textWidth` wide in `room`, or nil when it fits, there is no room, or
    /// Reduce Motion is on. It travels until the text's end clears the fade at the trailing edge.
    static func plan(textWidth: CGFloat, room: CGFloat, reduceMotion: Bool) -> Plan? {
        guard !reduceMotion, room > 0, !fits(textWidth: textWidth, room: room) else { return nil }
        let distance = textWidth - room + fade
        return Plan(distance: distance, travel: TimeInterval(distance / speed))
    }

    /// What restarts the scroll: the track (title and artist), not its play state, so pausing or
    /// playing again does not scroll. In memory only, like the title itself.
    static func trackKey(_ info: NowPlayingInfo?) -> String {
        guard let info else { return "" }
        return (info.title ?? "") + "\u{1F}" + (info.artist ?? "")
    }
}

/// One line of now playing text that scrolls once when it doesn't fit (NowPlayingScroll): clipped
/// to `room` with a soft fade at the clipped edge, resting at its beginning.
struct ScrollOnceText: View {
    let text: String
    /// NowPlayingScroll.trackKey: a new one scrolls again.
    let trackKey: String
    /// Measured with the same font as the place's width (NotchWingsView.textWidth).
    let textWidth: CGFloat
    let room: CGFloat
    let font: Font

    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @State private var offset: CGFloat = 0
    @State private var moving = false

    var body: some View {
        let overflow = !NowPlayingScroll.fits(textWidth: textWidth, room: room)
        Text(text)
            .font(font)
            .lineLimit(1)
            .fixedSize()
            .offset(x: offset)
            .frame(width: overflow ? room : nil, alignment: .leading)
            .clipped()
            .mask { if overflow { fadeMask } else { Rectangle() } }
            .accessibilityLabel(text)
            .task(id: trackKey) { await scrollOnce() }
    }

    /// Opaque, fading out at the trailing edge (the text runs on) and, while the text has moved,
    /// at the leading edge too.
    private var fadeMask: some View {
        let f = room > 0 ? min(0.4, NowPlayingScroll.fade / room) : 0
        return LinearGradient(stops: [
            .init(color: moving ? .clear : .black, location: 0),
            .init(color: .black, location: f),
            .init(color: .black, location: 1 - f),
            .init(color: .clear, location: 1),
        ], startPoint: .leading, endPoint: .trailing)
    }

    private func scrollOnce() async {
        var snap = Transaction()
        snap.disablesAnimations = true
        withTransaction(snap) {
            offset = 0
            moving = false
        }
        guard let plan = NowPlayingScroll.plan(textWidth: textWidth, room: room, reduceMotion: reduceMotion) else { return }
        do {
            try await Task.sleep(for: .seconds(NowPlayingScroll.startPause))
            moving = true
            withAnimation(.easeInOut(duration: plan.travel)) { offset = -plan.distance }
            try await Task.sleep(for: .seconds(plan.travel + NowPlayingScroll.endPause))
            withAnimation(.easeInOut(duration: NowPlayingScroll.returnDuration)) { offset = 0 }
            try await Task.sleep(for: .seconds(NowPlayingScroll.returnDuration))
            moving = false
        } catch {
            // Cancelled: a new track or the place went away; the next run starts at the beginning.
        }
    }
}
