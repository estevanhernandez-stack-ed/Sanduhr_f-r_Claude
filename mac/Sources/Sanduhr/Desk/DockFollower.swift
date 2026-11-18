import AppKit
import SwiftUI

/// Keeps the Desk's corners clear of the Dock (item 56), on every Desk display (2.45).
///
/// Always shown: the Dock's share of the screen comes from `visibleFrame` against `frame` on the
/// Dock's side (DockGeometry.alwaysShown), read again whenever the screen parameters change,
/// which they do when the Dock moves, resizes or starts or stops hiding.
///
/// Auto-hiding: the items rest at the edge and slide clear while the Dock shows (DockSlide).
/// The slide starts when the pointer has rested at the Dock's edge for the Dock's own delay
/// (`autohide-delay`), the moment the Dock starts to rise, and runs as long as the Dock's rise
/// (`autohide-time-modifier`) with an ease-out; it slides back with the Dock's drop when the
/// pointer leaves the Dock's zone. Whether the Dock really came up comes from the window list (`CGWindowListCopyWindowInfo`): a window owned
/// by the Dock process at the Dock's window layer, on screen and over this screen. Bounds and
/// flags only, so no Screen Recording permission. The list is read only on the Desk's pointer
/// watch, while the pointer is in the Dock's trigger zone (its edge, a few points deep, or over
/// the shown Dock) or the Dock has not hidden again yet (DockWatch). A slide the list has not
/// confirmed within DockSlide.confirmWithin goes back. A Dock that comes up for
/// another reason while the pointer is elsewhere (Mission Control, App Exposé) is followed only
/// once the pointer comes near; one shown and gone without the pointer near is missed.
///
/// Every display (2.45): with "Displays have separate Spaces" on, the auto-hiding Dock comes up at
/// the edge of the display the pointer is on. The follower watches the Dock edge of the Desk
/// display under the pointer (DockTarget), reads the window list over that display, and slides
/// that display's window: the main one through `apply`, another through `applyExtra`. One display
/// slides at a time; the pointer crossing to another display lets the last one's slide go back
/// first. The always-shown Dock's reservation on another display stays with its geometry
/// (DeskScreenGeometry.extra).
final class DockFollower {
    /// Called with the insets to lay out against, and the animation for the auto-hiding Dock's
    /// slide (nil: no slide, as with Reduce Motion).
    var apply: ((DockInsets, _ animation: Animation?) -> Void)?
    /// Called for a Desk window besides the main one with the auto-hiding Dock's slide there, and
    /// the same animation; nil insets while the Dock does not hide (the window keeps its
    /// geometry's reservation).
    var applyExtra: ((DeskDisplay, DockInsets?, _ animation: Animation?) -> Void)?

    /// Where the settings, the window list and the Spaces setting come from: the system in the
    /// app, values in tests.
    var prefsSource: () -> DockPrefs = { DockFollower.readPrefs() }
    var windowSource: () -> [DockGeometry.DockWindow] = { DockFollower.dockWindows() }
    var separateSpacesSource: () -> Bool = { DockFollower.readSeparateSpaces() }

    private(set) var prefs = DockPrefs()
    private(set) var slide = DockSlide()
    /// The pointer's rest at the Dock's edge, for the anticipated slide.
    private var dwell = DockDwell()
    /// The always-shown Dock's reservation on the main display.
    private var reserved = DockInsets()
    /// The main display's screen, for its visible frame when the settings change.
    private weak var screen: NSScreen?
    /// The main display's frame and visible frame (AppKit coordinates).
    private var mainFrame: CGRect = .zero
    private var mainVisible: CGRect = .zero
    /// The main display in CoreGraphics coordinates (top-left origin of the primary screen).
    private var main: DockDisplay?
    /// The other displays with a Desk window (DeskController.buildExtraWindows).
    private(set) var extras: [DockDisplay] = []
    /// The display whose window the slide follows now.
    private(set) var target = DeskDisplay.main
    /// "Displays have separate Spaces", read with the Dock's settings.
    private var separateSpaces = true
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

