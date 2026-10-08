import AppKit
import SwiftUI
import Carbon.HIToolbox

/// Sanduhr's desk surfaces: a click-through layer on the desktop (clock, Claude meters,
/// meetings, the message) and the notch island. Off until turned on in Settings, General,
/// Surfaces, so nothing changes for widget-only users; a brand-new
/// install starts with it on (DeskFirstRun). Its settings
/// live in their own defaults suite (UserDefaults.desk) so their short names never collide
/// with the widget's.
final class DeskController: NSObject, NSMenuDelegate {
    static let shared = DeskController()
    static let enabledKey = "deskEnabled"
    /// The notch island, off until switched on in Settings (General or Notch).
    static let notchKey = "notch"
    /// 2.10.0's one switch for ⌥J and ⌥S; slice 2 split it (SanduhrHotKeys).
    static let hotKeysKey = SanduhrHotKeys.legacyKey
    private(set) var running = false
    private(set) var window: NSWindow?
    private var statusItem: NSStatusItem?
    private var mouseMonitors: [Any] = []
    /// The close pointer watch (DeskPointerWatch): runs only while the pointer is near a block.
    private var approachTimer: Timer?
    /// Where the pointer was at the close watch's last tick (screen coordinates).
    private var lastWatchedPointer: NSPoint?
    private(set) var wingsWindow: NSWindow?
    private var wingsTimer: Timer?
    let model = DeskModel()
    /// Keeps the corners clear of the Dock (item 56).
    let dock = DockFollower()
    /// The pointer was near a Desk block at the last mouse-through check.
    private var nearBlocks = false
    private let hotKeys = DeskHotKeys()
    /// The shortcuts asked for now, by switch and keys (applyHotKeys).
    private var registeredShortcuts: [HotKeyRequest] = []
    private struct HotKeyRequest: Equatable {
        let shortcut: SanduhrHotKeys.Shortcut
        let combo: HotKeyCombo
    }
    /// The shortcuts whose keys another app holds, so they did not register (state.yaml
    /// `hot_keys.<name>_taken`, the note under the switch on General).
    private(set) var takenShortcuts: Set<SanduhrHotKeys.Shortcut> = []
    /// While General's recorder listens, nothing is registered, so the keys pressed reach it
    /// instead of opening Settings or joining a meeting.
    var hotKeysSuspended = false {
        didSet { if hotKeysSuspended != oldValue { applyHotKeys() } }
    }
    /// How many shortcuts are registered (state.yaml `hot_keys.registered`).
    var hotKeysRegistered: Int { hotKeys.count }

    var enabled: Bool { UserDefaults.desk.bool(forKey: Self.enabledKey) }

    /// Desk's on/off as of the last apply(), nil before the first (at launch).
    private var appliedEnabled: Bool?

    /// Called at launch and whenever the Desk switch flips. A flip after launch also lets the
    /// widget follow its When the widget shows choice (AppDelegate.deskDidChange).
    func apply() {
        if enabled, !running { start() }
        if !enabled, running { stop() }
        // Now playing runs only while Desk does (and its own switch is on).
        NowPlayingController.shared.apply()
        // The camera and mic indicators run only while Desk does (and their switches are on).
        MainActor.assumeIsolated { AVIndicatorController.shared.apply() }
        // The two shortcuts work whenever Sanduhr runs, Desk or not (slice 2).
        applyHotKeys()
        let previous = appliedEnabled
        appliedEnabled = enabled
        if let previous, previous != enabled {
            MainActor.assumeIsolated { (NSApp.delegate as? AppDelegate)?.deskDidChange() }
        }
    }

    private func start() {
        running = true
        // The standalone Desk and Sanduhr Desk apps did this job before; one desk is enough.
        for id in ["com.estevan.desk", "com.626labs.sanduhrdesk"] {
            NSRunningApplication.runningApplications(withBundleIdentifier: id).forEach { $0.terminate() }
        }
        dock.apply = { [weak self] insets, animation in self?.applyDock(insets, animation: animation) }
        dock.start()
        buildWindow()
        // The clock menu is off by default: Sanduhr owns the menu bar, meetings join from the
        // desktop or the join shortcut. `defaults write com.626labs.sanduhr.desk menuIcon -bool true` brings it back.
        if UserDefaults.desk.bool(forKey: "menuIcon") { buildMenu() }
        model.start()
        watchMouse()
        applyHotKeys()
        NotificationCenter.default.addObserver(
            self, selector: #selector(screensChanged),
            name: NSApplication.didChangeScreenParametersNotification, object: nil)
        NSWorkspace.shared.notificationCenter.addObserver(
            self, selector: #selector(spaceChanged),
            name: NSWorkspace.activeSpaceDidChangeNotification, object: nil)
        // Back from System Settings: a Calendar grant shows without waiting for the minute tick.
        NotificationCenter.default.addObserver(
            self, selector: #selector(appBecameActive),
            name: NSApplication.didBecomeActiveNotification, object: nil)
        // Command-Tab or a click elsewhere: the pointer may rest on the meters without moving.
        NSWorkspace.shared.notificationCenter.addObserver(
            self, selector: #selector(otherAppActivated),
            name: NSWorkspace.didActivateApplicationNotification, object: nil)
        watchVisibility()
    }

