import SwiftUI
import AppKit

/// The Desk panes' preview cards (item 68): Notch, Look, Meters, Message and Now Playing, each
/// drawn by the Desk's own views (NotchView, NotchWingsView, NotchGlowView, AVIndicatorBadge,
/// DeskPiece) over a preview DeskModel (SurfacePreviewData.fill). Layout's map is DeskLayoutMap.

// MARK: - Notch

/// The notch the previews draw around: this Mac's notched screen, or a typical one (for the Now
/// Playing wings) on a Mac without one.
struct NotchPreviewGeometry: Equatable {
    var notch: CGSize
    /// The wings' height: the notch or the menu bar, whichever is taller.
    var bar: CGFloat
    var hasNotch: Bool

    /// A 14-inch MacBook Pro's notch.
    static let typical = NotchPreviewGeometry(notch: CGSize(width: 185, height: 32), bar: 33, hasNotch: false)

    @MainActor static func current() -> NotchPreviewGeometry {
        guard let screen = NSScreen.screens.first(where: { $0.cameraNotch != nil }),
              let notch = screen.cameraNotch else { return typical }
        let menuBar = screen.frame.maxY - screen.visibleFrame.maxY
        return NotchPreviewGeometry(notch: notch.size, bar: max(notch.height, menuBar), hasNotch: true)
    }

    /// The wings' window width past the notch on each side, as DeskController makes it.
    var pad: CGFloat { NotchWingsView.windowPad(notchHeight: notch.height) }
    /// The wings' window: the notch and the pad on each side.
    var wingsWidth: CGFloat { notch.width + pad * 2 }
    /// The glow's window: the wings' and the glow's reach on each side.
    var canvasWidth: CGFloat { wingsWidth + NotchGlowLayout.reach * 2 }

    /// The notch in the canvas, top-left origin, as `DeskModel.notchRect` holds it on a screen.
    var notchRect: CGRect {
        CGRect(x: NotchGlowLayout.reach + pad, y: 0, width: notch.width, height: notch.height)
    }
}

/// The hardware notch itself, which the island continues.
private struct HardwareNotch: View {
    let size: CGSize
    var body: some View {
        IslandShape(flare: 0, radius: NotchGlowLayout.plainRadius(notchHeight: size.height))
            .fill(Color.black)
            .frame(width: size.width, height: size.height)
    }
}

/// The menu bar strip the island sits in.
private struct MenuBarStrip: View {
    let height: CGFloat
    var body: some View {
        Rectangle().fill(Color.black.opacity(0.3)).frame(height: height)
    }
}

struct NotchPreview: View {
    var live: DeskModel
    @State private var preview = DeskModel()
    @State private var samples: PreviewSamples = []
    @State private var glow = 0.0
    @AppStorage(DeskController.notchKey, store: .desk) private var enabled = false
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        let g = NotchPreviewGeometry.current()
        SettingsPreviewCard(kind: .notch, label: Self.spoken(enabled: enabled, hasNotch: g.hasNotch),
                            samples: samples, accessory: AnyView(testGlow)) {
            if g.hasNotch {
                NotchIslandCanvas(model: preview, geometry: g, glow: glow)
            } else {
                NotchTabCanvas(model: preview, geometry: g)
            }
        }
        .modifier(PreviewModelSync(preview: preview, live: live, islandUp: enabled && g.hasNotch, samples: $samples))
        .onAppear { preview.notchRect = g.hasNotch ? g.notchRect : nil }
        .onReceive(NotificationCenter.default.publisher(for: NotchGlowController.didFire)) { _ in glowOnce() }
    }

    static func spoken(enabled: Bool, hasNotch: Bool) -> String {
        if !hasNotch { return "This Mac has no notch, so the camera and mic indicators show in a small tab at the top." }
        return enabled
            ? "The notch island with its wings, the strip under the camera and the camera and mic indicators, as set below."
            : "The camera notch as it is now: the island is off."
    }

    /// The pane's Test Glow, in place: it fires the real glow, and the card glows with it.
    private var testGlow: some View {
        Button("Test Glow") { NotchGlowController.shared.fire() }
            .controlSize(.small)
            .help("Glows the notch once, here and on the screen, whatever the switches below say")
    }

    /// The glow's timing (NotchGlowController); with Reduce Motion it shows and goes without fading.
    private func glowOnce() {
        let c = NotchGlowController.self
        if reduceMotion {
            glow = 1
            DispatchQueue.main.asyncAfter(deadline: .now() + c.fadeIn + c.hold + c.fadeOut) { glow = 0 }
            return
        }
        withAnimation(.easeOut(duration: c.fadeIn)) { glow = 1 }
        DispatchQueue.main.asyncAfter(deadline: .now() + c.fadeIn + c.hold) {
            withAnimation(.easeIn(duration: c.fadeOut)) { glow = 0 }
        }
    }
}

