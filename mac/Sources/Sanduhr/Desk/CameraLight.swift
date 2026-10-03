import AppKit
import SwiftUI

/// Where the camera light sits and how big it is, kept apart from AppKit so it tests on its own.
/// The light hugs the notch (or, on a screen without one, a notch-wide spot at the top center):
/// it covers the menu bar beside the camera and reaches `size` points below it, with rounded
/// lower corners and a feathered edge.
enum CameraLightLayout {
    /// How far the soft edge reaches past the light's shape; the window is this much larger.
    static let feather: CGFloat = 22
    /// The light's core width on a screen without a notch.
    static let noNotchWidth: CGFloat = 200
    static let sizeRange: ClosedRange<Double> = 20...200
    static let brightnessRange: ClosedRange<Double> = 0.2...1

    /// The light's own width and height (before the feather) and its corner radius.
    struct Shape: Equatable {
        var width: CGFloat
        var height: CGFloat
        var radius: CGFloat
    }

    /// `notch` is the notch in screen-relative points (x from the screen's left edge), nil for none.
    static func shape(notch: CGRect?, barHeight: CGFloat, size: Double) -> Shape {
        let below = CGFloat(min(max(size, sizeRange.lowerBound), sizeRange.upperBound))
        let core = notch?.width ?? noNotchWidth
        let side = max(36, below)
        let height = max(barHeight, notch?.height ?? 0) + below
        return Shape(width: core + side * 2, height: height, radius: min(below + 12, height / 2))
    }

    /// The window frame in AppKit screen coordinates (bottom-left origin): centered on the notch
    /// or the screen, flush with the top, the feather added left, right and below.
    static func frame(screen: CGRect, notch: CGRect?, barHeight: CGFloat, size: Double) -> CGRect {
        let s = shape(notch: notch, barHeight: barHeight, size: size)
        let centerX = notch.map { screen.minX + $0.midX } ?? screen.midX
        let width = s.width + feather * 2
        let height = s.height + feather
        return CGRect(x: centerX - width / 2, y: screen.maxY - height, width: width, height: height)
    }

    /// The light shows while switched on by hand, or while the setting is on and a camera runs.
    static func showing(enabled: Bool, cameraInUse: Bool, manual: Bool) -> Bool {
        manual || (enabled && cameraInUse)
    }
}

/// The light itself: a white shape with rounded lower corners, blurred at its edge. Its top runs
/// past the window's top so the blur only softens the sides and the bottom.
struct CameraLightView: View {
    @AppStorage(CameraLightController.brightnessKey, store: .desk) private var brightness = CameraLightController.defaultBrightness
    let shape: CameraLightLayout.Shape

    var body: some View {
        let feather = CameraLightLayout.feather
        let level = min(max(brightness, CameraLightLayout.brightnessRange.lowerBound), CameraLightLayout.brightnessRange.upperBound)
        GeometryReader { geo in
            UnevenRoundedRectangle(bottomLeadingRadius: shape.radius, bottomTrailingRadius: shape.radius)
                .fill(Color.white)
                .frame(width: shape.width, height: shape.height + feather)
                .position(x: geo.size.width / 2, y: (shape.height + feather) / 2 - feather)
                .blur(radius: feather / 2)
                .opacity(level)
        }
        .allowsHitTesting(false)
    }
}

/// Holds the light's window and accessibility name, so smoke's tree.yaml lists a "camera" window
/// with a node even though the light draws no text.
private final class CameraLightContainer: NSView {
    override func isAccessibilityElement() -> Bool { true }
    override func accessibilityRole() -> NSAccessibility.Role? { .image }
    override func accessibilityLabel() -> String? { "Camera light" }
    override func hitTest(_ point: NSPoint) -> NSView? { nil }
}

/// The notch as a camera light (Settings, Desk, Notch): while any app uses a camera, a soft white
/// light around the notch lights the face. Its own click-through window above every app, full
/// screen ones included; the notch island underneath is left as it is. The camera monitor runs
/// whenever the switch is on, whether or not Desk runs. Tools, Camera Light shows it by hand
/// until chosen again.
@MainActor
final class CameraLightController {
    static let shared = CameraLightController()
    static let enabledKey = "cameraLight"
    static let brightnessKey = "cameraLightBrightness"
    static let sizeKey = "cameraLightSize"
    static let defaultBrightness = 0.8
    static let defaultSize = 60.0
    static let fade: TimeInterval = 0.3

    private let monitor = CameraMonitor()
    private(set) var window: NSWindow?
    /// Shown by hand from the Tools menu.
    private(set) var manual = false
    private var visible = false
    private var refront: Timer?
    private var observing = false
    /// Bumped on every show and hide, so a fade-out that finishes after a new show leaves it up.
    private var generation = 0

    var enabled: Bool { UserDefaults.desk.bool(forKey: Self.enabledKey) }
    var cameraInUse: Bool { monitor.inUse }
    /// The light is on screen, or fading in.
    var showing: Bool { visible }

    private init() {
        monitor.onChange = { [weak self] _ in
            // CameraMonitor publishes on the main queue.
            MainActor.assumeIsolated { self?.update() }
        }
    }

