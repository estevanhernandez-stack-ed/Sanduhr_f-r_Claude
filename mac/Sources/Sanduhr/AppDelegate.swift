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
    /// Rotate's timer (Settings, General, Menu bar), nil in every other mode; the step counts its turns.
    private var menuBarRotateTimer: Timer?
    private var menuBarStep = 0
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
        // EsteFont 26 (item 58), from Contents/Resources/Fonts, for this process only: before any
        // view draws, so the Desk and the widget find it on a Mac that never had it installed.
        BundledFonts.register()
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
        // Before DeskMigration marks the suite: an earlier version's Desk with no font picked keeps
        // the system font; a new install draws in EsteFont 26.
        DeskFont.keepExistingDefault()

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
            account: KeychainStore.accounts.active, in: UserDefaults.standard)
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
            // Remembered for the next launch: has the active account's key fetched (SignInGate)?
            SignInGate.record(fetched: fetched, needsSignIn: vm.status.needsSignIn,
                              account: KeychainStore.accounts.active, in: UserDefaults.standard)
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
            // During a switch the Desk lays out the old account's meters unseen (veiled), as the
            // widget's cards do, and fades in the new ones with the view model's animation. The
            // line keeps the old account's name until those numbers have faded out.
            DeskController.shared.model.update(DeskUsage(
                usage: vm.shownUsage, fetchedAt: vm.lastUpdated, signInNeeded: vm.status.needsSignIn,
                account: vm.shownAccountLabel, veiled: vm.switchVeil, switchNote: vm.switchNote))
        }

        // When the user toggles compact mode, resize the panel to fit the
        // (now much smaller) content instead of leaving an empty window.
        NotificationCenter.default.addObserver(
            self, selector: #selector(applyCompactState),
            name: .sanduhrCompactDidChange, object: nil)

        viewModel.bootstrap()
        applyMenuBarRotation()
        renderStatusItem()

        // Desk: the desktop layer and the notch, when switched on (Settings, General, Surfaces).
        DeskMigration.run()
        // Item 53's Now Playing switch becomes a place in the Desk layout, once (item 53b).
        NowPlayingPlacement.upgrade(UserDefaults.desk)
        DeskController.shared.apply()
        // The camera light watches the cameras while its switch is on, with or without Desk.
        CameraLightController.shared.apply()
        // The notch glow checks for meetings a minute out while its meetings switch is on.
        NotchGlowController.shared.apply()
        // Claude's suggested Desk messages (item 54): watches for propose_desk_messages' requests.
        DeskMessageHandoff.shared.startForApp()
        // Claude's suggested themes (item 55): propose_theme's requests, on the same folder watch.
        ThemeProposalHandoff.shared.startForApp(vm: viewModel)
        // Claude Code integrations (item 49): where they are installed, this version's scripts
        // replace the last one's (a new stamped folder, the link swapped). No install, no write.
        Task.detached(priority: .utility) { IntegrationScripts.standard.refreshIfInstalled() }
        showWhatsNewIfUpdated(fresh: firstRun == .fresh)
    }

    /// What's New after an update (item 57): the cards of the releases since the last one seen,
    /// once, a moment after the widget and Desk are up. A fresh install's first launch only
    /// records the version; while onboarding is up (no session key) the cards wait for a later
    /// launch. The version is recorded as the window opens, so it shows once even if Sanduhr quits.
    private func showWhatsNewIfUpdated(fresh: Bool) {
        let current = AppInfo.current.version
        let decision = WhatsNew.atLaunch(
            lastSeen: WhatsNew.lastSeen(), current: current, fresh: fresh,
            onboarding: !KeychainStore.exists(account: KeychainAccount.sessionKey), hidden: WhatsNew.hidden())
        if decision.record { WhatsNew.record(current) }
        guard !decision.show.isEmpty else { return }
        DispatchQueue.main.asyncAfter(deadline: .now() + 2) {
            MainActor.assumeIsolated { WhatsNewWindowController.shared.show(decision.show) }
        }
    }

    /// estedesk:// and sanduhr:// links (Option+J joins the next meeting, …/settings opens
    /// Sanduhr Settings). sanduhr://debug/… goes to the smoke tools' hooks, which ignore it
    /// unless they are switched on (DebugGate). sanduhr://claude-code?event=… comes from Claude
    /// Code's hooks (item 51) and is public: it carries only an event, and anything else on that
    /// host is dropped.
    func application(_ application: NSApplication, open urls: [URL]) {
        for url in urls {
            if DebugLink.isDebug(url) {
                DebugHooks.handle(url, app: self)
            } else if ClaudeCodeLink.isClaudeCode(url) {
                if let event = ClaudeCodeLink.event(url) { NotchGlowController.shared.claudeCode(event) }
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

    /// Now playing's adapter runs as a child process (item 53): it goes when Sanduhr quits.
    func applicationWillTerminate(_ notification: Notification) {
        NowPlayingController.shared.shutdown()
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

    /// Settings, General, Menu bar changed: start or stop Rotate's timer and redraw at once.
    func menuBarModeDidChange() {
        applyMenuBarRotation()
        renderStatusItem()
    }

    /// Rotate's timer runs only while Rotate is chosen. Added in the common modes so the text
    /// keeps turning while a menu is open; only the shown text changes, nothing is fetched.
    private func applyMenuBarRotation() {
        let rotating = MenuBarMode.saved() == .rotate
        if rotating, menuBarRotateTimer == nil {
            let timer = Timer(timeInterval: MenuBarMode.rotateInterval, repeats: true) { [weak self] _ in
                MainActor.assumeIsolated {
                    guard let self else { return }
                    self.menuBarStep &+= 1
                    self.renderStatusItem()
                }
            }
            timer.tolerance = 1
            RunLoop.main.add(timer, forMode: .common)
            menuBarRotateTimer = timer
        } else if !rotating, let timer = menuBarRotateTimer {
            timer.invalidate()
            menuBarRotateTimer = nil
            menuBarStep = 0
        }
    }

    /// Re-renders the status item title from the current usage and the Menu bar choice
    /// (MenuBarText: the session, the weekly limit, the higher of the two, or both in turn).
    func renderStatusItem() {
        guard let button = statusItem?.button else { return }
        let reading = MenuBarText.reading(viewModel.usage, mode: .saved(), step: menuBarStep)

        if let reading {
            let intPct = reading.percent
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
                string: " \(reading.text)",
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
        addMenuItems(to: menu, menuBarModes: true)

        // Briefly attach, pop, detach — so default L-click behavior stays
        // as "toggle panel" rather than "always show menu".
        statusItem?.menu = menu
        button.performClick(nil)
        statusItem?.menu = nil
    }

    /// The shared menu (SanduhrMenu) as AppKit items, for the menu bar item's menu and Desk's
    /// clock menu. The widget's own two-finger menu (RootView) renders the same groups.
    /// `accounts` false leaves the Accounts submenu out, for a limit menu that already has it;
    /// `menuBarModes` adds Menu Bar Shows after it, in the menu bar item's own menu only;
    /// `showHide` false leaves Show or Hide Widget out, for a Desk limit menu that has it on top.
    func addMenuItems(to menu: NSMenu, accounts withAccounts: Bool = true, menuBarModes: Bool = false,
                      showHide: Bool = true) {
        let accounts = withAccounts ? currentAccountsMenu() : nil
        var groups = currentMenu(widgetVisible: panel?.isVisible ?? false)
        // A Desk limit menu has Show or Hide Widget at its top already.
        if !showHide { groups = SanduhrMenu.without(.showHide, in: groups) }
        for (i, group) in groups.enumerated() {
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
            // The Accounts submenu sits after Show/Hide, with two or more accounts.
            if i == 0, let accounts {
                menu.addItem(.separator())
                menu.addItem(accountsMenuItem(accounts))
            }
            // Menu Bar Shows sits with the Accounts submenu, or after Show/Hide on its own.
            if i == 0, menuBarModes {
                if accounts == nil { menu.addItem(.separator()) }
                menu.addItem(menuBarModesMenuItem(SanduhrMenu.menuBarModes(current: .saved())))
            }
        }
    }

    /// "Menu Bar Shows ▸": the four Menu bar choices, the current one checked.
    private func menuBarModesMenuItem(_ modes: MenuBarModeMenu) -> NSMenuItem {
        let sub = NSMenu(title: MenuBarModeMenu.title)
        for item in modes.items {
            let m = NSMenuItem(title: item.title, action: #selector(menuBarModeChosen(_:)), keyEquivalent: "")
            m.target = self
            m.representedObject = item.mode.rawValue
            m.state = item.checked ? .on : .off
            sub.addItem(m)
        }
        let top = NSMenuItem(title: MenuBarModeMenu.title, action: nil, keyEquivalent: "")
        top.submenu = sub
        return top
    }

    /// A Menu Bar Shows choice: saved where Settings, General, Menu bar reads it (the picker
    /// follows), and the menu bar redrawn at once.
    @objc private func menuBarModeChosen(_ sender: NSMenuItem) {
        guard let raw = sender.representedObject as? String, let mode = MenuBarMode(rawValue: raw) else { return }
        MenuBarMode.save(mode)
        menuBarModeDidChange()
    }

    /// A Desk meter row's two-finger menu (LimitMenu): Show or Hide Widget, Accounts, Hide and the
    /// warnings item for `tier`, Meter Settings…, then the shared menu under a separator, less its
    /// own Show or Hide Widget. `tier` nil, a click beside the rows, leaves the limit's own items out.
    func addLimitMenuItems(to menu: NSMenu, tier: Tier?) {
        let groups = LimitMenu.groups(tier: tier, accounts: currentAccountsMenu(), store: UserDefaults.desk,
                                      usage: viewModel.usage, now: Date(), widgetVisible: widgetVisible)
        for (i, group) in groups.enumerated() {
            if i > 0 { menu.addItem(.separator()) }
            for entry in group { menu.addItem(limitMenuItem(entry)) }
        }
        menu.addItem(.separator())
        addMenuItems(to: menu, accounts: false, showHide: false)
    }

    private func limitMenuItem(_ entry: LimitMenuEntry) -> NSMenuItem {
        if case .accounts(let accounts) = entry { return accountsMenuItem(accounts) }
        if case .hiddenLimits(let tiers) = entry { return hiddenLimitsMenuItem(tiers) }
        let m = NSMenuItem(title: entry.title, action: #selector(limitItemChosen(_:)), keyEquivalent: "")
        m.target = self
        m.representedObject = LimitMenuBox(entry)
        return m
    }

    @objc private func limitItemChosen(_ sender: NSMenuItem) {
        if let box = sender.representedObject as? LimitMenuBox { performLimit(box.entry) }
    }

    /// "Hidden Limits ▸": each hidden limit; picking one shows it again.
    private func hiddenLimitsMenuItem(_ tiers: [Tier]) -> NSMenuItem {
        let sub = NSMenu(title: LimitMenu.hiddenLimits)
        for tier in tiers { sub.addItem(limitMenuItem(.show(tier))) }
        let top = NSMenuItem(title: LimitMenu.hiddenLimits, action: nil, keyEquivalent: "")
        top.submenu = sub
        return top
    }

    /// What a limit menu's items do, from Desk and the widget alike: Hide, a Hidden Limits item
    /// and the warnings item write the desk-suite keys Settings, Desk, Meters reads (both refresh
    /// on the change notice; Hide records the limit's current numbers), Meter Settings… opens
    /// that page.
    func performLimit(_ entry: LimitMenuEntry) {
        switch entry {
        case .widget(let visible):
            if visible { hidePanel() } else { DeskController.shared.showWidgetBesideMeters() }
        case .accounts, .hiddenLimits: break
        case .meterSettings: SettingsWindowController.shared.show(.deskMeters)
        case .hide, .show, .warnings: LimitMenu.apply(entry, to: UserDefaults.desk, usage: viewModel.usage)
        }
    }

    /// "Accounts ▸": each account (the active one checked), then Manage Accounts….
    private func accountsMenuItem(_ accounts: AccountsMenu) -> NSMenuItem {
        let sub = NSMenu(title: AccountsMenu.title)
        for item in accounts.items {
            let m = NSMenuItem(title: item.title, action: #selector(accountChosen(_:)), keyEquivalent: "")
            m.target = self
            m.representedObject = item.label
            m.state = item.checked ? .on : .off
            sub.addItem(m)
        }
        sub.addItem(.separator())
        let manage = NSMenuItem(title: AccountsMenu.manage, action: #selector(manageAccounts), keyEquivalent: "")
        manage.target = self
        sub.addItem(manage)
        let top = NSMenuItem(title: AccountsMenu.title, action: nil, keyEquivalent: "")
        top.submenu = sub
        return top
    }

    /// The shared menu with the tools' current checkmarks.
    func currentMenu(widgetVisible: Bool) -> [MenuGroup] {
        SanduhrMenu.groups(widgetVisible: widgetVisible,
                           deepWork: viewModel.activeTool == .deepWork,
                           pacing: viewModel.pacingPinned,
                           snake: viewModel.activeTool == .snake,
                           cameraLight: CameraLightController.shared.manual)
    }

    /// The Accounts submenu as it stands, nil with fewer than two accounts.
    func currentAccountsMenu() -> AccountsMenu? {
        SanduhrMenu.accounts(viewModel.accountLabels, active: viewModel.activeAccount,
                             inUse: viewModel.accountsInUse)
    }

    @objc private func menuItemChosen(_ sender: NSMenuItem) {
        if let command = MenuCommand(rawValue: sender.tag) { perform(command) }
    }

    @objc private func accountChosen(_ sender: NSMenuItem) {
        if let label = sender.representedObject as? String { viewModel.switchAccount(to: label) }
    }

    /// Manage Accounts…: Settings, Accounts.
    @objc func manageAccounts() {
        SettingsWindowController.shared.show(.credentials)
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
        case .usage: SettingsWindowController.shared.show(.usage)
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
        case .whatsNew: WhatsNewWindowController.shared.show()
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

    /// Show Widget in the Desk meters' menu: a hidden widget comes back beside them (`meters` is
    /// their frame on screen); a widget already showing is only brought forward.
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

/// Carries a limit menu item's entry on its NSMenuItem.
private final class LimitMenuBox: NSObject {
    let entry: LimitMenuEntry
    init(_ entry: LimitMenuEntry) { self.entry = entry }
}
