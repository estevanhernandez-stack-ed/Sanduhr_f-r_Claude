import SwiftUI

/// First run, shown while no sessionKey is saved: sign in to Claude in Sanduhr's own window
/// (item 62), or paste a key in Settings, Accounts.
struct OnboardingSheet: View {
    @Bindable var vm: UsageViewModel
    /// Sign In to Claude.
    var onSignIn: () -> Void
    /// Paste a key instead: Settings, Accounts.
    var onPaste: () -> Void

    var body: some View {
        let t = vm.theme.palette
        VStack(alignment: .leading, spacing: 14) {
            Text("Welcome to Sanduhr")
                .font(.title3.bold())
                .foregroundStyle(t.text)

            Text("Sign in to the claude.ai account whose usage you want to watch. Sanduhr keeps only its session key, in your macOS Keychain.")
                .font(.callout)
                .foregroundStyle(t.textSecondary)
                .fixedSize(horizontal: false, vertical: true)

            Text("Signed up with Google? Google doesn't allow sign-in inside apps: sign in in your browser and choose **Paste a Key Instead**.")
                .font(.system(size: 11))
                .foregroundStyle(t.textDim)
                .fixedSize(horizontal: false, vertical: true)

            HStack {
                Button("Paste a Key Instead", action: onPaste)
                Spacer()
                Button("Sign In to Claude…", action: onSignIn)
                    .keyboardShortcut(.defaultAction)
            }
        }
        .padding(22)
        .frame(width: 440)
    }
}
