import SwiftUI

/// The sign-in sheet, shown while Track my AI usage is ticked in Set up Sanduhr and no sessionKey is
/// saved: sign in to Claude in Sanduhr's own window (item 62), paste a key in Settings, Accounts, or
/// put it off for this launch with Not Now (item 72, slice 4). Set up Sanduhr is the first-run
/// welcome since 2.41.0, and its Not now replaces Just the Desk. No wider than the widget (F23).
struct OnboardingSheet: View {
    @Bindable var vm: UsageViewModel
    /// Sign In to Claude.
    var onSignIn: () -> Void
    /// Paste a key instead: Settings, Accounts.
    var onPaste: () -> Void
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

            Text("Signed up with Google? Choose Continue with Google in the window. If Google's page stays blank, sign in in your browser and choose **Paste a Key Instead**.")
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

            HStack {
                Button(WelcomeNotNow.title, action: onNotNow)
                    .keyboardShortcut(.cancelAction)
                    .help("Put this away for now. The widget's “Not signed in” line signs in later.")
                Spacer(minLength: 8)
                Button("Set up Sanduhr…") { SetupWindowController.shared.show() }
                    .help("Choose what Sanduhr shows, including turning usage tracking off")
            }
        }
        .padding(18)
        .frame(width: Self.width)
    }
}

/// A page about Claude usage, shown with no account signed in (usage off in Set up Sanduhr, or
/// signed out): one plain line saying it starts once you sign in, and the way to Accounts. Nothing
/// on the page is disabled; its settings wait for the numbers.
struct NeedsAccountNote: View {
    var vm: UsageViewModel
    let text: String

    /// Not signed in yet, signed out, or usage switched off: no numbers will come until a sign-in or
    /// until usage is turned on.
    static func shows(_ status: UsageViewModel.StatusMessage) -> Bool {
        status == .notSignedIn || status == .signedOut || status == .usageOff
    }

    /// The line the note says: the page's own sign-in line, or with usage off the usage-off line
    /// in its place.
    static func line(_ status: UsageViewModel.StatusMessage, signIn text: String) -> String {
        status == .usageOff ? SetupCopy.usageOffSettingsLine : text
    }

    /// The way to Accounts goes with the sign-in line only.
    static func linksToAccounts(_ status: UsageViewModel.StatusMessage) -> Bool { status != .usageOff }

    static let alerts = "Alerts are about your Claude limits, so they start once you sign in. You can set them up now."
    static let claudeCode = "The statusline, the meters above the prompt and the MCP server report your Claude usage. They show numbers once you sign in."

    var body: some View {
        if Self.shows(vm.status) {
            HStack(alignment: .firstTextBaseline, spacing: 10) {
                Image(systemName: "person.crop.circle.badge.questionmark")
                    .foregroundStyle(.secondary)
                    .accessibilityHidden(true)
                Text(Self.line(vm.status, signIn: text))
                    .font(.callout)
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
                Spacer(minLength: 8)
                if Self.linksToAccounts(vm.status) {
                    Button(SettingsSection.credentials.linkTitle) { SettingsWindowController.shared.show(.credentials) }
                }
            }
            .padding(10)
            .background(RoundedRectangle(cornerRadius: 8).fill(Color.primary.opacity(0.05)))
        }
    }
}
