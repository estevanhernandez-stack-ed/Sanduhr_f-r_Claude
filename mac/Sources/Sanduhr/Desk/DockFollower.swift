import AppKit
import SwiftUI

/// Keeps the Desk's corners clear of the Dock on the Desk's screen (item 56).
///
/// Always shown: the Dock's share of the screen comes from `visibleFrame` against `frame` on the
/// Dock's side (DockGeometry.alwaysShown), read again whenever the screen parameters change,
/// which they do when the Dock moves, resizes or starts or stops hiding.
///
/// Auto-hiding: the items rest at the edge and slide clear while the Dock shows (DockSlide).
/// Whether it shows comes from the window list (`CGWindowListCopyWindowInfo`): a window owned
/// by the Dock process at the Dock's window layer, on screen and over this screen. Bounds and
/// flags only, so no Screen Recording permission. The list is read only on the Desk's pointer
/// watch, while the pointer is in the Dock's trigger zone (its edge, a few points deep, or over
/// the shown Dock) or the Dock has not hidden again yet (DockWatch). A Dock that comes up for
/// another reason while the pointer is elsewhere (Mission Control, App Exposé) is followed only
/// once the pointer comes near; one shown and gone without the pointer near is missed.
final class DockFollower {
    /// Called with the insets to lay out against; `animated` for the auto-hiding Dock's slide.
    var apply: ((DockInsets, _ animated: Bool) -> Void)?

    private(set) var prefs = DockPrefs()
    private(set) var slide = DockSlide()
    /// The always-shown Dock's reservation on the Desk's screen.
    private var reserved = DockInsets()
    /// The Desk's screen.
    private weak var screen: NSScreen?
    /// The Desk's screen in CoreGraphics coordinates (top-left origin of the primary screen).
    private var screenCG: CGRect = .zero
    /// Last pointer movement or slide, for DockWatch's quiet rule.
    private var lastActivity = Date.distantPast
    private var ticks = 0
    /// Bumps with every slide, so a settle meant for an earlier one does nothing.
    private var generation = 0
    private var observing = false
    /// The Dock's reservation seen while it was always shown, with the tile size then: the
    /// best estimate of its thickness once it hides (DockGeometry.estimatedThickness).
    private var measured: (tilesize: CGFloat, side: DockSide, inset: CGFloat)?

    private static let prefsChanged = Notification.Name("com.apple.dock.prefchanged")

    /// What the Desk lays out against now.
    var insets: DockInsets { prefs.autohide ? .on(prefs.side, slide.inset) : reserved }

