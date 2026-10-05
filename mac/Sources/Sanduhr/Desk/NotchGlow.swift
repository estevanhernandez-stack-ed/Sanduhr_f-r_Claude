import AppKit
import SwiftUI

/// What can make the notch island glow: Sanduhr's own events, and Claude Code's hooks telling
/// it a session waits or finished (item 51, `ClaudeCodeEvent`).
enum NotchGlowEvent: Equatable {
    /// An alert went out, whatever its delivery (banner, Desk pulse or both).
    case alert
    /// A meeting starts within the minute. `id` is the meeting's key (see `NotchGlowRules.key`).
    case meetingSoon(id: String)
    /// The camera light came on.
    case cameraLightOn

    /// The switch that governs it, which is also the debug action's argument.
    enum Kind: String, CaseIterable {
        case alert, meeting, camera
        case claudeWaiting = "claude-waiting"
        case claudeDone = "claude-done"
    }

    var kind: Kind {
        switch self {
        case .alert: .alert
        case .meetingSoon: .meeting
        case .cameraLightOn: .camera
        }
    }
}

/// The switches in Settings, Desk, Notch, Glow. Every event's switch is off by default; "Not
/// while a terminal is in front" (Claude Code's events only) is on.
struct NotchGlowSwitches: Equatable {
    static let alertsKey = "notchGlowAlerts"
    static let meetingsKey = "notchGlowMeetings"
    static let cameraKey = "notchGlowCamera"
    static let claudeWaitingKey = "notchGlowClaudeWaiting"
    static let claudeDoneKey = "notchGlowClaudeDone"
    static let claudeSkipTerminalKey = "notchGlowClaudeSkipTerminal"

    var alerts = false
    var meetings = false
    var camera = false
    var claudeWaiting = false
    var claudeDone = false
    var claudeSkipTerminal = true

    init(alerts: Bool = false, meetings: Bool = false, camera: Bool = false,
         claudeWaiting: Bool = false, claudeDone: Bool = false, claudeSkipTerminal: Bool = true) {
        self.alerts = alerts
        self.meetings = meetings
        self.camera = camera
        self.claudeWaiting = claudeWaiting
        self.claudeDone = claudeDone
        self.claudeSkipTerminal = claudeSkipTerminal
    }

    init(_ d: DefaultsStore) {
        alerts = d.bool(forKey: Self.alertsKey)
        meetings = d.bool(forKey: Self.meetingsKey)
        camera = d.bool(forKey: Self.cameraKey)
        claudeWaiting = d.bool(forKey: Self.claudeWaitingKey)
        claudeDone = d.bool(forKey: Self.claudeDoneKey)
        claudeSkipTerminal = d.object(forKey: Self.claudeSkipTerminalKey) as? Bool ?? true
    }

    func isOn(_ kind: NotchGlowEvent.Kind) -> Bool {
        switch kind {
        case .alert: alerts
        case .meeting: meetings
        case .camera: camera
        case .claudeWaiting: claudeWaiting
        case .claudeDone: claudeDone
        }
    }
}

/// The meetings that already glowed today, so each glows once.
struct NotchGlowMemory: Equatable {
    var day: Date?
    var meetings: Set<String> = []
}

/// Which events glow. Pure: no windows, no defaults, so every rule is tested.
enum NotchGlowRules {
    /// A meeting glows when it starts within this many seconds…
    static let meetingLead: TimeInterval = 60
    /// …or started no longer ago than this (a check that lands just after the start still counts).
    static let meetingGrace: TimeInterval = 30

    /// One meeting occurrence: a recurring event keeps its id, so the start tells the days apart.
    static func key(id: String, start: Date) -> String {
        "\(id)@\(Int(start.timeIntervalSince1970))"
    }

    /// True when `start` falls in the minute-before window around `now`.
    static func isSoon(start: Date, now: Date) -> Bool {
        let until = start.timeIntervalSince(now)
        return until <= meetingLead && until >= -meetingGrace
    }

    /// Whether `event` glows. A meeting glows once per key per day: the memory is cleared when
    /// the day changes, and only a meeting that glows is remembered (one that comes due while
    /// its switch is off can still glow if the switch goes on within its minute).
    static func decide(_ event: NotchGlowEvent, switches: NotchGlowSwitches, memory: inout NotchGlowMemory,
                       now: Date, calendar: Calendar = .current) -> Bool {
        let today = calendar.startOfDay(for: now)
        if memory.day != today { memory = NotchGlowMemory(day: today) }
        guard switches.isOn(event.kind) else { return false }
        guard case .meetingSoon(let id) = event else { return true }
        return memory.meetings.insert(id).inserted
    }

