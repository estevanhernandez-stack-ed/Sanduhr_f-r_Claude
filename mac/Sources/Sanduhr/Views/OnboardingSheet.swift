import SwiftUI

/// First run, shown while no sessionKey is saved: sign in to Claude in Sanduhr's own window
/// (item 62), paste a key in Settings, Accounts, use Just the Desk without an account, or put it
/// off for this launch with Not Now (item 72, slice 4). No wider than the widget (F23).
struct OnboardingSheet: View {
    @Bindable var vm: UsageViewModel
    /// Sign In to Claude.
    var onSignIn: () -> Void
    /// Paste a key instead: Settings, Accounts.
    var onPaste: () -> Void
    /// Just the Desk: no account, the Desk on its own (JustTheDesk).
    var onJustTheDesk: () -> Void = {}
    /// Not Now: the sheet goes for this launch; the widget's "Not signed in" line stays the way in.
    var onNotNow: () -> Void = {}

    /// The widget's narrowest width: the sheet never overhangs it.
    static let width: CGFloat = 340

    var body: some View {
        let t = vm.theme.palette
        VStack(alignment: .leading, spacing: 14) {
            Text("Welcome to Sanduhr")
                .font(.title3.bold())
                .foregroundStyle(t.text)

            Text("Sign in to the claude.ai account whose usage you want to watch. Sanduhr keeps only its session key, \(SignedOut.keyHome(KeychainStore.kind)).")
                .font(.callout)
                .foregroundStyle(t.textSecondary)
                .fixedSize(horizontal: false, vertical: true)

            Text("Signed up with Google? Google doesn't allow sign-in inside apps: sign in in your browser and choose **Paste a Key Instead**.")
                .font(.system(size: 11))
                .foregroundStyle(t.textDim)
                .fixedSize(horizontal: false, vertical: true)

            HStack {
                Button(SignedOut.pasteTitle, action: onPaste)
                Spacer(minLength: 8)
                Button("Sign In to Claude…", action: onSignIn)
                    .keyboardShortcut(.defaultAction)
            }

            Divider()

            Text(JustTheDesk.caption)
                .font(.system(size: 11))
                .foregroundStyle(t.textDim)
                .fixedSize(horizontal: false, vertical: true)
            HStack {
                Button(WelcomeNotNow.title, action: onNotNow)
                    .keyboardShortcut(.cancelAction)
                    .help("Put this away for now. The widget's “Not signed in” line signs in later.")
                Spacer(minLength: 8)
                Button(JustTheDesk.buttonTitle, action: onJustTheDesk)
                    .help("Use the Desk without a Claude account; nothing is asked for")
            }
        }
        .padding(18)
        .frame(width: Self.width)
    }
}

/// A page about Claude usage, shown with no account signed in (Just the Desk, BACKLOG #4, or
/// signed out): one plain line saying it starts once you sign in, and the way to Accounts. Nothing
/// on the page is disabled; its settings wait for the numbers.
struct NeedsAccountNote: View {
    var vm: UsageViewModel
    let text: String

    /// Not signed in yet, or signed out: no numbers will come until a sign-in.
    static func shows(_ status: UsageViewModel.StatusMessage) -> Bool {
        status == .notSignedIn || status == .signedOut
    }

    static let alerts = "Alerts are about your Claude limits, so they start once you sign in. You can set them up now."
    static let claudeCode = "The statusline, the meters above the prompt and the MCP server report your Claude usage. They show numbers once you sign in."

    var body: some View {
        if Self.shows(vm.status) {
            HStack(alignment: .firstTextBaseline, spacing: 10) {
                Image(systemName: "person.crop.circle.badge.questionmark")
                    .foregroundStyle(.secondary)
                    .accessibilityHidden(true)
                Text(text)
                    .font(.callout)
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
                Spacer(minLength: 8)
                Button(SettingsSection.credentials.linkTitle) { SettingsWindowController.shared.show(.credentials) }
            }
            .padding(10)
            .background(RoundedRectangle(cornerRadius: 8).fill(Color.primary.opacity(0.05)))
        }
    }
}
