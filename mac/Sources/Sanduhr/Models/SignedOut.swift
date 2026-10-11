import Foundation

/// The signed-out forms (item 72, slice 4): what Settings, Accounts and Settings, Usage show
/// before the first account, and the credential store caption. Pure, so the copy and the
/// decisions are tested without a window.
enum SignedOut {
    // MARK: Accounts, the first account

    /// The first account's Label, editable before saving (F17).
    static let defaultLabel = AccountRegistry.defaultLabel
    static let intro = "Sign in to the claude.ai account whose usage you want to watch. Name it first if you like: Work, Personal, anything."
    static let pasteTitle = "Paste a Key Instead"
    static let keyHelpTitle = "How do I find this?"
    static let cloudflareTitle = "Seeing a Cloudflare error?"

    /// The guide's plain steps for finding the sessionKey (docs/setup-guide.md, section 1); the
    /// DevTools shorthand is gone (F19).
    static let chromeSteps = [
        "Open claude.ai in Chrome and sign in.",
        "Press Option-Command-I, then click Application.",
        "Under Cookies, click https://claude.ai.",
        "Find sessionKey and copy its Value.",
    ]
    static let safariSteps = [
        "In Safari, choose Settings, Advanced, and turn on Show features for web developers.",
        "Open claude.ai and sign in.",
        "Choose Develop, Show Web Inspector, then click Storage.",
        "Under Cookies, click claude.ai, find sessionKey and copy its Value.",
    ]
    static let cloudflareCaption = "Copy the cf_clearance cookie the same way and paste it here. Most people never need it."

    /// One row of the Data defaults shown before sign-in.
    struct DataRow: Equatable {
        let name: String
        let value: String
    }

    static let dataCaption = "These apply to the account you sign in with; change them after."

    /// What the first account's Data section will start with (F17): read from the store under
    /// the label being typed, so a choice already kept under that name shows as it will apply.
    /// A fresh profile reads Meter history 30 days, Claude Code activity Not tracked, Share with
    /// your agents Off.
    static func dataPreview(label: String, in store: DefaultsStore) -> [DataRow] {
        let name = label.trimmingCharacters(in: .whitespacesAndNewlines)
        let account = name.isEmpty ? defaultLabel : name
        let choices = AccountData.choices(for: account, in: store)
        return [
            DataRow(name: "Meter history", value: MeterHistory.isOn(account, in: store) ? "\(MeterHistory.days) days" : "Off"),
            DataRow(name: "Claude Code activity", value: choices.activity.title),
            DataRow(name: "Share with your agents", value: choices.share.title),
        ]
    }

    // MARK: Where the key is kept (F29)

    /// The caption under a key field on a dev build, nil on a release build: dev builds keep keys
    /// in a file on purpose (the Keychain's partition list defeats self-signed certificates).
    static func storeCaption(_ kind: CredentialStoreKind) -> String? {
        kind == .file ? "Dev build: the key is kept in a file, not the Keychain." : nil
    }

    /// The welcome sheet's "keeps only its session key, …" ending.
    static func keyHome(_ kind: CredentialStoreKind) -> String {
        kind == .file ? "in a file on this Mac (a dev build)" : "in your macOS Keychain"
    }

    // MARK: Usage

    /// What Settings, Usage shows.
    enum UsageState: Equatable {
        /// No account yet: a line and the way to Accounts, no empty charts.
        case signedOut
        /// Demo mode: the sample accounts' meter history.
        case demo
        /// An account's pages.
        case account(String)
    }

    static let usageLine = "Sign in to see your usage here. Your meters, their history and your Claude Code activity show on this page once an account is signed in."

    /// Usage, for an account with no usable key on this Mac: its numbers are saved ones.
    static let savedHistoryLine = "Signed out on this Mac — showing saved history."
    /// The same note as a callout: the headline in bold, the rest beneath it.
    static let savedHistoryTitle = "Signed out on this Mac"
    static let savedHistoryDetail = "The numbers below are saved history. Sign in again in Accounts to see live usage."

    static func usageState(label: String?, demo: Bool) -> UsageState {
        if demo { return .demo }
        guard let label else { return .signedOut }
        return .account(label)
    }
}

/// The welcome sheet's Not Now (item 72, slice 4): puts the sheet away for this launch without
/// choosing anything. In memory only, so the next launch without a key asks again; the widget's
/// "Not signed in — sign in" line stays as the way in.
enum WelcomeNotNow {
    static let title = "Not Now"
    @MainActor private(set) static var chosen = false

    @MainActor static func choose() { chosen = true }
}