    /// The meetings check, run every 15 seconds: one glow when any meeting not yet glowed is
    /// due, however many start together (each is remembered).
    static func meetingsDue(_ meetings: [(id: String, start: Date)], switches: NotchGlowSwitches,
                            memory: inout NotchGlowMemory, now: Date, calendar: Calendar = .current) -> Bool {
        var glow = false
        for m in meetings where isSoon(start: m.start, now: now) {
            let event = NotchGlowEvent.meetingSoon(id: key(id: m.id, start: m.start))
            if decide(event, switches: switches, memory: &memory, now: now, calendar: calendar) { glow = true }
        }
        return glow
    }
}

/// Where the glow's window sits: the wings window (menu-bar tall, the island's widest reach
/// plus its flare on each side) grown by the glow's reach left, right and below, and tall
/// enough for the strip under the camera.
enum NotchGlowLayout {
    /// How far the glow reaches past the island's edge.
    static let reach: CGFloat = 16

    /// The island's height: the strip's bottom when there is one, else the wings'.
    static func islandHeight(notchHeight: CGFloat, barHeight: CGFloat, chin: Double) -> CGFloat {
        chin > 0 ? max(barHeight, notchHeight + CGFloat(chin)) : barHeight
    }

    /// The window frame in AppKit screen coordinates (bottom-left origin), flush with the top.
    static func frame(wings: CGRect, islandHeight: CGFloat) -> CGRect {
        let height = islandHeight + reach * 2
        return CGRect(x: wings.minX - reach, y: wings.maxY - height,
                      width: wings.width + reach * 2, height: height)
    }

    /// The top band the glow leaves clear, in points: the screen edge and the camera housing
    /// show no light, so a glow running into them reads as broken.
    static let topClear: CGFloat = 2
    static let topRamp: CGFloat = 6

    /// Where the glow fades in, as fractions of the glow window's height from the top: clear until
    /// `start`, full from `end`. Only the last few points at the screen edge, so the sides glow
    /// the wings' full height.
    static func topFade(barHeight: CGFloat, islandHeight: CGFloat) -> (start: CGFloat, end: CGFloat) {
        let total = islandHeight + reach * 2
        guard total > 0 else { return (0, 0) }
        let start = min(1, topClear / total)
        let end = min(1, max(start, topRamp / total))
        return (start, end)
    }

    /// The strip's height as the glow outlines it: the strip only while it shows. An app window
    /// over it hides it (it lives in the Desk window, below app windows), and a glow traced
    /// around a strip nobody can see reads as a glow around nothing; then the glow follows the
    /// wings alone.
    static func glowChin(_ chin: Double, stripVisible: Bool) -> Double { stripVisible ? chin : 0 }

    /// The strip under the camera, in points from the screen's top-left corner: the island's
    /// width (the notch and both wings), from the wings' bottom to the strip's. Nil when nothing
    /// of the strip hangs below the wings.
    static func stripRect(notch: CGRect, barHeight: CGFloat, chin: Double,
                          left: CGFloat, right: CGFloat) -> CGRect? {
        guard chin > 0 else { return nil }
        let bottom = notch.minY + notch.height + CGFloat(chin)
        guard bottom > barHeight else { return nil }
        return CGRect(x: notch.minX - left, y: barHeight,
                      width: notch.width + left + right, height: bottom - barHeight)
    }

    /// A rect in points from `screen`'s top-left corner, in CoreGraphics global coordinates (top-left
    /// origin at the primary display, which is `primaryHeight` tall), the ones window bounds use.
    /// `screen` is the AppKit frame (bottom-left origin).
    static func global(_ local: CGRect, screen: CGRect, primaryHeight: CGFloat) -> CGRect {
        CGRect(x: screen.minX + local.minX, y: primaryHeight - screen.maxY + local.minY,
               width: local.width, height: local.height)
    }

    /// The island's corner radius, as the strip or the wings draw it.
    static func radius(barHeight: CGFloat, chin: Double) -> CGFloat {
        chin > 0 ? min(16, CGFloat(chin) * 0.7) : min(10, barHeight * 0.3)
    }

    // MARK: The plain notch

