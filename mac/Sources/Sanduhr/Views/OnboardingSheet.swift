import SwiftUI

/// First-run instructions, kicked off when no sessionKey is in the Keychain.
/// Replaces the Python `messagebox.showinfo` + `simpledialog.askstring` combo.
struct OnboardingSheet: View {
    @Bindable var vm: UsageViewModel
    var onContinue: () -> Void

    var body: some View {
        let t = vm.theme.palette
        VStack(alignment: .leading, spacing: 14) {
            Text("Welcome to Sanduhr")
                .font(.title3.bold())
                .foregroundStyle(t.text)

            Text("To track your Claude usage, paste your `sessionKey` cookie. It's stored in your macOS Keychain — not in a plaintext file.")
                .font(.callout)
                .foregroundStyle(t.textSecondary)
                .fixedSize(horizontal: false, vertical: true)

            VStack(alignment: .leading, spacing: 6) {
                step(1, "Open claude.ai and sign in.")
                step(2, "Open DevTools (⌥⌘I).")
                step(3, "Application → Cookies → claude.ai.")
                step(4, "Copy the value of the `sessionKey` cookie.")
                step(5, "Click Continue and paste it.")
            }
            .foregroundStyle(t.textDim)

            HStack {
                Spacer()
                Button("Continue", action: onContinue)
                    .keyboardShortcut(.defaultAction)
            }
        }
        .padding(22)
        .frame(width: 440)
    }

    @ViewBuilder
    private func step(_ n: Int, _ text: String) -> some View {
        HStack(alignment: .top, spacing: 8) {
            Text("\(n).")
                .font(.system(size: 11, weight: .semibold, design: .monospaced))
            Text(text).font(.system(size: 11))
        }
    }
}
