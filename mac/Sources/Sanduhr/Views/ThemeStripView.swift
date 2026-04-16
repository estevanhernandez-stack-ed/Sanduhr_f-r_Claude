import SwiftUI

/// Horizontal row of theme names; the active one is bold in its own accent
/// color. sanduhr.py:324-339.
struct ThemeStripView: View {
    @Bindable var vm: UsageViewModel

    var body: some View {
        HStack(spacing: 0) {
            ForEach(Theme.allCases) { t in
                let active = t == vm.theme
                Button {
                    vm.theme = t
                } label: {
                    Text(t.displayName)
                        .font(.system(size: 9, weight: active ? .bold : .regular,
                                      design: .rounded))
                        .foregroundStyle(active ? t.palette.accent
                                                : vm.theme.palette.textMuted)
                        .padding(.horizontal, 6)
                        .frame(maxHeight: .infinity)
                }
                .buttonStyle(.plain)
                .help(t.displayName)
            }
            Spacer()
        }
        .frame(height: 20)
        .background(
            LinearGradient(
                colors: [vm.theme.palette.glass.opacity(0.85),
                         vm.theme.palette.glass.opacity(0.65)],
                startPoint: .top, endPoint: .bottom))
    }
}
