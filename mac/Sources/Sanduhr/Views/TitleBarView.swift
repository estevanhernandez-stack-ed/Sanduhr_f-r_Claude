import SwiftUI
import AppKit

/// Mac-style header: traffic-light red close button on the left, app name
/// next to it, and a large draggable area filling the rest. Double-click
/// anywhere on the drag area toggles compact mode. Keyboard/mouse chrome
/// lives in `ActionIconRow` below the tier cards now — this bar is just
/// close + label + drag.
struct TitleBarView: View {
    @Bindable var vm: UsageViewModel
    var onClose: () -> Void

    var body: some View {
        let t = vm.theme.palette
        HStack(spacing: 8) {
            TrafficLightCloseButton(action: onClose)
                .padding(.leading, 10)

            // A theme's title_ink and title_style (item 65d); as before without them.
            ThemeTitleText(palette: t, size: 11)

            if let chip = vm.accountChip {
                AccountChip(chip: chip, palette: t) { vm.cycleAccount() }
            }

            // Draggable spacer — claims the rest of the row so the user can
            // grab the widget anywhere to move it.
            Rectangle()
                .fill(Color.clear)
                .contentShape(Rectangle())
                .onTapGesture(count: 2) { vm.compact.toggle() }
        }
        .frame(height: 28)
        .background(
            LinearGradient(
                colors: [t.titleBg, t.titleBg.opacity(0.88)],
                startPoint: .top, endPoint: .bottom)
            .opacity(Chrome.opacity))
    }
}

/// The active account's label, only with two or more accounts (UsageViewModel.accountChip). A
/// click switches to the next account, as Windows' CycleAccount.
private struct AccountChip: View {
    let chip: UsageViewModel.AccountChipText
    let palette: Theme.Palette
    var action: () -> Void
    @State private var hovering = false

    var body: some View {
        Button(action: action) {
            HStack(spacing: 4) {
                Text(chip.text)
                    .font(.app(size: 10, weight: .medium, design: .rounded))
                    .lineLimit(1)
                    // A switch's name change crossfades (AccountSwitchFade.nameAnimation).
                    .contentTransition(.opacity)
                if chip.otherInUse {
                    Circle().fill(palette.accent).frame(width: 5, height: 5)
                }
            }
            .foregroundStyle(palette.textSecondary)
            .padding(.horizontal, 7)
            .padding(.vertical, 2)
            .background(Capsule().fill(palette.text.opacity(hovering ? 0.16 : 0.08)))
        }
        .buttonStyle(.plain)
        .onHover { hovering = $0 }
        .help("Switch to the next account")
        .accessibilityLabel("Account \(chip.text)")
        .accessibilityHint("Switches to the next account")
    }
}

/// Apple-style red close button — 12×12 gradient disc with a subtle stroke,
/// showing a small `×` glyph on hover. Click hides the panel via `onClose`.
private struct TrafficLightCloseButton: View {
    var action: () -> Void
    @State private var hovering = false

    var body: some View {
        Button(action: action) {
            ZStack {
                Circle()
                    .fill(LinearGradient(
                        colors: [Color(red: 1.00, green: 0.37, blue: 0.37),
                                 Color(red: 0.92, green: 0.28, blue: 0.28)],
                        startPoint: .top, endPoint: .bottom))
                    .frame(width: 12, height: 12)
                    .overlay(
                        Circle().strokeBorder(Color.black.opacity(0.18),
                                              lineWidth: 0.5))
                if hovering {
                    Image(systemName: "xmark")
                        .font(.app(size: 7, weight: .bold))
                        .foregroundStyle(.black.opacity(0.55))
                }
            }
        }
        .buttonStyle(.plain)
        .onHover { hovering = $0 }
        .help("Close")
    }
}
