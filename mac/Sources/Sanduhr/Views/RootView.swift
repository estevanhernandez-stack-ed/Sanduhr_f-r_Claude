import SwiftUI
import AppKit

/// The top-level widget content. The floating NSPanel hosts this view inside
/// an NSVisualEffectView so the whole thing has real system vibrancy.
struct RootView: View {
    @Bindable var vm: UsageViewModel

    @State private var showOnboarding = false

    var body: some View {
        let t = vm.theme.palette
        let shadow = WidgetShadow.resolve(subtle: DisplaySettings.shared.drawsSubtle, ink: t.ink)
        VStack(spacing: 0) {
            // Thin accent strip, softened to a gradient pill.
            LinearGradient(
                colors: [t.accent.opacity(0.35), t.accent, t.accent.opacity(0.35)],
                startPoint: .leading, endPoint: .trailing)
                .frame(height: 2)
                .shadow(color: t.accent.opacity(t.accentBloom.alpha),
                        radius: t.accentBloom.blur, x: 0, y: 0)
                .opacity(Chrome.opacity)

            TitleBarView(
                vm: vm,
                // × HIDES the widget (status item click brings it back).
                // Quit still lives in the menu-bar right-click menu and the
                // right-click context menu below.
                onClose: { (NSApp.delegate as? AppDelegate)?.hidePanel() }
            )

            // Hairline separator — 0.5pt white at low opacity is the Mac
            // convention instead of a solid border line.
            Rectangle().fill(Color.white.opacity(0.07)).frame(height: 0.5)

            // Main content
            ZStack(alignment: .topLeading) {
                if vm.activeTool == .deepWork {
                    FocusView(vm: vm) {
                        withAnimation(.easeInOut(duration: 0.3)) { vm.activeTool = nil }
                    }
                    .transition(.opacity.combined(with: .scale(scale: 0.95)))
                } else if vm.activeTool == .snake {
                    SnakeGameOverlay(vm: vm) {
                        withAnimation(.easeInOut(duration: 0.3)) { vm.activeTool = nil }
                    }
                    .transition(.opacity.combined(with: .scale(scale: 0.95)))
                } else {
                    let rows = vm.visibleTiers()
                    VStack(alignment: .leading, spacing: 6) {
                        // A switch fading the old cards says so over them (switchNote), not here.
                        if vm.status != .idle && !(vm.status == .switching && !rows.isEmpty) {
                            if rows.isEmpty { statusLine.padding(.bottom, 2) } else { statusLine }
                        }

                        if !rows.isEmpty {
                            cards(rows, palette: t)
                        }
                    }
                    .transition(.opacity.combined(with: .scale(scale: 1.05)))
                }
            }
            .animation(.easeInOut(duration: 0.3), value: vm.activeTool)
            .frame(maxWidth: .infinity, alignment: .leading)
            .padding(.horizontal, 10)
            .padding(.vertical, 8)

            // Bottom chrome sits directly below the last tier card. No
            // Spacer anywhere — the window itself now auto-sizes to content
            // height via FloatingPanel.windowWillResize + fitPanelToContent
            // in AppDelegate, so "excess space between anything" is no
            // longer physically possible. Horizontal drag-resize still
            // works.
            Rectangle().fill(Color.white.opacity(0.07)).frame(height: 0.5)
            if !DisplaySettings.shared.drawsSubtle {
                ThemeStripView(vm: vm)
            }
            ActionIconRow(
                vm: vm,
                onShowSettings: { SettingsWindowController.shared.show() },
                onToggleFocus:  { toggle(.deepWork) },
                onToggleSnake:  { toggle(.snake) },
                onRefresh:      { Task { await vm.refresh() } }
            )
            FooterView(vm: vm)
        }
        // Width stays flexible for horizontal drag-resize; vertical goes
        // fixed to content so there's never empty space anywhere.
        .frame(minWidth: 340, maxWidth: .infinity, alignment: .top)
        .fixedSize(horizontal: false, vertical: true)
        .modifier(WidgetGlass(palette: t, shadow: shadow))
        .contextMenu {
            // The same items, in the same order, as the menu bar item's menu and Desk's clock
            // menu (SanduhrMenu). The widget shows while its menu is open.
            if let app = NSApp.delegate as? AppDelegate {
                SanduhrMenuItems(app: app, vm: vm)
            }
        }
        .sheet(isPresented: $showOnboarding) {
            onboarding
        }
        .onAppear {
            // `exists()` never prompts (no access control on the Keychain
            // items) — safe to call on every view appearance.
            if !KeychainStore.exists(account: KeychainAccount.sessionKey) {
                showOnboarding = true
            }
        }
    }

