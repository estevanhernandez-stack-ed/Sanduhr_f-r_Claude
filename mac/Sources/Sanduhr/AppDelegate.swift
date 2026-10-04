import AppKit
import Sparkle
import SwiftUI

/// Creates the borderless floating panel + menu bar status item.
/// Runs headless (LSUIElement=YES in Info.plist) so there's no dock icon.
@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {

    let viewModel = UsageViewModel()
    private var panel: FloatingPanel?
    private var statusItem: NSStatusItem?
    private let updaterController = SPUStandardUpdaterController(
        startingUpdater: true, updaterDelegate: nil, userDriverDelegate: nil)
    /// Sparkle's settings and Check Now for Settings, Updates; the same updater the menus use.
    private(set) lazy var updates = UpdaterSettings(controller: updaterController)
    /// Keeps Sanduhr out of App Nap. Its windows sit on the desktop layer, under every app window,
    /// so macOS counts them as hidden and naps the app: the five-minute refresh timer then stops
    /// firing, and after fifteen minutes Desk drops the meters as stale. A fetch every five
    /// minutes costs next to nothing; idle system sleep is still allowed.
    private var appNapActivity: NSObjectProtocol?

    // MARK: Lifecycle

    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.accessory)
        // Tooltips (the theme gallery's descriptions, the widget's buttons) come up a little
        // sooner than AppKit's default of about a second. Registered, so a defaults write wins.
        UserDefaults.standard.register(defaults: ["NSInitialToolTipDelay": 600])
        appNapActivity = ProcessInfo.processInfo.beginActivity(
            options: .userInitiatedAllowingIdleSystemSleep,
            reason: "Refreshes Claude usage every five minutes for the widget and Desk")

        // A brand-new install starts with Desk on and the widget hidden while Desk is on, once it
        // has signed in. Decided once, before the panel shows and before DeskMigration marks the suite.
        let firstRun = DeskFirstRun.run()

        // Build widget panel.
        let hosting = NSHostingController(rootView: RootView(vm: viewModel))
        hosting.view.wantsLayer = true
        hosting.view.layer?.cornerRadius = 14
        hosting.view.layer?.masksToBounds = true

        let panel = FloatingPanel(hosting: hosting)
        self.panel = panel
        // Self-delegate so `windowWillResize` can clamp the height to
        // content — user horizontal drag works, vertical drag snaps back
        // so the window never gains empty space.
        panel.delegate = panel
        placeInTopRightCorner(panel)
        // When the widget shows (Settings, General, Surfaces) decides at launch; with "Always
        // shown", hidden stays hidden across launches: Sanduhr keeps fetching, alerting and
        // writing snapshot.json, so a desktop clock or statusline can show the numbers while
        // the widget itself stays out of the way. Until a saved session key has fetched once
        // (SignInGate; a brand-new install, no key, or a key that never worked) the widget
        // shows for sign-in and the choice takes over after the first successful fetch.
        awaitingSignIn = SignInGate.awaitingSignIn(
            fresh: firstRun == .fresh,
            hasSessionKey: KeychainStore.exists(account: KeychainAccount.sessionKey),
            in: UserDefaults.standard)
        let wasShowing = !UserDefaults.standard.bool(forKey: Self.panelHiddenKey)
        let show = WidgetVisibilityRule.resolve(
            showing: wasShowing, setting: .saved(), deskOn: DeskController.shared.enabled,
            hasSessionKey: !awaitingSignIn, event: .launch)
        if show != wasShowing { UserDefaults.standard.set(!show, forKey: Self.panelHiddenKey) }
        if show {
            panel.makeKeyAndOrderFront(nil)
        }
        fitPanelToContent()

        // Build menu bar status item.
        setupStatusItem()

        // Wire up status-item updates.
        viewModel.onUsageUpdate = { [weak self] in
            self?.renderStatusItem()
            self?.fitPanelToContent()
            // Desk takes the numbers straight from the view model, so its meters move with
            // every refresh even while the widget is hidden (or Desk is off, ready for when it starts).
            guard let vm = self?.viewModel else { return }
            let fetched = vm.usage != nil && (vm.status == .idle || vm.status == .noTiers)
            // Remembered for the next launch: has this key fetched (SignInGate)?
            SignInGate.record(fetched: fetched, needsSignIn: vm.status.needsSignIn,
                              in: UserDefaults.standard)
            // The widget showed for sign-in; once the numbers arrive the choice takes over.
            if self?.awaitingSignIn == true, fetched {
                self?.awaitingSignIn = false
                self?.applyWidgetVisibility(.signedIn)
            }
            // Signed out (Settings, Credentials): the widget shows for sign-in again, as at a
            // launch without a key, until the next successful fetch.
            if vm.status == .signedOut, self?.awaitingSignIn == false {
                self?.awaitingSignIn = true
                self?.applyWidgetVisibility(.signedOut)
            }
            DeskController.shared.model.update(DeskUsage(
                usage: vm.usage, fetchedAt: vm.lastUpdated, signInNeeded: vm.status.needsSignIn))
        }

        // When the user toggles compact mode, resize the panel to fit the
        // (now much smaller) content instead of leaving an empty window.
        NotificationCenter.default.addObserver(
            self, selector: #selector(applyCompactState),
            name: .sanduhrCompactDidChange, object: nil)

        viewModel.bootstrap()
        renderStatusItem()

        // Desk: the desktop layer and the notch, when switched on (Settings, General, Surfaces).
        DeskMigration.run()
        DeskController.shared.apply()
        // The camera light watches the cameras while its switch is on, with or without Desk.
        CameraLightController.shared.apply()
        // The notch glow checks for meetings a minute out while its meetings switch is on.
        NotchGlowController.shared.apply()
    }

    /// estedesk:// and sanduhr:// links (Option+J joins the next meeting, …/settings opens
    /// Sanduhr Settings). sanduhr://debug/… goes to the smoke tools' hooks, which ignore it
    /// unless they are switched on (DebugGate).
    func application(_ application: NSApplication, open urls: [URL]) {
        for url in urls {
            if DebugLink.isDebug(url) {
                DebugHooks.handle(url, app: self)
            } else if ["estedesk", "sanduhr"].contains(url.scheme ?? "") {
                DeskController.shared.handle(url)
            }
        }
    }

    /// The widget is on screen.
    var widgetVisible: Bool { panel?.isVisible ?? false }

    /// True from a launch whose session key has not fetched yet (SignInGate: none saved, never
    /// worked, or a brand-new install's first launch), or from a sign-out, until the first
    /// successful fetch.
    private var awaitingSignIn = false

    /// Desk was switched on or off (DeskController.apply, from the Settings switch or a debug
    /// hook): the When the widget shows choice takes over again.
    func deskDidChange() { applyWidgetVisibility(.deskChanged) }

    /// The When the widget shows picker changed (Settings, General, Surfaces).
    func widgetVisibilityDidChange() { applyWidgetVisibility(.choiceChanged) }

    /// Shows or hides the widget as WidgetVisibilityRule says for `event`; leaves it alone when
    /// the rule does ("Always shown"), so a manual show or hide lasts until the next event.
    private func applyWidgetVisibility(_ event: WidgetVisibilityEvent) {
        guard let show = WidgetVisibilityRule.shouldShow(
            setting: .saved(), deskOn: DeskController.shared.enabled,
            hasSessionKey: !awaitingSignIn, event: event) else { return }
        if !show { hidePanel(); return }
        guard let panel, !panel.isVisible else { return }
        // Comes back without taking focus from whatever flipped Desk (the Settings window).
        UserDefaults.standard.set(false, forKey: Self.panelHiddenKey)
        panel.orderFrontRegardless()
    }

    /// Shrink or grow the panel so its height equals the SwiftUI
    /// content's fitting size. Called after the model signals a change
    /// (`onUsageUpdate`), when the user toggles compact, and at startup.
    @objc func applyCompactState() { fitPanelToContent() }

    /// Single source of truth for panel height: whatever SwiftUI's
    /// `.fixedSize(vertical: true)` reports as the hosting-view's
    /// fittingSize. Keeps the top edge pinned so the widget doesn't appear
    /// to jump when its height changes.
    func fitPanelToContent() {
        guard let panel else { return }
        DispatchQueue.main.async {
            guard let content = panel.contentView else { return }
            let fit = content.fittingSize.height
            guard fit > 0 else { return }
            // Allow the window to match content exactly; user horizontal
            // drags are preserved (width stays whatever it is).
            panel.minSize = NSSize(width: 340, height: fit)
            if abs(panel.frame.height - fit) < 0.5 { return }
            var frame = panel.frame
            let delta = frame.size.height - fit
            frame.origin.y += delta          // pin the top edge
            frame.size.height = fit
            panel.setFrame(frame, display: true, animate: true)
        }
    }

    // LSUIElement apps never get this called, but set it false anyway.
    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool {
        false
    }

    // MARK: Status item

    private func setupStatusItem() {
        let item = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        self.statusItem = item

        if let button = item.button {
            button.image = NSImage(systemSymbolName: "hourglass",
                                   accessibilityDescription: "Sanduhr")
            button.image?.isTemplate = true        // tints to menu bar colour
            button.imagePosition = .imageLeft
            button.title = ""
            // Respond to both mouse buttons so we can distinguish L (toggle)
            // from R (menu). An attached `menu` would intercept every click.
            button.target = self
            button.action = #selector(statusItemClicked(_:))
            button.sendAction(on: [.leftMouseUp, .rightMouseUp])
        }
    }

    /// Re-renders the status item title based on the current usage data.
    func renderStatusItem() {
        guard let button = statusItem?.button else { return }
        let pct = viewModel.highestTier()?.usage.utilization

        if let pct {
            let intPct = Int(pct)
            // Color the number only when urgency is high — keeps the menu
            // bar neutral the rest of the time (HIG preference).
            let color: NSColor
            switch intPct {
            case 90...:   color = NSColor(red: 0.97, green: 0.44, blue: 0.44, alpha: 1) // red
            case 75...89: color = NSColor(red: 0.98, green: 0.58, blue: 0.24, alpha: 1) // orange
            default:      color = .labelColor
            }
            let font = NSFont.monospacedDigitSystemFont(
                ofSize: NSFont.systemFontSize(for: .small), weight: .medium)
            button.attributedTitle = NSAttributedString(
                string: " \(intPct)%",
                attributes: [.foregroundColor: color, .font: font])
        } else {
            // No data yet — just the icon.
            button.attributedTitle = NSAttributedString(string: "")
        }
    }

    // MARK: Actions

    @objc private func statusItemClicked(_ sender: NSStatusBarButton) {
        let event = NSApp.currentEvent
        let rightClick = event?.type == .rightMouseUp
            || (event?.modifierFlags.contains(.control) ?? false)

        if rightClick {
            showStatusMenu(from: sender)
        } else {
            togglePanel()
        }
    }

    private func showStatusMenu(from button: NSStatusBarButton) {
        let menu = NSMenu()
        addMenuItems(to: menu)

        // Briefly attach, pop, detach — so default L-click behavior stays
        // as "toggle panel" rather than "always show menu".
        statusItem?.menu = menu
        button.performClick(nil)
        statusItem?.menu = nil
    }

    /// The shared menu (SanduhrMenu) as AppKit items, for the menu bar item's menu and Desk's
    /// clock menu. The widget's own two-finger menu (RootView) renders the same groups.
    func addMenuItems(to menu: NSMenu) {
        for (i, group) in currentMenu(widgetVisible: panel?.isVisible ?? false).enumerated() {
            if i > 0 { menu.addItem(.separator()) }
            if let header = group.header { menu.addItem(.sectionHeader(title: header)) }
            for entry in group.entries {
                let m = NSMenuItem(title: entry.title, action: #selector(menuItemChosen(_:)),
                                   keyEquivalent: entry.key)
                m.target = self
                m.tag = entry.command.rawValue
                m.state = entry.checked ? .on : .off
                menu.addItem(m)
            }
        }
    }

    /// The shared menu with the tools' current checkmarks.
    func currentMenu(widgetVisible: Bool) -> [MenuGroup] {
        SanduhrMenu.groups(widgetVisible: widgetVisible,
                           deepWork: viewModel.activeTool == .deepWork,
                           pacing: viewModel.pacingPinned,
                           snake: viewModel.activeTool == .snake,
                           cameraLight: CameraLightController.shared.manual)
    }

    @objc private func menuItemChosen(_ sender: NSMenuItem) {
        if let command = MenuCommand(rawValue: sender.tag) { perform(command) }
    }

    /// What every menu's items do. Each tool works with the widget hidden: it shows the widget
    /// first. Chosen again while it shows on a visible widget, a tool turns off. Camera Light
    /// leaves the widget alone.
    func perform(_ command: MenuCommand) {
        let visible = panel?.isVisible ?? false
        switch command {
        case .showHide: showOrHidePanel()
        case .deepWork: toggleTool(.deepWork, visible: visible)
        case .snake: toggleTool(.snake, visible: visible)
        case .cameraLight: CameraLightController.shared.toggleManual()
        case .pacing:
            if viewModel.pacingPinned && visible {
                viewModel.pacingPinned = false
            } else {
                viewModel.pacingPinned = true
                showPanel()
            }
        case .refresh: refreshNow()
        case .settings: SettingsWindowController.shared.show()
        case .checkForUpdates: updaterController.checkForUpdates(nil)
        case .quit: NSApp.terminate(nil)
        }
    }

    private func toggleTool(_ tool: UsageViewModel.WidgetTool, visible: Bool) {
        if viewModel.activeTool == tool && visible {
            viewModel.activeTool = nil
        } else {
            showPanel()
            viewModel.activeTool = tool
        }
    }

    @objc func togglePanel() {
        guard let panel else { return }
        // Status-item left-click UX:
        //   • Hidden  → show + bring to front.
        //   • Visible and active → hide.
        //   • Visible but behind other windows → bring to front (don't hide).
        //
        // NSApp.activate is required because the panel is a `.nonactivating`
        // NSPanel in an `.accessory` app — makeKeyAndOrderFront on its own
        // won't raise the window above other apps' windows if Sanduhr isn't
        // the frontmost process.
        if panel.isVisible && panel.isKeyWindow {
            panel.orderOut(nil)
            UserDefaults.standard.set(true, forKey: Self.panelHiddenKey)
        } else {
            UserDefaults.standard.set(false, forKey: Self.panelHiddenKey)
            NSApp.activate(ignoringOtherApps: true)
            panel.makeKeyAndOrderFront(nil)
            panel.orderFrontRegardless()
        }
    }

    @objc func hidePanel() {
        panel?.orderOut(nil)
        UserDefaults.standard.set(true, forKey: Self.panelHiddenKey)
    }

    /// Shows the widget where it was and brings it forward. Unlike togglePanel, never hides it.
    func showPanel() {
        guard let panel else { return }
        UserDefaults.standard.set(false, forKey: Self.panelHiddenKey)
        NSApp.activate(ignoringOtherApps: true)
        panel.makeKeyAndOrderFront(nil)
        panel.orderFrontRegardless()
    }

    /// A click on the Desk meters: a hidden widget comes back beside them (`meters` is their
    /// frame on screen); a widget already showing is only brought forward.
    func showPanel(beside meters: CGRect, on screen: NSScreen?) {
        if let panel, !panel.isVisible, let screen {
            let frame = DeskPanelPlacement.frame(beside: meters, size: panel.frame.size,
                                                 screen: screen.frame, visible: screen.visibleFrame)
            panel.setFrame(frame, display: false)
        }
        showPanel()
    }

    /// The menus' first item: "Hide Widget" when the widget shows, "Show Widget" when it doesn't.
    @objc func showOrHidePanel() {
        if panel?.isVisible ?? false { hidePanel() } else { showPanel() }
    }

    static let panelHiddenKey = "panelHidden"

    /// `open -a Sanduhr` (or a launcher, or clicking it in Applications) while it is
    /// already running toggles the widget, so a hidden Sanduhr is one command away.
    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        togglePanel()
        return false
    }

    @objc func refreshNow() {
        Task { await viewModel.refresh() }
    }

    // MARK: Panel placement

    private func placeInTopRightCorner(_ win: NSWindow) {
        // Restore both position AND size if a valid frame was persisted
        // last session. Users who resize the widget expect that to stick.
        if let s = UserDefaults.standard.string(forKey: "windowFrame") {
            let r = NSRectFromString(s)
            if r.size.width >= 340 && r.size.height >= 480,
               NSScreen.screens.contains(where: { $0.frame.intersects(r) }) {
                win.setFrame(r, display: true)
                return
            }
        }
        guard let screen = NSScreen.main else { return }
        let vf = screen.visibleFrame
        let size = win.frame.size
        let origin = CGPoint(x: vf.maxX - size.width - 24,
                             y: vf.maxY - size.height - 24)
        win.setFrameOrigin(origin)
    }
}