    /// What a glow outlines: the extended island while Desk draws it, else the hardware notch
    /// itself when a screen has one (whether or not Desk runs), else, for a glow that asks for it
    /// (Claude Code's), a notch-wide spot at the top center as the camera light uses, else nothing.
    static func shape(deskRunning: Bool, notchOn: Bool, hasIsland: Bool, hasNotch: Bool,
                      topFallback: Bool = false) -> NotchGlowShape {
        if deskRunning, notchOn, hasIsland { return .island }
        if hasNotch { return .plain }
        return topFallback ? .top : .none
    }

    /// The spot a top-center glow outlines on a screen without a notch, in points from the
    /// screen's top-left corner: the camera light's notch-wide core, the menu bar's height.
    static func topSpot(screenWidth: CGFloat, barHeight: CGFloat) -> CGRect {
        let w = CameraLightLayout.noNotchWidth
        return CGRect(x: (screenWidth - w) / 2, y: 0, width: w, height: max(barHeight, 1))
    }

    /// The plain notch's glow window, flush with the screen's top: the notch (points from the
    /// screen's top-left corner, as `NSScreen.cameraNotch` gives it) grown by the reach left,
    /// right and below, in AppKit screen coordinates.
    static func plainFrame(screen: CGRect, notch: CGRect) -> CGRect {
        let height = notch.height + reach * 2
        return CGRect(x: screen.minX + notch.minX - reach, y: screen.maxY - height,
                      width: notch.width + reach * 2, height: height)
    }

    /// The part cut out of the plain glow: exactly the hardware notch, so the halo starts at its edge.
    static func plainCutout(notch: CGRect) -> CGSize { notch.size }

    /// The hardware notch's bottom corner radius, near enough to hug it.
    static func plainRadius(notchHeight: CGFloat) -> CGFloat { min(8, notchHeight * 0.25) }
}

/// One on-screen window as `CGWindowListCopyWindowInfo` reports it: only what is readable without
/// Screen Recording permission (layer, alpha, bounds; never the name).
struct NotchCoverWindow: Equatable {
    let layer: Int
    let alpha: Double
    let bounds: CGRect

    init(layer: Int, alpha: Double = 1, bounds: CGRect) {
        self.layer = layer
        self.alpha = alpha
        self.bounds = bounds
    }

    /// From one entry of the window list; nil when its layer or bounds are missing.
    init?(info: [String: Any]) {
        guard let layer = info[kCGWindowLayer as String] as? Int,
              let b = info[kCGWindowBounds as String] as? [String: Any],
              let bounds = CGRect(dictionaryRepresentation: b as CFDictionary) else { return nil }
        self.layer = layer
        self.alpha = (info[kCGWindowAlpha as String] as? Double) ?? 1
        self.bounds = bounds
    }
}

/// Whether an app window hides the strip under the camera. Pure: the controller hands it the
/// window list.
enum NotchStripCover {
    /// The layers that count: normal windows (0) up to, not including, the menu bar's (24), so
    /// floating panels and modal dialogs count. Desk sits at -1, so all of them are above it.
    /// The menu bar, the wings, the glow and the camera light sit higher and never count, nor do
    /// overlays that float over everything.
    static let layers = 0..<24

    /// True when a visible window in `layers` overlaps the strip (`strip` and the bounds both in
    /// CoreGraphics global coordinates). A window that only touches its edge does not count.
    static func isCovered(strip: CGRect, by windows: [NotchCoverWindow]) -> Bool {
        let inner = strip.insetBy(dx: 1, dy: 1)
        guard !inner.isEmpty else { return false }
        return windows.contains { w in
            layers.contains(w.layer) && w.alpha > 0 && w.bounds.intersects(inner)
        }
    }
}

/// What the last glow outlined (smoke's glow_shape).
enum NotchGlowShape: String {
    /// The extended island: wings, and the strip under the camera when there is one.
    case island
    /// The hardware notch alone: the island is off, or Desk is not running.
    case plain
    /// No notch: a notch-wide spot at the top center, for Claude Code's glows (item 51).
    case top
    /// No notched screen (or no glow yet): nothing drawn.
    case none
}

/// The glow's look, whatever it outlines: an outline `width` by `height` hanging from the top,
/// filled in the notch ink and blurred, with the outline itself cut out, so only the halo outside
/// its edge shows and whatever is inside (the island and its text, or the camera) stays as it is.
/// The top few points fade out, so no light runs along the screen edge.
struct NotchHaloView: View {
    let width: CGFloat
    let height: CGFloat
    let radius: CGFloat
    /// Horizontal offset of the outline from the window's center (wings of unequal width).
    var offsetX: CGFloat = 0
    @AppStorage("notchTextColor", store: .desk) private var textColor = "ffffff"

