import SwiftUI
import AppKit

/// Where now playing (item 53) shows on the Desk, decided from the same settings DeskView reads.
enum DeskNowPlaying {
    /// True when the strip under the camera shows now playing text (NotchView): the island on,
    /// a notch, a strip, its text on, its choice Now playing, and a track to show. `strip` is the
    /// effective content (NotchContent.effective): with no line it is the When nothing is playing
    /// choice, so the strip takes no now playing clicks.
    static func stripShows(notch: Bool, hasNotch: Bool, chin: Double, chinText: Bool,
                           strip: NotchContent, hasTrack: Bool) -> Bool {
        notch && hasNotch && chin > 0 && chinText && strip == .nowPlaying && hasTrack
    }

    /// Room between the line and its neighbours in a corner, so their click areas never meet
    /// (DeskFrameCheck): more than the meters' 6 and the line's 4 points of slack.
    static let gap: CGFloat = 14
    /// The spacing DeskView puts between the pieces in a column.
    static let columnSpacing: CGFloat = 10
    /// The padding above and below the line that makes up the gap.
    static var padding: CGFloat { gap - columnSpacing }
}

/// The Desk's now playing line: "▶ Title · Artist" over a thin position bar, in the Desk ink.
/// While playing the bar moves once a second (a TimelineView exists only then); paused, it is
/// drawn once; with nothing playing the line is gone, so nothing ticks. A click plays or pauses
/// and a two-finger click opens its menu (DeskController); the faint plate behind it lets those
/// clicks reach the transparent Desk window, as on the meters.
struct DeskNowPlayingLine: View {
    let info: NowPlayingInfo
    var model: DeskModel
    let ink: String
    let font: String
    let size: CGFloat
    let width: CGFloat
    let alignment: HorizontalAlignment

    var body: some View {
        VStack(alignment: alignment, spacing: size * 0.35) {
            Text(NowPlayingText.desk(info) ?? "")
                .font(.custom(font, size: size))
                .lineLimit(1)
                .truncationMode(.tail)
                .opacity(0.85)
                .frame(maxWidth: width, alignment: alignment.deskEdge)
            if (info.duration ?? 0) > 0 {
                if info.state == .playing {
                    TimelineView(.periodic(from: .now, by: 1)) { context in
                        bar(info.progress(at: context.date) ?? 0)
                    }
                } else {
                    bar(info.progress(at: Date()) ?? 0)
                }
            }
        }
        .frame(width: width, alignment: alignment.deskEdge)
        .background(Color.black.opacity(DeskPointerMenu.hitPlateOpacity)
            .padding(EdgeInsets(top: -4, leading: -8, bottom: -4, trailing: -8)))
        .contentShape(Rectangle())
        .onHover { inside in
            if inside { NSCursor.pointingHand.push() } else { NSCursor.pop() }
        }
        .onGlobalFrame { model.nowPlayingFrame = $0 }
        .accessibilityElement(children: .combine)
        .accessibilityAddTraits(.isButton)
        .accessibilityHint("Plays or pauses")
    }

    private func bar(_ fraction: Double) -> some View {
        let height = max(2, size * 0.16)
        return ZStack(alignment: .leading) {
            Capsule().fill(LinearGradient.ink(ink)).opacity(0.22)
            Capsule().fill(LinearGradient.ink(ink))
                .frame(width: width * min(1, max(0, fraction)))
        }
        .frame(width: width, height: height)
        .accessibilityHidden(true)
    }
}
