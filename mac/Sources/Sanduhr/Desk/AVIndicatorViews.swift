import AppKit
import IOKit
import SwiftUI

/// How the indicators draw. The camera's red dot is the only red dot in Sanduhr: a failed watcher
/// draws a red triangle instead (WatcherLook.mark), so a red dot always means the camera.
enum AVIndicatorLook {
    static let camera = Color.hex("ff3b30")
    /// Orange, as macOS marks the microphone in the menu bar.
    static let mic = Color.hex("ff9f0a")
}

/// The indicators' motion. Coming and going is an opacity fade (and the island's room grows or
/// shrinks with it); the dot breathes between `breathLow` and full on a smooth cosine. With Reduce
/// Motion there is no breath and the fades are instant. Pure.
enum AVIndicatorMotion {
    static let fade: TimeInterval = 0.25
    static let breathPeriod: TimeInterval = 1.6
    static let breathLow = 0.55

    /// The fade for coming and going: nil (instant) with Reduce Motion.
    static func fadeAnimation(reduceMotion: Bool) -> Animation? {
        reduceMotion ? nil : .easeInOut(duration: fade)
    }

    /// The dot's opacity at `seconds` into the breath: 1 at the start of each period, `breathLow`
    /// halfway, eased in and out at both ends (a cosine, so it never jumps).
    static func breath(at seconds: TimeInterval) -> Double {
        let phase = seconds.truncatingRemainder(dividingBy: breathPeriod) / breathPeriod
        return breathLow + (1 - breathLow) * (0.5 + 0.5 * cos(2 * Double.pi * phase))
    }
}

/// The red recording dot. It breathes gently while the pulse switch is on, never with Reduce
/// Motion. The breath is computed from the frame's time (TimelineView .animation), not a state
/// that flips, so it moves smoothly and leaves nothing behind when the dot goes.
struct AVCameraDot: View {
    let diameter: CGFloat
    let pulse: Bool
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        let dot = Circle()
            .fill(AVIndicatorLook.camera)
            .frame(width: diameter, height: diameter)
        if pulse && !reduceMotion {
            TimelineView(.animation(minimumInterval: 1.0 / 30, paused: false)) { context in
                dot.opacity(AVIndicatorMotion.breath(at: context.date.timeIntervalSinceReferenceDate))
            }
        } else {
            dot
        }
    }
}

/// The dot and the mic glyph for what shows, in that order; nothing for nothing.
struct AVIndicatorView: View {
    let shown: AVIndicators
    let size: CGFloat
    @AppStorage(AVIndicators.pulseKey, store: .desk) private var pulse = true

    var body: some View {
        HStack(spacing: AVIndicatorLayout.gap(size)) {
            if shown.camera {
                AVCameraDot(diameter: AVIndicatorLayout.dot(size), pulse: pulse)
            }
            if shown.mic {
                Image(systemName: "mic.fill")
                    .font(.system(size: size * 0.8, weight: .semibold))
                    .foregroundStyle(AVIndicatorLook.mic)
                    .frame(width: AVIndicatorLayout.mic(size))
            }
        }
        .accessibilityElement(children: .ignore)
        .accessibilityLabel(shown.spoken)
    }
}

/// The slot beside the camera on the island (item 67). Always in the island's row, so it is never
/// inserted or removed: its width is the room the layout gives it (0 when nothing shows) and its
/// content fades with it, both in the controller's fade transaction. At width 0 it is clipped
/// away, transparent and takes no click, so the wing sits exactly where it was.
struct AVBesideSlot: View {
    var model: DeskModel
    let side: AVIndicatorSide
    let size: CGFloat
    let width: CGFloat

