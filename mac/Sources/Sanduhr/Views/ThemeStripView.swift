import SwiftUI

/// "Theme: <current> ▾" pop-up menu. Replaces the horizontal name strip —
/// with 7 built-ins plus user-loaded JSON themes the strip was truncating,
/// and a menu scales cleanly to any theme count.
struct ThemeStripView: View {
    @Bindable var vm: UsageViewModel

    var body: some View {
        let t = vm.theme.palette
        HStack {
            // `.id(vm.userThemesTick)` forces SwiftUI to rebuild the menu
            // whenever Settings → Themes saves/deletes/reloads. The static
            // `ThemeRegistry.themes` array isn't observable on its own.
            Menu {
                ForEach(ThemeRegistry.themes) { theme in
                    Button {
                        vm.theme = theme
                    } label: {
                        // SwiftUI's Label with a conditional systemImage
                        // gives us a native-looking check next to the
                        // currently-active theme.
                        if theme.id == vm.theme.id {
                            Label(theme.displayName, systemImage: "checkmark")
                        } else {
                            Text(theme.displayName)
                        }
                    }
                }
            } label: {
                HStack(spacing: 4) {
                    Text("Theme:")
                        .font(.system(size: 10, design: .rounded))
                        .foregroundStyle(t.textMuted)
                    Text(vm.theme.displayName)
                        .font(.system(size: 10, weight: .semibold, design: .rounded))
                        .foregroundStyle(t.accent)
                    Image(systemName: "chevron.down")
                        .font(.system(size: 7, weight: .semibold))
                        .foregroundStyle(t.textMuted)
                }
                .contentShape(Rectangle())
            }
            .menuStyle(.borderlessButton)
            .menuIndicator(.hidden)
            .fixedSize()
            .id(vm.userThemesTick)

            Spacer()
        }
        .padding(.horizontal, 10)
        .frame(height: 22)
        .background(
            LinearGradient(
                colors: [t.glass.opacity(0.85), t.glass.opacity(0.65)],
                startPoint: .top, endPoint: .bottom))
    }
}
