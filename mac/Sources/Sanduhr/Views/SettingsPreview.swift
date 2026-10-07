import SwiftUI
import AppKit
import Combine

/// Settings previews (item 68): a card at the top of each pane that controls something visible,
/// drawn by the same views that draw the real surface. The views read the same saved settings
/// the pane writes, so a change shows in the card within a frame; their data comes from a
/// preview model filled from the live one, with labeled sample data where there is none yet.
/// Nothing here captures the screen or shows another app.

/// Which preview a Settings section shows; state.yaml's `settings_preview`.
enum SettingsPreviewKind: String, CaseIterable {
    case notch, layout, look, meters, message, nowPlaying, widget, menuBar, integrations, mods

    /// The preview `section` shows, nil for a section without one (Themes has its gallery;
    /// Alerts, Accounts, Claude Usage, Updates and About draw nothing on screen).
    static func of(_ section: SettingsSection) -> SettingsPreviewKind? {
        switch section {
        case .notch: .notch
        case .deskLayout: .layout
        case .deskLook: .look
        case .deskMeters: .meters
        case .message: .message
        case .nowPlaying: .nowPlaying
        case .widgetLook, .pacing: .widget
        case .general: .menuBar
        case .integrations: .integrations
        case .mods: .mods
        case .alerts, .credentials, .usage, .themes, .updates, .about: nil
        }
    }

    /// The sample data a preview of this kind can show, for its label.
    var relevantSamples: PreviewSamples {
        switch self {
        case .notch: [.meters, .meetings, .message, .track, .watcher, .indicators]
        case .layout, .mods: []
        case .look: [.meters, .message]
        case .meters, .widget, .menuBar: [.meters]
        case .message: [.message, .specialDay]
        case .nowPlaying: [.track]
        case .integrations: [.watcher, .statusline]
        }
    }
}

/// The parts of a preview drawn from sample data, named on its "Sample" label.
struct PreviewSamples: OptionSet, Hashable {
    let rawValue: Int
    static let meters = PreviewSamples(rawValue: 1 << 0)
    static let meetings = PreviewSamples(rawValue: 1 << 1)
    static let message = PreviewSamples(rawValue: 1 << 2)
    static let track = PreviewSamples(rawValue: 1 << 3)
    static let watcher = PreviewSamples(rawValue: 1 << 4)
    static let indicators = PreviewSamples(rawValue: 1 << 5)
    static let statusline = PreviewSamples(rawValue: 1 << 6)
    static let specialDay = PreviewSamples(rawValue: 1 << 7)

    static let names: [(PreviewSamples, String)] = [
        (.meters, "meters"), (.meetings, "meetings"), (.message, "message"), (.track, "track"),
        (.watcher, "watcher"), (.indicators, "camera and mic"), (.statusline, "statusline input"),
        (.specialDay, "special day"),
    ]

    /// "Sample: meters, track", or nil when nothing shown is sample.
    var label: String? {
        let parts = Self.names.filter { contains($0.0) }.map(\.1)
        return parts.isEmpty ? nil : "Sample: " + parts.joined(separator: ", ")
    }
}

/// Scaled to fit, never cropped: the factor that fits `content` inside `room`, at most `maximum`.
enum PreviewScale {
    static let height: CGFloat = 160

    static func factor(content: CGSize, room: CGSize, maximum: CGFloat = 1) -> CGFloat {
        guard content.width > 0, content.height > 0, room.width > 0, room.height > 0 else { return 1 }
        return min(maximum, room.width / content.width, room.height / content.height)
    }
}

/// Draws `content` at its own size, scaled down (or up to `maximum`) to fit the room offered.
struct ScaledToFit<Content: View>: View {
    var maximum: CGFloat = 1
    var padding: CGFloat = 12
    @ViewBuilder let content: () -> Content
    @State private var size: CGSize = .zero

    var body: some View {
        GeometryReader { geo in
            let room = CGSize(width: max(0, geo.size.width - padding * 2), height: max(0, geo.size.height - padding * 2))
            content()
                .fixedSize()
                .onGeometryChange(for: CGSize.self) { $0.size } action: { size = $0 }
                .scaleEffect(PreviewScale.factor(content: size, room: room, maximum: maximum))
                .frame(width: geo.size.width, height: geo.size.height)
        }
    }
}

/// The dark wallpaper a preview draws on, as the Desk sits on a desktop.
struct PreviewWallpaper: View {
    var body: some View {
        LinearGradient(colors: [Color.hex("2a3446"), Color.hex("0d1118")],
                       startPoint: .topLeading, endPoint: .bottomTrailing)
    }
}

