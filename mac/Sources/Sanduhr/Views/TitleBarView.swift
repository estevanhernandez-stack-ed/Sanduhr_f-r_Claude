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

            Text("Sanduhr")
                .font(.system(size: 11, weight: .semibold, design: .rounded))
                .foregroundStyle(t.text)

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
                startPoint: .top, endPoint: .bottom))
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
                        .font(.system(size: 7, weight: .bold))
                        .foregroundStyle(.black.opacity(0.55))
                }
            }
        }
        .buttonStyle(.plain)
        .onHover { hovering = $0 }
        .help("Close")
    }
}