// MARK: - Floating Panel

/// Borderless, always-on-top, non-activating panel. Self-delegates so it
/// can clamp vertical drag-resize to the SwiftUI content's fitting size,
/// eliminating the dead-space-below-footer problem while keeping
/// horizontal drag-resize working.
final class FloatingPanel: NSPanel, NSWindowDelegate {
    /// Last Pin state the widget asked for; subtle-mode changes re-apply it.
    private static var pinned = true

    /// Pin floats the widget above every window, except in subtle mode (or Match Desk), where Pin
    /// sets it on the desktop instead: below every app window, above the wallpaper, on every Space.
    static func refreshLevel(pinned newValue: Bool? = nil) {
        if let newValue { pinned = newValue }
        guard let panel = NSApp.windows.compactMap({ $0 as? FloatingPanel }).first else { return }
        let subtle = DisplaySettings.shared.drawsSubtle
        if pinned && subtle {
            panel.isFloatingPanel = false
            panel.level = NSWindow.Level(rawValue: NSWindow.Level.normal.rawValue - 1)
            panel.collectionBehavior = [.canJoinAllSpaces, .stationary, .ignoresCycle]
            panel.hasShadow = false
        } else {
            panel.isFloatingPanel = pinned
            panel.level = pinned ? .floating : .normal
            panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
            panel.hasShadow = !subtle
        }
    }

