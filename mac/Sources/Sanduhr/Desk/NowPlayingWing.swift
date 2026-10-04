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

/// How a now playing place is laid out (item 53b): the text's room and, while paused, where the
/// Next button sits. A wing's button sits at its outer edge (away from the camera: the left wing's
/// left, the right wing's right); the strip's at its trailing end. The text keeps its beginning
/// visible on the other side.
enum NowPlayingWingLayout {
    enum Side: Equatable { case leading, trailing }

    /// Between a wing's text and the camera.
    static let cameraInset: CGFloat = 10
    /// Between a wing's content and its outer edge.
    static let outerInset: CGFloat = 12
    /// A wing's content is this much narrower than the wing.
    static var wingInsets: CGFloat { cameraInset + outerInset }
    /// Between a wing's text and its Next button.
    static let wingSpacing: CGFloat = 6
    /// The strip's padding on each side.
    static let stripPadding: CGFloat = 18
    /// Between the strip's text and its Next button: wider than both click slacks together
    /// (DeskHitTest), so their click areas never meet.
    static let stripSpacing: CGFloat = 14

    /// The Next button's width for a text size.
    static func nextWidth(_ size: CGFloat) -> CGFloat { ceil(size * 1.5) }

    /// Next shows while paused, and only then.
    static func showsNext(_ state: NowPlayingState?) -> Bool { state == .paused }

    /// The Next button's side at `place`, or nil when it doesn't show.
    static func nextSide(_ place: NotchContent.Place, state: NowPlayingState?) -> Side? {
        guard showsNext(state) else { return nil }
        return place == .left ? .leading : .trailing
    }

    /// The extra width a place needs for the button.
    static func nextRoom(_ place: NotchContent.Place, state: NowPlayingState?, size: CGFloat) -> CGFloat {
        guard showsNext(state) else { return 0 }
        return nextWidth(size) + (place == .strip ? stripSpacing : wingSpacing)
    }

    /// A wing's width for its text: room for the text, the insets and (paused) the button, at
    /// least `minimum` (the Wings setting), at most `maximum`.
    static func wingWidth(textWidth: CGFloat, place: NotchContent.Place, state: NowPlayingState?,
                          size: CGFloat, minimum: CGFloat, maximum: CGFloat) -> CGFloat {
        min(maximum, max(minimum, textWidth + wingInsets + nextRoom(place, state: state, size: size)))
    }

    /// The width the text has at `place`, given the place's whole width (a wing's, or the strip's).
    static func textRoom(_ place: NotchContent.Place, width: CGFloat, state: NowPlayingState?, size: CGFloat) -> CGFloat {
        let insets = place == .strip ? stripPadding * 2 : wingInsets
        return max(0, width - insets - nextRoom(place, state: state, size: size))
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
