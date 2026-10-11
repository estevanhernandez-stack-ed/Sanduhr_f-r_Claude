import SwiftUI

/// First run, shown while no sessionKey is saved: sign in to Claude in Sanduhr's own window
/// (item 62), paste a key in Settings, Accounts, or use Just the Desk without an account.
struct OnboardingSheet: View {
    @Bindable var vm: UsageViewModel
    /// Sign In to Claude.
    var onSignIn: () -> Void
    /// Paste a key instead: Settings, Accounts.
    var onPaste: () -> Void
    /// Just the Desk: no account, the Desk on its own (JustTheDesk).
    var onJustTheDesk: () -> Void = {}

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

            Divider()

            HStack(alignment: .firstTextBaseline, spacing: 12) {
                Text(JustTheDesk.caption)
                    .font(.system(size: 11))
                    .foregroundStyle(t.textDim)
                    .fixedSize(horizontal: false, vertical: true)
                Spacer(minLength: 0)
                Button(JustTheDesk.buttonTitle, action: onJustTheDesk)
                    .help("Use the Desk without a Claude account; nothing is asked for")
            }
        }
        .padding(22)
        .frame(width: 440)
    }
}
