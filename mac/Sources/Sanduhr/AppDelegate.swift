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

    // MARK: Lifecycle

    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.accessory)

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
        panel.makeKeyAndOrderFront(nil)
        placeInTopRightCorner(panel)
        fitPanelToContent()

        // Build menu bar status item.
        setupStatusItem()

        // Wire up status-item updates.
        viewModel.onUsageUpdate = { [weak self] in
            self?.renderStatusItem()
            self?.fitPanelToContent()
        }

        // When the user toggles compact mode, resize the panel to fit the
        // (now much smaller) content instead of leaving an empty window.
        NotificationCenter.default.addObserver(
            self, selector: #selector(applyCompactState),
            name: .sanduhrCompactDidChange, object: nil)

        viewModel.bootstrap()
        renderStatusItem()
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

        let toggleTitle = (panel?.isVisible ?? false) ? "Hide Sanduhr" : "Show Sanduhr"
        menu.addItem(item(toggleTitle, action: #selector(togglePanel)))
        menu.addItem(item("Refresh Now", action: #selector(refreshNow), key: "r"))
        menu.addItem(.separator())
        menu.addItem(item("Credentials…", action: #selector(openCredentials)))
        menu.addItem(.separator())
        let updatesItem = NSMenuItem(
            title: "Check for Updates…",
            action: #selector(SPUStandardUpdaterController.checkForUpdates(_:)),
            keyEquivalent: "")
        updatesItem.target = updaterController
        menu.addItem(updatesItem)
        menu.addItem(.separator())
        menu.addItem(item("Quit Sanduhr", action: #selector(NSApplication.terminate(_:)),
                          key: "q", target: NSApp))

        // Briefly attach, pop, detach — so default L-click behavior stays
        // as "toggle panel" rather than "always show menu".
        statusItem?.menu = menu
        button.performClick(nil)
        statusItem?.menu = nil
    }

    private func item(_ title: String, action: Selector, key: String = "",
                      target: AnyObject? = nil) -> NSMenuItem {
        let m = NSMenuItem(title: title, action: action, keyEquivalent: key)
        m.target = target ?? self
        return m
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
        } else {
            NSApp.activate(ignoringOtherApps: true)
            panel.makeKeyAndOrderFront(nil)
            panel.orderFrontRegardless()
        }
    }

    @objc func hidePanel() {
        panel?.orderOut(nil)
    }

    @objc func refreshNow() {
        Task { await viewModel.refresh() }
    }

    @objc func openCredentials() {
        panel?.makeKeyAndOrderFront(nil)
        viewModel.requestSettingsSheet = true
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