    /// What the main window lays out against now.
    var insets: DockInsets {
        prefs.autohide ? DockTarget.slideInsets(for: .main, target: target, side: prefs.side, inset: slide.inset) : reserved
    }

    /// The slide on another display's window, nil while the Dock does not hide.
    func insets(for display: DeskDisplay) -> DockInsets? {
        prefs.autohide ? DockTarget.slideInsets(for: display, target: target, side: prefs.side, inset: slide.inset) : nil
    }

    /// The Desk displays, the main one first.
    private var displays: [DockDisplay] { (main.map { [$0] } ?? []) + extras }

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
        dwell = DockDwell()
        generation += 1
        target = .main
        extras = []
    }

    @objc private func prefsDidChange() { refreshPrefs() }

    /// The Dock's settings, read only.
    static func readPrefs() -> DockPrefs {
        let d = UserDefaults(suiteName: DockPrefs.domain)
        return DockPrefs.read { d?.object(forKey: $0) }
    }

    /// "Displays have separate Spaces" (Mission Control's setting), read only.
    static func readSeparateSpaces() -> Bool {
        let d = UserDefaults(suiteName: "com.apple.spaces")
        return DockTarget.separateSpaces { d?.object(forKey: $0) }
    }

    /// Reads the Dock's settings again (an app switch, a Space change, its notification) and
    /// re-applies when they changed.
    func refreshPrefs() {
        separateSpaces = separateSpacesSource()
        let fresh = prefsSource()
        guard fresh != prefs else { return }
        prefs = fresh
        if let screen { mainVisible = screen.visibleFrame }
        measure()
        if !prefs.autohide { slide.reset() }
        push(nil)
    }

    /// The Desk's main window was built or rebuilt on `screen`: screen parameters changed.
    func screenChanged(_ screen: NSScreen) {
        self.screen = screen
        let primaryHeight = NSScreen.screens.first?.frame.height ?? screen.frame.height
        setMain(frame: screen.frame, visible: screen.visibleFrame, primaryHeight: primaryHeight)
    }

    /// The main display's frame and visible frame (AppKit coordinates), and the primary screen's
    /// height for the CoreGraphics frame.
    func setMain(frame: CGRect, visible: CGRect, primaryHeight: CGFloat) {
        prefs = prefsSource()
        separateSpaces = separateSpacesSource()
        mainFrame = frame
        mainVisible = visible
        main = DockDisplay(key: .main, frame: DebugTree.topLeft(frame, primaryHeight: primaryHeight))
        measure()
        if !prefs.autohide { slide.reset() }
        push(nil)
    }

    /// The other displays' windows were built again: the slide follows them too. A slide on a
    /// display that lost its window ends there.
    func setExtras(_ displays: [DockDisplay]) {
        extras = displays
        if target != .main, !displays.contains(where: { $0.key == target }) { retarget(.main) }
        for d in extras { applyExtra?(d.key, insets(for: d.key), nil) }
    }

    /// The always-shown Dock's reservation on the main display, kept as the thickness to expect
    /// once it hides.
    private func measure() {
        reserved = DockGeometry.alwaysShown(prefs, frame: mainFrame, visible: mainVisible)
        let inset = reserved.amount(on: prefs.side)
        if !prefs.autohide, inset > 0 { measured = (prefs.tilesize, prefs.side, inset) }
    }

    /// True while the pointer (global CoreGraphics coordinates) calls for the close watch: in the
    /// auto-hiding Dock's trigger zone on a Desk display, or the Dock not hidden again yet.
    func wantsWatch(pointer: CGPoint?) -> Bool {
        guard prefs.autohide else { return false }
        if slide.phase != .hidden { return true }
        guard let pointer, let d = DockTarget.display(under: pointer, among: displays) else { return false }
        return DockGeometry.inTriggerZone(DockTarget.local(pointer, in: d), size: d.frame.size,
                                          side: prefs.side, shownExtent: 0)
    }

    /// One tick of the Desk's close watch (pointer in global CoreGraphics coordinates): follows
    /// the display under the pointer, slides ahead of the Dock once the pointer has rested at its
    /// edge for its delay, back when the pointer leaves its zone, and reads the window list when
    /// DockWatch says so.
    func tick(pointer: CGPoint?, moved: Bool, now: Date = Date()) {
        guard prefs.autohide else { return }
        ticks &+= 1
        if moved { lastActivity = now }
        let under = pointer.flatMap { DockTarget.display(under: $0, among: displays) }
        if let under, under.key != target { retarget(under.key, now: now) }
        let local: (point: CGPoint, size: CGSize)? = under.flatMap { d in
            pointer.map { (DockTarget.local($0, in: d), d.frame.size) }
        }
        let inZone = local.map {
            DockGeometry.inTriggerZone($0.point, size: $0.size, side: prefs.side, shownExtent: slide.inset)
        } ?? false
        let atEdge = local.map { DockGeometry.atRevealEdge($0.point, size: $0.size, side: prefs.side) } ?? false
        dwell.update(atEdge: atEdge, now: now)
        if !inZone {
            if slide.pointerLeft() { slid(now: now) }
        } else if slide.phase == .hidden || slide.phase == .hiding, dwell.due(delay: prefs.delay, now: now),
                  DockTarget.anticipates(on: target, separateSpaces: separateSpaces) {
            dwell.spend()
            if slide.anticipate(thickness, now: now) { slid(now: now) }
        }
        guard DockWatch.shouldRead(inZone: inZone, phase: slide.phase, pointerMoved: moved,
                                   sinceActivity: now.timeIntervalSince(lastActivity),
                                   delay: prefs.delay, tick: ticks) else { return }
        observe(windowSource(), inZone: inZone, now: now)
    }

    /// The slide moves to `display`: a slide still up on the last one goes back with its Dock
    /// first, and the new display starts at rest.
    private func retarget(_ display: DeskDisplay, now: Date = Date()) {
        if slide.pointerLeft() { slid(now: now) }
        slide.reset()
        dwell = DockDwell()
        generation += 1
        target = display
    }

    /// A reading of the Dock's windows over the target display: confirm, start or end a slide.
    /// Only a pointer in the Dock's zone starts one.
    func observe(_ windows: [DockGeometry.DockWindow], inZone: Bool = true, now: Date = Date()) {
        guard let screen = displays.first(where: { $0.key == target })?.frame else { return }
        let seen = DockGeometry.shownExtent(windows, layer: Self.dockLayer, screen: screen,
                                            side: prefs.side, fallback: thickness)
        guard slide.observe(seen, canShow: inZone, now: now) else { return }
        slid(now: now)
    }

    /// The Dock's thickness when the window list gives no strip (and for an anticipated slide).
    private var thickness: CGFloat {
        DockGeometry.estimatedThickness(
            tilesize: prefs.tilesize,
            measured: measured.flatMap { $0.tilesize == prefs.tilesize && $0.side == prefs.side ? $0.inset : nil })
    }

    /// The inset changed: slide in step with the Dock (or jump with Reduce Motion or a Dock
    /// that does not animate), and settle when the slide is done.
    private func slid(now: Date) {
        lastActivity = now
        generation += 1
        let reduceMotion = NSWorkspace.shared.accessibilityDisplayShouldReduceMotion
        let duration = prefs.slideDuration
        let animation = reduceMotion ? nil : Self.animation(showing: slide.phase == .showing, duration: duration)
        push(animation)
        if animation == nil {
            slide.settle()
        } else {
            let mine = generation
            DispatchQueue.main.asyncAfter(deadline: .now() + duration) { [weak self] in
                guard let self, self.generation == mine else { return }
                self.slide.settle()
            }
        }
    }

    /// Every Desk window gets its insets: the target slides, the others rest.
    private func push(_ animation: Animation?) {
        apply?(insets, animation)
        for d in extras { applyExtra?(d.key, insets(for: d.key), animation) }
    }

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

    /// The slide's animation, as long as the Dock's own: an ease-out as it rises, an ease-in as
    /// it drops. Nil for a Dock that does not animate (`autohide-time-modifier` 0).
    static func animation(showing: Bool, duration: TimeInterval) -> Animation? {
        guard duration > 0 else { return nil }
        return showing ? .easeOut(duration: duration) : .easeIn(duration: duration)
    }
}
