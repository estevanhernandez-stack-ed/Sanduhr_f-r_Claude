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
    /// The accounts to announce now: newly marked by this answer, not the active one, not already announced,
    /// and none in demo mode (its accounts are made up).
    static func toNotify(before: ExpiredAccounts, after: ExpiredAccounts, active: String?, demo: Bool,
                         notified: Set<String>) -> Set<String> {
        guard !demo else { return [] }
        var added = after.labels.subtracting(before.labels)
        if let active { added.remove(active) }
        return added.subtracting(notified)
    }

    static func title(_ label: String) -> String { "\(label): session expired" }
    static let body = "Sanduhr can't read this account's usage until you sign in again in Settings, Accounts."
    static let idPrefix = "account-expired-"
    static func id(_ label: String) -> String { idPrefix + label }
}