/// The island at true proportions, laid out as the screen lays it out: the strip from the Desk
/// window (NotchView), the wings from their own window above it (NotchWingsView), the glow's
/// halo (NotchGlowView) around both.
private struct NotchIslandCanvas: View {
    var model: DeskModel
    let geometry: NotchPreviewGeometry
    let glow: Double
    @AppStorage("notchChin", store: .desk) private var chin = 26.0

    var body: some View {
        let g = geometry
        let island = NotchGlowLayout.islandHeight(notchHeight: g.notch.height, barHeight: g.bar, chin: chin)
        let height = island + NotchGlowLayout.reach * 2
        ZStack(alignment: .top) {
            MenuBarStrip(height: g.bar)
            NotchView(model: model)
                .frame(width: g.canvasWidth, height: height, alignment: .topLeading)
            HardwareNotch(size: g.notch)
            NotchWingsView(model: model, notchWidth: g.notch.width, notchHeight: g.notch.height, barHeight: g.bar)
                .frame(width: g.wingsWidth, height: g.bar)
            NotchGlowView(model: model, notchWidth: g.notch.width, notchHeight: g.notch.height, barHeight: g.bar)
                .frame(width: g.canvasWidth, height: height)
                .opacity(glow)
        }
        .frame(width: g.canvasWidth, height: height, alignment: .top)
    }
}

/// A Mac without a notch: the camera and mic tab (AVIndicatorBadge) at the top center.
private struct NotchTabCanvas: View {
    var model: DeskModel
    let geometry: NotchPreviewGeometry

    var body: some View {
        let size = max(10, geometry.bar * 0.42)
        let shown = model.avIndicators
        VStack(spacing: 12) {
            ZStack(alignment: .top) {
                MenuBarStrip(height: geometry.bar)
                if shown.any {
                    AVIndicatorBadge(model: model, size: size)
                        .frame(width: AVIndicatorLayout.badgeWidth(shown, size: size), height: geometry.bar)
                }
            }
            .frame(width: 520)
            Text("No notch on this Mac's screens: the island stays off.")
                .font(.system(size: 13))
                .foregroundStyle(.white.opacity(0.75))
        }
    }
}

// MARK: - Look

/// A Desk corner (the message, the clock and date, the meters) in the chosen fonts, sizes, ink
/// and glow.
struct DeskLookPreview: View {
    var live: DeskModel
    @State private var preview = DeskModel()
    @State private var samples: PreviewSamples = []
    @AppStorage("timeSize", store: .desk) private var timeSize = 112.0

    var body: some View {
        SettingsPreviewCard(kind: .look, label: "A Desk corner with the message, the clock and date, and the meters, in the chosen fonts, sizes, ink and glow.",
                            samples: samples) {
            VStack(alignment: .leading, spacing: DeskNowPlaying.columnSpacing) {
                DeskPiece(widget: .message, model: preview, alignment: .leading)
                DeskPiece(widget: .clock, model: preview, alignment: .leading)
                DeskPiece(widget: .meters, model: preview, alignment: .leading)
            }
            .padding(timeSize * 0.18)
        }
        .modifier(PreviewModelSync(preview: preview, live: live, samples: $samples))
    }
}

// MARK: - Meters

