import SwiftUI
import AppKit

/// The top-level widget content. The floating NSPanel hosts this view inside
/// an NSVisualEffectView so the whole thing has real system vibrancy.
struct RootView: View {
    @Bindable var vm: UsageViewModel

    @State private var showOnboarding = false

    var body: some View {
        let t = vm.theme.palette
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
                    VStack(alignment: .leading, spacing: 6) {
                        if vm.status != .idle && !vm.visibleTiers().isEmpty {
                            statusLine
                        } else if vm.status != .idle {
                            statusLine.padding(.bottom, 2)
                        }

                        if !vm.visibleTiers().isEmpty {
                            ForEach(vm.visibleTiers(), id: \.tier) { row in
                                TierCardView(
                                    tier: row.tier,
                                    usage: row.usage,
                                    history: vm.history[row.tier.rawValue]?.map(\.v) ?? [],
                                    palette: t,
                                    tick: vm.countdownTick,
                                    pinDeepMath: vm.pacingPinned
                                )
                            }
                            if let extra = vm.usage?.extraUsage, extra.isEnabled, !vm.compact {
                                ExtraUsageCard(extra: extra, palette: t)
                            }
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
            if !DisplaySettings.shared.subtle {
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
        // The full macOS glass stack: NSVisualEffectView behind the window
        // blurs whatever's underneath; a very faint theme tint adds mood
        // without killing the transparency; a hairline white inner stroke
        // is the "lit glass edge" convention Apple uses in Control Center,
        // the HUDs, etc.
        .background(
            ZStack {
                VisualEffectView(material: .hudWindow,
                                 blendingMode: .behindWindow,
                                 state: .active)
                // Theme tint — translucent for most themes (so vibrancy
                // dominates), opaque for Matrix (so the green reads pure
                // and the desktop wallpaper can't bleed through).
                t.bg.opacity(t.overlayOpacity)
                // Top-edge sheen: lighter at the top, nothing at the bottom.
                LinearGradient(
                    colors: [Color.white.opacity(0.06), Color.clear],
                    startPoint: .top, endPoint: .center)
            }
            .clipShape(RoundedRectangle(cornerRadius: 14, style: .continuous))
            .opacity(Chrome.opacity)
        )
        .overlay(
            // Outer hairline — slightly darker than the inner highlight so
            // the window reads "raised" against whatever's behind it.
            RoundedRectangle(cornerRadius: 14, style: .continuous)
                .strokeBorder(Color.white.opacity(0.10), lineWidth: 0.5)
                .opacity(Chrome.opacity)
        )
        .overlay(
            // Inset inner highlight — the "lit from above" glass rim.
            RoundedRectangle(cornerRadius: 13.5, style: .continuous)
                .strokeBorder(
                    LinearGradient(
                        colors: [Color.white.opacity(0.18),
                                 Color.white.opacity(0.04)],
                        startPoint: .top, endPoint: .bottom),
                    lineWidth: 0.5)
                .padding(0.5)
                .allowsHitTesting(false)
                .opacity(Chrome.opacity)
        )
        // Soft drop shadow for the floating-panel feel.
        // In subtle mode the same shadow, small and tight, keeps the text readable on any wallpaper.
        .shadow(color: .black.opacity(DisplaySettings.shared.subtle ? 0.6 : 0.45),
                radius: DisplaySettings.shared.subtle ? 3 : 24,
                x: 0, y: DisplaySettings.shared.subtle ? 1 : 10)
        .contextMenu {
            // The same items, in the same order, as the menu bar item's menu and Desk's clock
            // menu (SanduhrMenu). The widget shows while its menu is open.
            if let app = NSApp.delegate as? AppDelegate {
                let groups = app.currentMenu(widgetVisible: true)
                ForEach(groups.indices, id: \.self) { i in
                    if i > 0 { Divider() }
                    if let header = groups[i].header {
                        Section(header) { menuRows(groups[i].entries, app) }
                    } else {
                        menuRows(groups[i].entries, app)
                    }
                }
            }
        }
        .sheet(isPresented: $showOnboarding) {
            OnboardingSheet(vm: vm, onContinue: {
                showOnboarding = false
                SettingsWindowController.shared.show(.credentials)
            })
        }
        .onAppear {
            // `exists()` doesn't trigger Touch ID — safe to call on every
            // view appearance.
            if !KeychainStore.exists(account: KeychainAccount.sessionKey) {
                showOnboarding = true
            }
        }
    }

    /// One menu row: a checkmark toggle for a tool, a plain button otherwise.
    @ViewBuilder
    private func menuRows(_ entries: [MenuEntry], _ app: AppDelegate) -> some View {
        ForEach(entries, id: \.command) { entry in
            let row = Group {
                if [.deepWork, .pacing, .snake].contains(entry.command) {
                    Toggle(entry.title, isOn: Binding(
                        get: { entry.checked },
                        set: { _ in withAnimation { app.perform(entry.command) } }))
                } else {
                    Button(entry.title) { app.perform(entry.command) }
                }
            }
            if let key = entry.key.first {
                row.keyboardShortcut(KeyEquivalent(key), modifiers: .command)
            } else {
                row
            }
        }
    }

    /// The action row's Deep Work and Snake buttons: open the tool, or close it if it is open.
    private func toggle(_ tool: UsageViewModel.WidgetTool) {
        vm.activeTool = vm.activeTool == tool ? nil : tool
    }

    // MARK: Status line

    private var statusLine: some View {
        let t = vm.theme.palette
        let color: Color = vm.status.isError ? .hex("f87171") : t.textDim
        return Text(vm.status.text)
            .font(.app(size: 11))
            .foregroundStyle(color)
            .frame(maxWidth: .infinity, alignment: .leading)
    }
}
