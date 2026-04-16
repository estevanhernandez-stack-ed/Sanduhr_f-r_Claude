import AppKit
import SwiftUI

/// Creates the borderless floating panel + menu bar status item.
/// Runs headless (LSUIElement=YES in Info.plist) so there's no dock icon.
@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {

    let viewModel = UsageViewModel()
    private var panel: FloatingPanel?
    private var statusItem: NSStatusItem?

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
        panel.makeKeyAndOrderFront(nil)
        placeInTopRightCorner(panel)

        // Build menu bar status item.
        setupStatusItem()

        // Wire up status-item updates.
        viewModel.onUsageUpdate = { [weak self] in
            self?.renderStatusItem()
        }

        viewModel.bootstrap()
        renderStatusItem()
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
        if panel.isVisible {
            panel.orderOut(nil)
        } else {
            panel.makeKeyAndOrderFront(nil)
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
        if let s = UserDefaults.standard.string(forKey: "windowFrame") {
            let r = NSRectFromString(s)
            if r.size.width >= 400 && r.size.height >= 240,
               NSScreen.screens.contains(where: { $0.frame.intersects(r) }) {
                win.setFrameOrigin(r.origin)
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

/// Borderless, always-on-top, non-activating panel.
final class FloatingPanel: NSPanel {
    init(hosting: NSHostingController<RootView>) {
        // NOTE: deliberately no `.utilityWindow` here — that mask forces
        // always-on-top regardless of `level` / `isFloatingPanel`, which
        // makes the Pin toggle ineffective.
        let styleMask: NSWindow.StyleMask = [.borderless, .nonactivatingPanel]
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
        self.minSize = NSSize(width: 340, height: 240)
        self.standardWindowButton(.closeButton)?.isHidden = true
        self.standardWindowButton(.miniaturizeButton)?.isHidden = true
        self.standardWindowButton(.zoomButton)?.isHidden = true

        let root = hosting.view
        root.autoresizingMask = [.width, .height]
        self.contentView = root

        NotificationCenter.default.addObserver(
            self, selector: #selector(persistFrame),
            name: NSWindow.didMoveNotification, object: self)
    }

    @objc private func persistFrame() {
        guard frame.size.width >= 400, frame.size.height >= 240 else { return }
        UserDefaults.standard.set(NSStringFromRect(frame), forKey: "windowFrame")
    }

    override var canBecomeKey: Bool { true }
    override var canBecomeMain: Bool { false }
}