    @objc private func otherAppActivated() {
        // Back from System Settings, Desktop & Dock: the Dock may have moved or stopped hiding.
        dock.refreshPrefs()
        updateMouseThrough()
    }

    /// The Dock's reach changed (DockFollower): the corners move, sliding with the auto-hiding
    /// Dock (no slide with Reduce Motion, which DockFollower passes as no animation).
    private func applyDock(_ insets: DockInsets, animation: Animation?) {
        guard model.dockInsets != insets else { return }
        if let animation {
            withAnimation(animation) { model.dockInsets = insets }
        } else {
            model.dockInsets = insets
        }
    }

    // MARK: Out of sight (item 54: the message's {shimmer} rests)

    private var screensAsleep = false
    private var screenSaver = false
    private var sessionAway = false
    private static let screenSaverStarted = Notification.Name("com.apple.screensaver.didstart")
    private static let screenSaverStopped = Notification.Name("com.apple.screensaver.didstop")

    private func watchVisibility() {
        let ws = NSWorkspace.shared.notificationCenter
        for name in [NSWorkspace.screensDidSleepNotification, NSWorkspace.screensDidWakeNotification,
                     NSWorkspace.sessionDidResignActiveNotification, NSWorkspace.sessionDidBecomeActiveNotification] {
            ws.addObserver(self, selector: #selector(visibilityEvent(_:)), name: name, object: nil)
        }
        let dist = DistributedNotificationCenter.default()
        dist.addObserver(self, selector: #selector(visibilityEvent(_:)), name: Self.screenSaverStarted, object: nil)
        dist.addObserver(self, selector: #selector(visibilityEvent(_:)), name: Self.screenSaverStopped, object: nil)
    }

    @objc private func visibilityEvent(_ note: Notification) {
        switch note.name {
        case NSWorkspace.screensDidSleepNotification: screensAsleep = true
        case NSWorkspace.screensDidWakeNotification: screensAsleep = false
        case NSWorkspace.sessionDidResignActiveNotification: sessionAway = true
        case NSWorkspace.sessionDidBecomeActiveNotification: sessionAway = false
        case Self.screenSaverStarted: screenSaver = true
        case Self.screenSaverStopped: screenSaver = false
        default: break
        }
        updateMotion()
    }

    @objc private func occlusionChanged() { updateMotion() }

    private func updateMotion() {
        let visible = window.map { $0.occlusionState.contains(.visible) } ?? false
        let paused = MessageMotion.paused(deskVisible: visible, screensAsleep: screensAsleep,
                                          screenSaver: screenSaver, sessionAway: sessionAway)
        if model.motionPaused != paused {
            model.motionPaused = paused
            model.updateCycle()   // the turns rest while nobody can see them (item 69)
        }
    }

    @objc private func appBecameActive() {
        recheckCalendar()
        updateMouseThrough()
    }

    /// An alert chose the Desk (or the debug pulse): pulse those meters once and glow the notch,
    /// whatever the Glow switches, so a pulse has the one notch glow (item 27).
    @MainActor func pulse(_ tiers: Set<Tier>) {
        model.pulse(tiers)
        NotchGlowController.shared.fire()
    }

    /// Reads Calendar access again (Settings opening, Sanduhr becoming active), while Desk runs.
    func recheckCalendar() {
        if running { model.recheckCalendar() }
    }

    private func stop() {
        // Arrange mode ends with Desk; nothing was saved, so the layout stays as it was.
        endArrange(keep: false)
        running = false
        window?.close(); window = nil
        wingsWindow?.close(); wingsWindow = nil
        wingsTimer?.invalidate(); wingsTimer = nil
        mouseMonitors.forEach { NSEvent.removeMonitor($0) }
        mouseMonitors = []
        watchApproach(false)
        model.onHitAreasChange = nil
        dock.stop()
        dock.apply = nil
        model.dockInsets = DockInsets()
        setMenuIcon(false)
        model.stop()
        applyHotKeys()
        NotificationCenter.default.removeObserver(self)
        NSWorkspace.shared.notificationCenter.removeObserver(self)
        DistributedNotificationCenter.default().removeObserver(self)
        screensAsleep = false; screenSaver = false; sessionAway = false
    }

    /// Called at launch, when Desk starts or stops, when either shortcut's switch flips and when
    /// its keys change (General, Shortcuts). Each registers its saved keys while its switch is on,
    /// with or without the Desk.
    func applyHotKeys() {
        let desk = UserDefaults.desk
        let wanted = hotKeysSuspended ? [] : SanduhrHotKeys.Shortcut.allCases
            .filter { SanduhrHotKeys.isOn($0, in: desk) }
            .map { HotKeyRequest(shortcut: $0, combo: SanduhrHotKeys.combo($0, in: desk)) }
        guard wanted != registeredShortcuts else { return }
        registeredShortcuts = wanted
        var taken: Set<SanduhrHotKeys.Shortcut> = []
        if wanted.isEmpty {
            hotKeys.unregister()
        } else {
            let failed = hotKeys.register(wanted.map { r -> DeskHotKeys.Binding in
                switch r.shortcut {
                case .join: DeskHotKeys.Binding(keyCode: r.combo.keyCode, modifiers: r.combo.modifiers) { [weak self] in self?.joinNext() }
                case .settings: DeskHotKeys.Binding(keyCode: r.combo.keyCode, modifiers: r.combo.modifiers) { [weak self] in self?.showSettings() }
                }
            })
            taken = Set(failed.map { wanted[$0].shortcut })
        }
        // Suspended for the recorder, the last answer stands.
        if !hotKeysSuspended, taken != takenShortcuts {
            takenShortcuts = taken
            NotificationCenter.default.post(name: .sanduhrHotKeysDidRegister, object: nil)
        }
    }

    @objc private func screensChanged() { if running { buildWindow() } }

    /// One transparent, click-through window covering the main screen, above the desktop
    /// icons and below every app window.
    private func buildWindow() {
        if let old = window {
            NotificationCenter.default.removeObserver(self, name: NSWindow.didChangeOcclusionStateNotification, object: old)
        }
        window?.close()
        guard let screen = NSScreen.main else { return }
        let w = DeskWindow(contentRect: screen.frame, styleMask: [.borderless],
                           backing: .buffered, defer: false)
        w.isReleasedWhenClosed = false
        // Arrange mode carries over a rebuild (a display change): the window can still take the key.
        w.takesKey = model.arrange.active
        // Just below normal windows, not in the desktop layer: on newer macOS a click on the
        // desktop layer counts as clicking the wallpaper, which sweeps the windows away
        // instead of reaching the meeting row.
        w.level = NSWindow.Level(rawValue: NSWindow.Level.normal.rawValue - 1)
        w.collectionBehavior = [.canJoinAllSpaces, .stationary, .ignoresCycle]
        w.isOpaque = false
        w.backgroundColor = .clear
        w.hasShadow = false
        w.ignoresMouseEvents = true
        model.topInset = screen.frame.maxY - screen.visibleFrame.maxY
        model.notchRect = screen.cameraNotch
        dock.screenChanged(screen)
        buildWingsWindow(on: screen)
        w.contentView = FirstClickHostingView(rootView: DeskView(model: model))
        w.setFrame(screen.frame, display: true)
        w.orderFront(nil)
        window = w
        // The bar's panel follows the Desk to its new screen.
        if model.arrange.active { arrangeBar.place(on: screen) }
        NotificationCenter.default.addObserver(
            self, selector: #selector(occlusionChanged),
            name: NSWindow.didChangeOcclusionStateNotification, object: w)
        updateMotion()
        // A pointer already resting near the Desk is watched from the start, before any movement;
        // once the frames arrive (onHitAreasChange) it takes the mouse over a click area.
        updateMouseThrough()
        DispatchQueue.main.async { [weak self] in self?.updateMouseThrough() }
    }

    /// A menu-bar-tall, click-through window over the notch, one level above the menu bar,
    /// that carries the island's wings (see NotchWingsView).
    private func buildWingsWindow(on screen: NSScreen) {
        wingsWindow?.close()
        wingsWindow = nil
        guard let notch = model.notchRect else { return }
        // The menu bar can be a point taller than the notch (39 vs 38 on a 14-inch MacBook Pro);
        // wings only as tall as the notch leave its bottom row showing under them.
        let barHeight = max(notch.height, model.topInset)
        // The wing itself, the camera and mic indicators beside the camera (item 67) and the flare.
        let pad = NotchWingsView.windowPad(notchHeight: notch.height)
        let frame = NSRect(x: screen.frame.minX + notch.minX - pad,
                           y: screen.frame.maxY - barHeight,
                           width: notch.width + pad * 2, height: barHeight)
        let w = NSWindow(contentRect: frame, styleMask: [.borderless], backing: .buffered, defer: false)
        w.isReleasedWhenClosed = false
        // Above the menu bar and above menu bar tools that keep their own overlay on top of it
        // (Ice's tint re-raises itself, which is what hid the wings after a second).
        w.level = .popUpMenu
        w.collectionBehavior = [.canJoinAllSpaces, .stationary, .ignoresCycle, .fullScreenAuxiliary]
        w.isOpaque = false
        w.backgroundColor = .clear
        w.hasShadow = false
        // Clickable (the island opens settings); clicks on its transparent edges fall through.
        w.ignoresMouseEvents = false
        w.contentView = FirstClickHostingView(rootView: NotchWingsView(model: model, notchWidth: notch.width, notchHeight: notch.height, barHeight: barHeight))
        w.setFrame(frame, display: true)
        w.orderFrontRegardless()
        wingsWindow = w
        // Keep winning: re-front every few seconds and whenever the Space changes.
        wingsTimer?.invalidate()
        wingsTimer = Timer.scheduledTimer(withTimeInterval: 3, repeats: true) { [weak self] _ in
            self?.wingsWindow?.orderFrontRegardless()
        }
    }

    @objc private func spaceChanged() {
        dock.refreshPrefs()
        wingsWindow?.orderFrontRegardless()
        updateMouseThrough()
    }

    /// The window ignores the mouse, except while the pointer is over a Desk element that takes
    /// clicks (DeskHitTest), so the desktop and its icons keep working and those can be clicked.
    private func watchMouse() {
        let moved: (NSEvent) -> Void = { [weak self] _ in self?.updateMouseThrough() }
        if let g = NSEvent.addGlobalMonitorForEvents(matching: [.mouseMoved], handler: moved) {
            mouseMonitors.append(g)
        }
        if let l = NSEvent.addLocalMonitorForEvents(matching: [.mouseMoved], handler: { event in
            moved(event); return event
        }) {
            mouseMonitors.append(l)
        }
        // A drag ends (DeskPointerDrag): the window was held as it was while the button was down,
        // so it catches up with the pointer now, wherever the drop landed.
        let released: (NSEvent) -> Void = { [weak self] _ in
            DispatchQueue.main.async { [weak self] in self?.updateMouseThrough() }
        }
        if let g = NSEvent.addGlobalMonitorForEvents(matching: [.leftMouseUp, .rightMouseUp], handler: released) {
            mouseMonitors.append(g)
        }
        if let l = NSEvent.addLocalMonitorForEvents(matching: [.leftMouseUp, .rightMouseUp], handler: { event in
            released(event); return event
        }) {
            mouseMonitors.append(l)
        }
        // Clicks: whichever app macOS gives the click to, if it landed on a meeting row with a
        // link, open the meeting (DeskHitTest decides what is under the pointer). A plain click
        // on the meters does nothing. A click that reaches Desk itself is consumed.
        if let g = NSEvent.addGlobalMonitorForEvents(matching: [.leftMouseDown], handler: { [weak self] event in
            // Another app's window over the clock means the click was meant for that app.
            guard let self, !self.model.arrange.active, !Self.appWindowCoversPointer() else { return }
            // A control-click is a two-finger click.
            if event.modifierFlags.contains(.control) { self.limitMenuAfterMissedClick(); return }
            _ = self.clickUnderPointer()
        }) {
            mouseMonitors.append(g)
        }
        // A two-finger click that still went to another app (the desktop) over the meters.
        if let g = NSEvent.addGlobalMonitorForEvents(matching: [.rightMouseDown], handler: { [weak self] _ in
            guard let self, !self.model.arrange.active else { return }
            self.limitMenuAfterMissedClick()
        }) {
            mouseMonitors.append(g)
        }
        if let l = NSEvent.addLocalMonitorForEvents(matching: [.leftMouseDown], handler: { [weak self] event in
            // Same process as the widget and its settings: only clicks on the desk layer count.
            guard let self, event.window == nil || event.window === self.window else { return event }
            // Arrange mode: the click is the pieces' and the bar's (SwiftUI's gestures), never a join.
            if self.model.arrange.active { return event }
            // Control-click is a two-finger click.
            if event.modifierFlags.contains(.control) { return self.limitMenuUnderPointer(event) ? nil : event }
            return self.clickUnderPointer() ? nil : event
        }) {
            mouseMonitors.append(l)
        }
        // Two-finger clicks: on the meters, the limit menu. The window takes the mouse while the
        // pointer is over the meters and the hit plate behind them (DeskPointerMenu) gives every
        // point there a drawn pixel, so the click reaches Desk and the desktop never sees it.
        if let l = NSEvent.addLocalMonitorForEvents(matching: [.rightMouseDown], handler: { [weak self] event in
            guard let self, event.window == nil || event.window === self.window else { return event }
            if self.model.arrange.active { return event }
            return self.limitMenuUnderPointer(event) ? nil : event
        }) {
            mouseMonitors.append(l)
        }
        // The meters moved under a still pointer (a limit came or went, a font change): take the
        // mouse or let it through again without waiting for the pointer to move.
        model.onHitAreasChange = { [weak self] in self?.updateMouseThrough() }
    }

    /// A two-finger click over the meters that went to another app anyway (the window had not
    /// taken the mouse yet): Desk opens the limit menu itself, a moment later, unless an app
    /// window covers the meters or that app opened its own menu (DeskPointerMenu.fallbackOpens).
    /// The window then takes the mouse, so the next click reaches Desk directly.
    private func limitMenuAfterMissedClick() {
        guard DeskHitTest.hasMenu(elementUnderPointer()), !Self.appWindowCoversPointer() else { return }
        updateMouseThrough()
        DispatchQueue.main.asyncAfter(deadline: .now() + DeskPointerMenu.fallbackDelay) { [weak self] in
            guard let self, let w = self.window, let view = w.contentView else { return }
            let hit = self.elementUnderPointer()
            guard DeskPointerMenu.fallbackOpens(overMeters: DeskHitTest.hasMenu(hit),
                                                appWindowCovers: Self.appWindowCoversPointer(),
                                                otherMenuOpen: Self.otherAppMenuOpen()) else { return }
            let at = view.convert(w.mouseLocationOutsideOfEventStream, from: nil)
            self.menu(for: hit).popUp(positioning: nil, at: at, in: view)
        }
    }

    /// True when another app has a menu open (a window on the pop-up menu layer). Window list
    /// metadata only: no Screen Recording permission needed.
    private static func otherAppMenuOpen() -> Bool {
        let menuLayer = Int(CGWindowLevelForKey(.popUpMenuWindow))
        let mine = ProcessInfo.processInfo.processIdentifier
        guard let list = CGWindowListCopyWindowInfo([.optionOnScreenOnly], kCGNullWindowID) as? [[String: Any]] else { return false }
        return list.contains {
            ($0[kCGWindowLayer as String] as? Int) == menuLayer && ($0[kCGWindowOwnerPID as String] as? Int32) != mine
        }
    }

    /// The Desk element under the pointer (DeskHitTest), nil where Desk lets the mouse through.
    private func elementUnderPointer() -> DeskElement? {
        guard let point = pointerInWindow() else { return nil }
        return DeskHitTest.element(at: point, in: model.elements())
    }

    /// A two-finger click on the meters opens the limit menu for the row under the pointer
    /// (LimitMenu): Accounts, Hide, the warnings item, Alerts Settings…, then the shared menu.
    /// On now playing (the Desk line or the strip under the camera), its menu instead.
    private func limitMenuUnderPointer(_ event: NSEvent) -> Bool {
        let hit = elementUnderPointer()
        guard let view = window?.contentView, DeskHitTest.hasMenu(hit) else { return false }
        NSMenu.popUpContextMenu(menu(for: hit), with: event, for: view)
        return true
    }

    /// The two-finger menu for a hit: now playing's, a watcher's, the indicators', the shared menu
    /// on the meetings and the account name, or the limit menu on the meters. Every one of them
    /// has Arrange Desk… (item 60): the shared menu carries it, the others end with it.
    private func menu(for hit: DeskElement?) -> NSMenu {
        if hit?.kind == .watcher {
            return withArrangeItem(WatcherMenu.menu(for: DeskHitTest.watcher(hit, in: model.watchers)?.id))
        }
        if hit?.kind == .avIndicators { return withArrangeItem(AVIndicatorMenu.menu(model.avIndicators)) }
        if hit?.kind == .nowPlaying || hit?.kind == .nowPlayingNext {
            return withArrangeItem(NowPlayingController.shared.menu())
        }
        if DeskHitTest.hasSharedMenu(hit) {
            let menu = NSMenu()
            // The clock's Desk Look Settings… and the message's Edit Messages… (DeskPieceClicks), above
            // the shared items, whose Settings… then reads All Settings….
            let extra = hit.flatMap { DeskPieceClicks.menuItem($0.kind) }
            if let extra {
                let item = NSMenuItem(title: extra.title, action: #selector(pieceSettingsFromMenu(_:)), keyEquivalent: "")
                item.target = self
                item.representedObject = extra.section.rawValue
                menu.addItem(item)
                menu.addItem(.separator())
            }
            addStandardItems(to: menu, allSettings: extra != nil)
            return menu
        }
        return limitMenu(for: hit)
    }

    /// Desk Look Settings… (the clock's menu) or Edit Messages… (the message's): Settings at that page.
    @objc private func pieceSettingsFromMenu(_ sender: NSMenuItem) {
        guard let raw = sender.representedObject as? String, let page = SettingsSection.resolve(raw) else { return }
        MainActor.assumeIsolated { SettingsWindowController.shared.show(page.section, anchor: page.anchor) }
    }

    /// "Arrange Desk…" (item 60), at the end of a Desk menu that has no shared items.
    private func withArrangeItem(_ menu: NSMenu) -> NSMenu {
        menu.addItem(.separator())
        menu.addItem(arrangeMenuItem())
        return menu
    }

    private func arrangeMenuItem() -> NSMenuItem {
        let item = NSMenuItem(title: DeskArrangeCopy.menuItem, action: #selector(arrangeFromMenu), keyEquivalent: "")
        item.target = self
        return item
    }

    /// After the menu has closed, so the window takes the mouse and the key with nothing tracking.
    @objc private func arrangeFromMenu() {
        DispatchQueue.main.async { [weak self] in self?.arrangeDesk() }
    }

    /// Pops `menu` at the pointer in the Desk window.
    private func popUpAtPointer(_ menu: NSMenu) {
        guard let w = window, let view = w.contentView else { return }
        menu.popUp(positioning: nil, at: view.convert(w.mouseLocationOutsideOfEventStream, from: nil), in: view)
    }

    /// The limit menu for a hit on the meters: that row's limit, or none beside the rows.
    private func limitMenu(for hit: DeskElement?) -> NSMenu {
        let tier = hit?.kind == .meterRow ? hit?.key.flatMap(Tier.init(rawValue:)) : nil
        let menu = NSMenu()
        // Event monitors and their follow-ups run on the main thread. Arrange Desk… comes with
        // the shared items under the limit's own.
        MainActor.assumeIsolated { (NSApp.delegate as? AppDelegate)?.addLimitMenuItems(to: menu, tier: tier) }
        return menu
    }

    /// Pointer position in the window's SwiftUI coordinates (top-left origin).
    private func pointerInWindow() -> CGPoint? {
        guard let w = window else { return nil }
        let p = w.convertPoint(fromScreen: NSEvent.mouseLocation)
        return CGPoint(x: p.x, y: w.frame.height - p.y)
    }

    /// The Desk window's size, for checking the frames (DeskFrameCheck); nil while Desk is off.
    var windowSize: CGSize? { window?.frame.size }

    /// True when a normal app window (layer 0) is under the pointer. Desk sits at layer -1.
    private static func appWindowCoversPointer() -> Bool {
        guard let screen = NSScreen.screens.first else { return false }
        let m = NSEvent.mouseLocation
        let point = CGPoint(x: m.x, y: screen.frame.height - m.y)   // CoreGraphics: top-left origin
        let mine = ProcessInfo.processInfo.processIdentifier
        guard let list = CGWindowListCopyWindowInfo([.optionOnScreenOnly, .excludeDesktopElements],
                                                    kCGNullWindowID) as? [[String: Any]] else { return false }
        for info in list {
            guard (info[kCGWindowLayer as String] as? Int) == 0,
                  (info[kCGWindowOwnerPID as String] as? Int32) != mine,
                  let b = info[kCGWindowBounds as String] as? [String: CGFloat],
                  let x = b["X"], let y = b["Y"], let w = b["Width"], let h = b["Height"] else { continue }
            if CGRect(x: x, y: y, width: w, height: h).contains(point) { return true }
        }
        return false
    }

    /// A left click on whatever DeskHitTest finds under the pointer: a meeting row opens its
    /// link, the calendar note opens System Settings, the account label switches to the next
    /// account (as the widget chip does; a manual switch, so following pauses). The meters are
    /// passive (item 41): a plain click there does nothing at all, and the widget, history and
    /// tools are in their two-finger menu. True when the click was Desk's, so it goes no further.
    private func clickUnderPointer() -> Bool {
        guard let hit = elementUnderPointer() else { return false }
        switch hit.kind {
        case .meetingRow:
            guard let i = hit.key.flatMap(Int.init), model.meetings.indices.contains(i),
                  let link = model.meetings[i].link else { return false }
            MeetingOpener.open(link)
        case .note:
            model.openCalendarSettings()
        case .account:
            // Event monitors run on the main thread.
            MainActor.assumeIsolated { (NSApp.delegate as? AppDelegate)?.viewModel.cycleAccount() }
        case .nowPlaying:
            NowPlayingController.shared.togglePlayPause()
        case .nowPlayingNext:
            NowPlayingController.shared.next()
        case .watcher:
            // Its link, https only; a watcher without one takes the click and does nothing (the
            // window holds the mouse there for the two-finger menu).
            WatcherMenu.open(DeskHitTest.watcher(hit, in: model.watchers))
        case .avIndicators:
            // Read-only: a click opens the same menu as a two-finger click, a moment later so it
            // doesn't track inside the event monitor.
            DispatchQueue.main.async { [weak self] in
                guard let self else { return }
                self.popUpAtPointer(AVIndicatorMenu.menu(self.model.avIndicators))
            }
        case .meters, .meterRow:
            // The window holds the mouse over the meters so a two-finger click reaches the limit
            // menu; a plain click is swallowed there, so nothing reacts to it.
            break
        case .clock, .message, .claudeLine:
            // DeskPieceClicks: the clock opens Settings, Desk, Look, the message Settings, Desk,
            // Message; the claude line, like the meters, takes the click and does nothing.
            if let section = DeskPieceClicks.plainClick(hit.kind) {
                DispatchQueue.main.async { [weak self] in self?.showSettings(section) }
            }
        case .meetings:
            return false
        }
        return true
    }

    /// The limit menu's Show Widget: a hidden widget comes back beside the meters, or where it
    /// was when the meters are not drawn.
    func showWidgetBesideMeters() {
        guard let w = window, !model.metersFrame.isEmpty else {
            MainActor.assumeIsolated { (NSApp.delegate as? AppDelegate)?.showPanel() }
            return
        }
        let f = model.metersFrame
        let onScreen = CGRect(x: w.frame.minX + f.minX, y: w.frame.maxY - f.maxY,
                              width: f.width, height: f.height)
        let screen = w.screen ?? NSScreen.main
        // Menu items act on the main thread.
        MainActor.assumeIsolated { (NSApp.delegate as? AppDelegate)?.showPanel(beside: onScreen, on: screen) }
    }

    /// Takes the mouse over the clickable pieces (DeskHitTest: meeting rows with a link, the
    /// calendar note, the account label, the meters) and lets it through everywhere else. Runs on
    /// every mouse move, when the frames change, when the window appears, on a Space or app
    /// switch, and on the close watch's tick while the pointer is near a block (DeskPointerWatch).
    private func updateMouseThrough() {
        guard let w = window, let point = pointerInWindow() else {
            watchApproach(false)
            return
        }
        // Arrange mode (item 60) takes the whole frame; otherwise only the click areas, as before.
        let arranging = model.arrange.active
        let wanted = DeskArrange.takesMouse(arranging: arranging,
                                            overElement: DeskHitTest.element(at: point, in: model.elements()) != nil)
        // Outside Arrange mode, a button held down (a file dragged from the desktop) keeps the
        // window as it was, so a drag that started elsewhere crosses the plates to the Finder
        // (DeskPointerDrag); the button coming up runs this again.
        let over = arranging ? wanted
            : DeskPointerDrag.takesMouse(wanted: wanted, buttonDown: NSEvent.pressedMouseButtons != 0,
                                         takingNow: !w.ignoresMouseEvents)
        if w.ignoresMouseEvents == over { w.ignoresMouseEvents = !over }
        let blocks = [model.metersFrame, model.accountFrame, model.noteFrame, model.meetingsFrame,
                      model.nowPlayingFrame, model.stripFrame, model.stripNextFrame,
                      model.watchersFrame, model.stripWatcherFrame, model.stripAVFrame]
            + (DeskPieceClicks.isOn(in: UserDefaults.desk) ? [model.clockFrame, model.messageFrame, model.claudeLineFrame] : [])
        nearBlocks = DeskPointerWatch.near(point, frames: blocks)
        // The auto-hiding Dock's edge (item 56) runs the same watch, which reads the window list.
        watchApproach(nearBlocks || dock.wantsWatch(pointer: point, size: w.frame.size))
    }

    /// The close watch's tick: a pointer that has not moved since the last tick needs nothing
    /// for the click areas (layout changes arrive through onHitAreasChange), so a resting pointer
    /// costs one read. Near the auto-hiding Dock's edge the tick also looks for the Dock
    /// (DockFollower, DockWatch), and the watch stops once neither needs it.
    private func watchTick() {
        let p = NSEvent.mouseLocation
        let moved = p != lastWatchedPointer
        if moved {
            lastWatchedPointer = p
            updateMouseThrough()
        }
        guard approachTimer != nil, let w = window else { return }
        let point = pointerInWindow()
        dock.tick(pointer: point, size: w.frame.size, moved: moved)
        if !nearBlocks, !dock.wantsWatch(pointer: point, size: w.frame.size) { watchApproach(false) }
    }

    /// Starts the close watch when `on` and none runs, stops it when off. Common run loop modes,
    /// so it keeps ticking while a menu tracks or a window drags.
    private func watchApproach(_ on: Bool) {
        if !on {
            approachTimer?.invalidate()
            approachTimer = nil
            lastWatchedPointer = nil
            return
        }
        guard approachTimer == nil else { return }
        let t = Timer(timeInterval: DeskPointerWatch.interval, repeats: true) { [weak self] _ in
            self?.watchTick()
        }
        t.tolerance = DeskPointerWatch.interval / 2
        RunLoop.main.add(t, forMode: .common)
        approachTimer = t
    }

    // MARK: Arrange mode (item 60)

    private var arrangeKeyMonitor: Any?
    /// The bar with Cancel and Done, in its own floating panel while arranging.
    private let arrangeBar = DeskArrangeBarController()
    /// The windows put away while arranging (Settings), brought back when it ends.
    private var arrangeStash: DeskArrangeStash?

    /// state.yaml's `desk_arrange.click_through`: `whole` while arranging with the window taking
    /// the mouse, `drawn` otherwise.
    var clickThrough: String {
        DeskArrange.clickThrough(arranging: model.arrange.active, windowTakesMouse: window.map { !$0.ignoresMouseEvents } ?? false)
    }

    /// state.yaml's `desk_arrange.bar_visible`: the bar's panel is on screen.
    var arrangeBarVisible: Bool { arrangeBar.isVisible }

    /// "Arrange Desk…" in every Desk menu, the menu bar item's and the widget's menus, and on
    /// Settings, Desk, Layout: the pieces get outlines and handles and the window takes clicks over
    /// the whole screen. Settings steps aside until it ends, and the bar with Cancel and Done floats
    /// above every window at the middle of the Desk's screen, taking the key: Return is Done,
    /// Escape is Cancel, wherever the key focus is in Sanduhr. Nothing is saved until Done. While
    /// Desk is off, or already arranging, nothing happens.
    func arrangeDesk() {
        guard running, !model.arrange.active, let w = window as? DeskWindow else { return }
        model.arrange.begin()
        w.takesKey = true
        // Arrange runs on the main thread (menus, Settings' button, the debug hooks).
        arrangeStash = MainActor.assumeIsolated {
            let stash = DeskArrangeStash()
            stash.hide([SettingsWindowController.shared.window])
            return stash
        }
        NSApp.activate()
        arrangeBar.show(on: w.screen ?? NSScreen.main)
        arrangeKeyMonitor = NSEvent.addLocalMonitorForEvents(matching: [.keyDown]) { [weak self] event in
            guard let self, self.model.arrange.active,
                  let end = DeskArrange.endKey(event.keyCode) else { return event }
            self.endArrange(keep: end.keep)
            return nil
        }
        updateMouseThrough()
    }

    /// Done (or Return) keeps the edit, writing the layout once; Cancel (or Escape) drops it.
    /// Either way the bar's panel closes, Settings comes back if Arrange put it away, and the
    /// window goes back to taking clicks only where something is drawn.
    func endArrange(keep: Bool) {
        if let m = arrangeKeyMonitor {
            NSEvent.removeMonitor(m)
            arrangeKeyMonitor = nil
        }
        guard model.arrange.active else { return }
        if keep { model.arrange.done() } else { model.arrange.cancel() }
        arrangeBar.close()
        if let w = window as? DeskWindow {
            w.takesKey = false
            w.resignKey()
        }
        if let stash = arrangeStash {
            arrangeStash = nil
            MainActor.assumeIsolated { stash.restore() }
        }
        updateMouseThrough()
    }

    /// estedesk://join-next opens the next meeting's link (the join shortcut does the same, see applyHotKeys).
    /// estedesk://join-next, estedesk://settings (and the same on sanduhr://), forwarded by
    /// the app delegate. settings/<page>#<anchor> opens a page at a section (Settings v2, slice 3);
    /// the host alone opens Settings where it was left.
    func handle(_ url: URL) {
        if url.host == "join-next" { joinNext() }
        if url.host == "desk" { showSettings() }
        if url.host == "settings" {
            let target = SettingsLink.target(url)
            MainActor.assumeIsolated { SettingsWindowController.shared.show(target.section, anchor: target.anchor) }
        }
    }

    private func joinNext() {
        model.refreshEvents()
        if let link = model.nextJoinable?.link { MeetingOpener.open(link) } else { NSSound.beep() }
    }

    @objc private func joinNextFromMenu() { joinNext() }

    @objc private func joinMeeting(_ sender: NSMenuItem) {
        if let link = sender.representedObject as? URL { MeetingOpener.open(link) }
    }

    /// Rebuilt each time it opens: today's meetings first, each one clickable when it has a link.
    func menuNeedsUpdate(_ menu: NSMenu) {
        menu.removeAllItems()
        model.refreshEvents()
        if model.meetings.isEmpty {
            let none = NSMenuItem(title: model.calendarNote ?? model.noMeetingsLine, action: nil, keyEquivalent: "")
            none.isEnabled = false
            menu.addItem(none)
        }
        for meeting in model.meetings {
            let label = meeting.service.map { "\(meeting.time)   \(meeting.title)   (join on \($0))" }
                ?? "\(meeting.time)   \(meeting.title)"
            let row = NSMenuItem(title: label, action: meeting.link == nil ? nil : #selector(joinMeeting(_:)), keyEquivalent: "")
            row.target = self
            row.representedObject = meeting.link
            row.isEnabled = meeting.link != nil
            menu.addItem(row)
        }
        let next = NSMenuItem(title: "Join next meeting", action: #selector(joinNextFromMenu), keyEquivalent: "j")
        next.target = self
        next.isEnabled = model.nextJoinable != nil
        menu.addItem(.separator())
        menu.addItem(next)
        menu.addItem(.separator())
        addStandardItems(to: menu)
    }

    /// The Settings shortcut, …/settings links and the notch island: the one Settings window, at `section`
    /// when given.
    func showSettings(_ section: SettingsSection? = nil) {
        // Hotkeys, links and taps all arrive on the main thread.
        MainActor.assumeIsolated { SettingsWindowController.shared.show(section) }
    }

    /// Settings' "Desk menu in the menu bar" switch.
    func setMenuIcon(_ on: Bool) {
        if on, statusItem == nil { buildMenu() }
        if !on, let item = statusItem {
            NSStatusBar.system.removeStatusItem(item)
            statusItem = nil
        }
    }

    /// The same items as the menu bar item's menu and the widget's menu (SanduhrMenu).
    /// `allSettings` names Settings… "All Settings…", for a menu with a page's own Settings item.
    private func addStandardItems(to menu: NSMenu, allSettings: Bool = false) {
        // Menus are built on the main thread.
        MainActor.assumeIsolated { (NSApp.delegate as? AppDelegate)?.addMenuItems(to: menu, allSettings: allSettings) }
    }

    private func buildMenu() {
        let item = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        item.button?.image = NSImage(systemSymbolName: "clock", accessibilityDescription: "Desk")
        let menu = NSMenu()
        menu.delegate = self
        menuNeedsUpdate(menu)
        item.menu = menu
        statusItem = item
    }
}

/// The Desk window. It never takes the key, except in Arrange mode (item 60), where a click on a
/// piece may make it key; Return and Escape still reach the key monitor from there.
final class DeskWindow: NSWindow {
    var takesKey = false
    override var canBecomeKey: Bool { takesKey }
}

/// Takes the first click instead of spending it on activating Desk, so one click joins.
/// Desk sets these windows' frames itself, so the view never sizes its window: by default a
/// hosting view resizes the window to its content, which shrank the notch wings window to
/// 0 x 0 when the notch was switched off and regrew it above the screen when switched back on.
final class FirstClickHostingView<Content: View>: NSHostingView<Content> {
    required init(rootView: Content) {
        super.init(rootView: rootView)
        sizingOptions = []
    }

    @MainActor @preconcurrency required init?(coder: NSCoder) { fatalError("init(coder:) is not used") }

    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }
}

/// Opens a join link in the meeting's own app when it is installed (Teams, Zoom), so there is
/// no browser tab in between; anything else, or a Mac without the app, goes to the browser.
enum MeetingOpener {
    private static let apps: [(host: String, bundleIDs: [String])] = [
        ("teams.microsoft.com", ["com.microsoft.teams2", "com.microsoft.teams"]),
        ("teams.live.com", ["com.microsoft.teams2", "com.microsoft.teams"]),
        ("zoom.us", ["us.zoom.xos"]),
    ]

    static func open(_ link: URL) {
        let host = link.host ?? ""
        for entry in apps where host.hasSuffix(entry.host) {
            for id in entry.bundleIDs {
                if let app = NSWorkspace.shared.urlForApplication(withBundleIdentifier: id) {
                    let config = NSWorkspace.OpenConfiguration()
                    config.activates = true
                    NSWorkspace.shared.open([link], withApplicationAt: app, configuration: config) { _, error in
                        if error != nil { DispatchQueue.main.async { _ = NSWorkspace.shared.open(link) } }
                    }
                    return
                }
            }
        }
        NSWorkspace.shared.open(link)
    }
}

extension Notification.Name {
    /// The shortcuts registered again; DeskController.takenShortcuts may have changed.
    static let sanduhrHotKeysDidRegister = Notification.Name("sanduhrHotKeysDidRegister")
}
