import AppKit
import SwiftUI

/// What can make the notch island glow: Sanduhr's own events, never another app's.
enum NotchGlowEvent: Equatable {
    /// An alert went out, whatever its delivery (banner, Desk pulse or both).
    case alert
    /// A meeting starts within the minute. `id` is the meeting's key (see `NotchGlowRules.key`).
    case meetingSoon(id: String)
    /// The camera light came on.
    case cameraLightOn

    /// The switch that governs it, which is also the debug action's argument.
    enum Kind: String, CaseIterable { case alert, meeting, camera }

    var kind: Kind {
        switch self {
        case .alert: .alert
        case .meetingSoon: .meeting
        case .cameraLightOn: .camera
        }
    }
}

/// The three switches in Settings, Desk, Notch, Glow. All off by default.
struct NotchGlowSwitches: Equatable {
    static let alertsKey = "notchGlowAlerts"
    static let meetingsKey = "notchGlowMeetings"
    static let cameraKey = "notchGlowCamera"

    var alerts = false
    var meetings = false
    var camera = false

    init(alerts: Bool = false, meetings: Bool = false, camera: Bool = false) {
        self.alerts = alerts
        self.meetings = meetings
        self.camera = camera
    }

    init(_ d: DefaultsStore) {
        alerts = d.bool(forKey: Self.alertsKey)
        meetings = d.bool(forKey: Self.meetingsKey)
        camera = d.bool(forKey: Self.cameraKey)
    }

    func isOn(_ kind: NotchGlowEvent.Kind) -> Bool {
        switch kind {
        case .alert: alerts
        case .meeting: meetings
        case .camera: camera
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

    /// The island's corner radius, as the strip or the wings draw it.
    /// Where the glow fades in, as fractions of the glow window's height from the top: clear
    /// until `start`, full from `end`. The top of the island meets the screen edge and the camera
    /// housing, where no light can show, so a glow there reads as broken; it stays clear across
    /// the upper part of the menu bar and comes in by its bottom edge, leaving a U around the
    /// island's sides and bottom.
    static func topFade(barHeight: CGFloat, islandHeight: CGFloat) -> (start: CGFloat, end: CGFloat) {
        let total = islandHeight + reach * 2
        guard total > 0 else { return (0, 0) }
        let start = min(1, barHeight * 0.45 / total)
        let end = min(1, max(start, barHeight / total))
        return (start, end)
    }

    static func radius(barHeight: CGFloat, chin: Double) -> CGFloat {
        chin > 0 ? min(16, CGFloat(chin) * 0.7) : min(10, barHeight * 0.3)
    }
}

/// The glow: the island's outline filled in the notch ink and blurred, with the island itself
/// cut out, so only the halo outside its edge shows and the island and its text stay as they are.
struct NotchGlowView: View {
    var model: DeskModel
    let notchWidth: CGFloat
    let notchHeight: CGFloat
    let barHeight: CGFloat
    @AppStorage("notchWings", store: .desk) private var wings = 36.0
    @AppStorage("notchChin", store: .desk) private var chin = 26.0
    @AppStorage("notchText", store: .desk) private var showText = true
    @AppStorage("notchTextColor", store: .desk) private var textColor = "ffffff"
    @AppStorage("font", store: .desk) private var font = ""
    @AppStorage(NotchContent.Place.left.key, store: .desk) private var leftContent = NotchContent.Place.left.fallback
    @AppStorage(NotchContent.Place.right.key, store: .desk) private var rightContent = NotchContent.Place.right.fallback

    var body: some View {
        // The same widths the wings and the strip use, so the halo hugs the island as drawn.
        let w = NotchWingsView.layout(model: model, now: Date(), wings: wings, showText: showText,
                                      left: leftContent, right: rightContent,
                                      font: font, notchHeight: notchHeight)
        let width = notchWidth + w.left + w.right
        let height = NotchGlowLayout.islandHeight(notchHeight: notchHeight, barHeight: barHeight, chin: chin)
        let radius = NotchGlowLayout.radius(barHeight: barHeight, chin: chin)
        let reach = NotchGlowLayout.reach
        ZStack(alignment: .top) {
            IslandShape(flare: 8, radius: radius + 4)
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
        .offset(x: (w.right - w.left) / 2)
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
        .mask {
            let fade = NotchGlowLayout.topFade(barHeight: barHeight, islandHeight: height)
            LinearGradient(stops: [.init(color: .clear, location: 0),
                                   .init(color: .clear, location: fade.start),
                                   .init(color: .white, location: fade.end)],
                           startPoint: .top, endPoint: .bottom)
        }
        .allowsHitTesting(false)
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
/// minute out, the camera light coming on. A gentle halo in the notch ink around the island,
/// about three seconds, in its own click-through window over the wings. Only while Desk runs
/// with the island on, on a screen with a notch.
@MainActor
final class NotchGlowController {
    static let shared = NotchGlowController()
    static let fadeIn: TimeInterval = 1.0
    static let hold: TimeInterval = 0.8
    static let fadeOut: TimeInterval = 1.2

    private(set) var window: NSWindow?
    /// Glows fired so far, drawn or not (smoke's glow_count).
    private(set) var count = 0
    private var memory = NotchGlowMemory()
    private var meetingTimer: Timer?
    /// Bumped on every glow, so an older glow's fade-out cannot end a newer one.
    private var generation = 0

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

    private func checkMeetings() {
        let desk = DeskController.shared
        guard desk.running else { return }
        let meetings = desk.model.meetings.map { (id: $0.id, start: $0.start) }
        if NotchGlowRules.meetingsDue(meetings, switches: switches, memory: &memory, now: Date()) { fire() }
    }

    /// Glows once, whatever the switches (the debug action calls this directly).
    func fire() {
        count += 1
        let desk = DeskController.shared
        guard desk.running, UserDefaults.desk.bool(forKey: DeskController.notchKey),
              let wings = desk.wingsWindow, let notch = desk.model.notchRect else { return }
        let barHeight = wings.frame.height
        let chin = UserDefaults.desk.object(forKey: "notchChin") as? Double ?? 26
        let island = NotchGlowLayout.islandHeight(notchHeight: notch.height, barHeight: barHeight, chin: chin)
        let frame = NotchGlowLayout.frame(wings: wings.frame, islandHeight: island)
        let w = window ?? makeWindow()
        window = w
        let container = NotchGlowContainer(frame: CGRect(origin: .zero, size: frame.size))
        let host = FirstClickHostingView(rootView: NotchGlowView(
            model: desk.model, notchWidth: notch.width, notchHeight: notch.height, barHeight: barHeight))
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
