import SwiftUI
import AppKit

/// The notch, extended. On a Mac with a camera notch, Desk draws pure black that continues the
/// cutout a little wider and a little lower, with soft rounded corners, so it reads as one
/// bigger island. Inside the extra strip under the hardware notch it prints one short line,
/// by default the next meeting when one starts within the hour, otherwise the Claude meters
/// (Settings, Desk, Notch picks another; see `NotchContent`).
/// Nothing in the menu bar lives there (the menu bar never draws behind the notch, and Ice's
/// split bar leaves the middle clear), so it is free space.
///
///   defaults write com.626labs.sanduhr.desk notch -bool true         (turn it on; off by default)
///   defaults write com.626labs.sanduhr.desk notchWings -float 36     (extra width on each side)
///   defaults write com.626labs.sanduhr.desk notchChin -float 26      (extra height below the notch; 0 = none)
/// On a screen without a notch (an external display) nothing is drawn.
struct NotchView: View {
    var model: DeskModel

    @AppStorage(DeskController.notchKey, store: .desk) private var enabled = false
    @AppStorage("notchWings", store: .desk) private var wings = 36.0
    @AppStorage("notchChin", store: .desk) private var chin = 26.0
    @AppStorage("font", store: .desk) private var font = ""
    @AppStorage("notchChinText", store: .desk) private var showChinText = false
    @AppStorage("notchTextColor", store: .desk) private var textColor = "ffffff"
    @AppStorage("notchText", store: .desk) private var wingText = true
    @AppStorage(NotchContent.Place.left.key, store: .desk) private var leftContent = NotchContent.Place.left.fallback
    @AppStorage(NotchContent.Place.right.key, store: .desk) private var rightContent = NotchContent.Place.right.fallback
    @AppStorage(NotchContent.Place.strip.key, store: .desk) private var stripContent = NotchContent.Place.strip.fallback

    var body: some View {
        // Extra height 0 means no strip under the camera at all: just the wings.
        if enabled, chin > 0, let notch = model.notchRect {
            let height = notch.height + chin
            // Read here so a track change redraws at once, not at the next 15-second tick.
            let _ = model.nowPlaying
            TimelineView(.periodic(from: .now, by: 15)) { context in
                // Same widths as the wings above, so the strip and the wings stay one shape
                // even when a wing grows to fit its text.
                let w = NotchWingsView.layout(model: model, now: context.date, wings: wings,
                                              showText: wingText, left: leftContent, right: rightContent,
                                              font: font, notchHeight: notch.height)
                ZStack(alignment: .bottom) {
                    IslandShape(flare: 8, radius: min(16, chin * 0.7))
                        .fill(Color.black)
                    if showChinText, let line = stripContent.text(
                        at: .strip, meetings: model.meetings, meters: model.claudeCompact,
                        message: model.message, nowPlaying: model.nowPlaying, now: context.date) {
                        if stripContent == .nowPlaying {
                            stripNowPlaying(line, width: notch.width + w.left + w.right)
                        } else {
                            Text(line)
                                .font(.custom(font, size: stripSize))
                                .lineLimit(1)
                                .truncationMode(.tail)
                                .minimumScaleFactor(0.7)
                                .foregroundStyle(LinearGradient.ink(textColor))
                                .opacity(0.85)
                                .padding(.horizontal, 18)
                                .frame(height: chin)
                        }
                    }
                }
                .frame(width: notch.width + w.left + w.right, height: height)
                .offset(x: (w.right - w.left) / 2)
            }
            .position(x: notch.midX, y: height / 2)
            .allowsHitTesting(false)
        }
    }

    private var stripSize: CGFloat { max(11, chin * 0.55) }