    var body: some View {
        let showing = width > 0 && model.avIndicators.any
        AVIndicatorView(shown: model.avDrawn, size: size)
            .fixedSize()
            .frame(width: max(0, width - AVIndicatorLayout.spacing), alignment: side == .left ? .trailing : .leading)
            .frame(maxHeight: .infinity)
            .padding(side == .left ? .leading : .trailing, width > 0 ? AVIndicatorLayout.spacing : 0)
            .frame(width: width)
            .clipped()
            .opacity(showing ? 1 : 0)
            .background(Color.black.opacity(showing ? DeskPointerMenu.hitPlateOpacity : 0))
            .contentShape(Rectangle())
            .onTapGesture { AVIndicatorMenu.popUpAtPointer(model.avIndicators) }
            .contextMenu { AVIndicatorMenuItems(shown: model.avIndicators) }
            .help("Camera and microphone in use. Click for more.")
            .allowsHitTesting(showing)
            .accessibilityHidden(!showing)
            .accessibilityAddTraits(.isButton)
    }
}

/// The indicators where they take their own clicks in the tab: a click or a two-finger click
/// opens the read-only menu. The faint plate gives the transparent window a pixel under the whole
/// area, so the click reaches it.
struct AVIndicatorButton: View {
    var model: DeskModel
    let size: CGFloat

    var body: some View {
        AVIndicatorView(shown: model.avDrawn, size: size)
            .opacity(model.avIndicators.any ? 1 : 0)
            .frame(maxWidth: .infinity, maxHeight: .infinity)
            .background(Color.black.opacity(DeskPointerMenu.hitPlateOpacity))
            .contentShape(Rectangle())
            .onTapGesture { AVIndicatorMenu.popUpAtPointer(model.avIndicators) }
            .contextMenu { AVIndicatorMenuItems(shown: model.avIndicators) }
            .help("Camera and microphone in use. Click for more.")
            .accessibilityAddTraits(.isButton)
    }
}

/// The tab at the top (AVIndicatorSpot.badge): a small black shape with rounded lower corners.
/// The window fades in and out as a whole (AVIndicatorController).
struct AVIndicatorBadge: View {
    var model: DeskModel
    let size: CGFloat

    var body: some View {
        GeometryReader { geo in
            ZStack {
                IslandShape(flare: 0, radius: min(8, geo.size.height * 0.3))
                    .fill(Color.black)
                AVIndicatorButton(model: model, size: size)
            }
        }
    }
}

/// The menu as SwiftUI items, for the wings' context menu.
struct AVIndicatorMenuItems: View {
    let shown: AVIndicators

    var body: some View {
        ForEach(Array(AVIndicatorMenu.items(shown).enumerated()), id: \.offset) { _, item in
            if item.separatorBefore { Divider() }
            if item.enabled {
                Button(item.title) { SettingsWindowController.shared.show(.notch) }
            } else {
                Button(item.title) {}.disabled(true)
            }
        }
    }
}

extension AVIndicatorMenu {
    /// The NSMenu (the strip's clicks through DeskController, a click in SwiftUI).
    static func menu(_ shown: AVIndicators) -> NSMenu {
        let menu = NSMenu()
        menu.autoenablesItems = false
        for item in items(shown) {
            if item.separatorBefore { menu.addItem(.separator()) }
            let row = NSMenuItem(title: item.title,
                                 action: item.enabled ? #selector(AVIndicatorMenuTarget.settings) : nil,
                                 keyEquivalent: "")
            row.target = item.enabled ? AVIndicatorMenuTarget.shared : nil
            row.isEnabled = item.enabled
            menu.addItem(row)
        }
        return menu
    }

    /// Opens the menu at the pointer in the window that took the click.
    @MainActor static func popUpAtPointer(_ shown: AVIndicators) {
        guard let window = NSApp.currentEvent?.window, let view = window.contentView else { return }
        let at = view.convert(window.mouseLocationOutsideOfEventStream, from: nil)
        // After the click finishes, so the menu doesn't track inside the gesture's callback.
        DispatchQueue.main.async { menu(shown).popUp(positioning: nil, at: at, in: view) }
    }
}

final class AVIndicatorMenuTarget: NSObject {
    static let shared = AVIndicatorMenuTarget()
    // Menu items act on the main thread.
    @objc func settings() { MainActor.assumeIsolated { SettingsWindowController.shared.show(.notch) } }
}