    private var onboarding: some View {
        OnboardingSheet(vm: vm, onSignIn: {
            showOnboarding = false
            Task {
                switch await SignInWindowController.shared.run() {
                case .signedIn(let c):
                    // The first key, as Settings, Accounts saves it: it becomes Personal.
                    KeychainStore.set(c.sessionKey, account: KeychainAccount.sessionKey)
                    if let cf = c.cfClearance { KeychainStore.set(cf, account: KeychainAccount.cfClearance) }
                    vm.credentialsChanged()
                case .pasteInstead:
                    SettingsWindowController.shared.show(.credentials)
                case .cancelled:
                    if !KeychainStore.exists(account: KeychainAccount.sessionKey) { showOnboarding = true }
                }
            }
        }, onPaste: {
            showOnboarding = false
            SettingsWindowController.shared.show(.credentials)
        })
    }

    /// The tier cards and the extra-usage card. During an account switch these are the old
    /// account's, faded out and kept in the layout unseen so the widget keeps its height, with
    /// the faint "Switching account…" over them once the fetch outlasts the fade; the new
    /// account's fade in when they arrive (AccountSwitchFade).
    private func cards(_ rows: [(tier: Tier, usage: TierUsage)], palette t: Theme.Palette) -> some View {
        WidgetCardStack(cards: WidgetCards.live(vm, rows: rows), palette: t, vm: vm)
    }

    /// The action row's Deep Work and Snake buttons: open the tool, or close it if it is open.
    private func toggle(_ tool: UsageViewModel.WidgetTool) {
        vm.activeTool = vm.activeTool == tool ? nil : tool
    }

    // MARK: Status line

    @ViewBuilder
    private var statusLine: some View {
        let t = vm.theme.palette
        let color: Color = vm.status.isError ? .hex("f87171") : t.textDim
        if vm.status == .signedOut {
            // Signed out: the line is the way back, straight to Settings, Accounts.
            Button { SettingsWindowController.shared.show(.credentials) } label: {
                Text(vm.status.text)
                    .font(.app(size: 11))
                    .underline()
                    .foregroundStyle(t.accent)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            .accessibilityHint("Opens Settings, Accounts")
        } else {
            Text(vm.status.text)
                .font(.app(size: 11))
                .foregroundStyle(color)
                .frame(maxWidth: .infinity, alignment: .leading)
        }
    }
}

/// The widget menu's Accounts submenu (SanduhrMenu.accounts): each account, the active one
/// checked, then Manage Accounts….
struct AccountsSubmenu: View {
    let accounts: AccountsMenu
    var vm: UsageViewModel
    var onManage: () -> Void

    var body: some View {
        Menu(AccountsMenu.title) {
            ForEach(accounts.items, id: \.label) { item in
                Toggle(item.title, isOn: Binding(
                    get: { item.checked },
                    set: { _ in vm.switchAccount(to: item.label) }))
            }
            Divider()
            Button(AccountsMenu.manage, action: onManage)
        }
    }
}

/// "Menu Bar Shows ▸" in the widget's menus, as AppDelegate draws it in the AppKit ones: the four
/// choices, the current one checked; a pick saves where Settings reads it and redraws the menu bar.
struct MenuBarModesSubmenu: View {
    let app: AppDelegate

    var body: some View {
        let modes = SanduhrMenu.menuBarModes(current: .saved())
        Menu(MenuBarModeMenu.title) {
            ForEach(modes.items, id: \.mode) { item in
                Toggle(item.title, isOn: Binding(
                    get: { item.checked },
                    set: { _ in
                        MenuBarMode.save(item.mode)
                        app.menuBarModeDidChange()
                    }))
            }
        }
    }
}

/// The shared menu (SanduhrMenu) as SwiftUI items, separators between the groups, the Accounts
/// and Menu Bar Shows submenus after Show/Hide as AppDelegate.addMenuItems puts them (SanduhrMenu.submenus). `accounts` false leaves the
/// submenu out, for a card's limit menu that already has it; `allSettings` names Settings…
/// "All Settings…" there, beside the card's Meters Settings….
struct SanduhrMenuItems: View {
    let app: AppDelegate
    var vm: UsageViewModel
    var accounts = true
    var allSettings = false

