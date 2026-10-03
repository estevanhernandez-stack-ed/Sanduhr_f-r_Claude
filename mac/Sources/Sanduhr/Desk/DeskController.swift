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
    /// Option+J and Option+S while Desk runs, on by default.
    static let hotKeysKey = "hotKeys"
    private(set) var running = false
    private(set) var window: NSWindow?
    private var statusItem: NSStatusItem?
    private var mouseMonitors: [Any] = []
    private(set) var wingsWindow: NSWindow?
    private var wingsTimer: Timer?
    let model = DeskModel()
    private let hotKeys = DeskHotKeys()

    var enabled: Bool { UserDefaults.desk.bool(forKey: Self.enabledKey) }

    /// Called at launch and whenever the Desk switch flips.
    func apply() {
        if enabled, !running { start() }
        if !enabled, running { stop() }
    }

    private func start() {
        running = true
        // The standalone Desk and Sanduhr Desk apps did this job before; one desk is enough.
        for id in ["com.estevan.desk", "com.626labs.sanduhrdesk"] {
            NSRunningApplication.runningApplications(withBundleIdentifier: id).forEach { $0.terminate() }
        }
        buildWindow()
        // The clock menu is off by default: Sanduhr owns the menu bar, meetings join from the
        // desktop or Option+J. `defaults write com.626labs.sanduhr.desk menuIcon -bool true` brings it back.
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
    }

    @objc private func appBecameActive() { recheckCalendar() }

    /// Reads Calendar access again (Settings opening, Sanduhr becoming active), while Desk runs.
    func recheckCalendar() {
        if running { model.recheckCalendar() }
    }

    private func stop() {
        running = false
        window?.close(); window = nil
        wingsWindow?.close(); wingsWindow = nil
        wingsTimer?.invalidate(); wingsTimer = nil
        mouseMonitors.forEach { NSEvent.removeMonitor($0) }
        mouseMonitors = []
        setMenuIcon(false)
        model.stop()
        applyHotKeys()
        NotificationCenter.default.removeObserver(self)
        NSWorkspace.shared.notificationCenter.removeObserver(self)
    }

    /// Called when Desk starts or stops and when the shortcuts switch flips.
    func applyHotKeys() {
        let wanted = running && (UserDefaults.desk.object(forKey: Self.hotKeysKey) as? Bool ?? true)
        guard wanted != hotKeys.isRegistered else { return }
        if wanted {
            let option = UInt32(optionKey)
            hotKeys.register([
                .init(keyCode: UInt32(kVK_ANSI_J), modifiers: option) { [weak self] in self?.joinNext() },
                .init(keyCode: UInt32(kVK_ANSI_S), modifiers: option) { [weak self] in self?.showSettings() },
            ])
        } else {
            hotKeys.unregister()
        }
    }

    @objc private func screensChanged() { if running { buildWindow() } }

    /// One transparent, click-through window covering the main screen, above the desktop
    /// icons and below every app window.
    private func buildWindow() {
        window?.close()
        guard let screen = NSScreen.main else { return }
        let w = NSWindow(contentRect: screen.frame, styleMask: [.borderless],
                         backing: .buffered, defer: false)
        w.isReleasedWhenClosed = false
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
        buildWingsWindow(on: screen)
        w.contentView = FirstClickHostingView(rootView: DeskView(model: model))
        w.setFrame(screen.frame, display: true)
        w.orderFront(nil)
        window = w
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
        let pad = NotchWingsView.maxWings + 12   // the wing itself plus its flare
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

    @objc private func spaceChanged() { wingsWindow?.orderFrontRegardless() }

    /// The window ignores the mouse, except while the pointer is over the meeting list, the
    /// calendar note or the meters, so the desktop and its icons keep working and those can still be clicked.
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
        // Clicks: whichever app macOS gives the click to, if it landed on a meeting row with a
        // link, open the meeting; on the meters, show the widget. A click that reaches Desk
        // itself is consumed.
        if let g = NSEvent.addGlobalMonitorForEvents(matching: [.leftMouseDown], handler: { [weak self] _ in
            // Another app's window over the clock means the click was meant for that app.
            guard let self, !Self.appWindowCoversPointer() else { return }
            _ = self.clickUnderPointer()
        }) {
            mouseMonitors.append(g)
        }
        if let l = NSEvent.addLocalMonitorForEvents(matching: [.leftMouseDown], handler: { [weak self] event in
            // Same process as the widget and its settings: only clicks on the desk layer count.
            guard let self, event.window == nil || event.window === self.window else { return event }
            return self.clickUnderPointer() ? nil : event
        }) {
            mouseMonitors.append(l)
        }
    }

    /// Pointer position in the window's SwiftUI coordinates (top-left origin).
    private func pointerInWindow() -> CGPoint? {
        guard let w = window else { return nil }
        let p = w.convertPoint(fromScreen: NSEvent.mouseLocation)
        return CGPoint(x: p.x, y: w.frame.height - p.y)
    }

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

    private func joinMeetingUnderPointer() -> Bool {
        guard let point = pointerInWindow() else { return false }
        for meeting in model.meetings {
            guard let link = meeting.link,
                  let frame = model.rowFrames[meeting.id],
                  frame.insetBy(dx: -8, dy: -4).contains(point) else { continue }
            MeetingOpener.open(link)
            return true
        }
        return false
    }

    /// A meeting row first, then the calendar note, then the meters. True when the click was used.
    private func clickUnderPointer() -> Bool {
        joinMeetingUnderPointer() || openCalendarSettingsFromNote() || showWidgetFromMeters()
    }

    /// True when the calendar note is drawn and the pointer is over it.
    private func pointerOverNote(_ point: CGPoint) -> Bool {
        model.calendarNote != nil && !model.noteFrame.isEmpty
            && model.noteFrame.insetBy(dx: -8, dy: -4).contains(point)
    }

    /// A click on the calendar note opens System Settings at Privacy & Security, Calendars.
    private func openCalendarSettingsFromNote() -> Bool {
        guard let point = pointerInWindow(), pointerOverNote(point) else { return false }
        model.openCalendarSettings()
        return true
    }

    /// True when the pointer is over the meters (with a little slack, as for the meeting list).
    private func pointerOverMeters(_ point: CGPoint) -> Bool {
        !model.metersFrame.isEmpty && model.metersFrame.insetBy(dx: -8, dy: -6).contains(point)
    }

    /// A click on the meters ends the hint and shows the widget beside them.
    private func showWidgetFromMeters() -> Bool {
        guard let w = window, let point = pointerInWindow(), pointerOverMeters(point) else { return false }
        model.meterHintDismissed()
        let f = model.metersFrame
        let onScreen = CGRect(x: w.frame.minX + f.minX, y: w.frame.maxY - f.maxY,
                              width: f.width, height: f.height)
        let screen = w.screen ?? NSScreen.main
        // Event monitors run on the main thread.
        MainActor.assumeIsolated { (NSApp.delegate as? AppDelegate)?.showPanel(beside: onScreen, on: screen) }
        return true
    }

    private func updateMouseThrough() {
        guard let w = window, let point = pointerInWindow() else { return }
        let overRows = model.meetings.contains { $0.link != nil }
            && model.meetingsFrame.insetBy(dx: -8, dy: -6).contains(point)
        let over = overRows || pointerOverNote(point) || pointerOverMeters(point)
        if w.ignoresMouseEvents == over { w.ignoresMouseEvents = !over }
    }

    /// estedesk://join-next opens the next meeting's link (Option+J does the same, see applyHotKeys).
    /// estedesk://join-next, estedesk://settings (and the same on sanduhr://), forwarded by
    /// the app delegate.
    func handle(_ url: URL) {
        if url.host == "join-next" { joinNext() }
        if url.host == "settings" || url.host == "desk" { showSettings() }
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
            let none = NSMenuItem(title: model.calendarNote ?? "Nothing else on the calendar today", action: nil, keyEquivalent: "")
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

    /// Option+S, …/settings links and the notch island: the one Settings window.
    func showSettings() {
        // Hotkeys, links and taps all arrive on the main thread.
        MainActor.assumeIsolated { SettingsWindowController.shared.show() }
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
    private func addStandardItems(to menu: NSMenu) {
        // Menus are built on the main thread.
        MainActor.assumeIsolated { (NSApp.delegate as? AppDelegate)?.addMenuItems(to: menu) }
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