/// The card: about 160 points tall, as wide as the pane, the real surface scaled to fit on a
/// dark wallpaper, a "Sample" label naming what is made up, one VoiceOver sentence, and an
/// optional button (Replay, Test Glow) at its top right. The surface takes no clicks and reports
/// no frames (`isSurfacePreview`).
struct SettingsPreviewCard<Content: View>: View {
    let kind: SettingsPreviewKind
    /// What VoiceOver reads: one sentence.
    let label: String
    var samples: PreviewSamples = []
    var maximum: CGFloat = 1
    var accessory: AnyView?
    @ViewBuilder let content: () -> Content

    var body: some View {
        ZStack(alignment: .topTrailing) {
            ScaledToFit(maximum: maximum) { content() }
                .environment(\.isSurfacePreview, true)
                .allowsHitTesting(false)
                .accessibilityElement(children: .ignore)
                .accessibilityLabel(label)
                .accessibilityAddTraits(.isImage)
            HStack(spacing: 6) {
                if let text = samples.intersection(kind.relevantSamples).label {
                    Text(text)
                        .font(.caption2.weight(.semibold))
                        .foregroundStyle(.white.opacity(0.85))
                        .padding(.horizontal, 6)
                        .padding(.vertical, 2)
                        .background(Capsule().fill(Color.black.opacity(0.55)))
                        .help("Made-up data, shown until Sanduhr has your own")
                }
                accessory
            }
            .padding(8)
        }
        .frame(maxWidth: .infinity)
        .frame(height: PreviewScale.height)
        .background(PreviewWallpaper())
        .clipShape(RoundedRectangle(cornerRadius: 10, style: .continuous))
        .overlay(RoundedRectangle(cornerRadius: 10, style: .continuous).strokeBorder(Color.secondary.opacity(0.25)))
        .environment(\.colorScheme, .dark)
    }
}

extension View {
    /// The pane with its preview card on top, the card outside the pane's scrolling form.
    func withPreview<Card: View>(@ViewBuilder _ card: () -> Card) -> some View {
        VStack(spacing: 0) {
            card()
                .padding(.horizontal, 20)
                .padding(.top, 14)
                .padding(.bottom, 2)
            self
        }
    }
}

// MARK: - Sample data and the preview model

/// The sample data a preview shows before Sanduhr has the user's own, and the filling of a
/// preview DeskModel from the live one.
enum SurfacePreviewData {
    /// The Message card's sample date line (item 69), until the user has one today.
    static let sampleSpecialLine = "{ink:#ff7e5f,#feb47b,#ffd86f} happy birthday, Sam."

    /// A session at 42%, the weekly limit at 91% four days out (a warning with the default Meters
    /// settings) and a model limit at 18%.
    static func sampleUsage(now: Date = Date()) -> UsageResponse {
        let f = ISO8601DateFormatter()
        func tier(_ util: Double, in seconds: TimeInterval) -> TierUsage {
            TierUsage(utilization: util, resetsAt: f.string(from: now.addingTimeInterval(seconds)))
        }
        return UsageResponse(tiers: [
            .fiveHour: tier(42, in: 2 * 3600),
            .sevenDay: tier(91, in: 4 * 86400),
            .sevenDayOpus: tier(18, in: 4 * 86400),
        ])
    }

    /// A made-up track, playing or paused.
    static func sampleTrack(_ state: NowPlayingState, now: Date = Date()) -> NowPlayingInfo {
        NowPlayingInfo(title: "Night Drive", artist: "Sample Artist", album: nil, duration: 214,
                       elapsed: 71, elapsedAt: now, rate: state == .playing ? 1 : 0,
                       playing: state == .playing, bundleID: nil, pid: nil, itemID: "sample")
    }

    /// A made-up watcher: an agent running a test suite.
    static func sampleWatcher(now: Date = Date()) -> Watcher {
        Watcher(id: "a:sample", source: .agent, title: "Run the test suite", short: "tests", link: nil,
                total: 10, done: 6, note: "6 of 10 suites passed", state: .running,
                started: now.addingTimeInterval(-252), touched: now)
    }

    /// The camera and the microphone both in use, before the indicators' switches.
    static let sampleInUse = AVIndicators(camera: true, mic: true)

