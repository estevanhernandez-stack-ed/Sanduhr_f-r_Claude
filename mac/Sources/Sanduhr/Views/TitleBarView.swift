import SwiftUI
import AppKit

/// Title bar with app name + Key / Refresh / Pin / Close buttons.
/// Dragging the title bar moves the window; double-click toggles compact mode.
/// Mirrors sanduhr.py:300-322.
struct TitleBarView: View {
    @Bindable var vm: UsageViewModel
    var onShowSettings: () -> Void
    var onRefresh: () -> Void
    var onClose: () -> Void

    var body: some View {
        let t = vm.theme.palette
        HStack(spacing: 0) {
            Text("Sanduhr")
                .font(.system(size: 11, weight: .semibold, design: .rounded))
                .foregroundStyle(t.text)
                .padding(.leading, 10)

            // Draggable spacer — grabs the window by anywhere here.
            Rectangle()
                .fill(Color.clear)
                .contentShape(Rectangle())
                .onTapGesture(count: 2) { vm.compact.toggle() }

            tbButton("Key",     color: t.textDim, action: onShowSettings)
            tbButton("↻",       color: t.textDim, action: onRefresh)
            // Different labels so the pin state is readable at a glance.
            tbButton(vm.pinned ? "Pinned" : "Pin",
                     color: vm.pinned ? t.accent : t.textMuted,
                     bold: vm.pinned) {
                vm.pinned.toggle()
                DispatchQueue.main.async { applyPinnedState() }
            }
            tbButton("×", color: .hex("f87171"), action: onClose)
                .padding(.trailing, 2)
        }
        .frame(height: 28)
        .background(
            // Subtle gradient in the title bar for depth.
            LinearGradient(
                colors: [t.titleBg, t.titleBg.opacity(0.88)],
                startPoint: .top, endPoint: .bottom))
    }

    @ViewBuilder
    private func tbButton(_ label: String, color: Color, bold: Bool = false,
                          action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Text(label)
                .font(.system(size: 9, weight: bold ? .bold : .regular))
                .foregroundStyle(color)
                .padding(.horizontal, 6)
                .frame(maxHeight: .infinity)
        }
        .buttonStyle(.plain)
    }

    /// Toggles both `level` AND `isFloatingPanel` — the latter forces always-
    /// on-top behavior on NSPanel regardless of level, so we have to flip it
    /// alongside. Without this, "unpin" had no visible effect.
    private func applyPinnedState() {
        guard let panel = NSApp.windows.compactMap({ $0 as? FloatingPanel }).first
        else { return }
        panel.isFloatingPanel = vm.pinned
        panel.level = vm.pinned ? .floating : .normal
    }
}
