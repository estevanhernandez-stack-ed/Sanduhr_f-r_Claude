import SwiftUI

/// Shows last-updated timestamp + mode on the left, Sonnet shortcut on the
/// right. sanduhr.py:353-363. The shortcut waits for numbers: signed out or before the first
/// fetch the footer offers nothing to act on (Settings v2, F23).
struct FooterView: View {
    @Bindable var vm: UsageViewModel

    var body: some View {
        let t = vm.theme.palette
        HStack {
            Text(vm.footerText())
                .font(.app(size: 9, design: .rounded))
                .foregroundStyle(t.textMuted)
                .padding(.leading, 10)
                .id(vm.countdownTick)

            Spacer()

            if FooterView.showsModelLink(hasNumbers: vm.shownUsage != nil, needsSignIn: vm.status.needsSignIn) {
                Link("Use Sonnet",
                     destination: URL(string: "https://claude.ai/new?model=claude-sonnet-4-6")!)
                    .font(.app(size: 9, weight: .semibold, design: .rounded))
                    .foregroundStyle(t.accent)
                    .padding(.trailing, 10)
                    .padding(.leading, 4)
            }
        }
        .frame(height: 24)
        .background(
            LinearGradient(
                colors: [t.footerBg.opacity(0.95), t.footerBg],
                startPoint: .top, endPoint: .bottom)
            .opacity(Chrome.opacity))
    }

    /// Use Sonnet shows only with numbers on the widget and a working sign-in.
    static func showsModelLink(hasNumbers: Bool, needsSignIn: Bool) -> Bool { hasNumbers && !needsSignIn }
}