    var body: some View {
        let groups = app.currentMenu(widgetVisible: true, allSettings: allSettings)
        ForEach(groups.indices, id: \.self) { i in
            if i > 0 { Divider() }
            if let header = groups[i].header {
                Section(header) { menuRows(groups[i].entries) }
            } else {
                menuRows(groups[i].entries)
            }
            if i == 0 {
                Divider()
                if accounts, let menu = app.currentAccountsMenu() {
                    AccountsSubmenu(accounts: menu, vm: vm, onManage: app.manageAccounts)
                }
                MenuBarModesSubmenu(app: app)
            }
        }
    }

    /// One menu row: a checkmark toggle for a tool, a plain button otherwise.
    @ViewBuilder
    private func menuRows(_ entries: [MenuEntry]) -> some View {
        ForEach(entries, id: \.command) { entry in
            let row = Group {
                if [.deepWork, .pacing, .snake, .cameraLight].contains(entry.command) {
                    Toggle(entry.title, isOn: Binding(
                        get: { entry.checked },
                        set: { _ in withAnimation { app.perform(entry.command) } }))
                } else if let note = entry.note {
                    // An item that is off says why (Arrange Desk… while Desk is off).
                    Button { app.perform(entry.command) } label: {
                        Text(entry.title)
                        Text(note)
                    }
                    .disabled(!entry.enabled)
                    .help(note)
                } else {
                    Button(entry.title) { app.perform(entry.command) }
                        .disabled(!entry.enabled)
                }
            }
            if let key = entry.key.first {
                row.keyboardShortcut(KeyEquivalent(key), modifiers: .command)
            } else {
                row
            }
        }
    }
}

/// A tier card's two-finger menu (LimitMenu), in place of the widget's own menu on the card:
/// Accounts, Hide (temporary limits only) and the warnings item for the card's limit, Hidden
/// Limits (when something is hidden), Meters Settings…, then the widget's
/// shared items under a separator, so nothing the widget menu offers is lost on a card. The
/// warning switch is read through @AppStorage on the same key as Settings, Desk, Meters, so the
/// menu reads the current setting whichever side changed it.
struct LimitContextMenu: ViewModifier {
    let tier: Tier
    var vm: UsageViewModel
    @AppStorage private var warningsOn: Bool

    init(tier: Tier, vm: UsageViewModel) {
        self.tier = tier
        self.vm = vm
        _warningsOn = AppStorage(wrappedValue: MeterWarningSettings.standard(for: tier).enabled,
                                 MeterWarningSettings.onKey(tier), store: .desk)
    }

    func body(content: Content) -> some View {
        content.contextMenu {
            if let app = NSApp.delegate as? AppDelegate {
                let groups = LimitMenu.groups(tier: tier, accounts: app.currentAccountsMenu(),
                                              hidden: vm.hiddenTiers, temporary: vm.temporaryTiers,
                                              warningsOn: warningsOn)
                ForEach(groups.indices, id: \.self) { i in
                    if i > 0 { Divider() }
                    ForEach(groups[i].indices, id: \.self) { j in
                        entryView(groups[i][j], app: app)
                    }
                }
                Divider()
                SanduhrMenuItems(app: app, vm: vm, accounts: false, allSettings: true)
            }
        }
    }

    @ViewBuilder
    private func entryView(_ entry: LimitMenuEntry, app: AppDelegate) -> some View {
        if case .accounts(let accounts) = entry {
            AccountsSubmenu(accounts: accounts, vm: vm, onManage: app.manageAccounts)
        } else if case .hiddenLimits(let tiers) = entry {
            Menu(LimitMenu.hiddenLimits) {
                ForEach(tiers, id: \.self) { tier in
                    Button(tier.label) { app.performLimit(.show(tier)) }
                }
            }
        } else {
            Button(entry.title) { app.performLimit(entry) }
        }
    }
}