/// Runs the camera and mic indicators while Desk runs and they are switched on: the camera signal
/// from its own CameraMonitor (the camera light's), the microphone's from MicMonitor. Publishes
/// what shows and where (AVIndicatorPlacement) to the Desk model, which the island draws, and
/// draws the tab itself when the island can't show them. In-use booleans only, in memory.
@MainActor
final class AVIndicatorController {
    static let shared = AVIndicatorController()

    private let camera = CameraMonitor()
    private let mic = MicMonitor()
    /// `av-test camera on|off` and `av-test mic on|off` (debug hooks): a faked signal, held in
    /// memory only. Shown like a real one, through the same switches; a faked camera counts as one
    /// without a visible light.
    private(set) var fakeCamera = false
    private(set) var fakeMic = false
    private(set) var window: NSWindow?
    private var windowSize: CGFloat = 0
    private var refront: Timer?
    private var observing = false
    private var badgeShown = false
    /// Bumped on every show and hide of the tab, so a fade-out that ends after a new show leaves it up.
    private var generation = 0
    private(set) var spot = AVIndicatorSpot.none

    var cameraInUse: Bool { fakeCamera || camera.inUse }
    var micInUse: Bool { fakeMic || mic.inUse }

    private init() {
        // Both monitors publish on the main queue. The camera reports every reading, so a switch
        // from the built-in camera to another one is seen while the camera stays in use.
        camera.onActivity = { [weak self] in MainActor.assumeIsolated { self?.update() } }
        mic.onChange = { [weak self] _ in MainActor.assumeIsolated { self?.update() } }
    }

    /// At Desk's start and stop and whenever a Desk setting changes: run each monitor only while
    /// Desk runs and its indicator is on.
    func apply() {
        let desk = UserDefaults.desk
        AVCameraDotMode.migrate(desk)
        let running = DeskController.shared.running
        if running && AVCameraDotMode.saved(in: desk).watches { camera.start() } else { camera.stop() }
        if running && desk.bool(forKey: AVIndicators.micKey) { mic.start() } else { mic.stop() }
        if !observing {
            observing = true
            NotificationCenter.default.addObserver(
                self, selector: #selector(settingsChanged),
                name: UserDefaults.didChangeNotification, object: UserDefaults.desk)
            // Closing or opening the lid changes the screens: the built-in camera's light changes
            // from out of sight to visible.
            NotificationCenter.default.addObserver(
                self, selector: #selector(screensChanged),
                name: NSApplication.didChangeScreenParametersNotification, object: nil)
        }
        update()
    }

    func setFake(camera on: Bool) { fakeCamera = on; update() }
    func setFake(mic on: Bool) { fakeMic = on; update() }

    /// Defaults post on the writer's thread.
    @objc nonisolated private func settingsChanged() {
        DispatchQueue.main.async { MainActor.assumeIsolated { AVIndicatorController.shared.apply() } }
    }

    /// After Desk rebuilds the island for the new screens (its own observer of this notice).
    @objc private func screensChanged() {
        windowSize = 0
        DispatchQueue.main.async { MainActor.assumeIsolated { AVIndicatorController.shared.update() } }
    }

    /// Whether the red dot shows now, by its mode.
    private func cameraDot(_ mode: AVCameraDotMode) -> Bool {
        guard cameraInUse else { return false }
        switch mode {
        case .never: return false
        case .always: return true
        case .hiddenLight:
            return fakeCamera || CameraLightVisibility.withoutVisibleLight(
                active: camera.activeDevices, builtIn: camera.builtInDevices, lidClosed: Self.lidClosed())
        }
    }

    /// The lid is closed (clamshell). IOPMrootDomain's AppleClamshellState: a registry read.
    private static func lidClosed() -> Bool {
        let service = IOServiceGetMatchingService(kIOMainPortDefault, IOServiceMatching("IOPMrootDomain"))
        guard service != 0 else { return false }
        defer { IOObjectRelease(service) }
        let value = IORegistryEntryCreateCFProperty(service, "AppleClamshellState" as CFString, kCFAllocatorDefault, 0)
        return (value?.takeRetainedValue() as? Bool) ?? false
    }

