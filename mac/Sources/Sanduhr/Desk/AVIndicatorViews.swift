import AppKit
import SwiftUI

/// How the indicators draw. The camera's red dot is the only red dot in Sanduhr: a failed watcher
/// draws a red triangle instead (WatcherLook.mark), so a red dot always means the camera.
enum AVIndicatorLook {
    static let camera = Color.hex("ff3b30")
    /// Orange, as macOS marks the microphone in the menu bar.
    static let mic = Color.hex("ff9f0a")
}

/// The red recording dot. It pulses gently while the pulse switch is on, never with Reduce Motion.
struct AVCameraDot: View {
    let diameter: CGFloat
    let pulse: Bool
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        let dot = Circle()
            .fill(AVIndicatorLook.camera)
            .frame(width: diameter, height: diameter)
        if pulse && !reduceMotion {
            dot.phaseAnimator([false, true]) { view, dim in
                view.opacity(dim ? 0.55 : 1)
            } animation: { _ in .easeInOut(duration: 1.1) }
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

/// The indicators where they take their own clicks (a wing, beside the camera, the tab): a click
/// or a two-finger click opens the read-only menu. The faint plate gives the transparent window a
/// pixel under the whole area, so the click reaches it.
struct AVIndicatorButton: View {
    var model: DeskModel
    let size: CGFloat

    var body: some View {
        let shown = model.avIndicators
        AVIndicatorView(shown: shown, size: size)
            .frame(maxWidth: .infinity, maxHeight: .infinity)
            .background(Color.black.opacity(DeskPointerMenu.hitPlateOpacity))
            .contentShape(Rectangle())
            .onTapGesture { AVIndicatorMenu.popUpAtPointer(shown) }
            .contextMenu { AVIndicatorMenuItems(shown: shown) }
            .help("Camera and microphone in use. Click for more.")
            .accessibilityAddTraits(.isButton)
    }
}

/// The tab at the top (AVIndicatorSpot.badge): a small black shape with rounded lower corners.
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

/// Runs the camera and mic indicators while Desk runs and their switch is on: the camera signal
/// from its own CameraMonitor (the camera light's), the microphone's from MicMonitor. Publishes
/// what shows and where (AVIndicatorPlacement) to the Desk model, which the island draws, and
/// draws the tab itself when the island can't show them. In-use booleans only, in memory.
@MainActor
final class AVIndicatorController {
    static let shared = AVIndicatorController()

    private let camera = CameraMonitor()
    private let mic = MicMonitor()
    /// `av-test camera on|off` and `av-test mic on|off` (debug hooks): a faked signal, held in
    /// memory only. Shown like a real one, through the same switches.
    private(set) var fakeCamera = false
    private(set) var fakeMic = false
    private(set) var window: NSWindow?
    private var windowSize: CGFloat = 0
    private var refront: Timer?
    private var observing = false
    private(set) var spot = AVIndicatorSpot.none

    var cameraInUse: Bool { fakeCamera || camera.inUse }
    var micInUse: Bool { fakeMic || mic.inUse }

    private init() {
        // Both monitors publish on the main queue.
        camera.onChange = { [weak self] _ in MainActor.assumeIsolated { self?.update() } }
        mic.onChange = { [weak self] _ in MainActor.assumeIsolated { self?.update() } }
    }

    /// At Desk's start and stop and whenever a Desk setting changes: run each monitor only while
    /// Desk runs and its switch is on.
    func apply() {
        let desk = UserDefaults.desk
        let running = DeskController.shared.running
        if running && desk.bool(forKey: AVIndicators.cameraKey) { camera.start() } else { camera.stop() }
        if running && desk.bool(forKey: AVIndicators.micKey) { mic.start() } else { mic.stop() }
        if !observing {
            observing = true
            NotificationCenter.default.addObserver(
                self, selector: #selector(settingsChanged),
                name: UserDefaults.didChangeNotification, object: UserDefaults.desk)
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

    /// Works out what shows and where, and hands it to the island or the tab.
    func update() {
        let desk = UserDefaults.desk
        let controller = DeskController.shared
        let running = controller.running
        let shown = running
            ? AVIndicators.shown(cameraInUse: cameraInUse, micInUse: micInUse,
                                 cameraSwitch: desk.bool(forKey: AVIndicators.cameraKey),
                                 micSwitch: desk.bool(forKey: AVIndicators.micKey))
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
        if model.avIndicators != shown { model.avIndicators = shown }
        if model.avSpot != next { model.avSpot = next }
        spot = next
        if next == .badge { showBadge(shown) } else { hideBadge() }
    }

    // MARK: The tab

    private func showBadge(_ shown: AVIndicators) {
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
        w.orderFrontRegardless()
        if refront == nil {
            // Menu bar tools that re-raise their own overlays would cover it otherwise (as for the wings).
            refront = Timer.scheduledTimer(withTimeInterval: 3, repeats: true) { [weak self] _ in
                MainActor.assumeIsolated { self?.window?.orderFrontRegardless() }
            }
        }
    }

    private func hideBadge() {
        refront?.invalidate(); refront = nil
        window?.orderOut(nil)
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
        return w
    }
}