    /// Now playing under the camera: the whole line, scrolling once when it doesn't fit, and while
    /// paused a Next button at its trailing end (item 53b). Both take clicks like the Desk line
    /// (DeskController: the text plays or pauses, the button skips, a two-finger click opens the
    /// menu), found by the frames they report.
    private func stripNowPlaying(_ line: String, width: CGFloat) -> some View {
        let state = model.nowPlaying?.state
        let room = NowPlayingWingLayout.textRoom(.strip, width: width, state: state, size: stripSize)
        return HStack(spacing: NowPlayingWingLayout.stripSpacing) {
            ScrollOnceText(text: line, trackKey: NowPlayingScroll.trackKey(model.nowPlaying),
                           scrolls: NowPlayingScroll.scrolls(model.nowPlaying),
                           textWidth: NotchWingsView.textWidth(line, stripSize, font), room: room,
                           font: .custom(font, size: stripSize))
                .foregroundStyle(LinearGradient.ink(textColor))
                .opacity(0.85)
                .frame(height: chin)
                .background(Color.black.opacity(DeskPointerMenu.hitPlateOpacity))
                .onGlobalFrame { model.stripFrame = $0 }
            if NowPlayingWingLayout.nextSide(.strip, state: state) != nil {
                Image(systemName: "forward.end.fill")
                    .font(.system(size: stripSize * 0.8, weight: .semibold))
                    .foregroundStyle(LinearGradient.ink(textColor))
                    .opacity(0.85)
                    .frame(width: NowPlayingWingLayout.nextWidth(stripSize), height: chin)
                    .background(Color.black.opacity(DeskPointerMenu.hitPlateOpacity))
                    .onGlobalFrame { model.stripNextFrame = $0 }
                    .accessibilityLabel("Next")
            }
        }
        .frame(width: max(0, width - NowPlayingWingLayout.stripPadding * 2))
    }
}

/// A rectangle hanging from the top edge with rounded bottom corners and small outward flares
/// at the top, the same silhouette as the hardware notch, so the two blend.
struct IslandShape: Shape {
    let flare: CGFloat
    let radius: CGFloat

    func path(in r: CGRect) -> Path {
        var p = Path()
        p.move(to: CGPoint(x: r.minX - flare, y: r.minY))
        p.addQuadCurve(to: CGPoint(x: r.minX, y: r.minY + flare), control: CGPoint(x: r.minX, y: r.minY))
        p.addLine(to: CGPoint(x: r.minX, y: r.maxY - radius))
        p.addQuadCurve(to: CGPoint(x: r.minX + radius, y: r.maxY), control: CGPoint(x: r.minX, y: r.maxY))
        p.addLine(to: CGPoint(x: r.maxX - radius, y: r.maxY))
        p.addQuadCurve(to: CGPoint(x: r.maxX, y: r.maxY - radius), control: CGPoint(x: r.maxX, y: r.maxY))
        p.addLine(to: CGPoint(x: r.maxX, y: r.minY + flare))
        p.addQuadCurve(to: CGPoint(x: r.maxX + flare, y: r.minY), control: CGPoint(x: r.maxX, y: r.minY))
        p.closeSubpath()
        return p
    }
}

/// The wings: the part of the island level with the menu bar, beside the hardware notch.
/// The desktop window sits below the menu bar layer, so this lives in its own small window
/// above the menu bar instead, where it always shows, like the notch itself. It is only
/// menu-bar tall, so it never covers an app's content.
///
/// Text rides in the wings, since they are visible over every app. By default (Settings, Desk,
/// Notch picks each wing's content; see `NotchContent`):
///   left wing   the next meeting when one starts within the hour ("standup 12m", "now standup"),
///               otherwise the time (so the macOS clock can go analog or hide)
///   right wing  the Claude meters ("5h 7%  wk 63%") while Sanduhr's numbers are fresh
/// A wing grows past the Wings setting when its text needs the room.
/// Click the island to open Desk's settings.
struct NotchWingsView: View {
    var model: DeskModel
    let notchWidth: CGFloat
    let notchHeight: CGFloat
    /// How far down the black reaches: the notch or the menu bar, whichever is taller.
    let barHeight: CGFloat
    @AppStorage(DeskController.notchKey, store: .desk) private var enabled = false
    @AppStorage("notchWings", store: .desk) private var wings = 36.0
    @AppStorage("notchText", store: .desk) private var showText = true
    @AppStorage("notchTextColor", store: .desk) private var textColor = "ffffff"
    @AppStorage("font", store: .desk) private var font = ""
    @AppStorage(NotchContent.Place.left.key, store: .desk) private var leftContent = NotchContent.Place.left.fallback
    @AppStorage(NotchContent.Place.right.key, store: .desk) private var rightContent = NotchContent.Place.right.fallback

