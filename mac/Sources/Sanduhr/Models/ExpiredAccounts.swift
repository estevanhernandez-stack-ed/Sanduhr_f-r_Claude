import Foundation

/// The accounts whose saved key claude.ai refused at their last fetch (2.42.5). Before this,
/// Settings, Accounts read only the active account's status, so another account whose key had
/// expired still showed Signed in in green. Every fetch of any account feeds it: the active
/// account's own, Following's check of an inactive one, and a per-seat meter round. Nothing is
/// asked for it; an account nothing fetches keeps what it last showed.
struct ExpiredAccounts: Equatable {
    private(set) var labels: Set<String> = []

    func contains(_ label: String) -> Bool { labels.contains(label) }

    /// A refused key marks the account; a good answer clears it. A Cloudflare challenge or a
    /// network failure says nothing about the key, so it changes nothing.
    mutating func answered(_ label: String?, _ result: Result<UsageResponse, Error>) {
        guard let label else { return }
        switch result {
        case .success: labels.remove(label)
        case .failure(let e):
            if case .unauthorized? = e as? ClaudeAPI.APIError { labels.insert(label) }
        }
    }

    /// A new key, a sign-out or a removal: what the old key got no longer applies.
    mutating func forget(_ label: String) { labels.remove(label) }

    mutating func rename(_ old: String, to new: String) {
        if labels.remove(old) != nil { labels.insert(new) }
    }
}

/// 2.43.1: one banner when an account you aren't using has its key refused. In 2.42.5 only Settings, Accounts
/// said so, in orange, so nothing told you until you looked there. The active account needs none: its own
/// sign-in prompt already asks. Once per expiry, kept across launches (`notified`), so an account left expired
/// is not announced again every time Sanduhr starts; a good answer, a new key, a sign-out or a removal ends it.
enum ExpiredAccountNotice {
    /// The accounts to announce now, and to record as announced: every expired account but the active one that
    /// was not announced yet, none in demo mode (its accounts are made up), and only while a banner can go out
    /// (Alerts on, outside quiet hours). Checked at every answer, so an account that expired while Alerts were
    /// off is announced at the first answer after they allow it (2.43.2; 2.43.1 recorded it unseen).
    static func toNotify(expired: ExpiredAccounts, active: String?, demo: Bool, notified: Set<String>,
                         bannerAllowed: Bool) -> Set<String> {
        guard !demo, bannerAllowed else { return [] }
        var due = expired.labels.subtracting(notified)
        if let active { due.remove(active) }
        return due
    }

    /// The active account when it is expired: its own "sign in again" prompt has told the user, so it counts as
    /// announced and switching away from it later brings no banner.
    static func toldByPrompt(expired: ExpiredAccounts, active: String?) -> String? {
        guard let active, expired.labels.contains(active) else { return nil }
        return active
    }

    static func title(_ label: String) -> String { "\(label): session expired" }
    static let body = "Sanduhr can't read this account's usage until you sign in again in Settings, Accounts."
    static let idPrefix = "account-expired-"
    static func id(_ label: String) -> String { idPrefix + label }
}