    /// Works out what shows and where, and hands it to the island or the tab. Changes land in one
    /// fade transaction: the slot's room and its opacity move together.
    func update() {
        let desk = UserDefaults.desk
        let controller = DeskController.shared
        let running = controller.running
        let shown = running
            ? AVIndicators(camera: cameraDot(.saved(in: desk)),
                           mic: desk.bool(forKey: AVIndicators.micKey) && micInUse)
            : AVIndicators()
        let chosen = AVIndicatorPlacement.chosen(
            left: NotchContent.saved(.left, in: desk), right: NotchContent.saved(.right, in: desk),
            strip: NotchContent.saved(.strip, in: desk),
            wingText: desk.object(forKey: "notchText") as? Bool ?? true,
            chinText: desk.bool(forKey: "notchChinText"),
            chin: desk.object(forKey: "notchChin") as? Double ?? 26)
        let islandUp = running && desk.bool(forKey: DeskController.notchKey) && controller.wingsWindow != nil
        let next = AVIndicatorPlacement.spot(shown, islandUp: islandUp, side: .saved(in: desk), chosen: chosen)
        let model = controller.model
        let reduceMotion = NSWorkspace.shared.accessibilityDisplayShouldReduceMotion
        withAnimation(AVIndicatorMotion.fadeAnimation(reduceMotion: reduceMotion)) {
            // What the slot draws while it fades out: the last indicators that showed.
            if shown.any, model.avDrawn != shown { model.avDrawn = shown }
            if model.avIndicators != shown { model.avIndicators = shown }
            if model.avSpot != next { model.avSpot = next }
        }
        spot = next
        if next == .badge { showBadge(shown, reduceMotion: reduceMotion) } else { hideBadge(reduceMotion: reduceMotion) }
    }

    // MARK: The tab

    private func showBadge(_ shown: AVIndicators, reduceMotion: Bool) {
        guard let screen = NSScreen.screens.first(where: { $0.cameraNotch != nil }) ?? NSScreen.main else { return }
        let notch = screen.cameraNotch
        let bar = max(screen.frame.maxY - screen.visibleFrame.maxY, 24)
        let size = max(10, (notch?.height ?? bar) * 0.42)
        let frame = AVIndicatorLayout.badgeFrame(screen: screen.frame, notch: notch, barHeight: bar,
                                                 side: .saved(in: .desk),
                                                 width: AVIndicatorLayout.badgeWidth(shown, size: size))
        let w = window ?? makeWindow()
        window = w
        if w.contentView == nil || windowSize != size {
            windowSize = size
            w.contentView = FirstClickHostingView(rootView: AVIndicatorBadge(model: DeskController.shared.model, size: size))
        }
        if w.frame != frame { w.setFrame(frame, display: true) }
        guard !badgeShown else { return }
        badgeShown = true
        generation += 1
        w.orderFrontRegardless()
        if reduceMotion {
            w.alphaValue = 1
        } else {
            NSAnimationContext.runAnimationGroup { ctx in
                ctx.duration = AVIndicatorMotion.fade
                w.animator().alphaValue = 1
            }
        }
        // Menu bar tools that re-raise their own overlays would cover it otherwise (as for the wings).
        refront?.invalidate()
        refront = Timer.scheduledTimer(withTimeInterval: 3, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated { self?.window?.orderFrontRegardless() }
        }
    }

    private func hideBadge(reduceMotion: Bool) {
        refront?.invalidate(); refront = nil
        guard badgeShown, let w = window else { return }
        badgeShown = false
        generation += 1
        let mine = generation
        if reduceMotion {
            w.alphaValue = 0
            w.orderOut(nil)
            return
        }
        NSAnimationContext.runAnimationGroup({ ctx in
            ctx.duration = AVIndicatorMotion.fade
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
        // With the notch island's wings: above the menu bar and over full-screen apps.
        w.level = .popUpMenu
        w.collectionBehavior = [.canJoinAllSpaces, .stationary, .ignoresCycle, .fullScreenAuxiliary]
        w.isOpaque = false
        w.backgroundColor = .clear
        w.hasShadow = false
        w.ignoresMouseEvents = false
        w.alphaValue = 0
        return w
    }
}
