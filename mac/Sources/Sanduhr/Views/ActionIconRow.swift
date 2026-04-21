import SwiftUI
import AppKit

/// Row of SF Symbol icon buttons living just above the footer —
/// Settings / Compact / Deep Work / Snake / Refresh / Pin. Replaces the
/// emoji-labeled buttons that used to sit in the title bar.
struct ActionIconRow: View {
    @Bindable var vm: UsageViewModel
    var onShowSettings: () -> Void
    var onToggleFocus: () -> Void
    var onToggleSnake: () -> Void
    var onRefresh: () -> Void

    var body: some View {
        let t = vm.theme.palette
        HStack(spacing: 4) {
            iconButton("gearshape", help: "Settings", action: onShowSettings)
            iconButton(vm.compact ? "rectangle.expand.vertical"
                                  : "rectangle.compress.vertical",
                       help: vm.compact ? "Expand" : "Compact Mode") {
                vm.compact.toggle()
            }
            iconButton("hourglass", help: "Deep Work", action: onToggleFocus)
            iconButton("gamecontroller", help: "Cooldown Snake",
                       action: onToggleSnake)
            iconButton("arrow.clockwise", help: "Refresh Now",
                       action: onRefresh)

            Spacer()

            iconButton(vm.pinned ? "pin.fill" : "pin.slash",
                       tint: vm.pinned ? t.accent : nil,
                       help: vm.pinned ? "Unpin" : "Pin") {
                vm.pinned.toggle()
                DispatchQueue.main.async { applyPinnedState() }
            }
        }
        .padding(.horizontal, 10)
        .frame(height: 28)
        // Match FooterView's background exactly so the icon row and
        // the "Updated …" footer read as a single continuous dark band
        // instead of two stacked strips with a visible seam between.
        .background(
            LinearGradient(
                colors: [t.footerBg.opacity(0.95), t.footerBg],
                startPoint: .top, endPoint: .bottom))
    }

    @ViewBuilder
    private func iconButton(_ name: String, tint: Color? = nil, help: String,
                            action: @escaping () -> Void) -> some View {
        let t = vm.theme.palette
        Button(action: action) {
            Image(systemName: name)
                .font(.system(size: 13, weight: .medium))
                .foregroundStyle(tint ?? t.textSecondary)
                .frame(width: 24, height: 24)
                .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .help(help)
    }

    /// Mirrors `vm.pinned` onto the NSPanel level/floating state so the
    /// window actually un-floats when the user toggles Pin.
    private func applyPinnedState() {
        guard let panel = NSApp.windows.compactMap({ $0 as? FloatingPanel }).first
        else { return }
        panel.isFloatingPanel = vm.pinned
        panel.level = vm.pinned ? .floating : .normal
    }
}