    /// At launch and whenever the switch flips: watch the cameras only while it is on.
    func apply() {
        if enabled { monitor.start() } else { monitor.stop() }
        if !observing {
            observing = true
            NotificationCenter.default.addObserver(
                self, selector: #selector(screensChanged),
                name: NSApplication.didChangeScreenParametersNotification, object: nil)
            NotificationCenter.default.addObserver(
                self, selector: #selector(settingsChanged),
                name: UserDefaults.didChangeNotification, object: UserDefaults.desk)
            NSWorkspace.shared.notificationCenter.addObserver(
                self, selector: #selector(spaceChanged),
                name: NSWorkspace.activeSpaceDidChangeNotification, object: nil)
        }
        update()
    }

    /// Tools, Camera Light.
    func toggleManual() { setManual(!manual) }

    func setManual(_ on: Bool) {
        manual = on
        update()
    }

    private func update() {
        let want = CameraLightLayout.showing(enabled: enabled, cameraInUse: monitor.inUse, manual: manual)
        if want { show() } else { hide() }
    }

    @objc private func screensChanged() { if visible { place() } }
    @objc private func spaceChanged() { if visible { window?.orderFrontRegardless() } }

    /// The size slider moves the light while it shows. Defaults post on the writer's thread.
    @objc nonisolated private func settingsChanged() {
        DispatchQueue.main.async { [weak self] in
            MainActor.assumeIsolated {
                guard let self, self.visible, let w = self.window else { return }
                if w.frame != self.targetFrame()?.frame { self.place() }
            }
        }
    }

    // MARK: Window

    /// The screen with the camera notch, else the main screen.
    private static func screen() -> NSScreen? {
        NSScreen.screens.first { $0.cameraNotch != nil } ?? NSScreen.main ?? NSScreen.screens.first
    }

    private func targetFrame() -> (frame: CGRect, shape: CameraLightLayout.Shape)? {
        guard let screen = Self.screen() else { return nil }
        let size = UserDefaults.desk.object(forKey: Self.sizeKey) as? Double ?? Self.defaultSize
        let notch = screen.cameraNotch
        let bar = screen.frame.maxY - screen.visibleFrame.maxY
        let frame = CameraLightLayout.frame(screen: screen.frame, notch: notch, barHeight: bar, size: size)
        return (frame, CameraLightLayout.shape(notch: notch, barHeight: bar, size: size))
    }

    private func makeWindow() -> NSWindow {
        let w = NSWindow(contentRect: .zero, styleMask: [.borderless], backing: .buffered, defer: false)
        w.isReleasedWhenClosed = false
        // Above menus and the notch island (.popUpMenu), so it shows over every app and over
        // full-screen ones; far below the screen saver and shielding levels.
        w.level = NSWindow.Level(rawValue: NSWindow.Level.popUpMenu.rawValue + 1)
        w.collectionBehavior = [.canJoinAllSpaces, .stationary, .ignoresCycle, .fullScreenAuxiliary]
        w.isOpaque = false
        w.backgroundColor = .clear
        w.hasShadow = false
        w.ignoresMouseEvents = true
        w.alphaValue = 0
        return w
    }

    /// Sizes the window and redraws its light for the current screen and size setting.
    private func place() {
        guard let target = targetFrame() else { return }
        let w = window ?? makeWindow()
        window = w
        let container = CameraLightContainer(frame: CGRect(origin: .zero, size: target.frame.size))
        let host = FirstClickHostingView(rootView: CameraLightView(shape: target.shape))
        host.frame = container.bounds
        host.autoresizingMask = [.width, .height]
        container.addSubview(host)
        w.contentView = container
        w.setFrame(target.frame, display: true)
    }

    private func show() {
        guard !visible else { return }
        visible = true
        generation += 1
        place()
        NotchGlowController.shared.event(.cameraLightOn)
        guard let w = window else { return }
        w.orderFrontRegardless()
        NSAnimationContext.runAnimationGroup { ctx in
            ctx.duration = Self.fade
            w.animator().alphaValue = 1
        }
        // Menu bar tools that re-raise their own overlays would cover it otherwise (as for the wings).
        refront?.invalidate()
        refront = Timer.scheduledTimer(withTimeInterval: 3, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated { self?.window?.orderFrontRegardless() }
        }
    }

    private func hide() {
        guard visible else { return }
        visible = false
        generation += 1
        let mine = generation
        refront?.invalidate(); refront = nil
        guard let w = window else { return }
        NSAnimationContext.runAnimationGroup({ ctx in
            ctx.duration = Self.fade
            w.animator().alphaValue = 0
        }, completionHandler: { [weak self] in
            MainActor.assumeIsolated {
                guard let self, self.generation == mine else { return }
                w.orderOut(nil)
            }
        })
    }
}

extension NSScreen {
    /// The camera notch in points from this screen's top-left corner, nil on a screen without one.
    /// It sits between the two "auxiliary" top areas macOS reports for notched screens.
    var cameraNotch: CGRect? {
        guard let left = auxiliaryTopLeftArea, let right = auxiliaryTopRightArea,
              safeAreaInsets.top > 0 else { return nil }
        return CGRect(x: left.maxX - frame.minX, y: 0, width: right.minX - left.maxX, height: safeAreaInsets.top)
    }
}