    func windowWillResize(_ sender: NSWindow, to frameSize: NSSize) -> NSSize {
        let fit = self.contentView?.fittingSize.height ?? frameSize.height
        return NSSize(width: frameSize.width, height: fit)
    }
    init(hosting: NSHostingController<RootView>) {
        // NOTE: deliberately no `.utilityWindow` here — that mask forces
        // always-on-top regardless of `level` / `isFloatingPanel`, which
        // makes the Pin toggle ineffective. `.resizable` enables edge-drag
        // resize so users can make the widget larger or taller.
        let styleMask: NSWindow.StyleMask = [.borderless, .nonactivatingPanel, .resizable]
        let initial = NSRect(x: 0, y: 0, width: 340, height: 520)
        super.init(contentRect: initial,
                   styleMask: styleMask,
                   backing: .buffered,
                   defer: false)
        self.isFloatingPanel = true
        self.level = .floating
        self.hidesOnDeactivate = false
        self.isMovableByWindowBackground = true
        self.backgroundColor = .clear
        self.isOpaque = false
        self.hasShadow = true
        self.titlebarAppearsTransparent = true
        self.titleVisibility = .hidden
        self.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        // Minimum size matches Windows v2.0.4's 380×520 spec, adjusted down
        // 40 pts wide for Mac's tighter intrinsic card layout (340-wide
        // cards already fit the content; we just want enough vertical room
        // for 4-5 tier cards + footer without overflow).
        self.minSize = NSSize(width: 340, height: 480)
        // Subtle mode saved from last time: start on the desktop instead of floating.
        DispatchQueue.main.async { FloatingPanel.refreshLevel() }
        self.standardWindowButton(.closeButton)?.isHidden = true
        self.standardWindowButton(.miniaturizeButton)?.isHidden = true
        self.standardWindowButton(.zoomButton)?.isHidden = true

        let root = hosting.view
        root.autoresizingMask = [.width, .height]
        self.contentView = root

        // Persist both moves and resizes so the frame survives a quit.
        let center = NotificationCenter.default
        center.addObserver(self, selector: #selector(persistFrame),
                           name: NSWindow.didMoveNotification, object: self)
        center.addObserver(self, selector: #selector(persistFrame),
                           name: NSWindow.didResizeNotification, object: self)
    }

    @objc private func persistFrame() {
        guard frame.size.width >= 340, frame.size.height >= 480 else { return }
        UserDefaults.standard.set(NSStringFromRect(frame), forKey: "windowFrame")
    }

    override var canBecomeKey: Bool { true }
    override var canBecomeMain: Bool { false }
}