    var body: some View {
        let reach = NotchGlowLayout.reach
        ZStack(alignment: .top) {
            // No top flares: blurred, they bled along the screen edge and broke up beside the camera.
            IslandShape(flare: 0, radius: radius + 4)
                .fill(LinearGradient.ink(textColor))
                .frame(width: width + 8, height: height + 4)
                .blur(radius: reach * 0.55)
                .opacity(0.75)
            IslandShape(flare: 8, radius: radius)
                .fill(Color.black)
                .frame(width: width, height: height)
                .blendMode(.destinationOut)
        }
        .compositingGroup()
        .offset(x: offsetX)
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
        .mask {
            let fade = NotchGlowLayout.topFade(barHeight: height, islandHeight: height)
            LinearGradient(stops: [.init(color: .clear, location: 0),
                                   .init(color: .clear, location: fade.start),
                                   .init(color: .white, location: fade.end)],
                           startPoint: .top, endPoint: .bottom)
        }
        .allowsHitTesting(false)
    }
}

/// The glow around the extended island: the halo at the island's size as drawn.
struct NotchGlowView: View {
    var model: DeskModel
    let notchWidth: CGFloat
    let notchHeight: CGFloat
    let barHeight: CGFloat
    /// False when an app window hides the strip under the camera: the halo hugs the wings alone.
    var stripVisible = true
    @AppStorage("notchWings", store: .desk) private var wings = 36.0
    @AppStorage("notchChin", store: .desk) private var savedChin = 26.0
    @AppStorage("notchText", store: .desk) private var showText = true
    @AppStorage("font", store: .desk) private var savedFont: String?
    /// The Desk font as drawn: EsteFont 26 unless a font was picked (DeskFont, item 58).
    private var font: String { DeskFont.resolve(saved: savedFont) }
    @AppStorage(NotchContent.Place.left.key, store: .desk) private var leftContent = NotchContent.Place.left.fallback
    @AppStorage(NotchContent.Place.right.key, store: .desk) private var rightContent = NotchContent.Place.right.fallback

    var body: some View {
        // The same widths the wings and the strip use, so the halo hugs the island as drawn.
        let w = NotchWingsView.layout(model: model, now: Date(), wings: wings, showText: showText,
                                      left: leftContent, right: rightContent,
                                      font: font, notchHeight: notchHeight)
        let chin = NotchGlowLayout.glowChin(savedChin, stripVisible: stripVisible)
        NotchHaloView(width: notchWidth + w.left + w.right,
                      height: NotchGlowLayout.islandHeight(notchHeight: notchHeight, barHeight: barHeight, chin: chin),
                      radius: NotchGlowLayout.radius(barHeight: barHeight, chin: chin),
                      offsetX: (w.right - w.left) / 2)
    }
}

/// Holds the glow and names it for smoke's tree.yaml; never takes a click.
private final class NotchGlowContainer: NSView {
    override func isAccessibilityElement() -> Bool { true }
    override func accessibilityRole() -> NSAccessibility.Role? { .image }
    override func accessibilityLabel() -> String? { "Notch glow" }
    override func hitTest(_ point: NSPoint) -> NSView? { nil }
}

/// The notch glows for Sanduhr's events (Settings, Desk, Notch, Glow): an alert, a meeting a
/// minute out, the camera light coming on, Claude Code waiting or finished (item 51), and every
/// Desk pulse (forced). A gentle halo in the
/// notch ink, about three seconds, in its own click-through window: around the island while Desk
/// runs with the island on, else around the plain hardware notch, whenever Sanduhr runs on a Mac
/// with a notched screen.
@MainActor
final class NotchGlowController {
    static let shared = NotchGlowController()
    static let fadeIn: TimeInterval = 1.0
    static let hold: TimeInterval = 0.8
    static let fadeOut: TimeInterval = 1.2

    private(set) var window: NSWindow?
    /// Glows fired so far, drawn or not (smoke's glow_count).
    private(set) var count = 0
    /// What the last glow outlined (smoke's glow_shape); `.none` until one is drawn.
    private(set) var lastShape = NotchGlowShape.none
    private var memory = NotchGlowMemory()
    private var meetingTimer: Timer?
    /// Bumped on every glow, so an older glow's fade-out cannot end a newer one.
    private var generation = 0
    /// Claude Code's glows so far, for the rate limit.
    private var claudeLimiter = ClaudeCodeGlowLimiter()

