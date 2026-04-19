import SwiftUI
import AppKit

/// The top-level widget content. The floating NSPanel hosts this view inside
/// an NSVisualEffectView so the whole thing has real system vibrancy.
struct RootView: View {
    @Bindable var vm: UsageViewModel

    @State private var showSettings = false
    @State private var showOnboarding = false
    @State private var isFocusMode = false
    @State private var isSnakeGameActive = false
    
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

            TitleBarView(
                vm: vm,
                onShowSettings: { showSettings = true },
                onToggleFocus: { isFocusMode.toggle() },
                onToggleSnake: { isSnakeGameActive.toggle() },
                onRefresh:      { Task { await vm.refresh() } },
                // × now HIDES (status item click brings it back). Actual
                // quit lives in the menu bar's right-click menu and the
                // widget's own right-click context menu below.
                onClose:        { (NSApp.delegate as? AppDelegate)?.hidePanel() }
            )

            ThemeStripView(vm: vm)
            // Hairline separator — 0.5pt white at low opacity is the Mac
            // convention instead of a solid border line.
            Rectangle().fill(Color.white.opacity(0.07)).frame(height: 0.5)

            // Main content
            ZStack(alignment: .topLeading) {
                if isFocusMode {
                    FocusView(vm: vm) {
                        withAnimation(.easeInOut(duration: 0.3)) { isFocusMode = false }
                    }
                    .transition(.opacity.combined(with: .scale(scale: 0.95)))
                } else if isSnakeGameActive {
                    SnakeGameOverlay(vm: vm) {
                        withAnimation(.easeInOut(duration: 0.3)) { isSnakeGameActive = false }
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
                                    tick: vm.countdownTick
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
            .animation(.easeInOut(duration: 0.3), value: isFocusMode)
            .animation(.easeInOut(duration: 0.3), value: isSnakeGameActive)
            .frame(maxWidth: .infinity, alignment: .leading)
            .padding(.horizontal, 10)
            .padding(.vertical, 8)

            Spacer(minLength: 0)
            FooterView(vm: vm)
        }
        .frame(width: 340)
        .fixedSize(horizontal: true, vertical: true)
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
        )
        .overlay(
            // Outer hairline — slightly darker than the inner highlight so
            // the window reads "raised" against whatever's behind it.
            RoundedRectangle(cornerRadius: 14, style: .continuous)
                .strokeBorder(Color.white.opacity(0.10), lineWidth: 0.5)
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
        )
        // Soft drop shadow for the floating-panel feel.
        .shadow(color: .black.opacity(0.45), radius: 24, x: 0, y: 10)
        .contextMenu {
            Button("Refresh") { Task { await vm.refresh() } }
            Button(vm.compact ? "Expand" : "Compact Mode") { vm.compact.toggle() }
            Button(isFocusMode ? "Exit Deep Work" : "Enter Deep Work") { 
                withAnimation { isFocusMode.toggle() } 
            }
            .keyboardShortcut("p", modifiers: .command)
            Button("Play Cooldown Snake") { 
                withAnimation { isSnakeGameActive.toggle() } 
            }
            Button("Credentials…") { showSettings = true }
            Divider()
            Button("Quit Sanduhr") { NSApp.terminate(nil) }
        }
        .sheet(isPresented: $showSettings) {
            SettingsSheet(vm: vm, onDismiss: { showSettings = false })
        }
        .sheet(isPresented: $showOnboarding) {
            OnboardingSheet(vm: vm, onContinue: {
                showOnboarding = false
                showSettings = true
            })
        }
        .onAppear {
            // `exists()` doesn't trigger Touch ID — safe to call on every
            // view appearance.
            if !KeychainStore.exists(account: KeychainAccount.sessionKey) {
                showOnboarding = true
            }
        }
        // Menu bar can ask the widget to open its credentials sheet.
        .onChange(of: vm.requestSettingsSheet) { _, newValue in
            if newValue {
                showSettings = true
                vm.requestSettingsSheet = false
            }
        }
    }

    // MARK: Status line

    private var statusLine: some View {
        let t = vm.theme.palette
        let color: Color = vm.status.isError ? .hex("f87171") : t.textDim
        return Text(vm.status.text)
            .font(.system(size: 11))
            .foregroundStyle(color)
            .frame(maxWidth: .infinity, alignment: .leading)
    }
}