    var body: some View {
        if enabled {
            // Read here so a track change redraws at once, not at the next 15-second tick.
            let _ = model.nowPlaying
            TimelineView(.periodic(from: .now, by: 15)) { context in
                let w = Self.layout(model: model, now: context.date, wings: wings, showText: showText,
                                    left: leftContent, right: rightContent,
                                    font: font, notchHeight: notchHeight)
                let left = w.leftText, right = w.rightText, size = w.size
                let wingL = w.left, wingR = w.right
                if wingL > 0 || wingR > 0 {
                    ZStack {
                        IslandShape(flare: 8, radius: min(10, barHeight * 0.3))
                            .fill(Color.black)
                        HStack(spacing: 0) {
                            wing(left, size, leftContent, place: .left, width: wingL).frame(width: max(0, wingL - 10), alignment: .trailing)
                            Color.clear.frame(width: notchWidth + 20)
                            wing(right, size, rightContent, place: .right, width: wingR).frame(width: max(0, wingR - 10), alignment: .leading)
                        }
                    }
                    .frame(width: notchWidth + wingL + wingR, height: barHeight)
                    .contentShape(Rectangle())
                    .onTapGesture { DeskController.shared.showSettings() }
                    .help("Sanduhr Settings")
                    // Last, so the island's click area moves with its drawing: an offset before
                    // contentShape left the click area at the unshifted place, so the far end of
                    // the wider wing (a paused Next button) drew where nothing took the click.
                    .offset(x: (wingR - wingL) / 2)
                    .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
                }
            }
        }
    }

    /// One wing's text. Now playing takes its own clicks: a click plays or pauses, a two-finger
    /// click opens Previous, Play/Pause, Next and Now Playing Settings…; the rest of the island
    /// still opens Settings. A title too long for the wing scrolls through once (item 53b).
    @ViewBuilder
    private func wing(_ text: String?, _ size: CGFloat, _ content: NotchContent, place: NotchContent.Place,
                      width: CGFloat) -> some View {
        if content == .nowPlaying, let text {
            nowPlayingWing(text, size, place: place, width: width)
        } else {
            label(text, size)
        }
    }

    /// Now playing in a wing: the title, scrolling once when it doesn't fit (item 53b), and while
    /// paused a Next button at the wing's outer edge (item 53b). The title keeps its beginning
    /// visible; a click on it plays or pauses, a click on the button skips. Both carry the
    /// two-finger menu.
    private func nowPlayingWing(_ text: String, _ size: CGFloat, place: NotchContent.Place, width: CGFloat) -> some View {
        let state = model.nowPlaying?.state
        let side = NowPlayingWingLayout.nextSide(place, state: state)
        let room = NowPlayingWingLayout.textRoom(place, width: width, state: state, size: size)
        let parts = NowPlayingText.splitGlyph(text)
        let glyphGap = size * 0.3
        let glyphWidth = parts.glyph.map { Self.textWidth($0, size, font) + glyphGap } ?? 0
        let titleRoom = max(0, room - glyphWidth)
        return HStack(spacing: 0) {
            if side == .leading {
                nextButton(size)
                Spacer(minLength: NowPlayingWingLayout.wingSpacing)
            } else if place == .left {
                Spacer(minLength: 0)
            }
            // The play-state glyph stays put at the wing's inner end; only the title scrolls.
            HStack(spacing: glyphGap) {
                if let glyph = parts.glyph {
                    Text(glyph).font(.custom(font, size: size)).fixedSize()
                }
                ScrollOnceText(text: parts.rest, trackKey: NowPlayingScroll.trackKey(model.nowPlaying),
                               scrolls: NowPlayingScroll.scrolls(model.nowPlaying),
                               textWidth: Self.textWidth(parts.rest, size, font), room: titleRoom,
                               font: .custom(font, size: size))
            }
                .foregroundStyle(LinearGradient.ink(textColor))
                .opacity(0.88)
                .frame(maxHeight: .infinity)
                .background(Color.black.opacity(DeskPointerMenu.hitPlateOpacity))
                .contentShape(Rectangle())
                .onTapGesture { NowPlayingController.shared.togglePlayPause() }
                .help(state == .paused ? "Play. Two-finger click for more." : "Play or pause. Two-finger click for more.")
            if side == .trailing {
                Spacer(minLength: NowPlayingWingLayout.wingSpacing)
                nextButton(size)
            } else if place == .right {
                Spacer(minLength: 0)
            }
        }
        .frame(width: max(0, width - NowPlayingWingLayout.wingInsets))
        .padding(place == .left ? .leading : .trailing, NowPlayingWingLayout.outerInset)
        // The whole wing is now playing's: a click in the gaps beside the title or Next lands here
        // and does nothing, instead of reaching the island's tap that opens Settings.
        .background(Color.black.opacity(DeskPointerMenu.hitPlateOpacity))
        .contentShape(Rectangle())
        .onTapGesture {}
        .contextMenu { NowPlayingMenuItems() }
    }