/// The Desk's meters with the saved warnings and hidden limits: a warning row red with its glow,
/// and the limits hidden or believed temporary named under them.
struct DeskMetersPreview: View {
    var live: DeskModel
    @State private var preview = DeskModel()
    @State private var samples: PreviewSamples = []
    @AppStorage("timeSize", store: .desk) private var timeSize = 112.0

    var body: some View {
        SettingsPreviewCard(kind: .meters, label: Self.spoken(preview), samples: samples) {
            VStack(alignment: .leading, spacing: 10) {
                DeskPiece(widget: .meters, model: preview, alignment: .leading)
                ForEach(Self.notes(preview), id: \.self) { note in
                    Text(note)
                        .font(.system(size: max(11, timeSize * 0.13)))
                        .foregroundStyle(.white.opacity(0.65))
                }
            }
            .padding(timeSize * 0.18)
        }
        .modifier(PreviewModelSync(preview: preview, live: live, samples: $samples))
    }

    /// "Hidden: Weekly — Opus" and "Temporary: …", from the model's reported, shown and
    /// temporary limits.
    static func notes(_ m: DeskModel) -> [String] {
        let shown = Set(m.meters.map(\.tier))
        let hidden = m.reportedTiers.filter { !shown.contains($0) }.map(\.label)
        let temporary = m.reportedTiers.filter { shown.contains($0) && m.temporaryTiers.contains($0) }.map(\.label)
        var out: [String] = []
        if !hidden.isEmpty { out.append("Hidden: " + hidden.joined(separator: ", ")) }
        if !temporary.isEmpty { out.append("Temporary: " + temporary.joined(separator: ", ")) }
        return out
    }

    static func spoken(_ m: DeskModel) -> String {
        let warned = m.meters.filter(\.warning).map(\.label)
        let rows = m.meters.map { "\($0.label) \($0.percent)%" }.joined(separator: ", ")
        let warning = warned.isEmpty ? "" : "; nearly full: " + warned.joined(separator: ", ")
        return "The Desk meters as they draw: \(rows.isEmpty ? "none showing" : rows)\(warning)."
    }
}

// MARK: - Message

/// Today's line with its effects; Replay writes a `{write}` line in again and sweeps a
/// `{shimmer}` line at once.
struct DeskMessagePreview: View {
    var live: DeskModel
    @State private var preview = DeskModel()
    @State private var samples: PreviewSamples = []
    @State private var replay = 0
    @AppStorage("messageSize", store: .desk) private var messageSize = 84.0
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        SettingsPreviewCard(kind: .message, label: Self.spoken(preview.message), samples: samples,
                            accessory: Self.replays(preview.message) ? AnyView(replayButton) : nil) {
            DeskPiece(widget: .message, model: preview, alignment: .leading, sweepFirst: replay > 0)
                .id(replay)
                .frame(maxWidth: messageSize * 9, alignment: .leading)
                .padding(messageSize * 0.2)
        }
        .modifier(PreviewModelSync(preview: preview, live: live, samples: $samples))
    }

    private var replayButton: some View {
        Button("Replay") { replay += 1 }
            .controlSize(.small)
            .disabled(reduceMotion)
            .help(reduceMotion ? "Reduce Motion is on: the line stays still" : "Plays the line's {write} and {shimmer} again")
    }

    /// A line with `{write}` or `{shimmer}` has something to replay.
    static func replays(_ raw: String?) -> Bool {
        guard let raw else { return false }
        let e = MessageMarkup.parse(raw).effects
        return e.write || e.shimmer
    }

    static func spoken(_ raw: String?) -> String {
        guard let raw else { return "No message today." }
        return "Today's message as the Desk draws it: \(MessageMarkup.parse(raw).text)"
    }
}

// MARK: - Now Playing

