import SwiftUI

/// Shows last-updated timestamp + mode on the left, Sonnet shortcut on the
/// right. sanduhr.py:353-363.
struct FooterView: View {
    @Bindable var vm: UsageViewModel

    var body: some View {
        let t = vm.theme.palette
        HStack {
            Text(vm.footerText())
                .font(.system(size: 9, design: .rounded))
                .foregroundStyle(t.textMuted)
                .padding(.leading, 10)
                .id(vm.countdownTick)

            Spacer()

            Link("Use Sonnet",
                 destination: URL(string: "https://claude.ai/new?model=claude-sonnet-4-6")!)
                .font(.system(size: 9, weight: .semibold, design: .rounded))
                .foregroundStyle(t.accent)
                .padding(.trailing, 10)
                .padding(.leading, 4)
        }
        .frame(height: 24)
        .background(
            LinearGradient(
                colors: [t.footerBg.opacity(0.95), t.footerBg],
                startPoint: .top, endPoint: .bottom))
    }
}