    var switches: NotchGlowSwitches { NotchGlowSwitches(UserDefaults.desk) }

    private init() {}

    /// At launch and when the meetings switch flips: check the meetings every 15 seconds while
    /// it is on, so a glow lands 45 to 60 seconds before the start.
    func apply() {
        let wanted = switches.meetings
        if wanted, meetingTimer == nil {
            meetingTimer = Timer.scheduledTimer(withTimeInterval: 15, repeats: true) { _ in
                MainActor.assumeIsolated { NotchGlowController.shared.checkMeetings() }
            }
            checkMeetings()
        } else if !wanted {
            meetingTimer?.invalidate(); meetingTimer = nil
        }
    }

    /// An event happened: glow when its switch is on.
    func event(_ event: NotchGlowEvent) {
        if NotchGlowRules.decide(event, switches: switches, memory: &memory, now: Date()) { fire() }
    }

    /// Claude Code's hook opened `sanduhr://claude-code?event=…` (item 51): glow when its switch
    /// is on, no terminal is in front (when that option is on) and the rate limit allows. Screens
    /// without a notch get the glow at the top center.
    func claudeCode(_ event: ClaudeCodeEvent) {
        let front = NSWorkspace.shared.frontmostApplication?.bundleIdentifier
        guard ClaudeCodeGlowRules.decide(event, switches: switches, frontmost: front,
                                         limiter: &claudeLimiter, now: Date()) else { return }
        fire(topFallback: true)
    }

    private func checkMeetings() {
        let desk = DeskController.shared
        guard desk.running else { return }
        let meetings = desk.model.meetings.map { (id: $0.id, start: $0.start) }
        if NotchGlowRules.meetingsDue(meetings, switches: switches, memory: &memory, now: Date()) { fire() }
    }

    /// Glows once, whatever the switches (a Desk pulse and the debug action call this directly).
    /// With `topFallback`, a Mac with no notched screen glows at the top center of the main
    /// screen instead of not at all.
    func fire(topFallback: Bool = false) {
        count += 1
        let desk = DeskController.shared
        let notchOn = UserDefaults.desk.bool(forKey: DeskController.notchKey)
        let screen = Self.notchedScreen()
        let shape = NotchGlowLayout.shape(deskRunning: desk.running, notchOn: notchOn,
                                          hasIsland: desk.wingsWindow != nil && desk.model.notchRect != nil,
                                          hasNotch: screen != nil, topFallback: topFallback)
        let frame: CGRect
        let content: AnyView
        switch shape {
        case .island:
            guard let wings = desk.wingsWindow, let notch = desk.model.notchRect else { return }
            let barHeight = wings.frame.height
            let savedChin = UserDefaults.desk.object(forKey: "notchChin") as? Double ?? 26
            let visible = Self.stripVisible(notch: notch, barHeight: barHeight, chin: savedChin,
                                            model: desk.model, screen: wings.screen)
            let chin = NotchGlowLayout.glowChin(savedChin, stripVisible: visible)
            let island = NotchGlowLayout.islandHeight(notchHeight: notch.height, barHeight: barHeight, chin: chin)
            frame = NotchGlowLayout.frame(wings: wings.frame, islandHeight: island)
            content = AnyView(NotchGlowView(model: desk.model, notchWidth: notch.width,
                                            notchHeight: notch.height, barHeight: barHeight,
                                            stripVisible: visible))
        case .plain:
            guard let screen, let notch = screen.cameraNotch else { return }
            frame = NotchGlowLayout.plainFrame(screen: screen.frame, notch: notch)
            let cut = NotchGlowLayout.plainCutout(notch: notch)
            content = AnyView(NotchHaloView(width: cut.width, height: cut.height,
                                            radius: NotchGlowLayout.plainRadius(notchHeight: notch.height)))
        case .top:
            guard let main = NSScreen.main ?? NSScreen.screens.first else { return }
            // A hidden menu bar leaves no inset: use the bar's usual thickness.
            let inset = main.frame.maxY - main.visibleFrame.maxY
            let bar = inset > 0 ? inset : NSStatusBar.system.thickness
            let spot = NotchGlowLayout.topSpot(screenWidth: main.frame.width, barHeight: bar)
            frame = NotchGlowLayout.plainFrame(screen: main.frame, notch: spot)
            content = AnyView(NotchHaloView(width: spot.width, height: spot.height,
                                            radius: NotchGlowLayout.plainRadius(notchHeight: spot.height)))
        case .none:
            return
        }
        lastShape = shape
        let w = window ?? makeWindow()
        window = w
        let container = NotchGlowContainer(frame: CGRect(origin: .zero, size: frame.size))
        let host = FirstClickHostingView(rootView: content)
        host.frame = container.bounds
        host.autoresizingMask = [.width, .height]
        container.addSubview(host)
        w.contentView = container
        w.setFrame(frame, display: true)
        w.orderFrontRegardless()

        generation += 1
        let mine = generation
        NSAnimationContext.runAnimationGroup({ ctx in
            ctx.duration = Self.fadeIn
            ctx.timingFunction = CAMediaTimingFunction(name: .easeOut)
            w.animator().alphaValue = 1
        }, completionHandler: { [weak self] in
            MainActor.assumeIsolated {
                guard let self, self.generation == mine else { return }
                DispatchQueue.main.asyncAfter(deadline: .now() + Self.hold) {
                    MainActor.assumeIsolated { self.fadeOut(w, mine) }
                }
            }
        })
    }