/// A wing playing a track, the same wing paused (with Next, or the stand-in when paused tracks
/// hide), the wing with nothing playing (the When nothing is playing choice), and the Desk line.
struct NowPlayingPreview: View {
    var live: DeskModel
    @State private var playing = DeskModel()
    @State private var paused = DeskModel()
    @State private var idle = DeskModel()
    @State private var samples: PreviewSamples = []
    @AppStorage(NotchContent.Place.left.key, store: .desk) private var left = NotchContent.Place.left.fallback
    @AppStorage(NowPlayingPrefs.hidePausedKey, store: .desk) private var hidePaused = false

    var body: some View {
        let g = NotchPreviewGeometry.current()
        let showing = Self.showing(left: left)
        SettingsPreviewCard(kind: .nowPlaying,
                            label: "A notch wing with a track playing, then paused, then with nothing playing, and the Desk's now playing line.",
                            samples: samples) {
            VStack(alignment: .leading, spacing: 8) {
                NowPlayingWingRow(title: "Playing", model: playing, geometry: g, showing: showing)
                NowPlayingWingRow(title: hidePaused ? "Paused (hidden)" : "Paused", model: paused, geometry: g, showing: showing)
                NowPlayingWingRow(title: "Nothing playing", model: idle, geometry: g, showing: showing)
                DeskPiece(widget: .nowPlaying, model: playing, alignment: .leading)
                    .padding(.leading, 120)
                    .padding(.top, 6)
            }
            .padding(10)
        }
        .onAppear(perform: sync)
        .onChange(of: PreviewLiveKey(live)) { _, _ in sync() }
        .onReceive(NotificationCenter.default.publisher(for: UserDefaults.didChangeNotification)
            .receive(on: RunLoop.main)) { _ in sync() }
    }

    /// Now playing in the left wing when it is placed there, else the right; the other wing plain.
    static func showing(left: NotchContent) -> NotchWingsView.Showing {
        left == .nowPlaying ? .init(left: .nowPlaying, right: .nothing) : .init(left: .nothing, right: .nowPlaying)
    }

    /// The three models, filled like the others; then each gets its track through Now Playing's
    /// own hide rule (NowPlayingPrefs.visible), so Hide while paused shows the stand-in.
    private func sync() {
        var fresh: PreviewSamples = []
        for m in [playing, paused, idle] {
            fresh.formUnion(SurfacePreviewData.fill(m, from: live, islandUp: true))
        }
        fresh.remove(.track)
        let prefs = NowPlayingPrefs.saved(in: .desk)
        let livePlaying = live.nowPlaying?.state == .playing ? live.nowPlaying : nil
        let livePaused = live.nowPlaying?.state == .paused ? live.nowPlaying : nil
        if livePlaying == nil || livePaused == nil { fresh.insert(.track) }
        Self.set(playing, prefs.visible(livePlaying ?? SurfacePreviewData.sampleTrack(.playing)))
        Self.set(paused, prefs.visible(livePaused ?? SurfacePreviewData.sampleTrack(.paused)))
        Self.set(idle, nil)
        if fresh != samples { samples = fresh }
    }

    /// Only a different track or state is assigned, so a sample's position bar doesn't restart
    /// on every settings change.
    private static func set(_ m: DeskModel, _ track: NowPlayingInfo?) {
        let old = m.nowPlaying
        guard old?.itemID != track?.itemID || old?.state != track?.state || old?.title != track?.title else { return }
        m.nowPlaying = track
    }
}

private struct NowPlayingWingRow: View {
    let title: String
    var model: DeskModel
    let geometry: NotchPreviewGeometry
    let showing: NotchWingsView.Showing

    var body: some View {
        let g = geometry
        HStack(spacing: 10) {
            Text(title)
                .font(.system(size: 12))
                .foregroundStyle(.white.opacity(0.75))
                .frame(width: 110, alignment: .trailing)
            ZStack(alignment: .top) {
                MenuBarStrip(height: g.bar)
                HardwareNotch(size: g.notch)
                NotchWingsView(model: model, notchWidth: g.notch.width, notchHeight: g.notch.height,
                               barHeight: g.bar, showing: showing)
                    .frame(width: g.wingsWidth, height: g.bar)
            }
            .frame(width: g.wingsWidth, height: g.bar)
        }
    }
}