    func start() {
        guard !observing else { return }
        observing = true
        // Posted by the Dock when its settings change, where macOS posts it; screen parameter
        // changes and app switches re-read the settings too, so nothing depends on it.
        DistributedNotificationCenter.default().addObserver(
            self, selector: #selector(prefsDidChange), name: Self.prefsChanged, object: nil)
    }

    func stop() {
        if observing { DistributedNotificationCenter.default().removeObserver(self) }
        observing = false
        slide.reset()
        generation += 1
    }

    @objc private func prefsDidChange() { refreshPrefs() }

    /// The Dock's settings, read only.
    static func readPrefs() -> DockPrefs {
        let d = UserDefaults(suiteName: DockPrefs.domain)
        return DockPrefs.read { d?.object(forKey: $0) }
    }

    /// Reads the Dock's settings again (an app switch, a Space change, its notification) and
    /// re-applies when they changed.
    func refreshPrefs() {
        let fresh = Self.readPrefs()
        guard fresh != prefs else { return }
        prefs = fresh
        if let screen { measure(screen) }
        if !prefs.autohide { slide.reset() }
        push(animated: false)
    }

    /// The Desk's window was built or rebuilt on `screen`: screen parameters changed.
    func screenChanged(_ screen: NSScreen) {
        self.screen = screen
        prefs = Self.readPrefs()
        let primaryHeight = NSScreen.screens.first?.frame.height ?? screen.frame.height
        screenCG = DebugTree.topLeft(screen.frame, primaryHeight: primaryHeight)
        measure(screen)
        if !prefs.autohide { slide.reset() }
        push(animated: false)
    }

    /// The always-shown Dock's reservation on `screen`, kept as the thickness to expect once
    /// it hides.
    private func measure(_ screen: NSScreen) {
        reserved = DockGeometry.alwaysShown(prefs, frame: screen.frame, visible: screen.visibleFrame)
        let inset = reserved.amount(on: prefs.side)
        if !prefs.autohide, inset > 0 { measured = (prefs.tilesize, prefs.side, inset) }
    }

    /// True while the pointer (Desk window coordinates) calls for the close watch: in the
    /// auto-hiding Dock's trigger zone, or the Dock not hidden again yet.
    func wantsWatch(pointer: CGPoint?, size: CGSize) -> Bool {
        guard prefs.autohide else { return false }
        if slide.phase != .hidden { return true }
        guard let pointer else { return false }
        return DockGeometry.inTriggerZone(pointer, size: size, side: prefs.side, shownExtent: 0)
    }

    /// One tick of the Desk's close watch. Reads the window list when DockWatch says so.
    func tick(pointer: CGPoint?, size: CGSize, moved: Bool, now: Date = Date()) {
        guard prefs.autohide else { return }
        ticks &+= 1
        if moved { lastActivity = now }
        let shown = slide.phase == .shown || slide.phase == .showing ? slide.extent : 0
        let inZone = pointer.map {
            DockGeometry.inTriggerZone($0, size: size, side: prefs.side, shownExtent: shown)
        } ?? false
        guard DockWatch.shouldRead(inZone: inZone, phase: slide.phase, pointerMoved: moved,
                                   sinceActivity: now.timeIntervalSince(lastActivity),
                                   delay: prefs.delay, tick: ticks) else { return }
        observe(Self.dockWindows(), now: now)
    }

    /// A reading of the Dock's windows: start a slide when the Dock came or went.
    func observe(_ windows: [DockGeometry.DockWindow], now: Date = Date()) {
        let fallback = DockGeometry.estimatedThickness(
            tilesize: prefs.tilesize,
            measured: measured.flatMap { $0.tilesize == prefs.tilesize && $0.side == prefs.side ? $0.inset : nil })
        let seen = DockGeometry.shownExtent(windows, layer: Self.dockLayer, screen: screenCG,
                                            side: prefs.side, fallback: fallback)
        guard slide.observe(seen) else { return }
        lastActivity = now
        generation += 1
        let reduceMotion = NSWorkspace.shared.accessibilityDisplayShouldReduceMotion
        push(animated: !reduceMotion)
        if reduceMotion {
            slide.settle()
        } else {
            let mine = generation
            DispatchQueue.main.asyncAfter(deadline: .now() + DockSlide.duration) { [weak self] in
                guard let self, self.generation == mine else { return }
                self.slide.settle()
            }
        }
    }

    private func push(animated: Bool) { apply?(insets, animated) }

    /// The Dock's window layer.
    static var dockLayer: Int { Int(CGWindowLevelForKey(.dockWindow)) }

    /// The Dock process's on-screen windows: layer and bounds. Window metadata only.
    static func dockWindows() -> [DockGeometry.DockWindow] {
        guard let dock = NSRunningApplication.runningApplications(withBundleIdentifier: "com.apple.dock").first,
              let list = CGWindowListCopyWindowInfo([.optionOnScreenOnly], kCGNullWindowID) as? [[String: Any]] else { return [] }
        let pid = dock.processIdentifier
        return list.compactMap { info in
            guard (info[kCGWindowOwnerPID as String] as? Int32) == pid,
                  let layer = info[kCGWindowLayer as String] as? Int,
                  let b = info[kCGWindowBounds as String] as? [String: CGFloat],
                  let x = b["X"], let y = b["Y"], let w = b["Width"], let h = b["Height"] else { return nil }
            // On-screen windows only: the list is shorter, and every one here is on screen.
            return DockGeometry.DockWindow(layer: layer, bounds: CGRect(x: x, y: y, width: w, height: h), onScreen: true)
        }
    }

    /// The slide's animation: an ease about as long as the Dock's own.
    static let animation = Animation.easeInOut(duration: DockSlide.duration)
}