    /// The paused wing's Next button: its own click area, the wing's height.
    private func nextButton(_ size: CGFloat) -> some View {
        Image(systemName: "forward.end.fill")
            .font(.system(size: size * 0.8, weight: .semibold))
            .foregroundStyle(LinearGradient.ink(textColor))
            .opacity(0.88)
            .frame(width: NowPlayingWingLayout.nextWidth(size))
            .frame(maxHeight: .infinity)
            // contentShape only routes clicks inside SwiftUI; the window server gives this
            // transparent window a click only where a pixel is drawn, so a click between the
            // glyph's strokes (or beside it, past the island's black) fell through to the menu
            // bar. The faint plate draws a pixel under the whole button (DeskPointerMenu).
            .background(Color.black.opacity(DeskPointerMenu.hitPlateOpacity))
            .contentShape(Rectangle())
            .onTapGesture { NowPlayingController.shared.next() }
            .help("Next")
            .accessibilityElement()
            .accessibilityLabel("Next")
            .accessibilityAddTraits(.isButton)
    }

    private func label(_ text: String?, _ size: CGFloat) -> some View {
        Text(text ?? "")
            .font(.custom(font, size: size))
            .foregroundStyle(LinearGradient.ink(textColor))
            .opacity(0.88)
            .lineLimit(1)
            .truncationMode(.tail)
    }

    /// Wing widths and texts, shared with the strip under the notch so both draw one shape.
    struct Layout { let left: CGFloat; let right: CGFloat; let leftText: String?; let rightText: String?; let size: CGFloat }

    static func layout(model: DeskModel, now: Date, wings: Double, showText: Bool,
                       left leftContent: NotchContent, right rightContent: NotchContent,
                       font: String, notchHeight: CGFloat) -> Layout {
        let left = showText ? text(leftContent, at: .left, model: model, now: now) : nil
        let right = showText ? text(rightContent, at: .right, model: model, now: now) : nil
        let size = max(10, notchHeight * 0.42)
        let state = model.nowPlaying?.state
        func wingWidth(_ text: String?, _ content: NotchContent, _ place: NotchContent.Place) -> CGFloat {
            // A paused now playing also makes room for its Next button (item 53b).
            NowPlayingWingLayout.wingWidth(
                textWidth: textWidth(text, size, font), place: place,
                state: content == .nowPlaying && text != nil ? state : nil,
                size: size, minimum: wings, maximum: maxWings)
        }
        return Layout(left: wingWidth(left, leftContent, .left),
                      right: wingWidth(right, rightContent, .right),
                      leftText: left, rightText: right, size: size)
    }

    private static func text(_ content: NotchContent, at place: NotchContent.Place,
                             model: DeskModel, now: Date) -> String? {
        content.text(at: place, meetings: model.meetings, meters: model.claudeCompact,
                     message: model.message, nowPlaying: model.nowPlaying, now: now)
    }

    /// The text's width in the notch font: what the wings grow by and what a now playing title
    /// scrolls against.
    static func textWidth(_ text: String?, _ size: CGFloat, _ font: String) -> CGFloat {
        guard let text, !text.isEmpty else { return 0 }
        // The setting holds a family name; measure with that family's regular face.
        let nsFont = NSFontManager.shared.font(withFamily: font, traits: [], weight: 5, size: size)
            ?? NSFont(name: font, size: size) ?? NSFont.systemFont(ofSize: size)
        return ceil((text as NSString).size(withAttributes: [.font: nsFont]).width)
    }

    /// Widest a wing can get, so the window never needs resizing.
    static let maxWings: CGFloat = 180
}

/// The now playing menu as SwiftUI items, for the notch wings' context menu (the Desk uses
/// NowPlayingController.menu(), an NSMenu, from its event monitors).
struct NowPlayingMenuItems: View {
    var body: some View {
        let controller = NowPlayingController.shared
        Button("Previous") { controller.previous() }
        Button(controller.state == .playing ? "Pause" : "Play") { controller.togglePlayPause() }
        Button("Next") { controller.next() }
        Divider()
        Button("Now Playing Settings…") { DeskController.shared.showSettings(.nowPlaying) }
    }
}