    /// Fills `preview` from `live`, sample data where `live` has none; returns what is sample.
    /// The numbers go through `DeskModel.update`, so the rows, the claude line and the notch's
    /// short meters come from the same functions as the Desk's, with the saved Meters settings.
    @MainActor @discardableResult
    static func fill(_ preview: DeskModel, from live: DeskModel, islandUp: Bool, sampleSpecialDay: Bool = false,
                     desk: UserDefaults = .desk, now: Date = Date()) -> PreviewSamples {
        var samples: PreviewSamples = []
        let usage = live.lastUsage
        if usage.usage != nil || usage.signInNeeded {
            preview.update(usage)
        } else {
            preview.update(DeskUsage(usage: sampleUsage(now: now), fetchedAt: now))
            samples.insert(.meters)
        }
        if !live.meetings.isEmpty || live.calendarNote != nil {
            if live.meetings.map(\.id) != preview.meetings.map(\.id) { preview.meetings = live.meetings }
            if preview.calendarNote != live.calendarNote { preview.calendarNote = live.calendarNote }
        } else {
            if preview.meetings.first?.id != "demo-1" { preview.meetings = DeskModel.demoMeetings(now: now) }
            if preview.calendarNote != nil { preview.calendarNote = nil }
            samples.insert(.meetings)
        }
        // Item 69: a date's own lines count as the user's message too.
        let hasOwn = live.message != nil || !live.specialMessages.isEmpty
        let message = hasOwn ? live.message : DeskModel.demoMessage
        if !hasOwn { samples.insert(.message) }
        if preview.message != message { preview.message = message }
        // The Message card (item 69) shows On special days with a sample date line until there is one.
        let sampleSpecial = sampleSpecialDay && live.specialMessages.isEmpty
        let specials = sampleSpecial ? [sampleSpecialLine] : live.specialMessages
        if sampleSpecial { samples.insert(.specialDay) }
        if preview.specialMessages != specials { preview.specialMessages = specials }
        preview.applySpecialSettings(mode: .saved(in: desk), seconds: MessageSpecialMode.savedSeconds(in: desk), now: now)
        let track = live.nowPlaying ?? sampleTrack(.playing, now: now)
        if live.nowPlaying == nil { samples.insert(.track) }
        if preview.nowPlaying?.itemID != track.itemID || preview.nowPlaying?.state != track.state
            || preview.nowPlaying?.title != track.title { preview.nowPlaying = track }
        let watchers = live.watchers.isEmpty ? [sampleWatcher(now: now)] : live.watchers
        if live.watchers.isEmpty { samples.insert(.watcher) }
        if preview.watchers.map(\.id) != watchers.map(\.id) || preview.watchers.map(\.state) != watchers.map(\.state) {
            preview.watchers = watchers
        }
        var shown = live.avIndicators
        if !shown.any {
            shown = AVIndicators.shown(cameraInUse: sampleInUse.camera, micInUse: sampleInUse.mic,
                                       // The sample camera stands for one whose light you can't see, so any mode but Never shows it.
                                       cameraSwitch: AVCameraDotMode.saved(in: desk).watches,
                                       micSwitch: desk.bool(forKey: AVIndicators.micKey))
            if shown.any { samples.insert(.indicators) }
        }
        if preview.avIndicators != shown { preview.avIndicators = shown }
        let spot = AVIndicatorPlacement.spot(shown, islandUp: islandUp, in: desk)
        if preview.avSpot != spot { preview.avSpot = spot }
        return samples
    }
}

/// What a preview follows on the live model: when any of it changes, the preview refills.
struct PreviewLiveKey: Equatable {
    var claudeLine: String?
    var compact: String?
    var meters: [DeskMeterRow]
    var signIn: Bool
    var message: String?
    var meetings: [String]
    var specials: [String]
    var note: String?
    var track: NowPlayingInfo?
    var watchers: [Watcher]
    var indicators: AVIndicators

    init(_ m: DeskModel) {
        claudeLine = m.claudeLine
        compact = m.claudeCompact
        meters = m.meters
        signIn = m.signInNeeded
        message = m.message
        specials = m.specialMessages
        meetings = m.meetings.map(\.id)
        note = m.calendarNote
        track = m.nowPlaying
        watchers = m.watchers
        indicators = m.avIndicators
    }
}

/// Keeps a preview DeskModel filled from the live one: on appear, whenever the live data
/// changes, and on any settings change (a Meters warning or a hidden limit restyles the rows at
/// once, as on the Desk).
struct PreviewModelSync: ViewModifier {
    let preview: DeskModel
    let live: DeskModel
    var islandUp = false
    /// The Message card: a sample date line until the user has one today (item 69).
    var sampleSpecialDay = false
    @Binding var samples: PreviewSamples

    func body(content: Content) -> some View {
        content
            .onAppear(perform: sync)
            .onChange(of: PreviewLiveKey(live)) { _, _ in sync() }
            .onChange(of: islandUp) { _, _ in sync() }
            .onReceive(NotificationCenter.default.publisher(for: UserDefaults.didChangeNotification)
                .receive(on: RunLoop.main)) { _ in sync() }
    }

    private func sync() {
        let fresh = SurfacePreviewData.fill(preview, from: live, islandUp: islandUp, sampleSpecialDay: sampleSpecialDay)
        if fresh != samples { samples = fresh }
    }
}