    private func fadeOut(_ w: NSWindow, _ mine: Int) {
        guard generation == mine else { return }
        NSAnimationContext.runAnimationGroup({ ctx in
            ctx.duration = Self.fadeOut
            ctx.timingFunction = CAMediaTimingFunction(name: .easeInEaseOut)
            w.animator().alphaValue = 0
        }, completionHandler: { [weak self] in
            MainActor.assumeIsolated {
                guard let self, self.generation == mine else { return }
                w.orderOut(nil)
            }
        })
    }

    /// Whether the strip under the camera shows, checked as the glow starts (a glow lasts three
    /// seconds; a window moved during it keeps the outline it started with). The on-screen window
    /// list's layers and bounds need no Screen Recording permission; names are never read. When
    /// the list cannot be read the strip counts as visible, as before.
    private static func stripVisible(notch: CGRect, barHeight: CGFloat, chin: Double,
                                     model: DeskModel, screen: NSScreen?) -> Bool {
        let d = UserDefaults.desk
        let w = NotchWingsView.layout(
            model: model, now: Date(), wings: d.object(forKey: "notchWings") as? Double ?? 36,
            showText: d.object(forKey: "notchText") as? Bool ?? true,
            left: d.string(forKey: NotchContent.Place.left.key).flatMap(NotchContent.init(rawValue:))
                ?? NotchContent.Place.left.fallback,
            right: d.string(forKey: NotchContent.Place.right.key).flatMap(NotchContent.init(rawValue:))
                ?? NotchContent.Place.right.fallback,
            font: DeskFont.resolve(d), notchHeight: notch.height)
        guard let screen, let primary = NSScreen.screens.first,
              let local = NotchGlowLayout.stripRect(notch: notch, barHeight: barHeight, chin: chin,
                                                    left: w.left, right: w.right),
              let list = CGWindowListCopyWindowInfo([.optionOnScreenOnly, .excludeDesktopElements],
                                                    kCGNullWindowID) as? [[String: Any]] else { return true }
        let strip = NotchGlowLayout.global(local, screen: screen.frame, primaryHeight: primary.frame.height)
        return !NotchStripCover.isCovered(strip: strip, by: list.compactMap(NotchCoverWindow.init(info:)))
    }

    /// The screen with the camera notch, nil when none has one (an external display alone).
    private static func notchedScreen() -> NSScreen? {
        NSScreen.screens.first { $0.cameraNotch != nil }
    }

    private func makeWindow() -> NSWindow {
        let w = NSWindow(contentRect: .zero, styleMask: [.borderless], backing: .buffered, defer: false)
        w.isReleasedWhenClosed = false
        // Over the wings (.popUpMenu) and the camera light (+1), so a camera glow shows too; the
        // island is cut out of it, so the wings' text and their click stay as they are.
        w.level = NSWindow.Level(rawValue: NSWindow.Level.popUpMenu.rawValue + 2)
        w.collectionBehavior = [.canJoinAllSpaces, .stationary, .ignoresCycle, .fullScreenAuxiliary]
        w.isOpaque = false
        w.backgroundColor = .clear
        w.hasShadow = false
        w.ignoresMouseEvents = true
        w.alphaValue = 0
        return w
    }
}
